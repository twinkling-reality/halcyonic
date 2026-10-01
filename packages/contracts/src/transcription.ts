import Type, { type Static } from 'typebox';
import { Text } from './primitives.ts';

const strict = { additionalProperties: false } as const;

/** Samples a second in a clip: 16-bit mono PCM at 16 kHz, nothing else. */
export const TRANSCRIPTION_SAMPLE_RATE = 16_000;

/** The shortest clip transcribed, in seconds. */
export const TRANSCRIPTION_MIN_SECONDS = 0.5;

/** The longest clip transcribed, in seconds. */
export const TRANSCRIPTION_MAX_SECONDS = 30;

/** The largest `audio/wav` body: a 44-byte header and 30 s of 16 kHz mono PCM16. */
export const TRANSCRIPTION_MAX_BYTES =
  44 + TRANSCRIPTION_MAX_SECONDS * TRANSCRIPTION_SAMPLE_RATE * 2;

/** The engine that transcribed a clip, as its binary reports itself: an open vocabulary. */
export const TranscriptionEngine = Type.Object({ name: Text(64), version: Text(64) }, strict);
export type TranscriptionEngine = Static<typeof TranscriptionEngine>;

/**
 * What the Mac heard in one clip (`POST /api/transcriptions`, ADR 0021); neither the clip nor the
 * text is journaled, stored or logged. `heard`: the engine's transcript and the language it
 * transcribed in, as a BCP 47 tag. The text is untrusted: a draft shown through the one rule for
 * text Halcyonic did not write, which is never sent anywhere without the person's confirmation.
 * `nothing_heard`: the engine found no speech in the clip, so there is no draft.
 */
export const TranscriptionResponse = Type.Union([
  Type.Object(
    {
      outcome: Type.Literal('heard'),
      text: Text(4096),
      language: Type.String({ pattern: '^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8}){0,4}$' }),
      engine: TranscriptionEngine,
    },
    strict,
  ),
  Type.Object({ outcome: Type.Literal('nothing_heard'), engine: TranscriptionEngine }, strict),
]);
export type TranscriptionResponse = Static<typeof TranscriptionResponse>;
