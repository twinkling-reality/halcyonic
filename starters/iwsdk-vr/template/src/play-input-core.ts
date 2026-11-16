/**
 * The parts of play-input.ts that need no SDK: what counts as a new press, which way the view turns when the
 * mouse drags, when a controller or hand touches something, and the counts the person's computer reads when it
 * checks the app. Kept free of imports so it can be tested on its own.
 */

/** Where an input came from: a laptop (mouse, key) or a headset (trigger, pinch, touch). */
export type PlaySource = 'mouse' | 'key' | 'trigger' | 'pinch' | 'touch';

export interface Point {
  readonly x: number;
  readonly y: number;
  readonly z: number;
}

/** True once each time a held state starts: a press, not a hold. */
export class Edge {
  #was = false;

  rise(now: boolean): boolean {
    const rose = now && !this.#was;
    this.#was = now;
    return rose;
  }
}

/** How many times each kind of input reached the game. */
export class Counts {
  readonly #counts: Record<PlaySource, number> = { mouse: 0, key: 0, trigger: 0, pinch: 0, touch: 0 };

  add(source: PlaySource): void {
    this.#counts[source] += 1;
  }

  snapshot(): Record<PlaySource, number> {
    return { ...this.#counts };
  }
}

/** Turning the view with the mouse: radians per pixel dragged, and how far up or down the view may tip. */
export const LOOK = { radiansPerPixel: 0.005, pitchLimit: 1.2 } as const;

/** A press that moves farther than this many pixels before it lifts is a drag to look, not a click. */
export const DRAG_PIXELS = 6;

/** The view's turn after dragging dx, dy pixels: dragging right turns right, dragging up looks up. */
export function look(yaw: number, pitch: number, dx: number, dy: number): { yaw: number; pitch: number } {
  const tipped = pitch - dy * LOOK.radiansPerPixel;
  return {
    yaw: yaw - dx * LOOK.radiansPerPixel,
    pitch: Math.max(-LOOK.pitchLimit, Math.min(LOOK.pitchLimit, tipped)),
  };
}

/** The yaw and pitch of a camera looking along `forward` (three.js: the camera looks down its -Z axis). */
export function yawPitchOf(forward: Point): { yaw: number; pitch: number } {
  const flat = Math.hypot(forward.x, forward.z);
  return { yaw: Math.atan2(-forward.x, -forward.z), pitch: Math.atan2(forward.y, flat) };
}

/** True when the press has moved far enough to be a drag. */
export function isDrag(dx: number, dy: number): boolean {
  return Math.hypot(dx, dy) > DRAG_PIXELS;
}

/** True when two points are within `radius` metres of each other. */
export function within(a: Point, b: Point, radius: number): boolean {
  return Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z) <= radius;
}

/** On a headset a ray press is a trigger when a controller is held, else a hand's pinch. */
export function headsetSource(holdingController: boolean): 'trigger' | 'pinch' {
  return holdingController ? 'trigger' : 'pinch';
}

/** The laptop keys and headset buttons for the game's two buttons. */
export const BUTTONS = {
  jump: { key: 'Space', headset: 'a-button' },
  action: { key: 'KeyE', headset: 'b-button' },
} as const;

export type ButtonName = keyof typeof BUTTONS;

/**
 * Every key the layer binds on a laptop: the SDK's browser-first-person keys (W A S D and the arrows, and Space, the
 * jump), E for the action and H for the welcome card (`HELP_KEY` in play-input.ts). Nothing else, so the check's F24,
 * which steps its clock, is never one.
 */
export const BOUND_KEYS: ReadonlySet<string> = new Set([
  'KeyW',
  'KeyA',
  'KeyS',
  'KeyD',
  'ArrowUp',
  'ArrowDown',
  'ArrowLeft',
  'ArrowRight',
  BUTTONS.jump.key,
  BUTTONS.action.key,
  'KeyH',
]);

/**
 * Calls `stop` once, at the first input the layer binds on a laptop: a key of `BOUND_KEYS` going down on `keys` (the
 * window), or the main button pressed on `canvas`, which starts both a click and a drag to look. A hover, another
 * button, a key let go or a key the layer does not bind never calls it. Its listeners go once it has.
 */
export function stopOnInput(keys: EventTarget, canvas: EventTarget, stop: () => void): void {
  const off = new AbortController();
  const once = () => {
    off.abort();
    stop();
  };
  keys.addEventListener(
    'keydown',
    (event) => {
      const code = (event as Event & { code?: unknown }).code;
      if (typeof code === 'string' && BOUND_KEYS.has(code)) once();
    },
    { capture: true, signal: off.signal },
  );
  canvas.addEventListener(
    'pointerdown',
    (event) => {
      if ((event as Event & { button?: unknown }).button === 0) once();
    },
    { capture: true, signal: off.signal },
  );
}
