import { realpathSync, statSync } from 'node:fs';
import { isAbsolute, relative, resolve, sep } from 'node:path';

/** Whose folders these are: the person's home, Halcyonic's data, and the user running it. */
export interface FolderContext {
  readonly home: string;
  readonly dataDir: string;
  /** Undefined where the platform has no user ids; ownership is then not checked. */
  readonly uid: number | undefined;
}

/** Whether a folder may hold projects: never, only after a second thought, or yes. */
export type FolderAssessment =
  | { readonly verdict: 'refused'; readonly reason: string }
  | { readonly verdict: 'broad'; readonly reason: string }
  | { readonly verdict: 'fine' };

/** Folders that hold every user's or every drive's files, refused themselves. */
const SHARED_ROOTS = [
  '/',
  '/Users',
  '/Volumes',
  '/private',
  '/private/var',
  '/private/tmp',
  '/var',
  '/tmp',
  '/opt',
];

/** Folders whose whole content belongs to macOS, its apps, other people or programs on the PATH. */
const SYSTEM_TREES = [
  '/System',
  '/Library',
  '/Applications',
  '/usr',
  '/bin',
  '/sbin',
  '/etc',
  '/private/etc',
  '/cores',
  '/dev',
  '/opt/homebrew',
  '/Users/Shared',
];

/** Folders in a home folder that hold much more than projects. */
const PERSONAL_FOLDERS = [
  'Desktop',
  'Documents',
  'Downloads',
  'Pictures',
  'Movies',
  'Music',
  'Public',
];

/**
 * Whether a folder may be a project root, which lets agents read and change everything in it
 * (ADR 0020, ADR 0024). Refused: the whole disk and folders shared by every user or drive; what
 * belongs to macOS, its apps and programs on the PATH, in the forms `realpath(3)` gives them
 * (`/etc` is `/private/etc`); `/private/var`, except a folder made inside a user's own temporary
 * or cache folder; a whole drive; another person's home; the home folder and every folder holding
 * it; Halcyonic's data and anything holding it or in it; the hidden folders and Library in the home
 * folder, where apps keep settings and keys; a folder another user owns; and a folder any user can
 * change. Broad: a personal folder such as Documents. `real` is the folder's real path; a folder
 * that is not there yet is judged by its path alone.
 */
export function assessFolder(real: string, context: FolderContext): FolderAssessment {
  const home = realOrSelf(context.home);
  const data = realOrSelf(context.dataDir);
  if (SHARED_ROOTS.includes(real) || SYSTEM_TREES.some((tree) => within(tree, real))) {
    return refused('It holds files of macOS, its apps or other people, not only your projects.');
  }
  if (within('/private/var', real) && !insideOwnTemporaryFolder(real)) {
    return refused('It holds files of macOS, its apps or other people, not only your projects.');
  }
  if (segments('/Volumes', real)?.length === 1) {
    return refused('It is a whole drive, with everything on it.');
  }
  if (within('/Users', real) && !within(home, real) && !within(real, home)) {
    return refused("It is another person's folder.");
  }
  if (within(real, home)) {
    return refused('It holds your whole home folder, so agents could change everything you have.');
  }
  if (within(real, data) || within(data, real)) {
    return refused("It holds Halcyonic's own data, such as its access token and history.");
  }
  const [first = ''] = segments(home, real) ?? [];
  if (first.startsWith('.') || first === 'Library') {
    return refused('Apps keep their settings and keys there.');
  }
  let owner: number | undefined;
  let mode = 0;
  try {
    const stats = statSync(real);
    owner = stats.uid;
    mode = stats.mode;
  } catch {
    owner = undefined;
  }
  if (owner !== undefined && context.uid !== undefined && owner !== context.uid) {
    return refused('It belongs to another user of this Mac.');
  }
  if ((mode & 0o002) !== 0) return refused('Any user of this Mac can change what is in it.');
  if (PERSONAL_FOLDERS.includes(first) && segments(home, real)?.length === 1) {
    const iCloud =
      first === 'Desktop' || first === 'Documents'
        ? ' If iCloud keeps your Desktop and Documents, what agents change there also goes to iCloud.'
        : '';
    return {
      verdict: 'broad',
      reason: `It holds much more than projects, and agents could change any of it.${iCloud}`,
    };
  }
  return { verdict: 'fine' };
}

function refused(reason: string): FolderAssessment {
  return { verdict: 'refused', reason };
}

/**
 * A folder made inside a user's own temporary or cache folder, `/private/var/folders/xx/yyy/T`
 * (or `C`, `0`), as a test or a scratch run makes, but never those folders themselves.
 */
function insideOwnTemporaryFolder(real: string): boolean {
  const parts = segments('/private/var/folders', real);
  return parts !== null && parts.length >= 4 && ['T', 'C', '0'].includes(parts[2] ?? '');
}

/** The path segments of `candidate` below `root`, or null when it is not inside it. */
function segments(root: string, candidate: string): string[] | null {
  const path = relative(root, candidate);
  if (path === '') return [];
  if (path === '..' || path.startsWith(`..${sep}`) || isAbsolute(path)) return null;
  return path.split(sep);
}

/** Whether `candidate` is `root` or lies inside it. */
export function within(root: string, candidate: string): boolean {
  return segments(root, candidate) !== null;
}

/** A path's real path, or the path itself, resolved, when it is not there. */
export function realOrSelf(path: string): string {
  try {
    return realpathSync.native(path);
  } catch {
    return resolve(path);
  }
}
