import { lstatSync, realpathSync } from 'node:fs';
import { dirname } from 'node:path';

/**
 * Where a git the system owns is found on a Mac when `HALCYONIC_GIT_BIN` is not set (AUDIT3 G1, 2026-11-16), in this
 * order: Apple's Command Line Tools, then Xcode's own. Never PATH, never /opt/homebrew (a link the account owns,
 * which any process of the account could point elsewhere), and never /usr/bin/git: that is xcode-select's shim, which
 * runs the git of whichever developer folder xcode-select names, so judging the shim's own file says nothing about
 * what it runs.
 */
export const GIT_CANDIDATES: readonly string[] = [
  '/Library/Developer/CommandLineTools/usr/bin/git',
  '/Applications/Xcode.app/Contents/Developer/usr/bin/git',
];

/** What the finder reads of a path, so tests stand in for the disk. */
export interface TrustedGitDisk {
  /** The path after every link, or null when it does not resolve. */
  realpath(path: string): string | null;
  /** The entry itself (never following a link), or null when it is missing. */
  lstat(path: string): {
    readonly uid: number;
    readonly mode: number;
    isFile(): boolean;
    isDirectory(): boolean;
  } | null;
}

const DISK: TrustedGitDisk = {
  realpath: (path) => {
    try {
      return realpathSync.native(path);
    } catch {
      return null;
    }
  },
  lstat: (path) => {
    try {
      return lstatSync(path);
    } catch {
      return null;
    }
  },
};

/** Group or world may write it. */
const WRITABLE_BY_OTHERS = 0o022;

/**
 * Whether a program is one only the system could have put there (AUDIT3 G2): taken after every link, it is a file
 * owned by root that neither its group nor the world may write, and so is every folder above it up to /, so no
 * process of this account could swap it or a folder on its way. Read once; a later change does not reach a plane
 * already started.
 */
export function systemOwned(path: string, disk: TrustedGitDisk = DISK): boolean {
  const real = disk.realpath(path);
  if (real === null) return false;
  const program = disk.lstat(real);
  if (program === null || !program.isFile()) return false;
  if (program.uid !== 0 || (program.mode & WRITABLE_BY_OTHERS) !== 0) return false;
  for (let folder = dirname(real); ; folder = dirname(folder)) {
    const entry = disk.lstat(folder);
    if (entry === null || !entry.isDirectory()) return false;
    if (entry.uid !== 0 || (entry.mode & WRITABLE_BY_OTHERS) !== 0) return false;
    if (folder === dirname(folder)) return true;
  }
}

/** The first git of `GIT_CANDIDATES` the system owns, as its real path, or null when none is. */
export function findTrustedGit(
  disk: TrustedGitDisk = DISK,
  candidates: readonly string[] = GIT_CANDIDATES,
): string | null {
  for (const candidate of candidates) {
    if (systemOwned(candidate, disk)) return disk.realpath(candidate);
  }
  return null;
}
