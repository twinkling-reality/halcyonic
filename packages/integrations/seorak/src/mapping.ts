import { ESTIMATED_COST_NOTE, type Evaluation } from '@halcyonic/contracts';
import type { ReadLens, WireMetadata, WireOutcome } from './wire.ts';

type Read = Pick<Evaluation['cost'], 'availability' | 'coverage' | 'freshness'>;
type WireReason = NonNullable<WireMetadata['availability']['reason']>;
type WireOmission = WireMetadata['coverage']['omissions'][number];

/**
 * Seorak spells its values with hyphens; Halcyonic's vocabulary is snake_case. Written out, so a
 * value either side adds fails the type check instead of passing unmapped.
 */
const REASON: Record<WireReason, NonNullable<Read['availability']['reason']>> = {
  'not-captured': 'not_captured',
  'not-retained': 'not_retained',
  'not-yet-computed': 'not_yet_computed',
  'outside-credential-restriction': 'outside_credential_restriction',
  'temporarily-unavailable': 'temporarily_unavailable',
  'result-limit': 'result_limit',
};

const OMISSION: Record<WireOmission, Read['coverage']['omissions'][number]> = {
  'outside-retention': 'outside_retention',
  'capture-unavailable': 'capture_unavailable',
  'projection-pending': 'projection_pending',
  'credential-restriction': 'credential_restriction',
  'result-limit': 'result_limit',
};

/** The same instant in Halcyonic's form, `toISOString()`'s. */
function instant(value: string): string {
  return new Date(value).toISOString();
}

/** Seorak's own statement about one read: whether it could answer, what it covers, how current. */
function read({ availability, coverage, freshness }: WireMetadata): Read {
  return {
    availability: {
      state: availability.state,
      reason: availability.reason && REASON[availability.reason],
    },
    coverage: {
      requested: { from: coverage.requested.from, through: coverage.requested.through },
      observed: coverage.observed && {
        from: coverage.observed.from,
        through: coverage.observed.through,
      },
      matched_sessions: coverage.matchedSessionCount,
      included_sessions: coverage.includedSessionCount,
      complete: coverage.complete,
      omissions: coverage.omissions.map((omission) => OMISSION[omission]),
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
    source: { system: 'seorak', api_version: 'v1' },
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
          fate: measure.lineSurvival.fate,
          rate: measure.lineSurvival.rate,
          lines_authored: measure.lineSurvival.linesAuthored,
          lines_surviving: measure.lineSurvival.linesSurviving,
          commits_checked: measure.lineSurvival.commitsChecked,
        },
        error_count: measure.errorCount,
        first_error_at: measure.firstErrorAt && instant(measure.firstErrorAt),
        end_reason: measure.endReason,
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
