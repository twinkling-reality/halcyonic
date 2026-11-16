import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { findTrustedGit, GIT_CANDIDATES, systemOwned, type TrustedGitDisk } from './trusted-git.ts';

// The git used where HALCYONIC_GIT_BIN is not set: only one the system owns, at a fixed place (AUDIT3 G1, G2).

const ROOT_DIR = { uid: 0, mode: 0o40755, isFile: () => false, isDirectory: () => true };
const ROOT_FILE = { uid: 0, mode: 0o100755, isFile: () => true, isDirectory: () => false };

/** A disk of entries by path, every folder of each path root-owned and closed unless given. */
function disk(
  files: Record<string, Partial<typeof ROOT_FILE>>,
  folders: Record<string, Partial<typeof ROOT_DIR>> = {},
  links: Record<string, string> = {},
): TrustedGitDisk {
  return {
    realpath: (path) => {
      const real = links[path] ?? path;
      return files[real] === undefined ? null : real;
    },
    lstat: (path) => {
      if (files[path] !== undefined) return { ...ROOT_FILE, ...files[path] };
      if (folders[path] !== undefined) return { ...ROOT_DIR, ...folders[path] };
      return Object.keys(files).some((file) => file.startsWith(path === '/' ? '/' : `${path}/`))
        ? ROOT_DIR
        : null;
    },
  };
}

const CLT = GIT_CANDIDATES[0] as string;
const XCODE = GIT_CANDIDATES[1] as string;

describe('the git the system owns (trusted-git)', () => {
  test("takes Apple's command line tools first, then Xcode's, and none from PATH, Homebrew or the shim", () => {
    assert.deepEqual(GIT_CANDIDATES, [
      '/Library/Developer/CommandLineTools/usr/bin/git',
      '/Applications/Xcode.app/Contents/Developer/usr/bin/git',
    ]);
    assert.equal(findTrustedGit(disk({ [CLT]: {}, [XCODE]: {} })), CLT);
    assert.equal(findTrustedGit(disk({ [XCODE]: {} })), XCODE);
    assert.equal(findTrustedGit(disk({ '/usr/bin/git': {}, '/opt/homebrew/bin/git': {} })), null);
  });

  test('refuses a program not owned by root, or one its group or the world may write', () => {
    assert.equal(systemOwned(CLT, disk({ [CLT]: { uid: 501 } })), false);
    assert.equal(systemOwned(CLT, disk({ [CLT]: { mode: 0o100775 } })), false);
    assert.equal(systemOwned(CLT, disk({ [CLT]: { mode: 0o100757 } })), false);
    assert.equal(systemOwned(CLT, disk({ [CLT]: {} })), true);
  });

  test('refuses it when any folder above it is not root-owned or is writable by others, as /Applications is', () => {
    // /Applications is root:admin drwxrwxr-x on a Mac: the admin group could move Xcode aside.
    const xcode = disk({ [XCODE]: {} }, { '/Applications': { mode: 0o40775 } });
    assert.equal(systemOwned(XCODE, xcode), false);
    assert.equal(findTrustedGit(xcode), null);
    assert.equal(
      systemOwned(CLT, disk({ [CLT]: {} }, { '/Library/Developer': { uid: 501 } })),
      false,
    );
    assert.equal(systemOwned(CLT, disk({ [CLT]: {} }, { '/': { mode: 0o40777 } })), false);
  });

  test('judges the program after every link, so a link to a program of the account is refused', () => {
    const linked = disk(
      { '/Users/someone/git': { uid: 501 } },
      {},
      { [CLT]: '/Users/someone/git' },
    );
    assert.equal(systemOwned(CLT, linked), false);
    const toRoot = disk(
      { '/usr/libexec/git-core/git': {} },
      {},
      { [CLT]: '/usr/libexec/git-core/git' },
    );
    assert.equal(
      findTrustedGit(toRoot),
      '/usr/libexec/git-core/git',
      'the real path is what is kept',
    );
  });

  test('a missing program, or a folder where the program should be, is none', () => {
    assert.equal(findTrustedGit(disk({})), null);
    assert.equal(systemOwned(CLT, disk({ [CLT]: { isFile: () => false } })), false);
  });
});
