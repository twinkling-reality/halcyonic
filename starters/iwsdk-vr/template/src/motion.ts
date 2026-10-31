/**
 * Motion for a game's objects (ART7's curves): each helper is a pure function of the seconds since its motion began,
 * taken from the `time` a system's `update(delta, time)` is given, so the same time always gives the same answer.
 * For example, `mole.position.y = pop(time - poppedAt)`. Never a timer of your own.
 * How they feel: a pop is quick, with a small overshoot, like a cork; a bob is a slow, calm breath; a wiggle is a
 * shiver that dies out within half a second; a bonk is a squash you can feel, then a springy recovery.
 */

export const motion = {
  popMs: 320,
  popBack: 1.8,
  bobAmp: 0.03,
  bobPeriod: 1.6,
  squash: 0.04,
  wiggleAmp: 0.2,
  wiggleHz: 6,
  wiggleDecay: 5,
  bonkSquash: [1.25, 0.6] as const,
  bonkMs: 90,
  settleMs: 420,
  hideMs: 200,
};

const TAU = Math.PI * 2;
const DEPTH = 0.3;

export const easeOutBack = (x: number, s = motion.popBack) => 1 + (s + 1) * (x - 1) ** 3 + s * (x - 1) ** 2;
export const easeInBack = (x: number, s = motion.popBack) => (s + 1) * x ** 3 - s * x ** 2;

/** Height for a pop: rises from 0.3 m below to 0 over popMs, with a small overshoot. */
export function pop(seconds: number): number {
  const x = Math.min(1, Math.max(0, (seconds * 1000) / motion.popMs));
  return x === 1 ? 0 : -DEPTH + DEPTH * easeOutBack(x);
}

/** Height for a hide: sinks from 0 to 0.3 m below over hideMs. */
export function hide(seconds: number): number {
  const x = Math.min(1, Math.max(0, (seconds * 1000) / motion.hideMs));
  return x === 0 ? 0 : -DEPTH * easeInBack(x);
}

/** A breath: a small rise and fall, with a squash that keeps the volume (sy * sxz * sxz = 1). */
export function bob(seconds: number): { y: number; sy: number; sxz: number } {
  const wave = Math.sin((TAU * seconds) / motion.bobPeriod);
  const sy = 1 + motion.squash * wave;
  return { y: motion.bobAmp * wave, sy, sxz: 1 / Math.sqrt(sy) };
}

/** A tilt in radians around z, for a near miss or a happy moment, that dies out. */
export function wiggle(seconds: number): number {
  if (seconds <= 0) return 0;
  return motion.wiggleAmp * Math.sin(seconds * motion.wiggleHz * TAU) * Math.exp(-seconds * motion.wiggleDecay);
}

/**
 * A bonk's scale: squashes to (1.25, 0.6, 1.25) over bonkMs, then springs back to exactly 1 over settleMs, then hide
 * the thing that was bonked.
 */
export function bonk(seconds: number): { sxz: number; sy: number } {
  const ms = seconds * 1000;
  const [wide, low] = motion.bonkSquash;
  if (ms <= 0 || ms >= motion.bonkMs + motion.settleMs) return { sxz: 1, sy: 1 };
  if (ms <= motion.bonkMs) {
    const x = ms / motion.bonkMs;
    return { sxz: 1 + (wide - 1) * x, sy: 1 + (low - 1) * x };
  }
  const t = (ms - motion.bonkMs) / 1000;
  const spring = Math.exp(-7 * t) * Math.cos(14 * t);
  return { sxz: 1 + (wide - 1) * spring, sy: 1 + (low - 1) * spring };
}
