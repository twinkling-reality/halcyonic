import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import {
  cpSync,
  existsSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { join, relative } from 'node:path';
import { after, before, describe, type TestContext, test } from 'node:test';
import {
  type CheckStep,
  DRAWN_EDGES,
  drawnFrom,
  OWN_DRAWN,
  type OwnReference,
  type PageFindings,
  passes,
  runCheck,
  summarizeExceptions,
} from './check.ts';
import { installLauncher, launcherFor } from './launcher.ts';
import { browserBinary, browserProblem } from './play.ts';

const ok: CheckStep = { exit: 0, seconds: 1 };
const page = (findings: Partial<PageFindings>): PageFindings => ({
  exceptions: [],
  exception_count: 0,
  console_errors: 0,
  canvases: 1,
  edges: { at_3s: 0, at_10s: 0 },
  drawn: true,
  own: { share: 0.5, reference: 'compared', frame: null },
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
    const own = { share: OWN_DRAWN * 2, reference: 'compared' } as const;
    assert.ok(!drawnFrom(0, 0, own));
    assert.ok(!drawnFrom(null, null, own));
    assert.ok(!drawnFrom(DRAWN_EDGES, DRAWN_EDGES, own));
    assert.ok(drawnFrom(DRAWN_EDGES + 1, 0, own));
    assert.ok(drawnFrom(null, 7451, own));
  });

  test("drawn needs pixels of the project's own too: the starter's scenery alone never draws, nothing is assumed (DRAWN)", () => {
    const own = (
      share: number | null,
      reference: 'compared' | 'other_template' | 'unavailable' = 'compared',
    ) => ({ share, reference });
    // The starter's island, sky and decor: edges by the thousand, nothing of a game's own.
    assert.ok(!drawnFrom(56_000, 56_000, own(0)));
    assert.ok(!drawnFrom(56_000, 56_000, own(OWN_DRAWN)));
    assert.ok(!drawnFrom(56_000, 56_000, own(null)));
    assert.ok(drawnFrom(56_000, 56_000, own(OWN_DRAWN * 1.5)));
    // Pixels of its own are no picture without edges: a white or black page differs everywhere.
    assert.ok(!drawnFrom(0, 0, own(1)));
    // Another template than the tracked one: the edges alone, as before DRAWN.
    assert.ok(drawnFrom(56_000, 56_000, own(null, 'other_template')));
    assert.ok(!drawnFrom(DRAWN_EDGES, 0, own(null, 'other_template')));
    // No starter's picture to compare with: never drawn.
    assert.ok(!drawnFrom(56_000, 56_000, own(null, 'unavailable')));
    assert.ok(!drawnFrom(56_000, 56_000, own(0.5, 'unavailable')));
  });

  test('reports at most five distinct exceptions, each its first two lines, whole for the control plane to redact and then cut (P1b)', () => {
    const many = Array.from(
      { length: 9 },
      (_, n) => `Uncaught TypeError ${n % 6}\n    at a (x.ts:1)\n    at b (y.ts:2)`,
    );
    const summary = summarizeExceptions(many);
    assert.equal(summary.length, 5);
    assert.equal(summary[0], 'Uncaught TypeError 0 at a (x.ts:1)');
    assert.equal(summarizeExceptions([`E ${'x'.repeat(1000)}`])[0]?.length, 1002);
  });
});

// The whole check against a real Chrome and an installed starter set: opt-in, since the repository does not install
// the starter. HALCYONIC_TEST_STARTER names the installed folder (node_modules, input-profiles), HALCYONIC_TEST_BROWSER
// the Chrome binary. Port 47886, or HALCYONIC_TEST_CHECK_PORT.
const STARTER = process.env.HALCYONIC_TEST_STARTER ?? '';
const BROWSER = process.env.HALCYONIC_TEST_BROWSER ?? '';
const BROWSER_SHA256 = process.env.HALCYONIC_TEST_BROWSER_SHA256 ?? '';
const PORT = Number(process.env.HALCYONIC_TEST_CHECK_PORT ?? '47886');
const SKIP =
  STARTER === '' || BROWSER === '' || BROWSER_SHA256 === ''
    ? 'HALCYONIC_TEST_STARTER, HALCYONIC_TEST_BROWSER and HALCYONIC_TEST_BROWSER_SHA256 are not set'
    : process.platform !== 'darwin' && 'Seatbelt is macOS only';

describe('the check, end to end with the installed starter and Chrome', { skip: SKIP }, () => {
  // The check's browser, chrome-headless-shell, started through a launcher built here (S1).
  const launcherData = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-check-launcher-')));
  let launcher: string | null = null;
  /** The pristine starter's own picture, made in `before` as the host makes it (DRAWN). */
  let reference: OwnReference = { kind: 'unavailable', why: 'not made' };
  const template = new URL('../../../../starters/iwsdk-vr/template/', import.meta.url).pathname;
  const host = new URL('../../../../starters/iwsdk-vr/host/', import.meta.url).pathname;
  const references = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-check-reference-')));
  before(async () => {
    assert.equal(browserProblem(BROWSER, BROWSER_SHA256), null);
    await installLauncher({ dataDir: launcherData, clang: '/usr/bin/clang' });
    launcher = launcherFor(launcherData);
    assert.notEqual(launcher, null);
    const project = join(references, 'project');
    cpSync(template, project, { recursive: true });
    symlinkSync(join(STARTER, 'node_modules'), join(project, 'node_modules'));
    const made = await runCheck({
      ...options(project, join(references, 'cache')),
      reference: { kind: 'making' },
    });
    const frame = made.page?.own.frame ?? null;
    assert.notEqual(frame, null, JSON.stringify(made.unknown));
    reference = { kind: 'compared', png: readFileSync(frame?.file ?? '') };
  });
  after(() => {
    rmSync(launcherData, { recursive: true, force: true });
    rmSync(references, { recursive: true, force: true });
  });
  /** What every check here is given, but its reference. */
  const options = (project: string, cache: string) => ({
    node: process.execPath,
    chrome: browserBinary(BROWSER),
    launcher,
    home: homedir(),
    project,
    starter: STARTER,
    hostSource: host,
    template,
    cache,
    profiles: existsSync(join(STARTER, 'input-profiles')) ? join(STARTER, 'input-profiles') : null,
    port: PORT,
  });
  const check = async (t: TestContext, change: (project: string) => void, twinStepMs?: number) => {
    const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-check-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const project = join(root, 'project');
    cpSync(template, project, { recursive: true });
    symlinkSync(join(STARTER, 'node_modules'), join(project, 'node_modules'));
    change(project);
    const cache = join(root, 'cache');
    const result = await runCheck({
      node: process.execPath,
      chrome: browserBinary(BROWSER),
      launcher,
      home: homedir(),
      project,
      starter: STARTER,
      hostSource: host,
      template,
      cache,
      profiles: existsSync(join(STARTER, 'input-profiles'))
        ? join(STARTER, 'input-profiles')
        : null,
      port: PORT,
      reference,
      ...(twinStepMs === undefined ? {} : { twinStepMs }),
    });
    return { ...result, cache };
  };
  /** A folder outside the project, the starter and the home folder, as `/private/tmp` or a drive is. */
  const outsideFolder = (t: TestContext) => {
    const path = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-check-outside-')));
    t.after(() => rmSync(path, { recursive: true, force: true }));
    return path;
  };
  /** Every file the build left in the copy it serves, as text, or nothing when it made none. */
  const builtCopy = (cache: string) =>
    existsSync(join(cache, 'dist'))
      ? readdirSync(join(cache, 'dist'), { recursive: true, withFileTypes: true })
          .filter((entry) => entry.isFile())
          .map((entry) => readFileSync(join(entry.parentPath, entry.name), 'utf8'))
          .join('\n')
      : '';
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
    assert.equal(result.page?.own.reference, 'compared');
    assert.ok((result.page?.own.share ?? 1) <= OWN_DRAWN, String(result.page?.own.share));
    assert.equal(result.page?.drawn, false);
    assert.equal(result.passed, false);
    // Nothing to press, but the starter's World offers VR as it loads (M1.8b).
    assert.equal(result.laptop?.layer, 'starter');
    assert.equal(result.laptop?.mouse, 'no_target');
    assert.equal(result.vr?.offered, 'offered');
    assert.equal(result.ready, false);
    // The one note is the starter's own: every new game starts in the default world (W1).
    assert.equal(result.notes?.scanned, 'full');
    assert.deepEqual(
      result.notes?.items.map((item) => [item.kind, item.file]),
      [['default_world', 'src/index.ts']],
    );
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
    assert.ok((result.page?.own.share ?? 0) > OWN_DRAWN, String(result.page?.own.share));
    assert.equal(result.passed, true);
  });

  test('a game that names itself on its card but draws nothing of its own is not drawn (DRAWN)', {
    timeout: 180_000,
  }, async (t) => {
    const result = await check(t, (project) => {
      const card = join(project, 'public', 'ui', 'welcome.uikitml');
      writeFileSync(
        card,
        readFileSync(card, 'utf8')
          .replace('Untitled Game', 'Bonk the Moles')
          .replace(
            '<div id="card-line" class="sub-heading"></div>',
            '<div id="card-line" class="sub-heading">Bonk a mole when it pops up</div>',
          ),
      );
    });
    assert.equal(result.page?.exception_count, 0);
    assert.equal(result.page?.own.reference, 'compared');
    assert.ok((result.page?.own.share ?? 1) <= OWN_DRAWN, String(result.page?.own.share));
    assert.equal(result.page?.drawn, false);
    assert.equal(result.passed, false);
  });

  test("the type check reads no file outside its scope that the project's tsconfig names, so its log gives the agent none of it (AUDIT P1)", {
    timeout: 180_000,
  }, async (t) => {
    const outside = outsideFolder(t);
    const name = `halcyonicOutside${randomUUID().replaceAll('-', '')}`;
    writeFileSync(join(outside, 'secret.ts'), `export const b = ${name};\n`);
    const result = await check(t, (project) => {
      const file = join(project, 'tsconfig.json');
      const config = JSON.parse(readFileSync(file, 'utf8')) as Record<string, unknown>;
      writeFileSync(file, JSON.stringify({ ...config, files: [join(outside, 'secret.ts')] }));
    });
    // The failed type check's first lines go after the person's next words to the agent (FX1).
    const log = readFileSync(join(result.cache, 'tsc.log'), 'utf8');
    assert.notEqual(result.tsc.exit, 0, log);
    assert.ok(!log.includes(name), log);
  });

  test("the build reads no asset outside its scope that the project's source names, so the copy it serves holds none of it (AUDIT P1)", {
    timeout: 180_000,
  }, async (t) => {
    const outside = outsideFolder(t);
    const marker = `halcyonic-outside-${randomUUID()}`;
    writeFileSync(join(outside, 'marker.txt'), marker);
    const result = await check(t, (project) => {
      const asset = relative(join(project, 'src'), join(outside, 'marker.txt'));
      addToIndex(
        project,
        '',
        `console.log(new URL(${JSON.stringify(asset)}, import.meta.url).href);`,
      );
    });
    // Under 4 KiB Vite inlines an asset as base64; otherwise it copies it beside the page.
    const copy = builtCopy(result.cache);
    assert.ok(!copy.includes(marker), 'the marker is in the built copy');
    assert.ok(!copy.includes(Buffer.from(marker).toString('base64')), 'the marker is inlined');
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

  test("the preview's every response is never cached: its page and its script both say no-store (AUDIT M16A-C2)", {
    timeout: 180_000,
  }, async (t) => {
    const result = await check(t, (project) =>
      addToIndex(
        project,
        '',
        "Promise.all([fetch('./'), fetch(new URL(import.meta.url).href)]).then((answers) => { const bad = answers.filter((answer) => answer.headers.get('cache-control') !== 'no-store').map((answer) => answer.url + ' ' + answer.headers.get('cache-control')); if (bad.length > 0) setTimeout(() => { throw new Error('marker-cached ' + bad.join(', ')); }); });",
      ),
    );
    assert.equal(result.page?.exception_count, 0, JSON.stringify(result.page?.exceptions));
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
    const result = await check(t, (project) => {
      addToIndex(project, BOX_IMPORTS, BOX);
      // Since U3 the starter's scenery moves by itself, so one load cannot tell a press's change from it and the twin
      // replay sees the press instead (M1.8g's case). Held at its pose at time 0, the scenery stands still and this
      // case covers the single-load path: a still control pair, then the screen changed by the press. The movers still
      // run each frame, so the look still shows its canvas.
      const look = join(project, 'src', 'look.ts');
      const moving = 'for (const move of movers) move(time);';
      assert.ok(
        readFileSync(look, 'utf8').includes(moving),
        'the look moves its scenery in one loop',
      );
      writeFileSync(
        look,
        readFileSync(look, 'utf8').replace(moving, 'for (const move of movers) move(0);'),
      );
    });
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
