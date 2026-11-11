import { execFile } from 'node:child_process';
import { copyFile, lstat, realpath, rename, rm, stat } from 'node:fs/promises';
import { basename, join, relative, sep } from 'node:path';
import { secretShapedPath } from '@halcyonic/runtime-core';
import type { CheckpointStore } from './store.ts';

/**
 * Keeping what was made (F3): the project's own folder shown in Finder on the Mac, and a copy of it
 * saved as a zip in the Mac's Downloads folder. The zip is the store's tree of the folder, what a
 * checkpoint keeps, so never `.git` and never what the project ignores; the store refuses a folder
 * holding Halcyonic's data; and never a file one list names as one that may hold secrets
 * (`secretShapedPath`, the list a sandbox keeps agents from), whatever the case of its name.
 */

export interface ProjectKeepingOptions {
  readonly store: Pick<CheckpointStore, 'archive'>;
  /** The folder a saved copy goes in: the Mac's Downloads folder of the account. */
  readonly downloads: string;
  readonly now: () => Date;
  /** Shows `path` selected in Finder; true when it did. `/usr/bin/open -R` by default. */
  readonly reveal?: (path: string) => Promise<boolean>;
}

export type KeepingFailure = {
  readonly ok: false;
  readonly code: string;
  readonly message: string;
};

export class ProjectKeeping {
  readonly #options: ProjectKeepingOptions;
  readonly #reveal: (path: string) => Promise<boolean>;

  constructor(options: ProjectKeepingOptions) {
    this.#options = options;
    this.#reveal = options.reveal ?? openSelected;
  }

  /** Shows the project's folder in Finder on the Mac. */
  async show(folder: string): Promise<{ readonly ok: true } | KeepingFailure> {
    if ((await lstat(folder).catch(() => null))?.isDirectory() !== true)
      return { ok: false, code: 'location_missing', message: "The project's folder is not there." };
    return (await this.#reveal(folder))
      ? { ok: true }
      : { ok: false, code: 'finder_failed', message: 'Finder could not show the folder.' };
  }

  /**
   * Saves the zip as `<the folder's own name> <date and time>.zip` in Downloads, never over a file
   * that is there, then shows it in Finder. `otherFolders` are every other project's folder: any
   * inside this one is left out.
   */
  async save(
    projectId: string,
    folder: string,
    otherFolders: readonly string[],
  ): Promise<
    | { readonly ok: true; readonly file: string; readonly files: number; readonly bytes: number }
    | KeepingFailure
  > {
    if ((await lstat(folder).catch(() => null))?.isDirectory() !== true)
      return { ok: false, code: 'location_missing', message: "The project's folder is not there." };
    if ((await lstat(this.#options.downloads).catch(() => null))?.isDirectory() !== true)
      return { ok: false, code: 'downloads_missing', message: 'This Mac has no Downloads folder.' };
    const real = await realpath(folder);
    const inside: string[] = [];
    for (const other of otherFolders) {
      const path = await realpath(other).catch(() => other);
      if (path !== real && path.startsWith(real + sep))
        inside.push(relative(real, path).split(sep).join('/'));
    }
    const made = await this.#options.store.archive(
      projectId,
      real,
      (path) =>
        secretShapedPath(path) ||
        inside.some((other) => path === other || path.startsWith(`${other}/`)),
    );
    if (!made.ok) return made;
    try {
      const target = await this.#freeName(basename(real));
      await move(made.file, target);
      const bytes = (await stat(target)).size;
      await this.#reveal(target);
      return { ok: true, file: basename(target), files: made.files, bytes };
    } catch {
      await rm(made.file, { force: true });
      return {
        ok: false,
        code: 'save_failed',
        message: 'The copy could not be saved in Downloads.',
      };
    }
  }

  /** A name in Downloads nothing has yet: the folder's name and the time, then ` 2`, ` 3`... */
  async #freeName(name: string): Promise<string> {
    const now = this.#options.now();
    const pad = (value: number) => String(value).padStart(2, '0');
    const stamp = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())} ${pad(now.getHours())}-${pad(now.getMinutes())}`;
    const safe = name.replace(/[/:\0]/g, '-');
    for (let n = 1; n < 1000; n += 1) {
      const candidate = join(
        this.#options.downloads,
        `${safe} ${stamp}${n === 1 ? '' : ` ${n}`}.zip`,
      );
      if ((await lstat(candidate).catch(() => null)) === null) return candidate;
    }
    throw new Error('no free name');
  }
}

/** Moves a file, copying where it crosses volumes; never over one that is there. */
async function move(from: string, to: string): Promise<void> {
  if ((await lstat(to).catch(() => null)) !== null) throw new Error('taken');
  try {
    await rename(from, to);
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code !== 'EXDEV') throw error;
    await copyFile(from, to, 1 /* COPYFILE_EXCL */);
    await rm(from, { force: true });
  }
}

function openSelected(path: string): Promise<boolean> {
  return new Promise((resolve) => {
    execFile('/usr/bin/open', ['-R', path], { timeout: 30_000 }, (error) =>
      resolve(error === null),
    );
  });
}
