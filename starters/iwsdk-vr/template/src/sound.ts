import type { WorldLook } from './worlds.js';

/**
 * The world's own sound (`sound` in `src/worlds.ts`): wind, birds, waves, gulls or crickets, made in the browser from
 * noise and tones, so there is no file to load. A browser plays sound only after the person has pressed something on
 * the page (entering VR is a press), so it starts then, by itself. The calls and chirps come on the game's own time,
 * from `ambientMotion`; leave this to `setupLook`.
 */
export function startAmbience(sound: WorldLook['sound'], ambientMotion: (move: (time: number) => void) => void): void {
  const wanted = Object.values(sound).some((level) => (level ?? 0) > 0);
  if (!wanted || typeof AudioContext === 'undefined') return;
  let audio: { context: AudioContext; out: GainNode } | null = null;
  let failed = false;
  let nextBird = 2;
  let nextGull = 4;
  let birdCall = 0;
  ambientMotion((time) => {
    if (failed) return;
    if (audio === null) {
      // Only once the page has had a press: before it, a browser keeps a new context silent and warns.
      if (!(navigator.userActivation?.hasBeenActive ?? false)) return;
      try {
        audio = build(sound);
      } catch {
        failed = true;
        return;
      }
    }
    const { context, out } = audio;
    if (context.state === 'suspended') {
      context.resume().catch(() => {});
      return;
    }
    if ((sound.birds ?? 0) > 0 && time >= nextBird) {
      birdCall += 1;
      chirp(context, out, sound.birds ?? 0, birdCall);
      nextBird = time + 2.2 + ((birdCall * 1.7) % 3.1);
    }
    if ((sound.gulls ?? 0) > 0 && time >= nextGull) {
      gull(context, out, sound.gulls ?? 0);
      nextGull = time + 6.5 + ((time * 0.37) % 4);
    }
  });
}

/** The context, its master level, and the steady layers: wind, waves and crickets. */
function build(sound: WorldLook['sound']): { context: AudioContext; out: GainNode } {
  const context = new AudioContext();
  const out = context.createGain();
  out.gain.value = 0.32;
  out.connect(context.destination);
  const noise = context.createBuffer(1, context.sampleRate * 2, context.sampleRate);
  const samples = noise.getChannelData(0);
  // A fixed sequence, so the sound is the same on every load.
  let seed = 1;
  for (let i = 0; i < samples.length; i++) {
    seed = (seed * 16807) % 2147483647;
    samples[i] = (seed / 2147483647) * 2 - 1;
  }
  const hiss = (cutoff: number) => {
    const source = context.createBufferSource();
    source.buffer = noise;
    source.loop = true;
    const filter = context.createBiquadFilter();
    filter.type = 'lowpass';
    filter.frequency.value = cutoff;
    source.connect(filter);
    source.start();
    return filter;
  };
  const swell = (node: AudioNode, level: number, depth: number, hz: number) => {
    const gain = context.createGain();
    gain.gain.value = level;
    const lfo = context.createOscillator();
    lfo.frequency.value = hz;
    const amount = context.createGain();
    amount.gain.value = level * depth;
    lfo.connect(amount);
    amount.connect(gain.gain);
    lfo.start();
    node.connect(gain);
    gain.connect(out);
  };
  if ((sound.wind ?? 0) > 0) swell(hiss(380), 0.5 * (sound.wind ?? 0), 0.6, 0.07);
  if ((sound.waves ?? 0) > 0) swell(hiss(650), 0.7 * (sound.waves ?? 0), 0.85, 0.11);
  if ((sound.crickets ?? 0) > 0) {
    // A high tone, pulsed fast, in slow bursts: two crickets out of step.
    for (const [pitch, rate, burst] of [
      [4400, 28, 0.55],
      [4900, 31, 0.43],
    ] as const) {
      const tone = context.createOscillator();
      tone.frequency.value = pitch;
      // Each gain swings round its own value by as much: from silent to full, never through zero to inverted.
      const pulse = context.createGain();
      pulse.gain.value = 0.5;
      const fast = context.createOscillator();
      fast.type = 'square';
      fast.frequency.value = rate;
      const fastAmount = context.createGain();
      fastAmount.gain.value = 0.5;
      fast.connect(fastAmount);
      fastAmount.connect(pulse.gain);
      const bursts = context.createGain();
      bursts.gain.value = 0.06 * (sound.crickets ?? 0);
      const slow = context.createOscillator();
      slow.type = 'square';
      slow.frequency.value = burst;
      const slowAmount = context.createGain();
      slowAmount.gain.value = 0.06 * (sound.crickets ?? 0);
      slow.connect(slowAmount);
      slowAmount.connect(bursts.gain);
      tone.connect(pulse);
      pulse.connect(bursts);
      bursts.connect(out);
      for (const node of [tone, fast, slow]) node.start();
    }
  }
  return { context, out };
}

/** A songbird's few quick notes, each a rising sweep. */
function chirp(context: AudioContext, out: GainNode, level: number, call: number): void {
  const notes = 2 + (call % 3);
  const base = 2600 + ((call * 377) % 900);
  for (let n = 0; n < notes; n++) {
    const at = context.currentTime + n * 0.11;
    const tone = context.createOscillator();
    tone.frequency.setValueAtTime(base, at);
    tone.frequency.exponentialRampToValueAtTime(base * 1.45, at + 0.07);
    const gain = context.createGain();
    gain.gain.setValueAtTime(0.0001, at);
    gain.gain.exponentialRampToValueAtTime(0.12 * level, at + 0.015);
    gain.gain.exponentialRampToValueAtTime(0.0001, at + 0.09);
    tone.connect(gain);
    gain.connect(out);
    tone.start(at);
    tone.stop(at + 0.1);
  }
}

/** A gull's call far off: two falling cries. */
function gull(context: AudioContext, out: GainNode, level: number): void {
  for (let n = 0; n < 2; n++) {
    const at = context.currentTime + n * 0.32;
    const tone = context.createOscillator();
    tone.type = 'sawtooth';
    tone.frequency.setValueAtTime(1250, at);
    tone.frequency.exponentialRampToValueAtTime(720, at + 0.26);
    const filter = context.createBiquadFilter();
    filter.type = 'bandpass';
    filter.frequency.value = 1400;
    filter.Q.value = 2;
    const gain = context.createGain();
    gain.gain.setValueAtTime(0.0001, at);
    gain.gain.exponentialRampToValueAtTime(0.05 * level, at + 0.04);
    gain.gain.exponentialRampToValueAtTime(0.0001, at + 0.28);
    tone.connect(filter);
    filter.connect(gain);
    gain.connect(out);
    tone.start(at);
    tone.stop(at + 0.3);
  }
}
