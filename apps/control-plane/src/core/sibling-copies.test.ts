import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type { ApprovalPath } from '@halcyonic/contracts';
import { INTRUSION_WORDS, intrusionOf } from './sibling-copies.ts';

const root = mkdtempSync(join(tmpdir(), 'halcyonic-siblings-'));
after(() => rmSync(root, { recursive: true, force: true }));
const home = join(root, 'home');
const tasks = join(home, '.halcyonic-tasks');
const data = join(home, '.halcyonic');
const own = join(tasks, 'garden', 'task-1');
const sibling = join(tasks, 'garden', 'task-2');
const person = join(home, 'code', 'garden');
for (const folder of [
  join(own, 'src'),
  join(sibling, 'src'),
  person,
  join(data, 'confined', 'x', 'tmp'),
])
  mkdirSync(folder, { recursive: true });
writeFileSync(join(sibling, 'src', 'index.ts'), 'export {};\n');
symlinkSync(join(sibling, 'src'), join(own, 'theirs'));
const folders = { tasks, data, home };

const read = (...paths: string[]): ApprovalPath[] =>
  paths.map((path) => ({ path, access: 'read' }));
const judge = (paths: ApprovalPath[], extra: { complete?: boolean; cut?: boolean } = {}) =>
  intrusionOf({ paths, complete: extra.complete ?? true, cut: extra.cut ?? false }, own, folders);

describe("a read into another task's copy is refused by the host, narrowly (AUDIT3 HR1 to HR5)", () => {
  test("a sibling task's copy is another task's copy (HR5)", () => {
    assert.equal(judge(read(join(sibling, 'src', 'index.ts'))), 'another_task_copy');
    // Written relative to this task's folder, it is still the sibling's.
    assert.equal(judge(read('../task-2/src/index.ts')), 'another_task_copy');
  });

  test("a link inside this task's copy that leads into a sibling is the sibling's (HR5)", () => {
    assert.equal(judge(read(join(own, 'theirs', 'index.ts'))), 'another_task_copy');
  });

  test('a path in the data folder or another .halcyonic home is a host folder (HR5)', () => {
    assert.equal(judge(read(join(data, 'journal.sqlite'))), 'host_folder');
    assert.equal(judge(read(join(home, '.halcyonic-proving', 'x'))), 'host_folder');
  });

  test("a path in the person's own project is never answered: it reaches the person (HR1, HR5)", () => {
    assert.equal(judge(read(join(person, 'src', 'index.ts'))), null);
    assert.equal(judge(read(join(home, 'notes.txt'))), null);
    // One place the person decides keeps the whole request theirs, whatever else it names.
    assert.equal(judge(read(join(sibling, 'src', 'index.ts'), join(person, 'a.ts'))), null);
  });

  test('a request not complete, or cut, is never judged (HR4, HR5)', () => {
    const path = read(join(sibling, 'src', 'index.ts'));
    assert.equal(judge(path, { complete: false }), null);
    assert.equal(judge(path, { cut: true }), null);
    assert.equal(judge([]), null);
  });

  test("this task's own copy, and the run's own folders, are its own", () => {
    assert.equal(judge(read(join(own, 'src', 'a.ts'))), null);
    const confined = join(data, 'confined', 'x');
    const theRun = intrusionOf(
      { paths: read(join(confined, 'tmp', 'out.txt')), complete: true, cut: false },
      own,
      { ...folders },
      [confined],
    );
    assert.equal(theRun, null);
    // Inside its own copy and a sibling's at once: the sibling's part is refused anyway.
    assert.equal(
      judge(read(join(own, 'src', 'a.ts'), join(sibling, 'src', 'index.ts'))),
      'another_task_copy',
    );
  });

  test('a write is judged as a read is', () => {
    assert.equal(
      judge([{ path: join(sibling, 'src', 'index.ts'), access: 'write' }]),
      'another_task_copy',
    );
  });

  test('the words say why and that the agent can go on in its own copy, never a product path', () => {
    assert.match(
      INTRUSION_WORDS.another_task_copy,
      /another task's copy, which this task cannot read/,
    );
    for (const words of Object.values(INTRUSION_WORDS)) {
      assert.match(words, /Work in this task's own copy\.$/);
      assert.doesNotMatch(words, /halcyonic|halora|\//i, 'no product name and no path');
    }
  });
});
