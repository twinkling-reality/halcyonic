import {
  compileValidator,
  Nullable,
  type Validated,
  type ValidationIssue,
} from '@halcyonic/contracts';
import Type, { type Static } from 'typebox';

/**
 * Seorak's integration API v1, as this client reads it.
 *
 * Written from Seorak's public sources only: the published `@seorak/types` 0.2.0
 * (`integration-api.d.ts`, the route builders in `api.d.ts`, and the values of its constants in
 * the compiled `index.js`), the public ADR 002 at commit aa2d57e, the commit the `seorak` 0.3.0
 * CLI, whose local plane serves this API, was published from, and ADR 007 at commit 8199865,
 * whose section 2 states what a v1 client may rely on. Halcyonic depends on the wire contract
 * only, never on Seorak's packages, so these schemas are Halcyonic's own.
 *
 * They are tolerant readers. Each requires only the properties this package uses and leaves every
 * object open, because v1 grows by adding response keys and a client must ignore keys it does not
 * know. What they do require is checked as the published types state it, including nullability
 * and the closed unions, so a value outside them means the plane is not speaking v1. The five
 * unions v1 may grow (availability reasons, coverage omissions, end reasons, line survival fates
 * and metric units) are read as any string instead, and the mapping reads a value it does not
 * know as unknown. The types bound no text; the bounds below are Halcyonic's, so that every
 * document this reader accepts maps onto a valid evaluation.
 */

export const API_BASE_PATH = '/api/v1';

/** The loopback port of Seorak's local plane, unless its owner configured another. */
export const DEFAULT_PORT = 4317;

/** `INTEGRATION_RESOLVABLE_AGENTS`: the agents whose native session ids Seorak resolves. */
export type SeorakAgent = 'claude-code' | 'codex';

/**
 * `NATIVE_SESSION_ID_PATTERN`: the native ids a resolve accepts. The types declare it as a RegExp;
 * its value is published in the package's compiled code.
 */
export const NATIVE_SESSION_ID_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$/;

/**
 * An owner-issued integration credential. Seorak prefixes them `srkx_` (ADR 002) and publishes
 * nothing more about their shape, so the rest is held only to RFC 6750's bearer token syntax.
 */
export const CREDENTIAL_PATTERN = /^srkx_[A-Za-z0-9._~+/-]+=*$/;

/** The opaque session reference, as the published MCP tool catalog patterns it. */
const SessionRef = Type.String({ pattern: '^ses_[0-9a-f]{32}$' });

const Count = Type.Integer({ minimum: 0 });
const Fraction = Type.Number({ minimum: 0, maximum: 1 });
/**
 * `IntegrationIsoTimestamp` is a plain string in the types, and ADR 007's example writes
 * `toISOString()`'s form. Any RFC 3339 instant is read, except a leap second, which JavaScript
 * cannot represent; the mapping normalizes it to Halcyonic's form.
 */
const Instant = Type.String({
  format: 'date-time',
  pattern: '^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:[0-5]\\d(\\.\\d+)?(Z|[+-]\\d{2}:\\d{2})$',
});
/** `IntegrationDate`: `YYYY-MM-DD`, interpreted as UTC. */
const CalendarDate = Type.String({ pattern: '^\\d{4}-\\d{2}-\\d{2}$' });
const DateRange = Type.Object({ from: CalendarDate, through: CalendarDate });

/**
 * A value of one of the five closed unions v1 may grow (ADR 007, section 2). Any string is read;
 * the mapping keeps the values it knows and reads any other as unknown.
 */
const Growing = Type.String();

/** `IntegrationReadMetadata`: on every private read, including an honest empty one. */
const Metadata = {
  apiVersion: Type.Literal('v1'),
  availability: Type.Object({
    state: Type.Enum(['available', 'partial', 'unavailable']),
    reason: Nullable(Growing),
  }),
  coverage: Type.Object({
    requested: DateRange,
    observed: Nullable(DateRange),
    matchedSessionCount: Count,
    includedSessionCount: Count,
    complete: Type.Boolean(),
    omissions: Type.Array(Growing),
  }),
  freshness: Type.Object({
    state: Type.Enum(['fresh', 'stale', 'revalidating']),
    generatedAt: Instant,
    dataThrough: Nullable(Instant),
    staleAt: Instant,
  }),
};
const WireMetadata = Type.Object(Metadata);
export type WireMetadata = Static<typeof WireMetadata>;

/** `PrivateSessionDto`, the answer to a resolve (ADR 007). `session` is null on a miss. */
export const WireSession = Type.Object({
  ...Metadata,
  session: Nullable(
    Type.Object({
      sessionRef: SessionRef,
      agent: Type.String(),
      costUsd: Nullable(Type.Number({ minimum: 0 })),
    }),
  ),
});
export type WireSession = Static<typeof WireSession>;

/** `PrivateOutcomeDto`. `outcome` is null when it is unavailable or has not matured. */
export const WireOutcome = Type.Object({
  ...Metadata,
  sessionRef: SessionRef,
  outcome: Nullable(
    Type.Object({
      commitsLanded: Nullable(Count),
      uncommitted: Nullable(
        Type.Object({
          filesTouched: Count,
          linesAdded: Count,
          linesRemoved: Count,
          generatedLinesExcluded: Count,
        }),
      ),
      lineSurvival: Nullable(
        Type.Object({
          rung: Type.Literal('3d'),
          fate: Growing,
          rate: Nullable(Fraction),
          linesAuthored: Count,
          linesSurviving: Count,
          commitsChecked: Count,
        }),
      ),
      errorCount: Nullable(Count),
      firstErrorAt: Nullable(Instant),
      endReason: Nullable(Growing),
    }),
  ),
});
export type WireOutcome = Static<typeof WireOutcome>;

const LensRow = Type.Object({
  label: Type.String({ maxLength: 120 }),
  metrics: Type.Array(
    Type.Object({
      key: Type.String(),
      value: Type.Union([Type.Number(), Type.Boolean(), Type.Null()]),
      unit: Growing,
    }),
  ),
});
type LensRow = Static<typeof LensRow>;

/** `PrivateReplayLensDto` for the `verification` lens of one session. */
const WireLens = Type.Object({
  ...Metadata,
  result: Nullable(
    Type.Object({
      lens: Type.Literal('verification'),
      target: Type.Object({ kind: Type.Literal('session'), sessionRef: SessionRef }),
      rows: Type.Array(LensRow),
      emptyReason: Nullable(Type.String({ maxLength: 600 })),
    }),
  ),
});

/** One kind of check in the verification lens, with the three metrics this client reads. */
export interface VerificationRow {
  readonly label: string;
  readonly runs: number | null;
  readonly passed: number | null;
  readonly passRate: number | null;
}

/** A verification lens whose rows have been read. */
export interface ReadLens extends WireMetadata {
  readonly result: {
    readonly sessionRef: string;
    readonly rows: readonly VerificationRow[];
    readonly emptyReason: string | null;
  } | null;
}

const isCount = (value: number | boolean) =>
  typeof value === 'number' && Number.isInteger(value) && value >= 0;
const isFraction = (value: number | boolean) =>
  typeof value === 'number' && value >= 0 && value <= 1;

/** `PrivateReplayLensUnit` as published. v1 may add units; a metric in one is unknown. */
const KNOWN_UNITS: ReadonlySet<string> = new Set([
  'count',
  'percent',
  'usd',
  'seconds',
  'milliseconds',
  'tokens',
  'none',
]);

/**
 * The verification lens as ADR 007 (section 2) states it: one row per kind of check that was
 * measured, with `runs` and `passed` in the unit `count` and `passRate` in the unit `percent`,
 * whose value is nonetheless a fraction from 0 to 1, or null when nothing ran.
 */
const VERIFICATION_METRICS = {
  runs: { unit: 'count', valid: isCount, expected: 'a count' },
  passed: { unit: 'count', valid: isCount, expected: 'a count' },
  passRate: { unit: 'percent', valid: isFraction, expected: 'a fraction from 0 to 1' },
} as const;

/**
 * Reads one metric of a verification row by its key. A metric in a unit v1 added after this
 * client is unknown, so it reads as null. A missing metric, another known unit, or a value of the
 * wrong kind or range contradicts the lens as stated, so it is an issue: refused rather than
 * rescaled or guessed.
 */
function metric(
  row: LensRow,
  path: string,
  key: keyof typeof VERIFICATION_METRICS,
  issues: ValidationIssue[],
): number | null {
  const at = row.metrics.findIndex((candidate) => candidate.key === key);
  const found = row.metrics[at];
  if (found === undefined) {
    issues.push({ path: `${path}/metrics`, message: `has no ${key} metric` });
    return null;
  }
  if (!KNOWN_UNITS.has(found.unit)) return null;
  const { unit, valid, expected } = VERIFICATION_METRICS[key];
  if (found.unit !== unit) {
    issues.push({ path: `${path}/metrics/${at}/unit`, message: `must be ${unit} for ${key}` });
    return null;
  }
  if (found.value === null || valid(found.value)) return found.value as number | null;
  issues.push({ path: `${path}/metrics/${at}/value`, message: `must be ${expected} or null` });
  return null;
}

const validateLensShape = compileValidator(WireLens);

export function readLens(value: unknown): Validated<ReadLens> {
  const shape = validateLensShape(value);
  if (!shape.ok) return shape;
  const { result, ...metadata } = shape.value;
  if (result === null) return { ok: true, value: { ...metadata, result: null } };
  const issues: ValidationIssue[] = [];
  const rows = result.rows.map((row, index): VerificationRow => {
    const path = `/result/rows/${index}`;
    const runs = metric(row, path, 'runs', issues);
    const passed = metric(row, path, 'passed', issues);
    const passRate = metric(row, path, 'passRate', issues);
    if (runs !== null && passed !== null && passed > runs)
      issues.push({ path, message: 'has more passed runs than runs' });
    return { label: row.label, runs, passed, passRate };
  });
  if (issues.length > 0) return { ok: false, issues };
  const read = { sessionRef: result.target.sessionRef, rows, emptyReason: result.emptyReason };
  return { ok: true, value: { ...metadata, result: read } };
}

export const validateSession = compileValidator(WireSession);
export const validateOutcome = compileValidator(WireOutcome);
