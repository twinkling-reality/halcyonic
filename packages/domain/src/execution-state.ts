import type {
  ApprovalLevel,
  ApprovalView,
  AutoFixView,
  CommandId,
  ErrorInfo,
  ExecutionId,
  ExecutionStatus,
  ExecutionUsage,
  ExecutionView,
  ProjectId,
  QuestionView,
  QueuedInstructionView,
  RuntimeRef,
  TestRunResultView,
  TestRunView,
  Timestamp,
  ToolActivityView,
  UsageEstimate,
  UsageTokens,
  WorkstreamId,
} from '@halcyonic/contracts';
import { USAGE_USD_MAX } from '@halcyonic/contracts';
import { shownQuestions } from './questions.ts';

export type TurnOutcome = 'completed' | 'failed' | 'interrupted';

/**
 * The facts known about one execution. Status is never stored: it is derived from these facts
 * by `deriveExecutionStatus`, so it cannot drift from the evidence.
 */
export interface ExecutionState {
  readonly executionId: ExecutionId;
  readonly workstreamId: WorkstreamId;
  readonly projectId: ProjectId;
  readonly runtime: RuntimeRef;
  readonly instruction: string;
  /** The folder the runtime was given; null for none, or not recorded. */
  readonly directory: string | null;
  readonly createdAt: Timestamp;
  /**
   * The command whose start created it, from `execution.created`'s causation, which holds the
   * start's options and chosen model; null when the event names none.
   */
  readonly startCommandId: CommandId | null;
  nativeId: string | null;
  /**
   * Whether its start recorded that it ran inside the host's confinement of the project (S2); false
   * when the start said not, said nothing, or has not come.
   */
  confined: boolean;
  /**
   * The approval level its start recorded the runtime confirmed (ADR 0043); null when the start
   * recorded none or has not come.
   */
  approvalLevel: ApprovalLevel | null;
  /** Whether its start recorded the runtime's own plan mode (PLAN); false otherwise or unknown. */
  plan: boolean;
  /** Whether the runtime has since left the plan to carry it out (`runtime.plan.left`). */
  planLeft: boolean;
  /** Why its start was not counted confined, as recorded (BOX-4); null where none was. */
  unconfinedReason: string | null;
  /** The model the runtime last reported using, as its model list names it. */
  modelRef: string | null;
  /** True once the runtime has reported any activity, meaning a native session exists. */
  runtimeStarted: boolean;
  startedAt: Timestamp | null;
  updatedAt: Timestamp;
  activeTurn: { turnId: string | null } | null;
  lastTurn: { outcome: TurnOutcome; error: ErrorInfo | null } | null;
  turnCount: number;
  pendingApprovals: Map<string, ApprovalView>;
  /** Questions the agent asked that wait for the person (ADR 0022). */
  pendingQuestions: Map<string, QuestionView>;
  activeTools: Map<string, ToolActivityView>;
  activeTestRun: TestRunView | null;
  lastTestRun: TestRunResultView | null;
  startFailure: ErrorInfo | null;
  /** Set when the execution can no longer be observed; cleared by the next runtime observation. */
  unknownReason: ErrorInfo | null;
  /** Highest source sequence applied, used to ignore reordered runtime events. */
  lastSequence: number | null;
  /** The latest chain of the project's failed check sent back to it on its own (ADR 0044). */
  autoFix: AutoFixView | null;
  /** Whether its latest accepted instruction was one the host sent on its own (ADR 0044). */
  autoFixDriven: boolean;
  /** The person's instructions waiting for the turn to end, oldest first (ADR 0056). */
  queued: QueuedInstructionView[];
  /** What its reported turns used (`runtime.usage.reported`); null until one is reported. */
  usage: ExecutionUsage | null;
}

export function createExecutionState(init: {
  executionId: ExecutionId;
  workstreamId: WorkstreamId;
  projectId: ProjectId;
  runtime: RuntimeRef;
  instruction: string;
  directory: string | null;
  createdAt: Timestamp;
  startCommandId: CommandId | null;
}): ExecutionState {
  return {
    ...init,
    nativeId: null,
    confined: false,
    approvalLevel: null,
    plan: false,
    planLeft: false,
    unconfinedReason: null,
    modelRef: null,
    runtimeStarted: false,
    startedAt: null,
    updatedAt: init.createdAt,
    activeTurn: null,
    lastTurn: null,
    turnCount: 0,
    pendingApprovals: new Map(),
    pendingQuestions: new Map(),
    activeTools: new Map(),
    activeTestRun: null,
    lastTestRun: null,
    startFailure: null,
    unknownReason: null,
    lastSequence: null,
    autoFix: null,
    autoFixDriven: false,
    queued: [],
    usage: null,
  };
}

const TOKEN_PARTS = ['input', 'cache_read', 'cache_write', 'output', 'reasoning'] as const;

/** Whether every token part is a counted 0: nothing was used, so no price could make it cost. */
function usedNothing(tokens: UsageTokens): boolean {
  return TOKEN_PARTS.every((part) => tokens[part] === 0);
}

/**
 * Adds one turn's reported usage to what an execution's turns used before: each token part is
 * summed, and stays null once any turn left it null; the cost is summed while every turn gave one
 * by the same prices, and null from the first that did not. A stretch that used nothing at all, every
 * part a counted 0, costs nothing whatever its estimate says, so it never voids the cost. A sum
 * past `USAGE_USD_MAX` is unknown, so the view keeps to its contract. Unknown never becomes a number.
 */
export function addTurnUsage(
  before: ExecutionUsage | null,
  turn: { readonly tokens: UsageTokens; readonly cost: UsageEstimate | null },
): ExecutionUsage {
  if (before === null)
    return { turns: 1, tokens: { ...turn.tokens }, cost: turn.cost && { ...turn.cost } };
  const tokens = { ...before.tokens };
  for (const part of TOKEN_PARTS) {
    const was = before.tokens[part];
    const now = turn.tokens[part];
    tokens[part] = was === null || now === null ? null : was + now;
  }
  const cost =
    turn.cost === null && usedNothing(turn.tokens)
      ? before.cost && { ...before.cost }
      : before.cost === null && usedNothing(before.tokens)
        ? turn.cost && { ...turn.cost }
        : before.cost !== null &&
            turn.cost !== null &&
            before.cost.prices === turn.cost.prices &&
            before.cost.estimated_usd + turn.cost.estimated_usd <= USAGE_USD_MAX
          ? {
              estimated_usd: before.cost.estimated_usd + turn.cost.estimated_usd,
              prices: before.cost.prices,
            }
          : null;
  return { turns: before.turns + 1, tokens, cost };
}

/**
 * Precedence, highest first: lost contact, failed start, blocked on a human, turn in progress,
 * outcome of the last turn, and finally not yet started.
 */
export function deriveExecutionStatus(state: ExecutionState): ExecutionStatus {
  if (state.unknownReason !== null) return 'unknown';
  if (state.startFailure !== null) return 'failed';
  if (state.pendingApprovals.size > 0 || state.pendingQuestions.size > 0) {
    return 'waiting_for_human';
  }
  if (state.activeTurn !== null) return state.activeTestRun !== null ? 'verifying' : 'running';
  if (state.lastTurn !== null) return state.lastTurn.outcome;
  return 'starting';
}

export function deriveStatusReason(state: ExecutionState): ErrorInfo | null {
  if (state.unknownReason !== null) return state.unknownReason;
  if (state.startFailure !== null) return state.startFailure;
  if (deriveExecutionStatus(state) === 'failed') return state.lastTurn?.error ?? null;
  return null;
}

/** Ends the active turn. Nothing can still be in flight once a turn is over. */
export function endTurn(
  state: ExecutionState,
  outcome: TurnOutcome,
  error: ErrorInfo | null,
): void {
  state.activeTurn = null;
  state.lastTurn = { outcome, error };
  state.pendingApprovals.clear();
  state.pendingQuestions.clear();
  state.activeTools.clear();
  state.activeTestRun = null;
}

/**
 * Whether a tool call runs, from the facts alone (`ExecutionView`'s `tool_activity`): an open call,
 * or a test run, counts only in an active turn whose status is not `unknown`, and none open counts
 * as none only on a runtime that reports every tool call it makes.
 */
export function deriveToolActivity(
  state: ExecutionState,
  reportsToolActivity: boolean,
): ExecutionView['tool_activity'] {
  if (state.activeTurn === null) return 'none';
  if (deriveExecutionStatus(state) === 'unknown') return 'unknown';
  if (state.activeTools.size > 0 || state.activeTestRun !== null) return 'running';
  return reportsToolActivity ? 'none' : 'unknown';
}

export function toExecutionView(
  state: ExecutionState,
  reportsToolActivity: boolean,
): ExecutionView {
  return {
    execution_id: state.executionId,
    workstream_id: state.workstreamId,
    project_id: state.projectId,
    runtime: state.runtime,
    native_id: state.nativeId,
    model_ref: state.modelRef,
    ...(state.approvalLevel !== null && { approval_level: state.approvalLevel }),
    ...(state.plan && { plan: true }),
    ...(state.planLeft && { plan_left: true }),
    ...(state.unconfinedReason !== null && { unconfined_reason: state.unconfinedReason }),
    instruction: state.instruction,
    directory: state.directory,
    status: deriveExecutionStatus(state),
    status_reason: deriveStatusReason(state),
    pending_approvals: [...state.pendingApprovals.values()],
    pending_questions: shownQuestions(state.pendingQuestions),
    active_tools: [...state.activeTools.values()],
    tool_activity: deriveToolActivity(state, reportsToolActivity),
    active_test_run: state.activeTestRun,
    last_test_run: state.lastTestRun,
    turn_count: state.turnCount,
    ...(state.autoFix !== null && { auto_fix: { ...state.autoFix } }),
    // A state from before the queue (ADR 0056) has none.
    ...((state.queued ?? []).length > 0 && {
      queued_instructions: state.queued.map((queued) => ({ ...queued })),
    }),
    // A state from before usage was recorded has none.
    ...(state.usage != null && { usage: structuredClone(state.usage) }),
    created_at: state.createdAt,
    started_at: state.startedAt,
    updated_at: state.updatedAt,
  };
}
