import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { lstat, readdir } from 'node:fs/promises';
import { join } from 'node:path';
import {
  type FileChanged,
  type FileCreated,
  type FileDeleted,
  type FilesObserved,
  type FilesObservedSkip,
  OBSERVED_FILES_LISTED,
  type Timestamp,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import type { Clock } from '@halcyonic/runtime-core';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import type { EventPublisher } from '../core/publisher.ts';
import type { Recorder } from '../core/recorder.ts';
import type { Logger } from '../logger.ts';

/**
 * The Mac's own reading of what changed in a task's folder, turn by turn (ADR 0038). It keeps, for
 * each file, its size, modification time, SHA-256 and line count; never its contents. A turn's
 * changes are the difference between the reading taken before (when the start was admitted, or the
 * end of the turn before) and the reading at the turn's end. It cannot tell who changed a file.
 */

export interface ReadingLimits {
  /** The most files it reads in one folder; a larger folder is refused as too large. */
  readonly files: number;
  /** The most bytes of file content it reads in one folder; past them the folder is refused as too large. */
  readonly bytes?: number;
  /** Files larger than this many bytes are hashed, but their lines are not counted. */
  readonly countLinesUpTo: number;
}

export const DEFAULT_READING_LIMITS: ReadingLimits = {
  files: 20_000,
  bytes: 256 * 1024 * 1024,
  countLinesUpTo: 2 * 1024 * 1024,
};

interface Entry {
  readonly bytes: number;
  readonly mtimeMs: number;
  readonly hash: string;
  readonly lines: number | null;
}

/** A folder as the Mac read it: each file's path relative to the folder, with what it knows of it. */
export type Reading = ReadonlyMap<string, Entry>;

export type ReadResult =
  | { readonly ok: true; readonly reading: Reading }
  | { readonly ok: false; readonly code: FilesObservedSkip['code']; readonly message: string };

/** Never read: a repository's own records, and installed dependencies (in M1.3 a shared link). */
const SKIPPED_NAMES = new Set(['.git', 'node_modules']);

/** How many bytes are looked at to tell a binary file from text. */
const SNIFF_BYTES = 8192;

/**
 * Reads every file of the folder, links never followed. A file whose size and modification time
 * are as in `previous` is taken from it rather than read again.
 */
export async function readFolder(
  folder: string,
  previous?: Reading,
  limits: ReadingLimits = DEFAULT_READING_LIMITS,
): Promise<ReadResult> {
  try {
    if (!(await lstat(folder)).isDirectory())
      return { ok: false, code: 'location_missing', message: "The task's folder is not a folder." };
  } catch {
    return { ok: false, code: 'location_missing', message: "The task's folder is not there." };
  }
  const reading = new Map<string, Entry>();
  const pending: string[] = [''];
  let bytes = 0;
  try {
    while (pending.length > 0) {
      const relative = pending.pop() as string;
      for (const entry of await readdir(join(folder, relative), { withFileTypes: true })) {
        if (SKIPPED_NAMES.has(entry.name)) continue;
        const path = relative === '' ? entry.name : `${relative}/${entry.name}`;
        if (entry.isDirectory()) pending.push(path);
        else if (entry.isFile()) {
          if (reading.size >= limits.files)
            return {
              ok: false,
              code: 'too_large',
              message: `The folder holds more than the ${limits.files} files the Mac reads for a turn.`,
            };
          const read = await readEntry(join(folder, path), previous?.get(path), limits);
          bytes += read.bytes;
          if (bytes > (limits.bytes ?? Number.POSITIVE_INFINITY))
            return {
              ok: false,
              code: 'too_large',
              message: 'The folder holds more bytes than the Mac reads for a turn.',
            };
          reading.set(path, read);
        }
      }
    }
  } catch {
    return { ok: false, code: 'unreadable', message: 'A file or folder in it could not be read.' };
  }
  return { ok: true, reading };
}

async function readEntry(
  path: string,
  previous: Entry | undefined,
  limits: ReadingLimits,
): Promise<Entry> {
  const stats = await lstat(path);
  if (previous !== undefined && previous.bytes === stats.size && previous.mtimeMs === stats.mtimeMs)
    return previous;
  const hash = createHash('sha256');
  let lines = 0;
  let last = -1;
  let binary = false;
  let seen = 0;
  const countLines = stats.size <= limits.countLinesUpTo;
  await new Promise<void>((resolveRead, reject) => {
    createReadStream(path)
      .on('data', (chunk: Buffer | string) => {
        const bytes = typeof chunk === 'string' ? Buffer.from(chunk) : chunk;
        hash.update(bytes);
        if (!countLines || binary) return;
        if (seen < SNIFF_BYTES && bytes.subarray(0, SNIFF_BYTES - seen).includes(0)) binary = true;
        seen += bytes.length;
        for (const byte of bytes) if (byte === 0x0a) lines += 1;
        if (bytes.length > 0) last = bytes[bytes.length - 1] as number;
      })
      .on('error', reject)
      .on('end', () => resolveRead());
  });
  // A last line without a newline still counts.
  const counted = countLines && !binary ? lines + (last !== -1 && last !== 0x0a ? 1 : 0) : null;
  return { bytes: stats.size, mtimeMs: stats.mtimeMs, hash: hash.digest('hex'), lines: counted };
}

/** What differs between two readings, each list in path order. */
export function compareReadings(
  before: Reading,
  after: Reading,
): { created: FileCreated[]; changed: FileChanged[]; deleted: FileDeleted[] } {
  const created: FileCreated[] = [];
  const changed: FileChanged[] = [];
  const deleted: FileDeleted[] = [];
  for (const [path, entry] of after) {
    const was = before.get(path);
    if (was === undefined) created.push({ path, lines: entry.lines, bytes: entry.bytes });
    else if (was.hash !== entry.hash)
      changed.push({ path, lines_before: was.lines, lines_after: entry.lines, bytes: entry.bytes });
  }
  for (const [path, entry] of before)
    if (!after.has(path)) deleted.push({ path, lines_before: entry.lines });
  const byPath = (a: { path: string }, b: { path: string }) =>
    a.path < b.path ? -1 : a.path > b.path ? 1 : 0;
  return {
    created: created.sort(byPath),
    changed: changed.sort(byPath),
    deleted: deleted.sort(byPath),
  };
}

export interface FileObserverAttachment {
  readonly publisher: EventPublisher;
  readonly recorder: Recorder;
  readonly projection: Projection;
  readonly clock: Clock;
  readonly logger: Logger;
}

/** Keeps the latest reading of each execution's folder and journals what each turn changed. */
export class FileObserver {
  readonly #limits: ReadingLimits;
  readonly #readings = new Map<string, Reading>();
  /**
   * Executions started through this control plane, whose turns it observes. Turn events of any other
   * (a recorded run replayed, a journal imported) are history, not the folder now, and are left alone.
   */
  readonly #watching = new Set<string>();
  /** Executions whose first reading came too late to be trusted: the runtime may already have written. */
  readonly #abandoned = new Set<string>();
  readonly #queues = new Map<string, Promise<unknown>>();

  constructor(options: { readonly limits?: ReadingLimits } = {}) {
    this.#limits = options.limits ?? DEFAULT_READING_LIMITS;
  }

  /** The reading before a task's runtime is asked. A folder that cannot be read leaves none. */
  baseline(executionId: string, directory: string): Promise<void> {
    this.#watching.add(executionId);
    return this.#serially(directory, async () => {
      const read = await readFolder(directory, undefined, this.#limits);
      if (read.ok && !this.#abandoned.has(executionId))
        this.#readings.set(executionId, read.reading);
      else this.#readings.delete(executionId);
    });
  }

  /**
   * Drops the first reading of an execution whose start could not wait for it: taken late, it may hold
   * what the runtime already wrote, so the first turn says it has no earlier reading instead.
   */
  abandon(executionId: string): void {
    this.#abandoned.add(executionId);
    this.#readings.delete(executionId);
  }

  /** What changed since the last reading, which this reading then replaces. */
  turnEnded(
    executionId: string,
    directory: string,
    turnId: string | null = null,
  ): Promise<FilesObserved> {
    return this.#serially(directory, async () => {
      const before = this.#readings.get(executionId);
      const read = await readFolder(directory, before, this.#limits);
      if (read.ok) this.#readings.set(executionId, read.reading);
      else this.#readings.delete(executionId);
      if (!read.ok) return skipped(turnId, { code: read.code, message: read.message });
      if (before === undefined)
        return skipped(turnId, {
          code: 'no_baseline',
          message:
            'The Mac had no earlier reading of the folder for this task, so it cannot say what this turn changed.',
        });
      const { created, changed, deleted } = compareReadings(before, read.reading);
      return {
        turn_id: turnId,
        created: listed(created),
        changed: listed(changed),
        deleted: listed(deleted),
        skipped: null,
      };
    });
  }

  /** Journals each turn's changes as the runtime reports the turn ended in a folder it works in. */
  attach(attachment: FileObserverAttachment): () => void {
    return attachment.publisher.subscribe(({ event }) => {
      if (
        event.event_type !== 'runtime.turn.completed' &&
        event.event_type !== 'runtime.turn.failed' &&
        event.event_type !== 'runtime.turn.interrupted'
      )
        return;
      if (!this.#watching.has(event.execution_id)) return;
      const directory = attachment.projection.execution(event.execution_id)?.directory ?? null;
      if (directory === null) return;
      const scope = {
        project_id: event.project_id,
        workstream_id: event.workstream_id,
        execution_id: event.execution_id,
      };
      void this.turnEnded(event.execution_id, directory, event.payload.turn_id ?? null)
        .then((observed) =>
          attachment.recorder.record(
            controlPlaneDraft(
              'execution.files.observed',
              scope,
              observed,
              attachment.clock.now().toISOString() as Timestamp,
              NO_CAUSE,
            ),
          ),
        )
        .catch((error: unknown) =>
          attachment.logger.error(
            { err: error, execution_id: event.execution_id },
            'turn files not recorded',
          ),
        );
    });
  }

  #serially<T>(key: string, step: () => Promise<T>): Promise<T> {
    const previous = this.#queues.get(key) ?? Promise.resolve();
    const next = previous.then(step, step);
    const settled = next.catch(() => undefined);
    this.#queues.set(key, settled);
    void settled.then(() => {
      if (this.#queues.get(key) === settled) this.#queues.delete(key);
    });
    return next;
  }
}

function skipped(turnId: string | null, skip: FilesObservedSkip): FilesObserved {
  const none = { files: [], total: 0 };
  return { turn_id: turnId, created: none, changed: none, deleted: none, skipped: skip };
}

/** The first files of a list that the contract can carry, and how many the list holds. */
function listed<T extends { readonly path: string }>(
  files: readonly T[],
): { files: T[]; total: number } {
  const listable = files.filter(
    (file) =>
      file.path.length <= 1024 &&
      [...file.path].every((character) => {
        const code = character.codePointAt(0) ?? 0;
        return code > 0x1f && code !== 0x7f;
      }),
  );
  return { files: listable.slice(0, OBSERVED_FILES_LISTED), total: files.length };
}
