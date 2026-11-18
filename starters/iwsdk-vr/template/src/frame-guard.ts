/**
 * A guard for the headset's frame rate, for scenery a headset may not afford: in an XR session only, it times the
 * frames of the first two seconds, and if their median takes longer than a 72 Hz frame (13.9 ms) it drops the first
 * of the `knobs` (the costliest scenery that can go, in the order given) and times two seconds more, until the median
 * fits or nothing is left to drop. Once a window fits it stops. Outside a session (a laptop, the person's computer's
 * checks) it does nothing, so what they see never depends on how fast the machine is.
 */
export interface FrameKnob {
  /** What it drops, for the one line the guard logs. */
  readonly name: string;
  drop(): void;
}

/** A 72 Hz frame, in milliseconds. */
export const FRAME_BUDGET_MS = 1000 / 72;

/** How long each window the guard times is, in seconds of game time. */
export const GUARD_WINDOW_S = 2;

export class FrameGuard {
  readonly #knobs: readonly FrameKnob[];
  #next = 0;
  #done = false;
  #start: number | null = null;
  #last: number | null = null;
  #frames: number[] = [];

  constructor(knobs: readonly FrameKnob[]) {
    this.#knobs = knobs;
  }

  /**
   * One frame at game time `time` (seconds), in an XR session or not. Returns the name of the knob it dropped on this
   * frame, or null.
   */
  step(time: number, inSession: boolean): string | null {
    if (this.#done) return null;
    if (!inSession) {
      // Only a session's own frames count: leaving one starts the window again.
      this.#start = null;
      this.#last = null;
      this.#frames = [];
      return null;
    }
    if (this.#start === null || this.#last === null) {
      this.#start = time;
      this.#last = time;
      return null;
    }
    this.#frames.push((time - this.#last) * 1000);
    this.#last = time;
    if (time - this.#start < GUARD_WINDOW_S) return null;
    const sorted = [...this.#frames].sort((a, b) => a - b);
    const median = sorted[Math.floor(sorted.length / 2)] ?? 0;
    this.#frames = [];
    this.#start = time;
    if (median <= FRAME_BUDGET_MS) {
      this.#done = true;
      return null;
    }
    const knob = this.#knobs[this.#next];
    if (knob === undefined) {
      this.#done = true;
      return null;
    }
    this.#next += 1;
    if (this.#next >= this.#knobs.length) this.#done = true;
    knob.drop();
    return knob.name;
  }
}
