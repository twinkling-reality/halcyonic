import type {
  ApprovalView,
  ErrorInfo,
  ExecutionId,
  ExecutionStatus,
  ExecutionView,
  ProjectId,
  RuntimeRef,
  TestRunResultView,
  TestRunView,
  Timestamp,
  ToolActivityView,
  WorkstreamId,
} from '@halcyonic/contracts';

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
  readonly createdAt: Timestamp;
  nativeId: string | null;
  /** True once the runtime has reported any activity, meaning a native session exists. */
  runtimeStarted: boolean;
  startedAt: Timestamp | null;
  updatedAt: Timestamp;
  activeTurn: { turnId: string | null } | null;
  lastTurn: { outcome: TurnOutcome; error: ErrorInfo | null } | null;
  turnCount: number;
  pendingApprovals: Map<string, ApprovalView>;
  activeTools: Map<string, ToolActivityView>;
  activeTestRun: TestRunView | null;
  lastTestRun: TestRunResultView | null;
  startFailure: ErrorInfo | null;
  /** Set when the execution can no longer be observed; cleared by the next runtime observation. */
  unknownReason: ErrorInfo | null;
  /** Highest source sequence applied, used to ignore reordered runtime events. */
  lastSequence: number | null;
}

export function createExecutionState(init: {
  executionId: ExecutionId;
  workstreamId: WorkstreamId;
  projectId: ProjectId;
  runtime: RuntimeRef;
  instruction: string;
  createdAt: Timestamp;
}): ExecutionState {
  return {
    ...init,
    nativeId: null,
    runtimeStarted: false,
    startedAt: null,
    updatedAt: init.createdAt,
    activeTurn: null,
    lastTurn: null,
    turnCount: 0,
    pendingApprovals: new Map(),
    activeTools: new Map(),
    activeTestRun: null,
    lastTestRun: null,
    startFailure: null,
    unknownReason: null,
    lastSequence: null,
  };
}

/**
 * Precedence, highest first: lost contact, failed start, blocked on a human, turn in progress,
 * outcome of the last turn, and finally not yet started.
 */
export function deriveExecutionStatus(state: ExecutionState): ExecutionStatus {
  if (state.unknownReason !== null) return 'unknown';
  if (state.startFailure !== null) return 'failed';
  if (state.pendingApprovals.size > 0) return 'waiting_for_human';
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
  state.activeTools.clear();
  state.activeTestRun = null;
}

export function toExecutionView(state: ExecutionState): ExecutionView {
  return {
    execution_id: state.executionId,
    workstream_id: state.workstreamId,
    project_id: state.projectId,
    runtime: state.runtime,
    native_id: state.nativeId,
    instruction: state.instruction,
    status: deriveExecutionStatus(state),
    status_reason: deriveStatusReason(state),
    pending_approvals: [...state.pendingApprovals.values()],
    active_tools: [...state.activeTools.values()],
    active_test_run: state.activeTestRun,
    last_test_run: state.lastTestRun,
    turn_count: state.turnCount,
    created_at: state.createdAt,
    started_at: state.startedAt,
    updated_at: state.updatedAt,
  };
}
