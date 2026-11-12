import { lstat } from 'node:fs/promises';
import {
  COMMAND_SCHEMA_VERSION,
  type CommandId,
  type ProjectId,
  type Snapshot,
  type Timestamp,
  type WorkstreamId,
} from '@halcyonic/contracts';
import { Projection } from '@halcyonic/domain';
import type { Clock, RuntimeAdapter, Scheduler } from '@halcyonic/runtime-core';
import { Checkpointer } from '../checkpoints/checkpointer.ts';
import { ProjectKeeping } from '../checkpoints/keeping.ts';
import type { CheckpointStore, RestoreHooks } from '../checkpoints/store.ts';
import { readCopiesAfterTurns, TaskCopies } from '../checkpoints/task-copies.ts';
import type { IdGenerator } from '../ids.ts';
import {
  CHECKPOINT_EVERY,
  checkpointCodeIdentity,
  restoreCheckpoint,
  writeCheckpoint,
} from '../journal/checkpoint.ts';
import type { EventJournal } from '../journal/journal.ts';
import { createHostLocations, type HostLocations } from '../locations.ts';
import type { Logger } from '../logger.ts';
import { FileObserver } from '../observer/file-observer.ts';
import { attachWorldReader } from '../observer/world-reader.ts';
import { OwnChecks, type OwnChecksDeps } from '../own-check/own-check.ts';
import { HostPreviews, type HostPreviewsDeps } from '../preview/preview.ts';
import { ProjectShares, type ProjectSharesDeps } from '../preview/share.ts';
import { SecretStore } from '../secrets/store.ts';
import { GitHubSender, type GitHubSenderDeps } from '../sending/github.ts';
import type { HostStarters } from '../starters.ts';
import { AutoFix } from './auto-fix.ts';
import { type CatalogLinks, CommandService } from './command-service.ts';
import { controlPlaneDraft, NO_CAUSE } from './drafts.ts';
import { InstructionQueue } from './instruction-queue.ts';
import type { OllamaUnloader } from './ollama-unload.ts';
import { EventPublisher } from './publisher.ts';
import { Reattacher } from './reattach.ts';
import { Recorder } from './recorder.ts';
import { type HeldSecret, type Redaction, redaction } from './redaction.ts';
import { RuntimeRegistry } from './runtime-registry.ts';

/** The key of a project's own folder among the folders that need an automatic check. */
const PROJECT_FOLDER = '';
export interface ControlPlaneOptions {
  readonly journal: EventJournal;
  readonly adapters: readonly RuntimeAdapter[];
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly scheduler: Scheduler;
  readonly logger: Logger;
  readonly commandTimeoutMs: number;
  /**
   * Where projects may live on the host. Defaults to nowhere, as with no project roots configured,
   * which suits the mock runtime and recorded traces.
   */
  readonly locations?: HostLocations;
  /** How many finished commands a snapshot includes alongside the pending ones. */
  readonly snapshotFinishedCommands?: number;
  /**
   * Where Halcyonic keeps checkpoints of project folders for its own undo (ADR 0036). Absent,
   * undo is off: no checkpoint is taken and `project.restore` is refused.
   */
  readonly checkpoints?: {
    readonly store: CheckpointStore;
    readonly restoreHooks?: RestoreHooks;
    /** For tests: how many newest checkpoints a prune keeps, and how many more gather before one. */
    readonly prune?: { readonly keep: number; readonly slack: number };
    /** How long a close waits for checkpoints and restores under way; 10 s by default. */
    readonly closeWaitMs?: number;
    /**
     * Where every task's own copy of its project's folder lives (ADR 0045), with the folders no
     * copied project may hold. Absent, every task works in its project's folder, one at a time.
     */
    readonly copies?: {
      readonly directory: string;
      readonly protect?: readonly string[];
      /** Whether a runtime is held to a folder it is given; absent, none is, and no copy is made. */
      readonly heldToFolder?: (runtimeId: string, directory: string) => boolean;
    };
    /**
     * Where a saved copy of a project goes, the Mac's Downloads folder, and how a file is shown in
     * Finder (F3). Absent, Show in Finder and Save a Copy are refused.
     */
    readonly keeping?: {
      readonly downloads: string;
      readonly reveal?: (path: string) => Promise<boolean>;
    };
  };
  /**
   * The pinned tunnel that shares a running preview at a public address (F1), with the folder it
   * may use as its home. Absent, sharing is refused. Only built where previews are on.
   */
  readonly share?: Omit<ProjectSharesDeps, 'recorder' | 'projection' | 'clock' | 'logger'>;
  /** Send to GitHub with the person's own git and gh (ADR 0039, amended); absent, it is refused. */
  readonly github?: GitHubSenderDeps;
  /** The starters a new project can begin from (ADR 0037); none by default. */
  readonly starters?: HostStarters;
  /** The person's catalog as last read, so a link is checked against it (ADR 0039); none by default. */
  readonly catalogLinks?: CatalogLinks;
  /**
   * The host's checks and previews of projects made from a starter (ADR 0037). Absent, checks are
   * off and `project.check` and `project.preview.*` are refused.
   */
  readonly previews?: Omit<
    HostPreviewsDeps,
    'recorder' | 'projection' | 'ids' | 'clock' | 'logger' | 'redaction'
  >;
  /**
   * A project's own check, chosen by the person and run confined as an agent (ADR 0055). Absent, a
   * project not made from a starter cannot be checked.
   */
  readonly ownChecks?: Omit<OwnChecksDeps, 'recorder' | 'projection' | 'ids' | 'clock' | 'logger'>;
  /**
   * Whether the Mac reads each task's folder and journals what every turn changed (ADR 0038), and
   * the folders of its own data that a task's folder must not hold for it to be read. Off by
   * default, as for recorded traces and most tests; the control plane's own start turns it on.
   */
  readonly observeFiles?: { readonly protect: readonly string[] };
  /**
   * Whether a failed automatic check goes back on its own to the agent whose turn changed the
   * files, up to a cap (ADR 0044). On wherever checks are, unless false.
   */
  readonly autoFix?: boolean;
  /**
   * Every secret Halcyonic holds or passes to a runtime, asked for each time a runtime's text is
   * journaled or read through to a device, since some change (redaction.ts). None by default;
   * credential shapes still go from error text.
   */
  readonly secrets?: () => Iterable<HeldSecret>;
  /**
   * Where the person's project secrets are kept (ADR 0057): `<dataDir>/secrets`. Absent, a set is
   * refused and no task gets a secret, as for recorded traces and most tests.
   */
  readonly projectSecrets?: { readonly dataDir: string };
  /**
   * Unloads a local Ollama model that Halcyonic loaded once no active execution in Halcyonic uses
   * it (UL1); none by default, so tests do not touch loopback Ollama unless they pass one.
   */
  readonly ollamaUnload?: OllamaUnloader;
  /**
   * Events appended between two checkpoints of the projection (ADR 0050); `CHECKPOINT_EVERY` by
   * default. A checkpoint is also written at close. Fixture journals are never checkpointed.
   */
  readonly checkpointEvery?: number;
}

/**
 * Wires the journal, projection, runtimes and command handling together. It holds no logic of
 * its own beyond startup: rebuilding state from the journal and reconciling after a restart.
 */
export class ControlPlane {
  readonly journal: EventJournal;
  // Whether a runtime reports every tool call it makes, read from its descriptor when asked.
  readonly projection = new Projection({
    reportsToolActivity: (runtimeId) =>
      this.registry.get(runtimeId)?.reports_tool_activity === true,
  });
  readonly publisher: EventPublisher;
  readonly registry = new RuntimeRegistry();
  readonly recorder: Recorder;
  readonly commands: CommandService;
  /** Halcyonic's own undo, or null when it is off. */
  readonly checkpoints: Checkpointer | null;
  /** Every task's own copy (ADR 0045), or null where copies are off. */
  readonly copies: TaskCopies | null;
  /** The starters a new project can begin from, or null when there are none. */
  readonly starters: HostStarters | null;
  /** Folders of Halcyonic's own data that no read of a project's files may reach. */
  readonly protectedFolders: readonly string[];
  /** The Mac's own reading of what each turn changed in its folder (ADR 0038), or null when off. */
  readonly files: FileObserver | null;
  /** The host's checks and previews of starter projects, or null when off. */
  readonly previews: HostPreviews | null;
  /** A project's own check (ADR 0055), or null when off. */
  readonly ownChecks: OwnChecks | null;
  /** Shared links to a project's running preview (F1), where previews and the pinned tunnel are on. */
  readonly shares: ProjectShares | null;
  /** Send to GitHub (ADR 0039, amended), where the person's git is set up. */
  readonly github: GitHubSender | null;
  /** The person's project secrets (ADR 0057), or null where none are kept. */
  readonly projectSecrets: SecretStore | null;
  /** Unloads a local Ollama model that Halcyonic loaded once no execution uses it (UL1), or null. */
  readonly ollamaUnload: OllamaUnloader | null;
  /** Re-attaches what an earlier control plane started (ADR 0046); null for a fixture journal. */
  readonly reattacher: Reattacher | null;
  readonly locations: HostLocations;
  readonly clock: Clock;
  /** How a runtime's or a provider's text loses credentials before a device sees it. */
  readonly redaction: Redaction;
  readonly #ids: IdGenerator;
  readonly #logger: Logger;
  readonly #snapshotFinishedCommands: number;
  /**
   * Per project, the folders that need an automatic check: a task's id for its own copy (ADR 0045),
   * or `PROJECT_FOLDER` for the project's own.
   */
  readonly #autoCheckNeeded = new Map<ProjectId, Set<string>>();
  #autoCheckScheduled = false;
  readonly #autoFix: AutoFix | null;
  #closed = false;
  readonly #closeWaitMs: number;
  /** Events between checkpoints, or null for a fixture journal, never checkpointed (ADR 0050). */
  readonly #checkpointEvery: number | null;
  /** The position of the last checkpoint written or restored. */
  #checkpointed = 0;
  #checkpointDue = false;
  /** Settles once every execution the last restart cut is re-attached or recorded lost. */
  reattached: Promise<void> = Promise.resolve();

  constructor(options: ControlPlaneOptions) {
    this.journal = options.journal;
    this.clock = options.clock;
    this.locations = options.locations ?? createHostLocations([]);
    this.#ids = options.ids;
    this.#logger = options.logger;
    this.projectSecrets =
      options.projectSecrets === undefined ? null : new SecretStore(options.projectSecrets.dataDir);
    // A project secret is known to redaction from the moment the page stages it (ADR 0057).
    this.redaction = redaction(() => [
      ...(options.secrets?.() ?? []),
      ...(this.projectSecrets?.held() ?? []),
    ]);
    this.#snapshotFinishedCommands = options.snapshotFinishedCommands ?? 50;
    this.#closeWaitMs = options.checkpoints?.closeWaitMs ?? 10_000;
    this.publisher = new EventPublisher(options.logger);
    for (const adapter of options.adapters) this.registry.register(adapter);
    this.recorder = new Recorder({
      journal: this.journal,
      projection: this.projection,
      publisher: this.publisher,
      ids: options.ids,
      clock: options.clock,
      logger: options.logger,
    });
    this.checkpoints =
      options.checkpoints === undefined
        ? null
        : new Checkpointer({
            store: options.checkpoints.store,
            recorder: this.recorder,
            projection: this.projection,
            locations: this.locations,
            ids: options.ids,
            clock: options.clock,
            logger: options.logger,
            ...(options.checkpoints.restoreHooks === undefined
              ? {}
              : { restoreHooks: options.checkpoints.restoreHooks }),
            ...(options.checkpoints.prune === undefined
              ? {}
              : { prune: options.checkpoints.prune }),
          });
    this.checkpoints?.attach(this.publisher);
    this.starters = options.starters ?? null;
    this.previews =
      options.previews === undefined
        ? null
        : new HostPreviews({
            ...options.previews,
            recorder: this.recorder,
            projection: this.projection,
            ids: options.ids,
            clock: options.clock,
            logger: options.logger,
            redaction: this.redaction,
          });
    this.ownChecks =
      options.ownChecks === undefined
        ? null
        : new OwnChecks({
            ...options.ownChecks,
            redact: (text) => this.redaction.held(text),
            recorder: this.recorder,
            projection: this.projection,
            ids: options.ids,
            clock: options.clock,
            logger: options.logger,
          });
    this.protectedFolders = options.observeFiles?.protect ?? [];
    this.github = options.github === undefined ? null : new GitHubSender(options.github);
    this.shares =
      this.previews === null || options.share === undefined
        ? null
        : new ProjectShares({
            ...options.share,
            recorder: this.recorder,
            projection: this.projection,
            clock: options.clock,
            logger: options.logger,
          });
    this.files =
      options.observeFiles === undefined
        ? null
        : new FileObserver({ protect: options.observeFiles.protect });
    this.files?.attach({
      publisher: this.publisher,
      recorder: this.recorder,
      projection: this.projection,
      clock: options.clock,
      logger: options.logger,
    });
    // A world's description is read wherever the Mac reads folders at all (ADR 0049).
    if (this.files !== null)
      attachWorldReader({
        publisher: this.publisher,
        recorder: this.recorder,
        projection: this.projection,
        clock: options.clock,
        logger: options.logger,
      });
    this.ollamaUnload = options.ollamaUnload ?? null;
    this.ollamaUnload?.attach(this.publisher, this.projection);
    // Fixture journals are historical: nothing in them is re-attached or lost.
    this.reattacher =
      this.journal.info.origin === 'live'
        ? new Reattacher({
            projection: this.projection,
            recorder: this.recorder,
            registry: this.registry,
            clock: options.clock,
            scheduler: options.scheduler,
            logger: options.logger,
            redaction: this.redaction,
            projectSecrets: this.projectSecrets,
            timeoutMs: options.commandTimeoutMs,
          })
        : null;
    const copies: TaskCopies | null =
      options.checkpoints?.copies === undefined
        ? null
        : new TaskCopies({
            directory: options.checkpoints.copies.directory,
            store: options.checkpoints.store,
            nextId: () => options.ids.next(),
            ...(options.checkpoints.copies.protect === undefined
              ? {}
              : { protect: options.checkpoints.copies.protect }),
            ...(options.checkpoints.copies.heldToFolder === undefined
              ? {}
              : { heldToFolder: options.checkpoints.copies.heldToFolder }),
          });
    this.copies = copies;
    if (copies !== null)
      readCopiesAfterTurns(copies, {
        publisher: this.publisher,
        recorder: this.recorder,
        projection: this.projection,
        now: () => options.clock.now().toISOString(),
      });
    // Said to clients on each runtime that cannot steer, from the same rule as the queue (ADR 0056).
    this.registry.holdsInstructions = this.journal.info.origin === 'live';
    this.commands = new CommandService({
      projection: this.projection,
      recorder: this.recorder,
      registry: this.registry,
      // The queue below runs only on the person's own journal (ADR 0056).
      queuesInstructions: this.journal.info.origin === 'live',
      locations: this.locations,
      protectedFolders: this.protectedFolders,
      ids: options.ids,
      clock: options.clock,
      scheduler: options.scheduler,
      logger: options.logger,
      commandTimeoutMs: options.commandTimeoutMs,
      redaction: this.redaction,
      checkpoints: this.checkpoints,
      copies,
      keeping:
        options.checkpoints?.keeping === undefined
          ? null
          : new ProjectKeeping({
              store: options.checkpoints.store,
              downloads: options.checkpoints.keeping.downloads,
              now: () => options.clock.now(),
              ...(options.checkpoints.keeping.reveal === undefined
                ? {}
                : { reveal: options.checkpoints.keeping.reveal }),
            }),
      starters: this.starters,
      files: this.files,
      catalogLinks: options.catalogLinks ?? null,
      previews: this.previews,
      ownChecks: this.ownChecks,
      ollamaUnload: this.ollamaUnload,
      reattacher: this.reattacher,
      shares: this.shares,
      github: this.github,
      projectSecrets: this.projectSecrets,
    });
    this.#checkpointEvery =
      this.journal.info.origin === 'live' ? (options.checkpointEvery ?? CHECKPOINT_EVERY) : null;
    this.#rebuild();
    // Once the journal is folded: only the values of names it lists are kept.
    this.projectSecrets?.open((projectId) => this.projection.project(projectId)?.secrets ?? []);
    // Once the journal is folded: a share a restart left recorded as live is recorded stopped.
    this.shares?.attach(this.publisher);
    this.#autoFix =
      (this.previews === null && this.ownChecks === null) || options.autoFix === false
        ? null
        : new AutoFix({
            projection: this.projection,
            recorder: this.recorder,
            publisher: this.publisher,
            commands: this.commands,
            registry: this.registry,
            previews: this.previews,
            ownChecks: this.ownChecks,
            ids: options.ids,
            clock: options.clock,
            logger: options.logger,
            live: this.journal.info.origin === 'live',
          });
    this.#autoFix?.attach();
    // The person's instructions held for a runtime that cannot steer (ADR 0056).
    new InstructionQueue({
      projection: this.projection,
      commands: this.commands,
      live: this.journal.info.origin === 'live',
    }).attach(this.publisher);
    if (this.#checkpointEvery !== null) this.#attachCheckpoints(this.#checkpointEvery);
    if (this.previews !== null || this.ownChecks !== null) this.#attachAutoCheck();
  }

  /**
   * Checks a starter project on its own after a turn that changed files in its folder (A1): one
   * check at a time per project, journaled as an internal command, waiting if a check or another
   * task in the project is still busy.
   */
  #attachAutoCheck(): void {
    this.publisher.subscribe(({ event }) => {
      if (this.#closed) return;
      if (event.event_type === 'execution.files.observed') {
        const { created, changed, deleted } = event.payload;
        if (created.total === 0 && changed.total === 0 && deleted.total === 0) return;
        const project = this.projection.project(event.project_id);
        if (!this.#autoChecked(event.project_id)) return;
        if (project === undefined) return;
        // A turn in a task's own copy needs that copy checked, not the project's folder.
        const copy = this.projection.workstream(event.workstream_id)?.copy;
        const directory = this.projection.execution(event.execution_id)?.directory ?? null;
        const key =
          copy?.state === 'own' && directory === copy.directory
            ? event.workstream_id
            : PROJECT_FOLDER;
        const needed = this.#autoCheckNeeded.get(event.project_id) ?? new Set<string>();
        needed.add(key);
        this.#autoCheckNeeded.set(event.project_id, needed);
        this.#scheduleAutoChecks();
        return;
      }
      if (
        event.event_type === 'command.accepted' &&
        event.payload.command.command_type === 'project.check' &&
        event.project_id !== null
      ) {
        const needed = this.#autoCheckNeeded.get(event.project_id);
        needed?.delete(event.payload.command.payload.workstream_id ?? PROJECT_FOLDER);
        if (needed?.size === 0) this.#autoCheckNeeded.delete(event.project_id);
        return;
      }
      if (
        (event.event_type === 'command.completed' ||
          event.event_type === 'command.failed' ||
          event.event_type === 'runtime.turn.completed' ||
          event.event_type === 'runtime.turn.failed' ||
          event.event_type === 'runtime.turn.interrupted' ||
          event.event_type === 'execution.state_unknown' ||
          event.event_type === 'runtime.connection.lost') &&
        event.project_id !== null &&
        this.#autoCheckNeeded.has(event.project_id)
      ) {
        this.#scheduleAutoChecks();
      }
    });
  }

  /**
   * Whether the project is checked on its own after a turn: a starter's but a world's, which has no
   * build (ADR 0049), where checks are on; or one whose own check the person chose (ADR 0055).
   */
  #autoChecked(projectId: ProjectId): boolean {
    const project = this.projection.project(projectId);
    if (project === undefined || project.location === null) return false;
    if (project.starter === null)
      return this.ownChecks !== null && this.projection.ownCheck(projectId) !== undefined;
    // A starter that declares no build is not checked after a turn (R2), whatever its id.
    return this.previews !== null && this.starters?.declares(project.starter).check !== 'none';
  }

  #scheduleAutoChecks(): void {
    if (this.#autoCheckScheduled || this.#closed) return;
    this.#autoCheckScheduled = true;
    queueMicrotask(() => {
      this.#autoCheckScheduled = false;
      this.#drainAutoChecks();
    });
  }

  #drainAutoChecks(): void {
    if (this.#closed) return;
    for (const [projectId, needed] of [...this.#autoCheckNeeded]) {
      const project = this.projection.project(projectId);
      if (project === undefined || project.location === null || !this.#autoChecked(projectId)) {
        this.#autoCheckNeeded.delete(projectId);
        continue;
      }
      // One check at a time per project: the next waits for this one's outcome.
      for (const key of [...needed]) {
        const copy = key === PROJECT_FOLDER ? undefined : this.projection.workstream(key)?.copy;
        if (key !== PROJECT_FOLDER && copy?.state !== 'own') {
          needed.delete(key);
          continue;
        }
        const busy =
          copy?.state === 'own'
            ? this.#copyBusy(projectId, key as WorkstreamId)
            : this.projection.projectBusy(projectId) !== null;
        if (busy) continue;
        needed.delete(key);
        if (needed.size === 0) this.#autoCheckNeeded.delete(projectId);
        const commandId = this.#ids.next() as CommandId;
        this.#autoFix?.checkQueued(
          projectId,
          commandId,
          copy?.state === 'own' ? copy.directory : project.location.path,
        );
        this.commands.submit(
          {
            schema_version: COMMAND_SCHEMA_VERSION,
            command_id: commandId,
            issued_at: this.clock.now().toISOString() as Timestamp,
            client: { name: 'halcyonic-control-plane', version: null, device_label: null },
            command_type: 'project.check',
            payload: {
              project_id: projectId,
              ...(copy?.state === 'own' ? { workstream_id: key as WorkstreamId } : {}),
            },
          },
          'internal',
          null,
        );
        break;
      }
    }
  }

  /**
   * Whether a check of a task's own copy must wait, as its admission says: its task at work, or
   * anything of it, or a check, restore or preview start of the project, without its outcome.
   */
  #copyBusy(projectId: ProjectId, workstreamId: WorkstreamId): boolean {
    if (this.projection.executionsAtWork().some((facts) => facts.workstreamId === workstreamId))
      return true;
    return this.projection
      .pendingCommands()
      .some(
        (pending) =>
          pending.workstream_id === workstreamId ||
          (pending.project_id === projectId &&
            (pending.command_type === 'project.check' ||
              pending.command_type === 'project.restore' ||
              pending.command_type === 'project.preview.start')),
      );
  }

  #rebuild(): void {
    let events = 0;
    // A live journal's checkpoint, when it can be trusted, holds the fold of everything up to its
    // position; only the events after it are folded (ADR 0050). Fixture journals are folded whole.
    let from = 0;
    let checkpoint: string | null = null;
    if (this.#checkpointEvery !== null) {
      checkpointCodeIdentity();
      const restored = restoreCheckpoint(this.journal, this.projection);
      if (restored.restored) {
        from = restored.position;
        this.#checkpointed = restored.position;
        checkpoint = `restored at position ${restored.position}`;
      } else if (restored.reason !== null) {
        checkpoint = `not taken: ${restored.reason}`;
        this.#logger.warn(
          { journal_id: this.journal.info.journal_id, reason: restored.reason },
          'checkpoint not taken; folding the whole journal',
        );
      }
    }
    for (const stored of this.journal.readAll(from)) {
      const { notes } = this.projection.apply(stored);
      for (const note of notes) {
        this.#logger.warn(
          {
            note: note.code,
            event_id: note.event_id,
            position: stored.position,
            detail: note.message,
          },
          'projection did not apply part of a journaled event',
        );
      }
      events += 1;
    }
    this.#logger.info(
      {
        journal_id: this.journal.info.journal_id,
        events,
        position: this.projection.position,
        checkpoint,
      },
      'state rebuilt from journal',
    );
  }

  /** Writes a checkpoint of the projection once enough events came since the last (ADR 0050). */
  #attachCheckpoints(every: number): void {
    this.publisher.subscribe(({ position }) => {
      if (this.#closed || this.#checkpointDue || position - this.#checkpointed < every) return;
      this.#checkpointDue = true;
      setImmediate(() => {
        this.#checkpointDue = false;
        if (!this.#closed) this.#checkpoint();
      });
    });
  }

  #checkpoint(): void {
    if (this.projection.position === this.#checkpointed) return;
    const written = writeCheckpoint(this.journal, this.projection, this.clock.now());
    if (written === null) return;
    if ('error' in written) {
      this.#logger.warn(
        { err: written.error },
        'checkpoint not written; the journal holds everything',
      );
      return;
    }
    this.#checkpointed = written.position;
    this.#logger.info({ position: written.position }, 'checkpoint written');
  }

  /**
   * Records what a restart made unknowable, then re-attaches each execution it cut where the
   * runtime keeps its session, or records it lost (ADR 0046); `reattached` settles once each has.
   * Fixture journals are historical and left as is.
   */
  reconcile(): void {
    if (this.journal.info.origin !== 'live') return;
    const { cut, ...result } = this.commands.reconcileAfterRestart();
    if (result.executions > 0 || result.commands > 0) {
      this.#logger.warn(result, 'marked in-flight work unknown after restart');
    }
    this.reattached =
      this.reattacher
        ?.reattachCut(cut)
        .catch((error: unknown) =>
          this.#logger.warn({ err: error }, 'executions not re-attached'),
        ) ?? Promise.resolve();
    // A preview the last run left serving is stopped, and recorded stopped.
    void this.previews
      ?.reconcile()
      .catch((error: unknown) => this.#logger.warn({ err: error }, 'previews not reconciled'));
  }

  /**
   * At a start, before any task can start (ADR 0045, amended): records each copy this journal holds
   * whose folder is gone (`workstream.copy_missing`, once) or back again (`workstream.copy_found`),
   * so the task says it, and counts what the copies' folder holds that the journal does not,
   * removing none of it. Null where copies are off or the journal is not this Mac's own.
   */
  async noteCopies(): Promise<{
    readonly missing: number;
    readonly found: number;
    readonly unheld: number;
    readonly other: number;
  } | null> {
    if (this.copies === null || this.journal.info.origin !== 'live') return null;
    let missing = 0;
    let found = 0;
    const held = new Set<string>();
    for (const workstream of this.projection.workstreams()) {
      const copy = workstream.copy;
      if (copy?.state !== 'own') continue;
      held.add(copy.directory);
      const there = (await lstat(copy.directory).catch(() => null))?.isDirectory() === true;
      if (there === (copy.missing_at === undefined)) continue;
      this.recorder.record(
        controlPlaneDraft(
          there ? 'workstream.copy_found' : 'workstream.copy_missing',
          {
            project_id: workstream.project_id,
            workstream_id: workstream.workstream_id,
            execution_id: null,
          },
          { directory: copy.directory },
          this.clock.now().toISOString() as Timestamp,
          NO_CAUSE,
        ),
      );
      if (there) found += 1;
      else missing += 1;
    }
    return { missing, found, ...(await this.copies.unheld(held)) };
  }

  snapshot(): Snapshot {
    return {
      journal: this.journal.info,
      position: this.projection.position,
      projects: this.projection.projects(),
      workstreams: this.projection.workstreams(),
      executions: this.projection.executions(),
      commands: this.projection.commands(this.#snapshotFinishedCommands),
      runtimes: this.registry.descriptors(),
    };
  }

  async close(): Promise<void> {
    this.#closed = true;
    this.#autoCheckNeeded.clear();
    this.#autoFix?.close();
    // A shared link stops before the preview it reaches.
    await this.shares?.close();
    await this.previews?.close();
    // A project's own check running now stops with the control plane (AUDIT3 4).
    this.ownChecks?.close();
    await this.registry.closeAll(this.#logger);
    await this.ollamaUnload?.close();
    // Undo checkpoints and restores under way journal their outcomes before the journal closes.
    await this.checkpoints?.settle(this.#closeWaitMs);
    if (this.#checkpointEvery !== null) this.#checkpoint();
    this.journal.close();
  }
}
