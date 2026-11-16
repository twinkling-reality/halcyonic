import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { READING_FILES } from '../apps/control-plane/src/preview/laptop-probe.ts';
import { currentSet, KIT_FILES, type KitVersions, LIST } from './kit-versions.ts';

// The released kit sets the laptop check reads (VRIFY, AUDIT3 K4, 2026-11-16): a kit change lands with its set.
const list = JSON.parse(readFileSync(LIST, 'utf8')) as KitVersions;

test('the list names exactly the kit files the laptop check compares', () => {
  assert.deepEqual([...KIT_FILES].sort(), [...READING_FILES].sort());
  assert.deepEqual([...list.files].sort(), [...KIT_FILES].sort());
});

test("the template's kit as it stands is the list's last version: run `node tooling/kit-versions.ts --add` with a kit change", () => {
  const last = list.versions.at(-1);
  assert.ok(last !== undefined, 'the list has a version');
  assert.deepEqual(last.sha256, currentSet());
});

test('every version is a set of known kit files, each a SHA-256, and no two in a row are the same', () => {
  let previous: string | null = null;
  for (const version of list.versions) {
    assert.equal(typeof version.from, 'string');
    for (const [file, hex] of Object.entries(version.sha256)) {
      assert.ok((KIT_FILES as readonly string[]).includes(file), file);
      assert.match(hex, /^[0-9a-f]{64}$/, file);
    }
    const key = JSON.stringify(version.sha256);
    assert.notEqual(key, previous, version.from);
    previous = key;
  }
});
