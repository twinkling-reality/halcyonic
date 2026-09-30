import Type, { type Static } from 'typebox';
import { EvaluationSource } from './evaluation.ts';
import { ErrorInfo, Text } from './primitives.ts';

const strict = { additionalProperties: false } as const;

/**
 * Whose provider account a reading belongs to. The source cannot yet tell accounts apart, so a
 * reading is never tied to the selected runtime, account or model, and a client must say so.
 */
export const UsageLimitAccount = Type.Object({ state: Type.Literal('unidentified') }, strict);
export type UsageLimitAccount = Static<typeof UsageLimitAccount>;

/**
 * One provider-reported limit window, as last observed. `used_percent` is what the provider
 * reported at `observed_at`; `100 - used_percent` is at most what was left then, never a current
 * value. A reading is only served before its `resets_at`. It is account wide, never a Workstream,
 * session or project measure.
 */
export const UsageLimit = Type.Object(
  {
    /** The source's own agent id, an open vocabulary kept as an opaque reference. */
    agent: Type.String({ pattern: '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' }),
    /** How the control plane names the agent to a person. */
    label: Text(64),
    window: Type.Union([Type.Literal('rolling-5h'), Type.Literal('weekly')]),
    used_percent: Type.Number({ minimum: 0, maximum: 100 }),
    resets_at: Type.String({ format: 'date-time' }),
    observed_at: Type.String({ format: 'date-time' }),
    /** The source's flag: `fresh` while observed within five minutes of the read. */
    freshness: Type.Union([Type.Literal('fresh'), Type.Literal('stale')]),
    account: UsageLimitAccount,
  },
  strict,
);
export type UsageLimit = Static<typeof UsageLimit>;

/**
 * Provider usage limits, read through from the usage source (Seorak) when a person asks and never
 * journaled. Nothing but `available` carries a reading, so an absent limit never looks like 0%.
 */
export const UsageLimitsResponse = Type.Union([
  Type.Object(
    {
      availability: Type.Literal('available'),
      /** Where the readings come from; `synthetic` as for an evaluation (ADR 0019). */
      source: EvaluationSource,
      readings: Type.Array(UsageLimit, { minItems: 1 }),
    },
    strict,
  ),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('unauthorized'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('incompatible'), reason: ErrorInfo }, strict),
]);
export type UsageLimitsResponse = Static<typeof UsageLimitsResponse>;
