import { createHash } from 'node:crypto';
import {
  copyFileSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  realpathSync,
  renameSync,
  rmSync,
  symlinkSync,
  writeFileSync,
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

/** How a starter's installed set stands against its tracked lockfile (`pnpm mac-setup starter`). */
export type InstalledSet =
  | { readonly state: 'missing' }
  /** The starter's folder is a link, or anything but a folder of its own. */
  | { readonly state: 'not_a_folder' }
  | { readonly state: 'differs'; readonly differs: readonly string[] }
  | { readonly state: 'matches'; readonly lockfile: string; readonly packages: number };

/**
 * Whether `<installedDir>/<id>/node_modules` holds exactly the packages the tracked lockfile pins,
 * each at its pinned version as its own package.json says, and nothing else (AUDIT SETUP-2): only
 * folders and regular files, no link but npm's `.bin` links to files inside the set, and no name in
 * a `node_modules` folder that the lockfile does not list (`.bin` and npm's `.package-lock.json`
 * aside). An optional package (a platform's build) may be absent; a lockfile entry without a version
 * never matches. Every entry is looked at with `lstat` before anything is read, so a planted pipe
 * cannot hang it. `lockfile` is the tracked lockfile's SHA-256; `differs` names paths in the
 * starter's folder.
 */
export function installedSet(trackedDir: string, installedDir: string, id: string): InstalledSet {
  const folder = join(installedDir, id);
  try {
    if (!lstatSync(folder).isDirectory()) return { state: 'not_a_folder' };
    if (!lstatSync(join(folder, 'node_modules')).isDirectory())
      return { state: 'differs', differs: ['node_modules'] };
  } catch {
    return { state: 'missing' };
  }
  const text = readFileSync(join(trackedDir, id, 'package-lock.json'));
  const lock = JSON.parse(text.toString('utf8')) as {
    packages?: Record<string, { version?: unknown; optional?: unknown }>;
  };
  const entries = Object.entries(lock.packages ?? {}).filter(([path]) => path !== '');
  const listed = new Set(entries.map(([path]) => path));
  const modules = realpathSync(join(folder, 'node_modules'));
  const differs: string[] = [];
  const files = new Set<string>();
  const found = new Set<string>();
  /** What a folder's entries may be: a `node_modules` folder's, a scope's, npm's `.bin` or a package's. */
  type Kind = 'modules' | 'scope' | 'bin' | 'package';
  const walk = (relative: string, kind: Kind): void => {
    for (const name of readdirSync(join(folder, relative)).sort()) {
      const path = `${relative}/${name}`;
      const stats = lstatSync(join(folder, path));
      let inner: Kind | 'file' | null;
      if (kind === 'bin')
        inner =
          stats.isFile() || (stats.isSymbolicLink() && fileInside(join(folder, path), modules))
            ? 'file'
            : null;
      else if (kind === 'package')
        inner = stats.isFile()
          ? 'file'
          : stats.isDirectory()
            ? name === 'node_modules'
              ? 'modules'
              : 'package'
            : null;
      else if (kind === 'modules' && name === '.bin') inner = stats.isDirectory() ? 'bin' : null;
      else if (kind === 'modules' && name === '.package-lock.json' && relative === 'node_modules')
        inner = stats.isFile() ? 'file' : null;
      else if (kind === 'modules' && name.startsWith('@'))
        inner = stats.isDirectory() ? 'scope' : null;
      else inner = listed.has(path) && stats.isDirectory() ? 'package' : null;
      if (inner === null) differs.push(path);
      else if (inner === 'file') files.add(path);
      else {
        if (inner === 'package' && kind !== 'package') found.add(path);
        walk(path, inner);
      }
    }
  };
  walk('node_modules', 'modules');
  let packages = 0;
  for (const [path, entry] of entries) {
    if (!found.has(path) && entry.optional === true && typeof entry.version === 'string') continue;
    let version: unknown;
    try {
      version = files.has(`${path}/package.json`)
        ? (
            JSON.parse(readFileSync(join(folder, path, 'package.json'), 'utf8')) as {
              version?: unknown;
            }
          ).version
        : undefined;
    } catch {
      version = undefined;
    }
    if (typeof entry.version === 'string' && version === entry.version) packages += 1;
    else differs.push(path);
  }
  return differs.length > 0
    ? { state: 'differs', differs }
    : { state: 'matches', lockfile: createHash('sha256').update(text).digest('hex'), packages };
}

/** Whether the link at `path` leads to a regular file inside `root`, a real path. */
function fileInside(path: string, root: string): boolean {
  try {
    const real = realpathSync(path);
    return real.startsWith(`${root}${sep}`) && lstatSync(real).isFile();
  } catch {
    return false;
  }
}

/**
 * Records the installed set as made from the tracked lockfile, so the starter counts as installed.
 * The record is written as the settings file is: a new file of its own (mode 600) renamed over the
 * old, so a link at its name is never followed (AUDIT SETUP-1).
 */
export function recordInstalled(installedDir: string, id: string, lockfile: string): void {
  const path = join(installedDir, id, 'installed.json');
  const temporary = `${path}.${process.pid}.tmp`;
  rmSync(temporary, { force: true });
  writeFileSync(temporary, `${JSON.stringify({ format: 1, lockfile })}\n`, {
    mode: 0o600,
    flag: 'wx',
  });
  renameSync(temporary, path);
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
