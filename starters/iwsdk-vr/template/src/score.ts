/**
 * Music as data, and when each note sounds. A score is a list of notes in beats; `occurrences` says, for a stretch of
 * play time in seconds, which notes start in it and exactly when, looping if asked. The light of an app and its sound
 * both read these times, so a bar of light lands on its key on the frame its note is heard. Kept free of imports so it
 * can be tested on its own; `playScore` in `src/instruments.ts` plays one.
 */

/** One note: when it starts and how long it rings, in beats from the score's start; its key; how hard it is struck. */
export interface Note {
  readonly at: number;
  readonly length: number;
  /** The key as a MIDI number: 60 is middle C, 69 the A above it at 440 Hz. */
  readonly pitch: number;
  /** How hard, from 0 (barely) to 1 (as hard as it goes). */
  readonly loud: number;
  /** Which part plays it, from 0: a hand, a voice, a line. An app colours or places each part as it likes. */
  readonly part: number;
}

export interface Score {
  readonly title: string;
  /** Who wrote it and when, for the app to show if it shows anything. */
  readonly by: string;
  /** Beats a minute. */
  readonly tempo: number;
  /** The score's length in beats, rests at its end included. */
  readonly beats: number;
  /** The notes in the order they start. */
  readonly notes: readonly Note[];
  /**
   * Where the sustain pedal is down, as [from, to] in beats. A note let go while the pedal is down rings on until it
   * lifts; the light still shows the key let go. None: every note stops when it is let go.
   */
  readonly pedal?: readonly (readonly [number, number])[];
}

/** When a note's sound stops, in beats: when it is let go, or later when the pedal is down then. */
export function ringsUntil(score: Score, note: Note): number {
  const off = note.at + note.length;
  for (const [down, up] of score.pedal ?? []) if (off >= down && off < up) return Math.max(off, up);
  return off;
}

/** A note as played: which of the score's notes, and when it starts and stops in play seconds. */
export interface Heard {
  readonly note: Note;
  readonly index: number;
  /** Which time round, from 0, when the score loops. */
  readonly round: number;
  readonly start: number;
  readonly end: number;
  /** When its sound stops, in play seconds: `end`, or later when the pedal holds it. */
  readonly rings: number;
}

/** Seconds a beat lasts. */
export function beatSeconds(score: Score): number {
  return 60 / score.tempo;
}

/** Seconds the score lasts once through, rests at its end included. */
export function scoreSeconds(score: Score): number {
  return score.beats * beatSeconds(score);
}

/** Seconds from one round's start to the next when looping with `gap` seconds of quiet between rounds. */
export function roundSeconds(score: Score, gap: number): number {
  return scoreSeconds(score) + Math.max(0, gap);
}

/**
 * The notes that START in play time [from, to), in order, each with its start and end in play seconds. Play time 0 is
 * the score's first beat; with `loop`, round k starts at k * roundSeconds(score, gap). Negative play time is before
 * the score begins and holds nothing.
 */
export function occurrences(score: Score, from: number, to: number, loop = false, gap = 0): Heard[] {
  const found: Heard[] = [];
  eachHeard(score, from, to, loop, gap, (note, index, round, start, end, rings) =>
    found.push({ note, index, round, start, end, rings }),
  );
  return found;
}

/**
 * The same as `occurrences`, without making a list: `visit` is called for each note that starts in [from, to), so an
 * app can read the score every frame and make nothing new.
 */
export function eachHeard(
  score: Score,
  from: number,
  to: number,
  loop: boolean,
  gap: number,
  visit: (note: Note, index: number, round: number, start: number, end: number, rings: number) => void,
): void {
  if (!(to > from) || score.notes.length === 0) return;
  const beat = beatSeconds(score);
  const round = roundSeconds(score, gap);
  const first = Math.max(0, Math.floor(Math.max(0, from) / round));
  const last = loop ? Math.floor(to / round) : 0;
  for (let k = first; k <= last; k++) {
    const offset = k * round;
    if (offset >= to) break;
    for (let index = 0; index < score.notes.length; index++) {
      const note = score.notes[index] as Note;
      const start = offset + note.at * beat;
      if (start < from) continue;
      if (start >= to) break;
      visit(note, index, k, start, start + note.length * beat, offset + ringsUntil(score, note) * beat);
    }
  }
}

/** The longest note's length in seconds: how far back to look for notes still held. */
export function longestSeconds(score: Score): number {
  let most = 0;
  for (const note of score.notes) most = Math.max(most, note.length);
  return most * beatSeconds(score);
}

/**
 * The notes SOUNDING or due in play time [from, to): every note whose span [start, end) meets it. For light that shows
 * what is coming (bars falling toward their keys) and what is still ringing (keys held down).
 */
export function spanning(score: Score, from: number, to: number, loop = false, gap = 0): Heard[] {
  return occurrences(score, from - longestSeconds(score), to, loop, gap).filter((heard) => heard.end > from);
}

/** The lowest and highest pitch a score uses, for an app to fit its keyboard to: [low, high]. */
export function range(scores: readonly Score[]): [number, number] {
  let low = Number.POSITIVE_INFINITY;
  let high = Number.NEGATIVE_INFINITY;
  for (const score of scores)
    for (const note of score.notes) {
      low = Math.min(low, note.pitch);
      high = Math.max(high, note.pitch);
    }
  return [low, high];
}

/** A MIDI number's frequency in hertz, equal tempered, A4 (69) at 440 Hz. */
export function frequency(midi: number): number {
  return 440 * 2 ** ((midi - 69) / 12);
}

/** Whether a MIDI key is a black key. */
export function isBlack(midi: number): boolean {
  return [1, 3, 6, 8, 10].includes(((midi % 12) + 12) % 12);
}

/**
 * Several scores played one after another as one, with `gap` seconds of quiet after each: a programme. The result
 * counts in seconds (a tempo of 60), so pieces at different tempos join exactly.
 */
export function join(scores: readonly Score[], gap = 3): Score {
  const notes: Note[] = [];
  const pedal: [number, number][] = [];
  let offset = 0;
  for (const score of scores) {
    const beat = beatSeconds(score);
    for (const note of score.notes) notes.push({ ...note, at: offset + note.at * beat, length: note.length * beat });
    for (const [down, up] of score.pedal ?? []) pedal.push([offset + down * beat, offset + up * beat]);
    offset += scoreSeconds(score) + Math.max(0, gap);
  }
  return {
    title: scores.map((score) => score.title).join(', '),
    by: scores.map((score) => score.by).join('; '),
    tempo: 60,
    beats: offset,
    notes,
    pedal,
  };
}

/**
 * Which of `columns` places round the person a note's light stands at (0 ahead, counting to the right), for light
 * that answers the music all round (`lightHall`): each octave runs once round the person, C ahead and each key up a
 * twelfth of a turn to the right, so the notes of any scale stand on every side, a chord spreads round the person and
 * a run circles them; one octave and the next take neighbouring places.
 */
export function roundColumn(midi: number, columns = 24): number {
  const key = ((midi % 12) + 12) % 12;
  const octave = ((Math.floor(midi / 12) % 2) + 2) % 2;
  return Math.floor(((key * 2 + octave) * columns) / 24) % columns;
}

/**
 * How light that builds with a piece stands `seconds` into it (play time), building over its first `rise` seconds:
 * `built`, 0 before the music to 1 at `rise`, slow at first; `hush`, a breath held just before the peak, rising from
 * 0 three seconds before `rise` to 1 just before it and gone at it; and `bloom`, the peak itself, nothing before
 * `rise` and all at once at it (one hit a cut can land on), a flash that falls within a second to a warm glow that
 * holds for several seconds more.
 */
export function swell(
  seconds: number,
  rise: number,
): { readonly built: number; readonly hush: number; readonly bloom: number } {
  const built = Math.min(1, Math.max(0, seconds / rise)) ** 1.6;
  const since = seconds - rise;
  const k = Math.min(1, Math.max(0, (since + 3) / 2.6));
  const hush = since < 0 ? k * k * (3 - 2 * k) : 0;
  const bloom = since < 0 ? 0 : 0.5 * Math.exp(-since / 0.45) + 0.5 * Math.exp(-since / 5);
  return { built, hush, bloom };
}

/**
 * How far down a played key is (0 up, 1 fully down) from how brightly it is lit (0 to 1): fully down while its note is
 * struck or held, coming back up only as its light dies away after the note is let go.
 */
export function keyDown(lit: number): number {
  return Math.min(1, Math.max(0, lit / 0.22));
}

/**
 * The ring of light a landing sends out across the floor, `seconds` after the note sounds: its radius in metres,
 * growing steadily from a hand's width, far enough that it is seen running out past the keyboard.
 */
export function ringRadius(seconds: number): number {
  return 0.1 + Math.max(0, seconds) * 2.6;
}

/**
 * How bright that ring is, `seconds` after the note sounds and `loud` (0 to 1) as it was played: brightest at the
 * landing, louder notes brighter, fading as it runs out, 0 before the note sounds and from `RING_SECONDS` on.
 */
export function ringStrength(seconds: number, loud: number): number {
  if (seconds < 0 || seconds >= RING_SECONDS) return 0;
  return (1 - seconds / RING_SECONDS) ** 1.4 * 0.4 * (0.6 + 0.4 * loud);
}

/** How long a landing's ring runs out across the floor, in seconds. */
export const RING_SECONDS = 1.8;
