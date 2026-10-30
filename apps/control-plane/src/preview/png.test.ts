import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { deflateSync } from 'node:zlib';
import { edgeCount } from './check.ts';
import { decodePng, MAX_SIDE } from './png.ts';

function chunk(type: string, body: Buffer): Buffer {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(body.length);
  // The decoder does not read CRCs; four zero bytes stand in.
  return Buffer.concat([length, Buffer.from(type, 'latin1'), body, Buffer.alloc(4)]);
}

/** A PNG of the pixels, each row filtered with the given filter, as an encoder would. */
function encode(
  width: number,
  height: number,
  pixels: Uint8Array,
  options: { channels?: 3 | 4; filters?: number[]; header?: Partial<Record<string, number>> } = {},
): Buffer {
  const channels = options.channels ?? 4;
  const stride = width * channels;
  const raw = Buffer.alloc(height * (stride + 1));
  const at = (x: number, y: number) => (x < 0 || y < 0 ? 0 : (pixels[y * stride + x] ?? 0));
  for (let y = 0; y < height; y += 1) {
    const kind = options.filters?.[y % options.filters.length] ?? 0;
    raw[y * (stride + 1)] = kind;
    for (let x = 0; x < stride; x += 1) {
      const left = at(x - channels, y);
      const up = at(x, y - 1);
      const corner = at(x - channels, y - 1);
      const p = left + up - corner;
      const paeth =
        Math.abs(p - left) <= Math.abs(p - up) && Math.abs(p - left) <= Math.abs(p - corner)
          ? left
          : Math.abs(p - up) <= Math.abs(p - corner)
            ? up
            : corner;
      const predicted = [0, left, up, (left + up) >> 1, paeth][kind] ?? 0;
      raw[y * (stride + 1) + 1 + x] = (at(x, y) - predicted) & 0xff;
    }
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(options.header?.width ?? width, 0);
  header.writeUInt32BE(options.header?.height ?? height, 4);
  header[8] = options.header?.depth ?? 8;
  header[9] = options.header?.colour ?? (channels === 4 ? 6 : 2);
  header[12] = options.header?.interlace ?? 0;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

/** Pixels that differ from their neighbours in every channel, so every filter has work to do. */
function noisy(width: number, height: number, channels: number): Uint8Array {
  const pixels = new Uint8Array(width * height * channels);
  for (let i = 0; i < pixels.length; i += 1) pixels[i] = (i * 97 + (i >> 3) * 31) & 0xff;
  return pixels;
}

/** A picture of Chrome's size, one grey, with a white bar from `x0` to `x1` across rows `y0` to `y1`. */
function picture(bar?: { x0: number; x1: number; y0: number; y1: number }): Buffer {
  const width = 1280;
  const height = 800;
  const pixels = new Uint8Array(width * height * 4).fill(60);
  if (bar !== undefined) {
    for (let y = bar.y0; y < bar.y1; y += 1) {
      for (let x = bar.x0; x < bar.x1; x += 1)
        pixels.fill(255, (y * width + x) * 4, (y * width + x) * 4 + 3);
    }
  }
  return encode(width, height, pixels);
}

describe("the check's own reading of its pictures (AUDIT M18B-M1)", () => {
  test('decodes RGBA and RGB through every row filter to the exact pixels', () => {
    for (const channels of [4, 3] as const) {
      const pixels = noisy(7, 6, channels);
      const decoded = decodePng(encode(7, 6, pixels, { channels, filters: [0, 1, 2, 3, 4] }));
      assert.equal(decoded.width, 7);
      assert.equal(decoded.height, 6);
      for (let p = 0; p < 7 * 6; p += 1) {
        for (let c = 0; c < 3; c += 1)
          assert.equal(decoded.rgba[p * 4 + c], pixels[p * channels + c], `${channels} ${p} ${c}`);
        assert.equal(decoded.rgba[p * 4 + 3], channels === 4 ? pixels[p * 4 + 3] : 255);
      }
    }
  });

  test('refuses what it does not read, rather than guessing', () => {
    const pixels = noisy(4, 4, 4);
    assert.throws(
      () => decodePng(Buffer.from('not a picture at all, not even close')),
      /not a PNG/,
    );
    assert.throws(
      () => decodePng(encode(4, 4, pixels, { header: { depth: 16 } })),
      /does not read/,
    );
    assert.throws(
      () => decodePng(encode(4, 4, pixels, { header: { colour: 3 } })),
      /does not read/,
    );
    assert.throws(
      () => decodePng(encode(4, 4, pixels, { header: { interlace: 1 } })),
      /interlaced/,
    );
    assert.throws(
      () => decodePng(encode(4, 4, pixels, { header: { width: MAX_SIDE + 1 } })),
      /unexpected size/,
    );
    // Data that inflates to more than the header allows stops at the header's length.
    assert.throws(() => decodePng(encode(4, 4, pixels, { header: { height: 2 } })));
    assert.throws(() => decodePng(encode(4, 4, pixels, { filters: [9] })), /unknown PNG filter/);
  });

  test('counts edges in the host: none in a flat picture, a bar outside the panel corner, nothing inside it', () => {
    assert.equal(edgeCount(picture()), 0);
    const bar = edgeCount(picture({ x0: 600, x1: 700, y0: 300, y1: 500 }));
    assert.ok(bar !== null && bar > 200, String(bar));
    assert.equal(edgeCount(picture({ x0: 20, x1: 300, y0: 20, y1: 200 })), 0);
    assert.equal(edgeCount(Buffer.from('no picture')), null);
  });
});
