import Type, { type Static } from 'typebox';
import { ErrorInfo } from './primitives.ts';

const strict = { additionalProperties: false } as const;

/** A provider's captured quota ratio, as last observed; never a session or project measure. */
export const UsageLimit = Type.Object(
  {
    provider: Type.Enum(['codex', 'claude-code']),
    window: Type.Enum(['rolling-5h', 'weekly']),
    remaining_percent: Type.Number({ minimum: 0, maximum: 100 }),
    resets_at: Type.String({ format: 'date-time' }),
    observed_at: Type.String({ format: 'date-time' }),
  },
  strict,
);
export type UsageLimit = Static<typeof UsageLimit>;

/** Read through from Seorak on request. Empty or unauthorized answers never suggest a quota. */
export const UsageLimitsResponse = Type.Union([
  Type.Object(
    { availability: Type.Literal('available'), readings: Type.Array(UsageLimit) },
    strict,
  ),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('unauthorized'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('incompatible'), reason: ErrorInfo }, strict),
]);
export type UsageLimitsResponse = Static<typeof UsageLimitsResponse>;
