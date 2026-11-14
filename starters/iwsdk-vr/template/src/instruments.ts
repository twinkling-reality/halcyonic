import type { Heard, Note, Score } from './score.js';
import { eachHeard, frequency, longestSeconds, occurrences, roundSeconds } from './score.js';
import { type StruckKind, struck, strikeSteps } from './tone.js';

/**
 * Instruments made in the browser, no file and no network: a piano, a bell and a warm pad, playing through a room made
 * in code. A browser plays sound only after the person has pressed something on the page (entering VR is a press), so
 * the music starts then, by itself. `playScore` plays a score from `src/pieces.ts` on the audio clock and tells the
 * app when each note sounds, so light and sound land together. The piano's and the bell's keys are made from their
 * partials (`src/tone.ts`) off the page's own thread, in the order the score first needs them.
 */

/** A sounding note, to let it go early. */
export interface Sounding {
  /** Lets the note go at audio time `at` (now when not given): it dies away as a damped string does. */
  stop(at?: number): void;
}

/** An instrument: it is prepared for the keys it will play, then plays them on the audio clock. */
export interface Voice {
  /** Makes the sound of each key, in the order given, before it is played. */
  prepare(keys: Iterable<number>): Promise<void>;
  /**
   * Plays `midi` at audio time `when`, held `seconds`, `loud` from 0 to 1. A key not yet made is played from the
   * nearest one made, retuned; with none near, nothing plays.
   */
  play(midi: number, when: number, seconds: number, loud: number): Sounding;
}

export type VoiceName = 'piano' | 'bell' | 'pad';

/** The instruments on one audio context. */
export interface Music {
  readonly context: BaseAudioContext;
  readonly voices: Readonly<Record<VoiceName, Voice>>;
}

/** A fixed sequence of noise, so every load sounds the same. */
function noise(length: number, seed: number): Float32Array<ArrayBuffer> {
  const samples = new Float32Array(length);
  let state = seed;
  for (let i = 0; i < length; i++) {
    state = (state * 16807) % 2147483647;
    samples[i] = (state / 2147483647) * 2 - 1;
  }
  return samples;
}

/**
 * A room made in code: a stereo impulse of early reflections and a tail of about two seconds whose highs die first,
 * as a quiet hall's do.
 */
function roomImpulse(context: BaseAudioContext): AudioBuffer {
  const rate = context.sampleRate;
  const seconds = 2.4;
  const length = Math.floor(rate * seconds);
  const impulse = context.createBuffer(2, length, rate);
  for (let channel = 0; channel < 2; channel++) {
    const data = impulse.getChannelData(channel);
    const hiss = noise(length, 7 + channel * 101);
    let low = 0;
    const delay = Math.floor(0.016 * rate);
    for (let i = delay; i < length; i++) {
      const t = (i - delay) / rate;
      // Darker as it goes: a lowpass that closes from wide open to about 2 kHz over the tail.
      const close = Math.min(0.92, 0.15 + t * 0.55);
      low = low * close + (hiss[i] as number) * (1 - close);
      data[i] = low * Math.exp((-6.9 * t) / 2.0) * (1 + 2.5 * (1 - close));
    }
    // Early reflections from walls at different distances, a little different in each ear.
    [0.011, 0.019, 0.027, 0.034, 0.047, 0.058, 0.071].forEach((at, k) => {
      const i = Math.floor((at + channel * 0.0023 * (k % 3)) * rate);
      if (i < length) data[i] = (data[i] as number) + (k % 2 === 0 ? 0.6 : -0.5) / (1 + k * 0.6);
    });
  }
  return impulse;
}

/** A soft ceiling: straight up to 0.7, then curving gently to 1, so many keys at once never clip, with no delay. */
function ceiling(context: BaseAudioContext): WaveShaperNode {
  const shaper = context.createWaveShaper();
  const curve = new Float32Array(4097);
  for (let i = 0; i < curve.length; i++) {
    const x = (i / (curve.length - 1)) * 2 - 1;
    const a = Math.abs(x);
    curve[i] = Math.sign(x) * (a <= 0.7 ? a : 0.7 + 0.3 * Math.tanh((a - 0.7) / 0.3));
  }
  shaper.curve = curve;
  return shaper;
}

let worker: Worker | null | undefined;
let asked = 0;
const waiting = new Map<number, { kind: StruckKind; midi: number; rate: number; resolve: (samples: Float32Array<ArrayBuffer>) => void }>();

/** Makes a key's samples on the page itself, a slice at a time between frames, so no frame stalls long. */
function makeHere(kind: StruckKind, midi: number, rate: number): Promise<Float32Array<ArrayBuffer>> {
  const { partials, seconds, options } = struck(kind, midi, rate);
  const { samples, step } = strikeSteps(partials, seconds, rate, options);
  return new Promise((resolve) => {
    const more = () => (step(24000) ? resolve(samples) : setTimeout(more, 0));
    more();
  });
}

/** Makes a struck key's samples: on a worker when the page can start one, otherwise on the page between frames. */
function makeKey(kind: StruckKind, midi: number, rate: number): Promise<Float32Array<ArrayBuffer>> {
  if (worker === undefined) {
    try {
      worker = new Worker(new URL('./tone-worker.ts', import.meta.url), { type: 'module' });
      worker.onmessage = (event: MessageEvent<{ id: number; samples: Float32Array<ArrayBuffer> }>) => {
        waiting.get(event.data.id)?.resolve(event.data.samples);
        waiting.delete(event.data.id);
      };
      // A worker that cannot run after all: drop it, and make what it was asked for on the page instead.
      worker.onerror = () => {
        worker = null;
        for (const [id, job] of waiting) {
          waiting.delete(id);
          void makeHere(job.kind, job.midi, job.rate).then(job.resolve);
        }
      };
    } catch {
      worker = null;
    }
  }
  if (worker !== null && worker !== undefined) {
    const id = ++asked;
    const sent = worker;
    return new Promise((resolve) => {
      waiting.set(id, { kind, midi, rate, resolve });
      sent.postMessage({ id, kind, midi, rate });
    });
  }
  return makeHere(kind, midi, rate);
}

/**
 * A voice that plays made keys: each note one buffer source, a lowpass that opens with how hard it is struck, its
 * level, and its place left to right (low keys to the left, as at a keyboard).
 */
function struckVoice(
  context: BaseAudioContext,
  out: AudioNode,
  kind: StruckKind,
  tone: { level: number; bright: number; damp: (midi: number) => number; undamped: number },
): Voice {
  const buffers = new Map<number, AudioBuffer>();
  const asking = new Map<number, Promise<void>>();
  const nearest = (midi: number): number | null => {
    for (let away = 0; away <= 4; away++) {
      if (buffers.has(midi - away)) return midi - away;
      if (buffers.has(midi + away)) return midi + away;
    }
    return null;
  };
  return {
    async prepare(keys) {
      for (const midi of keys) {
        if (buffers.has(midi)) continue;
        let made = asking.get(midi);
        if (made === undefined) {
          made = makeKey(kind, midi, context.sampleRate).then((samples) => {
            const buffer = context.createBuffer(1, samples.length, context.sampleRate);
            buffer.copyToChannel(samples, 0);
            buffers.set(midi, buffer);
          });
          asking.set(midi, made);
        }
        await made;
      }
    },
    play(midi, when, seconds, loud) {
      const from = nearest(midi);
      if (from === null) return { stop() {} };
      const buffer = buffers.get(from) as AudioBuffer;
      const touch = Math.min(1, Math.max(0, loud));
      const source = context.createBufferSource();
      source.buffer = buffer;
      source.playbackRate.value = 2 ** ((midi - from) / 12);
      const filter = context.createBiquadFilter();
      filter.type = 'lowpass';
      filter.frequency.value = Math.min(18000, Math.max(700, frequency(midi) * (2 + tone.bright * touch * touch)));
      filter.Q.value = 0.5;
      const level = context.createGain();
      // Loudness by touch: about 30 dB from the softest to the hardest; the treble a little softer, as the ear wants.
      const treble = Math.max(0, midi - 72) / 48;
      level.gain.value = tone.level * (0.03 + 0.97 * touch ** 1.7) * (1 - 0.35 * treble);
      const place = context.createStereoPanner();
      place.pan.value = Math.min(0.55, Math.max(-0.55, (midi - 62) / 40));
      source.connect(filter);
      filter.connect(level);
      level.connect(place);
      place.connect(out);
      const start = Math.max(when, context.currentTime);
      source.start(start);
      const ends = start + buffer.duration / source.playbackRate.value;
      // A damper on the string; keys above the undamped line ring on, as on a real piano.
      const tau = midi >= tone.undamped ? 0.6 : tone.damp(midi);
      let released = Number.POSITIVE_INFINITY;
      const stop = (at?: number) => {
        const off = Math.max(at ?? context.currentTime, context.currentTime);
        if (off >= released) return;
        released = off;
        // Let go before it is heard: it is never heard.
        if (off <= start) {
          source.stop(start);
          return;
        }
        if (off + tau * 7 >= ends) return;
        level.gain.cancelScheduledValues(off);
        level.gain.setTargetAtTime(0, off, tau);
        source.stop(off + tau * 7);
      };
      if (Number.isFinite(seconds)) stop(start + Math.max(0.02, seconds));
      return { stop };
    },
  };
}

/** A warm pad, made live: two detuned saws and a soft octave below, through a gentle lowpass, swelling in and out. */
function padVoice(context: BaseAudioContext, out: AudioNode): Voice {
  return {
    async prepare() {},
    play(midi, when, seconds, loud) {
      const start = Math.max(when, context.currentTime);
      const f = frequency(midi);
      const level = context.createGain();
      const peak = 0.06 * (0.3 + 0.7 * Math.min(1, Math.max(0, loud)));
      const rise = Math.min(1.6, Math.max(0.3, seconds * 0.35));
      level.gain.setValueAtTime(0, start);
      level.gain.linearRampToValueAtTime(peak, start + rise);
      const filter = context.createBiquadFilter();
      filter.type = 'lowpass';
      filter.frequency.value = Math.min(2400, 500 + f * 2);
      filter.Q.value = 0.4;
      const place = context.createStereoPanner();
      place.pan.value = ((midi % 5) - 2) * 0.18;
      filter.connect(level);
      level.connect(place);
      place.connect(out);
      const oscillators = [
        { type: 'sawtooth' as const, hz: f * 2 ** (6 / 1200), gain: 0.5 },
        { type: 'sawtooth' as const, hz: f * 2 ** (-6 / 1200), gain: 0.5 },
        { type: 'triangle' as const, hz: f / 2, gain: 0.35 },
      ].map(({ type, hz, gain }) => {
        const oscillator = context.createOscillator();
        oscillator.type = type;
        oscillator.frequency.value = hz;
        const share = context.createGain();
        share.gain.value = gain;
        oscillator.connect(share);
        share.connect(filter);
        oscillator.start(start);
        return oscillator;
      });
      let stopped = false;
      const stop = (at?: number) => {
        if (stopped) return;
        stopped = true;
        const off = Math.max(at ?? context.currentTime, start + 0.05);
        level.gain.cancelScheduledValues(off);
        level.gain.setValueAtTime(peak, off);
        level.gain.setTargetAtTime(0, off, 0.9);
        for (const oscillator of oscillators) oscillator.stop(off + 6);
      };
      if (Number.isFinite(seconds)) stop(start + seconds);
      return { stop };
    },
  };
}

/**
 * The instruments on a context: a piano, a bell and a pad, through one room, into `destination`. A context of the
 * page's own comes from `pageMusic`; an offline one renders a score to measure it (`renderScore`).
 */
export function createMusic(context: BaseAudioContext, destination: AudioNode = context.destination): Music {
  const bus = context.createGain();
  const dry = context.createGain();
  dry.gain.value = 0.85;
  const send = context.createGain();
  send.gain.value = 0.3;
  const room = context.createConvolver();
  room.buffer = roomImpulse(context);
  const master = context.createGain();
  master.gain.value = 2.2;
  const top = ceiling(context);
  bus.connect(dry);
  bus.connect(send);
  send.connect(room);
  dry.connect(master);
  room.connect(master);
  master.connect(top);
  top.connect(destination);
  const piano = struckVoice(context, bus, 'piano', {
    level: 0.34,
    bright: 26,
    damp: (midi) => (midi < 48 ? 0.22 : 0.09),
    undamped: 89,
  });
  const bell = struckVoice(context, bus, 'bell', { level: 0.22, bright: 30, damp: () => 1.2, undamped: 0 });
  return { context, voices: { piano, bell, pad: padVoice(context, bus) } };
}

let shared: Music | null = null;

/** The page's music, made at the first call after a press, running or not yet. */
function madeMusic(): Music | null {
  if (shared === null) {
    if (typeof AudioContext === 'undefined') return null;
    // Only once the page has had a press: before it, a browser keeps a new context silent and warns.
    if (!(navigator.userActivation?.hasBeenActive ?? false)) return null;
    try {
      shared = createMusic(new AudioContext({ latencyHint: 'interactive' }));
    } catch {
      return null;
    }
  }
  const context = shared.context as AudioContext;
  if (context.state === 'suspended') context.resume().catch(() => {});
  return shared;
}

/**
 * The page's music, once a press has let the browser play sound: null before then, and while the browser holds it
 * paused. Call it every frame (playScore does); the first call after a press makes the audio context.
 */
export function pageMusic(): Music | null {
  const music = madeMusic();
  return music !== null && music.context.state === 'running' ? music : null;
}

/** The audio time being heard at this moment: the context's clock less what is still on its way to the ears. */
function heardNow(context: BaseAudioContext): number {
  const live = context as AudioContext;
  const stamp = typeof live.getOutputTimestamp === 'function' ? live.getOutputTimestamp() : undefined;
  if (stamp?.contextTime !== undefined && stamp.performanceTime !== undefined && stamp.performanceTime > 0)
    return stamp.contextTime + (performance.now() - stamp.performanceTime) / 1000;
  return context.currentTime - (live.outputLatency ?? 0) - (live.baseLatency ?? 0);
}

export interface PlayOptions {
  /** Which voice plays each part (by its number in the score): one voice for all, or a voice per part. */
  readonly voice?: VoiceName | ((part: number) => VoiceName | null);
  /** Play it again after `gap` seconds of quiet, for ever. */
  readonly loop?: boolean;
  readonly gap?: number;
  /** Seconds before the first beat when the player starts, so light can come toward it first. */
  readonly lead?: number;
  /**
   * When sound first starts (at the person's first press, or entering VR), go back to the top so they hear it from
   * its first note, a second on. True unless false: false carries on from wherever the light had got to.
   */
  readonly fromTop?: boolean;
}

/** A score playing. Its time is play seconds: 0 is the score's first beat, rounds follow on when it loops. */
export interface Player {
  readonly score: Score;
  /**
   * The play time being heard now: on the audio clock once sound runs, on the game's time before it (and in a check
   * with no sound), so the light moves the same either way. Read it once a frame and place everything from it.
   */
  now(): number;
  /**
   * Visits each note due or still held from `behind` seconds ago to `ahead` seconds on, with its start and end in
   * play seconds: what light should show. Makes nothing new, so it can run every frame.
   */
  each(behind: number, ahead: number, visit: (note: Note, start: number, end: number) => void): void;
  /** Whether sound is playing yet: false before a press and while the instruments are made. */
  readonly sounding: boolean;
  readonly playing: boolean;
  pause(): void;
  resume(): void;
}

/** The keys a score needs for each voice, each in the order it is first played. */
function keysInOrder(score: Score, voiceOf: (part: number) => VoiceName | null): Map<VoiceName, number[]> {
  const keys = new Map<VoiceName, number[]>();
  for (const note of score.notes) {
    const name = voiceOf(note.part);
    if (name === null) continue;
    const list = keys.get(name) ?? [];
    if (!list.includes(note.pitch)) list.push(note.pitch);
    keys.set(name, list);
  }
  return keys;
}

/**
 * Plays `score` (from `src/pieces.ts`, or your own) on the given frame hook (`ambientMotion` from the kit): on the
 * game's time at first, then on the audio clock once a press lets sound start, scheduling each note a little ahead so
 * it sounds on time to the sample. A note is heard at play time `start`; light placed from `player.now()` lands with
 * it on the same frame.
 */
export function playScore(
  score: Score,
  frame: (move: (time: number) => void) => void,
  options: PlayOptions = {},
): Player {
  const loop = options.loop ?? false;
  const gap = options.gap ?? 4;
  const voiceOf = (part: number): VoiceName | null =>
    typeof options.voice === 'function' ? options.voice(part) : (options.voice ?? 'piano');
  const ahead = 0.3;
  const longest = longestSeconds(score);
  let paused = false;
  let pausedAt = 0;
  // Play time is clock - origin: on the game's clock until sound runs, then on the heard audio clock.
  let onAudio = false;
  let origin = Number.NaN;
  let latestGame = 0;
  let music: Music | null = null;
  let ready = false;
  let scheduled = 0;
  const sounding = new Map<number, Sounding>();
  const live: Sounding[] = [];
  let context: BaseAudioContext | null = null;
  let lag = 0;

  const clock = () => (onAudio && music !== null ? heardNow(music.context) : latestGame);
  const now = () => (paused ? pausedAt : clock() - origin);
  const sound = (note: Note, _index: number, _round: number, start: number, _end: number, rings: number) => {
    const name = voiceOf(note.part);
    if (name === null || music === null || context === null) return;
    const when = origin + start + lag;
    if (when < context.currentTime - 0.01) return;
    sounding.get(note.pitch)?.stop(when);
    const playing = music.voices[name].play(note.pitch, when, rings - start, note.loud);
    sounding.set(note.pitch, playing);
    live.push(playing);
    if (live.length > 128) live.shift();
  };

  frame((time) => {
    latestGame = time;
    if (Number.isNaN(origin)) origin = time + (options.lead ?? 3);
    if (music === null) {
      music = pageMusic();
      if (music !== null) {
        const found = music;
        const keys = keysInOrder(score, voiceOf);
        // The opening's keys first, then the rest while it plays.
        Promise.all([...keys].map(([name, list]) => found.voices[name].prepare(list.slice(0, 8))))
          .then(() => {
            ready = true;
            for (const [name, list] of keys) void found.voices[name].prepare(list);
          })
          .catch(() => {});
      }
    }
    if (music === null || !ready || paused) return;
    context = music.context;
    if (!onAudio) {
      // Hand the clock over: from the top, a second before the first note, or carrying on without a jump.
      const at = options.fromTop === false ? clock() - origin : -Math.min(1, options.lead ?? 3);
      onAudio = true;
      origin = heardNow(context) - at;
      scheduled = at;
    }
    // Heard time and the context's own clock differ by the output's delay: notes are scheduled on the context's.
    lag = context.currentTime - heardNow(context);
    const until = now() + ahead;
    if (until <= scheduled) return;
    eachHeard(score, scheduled, until, loop, gap, sound);
    scheduled = until;
  });

  return {
    score,
    now,
    each(behind, ahead, visit) {
      const at = now();
      eachHeard(score, at - behind - longest, at + ahead, loop, gap, (note, _index, _round, start, end) => {
        if (end > at - behind) visit(note, start, end);
      });
    },
    get sounding() {
      return onAudio;
    },
    get playing() {
      return !paused;
    },
    pause() {
      if (paused) return;
      pausedAt = now();
      paused = true;
      for (const note of live) note.stop();
      live.length = 0;
      sounding.clear();
    },
    resume() {
      if (!paused) return;
      paused = false;
      origin = clock() - pausedAt;
      scheduled = pausedAt;
    },
  };
}

/**
 * Renders `score` once through into an offline buffer, for measuring what it sounds like on a computer that cannot
 * listen: the same instruments, room and scheduling as the page plays.
 */
export async function renderScore(
  score: Score,
  options: { voice?: PlayOptions['voice']; seconds?: number; rate?: number } = {},
): Promise<{ buffer: AudioBuffer; heard: Heard[] }> {
  const rate = options.rate ?? 48000;
  const seconds = options.seconds ?? roundSeconds(score, 0) + 3;
  const offline = new OfflineAudioContext(2, Math.ceil(seconds * rate), rate);
  const music = createMusic(offline);
  const voiceOf = (part: number): VoiceName | null =>
    typeof options.voice === 'function' ? options.voice(part) : (options.voice ?? 'piano');
  for (const [name, list] of keysInOrder(score, voiceOf)) await music.voices[name].prepare(list);
  const heard = occurrences(score, 0, seconds);
  const sounding = new Map<number, Sounding>();
  for (const note of heard) {
    const name = voiceOf(note.note.part);
    if (name === null) continue;
    sounding.get(note.note.pitch)?.stop(note.start);
    sounding.set(note.note.pitch, music.voices[name].play(note.note.pitch, note.start, note.rings - note.start, note.note.loud));
  }
  return { buffer: await offline.startRendering(), heard };
}
