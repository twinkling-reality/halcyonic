import {
  type Dir,
  lstatSync,
  mkdirSync,
  opendirSync,
  realpathSync,
  type Stats,
  statSync,
} from 'node:fs';
import { basename, dirname, join } from 'node:path';
import {
  type LocationFolder,
  type LocationRoot,
  type LocationsResponse,
  MAX_FOLDER_USERS,
  MAX_LOCATION_FOLDERS,
  type ProjectId,
  type ProjectLocation,
  type ProjectLocationChoice,
} from '@halcyonic/contracts';
import type { DirectoryPolicy } from '@halcyonic/runtime-core';
import { createDirectoryPolicy } from './directory-policy.ts';

/**
 * Why a location cannot be used, as the code its command is refused or fails with. The refusal
 * codes of the directory policy, and `location_exists` for a new folder whose name is taken.
 * `location_not_created` only fails a command: the file system refused to make the folder
 * (`effect: 'none'`), or a folder was made but cannot be used (`effect: 'unknown'`, the message
 * says where it is).
 */
export type LocationRefusal = {
  readonly ok: false;
  readonly code: 'location_missing' | 'location_not_allowed' | 'location_exists';
  readonly message: string;
};

export type LocationBinding =
  | { readonly ok: true; readonly location: ProjectLocation }
  | LocationRefusal
  | {
      readonly ok: false;
      readonly code: 'location_not_created';
      readonly message: string;
      readonly effect: 'none' | 'unknown';
    };

/**
 * Where the host lets projects live: its project roots, the folders directly inside them, and the
 * directory policy every runtime asks before an agent starts. Paths from a client are never used
 * as given: a choice names a root, which must be one of the host's, and a folder directly inside
 * it, and the host composes the path.
 */
/** A project bound to a folder, as the projection has it, so the listing can say who uses what. */
export interface BoundProject {
  readonly project_id: ProjectId;
  readonly path: string;
}

export interface HostLocations {
  readonly policy: DirectoryPolicy;
  /**
   * What a person can choose from now, read from the file system each time. `bound` are the
   * projects with a folder, oldest first: each listed folder names those bound to it.
   */
  list(bound?: readonly BoundProject[]): LocationsResponse;
  /** Whether a choice can be bound now, changing nothing. */
  check(choice: ProjectLocationChoice): { readonly ok: true } | LocationRefusal;
  /** Binds a choice: checks it again and creates a new folder when asked for one. */
  bind(choice: ProjectLocationChoice): LocationBinding;
}

interface Root {
  /** As configured. */
  readonly configured: string;
  /** Its real path when the control plane started, which is what clients are shown. */
  readonly real: string;
  /** The folder's identity then, so a folder put in its place later is not taken for it. */
  readonly device: number;
  readonly inode: number;
}

const NEW_FOLDER_NAME = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;
/** The most entries of one root read to list its folders. */
export const MAX_SCANNED_ENTRIES = 10_000;
const byName = new Intl.Collator('en', { numeric: true, sensitivity: 'base' });

/** `policy` is the host's directory policy over the same roots; a test may pass another. */
export function createHostLocations(
  roots: readonly string[],
  policy: DirectoryPolicy = createDirectoryPolicy(roots),
): HostLocations {
  const known: Root[] = roots.map((configured) => {
    const real = realpathSync.native(configured);
    const { dev, ino } = statSync(real);
    return { configured, real, device: dev, inode: ino };
  });

  const rootOf = (path: string): Root | LocationRefusal =>
    known.find((root) => root.real === path || root.configured === path) ?? {
      ok: false,
      code: 'location_not_allowed',
      message:
        known.length === 0
          ? 'This computer lets agents work in no folder yet: its owner has to allow one first (HALCYONIC_PROJECT_ROOTS).'
          : `${shown(path)} is not one of the folders this computer lets agents work in.`,
    };

  const target = (
    choice: ProjectLocationChoice,
  ): { root: Root; path: string } | LocationRefusal => {
    const root = rootOf(choice.root);
    if ('ok' in root) return root;
    const changed = rootChanged(root);
    if (changed !== null) return changed;
    const name = choice.folder_name;
    if (name === null) return { root, path: root.real };
    if (!singleVisibleSegment(name)) {
      return {
        ok: false,
        code: 'location_not_allowed',
        message: `${JSON.stringify(shown(name))} is not the name of a folder directly inside ${root.real}.`,
      };
    }
    return { root, path: join(root.real, name) };
  };

  const check = (choice: ProjectLocationChoice): { ok: true } | LocationRefusal =>
    unreadable(() => {
      const checked = checkReadable(choice);
      return checked.ok ? { ok: true } : checked;
    });

  /** The check; for an existing folder, `directory` is its real path, the one to record. */
  const checkReadable = (
    choice: ProjectLocationChoice,
  ): { ok: true; directory: string | null } | LocationRefusal => {
    const found = target(choice);
    if ('ok' in found) return found;
    const entry = entryAt(found.path);
    if (choice.kind === 'new_folder') {
      if (!NEW_FOLDER_NAME.test(choice.folder_name)) {
        return {
          ok: false,
          code: 'location_not_allowed',
          message: `${JSON.stringify(choice.folder_name)} cannot name a new folder.`,
        };
      }
      return entry === undefined
        ? { ok: true, directory: null }
        : {
            ok: false,
            code: 'location_exists',
            message: `Something named ${choice.folder_name} is already in ${found.root.real}. Choose it as an existing folder, or give the new folder another name.`,
          };
    }
    if (entry === undefined) {
      return { ok: false, code: 'location_missing', message: `${found.path} is not there.` };
    }
    if (entry.isSymbolicLink()) {
      return {
        ok: false,
        code: 'location_not_allowed',
        message: `${found.path} is a symbolic link. Choose the folder itself.`,
      };
    }
    if (!entry.isDirectory()) {
      return { ok: false, code: 'location_missing', message: `${found.path} is not a folder.` };
    }
    const decision = policy(found.path);
    if (!decision.ok) return decision;
    // The file system's spelling may differ from the client's (case, Unicode form), but it must
    // still be the root itself or a folder directly inside it.
    const inPlace =
      choice.folder_name === null
        ? decision.directory === found.root.real
        : dirname(decision.directory) === found.root.real;
    if (!inPlace) {
      return {
        ok: false,
        code: 'location_not_allowed',
        message: `${found.path} leads to ${decision.directory} through a symbolic link.`,
      };
    }
    return { ok: true, directory: decision.directory };
  };

  return {
    policy,
    list: (bound = []) => {
      const users = usersByFolder(bound, known);
      return { roots: known.map((root) => listRoot(root, users)) };
    },
    check,
    bind: (choice) => unreadable(() => bindReadable(choice)),
  };

  function bindReadable(choice: ProjectLocationChoice): LocationBinding {
    const checked = checkReadable(choice);
    if (!checked.ok) return checked;
    const found = target(choice);
    if ('ok' in found) return found;
    if (choice.kind === 'existing_folder' && checked.directory !== null) {
      return {
        ok: true,
        location: { path: checked.directory, name: nameOf(checked.directory), created: false },
      };
    }
    try {
      // Not recursive, so it fails rather than follow or reuse anything already at the path.
      mkdirSync(found.path);
    } catch (error) {
      return notCreated(found.path, error);
    }
    const decision = policy(found.path);
    if (!decision.ok || decision.directory !== found.path) {
      // Something changed between the check and the folder being made: it exists, and is left
      // where it is, but no project is bound to it. Where it really is, not the path through a
      // link that may have replaced the root, is what the person needs to find it.
      let made = found.path;
      try {
        made = realpathSync.native(found.path);
      } catch {
        made = found.path;
      }
      return {
        ok: false,
        code: 'location_not_created',
        effect: 'unknown',
        message: `A folder was made at ${made}, but it cannot be used, so no project was created and the folder was left in place: ${decision.ok ? `it leads to ${decision.directory}` : decision.message}`,
      };
    }
    return { ok: true, location: { path: found.path, name: nameOf(found.path), created: true } };
  }
}

/**
 * Runs a check that reads the file system, turning anything it throws, such as a root that became
 * unreadable after startup, into a refusal instead of an exception in the command's path.
 */
function unreadable<T>(read: () => T | LocationRefusal): T | LocationRefusal {
  try {
    return read();
  } catch (error) {
    const code = (error as NodeJS.ErrnoException | undefined)?.code;
    return {
      ok: false,
      code: 'location_missing',
      message: `The folder cannot be read${typeof code === 'string' ? ` (${code})` : ''}.`,
    };
  }
}

/** A client's text as a message quotes it: at most 200 characters. */
function shown(text: string): string {
  return text.length <= 200 ? text : `${text.slice(0, 199)}…`;
}

/**
 * Lists a root's folders from at most `MAX_SCANNED_ENTRIES` of its entries, in the order the file
 * system gives them, so a root holding a very large number of files cannot hold up the control
 * plane; a root with more is listed as truncated. Facts are read only for the folders listed, at
 * most three `lstat` calls each.
 */
function listRoot(root: Root, users: ReadonlyMap<string, ProjectId[]>): LocationRoot {
  let available = rootChanged(root) === null;
  let names: string[] = [];
  let unread = false;
  if (available) {
    let directory: Dir | null = null;
    try {
      directory = opendirSync(root.real);
      let scanned = 0;
      for (let entry = directory.readSync(); entry !== null; entry = directory.readSync()) {
        if (scanned === MAX_SCANNED_ENTRIES) {
          unread = true;
          break;
        }
        scanned += 1;
        // A symbolic link reads as one here, never as a directory, so none is listed.
        if (entry.isDirectory() && singleVisibleSegment(entry.name)) names.push(entry.name);
      }
    } catch {
      // A root that cannot be read lists as missing: nothing in it can be used.
      available = false;
      names = [];
    } finally {
      try {
        directory?.closeSync();
      } catch {
        // Already closed.
      }
    }
  }
  names.sort((a, b) => byName.compare(a, b) || (a < b ? -1 : 1));
  const folders: LocationFolder[] = [];
  for (const name of names.slice(0, MAX_LOCATION_FOLDERS)) {
    const path = join(root.real, name);
    const facts = factsOf(path, users);
    // Gone, or no longer a folder, since the root was read: not listed.
    if (facts !== null) folders.push({ name, path, ...facts });
  }
  const rootFacts = available ? factsOf(root.real, users) : null;
  // A root replaced while it was read may have had its folders' facts read elsewhere: none is kept.
  if (available && rootChanged(root) !== null) {
    return {
      path: root.real,
      name: nameOf(root.real),
      status: 'missing',
      repository: null,
      changed_at: null,
      used_by: [],
      folders: [],
      folders_truncated: false,
    };
  }
  return {
    path: root.real,
    name: nameOf(root.real),
    status: available ? 'available' : 'missing',
    ...(rootFacts ?? { repository: null, changed_at: null, used_by: [] }),
    folders,
    folders_truncated: unread || names.length > MAX_LOCATION_FOLDERS,
  };
}

type FolderFacts = Pick<LocationFolder, 'repository' | 'changed_at' | 'used_by'>;

/** A folder's identity on the file system, which no spelling of its path changes. */
function identity(entry: Stats): string {
  return `${entry.dev}:${entry.ino}`;
}

/**
 * The projects bound to each folder, by the identity of the folder now at each project's path.
 * Only a path that is a root or directly inside one can name a listed folder, so no other is read,
 * and each path is read once however many projects share it. A path counts only while it is its
 * own real path, the rule every start applies, so a project whose path now leads anywhere through
 * a symbolic link, at its end or on the way, names no folder; nor does one that cannot be read.
 */
function usersByFolder(
  bound: readonly BoundProject[],
  roots: readonly Root[],
): Map<string, ProjectId[]> {
  const real = new Set(roots.map((root) => root.real));
  const byPath = new Map<string, ProjectId[]>();
  for (const project of bound) {
    if (!real.has(project.path) && !real.has(dirname(project.path))) continue;
    const list = byPath.get(project.path) ?? [];
    list.push(project.project_id);
    byPath.set(project.path, list);
  }
  const users = new Map<string, ProjectId[]>();
  for (const [path, projects] of byPath) {
    let entry: Stats | undefined;
    try {
      entry = realpathSync.native(path) === path ? entryAt(path) : undefined;
    } catch {
      entry = undefined;
    }
    if (entry === undefined || !entry.isDirectory()) continue;
    const key = identity(entry);
    users.set(key, [...(users.get(key) ?? []), ...projects]);
  }
  // Oldest first across paths, as the projects came, and at most the contract's number.
  const order = new Map(bound.map((project, index) => [project.project_id, index]));
  for (const [key, list] of users) {
    users.set(
      key,
      list.sort((a, b) => (order.get(a) ?? 0) - (order.get(b) ?? 0)).slice(0, MAX_FOLDER_USERS),
    );
  }
  return users;
}

/**
 * What the folder at `path` shows of itself: whether a `.git` entry sits directly inside it, the
 * newer change time of the two, and the projects bound to it. Each read leaves a link at the end
 * of its path unfollowed. Null when it is not a folder, or not the same folder after the reads as
 * before, so a folder simply swapped meanwhile is left out; one swapped for a link and back between
 * two reads, or a root replaced meanwhile (which `listRoot` checks again afterwards), can still have
 * a fact read elsewhere. Whoever can do that already reads the file system directly.
 */
function factsOf(path: string, users: ReadonlyMap<string, ProjectId[]>): FolderFacts | null {
  let before: Stats | undefined;
  try {
    before = entryAt(path);
  } catch {
    return null;
  }
  if (before === undefined || !before.isDirectory()) return null;
  let git: Stats | undefined | null;
  try {
    git = entryAt(join(path, '.git'));
  } catch {
    // The folder cannot be searched: what is inside it is unknown.
    git = null;
  }
  let after: Stats | undefined;
  try {
    after = entryAt(path);
  } catch {
    return null;
  }
  if (after === undefined || !after.isDirectory() || identity(after) !== identity(before)) {
    return null;
  }
  const repository = git === null ? null : git !== undefined && (git.isDirectory() || git.isFile());
  const newest = repository === true && git ? Math.max(after.mtimeMs, git.mtimeMs) : after.mtimeMs;
  return {
    repository,
    changed_at: repository === null ? null : timestamp(newest),
    used_by: [...(users.get(identity(after)) ?? [])],
  };
}

/** A file system time as a contract timestamp; null for one the contract cannot carry. */
function timestamp(milliseconds: number): string | null {
  if (!Number.isFinite(milliseconds)) return null;
  const date = new Date(Math.floor(milliseconds));
  if (Number.isNaN(date.getTime())) return null;
  const text = date.toISOString();
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/.test(text) ? text : null;
}

function notCreated(path: string, error: unknown): LocationBinding {
  const code = (error as NodeJS.ErrnoException).code;
  if (code === 'EEXIST') {
    return {
      ok: false,
      code: 'location_exists',
      message: `Something named ${basename(path)} appeared in the meantime. Choose it as an existing folder, or give the new folder another name.`,
    };
  }
  if (code === 'ENOENT' || code === 'ENOTDIR') {
    return {
      ok: false,
      code: 'location_missing',
      message: `The folder the new one was to go in is not there any more.`,
    };
  }
  return {
    ok: false,
    code: 'location_not_created',
    effect: 'none',
    message: `The folder ${path} could not be made${code === undefined ? '' : ` (${code})`}.`,
  };
}

/**
 * Null while the root is still the folder the control plane found at startup: at the same real
 * path, with no symbolic link anywhere along it, and the same folder on the same device. A root
 * replaced since, by a link or by another folder, is refused, so nothing is made or bound through
 * it; restarting the control plane takes the roots as they are then.
 */
function rootChanged(root: Root): LocationRefusal | null {
  let entry: Stats | undefined;
  try {
    entry = entryAt(root.real);
  } catch (error) {
    const code = (error as NodeJS.ErrnoException | undefined)?.code;
    return {
      ok: false,
      code: 'location_missing',
      message: `The folder ${root.real}, where projects live, cannot be read${typeof code === 'string' ? ` (${code})` : ''}.`,
    };
  }
  if (entry === undefined || (!entry.isDirectory() && !entry.isSymbolicLink())) {
    return {
      ok: false,
      code: 'location_missing',
      message: `The folder ${root.real}, where projects live, is not there any more.`,
    };
  }
  let real: string | null = null;
  try {
    real = realpathSync.native(root.real);
  } catch {
    real = null;
  }
  if (
    entry.isSymbolicLink() ||
    real !== root.real ||
    entry.dev !== root.device ||
    entry.ino !== root.inode
  ) {
    return {
      ok: false,
      code: 'location_not_allowed',
      message: `The folder ${root.real}, where projects live, has been replaced since this computer allowed it. Its owner restarts Halcyonic on the Mac to allow what is there now.`,
    };
  }
  return null;
}

function singleVisibleSegment(name: string): boolean {
  return (
    name.length > 0 &&
    name.length <= 255 &&
    !name.startsWith('.') &&
    !name.includes('/') &&
    !name.includes('\0')
  );
}

/**
 * What is at the path itself, without following a symbolic link; undefined for nothing there. A
 * path the file system will not read (EACCES, EPERM) throws, and callers say it cannot be read.
 */
function entryAt(path: string): Stats | undefined {
  return lstatSync(path, { throwIfNoEntry: false });
}

function nameOf(path: string): string {
  return basename(path) || path;
}
