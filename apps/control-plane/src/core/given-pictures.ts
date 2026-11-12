import { createHash } from 'node:crypto';
import { constants } from 'node:fs';
import { chmod, mkdir, open, readdir, rm, stat, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { type GivenPicture, PICTURE_BYTES } from '@halcyonic/contracts';
import type { InstructionPicture } from '@halcyonic/runtime-core';
import { pictureKind, wholePicture } from './pictures.ts';

/** How many given pictures the host keeps, and for how long, before the oldest go. */
export const GIVEN_PICTURES_KEPT = 64;
export const GIVEN_PICTURE_HOURS = 24;

const ID = /^[0-9a-f]{64}$/;
const NO_LINK = constants.O_NOFOLLOW ?? 0;

export type GivenRefusal = 'picture_too_large' | 'picture_not_a_picture';

/**
 * Pictures the person gave the host to go with an instruction (ADR 0058, amended): what they see, a
 * frame of the running game. Each is kept once by its SHA-256 in a folder of the data folder (mode
 * 0700, each file 0600), at most `GIVEN_PICTURES_KEPT` of them for `GIVEN_PICTURE_HOURS`; never
 * journaled, never logged. What it is comes from its first bytes, and only a whole PNG, JPEG or WebP
 * of at most `PICTURE_BYTES` is kept. An instruction names one by its id, and the host reads it again,
 * checks its digest and hands the runtime those bytes.
 */
export class GivenPictures {
  readonly #folder: string;
  readonly #now: () => number;

  constructor(folder: string, now: () => number = Date.now) {
    this.#folder = folder;
    this.#now = now;
  }

  /** Keeps a picture the person gave, or says why not; the same picture given twice is kept once. */
  async give(
    bytes: Uint8Array,
  ): Promise<
    | { readonly ok: true; readonly picture: GivenPicture }
    | { readonly ok: false; readonly code: GivenRefusal; readonly message: string }
  > {
    if (bytes.length > PICTURE_BYTES)
      return {
        ok: false,
        code: 'picture_too_large',
        message: `A picture is at most ${PICTURE_BYTES / 1_048_576} MB.`,
      };
    const kind = pictureKind(bytes);
    if (kind === null || !wholePicture(bytes))
      return {
        ok: false,
        code: 'picture_not_a_picture',
        message: 'That is not a whole PNG, JPEG or WebP picture.',
      };
    const id = createHash('sha256').update(bytes).digest('hex');
    await mkdir(this.#folder, { recursive: true, mode: 0o700 });
    await chmod(this.#folder, 0o700);
    await writeFile(join(this.#folder, id), bytes, { mode: 0o600 });
    await this.#prune(id);
    return { ok: true, picture: { id, media_type: kind, size_bytes: bytes.length } };
  }

  /** The picture `id` as the host keeps it, read once and checked against its digest, or null. */
  async read(id: string): Promise<InstructionPicture | null> {
    if (!ID.test(id)) return null;
    const handle = await open(join(this.#folder, id), constants.O_RDONLY | NO_LINK).catch(
      () => null,
    );
    if (handle === null) return null;
    try {
      const info = await handle.stat();
      if (!info.isFile() || info.size > PICTURE_BYTES) return null;
      if (this.#now() - info.mtimeMs > GIVEN_PICTURE_HOURS * 3_600_000) return null;
      const bytes = new Uint8Array(await handle.readFile());
      if (createHash('sha256').update(bytes).digest('hex') !== id) return null;
      const kind = pictureKind(bytes);
      if (kind === null || !wholePicture(bytes)) return null;
      return { path: 'a picture you gave', media_type: kind, bytes, sha256: id };
    } finally {
      await handle.close();
    }
  }

  /** Keeps the newest within their hours, `kept` among them always; the rest go. */
  async #prune(kept: string): Promise<void> {
    const names = (await readdir(this.#folder).catch(() => [] as string[])).filter((name) =>
      ID.test(name),
    );
    const dated = await Promise.all(
      names.map(async (name) => ({
        name,
        at: (await stat(join(this.#folder, name)).catch(() => null))?.mtimeMs ?? 0,
      })),
    );
    dated.sort((a, b) => b.at - a.at);
    const cutoff = this.#now() - GIVEN_PICTURE_HOURS * 3_600_000;
    for (const [index, { name, at }] of dated.entries())
      if (name !== kept && (index >= GIVEN_PICTURES_KEPT || at < cutoff))
        await rm(join(this.#folder, name), { force: true });
  }
}
