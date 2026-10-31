import {
  compileValidator,
  type Principal,
  TranscriptionResponse,
  type TranscriptionResponse as TranscriptionResponseType,
} from '@halcyonic/contracts';
import type { Clock } from '@halcyonic/runtime-core';
import { WindowCounter } from '../network/limits.ts';
import { spokenNames, tidySpeech } from './tidy.ts';
import { readClip } from './wav.ts';
import { LANGUAGE, type SpeechEngine } from './whisper.ts';

const validateResponse = compileValidator(TranscriptionResponse);

/** Clips one principal may have transcribed in a minute. */
export const CLIPS_PER_MINUTE = 6;

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
 * for each principal, six a minute that reach the engine, and one transcription at a time on the
 * Mac. What was heard is tidied before it becomes the draft, and shown as heard if tidying fails.
 * Nothing is journaled or kept, and the caller logs no text.
 */
export class Transcriptions {
  readonly #engine: SpeechEngine | null;
  readonly #names: () => readonly string[];
  readonly #perMinute: WindowCounter;
  readonly #inFlight = new Set<string>();
  #busy = false;

  /** `names`: the names a draft is spelled with, read per clip; else Halcyonic's own words. */
  constructor(options: {
    readonly engine: SpeechEngine | null;
    readonly clock: Clock;
    readonly names?: () => readonly string[];
  }) {
    this.#engine = options.engine;
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
    if (this.#busy) {
      return refused(
        503,
        'transcription_busy_on_mac',
        'The Mac is transcribing another clip; try again in a moment.',
      );
    }
    if (!this.#perMinute.take(key)) {
      return refused(
        429,
        'rate_limited',
        `At most ${CLIPS_PER_MINUTE} clips a minute; wait a moment.`,
      );
    }
    this.#busy = true;
    this.#inFlight.add(key);
    try {
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
    } finally {
      this.#busy = false;
      this.#inFlight.delete(key);
    }
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
      this.#busy = false;
    }
  }
}

function refused(status: number, code: string, message: string): TranscriptionAnswer {
  return { kind: 'refused', status, code, message };
}
