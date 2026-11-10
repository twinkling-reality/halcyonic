import { constants, type Stats } from 'node:fs';
import { lstat, open, readdir } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';
import type { CheckNoteKind, CheckNotes } from '@halcyonic/contracts';
import { NO_LINK } from '../observer/file-observer.ts';

/** How much of a project's source the scan reads; at any of these it stops and says it was partial. */
export const SCAN_LIMITS = { files: 500, bytes: 1024 * 1024, depth: 20, entries: 5000 } as const;

const MAX_NOTES = 5;
const SOURCE = /\.(ts|js|mts|mjs)$/;
/** The starter's own input layer, the one place a game may read input; not scanned. */
const LAYER = new Set(['src/play-input.ts', 'src/play-input-core.ts']);
const PLAIN_PATH = /^[A-Za-z0-9._/-]{1,120}$/;
const LISTENS = String.raw`\s*\.\s*(addEventListener\s*\(|on(key|mouse|pointer|touch|click|wheel)\w*\s*=)`;

/** What the scan looks for, line by line; a line counts once, for the first that matches. */
const PATTERNS: readonly (readonly [CheckNoteKind, RegExp])[] = [
  ['window_listener', new RegExp(String.raw`\b(window|globalThis|self)${LISTENS}`)],
  ['document_listener', new RegExp(String.raw`\bdocument(\s*\.\s*body)?${LISTENS}`)],
  ['canvas_listener', new RegExp(String.raw`\b(canvas|domElement)${LISTENS}`)],
  ['xr_input_read', /\binput\s*\.\s*xr\b/],
  ['gamepad_read', /\bnavigator\s*\.\s*getGamepads\b|\.\s*gamepads?\b/],
  // A call that sets the starter's look up with no world, or the default's by name: the meadow every new game
  // shares (W1). Only a statement that starts with the call, so the starter's own error text, which names
  // `setupLook(world)` in a string, and a comment are never noted (AUDIT W1B-1). The name after `const`
  // starts with a non-space, so the pattern never backtracks over white space (AUDIT W1B-FIX-1).
  [
    'default_world',
    /^\s*(?:(?:const|let|var)\s+[^=\s][^=]*=\s*)?(?:await\s+)?setupLook\s*\(\s*\w+\s*(?:,\s*['"`]meadow['"`]\s*)?\)/,
  ],
];

/**
 * Reads the project's `src/` for input read around the starter's layer: listeners on the window,
 * the document or the canvas, and controllers read directly; and for a game still set in the
 * starter's default world (W1). Never follows a link; a note is never a pass or a fail by itself
 * (M1.8b). Every kind shares the five notes, in file and line order, so a pristine starter's own
 * `default_world` note in `src/index.ts` takes one of them.
 */
export async function scanSource(project: string): Promise<CheckNotes> {
  const items: CheckNotes['items'][number][] = [];
  let partial = false;
  let files = 0;
  let entriesSeen = 0;
  // The walk stops at the first limit it meets: what is past it is never read (AUDIT M18B-S1). A link
  // or an unreadable entry is skipped, and the scan still says it was partial.
  let stopped = false;
  const stop = () => {
    stopped = true;
    partial = true;
  };
  const visit = async (folder: string, depth: number): Promise<void> => {
    if (stopped) return;
    if (depth > SCAN_LIMITS.depth) return stop();
    const entries = await readdir(folder, { withFileTypes: true }).catch(() => null);
    if (entries === null) {
      partial = true;
      return;
    }
    for (const entry of entries.sort((a, b) => a.name.localeCompare(b.name))) {
      if (stopped) return;
      entriesSeen += 1;
      if (entriesSeen > SCAN_LIMITS.entries) return stop();
      const full = join(folder, entry.name);
      const info = await lstat(full).catch(() => null);
      if (info === null || info.isSymbolicLink()) {
        partial = true;
        continue;
      }
      if (info.isDirectory()) {
        await visit(full, depth + 1);
        continue;
      }
      if (!info.isFile() || !SOURCE.test(entry.name)) continue;
      const path = relative(project, full).split(sep).join('/');
      if (LAYER.has(path)) continue;
      files += 1;
      if (files > SCAN_LIMITS.files || info.size > SCAN_LIMITS.bytes) return stop();
      const text = (await readNoLink(full, info, SCAN_LIMITS.bytes))?.toString('utf8') ?? null;
      if (text === null) {
        partial = true;
        continue;
      }
      text.split('\n').forEach((line, index) => {
        if (items.length >= MAX_NOTES) return;
        const found = PATTERNS.find(([, pattern]) => pattern.test(line));
        if (found === undefined) return;
        items.push({
          kind: found[0],
          file: PLAIN_PATH.test(path) ? path : null,
          line: index + 1,
        });
      });
    }
  };
  const source = join(project, 'src');
  const info = await lstat(source).catch(() => null);
  if (info === null || !info.isDirectory()) return { scanned: 'partial', items: [] };
  await visit(source, 0);
  return { scanned: partial ? 'partial' : 'full', items };
}

/**
 * The file at `path` as the check looked at it (`seen`, its `lstat`), or null: opened once without
 * waiting on a pipe and without following a link, on macOS anywhere in its path (`NO_LINK`), then
 * judged and read through that one handle, so a file or a folder above it swapped for a link, or the
 * file swapped for another, after the look is never read through (AUDIT S3). At most `maxBytes`.
 */
export async function readNoLink(
  path: string,
  seen: Stats,
  maxBytes: number,
): Promise<Buffer | null> {
  const file = await open(path, constants.O_RDONLY | constants.O_NONBLOCK | NO_LINK).catch(
    () => null,
  );
  if (file === null) return null;
  try {
    const opened = await file.stat();
    if (
      !opened.isFile() ||
      opened.ino !== seen.ino ||
      opened.dev !== seen.dev ||
      opened.size > maxBytes
    )
      return null;
    const buffer = Buffer.alloc(opened.size + 1);
    let length = 0;
    while (length < buffer.length) {
      const { bytesRead } = await file.read(buffer, length, buffer.length - length, length);
      if (bytesRead === 0) break;
      length += bytesRead;
    }
    return length > maxBytes ? null : buffer.subarray(0, length);
  } catch {
    return null;
  } finally {
    await file.close();
  }
}
