import { createHash, randomBytes } from 'node:crypto';
import { lstat, readFile } from 'node:fs/promises';
import { join } from 'node:path';
import type { CheckLaptop, CheckVr } from '@halcyonic/contracts';
import type { DevToolsPipe } from './chrome.ts';
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
    const ready = await report();
    const point = ready?.targets[attempt] ?? ready?.targets[0];
    if (ready === null || point === undefined) {
      mouse = 'unknown';
      break;
    }
    const control = await driver.picture();
    await driver.wait(PRESS_MS);
    const before = await driver.picture();
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
