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
  standInScript,
  TWIN_FRAMES,
  TWIN_LOAD_MS,
  TWIN_TARGETS,
  type TwinLoad,
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

describe("the twin step's stepped clock (M1.8g)", () => {
  const BINDING = 'h00112233445566778899aabbcc';

  /** Runs the clock as a page would see it, before the page's scripts; the host holds its frames. */
  function page() {
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
    const before = new Set(Object.getOwnPropertyNames(context));
    runInContext(clockScript(BINDING), context);
    const added = Object.getOwnPropertyNames(context).filter((name) => !before.has(name));
    // Values from the page's realm, read back as plain data.
    const run = (code: string) => {
      const value = runInContext(code, context) as unknown;
      return typeof value === 'object' && value !== null
        ? JSON.parse(JSON.stringify(value))
        : value;
    };
    const frames = (n: number) => {
      for (let i = 0; i < n; i += 1) (tick as unknown as () => void)();
    };
    const key = (isTrusted: boolean, type = 'keydown') => {
      const event = {
        isTrusted,
        code: 'F24',
        type,
        stopImmediatePropagation() {},
        preventDefault() {},
      };
      for (const listener of listeners) if (listener.type === type) listener.fn(event);
    };
    return { context, sent, added, run, frames, key };
  }

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
    assert.deepEqual(sent, [`frame ${first}`]);
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

describe('the twin step: a press seen against its no-input twin (M1.8g)', () => {
  const load = (before: number, end: number, extra: Partial<TwinLoad> = {}): TwinLoad => ({
    before: picture(before),
    end: picture(end),
    targets: 3,
    reached: true,
    ...extra,
  });
  /** A step whose twin is `twin` and whose presses are `presses`, in order; counts the loads. */
  const step = (twin: TwinLoad, presses: TwinLoad[]) => {
    const asked: (number | null)[] = [];
    return {
      asked,
      run: () =>
        twinStep(async (target) => {
          asked.push(target);
          return target === null ? twin : (presses[target] ?? twin);
        }),
    };
  };
  // 200 by 100 pictures: 0.05% is 10 pixels, 0.01% is 2.
  test('seen when a press that reached the layer changes the end frame beyond the twin', async () => {
    const { run, asked } = step(load(0, 0), [load(0, 0), load(0, 50)]);
    assert.equal(await run(), true);
    // The twin, its no-input control, each press, and the seen press once more.
    assert.deepEqual(asked, [null, null, 0, 1, 1]);
  });

  test('not seen when no press changes the end frame, after every target up to the limit', async () => {
    const { run, asked } = step(load(0, 0, { targets: 9 }), []);
    assert.equal(await run(), false);
    assert.equal(asked.length, 2 + TWIN_TARGETS);
  });

  test('a change the layer did not hear is not a response', async () => {
    const { run } = step(load(0, 0), [load(0, 50, { reached: false })]);
    assert.equal(await run(), false);
  });

  test('unknown when a press run and its twin differ before any input, or nothing can be pressed', async () => {
    assert.equal(await step(load(0, 0), [load(5, 50)]).run(), null);
    assert.equal(await step(load(0, 0, { targets: 0 }), []).run(), null);
    assert.equal(await step(load(0, 0, { before: null }), []).run(), null);
  });

  test('unknown when two loads with no input end apart, however little the press changes (AUDIT M18G-M1)', async () => {
    // Each load ends a little further on, input or not, as a CSS animation started after frame 300 would.
    let loads = 0;
    const drifting = async (target: number | null) => {
      loads += 1;
      return load(0, 20 * loads, { reached: target !== null });
    };
    assert.equal(await twinStep(drifting), null);
    assert.equal(loads, 2);
    // The control must also match the twin before any input.
    const asked: (number | null)[] = [];
    const early = async (target: number | null) => {
      asked.push(target);
      return asked.length === 2 ? load(5, 0) : load(0, 50);
    };
    assert.equal(await twinStep(early), null);
    assert.deepEqual(asked, [null, null]);
  });

  test('unknown when the same press, loaded again, ends otherwise: the change was not its own', async () => {
    let presses = 0;
    const result = await twinStep(async (target) => {
      if (target === null) return load(0, 0);
      presses += 1;
      return load(0, presses === 1 ? 50 : 90);
    });
    assert.equal(result, null);
    assert.equal(presses, 2);
  });

  test('a step past its time reads unknown, after its load was told to stop and stopped (AUDIT M18G-M2)', {
    timeout: 10_000,
  }, async () => {
    const seen: { aborted: boolean; settled: boolean }[] = [];
    // A page that never reaches a frame: the load waits until it is told to stop, then stops.
    const never = (_target: number | null, signal?: AbortSignal) => {
      const state = { aborted: false, settled: false };
      seen.push(state);
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
    assert.equal(await twinStep(never, 300), null);
    assert.ok(Date.now() - started < 3_000, `took ${Date.now() - started} ms`);
    assert.deepEqual(seen, [{ aborted: true, settled: true }]);
  });
});

describe('a twin load against a page that never reaches a frame (AUDIT M18G-M2)', () => {
  /** A Chrome that answers every command and never reports a frame; it records what it was sent. */
  const silentChrome = (): ChromeRun & { sent: string[] } => {
    const toChrome = new PassThrough();
    const fromChrome = new PassThrough();
    const sent: string[] = [];
    toChrome.setEncoding('utf8');
    toChrome.on('data', (chunk: string) => {
      for (const text of chunk.split('\0').filter((part) => part !== '')) {
        const { id, method } = JSON.parse(text) as { id: number; method: string };
        sent.push(method);
        fromChrome.write(`${JSON.stringify({ id, result: {} })}\0`);
      }
    });
    return {
      pid: 0,
      devtools: new DevToolsPipe(toChrome, fromChrome),
      session: 'page',
      stop: async () => {},
      sent,
    };
  };

  test('each step is bounded by the wall clock and fails past it', {
    timeout: 15_000,
  }, async () => {
    const chrome = silentChrome();
    const started = Date.now();
    await assert.rejects(
      twinLoad(chrome, 'http://127.0.0.1:1/?check', null, undefined, 400),
      /did not reach frame 300/,
    );
    const took = Date.now() - started;
    assert.ok(took < TWIN_LOAD_MS + 2_000, `took ${took} ms`);
    assert.ok(chrome.sent.includes('Input.dispatchKeyEvent'));
  });

  test('a step told to stop fails at once, without waiting out its time', {
    timeout: 15_000,
  }, async () => {
    const chrome = silentChrome();
    const out = new AbortController();
    setTimeout(() => out.abort(), TWIN_LOAD_MS + 300);
    const started = Date.now();
    await assert.rejects(
      twinLoad(chrome, 'http://127.0.0.1:1/?check', null, out.signal),
      /ran out of time/,
    );
    assert.ok(Date.now() - started < TWIN_LOAD_MS + 2_000);
  });
});
