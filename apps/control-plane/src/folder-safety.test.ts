import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, mkdtempSync, realpathSync, rmSync } from 'node:fs';
import { tmpdir, userInfo } from 'node:os';
import { dirname, join } from 'node:path';
import { after, describe, test } from 'node:test';
import { ConfigError, loadConfig } from './config.ts';
import { assessFolder, type FolderContext } from './folder-safety.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-folders-')));
after(() => rmSync(base, { recursive: true, force: true }));

const someone: FolderContext = {
  home: '/Users/someone',
  dataDir: '/Users/someone/.halcyonic',
  uid: undefined,
};

function verdict(path: string, context: FolderContext = someone): string {
  return assessFolder(path, context).verdict;
}

describe('folders that may hold projects', () => {
  test('never the disk, a shared folder, or what belongs to macOS, its apps or programs on the PATH', () => {
    for (const path of [
      '/',
      '/Users',
      '/Volumes',
      '/private',
      '/private/var',
      '/private/tmp',
      '/var',
      '/tmp',
      '/opt',
      '/System/Library',
      '/Library/Preferences',
      '/Applications',
      '/Applications/Xcode.app',
      '/usr/local',
      '/usr/local/bin',
      '/etc',
      '/private/etc',
      '/private/etc/ssh',
      '/private/var/db',
      '/private/var/db/dslocal',
      '/private/var/root',
      '/opt/homebrew',
      '/opt/homebrew/bin',
      '/Users/Shared',
      '/Users/Shared/projects',
      '/dev',
    ]) {
      assert.equal(verdict(path), 'refused', path);
    }
  });

  test("never a user's own temporary folder itself, or anything beside it, though a folder made inside it is fine", () => {
    for (const path of [
      '/private/var/folders',
      '/private/var/folders/x3',
      '/private/var/folders/x3/abc123',
      '/private/var/folders/x3/abc123/T',
      '/private/var/folders/x3/abc123/C',
      '/private/var/folders/x3/abc123/X',
      '/private/var/folders/x3/abc123/X/scratch',
      '/private/var/folders/x3/abc123/0/com.apple.LaunchServices',
      '/private/var/folders/x3/abc123/0/Store',
      '/private/var/folders/x3/abc123/C/com.apple.AudioComponentRegistrar',
    ]) {
      assert.equal(verdict(path), 'refused', path);
    }
    assert.equal(verdict('/private/var/folders/x3/abc123/T/scratch'), 'fine');
    assert.equal(verdict('/private/var/folders/x3/abc123/T/scratch/deeper'), 'fine');
  });

  test("never a whole drive, another person's folders, the home folder or one holding it", () => {
    for (const path of ['/Volumes/Work', '/Users/other', '/Users/other/dev', '/Users/someone']) {
      assert.equal(verdict(path), 'refused', path);
    }
    assert.equal(verdict('/Volumes/Work/projects'), 'fine');
    assert.equal(verdict('/Users/someone/dev'), 'fine');
    assert.equal(verdict('/Users/someone/HalcyonicProjects'), 'fine');
  });

  test("never Halcyonic's data, or where apps keep settings and keys", () => {
    for (const path of [
      '/Users/someone/.halcyonic',
      '/Users/someone/.halcyonic/runtimes',
      '/Users/someone/.ssh',
      '/Users/someone/.config/opencode',
      '/Users/someone/.codex',
      '/Users/someone/Library',
      '/Users/someone/Library/Mobile Documents',
    ]) {
      assert.equal(verdict(path), 'refused', path);
    }
    const elsewhere = { ...someone, dataDir: '/Users/someone/dev/halcyonic-data' };
    assert.equal(verdict('/Users/someone/dev', elsewhere), 'refused', 'a folder holding the data');
  });

  test('a personal folder such as Documents is allowed only after a second thought', () => {
    for (const folder of [
      'Desktop',
      'Documents',
      'Downloads',
      'Pictures',
      'Movies',
      'Music',
      'Public',
    ]) {
      assert.equal(verdict(`/Users/someone/${folder}`), 'broad', folder);
    }
    assert.equal(verdict('/Users/someone/Documents/code'), 'fine');
  });

  test('never a folder another user owns or any user can change', () => {
    const owned = join(base, 'owned');
    mkdirSync(owned);
    const uid = process.getuid?.();
    if (uid === undefined) return;
    const context = { home: join(base, 'home'), dataDir: join(base, 'data'), uid };
    assert.equal(verdict(owned, context), 'fine');
    assert.equal(verdict(owned, { ...context, uid: uid + 1 }), 'refused');
    const open = join(base, 'open');
    mkdirSync(open);
    chmodSync(open, 0o777);
    assert.equal(verdict(open, context), 'refused');
  });

  test('the control plane itself refuses such a root, taking the home folder from the account, not $HOME', () => {
    const projects = join(base, 'projects');
    mkdirSync(projects, { recursive: true });
    const data = join(base, 'data');
    const env = { HALCYONIC_DATA_DIR: data };
    assert.deepEqual(loadConfig({ ...env, HALCYONIC_PROJECT_ROOTS: projects }).projectRoots, [
      projects,
    ]);
    const home = userInfo().homedir;
    mkdirSync(data, { recursive: true });
    for (const root of [home, dirname(home), base, data, '/', '/etc', '/private/var/db']) {
      assert.throws(
        () => loadConfig({ ...env, HALCYONIC_PROJECT_ROOTS: root }),
        (error: unknown) =>
          error instanceof ConfigError && /can't hold projects/.test(error.message),
        root,
      );
    }
    // A launcher that changes $HOME changes neither which home is refused nor whose folders are whose.
    assert.throws(
      () => loadConfig({ ...env, HOME: '/', HALCYONIC_PROJECT_ROOTS: home }),
      ConfigError,
    );
    assert.deepEqual(
      loadConfig({ ...env, HOME: projects, HALCYONIC_PROJECT_ROOTS: projects }).projectRoots,
      [projects],
    );
  });
});
