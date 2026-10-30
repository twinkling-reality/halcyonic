import assert from 'node:assert/strict';
import { cpSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { createContext, runInContext } from 'node:vm';
import { deflateSync } from 'node:zlib';
import {
  acceptCall,
  CHANGED_AFTER,
  type Driver,
  laptopStep,
  layerState,
  parseReport,
  STILL_CONTROL,
  screenChanged,
  standInScript,
} from './laptop-probe.ts';

const W = 200;
const H = 100;

/** A PNG of a grey picture with `lit` pixels of its first rows white; filter 0, no CRCs read. */
function picture(lit: number): Buffer {
  const raw = Buffer.alloc(H * (W * 4 + 1));
  for (let p = 0; p < W * H; p += 1) {
    const at = Math.floor(p / W) * (W * 4 + 1) + 1 + (p % W) * 4;
    raw.fill(p < lit ? 255 : 60, at, at + 3);
    raw[at + 3] = 255;
  }
  const chunk = (type: string, body: Buffer) => {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(body.length);
    return Buffer.concat([length, Buffer.from(type, 'latin1'), body, Buffer.alloc(4)]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(W, 0);
  header.writeUInt32BE(H, 4);
  header[8] = 8;
  header[9] = 6;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

interface Page {
  /** Whether the page has the hook at all. */
  hook?: boolean;
  targets?: { x: number; y: number }[];
  /** Whether a click reaches the layer, and how many pixels it changes. */
  reaches?: boolean;
  changes?: number;
  /** Pixels that change on their own between any two pictures. */
  moving?: number;
}

/** A page standing in for Chrome: what the hook reports and what each picture shows. */
function driver(page: Page) {
  const counts = { mouse: 0, key: 0, trigger: 0, pinch: 0, touch: 0 };
  let lit = 0;
  let frames = 0;
  const actions: string[] = [];
  const fake: Driver = {
    evaluate: async () =>
      page.hook === false ? null : JSON.stringify({ counts, targets: page.targets ?? [] }),
    async mouse(type, x, y) {
      actions.push(`${type} ${x},${y}`);
      if (type === 'mouseReleased' && page.reaches !== false) {
        counts.mouse += 1;
        lit += page.changes ?? 0;
      }
    },
    async key(code) {
      actions.push(code);
      counts.key += 1;
    },
    async picture() {
      frames += 1;
      return picture(lit + (frames % 2) * (page.moving ?? 0));
    },
    wait: async () => {},
  };
  return { fake, actions };
}

const AT = { x: 640, y: 400 };
const MANY = Math.ceil(W * H * CHANGED_AFTER) + 50;

describe("the check's laptop step (M1.8b)", () => {
  test('a press that reaches the layer and changes the screen, with a still control pair, is ready', async () => {
    const { fake, actions } = driver({ targets: [AT], changes: MANY });
    const laptop = await laptopStep(fake, 'starter');
    assert.deepEqual(laptop, {
      layer: 'starter',
      targets: 1,
      mouse: 'reached',
      key: 'reached',
      screen_changed: true,
      ready: true,
    });
    assert.deepEqual(actions, [
      'mouseMoved 640,400',
      'mousePressed 640,400',
      'mouseReleased 640,400',
      'Space',
      'KeyE',
    ]);
  });

  test('a press the layer reports but the screen does not show is not ready', async () => {
    const { fake } = driver({ targets: [AT], changes: 5 });
    const laptop = await laptopStep(fake, 'starter');
    assert.equal(laptop.mouse, 'reached');
    assert.equal(laptop.screen_changed, false);
    assert.equal(laptop.ready, false);
  });

  test('a scene that moves on its own is unknown, never ready, however much the press changes', async () => {
    const { fake } = driver({
      targets: [AT],
      changes: MANY,
      moving: Math.ceil(W * H * STILL_CONTROL) + 10,
    });
    const laptop = await laptopStep(fake, 'starter');
    assert.equal(laptop.screen_changed, null);
    assert.equal(laptop.ready, false);
  });

  test('a changed layer is never ready, even when everything else is', async () => {
    const { fake } = driver({ targets: [AT], changes: MANY });
    const laptop = await laptopStep(fake, 'changed');
    assert.equal(laptop.screen_changed, true);
    assert.equal(laptop.ready, false);
  });

  test('nothing to press, a press that never reaches, and no hook at all are each said as they are', async () => {
    assert.equal((await laptopStep(driver({ targets: [] }).fake, 'starter')).mouse, 'no_target');
    const missed = driver({
      targets: [AT, { x: 10, y: 10 }, { x: 20, y: 20 }, { x: 30, y: 30 }],
      reaches: false,
    });
    const notReached = await laptopStep(missed.fake, 'starter');
    assert.equal(notReached.mouse, 'not_reached');
    assert.equal(missed.actions.filter((action) => action.startsWith('mousePressed')).length, 3);
    const none = await laptopStep(driver({ hook: false }).fake, 'starter');
    assert.deepEqual(none, {
      layer: 'starter',
      targets: 0,
      mouse: 'unknown',
      key: 'unknown',
      screen_changed: null,
      ready: false,
    });
  });

  test("the project's input layer is the starter's only when both files are byte for byte the template's", async (t) => {
    const template = new URL('../../../../starters/iwsdk-vr/template/', import.meta.url).pathname;
    const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-layer-')));
    t.after(() => rmSync(root, { recursive: true, force: true }));
    const project = join(root, 'project');
    cpSync(join(template, 'src'), join(project, 'src'), { recursive: true });
    assert.equal(await layerState(project, template), 'starter');
    writeFileSync(join(project, 'src', 'play-input-core.ts'), '// changed\n', { flag: 'a' });
    assert.equal(await layerState(project, template), 'changed');
    rmSync(join(project, 'src', 'play-input-core.ts'));
    assert.equal(await layerState(project, template), 'missing');
    // A link to the template's own file is not the project's file.
    symlinkSync(
      join(template, 'src', 'play-input-core.ts'),
      join(project, 'src', 'play-input-core.ts'),
    );
    assert.equal(await layerState(project, template), 'missing');
  });

  test("the hook's report is read in the host against a strict shape", () => {
    const counts = { mouse: 1, key: 0, trigger: 0, pinch: 0, touch: 0 };
    const ok = parseReport(JSON.stringify({ counts, targets: [AT] }));
    assert.deepEqual(ok, { counts, targets: [AT] });
    for (const bad of [
      null,
      42,
      'not json',
      JSON.stringify({ counts, targets: [AT] }).padEnd(17_000, ' '),
      JSON.stringify({ counts: { ...counts, mouse: -1 }, targets: [] }),
      JSON.stringify({ counts: { ...counts, mouse: 1.5 }, targets: [] }),
      JSON.stringify({ counts: { mouse: 1 }, targets: [] }),
      JSON.stringify({ counts, targets: [{ x: 'a', y: 1 }] }),
      JSON.stringify({ counts, targets: [{ x: 5000, y: 1 }] }),
      JSON.stringify({ counts, targets: Array.from({ length: 33 }, () => AT) }),
    ]) {
      assert.equal(parseReport(bad), null, String(bad).slice(0, 60));
    }
  });

  test('the screen changed only above its share, with the control at most its own', () => {
    assert.equal(screenChanged(0, CHANGED_AFTER + 0.001), true);
    assert.equal(screenChanged(STILL_CONTROL, CHANGED_AFTER), false);
    assert.equal(screenChanged(STILL_CONTROL + 0.0001, 0.5), null);
    assert.equal(screenChanged(null, 0.5), null);
    assert.equal(screenChanged(0, null), null);
  });
});

describe("the VR-entry step's stand-in WebXR (M1.8b)", () => {
  /** Runs the stand-in as a page would see it, with the binding Chrome adds before it. */
  function page(binding: string) {
    const sent: unknown[] = [];
    class Navigator {}
    const context = createContext({
      Navigator,
      [binding]: (payload: unknown) => sent.push(payload),
    });
    runInContext(standInScript(binding), context);
    const xr = Object.getOwnPropertyDescriptor(Navigator.prototype, 'xr')?.get?.() as Record<
      string,
      (...args: unknown[]) => unknown
    >;
    return { context, sent, xr };
  }

  test('reports an offer or a request of immersive-vr, and nothing else', async () => {
    const { sent, xr } = page('h0123456789abcdef01234567');
    assert.equal(await xr.isSessionSupported?.('immersive-vr'), true);
    assert.equal(await xr.isSessionSupported?.('immersive-ar'), false);
    void xr.offerSession?.('immersive-ar');
    void xr.offerSession?.({ toString: () => 'immersive-vr' });
    assert.deepEqual(sent, []);
    void xr.offerSession?.('immersive-vr');
    void xr.requestSession?.('immersive-vr');
    assert.deepEqual(sent, ['offer_session', 'request_session']);
  });

  test("takes the binding off the page's global object, and names it in no function the page can read", () => {
    const binding = 'h0123456789abcdef01234567';
    const { context, xr } = page(binding);
    assert.equal(Object.hasOwn(context, binding), false);
    for (const fn of Object.values(xr)) {
      if (typeof fn === 'function') assert.ok(!String(fn).includes(binding), String(fn));
    }
  });

  test('the host keeps only the two calls from the binding', () => {
    assert.equal(acceptCall('offer_session'), 'offer_session');
    assert.equal(acceptCall('request_session'), 'request_session');
    for (const other of ['immersive-vr', 'offer_session ', '', null, 1, { how: 'offer_session' }])
      assert.equal(acceptCall(other), null);
  });
});
