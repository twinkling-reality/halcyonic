import { type ChildProcess, execFileSync, spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  constants,
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
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

/** A choice the host refuses before anything runs, with its own code and words for the command's failure. */
export class OwnCheckRefused extends Error {
  readonly code: 'command_changed' | 'own_check_unavailable';

  constructor(code: 'command_changed' | 'own_check_unavailable', message: string) {
    super(message);
    this.code = code;
  }
}

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

/** How many of a failed script's last lines are read for a refusal: a passing test's log above them does not count (AUDIT3 S3). */
const REFUSED_LAST_LINES = 20;

/** The empty config files the manager is pointed at as the person's and the global one, written fresh each run (npm refuses one file as both). */
const EMPTY_CONFIG = 'empty-npmrc';
const EMPTY_GLOBAL_CONFIG = 'empty-global-npmrc';

/** How long trailing output is awaited once a script exited: a pipe still held after it means a child runs on. */
const AFTER_EXIT_MS = 200;

/** How long after the last script the folder is read again, for writes a child left running made (AUDIT3 3). */
const SETTLE_MS = 2000;

/**
 * Settings given in the environment, which outrank a project's own `.npmrc` (AUDIT3 1), so what runs is the chosen
 * script itself: no pre or post script (npm's ignore-scripts, pnpm's enable-pre-post-scripts), the system shell rather
 * than one the project names, and no config of the person's or the machine's.
 */
function managerEnvironment(home: string, temp: string): Record<string, string> {
  return {
    npm_config_ignore_scripts: 'true',
    npm_config_enable_pre_post_scripts: 'false',
    npm_config_script_shell: '/bin/sh',
    // A project's node-options would become NODE_OPTIONS for the script (a --require that exits 0 makes a failing
    // test pass, AUDIT3 reread 2). An empty value does not clear it, so a no-op one does: Node's own default.
    npm_config_node_options: '--stack-trace-limit=10',
    // pnpm's own way of running scripts, in place of the system shell.
    npm_config_shell_emulator: 'false',
    npm_config_userconfig: join(home, EMPTY_CONFIG),
    npm_config_globalconfig: join(home, EMPTY_GLOBAL_CONFIG),
    npm_config_cache: join(temp, 'npm-cache'),
    npm_config_update_notifier: 'false',
  };
}

/** When the process `pid` started, as `ps` says it, or null when no process has that pid. */
function startOf(pid: number): string | null {
  try {
    const started = execFileSync('/bin/ps', ['-o', 'lstart=', '-p', String(pid)], {
      encoding: 'utf8',
    }).trim();
    return started === '' ? null : started;
  } catch {
    return null;
  }
}

/**
 * Go's settings for a check: its caches in the run's own fresh folders, no network (no module proxy, no checksum
 * database), the toolchain installed here only, and no go env file of the person's. A module whose dependencies are not
 * vendored fails here; installs need the network, which the profile refuses anyway.
 */
function goEnvironment(home: string, temp: string): Record<string, string> {
  return {
    GOCACHE: join(temp, 'go-build'),
    GOPATH: join(home, 'go'),
    GOMODCACHE: join(home, 'go', 'pkg', 'mod'),
    GOENV: 'off',
    GOTOOLCHAIN: 'local',
    GOPROXY: 'off',
    GOSUMDB: 'off',
    GOTELEMETRY: 'off',
    GOFLAGS: '',
  };
}

/** Whether the last lines of a failed script's output name a refusal of the sandbox. */
export function refusedAtEnd(log: string): boolean {
  const lines = log.split('\n').filter((line) => line.trim() !== '');
  return lines.slice(-REFUSED_LAST_LINES).some((line) => REFUSED.test(line));
}

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
  /** How long after the run the folder is read again; 2 s unless a test passes its own. */
  readonly settleMs?: number;
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
  return { npm: find('npm'), pnpm: find('pnpm'), yarn: find('yarn'), go: find('go') };
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
    if ((error as NodeJS.ErrnoException).code !== 'ENOENT')
      return { ok: false, reason: 'manifest_unreadable' };
    return readGoModule(folder);
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

/**
 * A Go module's check, where the folder has no `package.json`: a regular `go.mod` at its top, read without following a
 * link, at most 256 KiB. Its commands are Halcyonic's own and fixed, never the project's (`GO_COMMANDS`).
 */
async function readGoModule(folder: string): Promise<Manifest> {
  try {
    const stat = await lstat(join(folder, 'go.mod'));
    if (!stat.isFile() || stat.size > MANIFEST_BYTES)
      return { ok: false, reason: 'manifest_unreadable' };
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === 'ENOENT'
      ? { ok: false, reason: 'no_manifest' }
      : { ok: false, reason: 'manifest_unreadable' };
  }
  return {
    ok: true,
    scripts: new Map(Object.entries(GO_COMMANDS) as [OwnCheckScriptName, string][]),
  };
}

/**
 * The commands a Go module's check offers (probed inside the profile on 2026-11-11: go test passes with exit 0 and a
 * failing test exits 1 with its FAIL line). Shown to the person as written here, and run as these arguments.
 */
export const GO_COMMANDS: Readonly<Partial<Record<OwnCheckScriptName, string>>> = {
  test: 'go test ./...',
  check: 'go vet ./...',
};

/** The arguments of each Go command, after the binary. */
const GO_ARGUMENTS: Readonly<Partial<Record<OwnCheckScriptName, readonly string[]>>> = {
  test: ['test', './...'],
  check: ['vet', './...'],
};

/** The manager the folder's lockfile names; npm with none; go for a Go module with no package.json. */
export function managerOf(folder: string): OwnCheckManager {
  if (!existsSync(join(folder, 'package.json')) && existsSync(join(folder, 'go.mod'))) return 'go';
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
  /** Every script running now, so a close stops it (AUDIT3 4). */
  readonly #running = new Set<ChildProcess>();
  #closed = false;

  constructor(deps: OwnChecksDeps) {
    this.#deps = deps;
    this.#reap();
  }

  /** The folder of the records of scripts running, one file per process, so a later start can stop what a crash left. */
  get #records(): string {
    return join(this.#deps.dataDir, 'own-checks', 'running');
  }

  /**
   * The record of a running script: its pid and when that process started, as `ps` gives it. `sandbox-exec` execs the
   * manager, so the process keeps its pid and start time but not the profile in its arguments (AUDIT3 reread 1).
   */
  #remember(pid: number | undefined, profile: string): void {
    if (pid === undefined) return;
    try {
      const started = startOf(pid);
      mkdirSync(this.#records, { recursive: true });
      writeFileSync(join(this.#records, String(pid)), JSON.stringify({ pid, profile, started }), {
        mode: 0o600,
      });
    } catch (error) {
      this.#deps.logger.warn({ err: error }, 'own check record not written');
    }
  }

  #forget(pid: number | undefined): void {
    if (pid === undefined) return;
    rmSync(join(this.#records, String(pid)), { force: true });
  }

  /**
   * At start: stops the group of every script an earlier control plane left running, only while that process still runs
   * this check's own profile, as its command line names it (a pid reused since names another), then drops the record.
   */
  #reap(): void {
    let names: string[];
    try {
      names = readdirSync(this.#records);
    } catch {
      return;
    }
    for (const name of names) {
      const path = join(this.#records, name);
      let record: { pid?: unknown; started?: unknown };
      try {
        record = JSON.parse(readFileSync(path, 'utf8')) as typeof record;
      } catch {
        rmSync(path, { force: true });
        continue;
      }
      // The same process only while its pid still has the start time recorded: a pid reused since started later.
      if (
        typeof record.pid === 'number' &&
        typeof record.started === 'string' &&
        startOf(record.pid) === record.started
      ) {
        try {
          process.kill(-record.pid, 'SIGKILL');
        } catch {
          // Its group is gone.
        }
      }
      rmSync(path, { force: true });
    }
  }

  /** Stops every script running now, with its group, when the control plane closes (AUDIT3 4). */
  close(): void {
    this.#closed = true;
    for (const child of this.#running) {
      try {
        if (child.pid !== undefined) process.kill(-child.pid, 'SIGKILL');
      } catch {
        // Already gone.
      }
      child.stdout?.destroy();
      child.stderr?.destroy();
      this.#forget(child.pid);
    }
    this.#running.clear();
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
    const scripts: OwnCheckScript[] = [...manifest.scripts].map(([name, command]) => ({
      name,
      command,
      command_sha256: commandSha256(command),
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
    if (found.manager === null)
      throw new OwnCheckRefused(
        'own_check_unavailable',
        'This project offers no check of its own now.',
      );
    const redact = this.#deps.redact ?? ((text: string) => text);
    const scripts: OwnCheckScript[] = [];
    for (const name of OWN_CHECK_SCRIPT_NAMES) {
      const chosen = command.payload.scripts.find((script) => script.name === name);
      if (chosen === undefined) continue;
      const script = found.scripts.find((offered) => offered.name === name);
      if (script === undefined)
        throw new OwnCheckRefused(
          'own_check_unavailable',
          `The project's ${name} script is not there now.`,
        );
      // The choice is bound to what the person read: a command changed since is refused (AUDIT3 S1).
      if (script.command_sha256 !== chosen.command_sha256)
        throw new OwnCheckRefused(
          'command_changed',
          `The project's ${name} script changed since it was shown. Read it again before choosing.`,
        );
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
      if (this.#closed) break;
      const run = await this.#script(script.name, manager, folder, binary, place, cache);
      runs.push(run);
      if (run.exit !== 0) break;
    }
    const after = before.ok ? await readFolder(folder, before.reading) : null;
    // Read again a moment later: a write since then came from something the run left behind (AUDIT3 3).
    if (after?.ok === true && runs.length > 0 && !this.#closed) {
      await new Promise((resolve) => setTimeout(resolve, this.#deps.settleMs ?? SETTLE_MS));
      const later = await readFolder(folder, after.reading);
      const moved = later.ok ? writtenBetween(after.reading, later.reading) : null;
      if (moved === null || moved.created + moved.changed + moved.deleted > 0) {
        const last = runs.length - 1;
        runs[last] = { ...(runs[last] as OwnCheckRun), left_running: true };
      }
    }
    return {
      outcome: 'ran',
      manager,
      runs,
      // Passed only where every chosen script exited 0 and nothing it started outlived the run (AUDIT3 reread S1).
      passed:
        runs.length === chosen.length && runs.every((run) => run.exit === 0 && !run.left_running),
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
    // Both fresh for every run, so nothing a run leaves there (a ~/.npmrc of its own) reaches the next (AUDIT3 2).
    await rm(home, { recursive: true, force: true });
    await rm(temp, { recursive: true, force: true });
    await mkdir(home, { recursive: true });
    await mkdir(temp, { recursive: true });
    await writeFile(join(home, EMPTY_CONFIG), '', { mode: 0o600 });
    await writeFile(join(home, EMPTY_GLOBAL_CONFIG), '', { mode: 0o600 });
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

  /**
   * One script, read over pipes into the check's own folder. It settles on the script's own exit, never on its pipes,
   * so a child holding them cannot keep the command pending (AUDIT3 3): the pipes are given a moment for trailing
   * output, then closed, and the whole group is stopped. At the limit the group is stopped and the run settles as timed
   * out. A child that left the group (a new session) escapes this; the folder read again after the run says so.
   */
  #script(
    name: OwnCheckScriptName,
    manager: OwnCheckManager,
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
      let settled = false;
      let child: ChildProcess | undefined;
      const keep = (chunk: Buffer) => {
        if (kept >= LOG_BYTES) return;
        const part = chunk.subarray(0, LOG_BYTES - kept);
        chunks.push(part);
        kept += part.length;
      };
      const stopGroup = () => {
        try {
          if (child?.pid !== undefined) process.kill(-child.pid, 'SIGKILL');
        } catch {
          // Already gone.
        }
      };
      const finish = (exit: number | null, leftRunning: boolean) => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        stopGroup();
        child?.stdout?.destroy();
        child?.stderr?.destroy();
        if (child !== undefined) this.#running.delete(child);
        this.#forget(child?.pid);
        const log = Buffer.concat(chunks);
        const failed = !timedOut && exit !== 0;
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
              refused: failed && refusedAtEnd(log.toString('utf8')),
              left_running: leftRunning,
            }),
          );
      };
      try {
        child = spawn(
          this.#deps.sandboxExec ?? '/usr/bin/sandbox-exec',
          [
            '-f',
            place.profile,
            binary,
            ...(manager === 'go' ? (GO_ARGUMENTS[name] ?? []) : ['run', '-s', name]),
          ],
          {
            cwd: folder,
            env: {
              PATH: place.path,
              HOME: place.home,
              TMPDIR: place.temp,
              ...(manager === 'go'
                ? goEnvironment(place.home, place.temp)
                : managerEnvironment(place.home, place.temp)),
              CI: '1',
              NO_COLOR: '1',
            },
            stdio: ['ignore', 'pipe', 'pipe'],
            detached: true,
          },
        );
      } catch {
        resolve({
          name,
          exit: null,
          seconds: 0,
          timed_out: false,
          refused: false,
          left_running: false,
        });
        return;
      }
      this.#running.add(child);
      this.#remember(child.pid, place.profile);
      const timer = setTimeout(() => {
        timedOut = true;
        stopGroup();
        // A child holding the pipes after its group was stopped does not hold the run.
        setTimeout(() => finish(null, true), AFTER_EXIT_MS);
      }, limit);
      child.stdout?.on('data', keep);
      child.stderr?.on('data', keep);
      child.on('error', () => finish(null, false));
      child.on('exit', (code) => {
        const open = () =>
          child?.stdout?.readableEnded === false || child?.stderr?.readableEnded === false;
        if (!open()) {
          finish(code, false);
          return;
        }
        setTimeout(() => finish(code, open()), AFTER_EXIT_MS);
      });
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
