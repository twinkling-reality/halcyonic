import assert from 'node:assert/strict';
import {
  cpSync,
  existsSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import {
  type CheckStep,
  DRAWN_EDGES,
  drawnFrom,
  type PageFindings,
  passes,
  runCheck,
  summarizeExceptions,
} from './check.ts';

const ok: CheckStep = { exit: 0, seconds: 1 };
const page = (findings: Partial<PageFindings>): PageFindings => ({
  exceptions: [],
  exception_count: 0,
  console_errors: 0,
  canvases: 1,
  edges: { at_3s: 0, at_10s: 0 },
  drawn: true,
  pictures: [],
  ...findings,
});

describe('the check of a project from a starter (ADR 0037)', () => {
  test('passes only with type check 0, build 0, the page opened, no uncaught exception, and something drawn', () => {
    assert.ok(passes(ok, ok, page({})));
    assert.ok(!passes({ exit: 2, seconds: 1 }, ok, page({})));
    assert.ok(!passes(ok, { exit: null, seconds: 180 }, page({})));
    assert.ok(!passes(ok, ok, null), 'a page that was not opened is not a pass');
    assert.ok(!passes(ok, ok, page({ exception_count: 1 })));
    assert.ok(!passes(ok, ok, page({ drawn: false })));
  });

  test('something is drawn when either picture has more sharp edges than the threshold; nothing is assumed', () => {
    assert.ok(!drawnFrom(0, 0));
    assert.ok(!drawnFrom(null, null));
    assert.ok(!drawnFrom(DRAWN_EDGES, DRAWN_EDGES));
    assert.ok(drawnFrom(DRAWN_EDGES + 1, 0));
    assert.ok(drawnFrom(null, 7451));
  });

  test('reports at most five distinct exceptions, each its first two lines, clipped', () => {
    const many = Array.from(
      { length: 9 },
      (_, n) => `Uncaught TypeError ${n % 6}\n    at a (x.ts:1)\n    at b (y.ts:2)`,
    );
    const summary = summarizeExceptions(many);
    assert.equal(summary.length, 5);
    assert.equal(summary[0], 'Uncaught TypeError 0 at a (x.ts:1)');
    assert.equal(summarizeExceptions([`E ${'x'.repeat(1000)}`])[0]?.length, 300);
  });
});

// The whole check against a real Chrome and an installed starter set: opt-in, since the repository does not install
// the starter. HALCYONIC_TEST_STARTER names the installed folder (node_modules, input-profiles), HALCYONIC_TEST_CHROME
// the Chrome binary. Port 47886.
const STARTER = process.env.HALCYONIC_TEST_STARTER ?? '';
const CHROME = process.env.HALCYONIC_TEST_CHROME ?? '';
const SKIP =
  STARTER === '' || CHROME === ''
    ? 'HALCYONIC_TEST_STARTER and HALCYONIC_TEST_CHROME are not set'
    : process.platform !== 'darwin' && 'Seatbelt is macOS only';

describe('the check, end to end with the installed starter and Chrome', { skip: SKIP }, () => {
  const template = new URL('../../../../starters/iwsdk-vr/template/', import.meta.url).pathname;
  const host = new URL('../../../../starters/iwsdk-vr/host/', import.meta.url).pathname;
  const check = async (t: TestContext, change: (project: string) => void) => {
    const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-check-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const project = join(root, 'project');
    cpSync(template, project, { recursive: true });
    symlinkSync(join(STARTER, 'node_modules'), join(project, 'node_modules'));
    change(project);
    return runCheck({
      node: process.execPath,
      chrome: CHROME,
      home: homedir(),
      project,
      starter: STARTER,
      hostSource: host,
      cache: join(root, 'cache'),
      profiles: existsSync(join(STARTER, 'input-profiles'))
        ? join(STARTER, 'input-profiles')
        : null,
      port: 47886,
    });
  };
  const addToIndex = (project: string, imports: string, code: string) => {
    const index = join(project, 'src', 'index.ts');
    const text = readFileSync(index, 'utf8');
    writeFileSync(
      index,
      `${imports}\n${text.replace('world.registerSystem(PanelSystem);', `world.registerSystem(PanelSystem);\n  ${code}`)}`,
    );
  };

  test('the empty starter builds and runs cleanly but draws nothing of a game, so it does not pass', {
    timeout: 180_000,
  }, async (t) => {
    const result = await check(t, () => undefined);
    assert.equal(result.tsc.exit, 0);
    assert.equal(result.build.exit, 0);
    assert.equal(result.page?.exception_count, 0);
    assert.equal(result.page?.drawn, false);
    assert.equal(result.passed, false);
  });

  test('a box in front of the camera is drawn, and the check passes', {
    timeout: 180_000,
  }, async (t) => {
    const result = await check(t, (project) =>
      addToIndex(
        project,
        "import { BoxGeometry, Mesh, MeshStandardMaterial } from 'three';",
        'world.createTransformEntity(new Mesh(new BoxGeometry(0.6, 0.6, 0.6), new MeshStandardMaterial({ color: 0xff8800 })).translateX(-2).translateY(1.3).translateZ(-3.9));',
      ),
    );
    assert.equal(result.page?.drawn, true, JSON.stringify(result.page?.edges));
    assert.equal(result.passed, true);
  });

  test('an uncaught exception in the page is caught and fails the check', {
    timeout: 180_000,
  }, async (t) => {
    const result = await check(t, (project) =>
      addToIndex(project, '', "setTimeout(() => { throw new Error('marker-3c9a'); }, 500);"),
    );
    assert.equal(result.page?.exception_count, 1);
    assert.match(result.page?.exceptions[0] ?? '', /marker-3c9a/);
    assert.equal(result.passed, false);
  });

  test('a page that fakes getImageData, Image and Math.abs cannot make itself drawn: the host counts the edges (AUDIT M18B-M1)', {
    timeout: 180_000,
  }, async (t) => {
    const result = await check(t, (project) =>
      addToIndex(
        project,
        '',
        'CanvasRenderingContext2D.prototype.getImageData = function (_x: number, _y: number, w: number, h: number) { const d = new Uint8ClampedArray(w * h * 4); for (let i = 0; i < d.length; i += 1) d[i] = (i * 97) % 256; return new ImageData(d, w, h); }; Math.abs = () => 255; HTMLImageElement.prototype.decode = () => Promise.resolve(); Object.defineProperty(HTMLImageElement.prototype, "width", { get: () => 1280 }); Object.defineProperty(HTMLImageElement.prototype, "height", { get: () => 800 });',
      ),
    );
    assert.equal(result.page?.exception_count, 0);
    assert.equal(result.page?.drawn, false, JSON.stringify(result.page?.edges));
    assert.equal(result.passed, false);
  });

  test('a page that makes Chrome send more than the check reads ends the run, unknown and not passed (AUDIT M18B-M2)', {
    timeout: 180_000,
  }, async (t) => {
    const result = await check(t, (project) =>
      addToIndex(
        project,
        "import { BoxGeometry, Mesh, MeshStandardMaterial } from 'three';",
        "world.createTransformEntity(new Mesh(new BoxGeometry(0.6, 0.6, 0.6), new MeshStandardMaterial({ color: 0xff8800 })).translateX(-2).translateY(1.3).translateZ(-3.9)); setTimeout(() => console.log('x'.repeat(20_000_000)), 1000);",
      ),
    );
    assert.equal(result.page, null);
    assert.match(result.unknown ?? '', /more than the check reads/);
    assert.equal(result.passed, false);
  });
});
