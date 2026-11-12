import assert from 'node:assert/strict';
import { mkdtempSync, readdirSync, realpathSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { crc32, deflateSync } from 'node:zlib';
import { startTestServer } from '../testing/harness.ts';
import { GIVEN_PICTURES_KEPT, GivenPictures } from './given-pictures.ts';
import { readPictures } from './pictures.ts';

const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-given-')));
after(() => rmSync(root, { recursive: true, force: true }));

/** A 2 by 2 PNG of one colour, whole and decodable. */
function png([r, g, b]: readonly [number, number, number]): Buffer {
  const chunk = (type: string, data: Buffer) => {
    const body = Buffer.concat([Buffer.from(type, 'latin1'), data]);
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(body));
    return Buffer.concat([length, body, crc]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(2, 0);
  header.writeUInt32BE(2, 4);
  header.set([8, 2, 0, 0, 0], 8);
  const row = Buffer.from([0, r, g, b, r, g, b]);
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(Buffer.concat([row, row]))),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

test('a given picture is kept once by its digest, private, read back checked, and the oldest go (ADR 0058, amended)', async () => {
  const folder = join(root, 'store');
  const store = new GivenPictures(folder);
  const red = png([200, 40, 40]);
  const kept = await store.give(red);
  assert.ok(kept.ok);
  assert.equal(kept.picture.media_type, 'image/png');
  assert.equal((statSync(folder).mode & 0o777).toString(8), '700');
  assert.equal((statSync(join(folder, kept.picture.id)).mode & 0o777).toString(8), '600');
  const read = await store.read(kept.picture.id);
  assert.ok(read !== null);
  assert.deepEqual(Buffer.from(read.bytes), red);
  assert.equal(await store.read('../../etc/passwd'), null, 'never a path');
  // A file changed after it was kept no longer matches its digest, so it is not handed on.
  writeFileSync(join(folder, kept.picture.id), png([1, 2, 3]));
  assert.equal(await store.read(kept.picture.id), null);
  const refused = await store.give(Buffer.from('not a picture'));
  assert.ok(!refused.ok && refused.code === 'picture_not_a_picture');
  for (let i = 0; i < GIVEN_PICTURES_KEPT + 3; i += 1)
    assert.ok((await store.give(png([i, 0, 0]))).ok);
  assert.ok(readdirSync(folder).length <= GIVEN_PICTURES_KEPT);
});

test('an instruction’s pictures read in order, one from the folder and one given, and a given one needs no folder', async () => {
  const folder = join(root, 'project');
  const { mkdirSync } = await import('node:fs');
  mkdirSync(join(folder, 'art'), { recursive: true });
  writeFileSync(join(folder, 'art', 'blue.png'), png([30, 60, 220]));
  const store = new GivenPictures(join(root, 'store2'));
  const given = await store.give(png([200, 40, 40]));
  assert.ok(given.ok);
  const both = await readPictures(
    folder,
    [
      { kind: 'given', picture: given.picture.id },
      { kind: 'image', path: 'art/blue.png' },
    ],
    [],
    (id) => store.read(id),
  );
  assert.ok(both.ok);
  assert.deepEqual(
    both.pictures.map((p) => p.sha256 === given.picture.id),
    [true, false],
  );
  const alone = await readPictures(null, [{ kind: 'given', picture: given.picture.id }], [], (id) =>
    store.read(id),
  );
  assert.ok(alone.ok, 'no folder is needed for a given picture');
  const missing = await readPictures(null, [{ kind: 'given', picture: 'f'.repeat(64) }], [], (id) =>
    store.read(id),
  );
  assert.ok(!missing.ok && missing.code === 'picture_not_found');
});

test('POST /api/pictures keeps one picture from this Mac or a headset, and refuses what is not one', async (t) => {
  const server = await startTestServer({ givenPictures: { dataDir: join(root, 'data') } });
  t.after(() => server.stop());
  const post = (body: Buffer, type: string) =>
    fetch(`${server.baseUrl}/api/pictures`, {
      method: 'POST',
      headers: { authorization: `Bearer ${server.token}`, 'content-type': type },
      body,
    });
  const kept = await post(png([10, 200, 10]), 'image/png');
  assert.equal(kept.status, 201);
  const body = (await kept.json()) as { picture: { id: string; media_type: string } };
  assert.match(body.picture.id, /^[0-9a-f]{64}$/);
  assert.equal((await post(Buffer.from('words'), 'image/png')).status, 422);
  assert.equal((await post(png([1, 1, 1]), 'text/plain')).status, 415);
});
