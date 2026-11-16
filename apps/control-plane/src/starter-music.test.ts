import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { openings, pieces, pitch } from '../../../starters/iwsdk-vr/template/src/pieces.ts';
import {
  eachHeard,
  isBlack,
  join,
  keyDown,
  occurrences,
  RING_SECONDS,
  range,
  ringRadius,
  ringStrength,
  ringsUntil,
  roundColumn,
  type Score,
  scoreSeconds,
  spanning,
  swell,
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
    /\b(fetch|XMLHttpRequest|WebSocket|EventSource|importScripts|eval|localStorage|sessionStorage|indexedDB|caches|serviceWorker|sendBeacon|createObjectURL|innerHTML|WebAssembly|RTCPeerConnection|location)\b|\bFunction\(|window\.open|\bimport\(|document\.cookie|\bBlob\b|https?:\/\//;
  const found = (text: string) => text.split('\n').filter((line) => denied.test(line));

  test('the scan finds each name it is for in a line that holds one', () => {
    for (const line of [
      "await fetch('/x')",
      'localStorage.setItem(k, v)',
      "import('./later.js')",
      'new Blob([code])',
      "const u = 'https://cdn.example/x.js';",
      'const run = Function(text);',
      'await WebAssembly.instantiate(bytes);',
      'new RTCPeerConnection();',
      "window.open('/elsewhere');",
      "location.href = '/elsewhere';",
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

describe('the hall of light answers the music all round and builds with it (SHOW, 2026-11-16)', () => {
  test('each octave runs once round the person: C ahead, rising to the right, the next octave beside it', () => {
    assert.equal(roundColumn(48), 0);
    assert.equal(roundColumn(60), 1);
    // E a third of the way round to the right, G past half way, B just short of ahead again.
    assert.equal(roundColumn(52), 8);
    assert.equal(roundColumn(55), 14);
    assert.equal(roundColumn(59), 22);
    // A C major chord's notes stand far apart round the person, never side by side.
    assert.deepEqual(
      [60, 64, 67].map((key) => roundColumn(key)),
      [1, 9, 15],
    );
  });

  test("a scale's notes stand on every side, and every place is some key in one octave or the next", () => {
    const major = [0, 2, 4, 5, 7, 9, 11].map((step) => roundColumn(60 + step));
    for (const [from, to] of [
      [0, 6],
      [6, 12],
      [12, 18],
      [18, 24],
    ])
      assert.ok(
        major.some((column) => column >= (from as number) && column < (to as number)),
        `a C major note in ${from} to ${to}`,
      );
    const taken = new Set<number>();
    for (let key = 48; key < 72; key++) taken.add(roundColumn(key));
    assert.equal(taken.size, 24);
    for (let key = 21; key <= 108; key++) {
      const column = roundColumn(key, 12);
      assert.ok(Number.isInteger(column) && column >= 0 && column < 12, String(key));
    }
  });

  test('it builds from nothing to full over the rise, slowly at first, and blooms once at the top of it', () => {
    assert.equal(swell(-3, 20).built, 0);
    assert.equal(swell(0, 20).built, 0);
    assert.equal(swell(20, 20).built, 1);
    assert.equal(swell(90, 20).built, 1);
    // Slow at first: halfway through the rise it has built a third, not a half.
    assert.ok(swell(10, 20).built < 0.35 && swell(10, 20).built > 0.3);
    for (let t = 0; t < 20; t += 0.5)
      assert.ok(swell(t + 0.5, 20).built > swell(t, 20).built, String(t));
    assert.equal(swell(20, 20).bloom, 1);
    // One hit: nothing at all before it, so a cut can land on it.
    assert.equal(swell(19, 20).bloom, 0);
    assert.equal(swell(19.97, 20).bloom, 0);
    // A flash that falls within a second to a warm glow, held for several seconds, gone by the time the piece has
    // gone on.
    assert.ok(swell(21, 20).bloom < 0.5);
    assert.ok(swell(22, 20).bloom > 0.3);
    assert.ok(swell(24, 20).bloom > 0.2);
    assert.ok(swell(45, 20).bloom < 0.01);
  });

  test('it holds its breath just before the peak: dim from three seconds before, deepest just before, gone at it', () => {
    assert.equal(swell(10, 20).hush, 0);
    assert.equal(swell(16.9, 20).hush, 0);
    assert.ok(swell(18.3, 20).hush > 0.3 && swell(18.3, 20).hush < 0.7);
    assert.equal(swell(19.7, 20).hush, 1);
    assert.equal(swell(20, 20).hush, 0);
    assert.equal(swell(25, 20).hush, 0);
  });
});

describe("Morning Mood's opening, for a moment on camera (SHOW, 2026-11-16)", () => {
  const { morning } = openings;
  test('its tune fills eight seconds (nine with its last note ringing), a dotted quarter at 60 in 6/8, two hands', () => {
    assert.equal(scoreSeconds(morning), 27 / 3);
    // The tune's last note starts at the 21st eighth, seven seconds in, and the opening is four bars of six eighths.
    assert.equal(Math.max(...morning.notes.map((note) => note.at)), 21);
    assert.ok(morning.notes.every((note, i, all) => i === 0 || (all[i - 1]?.at ?? 0) <= note.at));
    assert.deepEqual([...new Set(morning.notes.map((note) => note.part))].sort(), [0, 1]);
    assert.ok(
      morning.notes.every(
        (note) => note.at + note.length <= morning.beats && note.loud > 0 && note.loud <= 1,
      ),
    );
  });

  test('the tune opens as Grieg wrote it, B G sharp F sharp E F sharp G sharp, and comes home to E', () => {
    const tune = morning.notes.filter((note) => note.part === 1);
    assert.deepEqual(
      tune.slice(0, 6).map((note) => note.pitch),
      ['B4', 'G#4', 'F#4', 'E4', 'F#4', 'G#4'].map(pitch),
    );
    assert.equal(tune.at(-1)?.pitch, pitch('E4'));
    // The third bar reaches up to C sharp, twice.
    assert.equal(tune.filter((note) => note.pitch === pitch('C#5')).length, 2);
  });
});

describe('a struck key and its landing on the floor (SHOW, 2026-11-16)', () => {
  test('a key is fully down while its note is struck or held, and comes up only as its light dies away', () => {
    assert.equal(keyDown(0), 0);
    // Struck (about 1) and held (the low glow of a held note, about 0.25): fully down.
    assert.equal(keyDown(1), 1);
    assert.equal(keyDown(0.25), 1);
    // Let go, its light dying away: coming up, steadily, all the way.
    assert.ok(keyDown(0.11) > 0.4 && keyDown(0.11) < 0.6);
    for (let lit = 0; lit < 0.22; lit += 0.02)
      assert.ok(keyDown(lit + 0.02) > keyDown(lit), String(lit));
  });

  test('a landing sends one ring out across the floor that grows, fades and is gone, louder notes brighter', () => {
    assert.equal(ringStrength(-0.01, 0.5), 0);
    assert.equal(ringStrength(RING_SECONDS, 0.5), 0);
    assert.ok(ringRadius(0) > 0.05 && ringRadius(0) < 0.15);
    for (let t = 0.1; t < RING_SECONDS; t += 0.1) {
      assert.ok(ringRadius(t) > ringRadius(t - 0.1), String(t));
      assert.ok(ringStrength(t, 0.5) < ringStrength(t - 0.1, 0.5), String(t));
    }
    // Out past the keyboard, four metres and more, by the time it has faded nearly to nothing.
    assert.ok(ringRadius(RING_SECONDS - 0.05) > 4 && ringStrength(RING_SECONDS - 0.05, 0.5) < 0.01);
    assert.ok(ringStrength(0.3, 0.9) > ringStrength(0.3, 0.2));
  });
});
