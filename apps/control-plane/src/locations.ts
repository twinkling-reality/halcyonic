import { lstatSync, mkdirSync, readdirSync, realpathSync, type Stats, statSync } from 'node:fs';
import { basename, join } from 'node:path';
import {
  type LocationFolder,
  type LocationRoot,
  type LocationsResponse,
  MAX_LOCATION_FOLDERS,
  type ProjectLocation,
  type ProjectLocationChoice,
} from '@halcyonic/contracts';
import type { DirectoryPolicy } from '@halcyonic/runtime-core';
import { createDirectoryPolicy } from './directory-policy.ts';

/**
 * Why a location cannot be used, as the code its command is refused or fails with. The refusal
 * codes of the directory policy, and `location_exists` for a new folder whose name is taken.
 * `location_not_created` only fails a command: the file system refused to make the folder.
 */
export type LocationRefusal = {
  readonly ok: false;
  readonly code: 'location_missing' | 'location_not_allowed' | 'location_exists';
  readonly message: string;
};

export type LocationBinding =
  | { readonly ok: true; readonly location: ProjectLocation }
  | LocationRefusal
  | { readonly ok: false; readonly code: 'location_not_created'; readonly message: string };

/**
 * Where the host lets projects live: its project roots, the folders directly inside them, and the
 * directory policy every runtime asks before an agent starts. Paths from a client are never used
 * as given: a choice names a root, which must be one of the host's, and a folder directly inside
 * it, and the host composes the path.
 */
export interface HostLocations {
  readonly policy: DirectoryPolicy;
  /** What a person can choose from now, read from the file system each time. */
  list(): LocationsResponse;
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
}

const NEW_FOLDER_NAME = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;
const byName = new Intl.Collator('en', { numeric: true, sensitivity: 'base' });

export function createHostLocations(roots: readonly string[]): HostLocations {
  const policy = createDirectoryPolicy(roots);
  const known: Root[] = roots.map((configured) => ({
    configured,
    real: realpathSync(configured),
  }));

  const rootOf = (path: string): Root | LocationRefusal =>
    known.find((root) => root.real === path || root.configured === path) ?? {
      ok: false,
      code: 'location_not_allowed',
      message:
        known.length === 0
          ? 'This computer lets agents work in no folder yet: its owner has to allow one first (HALCYONIC_PROJECT_ROOTS).'
          : `${path} is not one of the folders this computer lets agents work in.`,
    };

  const target = (
    choice: ProjectLocationChoice,
  ): { root: Root; path: string } | LocationRefusal => {
    const root = rootOf(choice.root);
    if ('ok' in root) return root;
    if (!isDirectory(root.real)) {
      return {
        ok: false,
        code: 'location_missing',
        message: `The folder ${root.real}, where projects live, is not there any more.`,
      };
    }
    const name = choice.folder_name;
    if (name === null) return { root, path: root.real };
    if (!singleVisibleSegment(name)) {
      return {
        ok: false,
        code: 'location_not_allowed',
        message: `${JSON.stringify(name)} is not the name of a folder directly inside ${root.real}.`,
      };
    }
    return { root, path: join(root.real, name) };
  };

  const check = (choice: ProjectLocationChoice): { ok: true } | LocationRefusal => {
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
        ? { ok: true }
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
    if (decision.directory !== found.path) {
      return {
        ok: false,
        code: 'location_not_allowed',
        message: `${found.path} leads to ${decision.directory} through a symbolic link.`,
      };
    }
    return { ok: true };
  };

  return {
    policy,
    list: () => ({ roots: known.map(listRoot) }),
    check,
    bind(choice) {
      const checked = check(choice);
      if (!checked.ok) return checked;
      const found = target(choice);
      if ('ok' in found) return found;
      if (choice.kind === 'existing_folder') {
        return {
          ok: true,
          location: { path: found.path, name: nameOf(found.path), created: false },
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
        return {
          ok: false,
          code: 'location_not_created',
          message: `The folder ${found.path} was made, but is not usable as it is: ${decision.ok ? `it leads to ${decision.directory}` : decision.message}`,
        };
      }
      return { ok: true, location: { path: found.path, name: nameOf(found.path), created: true } };
    },
  };
}

function listRoot(root: Root): LocationRoot {
  const available = isDirectory(root.real);
  let folders: LocationFolder[] = [];
  if (available) {
    try {
      folders = readdirSync(root.real, { withFileTypes: true })
        // A symbolic link reads as one here, never as a directory, so none is listed.
        .filter((entry) => entry.isDirectory() && singleVisibleSegment(entry.name))
        .map((entry) => ({ name: entry.name, path: join(root.real, entry.name) }));
    } catch {
      folders = [];
    }
  }
  folders.sort((a, b) => byName.compare(a.name, b.name) || (a.name < b.name ? -1 : 1));
  return {
    path: root.real,
    name: nameOf(root.real),
    status: available ? 'available' : 'missing',
    folders: folders.slice(0, MAX_LOCATION_FOLDERS),
    folders_truncated: folders.length > MAX_LOCATION_FOLDERS,
  };
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
    message: `The folder ${path} could not be made${code === undefined ? '' : ` (${code})`}.`,
  };
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

/** What is at the path itself, without following a symbolic link; undefined for nothing readable. */
function entryAt(path: string): Stats | undefined {
  try {
    return lstatSync(path, { throwIfNoEntry: false });
  } catch {
    return undefined;
  }
}

function isDirectory(path: string): boolean {
  return statSync(path, { throwIfNoEntry: false })?.isDirectory() === true;
}

function nameOf(path: string): string {
  return basename(path) || path;
}
