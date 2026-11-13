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
  ProjectUsage,
  QuestionView,
  QueuedInstructionView,
  RuntimeRef,
  SpendingCapView,
  TestRunResultView,
  TestRunView,
  Timestamp,
  ToolActivityView,
  UsageEstimate,
  UsagePrices,
  UsageTokens,
  WorkstreamId,
} from '@halcyonic/contracts';
import { SPENDING_CAP_REACHED, USAGE_USD_MAX } from '@halcyonic/contracts';
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
  /** Its spending limit and the span the app's estimates are summed over; null for none. */
  spendingCap: SpendingCapState | null;
}

/**
 * A task's spending limit as the host keeps it (lead's ruling, 2026-11-13): the limit the person set
 * or raised it by, and the sum of the app's own estimates since then, null once not known.
 */
export interface SpendingCapState {
  limitUsd: number;
  spentUsd: number | null;
  prices: UsagePrices | null;
  /** Whether the running turn reported its usage; a turn that ends without one leaves the sum unknown. */
  turnReported: boolean;
  /** Re-attached with nothing it could be handed: it carries on only under a limit that counts from now. */
  waiting: boolean;
  /** Whether the span began at a raise (Carry On) rather than at the task's start. */
  raised: boolean;
  /** The known sum of the spans before this one, 0 at the start; null once any of them was not known. */
  earlierUsd: number | null;
  /** The known sum when the app stopped it at the limit, null when not known; absent before a stop. */
  stoppedAt?: { readonly spentUsd: number | null };
}

/**
 * A limit set at the start, or raised now by Carry On from the span before (`after`): nothing spent
 * against it yet, and the earlier spans' known sum carried, so the whole task's figure can be said.
 */
export function newSpendingCap(
  limitUsd: number,
  after?: SpendingCapState | null,
): SpendingCapState {
  const earlier =
    after == null
      ? 0
      : // A state from before the earlier sum was kept has none: not known.
        after.earlierUsd == null || after.spentUsd === null
        ? null
        : after.earlierUsd + after.spentUsd;
  return {
    limitUsd,
    spentUsd: 0,
    prices: null,
    turnReported: false,
    waiting: false,
    raised: after != null,
    earlierUsd: earlier,
  };
}

/**
 * What a person reads when the app stopped a task at its limit (ADR 0062): the limit, then the
 * known sum over the person's own span, named only where a raise began it, or that it is not known.
 * Never a span the person cannot see, never "at most".
 */
export function capStopWords(cap: SpendingCapState): string {
  const spent = cap.stoppedAt === undefined ? cap.spentUsd : cap.stoppedAt.spentUsd;
  const limit = dollars(cap.limitUsd);
  const unreported = 'because the app did not report the cost of some of its work';
  if (cap.raised) {
    const first = `You let this task spend ${limit} more, and the app stopped it once its estimate passed that.`;
    const earlier = cap.earlierUsd ?? null;
    const second =
      spent === null
        ? `How much it spent since then is not known, ${unreported}.`
        : earlier === null
          ? `It estimates the task spent ${dollars(spent)} since then but cannot say what it spent before, an estimate and not your provider's bill.`
          : `It estimates the task spent ${dollars(spent)} since then and ${dollars(earlier + spent)} in all, an estimate and not your provider's bill.`;
    return `${first} ${second}`;
  }
  const first = `The app stopped this task once its estimate passed your ${limit} limit.`;
  const second =
    spent === null
      ? `How much the task spent in all is not known, ${unreported}.`
      : `It estimates the task spent ${dollars(spent)}, an estimate and not your provider's bill.`;
  return `${first} ${second}`;
}

function dollars(value: number): string {
  return `$${value.toFixed(2)}`;
}

/** Adds a turn's reported estimate to the sum: unknown once a turn gives none or other prices. */
export function capOnUsage(
  cap: SpendingCapState,
  turn: { readonly tokens: UsageTokens; readonly cost: UsageEstimate | null },
): void {
  cap.turnReported = true;
  if (cap.spentUsd === null) return;
  if (turn.cost === null) {
    if (!usedNothing(turn.tokens)) cap.spentUsd = null;
    return;
  }
  if (cap.prices !== null && cap.prices !== turn.cost.prices) {
    cap.spentUsd = null;
    return;
  }
  const spent = cap.spentUsd + turn.cost.estimated_usd;
  cap.spentUsd = spent <= USAGE_USD_MAX ? spent : null;
  cap.prices = turn.cost.prices;
}

/** A turn ended: if it reported nothing, what it spent is not known, so neither is the sum. */
export function capOnTurnEnded(cap: SpendingCapState): void {
  if (!cap.turnReported) cap.spentUsd = null;
  cap.turnReported = false;
}

/** The view of a task's limit: reached once the app stopped it there or the known sum is at it. */
export function spendingCapView(state: ExecutionState): SpendingCapView | null {
  const cap = state.spendingCap ?? null;
  if (cap === null) return null;
  return { limit_usd: cap.limitUsd, spent_usd: cap.spentUsd, state: capState(state, cap) };
}

function capState(state: ExecutionState, cap: SpendingCapState): SpendingCapView['state'] {
  const stopped = state.activeTurn === null && state.lastTurn?.error?.code === SPENDING_CAP_REACHED;
  if (stopped || (cap.spentUsd !== null && cap.spentUsd >= cap.limitUsd)) return 'reached';
  return cap.waiting ? 'waiting' : 'open';
}

/**
 * What is left of the limit to hand the session's next launch: the limit less the known sum, or
 * null where the sum is not known (a turn in flight that has not reported counts as not known) or
 * nothing is left. Never more than the limit.
 */
export function spendingAllowance(state: ExecutionState): number | null {
  const cap = state.spendingCap ?? null;
  if (cap === null || cap.spentUsd === null) return null;
  if (state.activeTurn !== null && !cap.turnReported) return null;
  const left = cap.limitUsd - cap.spentUsd;
  return left > 0 ? Math.min(left, cap.limitUsd) : null;
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
    spendingCap: null,
  };
}

const TOKEN_PARTS = ['input', 'cache_read', 'cache_write', 'output', 'reasoning'] as const;

/** Whether every token part is a counted 0: nothing was used, so no price could make it cost. */
function usedNothing(tokens: UsageTokens): boolean {
  return TOKEN_PARTS.every((part) => tokens[part] === 0);
}

/**
 * Adds one turn's reported usage to what an execution's turns used before (`addUsage`).
 */
export function addTurnUsage(
  before: ExecutionUsage | null,
  turn: { readonly tokens: UsageTokens; readonly cost: UsageEstimate | null },
): ExecutionUsage {
  return addUsage(before, { turns: 1, tokens: turn.tokens, cost: turn.cost });
}

/**
 * Adds what more turns used to what turns used before: each token part is summed, and stays null
 * once any turn left it null; the cost is summed while every turn gave one by the same prices, and
 * null from the first that did not. A stretch that used nothing at all, every part a counted 0,
 * costs nothing whatever its estimate says, so it never voids the cost. A sum past `USAGE_USD_MAX`
 * is unknown, so a view keeps to its contract. Unknown never becomes a number.
 */
export function addUsage(before: ExecutionUsage | null, more: ExecutionUsage): ExecutionUsage {
  if (before === null)
    return { turns: more.turns, tokens: { ...more.tokens }, cost: more.cost && { ...more.cost } };
  const tokens = { ...before.tokens };
  for (const part of TOKEN_PARTS) {
    const was = before.tokens[part];
    const now = more.tokens[part];
    tokens[part] = was === null || now === null ? null : was + now;
  }
  const cost =
    more.cost === null && usedNothing(more.tokens)
      ? before.cost && { ...before.cost }
      : before.cost === null && usedNothing(before.tokens)
        ? more.cost && { ...more.cost }
        : before.cost !== null &&
            more.cost !== null &&
            before.cost.prices === more.cost.prices &&
            before.cost.estimated_usd + more.cost.estimated_usd <= USAGE_USD_MAX
          ? {
              estimated_usd: before.cost.estimated_usd + more.cost.estimated_usd,
              prices: before.cost.prices,
            }
          : null;
  return { turns: before.turns + more.turns, tokens, cost };
}

/**
 * What a project's runs used: their reported usage added up (`addUsage`), and every ended turn of
 * them counted, reported or not, so a client can say how much of the use is in it. Null where none of
 * its runs reported any.
 */
export function projectUsage(executions: Iterable<ExecutionState>): ProjectUsage | null {
  let sum: ExecutionUsage | null = null;
  let ended = 0;
  for (const execution of executions) {
    ended += execution.turnCount - (execution.activeTurn === null ? 0 : 1);
    if (execution.usage != null) sum = addUsage(sum, execution.usage);
  }
  if (sum === null) return null;
  // A report never comes before its turn's start, so this holds; the bound keeps the view whole if not.
  return {
    turns: sum.turns,
    turns_ended: Math.max(ended, sum.turns),
    tokens: sum.tokens,
    cost: sum.cost,
  };
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
  if (deriveExecutionStatus(state) === 'failed') {
    const error = state.lastTurn?.error ?? null;
    // A stop at the limit says the person's own figures, in the host's words (ADR 0062).
    if (error?.code === SPENDING_CAP_REACHED && state.spendingCap != null)
      return { code: error.code, message: capStopWords(state.spendingCap) };
    return error;
  }
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
    // A state from before the limit was recorded has none.
    ...(spendingCapView(state) !== null && {
      spending_cap: spendingCapView(state) as SpendingCapView,
    }),
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
