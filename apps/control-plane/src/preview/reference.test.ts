import assert from 'node:assert/strict';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { HOST_FILES } from './check.ts';
import { hostFilesDigest, referenceKey, StarterReferences } from './reference.ts';

const PARTS = {
  template: 'a'.repeat(64),
  lockfile: 'b'.repeat(64),
  host: 'c'.repeat(64),
  browser: 'd'.repeat(64),
};

const folder = (t: TestContext) => {
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-references-')));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  return root;
};

describe("the starter's own pictures a check compares with (DRAWN)", () => {
  test('a key names everything that shapes the picture: each part changes it, the same parts give the same key', () => {
    const key = referenceKey(PARTS);
    assert.match(key, /^[0-9a-f]{64}$/);
    assert.equal(referenceKey({ ...PARTS }), key);
    for (const part of Object.keys(PARTS) as (keyof typeof PARTS)[])
      assert.notEqual(referenceKey({ ...PARTS, [part]: 'e'.repeat(64) }), key, part);
  });

  test("the host files' digest covers each file by name and content, and is null when one is missing", (t) => {
    const host = folder(t);
    for (const file of HOST_FILES) writeFileSync(join(host, file), `${file}\n`);
    const digest = hostFilesDigest(host);
    assert.match(digest ?? '', /^[0-9a-f]{64}$/);
    writeFileSync(join(host, HOST_FILES[0]), 'changed\n');
    assert.notEqual(hostFilesDigest(host), digest);
    rmSync(join(host, HOST_FILES[1]));
    assert.equal(hostFilesDigest(host), null);
  });

  test('a picture is made once, kept as the only file in its key folder, and read back after', async (t) => {
    const root = folder(t);
    const references = new StarterReferences(root);
    let made = 0;
    const make = async (work: string) => {
      made += 1;
      mkdirSync(join(work, 'project'), { recursive: true });
      return Buffer.from('frame');
    };
    assert.deepEqual(await references.get('iwsdk-vr', 'k1', make), Buffer.from('frame'));
    assert.deepEqual(await references.get('iwsdk-vr', 'k1', make), Buffer.from('frame'));
    assert.equal(made, 1);
    assert.deepEqual(readdirSync(join(root, 'iwsdk-vr', 'k1')), ['frame.png']);
    assert.deepEqual(
      await new StarterReferences(root).get('iwsdk-vr', 'k1', make),
      Buffer.from('frame'),
    );
    assert.equal(made, 1, 'kept across a restart');
    // A new key's picture removes the older ones, which nothing reads again.
    await references.get('iwsdk-vr', 'k2', make);
    assert.deepEqual(readdirSync(join(root, 'iwsdk-vr')), ['k2']);
    assert.equal(made, 2);
  });

  test('two checks at once wait for one making; one that fails keeps nothing and is made again next time', async (t) => {
    const root = folder(t);
    const references = new StarterReferences(root);
    let made = 0;
    let release = () => {};
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const slow = async () => {
      made += 1;
      await held;
      return Buffer.from('frame');
    };
    const both = Promise.all([
      references.get('iwsdk-vr', 'k2', slow),
      references.get('iwsdk-vr', 'k2', slow),
    ]);
    release();
    assert.deepEqual(await both, [Buffer.from('frame'), Buffer.from('frame')]);
    assert.equal(made, 1);
    const failing = async () => null;
    assert.equal(await references.get('iwsdk-vr', 'k3', failing), null);
    assert.equal(existsSync(join(root, 'iwsdk-vr', 'k3')), false);
    assert.equal(await references.get('iwsdk-vr', 'k3', failing), null);
    // A making that throws is unavailable too, and leaves nothing.
    const throwing = async () => {
      throw new Error('no browser');
    };
    assert.equal(await references.get('iwsdk-vr', 'k4', throwing), null);
    assert.equal(existsSync(join(root, 'iwsdk-vr', 'k4')), false);
  });
});
