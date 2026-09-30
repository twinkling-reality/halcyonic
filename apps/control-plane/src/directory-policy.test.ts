import assert from 'node:assert/strict';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { createDirectoryPolicy } from './directory-policy.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-directory-policy-')));
after(() => rmSync(base, { recursive: true, force: true }));

function layout(name: string) {
  const top = join(base, name);
  const root = join(top, 'projects');
  const inside = join(root, 'app');
  const outside = join(top, 'private');
  mkdirSync(inside, { recursive: true });
  mkdirSync(outside, { recursive: true });
  return { root, inside, outside };
}

describe('directory policy', () => {
  test('allows the root and directories inside it, as real paths', () => {
    const { root, inside } = layout('allows');
    const allow = createDirectoryPolicy([root]);
    assert.deepEqual(allow(root), { ok: true, directory: root });
    assert.deepEqual(allow(inside), { ok: true, directory: inside });
    assert.deepEqual(allow(`${inside}/../app`), { ok: true, directory: inside });
  });

  test('refuses directories outside every root, including through .. and symbolic links', () => {
    const { root, outside } = layout('refuses');
    const link = join(root, 'escape');
    symlinkSync(outside, link);
    const allow = createDirectoryPolicy([root]);
    for (const path of [outside, `${root}/../private`, link]) {
      const decision = allow(path);
      assert.equal(decision.ok ? 'ok' : decision.code, 'location_not_allowed', path);
    }
  });

  test('answers the one spelling the file system has, whatever case or Unicode form is asked', (t) => {
    const { root, inside } = layout('spelling');
    const accented = join(root, 'café');
    mkdirSync(accented);
    const allow = createDirectoryPolicy([root]);
    const answer = (path: string) => {
      const decision = allow(path);
      return decision.ok ? decision.directory : decision.code;
    };
    // The composed and decomposed forms of the same name name one folder, on macOS volumes.
    if (!existsSync(join(root, 'café'))) {
      t.skip('this volume tells Unicode forms apart');
      return;
    }
    assert.equal(answer(join(root, 'café')), answer(accented));
    if (existsSync(join(root, 'APP'))) assert.equal(answer(join(root, 'APP')), inside);
  });

  test('a sibling whose name starts with the root name is not inside it', () => {
    const { root } = layout('prefix');
    const sibling = `${root}-other`;
    mkdirSync(sibling);
    assert.equal(createDirectoryPolicy([root])(sibling).ok, false);
  });

  test('refuses relative paths, missing paths and files', () => {
    const { root, inside } = layout('invalid');
    const file = join(inside, 'README.md');
    writeFileSync(file, 'x');
    const allow = createDirectoryPolicy([root]);
    const code = (path: string) => {
      const decision = allow(path);
      return decision.ok ? 'ok' : decision.code;
    };
    assert.equal(code('projects/app'), 'location_not_allowed');
    assert.equal(code(join(root, 'missing')), 'location_missing');
    assert.equal(code(file), 'location_missing');
  });

  test('with no roots configured nothing is allowed, and the reason says how to configure one', () => {
    const { inside } = layout('none');
    const decision = createDirectoryPolicy([])(inside);
    assert.equal(decision.ok ? 'ok' : decision.code, 'location_not_allowed');
    assert.match(decision.ok ? '' : decision.message, /HALCYONIC_PROJECT_ROOTS/);
  });
});
