import { type EventEnvelope, RUNTIME_EVENT_TYPES } from '@halcyonic/contracts';
import type { ControlPlane } from '../core/control-plane.ts';
import { checkStateOf } from './fact-words.ts';

/** The most file paths the evidence lists; totals still count every file. */
export const EVIDENCE_FILES = 12;
/** The most journal events read for one task's file evidence, newest turns kept. */
const SCANNED = 4096;

type Outside = (text: string, maximum: number) => { text: string; complete: boolean };

/**
 * A task's status as the model reads it: a turn that ended is not work that is done, so the
 * statuses that name a turn's end say so (`completed` read as "complete" in the first real runs).
 */
export function turnStatus(status: string): string {
  return status === 'completed'
    ? 'turn_ended'
    : status === 'failed'
      ? 'turn_failed'
      : status === 'interrupted'
        ? 'turn_stopped'
        : status;
}

/** How a step of the Mac's check ended, in the words the model reads. */
function step(exit: number | null): 'passed' | 'failed' | 'stopped_at_limit' {
  return exit === null ? 'stopped_at_limit' : exit === 0 ? 'passed' : 'failed';
}

/**
 * What the Mac itself observed about one task (ADR 0030, LOOP unit 3): the files each turn
 * created, changed and deleted (ADR 0038), the project's latest check and whether its files changed
 * since (ADR 0037), the failed check sent back on its own (ADR 0044), and the model the runtime
 * reported. Paths and page errors are outside text, marked as data. Never the agent's own claims.
 */
export function taskEvidence(plane: ControlPlane, executionId: string, outside: Outside) {
  const run = plane.projection.execution(executionId);
  if (!run) return { availability: 'unavailable', reason: 'execution_missing' } as const;
  // Runtime events are many and carry no file facts: leave them out so the bound covers turns.
  const skip = RUNTIME_EVENT_TYPES.filter((type) => !type.startsWith('runtime.turn.'));
  const turns: {
    created: number;
    changed: number;
    deleted: number;
    incomplete: boolean;
    files: { path: string; how: string }[];
  }[] = [];
  let scanned = 0;
  let complete = false;
  for (let after = 0; scanned < SCANNED; ) {
    const batch = plane.journal.read({
      after,
      limit: 256,
      workstreamId: run.workstream_id,
      excludeEventTypes: skip,
    });
    if (batch.length === 0) {
      complete = true;
      break;
    }
    for (const { position, event } of batch) {
      after = position;
      scanned++;
      if (event.execution_id !== executionId || event.event_type !== 'execution.files.observed')
        continue;
      const { created, changed, deleted, skipped, not_read } = event.payload;
      turns.push({
        created: created.total,
        changed: changed.total,
        deleted: deleted.total,
        incomplete: skipped !== null || not_read > 0,
        files: [
          ...created.files.map((file) => ({ path: file.path, how: 'created' })),
          ...changed.files.map((file) => ({ path: file.path, how: 'changed' })),
          ...deleted.files.map((file) => ({ path: file.path, how: 'deleted' })),
        ],
      });
    }
    if (batch.length < 256) {
      complete = true;
      break;
    }
  }
  const files: { path: { text: string; complete: boolean }; how: string }[] = [];
  const seen = new Set<string>();
  let listed = 0;
  for (const turn of [...turns].reverse()) {
    for (const file of turn.files) {
      listed++;
      if (seen.has(file.path) || files.length >= EVIDENCE_FILES) continue;
      seen.add(file.path);
      files.push({ path: outside(file.path, 200), how: file.how });
    }
  }
  const sum = (key: 'created' | 'changed' | 'deleted') =>
    turns.reduce((total, turn) => total + turn[key], 0);
  const latest = turns.at(-1);
  const check = checkOfTask(plane, run.project_id, run.workstream_id);
  const findings = check?.findings;
  return {
    observed_by: 'the Mac, not the agent',
    task: { status: turnStatus(run.status), turns: run.turn_count, model_reported: run.model_ref },
    changes: {
      turns_observed: turns.length,
      all_turns_read: complete,
      some_turn_read_incompletely: turns.some((turn) => turn.incomplete),
      created: sum('created'),
      changed: sum('changed'),
      deleted: sum('deleted'),
      latest_turn: latest
        ? { created: latest.created, changed: latest.changed, deleted: latest.deleted }
        : null,
      files_newest_first: files,
      /** Paths the observations listed beyond those shown (a file changed in two turns counts twice). */
      more_listed: Math.max(0, listed - files.length),
    },
    check:
      check === undefined || findings === undefined
        ? { state: 'not_checked' }
        : {
            state: checkStateOf(findings),
            type_check: step(findings.tsc.exit),
            build: step(findings.build.exit),
            page:
              findings.page === null
                ? null
                : {
                    errors: findings.page.exception_count,
                    first_errors: findings.page.exceptions
                      .slice(0, 2)
                      .map((line) => outside(line, 300)),
                    showed_its_own_picture: findings.page.drawn,
                  },
            unknown: findings.unknown === null ? null : outside(findings.unknown, 300),
            files_since_check: check.files,
            recorded_at: check.recorded_at,
          },
    sent_back_on_its_own: run.auto_fix ?? null,
  };
}

/**
 * The host's own facts in a task's event, for `read_events`: counts and states only, never paths
 * or text (those are in `read_workspace`'s evidence, marked as data); null for any other event.
 */
export function hostDetails(event: EventEnvelope): string | null {
  switch (event.event_type) {
    case 'execution.files.observed': {
      const { created, changed, deleted, skipped } = event.payload;
      return JSON.stringify({
        created: created.total,
        changed: changed.total,
        deleted: deleted.total,
        read_whole: skipped === null,
      });
    }
    case 'execution.check_output_given':
      return JSON.stringify({ check_lines_given: event.payload.lines, parts: event.payload.parts });
    case 'execution.auto_fix.sent':
      return JSON.stringify({ sent_back_on_its_own: event.payload.attempt, of: event.payload.cap });
    case 'execution.auto_fix.ended':
      return JSON.stringify({
        sending_back_ended: event.payload.reason,
        tries: event.payload.attempts,
      });
    default:
      return null;
  }
}

/**
 * The project's latest check where it is this task's own folder's (AUDIT2, 2026-11-11): the task's own copy's check for a
 * task in a copy of its own (ADR 0045), the project folder's for a task working there. Another task's copy's check is
 * not this task's, and reads as none.
 */
export function checkOfTask(plane: ControlPlane, projectId: string, workstreamId: string) {
  const check = plane.projection.latestCheck(projectId);
  if (check === undefined) return undefined;
  const copy = plane.projection.workstream(workstreamId)?.copy;
  const own = copy?.state === 'own';
  return (own ? check.workstream_id === workstreamId : check.workstream_id === null)
    ? check
    : undefined;
}
