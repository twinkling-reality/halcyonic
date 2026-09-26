import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
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
      assert.equal(decision.ok, false, path);
    }
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
    assert.equal(allow('projects/app').ok, false);
    assert.equal(allow(join(root, 'missing')).ok, false);
    assert.equal(allow(file).ok, false);
  });

  test('with no roots configured nothing is allowed, and the reason says how to configure one', () => {
    const { inside } = layout('none');
    const decision = createDirectoryPolicy([])(inside);
    assert.equal(decision.ok, false);
    assert.match(decision.ok ? '' : decision.message, /HALCYONIC_PROJECT_ROOTS/);
  });
});
