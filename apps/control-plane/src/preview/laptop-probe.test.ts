import assert from 'node:assert/strict';
import { cpSync, mkdtempSync, realpathSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { PassThrough } from 'node:stream';
import { describe, test } from 'node:test';
import { createContext, runInContext } from 'node:vm';
import { deflateSync } from 'node:zlib';
import { type ChromeRun, DevToolsPipe } from './chrome.ts';
import {
  acceptCall,
  CHANGED_AFTER,
  clockScript,
  type Driver,
  laptopStep,
  layerState,
  parseReport,
  STILL_CONTROL,
  screenChanged,
  settledPicture,
  standInScript,
  TWIN_FRAMES,
  TWIN_QUIET_MS,
  TWIN_TARGETS,
  type TwinLoad,
  TwinOutOfTime,
  twinLoad,
  twinStep,
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

  test('presses where the target is right before the press, not where it was before the control pair (M1.8g)', async () => {
    const counts = { mouse: 0, key: 0, trigger: 0, pinch: 0, touch: 0 };
    let pictures = 0;
    const pressed: string[] = [];
    const moving: Driver = {
      // The target moves once the control pair has been taken.
      evaluate: async () =>
        JSON.stringify({
          counts,
          targets: [pictures >= 2 ? { x: 900, y: 300 } : { x: 100, y: 100 }],
        }),
      async mouse(type, x, y) {
        if (type === 'mousePressed') pressed.push(`${x},${y}`);
        if (type === 'mouseReleased') counts.mouse += 1;
      },
      key: async () => {},
      async picture() {
        pictures += 1;
        return picture(0);
      },
      wait: async () => {},
    };
    await laptopStep(moving, 'starter');
    assert.deepEqual(pressed, ['900,300']);
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

const BINDING = 'h00112233445566778899aabbcc';

/**
 * Runs the clock as a page would see it, before the page's scripts; the host holds its frames. `setup` runs in the
 * page's realm first, as the browser's own globals would be there.
 */
function page(setup = '') {
  const sent: string[] = [];
  type Key = { isTrusted: boolean; code: string; type: string };
  const listeners: { type: string; fn: (event: Key) => void }[] = [];
  let tick: (() => void) | null = null;
  const context = createContext({
    [BINDING]: (payload: string) => sent.push(payload),
    performance: { now: () => 12345 },
    crypto: { getRandomValues: () => 0, randomUUID: () => 'real' },
    requestAnimationFrame: (cb: () => void) => {
      tick = cb;
      return 1;
    },
    addEventListener: (type: string, fn: (event: Key) => void, capture: boolean) => {
      if (capture) listeners.push({ type, fn });
    },
    Date,
    queueMicrotask,
    setTimeout,
    setInterval,
    clearTimeout,
    clearInterval,
    cancelAnimationFrame: () => {},
  });
  // The browser's Event, whose methods a page can replace.
  runInContext(
    'globalThis.Event = class Event { stopImmediatePropagation() {} preventDefault() {} };',
    context,
  );
  if (setup !== '') runInContext(setup, context);
  const before = new Set(Object.getOwnPropertyNames(context));
  runInContext(clockScript(BINDING), context);
  const added = Object.getOwnPropertyNames(context).filter((name) => !before.has(name));
  // Values from the page's realm, read back as plain data.
  const run = (code: string) => {
    const value = runInContext(code, context) as unknown;
    return typeof value === 'object' && value !== null ? JSON.parse(JSON.stringify(value)) : value;
  };
  const frames = (n: number) => {
    for (let i = 0; i < n; i += 1) (tick as unknown as () => void)();
  };
  /** A key event to the listeners for `type`; `shown` is the type the event itself says it is. */
  const key = (isTrusted: boolean, type = 'keydown', shown = type) => {
    const proto = runInContext('Event.prototype', context) as object;
    const event = Object.assign(Object.create(proto), {
      isTrusted,
      code: 'F24',
      type: shown,
    }) as Key;
    for (const listener of listeners) if (listener.type === type) listener.fn(event);
  };
  return { context, sent, added, run, frames, key };
}

describe("the twin step's stepped clock (M1.8g)", () => {
  test('leaves no global behind, takes the binding off the page, and names it nowhere the page can read', () => {
    const { context, added } = page();
    assert.deepEqual(added, []);
    assert.equal(Object.hasOwn(context, BINDING), false);
    for (const name of [
      'setTimeout',
      'requestAnimationFrame',
      'Date',
      'performance.now',
      'Math.random',
      'crypto.getRandomValues',
    ])
      assert.ok(!String(runInContext(name, context)).includes(BINDING), name);
  });

  test('time and frames move only when the host steps them, with a trusted F24, never a synthetic one', () => {
    const { sent, run, frames, key } = page();
    frames(50);
    assert.equal(run('performance.now()'), 0, 'held at frame 0 until the host steps');
    key(false);
    frames(50);
    assert.equal(run('performance.now()'), 0, 'a synthetic F24 does nothing');
    key(true, 'keyup');
    frames(50);
    assert.equal(run('performance.now()'), 0, 'only the key going down steps');
    key(true);
    frames(400);
    const [first] = TWIN_FRAMES;
    assert.equal(run('performance.now()'), (first * 1000) / 60);
    assert.equal(run('Date.now()'), 1767225600000 + (first * 1000) / 60);
    // Every frame is reported, so the host sees a slow page still moving (M1.8g-b), and none past the stop.
    assert.deepEqual(
      sent,
      Array.from({ length: first }, (_, index) => `frame ${index + 1}`),
    );
  });

  test("timers and the page's animation frames run on stepped frames, in order", () => {
    const { run, frames, key } = page();
    run(`globalThis.log = []; setTimeout(() => log.push('b'), 100); setTimeout(() => log.push('a'), 50);
      requestAnimationFrame((t) => log.push('frame ' + t)); const i = setInterval(() => log.push('i'), 1000 / 60 * 2);`);
    key(true);
    frames(4);
    assert.deepEqual(run('log'), ['frame 16.666666666666668', 'i', 'a', 'i']);
    frames(4);
    assert.ok((run('log') as string[]).includes('b'));
  });

  test('every source of chance is seeded: two pages draw the same numbers, bytes and ids', () => {
    const draw = () => {
      const { run } = page();
      return run(
        `[Math.random(), Array.from(crypto.getRandomValues(new Uint8Array(8))), crypto.randomUUID()]`,
      );
    };
    const a = draw() as [number, number[], string];
    assert.deepEqual(a, draw());
    assert.match(a[2], /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  });
});

const FRAME = 1000 / 60;
/**
 * Stand-ins for the browser's download functions, in the page's realm. Each download waits in `__waiting` until the
 * test lets it finish with `finish()`; events it fires are trusted, as the browser's are.
 */
const DOWNLOADS = `
  globalThis.__waiting = [];
  const wait = (what, done) => __waiting.push({ what, done });
  const fire = (target, type) => { for (const [t, fn] of target.l ?? []) if (t === type) fn({ type, isTrusted: true }); };
  class Listening { addEventListener(type, fn) { (this.l ??= []).push([type, fn]); } }
  globalThis.Response = class Response { constructor(url) { this.url = url; }
    json() { return new Promise((resolve) => wait(this.url + ' body', () => resolve({ from: this.url }))); } };
  globalThis.fetch = (url) => new Promise((resolve) => wait(url, () => resolve(new Response(url))));
  globalThis.createImageBitmap = () => new Promise((resolve) => wait('bitmap', () => resolve({})));
  globalThis.XMLHttpRequest = class extends Listening { open(method, url) { this.url = url; }
    send() { wait(this.url, () => fire(this, 'loadend')); } };
  globalThis.HTMLImageElement = class extends Listening {
    get src() { return this.s; }
    set src(value) { this.s = value; wait(value, () => fire(this, 'load')); }
    decode() { return new Promise((resolve) => wait('decode', () => resolve())); } };
`;
/** Lets every waiting download finish, then runs the page's callbacks. */
const finish = async (run: (code: string) => unknown) => {
  run('for (const item of __waiting.splice(0)) item.done();');
  for (let i = 0; i < 5; i += 1) await new Promise((resolve) => setImmediate(resolve));
};
const stepped = (run: (code: string) => unknown) =>
  Math.round((run('performance.now()') as number) / FRAME);

describe("the stepped clock holds its frames while the page's own downloads are pending (M1.8g-d)", () => {
  test('frames hold while a fetch, and the body read after it, are pending; the data lands before the next frame', async () => {
    const { run, frames, key } = page(DOWNLOADS);
    key(true);
    frames(3);
    run(
      `fetch('model.glb').then((r) => r.json()).then(() => { globalThis.landed = performance.now(); });`,
    );
    frames(10);
    assert.equal(stepped(run), 3, 'held while the fetch is pending');
    await finish(run);
    frames(10);
    assert.equal(stepped(run), 3, 'held while its body is read');
    await finish(run);
    assert.equal(Math.round((run('landed') as number) / FRAME), 3);
    frames(10);
    assert.equal(stepped(run), 13);
  });

  test("an XHR, an image's src, createImageBitmap and an image's decode each hold the frames until they finish", async () => {
    for (const start of [
      "const x = new XMLHttpRequest(); x.open('GET', 'a.bin'); x.send();",
      "new HTMLImageElement().src = 'a.png';",
      'createImageBitmap({});',
      'new HTMLImageElement().decode();',
    ]) {
      const { run, frames, key } = page(DOWNLOADS);
      key(true);
      run(start);
      frames(10);
      assert.equal(stepped(run), 0, start);
      await finish(run);
      frames(10);
      assert.equal(stepped(run), 10, start);
    }
  });

  test('two loads whose download takes a different real time land it at the same frame', async () => {
    const landedAt = async (delayMs: number) => {
      const { run, frames, key } = page(DOWNLOADS);
      run(
        `setTimeout(() => fetch('late.json').then((r) => r.json()).then(() => { globalThis.landed = performance.now(); }), 50);`,
      );
      key(true);
      const started = Date.now();
      for (let i = 0; i < 150; i += 1) {
        frames(1);
        await new Promise((resolve) => setTimeout(resolve, 2));
        if (Date.now() - started > delayMs) await finish(run);
      }
      return Math.round((run('landed') as number) / FRAME);
    };
    const fast = await landedAt(5);
    const slow = await landedAt(120);
    assert.equal(fast, slow);
    assert.equal(fast, 3);
  });

  test("the page cannot end a hold early: not by replacing Promise's methods, nor by firing its own load events", async () => {
    const { run, frames, key } = page(DOWNLOADS);
    key(true);
    run(`
      Promise.prototype.then = function (ok) { if (typeof ok === 'function') ok(); return this; };
      Promise.prototype.finally = function (fn) { if (typeof fn === 'function') fn(); return this; };
      fetch('a.glb');
      const image = new HTMLImageElement();
      image.src = 'b.png';
      for (const [type, fn] of image.l ?? []) fn({ type: 'load', isTrusted: false });
    `);
    frames(10);
    assert.equal(stepped(run), 0);
  });
});

describe('the stepped clock gives a page nothing to call (AUDIT M18GD-M1, M18GD-M2)', () => {
  /** A forged F24 key down, as a page would build one. */
  const FORGE =
    "({ isTrusted: true, code: 'F24', type: 'keydown', stopImmediatePropagation() {}, preventDefault() {} })";

  test("a forged call through a replaced Event method's caller leaves the clock where the host put it", () => {
    const { run, frames, key } = page();
    run(`
      const forge = function forge() { const caller = forge.caller; if (typeof caller === 'function') caller(${FORGE}); };
      Event.prototype.stopImmediatePropagation = forge;
      Event.prototype.preventDefault = forge;
    `);
    key(true);
    frames(400);
    assert.equal(stepped(run), TWIN_FRAMES[0]);
  });

  test("a forged call through the caller of a page's timer, animation frame or a replaced collection method does nothing", () => {
    const { run, frames, key } = page();
    run(`
      globalThis.callers = [];
      const reach = (fn) => { try { const caller = fn.caller; callers.push(typeof caller); if (typeof caller === 'function') caller(${FORGE}); } catch { callers.push('refused'); } };
      setTimeout(function later() { reach(later); }, 20);
      requestAnimationFrame(function drawn() { reach(drawn); });
      for (const C of [Set, Map]) {
        const size = Object.getOwnPropertyDescriptor(C.prototype, 'size');
        Object.defineProperty(C.prototype, 'size', { get: function sized() { reach(sized); return size.get.call(this); } });
      }
    `);
    key(true);
    frames(400);
    assert.equal(stepped(run), TWIN_FRAMES[0]);
    const callers = run('callers') as string[];
    assert.ok(callers.length > 0 && !callers.includes('function'), JSON.stringify(callers));
  });

  test('a key going up steps nothing, whatever type the event says it is', () => {
    const { run, frames, key } = page();
    key(true, 'keyup', 'keydown');
    key(true, 'keypress', 'keydown');
    frames(400);
    assert.equal(stepped(run), 0);
  });

  test("a page that poisons promises' species cannot end a hold: it stays held, and the stall limit ends it (AUDIT M18GD-A-M1)", async () => {
    const { run, frames, key } = page(DOWNLOADS);
    key(true);
    run(`
      Object.defineProperty(Promise.prototype, 'constructor', {
        get() { return { get [Symbol.species]() { throw new Error('poisoned'); } }; },
      });
      fetch('model.glb');
    `);
    frames(10);
    assert.equal(stepped(run), 0);
    await finish(run);
    frames(10);
    assert.equal(
      stepped(run),
      0,
      'held for good: the host reads out of time once the page stops reaching frames',
    );
  });

  test('a page that replaces Set and Map methods cannot end a hold: it lasts until the browser answers', async () => {
    const { run, frames, key } = page(DOWNLOADS);
    key(true);
    run(`
      for (const C of [Set, Map]) {
        C.prototype.add = function () { this.clear(); return this; };
        C.prototype.delete = function () { return true; };
        Object.defineProperty(C.prototype, 'size', { get() { return 0; } });
      }
      fetch('model.glb');
    `);
    frames(10);
    assert.equal(stepped(run), 0);
    await finish(run);
    frames(10);
    assert.equal(stepped(run), 10);
  });
});

describe('the twin step: a press seen against its no-input twin (M1.8g)', () => {
  const load = (before: number, end: number, extra: Partial<TwinLoad> = {}): TwinLoad => ({
    before: picture(before),
    end: picture(end),
    targets: 3,
    reached: true,
    ...extra,
  });
  /** A step whose twin and control are `twin` and whose presses are `presses`, in order; counts the loads. */
  const step = (twin: TwinLoad, presses: TwinLoad[], control: TwinLoad = twin) => {
    const asked: (number | null)[] = [];
    return {
      asked,
      run: () =>
        twinStep(async (target) => {
          asked.push(target);
          if (target !== null) return presses[target] ?? twin;
          return asked.length === 1 ? twin : control;
        }),
    };
  };
  const seen = { seen: true, unknown: null };
  const notSeen = { seen: false, unknown: null };
  const unknown = (reason: string) => ({ seen: null, unknown: reason });
  // 200 by 100 pictures: 0.05% is 10 pixels, 0.01% is 2.
  test('seen when a press that reached the layer changes the end frame beyond the twin', async () => {
    const { run, asked } = step(load(0, 0), [load(0, 0), load(0, 50)]);
    assert.deepEqual(await run(), seen);
    // The twin, each press, then the no-input control and the seen press once more (AUDIT M18G-S1).
    assert.deepEqual(asked, [null, 0, 1, null, 1]);
  });

  test('not seen when no press changes the end frame, after every target up to the limit and the control', async () => {
    const { run, asked } = step(load(0, 0, { targets: 9 }), []);
    assert.deepEqual(await run(), notSeen);
    assert.equal(asked.length, 2 + TWIN_TARGETS);
    assert.equal(asked.at(-1), null);
  });

  test('a change the layer did not hear is not a response', async () => {
    const { run } = step(load(0, 0), [load(0, 50, { reached: false })]);
    assert.deepEqual(await run(), notSeen);
  });

  test('unknown, and why: a press run apart from its twin before any input, or nothing to press or compare', async () => {
    assert.deepEqual(await step(load(0, 0), [load(5, 50)]).run(), unknown('press_differs_before'));
    assert.deepEqual(await step(load(0, 0, { targets: 0 }), []).run(), unknown('failed'));
    assert.deepEqual(await step(load(0, 0, { before: null }), []).run(), unknown('failed'));
    const broken = await twinStep(async () => {
      throw new Error('Chrome did not start');
    });
    assert.deepEqual(broken, unknown('failed'));
  });

  test('a load that stopped making progress reads out of time, not failed', async () => {
    const stalled = await twinStep(async () => {
      throw new TwinOutOfTime('the page did not reach frame 300');
    });
    assert.deepEqual(stalled, unknown('out_of_time'));
  });

  test('unknown when two loads with no input end apart, however little the press changes (AUDIT M18G-M1)', async () => {
    // Each load ends a little further on, input or not, as a CSS animation started after frame 300 would.
    let loads = 0;
    const drifting = async (target: number | null) => {
      loads += 1;
      return load(0, 20 * loads, { reached: target !== null });
    };
    assert.deepEqual(await twinStep(drifting), unknown('controls_differ'));
    assert.equal(loads, 3);
    // The control must also match the twin before any input, and even when no press read seen.
    const before = step(load(0, 0), [load(0, 50)], load(5, 0));
    assert.deepEqual(await before.run(), unknown('controls_differ'));
    assert.deepEqual(before.asked, [null, 0, null]);
    assert.deepEqual(await step(load(0, 0), [], load(0, 30)).run(), unknown('controls_differ'));
  });

  test('unknown when the same press, loaded again, ends otherwise: the change was not its own', async () => {
    let presses = 0;
    const result = await twinStep(async (target) => {
      if (target === null) return load(0, 0);
      presses += 1;
      return load(0, presses === 1 ? 50 : 90);
    });
    assert.deepEqual(result, unknown('repeat_differs'));
    assert.equal(presses, 2);
  });

  test('a step past its time reads unknown, after its load was told to stop and stopped (AUDIT M18G-M2)', {
    timeout: 10_000,
  }, async () => {
    const states: { aborted: boolean; settled: boolean }[] = [];
    // A page that never reaches a frame: the load waits until it is told to stop, then stops.
    const never = (_target: number | null, signal?: AbortSignal) => {
      const state = { aborted: false, settled: false };
      states.push(state);
      return new Promise<TwinLoad>((_resolve, reject) => {
        signal?.addEventListener('abort', () => {
          state.aborted = true;
          setTimeout(() => {
            state.settled = true;
            reject(new Error('stopped'));
          }, 100);
        });
      });
    };
    const started = Date.now();
    assert.deepEqual(await twinStep(never, 600, 300), unknown('out_of_time'));
    assert.ok(Date.now() - started < 3_000, `took ${Date.now() - started} ms`);
    assert.deepEqual(states, [{ aborted: true, settled: true }]);
  });

  test("the whole step, a stopped load's settling included, keeps to its budget (AUDIT M18G-S2)", {
    timeout: 10_000,
  }, async () => {
    let aborted = false;
    // A load that is told to stop and never settles.
    const stuck = (_target: number | null, signal?: AbortSignal) =>
      new Promise<TwinLoad>(() => {
        signal?.addEventListener('abort', () => {
          aborted = true;
        });
      });
    const started = Date.now();
    assert.deepEqual(await twinStep(stuck, 800, 300), unknown('out_of_time'));
    const took = Date.now() - started;
    assert.ok(took >= 700 && took < 1_300, `took ${took} ms`);
    assert.equal(aborted, true);
  });
});

describe('a picture of a held frame, taken again until two in a row match', () => {
  test('a picture that is still changing is taken again, and the first one that repeats is kept', async () => {
    const shots = [picture(0), picture(40), picture(40)];
    let taken = 0;
    const kept = await settledPicture(
      async () => shots[Math.min(taken++, shots.length - 1)] ?? null,
      2_000,
    );
    assert.deepEqual(kept, picture(40));
    assert.equal(taken, 3);
  });

  test('a picture that never repeats in time is the last one taken; a missing one is missing', async () => {
    let taken = 0;
    const started = Date.now();
    const kept = await settledPicture(async () => picture(10 * (taken++ % 5)), 600);
    assert.ok(Date.now() - started < 1_500);
    assert.ok(taken >= 2);
    assert.deepEqual(kept, picture(10 * ((taken - 1) % 5)));
    assert.equal(await settledPicture(async () => null, 300), null);
  });
});

describe('a twin load against a page that stops reaching frames (AUDIT M18G-M2)', () => {
  /** What the page does after it is navigated to: Chrome events, each this many ms after the navigation. */
  type PageEvent = { at: number; method: string; params?: Record<string, unknown> };
  /** A page that loads at once and fetches nothing. */
  const QUIET: PageEvent[] = [{ at: 50, method: 'Page.loadEventFired' }];
  /**
   * A Chrome that answers every command. `pace` is how the page's frames advance after each F24 key: frames per
   * 100 ms toward the next stop, or 0 for a page that never reaches a frame. `page` is what the page does once
   * navigated to; `firstKeyAt` is when the first F24 went down, in ms after the navigation.
   */
  const fakeChrome = (
    pace: number,
    page: PageEvent[] = QUIET,
  ): ChromeRun & { sent: string[]; firstKeyAt: () => number | null } => {
    const toChrome = new PassThrough();
    const fromChrome = new PassThrough();
    const sent: string[] = [];
    const timers: NodeJS.Timeout[] = [];
    let navigatedAt = 0;
    let firstKeyAt: number | null = null;
    let binding = '';
    let frame = 0;
    let target = 0;
    let next = 0;
    const timer = setInterval(() => {
      if (pace === 0 || frame >= target) return;
      frame = Math.min(target, frame + pace);
      const event = {
        method: 'Runtime.bindingCalled',
        sessionId: 'page',
        params: { name: binding, payload: `frame ${frame}` },
      };
      fromChrome.write(`${JSON.stringify(event)}\0`);
    }, 100);
    // A failing test must not keep the run alive.
    timer.unref();
    const send = (method: string, params: Record<string, unknown> = {}) =>
      fromChrome.write(`${JSON.stringify({ method, sessionId: 'page', params })}\0`);
    toChrome.setEncoding('utf8');
    toChrome.on('data', (chunk: string) => {
      for (const text of chunk.split('\0').filter((part) => part !== '')) {
        const { id, method, params } = JSON.parse(text) as {
          id: number;
          method: string;
          params: { name?: string; type?: string };
        };
        sent.push(method);
        if (method === 'Runtime.addBinding') binding = params.name ?? '';
        if (method === 'Page.navigate') {
          navigatedAt = Date.now();
          for (const event of page) {
            const later = setTimeout(() => send(event.method, event.params), event.at);
            later.unref();
            timers.push(later);
          }
        }
        if (method === 'Input.dispatchKeyEvent' && params.type === 'keyDown') {
          firstKeyAt ??= Date.now() - navigatedAt;
          target = TWIN_FRAMES[next] ?? target;
          next += 1;
        }
        fromChrome.write(`${JSON.stringify({ id, result: {} })}\0`);
      }
    });
    return {
      pid: 0,
      devtools: new DevToolsPipe(toChrome, fromChrome),
      session: 'page',
      stop: async () => {
        clearInterval(timer);
        for (const later of timers) clearTimeout(later);
      },
      sent,
      firstKeyAt: () => firstKeyAt,
    };
  };

  /** Requests the page makes: one sent at `at` and finished `takes` ms later, as Chrome's Network domain says. */
  const request = (id: string, at: number, takes: number): PageEvent[] => [
    { at, method: 'Network.requestWillBeSent', params: { requestId: id } },
    { at: at + takes, method: 'Network.loadingFinished', params: { requestId: id } },
  ];

  test('steps once the page has loaded and gone quiet for a second, not after a fixed wait (M1.8g-c)', {
    timeout: 20_000,
  }, async () => {
    const chrome = fakeChrome(400);
    await twinLoad(chrome, 'http://127.0.0.1:1/?check', null);
    const at = chrome.firstKeyAt() ?? -1;
    assert.ok(at >= TWIN_QUIET_MS && at < TWIN_QUIET_MS + 1_000, `first step at ${at} ms`);
    await chrome.stop();
  });

  test('waits for an asset that finishes late: the first step comes a quiet second after it (M1.8g-c)', {
    timeout: 20_000,
  }, async () => {
    const chrome = fakeChrome(400, [...QUIET, ...request('late', 200, 3_300)]);
    await twinLoad(chrome, 'http://127.0.0.1:1/?check', null);
    const at = chrome.firstKeyAt() ?? -1;
    assert.ok(
      at >= 3_500 + TWIN_QUIET_MS && at < 3_500 + TWIN_QUIET_MS + 1_000,
      `first step at ${at} ms`,
    );
    await chrome.stop();
  });

  test('a page that never goes quiet is stepped once the stall limit has passed (M1.8g-c)', {
    timeout: 20_000,
  }, async () => {
    const busy = Array.from({ length: 30 }, (_, index) =>
      request(`poll-${index}`, 100 + index * 300, 100),
    ).flat();
    const chrome = fakeChrome(400, [...QUIET, ...busy]);
    await twinLoad(chrome, 'http://127.0.0.1:1/?check', null, undefined, 2_000);
    const at = chrome.firstKeyAt() ?? -1;
    assert.ok(at >= 2_000 && at < 2_800, `first step at ${at} ms`);
    await chrome.stop();
  });

  test('a page that never reaches a frame fails once it has made no progress for the stall limit', {
    timeout: 15_000,
  }, async () => {
    const chrome = fakeChrome(0);
    const started = Date.now();
    await assert.rejects(
      twinLoad(chrome, 'http://127.0.0.1:1/?check', null, undefined, 400),
      /did not reach frame 300/,
    );
    const took = Date.now() - started;
    assert.ok(took < TWIN_QUIET_MS + 2_000, `took ${took} ms`);
    assert.ok(chrome.sent.includes('Input.dispatchKeyEvent'));
    await chrome.stop();
  });

  test('a page whose frames come slowly but keep coming is not cut off, however long a step takes', {
    timeout: 20_000,
  }, async () => {
    // 50 frames every 100 ms: the step to frame 300 takes about 600 ms, past a 400 ms limit, never 400 ms without one.
    const chrome = fakeChrome(50);
    const loaded = await twinLoad(chrome, 'http://127.0.0.1:1/?check', null, undefined, 400);
    assert.equal(loaded.targets, 0);
    await chrome.stop();
  });

  test('a step told to stop fails at once, without waiting out its time', {
    timeout: 15_000,
  }, async () => {
    const chrome = fakeChrome(0);
    const out = new AbortController();
    setTimeout(() => out.abort(), TWIN_QUIET_MS + 500);
    const started = Date.now();
    await assert.rejects(
      twinLoad(chrome, 'http://127.0.0.1:1/?check', null, out.signal),
      /ran out of time/,
    );
    assert.ok(Date.now() - started < TWIN_QUIET_MS + 2_000);
    await chrome.stop();
  });
});
