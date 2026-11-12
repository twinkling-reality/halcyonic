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
import { TaskCopies } from './task-copies.ts';

// The system's git and cp, as the owner's Mac has them; a task's copy is an APFS clone (ADR 0045).
const GIT = '/usr/bin/git';
const PROJECT = '01920000-0000-7000-8000-00000000abcd';
const TASK = '01920000-0000-7000-8000-00000000ab01';

let root = '';
let folder = '';
let copies: TaskCopies;
let ids = 0;
const nextId = () => {
  ids += 1;
  return `01920000-0000-7000-8000-${ids.toString(16).padStart(12, '0')}`;
};

/** What the person is shown of a copy: its reading, whose tree an add may bring (ADR 0045). */
async function shown(made: { directory: string; baseTree: string }): Promise<string | null> {
  return (await copies.readChanges(PROJECT, TASK, made.directory, made.baseTree))?.tree ?? null;
}

/** Whether anything, a link that leads nowhere included, is at `path`. */
function lstatExists(path: string): boolean {
  try {
    lstatSync(path);
    return true;
  } catch {
    return false;
  }
}

function write(relative: string, content: string, mode?: number): void {
  const path = join(folder, relative);
  mkdirSync(join(path, '..'), { recursive: true });
  writeFileSync(path, content);
  if (mode !== undefined) chmodSync(path, mode);
}

beforeEach(() => {
  root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-copies-')));
  folder = join(root, 'projects', 'app');
  mkdirSync(folder, { recursive: true });
  const data = join(root, 'data');
  copies = new TaskCopies({
    directory: join(root, 'tasks'),
    store: new CheckpointStore({
      git: GIT,
      directory: join(data, 'checkpoints'),
      protect: [data],
    }),
    nextId,
    protect: [data],
  });
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe("a task's own copy (ADR 0045)", { skip: process.platform !== 'darwin' }, () => {
  test('a plain folder is cloned whole, links and modes kept, and its base is the copy as made', async () => {
    write('src/main.ts', 'export const a = 1;\n');
    write('run.sh', '#!/bin/sh\n', 0o755);
    write('.gitignore', 'node_modules\n');
    const shared = join(root, 'shared', 'node_modules');
    mkdirSync(join(shared, 'pkg'), { recursive: true });
    writeFileSync(join(shared, 'pkg', 'index.js'), 'shared\n');
    symlinkSync(shared, join(folder, 'node_modules'), 'dir');

    const made = await copies.prepare(PROJECT, TASK, folder);
    assert.ok(made.ok, made.ok ? '' : `${made.code}: ${made.message}`);
    assert.equal(made.directory, join(root, 'tasks', PROJECT, TASK, 'app'));
    assert.match(made.baseTree, /^[0-9a-f]{64}$/);
    assert.equal(made.files, 3, 'the ignored link is copied, never kept in the base');
    assert.equal(
      readFileSync(join(made.directory, 'src/main.ts'), 'utf8'),
      'export const a = 1;\n',
    );
    assert.equal(statSync(join(made.directory, 'run.sh')).mode & 0o777, 0o755);
    assert.equal(readlinkSync(join(made.directory, 'node_modules')), shared);
    assert.equal(existsSync(join(folder, '.git')), false, 'nothing is written to the project');

    // The copy is the task's alone: a change in it never reaches the project.
    writeFileSync(join(made.directory, 'src/main.ts'), 'export const a = 2;\n');
    assert.equal(readFileSync(join(folder, 'src/main.ts'), 'utf8'), 'export const a = 1;\n');
  });

  test("a git project's copy has its own repository: a commit in it stays there", async () => {
    write('a.txt', 'one\n');
    const git = (cwd: string, ...args: string[]) =>
      execFileSync(GIT, ['-c', 'user.name=t', '-c', 'user.email=t@t', ...args], {
        cwd,
        env: { PATH: '/usr/bin:/bin', HOME: root, GIT_CONFIG_NOSYSTEM: '1' },
      })
        .toString()
        .trim();
    git(folder, 'init', '-q');
    git(folder, 'add', 'a.txt');
    git(folder, 'commit', '-qm', 'one');
    const made = await copies.prepare(PROJECT, TASK, folder);
    assert.ok(made.ok, made.ok ? '' : made.code);
    writeFileSync(join(made.directory, 'b.txt'), 'two\n');
    git(made.directory, 'add', 'b.txt');
    git(made.directory, 'commit', '-qm', 'two');
    assert.equal(git(folder, 'rev-list', '--count', 'HEAD'), '1');
    assert.equal(git(made.directory, 'rev-list', '--count', 'HEAD'), '2');
    assert.equal(existsSync(join(folder, 'b.txt')), false);
  });

  test("a copy keeps no token of the person's in its git settings: addresses lose their user part, headers, helpers and includes go, in the copy only", async () => {
    write('a.txt', 'one\n');
    const env = { PATH: '/usr/bin:/bin', HOME: root, GIT_CONFIG_NOSYSTEM: '1' };
    const git = (cwd: string, ...args: string[]) =>
      execFileSync(GIT, args, { cwd, env }).toString().trim();
    git(folder, 'init', '-q');
    git(
      folder,
      'remote',
      'add',
      'origin',
      'https://person:secret-token@example.invalid/team/app.git',
    );
    git(
      folder,
      'config',
      'remote.origin.pushurl',
      'https://other-token@example.invalid/team/app.git',
    );
    git(folder, 'remote', 'add', 'ssh', 'git@example.invalid:team/app.git');
    git(
      folder,
      'config',
      'url.https://ci-token@example.invalid/.insteadOf',
      'https://example.invalid/',
    );
    const set = (key: string, value: string) => git(folder, 'config', key, value);
    set('url.https://example.invalid/mirror/.insteadOf', 'https://value-token@example.invalid/');
    set('http.https://example.invalid/.extraHeader', 'Authorization: Bearer header-token');
    set('http.extraHeader', 'Authorization: Basic plain-token');
    set('credential.helper', '!f() { echo password=helper-token; }; f');
    set('credential.https://example.invalid.username', 'person');
    set('include.path', '/elsewhere/with-token.gitconfig');
    set('includeIf.gitdir:/elsewhere/.path', '/elsewhere/other-token.gitconfig');
    set('http.proxy', 'http://proxy-user:proxy-token@proxy.invalid:3128');
    set('submodule.lib.url', 'https://sub-token@example.invalid/lib.git');
    set('lfs.url', 'https://lfs-token@example.invalid/team/app.git/info/lfs');
    set('user.name', 'Person');
    const made = await copies.prepare(PROJECT, TASK, folder);
    assert.ok(made.ok, made.ok ? '' : made.code);
    const copied = readFileSync(join(made.directory, '.git', 'config'), 'utf8');
    assert.doesNotMatch(copied, /token/);
    assert.equal(
      git(made.directory, 'config', 'remote.origin.url'),
      'https://example.invalid/team/app.git',
    );
    assert.equal(
      git(made.directory, 'config', 'remote.origin.pushurl'),
      'https://example.invalid/team/app.git',
    );
    assert.equal(
      git(made.directory, 'config', 'remote.ssh.url'),
      'git@example.invalid:team/app.git',
    );
    assert.equal(git(made.directory, 'config', 'http.proxy'), 'http://proxy.invalid:3128');
    assert.doesNotMatch(copied, /extraheader|credential|include|helper/i);
    // What carries no token stays.
    assert.equal(git(made.directory, 'config', 'user.name'), 'Person');
    // The person's own repository is untouched.
    assert.match(readFileSync(join(folder, '.git', 'config'), 'utf8'), /secret-token/);
  });

  test('a folder whose .git is a file is refused: its copy would act on the repository it points to', async () => {
    write('a.txt', 'one\n');
    writeFileSync(join(folder, '.git'), 'gitdir: /elsewhere/.git/worktrees/app\n');
    const refused = await copies.prepare(PROJECT, TASK, folder);
    assert.equal(refused.ok ? null : refused.code, 'linked_repository');
    assert.equal(existsSync(join(root, 'tasks', PROJECT, TASK, 'app')), false);
  });

  test('a folder holding Halcyonic data, or the copies themselves, is refused before anything is copied', async () => {
    const holder = join(root, 'holder');
    mkdirSync(join(holder, 'data-inside'), { recursive: true });
    const guarded = new TaskCopies({
      directory: join(holder, 'tasks'),
      store: new CheckpointStore({ git: GIT, directory: join(holder, 'unused') }),
      nextId,
    });
    const refused = await guarded.prepare(PROJECT, TASK, holder);
    assert.equal(refused.ok ? null : refused.code, 'holds_host_data');
  });

  test('a folder the store will not keep whole is refused, and its copy removed', async () => {
    write('a.txt', 'one\n');
    const nested = join(folder, 'vendor', 'lib');
    mkdirSync(nested, { recursive: true });
    writeFileSync(join(nested, 'x.txt'), 'x\n');
    execFileSync(GIT, ['init', '-q'], { cwd: nested, env: { PATH: '/usr/bin:/bin', HOME: root } });
    execFileSync(GIT, ['-c', 'user.name=t', '-c', 'user.email=t@t', 'add', 'x.txt'], {
      cwd: nested,
      env: { PATH: '/usr/bin:/bin', HOME: root },
    });
    execFileSync(GIT, ['-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-qm', 'x'], {
      cwd: nested,
      env: { PATH: '/usr/bin:/bin', HOME: root },
    });
    const refused = await copies.prepare(PROJECT, TASK, folder);
    assert.equal(refused.ok ? null : refused.code, 'nested_repository');
    assert.equal(existsSync(join(root, 'tasks', PROJECT, TASK, 'app')), false);
  });

  test('a copy an earlier attempt left without recording it is replaced, never kept', async () => {
    write('a.txt', 'one\n');
    const stale = join(root, 'tasks', PROJECT, TASK, 'app');
    mkdirSync(stale, { recursive: true });
    writeFileSync(join(stale, 'left.txt'), 'from before\n');
    const made = await copies.prepare(PROJECT, TASK, folder);
    assert.ok(made.ok);
    assert.equal(existsSync(join(made.directory, 'left.txt')), false);
    assert.equal(lstatSync(join(made.directory, 'a.txt')).isFile(), true);
  });

  test('an add stops, writing nothing, when the person changes the project after it was read', async () => {
    write('a.txt', 'one\n');
    const made = await copies.prepare(PROJECT, TASK, folder);
    assert.ok(made.ok);
    writeFileSync(join(made.directory, 'b.txt'), 'from the task\n');
    const outcome = await copies.add(
      PROJECT,
      TASK,
      { copy: made.directory, base: made.baseTree, folder, reviewed: await shown(made) },
      async () => {
        // The person's edit lands between the read and the write.
        writeFileSync(join(folder, 'a.txt'), 'two\n');
        return '01920000-0000-7000-8000-0000000000c0';
      },
    );
    assert.equal(outcome.kind === 'failed' && outcome.code, 'folder_changed');
    assert.equal(outcome.kind === 'failed' && outcome.written, false);
    assert.equal(readFileSync(join(folder, 'a.txt'), 'utf8'), 'two\n');
    assert.equal(existsSync(join(folder, 'b.txt')), false);
    // Read again, the add goes through.
    const again = await copies.add(
      PROJECT,
      TASK,
      { copy: made.directory, base: made.baseTree, folder, reviewed: await shown(made) },
      async () => '01920000-0000-7000-8000-0000000000c1',
    );
    assert.equal(again.kind, 'added');
    assert.equal(readFileSync(join(folder, 'b.txt'), 'utf8'), 'from the task\n');
    assert.equal(readFileSync(join(folder, 'a.txt'), 'utf8'), 'two\n');
  });

  test('an add never brings a link the task made that points outside the project, and says so; one inside it comes as a link', async () => {
    write('a.txt', 'one\n');
    const made = await copies.prepare(PROJECT, TASK, folder);
    assert.ok(made.ok);
    // A task's command can make links in its copy: to the person's home, to the project's own folder.
    symlinkSync('/etc/hosts', join(made.directory, 'home-link'));
    symlinkSync('../../escape', join(made.directory, 'up'));
    symlinkSync('a.txt', join(made.directory, 'inside'));
    const refused = await copies.add(
      PROJECT,
      TASK,
      { copy: made.directory, base: made.baseTree, folder, reviewed: await shown(made) },
      async () => '01920000-0000-7000-8000-0000000000d0',
    );
    assert.equal(refused.kind === 'failed' && refused.code, 'link_outside');
    assert.match(
      refused.kind === 'failed' ? refused.message : '',
      /a link to a place outside the project \(home-link and 1 more\)/,
    );
    assert.equal(refused.kind === 'failed' && refused.written, false);
    for (const name of ['home-link', 'up', 'inside'])
      assert.equal(existsSync(join(folder, name)) || lstatExists(join(folder, name)), false, name);
    // The task removes them; the add goes through, and the inside link arrives as a link.
    rmSync(join(made.directory, 'home-link'));
    rmSync(join(made.directory, 'up'));
    const added = await copies.add(
      PROJECT,
      TASK,
      { copy: made.directory, base: made.baseTree, folder, reviewed: await shown(made) },
      async () => '01920000-0000-7000-8000-0000000000d1',
    );
    assert.equal(added.kind, 'added');
    assert.equal(readlinkSync(join(folder, 'inside')), 'a.txt');
  });

  test('an add brings only what the person was shown: a file written after it, by anyone, stops it (AUDIT2)', async () => {
    write('a.txt', 'one\n');
    const made = await copies.prepare(PROJECT, TASK, folder);
    assert.ok(made.ok);
    writeFileSync(join(made.directory, 'b.txt'), 'reviewed\n');
    const read = await copies.readChanges(PROJECT, TASK, made.directory, made.baseTree);
    assert.deepEqual(read?.changed, ['b.txt']);
    // Another writer adds a file the person never saw.
    writeFileSync(join(made.directory, 'c.txt'), 'never shown\n');
    const outcome = await copies.add(
      PROJECT,
      TASK,
      { copy: made.directory, base: made.baseTree, folder, reviewed: read?.tree ?? null },
      async () => assert.fail('nothing is kept or written for a copy the person was not shown'),
    );
    assert.equal(outcome.kind, 'unreviewed');
    assert.deepEqual(outcome.kind === 'unreviewed' && outcome.changed, ['b.txt', 'c.txt']);
    assert.deepEqual(outcome.kind === 'unreviewed' && outcome.files, [
      { path: 'b.txt', change: 'added' },
      { path: 'c.txt', change: 'added' },
    ]);
    assert.equal(existsSync(join(folder, 'b.txt')), false);
    assert.equal(existsSync(join(folder, 'c.txt')), false);
    // With nothing ever shown, nothing is added either.
    const never = await copies.add(
      PROJECT,
      TASK,
      { copy: made.directory, base: made.baseTree, folder, reviewed: null },
      async () => assert.fail('never'),
    );
    assert.equal(never.kind, 'unreviewed');
  });

  test('a reading says how each file changed: added, changed or removed', async () => {
    write('kept.txt', 'one\n');
    write('gone.txt', 'two\n');
    write('src/edit.ts', 'export {};\n');
    const made = await copies.prepare(PROJECT, TASK, folder);
    assert.ok(made.ok);
    writeFileSync(join(made.directory, 'src/edit.ts'), 'export const a = 1;\n');
    rmSync(join(made.directory, 'gone.txt'));
    mkdirSync(join(made.directory, 'art'));
    writeFileSync(join(made.directory, 'art/new.svg'), '<svg/>\n');
    const read = await copies.readChanges(PROJECT, TASK, made.directory, made.baseTree);
    assert.deepEqual(read?.files, [
      { path: 'art/new.svg', change: 'added' },
      { path: 'gone.txt', change: 'removed' },
      { path: 'src/edit.ts', change: 'changed' },
    ]);
    assert.deepEqual(read?.changed, ['art/new.svg', 'gone.txt', 'src/edit.ts']);
  });

  test('an add never goes through without its checkpoint of the project: nothing is written', async () => {
    write('a.txt', 'one\n');
    const made = await copies.prepare(PROJECT, TASK, folder);
    assert.ok(made.ok);
    writeFileSync(join(made.directory, 'b.txt'), 'from the task\n');
    const outcome = await copies.add(
      PROJECT,
      TASK,
      { copy: made.directory, base: made.baseTree, folder, reviewed: await shown(made) },
      async () => null,
    );
    assert.equal(outcome.kind === 'failed' && outcome.code, 'before_add_failed');
    assert.equal(outcome.kind === 'failed' && outcome.written, false);
    assert.equal(existsSync(join(folder, 'b.txt')), false);
  });

  test('a binary file both changed is named as binary, and a file one deleted as deleted', async () => {
    write('img.bin', 'base');
    write('gone.txt', 'keep\n');
    const made = await copies.prepare(PROJECT, TASK, folder);
    assert.ok(made.ok);
    writeFileSync(join(made.directory, 'img.bin'), Buffer.from([0, 1, 2, 3, 0]));
    writeFileSync(join(folder, 'img.bin'), Buffer.from([0, 9, 9, 9, 0]));
    rmSync(join(made.directory, 'gone.txt'));
    writeFileSync(join(folder, 'gone.txt'), 'changed\n');
    const outcome = await copies.add(
      PROJECT,
      TASK,
      { copy: made.directory, base: made.baseTree, folder, reviewed: await shown(made) },
      async () => assert.fail('nothing is kept or written when they met'),
    );
    assert.equal(outcome.kind, 'conflicted');
    if (outcome.kind !== 'conflicted') return;
    assert.deepEqual(
      [...outcome.conflicts].sort((a, b) => a.path.localeCompare(b.path)),
      [
        { path: 'gone.txt', kind: 'deleted' },
        { path: 'img.bin', kind: 'binary' },
      ],
    );
  });

  test('a count of what the folder holds that the journal does not removes none of it (ADR 0045, amended)', async () => {
    write('a.txt', 'one\n');
    const OTHER = '01920000-0000-7000-8000-00000000ab02';
    const GONE_PROJECT = '01920000-0000-7000-8000-00000000dead';
    const kept = await copies.prepare(PROJECT, TASK, folder);
    const gone = await copies.prepare(PROJECT, OTHER, folder);
    const elsewhere = await copies.prepare(GONE_PROJECT, TASK, folder);
    assert.ok(kept.ok && gone.ok && elsewhere.ok);
    const tasks = join(root, 'tasks');
    // What a sweep cannot tell is a copy: a stray file, a folder not named by ids, a link out.
    writeFileSync(join(tasks, 'notes.txt'), 'mine\n');
    mkdirSync(join(tasks, 'not-an-id'));
    const outside = join(root, 'outside');
    mkdirSync(outside);
    writeFileSync(join(outside, 'keep.txt'), 'keep\n');
    symlinkSync(outside, join(tasks, PROJECT, '01920000-0000-7000-8000-00000000ab03'));

    const found = await copies.unheld(new Set([kept.directory]));
    assert.deepEqual(found, { unheld: 2, other: 3 });
    assert.equal(existsSync(kept.directory), true);
    // Another plane's copy, or a task's unadded work an older journal does not name: never removed.
    assert.equal(existsSync(gone.directory), true);
    assert.equal(existsSync(elsewhere.directory), true);
    assert.equal(
      readFileSync(join(outside, 'keep.txt'), 'utf8'),
      'keep\n',
      'a link is never followed',
    );
    assert.equal(existsSync(join(tasks, 'notes.txt')), true);
    assert.equal(existsSync(join(tasks, 'not-an-id')), true);
    assert.equal(
      readFileSync(join(folder, 'a.txt'), 'utf8'),
      'one\n',
      'the project is never touched',
    );
  });

  test('a missing folder is refused', async () => {
    const refused = await copies.prepare(PROJECT, TASK, join(root, 'gone'));
    assert.equal(refused.ok ? null : refused.code, 'location_missing');
  });
});
