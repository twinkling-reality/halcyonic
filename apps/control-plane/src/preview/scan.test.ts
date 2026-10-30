import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { SCAN_LIMITS, scanSource } from './scan.ts';

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
});
