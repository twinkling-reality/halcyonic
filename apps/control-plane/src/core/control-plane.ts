import type { Snapshot } from '@halcyonic/contracts';
import { Projection } from '@halcyonic/domain';
import type { Clock, RuntimeAdapter, Scheduler } from '@halcyonic/runtime-core';
import type { IdGenerator } from '../ids.ts';
import type { EventJournal } from '../journal/journal.ts';
import { createHostLocations, type HostLocations } from '../locations.ts';
import type { Logger } from '../logger.ts';
import { CommandService } from './command-service.ts';
import { EventPublisher } from './publisher.ts';
import { Recorder } from './recorder.ts';
import { redactSecrets } from './redaction.ts';
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
   * Every secret Halcyonic holds or passes to a runtime, asked for each time a runtime's error text
   * is journaled, since some change (redaction.ts). None by default; credential shapes still go.
   */
  readonly secrets?: () => Iterable<string>;
}

/**
 * Wires the journal, projection, runtimes and command handling together. It holds no logic of
 * its own beyond startup: rebuilding state from the journal and reconciling after a restart.
 */
export class ControlPlane {
  readonly journal: EventJournal;
  readonly projection = new Projection();
  readonly publisher: EventPublisher;
  readonly registry = new RuntimeRegistry();
  readonly recorder: Recorder;
  readonly commands: CommandService;
  readonly locations: HostLocations;
  readonly clock: Clock;
  readonly #logger: Logger;
  readonly #snapshotFinishedCommands: number;

  constructor(options: ControlPlaneOptions) {
    this.journal = options.journal;
    this.clock = options.clock;
    this.locations = options.locations ?? createHostLocations([]);
    this.#logger = options.logger;
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
      redact: (text) => redactSecrets(text, options.secrets?.() ?? []),
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
