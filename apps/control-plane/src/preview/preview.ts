import { type ChildProcess, spawn } from 'node:child_process';
import { closeSync, constants, existsSync, openSync, readSync } from 'node:fs';
import { mkdir, readdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { join } from 'node:path';
import {
  type CheckFindings,
  type CheckId,
  type CommandOf,
  PREVIEW_PORT_FIRST,
  PREVIEW_PORT_LAST,
  type PreviewStopReason,
  type ProjectId,
  type WorkstreamId,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import { type Cause, causedBy, controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import type { Recorder } from '../core/recorder.ts';
import { fitted, type Redaction } from '../core/redaction.ts';
import type { IdGenerator } from '../ids.ts';
import type { Logger } from '../logger.ts';
import { HostStarters, templateDigest } from '../starters.ts';
import {
  type CheckOptions,
  type CheckResult,
  MAX_EXCEPTION_CHARS,
  MAX_EXCEPTIONS,
  type OwnReference,
  runCheck,
  servesCopy,
} from './check.ts';
import { type CheckOutput, checkOutput } from './check-output.ts';
import {
  browserBinary,
  browserProblem,
  HostPlays,
  type HostPlaysDeps,
  type PlayEnd,
} from './play.ts';
import { buildProfile, SANDBOX_EXEC } from './profiles.ts';
import { hostFilesDigest, referenceKey, StarterReferences } from './reference.ts';

export interface HostPreviewsDeps {
  readonly recorder: Recorder;
  readonly projection: Projection;
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly logger: Logger;
  /** How a page's error text loses what Halcyonic holds and what looks like a key before it is journaled. */
  readonly redaction: Redaction;
  /** Halcyonic's data directory: checks and preview records live under it. */
  readonly dataDir: string;
  /** The person's home folder, which the check's profiles keep out of reach. */
  readonly home: string;
  /** Node, by absolute path, for the starter's own TypeScript and Vite. */
  readonly node: string;
  /**
   * The chrome-headless-shell copy in the data folder that both the check and a play run, and its
   * binary's recorded SHA-256 (S1), or null: checks then cannot pass and no game is played.
   */
  readonly browser: { readonly folder: string; readonly sha256: string } | null;
  /** The launcher the browser starts through, checked now, or null when it is missing or changed (S1). */
  readonly launcher: () => string | null;
  /** For tests: the browser copy's own check. */
  readonly browserProblem?: (folder: string, sha256: string) => string | null;
  /** The repository's tracked starters and the data directory's installed ones. */
  readonly starters: { readonly trackedDir: string; readonly installedDir: string };
  /** Whether this host confines a starter project's Codex to its folder (ADR 0041). */
  readonly confinesCodex: boolean;
  /** Playing a checked game on this Mac (P-A): its timings and, for tests, its browser's start. */
  readonly play?: Pick<
    HostPlaysDeps,
    'launch' | 'viewerWaitMs' | 'viewerPorts' | 'viewerGraceMs' | 'audible'
  > & {
    /** Opens the viewer's one-time link on the Mac. */
    readonly openViewer?: (link: string) => void;
  };
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
  /** The plays on this Mac, each of the copy a running preview serves. */
  readonly plays: HostPlays;
  /** The starters' own pictures each check compares a project's with (DRAWN). */
  readonly #references: StarterReferences;

  constructor(deps: HostPreviewsDeps) {
    this.#deps = deps;
    this.#references = new StarterReferences(join(deps.dataDir, 'checks', 'references'));
    this.plays = new HostPlays({
      dataDir: deps.dataDir,
      home: deps.home,
      browser: deps.browser,
      launcher: deps.launcher,
      ...(deps.browserProblem === undefined ? {} : { browserProblem: deps.browserProblem }),
      ...deps.play,
      onEnded: (project, checkId, reason) => {
        const projectId = project as ProjectId;
        // Only a play the journal shows started is recorded stopped; one that failed to start never was.
        if (this.#deps.projection.macPlay(projectId) !== checkId) return;
        this.#deps.recorder.record(
          controlPlaneDraft(
            'project.mac_play.stopped',
            scope(projectId),
            { check_id: checkId as CheckId, reason },
            this.#now(),
            NO_CAUSE,
          ),
        );
      },
    });
  }

  /**
   * Whether this host can play a checked game on the Mac now: its browser is set up and its launcher
   * is in place and unchanged, so the browser has none of the app's macOS permissions.
   */
  get canPlay(): boolean {
    return this.#deps.browser !== null && this.#deps.launcher() !== null;
  }

  /**
   * Plays the copy the project's running preview serves, in the contained browser, for Halcyonic's
   * viewer to show. Records the play started once the browser has opened the page.
   */
  async openOnMac(command: CommandOf<'project.open_on_mac'>): Promise<void> {
    const projectId = command.payload.project_id;
    if (this.#closed) throw new Error('the host is closing');
    const running = this.#running.get(projectId);
    const latest = this.#deps.projection.latestCheck(projectId);
    if (running === undefined || running.checkId !== latest?.check_id || latest.files !== 'current')
      throw new Error('no preview of the latest check is running');
    const { link } = await this.plays.start(projectId, running.checkId, running.port);
    // The preview stopped while the browser started: so does the play, and it is not recorded.
    if (this.#running.get(projectId) !== running) {
      await this.plays.stop(projectId, 'preview_stopped');
      throw new Error('the preview stopped while the game opened');
    }
    this.#deps.recorder.record(
      controlPlaneDraft(
        'project.mac_play.started',
        scope(projectId),
        { check_id: running.checkId },
        this.#now(),
        causedBy(command.command_id),
      ),
    );
    // The link is never journaled or logged: its code opens the viewer once.
    this.#deps.play?.openViewer?.(link);
  }

  /** Checks the project and records what was found; resolves with the check's id and outcome. */
  async check(
    command: CommandOf<'project.check'>,
  ): Promise<{ check_id: CheckId; passed: boolean; ready: boolean }> {
    const projectId = command.payload.project_id;
    const project = this.#deps.projection.project(projectId);
    if (project?.starter == null || project.location === null)
      throw new Error('not a starter project');
    // A task's own copy, when the check names its task, so its work is checked before it is added
    // (ADR 0045); else the project's own folder.
    const taskId = command.payload.workstream_id;
    const copy = taskId === undefined ? undefined : this.#deps.projection.workstream(taskId)?.copy;
    if (taskId !== undefined && copy?.state !== 'own')
      throw new Error('the task has no copy of its own');
    const folder = copy?.state === 'own' ? copy.directory : project.location.path;
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
      port = await this.#takePort();
      // The browser copy is used only while it is the one recorded; the launcher only while unchanged.
      const browser = this.#deps.browser;
      const chrome =
        browser !== null &&
        (this.#deps.browserProblem ?? browserProblem)(browser.folder, browser.sha256) === null
          ? browserBinary(browser.folder)
          : null;
      const launcher = this.#deps.launcher();
      const common = {
        node: this.#deps.node,
        chrome,
        launcher,
        home: this.#deps.home,
        starter: installed,
        hostSource: join(this.#deps.starters.trackedDir, project.starter, 'host'),
        template: join(this.#deps.starters.trackedDir, project.starter, 'template'),
        profiles: existsSync(profiles) ? profiles : null,
      };
      const reference = await this.#reference(projectId, project.starter, common);
      const cache = this.#cache(projectId, checkId);
      const result = await (this.#deps.runCheck ?? runCheck)({
        ...common,
        project: folder,
        cache,
        port,
        reference,
        onServer: (pid) =>
          this.#writeRecord(projectId, { pid, copy: join(cache, 'dist') }, 'check'),
      });
      const findings = this.#findings(
        projectId,
        result,
        chrome === null ? null : launcher !== null,
      );
      this.#deps.recorder.record(
        controlPlaneDraft(
          'project.check.recorded',
          scope(projectId),
          {
            check_id: checkId,
            findings,
            ...(taskId === undefined ? {} : { workstream_id: taskId }),
          },
          this.#now(),
          cause,
        ),
      );
      return { check_id: checkId, passed: findings.passed, ready: findings.ready === true };
    } finally {
      if (port !== null) this.#ports.delete(port);
      try {
        // Its server stopped with the check, however the check ended.
        await this.#removeRecord(projectId, 'check');
      } finally {
        this.#checking.delete(projectId);
      }
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
      port = await this.#takePort();
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
    if (
      check === undefined ||
      check.files !== 'current' ||
      !check.findings.passed ||
      project?.starter == null
    ) {
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
      void this.plays.stop(projectId, 'preview_stopped');
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
    await this.plays.close();
    await Promise.all(
      [...this.#running.keys()].map((projectId) =>
        this.#stop(projectId, 'host_restarted', NO_CAUSE),
      ),
    );
    await Promise.all([...this.#children].map((child) => stopChild(child)));
  }

  /**
   * At startup: a preview's server or a check's own that a crash left running is stopped, by the pid
   * its record names, only when that process still serves that copy; every preview the journal shows
   * running is recorded stopped.
   */
  async reconcile(): Promise<void> {
    const folder = join(this.#deps.dataDir, 'previews');
    const names = existsSync(folder) ? await readdir(folder) : [];
    for (const name of names) {
      try {
        const record = JSON.parse(await readFile(join(folder, name), 'utf8')) as {
          pid?: unknown;
          check_id?: unknown;
          copy?: unknown;
        };
        if (typeof record.pid === 'number' && typeof record.copy === 'string') {
          await stopRecorded(record.pid, `--outDir ${record.copy}`);
        } else if (typeof record.pid === 'number' && typeof record.check_id === 'string') {
          await stopRecorded(record.pid, record.check_id);
        }
      } catch (error) {
        this.#deps.logger.warn({ err: error }, 'a preview record could not be read');
      }
      await rm(join(folder, name), { force: true });
    }
    await this.plays.sweep();
    for (const project of this.#deps.projection.projects()) {
      const play = this.#deps.projection.macPlay(project.project_id);
      if (play !== undefined)
        this.#deps.recorder.record(
          controlPlaneDraft(
            'project.mac_play.stopped',
            scope(project.project_id),
            { check_id: play, reason: 'host_stopped' },
            this.#now(),
            NO_CAUSE,
          ),
        );

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
    await this.plays.stop(projectId, PLAY_END[reason]);
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

  /**
   * What the project's own picture is compared with (DRAWN): the pristine starter's picture under the
   * key of everything that shapes it, made from the tracked template, by a check of its own in a folder
   * of the host's, the first time it is needed. A project begun from another template is not compared;
   * when none can be made, the check is told why and the page is not drawn.
   */
  async #reference(
    projectId: ProjectId,
    starterId: string,
    common: Omit<CheckOptions, 'project' | 'cache' | 'port' | 'reference' | 'onServer'>,
  ): Promise<OwnReference> {
    const template = templateDigest(common.template);
    if (template === null)
      return { kind: 'unavailable', why: "the starter's template could not be read" };
    if (this.#deps.projection.starterTemplate(projectId) !== template)
      return { kind: 'other_template' };
    const browser = this.#deps.browser;
    if (common.chrome === null || browser === null)
      return { kind: 'unavailable', why: 'no browser is set up' };
    const starters = new HostStarters(this.#deps.starters);
    const lockfile = starters.lockfile(starterId);
    const host = hostFilesDigest(common.hostSource);
    if (lockfile === null || host === null)
      return { kind: 'unavailable', why: "the starter's packages or host files could not be read" };
    const key = referenceKey({ template, lockfile, host, browser: browser.sha256 });
    const made = this.#references.get(starterId, key, async (work) => {
      const project = join(work, 'project');
      const applied = starters.apply(starterId, project);
      if (!applied.ok || applied.template !== template || applied.lockfile !== lockfile)
        return null;
      const port = await this.#takePort();
      try {
        const result = await (this.#deps.runCheck ?? runCheck)({
          ...common,
          project,
          cache: join(work, 'cache'),
          port,
          reference: { kind: 'making' },
          onServer: (pid) =>
            this.#writeRecord(projectId, { pid, copy: join(work, 'cache', 'dist') }, 'reference'),
        });
        // A starter that does not build, throws, or gives no picture of its own makes none.
        const frame = result.page?.own.frame ?? null;
        if (frame === null || result.tsc.exit !== 0 || result.build.exit !== 0) return null;
        if (result.page?.exception_count !== 0) return null;
        return await readFile(frame.file);
      } finally {
        this.#ports.delete(port);
        await this.#removeRecord(projectId, 'reference');
      }
    });
    // A folder that cannot be written reads as no picture, never as a failed check command.
    const png = await made.catch(() => null);
    return png === null
      ? { kind: 'unavailable', why: "the starter's own picture could not be taken" }
      : { kind: 'compared', png };
  }

  /** `ownPermissions` is whether the browser started through the launcher, or null when none was started. */
  #findings(
    projectId: ProjectId,
    result: CheckResult,
    ownPermissions: boolean | null,
  ): CheckFindings {
    const page = result.page;
    return {
      tsc: result.tsc,
      build: result.build,
      page:
        page === null
          ? null
          : {
              // A page can throw what its build pulled in, and every paired device reads the journal:
              // secrets out of each whole text, then the cut, then the distinct ones (P1b).
              exceptions: [
                ...new Set(
                  page.exceptions.map((text) =>
                    fitted(this.#deps.redaction.errorText(text), MAX_EXCEPTION_CHARS),
                  ),
                ),
              ].slice(0, MAX_EXCEPTIONS),
              exception_count: page.exception_count,
              console_errors: page.console_errors,
              canvases: page.canvases,
              edges_at_3s: page.edges.at_3s,
              edges_at_10s: page.edges.at_10s,
              drawn: page.drawn,
              own: { share: page.own.share, reference: page.own.reference },
              pictures: page.pictures.map(({ at_ms, sha256 }) => ({ at_ms, sha256 })),
            },
      unknown: result.unknown,
      passed: result.passed,
      reads_confined: this.#readsConfined(projectId),
      laptop: result.laptop === null ? null : { ...result.laptop },
      vr: result.vr === null ? null : { ...result.vr },
      notes: {
        scanned: result.notes.scanned,
        items: result.notes.items.map((item) => ({ ...item })),
      },
      ready: result.ready,
      // Only when the check's browser ran: after a build, with a browser set up.
      ...(ownPermissions === null || result.build.exit !== 0
        ? {}
        : { own_permissions: ownPermissions }),
    };
  }

  /**
   * True only when this host confines Codex and every execution in the project recorded, at its
   * start, that it ran confined (S2): observed per execution, never inferred from the runtime.
   */
  #readsConfined(projectId: ProjectId): boolean {
    if (!this.#deps.confinesCodex) return false;
    const projection = this.#deps.projection;
    return projection
      .executions()
      .filter((execution) => execution.project_id === projectId)
      .every((execution) => projection.executionConfined(execution.execution_id));
  }

  /**
   * Takes the first port of the range that no check or preview of this host holds and that the
   * system lets it listen on now. Each port is reserved before it is tried, synchronously, so two
   * takes never share one; one another process holds is passed over. Another process can still
   * take the port between this try and the server's own listen; the start then fails, since it
   * records a preview only once its own server answers there.
   */
  async #takePort(): Promise<number> {
    for (let port = PREVIEW_PORT_FIRST; port <= PREVIEW_PORT_LAST; port += 1) {
      if (this.#ports.has(port)) continue;
      this.#ports.add(port);
      if (await listenable(port)) return port;
      this.#ports.delete(port);
    }
    throw new Error('every preview port is in use');
  }

  /**
   * The first lines of the project's latest check where its type check or build failed, from the
   * check's own folder, for the agent with the person's next instruction (FX1); null otherwise.
   */
  failedCheckOutput(
    projectId: ProjectId,
    workstreamId: WorkstreamId | null = null,
  ): { checkId: CheckId; output: CheckOutput } | null {
    const check = this.#deps.projection.latestCheck(projectId);
    // A check of one task's own copy is that task's agent's alone (ADR 0045), and hides that folder.
    if (check !== undefined && check.workstream_id !== null && check.workstream_id !== workstreamId)
      return null;
    const copy =
      check?.workstream_id == null
        ? undefined
        : this.#deps.projection.workstream(check.workstream_id)?.copy;
    const folder =
      copy?.state === 'own'
        ? copy.directory
        : this.#deps.projection.project(projectId)?.location?.path;
    if (
      check === undefined ||
      check.files !== 'current' ||
      folder === undefined ||
      check.findings.passed
    )
      return null;
    const cache = this.#cache(projectId, check.check_id);
    const output = checkOutput({
      typecheck: { exit: check.findings.tsc.exit, log: readStart(join(cache, 'tsc.log')) },
      build: { exit: check.findings.build.exit, log: readStart(join(cache, 'build.log')) },
      projectFolder: folder,
      cacheFolder: cache,
      dataDir: this.#deps.dataDir,
      home: this.#deps.home,
    });
    return output === null ? null : { checkId: check.check_id, output };
  }

  #cache(projectId: ProjectId, checkId: CheckId): string {
    return join(this.#deps.dataDir, 'checks', projectId, checkId);
  }

  /** The record of a server this host runs for the project, each kind in a file of its own. */
  #recordFile(projectId: ProjectId, server: RecordedServer): string {
    const name = server === 'preview' ? `${projectId}.json` : `${projectId}.${server}.json`;
    return join(this.#deps.dataDir, 'previews', name);
  }

  async #writeRecord(
    projectId: ProjectId,
    record: { pid: number; port: number; check_id: CheckId } | { pid: number; copy: string },
    server: RecordedServer = 'preview',
  ) {
    await mkdir(join(this.#deps.dataDir, 'previews'), { recursive: true, mode: 0o700 });
    // Written beside, then renamed, so a reader (a restart's reconcile) never sees half a record.
    // A crash between the two leaves only the side file, which reconcile reads, cannot use and removes.
    const file = this.#recordFile(projectId, server);
    const writing = `${file}.${process.pid}.writing`;
    await writeFile(writing, JSON.stringify(record), { mode: 0o600 });
    await rename(writing, file);
  }

  async #removeRecord(projectId: ProjectId, server: RecordedServer = 'preview') {
    await rm(this.#recordFile(projectId, server), { force: true });
  }

  #now() {
    return this.#deps.clock.now().toISOString() as ReturnType<
      typeof controlPlaneDraft
    >['occurred_at'];
  }
}

/** A server the host records while it runs: a preview, a check's own, or the starter's check's (S1). */
type RecordedServer = 'preview' | 'check' | 'reference';

/** Why a play ends when its preview stops. */
const PLAY_END: Record<PreviewStopReason, PlayEnd> = {
  asked: 'preview_stopped',
  new_check: 'newer_check',
  server_exited: 'preview_stopped',
  host_restarted: 'host_stopped',
};

/** Whether a server can listen on 127.0.0.1:`port` now: it listens, then closes at once. */
function listenable(port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const probe = createServer();
    probe.once('error', () => resolve(false));
    probe.listen({ port, host: '127.0.0.1', exclusive: true }, () =>
      probe.close(() => resolve(true)),
    );
  });
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

/**
 * Stops a recorded server only while that pid still runs `vite preview` with `mark` in its command
 * line: the check a preview's copy is of, or the copy a check's own server serves.
 */
async function stopRecorded(pid: number, mark: string): Promise<void> {
  const { execFile } = await import('node:child_process');
  const command = await new Promise<string>((resolve) =>
    execFile('/bin/ps', ['-o', 'command=', '-p', String(pid)], (error, stdout) =>
      resolve(error ? '' : stdout),
    ),
  );
  if (!command.includes('vite.js preview') || !command.includes(mark)) return;
  try {
    process.kill(pid, 'SIGTERM');
  } catch {
    // Gone already.
  }
}

/** The first 64 KB of a file the host wrote, never through a link; null when it cannot be read. */
function readStart(path: string): string | null {
  let fd: number | undefined;
  try {
    fd = openSync(path, constants.O_RDONLY | constants.O_NOFOLLOW);
    const buffer = Buffer.alloc(64 * 1024);
    const read = readSync(fd, buffer, 0, buffer.length, 0);
    return buffer.subarray(0, read).toString('utf8');
  } catch {
    return null;
  } finally {
    if (fd !== undefined) closeSync(fd);
  }
}
