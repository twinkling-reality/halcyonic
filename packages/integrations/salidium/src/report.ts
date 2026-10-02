import type { Understanding } from '@halcyonic/contracts';
import type { Discovered } from './connection.ts';
import type { WireReport } from './wire.ts';

type Source = Understanding['source'];
type Statement = NonNullable<Understanding['latest_statement']>;
type Verification = Understanding['verification']['latest_by_method'][number];
type Waiting = NonNullable<Understanding['waiting']>;
type WireStatement = NonNullable<WireReport['latestStatement']>;
type WireRun = WireReport['verification']['latestByMethod'][number];
type WireWaiting = NonNullable<WireReport['waiting']>;

/** Written out, so a kind either side adds fails the type check instead of passing unmapped. */
const WAITING_KIND: Record<WireWaiting['kind'], Waiting['kind']> = {
  permission: 'permission',
  question: 'question',
  input: 'input',
};

/** Salidium spells two values with hyphens; Halcyonic's vocabulary is snake_case. */
const EXIT_OBSERVATION: Record<
  WireRun['exit']['observation'],
  Verification['exit']['observation']
> = {
  explicit: 'explicit',
  'inferred-success': 'inferred_success',
  'inferred-failure': 'inferred_failure',
  unknown: 'unknown',
};

function statement(line: WireStatement): Statement {
  return { text: line.text, author: line.author, at: line.at, epistemic: line.provenance };
}

function verification(run: WireRun): Verification {
  return {
    method: run.method,
    runner: run.runner,
    label: run.label,
    outcome: run.outcome,
    at: run.at,
    scope: run.scope,
    counts: run.counts && {
      passed: run.counts.passed,
      failed: run.counts.failed,
      skipped: run.counts.skipped,
      total: run.counts.total,
    },
    exit: { code: run.exit.code, observation: EXIT_OBSERVATION[run.exit.observation] },
    caveats: [...run.caveats],
    stale: run.stale,
    later_unreadable: run.laterUnreadable,
    epistemic: run.provenance,
  };
}

/**
 * Maps a Salidium session report onto Halcyonic's understanding, field by field.
 *
 * Every epistemic class is copied from the report as it is. Nothing is upgraded: a claim the agent
 * made stays `reported`, file coverage stays `inferred`, and the generated explanation stays
 * `explained`. Properties this function does not name never reach the result.
 */
export function toUnderstanding(
  report: WireReport,
  { discovery, contract }: Discovered,
): Understanding {
  const source: Source = {
    system: 'salidium',
    // Read from Salidium. The recorded demonstration marks its stand-in's answers synthetic.
    synthetic: false,
    version: discovery.salidium.version,
    contract: { name: contract.name, major: contract.major, minor: contract.minor },
    instance_id: discovery.instanceId,
    generated_at: report.generatedAt,
    evidence_sequence: report.session.evidenceSeq,
  };
  const { changes, verification: verified, review, remaining, explanation } = report;
  const content = explanation.content;
  return {
    source,
    verdict: {
      headline: report.verdict.headline,
      tone: report.verdict.tone,
      because: report.verdict.because,
      at: report.verdict.at,
      epistemic: report.verdict.provenance,
    },
    latest_statement: report.latestStatement && statement(report.latestStatement),
    waiting: report.waiting && {
      kind: WAITING_KIND[report.waiting.kind],
      summary: report.waiting.summary,
      since: report.waiting.since,
      epistemic: report.waiting.provenance,
    },
    // Contract 1.1's anchors and per-file repository, without the host's paths; null from 1.0.
    revision: {
      at_start: anchor(report.revision?.atStart ?? null),
      at_latest_turn_end: anchor(report.revision?.atLatestTurnEnd ?? null),
    },
    changes: {
      summary: changes.glance,
      files: changes.files.map((file) => ({
        path: file.path,
        repository_path: file.repository?.path ?? null,
        change_count: file.changeCount,
        lines_added: file.linesAdded,
        lines_removed: file.linesRemoved,
        lines_removed_exact: file.linesRemovedExact ?? null,
        kinds: [...file.kinds],
        last_changed_at: file.lastChangedAt,
        coverage: {
          verified_after: file.coverage.verifiedAfter,
          by: file.coverage.by,
          epistemic: file.coverage.provenance,
        },
        reason: file.reason && statement(file.reason),
      })),
      commits: changes.commits.map((commit) => ({ sha: commit.sha, at: commit.at })),
    },
    verification: {
      summary: verified.glance,
      latest_by_method: verified.latestByMethod.map(verification),
      unverified_files: [...verified.unverifiedFiles],
      statements: verified.statements.map(statement),
    },
    review: {
      summary: review.glance,
      open: review.open,
      resolved: review.resolved,
      groups: review.groups.map((group) => ({
        rule: group.rule,
        label: group.label,
        severity: group.severity,
        occurrences: group.occurrences,
        latest_at: group.latestAt,
        items: group.items.map((item) => ({
          label: item.label,
          instance: item.instance,
          created_at: item.createdAt,
          repeats: item.repeats,
          epistemic: item.provenance,
        })),
      })),
    },
    remaining: {
      summary: remaining.glance,
      items: remaining.items.map((item) => ({
        text: item.text,
        status: item.status,
        source: item.source,
        epistemic: item.provenance,
      })),
    },
    explanation: {
      status: explanation.status,
      current: explanation.current,
      based_on_sequence: explanation.basedOnSeq,
      generated_at: explanation.generatedAt,
      model: explanation.model,
      epistemic: explanation.provenance,
      content: content && {
        what: { summary: content.what.summary, currently: content.what.currently },
        why: {
          summary: content.why.summary,
          lanes: content.why.lanes.map((lane) => ({ title: lane.title, steps: [...lane.steps] })),
          chain: [...content.why.chain],
        },
        how: {
          summary: content.how.summary,
          root: content.how.root,
          steps: [...content.how.steps],
        },
        approach_change: content.approachChange && {
          from: content.approachChange.from,
          from_steps: [...content.approachChange.fromSteps],
          why: content.approachChange.why,
          to: content.approachChange.to,
          to_steps: [...content.approachChange.toSteps],
        },
      },
    },
  };
}

type WireAnchor = NonNullable<NonNullable<WireReport['revision']>['atStart']>;

/** One revision anchor, the commit and branch only: the repository's path on the host stays there. */
function anchor(value: WireAnchor | null): Understanding['revision']['at_start'] {
  return (
    value && { head: value.head, branch: value.branch, at: value.at, epistemic: value.provenance }
  );
}
