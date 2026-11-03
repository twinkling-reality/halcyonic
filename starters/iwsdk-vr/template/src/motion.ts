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

/**
 * The world's own motion, which `src/look.ts` and `src/decor.ts` run by themselves so a game is alive from its first
 * frame: a breeze that sways flowers, grass and trees, clouds that wander, butterflies. Small and slow, never the
 * ground the player stands on, and like the curves above, pure functions of the time a system is given.
 */
export const ambient = {
  swayAmp: 0.09,
  swayPeriod: 2.6,
  gust: 0.7,
  treeAmp: 0.022,
  treePeriod: 4.4,
  cloudDrift: 2.2,
  cloudPeriod: 48,
  cloudLift: 0.1,
  flapHz: 6.5,
  flightPeriod: 7.5,
} as const;

/**
 * A lean in radians for something rooted at (x, z), as a breeze crosses the island: places a metre apart lean at
 * different moments, so the grass moves like a field rather than all at once.
 */
export function sway(
  seconds: number,
  x: number,
  z: number,
  amp: number = ambient.swayAmp,
  period: number = ambient.swayPeriod,
): number {
  return amp * Math.sin((TAU * seconds) / period - ambient.gust * (0.8 * x + 0.6 * z));
}

/** A cloud's offset from its home in metres: a slow wander sideways and a breath up and down, never a jump. */
export function drift(seconds: number, phase: number): { x: number; y: number } {
  const a = (TAU * seconds) / ambient.cloudPeriod + phase;
  return { x: ambient.cloudDrift * Math.sin(a), y: ambient.cloudLift * Math.sin(2.3 * a + phase) };
}

/** A butterfly's wing angle in radians up from flat: quick beats that open wide and close nearly shut. */
export function flap(seconds: number, phase: number): number {
  const beat = 0.5 + 0.5 * Math.sin(TAU * ambient.flapHz * seconds + phase);
  return 0.15 + 1.05 * beat * beat;
}

/**
 * A butterfly's place on its loop around its spot, within `reach` metres, rising and dipping a little, and its heading
 * (radians about y, 0 toward +z) along the way it flies.
 */
export function flight(
  seconds: number,
  phase: number,
  reach = 0.5,
): { x: number; y: number; z: number; heading: number } {
  const a = (TAU * seconds) / ambient.flightPeriod + phase;
  return {
    x: reach * Math.sin(a),
    y: 0.12 * Math.sin(3 * a + phase),
    z: 0.6 * reach * Math.sin(2 * a),
    heading: Math.atan2(Math.cos(a), 1.2 * Math.cos(2 * a)),
  };
}

/** A hit's juice (U6): confetti that bursts from it and a score that jumps when it changes. */
export const juice = {
  pieces: 24,
  life: 1.4,
  gravity: 4.2,
  drag: 1.6,
  sideDrag: 2.0,
  speed: [2.2, 3.0] as const,
  tilt: [0.25, 0.75] as const,
  pulseMs: 380,
  pulseAmp: 0.45,
};

/** The fractional part, for spreading pieces evenly without chance. */
const frac = (x: number) => x - Math.floor(x);

/**
 * Where confetti piece `i` is, `seconds` after its burst, from the hit: it flies up and out, slowed by the air, then
 * flutters down, turning, and shrinks away by the end of its life. Every burst is the same, so the check sees it alike.
 */
export function confettiPiece(
  seconds: number,
  i: number,
): { x: number; y: number; z: number; rx: number; ry: number; rz: number; scale: number } {
  const t = seconds;
  if (!(t >= 0 && t < juice.life)) return { x: 0, y: 0, z: 0, rx: 0, ry: 0, rz: 0, scale: 0 };
  const around = i * 2.39996;
  const tilt = juice.tilt[0] + (juice.tilt[1] - juice.tilt[0]) * frac(i * 0.618);
  const speed = juice.speed[0] + (juice.speed[1] - juice.speed[0]) * frac(i * 0.381);
  const up = speed * Math.cos(tilt);
  const out = ((speed * Math.sin(tilt)) / juice.sideDrag) * (1 - Math.exp(-juice.sideDrag * t));
  const falling = juice.gravity / juice.drag;
  const flutter = 0.03 * Math.sin(9 * t + i * 1.7) * Math.min(1, t * 4);
  const scale = t > juice.life - 0.35 ? (juice.life - t) / 0.35 : Math.min(1, t / 0.05);
  return {
    x: out * Math.cos(around) + flutter,
    y: ((up + falling) / juice.drag) * (1 - Math.exp(-juice.drag * t)) - falling * t,
    z: out * Math.sin(around),
    rx: (5 + 4 * frac(i * 0.37)) * t,
    ry: (3 + 3 * frac(i * 0.53)) * t,
    rz: (2 + 5 * frac(i * 0.71)) * t,
    scale,
  };
}

/** A score's jump when it changes: a quick grow, a little past, back to exactly 1 within pulseMs. */
export function pulse(seconds: number): number {
  const x = (seconds * 1000) / juice.pulseMs;
  if (!(x > 0 && x < 1)) return 1;
  return 1 + juice.pulseAmp * Math.sin(Math.PI * x) * (1 - x) ** 0.6;
}
