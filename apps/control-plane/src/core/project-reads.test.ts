import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type {
  CheckpointId,
  CommandEnvelope,
  CommandId,
  ExecutionId,
  JournalId,
  Principal,
  ProjectId,
} from '@halcyonic/contracts';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { CheckpointStore } from '../checkpoints/store.ts';
import { createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { createHostLocations } from '../locations.ts';
import { capturingLogger } from '../testing/harness.ts';
import { ControlPlane } from './control-plane.ts';
import { NO_CAUSE } from './drafts.ts';
import { ProjectReads, withheldPath } from './project-reads.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-project-reads-')));
after(() => rmSync(base, { recursive: true, force: true }));
const LOCAL: Principal = { kind: 'local' };
const SECRET = 'sk-held-secret-value-0123456789';

function setup(name: string) {
  const root = join(base, name);
  const folder = join(root, 'app');
  mkdirSync(join(folder, 'src'), { recursive: true });
  mkdirSync(join(root, 'outside'), { recursive: true });
  writeFileSync(join(folder, 'src', 'main.ts'), 'const a = 1;\nconst b = 2;\n');
  writeFileSync(join(folder, 'old.txt'), 'going\n');
  writeFileSync(join(folder, '.env'), 'KEY=value\n');
  writeFileSync(join(root, 'outside', 'secret.txt'), 'not yours\n');
  const time = createVirtualTime(new Date('2026-11-11T02:00:00.000Z'));
  const ids = createUuidV7Generator({ now: () => Date.now() });
  const own = join(folder, 'halcyonic-data');
  mkdirSync(own);
  writeFileSync(join(own, 'access-token'), 'token\n');
  const controlPlane = new ControlPlane({
    journal: openSqliteJournal({ path: ':memory:', originIfNew: 'live', ids }),
    adapters: [],
    ids,
    clock: time,
    scheduler: time,
    logger: capturingLogger().logger,
    commandTimeoutMs: 30_000,
    locations: createHostLocations([root]),
    checkpoints: {
      store: new CheckpointStore({ git: '/usr/bin/git', directory: join(root, 'store') }),
    },
    observeFiles: { protect: [own] },
    secrets: () => [{ what: 'test key', value: SECRET }],
  });
  const created = controlPlane.commands.submit(
    {
      schema_version: 1,
      command_id: '00000000-0000-4000-8000-000000000001' as CommandId,
      issued_at: '2026-11-11T02:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: 'project.create',
      payload: { name: 'Garden', location: { kind: 'existing_folder', root, folder_name: 'app' } },
    } as CommandEnvelope,
    'internal',
  );
  const projectId = (
    created.command?.result?.kind === 'project_created' ? created.command.result.project_id : ''
  ) as ProjectId;
  const journal = controlPlane.journal.info.journal_id;
  const checkpoint = async (): Promise<CheckpointId> => {
    const before = controlPlane.projection.checkpoints(projectId).length;
    await controlPlane.checkpoints?.beforeTask(
      projectId,
      ids.next() as ExecutionId,
      realpathSync(folder),
      NO_CAUSE,
    );
    const list = controlPlane.projection.checkpoints(projectId);
    assert.equal(list.length, before + 1, 'a checkpoint was kept');
    return (list.at(-1) as { checkpoint_id: CheckpointId }).checkpoint_id;
  };
  return {
    root,
    folder,
    controlPlane,
    projectId,
    journal,
    checkpoint,
    reads: new ProjectReads(controlPlane),
  };
}

describe('reading a project (the parity map, gaps 5 and 10)', () => {
  test('what changed between two checkpoints: each file with its kind and lines, and one file as a diff', async (t) => {
    const { folder, controlPlane, projectId, journal, checkpoint, reads } = setup('changes');
    t.after(() => controlPlane.close());
    const from = await checkpoint();
    writeFileSync(
      join(folder, 'src', 'main.ts'),
      `const a = 1;\nconst b = 3;\nconst key = '${SECRET}';\n`,
    );
    rmSync(join(folder, 'old.txt'));
    writeFileSync(join(folder, 'new.txt'), 'here\n');
    writeFileSync(join(folder, 'logo.png'), Buffer.from([0x89, 0x50, 0x4e, 0x47, 0, 1, 2, 3]));
    writeFileSync(join(folder, '.env'), 'KEY=changed\n');
    const to = await checkpoint();

    const changes = await reads.changes(projectId, { journal_id: journal, from, to }, LOCAL);
    assert.deepEqual(
      changes?.files.map((f) => [f.path, f.change, f.added_lines, f.removed_lines, f.binary]),
      [
        ['.env', 'changed', 1, 1, false],
        ['logo.png', 'added', null, null, true],
        ['new.txt', 'added', 1, 0, false],
        ['old.txt', 'removed', 0, 1, false],
        ['src/main.ts', 'changed', 2, 1, false],
      ],
    );
    assert.deepEqual(
      [changes?.files_total, changes?.complete, changes?.source],
      [5, true, 'project_files'],
    );

    const diff = await reads.fileChange(
      projectId,
      { journal_id: journal, from, to, path: 'src/main.ts' },
      LOCAL,
    );
    assert.equal(diff?.change, 'changed');
    assert.match(diff?.diff ?? '', /^-const b = 2;$/m);
    assert.match(diff?.diff ?? '', /^\+const b = 3;$/m);
    assert.ok(!(diff?.diff ?? '').includes(SECRET), 'a held secret is taken out');
    assert.match(diff?.diff ?? '', /\[redacted: test key\]/);

    const env = await reads.fileChange(
      projectId,
      { journal_id: journal, from, to, path: '.env' },
      LOCAL,
    );
    assert.deepEqual([env?.withheld, env?.diff], [true, null]);
    const binary = await reads.fileChange(
      projectId,
      { journal_id: journal, from, to, path: 'logo.png' },
      LOCAL,
    );
    assert.deepEqual([binary?.binary, binary?.diff], [true, null]);
    assert.equal(
      await reads.fileChange(
        projectId,
        { journal_id: journal, from, to, path: 'src/unchanged.ts' },
        LOCAL,
      ),
      undefined,
    );
    // Only the project's own checkpoints, its own journal, and an owner-journal principal.
    const other = '01920000-0000-7000-8000-000000000999' as CheckpointId;
    assert.equal(
      await reads.changes(projectId, { journal_id: journal, from, to: other }, LOCAL),
      undefined,
    );
    assert.equal(
      await reads.changes(
        projectId,
        { journal_id: other as unknown as JournalId, from, to },
        LOCAL,
      ),
      undefined,
    );
    assert.equal(
      await reads.changes(projectId, { journal_id: journal, from, to }, null),
      undefined,
    );
  });

  test('a diff past its bound is cut at a line and marked incomplete, git stopped there (AUDIT2 S3)', async (t) => {
    const { folder, controlPlane, projectId, journal, checkpoint, reads } = setup('long-diff');
    t.after(() => controlPlane.close());
    writeFileSync(join(folder, 'long.txt'), 'a\n'.repeat(10));
    const from = await checkpoint();
    writeFileSync(
      join(folder, 'long.txt'),
      `${'line of new text that is long enough\n'.repeat(4000)}`,
    );
    const to = await checkpoint();
    const diff = await reads.fileChange(
      projectId,
      { journal_id: journal, from, to, path: 'long.txt' },
      LOCAL,
    );
    assert.equal(diff?.complete, false);
    assert.ok(Buffer.byteLength(diff?.diff ?? '') <= 65_536);
    assert.ok((diff?.diff ?? '').endsWith('\n'), 'cut at a whole line');
  });

  test('one file as it is now: text whole, binary, too large and secret-shaped by kind; never a link, outside or Halcyonic data', async (t) => {
    const { root, folder, controlPlane, projectId, journal, reads } = setup('file');
    t.after(() => controlPlane.close());
    writeFileSync(join(folder, 'src', 'key.ts'), `export const key = '${SECRET}';\n`);
    writeFileSync(join(folder, 'big.txt'), 'x'.repeat(300_000));
    writeFileSync(join(folder, 'data.bin'), Buffer.from([1, 0, 2]));
    symlinkSync(join(root, 'outside', 'secret.txt'), join(folder, 'link.txt'));
    symlinkSync(join(root, 'outside'), join(folder, 'linked'));
    const read = (path: string) => reads.file(projectId, { journal_id: journal, path }, LOCAL);

    const main = await read('src/main.ts');
    assert.deepEqual(
      [main?.kind, main?.text, main?.lines, main?.size_bytes],
      ['text', 'const a = 1;\nconst b = 2;\n', 2, 26],
    );
    assert.equal(main?.source, 'project_files');
    assert.equal((await read('src/key.ts'))?.text, "export const key = '[redacted: test key]';\n");
    assert.deepEqual(
      [(await read('big.txt'))?.kind, (await read('big.txt'))?.text],
      ['too_large', null],
    );
    assert.equal((await read('data.bin'))?.kind, 'binary');
    assert.deepEqual([(await read('.env'))?.kind, (await read('.env'))?.text], ['withheld', null]);
    for (const refused of [
      'link.txt',
      'linked/secret.txt',
      'halcyonic-data/access-token',
      // Halcyonic's data named in another case, which the Mac's volume ignores (AUDIT2 S4).
      'HALCYONIC-DATA/access-token',
      'src',
      'missing.ts',
      '../outside/secret.txt',
      '/etc/hosts',
      'src/../old.txt',
    ]) {
      assert.equal(await read(refused), undefined, refused);
    }
    assert.equal(
      await reads.file(projectId, { journal_id: journal, path: 'src/main.ts' }, null),
      undefined,
    );
  });

  test('a check step read without previews set up is unavailable', async (t) => {
    const { controlPlane, projectId, journal, reads } = setup('check');
    t.after(() => controlPlane.close());
    assert.equal(
      reads.checkOutput(
        projectId,
        '01920000-0000-7000-8000-000000000998',
        { journal_id: journal, step: 'build' },
        LOCAL,
      ),
      undefined,
    );
  });

  test('secret-shaped names are withheld as agents are kept from them', () => {
    for (const path of [
      '.env',
      '.env.local',
      'config/.ENV.production',
      '.git/config',
      'a/.git/HEAD',
      'server.key',
      'cert.PEM',
      'id_rsa',
      'keys/id_ed25519.pub',
      '.npmrc',
      'credentials',
      'secrets.json',
    ]) {
      assert.equal(withheldPath(path), true, path);
    }
    for (const path of [
      'src/main.ts',
      'environment.ts',
      'keyboard.ts',
      'docs/secrets-guide.md',
      'gitignore.txt',
    ]) {
      assert.equal(withheldPath(path), false, path);
    }
  });
});
