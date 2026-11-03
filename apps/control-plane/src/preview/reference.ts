import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { lstat, mkdir, readdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { release } from 'node:os';
import { join } from 'node:path';
import { HOST_FILES } from './check.ts';
import { chromeArguments } from './chrome.ts';
import { TWIN_FRAMES } from './laptop-probe.ts';

/**
 * The pristine starter's own picture a check compares a project's with (DRAWN, ADR 0037): the tracked
 * template applied to a folder of the host's and checked as a project is, with the same profile, host
 * config, server, browser and stepped clock, its no-input picture at the last of `TWIN_FRAMES` kept as
 * a PNG. It is made at the first check that needs it and kept, under the key of everything that shapes
 * the picture, in the data folder, which no agent may read.
 */

/** Raised whenever what a reference is, or how it is taken, changes. */
export const REFERENCE_FORMAT = 1;

/** What shapes a reference, besides the format, the frames, the browser's window and the system's release. */
export interface ReferenceParts {
  /** The template's digest, as `apply` records it. */
  readonly template: string;
  /** The SHA-256 of the lockfile the shared dependencies were installed from. */
  readonly lockfile: string;
  /** The host files' digest (`hostFilesDigest`). */
  readonly host: string;
  /** The browser's recorded SHA-256. */
  readonly browser: string;
}

/**
 * The key a reference is kept under: a SHA-256 over its parts, the format, the frames, the window and
 * the system's release, so a system update, which can change how the same page is drawn, makes a new one.
 */
export function referenceKey(parts: ReferenceParts): string {
  const window = chromeArguments('', 0).find((argument) => argument.startsWith('--window-size='));
  return createHash('sha256')
    .update(
      JSON.stringify([
        REFERENCE_FORMAT,
        TWIN_FRAMES,
        window ?? null,
        release(),
        parts.template,
        parts.lockfile,
        parts.host,
        parts.browser,
      ]),
    )
    .digest('hex');
}

/** The SHA-256 over the starter's host files, each its name and its content's SHA-256; null when one cannot be read. */
export function hostFilesDigest(hostSource: string): string | null {
  const digest = createHash('sha256');
  try {
    for (const file of HOST_FILES) {
      const content = readFileSync(join(hostSource, file));
      digest.update(`${file}\0${createHash('sha256').update(content).digest('hex')}\n`);
    }
  } catch {
    return null;
  }
  return digest.digest('hex');
}

/** The references kept under `root` (`<data dir>/checks/references`), each made once at a time. */
export class StarterReferences {
  readonly #root: string;
  readonly #making = new Map<string, Promise<Buffer | null>>();

  constructor(root: string) {
    this.#root = root;
  }

  /**
   * The picture for a starter and key: read back when kept, else made by `make` in a working folder
   * inside the key's folder, kept as `frame.png` and its working folder removed; the starter's other
   * keys, which nothing reads again, are removed then, unless one is being made. A second caller for a
   * key being made waits for that making. Null when it cannot be made: then nothing is kept, so the
   * next check tries again.
   */
  get(
    starter: string,
    key: string,
    make: (work: string) => Promise<Buffer | null>,
  ): Promise<Buffer | null> {
    const name = `${starter}/${key}`;
    const pending = this.#making.get(name);
    if (pending !== undefined) return pending;
    const getting = this.#get(starter, key, make).finally(() => this.#making.delete(name));
    this.#making.set(name, getting);
    return getting;
  }

  async #get(
    starter: string,
    key: string,
    make: (work: string) => Promise<Buffer | null>,
  ): Promise<Buffer | null> {
    const folder = join(this.#root, starter, key);
    const file = join(folder, 'frame.png');
    try {
      if ((await lstat(file)).isFile()) return await readFile(file);
    } catch {}
    await rm(folder, { recursive: true, force: true });
    const work = join(folder, 'work');
    await mkdir(work, { recursive: true, mode: 0o700 });
    const frame = await make(work).catch(() => null);
    if (frame === null) {
      await rm(folder, { recursive: true, force: true });
      return null;
    }
    // Written as a new file of its own and renamed over, so a reader never sees half of it.
    const temporary = `${file}.${process.pid}.tmp`;
    await writeFile(temporary, frame, { mode: 0o600, flag: 'wx' });
    await rename(temporary, file);
    await rm(work, { recursive: true, force: true });
    for (const other of await readdir(join(this.#root, starter))) {
      if (other !== key && !this.#making.has(`${starter}/${other}`))
        await rm(join(this.#root, starter, other), { recursive: true, force: true });
    }
    return frame;
  }
}
