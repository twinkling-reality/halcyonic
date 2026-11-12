import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { refusalsFor, shareImages, shareTargets } from './refusals.ts';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-refusals-')));
after(() => rmSync(base, { recursive: true, force: true }));

const PICTURE = Buffer.from('a share picture, 1200 by 630');
const WORDS = 'Not the share picture: bring the original view.';

/** An original web app whose page names `/og-image.png` for link previews, kept in its `public/`. */
function original(
  name: string,
  html = '<meta property="og:image" content="/og-image.png" />',
): string {
  const folder = join(base, name);
  mkdirSync(join(folder, 'public'), { recursive: true });
  writeFileSync(join(folder, 'index.html'), `<html><head>${html}</head></html>\n`);
  writeFileSync(join(folder, 'public', 'og-image.png'), PICTURE);
  return folder;
}

function project(name: string, files: Record<string, Buffer | string>): string {
  const folder = join(base, name);
  for (const [path, bytes] of Object.entries(files)) {
    mkdirSync(join(folder, path, '..'), { recursive: true });
    writeFileSync(join(folder, path), bytes);
  }
  return folder;
}

describe("what a starter's check refuses (VRIFY, 2026-11-12)", () => {
  test('the share images a page names: og:image and twitter:image, by path or address, never one leaving its folder', () => {
    assert.deepEqual(
      shareTargets(
        [
          '<meta property="og:image" content="/og-image.png">',
          "<meta name='twitter:image' content='https://example.test/img/card.png?v=2'>",
          '<meta property="og:image:width" content="1200">',
          '<meta property="og:title" content="/not-an-image.png">',
          '<meta property="og:image" content="/../../etc/passwd">',
        ].join('\n'),
      ),
      ['og-image.png', 'img/card.png', 'etc/passwd'],
    );
    assert.deepEqual(shareTargets('<html></html>'), []);
  });

  test("a file of the made version that is, byte for byte, the original's share image is refused, with the starter's words and the file", async () => {
    const from = original('shared');
    const made = project('made', {
      'src/index.ts': 'export {};\n',
      'public/textures/hero.png': PICTURE,
    });
    assert.equal((await shareImages(from)).size, 1);
    assert.deepEqual(
      await refusalsFor([{ kind: 'original_share_image', words: WORDS }], made, from),
      [{ kind: 'original_share_image', words: WORDS, file: 'public/textures/hero.png' }],
    );
  });

  test('nothing is refused: another picture, no share image named, no original, or the file only behind a link', async () => {
    const from = original('plain');
    const declared = [{ kind: 'original_share_image' as const, words: WORDS }];
    const other = project('other', { 'public/textures/hero.png': Buffer.from('its own picture') });
    assert.deepEqual(await refusalsFor(declared, other, from), []);
    const unnamed = original('unnamed', '<title>No share image</title>');
    const copied = project('copied', { 'public/og.png': PICTURE });
    assert.deepEqual(await refusalsFor(declared, copied, unnamed), []);
    assert.deepEqual(await refusalsFor(declared, copied, null), []);
    assert.deepEqual(await refusalsFor([], copied, from), []);
    const linked = project('linked', { 'src/index.ts': 'export {};\n' });
    symlinkSync(join(from, 'public', 'og-image.png'), join(linked, 'hero.png'));
    assert.deepEqual(await refusalsFor(declared, linked, from), []);
    // Its dependencies and build are not the made version's own files.
    const built = project('built', {
      'dist/hero.png': PICTURE,
      'node_modules/x/hero.png': PICTURE,
    });
    assert.deepEqual(await refusalsFor(declared, built, from), []);
  });
});
