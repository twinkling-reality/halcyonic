/**
 * The sound of a struck string or bar, as numbers: its partials, how each dies away, and the samples they make. Kept
 * free of imports so it can be tested on its own; `src/instruments.ts` plays what it makes.
 */

/** One partial: its frequency, its level, and how fast its two stages fall to a third (1/e), in seconds. */
export interface Partial {
  readonly hz: number;
  readonly level: number;
  readonly quick: number;
  readonly long: number;
}

/** A MIDI number's frequency in hertz, equal tempered, A4 (69) at 440 Hz. */
export function hertz(midi: number): number {
  return 440 * 2 ** ((midi - 69) / 12);
}

/**
 * How stiff a piano string is at a key, as the inharmonicity B: the n-th partial sounds at n * f * sqrt(1 + B n^2)
 * over n * f * sqrt(1 + B), a little sharp, more so in the treble.
 */
export function stiffness(midi: number): number {
  return midi <= 48 ? 0.00012 : 10 ** (-3.92 + (midi - 48) * 0.035);
}

/** Seconds the long ring of a piano key's fundamental takes to fall to a third: long in the bass, short in the treble. */
export function pianoRing(midi: number): number {
  return Math.min(9, Math.max(0.35, 4.5 * 2 ** (-(midi - 48) / 14)));
}

/** Seconds of sound kept for a piano key: until its ring has fallen about 30 dB, between 1.2 and 5. */
export function pianoSeconds(midi: number): number {
  return Math.min(5, Math.max(1.2, 3.5 * pianoRing(midi)));
}

/**
 * The partials of a piano key: the fundamental exactly in tune, the others slightly sharp as on real strings, the
 * hammer's strike point thinning some, the higher ones dying faster, each with a quick first stage and a long ring.
 */
export function pianoPartials(midi: number, sampleRate = 48000): Partial[] {
  const f = hertz(midi);
  const b = stiffness(midi);
  const ring = pianoRing(midi);
  const most = Math.round(Math.min(30, Math.max(3, 56 * (55 / f) ** 0.7)));
  const top = Math.min(11000, sampleRate * 0.45);
  const partials: Partial[] = [];
  for (let n = 1; n <= most; n++) {
    const hz = (n * f * Math.sqrt(1 + b * n * n)) / Math.sqrt(1 + b);
    if (hz >= top) break;
    // The hammer strikes about an eighth of the way along: partials near multiples of 8.5 are thin.
    const struck = Math.abs(Math.sin((Math.PI * n) / 8.5));
    // Treble keys are mostly fundamental; bass keys are rich.
    const tilt = 1 / n ** (0.75 + Math.max(0, midi - 60) / 40);
    const long = ring / (1 + hz / 2000);
    partials.push({ hz, level: struck * tilt, quick: long / 6, long });
  }
  return partials;
}

/** The partials of a soft bell, a tuned metal bar: a strong fundamental and a few bright, quickly fading overtones. */
export function bellPartials(midi: number): Partial[] {
  const f = hertz(midi);
  const ring = Math.min(5, Math.max(0.8, 2.6 * (523 / f) ** 0.35));
  const overtones: readonly (readonly [number, number])[] = [
    [1, 1],
    [2, 0.18],
    [2.76, 0.22],
    [5.4, 0.09],
    [8.93, 0.035],
  ];
  return overtones.map(([ratio, level]) => {
    const long = ring / ratio ** 0.8;
    return { hz: f * ratio, level, quick: long / 3, long };
  });
}

/** Seconds of sound kept for a bell. */
export function bellSeconds(midi: number): number {
  return Math.min(6, 4 * (bellPartials(midi)[0]?.long ?? 1.5));
}

/**
 * Makes the samples of a struck tone from its partials, a slice at a time: each call of the returned step adds up to
 * `slice` more samples and says whether it is done, so a page can make a tone over several frames without stalling
 * one. Each partial is two strings: one a hair flat carrying the quick first stage, one exactly in tune carrying the
 * long ring, so they beat slowly as a piano's do. A short strike of noise (`strike`, its level) marks the hammer.
 * The samples are normalised to a peak of 1 when done.
 */
export function strikeSteps(
  partials: readonly Partial[],
  seconds: number,
  rate: number,
  options: { strike?: number; strikeSeconds?: number; apart?: number } = {},
): { samples: Float32Array<ArrayBuffer>; step: (slice: number) => boolean } {
  const length = Math.ceil(seconds * rate);
  const samples = new Float32Array(length);
  const apart = options.apart ?? 0.0005;
  const fadeFrom = Math.max(0, length - Math.floor(0.25 * rate));
  const attack = Math.max(1, Math.floor(0.0015 * rate));
  let partial = 0;
  let from = 0;
  // Each partial is made whole before the next, from its own oscillators: a sine by recurrence, its level by a
  // constant ratio per sample, so a sample costs a few multiplications.
  let s1a = 0;
  let s2a = 0;
  let s1b = 0;
  let s2b = 0;
  let ka = 0;
  let kb = 0;
  let ea = 0;
  let eb = 0;
  let da = 0;
  let db = 0;
  const begin = (p: Partial, n: number) => {
    const wa = (2 * Math.PI * p.hz * (1 - apart)) / rate;
    const wb = (2 * Math.PI * p.hz) / rate;
    const phase = (n * 2.399) % (2 * Math.PI);
    ka = 2 * Math.cos(wa);
    kb = 2 * Math.cos(wb);
    s1a = Math.sin(phase - wa);
    s2a = Math.sin(phase - 2 * wa);
    s1b = Math.sin(phase + 1.1 - wb);
    s2b = Math.sin(phase + 1.1 - 2 * wb);
    ea = p.level * 0.62;
    eb = p.level * 0.38;
    da = Math.exp(-1 / (p.quick * rate));
    db = Math.exp(-1 / (p.long * rate));
  };
  if (partials.length > 0) begin(partials[0] as Partial, 0);
  let noiseDone = false;
  const step = (slice: number): boolean => {
    let budget = slice;
    while (partial < partials.length && budget > 0) {
      const to = Math.min(length, from + budget);
      // Locals, not the closure's own variables, so the loop runs in registers.
      let x1a = s1a;
      let x2a = s2a;
      let x1b = s1b;
      let x2b = s2b;
      let la = ea;
      let lb = eb;
      const ca = ka;
      const cb = kb;
      const fa = da;
      const fb = db;
      for (let i = from; i < to; i++) {
        const a = ca * x1a - x2a;
        x2a = x1a;
        x1a = a;
        const b = cb * x1b - x2b;
        x2b = x1b;
        x1b = b;
        const v = a * la + b * lb;
        samples[i] = (samples[i] as number) + (i < attack ? (v * i) / attack : v);
        la *= fa;
        lb *= fb;
      }
      s1a = x1a;
      s2a = x2a;
      s1b = x1b;
      s2b = x2b;
      ea = la;
      eb = lb;
      budget -= to - from;
      from = to;
      // A partial is done at the end, or once both its strings have fallen below -90 dB.
      if (from >= length || ea + eb < 1e-4) {
        partial += 1;
        from = 0;
        if (partial < partials.length) begin(partials[partial] as Partial, partial);
      }
    }
    if (partial < partials.length) return false;
    if (!noiseDone) {
      noiseDone = true;
      // The strike: a few milliseconds of noise, smoothed to take the hiss off, falling away fast.
      const level = options.strike ?? 0.04;
      const lasts = options.strikeSeconds ?? 0.006;
      let state = 3;
      let smooth = 0;
      const n = Math.min(length, Math.ceil(lasts * 8 * rate));
      const fall = Math.exp(-1 / (lasts * rate));
      let e = level;
      for (let i = 0; i < n; i++) {
        state = (state * 16807) % 2147483647;
        smooth = smooth * 0.55 + ((state / 2147483647) * 2 - 1) * 0.45;
        samples[i] = (samples[i] as number) + smooth * e;
        e *= fall;
      }
      let peak = 0;
      for (let i = 0; i < length; i++) {
        if (i >= fadeFrom) samples[i] = (samples[i] as number) * (1 - (i - fadeFrom) / (length - fadeFrom));
        peak = Math.max(peak, Math.abs(samples[i] as number));
      }
      if (peak > 0) for (let i = 0; i < length; i++) samples[i] = (samples[i] as number) / peak;
    }
    return true;
  };
  return { samples, step };
}

/** The struck instruments `src/instruments.ts` plays from made samples. */
export type StruckKind = 'piano' | 'bell';

/** Everything that makes one key of a struck instrument: its partials, its length and its strike. */
export function struck(
  kind: StruckKind,
  midi: number,
  rate: number,
): { partials: Partial[]; seconds: number; options: { strike: number; strikeSeconds: number; apart: number } } {
  if (kind === 'bell')
    return { partials: bellPartials(midi), seconds: bellSeconds(midi), options: { strike: 0.02, strikeSeconds: 0.002, apart: 0.0008 } };
  return {
    partials: pianoPartials(midi, rate),
    seconds: pianoSeconds(midi),
    options: { strike: 0.05, strikeSeconds: midi < 48 ? 0.01 : 0.005, apart: 0.0005 },
  };
}

/** Makes a key's samples whole, at once. */
export function strikeKey(kind: StruckKind, midi: number, rate: number): Float32Array<ArrayBuffer> {
  const { partials, seconds, options } = struck(kind, midi, rate);
  const { samples, step } = strikeSteps(partials, seconds, rate, options);
  while (!step(1 << 30));
  return samples;
}
