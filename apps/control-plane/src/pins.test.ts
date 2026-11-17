import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { matchesPin, type PinnedFile } from './pins.ts';
import { browserManifest, manifestDigest } from './preview/play.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-pins-')));
after(() => rmSync(base, { recursive: true, force: true }));

/** A folder as an app's archive unpacks it: a launcher beside its runtime and its code. */
function unpacked(name: string): string {
  const folder = join(base, name);
  mkdirSync(join(folder, 'lib'), { recursive: true });
  writeFileSync(join(folder, 'cursor-agent'), '#!/bin/sh\nexec ./node ./index.js "$@"\n', {
    mode: 0o755,
  });
  writeFileSync(join(folder, 'index.js'), 'the agent\n');
  writeFileSync(join(folder, 'lib', 'chunk.js'), 'more of it\n');
  return folder;
}

describe('a folder pinned whole (AUDIT3 SU1, Cursor)', () => {
  test('matches only while every entry is as pinned: a changed file, an extra one or a link refuses it', async () => {
    const pinned = unpacked('pinned');
    const lines = browserManifest(pinned);
    assert.ok(Array.isArray(lines));
    const pin: PinnedFile = { path: 'x', sha256: manifestDigest(lines), folder: true };
    assert.equal(await matchesPin(unpacked('same'), pin), true);
    const changed = unpacked('changed');
    writeFileSync(join(changed, 'lib', 'chunk.js'), 'something else\n');
    assert.equal(await matchesPin(changed, pin), false, 'a changed file beside the launcher');
    const extra = unpacked('extra');
    writeFileSync(join(extra, 'lib', 'more.js'), 'planted\n');
    assert.equal(await matchesPin(extra, pin), false, 'an extra file');
    const linked = unpacked('linked');
    symlinkSync('/usr/bin/true', join(linked, 'lib', 'link'));
    assert.equal(await matchesPin(linked, pin), false, 'a link');
    assert.equal(await matchesPin(join(base, 'missing'), pin), false, 'no folder');
    // The same folder against a plain file's pin of that digest is not a match: the kind is the pin's own.
    assert.equal(await matchesPin(pinned, { path: 'x', sha256: pin.sha256 }), false);
  });
});
