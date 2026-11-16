import { createHash, randomBytes } from 'node:crypto';
import { constants } from 'node:fs';
import { chmod, mkdir, open, readdir, rename, stat, unlink, utimes } from 'node:fs/promises';
import { join } from 'node:path';
import { MEDIA_PHOTO_BYTES, MEDIA_VIDEO_BYTES, type MediaType } from '@halcyonic/contracts';
import { NO_LINK } from '../observer/file-observer.ts';
import { checkMedia, isVideo, mediaKind } from './media-kind.ts';

/**
 * Photos and videos the Mac's page received, kept until `project.media.add` places them (ADR 0066):
 * in `<data dir>/media` (0700), each file 0600 under its SHA-256, at most `KEPT_FILES` (the one being
 * received counted) and for `KEPT_MS`. Received one at a time, streamed to a file with its digest
 * taken as it arrives, never held whole in memory; whatever is refused or cut off is removed. Never
 * journaled and never logged: nothing of a file but its digest, type and size leaves this class.
 */

/** The most files kept at once, the one arriving counted. */
export const KEPT_FILES = 4;
/** How long a file is kept before it is removed unplaced. */
export const KEPT_MS = 2 * 60 * 60 * 1000;

const DIGEST = /^[0-9a-f]{64}$/;
const RECEIVING = '.receiving-';

export type Received =
  | {
      readonly ok: true;
      readonly id: string;
      readonly media_type: MediaType;
      readonly size_bytes: number;
    }
  | {
      readonly ok: false;
      /** busy: another file is arriving; full: `KEPT_FILES` are kept; too_large; not_a_media; failed. */
      readonly reason: 'busy' | 'full' | 'too_large' | 'not_a_media' | 'failed';
    };

export class MediaStore {
  readonly #folder: string;
  readonly #now: () => number;
  #receiving = false;
  /** The file being received now, which a sweep leaves alone. */
  #current: string | null = null;

  constructor(folder: string, now: () => number = Date.now) {
    this.#folder = folder;
    this.#now = now;
  }

  /**
   * Receives one file from `body`, at most `MEDIA_VIDEO_BYTES`, then checks it by its bytes and
   * structure (a photo only up to `MEDIA_PHOTO_BYTES`). `declared` is the request's own length, so
   * one too large is refused before a byte is read.
   */
  async receive(body: AsyncIterable<Uint8Array>, declared: number | null): Promise<Received> {
    if (this.#receiving) return { ok: false, reason: 'busy' };
    if (declared !== null && declared > MEDIA_VIDEO_BYTES)
      return { ok: false, reason: 'too_large' };
    this.#receiving = true;
    let temporary: string | null = null;
    try {
      await this.#ready();
      await this.sweep();
      if ((await this.#kept()).length >= KEPT_FILES) return { ok: false, reason: 'full' };
      temporary = join(this.#folder, `${RECEIVING}${randomBytes(8).toString('hex')}`);
      this.#current = temporary;
      const handle = await open(
        temporary,
        constants.O_WRONLY | constants.O_CREAT | constants.O_EXCL | NO_LINK,
        0o600,
      );
      const digest = createHash('sha256');
      let size = 0;
      let head: Buffer = Buffer.alloc(0);
      try {
        for await (const chunk of body) {
          size += chunk.byteLength;
          if (size > MEDIA_VIDEO_BYTES) return { ok: false, reason: 'too_large' };
          digest.update(chunk);
          if (head.length < 16) head = Buffer.concat([head, Buffer.from(chunk).subarray(0, 16)]);
          let written = 0;
          while (written < chunk.byteLength) {
            const { bytesWritten } = await handle.write(chunk, written, chunk.byteLength - written);
            written += bytesWritten;
          }
        }
        await handle.sync();
      } finally {
        await handle.close();
      }
      if (declared !== null && size !== declared) return { ok: false, reason: 'failed' };
      // A photo past its own bound is too large, not unknown.
      const kind = mediaKind(head);
      if (kind !== null && !isVideo(kind) && size > MEDIA_PHOTO_BYTES)
        return { ok: false, reason: 'too_large' };
      const checked = await checkMedia(temporary);
      if (checked === null) return { ok: false, reason: size > 0 ? 'not_a_media' : 'failed' };
      const id = digest.digest('hex');
      const kept = join(this.#folder, id);
      await rename(temporary, kept);
      temporary = null;
      const now = new Date(this.#now());
      await utimes(kept, now, now);
      return { ok: true, id, media_type: checked.type, size_bytes: checked.size };
    } catch {
      return { ok: false, reason: 'failed' };
    } finally {
      if (temporary !== null) await unlink(temporary).catch(() => undefined);
      this.#current = null;
      this.#receiving = false;
    }
  }

  /** The kept file's path while it is kept, else null. */
  async path(id: string): Promise<string | null> {
    if (!DIGEST.test(id)) return null;
    const path = join(this.#folder, id);
    try {
      const info = await stat(path);
      if (!info.isFile() || this.#now() - info.mtimeMs > KEPT_MS) return null;
      return path;
    } catch {
      return null;
    }
  }

  /** Removes a kept file, once it is placed. */
  async remove(id: string): Promise<void> {
    if (DIGEST.test(id)) await unlink(join(this.#folder, id)).catch(() => undefined);
  }

  /** Removes files kept past `KEPT_MS`, and files left half received by a host that stopped. */
  async sweep(): Promise<void> {
    let names: string[];
    try {
      names = await readdir(this.#folder);
    } catch {
      return;
    }
    for (const name of names) {
      const path = join(this.#folder, name);
      if (name.startsWith(RECEIVING)) {
        if (path !== this.#current) await unlink(path).catch(() => undefined);
        continue;
      }
      if (!DIGEST.test(name)) continue;
      const info = await stat(path).catch(() => null);
      if (info !== null && this.#now() - info.mtimeMs > KEPT_MS)
        await unlink(path).catch(() => undefined);
    }
  }

  async #kept(): Promise<string[]> {
    return (await readdir(this.#folder)).filter((name) => DIGEST.test(name));
  }

  async #ready(): Promise<void> {
    await mkdir(this.#folder, { recursive: true, mode: 0o700 });
    await chmod(this.#folder, 0o700);
  }
}
