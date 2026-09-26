import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { createSeededRandom, createUuidV7Generator } from './ids.ts';

const V7 = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

describe('UUIDv7 generation', () => {
  test('identifiers carry the version, variant and millisecond timestamp', () => {
    const at = Date.parse('2026-09-26T12:34:56.789Z');
    const id = createUuidV7Generator({ now: () => at }).next();
    assert.match(id, V7);
    assert.equal(Number.parseInt(id.replaceAll('-', '').slice(0, 12), 16), at);
  });

  test('identifiers strictly increase within a millisecond and when the clock steps back', () => {
    let now = 1_000_000;
    const ids = createUuidV7Generator({ now: () => now });
    const seen: string[] = [];
    for (let i = 0; i < 5000; i += 1) {
      if (i === 2500) now -= 10;
      seen.push(ids.next());
    }
    for (let i = 1; i < seen.length; i += 1) {
      assert.ok((seen[i] as string) > (seen[i - 1] as string), `id ${i} sorts after id ${i - 1}`);
      assert.match(seen[i] as string, V7);
    }
  });

  test('a seeded source makes generation reproducible', () => {
    const make = () => createUuidV7Generator({ now: () => 42, random: createSeededRandom(7) });
    const a = make();
    const b = make();
    assert.deepEqual([a.next(), a.next()], [b.next(), b.next()]);
  });
});
