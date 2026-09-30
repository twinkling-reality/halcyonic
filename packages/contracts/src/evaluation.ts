import Type, { type Static } from 'typebox';
import { ErrorInfo, Nullable, Timestamp } from './primitives.ts';

/**
 * What an evaluation source measured about one execution's session: its estimated cost, its
 * outcome and its verification runs.
 *
 * This is Halcyonic's shape, not the source's wire format. It is strict, like every Halcyonic
 * contract: the mapping from the source's documents is written field by field, so nothing the
 * source adds later reaches Halcyonic clients unannounced. It is read through from the source on
 * request and never journaled (ADR 0010).
 *
 * The three parts are read separately, and each keeps the source's own statement about itself:
 * `availability` (whether the source could support the claim at all), `coverage` (which dates and
 * sessions the claim covers) and `freshness` (how current it is). After `freshness.stale_at` a
 * client must describe that part as stale, whatever `freshness.state` said when it was read.
 *
 * `null` means the source does not have the value: show it as unknown or pending, never as zero,
 * blank or success. The measurements stay apart: nothing here is a score, and a client must not
 * combine them into one. Texts are the source's, untrusted: escape them when rendering.
 *
 * The source may add values to five of its vocabularies within its version (availability reasons,
 * coverage omissions, end reasons, line survival fates and metric units) but never removes or
 * redefines one. A value Halcyonic does not know yet arrives as `unknown`, never as a guess at one
 * it knows; an unknown omission makes the coverage incomplete, and a metric in an unknown unit is
 * null.
 */

const strict = { additionalProperties: false } as const;
const Count = Type.Integer({ minimum: 0 });
/** A fraction from 0 to 1. A client multiplies it by 100 to show a percentage. */
const Fraction = Type.Number({ minimum: 0, maximum: 1 });
/** A calendar date, `YYYY-MM-DD`, interpreted as UTC. */
const CalendarDate = Type.String({ pattern: '^\\d{4}-\\d{2}-\\d{2}$' });
const Text = (maxLength: number) => Type.String({ maxLength });

/** Calendar dates from `from` through `through`, inclusive. */
export const EvaluationDateRange = Type.Object(
  { from: CalendarDate, through: CalendarDate },
  strict,
);

/**
 * The note every cost estimate carries, Seorak's `COST_ESTIMATE_NOTE` word for word
 * (`@seorak/types` 0.2.0). No agent reports a dollar figure: the source derives the cost from token
 * counts at list prices, so it is an estimate for every session and never a bill.
 */
export const ESTIMATED_COST_NOTE = 'Estimated from token counts at list prices. Not a bill.';

/** Whether the source could support a part at all, in the source's vocabulary. */
export const EvaluationAvailability = Type.Object(
  {
    state: Type.Union([
      Type.Literal('available'),
      Type.Literal('partial'),
      Type.Literal('unavailable'),
    ]),
    /**
     * Null when fully available; otherwise the source's stable reason. Never read it as zero.
     * `unknown` is a reason the source added after this contract: unavailable for a reason
     * Halcyonic cannot name.
     */
    reason: Nullable(
      Type.Union([
        Type.Literal('not_captured'),
        Type.Literal('not_retained'),
        Type.Literal('not_yet_computed'),
        Type.Literal('outside_credential_restriction'),
        Type.Literal('temporarily_unavailable'),
        Type.Literal('result_limit'),
        Type.Literal('unknown'),
      ]),
    ),
  },
  strict,
);

/** Which dates and sessions a part actually covers. */
export const EvaluationCoverage = Type.Object(
  {
    requested: EvaluationDateRange,
    /** Null when nothing was observed. */
    observed: Nullable(EvaluationDateRange),
    matched_sessions: Count,
    included_sessions: Count,
    complete: Type.Boolean(),
    /**
     * Why the coverage is not complete. `unknown` is an omission the source added after this
     * contract; coverage that has one is never complete.
     */
    omissions: Type.Array(
      Type.Union([
        Type.Literal('outside_retention'),
        Type.Literal('capture_unavailable'),
        Type.Literal('projection_pending'),
        Type.Literal('credential_restriction'),
        Type.Literal('result_limit'),
        Type.Literal('unknown'),
      ]),
    ),
  },
  strict,
);
export const EvaluationCoverageOmission = EvaluationCoverage.properties.omissions.items;

/** How current a part is, by the source's clock. */
export const EvaluationFreshness = Type.Object(
  {
    state: Type.Union([Type.Literal('fresh'), Type.Literal('stale'), Type.Literal('revalidating')]),
    generated_at: Timestamp,
    /** The newest observation included, or null for an honest empty answer. */
    data_through: Nullable(Timestamp),
    /** After this instant the part must be described as stale. */
    stale_at: Timestamp,
  },
  strict,
);

const Read = {
  availability: EvaluationAvailability,
  coverage: EvaluationCoverage,
  freshness: EvaluationFreshness,
};

/**
 * Names the Seorak source of a read and whether a stand-in produced it. Both Seorak reads carry it:
 * an execution's evaluation and the account-wide provider usage limits (`UsageLimitsResponse`).
 */
export const EvaluationSource = Type.Object(
  {
    system: Type.Literal('seorak'),
    /**
     * True when a stand-in Halcyonic wrote produced these measurements from invented content, as
     * the recorded demonstration's sources do, speaking Seorak's integration API (ADR 0019); false
     * for everything read from Seorak itself. Clients must say "simulated" wherever they name a
     * synthetic source.
     */
    synthetic: Type.Boolean(),
    /** The version of the source's integration API these measurements were read through. */
    api_version: Type.Literal('v1'),
  },
  strict,
);

/** The session's estimated cost. */
export const EvaluationCost = Type.Object(
  {
    ...Read,
    /** US dollars, or null when the session is unpriced or its cost unknown. Never a bill. */
    estimated_usd: Nullable(Type.Number({ minimum: 0 })),
    /** Show this beside every estimate. */
    note: Type.Literal(ESTIMATED_COST_NOTE),
  },
  strict,
);

/** The uncommitted changes at a session's end. Generated and lock file lines are counted apart. */
export const EvaluationUncommitted = Type.Object(
  {
    files_touched: Count,
    lines_added: Count,
    lines_removed: Count,
    generated_lines_excluded: Count,
  },
  strict,
);

/**
 * Whether the lines a session wrote were still on the branch at a fixed maturation rung (three
 * days): until then the source has none, or reports the fate `unknown`. `rate` is null below the
 * source's own floor. `unknown` is also a fate the source added after this contract.
 */
export const EvaluationLineSurvival = Type.Object(
  {
    rung: Type.Literal('3d'),
    fate: Type.Union([
      Type.Literal('retained'),
      Type.Literal('overwritten'),
      Type.Literal('unreachable'),
      Type.Literal('unknown'),
    ]),
    rate: Nullable(Fraction),
    lines_authored: Count,
    lines_surviving: Count,
    commits_checked: Count,
  },
  strict,
);

/**
 * How a session ended, as its runtime reported it: a lifecycle event, never completion. `unknown`
 * is a reason the source added after this contract.
 */
export const EvaluationEndReason = Type.Union([
  Type.Literal('clear'),
  Type.Literal('resume'),
  Type.Literal('logout'),
  Type.Literal('prompt_input_exit'),
  Type.Literal('bypass_permissions_disabled'),
  Type.Literal('other'),
  Type.Literal('unknown'),
]);

/**
 * Whether the session's work landed and lasted, in counts and closed values only. Every field is
 * null until the source has the rows to determine it; a session that has not matured is pending,
 * never zero.
 */
export const EvaluationOutcomeMeasure = Type.Object(
  {
    /** Commits that landed during the session. */
    commits_landed: Nullable(Count),
    uncommitted: Nullable(EvaluationUncommitted),
    line_survival: Nullable(EvaluationLineSurvival),
    /** Tool calls that errored. Null when no call said whether it errored; 0 is a measured zero. */
    error_count: Nullable(Count),
    first_error_at: Nullable(Timestamp),
    /** Null while the session is active or when its end was not captured. */
    end_reason: Nullable(EvaluationEndReason),
  },
  strict,
);

export const EvaluationOutcome = Type.Object(
  {
    ...Read,
    /** Null when the outcome is unavailable or has not matured. */
    measure: Nullable(EvaluationOutcomeMeasure),
  },
  strict,
);

/**
 * The verification runs of one kind of check in the session. Each metric is null when the source
 * reports none, or in a unit Halcyonic does not know yet.
 */
export const EvaluationVerificationKind = Type.Object(
  {
    /** The source's name for the kind: `test`, `build`, `typecheck`, `lint`, or `Verification` for a run of no kind. */
    label: Text(120),
    /** Runs of this kind that returned a result. */
    runs: Nullable(Count),
    /** Runs of this kind that passed. Never more than `runs`. */
    passed: Nullable(Count),
    /** The share of runs that passed, or null when nothing ran. */
    pass_rate: Nullable(Fraction),
  },
  strict,
);

/** The session's verification runs, one entry per kind of check that was measured. */
export const EvaluationVerificationLens = Type.Object(
  {
    by_kind: Type.Array(EvaluationVerificationKind),
    /** The source's explanation when it measured nothing, as it does when `by_kind` is empty. */
    empty_reason: Nullable(Text(600)),
  },
  strict,
);

export const EvaluationVerification = Type.Object(
  {
    ...Read,
    /** Null when the source could not produce the verification lens. Measured for Claude Code only. */
    lens: Nullable(EvaluationVerificationLens),
  },
  strict,
);

export const Evaluation = Type.Object(
  {
    source: EvaluationSource,
    cost: EvaluationCost,
    outcome: EvaluationOutcome,
    verification: EvaluationVerification,
  },
  strict,
);
export type Evaluation = Static<typeof Evaluation>;

/**
 * The answer to "what does the evaluation source say about this execution". Only `available`
 * carries measurements; every other state names why there are none:
 * - `not_found`: the source holds no record of this session, possibly not yet. A session launched
 *   moments ago may not be captured yet, so retry with backoff.
 * - `unavailable`: the source is not running, cannot be reached, failed, asked for no request until
 *   later, or does not observe this runtime.
 * - `incompatible`: the source answered in a shape or with a status this client does not read.
 * - `unauthorized`: no credential is configured, or the source refused it.
 */
export const EvaluationResult = Type.Union([
  Type.Object({ availability: Type.Literal('available'), evaluation: Evaluation }, strict),
  Type.Object({ availability: Type.Literal('not_found'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('incompatible'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('unauthorized'), reason: ErrorInfo }, strict),
]);
export type EvaluationResult = Static<typeof EvaluationResult>;
export type EvaluationFailure = Exclude<EvaluationResult, { availability: 'available' }>;
