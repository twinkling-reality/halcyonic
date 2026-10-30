import { createHash, randomBytes } from 'node:crypto';
import { lstat, readFile } from 'node:fs/promises';
import { join } from 'node:path';
import type { CheckLaptop, CheckVr } from '@halcyonic/contracts';
import type { ChromeRun, DevToolsPipe } from './chrome.ts';
import { changedShare } from './png.ts';

/**
 * The screen changed after a press when more than this share of its pixels did, while at most
 * `STILL_CONTROL` did in a pair taken the same time apart with no input (validation/iwsdk-starter.md).
 */
export const CHANGED_AFTER = 0.005;
export const STILL_CONTROL = 0.001;
/** How long a press takes, from the move to the picture after it; the control pair is as far apart. */
export const PRESS_MS = 1100;
/** How long the step waits for the layer to report something to press. */
export const HOOK_MS = 10_000;
/** How long the second load waits for the page to offer VR. */
export const VR_MS = 10_000;

const LAYER_FILES = ['play-input.ts', 'play-input-core.ts'] as const;
const MAX_REPORT = 16 * 1024;
const MAX_TARGETS = 32;
const SOURCES = ['mouse', 'key', 'trigger', 'pinch', 'touch'] as const;

type Counts = Record<(typeof SOURCES)[number], number>;

/** What the page's input layer reports through its check hook; `reported`, never observed. */
export interface HookReport {
  readonly counts: Counts;
  readonly targets: readonly { readonly x: number; readonly y: number }[];
}

/** Whether the project's input layer is the starter's own, byte for byte. */
export async function layerState(project: string, template: string): Promise<CheckLaptop['layer']> {
  let changed = false;
  for (const file of LAYER_FILES) {
    const theirs = join(project, 'src', file);
    const info = await lstat(theirs).catch(() => null);
    if (info === null || info.isSymbolicLink() || !info.isFile() || info.size > 1024 * 1024)
      return 'missing';
    const [a, b] = await Promise.all([readFile(theirs), readFile(join(template, 'src', file))]);
    const hash = (bytes: Buffer) => createHash('sha256').update(bytes).digest('hex');
    if (hash(a) !== hash(b)) changed = true;
  }
  return changed ? 'changed' : 'starter';
}

/** Reads the hook in the page: what reached the layer and where its targets are, as one string. */
export const HOOK_EXPRESSION = `(() => {
  try {
    const hook = globalThis.__playCheck;
    if (hook === null || typeof hook !== 'object') return null;
    return JSON.stringify({ counts: hook.counts(), targets: hook.targets() });
  } catch {
    return null;
  }
})()`;

/** The hook's answer, read in the host against a strict shape; anything else is null. */
export function parseReport(value: unknown): HookReport | null {
  if (typeof value !== 'string' || value.length > MAX_REPORT) return null;
  let data: unknown;
  try {
    data = JSON.parse(value);
  } catch {
    return null;
  }
  if (data === null || typeof data !== 'object') return null;
  const { counts, targets } = data as { counts?: unknown; targets?: unknown };
  if (counts === null || typeof counts !== 'object' || !Array.isArray(targets)) return null;
  const read: Partial<Counts> = {};
  for (const source of SOURCES) {
    const n = (counts as Record<string, unknown>)[source];
    if (typeof n !== 'number' || !Number.isInteger(n) || n < 0 || n > 1e6) return null;
    read[source] = n;
  }
  if (targets.length > MAX_TARGETS) return null;
  const points: { x: number; y: number }[] = [];
  for (const target of targets) {
    const { x, y } = (target ?? {}) as { x?: unknown; y?: unknown };
    const inside = (n: unknown): n is number =>
      typeof n === 'number' && Number.isFinite(n) && n >= 0 && n <= 4096;
    if (!inside(x) || !inside(y)) return null;
    points.push({ x, y });
  }
  return { counts: read as Counts, targets: points };
}

/**
 * Whether the screen changed after a press, from the share of pixels that changed after it and in
 * the control pair: null (unknown) when either is unknown or the scene moved on its own.
 */
export function screenChanged(control: number | null, press: number | null): boolean | null {
  if (control === null || press === null || control > STILL_CONTROL) return null;
  return press > CHANGED_AFTER;
}

/** The few things the laptop step does to the page; the real one drives Chrome, a test's stands in. */
export interface Driver {
  evaluate(expression: string): Promise<unknown>;
  mouse(type: 'mouseMoved' | 'mousePressed' | 'mouseReleased', x: number, y: number): Promise<void>;
  key(code: 'Space' | 'KeyE'): Promise<void>;
  picture(): Promise<Buffer | null>;
  wait(ms: number): Promise<void>;
}

/** The driver for the check's Chrome, over its DevTools pipe and the page's session. */
export function pipeDriver(devtools: DevToolsPipe, session: string): Driver {
  const keys = {
    Space: { key: ' ', windowsVirtualKeyCode: 32 },
    KeyE: { key: 'e', windowsVirtualKeyCode: 69 },
  };
  return {
    async evaluate(expression) {
      const answer = await devtools.send(
        'Runtime.evaluate',
        { expression, returnByValue: true, timeout: 500 },
        session,
        5_000,
      );
      return (answer.result as { value?: unknown } | undefined)?.value;
    },
    async mouse(type, x, y) {
      const down = type === 'mousePressed';
      await devtools.send(
        'Input.dispatchMouseEvent',
        type === 'mouseMoved'
          ? { type, x, y }
          : { type, x, y, button: 'left', buttons: down ? 1 : 0, clickCount: 1 },
        session,
      );
    },
    async key(code) {
      for (const type of ['keyDown', 'keyUp'])
        await devtools.send('Input.dispatchKeyEvent', { type, code, ...keys[code] }, session);
    },
    async picture() {
      const shot = await devtools
        .send('Page.captureScreenshot', { format: 'png' }, session)
        .catch((): Record<string, unknown> => ({}));
      return typeof shot.data === 'string' ? Buffer.from(shot.data, 'base64') : null;
    },
    wait: (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  };
}

/**
 * The laptop step: waits for the layer to report a target, presses up to three of them with a real
 * mouse, each after a control pair, then presses Space and E. What reached the layer is reported;
 * whether the screen changed is observed in the host.
 */
export async function laptopStep(
  driver: Driver,
  layer: CheckLaptop['layer'],
): Promise<CheckLaptop> {
  const report = async () => parseReport(await driver.evaluate(HOOK_EXPRESSION).catch(() => null));
  let first: HookReport | null = null;
  for (let waited = 0; waited <= HOOK_MS; waited += 500) {
    const now = await report();
    if (now !== null) first = now;
    if (now !== null && now.targets.length > 0) break;
    await driver.wait(500);
  }
  const finish = (findings: Omit<CheckLaptop, 'layer' | 'ready'>): CheckLaptop => ({
    layer,
    ...findings,
    ready: layer === 'starter' && findings.mouse === 'reached' && findings.screen_changed === true,
  });
  if (first === null)
    return finish({ targets: 0, mouse: 'unknown', key: 'unknown', screen_changed: null });
  const targets = first.targets.length;
  let mouse: CheckLaptop['mouse'] = targets === 0 ? 'no_target' : 'not_reached';
  let screen: boolean | null = null;
  for (let attempt = 0; attempt < Math.min(3, targets); attempt += 1) {
    const control = await driver.picture();
    await driver.wait(PRESS_MS);
    const before = await driver.picture();
    // Where the target is now, right before the press: a moving one has moved since the control.
    const ready = await report();
    const point = ready?.targets[attempt] ?? ready?.targets[0];
    if (ready === null || point === undefined) {
      mouse = 'unknown';
      break;
    }
    await driver.mouse('mouseMoved', point.x, point.y);
    await driver.wait(300);
    await driver.mouse('mousePressed', point.x, point.y);
    await driver.wait(300);
    await driver.mouse('mouseReleased', point.x, point.y);
    await driver.wait(500);
    const after = await driver.picture();
    const now = await report();
    if (now === null) {
      mouse = 'unknown';
      break;
    }
    if (now.counts.mouse > ready.counts.mouse) {
      mouse = 'reached';
      const share = (a: Buffer | null, b: Buffer | null) =>
        a === null || b === null ? null : changedShare(a, b);
      screen = screenChanged(share(control, before), share(before, after));
      break;
    }
  }
  const beforeKeys = await report();
  let key: CheckLaptop['key'] = 'unknown';
  if (beforeKeys !== null) {
    await driver.key('Space');
    await driver.wait(300);
    await driver.key('KeyE');
    await driver.wait(300);
    const afterKeys = await report();
    if (afterKeys !== null)
      key = afterKeys.counts.key > beforeKeys.counts.key ? 'reached' : 'not_reached';
  }
  return finish({ targets, mouse, key, screen_changed: screen });
}

/**
 * The stand-in WebXR the second load runs with, before any of the page's scripts: it supports
 * `immersive-vr` and reports a call to `offerSession` or `requestSession` for it through the binding,
 * which it takes into its closure and removes from the page's global object first. No function the
 * page can reach names the binding.
 */
export function standInScript(binding: string): string {
  return `(() => {
  const name = ${JSON.stringify(binding)};
  const send = globalThis[name];
  try { delete globalThis[name]; } catch {}
  if (typeof send !== 'function') return;
  const record = (how, mode) => { if (mode === 'immersive-vr') send(how); };
  const never = () => new Promise(() => {});
  const xr = {
    isSessionSupported: (mode) => Promise.resolve(mode === 'immersive-vr' || mode === 'inline'),
    offerSession: (mode) => { record('offer_session', mode); return never(); },
    requestSession: (mode) => { record('request_session', mode); return never(); },
    addEventListener() {},
    removeEventListener() {},
    dispatchEvent() { return true; },
    ondevicechange: null,
  };
  Object.defineProperty(Navigator.prototype, 'xr', { configurable: true, get: () => xr });
})();`;
}

/** What the host accepts from the binding: exactly one of the two calls, nothing else. */
export function acceptCall(payload: unknown): CheckVr['how'] {
  return payload === 'offer_session' || payload === 'request_session' ? payload : null;
}

/**
 * The VR-entry step: loads the page again with the stand-in WebXR and waits for it to offer
 * `immersive-vr`. Offered to a stand-in on the computer, never to a headset.
 */
export async function vrStep(
  devtools: DevToolsPipe,
  session: string,
  url: string,
): Promise<CheckVr> {
  const binding = `h${randomBytes(12).toString('hex')}`;
  let how: CheckVr['how'] = null;
  let exceptions = 0;
  const off = devtools.onEvent((message) => {
    if (message.sessionId !== session) return;
    if (message.method === 'Runtime.exceptionThrown') exceptions += 1;
    if (message.method === 'Runtime.bindingCalled' && message.params?.name === binding)
      how ??= acceptCall(message.params.payload);
  });
  try {
    await devtools.send('Runtime.addBinding', { name: binding }, session);
    await devtools.send(
      'Page.addScriptToEvaluateOnNewDocument',
      { source: standInScript(binding) },
      session,
    );
    await devtools.send('Page.navigate', { url }, session);
    for (let waited = 0; waited < VR_MS && how === null; waited += 250)
      await new Promise((resolve) => setTimeout(resolve, 250));
  } finally {
    off();
  }
  return {
    offered: how === null ? 'not_offered' : 'offered',
    how,
    exceptions,
    ready: how !== null,
  };
}

/** The frames a twin load stops at: the page's 5th second, the move, the press, and a second later. */
export const TWIN_FRAMES = [300, 303, 306, 360] as const;
/** A press is seen when its end frame differs from the twin's by more than this share of pixels. */
export const RESPONSE_SEEN = 0.0005;
/** The press run and its twin must match at least this closely at the frame before input. */
export const TWINS_MATCH = 0.0001;
/** How many targets the twin step presses, one load each. */
export const TWIN_TARGETS = 5;
/** How long a twin load waits, its clock held at frame 0, for the page and its assets to load. */
export const TWIN_LOAD_MS = 3_000;
/** How long one step of a twin load may take, by the wall clock, to reach its frame. */
export const TWIN_STEP_FRAME_MS = 30_000;
/** How long the whole twin step may take, by the wall clock, before it reads unknown. */
export const TWIN_STEP_MS = 180_000;

/**
 * The clock a twin load runs under (M1.8g), before any of the page's scripts. Frames advance only to
 * the next of `TWIN_FRAMES` when the host sends a trusted F24 key, which this script takes first on
 * the window, in the capture phase, so the page never sees it; a page cannot make a trusted key. Each
 * frame reached is reported through the binding, taken into the closure and removed from the global
 * object first. Time (performance.now, Date, animation frames, timers) moves 1/60 s per frame, and
 * Math.random, crypto.getRandomValues and crypto.randomUUID are seeded, so a page's state depends only
 * on the frames it has run: two loads match at the same frame unless the page has another source of
 * time or chance, which the twin check then sees. No global names any of it.
 */
export function clockScript(binding: string): string {
  return `(() => {
  const name = ${JSON.stringify(binding)};
  const send = globalThis[name];
  try { delete globalThis[name]; } catch {}
  if (typeof send !== 'function') return;
  const STOPS = ${JSON.stringify(TWIN_FRAMES)};
  const FRAME = 1000 / 60;
  const EPOCH = 1767225600000;
  let frame = 0;
  let target = 0;
  let next = 0;
  const now = () => frame * FRAME;
  let seed = 0x2f6b1a3d;
  const random = () => {
    seed |= 0; seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  Math.random = random;
  const fill = (array) => {
    const bytes = new Uint8Array(array.buffer, array.byteOffset, array.byteLength);
    for (let i = 0; i < bytes.length; i += 1) bytes[i] = Math.floor(random() * 256);
    return array;
  };
  Object.defineProperty(crypto, 'getRandomValues', { value: fill });
  Object.defineProperty(crypto, 'randomUUID', {
    value: () => {
      const b = fill(new Uint8Array(16));
      b[6] = (b[6] & 0x0f) | 0x40; b[8] = (b[8] & 0x3f) | 0x80;
      const h = [...b].map((x) => x.toString(16).padStart(2, '0')).join('');
      return h.slice(0, 8) + '-' + h.slice(8, 12) + '-' + h.slice(12, 16) + '-' + h.slice(16, 20) + '-' + h.slice(20);
    },
  });
  performance.now = now;
  const RealDate = Date;
  function FixedDate(...args) {
    if (!new.target) return new RealDate(EPOCH + now()).toString();
    return args.length === 0 ? new RealDate(EPOCH + now()) : new RealDate(...args);
  }
  FixedDate.prototype = RealDate.prototype;
  FixedDate.now = () => EPOCH + now();
  FixedDate.parse = RealDate.parse;
  FixedDate.UTC = RealDate.UTC;
  globalThis.Date = FixedDate;
  let nextId = 1;
  const timers = new Map();
  const later = (fn, ms, args, every) => {
    const id = nextId++;
    const wait = Math.max(0, Number(ms) || 0);
    timers.set(id, { fn, args, every: every ? Math.max(wait, FRAME) : 0, due: now() + wait });
    return id;
  };
  globalThis.setTimeout = (fn, ms, ...args) => later(fn, ms, args, false);
  globalThis.setInterval = (fn, ms, ...args) => later(fn, ms, args, true);
  globalThis.clearTimeout = globalThis.clearInterval = (id) => { timers.delete(id); };
  const realFrame = requestAnimationFrame.bind(globalThis);
  const queued = new Map();
  let nextFrameId = 1;
  globalThis.requestAnimationFrame = (cb) => { const id = nextFrameId++; queued.set(id, cb); return id; };
  globalThis.cancelAnimationFrame = (id) => { queued.delete(id); };
  const call = (fn, args) => {
    try { if (typeof fn === 'function') fn(...args); } catch (error) { queueMicrotask(() => { throw error; }); }
  };
  const tick = () => {
    if (frame < target) {
      frame += 1;
      for (;;) {
        let first = null;
        for (const [id, t] of timers) {
          if (t.due <= now() && (first === null || t.due < first[1].due || (t.due === first[1].due && id < first[0]))) first = [id, t];
        }
        if (first === null) break;
        const [id, t] = first;
        if (t.every) t.due += t.every; else timers.delete(id);
        call(t.fn, t.args);
      }
      const callbacks = [...queued.values()];
      queued.clear();
      for (const cb of callbacks) call(cb, [now()]);
      if (frame === target) send('frame ' + frame);
    }
    realFrame(tick);
  };
  realFrame(tick);
  const step = (event) => {
    if (!event.isTrusted || event.code !== 'F24') return;
    event.stopImmediatePropagation();
    event.preventDefault();
    if (event.type === 'keydown' && next < STOPS.length) { target = STOPS[next]; next += 1; }
  };
  for (const type of ['keydown', 'keyup', 'keypress']) addEventListener(type, step, true);
})();`;
}

/** What one twin load saw: its pictures at the frame before input and at the end, and its targets. */
export interface TwinLoad {
  readonly before: Buffer | null;
  readonly end: Buffer | null;
  readonly targets: number;
  /** Whether the press, when there was one, reached the layer, as it reports. */
  readonly reached: boolean;
}

/**
 * Loads the page under the stepped clock and steps it through `TWIN_FRAMES`, pressing `target`
 * (an index into the layer's targets at the frame before input) between them, or nothing.
 */
export async function twinLoad(
  run: ChromeRun,
  url: string,
  target: number | null,
  signal?: AbortSignal,
  frameMs = TWIN_STEP_FRAME_MS,
): Promise<TwinLoad> {
  const { devtools, session } = run;
  const binding = `h${randomBytes(12).toString('hex')}`;
  let reached = 0;
  const off = devtools.onEvent((message) => {
    if (message.sessionId !== session || message.method !== 'Runtime.bindingCalled') return;
    if (message.params?.name !== binding || typeof message.params.payload !== 'string') return;
    const match = /^frame (\d{1,4})$/.exec(message.params.payload);
    if (match) reached = Math.max(reached, Number(match[1]));
  });
  try {
    for (const domain of ['Runtime.enable', 'Page.enable'])
      await devtools.send(domain, {}, session);
    await devtools.send('Runtime.addBinding', { name: binding }, session);
    await devtools.send(
      'Page.addScriptToEvaluateOnNewDocument',
      { source: clockScript(binding) },
      session,
    );
    await devtools.send('Page.navigate', { url }, session);
    const driver = pipeDriver(devtools, session);
    await driver.wait(TWIN_LOAD_MS);
    const stepTo = async (frame: number) => {
      for (const type of ['keyDown', 'keyUp'])
        await devtools.send(
          'Input.dispatchKeyEvent',
          { type, code: 'F24', key: 'F24', windowsVirtualKeyCode: 135 },
          session,
        );
      // By the wall clock: on a throttled Mac a short wait can take far longer than asked.
      const until = Date.now() + frameMs;
      while (reached < frame) {
        if (signal?.aborted) throw new Error('the twin step ran out of time');
        if (Date.now() > until) throw new Error(`the page did not reach frame ${frame}`);
        await driver.wait(50);
      }
      // The frame is drawn; let it reach the screen.
      await driver.wait(300);
    };
    const [first, moved, pressed, end] = TWIN_FRAMES;
    await stepTo(first);
    const report = parseReport(await driver.evaluate(HOOK_EXPRESSION).catch(() => null));
    const before = await driver.picture();
    const point = target === null ? undefined : report?.targets[target];
    if (point !== undefined) await driver.mouse('mouseMoved', point.x, point.y);
    await stepTo(moved);
    if (point !== undefined) await driver.mouse('mousePressed', point.x, point.y);
    await stepTo(pressed);
    if (point !== undefined) await driver.mouse('mouseReleased', point.x, point.y);
    await stepTo(end);
    const last = await driver.picture();
    const after = parseReport(await driver.evaluate(HOOK_EXPRESSION).catch(() => null));
    return {
      before,
      end: last,
      targets: report?.targets.length ?? 0,
      reached:
        point !== undefined &&
        report !== null &&
        after !== null &&
        after.counts.mouse > report.counts.mouse,
    };
  } finally {
    off();
  }
}

/** Whether two loads' pictures at the same frame match within `TWINS_MATCH`; null when either is missing. */
function matches(a: Buffer | null, b: Buffer | null): boolean | null {
  if (a === null || b === null) return null;
  const share = changedShare(a, b);
  return share === null ? null : share <= TWINS_MATCH;
}

/**
 * The twin step (M1.8g): the page twice with no input, then with a press on each of up to
 * `TWIN_TARGETS` targets, each in a load of its own under the stepped clock, compared at the same
 * frames. The two no-input loads must match at the frame before input and at the end (AUDIT
 * M18G-M1): a page that changes between loads with no input at all has a source of time or chance
 * the clock does not hold. Seen when a press that reached the layer changes the end frame from the
 * twin's by more than `RESPONSE_SEEN` and a second load with the same press ends the same way; null
 * when any two loads that should match do not, when nothing could be pressed or compared, or when the
 * step outlasts `budgetMs` by the wall clock (AUDIT M18G-M2). Each load is given a signal that aborts
 * then, and must stop its Chrome and settle; the step waits for that before it answers.
 */
export async function twinStep(
  load: (target: number | null, signal: AbortSignal) => Promise<TwinLoad>,
  budgetMs = TWIN_STEP_MS,
): Promise<boolean | null> {
  const out = new AbortController();
  const timer = setTimeout(() => out.abort(), budgetMs);
  const late = new Promise<null>((resolve) =>
    out.signal.addEventListener('abort', () => resolve(null), { once: true }),
  );
  /** One load, or null once the budget is spent, after the load has stopped. */
  const next = async (target: number | null): Promise<TwinLoad | null> => {
    if (out.signal.aborted) return null;
    const running = load(target, out.signal);
    const first = await Promise.race([running, late]);
    if (first === null) await running.catch(() => undefined);
    return first;
  };
  try {
    const twin = await next(null);
    if (twin === null || twin.before === null || twin.end === null || twin.targets === 0)
      return null;
    const control = await next(null);
    if (control === null) return null;
    if (matches(control.before, twin.before) !== true) return null;
    if (matches(control.end, twin.end) !== true) return null;
    for (let target = 0; target < Math.min(TWIN_TARGETS, twin.targets); target += 1) {
      const press = await next(target);
      if (press === null || press.before === null || press.end === null) return null;
      if (matches(press.before, twin.before) !== true) return null;
      const response = changedShare(press.end, twin.end);
      if (response === null) return null;
      if (press.reached && response > RESPONSE_SEEN) {
        const again = await next(target);
        if (again === null || !again.reached) return null;
        if (matches(again.before, twin.before) !== true) return null;
        return matches(again.end, press.end) === true ? true : null;
      }
    }
    return false;
  } finally {
    clearTimeout(timer);
  }
}
