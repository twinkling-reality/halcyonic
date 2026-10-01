import {
  compileValidator,
  type Principal,
  TranscriptionResponse,
  type TranscriptionResponse as TranscriptionResponseType,
} from '@halcyonic/contracts';
import type { Clock } from '@halcyonic/runtime-core';
import { WindowCounter } from '../network/limits.ts';
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
 * Mac. Nothing is journaled or kept, and the caller logs no text.
 */
export class Transcriptions {
  readonly #engine: SpeechEngine | null;
  readonly #perMinute: WindowCounter;
  readonly #inFlight = new Set<string>();
  #busy = false;

  constructor(options: { readonly engine: SpeechEngine | null; readonly clock: Clock }) {
    this.#engine = options.engine;
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
      const answer: TranscriptionResponseType =
        result.text === ''
          ? { outcome: 'nothing_heard', engine: engine.engine }
          : { outcome: 'heard', text: result.text, language: LANGUAGE, engine: engine.engine };
      if (!validateResponse(answer).ok) {
        return refused(502, 'transcription_failed', 'The transcript is too long for a draft.');
      }
      return { kind: 'answered', body: answer, seconds: clip.seconds };
    } finally {
      this.#busy = false;
      this.#inFlight.delete(key);
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
