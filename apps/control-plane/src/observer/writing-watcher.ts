import { type FSWatcher, watch } from 'node:fs';
import { sep } from 'node:path';
import {
  type ExecutionId,
  FILES_WRITING_COUNTED,
  FILES_WRITING_LISTED,
  type FilesWriting,
  type ProjectId,
  type Timestamp,
  type WorkstreamId,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import { safeRelative } from '../core/project-reads.ts';
import type { EventPublisher } from '../core/publisher.ts';
import type { Recorder } from '../core/recorder.ts';
import type { Logger } from '../logger.ts';
import { holdsHostData } from './file-observer.ts';

/** How often at most a turn's changed files are recorded (Watch It Work). */
export const WRITING_EVERY_MS = 2_000;

/** At the folder's top level, never watched: a repository's own records and installed dependencies. */
const SKIPPED_TOP = new Set(['.git', 'node_modules']);

export interface WritingWatcherOptions {
  readonly publisher: EventPublisher;
  readonly recorder: Recorder;
  readonly projection: Projection;
  readonly clock: Clock;
  readonly logger: Logger;
  /** Whether this process started the execution, so its folder is the folder now, not history. */
  readonly observes: (executionId: string) => boolean;
  /** Folders of Halcyonic's own data: a folder that holds one is never watched. */
  readonly protect: readonly string[];
  /** Tests shorten it. */
  readonly everyMs?: number;
  /** Tests stand in for the platform's watch. */
  readonly watchFolder?: (
    folder: string,
    onName: (name: string | null) => void,
  ) => { close(): void };
}

interface Watch {
  readonly scope: {
    readonly project_id: ProjectId;
    readonly workstream_id: WorkstreamId;
    readonly execution_id: ExecutionId;
  };
  readonly folder: string;
  handle: { close(): void } | null;
  timer: NodeJS.Timeout | null;
  /** The newest changed names, newest last, at most `FILES_WRITING_LISTED`. */
  readonly newest: string[];
  /** Every name counted this turn, up to `FILES_WRITING_COUNTED`. */
  readonly counted: Set<string>;
  complete: boolean;
  /** The list last recorded, to record only a change. */
  recorded: string;
}

/**
 * Watches the folder of each running turn and records, at most every two seconds and only when it
 * changed, which files the host saw change (`execution.files.writing`): names only, nothing opened or
 * followed, the newest 20 kept and the rest counted (AUDIT3 H2). The watch ends with the turn, at a
 * stop and when the control plane closes; the turn's end still records `execution.files.observed`.
 */
export class WritingWatcher {
  readonly #options: WritingWatcherOptions;
  readonly #watches = new Map<string, Watch>();
  /** Starts still deciding whether their folder may be watched; a stop meanwhile cancels the start (AUDIT3 WW1). */
  readonly #starting = new Set<string>();
  #detach: (() => void) | null = null;

  constructor(options: WritingWatcherOptions) {
    this.#options = options;
    this.#detach = options.publisher.subscribe(({ event }) => {
      if (event.execution_id === null || event.workstream_id === null) return;
      if (event.event_type === 'runtime.turn.started') {
        if (!options.observes(event.execution_id)) return;
        const folder = options.projection.execution(event.execution_id)?.directory ?? null;
        if (folder === null) return;
        void this.#start(
          {
            project_id: event.project_id,
            workstream_id: event.workstream_id,
            execution_id: event.execution_id,
          },
          folder,
        );
      } else if (
        event.event_type === 'runtime.turn.completed' ||
        event.event_type === 'runtime.turn.failed' ||
        event.event_type === 'runtime.turn.interrupted' ||
        event.event_type === 'execution.state_unknown'
      ) {
        this.#stop(event.execution_id);
      }
    });
  }

  /** How many folders are watched now; none outlives its turn. */
  get watching(): number {
    return this.#watches.size;
  }

  async #start(scope: Watch['scope'], folder: string): Promise<void> {
    this.#stop(scope.execution_id);
    this.#starting.add(scope.execution_id);
    const holds = await holdsHostData(folder, this.#options.protect);
    // A turn that ended while this was decided was stopped meanwhile: no watch outlives it.
    if (!this.#starting.delete(scope.execution_id) || holds) return;
    const watch: Watch = {
      scope,
      folder,
      handle: null,
      timer: null,
      newest: [],
      counted: new Set(),
      complete: true,
      recorded: '',
    };
    this.#watches.set(scope.execution_id, watch);
    try {
      watch.handle = (this.#options.watchFolder ?? watchRecursively)(folder, (name) =>
        this.#saw(watch, name),
      );
    } catch {
      // A folder that cannot be watched shows no files while the turn runs; its end still reads it.
      this.#watches.delete(scope.execution_id);
      return;
    }
    watch.timer = setInterval(() => this.#record(watch), this.#options.everyMs ?? WRITING_EVERY_MS);
    watch.timer.unref();
  }

  #saw(watch: Watch, raw: string | null): void {
    if (raw === null) return;
    const name = raw.split(sep).join('/').normalize('NFC');
    if (!safeRelative(name) || name.length > 1024) return;
    if (SKIPPED_TOP.has(name.split('/')[0] as string)) return;
    const at = watch.newest.indexOf(name);
    if (at !== -1) watch.newest.splice(at, 1);
    watch.newest.push(name);
    if (watch.newest.length > FILES_WRITING_LISTED) watch.newest.shift();
    if (watch.counted.size < FILES_WRITING_COUNTED) watch.counted.add(name);
    else if (!watch.counted.has(name)) watch.complete = false;
  }

  #record(watch: Watch): void {
    const paths = [...watch.newest].reverse();
    const key = JSON.stringify([paths, watch.counted.size, watch.complete]);
    if (paths.length === 0 || key === watch.recorded) return;
    watch.recorded = key;
    const payload: FilesWriting = {
      paths,
      paths_total: watch.counted.size,
      total_complete: watch.complete,
    };
    try {
      this.#options.recorder.record(
        controlPlaneDraft(
          'execution.files.writing',
          watch.scope,
          payload,
          this.#options.clock.now().toISOString() as Timestamp,
          NO_CAUSE,
        ),
      );
    } catch (error: unknown) {
      this.#options.logger.error(
        { err: error, execution_id: watch.scope.execution_id },
        'files being written not recorded',
      );
    }
  }

  #stop(executionId: string, flush = true): void {
    this.#starting.delete(executionId);
    const watch = this.#watches.get(executionId);
    if (watch === undefined) return;
    this.#watches.delete(executionId);
    if (watch.timer !== null) clearInterval(watch.timer);
    watch.handle?.close();
    // What changed after the last record, so the turn's end has it; not while the plane closes.
    if (flush) this.#record(watch);
  }

  /** Ends every watch, as the control plane closes. */
  close(): void {
    this.#detach?.();
    this.#detach = null;
    this.#starting.clear();
    for (const executionId of [...this.#watches.keys()]) this.#stop(executionId, false);
  }
}

/** The platform's recursive watch of a folder, names only, never following a link. */
function watchRecursively(folder: string, onName: (name: string | null) => void): FSWatcher {
  const watcher = watch(folder, { recursive: true, persistent: false }, (_type, name) =>
    onName(name),
  );
  watcher.on('error', () => watcher.close());
  return watcher;
}
