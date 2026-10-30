import { lstat, readdir, readFile } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';
import type { CheckNoteKind, CheckNotes } from '@halcyonic/contracts';

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
];

/**
 * Reads the project's `src/` for input read around the starter's layer: listeners on the window,
 * the document or the canvas, and controllers read directly. Never follows a link; a note is never a
 * pass or a fail by itself (M1.8b).
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
      const text = await readFile(full, 'utf8').catch(() => null);
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
