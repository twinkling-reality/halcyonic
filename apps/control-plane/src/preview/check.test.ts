import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
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
  const check = async (t: TestContext, change: (project: string) => void, twinStepMs?: number) => {
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
      template,
      cache: join(root, 'cache'),
      profiles: existsSync(join(STARTER, 'input-profiles'))
        ? join(STARTER, 'input-profiles')
        : null,
      port: 47886,
      ...(twinStepMs === undefined ? {} : { twinStepMs }),
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

  /** A box the layer listens to; a click makes it bigger and blue, which the host can see. */
  const BOX_IMPORTS =
    "import { BoxGeometry, Mesh, MeshStandardMaterial } from 'three';\nimport { onSelect } from './play-input.js';";
  const BOX =
    'const box = new Mesh(new BoxGeometry(0.9, 0.9, 0.9), new MeshStandardMaterial({ color: 0xff8800 })); box.position.set(-2, 1.3, -3.9); onSelect(world.createTransformEntity(box), () => { box.scale.setScalar(2.5); box.material.color.set(0x2266ff); });';
  const config = (project: string, offer: string) => {
    const file = join(project, 'iwsdk.config.json');
    writeFileSync(
      file,
      readFileSync(file, 'utf8').replace('"offer": "always"', `"offer": "${offer}"`),
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
    // Nothing to press, but the starter's World offers VR as it loads (M1.8b).
    assert.equal(result.laptop?.layer, 'starter');
    assert.equal(result.laptop?.mouse, 'no_target');
    assert.equal(result.vr?.offered, 'offered');
    assert.equal(result.ready, false);
    assert.deepEqual(result.notes, { scanned: 'full', items: [] });
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

  test('a page cannot register a service worker on the preview origin: the server refuses its script (AUDIT M16A-C1)', {
    timeout: 180_000,
  }, async (t) => {
    const result = await check(t, (project) => {
      writeFileSync(
        join(project, 'public', 'sw.js'),
        "self.addEventListener('fetch', () => {});\n",
      );
      addToIndex(
        project,
        '',
        "navigator.serviceWorker.register('./sw.js').then(() => setTimeout(() => { throw new Error('marker-sw-registered'); }), () => {});",
      );
    });
    assert.equal(result.page?.exception_count, 0, JSON.stringify(result.page?.exceptions));
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

  test('a box the game listens to: the mouse reaches it, the host sees the screen change, VR is offered, ready (M1.8b)', {
    timeout: 240_000,
  }, async (t) => {
    const result = await check(t, (project) => addToIndex(project, BOX_IMPORTS, BOX));
    assert.equal(result.passed, true, JSON.stringify(result.page));
    assert.deepEqual(result.laptop, {
      layer: 'starter',
      targets: 1,
      mouse: 'reached',
      key: 'reached',
      screen_changed: true,
      exceptions_during: 0,
      ready: true,
    });
    assert.deepEqual(result.vr, {
      offered: 'offered',
      how: 'offer_session',
      exceptions: 0,
      ready: true,
    });
    assert.equal(result.ready, true);
  });

  test('a game that changed the input layer is not ready on a laptop, whatever it reports (M1.8b)', {
    timeout: 240_000,
  }, async (t) => {
    const result = await check(t, (project) => {
      addToIndex(project, BOX_IMPORTS, BOX);
      writeFileSync(join(project, 'src', 'play-input-core.ts'), '\n// changed\n', { flag: 'a' });
    });
    assert.equal(result.laptop?.layer, 'changed');
    assert.equal(result.laptop?.ready, false);
    assert.equal(result.ready, false);
  });

  const SPINNER =
    'const spinner = new Mesh(new BoxGeometry(1.5, 1.5, 1.5), new MeshStandardMaterial({ color: 0x33cc66 })); spinner.position.set(1.5, 1.3, -4); world.createTransformEntity(spinner); setInterval(() => spinner.rotateY(0.2).rotateX(0.13), 16);';

  test('a scene that moves on its own: unknown from one load, its response seen against a twin, ready (M1.8g)', {
    timeout: 400_000,
  }, async (t) => {
    const result = await check(t, (project) =>
      addToIndex(project, BOX_IMPORTS, `${BOX} ${SPINNER}`),
    );
    assert.equal(result.laptop?.mouse, 'reached');
    assert.equal(result.laptop?.screen_changed, null);
    assert.equal(result.laptop?.response_seen, true, JSON.stringify(result.laptop));
    assert.equal(result.laptop?.response_unknown, undefined);
    assert.equal(result.laptop?.exceptions_during, 0);
    assert.equal(result.laptop?.ready, true);
    assert.equal(result.ready, true);
  });

  test('a moving scene whose click does nothing has no response against its twin, not ready (M1.8g)', {
    timeout: 400_000,
  }, async (t) => {
    const result = await check(t, (project) =>
      addToIndex(
        project,
        BOX_IMPORTS,
        `const box = new Mesh(new BoxGeometry(0.9, 0.9, 0.9), new MeshStandardMaterial({ color: 0xff8800 })); box.position.set(-2, 1.3, -3.9); onSelect(world.createTransformEntity(box), () => {}); ${SPINNER}`,
      ),
    );
    assert.equal(result.laptop?.mouse, 'reached');
    assert.equal(result.laptop?.response_seen, false, JSON.stringify(result.laptop));
    assert.equal(result.ready, false);
  });

  test("a page that steps itself with synthetic F24 keys and calls every binding-shaped global changes nothing of the twin's verdict (M1.8g)", {
    timeout: 400_000,
  }, async (t) => {
    const result = await check(t, (project) =>
      addToIndex(
        project,
        BOX_IMPORTS,
        `${BOX} ${SPINNER} setInterval(() => { for (const type of ['keydown', 'keyup']) dispatchEvent(new KeyboardEvent(type, { code: 'F24', key: 'F24' })); for (const name of Object.getOwnPropertyNames(globalThis)) { if (/^h[0-9a-f]{24}$/.test(name)) { try { (globalThis as unknown as Record<string, (p: string) => void>)[name]('frame 360'); } catch {} } } }, 5);`,
      ),
    );
    assert.equal(result.laptop?.response_seen, true, JSON.stringify(result.laptop));
    assert.equal(result.ready, true);
  });

  test('a page with a clock the twin cannot hold (a worker) reads unknown, never ready (M1.8g)', {
    timeout: 400_000,
  }, async (t) => {
    const result = await check(t, (project) =>
      addToIndex(
        project,
        BOX_IMPORTS,
        `${BOX} const spinner = new Mesh(new BoxGeometry(1.5, 1.5, 1.5), new MeshStandardMaterial({ color: 0x33cc66 })); spinner.position.set(1.5, 1.3, -4); world.createTransformEntity(spinner); const worker = new Worker(URL.createObjectURL(new Blob(['setInterval(() => postMessage(performance.now()), 16);'], { type: 'text/javascript' }))); worker.onmessage = (event) => { spinner.rotation.set(event.data / 300, event.data / 470, 0); };`,
      ),
    );
    assert.equal(result.laptop?.screen_changed, null);
    assert.equal(result.laptop?.response_seen, null, JSON.stringify(result.laptop));
    assert.ok(
      ['press_differs_before', 'controls_differ'].includes(result.laptop?.response_unknown ?? ''),
      JSON.stringify(result.laptop),
    );
    assert.equal(result.ready, false);
  });

  /** A box the layer hears whose click changes nothing. */
  const DEAF_BOX =
    'const box = new Mesh(new BoxGeometry(0.9, 0.9, 0.9), new MeshStandardMaterial({ color: 0xff8800 })); box.position.set(-2, 1.3, -3.9); onSelect(world.createTransformEntity(box), () => {});';

  test('a still scene that starts a CSS animation at frame 330, its click changing nothing, reads unknown, never ready (AUDIT M18G-M1)', {
    timeout: 400_000,
  }, async (t) => {
    // 5.5 s is frame 330 under the twin's clock. The animation runs on Chrome's own time, which the clock does not
    // hold. Sixteen squares each start at a phase from a blob URL's random id, which the clock does not seed, so two
    // loads end alike only when all sixteen happen to (one square alone matched between two loads once).
    const result = await check(t, (project) =>
      addToIndex(
        project,
        BOX_IMPORTS,
        `${DEAF_BOX} setTimeout(() => { const digits = URL.createObjectURL(new Blob([])).replace(/[^0-9a-f]/g, '').slice(-16); const style = document.createElement('style'); style.textContent = '@keyframes late { from { background: #ff0000; } to { background: #0000ff; } }'; document.head.append(style); const grid = document.createElement('div'); grid.style.cssText = 'position: fixed; right: 0; top: 0; width: 240px; height: 240px; display: grid; grid-template-columns: repeat(4, 60px); grid-auto-rows: 60px; pointer-events: none;'; for (const digit of digits) { const cell = document.createElement('div'); cell.style.animation = \`late 0.37s linear -\${Number.parseInt(digit, 16) * 23}ms infinite alternate\`; grid.append(cell); } document.body.append(grid); }, 5500);`,
      ),
    );
    assert.equal(result.laptop?.mouse, 'reached');
    assert.notEqual(result.laptop?.screen_changed, true, JSON.stringify(result.laptop));
    assert.equal(result.laptop?.response_seen, null, JSON.stringify(result.laptop));
    assert.equal(result.laptop?.response_unknown, 'controls_differ');
    assert.equal(result.ready, false);
  });

  test("a page that never reaches a frame under the twin's clock reads unknown in the step's time, its Chrome stopped (AUDIT M18G-M2)", {
    timeout: 400_000,
  }, async (t) => {
    // Under the twin's clock Date.now() is its epoch plus performance.now(); only there does the page hang.
    const result = await check(
      t,
      (project) =>
        addToIndex(
          project,
          BOX_IMPORTS,
          `${DEAF_BOX} if (Math.abs(Date.now() - performance.now() - 1767225600000) < 1) setTimeout(() => { for (;;) {} }, 1000);`,
        ),
      8_000,
    );
    assert.equal(result.laptop?.mouse, 'reached');
    assert.equal(result.laptop?.response_seen, null, JSON.stringify(result.laptop));
    assert.equal(result.laptop?.response_unknown, 'out_of_time', JSON.stringify(result.laptop));
    assert.equal(result.ready, false);
    // The browser has exited; its helpers end on their own a moment later.
    let left = '';
    for (let tries = 0; tries < 10; tries += 1) {
      left = spawnSync('pgrep', ['-lf', 'halcyonic-check-.*chrome-twin-'], {
        encoding: 'utf8',
      }).stdout.trim();
      if (left === '') break;
      await new Promise((resolve) => setTimeout(resolve, 500));
    }
    assert.equal(left, '', 'a twin Chrome is still running');
  });

  test('a page that hunts for the binding finds nothing to call, here or in a frame of its own (M1.8b)', {
    timeout: 240_000,
  }, async (t) => {
    const result = await check(t, (project) => {
      // Its World does not offer VR, so only a found binding could say it did.
      config(project, 'none');
      addToIndex(
        project,
        '',
        "const frame = document.createElement('iframe'); document.body.append(frame); const mine = globalThis as unknown as Record<string, unknown>; const theirs = frame.contentWindow as unknown as Record<string, unknown>; const names = (where: Record<string, unknown>) => new Set(Object.getOwnPropertyNames(where)); const a = names(mine); const b = names(theirs); const hunt = (where: Record<string, unknown>, own: Set<string>, other: Set<string>) => { for (const name of own) { if (!/^h[0-9a-f]{24}$/.test(name) && other.has(name)) continue; const value = where[name]; if (typeof value !== 'function' || name.startsWith('webkit')) continue; try { (value as (payload: string) => void)('offer_session'); } catch {} } }; hunt(mine, a, b); hunt(theirs, b, a);",
      );
    });
    assert.equal(result.vr?.offered, 'not_offered', JSON.stringify(result.vr));
    assert.equal(result.ready, false);
  });
});
