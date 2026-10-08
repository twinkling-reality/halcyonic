import type {
  ApprovalDecision,
  EventOf,
  ExecutionId,
  QuestionAnswer,
  RuntimeCapabilities,
  RuntimeDescriptor,
  RuntimeEventType,
  RuntimeId,
  RuntimeModel,
  RuntimeOptions,
} from '@halcyonic/contracts';
import {
  type Clock,
  type ExecutionContext,
  isAbortError,
  type ObservationSink,
  type OptionsValidation,
  RuntimeActionError,
  type RuntimeAdapter,
  type RuntimeObservation,
  type Scheduler,
  type StartExecutionRequest,
  type StartExecutionResult,
  systemClock,
  systemScheduler,
} from '@halcyonic/runtime-core';
import type { BranchStep, Scenario, ScenarioStep } from './scenario.ts';

/**
 * The mock supports what every verified runtime supports and nothing more. It does not accept
 * instructions while a turn runs, so clients exercise the capability checks honestly.
 */
export const MOCK_CAPABILITIES: RuntimeCapabilities = {
  start_execution: true,
  instruct_at_rest: true,
  instruct_while_running: false,
  respond_to_approval: true,
  answer_question: true,
  interrupt: true,
};

/**
 * Script for turns started by an instruction the scenario does not script. It performs no work and
 * says so.
 */
const CONTINUATION_STEPS: readonly ScenarioStep[] = [
  {
    after_ms: 400,
    emit: {
      type: 'runtime.agent_message',
      payload: {
        text: 'Mock runtime: continuing after a new instruction. No real work is performed.',
      },
    },
  },
  { after_ms: 600, emit: { type: 'runtime.turn.completed', payload: {} } },
];

/**
 * Synthetic models for a development control plane, so clients can exercise model choice without a
 * real runtime. Their names say they are simulated; nothing serves them.
 */
export const MOCK_MODELS: readonly RuntimeModel[] = [
  {
    model_ref: 'mock/fast',
    display_name: 'Simulated fast model (mock runtime, development fixture)',
    served: 'this_mac',
    tool_calling: 'declared',
    context_tokens: 65536,
  },
  {
    model_ref: 'mock/hosted',
    display_name: 'Simulated hosted model (mock runtime, development fixture)',
    served: 'remote',
    tool_calling: 'declared',
    context_tokens: 200000,
  },
  {
    model_ref: 'mock/no-tools',
    display_name: 'Simulated model without tools (mock runtime, development fixture)',
    served: 'this_mac',
    tool_calling: 'not_declared',
    context_tokens: null,
  },
];

export interface MockRuntimeOptions {
  readonly scenarios: ReadonlyMap<string, Scenario>;
  /**
   * The models the runtime lists, such as `MOCK_MODELS`. With none, the runtime offers no choice
   * of model, as the recorded demonstration's runtimes do.
   */
  readonly models?: readonly RuntimeModel[];
  readonly runtimeId?: RuntimeId;
  /**
   * What clients call the runtime. Whatever the name, the descriptor stays `synthetic`, so every
   * client still labels the work as simulated.
   */
  readonly displayName?: string;
  readonly clock?: Clock;
  readonly scheduler?: Scheduler;
}

/**
 * A development runtime that replays scripted scenarios. It is `synthetic`, so every client
 * labels its work as fabricated. It exists so the control plane, fixtures and XR client can be
 * built and tested without spending model tokens; it proves nothing about real runtimes.
 */
export class MockRuntimeAdapter implements RuntimeAdapter {
  readonly descriptor: RuntimeDescriptor;
  /** The synthetic models the runtime was given; present only when it was given some. */
  readonly listModels?: () => Promise<readonly RuntimeModel[]>;
  readonly #scenarios: ReadonlyMap<string, Scenario>;
  readonly #models: readonly RuntimeModel[];
  readonly #clock: Clock;
  readonly #scheduler: Scheduler;
  readonly #sessions = new Map<ExecutionId, MockSession>();
  #closed = false;

  /** A truthful, generic simulation when a client has no scenario picker. */
  static readonly DEFAULT_SCENARIO = 'simulated_start';

  constructor(options: MockRuntimeOptions) {
    this.#scenarios = options.scenarios;
    this.#models = options.models ?? [];
    this.#clock = options.clock ?? systemClock;
    this.#scheduler = options.scheduler ?? systemScheduler;
    this.descriptor = {
      runtime_id: options.runtimeId ?? ('mock' as RuntimeId),
      kind: 'mock',
      display_name: options.displayName ?? 'Mock runtime (development fixture)',
      synthetic: true,
      capabilities: MOCK_CAPABILITIES,
      model_choice: this.#models.length > 0 ? 'listed' : 'none',
      uses_project_location: false,
      reports_tool_activity: true,
    };
    if (this.#models.length > 0) this.listModels = async () => this.#models;
  }

  validateStartOptions(options: RuntimeOptions, modelRef: string | null): OptionsValidation {
    if (modelRef !== null && !this.#models.some((model) => model.model_ref === modelRef)) {
      return { ok: false, message: `The mock runtime lists no model ${modelRef}.` };
    }
    const available = [...this.#scenarios.keys()].join(', ');
    const unknown = Object.keys(options).filter((key) => key !== 'scenario');
    if (unknown.length > 0) {
      return {
        ok: false,
        message: `Unknown mock runtime options: ${unknown.join(', ')}. Only "scenario" is supported.`,
      };
    }
    const scenario =
      options.scenario === undefined && this.#scenarios.has(MockRuntimeAdapter.DEFAULT_SCENARIO)
        ? MockRuntimeAdapter.DEFAULT_SCENARIO
        : options.scenario;
    if (typeof scenario !== 'string') {
      return { ok: false, message: `Option "scenario" is required. Available: ${available}.` };
    }
    if (!this.#scenarios.has(scenario)) {
      const named = scenario.length <= 100 ? scenario : `${scenario.slice(0, 99)}…`;
      return { ok: false, message: `Unknown scenario "${named}". Available: ${available}.` };
    }
    return { ok: true };
  }

  async startExecution(request: StartExecutionRequest): Promise<StartExecutionResult> {
    if (this.#closed) throw new RuntimeActionError('runtime_closed', 'The mock runtime is closed.');
    // Checked against the list again, as a real runtime's list may have changed since admission.
    if (
      request.model_ref !== null &&
      !this.#models.some((model) => model.model_ref === request.model_ref)
    ) {
      throw new RuntimeActionError(
        'model_unavailable',
        `The mock runtime no longer lists the model ${request.model_ref}.`,
      );
    }
    const validation = this.validateStartOptions(request.options, request.model_ref);
    if (!validation.ok) throw new RuntimeActionError('invalid_runtime_options', validation.message);
    const scenarioId =
      request.options.scenario === undefined
        ? MockRuntimeAdapter.DEFAULT_SCENARIO
        : (request.options.scenario as string);
    const scenario = this.#scenarios.get(scenarioId);
    if (scenario === undefined) {
      throw new RuntimeActionError(
        'invalid_runtime_options',
        'Scenario disappeared after validation.',
      );
    }
    if (this.#sessions.has(request.execution.execution_id)) {
      throw new RuntimeActionError('duplicate_execution', 'The execution was already started.');
    }
    // Named after the execution, not a count of this process's sessions: the journal outlives
    // the process and deduplicates by native id, so a name reused after a restart would make it
    // drop every observation of the new session.
    const session = new MockSession(
      `mock-session-${request.execution.execution_id}`,
      request.emit,
      this.#clock,
      this.#scheduler,
      new Map((scenario.instructions ?? []).map(({ text, steps }) => [text, steps])),
    );
    this.#sessions.set(request.execution.execution_id, session);
    session.begin(scenario.steps, request.model_ref);
    return { native_id: session.nativeId };
  }

  async sendInstruction(request: { execution: ExecutionContext; text: string }): Promise<void> {
    this.#session(request.execution).instruct(request.text);
  }

  async respondToApproval(request: {
    execution: ExecutionContext;
    approval_id: string;
    decision: ApprovalDecision;
  }): Promise<void> {
    this.#session(request.execution).resolveApproval(request.approval_id, request.decision);
  }

  async answerQuestion(request: {
    execution: ExecutionContext;
    question_id: string;
    answers: readonly QuestionAnswer[];
  }): Promise<void> {
    this.#session(request.execution).answerQuestion(request.question_id, request.answers);
  }

  async interrupt(request: { execution: ExecutionContext }): Promise<void> {
    this.#session(request.execution).interrupt();
  }

  async close(): Promise<void> {
    this.#closed = true;
    for (const session of this.#sessions.values()) session.shutdown();
    this.#sessions.clear();
  }

  #session(execution: ExecutionContext): MockSession {
    const session = this.#sessions.get(execution.execution_id);
    if (session === undefined) {
      throw new RuntimeActionError(
        'execution_unknown_to_runtime',
        'The mock runtime has no session for this execution. Sessions do not survive a restart.',
      );
    }
    return session;
  }
}

interface ActiveTurn {
  readonly id: string;
  readonly abort: AbortController;
  approval: { readonly id: string; readonly decide: (decision: ApprovalDecision) => void } | null;
  question: {
    readonly id: string;
    readonly answer: (answers: readonly QuestionAnswer[]) => void;
  } | null;
}

class MockSession {
  readonly nativeId: string;
  readonly #emitObservation: ObservationSink;
  readonly #clock: Clock;
  readonly #scheduler: Scheduler;
  /** Turns scripted for particular instructions, by their exact text. */
  readonly #instructions: ReadonlyMap<string, readonly ScenarioStep[]>;
  #sequence = 0;
  #turnCount = 0;
  #turn: ActiveTurn | null = null;
  #unreachable = false;

  constructor(
    nativeId: string,
    emit: ObservationSink,
    clock: Clock,
    scheduler: Scheduler,
    instructions: ReadonlyMap<string, readonly ScenarioStep[]>,
  ) {
    this.nativeId = nativeId;
    this.#emitObservation = emit;
    this.#clock = clock;
    this.#scheduler = scheduler;
    this.#instructions = instructions;
  }

  begin(steps: readonly ScenarioStep[], modelRef: string | null): void {
    this.#emit('runtime.execution.started', { native_id: this.nativeId });
    // The mock "uses" the model it was given, and reports it as a runtime reports its model.
    if (modelRef !== null) this.#emit('runtime.model.used', { model_ref: modelRef });
    this.#startTurn(steps);
  }

  instruct(text: string): void {
    this.#assertReachable();
    if (this.#turn !== null) {
      throw new RuntimeActionError(
        'turn_in_progress',
        'The mock runtime only accepts instructions between turns.',
      );
    }
    this.#startTurn(this.#instructions.get(text) ?? CONTINUATION_STEPS);
  }

  resolveApproval(approvalId: string, decision: ApprovalDecision): void {
    this.#assertReachable();
    const turn = this.#turn;
    const approval = turn?.approval ?? null;
    if (turn === null || approval === null || approval.id !== approvalId) {
      throw new RuntimeActionError(
        'approval_not_pending',
        `Approval ${approvalId} is not pending.`,
      );
    }
    // Taken at once, so a second decision in the same tick finds nothing pending.
    turn.approval = null;
    // The resolution is reported before the call returns, so it is journaled before the
    // command that caused it completes.
    this.#emit('runtime.approval.resolved', {
      approval_id: approvalId,
      decision: decision === 'approve' ? 'approved' : 'denied',
    });
    approval.decide(decision);
  }

  answerQuestion(questionId: string, answers: readonly QuestionAnswer[]): void {
    this.#assertReachable();
    const turn = this.#turn;
    const question = turn?.question ?? null;
    if (turn === null || question === null || question.id !== questionId) {
      throw new RuntimeActionError(
        'question_not_pending',
        `Question ${questionId} is not waiting for an answer.`,
      );
    }
    // Taken at once, so a second answer in the same tick finds nothing pending.
    turn.question = null;
    // Reported before the call returns, as approvals are, so it is journaled before the command
    // that caused it completes.
    this.#emit('runtime.question.resolved', { question_id: questionId, outcome: 'answered' });
    question.answer(answers);
  }

  interrupt(): void {
    this.#assertReachable();
    const turn = this.#turn;
    if (turn === null) {
      throw new RuntimeActionError('no_running_turn', 'There is no running turn to interrupt.');
    }
    this.#turn = null;
    turn.abort.abort();
    this.#emit('runtime.turn.interrupted', { turn_id: turn.id });
  }

  shutdown(): void {
    this.#turn?.abort.abort();
    this.#turn = null;
    this.#unreachable = true;
  }

  #assertReachable(): void {
    if (this.#unreachable) {
      throw new RuntimeActionError(
        'runtime_unreachable',
        'The mock runtime session is not reachable.',
      );
    }
  }

  #startTurn(steps: readonly ScenarioStep[]): void {
    this.#turnCount += 1;
    const turn: ActiveTurn = {
      id: `${this.nativeId}-turn-${this.#turnCount}`,
      abort: new AbortController(),
      approval: null,
      question: null,
    };
    this.#turn = turn;
    this.#emit('runtime.turn.started', { turn_id: turn.id });
    void this.#runTurn(turn, steps).catch((error: unknown) => {
      if (isAbortError(error) || this.#turn !== turn) return;
      this.#turn = null;
      this.#emit('runtime.turn.failed', {
        turn_id: turn.id,
        error: { code: 'mock_internal_error', message: String(error) },
      });
    });
  }

  async #runTurn(turn: ActiveTurn, steps: readonly ScenarioStep[]): Promise<void> {
    for (const step of steps) {
      await this.#scheduler.sleep(step.after_ms, turn.abort.signal);
      if (await this.#runStep(turn, step)) return;
    }
    // A script that never ends its turn leaves it running, like a stalled agent. The mock does
    // not invent an ending the scenario did not state.
  }

  /** Runs one step. Returns true when the turn is over. */
  async #runStep(turn: ActiveTurn, step: ScenarioStep): Promise<boolean> {
    if ('emit' in step || 'disconnect' in step) return this.#runBranchStep(turn, step);
    if ('await_answer' in step) {
      const { question_id, prompts, answerable, if_answered } = step.await_answer;
      const answers = await new Promise<readonly QuestionAnswer[]>((resolve, reject) => {
        turn.question = { id: question_id, answer: resolve };
        turn.abort.signal.addEventListener('abort', () => reject(abortError()), { once: true });
        // Reported whole, as a real runtime's is: the control plane fits it.
        this.#emit('runtime.question.asked', { question_id, prompts: [...prompts], answerable });
      });
      this.#emit('runtime.agent_message', { text: describeAnswers(prompts, answers) });
      for (const branchStep of if_answered) {
        await this.#scheduler.sleep(branchStep.after_ms, turn.abort.signal);
        if (await this.#runBranchStep(turn, branchStep)) return true;
      }
      return false;
    }

    const { approval_id, subject, if_approved, if_denied } = step.await_approval;
    const decision = await new Promise<ApprovalDecision>((resolve, reject) => {
      turn.approval = { id: approval_id, decide: resolve };
      turn.abort.signal.addEventListener('abort', () => reject(abortError()), { once: true });
      this.#emit('runtime.approval.requested', { approval_id, subject, complete: true });
    });
    for (const branchStep of decision === 'approve' ? if_approved : if_denied) {
      await this.#scheduler.sleep(branchStep.after_ms, turn.abort.signal);
      if (await this.#runBranchStep(turn, branchStep)) return true;
    }
    return false;
  }

  async #runBranchStep(turn: ActiveTurn, step: BranchStep): Promise<boolean> {
    if ('disconnect' in step) {
      this.#turn = null;
      this.#unreachable = true;
      turn.abort.abort();
      this.#emit('runtime.connection.lost', { reason: step.disconnect.reason });
      return true;
    }
    const emission = step.emit;
    switch (emission.type) {
      case 'runtime.turn.completed':
        this.#turn = null;
        this.#emit('runtime.turn.completed', { turn_id: turn.id });
        return true;
      case 'runtime.turn.failed':
        this.#turn = null;
        this.#emit('runtime.turn.failed', { turn_id: turn.id, error: emission.payload.error });
        return true;
      case 'runtime.agent_message':
        this.#emit(emission.type, emission.payload);
        return false;
      case 'runtime.tool.started':
        this.#emit(emission.type, emission.payload);
        return false;
      case 'runtime.tool.completed':
        this.#emit(emission.type, emission.payload);
        return false;
      case 'runtime.test_run.started':
        this.#emit(emission.type, emission.payload);
        return false;
      case 'runtime.test_run.completed':
        this.#emit(emission.type, emission.payload);
        return false;
      default: {
        const unhandled: never = emission;
        throw new Error(`unhandled scenario emission ${JSON.stringify(unhandled)}`);
      }
    }
  }

  #emit<T extends RuntimeEventType>(type: T, payload: EventOf<T>['payload']): void {
    this.#sequence += 1;
    const observation = {
      type,
      payload,
      occurred_at: this.#clock.now().toISOString(),
      native_event_id: `${this.nativeId}:${this.#sequence}`,
      sequence: this.#sequence,
      // Agent text is a claim; everything else is the mock's own (synthetic) state.
      provenance:
        type === 'runtime.agent_message'
          ? { epistemic: 'reported', native_type: `mock/${type}` }
          : { epistemic: 'observed', native_type: `mock/${type}` },
    } as RuntimeObservation;
    this.#emitObservation(observation);
  }
}

/** What the mock "heard", in the words an agent would report it, so a client can see it arrived. */
function describeAnswers(
  prompts: readonly { readonly key: string; readonly text: string }[],
  answers: readonly QuestionAnswer[],
): string {
  const parts = prompts.map((prompt) => {
    const answer = answers.find((each) => each.key === prompt.key);
    const given = [...(answer?.selected ?? []), ...(answer?.text ? [answer.text] : [])];
    return `"${prompt.text}": ${given.length > 0 ? given.join(', ') : 'no answer'}`;
  });
  return `Answers received (simulated): ${parts.join('; ')}.`.slice(0, 32000);
}

function abortError(): Error {
  const error = new Error('The turn was interrupted');
  error.name = 'AbortError';
  return error;
}
