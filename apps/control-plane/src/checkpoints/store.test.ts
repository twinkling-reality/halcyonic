import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import {
  chmodSync,
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readlinkSync,
  realpathSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { CheckpointStore } from './store.ts';

// The system's git, as the owner's Mac has it; the tests need a real one.
const GIT = '/usr/bin/git';

let root = '';
let folder = '';
let outside = '';
let store: CheckpointStore;
let ids = 0;
const nextId = () => {
  ids += 1;
  return `01920000-0000-7000-8000-${ids.toString(16).padStart(12, '0')}`;
};
const PROJECT = '01920000-0000-7000-8000-00000000abcd';

function write(relative: string, content: string | Buffer, mode?: number): void {
  const path = join(folder, relative);
  mkdirSync(join(path, '..'), { recursive: true });
  writeFileSync(path, content);
  if (mode !== undefined) chmodSync(path, mode);
}

function read(relative: string): Buffer {
  return readFileSync(join(folder, relative));
}

async function take(keepAlso: readonly string[] = []): Promise<string> {
  const id = nextId();
  const taken = await store.take(PROJECT, folder, id, { keepAlso });
  assert.ok(taken.ok, taken.ok ? '' : `${taken.code}: ${taken.message}`);
  return id;
}

beforeEach(() => {
  root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-checkpoints-')));
  folder = join(root, 'project');
  outside = join(root, 'outside');
  mkdirSync(folder);
  mkdirSync(outside);
  writeFileSync(join(outside, 'secret.txt'), 'outside\n');
  store = new CheckpointStore({ git: GIT, directory: join(root, 'store') });
  write('.gitignore', 'Library/\n*.log\n');
  write('src/a.txt', 'one\n');
  write('src/run.sh', '#!/bin/sh\necho hi\n', 0o755);
  write('src/keep.md', 'keep\n');
  write('Library/cache.bin', 'cache\n');
  write('debug.log', 'log\n');
  symlinkSync('a.txt', join(folder, 'src/link'));
});

afterEach(() => {
  // A test may leave a folder unwritable; give it back so it can be removed.
  if (existsSync(join(folder, 'locked'))) chmodSync(join(folder, 'locked'), 0o755);
  rmSync(root, { recursive: true, force: true });
});

describe('taking a checkpoint', () => {
  test('keeps every file the project does not ignore, and counts them', async () => {
    const id = nextId();
    const taken = await store.take(PROJECT, folder, id);
    assert.ok(taken.ok);
    assert.match(taken.tree, /^[0-9a-f]{64}$/);
    // .gitignore, src/a.txt, src/run.sh, src/keep.md, src/link
    assert.equal(taken.files, 5);
    assert.equal(taken.projectHead, null);
  });

  test('refuses a folder holding another repository, and keeps nothing', async () => {
    mkdirSync(join(folder, 'nested'));
    execFileSync(GIT, ['init', '-q', join(folder, 'nested')]);
    write('nested/n.txt', 'n\n');
    const taken = await store.take(PROJECT, folder, nextId());
    assert.equal(taken.ok, false);
    assert.equal(!taken.ok && taken.code, 'nested_repository');
  });

  test('refuses more files than the limit', async () => {
    const small = new CheckpointStore({
      git: GIT,
      directory: join(root, 'store-small'),
      limits: { files: 3, bytes: 1_000_000 },
    });
    const taken = await small.take(PROJECT, folder, nextId());
    assert.equal(!taken.ok && taken.code, 'too_large');
  });

  test('refuses a folder that is not there', async () => {
    const taken = await store.take(PROJECT, join(root, 'gone'), nextId());
    assert.equal(!taken.ok && taken.code, 'location_missing');
  });

  test('a git that cannot start is a store failure, not a crash', async () => {
    const broken = new CheckpointStore({
      git: join(root, 'no-git'),
      directory: join(root, 'store-x'),
    });
    const taken = await broken.take(PROJECT, folder, nextId());
    assert.equal(!taken.ok && taken.code, 'store_failed');
  });

  test('reads the project’s own commit from its files', async () => {
    execFileSync(GIT, ['init', '-q', '-b', 'main', folder]);
    execFileSync(
      GIT,
      [
        '-C',
        folder,
        '-c',
        'user.name=t',
        '-c',
        'user.email=t@t',
        'commit',
        '-q',
        '--allow-empty',
        '-m',
        'x',
      ],
      {
        env: { PATH: '/usr/bin:/bin', GIT_CONFIG_GLOBAL: '/dev/null', GIT_CONFIG_NOSYSTEM: '1' },
      },
    );
    const head = execFileSync(GIT, ['-C', folder, 'rev-parse', 'HEAD']).toString().trim();
    const id = nextId();
    const taken = await store.take(PROJECT, folder, id);
    assert.ok(taken.ok);
    assert.equal(taken.projectHead, head);
  });
});

describe('putting a checkpoint back', () => {
  test('puts changed, removed and added files back exactly, and leaves what was ignored', async () => {
    const id = await take();
    write('src/a.txt', 'two\n');
    rmSync(join(folder, 'src/keep.md'));
    write('src/new.ts', 'added\n');
    chmodSync(join(folder, 'src/run.sh'), 0o644);
    rmSync(join(folder, 'src/link'));
    symlinkSync('keep.md', join(folder, 'src/link'));
    write('Library/other.bin', 'still ignored\n');

    const preview = await store.preview(PROJECT, folder, id);
    assert.ok(preview.ok);
    assert.deepEqual(preview.changedBack, ['src/a.txt', 'src/link', 'src/run.sh']);
    assert.deepEqual(preview.returned, ['src/keep.md']);
    assert.deepEqual(preview.removed, ['src/new.ts']);

    const restored = await store.restore(PROJECT, folder, id);
    assert.ok(restored.ok, restored.ok ? '' : restored.message);
    assert.equal(restored.compared, 5);
    assert.equal(read('src/a.txt').toString(), 'one\n');
    assert.equal(read('src/keep.md').toString(), 'keep\n');
    assert.equal(existsSync(join(folder, 'src/new.ts')), false);
    assert.equal(statSync(join(folder, 'src/run.sh')).mode & 0o111, 0o111);
    assert.equal(readlinkSync(join(folder, 'src/link')), 'a.txt');
    assert.equal(read('Library/other.bin').toString(), 'still ignored\n');
    assert.equal(read('debug.log').toString(), 'log\n');
  });

  test('removes files hidden by an ignore file the checkpoint did not have', async () => {
    const id = await take();
    write('src/gen/hidden.js', 'hidden\n');
    write('src/gen/.gitignore', '*\n');
    const restored = await store.restore(PROJECT, folder, id);
    assert.ok(restored.ok, restored.ok ? '' : restored.message);
    assert.equal(existsSync(join(folder, 'src/gen')), false);
  });

  test('a deleted ignore file does not turn what it ignored into files to remove', async () => {
    const id = await take();
    rmSync(join(folder, '.gitignore'));
    const restored = await store.restore(PROJECT, folder, id);
    assert.ok(restored.ok, restored.ok ? '' : restored.message);
    assert.equal(read('Library/cache.bin').toString(), 'cache\n');
    assert.equal(read('.gitignore').toString(), 'Library/\n*.log\n');
  });

  test('keeps bytes exact whatever the project’s attributes ask for', async () => {
    write('.gitattributes', '*.txt text eol=crlf\n');
    const id = await take();
    write('src/a.txt', 'changed\n');
    const restored = await store.restore(PROJECT, folder, id);
    assert.ok(restored.ok);
    assert.deepEqual(read('src/a.txt'), Buffer.from('one\n'));
  });

  test('never writes through a link: a folder replaced by a link outside is put back as a folder', async () => {
    const id = await take();
    rmSync(join(folder, 'src'), { recursive: true });
    symlinkSync(outside, join(folder, 'src'));
    const restored = await store.restore(PROJECT, folder, id);
    assert.ok(restored.ok, restored.ok ? '' : restored.message);
    assert.equal(lstatSync(join(folder, 'src')).isDirectory(), true);
    assert.equal(read('src/a.txt').toString(), 'one\n');
    assert.equal(readFileSync(join(outside, 'secret.txt'), 'utf8'), 'outside\n');
    assert.equal(existsSync(join(outside, 'a.txt')), false);
  });

  test('one byte different after writing means the restore does not complete', async () => {
    const id = await take();
    write('src/a.txt', 'two\n');
    const restored = await store.restore(PROJECT, folder, id, {
      onWritten: () => write('src/a.txt', 'onf\n'),
    });
    assert.equal(restored.ok, false);
    assert.equal(!restored.ok && restored.written, true);
    assert.deepEqual(!restored.ok && restored.notRestored, ['src/a.txt']);
  });

  test('a file that cannot be written makes the restore partial, and says which', async () => {
    write('locked/l.txt', 'before\n');
    const id = await take();
    write('locked/l.txt', 'after\n');
    write('src/a.txt', 'two\n');
    chmodSync(join(folder, 'locked/l.txt'), 0o444);
    chmodSync(join(folder, 'locked'), 0o555);
    const restored = await store.restore(PROJECT, folder, id);
    assert.equal(restored.ok, false);
    assert.equal(!restored.ok && restored.written, true);
    assert.ok(!restored.ok && restored.notRestored.includes('locked/l.txt'));
  });

  test('a checkpoint taken with files to keep brings an ignored file back', async () => {
    const id = await take();
    write('.env', 'TOKEN=person\n');
    write('.gitignore', 'Library/\n*.log\n.env\n');
    const before = await take(['.env']);
    await store.restore(PROJECT, folder, id);
    const back = await store.restore(PROJECT, folder, before);
    assert.ok(back.ok, back.ok ? '' : back.message);
    assert.equal(read('.env').toString(), 'TOKEN=person\n');
  });

  test('a checkpoint of another folder is refused', async () => {
    const id = await take();
    const elsewhere = join(root, 'elsewhere');
    mkdirSync(elsewhere);
    const restored = await store.restore(PROJECT, elsewhere, id);
    assert.equal(restored.ok, false);
    assert.equal(!restored.ok && restored.written, false);
    assert.equal(!restored.ok && restored.code, 'location_moved');
  });

  test('an unknown checkpoint is refused before anything is written', async () => {
    await take();
    const restored = await store.restore(PROJECT, folder, nextId());
    assert.equal(!restored.ok && restored.code, 'checkpoint_missing');
    assert.equal(!restored.ok && restored.written, false);
  });
});

describe('checking a folder against a checkpoint', () => {
  test('finds a change after the fact, and nothing when equal', async () => {
    const id = await take();
    assert.deepEqual(await store.verify(PROJECT, folder, id), { compared: 5, differing: [] });
    write('src/keep.md', 'keeq\n');
    assert.deepEqual((await store.verify(PROJECT, folder, id)).differing, ['src/keep.md']);
  });
});
