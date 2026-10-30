import type { Snapshot } from '@halcyonic/contracts';
import { Projection } from '@halcyonic/domain';
import type { Clock, RuntimeAdapter, Scheduler } from '@halcyonic/runtime-core';
import { Checkpointer } from '../checkpoints/checkpointer.ts';
import type { CheckpointStore, RestoreHooks } from '../checkpoints/store.ts';
import type { IdGenerator } from '../ids.ts';
import type { EventJournal } from '../journal/journal.ts';
import { createHostLocations, type HostLocations } from '../locations.ts';
import type { Logger } from '../logger.ts';
import { FileObserver } from '../observer/file-observer.ts';
import type { HostStarters } from '../starters.ts';
import { type CatalogLinks, CommandService } from './command-service.ts';
import { EventPublisher } from './publisher.ts';
import { Recorder } from './recorder.ts';
import { type HeldSecret, type Redaction, redaction } from './redaction.ts';
import { RuntimeRegistry } from './runtime-registry.ts';

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
  readonly checkpoints?: { readonly store: CheckpointStore; readonly restoreHooks?: RestoreHooks };
  /** The starters a new project can begin from (ADR 0037); none by default. */
  readonly starters?: HostStarters;
  /** The person's catalog as last read, so a link is checked against it (ADR 0039); none by default. */
  readonly catalogLinks?: CatalogLinks;
  /**
   * Whether the Mac reads each task's folder and journals what every turn changed (ADR 0038), and
   * the folders of its own data that a task's folder must not hold for it to be read. Off by
   * default, as for recorded traces and most tests; the control plane's own start turns it on.
   */
  readonly observeFiles?: { readonly protect: readonly string[] };
  /**
   * Every secret Halcyonic holds or passes to a runtime, asked for each time a runtime's text is
   * journaled or read through to a device, since some change (redaction.ts). None by default;
   * credential shapes still go from error text.
   */
  readonly secrets?: () => Iterable<HeldSecret>;
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
  /** The starters a new project can begin from, or null when there are none. */
  readonly starters: HostStarters | null;
  /** The Mac's own reading of what each turn changed in its folder (ADR 0038), or null when off. */
  readonly files: FileObserver | null;
  readonly locations: HostLocations;
  readonly clock: Clock;
  /** How a runtime's or a provider's text loses credentials before a device sees it. */
  readonly redaction: Redaction;
  readonly #logger: Logger;
  readonly #snapshotFinishedCommands: number;

  constructor(options: ControlPlaneOptions) {
    this.journal = options.journal;
    this.clock = options.clock;
    this.locations = options.locations ?? createHostLocations([]);
    this.#logger = options.logger;
    this.redaction = redaction(options.secrets);
    this.#snapshotFinishedCommands = options.snapshotFinishedCommands ?? 50;
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
          });
    this.checkpoints?.attach(this.publisher);
    this.starters = options.starters ?? null;
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
    this.commands = new CommandService({
      projection: this.projection,
      recorder: this.recorder,
      registry: this.registry,
      locations: this.locations,
      ids: options.ids,
      clock: options.clock,
      scheduler: options.scheduler,
      logger: options.logger,
      commandTimeoutMs: options.commandTimeoutMs,
      redaction: this.redaction,
      checkpoints: this.checkpoints,
      starters: this.starters,
      files: this.files,
      catalogLinks: options.catalogLinks ?? null,
    });
    this.#rebuild();
  }

  #rebuild(): void {
    let events = 0;
    for (const stored of this.journal.readAll()) {
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
      { journal_id: this.journal.info.journal_id, events, position: this.projection.position },
      'state rebuilt from journal',
    );
  }

  /** Records what a restart made unknowable. Fixture journals are historical and left as is. */
  reconcile(): void {
    if (this.journal.info.origin !== 'live') return;
    const result = this.commands.reconcileAfterRestart();
    if (result.executions > 0 || result.commands > 0) {
      this.#logger.warn(result, 'marked in-flight work unknown after restart');
    }
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
    await this.registry.closeAll(this.#logger);
    this.journal.close();
  }
}
