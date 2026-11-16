import { createHash } from 'node:crypto';
import { constants, createReadStream, realpathSync } from 'node:fs';
import {
  type FileHandle,
  lstat,
  mkdir,
  open,
  readdir,
  readFile,
  rm,
  unlink,
  writeFile,
} from 'node:fs/promises';
import { join, sep } from 'node:path';
import {
  MEDIA_FOLDER_BYTES,
  MEDIA_VIDEO_BYTES,
  MEDIA_VIDEO_MS,
  type MediaFile,
  type MediaNotMade,
  type MediaStill,
  type MediaType,
  PICTURE_BYTES,
} from '@halcyonic/contracts';
import { secretShapedPath } from '@halcyonic/runtime-core';
import { NO_LINK } from '../observer/file-observer.ts';
import { checkMedia, isVideo } from './media-kind.ts';
import type { MediaStore } from './media-store.ts';
import { type MediaFacts, type MediaTool, STILL_AT } from './media-tool.ts';
import { holdsHostFolder, wholePicture } from './pictures.ts';

/**
 * Placing a person's photo or video in a project's folder (ADR 0066). The kept upload is checked
 * again; the tool makes a stripped copy in its own folder; the host checks it and writes it into
 * `media/` under a name it chooses from the copy's digest, created at once with `O_EXCL` and no link
 * anywhere in its path, never renamed, never over another file. Then the stills and a video's play
 * copy, each checked and placed the same way while the folder's total stays within
 * `MEDIA_FOLDER_BYTES`. Before each write the host notes the name in its own data folder, so a write
 * cut off by a stop is removed at the next start, and only that.
 */

/** Why a medium was not added: the command fails with effect none, nothing left in the folder. */
export type AddRefusal =
  | 'media_not_kept'
  | 'not_a_media'
  | 'tool_missing'
  | 'tool_changed'
  | 'unreadable'
  | 'media_full'
  | 'media_present'
  | 'folder_refused';

export interface Prepared {
  readonly path: string;
  readonly stills: readonly MediaStill[];
  readonly stills_not_made: MediaNotMade | null;
  readonly play: MediaFile | null;
  readonly play_not_made: MediaNotMade | null;
  readonly width: number | null;
  readonly height: number | null;
  readonly duration_ms: number | null;
}

export type AddOutcome =
  | { readonly ok: true; readonly prepared: Prepared }
  | { readonly ok: false; readonly refusal: AddRefusal };

export interface AddedFile {
  readonly path: string;
  readonly media_type: MediaType;
  readonly size_bytes: number;
  readonly sha256: string;
}

/** The host's own names under `media/`: nothing else is ever written or removed there by it. */
export const MEDIA_NAME =
  /^media\/(?:photo|video)-[0-9a-f]{12}(?:\.(?:jpg|mov|mp4)|-play\.mp4|-stills\/[1-4]\.jpg)$/;

const EXTENSION: Readonly<Record<string, string>> = {
  'image/jpeg': 'jpg',
  'video/quicktime': 'mov',
  'video/mp4': 'mp4',
};

export interface MediaPlacerDeps {
  readonly store: MediaStore;
  readonly tool: MediaTool;
  /** A 0700 folder of the data folder for the notes of names being written. */
  readonly notes: string;
  readonly protectedFolders: readonly string[];
}

export class MediaPlacer {
  readonly #deps: MediaPlacerDeps;
  /** The start's sweep of a stopped host's writes; nothing is placed before it settles. */
  #swept: Promise<unknown> = Promise.resolve();

  constructor(deps: MediaPlacerDeps) {
    this.#deps = deps;
  }

  /** Starts the sweep of what a stopped host left (`sweep`); every later add waits for it. */
  reconcile(
    added: (projectId: string, path: string) => boolean,
  ): Promise<{ removed: number; left: number }> {
    const swept = this.#deps.store
      .sweep()
      .then(() => this.#deps.tool.clear())
      .then(() => this.sweep(added));
    this.#swept = swept.catch(() => undefined);
    return swept;
  }

  /**
   * Adds the kept medium `id` to the project's `folder`, whose media already take `bytes`. `added` is
   * called once the copy is in the folder, before anything is made from it.
   */
  async add(request: {
    readonly projectId: string;
    readonly commandId: string;
    readonly folder: string;
    readonly id: string;
    readonly bytes: number;
    readonly added: (file: AddedFile) => void;
  }): Promise<AddOutcome> {
    const { store, tool } = this.#deps;
    await this.#swept;
    const kept = await store.path(request.id);
    if (kept === null) return { ok: false, refusal: 'media_not_kept' };
    const upload = await checkMedia(kept);
    if (upload === null) return { ok: false, refusal: 'not_a_media' };
    const ready = await tool.ready();
    if (!ready.ok)
      return {
        ok: false,
        refusal: ready.reason === 'tool_changed' ? 'tool_changed' : 'tool_missing',
      };
    const folder = this.#folder(request.folder);
    if (folder === null) return { ok: false, refusal: 'folder_refused' };
    const run = await tool.folder();
    const note = new PlacingNote(this.#deps.notes, request.commandId, request.projectId, folder);
    try {
      const stripped = await tool.strip(kept, upload.type, run.path);
      if (!stripped.ok) return { ok: false, refusal: 'unreadable' };
      const copy = await checkMedia(stripped.value.path);
      const video = isVideo(upload.type);
      if (copy === null || isVideo(copy.type) !== video || (!video && copy.type !== 'image/jpeg'))
        return { ok: false, refusal: 'unreadable' };
      const digest = await sha256Of(stripped.value.path);
      let total = request.bytes + copy.size;
      if (total > MEDIA_FOLDER_BYTES) return { ok: false, refusal: 'media_full' };
      const stem = `media/${video ? 'video' : 'photo'}-${digest.slice(0, 12)}`;
      const path = `${stem}.${EXTENSION[copy.type]}`;
      await note.add({ path, size: copy.size, sha256: digest, medium: true });
      const placed = await placeFile(
        folder,
        path,
        stripped.value.path,
        this.#deps.protectedFolders,
      );
      if (placed !== 'placed') {
        await note.done();
        return { ok: false, refusal: placed };
      }
      request.added({ path, media_type: copy.type, size_bytes: copy.size, sha256: digest });

      const facts: MediaFacts = stripped.value.facts;
      const place = async (
        made: string,
        at: string,
        bound: number,
      ): Promise<{ ok: true; file: MediaFile } | { ok: false; reason: MediaNotMade }> => {
        const size = (await lstat(made).catch(() => null))?.size ?? null;
        if (size === null) return { ok: false, reason: 'unreadable' };
        if (size > bound) return { ok: false, reason: 'too_large' };
        if (total + size > MEDIA_FOLDER_BYTES) return { ok: false, reason: 'no_room' };
        const sha256 = await sha256Of(made);
        await note.add({ path: at, size, sha256, medium: false });
        const result = await placeFile(folder, at, made, this.#deps.protectedFolders);
        if (result !== 'placed') return { ok: false, reason: 'unreadable' };
        total += size;
        return { ok: true, file: { path: at, size_bytes: size, sha256 } };
      };

      const stills: MediaStill[] = [];
      let stillsNotMade: MediaNotMade | null = null;
      let play: MediaFile | null = null;
      let playNotMade: MediaNotMade | null = null;
      if (!video) {
        // One still made for every photo, never the photo itself (ADR 0066): every picture a runtime is
        // handed is then a stripped JPEG the host made, at most STILL_SIDE on its long side.
        const still = await this.#still(
          stripped.value.path,
          0,
          run.path,
          1,
          `${stem}-stills/1.jpg`,
          place,
        );
        if (still.ok) stills.push(still.still);
        else stillsNotMade = still.reason;
      } else if (facts.durationMs === null) {
        stillsNotMade = 'unreadable';
        playNotMade = 'unreadable';
      } else if (facts.durationMs > MEDIA_VIDEO_MS) {
        stillsNotMade = 'too_long';
        playNotMade = 'too_long';
      } else {
        for (const [index, part] of STILL_AT.entries()) {
          const atMs = Math.floor(facts.durationMs * part);
          const still = await this.#still(
            stripped.value.path,
            atMs,
            run.path,
            index + 1,
            `${stem}-stills/${index + 1}.jpg`,
            place,
          );
          if (!still.ok) {
            stillsNotMade = still.reason;
            break;
          }
          stills.push(still.still);
        }
        const made = await tool.play(stripped.value.path, run.path);
        if (!made.ok) playNotMade = made.reason;
        else {
          const checked = await checkMedia(made.value);
          if (checked === null || checked.type !== 'video/mp4') playNotMade = 'unreadable';
          else {
            const placedPlay = await place(made.value, `${stem}-play.mp4`, MEDIA_VIDEO_BYTES);
            if (placedPlay.ok) play = placedPlay.file;
            else playNotMade = placedPlay.reason;
          }
        }
      }
      await note.done();
      return {
        ok: true,
        prepared: {
          path,
          stills,
          stills_not_made: stills.length === 0 ? (stillsNotMade ?? 'unreadable') : null,
          play,
          play_not_made: video && play === null ? (playNotMade ?? 'unreadable') : null,
          width: facts.width,
          height: facts.height,
          duration_ms: video ? facts.durationMs : null,
        },
      };
    } catch (error) {
      // Stopped after the medium is in: what was made from it so far goes now, not at the next start.
      await note.abandon(this.#deps.protectedFolders);
      throw error;
    } finally {
      await run.done();
      await store.remove(request.id);
    }
  }

  async #still(
    from: string,
    atMs: number,
    out: string,
    index: number,
    at: string,
    place: (
      made: string,
      at: string,
      bound: number,
    ) => Promise<{ ok: true; file: MediaFile } | { ok: false; reason: MediaNotMade }>,
  ): Promise<{ ok: true; still: MediaStill } | { ok: false; reason: MediaNotMade }> {
    const made = await this.#deps.tool.still(from, atMs, out, index);
    if (!made.ok) return made;
    const bytes = await readFile(made.value).catch(() => null);
    if (bytes === null || !wholePicture(bytes)) return { ok: false, reason: 'unreadable' };
    const placed = await place(made.value, at, PICTURE_BYTES);
    if (!placed.ok) return placed;
    return { ok: true, still: { ...placed.file, at_ms: atMs } };
  }

  /** The project's folder by its real path, when it is a folder and none of the host's own. */
  #folder(folder: string): string | null {
    try {
      const real = realpathSync(folder);
      return holdsHostFolder(real, this.#deps.protectedFolders) ? null : real;
    } catch {
      return null;
    }
  }

  /**
   * Removes what a stopped host was writing (ADR 0066), before any command runs: each noted file only
   * if its path is one of the host's own names, `media/` is still the folder's own, it opens without a
   * link as a regular file, and it is no larger than the file being written. A medium whose
   * `project.media.added` is recorded is never removed. Anything else is left; nothing is logged but
   * counts.
   */
  async sweep(
    added: (projectId: string, path: string) => boolean,
  ): Promise<{ removed: number; left: number }> {
    let removed = 0;
    let left = 0;
    let names: string[];
    try {
      names = await readdir(this.#deps.notes);
    } catch {
      return { removed, left };
    }
    for (const name of names) {
      const file = join(this.#deps.notes, name);
      const note = await readNote(file);
      for (const entry of note?.entries ?? []) {
        if (note === null || (entry.medium && added(note.projectId, entry.path))) continue;
        if (await removeIfOurs(note.folder, entry, this.#deps.protectedFolders)) removed += 1;
        else left += 1;
      }
      await unlink(file).catch(() => undefined);
    }
    return { removed, left };
  }
}

interface NoteEntry {
  readonly path: string;
  readonly size: number;
  readonly sha256: string;
  readonly medium: boolean;
}

/** The names one add is writing, kept 0600 in the host's own folder, written before each write. */
class PlacingNote {
  readonly #file: string;
  readonly #body: { projectId: string; folder: string; entries: NoteEntry[] };
  readonly #dir: string;

  constructor(dir: string, commandId: string, projectId: string, folder: string) {
    this.#dir = dir;
    this.#file = join(
      dir,
      `${createHash('sha256').update(commandId).digest('hex').slice(0, 32)}.json`,
    );
    this.#body = { projectId, folder, entries: [] };
  }

  async add(entry: NoteEntry): Promise<void> {
    this.#body.entries.push(entry);
    await mkdir(this.#dir, { recursive: true, mode: 0o700 });
    await writeFile(this.#file, JSON.stringify(this.#body), { mode: 0o600 });
  }

  async done(): Promise<void> {
    await rm(this.#file, { force: true });
  }

  /**
   * Removes the files made from the medium that this add had written, as the start's sweep would,
   * and only those: the medium itself, once recorded added, stays (ADR 0066).
   */
  async abandon(protectedFolders: readonly string[]): Promise<void> {
    for (const entry of this.#body.entries)
      if (!entry.medium)
        await removeIfOurs(this.#body.folder, entry, protectedFolders).catch(() => false);
    await this.done();
  }
}

async function readNote(
  file: string,
): Promise<{ projectId: string; folder: string; entries: NoteEntry[] } | null> {
  try {
    const parsed = JSON.parse(await readFile(file, 'utf8')) as {
      projectId?: unknown;
      folder?: unknown;
      entries?: unknown;
    };
    if (typeof parsed.projectId !== 'string' || typeof parsed.folder !== 'string') return null;
    if (!Array.isArray(parsed.entries)) return null;
    const entries = parsed.entries.filter(
      (entry): entry is NoteEntry =>
        typeof entry === 'object' &&
        entry !== null &&
        typeof (entry as NoteEntry).path === 'string' &&
        typeof (entry as NoteEntry).size === 'number' &&
        typeof (entry as NoteEntry).medium === 'boolean',
    );
    return { projectId: parsed.projectId, folder: parsed.folder, entries };
  } catch {
    return null;
  }
}

async function removeIfOurs(
  folder: string,
  entry: NoteEntry,
  protectedFolders: readonly string[],
): Promise<boolean> {
  if (!MEDIA_NAME.test(entry.path)) return false;
  const real = realOr(folder);
  if (real === null || holdsHostFolder(real, protectedFolders)) return false;
  const parent = entry.path.slice(0, entry.path.lastIndexOf('/'));
  if (!(await ownFolder(real, parent))) return false;
  const path = join(real, entry.path);
  let handle: FileHandle;
  try {
    handle = await open(path, constants.O_RDONLY | NO_LINK);
  } catch {
    return false;
  }
  try {
    const info = await handle.stat();
    if (!info.isFile() || info.size > entry.size) return false;
    // The name still the file the handle holds, never one swapped in after the check.
    const named = await lstat(path);
    if (named.dev !== info.dev || named.ino !== info.ino) return false;
    await unlink(path);
    return true;
  } catch {
    return false;
  } finally {
    await handle.close();
  }
}

/**
 * Writes `source` into `folder` at the host's own name `at`: each folder of `media/` made without
 * recursion, or found a real folder whose real path is where it should be; the file created with
 * `O_EXCL` and no link anywhere in its path, written, synced and checked a regular file inside
 * `media/`, removed on any failure.
 */
export async function placeFile(
  folder: string,
  at: string,
  source: string,
  protectedFolders: readonly string[],
): Promise<'placed' | 'media_present' | 'folder_refused'> {
  if (!MEDIA_NAME.test(at) || secretShapedPath(at)) return 'folder_refused';
  const full = join(folder, at);
  if (holdsHostFolder(full, protectedFolders)) return 'folder_refused';
  const parents = at.split('/').slice(0, -1);
  for (let depth = 1; depth <= parents.length; depth += 1) {
    const relative = parents.slice(0, depth).join('/');
    try {
      await mkdir(join(folder, relative), { mode: 0o755 });
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'EEXIST') return 'folder_refused';
    }
    if (!(await ownFolder(folder, relative))) return 'folder_refused';
  }
  let handle: FileHandle;
  try {
    handle = await open(
      full,
      constants.O_WRONLY | constants.O_CREAT | constants.O_EXCL | NO_LINK,
      0o644,
    );
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === 'EEXIST' ? 'media_present' : 'folder_refused';
  }
  let ok = false;
  try {
    for await (const chunk of createReadStream(source)) {
      const bytes = chunk as Buffer;
      let written = 0;
      while (written < bytes.length) {
        const { bytesWritten } = await handle.write(bytes, written, bytes.length - written);
        written += bytesWritten;
      }
    }
    await handle.sync();
    const info = await handle.stat();
    const real = realOr(full);
    ok = info.isFile() && real?.startsWith(`${folder}${sep}media${sep}`) === true;
  } catch {
    ok = false;
  } finally {
    await handle.close();
  }
  if (!ok) {
    await unlink(full).catch(() => undefined);
    return 'folder_refused';
  }
  return 'placed';
}

/** Whether `relative` in `folder` is a real folder, no link, whose real path is exactly there. */
async function ownFolder(folder: string, relative: string): Promise<boolean> {
  const path = join(folder, relative);
  try {
    const info = await lstat(path);
    return info.isDirectory() && !info.isSymbolicLink() && realOr(path) === path;
  } catch {
    return false;
  }
}

function realOr(path: string): string | null {
  try {
    return realpathSync(path);
  } catch {
    return null;
  }
}

async function sha256Of(path: string): Promise<string> {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(path)) hash.update(chunk as Buffer);
  return hash.digest('hex');
}
