import { constants } from 'node:fs';
import { type FileHandle, open } from 'node:fs/promises';
import { MEDIA_PHOTO_BYTES, MEDIA_VIDEO_BYTES, type MediaType } from '@halcyonic/contracts';
import { NO_LINK } from '../observer/file-observer.ts';
import { pictureKind, wholePicture } from './pictures.ts';

/**
 * What a photo or video is (ADR 0066), from its bytes and their structure, never its name: PNG, JPEG
 * and WebP as pictures are read (ADR 0058); HEIC, MP4 and QuickTime by the brand of their first
 * `ftyp` box and a walk of their top-level boxes that must end exactly at the file's end, with a
 * `moov` box for a video. A truncated or padded file is none of them.
 */

/** `ftyp` brands, four bytes each, by what they mean. */
const HEIC_BRANDS = new Set(['heic', 'heix', 'mif1']);
const MP4_BRANDS = new Set([
  'isom',
  'iso2',
  'iso3',
  'iso4',
  'iso5',
  'iso6',
  'mp41',
  'mp42',
  'avc1',
  'M4V ',
]);
const QUICKTIME_BRAND = 'qt  ';

/** The most top-level boxes walked; a real file has a handful. */
const BOXES_MAX = 4096;

/** The type the first bytes name, or null: a picture's signature, or an `ftyp` box's major brand. */
export function mediaKind(head: Uint8Array): MediaType | null {
  const picture = pictureKind(head);
  if (picture !== null) return picture;
  if (head.length < 12) return null;
  const buffer = Buffer.from(head.buffer, head.byteOffset, head.byteLength);
  if (buffer.toString('latin1', 4, 8) !== 'ftyp') return null;
  const brand = buffer.toString('latin1', 8, 12);
  if (HEIC_BRANDS.has(brand)) return 'image/heic';
  if (MP4_BRANDS.has(brand)) return 'video/mp4';
  if (brand === QUICKTIME_BRAND) return 'video/quicktime';
  return null;
}

/** Whether the type is a video's. */
export function isVideo(type: MediaType): boolean {
  return type === 'video/mp4' || type === 'video/quicktime';
}

/** The most bytes a file of this type may have. */
export function mediaLimit(type: MediaType): number {
  return isVideo(type) ? MEDIA_VIDEO_BYTES : MEDIA_PHOTO_BYTES;
}

/** The file's type when the file is whole by its structure and within its type's size, else null. */
export async function checkMedia(path: string): Promise<{ type: MediaType; size: number } | null> {
  let handle: FileHandle;
  try {
    handle = await open(path, constants.O_RDONLY | NO_LINK);
  } catch {
    return null;
  }
  try {
    const stat = await handle.stat();
    if (!stat.isFile() || stat.size < 12) return null;
    const head = Buffer.alloc(16);
    await handle.read(head, 0, 16, 0);
    const type = mediaKind(head);
    if (type === null || stat.size > mediaLimit(type)) return null;
    if (type === 'image/png' || type === 'image/jpeg' || type === 'image/webp') {
      const bytes = Buffer.alloc(stat.size);
      const { bytesRead } = await handle.read(bytes, 0, stat.size, 0);
      if (bytesRead !== stat.size || !wholePicture(bytes)) return null;
      return { type, size: stat.size };
    }
    const boxes = await topBoxes(handle, stat.size);
    if (boxes === null) return null;
    if (isVideo(type) && !boxes.includes('moov')) return null;
    if (type === 'image/heic' && !boxes.includes('meta')) return null;
    return { type, size: stat.size };
  } catch {
    return null;
  } finally {
    await handle.close();
  }
}

/**
 * The file's top-level ISO BMFF box types, in order, or null unless they chain exactly from the
 * first byte to the last: a size of 1 means a 64-bit size follows, a size of 0 runs to the end and is
 * allowed only on the last box, and no box is smaller than its own header.
 */
export async function topBoxes(handle: FileHandle, size: number): Promise<string[] | null> {
  const types: string[] = [];
  const header = Buffer.alloc(16);
  let at = 0;
  while (at < size) {
    if (types.length >= BOXES_MAX || size - at < 8) return null;
    const { bytesRead } = await handle.read(header, 0, 16, at);
    if (bytesRead < 8) return null;
    let length = header.readUInt32BE(0);
    const type = header.toString('latin1', 4, 8);
    let headerLength = 8;
    if (length === 1) {
      if (bytesRead < 16) return null;
      const wide = header.readBigUInt64BE(8);
      if (wide > BigInt(Number.MAX_SAFE_INTEGER)) return null;
      length = Number(wide);
      headerLength = 16;
    } else if (length === 0) {
      length = size - at;
    }
    if (length < headerLength || at + length > size) return null;
    types.push(type);
    at += length;
  }
  return at === size && types[0] === 'ftyp' ? types : null;
}
