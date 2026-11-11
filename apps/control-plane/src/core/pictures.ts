import { createHash } from 'node:crypto';
import { constants } from 'node:fs';
import { type FileHandle, open, readdir } from 'node:fs/promises';
import { join, sep } from 'node:path';
import { crc32, inflateSync } from 'node:zlib';
import {
  type InstructionAttachment,
  PICTURE_BYTES,
  type PictureMediaType,
  PROJECT_PICTURES,
  type ProjectPicture,
  type Timestamp,
} from '@halcyonic/contracts';
import { type InstructionPicture, secretShapedPath } from '@halcyonic/runtime-core';
import { NO_LINK } from '../observer/file-observer.ts';
import { realpathOr, safeRelative } from './project-reads.ts';

/**
 * Pictures of a project's folder that go with an instruction (A4). The host reads each once, as the
 * project's own file read does: inside the folder, never through a link anywhere in its path, never a
 * withheld name or a folder of the host's own, judged and read through the one handle it opened. What
 * it is comes from its first bytes, never its name. The runtime is handed these bytes, never a path.
 */

/** Why an instruction's picture was not read: the command then fails with effect `none`. */
export type PictureRefusal =
  | 'picture_not_found'
  | 'picture_withheld'
  | 'picture_too_large'
  | 'picture_not_a_picture';

export type PicturesRead =
  | { readonly ok: true; readonly pictures: readonly InstructionPicture[] }
  | { readonly ok: false; readonly code: PictureRefusal; readonly message: string };

/** The picture's kind from its first bytes: PNG, JPEG or WebP, else null. */
export function pictureKind(head: Uint8Array): PictureMediaType | null {
  const at = (index: number, ...bytes: number[]) => bytes.every((b, i) => head[index + i] === b);
  if (at(0, 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a)) return 'image/png';
  if (at(0, 0xff, 0xd8, 0xff)) return 'image/jpeg';
  // "RIFF", a size, then "WEBP".
  if (at(0, 0x52, 0x49, 0x46, 0x46) && at(8, 0x57, 0x45, 0x42, 0x50)) return 'image/webp';
  return null;
}

/**
 * Whether the bytes are a whole picture of their kind, as far as its structure says, so a runtime is
 * never handed one it cannot decode: Codex 0.157.0 puts "image content omitted" in its place and sends
 * the words alone, and OpenCode 2.0.18 refuses the prompt (picture-and-file-input.md). A PNG's chunks
 * each with a matching CRC, from its header to its end, and its image data inflating to the size its
 * header names; a JPEG's segments through to its scan and its end marker; a WebP's RIFF size and its
 * first chunk. Not a full decode: pixels inside a JPEG or WebP scan are not checked.
 */
export function wholePicture(bytes: Uint8Array): boolean {
  const buffer = Buffer.from(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  switch (pictureKind(bytes)) {
    case 'image/png':
      return wholePng(buffer);
    case 'image/jpeg':
      return wholeJpeg(buffer);
    case 'image/webp':
      return wholeWebp(buffer);
    default:
      return false;
  }
}

/** Channels a sample has, by PNG colour type. */
const PNG_CHANNELS: Readonly<Record<number, number>> = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 };

function wholePng(bytes: Buffer): boolean {
  let at = 8;
  let header: {
    readonly width: number;
    readonly height: number;
    readonly bits: number;
    readonly channels: number;
    readonly interlaced: boolean;
  } | null = null;
  const data: Buffer[] = [];
  while (at + 12 <= bytes.length) {
    const length = bytes.readUInt32BE(at);
    const end = at + 12 + length;
    if (end > bytes.length) return false;
    const type = bytes.toString('latin1', at + 4, at + 8);
    if (crc32(bytes.subarray(at + 4, at + 8 + length)) !== bytes.readUInt32BE(at + 8 + length))
      return false;
    const body = bytes.subarray(at + 8, at + 8 + length);
    if (header === null) {
      if (type !== 'IHDR' || length !== 13) return false;
      const channels = PNG_CHANNELS[body[9] ?? -1];
      const bits = body[8] ?? 0;
      const width = body.readUInt32BE(0);
      const height = body.readUInt32BE(4);
      if (channels === undefined || width === 0 || height === 0 || ![1, 2, 4, 8, 16].includes(bits))
        return false;
      header = { width, height, bits, channels, interlaced: body[12] === 1 };
    } else if (type === 'IDAT') {
      data.push(body);
    } else if (type === 'IEND') {
      if (end !== bytes.length || data.length === 0) return false;
      let raw: Buffer;
      try {
        raw = inflateSync(Buffer.concat(data), { maxOutputLength: 268_435_456 });
      } catch {
        return false;
      }
      // An interlaced picture's passes have rows of their own, so its size is only bounded here.
      const row = Math.ceil((header.width * header.channels * header.bits) / 8) + 1;
      return header.interlaced ? raw.length > 0 : raw.length === row * header.height;
    }
    at = end;
  }
  return false;
}

/** Markers that start a frame: every SOFn but DHT (C4), JPG (C8) and DAC (CC). */
const JPEG_FRAMES = new Set([
  0xc0, 0xc1, 0xc2, 0xc3, 0xc5, 0xc6, 0xc7, 0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xcf,
]);

function wholeJpeg(bytes: Buffer): boolean {
  if (bytes.length < 4 || bytes[bytes.length - 2] !== 0xff || bytes[bytes.length - 1] !== 0xd9)
    return false;
  let at = 2;
  let frame = false;
  while (at + 4 <= bytes.length) {
    if (bytes[at] !== 0xff) return false;
    const marker = bytes[at + 1] ?? 0;
    if (marker === 0xff) {
      at += 1;
      continue;
    }
    const length = bytes.readUInt16BE(at + 2);
    if (length < 2 || at + 2 + length > bytes.length) return false;
    if (JPEG_FRAMES.has(marker)) frame = true;
    // The start of scan, with scan data before the end marker.
    if (marker === 0xda) return frame && at + 2 + length < bytes.length - 2;
    at += 2 + length;
  }
  return false;
}

function wholeWebp(bytes: Buffer): boolean {
  if (bytes.length < 20 || bytes.readUInt32LE(4) + 8 !== bytes.length) return false;
  const chunk = bytes.toString('latin1', 12, 16);
  const length = bytes.readUInt32LE(16);
  return ['VP8 ', 'VP8L', 'VP8X'].includes(chunk) && length > 0 && 20 + length <= bytes.length;
}

/** The folder's file at `path`, opened once without following a link, or null where it is not one to read. */
async function openInside(
  folder: string,
  path: string,
  protectedFolders: readonly string[],
): Promise<FileHandle | null> {
  if (!safeRelative(path)) return null;
  const full = join(folder, path);
  if (!full.startsWith(folder + sep)) return null;
  if (holdsHostFolder(full, protectedFolders)) return null;
  return open(full, constants.O_RDONLY | constants.O_NONBLOCK | NO_LINK).catch(() => null);
}

/** Whether a path is, or lies in or around, a folder of the host's own (compared without case). */
function holdsHostFolder(full: string, protectedFolders: readonly string[]): boolean {
  const lower = full.toLowerCase();
  return protectedFolders.some((own) => {
    const mine = (realpathOr(own) ?? own).toLowerCase();
    return lower === mine || lower.startsWith(mine + sep) || mine.startsWith(lower + sep);
  });
}

/**
 * Reads an instruction's pictures from the project's folder, in order. The first that cannot be
 * read refuses them all, so the words are never sent without a picture the person chose.
 */
export async function readPictures(
  folder: string,
  attachments: readonly InstructionAttachment[],
  protectedFolders: readonly string[],
): Promise<PicturesRead> {
  const pictures: InstructionPicture[] = [];
  for (const { path } of attachments) {
    const refused = (code: PictureRefusal, why: string): PicturesRead => ({
      ok: false,
      code,
      message: `The picture ${path} ${why}, so the instruction was not sent.`,
    });
    if (secretShapedPath(path))
      return refused('picture_withheld', 'has a name that may hold secrets');
    const handle = await openInside(folder, path, protectedFolders);
    if (handle === null)
      return refused('picture_not_found', "isn't a file in the project's folder");
    try {
      const stat = await handle.stat();
      if (!stat.isFile())
        return refused('picture_not_found', "isn't a file in the project's folder");
      if (stat.size > PICTURE_BYTES)
        return refused('picture_too_large', `is larger than ${PICTURE_BYTES / 1_048_576} MB`);
      const buffer = Buffer.alloc(stat.size + 1);
      let length = 0;
      while (length < buffer.length) {
        const { bytesRead } = await handle.read(buffer, length, buffer.length - length, length);
        if (bytesRead === 0) break;
        length += bytesRead;
      }
      if (length > PICTURE_BYTES)
        return refused('picture_too_large', `is larger than ${PICTURE_BYTES / 1_048_576} MB`);
      const bytes = new Uint8Array(buffer.subarray(0, length));
      const kind = pictureKind(bytes);
      if (kind === null || !wholePicture(bytes))
        return refused('picture_not_a_picture', "isn't a whole PNG, JPEG or WebP picture");
      pictures.push({
        path,
        media_type: kind,
        bytes,
        sha256: createHash('sha256').update(bytes).digest('hex'),
      });
    } catch {
      return refused('picture_not_found', 'could not be read');
    } finally {
      await handle.close();
    }
  }
  return { ok: true, pictures };
}

/** Folders the list never walks into: tools' own, and anything hidden. */
const SKIPPED_FOLDERS = new Set(['node_modules']);
const PICTURE_NAME = /\.(?:png|jpe?g|webp)$/i;
/** The list walks this deep, and looks at this many entries, at most. */
const LIST_DEPTH = 8;
const LIST_ENTRIES = 20_000;

/**
 * The pictures of a project's folder an instruction may carry, sorted by path: the first
 * `PROJECT_PICTURES`, and how many there are in all. A file named as a picture counts only when its
 * first bytes say it is one and it is no larger than `PICTURE_BYTES`. Never through a link, never a
 * hidden or withheld name, never `node_modules` or a folder of the host's own. Bounded in depth and in
 * entries looked at, so a large folder costs little; the total then counts what was looked at.
 */
export async function listPictures(
  folder: string,
  protectedFolders: readonly string[],
): Promise<{ readonly pictures: readonly ProjectPicture[]; readonly total: number }> {
  const found: string[] = [];
  let looked = 0;
  const walk = async (relative: string, depth: number): Promise<void> => {
    if (depth > LIST_DEPTH || looked >= LIST_ENTRIES) return;
    const entries = await readdir(relative === '' ? folder : join(folder, relative), {
      withFileTypes: true,
    }).catch(() => []);
    for (const entry of entries) {
      looked += 1;
      if (looked > LIST_ENTRIES) return;
      if (entry.name.startsWith('.')) continue;
      const path = relative === '' ? entry.name : `${relative}/${entry.name}`;
      if (secretShapedPath(path)) continue;
      // `withFileTypes` reads the entry itself: a link is neither a folder nor a file here.
      if (entry.isDirectory()) {
        if (SKIPPED_FOLDERS.has(entry.name)) continue;
        if (holdsHostFolder(join(folder, path), protectedFolders)) continue;
        await walk(path, depth + 1);
      } else if (entry.isFile() && PICTURE_NAME.test(entry.name)) {
        found.push(path);
      }
    }
  };
  await walk('', 0);
  found.sort();
  const pictures: ProjectPicture[] = [];
  let total = 0;
  for (const path of found) {
    const handle = await openInside(folder, path, protectedFolders);
    if (handle === null) continue;
    try {
      const stat = await handle.stat();
      if (!stat.isFile() || stat.size > PICTURE_BYTES) continue;
      const head = Buffer.alloc(12);
      const { bytesRead } = await handle.read(head, 0, 12, 0);
      const kind = pictureKind(head.subarray(0, bytesRead));
      if (kind === null) continue;
      total += 1;
      if (pictures.length < PROJECT_PICTURES)
        pictures.push({
          path,
          media_type: kind,
          size_bytes: stat.size,
          modified_at: stat.mtime.toISOString() as Timestamp,
        });
    } catch {
      // Not read: not listed.
    } finally {
      await handle.close();
    }
  }
  return { pictures, total };
}
