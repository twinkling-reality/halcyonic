import {
  compileValidator,
  type Principal,
  TranscriptionResponse,
  type TranscriptionResponse as TranscriptionResponseType,
} from '@halcyonic/contracts';
import { type Clock, type Scheduler, systemScheduler } from '@halcyonic/runtime-core';
import { WindowCounter } from '../network/limits.ts';
import { spokenNames, tidySpeech } from './tidy.ts';
import { readClip } from './wav.ts';
import { LANGUAGE, type SpeechEngine } from './whisper.ts';

const validateResponse = compileValidator(TranscriptionResponse);

/** Clips one principal may have transcribed in a minute, enough to direct several tasks by voice. */
export const CLIPS_PER_MINUTE = 30;

/**
 * Clips that may wait on the Mac behind the one being transcribed. Two transcriptions at once were
 * not measured on this Mac (voice-transcription.md), so the Mac transcribes one clip at a time and
 * the clips behind it wait their turn, each one person's (one clip at a time per principal).
 */
export const CLIPS_WAITING = 3;

/**
 * How long a waiting clip waits for its turn before it is refused in words: a clip takes about
 * 0.3 to 0.8 s (voice-transcription.md), and the headset gives a transcription 15 s in all.
 */
export const TURN_WAIT_MS = 4_000;

export type TranscriptionAnswer =
  | {
      readonly kind: 'answered';
      readonly body: TranscriptionResponseType;
      readonly seconds: number;
    }
  | {
      readonly kind: 'refused';
      readonly status: number;
      readonly code: string;
      readonly message: string;
    };

/**
 * Turns a clip of speech into a draft on this Mac (ADR 0021), within bounds: one clip at a time
 * for each principal, thirty a minute that reach the engine, and one transcription at a time on
 * the Mac with up to three clips waiting their turn, each for at most four seconds. A clip beyond
 * those is refused in words that say how many are ahead. What was heard is tidied before it
 * becomes the draft, and shown as heard if tidying fails. Nothing is journaled or kept, and the
 * caller logs no text.
 */
export class Transcriptions {
  readonly #engine: SpeechEngine | null;
  readonly #names: () => readonly string[];
  readonly #perMinute: WindowCounter;
  readonly #scheduler: Scheduler;
  readonly #inFlight = new Set<string>();
  /** The clips waiting for the Mac, first come first served; each starts its turn when called. */
  readonly #waiting: Array<() => void> = [];
  #busy = false;

  /** `names`: the names a draft is spelled with, read per clip; else Halcyonic's own words. */
  constructor(options: {
    readonly engine: SpeechEngine | null;
    readonly clock: Clock;
    readonly scheduler?: Scheduler;
    readonly names?: () => readonly string[];
  }) {
    this.#engine = options.engine;
    this.#scheduler = options.scheduler ?? systemScheduler;
    this.#names = options.names ?? (() => spokenNames([]));
    this.#perMinute = new WindowCounter(options.clock, CLIPS_PER_MINUTE, 60_000);
  }

  async transcribe(principal: Principal | null, body: unknown): Promise<TranscriptionAnswer> {
    const engine = this.#engine;
    if (engine === null) {
      return refused(503, 'transcription_unavailable', 'Voice is not set up on this Mac.');
    }
    const key = principal?.kind === 'device' ? `device:${principal.device_id}` : 'local';
    if (this.#inFlight.has(key)) {
      return refused(429, 'transcription_busy', 'A clip of yours is already being transcribed.');
    }
    if (!Buffer.isBuffer(body)) {
      return refused(400, 'invalid_audio', 'The clip must be sent as audio/wav.');
    }
    const clip = readClip(body);
    if (!clip.ok) return refused(400, clip.code, clip.message);
    if (this.#perMinute.exhausted(key)) return rateLimited();
    if (this.#busy && this.#waiting.length >= CLIPS_WAITING) {
      return refused(
        503,
        'transcription_busy_on_mac',
        `The Mac is transcribing a clip and ${CLIPS_WAITING} more are waiting, so this one was not transcribed. Try again in a moment.`,
      );
    }
    this.#inFlight.add(key);
    try {
      if (this.#busy) {
        const ahead = this.#waiting.length + 1;
        if (!(await this.#turn())) {
          return refused(
            503,
            'transcription_busy_on_mac',
            `The Mac was still busy with ${ahead === 1 ? 'the clip' : `the ${ahead} clips`} ahead of yours, so this one was not transcribed. Try again in a moment.`,
          );
        }
      } else {
        this.#busy = true;
      }
      try {
        return await this.#transcribe(engine, key, clip);
      } finally {
        this.#release();
      }
    } finally {
      this.#inFlight.delete(key);
    }
  }

  /** Transcribes one clip while it holds the Mac. */
  async #transcribe(
    engine: SpeechEngine,
    key: string,
    clip: { readonly wav: Buffer; readonly seconds: number },
  ): Promise<TranscriptionAnswer> {
    if (!this.#perMinute.take(key)) return rateLimited();
    const result = await engine.transcribe(clip.wav);
    if (result.kind === 'failed') return refused(502, 'transcription_failed', result.message);
    const heard = (text: string): TranscriptionResponseType => ({
      outcome: 'heard',
      text,
      language: LANGUAGE,
      engine: engine.engine,
    });
    let answer: TranscriptionResponseType =
      result.text === ''
        ? { outcome: 'nothing_heard', engine: engine.engine }
        : heard(this.#tidy(result.text));
    // A name spelled longer than heard may outgrow a draft; the words as heard may not.
    if (!validateResponse(answer).ok && result.text !== '') answer = heard(result.text);
    if (!validateResponse(answer).ok) {
      return refused(502, 'transcription_failed', 'The transcript is too long for a draft.');
    }
    return { kind: 'answered', body: answer, seconds: clip.seconds };
  }

  /**
   * Waits behind the clip on the Mac. True when this clip's turn has come (it then holds the Mac);
   * false, and out of the queue, when it did not come within TURN_WAIT_MS.
   */
  #turn(): Promise<boolean> {
    return new Promise((resolve) => {
      const stop = new AbortController();
      const start = () => {
        stop.abort();
        resolve(true);
      };
      this.#waiting.push(start);
      this.#scheduler.sleep(TURN_WAIT_MS, stop.signal).then(
        () => {
          const index = this.#waiting.indexOf(start);
          if (index < 0) return;
          this.#waiting.splice(index, 1);
          resolve(false);
        },
        () => undefined,
      );
    });
  }

  /** Hands the Mac to the first waiting clip, or frees it. */
  #release(): void {
    const next = this.#waiting.shift();
    if (next === undefined) this.#busy = false;
    else next();
  }

  /** What was heard, tidied; as heard when tidying fails or leaves nothing. */
  #tidy(text: string): string {
    try {
      return tidySpeech(text, this.#names()) || text;
    } catch {
      return text;
    }
  }

  /**
   * Warms the engine while holding the Mac, so no clip overlaps it. Returns how long it took, or
   * null when there is no engine or a clip is being transcribed, which warms it anyway.
   */
  async warmUp(): Promise<number | null> {
    if (this.#engine === null || this.#busy) return null;
    this.#busy = true;
    try {
      return await this.#engine.warmUp();
    } finally {
      this.#release();
    }
  }
}

function rateLimited(): TranscriptionAnswer {
  return refused(429, 'rate_limited', `At most ${CLIPS_PER_MINUTE} clips a minute; wait a moment.`);
}

function refused(status: number, code: string, message: string): TranscriptionAnswer {
  return { kind: 'refused', status, code, message };
}
