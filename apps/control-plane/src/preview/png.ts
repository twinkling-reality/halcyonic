import { inflateSync } from 'node:zlib';

/** The largest picture the check decodes, in pixels a side; Chrome's pictures are 1280 by 800. */
export const MAX_SIDE = 4096;

export interface Pixels {
  readonly width: number;
  readonly height: number;
  /** Red, green, blue and alpha, one byte each, row after row. */
  readonly rgba: Uint8Array;
}

const SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

/**
 * Decodes a PNG in the host, never in the page (AUDIT M18B-M1): 8-bit RGB or RGBA, not interlaced,
 * the only kinds Chrome's screenshots are. Anything else throws, so a picture is counted or unknown.
 */
export function decodePng(png: Buffer): Pixels {
  if (png.length < 33 || !png.subarray(0, 8).equals(SIGNATURE)) throw new Error('not a PNG');
  let offset = 8;
  let header: Buffer | null = null;
  const data: Buffer[] = [];
  while (offset + 12 <= png.length) {
    const length = png.readUInt32BE(offset);
    const type = png.toString('latin1', offset + 4, offset + 8);
    const body = png.subarray(offset + 8, offset + 8 + length);
    if (body.length !== length) throw new Error('a PNG chunk is cut off');
    if (type === 'IHDR') header = body;
    else if (type === 'IDAT') data.push(body);
    else if (type === 'IEND') break;
    offset += 12 + length;
  }
  if (header === null || header.length !== 13) throw new Error('no PNG header');
  const width = header.readUInt32BE(0);
  const height = header.readUInt32BE(4);
  const [depth, colour, compression, filter, interlace] = header.subarray(8, 13);
  if (width === 0 || height === 0 || width > MAX_SIDE || height > MAX_SIDE)
    throw new Error('a PNG of an unexpected size');
  if (depth !== 8 || (colour !== 2 && colour !== 6) || compression !== 0 || filter !== 0)
    throw new Error('a kind of PNG the check does not read');
  if (interlace !== 0) throw new Error('an interlaced PNG');
  const channels = colour === 6 ? 4 : 3;
  const stride = width * channels;
  const raw = inflateSync(Buffer.concat(data), { maxOutputLength: height * (stride + 1) });
  if (raw.length !== height * (stride + 1)) throw new Error('PNG data of the wrong length');
  const rows = new Uint8Array(height * stride);
  for (let y = 0; y < height; y += 1) {
    const kind = raw[y * (stride + 1)];
    const line = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    const at = y * stride;
    for (let x = 0; x < stride; x += 1) {
      const left = x >= channels ? (rows[at + x - channels] ?? 0) : 0;
      const up = y > 0 ? (rows[at - stride + x] ?? 0) : 0;
      const corner = x >= channels && y > 0 ? (rows[at - stride + x - channels] ?? 0) : 0;
      rows[at + x] = ((line[x] ?? 0) + predict(kind, left, up, corner)) & 0xff;
    }
  }
  if (channels === 4) return { width, height, rgba: rows };
  const rgba = new Uint8Array(width * height * 4);
  for (let i = 0, j = 0; i < rows.length; i += 3, j += 4) {
    rgba[j] = rows[i] ?? 0;
    rgba[j + 1] = rows[i + 1] ?? 0;
    rgba[j + 2] = rows[i + 2] ?? 0;
    rgba[j + 3] = 255;
  }
  return { width, height, rgba };
}

function predict(kind: number | undefined, left: number, up: number, corner: number): number {
  switch (kind) {
    case 0:
      return 0;
    case 1:
      return left;
    case 2:
      return up;
    case 3:
      return (left + up) >> 1;
    case 4: {
      const p = left + up - corner;
      const pa = Math.abs(p - left);
      const pb = Math.abs(p - up);
      const pc = Math.abs(p - corner);
      return pa <= pb && pa <= pc ? left : pb <= pc ? up : corner;
    }
    default:
      throw new Error('an unknown PNG filter');
  }
}

/** A pixel has changed between two pictures when any of its channels differs by more than this. */
export const CHANNEL_CHANGE = 40;

/**
 * The share of pixels that changed from one picture to another, in the host; null when either
 * cannot be read or they are not the same size.
 */
export function changedShare(before: Buffer, after: Buffer): number | null {
  let a: Pixels;
  let b: Pixels;
  try {
    a = decodePng(before);
    b = decodePng(after);
  } catch {
    return null;
  }
  if (a.width !== b.width || a.height !== b.height) return null;
  let changed = 0;
  for (let i = 0; i < a.rgba.length; i += 4) {
    for (let c = 0; c < 3; c += 1) {
      if (Math.abs((a.rgba[i + c] ?? 0) - (b.rgba[i + c] ?? 0)) > CHANNEL_CHANGE) {
        changed += 1;
        break;
      }
    }
  }
  return changed / (a.width * a.height);
}
