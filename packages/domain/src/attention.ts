import type { Attention, AttentionReason } from '@halcyonic/contracts';
import { deriveExecutionStatus, type ExecutionState } from './execution-state.ts';

/**
 * Whether a workstream needs its human, and why. Deterministic and derived only from observed
 * facts, so every signal can be explained by the events behind it.
 *
 * - Any execution blocked on an approval or on a question the agent asked requires action, even
 *   one the person can only answer by stopping the execution.
 * - The current execution having failed or become unobservable is a notice.
 * - The current execution reporting completion after a test run that did not pass is a notice:
 *   "the agent says done" is not the same as "the work is verified".
 *
 * Completion on its own is not an attention signal; the status already conveys it.
 */
export function deriveAttention(
  executions: readonly ExecutionState[],
  current: ExecutionState | undefined,
): Attention {
  const reasons: AttentionReason[] = [];

  for (const execution of executions) {
    if (deriveExecutionStatus(execution) !== 'waiting_for_human') continue;
    for (const approval of execution.pendingApprovals.values()) {
      reasons.push({
        kind: 'approval_pending',
        execution_id: execution.executionId,
        approval_id: approval.approval_id,
      });
    }
    for (const question of execution.pendingQuestions.values()) {
      reasons.push({
        kind: 'question_pending',
        execution_id: execution.executionId,
        question_id: question.question_id,
      });
    }
  }

  if (current !== undefined) {
    const status = deriveExecutionStatus(current);
    if (status === 'failed') {
      reasons.push({ kind: 'execution_failed', execution_id: current.executionId });
    } else if (status === 'unknown') {
      reasons.push({ kind: 'execution_state_unknown', execution_id: current.executionId });
    } else if (
      status === 'completed' &&
      current.lastTestRun !== null &&
      current.lastTestRun.outcome !== 'passed'
    ) {
      reasons.push({
        kind: 'verification_failed',
        execution_id: current.executionId,
        test_run_id: current.lastTestRun.test_run_id,
      });
    }
  }

  const level = reasons.some((reason) => reason.kind === 'approval_pending')
    ? 'action_required'
    : reasons.length > 0
      ? 'notice'
      : 'none';
  return { level, reasons };
}
