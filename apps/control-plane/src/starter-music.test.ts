import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { pieces, pitch } from '../../../starters/iwsdk-vr/template/src/pieces.ts';
import {
  centre,
  eachHeard,
  isBlack,
  join,
  occurrences,
  range,
  ringsUntil,
  type Score,
  scoreSeconds,
  spanning,
} from '../../../starters/iwsdk-vr/template/src/score.ts';
import {
  hertz,
  pianoPartials,
  pianoRing,
  stiffness,
  strikeKey,
} from '../../../starters/iwsdk-vr/template/src/tone.ts';

// The starter's music, as numbers: when each note of a score sounds, the pieces' own notes, and the piano's tone. The
// instruments themselves run only in a browser (Web Audio); a lane's offline renders measure those.

const tiny: Score = {
  title: 'tiny',
  by: 'test',
  tempo: 120,
  beats: 4,
  notes: [
    { at: 0, length: 1, pitch: 60, loud: 0.5, part: 0 },
    { at: 1, length: 0.5, pitch: 64, loud: 0.5, part: 1 },
    { at: 3, length: 1, pitch: 67, loud: 0.5, part: 1 },
  ],
  pedal: [[0.9, 2]],
};

/** The strongest frequency within 30 cents of `near` over [from, from + seconds) of `samples`, to a hundredth of a cent. */
function strongest(
  samples: Float32Array,
  rate: number,
  near: number,
  from: number,
  seconds: number,
): number {
  const start = Math.floor(from * rate);
  const length = Math.floor(seconds * rate);
  const at = (hz: number) => {
    let re = 0;
    let im = 0;
    const w = (2 * Math.PI * hz) / rate;
    for (let i = 0; i < length; i++) {
      const x = (samples[start + i] as number) * (0.5 - 0.5 * Math.cos((2 * Math.PI * i) / length));
      re += x * Math.cos(w * i);
      im += x * Math.sin(w * i);
    }
    return Math.hypot(re, im);
  };
  let best = near;
  let most = 0;
  for (const step of [1, 0.1, 0.01]) {
    const centre = best;
    for (let cents = -30 * step; cents <= 30 * step; cents += step) {
      const hz = centre * 2 ** (cents / 1200);
      const m = at(hz);
      if (m > most) {
        most = m;
        best = hz;
      }
    }
  }
  return best;
}

const cents = (a: number, b: number) => 1200 * Math.log2(a / b);

describe('the starter score: when each note sounds', () => {
  test('a note starts at its beat in seconds, and only notes starting in the stretch asked for are given', () => {
    const heard = occurrences(tiny, 0, 1);
    assert.deepEqual(
      heard.map((h) => [h.note.pitch, h.start, h.end]),
      [
        [60, 0, 0.5],
        [64, 0.5, 0.75],
      ],
    );
    assert.deepEqual(
      occurrences(tiny, 0.5, 1.5).map((h) => h.note.pitch),
      [64],
    );
    assert.deepEqual(
      occurrences(tiny, 0.5, 1.6).map((h) => h.note.pitch),
      [64, 67],
    );
    assert.deepEqual(occurrences(tiny, -5, 0), []);
  });

  test('a looping score comes round again after its length and the gap, and not without loop', () => {
    assert.equal(scoreSeconds(tiny), 2);
    const looped = occurrences(tiny, 2, 6, true, 1);
    assert.deepEqual(
      looped.map((h) => [h.round, h.note.pitch, h.start]),
      [
        [1, 60, 3],
        [1, 64, 3.5],
        [1, 67, 4.5],
      ],
    );
    assert.deepEqual(occurrences(tiny, 2, 6, false, 1), []);
  });

  test('a note let go while the pedal is down rings until it lifts; one let go after it does not', () => {
    const [first, second, third] = tiny.notes;
    assert.equal(ringsUntil(tiny, first as never), 2);
    assert.equal(ringsUntil(tiny, second as never), 2);
    assert.equal(ringsUntil(tiny, third as never), 4);
    assert.equal(occurrences(tiny, 0, 1)[0]?.rings, 1);
  });

  test('eachHeard visits exactly what occurrences lists, and spanning adds the notes still held', () => {
    const visited: number[][] = [];
    eachHeard(tiny, 0, 6, true, 1, (note, _index, round, start) =>
      visited.push([round, note.pitch, start]),
    );
    assert.deepEqual(
      visited,
      occurrences(tiny, 0, 6, true, 1).map((h) => [h.round, h.note.pitch, h.start]),
    );
    assert.deepEqual(
      spanning(tiny, 0.6, 0.7).map((h) => h.note.pitch),
      [64],
    );
  });

  test('join plays scores one after another in seconds, gaps included', () => {
    const both = join([tiny, tiny], 1);
    assert.equal(both.tempo, 60);
    assert.equal(scoreSeconds(both), 6);
    assert.deepEqual(
      both.notes.map((n) => n.at),
      [0, 0.5, 1.5, 3, 3.5, 4.5],
    );
    assert.deepEqual(both.pedal, [
      [0.45, 1],
      [3.45, 4],
    ]);
  });

  test('the middle of what scores play is the mean of their pitches, middle C for none', () => {
    assert.equal(centre([tiny]), (60 + 64 + 67) / 3);
    assert.equal(
      centre([tiny, { ...tiny, notes: [{ at: 0, length: 1, pitch: 72, loud: 0.5, part: 0 }] }]),
      (60 + 64 + 67 + 72) / 4,
    );
    assert.equal(centre([]), 60);
  });

  test('note names and black keys are read as a keyboard has them', () => {
    assert.equal(pitch('C4'), 60);
    assert.equal(pitch('A4'), 69);
    assert.equal(pitch('F#5'), 78);
    assert.equal(pitch('Bb2'), 46);
    assert.throws(() => pitch('H2'));
    assert.deepEqual([60, 61, 63, 65, 66, 70, 71].map(isBlack), [
      false,
      true,
      true,
      false,
      true,
      true,
      false,
    ]);
  });
});

describe('the starter pieces: public-domain notes, about a minute and a half each', () => {
  for (const [name, score] of Object.entries(pieces))
    test(`${name} lasts 60 to 100 seconds, its notes in start order, on a piano's keys, two hands`, () => {
      const seconds = scoreSeconds(score);
      assert.ok(seconds >= 60 && seconds <= 100, `${name} lasts ${seconds} s`);
      assert.ok(score.notes.every((note, i, all) => i === 0 || (all[i - 1]?.at ?? 0) <= note.at));
      const [low, high] = range([score]);
      assert.ok(low >= 21 && high <= 108);
      assert.ok(score.notes.every((note) => note.loud > 0 && note.loud <= 1 && note.length > 0));
      assert.deepEqual([...new Set(score.notes.map((note) => note.part))].sort(), [0, 1]);
      assert.ok(score.notes.every((note) => note.at + note.length <= score.beats));
    });

  test('the Prelude in C opens on its first chord, C E G C E, and closes on C with E G C above', () => {
    const { prelude } = pieces;
    const first = prelude.notes.filter((note) => note.at < 1.1).map((note) => note.pitch);
    assert.deepEqual(first, ['C4', 'E4', 'G4', 'C5', 'E5'].map(pitch));
    // The last chord is the one held six beats.
    const close = prelude.notes
      .filter((note) => note.length === 6)
      .map((note) => note.pitch)
      .sort((a, b) => a - b);
    assert.deepEqual(close, ['C2', 'C3', 'E4', 'G4', 'C5'].map(pitch));
    // Thirty-two bars of the pattern, eight sixteenths above twice a bar, then the two written-out bars.
    assert.equal(prelude.notes.filter((note) => note.part === 1).length, 32 * 12 + 2 * 14 + 3);
  });

  test('the Gymnopedie keeps G and D alternating in the bass for its first sixteen bars, under its tune', () => {
    const { gymnopedie } = pieces;
    const bass = gymnopedie.notes.filter(
      (note) => note.part === 0 && note.at % 3 === 0 && note.at < 48,
    );
    assert.deepEqual(
      bass.map((note) => note.pitch),
      Array.from({ length: 16 }, (_, bar) => pitch(bar % 2 === 0 ? 'G2' : 'D2')),
    );
    const tune = gymnopedie.notes.filter((note) => note.part === 1).slice(0, 9);
    assert.deepEqual(
      tune.map((note) => note.pitch),
      ['F#5', 'A5', 'G5', 'F#5', 'C#5', 'B4', 'C#5', 'D5', 'A4'].map(pitch),
    );
    assert.equal(tune[0]?.at, 13);
  });
});

describe('the starter piano tone', () => {
  test('its partials are a little sharp of whole multiples, more so up the keyboard, the fundamental exactly in tune', () => {
    for (const midi of [40, 60, 84]) {
      const partials = pianoPartials(midi);
      assert.equal(partials[0]?.hz, hertz(midi));
      const sharp = partials.slice(1, 5).map((p, i) => cents(p.hz, (i + 2) * hertz(midi)));
      assert.ok(
        sharp.every((c, i) => c > 0 && (i === 0 || c > (sharp[i - 1] as number))),
        `${midi}: ${sharp}`,
      );
    }
    assert.ok(stiffness(84) > stiffness(60) && stiffness(60) > stiffness(40));
  });

  test('a struck A4 sounds at 440 Hz to a tenth of a cent, and peaks at exactly full scale', () => {
    const rate = 48000;
    const samples = strikeKey('piano', 69, rate);
    const hz = strongest(samples, rate, 440, 0.6, 1);
    assert.ok(Math.abs(cents(hz, 440)) < 0.1, `A4 measured ${hz} Hz`);
    let peak = 0;
    for (const s of samples) peak = Math.max(peak, Math.abs(s));
    assert.equal(peak, 1);
    assert.ok(samples.every(Number.isFinite));
  });

  test('a bass key rings longer than a middle one, and a middle one longer than a treble one', () => {
    assert.ok(pianoRing(36) > pianoRing(60) && pianoRing(60) > pianoRing(84));
    const rate = 16000;
    const fallsBy20dB = (midi: number) => {
      const samples = strikeKey('piano', midi, rate);
      const window = rate / 20;
      const levels: number[] = [];
      for (let i = 0; i + window <= samples.length; i += window) {
        let sum = 0;
        for (let k = 0; k < window; k++) sum += (samples[i + k] as number) ** 2;
        levels.push(Math.sqrt(sum / window));
      }
      const top = Math.max(...levels);
      const at = levels.findIndex((level, k) => k > levels.indexOf(top) && level < top / 10);
      return at < 0 ? Number.POSITIVE_INFINITY : at / 20;
    };
    const [bass, middle, treble] = [36, 60, 84].map(fallsBy20dB) as [number, number, number];
    assert.ok(bass > middle && middle > treble, `-20 dB after ${bass}, ${middle}, ${treble} s`);
  });

  test('the same key is made the same every time', () => {
    assert.deepEqual(strikeKey('bell', 72, 8000), strikeKey('bell', 72, 8000));
  });
});

describe('the starter music and light kit keeps to its page', () => {
  // A tripwire, not a proof: it reads names in the kit's own files, not what the code does. A later change that
  // reaches the network, keeps something or evaluates code by one of these names fails here, and is read by a person.
  const kit = [
    'score.ts',
    'pieces.ts',
    'tone.ts',
    'tone-worker.ts',
    'instruments.ts',
    'glow.ts',
    'places.ts',
  ];
  const denied =
    /\b(fetch|XMLHttpRequest|WebSocket|EventSource|importScripts|eval|localStorage|sessionStorage|indexedDB|caches|serviceWorker|sendBeacon|createObjectURL|innerHTML)\b|\bnew Function\b|\bimport\(|document\.cookie|\bBlob\b|https?:\/\//;
  const found = (text: string) => text.split('\n').filter((line) => denied.test(line));

  test('the scan finds each name it is for in a line that holds one', () => {
    for (const line of [
      "await fetch('/x')",
      'localStorage.setItem(k, v)',
      "import('./later.js')",
      'new Blob([code])',
      "const u = 'https://cdn.example/x.js';",
    ])
      assert.deepEqual(found(line), [line]);
    assert.deepEqual(
      found("const sound = new Worker(new URL('./tone-worker.ts', import.meta.url));"),
      [],
    );
  });

  for (const file of kit)
    test(`${file} names no network, storage or evaluated code`, () => {
      const text = readFileSync(
        new URL(`../../../starters/iwsdk-vr/template/src/${file}`, import.meta.url),
        'utf8',
      );
      assert.ok(text.length > 200, `${file} is read`);
      assert.deepEqual(found(text), []);
    });
});
