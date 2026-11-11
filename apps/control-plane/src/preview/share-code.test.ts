import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { crc32, deflateSync } from 'node:zlib';
import { QUIET_ZONE, SHARE_CODE_MAX_BYTES, type ShareCodeCells, shareCode } from './share-code.ts';

// A share's address as a code to scan (ADR 0054): the encoder's structure here, and, on a Mac,
// each version read back by the system's own code detector (Core Image), never a library of ours.

/** The longest text each version holds at level M in byte mode (ISO/IEC 18004 table 7). */
const CAPACITY = [14, 26, 42, 62, 84, 106, 122, 152, 180, 213, 251, 287, 331];

function text(length: number): string {
  return `https://${'abcdefghijklmnopqrstuvwxyz0123456789-'.repeat(10)}`.slice(0, length);
}

const scratch = mkdtempSync(join(tmpdir(), 'halcyonic-share-code-'));
after(() => rmSync(scratch, { recursive: true, force: true }));

/** The code as a greyscale PNG, 8 pixels a cell, with its quiet zone. */
function png(code: ShareCodeCells): Buffer {
  const scale = 8;
  const side = (code.size + 2 * QUIET_ZONE) * scale;
  const raw = Buffer.alloc((side + 1) * side, 255);
  for (let y = 0; y < side; y += 1) {
    raw[y * (side + 1)] = 0;
    for (let x = 0; x < side; x += 1) {
      const cx = Math.floor(x / scale) - QUIET_ZONE;
      const cy = Math.floor(y / scale) - QUIET_ZONE;
      const dark =
        cx >= 0 && cy >= 0 && cx < code.size && cy < code.size && code.rows[cy]?.[cx] === '1';
      raw[y * (side + 1) + 1 + x] = dark ? 0 : 255;
    }
  }
  const chunk = (type: string, data: Buffer) => {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type), data]);
    const sum = Buffer.alloc(4);
    sum.writeUInt32BE(crc32(body));
    return Buffer.concat([length, body, sum]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(side, 0);
  header.writeUInt32BE(side, 4);
  header[8] = 8;
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

const DETECT = `ObjC.import('CoreImage');
function run(argv) {
  const image = $.CIImage.imageWithContentsOfURL($.NSURL.fileURLWithPath(argv[0]));
  const options = $.NSDictionary.dictionaryWithObjectForKey($.CIDetectorAccuracyHigh, $.CIDetectorAccuracy);
  const detector = $.CIDetector.detectorOfTypeContextOptions($.CIDetectorTypeQRCode, $(), options);
  const features = detector.featuresInImage(image);
  const out = [];
  for (let i = 0; i < features.count; i++) out.push(ObjC.unwrap(features.objectAtIndex(i).messageString));
  return JSON.stringify(out);
}`;

/** What macOS's own detector reads in the code. */
function detected(code: ShareCodeCells, name: string): Promise<string[]> {
  const file = join(scratch, `${name}.png`);
  writeFileSync(file, png(code));
  return new Promise((resolve, reject) => {
    execFile(
      '/usr/bin/osascript',
      ['-l', 'JavaScript', '-e', DETECT, file],
      { timeout: 30_000 },
      (error, stdout) => (error ? reject(error) : resolve(JSON.parse(stdout) as string[])),
    );
  });
}

describe("a share's address as a code to scan (ADR 0054)", () => {
  test('the smallest version that holds the address, up to version 13; longer is refused', () => {
    CAPACITY.forEach((capacity, index) => {
      assert.equal(shareCode(text(capacity)).version, index + 1, `${capacity} bytes`);
      if (index + 1 < CAPACITY.length)
        assert.equal(shareCode(text(capacity + 1)).version, index + 2, `${capacity + 1} bytes`);
    });
    assert.equal(SHARE_CODE_MAX_BYTES, CAPACITY.at(-1));
    assert.throws(() => shareCode('x'.repeat(SHARE_CODE_MAX_BYTES + 1)), /too long/);
    // Every address a share can have fits: the contract bounds it at 253 characters.
    assert.ok(shareCode(`https://${'a'.repeat(245)}`).version <= 12);
  });

  test('its cells are a square, with the three finders, the timing lines and the dark cell where they belong', () => {
    const code = shareCode('https://bright-quiet-garden.trycloudflare.com');
    assert.equal(code.size, 17 + 4 * code.version);
    assert.equal(code.rows.length, code.size);
    assert.ok(code.rows.every((row) => row.length === code.size && /^[01]+$/.test(row)));
    const finder = ['1111111', '1000001', '1011101', '1011101', '1011101', '1000001', '1111111'];
    const n = code.size;
    for (const [x, y] of [
      [0, 0],
      [n - 7, 0],
      [0, n - 7],
    ] as const)
      for (const [i, line] of finder.entries())
        assert.equal(code.rows[y + i]?.slice(x, x + 7), line);
    for (let i = 8; i < n - 8; i += 1) {
      assert.equal(code.rows[6]?.[i], i % 2 === 0 ? '1' : '0', 'the timing row');
      assert.equal(code.rows[i]?.[6], i % 2 === 0 ? '1' : '0', 'the timing column');
    }
    assert.equal(code.rows[n - 8]?.[8], '1', 'the dark cell');
    // The same address always gives the same code.
    assert.deepEqual(shareCode('https://bright-quiet-garden.trycloudflare.com'), code);
  });

  test("macOS's own detector reads back every version, a link, a Wi-Fi address and UTF-8", {
    skip: process.platform !== 'darwin',
  }, async () => {
    const samples = [
      'https://bright-quiet-garden.trycloudflare.com',
      'http://192.168.1.20:47970',
      'http://example.test/ü-日本-✓',
      ...CAPACITY.map((capacity) => text(capacity)),
    ];
    for (const [index, sample] of samples.entries())
      assert.deepEqual(await detected(shareCode(sample), `code-${index}`), [sample], sample);
  });
});
