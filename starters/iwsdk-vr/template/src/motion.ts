/**
 * Motion for a game's objects: each helper is a pure function of the `time` (in seconds) a system's
 * `update(delta, time)` is given, so the same time always gives the same answer. Use it every frame,
 * for example `mole.position.y = groundY + bob(time)`, and never a timer of your own.
 */

const TAU = Math.PI * 2;

/** Up and down by `height` metres, `perSecond` times a second; 0 at time 0. */
export function bob(time: number, height = 0.1, perSecond = 1): number {
  return height * Math.sin(TAU * perSecond * time);
}

/** A side to side tilt of up to `degrees`, `perSecond` times a second, in radians; 0 at time 0. */
export function wiggle(time: number, degrees = 10, perSecond = 2): number {
  return ((degrees * Math.PI) / 180) * Math.sin(TAU * perSecond * time);
}

/** A steady turn of `turnsPerSecond` turns a second, in radians; 0 at time 0. */
export function spin(time: number, turnsPerSecond = 0.25): number {
  return TAU * turnsPerSecond * time;
}

/** From 0 to 1, fast at first and slow at the end; 0 below 0 and 1 above 1. */
export function ease(t: number): number {
  const clamped = Math.min(1, Math.max(0, t));
  return 1 - (1 - clamped) ** 3;
}

/**
 * A scale for a quick pop when something is hit: give it the seconds since the hit. It grows to
 * `size` and settles back to exactly 1 after `length` seconds, and is 1 before a hit.
 */
export function pop(sinceHit: number, length = 0.3, size = 1.4): number {
  if (sinceHit <= 0 || sinceHit >= length) return 1;
  return 1 + (size - 1) * Math.sin(Math.PI * ease(sinceHit / length));
}
