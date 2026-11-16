import { writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { crc32, deflateSync } from 'node:zlib';
import type { MediaType } from '@halcyonic/contracts';
import { type MediaFacts, MediaTool, type ToolRun } from '../core/media-tool.ts';

/** Made-up media for tests (ADR 0066): box structures and tiny pictures, never anyone's footage. */

/** One ISO BMFF box. */
export function box(type: string, payload: Buffer = Buffer.alloc(0)): Buffer {
  const head = Buffer.alloc(8);
  head.writeUInt32BE(8 + payload.length, 0);
  head.write(type, 4, 'latin1');
  return Buffer.concat([head, payload]);
}

export const ftyp = (brand: string) =>
  box('ftyp', Buffer.from(`${brand}\0\0\0\0${brand}`, 'latin1'));

/** A made-up clip with the structure the host walks; `extra` bytes make another of its kind. */
export const clip = (brand = 'qt  ', extra = 0) =>
  Buffer.concat([
    ftyp(brand),
    box('moov', Buffer.alloc(32)),
    box('mdat', Buffer.alloc(64 + extra)),
  ]);

/** A JPEG whole by its structure: a frame, a scan with data, the end marker. */
export const JPEG = Buffer.from([
  0xff, 0xd8, 0xff, 0xc0, 0x00, 0x11, 0x08, 0x00, 0x02, 0x00, 0x02, 0x03, 0x01, 0x11, 0x00, 0x02,
  0x11, 0x01, 0x03, 0x11, 0x01, 0xff, 0xda, 0x00, 0x0c, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11,
  0x00, 0x3f, 0x00, 0x00, 0xff, 0xd9,
]);

/** A 2 by 2 PNG of one colour, whole and decodable. */
export function png([r, g, b]: readonly [number, number, number]): Buffer {
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

/** A stand-in for ffmpeg: a copy, a whole PNG for each still, a made-up MP4 for the play copy. */
export class StandInTool extends MediaTool {
  readonly calls: string[] = [];
  readonly options: { ready?: ToolRun<string>; facts?: MediaFacts; playBytes?: number };

  constructor(
    work: string,
    options: { ready?: ToolRun<string>; facts?: MediaFacts; playBytes?: number } = {},
  ) {
    super(null, work);
    this.options = options;
  }

  override async ready(): Promise<ToolRun<string>> {
    return this.options.ready ?? { ok: true, value: '/stand-in/ffmpeg' };
  }

  override async strip(_input: string, type: MediaType, out: string) {
    this.calls.push('strip');
    const video = type === 'video/mp4' || type === 'video/quicktime';
    const path = join(out, video ? (type === 'video/mp4' ? 'copy.mp4' : 'copy.mov') : 'copy.jpg');
    // Never the upload's bytes, as a real strip is not: the placed copy has a digest of its own.
    if (video) await writeFile(path, clip(type === 'video/mp4' ? 'isom' : 'qt  ', 5));
    else await writeFile(path, JPEG);
    return {
      ok: true as const,
      value: {
        path,
        facts: this.options.facts ?? { width: 1376, height: 1840, durationMs: 6_565 },
      },
    };
  }

  override async still(_input: string, atMs: number, out: string, index: number) {
    this.calls.push(`still ${atMs}`);
    const path = join(out, `still-${index}.jpg`);
    await writeFile(path, png([index, 0, 0]));
    return { ok: true as const, value: path };
  }

  override async play(_input: string, out: string) {
    this.calls.push('play');
    const path = join(out, 'play.mp4');
    await writeFile(path, clip('isom', this.options.playBytes ?? 0));
    return { ok: true as const, value: path };
  }
}
