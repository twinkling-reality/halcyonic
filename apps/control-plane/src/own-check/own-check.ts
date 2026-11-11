import { type ChildProcess, spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { constants, existsSync, realpathSync, statSync } from 'node:fs';
import { lstat, mkdir, open, rename, rm, writeFile } from 'node:fs/promises';
import { delimiter, dirname, join } from 'node:path';
import {
  type CheckId,
  type CommandOf,
  OWN_CHECK_COMMAND_MAX,
  OWN_CHECK_SCRIPT_NAMES,
  OWN_CHECK_WRITTEN_LISTED,
  type OwnCheckFindings,
  type OwnCheckFoundResponse,
  type OwnCheckFoundScript,
  type OwnCheckManager,
  type OwnCheckRun,
  type OwnCheckScript,
  type OwnCheckScriptName,
  type OwnCheckWritten,
  type ProjectId,
  type Timestamp,
  type WorkstreamId,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import { confinedProfile, confinementKey, toolFoldersFor } from '@halcyonic/seatbelt';
import { causedBy, controlPlaneDraft } from '../core/drafts.ts';
import type { Recorder } from '../core/recorder.ts';
import type { IdGenerator } from '../ids.ts';
import type { Logger } from '../logger.ts';
import { compareReadings, NO_LINK, type Reading, readFolder } from '../observer/file-observer.ts';
import { type CheckOutput, ownCheckOutput } from '../preview/check-output.ts';
import { readStart } from '../preview/preview.ts';
import { foldersInside } from '../runtimes.ts';

/** How long one chosen script may run before it is stopped, its whole process group with it. */
export const OWN_CHECK_SCRIPT_MS = 10 * 60_000;

/** The largest `package.json` the host reads. */
const MANIFEST_BYTES = 256 * 1024;

/** The most bytes of one script's output kept for the agent's lines and the person's. */
const LOG_BYTES = 1024 * 1024;

/** Which lockfile names which manager, in this order; with none, npm. */
const LOCKFILES: readonly [string, OwnCheckManager][] = [
  ['pnpm-lock.yaml', 'pnpm'],
  ['yarn.lock', 'yarn'],
  ['package-lock.json', 'npm'],
  ['npm-shrinkwrap.json', 'npm'],
];

/** A refusal of the sandbox, as a program reports it: never read as the agent's fault (ADR 0055). */
const REFUSED = /\bEPERM\b|operation not permitted/i;

export interface OwnChecksDeps {
  readonly dataDir: string;
  readonly projection: Projection;
  readonly recorder: Recorder;
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly logger: Logger;
  /** The person's home folder, named `~` in the lines an agent is given. */
  readonly home: string;
  /** The Node the managers run on: its installation's folders are read inside the sandbox. */
  readonly node: string;
  /** Where each manager is, resolved once from the host's own PATH; null where it is not found. */
  readonly managers: Readonly<Record<OwnCheckManager, string | null>>;
  readonly sandboxExec?: string;
  readonly scriptMs?: number;
  /** Takes out any secret Halcyonic holds from a command before it is journaled or shown. */
  readonly redact?: (text: string) => string;
}

/** Each manager's binary on `path`, as the host's own PATH names it, or null. */
export function managersOn(path: string | undefined): Record<OwnCheckManager, string | null> {
  const folders = (path ?? '').split(delimiter).filter((folder) => folder.startsWith('/'));
  const find = (name: string): string | null => {
    for (const folder of folders) {
      const candidate = join(folder, name);
      try {
        if (statSync(candidate).isFile()) return candidate;
      } catch {
        // Not here.
      }
    }
    return null;
  };
  return { npm: find('npm'), pnpm: find('pnpm'), yarn: find('yarn') };
}

type Manifest =
  | { readonly ok: true; readonly scripts: Map<OwnCheckScriptName, string> }
  | { readonly ok: false; readonly reason: 'no_manifest' | 'manifest_unreadable' };

/**
 * The offered scripts of the folder's own `package.json`: a regular file at its top, read without
 * following a link, at most 256 KiB, JSON with a `scripts` object. Only the offered names, each a
 * non-empty string of at most 2,000 characters.
 */
export async function readManifest(folder: string): Promise<Manifest> {
  const path = join(folder, 'package.json');
  let text: string;
  try {
    const stat = await lstat(path);
    if (!stat.isFile()) return { ok: false, reason: 'manifest_unreadable' };
    if (stat.size > MANIFEST_BYTES) return { ok: false, reason: 'manifest_unreadable' };
    const handle = await open(path, constants.O_RDONLY | NO_LINK);
    try {
      text = await handle.readFile({ encoding: 'utf8' });
    } finally {
      await handle.close();
    }
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === 'ENOENT'
      ? { ok: false, reason: 'no_manifest' }
      : { ok: false, reason: 'manifest_unreadable' };
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return { ok: false, reason: 'manifest_unreadable' };
  }
  const scripts =
    typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      ? (parsed as { scripts?: unknown }).scripts
      : undefined;
  if (typeof scripts !== 'object' || scripts === null || Array.isArray(scripts))
    return { ok: false, reason: 'manifest_unreadable' };
  const found = new Map<OwnCheckScriptName, string>();
  for (const name of OWN_CHECK_SCRIPT_NAMES) {
    const command = (scripts as Record<string, unknown>)[name];
    if (typeof command !== 'string') continue;
    if (command.trim() === '' || command.length > OWN_CHECK_COMMAND_MAX) continue;
    found.set(name, command);
  }
  return { ok: true, scripts: found };
}

/** The manager the folder's lockfile names; npm with none. */
export function managerOf(folder: string): OwnCheckManager {
  for (const [file, manager] of LOCKFILES) if (existsSync(join(folder, file))) return manager;
  return 'npm';
}

export function commandSha256(command: string): string {
  return createHash('sha256').update(command, 'utf8').digest('hex');
}

/** What the check itself changed, from readings before and after it; null when either is missing. */
export function writtenBetween(
  before: Reading | null,
  after: Reading | null,
): OwnCheckWritten | null {
  if (before === null || after === null) return null;
  const { created, changed, deleted } = compareReadings(before, after);
  const paths = [...created, ...changed, ...deleted]
    .map((file) => file.path)
    .sort()
    .slice(0, OWN_CHECK_WRITTEN_LISTED);
  return { created: created.length, changed: changed.length, deleted: deleted.length, paths };
}

/**
 * A project's own check (ADR 0055): the scripts of its `package.json` the person chose, run by the
 * host, one after another until one fails, inside the same closed Seatbelt profile as an agent: the
 * folder and the tools read, writes only in the folder and a home and temporary folder of its own,
 * no network, no port, standard input closed, and no variable of the host's but PATH. What it
 * observed is journaled; the scripts' output is kept beside the check, never journaled or logged.
 */
export class OwnChecks {
  readonly #deps: OwnChecksDeps;
  readonly #checking = new Set<ProjectId>();

  constructor(deps: OwnChecksDeps) {
    this.#deps = deps;
  }

  /** The scripts the folder offers now, for the person to read before choosing. */
  async found(projectId: ProjectId): Promise<OwnCheckFoundResponse> {
    const offered = await this.#offered(projectId);
    const redact = this.#deps.redact ?? ((text: string) => text);
    return {
      ...offered,
      scripts: offered.scripts.map((script) => ({ ...script, command: redact(script.command) })),
    };
  }

  /** The scripts as the folder holds them, unredacted, for their hashes. */
  async #offered(projectId: ProjectId): Promise<OwnCheckFoundResponse> {
    const folder = this.#deps.projection.project(projectId)?.location?.path ?? null;
    if (folder === null) return { manager: null, scripts: [], reason: 'no_manifest' };
    const manifest = await readManifest(folder);
    if (!manifest.ok) return { manager: null, scripts: [], reason: manifest.reason };
    const scripts: OwnCheckFoundScript[] = [...manifest.scripts].map(([name, command]) => ({
      name,
      command,
    }));
    if (scripts.length === 0) return { manager: null, scripts, reason: 'no_scripts' };
    const manager = managerOf(folder);
    if (this.#deps.managers[manager] === null)
      return { manager: null, scripts, reason: 'manager_missing' };
    return { manager, scripts, reason: null };
  }

  /**
   * Records the person's choice with each script's command as the folder holds it now, then runs it
   * once, so the person sees whether it passes before any turn. Throws when a chosen name is not
   * offered, which completes the command as failed and records no choice.
   */
  async choose(
    command: CommandOf<'project.own_check.choose'>,
  ): Promise<{ check_id: CheckId; passed: boolean }> {
    const projectId = command.payload.project_id;
    const found = await this.#offered(projectId);
    if (found.manager === null) throw new Error(`no own check offered (${found.reason})`);
    const redact = this.#deps.redact ?? ((text: string) => text);
    const scripts: OwnCheckScript[] = [];
    for (const name of OWN_CHECK_SCRIPT_NAMES) {
      if (!command.payload.scripts.includes(name)) continue;
      const script = found.scripts.find((offered) => offered.name === name);
      if (script === undefined) throw new Error(`script ${name} is not offered`);
      // The hash is of the command as written; what is journaled and shown holds no held secret.
      scripts.push({
        name,
        command: redact(script.command),
        command_sha256: commandSha256(script.command),
      });
    }
    this.#deps.recorder.record(
      controlPlaneDraft(
        'project.own_check.chosen',
        scope(projectId),
        { manager: found.manager, scripts },
        this.#now(),
        causedBy(command.command_id),
      ),
    );
    return this.#run(projectId, undefined, true, command.command_id);
  }

  /** Runs the chosen check of the project's folder, or of a task's own copy (ADR 0045). */
  check(command: CommandOf<'project.check'>): Promise<{ check_id: CheckId; passed: boolean }> {
    return this.#run(
      command.payload.project_id,
      command.payload.workstream_id,
      false,
      command.command_id,
    );
  }

  async #run(
    projectId: ProjectId,
    taskId: WorkstreamId | undefined,
    first: boolean,
    commandId: CommandOf<'project.check'>['command_id'],
  ): Promise<{ check_id: CheckId; passed: boolean }> {
    const { projection } = this.#deps;
    const own = projection.ownCheck(projectId);
    const location = projection.project(projectId)?.location ?? null;
    if (own === undefined || location === null) throw new Error('no own check chosen');
    const copy = taskId === undefined ? undefined : projection.workstream(taskId)?.copy;
    if (taskId !== undefined && copy?.state !== 'own')
      throw new Error('the task has no copy of its own');
    const folder = copy?.state === 'own' ? copy.directory : location.path;
    if (this.#checking.has(projectId))
      throw new Error('a check of this project is already running');
    this.#checking.add(projectId);
    try {
      const checkId = this.#deps.ids.next() as CheckId;
      const findings = await this.#findings(projectId, checkId, folder, own.manager, own.scripts);
      this.#deps.recorder.record(
        controlPlaneDraft(
          'project.own_check.recorded',
          scope(projectId),
          {
            check_id: checkId,
            first,
            findings,
            ...(taskId === undefined ? {} : { workstream_id: taskId }),
          },
          this.#now(),
          causedBy(commandId),
        ),
      );
      return { check_id: checkId, passed: findings.passed };
    } finally {
      this.#checking.delete(projectId);
    }
  }

  async #findings(
    projectId: ProjectId,
    checkId: CheckId,
    folder: string,
    manager: OwnCheckManager,
    chosen: readonly OwnCheckScript[],
  ): Promise<OwnCheckFindings> {
    const stopped = (outcome: OwnCheckFindings['outcome']): OwnCheckFindings => ({
      outcome,
      manager,
      runs: [],
      passed: false,
      written: null,
    });
    const manifest = await readManifest(folder);
    if (!manifest.ok) return stopped('manifest_unreadable');
    // A command changed since it was chosen, by anyone, is not run: the person chooses again.
    for (const script of chosen)
      if (commandSha256(manifest.scripts.get(script.name) ?? '') !== script.command_sha256)
        return stopped('command_changed');
    const binary = this.#deps.managers[manager];
    if (binary === null) return stopped('manager_missing');
    const place = await this.#place(projectId, folder, binary);
    const cache = this.cacheOf(projectId, checkId);
    await mkdir(cache, { recursive: true });
    const before = await readFolder(folder);
    const runs: OwnCheckRun[] = [];
    for (const script of chosen) {
      const run = await this.#script(script.name, folder, binary, place, cache);
      runs.push(run);
      if (run.exit !== 0) break;
    }
    const after = before.ok ? await readFolder(folder, before.reading) : null;
    return {
      outcome: 'ran',
      manager,
      runs,
      passed: runs.length === chosen.length && runs.every((run) => run.exit === 0),
      written: writtenBetween(
        before.ok ? before.reading : null,
        after?.ok === true ? after.reading : null,
      ),
    };
  }

  /** The profile and the own folders of the check of `folder`, written fresh for each run. */
  async #place(
    projectId: ProjectId,
    folder: string,
    binary: string,
  ): Promise<{ profile: string; home: string; temp: string; path: string }> {
    const base = join(this.#deps.dataDir, 'own-checks', confinementKey(folder));
    const home = join(base, 'home');
    const temp = join(base, 'temp');
    await rm(temp, { recursive: true, force: true });
    await mkdir(home, { recursive: true });
    await mkdir(temp, { recursive: true });
    const real = realpathSync(binary);
    const others = this.#deps.projection
      .projects()
      .filter((project) => project.project_id !== projectId)
      .flatMap((project) => (project.location === null ? [] : [project.location.path]));
    const inner = foldersInside(folder, others);
    const text = confinedProfile({
      project: folder,
      readable: [],
      // Halcyonic's data folder is refused outright, as to every agent (ADR 0048).
      hidden: [this.#deps.dataDir],
      ...(inner.length === 0 ? {} : { inner }),
      home,
      temp,
      runtimeFolder: dirname(dirname(real)),
      toolFolders: [...new Set([...toolFoldersFor(this.#deps.node), ...toolFoldersFor(real)])],
      loopbackPorts: [],
    });
    const profile = join(base, 'profile.sb');
    const draft = `${profile}.${process.pid}.new`;
    await writeFile(draft, text, { mode: 0o600 });
    await rename(draft, profile);
    const path = [...new Set([dirname(this.#deps.node), dirname(binary), '/usr/bin', '/bin'])].join(
      delimiter,
    );
    return { profile, home, temp, path };
  }

  /** One script, read over pipes into the check's own folder; its whole group stopped at the limit. */
  #script(
    name: OwnCheckScriptName,
    folder: string,
    binary: string,
    place: { profile: string; home: string; temp: string; path: string },
    cache: string,
  ): Promise<OwnCheckRun> {
    const started = Date.now();
    const limit = this.#deps.scriptMs ?? OWN_CHECK_SCRIPT_MS;
    return new Promise((resolve) => {
      const chunks: Buffer[] = [];
      let kept = 0;
      let timedOut = false;
      let child: ChildProcess | undefined;
      const keep = (chunk: Buffer) => {
        if (kept >= LOG_BYTES) return;
        const part = chunk.subarray(0, LOG_BYTES - kept);
        chunks.push(part);
        kept += part.length;
      };
      const finish = (exit: number | null) => {
        clearTimeout(timer);
        // Whatever the script left running in its group stops with it: a check leaves nothing behind.
        try {
          if (child?.pid !== undefined) process.kill(-child.pid, 'SIGKILL');
        } catch {
          // Already gone.
        }
        const log = Buffer.concat(chunks);
        void writeFile(join(cache, `${name}.log`), log, { mode: 0o600 })
          .catch((error: unknown) =>
            this.#deps.logger.warn({ err: error }, 'own check output not kept'),
          )
          .finally(() =>
            resolve({
              name,
              exit: timedOut ? null : exit,
              seconds: Math.round((Date.now() - started) / 100) / 10,
              timed_out: timedOut,
              refused: REFUSED.test(log.toString('utf8')),
            }),
          );
      };
      try {
        child = spawn(
          this.#deps.sandboxExec ?? '/usr/bin/sandbox-exec',
          ['-f', place.profile, binary, 'run', '-s', name],
          {
            cwd: folder,
            env: {
              PATH: place.path,
              HOME: place.home,
              TMPDIR: place.temp,
              npm_config_cache: join(place.home, 'npm-cache'),
              CI: '1',
              NO_COLOR: '1',
            },
            stdio: ['ignore', 'pipe', 'pipe'],
            detached: true,
          },
        );
      } catch {
        resolve({ name, exit: null, seconds: 0, timed_out: false, refused: false });
        return;
      }
      const timer = setTimeout(() => {
        timedOut = true;
        try {
          if (child?.pid !== undefined) process.kill(-child.pid, 'SIGKILL');
        } catch {
          // Already gone.
        }
      }, limit);
      child.stdout?.on('data', keep);
      child.stderr?.on('data', keep);
      child.on('error', () => finish(null));
      child.on('close', (code) => finish(code));
    });
  }

  /** The folder a check's output is kept in. */
  cacheOf(projectId: ProjectId, checkId: CheckId): string {
    return join(this.#deps.dataDir, 'checks', projectId, checkId);
  }

  /**
   * The failed script's first lines for the next instruction to an agent in the project (FX1), as the
   * Mac's own check: only while the latest run is current, ran, and a script failed on its own, never
   * timed out or refused by the sandbox; null otherwise. A check of a task's own copy is that task's.
   */
  failedOutput(
    projectId: ProjectId,
    workstreamId: WorkstreamId | null = null,
  ): { checkId: CheckId; output: CheckOutput } | null {
    const latest = this.#deps.projection.ownCheck(projectId)?.latest ?? null;
    if (latest === null || latest.files !== 'current' || latest.findings.passed) return null;
    const checked = latest.workstream_id ?? null;
    if (checked !== null && checked !== workstreamId) return null;
    const failed = latest.findings.runs.find((run) => run.exit !== 0);
    if (failed === undefined || failed.exit === null || failed.refused) return null;
    const copy = checked === null ? undefined : this.#deps.projection.workstream(checked)?.copy;
    const folder =
      copy?.state === 'own'
        ? copy.directory
        : this.#deps.projection.project(projectId)?.location?.path;
    if (folder === undefined) return null;
    const cache = this.cacheOf(projectId, latest.check_id);
    const output = ownCheckOutput({
      script: failed.name,
      exit: failed.exit,
      log: readStart(join(cache, `${failed.name}.log`)),
      projectFolder: folder,
      cacheFolder: cache,
      dataDir: this.#deps.dataDir,
      home: this.#deps.home,
    });
    return output === null ? null : { checkId: latest.check_id, output };
  }

  #now(): Timestamp {
    return this.#deps.clock.now().toISOString() as Timestamp;
  }
}

function scope(projectId: ProjectId) {
  return { project_id: projectId, workstream_id: null, execution_id: null } as const;
}
