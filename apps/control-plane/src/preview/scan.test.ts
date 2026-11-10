import assert from 'node:assert/strict';
import {
  cpSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { lstat, rename } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { readNoLink, SCAN_LIMITS, scanSource } from './scan.ts';

function project(t: TestContext, files: Record<string, string>): string {
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-scan-')));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  for (const [path, text] of Object.entries(files)) {
    mkdirSync(join(root, path, '..'), { recursive: true });
    writeFileSync(join(root, path), text);
  }
  return root;
}

describe("the scan of a new app's source for input read around the starter's layer (M1.8b)", () => {
  test('notes each kind, by file and line, and never the layer itself', (t) => {
    const root = project(t, {
      'src/play-input.ts': "window.addEventListener('keydown', f);\n",
      'src/play-input-core.ts': 'navigator.getGamepads();\n',
      'src/game.ts': [
        'const a = 1;',
        "window.addEventListener('keydown', jump);",
        'document.onpointerdown = shoot;',
        "renderer.domElement.addEventListener('click', hit);",
        'const pad = world.input.xr.gamepads.right;',
        'for (const g of navigator.getGamepads()) {}',
      ].join('\n'),
    });
    return scanSource(root).then((notes) => {
      assert.equal(notes.scanned, 'full');
      assert.deepEqual(notes.items, [
        { kind: 'window_listener', file: 'src/game.ts', line: 2 },
        { kind: 'document_listener', file: 'src/game.ts', line: 3 },
        { kind: 'canvas_listener', file: 'src/game.ts', line: 4 },
        { kind: 'xr_input_read', file: 'src/game.ts', line: 5 },
        { kind: 'gamepad_read', file: 'src/game.ts', line: 6 },
      ]);
    });
  });

  test('keeps at most five notes, and a path of other characters as no path', async (t) => {
    const lines = Array.from({ length: 9 }, () => "window.addEventListener('x', f);").join('\n');
    const root = project(t, { 'src/spiel ü.ts': lines });
    const notes = await scanSource(root);
    assert.equal(notes.items.length, 5);
    assert.ok(notes.items.every((item) => item.file === null));
  });

  test('follows no link, and says it was partial when it skipped one or hit a limit', async (t) => {
    const root = project(t, { 'src/ok.ts': 'const a = 1;\n' });
    const outside = project(t, { 'secret.ts': "window.addEventListener('k', f);\n" });
    symlinkSync(join(outside, 'secret.ts'), join(root, 'src', 'linked.ts'));
    const linked = await scanSource(root);
    assert.equal(linked.scanned, 'partial');
    assert.deepEqual(linked.items, []);
    const big = project(t, { 'src/big.ts': 'x'.repeat(SCAN_LIMITS.bytes + 1) });
    assert.equal((await scanSource(big)).scanned, 'partial');
    // No src at all, or src a link: nothing is read.
    const none = project(t, { 'index.html': '' });
    assert.deepEqual(await scanSource(none), { scanned: 'partial', items: [] });
  });

  test('stops at the first limit it meets: nothing past it is read, and entries count too (AUDIT M18B-S1)', async (t) => {
    // a.ts is over the size limit and comes first; b.ts after it would be noted if the walk went on.
    const big = project(t, {
      'src/a.ts': 'x'.repeat(SCAN_LIMITS.bytes + 1),
      'src/b.ts': "window.addEventListener('keydown', f);\n",
    });
    assert.deepEqual(await scanSource(big), { scanned: 'partial', items: [] });
    // Many entries that are not source still count, so a folder of them cannot make the walk long.
    const files = Object.fromEntries(
      Array.from({ length: SCAN_LIMITS.entries + 1 }, (_, n) => [`src/assets/${n}.png`, '']),
    );
    const many = project(t, { ...files, 'src/z.ts': "window.addEventListener('keydown', f);\n" });
    assert.deepEqual(await scanSource(many), { scanned: 'partial', items: [] });
  });
});

describe("the scan says when a game is still set in the starter's default world (W1)", () => {
  test('notes setupLook with no world or the meadow by name, and not a world the game picked or made', async (t) => {
    const root = project(t, {
      'src/index.ts': [
        'setupLook(world);',
        "setupLook(world, 'meadow');",
        "setupLook(world, 'night');",
        "setupLook(world, { ...worlds.seaside, name: 'lagoon' });",
      ].join('\n'),
    });
    assert.deepEqual((await scanSource(root)).items, [
      { kind: 'default_world', file: 'src/index.ts', line: 1 },
      { kind: 'default_world', file: 'src/index.ts', line: 2 },
    ]);
  });
});

describe("the default world's note on the starter's own template (AUDIT W1B-1)", () => {
  const template = fileURLToPath(
    new URL('../../../../starters/iwsdk-vr/template', import.meta.url),
  );

  test('a pristine starter has exactly its index.ts note, and none once index.ts names a world', async (t) => {
    const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-scan-template-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    cpSync(template, root, { recursive: true });
    const index = join(root, 'src', 'index.ts');
    const text = readFileSync(index, 'utf8');
    const line = text.split('\n').findIndex((l) => l.trim() === 'setupLook(world);') + 1;
    assert.ok(line > 0, 'the template calls setupLook(world)');
    assert.deepEqual(await scanSource(root), {
      scanned: 'full',
      items: [{ kind: 'default_world', file: 'src/index.ts', line }],
    });
    writeFileSync(index, text.replace('setupLook(world);', "setupLook(world, 'night');"));
    assert.deepEqual(await scanSource(root), { scanned: 'full', items: [] });
  });
});

describe("the default world's pattern stays linear (AUDIT W1B-FIX-1)", () => {
  test('a megabyte line of const and spaces scans within a second, with no note', async (t) => {
    const root = project(t, { 'src/x.ts': `const${' '.repeat(SCAN_LIMITS.bytes - 16)}` });
    const started = performance.now();
    const notes = await scanSource(root);
    const took = performance.now() - started;
    assert.deepEqual(notes, { scanned: 'full', items: [] });
    assert.ok(took < 1000, `took ${Math.round(took)} ms`);
  });

  test('still notes a call after a declaration, and not a picked world', async (t) => {
    const root = project(t, {
      'src/index.ts': [
        'const { island } = setupLook(world);',
        "const look = setupLook(world, 'garden');",
      ].join('\n'),
    });
    assert.deepEqual((await scanSource(root)).items, [
      { kind: 'default_world', file: 'src/index.ts', line: 1 },
    ]);
  });
});

describe('a file the check reads is the one it looked at (AUDIT S3)', () => {
  test('read through one handle opened without following a link, the same file it looked at', async (t) => {
    const root = project(t, {
      'src/game.ts': 'const a = 1;\n',
      'elsewhere/game.ts': 'const b = 2;\n',
    });
    const path = join(root, 'src', 'game.ts');
    const seen = await lstat(path);
    assert.equal((await readNoLink(path, seen, 1024))?.toString('utf8'), 'const a = 1;\n');
    // Swapped for another file after it was looked at: not the one it looked at.
    writeFileSync(join(root, 'swap.ts'), 'const c = 3;\n');
    await rename(join(root, 'swap.ts'), path);
    assert.equal(
      await readNoLink(path, seen, 1024),
      null,
      'a file swapped in after the look is not read',
    );
    // Swapped for a link at its end.
    rmSync(path);
    symlinkSync(join(root, 'elsewhere', 'game.ts'), path);
    assert.equal(await readNoLink(path, seen, 1024), null, 'a link at the end is not followed');
    // Larger than allowed, through the handle.
    const big = join(root, 'elsewhere', 'game.ts');
    assert.equal(
      await readNoLink(big, await lstat(big), 4),
      null,
      'a file past the limit is not read',
    );
  });

  test('a folder above the file swapped for a link leads nowhere', {
    skip: process.platform !== 'darwin',
  }, async (t) => {
    const root = project(t, {
      'src/level/game.ts': 'const a = 1;\n',
      'elsewhere/game.ts': 'const b = 2;\n',
    });
    const path = join(root, 'src', 'level', 'game.ts');
    const seen = await lstat(path);
    rmSync(join(root, 'src', 'level'), { recursive: true });
    symlinkSync(join(root, 'elsewhere'), join(root, 'src', 'level'));
    assert.equal(await readNoLink(path, seen, 1024), null);
  });
});
