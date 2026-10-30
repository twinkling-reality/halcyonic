import { createHash } from 'node:crypto';
import {
  copyFileSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  realpathSync,
  symlinkSync,
} from 'node:fs';
import { homedir } from 'node:os';
import { join, resolve, sep } from 'node:path';
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
  /** The control plane's data directory, which no VR version's original may hold; unknown refuses every original. */
  readonly dataDir?: string;
  /** The person's home folder; the system's own when not given. */
  readonly home?: string;
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

/** The starter a VR version of a project begins from (M1.8c-1). */
export const VR_STARTER = 'iwsdk-vr';

/**
 * What the host asks a VR version's agent to do (M1.8c-1, M1.8-PLAN section 5): read the original in
 * its folder, change nothing there, and build the same game in its own project from the starter.
 */
export function vrVersionInstruction(originalFolder: string): string {
  return [
    `Make a VR version of the web app in ${originalFolder}.`,
    'Read that folder to understand the game: what you see, what you do, how you score or win.',
    'Do not change anything there.',
    'Build the same game in this project with the Immersive Web SDK, so it plays in VR with controllers or hands and on a laptop with a mouse and keyboard.',
    'Read every input through src/play-input.ts.',
    'Copy pictures and sounds you need from the original into public/ and name them in src/assets.ts.',
    "Keep the original's colours and feel.",
    'Start with the main thing the player does, make it work, then add the rest one piece at a time.',
  ].join(' ');
}

/** The host's facts an original is weighed against (AUDIT M18C1-M1). */
export interface OriginalBounds {
  /** The project roots, as the host's locations name them. */
  readonly roots: readonly string[];
  readonly home: string;
  /** The control plane's data directory, or undefined: then no original is allowed. */
  readonly dataDir: string | undefined;
  /** Every project's folder. */
  readonly projects: readonly string[];
}

/** The folder as the file system resolves it, or as written when it cannot. */
function real(path: string): string {
  try {
    return realpathSync.native(path);
  } catch {
    return resolve(path);
  }
}

/** Whether `child` is `parent` or inside it. */
function within(child: string, parent: string): boolean {
  return child === parent || child.startsWith(parent.endsWith(sep) ? parent : parent + sep);
}

/**
 * Why a folder may not be a VR version's original, or null when it may (AUDIT M18C1-M1). A VR
 * version's agent reads the whole original, so the original must be one game's own folder: never a
 * project root, the home folder or a folder above it, a folder holding or inside the control plane's
 * data, or a folder holding another project's folder.
 */
export function originalRefusal(folder: string, bounds: OriginalBounds): string | null {
  const original = real(folder);
  if (bounds.dataDir === undefined)
    return 'This computer has not said where its data lives, so no game can be read.';
  if (bounds.roots.some((root) => within(real(root), original)))
    return `${original} is a whole projects folder or holds one, not one game's folder.`;
  if (within(real(bounds.home), original)) return `${original} is the home folder or holds it.`;
  const data = real(bounds.dataDir);
  if (within(data, original) || within(original, data))
    return `${original} holds Halcyonic's data or is inside it.`;
  if (
    bounds.projects.some((project) => real(project) !== original && within(real(project), original))
  )
    return `${original} holds another project's folder.`;
  return null;
}

/** A project as `originalFolderOf` reads it from the journal. */
export interface ProjectFolder {
  readonly id: string;
  readonly folder: string | null;
  readonly starter: string | null;
  readonly madeFrom: string | null;
}

/**
 * The original's folder a VR version's folder may read, or null (M1.8c-1): null unless exactly one
 * starter project has that folder (AUDIT M18C1-S2), it was made from a project that has a folder, and
 * that folder is allowed (`refuse` gives null).
 */
export function originalFolderOf(
  directory: string,
  projects: readonly ProjectFolder[],
  refuse: (folder: string) => string | null,
): string | null {
  const here = projects.filter(
    (project) => project.starter !== null && project.folder === directory,
  );
  if (here.length !== 1) return null;
  const madeFrom = here[0]?.madeFrom ?? null;
  const original = projects.find((project) => project.id === madeFrom)?.folder ?? null;
  if (original === null) return null;
  return refuse(original) === null ? original : null;
}

export class HostStarters {
  readonly #tracked: string;
  readonly #installed: string;
  readonly #dataDir: string | undefined;
  readonly #home: string;

  constructor(options: HostStartersOptions) {
    this.#tracked = options.trackedDir;
    this.#installed = options.installedDir;
    this.#dataDir = options.dataDir;
    this.#home = options.home ?? homedir();
  }

  /** Why a folder may not be a VR version's original on this host, or null (AUDIT M18C1-M1). */
  originalRefusal(
    folder: string,
    roots: readonly string[],
    projects: readonly string[],
  ): string | null {
    return originalRefusal(folder, { roots, home: this.#home, dataDir: this.#dataDir, projects });
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
