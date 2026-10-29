import { createHash } from 'node:crypto';
import { constants, type Dirent } from 'node:fs';
import { lstat, open, readdir, realpath } from 'node:fs/promises';
import { basename, dirname, join, sep } from 'node:path';
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
  // Reused only while all four are unchanged: a modification time can be set back, a change time cannot.
  readonly mtimeMs: number;
  readonly ctimeMs: number;
  readonly ino: number;
  readonly dev: number;
  readonly hash: string;
  readonly lines: number | null;
}

/** A folder as the Mac read it: each file's path relative to the folder, with what it knows of it. */
export type Reading = ReadonlyMap<string, Entry>;

export type ReadResult =
  | {
      readonly ok: true;
      readonly reading: Reading;
      /** Entries found and not read: links, FIFOs, sockets, devices, files swapped for one of those. */
      readonly notRead: number;
    }
  | { readonly ok: false; readonly code: FilesObservedSkip['code']; readonly message: string };

/**
 * Never read, at the folder's top level only: a repository's own records, and installed dependencies
 * (in M1.3 a shared link). Deeper, a folder of either name is the agent's own and is read like any other,
 * so nothing it writes there is left out of the record.
 */
const SKIPPED_NAMES = new Set(['.git', 'node_modules']);

/** How many bytes are read at a time. */
const CHUNK_BYTES = 64 * 1024;

/** How many bytes are looked at to tell a binary file from text. */
const SNIFF_BYTES = 8192;

/**
 * Reads every file of the folder, links never followed. A file whose size and modification time
 * are as in `previous` is taken from it rather than read again. The bytes bound counts what is read
 * or taken from `previous`, and a file past it is refused before it is read.
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
  let root: string;
  try {
    root = await realpath(folder);
  } catch {
    return { ok: false, code: 'location_missing', message: "The task's folder is not there." };
  }
  const reading = new Map<string, Entry>();
  const pending: string[] = [''];
  let bytes = 0;
  let notRead = 0;
  try {
    while (pending.length > 0) {
      const relative = pending.pop() as string;
      const entries = await listFolder(root, relative);
      if (entries === null) {
        // The folder itself changed while it was read: nothing below it can be trusted.
        if (relative === '') throw new Error('the folder changed while it was read');
        notRead += 1;
        continue;
      }
      for (const entry of entries) {
        if (relative === '' && SKIPPED_NAMES.has(entry.name)) continue;
        const path = relative === '' ? entry.name : `${relative}/${entry.name}`;
        if (entry.isDirectory()) pending.push(path);
        else if (entry.isFile()) {
          if (reading.size >= limits.files)
            return {
              ok: false,
              code: 'too_large',
              message: `The folder holds more than the ${limits.files} files the Mac reads for a turn.`,
            };
          const read = await readEntry(
            join(root, path),
            previous?.get(path),
            limits,
            (limits.bytes ?? Number.POSITIVE_INFINITY) - bytes,
          );
          if (read === 'too_large')
            return {
              ok: false,
              code: 'too_large',
              message: 'The folder holds more bytes than the Mac reads for a turn.',
            };
          // Swapped since the listing for a link or something other than a file: not read.
          if (read === null) {
            notRead += 1;
            continue;
          }
          bytes += read.bytes;
          reading.set(path, read);
        } else notRead += 1;
      }
    }
  } catch {
    return { ok: false, code: 'unreadable', message: 'A file or folder in it could not be read.' };
  }
  return { ok: true, reading, notRead };
}

/**
 * A folder's entries, only while it is the folder it was listed as: a real folder, not a link, at its
 * real path below `root` (a real path), checked before and after it is listed; `null` otherwise. This
 * narrows the time a folder swapped for a link could be walked; it does not close it. Exported for its
 * tests.
 */
export async function listFolder(root: string, relative: string): Promise<Dirent[] | null> {
  const path = relative === '' ? root : join(root, relative);
  if (!(await isFolderAt(path))) return null;
  const entries = await readdir(path, { withFileTypes: true });
  return (await isFolderAt(path)) ? entries : null;
}

async function isFolderAt(path: string): Promise<boolean> {
  try {
    return (await lstat(path)).isDirectory() && (await realpath(path)) === path;
  } catch {
    return false;
  }
}

/**
 * One file's entry, from the file as it is when opened: a link is never followed (the open refuses it),
 * a FIFO or device is never waited on, and nothing other than a file is read. `null` when what is at the
 * path is not a file; `'too_large'` when the file holds more than `remaining` bytes, refused before any
 * of it is read. At most the size the opened file had is read. Exported for its tests.
 */
export async function readEntry(
  path: string,
  previous: Entry | undefined,
  limits: ReadingLimits,
  remaining = Number.POSITIVE_INFINITY,
): Promise<Entry | null | 'too_large'> {
  let handle: Awaited<ReturnType<typeof open>>;
  try {
    handle = await open(path, constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
  } catch (error) {
    // A link where the file was listed.
    if ((error as NodeJS.ErrnoException).code === 'ELOOP') return null;
    throw error;
  }
  try {
    const stats = await handle.stat();
    if (!stats.isFile()) return null;
    if (stats.size > remaining) return 'too_large';
    if (
      previous !== undefined &&
      previous.bytes === stats.size &&
      previous.mtimeMs === stats.mtimeMs &&
      previous.ctimeMs === stats.ctimeMs &&
      previous.ino === stats.ino &&
      previous.dev === stats.dev
    )
      return previous;
    const hash = createHash('sha256');
    let lines = 0;
    let last = -1;
    let binary = false;
    let seen = 0;
    const countLines = stats.size <= limits.countLinesUpTo;
    const buffer = Buffer.alloc(Math.min(CHUNK_BYTES, Math.max(stats.size, 1)));
    let position = 0;
    while (position < stats.size) {
      const { bytesRead } = await handle.read(
        buffer,
        0,
        Math.min(buffer.length, stats.size - position),
        position,
      );
      if (bytesRead === 0) break;
      position += bytesRead;
      const bytes = buffer.subarray(0, bytesRead);
      hash.update(bytes);
      if (!countLines || binary) continue;
      if (seen < SNIFF_BYTES && bytes.subarray(0, SNIFF_BYTES - seen).includes(0)) binary = true;
      seen += bytes.length;
      for (const byte of bytes) if (byte === 0x0a) lines += 1;
      last = bytes[bytes.length - 1] as number;
    }
    // A last line without a newline still counts.
    const counted = countLines && !binary ? lines + (last !== -1 && last !== 0x0a ? 1 : 0) : null;
    return {
      bytes: position,
      mtimeMs: stats.mtimeMs,
      ctimeMs: stats.ctimeMs,
      ino: stats.ino,
      dev: stats.dev,
      hash: hash.digest('hex'),
      lines: counted,
    };
  } finally {
    await handle.close();
  }
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
  /** Folders of Halcyonic's own data: a task's folder that holds one is never read (as undo, ADR 0036). */
  readonly #protect: readonly string[];
  readonly #readings = new Map<string, Reading>();
  /**
   * Executions started through this control plane, whose turns it observes. Turn events of any other
   * (a recorded run replayed, a journal imported) are history, not the folder now, and are left alone.
   */
  readonly #watching = new Set<string>();
  /** Executions whose first reading came too late to be trusted: the runtime may already have written. */
  readonly #abandoned = new Set<string>();
  readonly #queues = new Map<string, Promise<unknown>>();

  constructor(
    options: { readonly limits?: ReadingLimits; readonly protect?: readonly string[] } = {},
  ) {
    this.#limits = options.limits ?? DEFAULT_READING_LIMITS;
    this.#protect = options.protect ?? [];
  }

  /** The reading before a task's runtime is asked. A folder that cannot be read leaves none. */
  baseline(executionId: string, directory: string): Promise<void> {
    this.#watching.add(executionId);
    return this.#serially(directory, async () => {
      if (await holdsHostData(directory, this.#protect)) {
        this.#readings.delete(executionId);
        return;
      }
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
      if (await holdsHostData(directory, this.#protect)) {
        this.#readings.delete(executionId);
        return skipped(turnId, {
          code: 'holds_host_data',
          message: "The task's folder holds Halcyonic's own data, so the Mac does not read it.",
        });
      }
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
        not_read: read.notRead,
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
  return {
    turn_id: turnId,
    created: none,
    changed: none,
    deleted: none,
    not_read: 0,
    skipped: skip,
  };
}

/**
 * Whether the folder is, or holds, one of the protected folders, compared by real path; a protected
 * folder not made yet is resolved through its nearest folder that exists (as undo's store does).
 */
async function holdsHostData(folder: string, protect: readonly string[]): Promise<boolean> {
  if (protect.length === 0) return false;
  let real: string;
  try {
    real = await realpath(folder);
  } catch {
    return false;
  }
  for (const path of protect) {
    const protectedPath = await resolvedThroughExisting(path);
    if (within(real, protectedPath)) return true;
  }
  return false;
}

/** Whether `path` is `base` or inside it, `/` included. Exported for its tests. */
export function within(base: string, path: string): boolean {
  return path === base || path.startsWith(base.endsWith(sep) ? base : base + sep);
}

/** The path as the file system resolves it, through its nearest folder that exists. */
async function resolvedThroughExisting(path: string): Promise<string> {
  const rest: string[] = [];
  let existing = path;
  while (true) {
    try {
      return join(await realpath(existing), ...rest.reverse());
    } catch {
      const parent = dirname(existing);
      if (parent === existing) return path;
      rest.push(basename(existing));
      existing = parent;
    }
  }
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
