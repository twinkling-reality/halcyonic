import type { Note, Score } from './score.js';

/**
 * Pieces to play, as note data (`src/score.ts`), each about a minute and a half. Both are compositions in the public
 * domain, their pitches and lengths typed by hand from public-domain editions of the score; the touch, the pedal and
 * the tempo are this starter's own playing of them. Play one with `playScore(pieces.prelude, ...)`. Part 0 is the left
 * hand, part 1 the right. Never add a tune still in copyright, nor one taken from a recording or a MIDI file.
 */

const STEPS: Readonly<Record<string, number>> = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 };

/** A note's name to its MIDI number: 'C4' is 60, 'F#5' 78, 'Bb2' 46. Throws on anything else. */
export function pitch(name: string): number {
  const match = /^([A-G])(#|b)?(-?\d)$/.exec(name);
  if (match === null) throw new Error(`not a note name: ${name}`);
  const [, letter, accidental, octave] = match;
  const step = STEPS[letter as string] as number;
  const shift = accidental === '#' ? 1 : accidental === 'b' ? -1 : 0;
  return (Number(octave) + 1) * 12 + step + shift;
}

/** A score's notes in start order, so `occurrences` can stop early. */
function ordered(notes: Note[]): Note[] {
  return notes.sort((a, b) => a.at - b.at || a.pitch - b.pitch);
}

/**
 * Johann Sebastian Bach (1685 to 1750), Prelude in C major, BWV 846, from The Well-Tempered Clavier, Book 1 (1722).
 * Each bar is one chord of five keys played as a pattern: two held low and three above, twice to a half bar.
 */
function prelude(): Score {
  // [held low, held second, then the three above], as written, bars 1 to 32.
  const bars: readonly (readonly [string, string, string, string, string])[] = [
    ['C4', 'E4', 'G4', 'C5', 'E5'],
    ['C4', 'D4', 'A4', 'D5', 'F5'],
    ['B3', 'D4', 'G4', 'D5', 'F5'],
    ['C4', 'E4', 'G4', 'C5', 'E5'],
    ['C4', 'E4', 'A4', 'E5', 'A5'],
    ['C4', 'D4', 'F#4', 'A4', 'D5'],
    ['B3', 'D4', 'G4', 'D5', 'G5'],
    ['B3', 'C4', 'E4', 'G4', 'C5'],
    ['A3', 'C4', 'E4', 'G4', 'C5'],
    ['D3', 'A3', 'D4', 'F#4', 'C5'],
    ['G3', 'B3', 'D4', 'G4', 'B4'],
    ['G3', 'Bb3', 'E4', 'G4', 'C#5'],
    ['F3', 'A3', 'D4', 'A4', 'D5'],
    ['F3', 'Ab3', 'D4', 'F4', 'B4'],
    ['E3', 'G3', 'C4', 'G4', 'C5'],
    ['E3', 'F3', 'A3', 'C4', 'F4'],
    ['D3', 'F3', 'A3', 'C4', 'F4'],
    ['G2', 'D3', 'G3', 'B3', 'F4'],
    ['C3', 'E3', 'G3', 'C4', 'E4'],
    ['C3', 'G3', 'Bb3', 'C4', 'E4'],
    ['F2', 'F3', 'A3', 'C4', 'E4'],
    ['F#2', 'C3', 'A3', 'C4', 'Eb4'],
    ['Ab2', 'F3', 'B3', 'C4', 'D4'],
    ['G2', 'F3', 'G3', 'B3', 'D4'],
    ['G2', 'E3', 'G3', 'C4', 'E4'],
    ['G2', 'D3', 'G3', 'C4', 'F4'],
    ['G2', 'D3', 'G3', 'B3', 'F4'],
    ['G2', 'Eb3', 'A3', 'C4', 'F#4'],
    ['G2', 'E3', 'G3', 'C4', 'G4'],
    ['G2', 'D3', 'G3', 'C4', 'F4'],
    ['G2', 'D3', 'G3', 'B3', 'F4'],
    ['C2', 'C3', 'G3', 'Bb3', 'E4'],
  ];
  // The last three bars, as written: the two held low keys, then fourteen sixteenths above them; then the last chord.
  const close: readonly (readonly [string, string, readonly string[]])[] = [
    ['C2', 'C3', ['F3', 'A3', 'C4', 'F4', 'C4', 'A3', 'C4', 'A3', 'F3', 'A3', 'F3', 'D3', 'F3', 'D3']],
    ['C2', 'B2', ['G4', 'B4', 'D5', 'F5', 'D5', 'B4', 'D5', 'B4', 'G4', 'B4', 'D4', 'F4', 'E4', 'D4']],
  ];
  const notes: Note[] = [];
  // How hard each bar is played: a slow rise to the chords of bars 21 to 24, held over the long G of bars 24 to 31,
  // and down again at the close.
  const shape = (bar: number) => 0.46 + 0.14 * Math.sin(Math.min(1, bar / 23) * Math.PI * 0.5) - (bar > 31 ? 0.06 : 0);
  bars.forEach((chord, bar) => {
    const [low, second, ...above] = chord.map(pitch) as [number, number, number, number, number];
    const loud = shape(bar);
    for (let half = 0; half < 2; half++) {
      const at = bar * 4 + half * 2;
      notes.push({ at, length: 2, pitch: low, loud: loud + 0.04, part: 0 });
      notes.push({ at: at + 0.25, length: 1.75, pitch: second, loud: loud - 0.04, part: 0 });
      for (let i = 0; i < 6; i++)
        notes.push({
          at: at + 0.5 + i * 0.25,
          length: 0.25,
          pitch: above[i % 3] as number,
          loud: loud - 0.06 + (i % 3 === 0 ? 0.05 : 0) - (half === 1 ? 0.02 : 0),
          part: 1,
        });
    }
  });
  // The close slows a little toward the last chord: beats after `from` are drawn out more and more.
  const from = bars.length * 4;
  const slow = (beat: number) => (beat <= from ? beat : from + (beat - from) * (1 + 0.035 * (beat - from)));
  close.forEach(([low, second, run], i) => {
    const at = from + i * 4;
    const loud = shape(32 + i) - 0.03 * i;
    notes.push({ at: slow(at), length: slow(at + 4) - slow(at), pitch: pitch(low), loud: loud + 0.04, part: 0 });
    notes.push({ at: slow(at + 0.25), length: slow(at + 4) - slow(at + 0.25), pitch: pitch(second), loud: loud - 0.04, part: 0 });
    run.forEach((name, k) => {
      const start = slow(at + 0.5 + k * 0.25);
      notes.push({ at: start, length: slow(at + 0.75 + k * 0.25) - start, pitch: pitch(name), loud: loud - 0.04, part: 1 });
    });
  });
  const last = slow(from + 8);
  ['C2', 'C3'].forEach((name, k) => notes.push({ at: last + k * 0.08, length: 6, pitch: pitch(name), loud: 0.46, part: 0 }));
  ['E4', 'G4', 'C5'].forEach((name, k) =>
    notes.push({ at: last + 0.16 + k * 0.08, length: 6, pitch: pitch(name), loud: 0.4, part: 1 }),
  );
  // The pedal down through each half bar, lifted just before the next, and held through the close.
  const pedal: [number, number][] = [];
  for (let half = 0; half < bars.length * 2; half++) pedal.push([half * 2 + 0.05, half * 2 + 1.97]);
  pedal.push([from + 0.05, last - 0.03], [last + 0.05, last + 6.5]);
  return {
    title: 'Prelude in C',
    by: 'Johann Sebastian Bach, 1722',
    tempo: 96,
    beats: Math.ceil(last + 7),
    notes: ordered(notes),
    pedal,
  };
}

/**
 * Erik Satie (1866 to 1925), Gymnopedie No. 1 (1888), once through with its second ending: a low key on each bar's
 * first beat, a soft chord on its second, and the slow tune above.
 */
function gymnopedie(): Score {
  // The low key of each bar, bars 1 to 39.
  const bass = [
    ...Array.from({ length: 16 }, (_, bar) => (bar % 2 === 0 ? 'G2' : 'D2')),
    'F#2',
    'B1',
    'E2',
    'E2',
    'D2',
    'A1',
    ...Array.from({ length: 9 }, () => 'D2'),
    ...Array.from({ length: 6 }, () => 'E2'),
    'A2',
    'D2',
  ];
  const b = ['F#4', 'D4', 'B3'];
  const a = ['F#4', 'C#4', 'A3'];
  // The chord on the second beat of each bar, held to its end, bars 1 to 36; bar 37 has two, and 38 and 39 none.
  const chords: readonly (readonly string[])[] = [
    b, a, b, a, b, a, b, a, b, a, b, a, b, a, b, a, a, b,
    ['B3', 'G3'],
    ['G4', 'D4', 'B3'],
    ['D4', 'A3', 'F3'],
    ['E4', 'C4', 'A3'],
    ['E4', 'B3', 'G3'],
    ['E4', 'B3', 'G3', 'D3'],
    ['D4', 'A3', 'E3', 'C3'],
    ['D4', 'A3', 'F#3', 'C3'],
    ['F4', 'C4', 'A3'],
    ['E4', 'C4', 'A3'],
    ['E4', 'B3', 'G3', 'D3'],
    ['D4', 'A3', 'E3', 'C3'],
    ['D4', 'A3', 'F#3', 'C3'],
    ['G4', 'E4', 'B3'],
    ['A4', 'F4', 'D4', 'A3'],
    ['F4', 'C4', 'A3'],
    ['A4', 'E4', 'C4'],
    ['A4', 'F4', 'C4', 'A3'],
  ];
  // The tune, as [key, beats], from bar 5's second beat; 'rest' is a rest. The last two bars are chords.
  const tune: readonly (readonly [string, number])[] = [
    ['F#5', 1], ['A5', 1],
    ['G5', 1], ['F#5', 1], ['C#5', 1],
    ['B4', 1], ['C#5', 1], ['D5', 1],
    ['A4', 3],
    ['F#4', 12],
    ['rest', 1], ['F#5', 1], ['A5', 1],
    ['G5', 1], ['F#5', 1], ['C#5', 1],
    ['B4', 1], ['C#5', 1], ['D5', 1],
    ['A4', 3],
    ['C#5', 3],
    ['F#5', 3],
    ['E4', 9],
    ['A4', 1], ['B4', 1], ['C5', 1],
    ['E5', 1], ['D5', 1], ['B4', 1],
    ['D5', 1], ['C5', 1], ['B4', 1],
    ['D5', 5], ['D5', 1],
    ['E5', 1], ['F5', 1], ['G5', 1],
    ['A5', 1], ['C5', 1], ['D5', 1],
    ['E5', 1], ['D5', 1], ['B4', 1],
    ['D5', 5], ['D5', 1],
    ['G5', 3],
    ['F5', 3],
    ['B4', 1], ['C5', 1], ['F5', 1],
    ['E5', 1], ['D5', 1], ['C5', 1],
    ['E5', 1], ['D5', 1], ['C5', 1],
    ['F4', 3],
  ];
  const notes: Note[] = [];
  // Played softly throughout, a touch more in the middle bars (22 to 31), the tune always over its chords.
  const middle = (bar: number) => (bar >= 21 && bar <= 30 ? 0.05 : 0);
  bass.forEach((name, bar) =>
    notes.push({ at: bar * 3, length: bar === 38 ? 6 : 3, pitch: pitch(name), loud: 0.4 + middle(bar), part: 0 }),
  );
  chords.forEach((chord, bar) =>
    chord.forEach((name) => notes.push({ at: bar * 3 + 1, length: 2, pitch: pitch(name), loud: 0.27 + middle(bar), part: 0 })),
  );
  // Bar 37: a key inside the low E on beats two and three, under two chords.
  for (const [name, beat] of [['B2', 1], ['E3', 2]] as const)
    notes.push({ at: 36 * 3 + beat, length: 1, pitch: pitch(name), loud: 0.3, part: 0 });
  for (const [names, beat] of [[['D4', 'A3'], 1], [['G4', 'D4', 'B3'], 2]] as const)
    for (const name of names) notes.push({ at: 36 * 3 + beat, length: 1, pitch: pitch(name), loud: 0.26, part: 0 });
  // Bar 38: a G above the low A; bar 39: the low D with its fifth and octave, held as long as the chord above.
  notes.push({ at: 37 * 3, length: 3, pitch: pitch('G3'), loud: 0.3, part: 0 });
  notes.push({ at: 38 * 3, length: 6, pitch: pitch('A2'), loud: 0.32, part: 0 });
  notes.push({ at: 38 * 3, length: 6, pitch: pitch('D3'), loud: 0.3, part: 0 });
  let at = 4 * 3 + 1;
  tune.forEach(([name, beats], i) => {
    if (name !== 'rest') {
      // Each phrase leans toward its middle; long notes are struck a little more, to sing over the bar.
      const lean = 0.06 * Math.sin(((i % 8) / 8) * Math.PI);
      notes.push({ at, length: beats, pitch: pitch(name), loud: 0.44 + lean + (beats >= 3 ? 0.03 : 0) + middle(Math.floor(at / 3)), part: 1 });
    }
    at += beats;
  });
  for (const [names, bar] of [[['C5', 'A4', 'E4', 'C4'], 37], [['D5', 'A4', 'F4', 'D4'], 38]] as const)
    names.forEach((name, k) =>
      notes.push({ at: bar * 3 + k * 0.03, length: bar === 38 ? 6 : 3, pitch: pitch(name), loud: 0.36 - k * 0.02, part: 1 }),
    );
  // The pedal down through every bar, changed at each new low key.
  const pedal: [number, number][] = [];
  for (let bar = 0; bar < 38; bar++) pedal.push([bar * 3 + 0.05, bar * 3 + 2.97]);
  pedal.push([38 * 3 + 0.05, 38 * 3 + 7]);
  return {
    title: 'Gymnopedie No. 1',
    by: 'Erik Satie, 1888',
    tempo: 76,
    beats: 39 * 3 + 5,
    notes: ordered(notes),
    pedal,
  };
}

/** The pieces by name. */
export const pieces = { prelude: prelude(), gymnopedie: gymnopedie() } as const;

export type PieceName = keyof typeof pieces;
