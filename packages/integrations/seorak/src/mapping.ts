import { ESTIMATED_COST_NOTE, type Evaluation } from '@halcyonic/contracts';
import type { ReadLens, WireMetadata, WireOutcome } from './wire.ts';

type Read = Pick<Evaluation['cost'], 'availability' | 'coverage' | 'freshness'>;
type Reason = NonNullable<Read['availability']['reason']>;
type Omission = Read['coverage']['omissions'][number];
type Measure = NonNullable<Evaluation['outcome']['measure']>;
type EndReason = NonNullable<Measure['end_reason']>;
type Fate = NonNullable<Measure['line_survival']>['fate'];

/**
 * Seorak spells its values with hyphens; Halcyonic's vocabulary is snake_case. v1 may add values
 * to these unions without notice (ADR 007, section 2), so a value missing here reads as unknown.
 */
const REASON: ReadonlyMap<string, Reason> = new Map([
  ['not-captured', 'not_captured'],
  ['not-retained', 'not_retained'],
  ['not-yet-computed', 'not_yet_computed'],
  ['outside-credential-restriction', 'outside_credential_restriction'],
  ['temporarily-unavailable', 'temporarily_unavailable'],
  ['result-limit', 'result_limit'],
]);

const OMISSION: ReadonlyMap<string, Omission> = new Map([
  ['outside-retention', 'outside_retention'],
  ['capture-unavailable', 'capture_unavailable'],
  ['projection-pending', 'projection_pending'],
  ['credential-restriction', 'credential_restriction'],
  ['result-limit', 'result_limit'],
]);

const END_REASONS: readonly EndReason[] = [
  'clear',
  'resume',
  'logout',
  'prompt_input_exit',
  'bypass_permissions_disabled',
  'other',
];

/** Seorak's own `unknown` fate and a fate this client does not know read alike: unknown. */
const FATES: readonly Fate[] = ['retained', 'overwritten', 'unreachable', 'unknown'];

/** A value this client knows, or `unknown` for one v1 added later. */
function known<T extends string>(values: readonly T[], value: string): T | 'unknown' {
  return values.find((candidate) => candidate === value) ?? 'unknown';
}

/** The same instant in Halcyonic's form, `toISOString()`'s. */
function instant(value: string): string {
  return new Date(value).toISOString();
}

/**
 * Seorak's own statement about one read: whether it could answer, what it covers, how current. An
 * omission this client does not know makes the coverage incomplete, whatever Seorak said.
 */
function read({ availability, coverage, freshness }: WireMetadata): Read {
  const omissions = [...new Set(coverage.omissions.map((o) => OMISSION.get(o) ?? 'unknown'))];
  return {
    availability: {
      state: availability.state,
      reason: availability.reason === null ? null : (REASON.get(availability.reason) ?? 'unknown'),
    },
    coverage: {
      requested: { from: coverage.requested.from, through: coverage.requested.through },
      observed: coverage.observed && {
        from: coverage.observed.from,
        through: coverage.observed.through,
      },
      matched_sessions: coverage.matchedSessionCount,
      included_sessions: coverage.includedSessionCount,
      complete: coverage.complete && !omissions.includes('unknown'),
      omissions,
    },
    freshness: {
      state: freshness.state,
      generated_at: instant(freshness.generatedAt),
      data_through: freshness.dataThrough && instant(freshness.dataThrough),
      stale_at: instant(freshness.staleAt),
    },
  };
}

/**
 * Maps the three reads onto Halcyonic's evaluation, field by field. Null stays null: an unpriced
 * cost, an outcome that has not matured and a metric without a result stay unknown, never zero.
 * Nothing is combined, and properties this function does not name never reach the result.
 */
export function toEvaluation(
  resolved: WireMetadata,
  costUsd: number | null,
  outcome: WireOutcome,
  lens: ReadLens,
): Evaluation {
  const measure = outcome.outcome;
  return {
    // Read from Seorak. The recorded demonstration marks its stand-in's answers synthetic.
    source: { system: 'seorak', synthetic: false, api_version: 'v1' },
    cost: { ...read(resolved), estimated_usd: costUsd, note: ESTIMATED_COST_NOTE },
    outcome: {
      ...read(outcome),
      measure: measure && {
        commits_landed: measure.commitsLanded,
        uncommitted: measure.uncommitted && {
          files_touched: measure.uncommitted.filesTouched,
          lines_added: measure.uncommitted.linesAdded,
          lines_removed: measure.uncommitted.linesRemoved,
          generated_lines_excluded: measure.uncommitted.generatedLinesExcluded,
        },
        line_survival: measure.lineSurvival && {
          rung: measure.lineSurvival.rung,
          fate: known(FATES, measure.lineSurvival.fate),
          rate: measure.lineSurvival.rate,
          lines_authored: measure.lineSurvival.linesAuthored,
          lines_surviving: measure.lineSurvival.linesSurviving,
          commits_checked: measure.lineSurvival.commitsChecked,
        },
        error_count: measure.errorCount,
        first_error_at: measure.firstErrorAt && instant(measure.firstErrorAt),
        end_reason: measure.endReason === null ? null : known(END_REASONS, measure.endReason),
      },
    },
    verification: {
      ...read(lens),
      lens: lens.result && {
        by_kind: lens.result.rows.map((row) => ({
          label: row.label,
          runs: row.runs,
          passed: row.passed,
          pass_rate: row.passRate,
        })),
        empty_reason: lens.result.emptyReason,
      },
    },
  };
}
