/**
 * No facts strung on dots where a person reads them (the owner, 2026-11-11 and 2026-11-16: "Label · Value" rows
 * are banned on every panel and page). A row that shows several facts lays them out apart: a title and a line
 * of its own, a label beside its value, never one string joined on a middle dot or a bullet. This holds the
 * host's words and the Mac's pages (setup, pairing, usage, media and the play viewer) to that: any separator
 * dot in their sources fails here, naming the file and line. The headset's own words are swept with the film
 * batch and the batch after it, in apps/xr, and are not read here.
 *
 * Not read: tests, and folders named fixtures, which hold recordings and stand-ins of another product's own
 * reports (Salidium's glance is Salidium's words, carried as reported).
 */
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));

/** Where the host's words and the Mac's pages live. */
const FOLDERS: readonly string[] = ['apps/control-plane/src', 'packages'];

/** Never read: installed and generated code, recordings and stand-ins. */
const SKIPPED = new Set(['node_modules', 'fixtures', 'csharp', 'schema', 'dist', 'build']);

const READ = /\.(ts|js|html|css|json)$/;

/** A middle dot, a bullet or a look-alike, written plainly or as an escape or an HTML entity. */
const DOT =
  /[·•∙⋅‧・･]|\\u00b7|\\u2022|\\u2219|\\u22c5|&middot;|&bull;|&#183;|&#8226;|&#xb7;|&#x2022;/i;

function sources(folder: string): string[] {
  const found: string[] = [];
  for (const entry of readdirSync(join(ROOT, folder), { withFileTypes: true })) {
    if (SKIPPED.has(entry.name)) continue;
    const path = join(folder, entry.name);
    if (entry.isDirectory()) found.push(...sources(path));
    else if (entry.isFile() && READ.test(entry.name) && !entry.name.endsWith('.test.ts')) {
      found.push(path);
    }
  }
  return found;
}

/** Each line of a source that holds a separator dot, as file:line. */
function dotRows(path: string, text: string): string[] {
  const found: string[] = [];
  text.split('\n').forEach((line, index) => {
    if (DOT.test(line)) found.push(`${path}:${index + 1}`);
  });
  return found;
}

describe('no facts strung on dots on the Mac pages or in the host words', () => {
  test('finds a dot written plainly, as an escape or as an entity, and nothing in plain words', () => {
    assert.deepEqual(dotRows('a.js', "const a = 'Game Ended · Preview Stopped';"), ['a.js:1']);
    assert.deepEqual(dotRows('a.js', "x\nconst a = 'Tokens \\u00b7 Cost';"), ['a.js:2']);
    assert.deepEqual(dotRows('a.html', '<p>Usage &middot; Today</p>'), ['a.html:1']);
    assert.deepEqual(dotRows('a.css', 'li + li::before { content: "•"; }'), ['a.css:1']);
    assert.deepEqual(dotRows('a.ts', "const a = 'Game Ended';\nconst b = 'v1.2.3';"), []);
  });

  test('no source of the Mac pages or the host words joins facts on a dot', () => {
    const files = FOLDERS.flatMap(sources);
    assert.ok(
      files.some((file) => file.endsWith('preview/viewer/viewer.js')),
      'the play viewer is read',
    );
    assert.ok(
      files.some((file) => file.endsWith('network/setup-page/setup.js')),
      'setup is read',
    );
    const found = files.flatMap((file) =>
      dotRows(relative(ROOT, join(ROOT, file)), readFileSync(join(ROOT, file), 'utf8')),
    );
    assert.deepEqual(found, [], 'lay these facts out apart, never on a dot');
  });
});
