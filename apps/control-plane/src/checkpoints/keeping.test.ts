import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readdirSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { ProjectKeeping } from './keeping.ts';
import { CheckpointStore } from './store.ts';

// Keeping what was made (F3): the system's git, a scratch folder as the Mac's Downloads.

const GIT = '/usr/bin/git';
const PROJECT = '01920000-0000-7000-8000-00000000cafe';

let root = '';
let folder = '';
let downloads = '';
let revealed: string[] = [];
let keeping: ProjectKeeping;

function write(relative: string, content: string): void {
  const path = join(folder, relative);
  mkdirSync(join(path, '..'), { recursive: true });
  writeFileSync(path, content);
}

function zipped(file: string): string[] {
  return execFileSync('/usr/bin/unzip', ['-Z1', join(downloads, file)])
    .toString()
    .split('\n')
    .filter((line) => line !== '' && !line.endsWith('/'))
    .sort();
}

beforeEach(() => {
  root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-keeping-')));
  folder = join(root, 'projects', 'garden');
  downloads = join(root, 'Downloads');
  mkdirSync(folder, { recursive: true });
  mkdirSync(downloads);
  revealed = [];
  keeping = new ProjectKeeping({
    store: new CheckpointStore({ git: GIT, directory: join(root, 'data', 'checkpoints') }),
    downloads,
    now: () => new Date(2026, 10, 11, 9, 5),
    reveal: async (path) => {
      revealed.push(path);
      return true;
    },
  });
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe("keeping a project's work (F3)", { skip: process.platform !== 'darwin' }, () => {
  test('a saved copy holds the files a checkpoint keeps, never .git, ignored files, the files the secret-file list names or another project', async () => {
    write('src/game.ts', 'export {};\n');
    write('index.html', '<p>hi</p>\n');
    write('.gitignore', 'node_modules\ndist\n');
    write('node_modules/pkg/index.js', 'x\n');
    write('dist/out.js', 'built\n');
    write('.env', 'KEY=secret\n');
    write('src/deep/.env.local', 'KEY=secret\n');
    write('certs/server.pem', 'pem\n');
    write('.npmrc', '//registry/:_authToken=t\n');
    write('notes.env.md', 'not a secret file\n');
    // Whatever the case of their names (AUDIT2): the disk here does not tell them apart.
    write('deploy/.ENV', 'KEY=secret\n');
    write('KEY.PEM', 'pem\n');
    write('aws/credentials', '[default]\n');
    write('config/Secrets.json', '{}\n');
    write('android/release.keystore', 'ks\n');
    write('certs/app.JKS', 'ks\n');
    write('home/id_dsa', 'key\n');
    write('home/id_ed25519.pub', 'key\n');
    write('vr/README.md', 'another project here\n');
    execFileSync(GIT, ['init', '-q'], { cwd: folder, env: { PATH: '/usr/bin:/bin', HOME: root } });

    const saved = await keeping.save(PROJECT, folder, [
      join(folder, 'vr'),
      join(root, 'elsewhere'),
    ]);
    assert.ok(saved.ok, saved.ok ? '' : `${saved.code}: ${saved.message}`);
    assert.equal(saved.file, 'garden 2026-11-11 09-05.zip');
    assert.deepEqual(zipped(saved.file), [
      '.gitignore',
      'index.html',
      'notes.env.md',
      'src/game.ts',
    ]);
    assert.equal(saved.files, 4);
    assert.ok(saved.bytes > 0);
    assert.deepEqual(revealed, [join(downloads, saved.file)], 'shown in Finder');
  });

  test('a second copy never replaces the first, and a missing folder or Downloads is said plainly', async () => {
    write('a.txt', 'one\n');
    const first = await keeping.save(PROJECT, folder, []);
    const second = await keeping.save(PROJECT, folder, []);
    assert.ok(first.ok && second.ok);
    assert.equal(second.file, 'garden 2026-11-11 09-05 2.zip');
    assert.equal(readdirSync(downloads).length, 2);
    const gone = await keeping.save(PROJECT, join(root, 'gone'), []);
    assert.equal(gone.ok ? null : gone.code, 'location_missing');
    rmSync(downloads, { recursive: true });
    const nowhere = await keeping.save(PROJECT, folder, []);
    assert.equal(nowhere.ok ? null : nowhere.code, 'downloads_missing');
  });

  test("Show in Finder shows the project's own folder", async () => {
    assert.deepEqual(await keeping.show(folder), { ok: true });
    assert.deepEqual(revealed, [folder]);
    const gone = await keeping.show(join(root, 'gone'));
    assert.equal(gone.ok ? null : gone.code, 'location_missing');
  });
});
