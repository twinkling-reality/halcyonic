import { createHash } from 'node:crypto';
import {
  copyFileSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  symlinkSync,
} from 'node:fs';
import { join } from 'node:path';
import type { StarterView } from '@halcyonic/contracts';

/**
 * Starters a new project can begin from (ADR 0037). Each is tracked in the repository under
 * `starters/<id>/`: `starter.json` (its title), the pinned `package-lock.json`, and `template/`, the
 * files a project begins with. Its dependencies are installed on each Mac into
 * `<data dir>/starters/<id>/node_modules`, beside `installed.json`, which names the SHA-256 of the
 * lockfile they were installed from; a starter counts as installed only while that matches the
 * tracked lockfile. A project gets the template's files and a link to the shared dependencies,
 * which sit outside every folder an agent's sandbox can write.
 */
export interface HostStartersOptions {
  /** The repository's `starters` folder. */
  readonly trackedDir: string;
  /** Where the host installs each starter's dependencies, in its data directory. */
  readonly installedDir: string;
}

export type StarterApplied =
  | {
      readonly ok: true;
      /** SHA-256 of the lockfile the shared dependencies were installed from. */
      readonly lockfile: string;
      readonly files: number;
      /** SHA-256 over every template file's path and content, in path order. */
      readonly template: string;
    }
  | { readonly ok: false; readonly message: string };

const ID = /^[a-z][a-z0-9-]{0,62}$/;

export class HostStarters {
  readonly #tracked: string;
  readonly #installed: string;

  constructor(options: HostStartersOptions) {
    this.#tracked = options.trackedDir;
    this.#installed = options.installedDir;
  }

  /** Every tracked starter, read from disk each time, and whether it is installed on this Mac. */
  list(): StarterView[] {
    if (!existsSync(this.#tracked)) return [];
    const views: StarterView[] = [];
    for (const id of readdirSync(this.#tracked).sort()) {
      if (!ID.test(id)) continue;
      const title = this.#title(id);
      if (title === null) continue;
      views.push({ starter_id: id, title, installed: this.#installedLockfile(id) !== null });
    }
    return views;
  }

  /** Whether a project can begin from the starter now, changing nothing. */
  check(id: string): { readonly ok: true } | { readonly ok: false; readonly message: string } {
    if (!ID.test(id) || this.#title(id) === null)
      return { ok: false, message: `There is no starter named ${id} on this computer.` };
    if (this.#installedLockfile(id) === null)
      return {
        ok: false,
        message:
          "That starter isn't set up on this computer yet: its dependencies aren't installed.",
      };
    return { ok: true };
  }

  /**
   * Copies the starter's template into `folder`, a new folder the host has just made, and links
   * `node_modules` to the shared dependencies. A template holding anything but files and folders is
   * refused before anything is copied.
   */
  apply(id: string, folder: string): StarterApplied {
    const checked = this.check(id);
    if (!checked.ok) return checked;
    const lockfile = this.#installedLockfile(id);
    if (lockfile === null) return { ok: false, message: 'The starter is not installed.' };
    const template = join(this.#tracked, id, 'template');
    const files = listFiles(template);
    if (files === null)
      return { ok: false, message: 'The starter holds something other than files and folders.' };
    const digest = createHash('sha256');
    for (const relative of files) {
      const content = readFileSync(join(template, relative));
      digest.update(`${relative}\0${createHash('sha256').update(content).digest('hex')}\n`);
      mkdirSync(join(folder, relative, '..'), { recursive: true });
      copyFileSync(join(template, relative), join(folder, relative));
    }
    symlinkSync(join(this.#installed, id, 'node_modules'), join(folder, 'node_modules'), 'dir');
    return { ok: true, lockfile, files: files.length, template: digest.digest('hex') };
  }

  #title(id: string): string | null {
    try {
      const parsed = JSON.parse(readFileSync(join(this.#tracked, id, 'starter.json'), 'utf8')) as {
        format?: unknown;
        title?: unknown;
      };
      return parsed.format === 1 &&
        typeof parsed.title === 'string' &&
        /\S/.test(parsed.title) &&
        parsed.title.length <= 80
        ? parsed.title
        : null;
    } catch {
      return null;
    }
  }

  /** The tracked lockfile's SHA-256 when the installed dependencies were made from it, else null. */
  #installedLockfile(id: string): string | null {
    try {
      const tracked = createHash('sha256')
        .update(readFileSync(join(this.#tracked, id, 'package-lock.json')))
        .digest('hex');
      const record = JSON.parse(
        readFileSync(join(this.#installed, id, 'installed.json'), 'utf8'),
      ) as {
        format?: unknown;
        lockfile?: unknown;
      };
      const modules = lstatSync(join(this.#installed, id, 'node_modules'));
      return record.format === 1 && record.lockfile === tracked && modules.isDirectory()
        ? tracked
        : null;
    } catch {
      return null;
    }
  }
}

/** Every file below `root`, relative and sorted, or null when it holds a link or anything else. */
function listFiles(root: string): string[] | null {
  const files: string[] = [];
  const walk = (relative: string): boolean => {
    for (const name of readdirSync(join(root, relative)).sort()) {
      const path = relative === '' ? name : `${relative}/${name}`;
      const stats = lstatSync(join(root, path));
      if (stats.isDirectory()) {
        if (!walk(path)) return false;
      } else if (stats.isFile()) files.push(path);
      else return false;
    }
    return true;
  };
  return walk('') ? files : null;
}
