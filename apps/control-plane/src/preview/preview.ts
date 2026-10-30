import { type ChildProcess, spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdir, readdir, readFile, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import {
  type CheckFindings,
  type CheckId,
  type CommandOf,
  PREVIEW_PORT_FIRST,
  PREVIEW_PORT_LAST,
  type PreviewStopReason,
  type ProjectId,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import { type Cause, causedBy, controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import type { Recorder } from '../core/recorder.ts';
import type { IdGenerator } from '../ids.ts';
import type { Logger } from '../logger.ts';
import { type CheckOptions, type CheckResult, runCheck, servesCopy } from './check.ts';
import { buildProfile, SANDBOX_EXEC } from './profiles.ts';

export interface HostPreviewsDeps {
  readonly recorder: Recorder;
  readonly projection: Projection;
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly logger: Logger;
  /** Halcyonic's data directory: checks and preview records live under it. */
  readonly dataDir: string;
  /** The person's home folder, which the check's profiles keep out of reach. */
  readonly home: string;
  /** Node, by absolute path, for the starter's own TypeScript and Vite. */
  readonly node: string;
  /** Chrome, by absolute path, or null: checks then cannot pass. */
  readonly chrome: string | null;
  /** The repository's tracked starters and the data directory's installed ones. */
  readonly starters: { readonly trackedDir: string; readonly installedDir: string };
  /** Whether this host confines a starter project's Codex to its folder (ADR 0041). */
  readonly confinesCodex: boolean;
  /** For tests: the check itself. */
  readonly runCheck?: (options: CheckOptions) => Promise<CheckResult>;
  /** For tests: starts the server for a built copy. */
  readonly serve?: (args: ServeArgs) => ChildProcess;
}

export interface ServeArgs {
  readonly profile: string;
  readonly node: string;
  readonly vite: string;
  readonly config: string;
  readonly dist: string;
  readonly env: Record<string, string>;
  readonly cwd: string;
}

interface Running {
  readonly checkId: CheckId;
  readonly port: number;
  readonly child: ChildProcess;
  stopping: boolean;
}

/**
 * The host's checks and previews of projects made from a starter (ADR 0037). A check type checks,
 * builds and opens the built copy in the sandboxed browser, then records what it found; a preview
 * serves the built copy of the project's latest passed check, never the live folder, on a loopback
 * port of its range. One check or start per project at a time, and no start while a check runs.
 */
export class HostPreviews {
  readonly #deps: HostPreviewsDeps;
  readonly #running = new Map<ProjectId, Running>();
  readonly #checking = new Set<ProjectId>();
  readonly #starting = new Set<ProjectId>();
  /** The ports of the range a check or a preview holds, taken synchronously so two never share one. */
  readonly #ports = new Set<number>();
  /** Every server spawned and not yet exited, started or not, so close() stops them all. */
  readonly #children = new Set<ChildProcess>();
  #closed = false;

  constructor(deps: HostPreviewsDeps) {
    this.#deps = deps;
  }

  /** Checks the project and records what was found; resolves with the check's id and outcome. */
  async check(
    command: CommandOf<'project.check'>,
  ): Promise<{ check_id: CheckId; passed: boolean }> {
    const projectId = command.payload.project_id;
    const project = this.#deps.projection.project(projectId);
    if (project?.starter == null || project.location === null)
      throw new Error('not a starter project');
    if (this.#checking.has(projectId))
      throw new Error('a check of this project is already running');
    if (this.#starting.has(projectId)) throw new Error('a preview of this project is starting');
    this.#checking.add(projectId);
    const cause = causedBy(command.command_id);
    let port: number | null = null;
    try {
      // The new check replaces the latest: a preview of the old one stops first.
      if (this.#running.has(projectId)) await this.#stop(projectId, 'new_check', cause);
      const checkId = this.#deps.ids.next() as CheckId;
      const installed = join(this.#deps.starters.installedDir, project.starter);
      const profiles = join(installed, 'input-profiles');
      port = this.#takePort();
      const result = await (this.#deps.runCheck ?? runCheck)({
        node: this.#deps.node,
        chrome: this.#deps.chrome,
        home: this.#deps.home,
        project: project.location.path,
        starter: installed,
        hostSource: join(this.#deps.starters.trackedDir, project.starter, 'host'),
        cache: this.#cache(projectId, checkId),
        profiles: existsSync(profiles) ? profiles : null,
        port,
      });
      const findings = this.#findings(projectId, result);
      this.#deps.recorder.record(
        controlPlaneDraft(
          'project.check.recorded',
          scope(projectId),
          { check_id: checkId, findings },
          this.#now(),
          cause,
        ),
      );
      return { check_id: checkId, passed: findings.passed };
    } finally {
      if (port !== null) this.#ports.delete(port);
      this.#checking.delete(projectId);
    }
  }

  /**
   * Serves the built copy of the project's latest passed check. Records the preview started, and
   * resolves, only once the server itself serves that copy on 127.0.0.1; otherwise it is stopped and
   * nothing is recorded.
   */
  async start(
    command: CommandOf<'project.preview.start'>,
  ): Promise<{ check_id: CheckId; port: number }> {
    const projectId = command.payload.project_id;
    if (this.#closed) throw new Error('the host is closing');
    if (this.#checking.has(projectId)) throw new Error('a check of this project is running');
    if (this.#starting.has(projectId) || this.#running.has(projectId))
      throw new Error('a preview of this project is already running');
    this.#starting.add(projectId);
    let port: number | null = null;
    try {
      port = this.#takePort();
      return await this.#start(projectId, port, causedBy(command.command_id));
    } catch (error) {
      // Nothing started, so nothing holds the port.
      if (port !== null) this.#ports.delete(port);
      throw error;
    } finally {
      this.#starting.delete(projectId);
    }
  }

  async #start(
    projectId: ProjectId,
    port: number,
    cause: Cause,
  ): Promise<{ check_id: CheckId; port: number }> {
    const check = this.#deps.projection.latestCheck(projectId);
    const project = this.#deps.projection.project(projectId);
    if (check === undefined || !check.findings.passed || project?.starter == null) {
      throw new Error('no passed check to serve');
    }
    const cache = this.#cache(projectId, check.check_id);
    // Only the check's own built copy is served; if it is gone, nothing is.
    if (!existsSync(join(cache, 'dist')))
      throw new Error('the checked copy is gone; check the project again');
    await mkdir(join(cache, 'tmp'), { recursive: true, mode: 0o700 });
    const installed = join(this.#deps.starters.installedDir, project.starter);
    const profile = join(cache, 'preview.sb');
    await writeFile(
      profile,
      buildProfile({
        home: this.#deps.home,
        project: join(cache, 'dist'),
        starter: installed,
        cache,
        profiles: existsSync(join(installed, 'input-profiles'))
          ? join(installed, 'input-profiles')
          : null,
      }),
      { mode: 0o600 },
    );
    const env: Record<string, string> = {
      PATH: '/usr/bin:/bin',
      TMPDIR: join(cache, 'tmp'),
      // The built copy is served; the live folder is never named to the server.
      HALCYONIC_PREVIEW_ROOT: join(cache, 'dist'),
      HALCYONIC_PREVIEW_PORT: String(port),
      HALCYONIC_PREVIEW_CACHE: join(cache, 'vite'),
      HALCYONIC_PREVIEW_DEPS: join(installed, 'node_modules'),
      ...(existsSync(join(installed, 'input-profiles'))
        ? { HALCYONIC_PREVIEW_PROFILES: join(installed, 'input-profiles') }
        : {}),
    };
    const args: ServeArgs = {
      profile,
      node: this.#deps.node,
      vite: join(installed, 'node_modules', 'vite', 'bin', 'vite.js'),
      config: join(installed, 'host', 'vite.host.config.mjs'),
      dist: join(cache, 'dist'),
      env,
      cwd: cache,
    };
    // A close() during the awaits above has already stopped every child: spawn none after it (AUDIT S4).
    if (this.#closed) throw new Error('the host is closing');
    const child = (this.#deps.serve ?? serve)(args);
    // A spawn failure is an exit without a pid; the error itself is not needed.
    child.on('error', () => {});
    const running: Running = { checkId: check.check_id, port, child, stopping: false };
    // At spawn, before any await: an exit at once is never missed.
    child.once('exit', () => {
      this.#children.delete(child);
      if (running.stopping || this.#running.get(projectId) !== running) return;
      this.#running.delete(projectId);
      this.#ports.delete(port);
      void this.#removeRecord(projectId);
      this.#deps.recorder.record(
        controlPlaneDraft(
          'project.preview.stopped',
          scope(projectId),
          { check_id: check.check_id, reason: 'server_exited' },
          this.#now(),
          NO_CAUSE,
        ),
      );
    });
    if (child.pid === undefined) throw new Error('the preview server did not start');
    this.#children.add(child);
    try {
      // Recorded before it serves, so a restart can stop a server a crash left mid-start.
      await this.#writeRecord(projectId, { pid: child.pid, port, check_id: check.check_id });
      const serves = await servesCopy(child, port, join(cache, 'dist'), 30_000);
      if (!serves || this.#closed || child.exitCode !== null || child.signalCode !== null)
        throw new Error('the preview server did not serve the checked copy');
    } catch (error) {
      running.stopping = true;
      await stopChild(child);
      await this.#removeRecord(projectId);
      throw error;
    }
    // No await from here to the record: the exit listener cannot run in between.
    this.#running.set(projectId, running);
    this.#deps.recorder.record(
      controlPlaneDraft(
        'project.preview.started',
        scope(projectId),
        { check_id: check.check_id, port },
        this.#now(),
        cause,
      ),
    );
    return { check_id: check.check_id, port };
  }

  /** Stops the project's preview. */
  stop(command: CommandOf<'project.preview.stop'>): Promise<void> {
    return this.#stop(command.payload.project_id, 'asked', causedBy(command.command_id));
  }

  /** Stops every preview this host runs, and every server still starting, as the control plane exits. */
  async close(): Promise<void> {
    this.#closed = true;
    await Promise.all(
      [...this.#running.keys()].map((projectId) =>
        this.#stop(projectId, 'host_restarted', NO_CAUSE),
      ),
    );
    await Promise.all([...this.#children].map((child) => stopChild(child)));
  }

  /**
   * At startup: a preview server a crash left running is stopped, by the pid its record names, only
   * when that process still serves that copy; every preview the journal shows running is recorded
   * stopped.
   */
  async reconcile(): Promise<void> {
    const folder = join(this.#deps.dataDir, 'previews');
    const names = existsSync(folder) ? await readdir(folder) : [];
    for (const name of names) {
      try {
        const record = JSON.parse(await readFile(join(folder, name), 'utf8')) as {
          pid?: unknown;
          check_id?: unknown;
        };
        if (typeof record.pid === 'number' && typeof record.check_id === 'string') {
          await stopRecorded(record.pid, record.check_id);
        }
      } catch (error) {
        this.#deps.logger.warn({ err: error }, 'a preview record could not be read');
      }
      await rm(join(folder, name), { force: true });
    }
    for (const project of this.#deps.projection.projects()) {
      const preview = this.#deps.projection.preview(project.project_id);
      if (preview === undefined) continue;
      this.#deps.recorder.record(
        controlPlaneDraft(
          'project.preview.stopped',
          scope(project.project_id),
          { check_id: preview.check_id, reason: 'host_restarted' },
          this.#now(),
          NO_CAUSE,
        ),
      );
    }
  }

  async #stop(projectId: ProjectId, reason: PreviewStopReason, cause: Cause): Promise<void> {
    const running = this.#running.get(projectId);
    if (running === undefined) return;
    running.stopping = true;
    this.#running.delete(projectId);
    await stopChild(running.child);
    this.#ports.delete(running.port);
    await this.#removeRecord(projectId);
    this.#deps.recorder.record(
      controlPlaneDraft(
        'project.preview.stopped',
        scope(projectId),
        { check_id: running.checkId, reason },
        this.#now(),
        cause,
      ),
    );
  }

  #findings(projectId: ProjectId, result: CheckResult): CheckFindings {
    const page = result.page;
    return {
      tsc: result.tsc,
      build: result.build,
      page:
        page === null
          ? null
          : {
              exceptions: [...page.exceptions],
              exception_count: page.exception_count,
              console_errors: page.console_errors,
              canvases: page.canvases,
              edges_at_3s: page.edges.at_3s,
              edges_at_10s: page.edges.at_10s,
              drawn: page.drawn,
              pictures: page.pictures.map(({ at_ms, sha256 }) => ({ at_ms, sha256 })),
            },
      unknown: result.unknown,
      passed: result.passed,
      reads_confined: this.#readsConfined(projectId),
    };
  }

  /** True only when this host confines Codex and every execution in the project ran on Codex. */
  #readsConfined(projectId: ProjectId): boolean {
    if (!this.#deps.confinesCodex) return false;
    return this.#deps.projection
      .executions()
      .filter((execution) => execution.project_id === projectId)
      .every((execution) => execution.runtime.kind === 'codex');
  }

  /** Takes a port of the range no check or preview of this host holds; synchronous, so never shared. */
  #takePort(): number {
    for (let port = PREVIEW_PORT_FIRST; port <= PREVIEW_PORT_LAST; port += 1) {
      if (!this.#ports.has(port)) {
        this.#ports.add(port);
        return port;
      }
    }
    throw new Error('every preview port is in use');
  }

  #cache(projectId: ProjectId, checkId: CheckId): string {
    return join(this.#deps.dataDir, 'checks', projectId, checkId);
  }

  async #writeRecord(
    projectId: ProjectId,
    record: { pid: number; port: number; check_id: CheckId },
  ) {
    await mkdir(join(this.#deps.dataDir, 'previews'), { recursive: true, mode: 0o700 });
    await writeFile(
      join(this.#deps.dataDir, 'previews', `${projectId}.json`),
      JSON.stringify(record),
      { mode: 0o600 },
    );
  }

  async #removeRecord(projectId: ProjectId) {
    await rm(join(this.#deps.dataDir, 'previews', `${projectId}.json`), { force: true });
  }

  #now() {
    return this.#deps.clock.now().toISOString() as ReturnType<
      typeof controlPlaneDraft
    >['occurred_at'];
  }
}

function scope(projectId: ProjectId) {
  return { project_id: projectId, workstream_id: null, execution_id: null } as const;
}

/** `vite preview` of a built copy, inside the build profile. */
function serve(args: ServeArgs): ChildProcess {
  return spawn(
    SANDBOX_EXEC,
    [
      '-f',
      args.profile,
      args.node,
      args.vite,
      'preview',
      '--configLoader',
      'native',
      '--config',
      args.config,
      '--outDir',
      args.dist,
    ],
    { env: args.env, cwd: args.cwd, stdio: 'ignore' },
  );
}

async function stopChild(child: ChildProcess): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null) return;
  const exited = new Promise<void>((resolve) => child.once('exit', () => resolve()));
  child.kill('SIGTERM');
  const forced = setTimeout(() => child.kill('SIGKILL'), 3000);
  await exited;
  clearTimeout(forced);
}

/** Stops a recorded preview server only while that pid still runs `vite preview` of that check's copy. */
async function stopRecorded(pid: number, checkId: string): Promise<void> {
  const { execFile } = await import('node:child_process');
  const command = await new Promise<string>((resolve) =>
    execFile('/bin/ps', ['-o', 'command=', '-p', String(pid)], (error, stdout) =>
      resolve(error ? '' : stdout),
    ),
  );
  if (!command.includes('vite.js preview') || !command.includes(checkId)) return;
  try {
    process.kill(pid, 'SIGTERM');
  } catch {
    // Gone already.
  }
}
