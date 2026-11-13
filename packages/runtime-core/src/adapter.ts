import type {
  ApprovalDecision,
  ApprovalLevel,
  EventOf,
  ExecutionId,
  ProjectId,
  Provenance,
  QuestionAnswer,
  RuntimeCapabilities,
  RuntimeDescriptor,
  RuntimeEventType,
  RuntimeModel,
  RuntimeOptions,
  Timestamp,
  WorkstreamId,
} from '@halcyonic/contracts';

/** The Halcyonic identity of an execution, as an adapter sees it. */
export interface ExecutionContext {
  readonly execution_id: ExecutionId;
  readonly workstream_id: WorkstreamId;
  readonly project_id: ProjectId;
}

/**
 * A normalized fact an adapter observed about one execution. The control plane adds identity,
 * scope, source and ingestion time, then validates and journals it. A tool's title, an approval's
 * summary, a question's texts, a test run's label and summary, and error text (a turn's failure,
 * why the connection was lost) come whole, however long: the control plane takes the secrets it
 * holds out of them, which it finds only whole, then cuts them to the contract, a question with
 * `fitQuestion`, leaving one with anything cut unanswerable.
 */
export type RuntimeObservation = {
  [T in RuntimeEventType]: {
    readonly type: T;
    readonly occurred_at: Timestamp;
    /**
     * Identifier of the native record, used for deduplication. Unique for the runtime id for as
     * long as the journal lives, across control plane restarts, not only within one process: the
     * journal drops a record whose id it already holds.
     */
    readonly native_event_id: string | null;
    /** Native ordering within the execution when the runtime provides one, otherwise arrival order. */
    readonly sequence: number | null;
    readonly provenance: Provenance;
    readonly payload: EventOf<T>['payload'];
  };
}[RuntimeEventType];

/** Receives observations for one execution. It never throws back into the adapter. */
export type ObservationSink = (observation: RuntimeObservation) => void;

/**
 * A picture that goes with an instruction (A4), as the host read it once from the project's folder:
 * its kind from its first bytes, and the bytes themselves. The adapter hands these bytes on, never
 * the path, so the runtime is given exactly what the host read and recorded.
 */
export interface InstructionPicture {
  /** Relative to the project's folder, for words only. */
  readonly path: string;
  readonly media_type: 'image/png' | 'image/jpeg' | 'image/webp';
  readonly bytes: Uint8Array;
  readonly sha256: string;
}

/** An instruction for a running or resting execution, with any pictures that go with it. */
export interface InstructionRequest {
  readonly execution: ExecutionContext;
  readonly text: string;
  /** As on `StartExecutionRequest.pictures`. */
  readonly pictures?: readonly InstructionPicture[];
  /** Leave the runtime's plan mode first (PLAN); see `sendInstruction`. */
  readonly leave_plan?: true;
  /**
   * A new spending limit to carry on under: admission passes it only to an adapter that declares
   * `offers_spending_cap`, for a task whose last turn ended at its limit. The runtime counts it from
   * this instruction on.
   */
  readonly spending_cap_usd?: number;
}

export interface StartExecutionRequest {
  readonly execution: ExecutionContext;
  readonly instruction: string;
  /**
   * Pictures that go with the instruction, in the person's order. Passed only to an adapter whose
   * descriptor declares `takes_pictures`; absent or empty means none. An adapter refuses, with no
   * effect, a model its runtime says takes no picture, rather than send the words alone.
   */
  readonly pictures?: readonly InstructionPicture[];
  readonly options: RuntimeOptions;
  /**
   * A model from the adapter's own list, or null to leave the choice to the runtime. The adapter
   * checks it against a fresh list before anything runs and refuses one no longer listed with
   * `model_unavailable`.
   */
  readonly model_ref: string | null;
  /**
   * How often the agent stops to ask the person (ADR 0043); absent means `ask`. Admission passes
   * `never_ask` only to an adapter whose descriptor declares `offers_never_ask`. The adapter
   * observes the level the runtime confirmed and reports it in `runtime.execution.started`.
   */
  readonly approval_level?: ApprovalLevel;
  /**
   * True to start in the runtime's own plan mode. Admission passes it only to an adapter whose
   * descriptor declares `offers_plan`, and never with `never_ask`; the adapter reports in
   * `runtime.execution.started` whether the runtime confirmed it.
   */
  readonly plan?: boolean;
  /**
   * The task's spending limit, for an adapter that declares `offers_spending_cap`: the runtime
   * stops the task once its own estimate passes it, and the turn fails `spending_cap_reached`.
   */
  readonly spending_cap_usd?: number;
  /**
   * The project's location: the real path the host's directory policy returned for it when the
   * control plane dispatched the start, or null when the project has none or the runtime does not
   * use one. An adapter whose descriptor declares `uses_project_location` runs its agent here and
   * nowhere else, after asking the policy again (`confirmProjectLocation`).
   */
  readonly directory: string | null;
  /**
   * The project's secrets, as variables for the agent's commands (ADR 0057), given only to an
   * adapter whose descriptor declares `secret_environment`; absent or empty means none. The adapter
   * never logs, records or reports a value, and never lets one override a variable of its own.
   */
  readonly environment?: Readonly<Record<string, string>>;
  /** Where the adapter sends every observation about this execution, for as long as it can observe it. */
  readonly emit: ObservationSink;
}

/**
 * Re-attaches an execution an earlier control plane started, after a restart (ADR 0046). It carries
 * the bounds the execution started with, from the journal: the start's options and chosen model,
 * the approval level the runtime confirmed, whether the runtime reported it confined, and its
 * folder. The adapter re-attaches the session only with exactly those bounds, checking what the
 * runtime reports as it checks a start, and the folder against the directory policy again;
 * anything else it refuses, so the session is lost rather than re-attached otherwise.
 */
export interface ReattachRequest {
  readonly execution: ExecutionContext;
  /** The runtime's own session or thread id, as `runtime.execution.started` reported it. */
  readonly native_id: string;
  readonly options: RuntimeOptions;
  readonly model_ref: string | null;
  /**
   * The level the runtime confirmed at the start. Absent where none was recorded, which means
   * `ask`, as for a start: a session the runtime would resume at any wider level is refused.
   */
  readonly approval_level?: ApprovalLevel;
  /** Whether its start recorded the runtime's own plan mode; absent or false: not a plan. */
  readonly plan?: boolean;
  /**
   * For a task with a spending limit: what is left of it to hand the session's next launch (the limit
   * less the app's known estimates since it was set), or null where that is not known or nothing is
   * left, so the session launches nothing until it carries on under a new limit. Absent for none.
   */
  readonly spending_allowance_usd?: number | null;
  /**
   * Whether the runtime reported the execution confined to its project (S2). False also where it
   * was not recorded (executions from before S2), so it never permits less confinement than a new
   * start would get now: the adapter confines as a new start would, and refuses when that differs.
   */
  readonly confined?: boolean;
  readonly directory: string | null;
  /**
   * The project's secrets as they are now, as variables for the agent's commands (ADR 0057), given only to an
   * adapter whose descriptor declares `secret_environment`; absent or empty means none. The adapter
   * never logs, records or reports a value, and never lets one override a variable of its own.
   */
  readonly environment?: Readonly<Record<string, string>>;
  /**
   * The turn in flight as the last control plane ended, by the id its start reported, or null when
   * none was. The restart cut it, and the processes running it were stopped: once re-attached, the
   * adapter reports it ended as `runtime.turn.interrupted` with this id, marked inferred, which also
   * ends the approvals, questions and tool calls it left open.
   */
  readonly cut_turn: { readonly turn_id: string | null } | null;
  /**
   * The highest sequence the journal holds for the execution's observations, or null when none
   * carried one. An adapter that numbers its observations goes on above it: the journal drops an
   * observation whose sequence is not after the last.
   */
  readonly last_sequence: number | null;
  /** Where the adapter sends every observation about this execution from now on. */
  readonly emit: ObservationSink;
}

export interface StartExecutionResult {
  /** The runtime's own session or thread id. */
  readonly native_id: string | null;
}

export type OptionsValidation =
  | { readonly ok: true }
  | { readonly ok: false; readonly message: string };

/**
 * Why the host refused a directory, as the code a command carries when it is refused or fails:
 * `location_missing` when nothing is there or it is not a directory, `location_not_allowed` when
 * it lies outside every project root (also through `..` or a symbolic link), is not absolute, or
 * no root is configured.
 */
export type DirectoryRefusal = 'location_missing' | 'location_not_allowed';

export type DirectoryDecision =
  | { readonly ok: true /** The real path to use. */; readonly directory: string }
  | { readonly ok: false; readonly code: DirectoryRefusal; readonly message: string };

/**
 * Which directories agents may work in, decided by the host that runs the control plane, never
 * by a client. An adapter that runs agents in a directory accepts one only through this policy and
 * uses the real path it returns, so a client cannot point an agent anywhere on the machine.
 */
export type DirectoryPolicy = (path: string) => DirectoryDecision;

/**
 * Asks the host's directory policy about a project's location, a real path the host resolved when
 * the project was bound to it. A different real path now means a symbolic link has replaced part
 * of that path since: the folder the person chose is no longer there, and nothing is sent wherever
 * the link leads.
 */
export function checkProjectLocation(
  policy: DirectoryPolicy,
  directory: string,
): DirectoryDecision {
  let decision: DirectoryDecision;
  try {
    decision = policy(directory);
  } catch (error) {
    return {
      ok: false,
      code: 'location_not_allowed',
      message: `The directory policy could not decide on ${directory}: ${error instanceof Error ? error.message : String(error)}`,
    };
  }
  if (decision.ok && decision.directory !== directory) {
    return {
      ok: false,
      code: 'location_missing',
      message: `The project's folder is no longer at ${directory}: that path now leads to ${decision.directory}. Choose the project's folder again.`,
    };
  }
  return decision;
}

/**
 * Checks the project's location once more, right before an agent starts there, and returns it; a
 * refusal is thrown as the start's failure. Call it before anything is launched, so the failure
 * truthfully says nothing happened.
 */
export function confirmProjectLocation(policy: DirectoryPolicy, directory: string | null): string {
  if (directory === null) {
    throw new RuntimeActionError(
      'location_required',
      "This runtime works in the project's folder, and the project has none. Choose a folder for the project first.",
    );
  }
  const decision = checkProjectLocation(policy, directory);
  if (!decision.ok) throw new RuntimeActionError(decision.code, decision.message);
  return directory;
}

/**
 * The boundary between the control plane and an agent runtime. An adapter maps Halcyonic
 * commands onto the runtime's official control surface and maps native activity back into
 * normalized observations. Vendor objects never cross this boundary.
 *
 * Action methods are optional. Each must be present exactly when its capability is declared
 * (see `capabilityProblems`), and the control plane only calls it after admission has checked
 * the capability. Every action resolves when the runtime has confirmed it and rejects with
 * `RuntimeActionError` otherwise; its effects are reported separately as observations.
 */
export interface RuntimeAdapter {
  readonly descriptor: RuntimeDescriptor;
  /**
   * Checks runtime-specific start options, and the form of a chosen model's `model_ref`, before
   * anything is recorded. Whether the model is still listed is checked at the start itself. An
   * adapter that offers `never_ask` also refuses options that would contradict the chosen
   * `approvalLevel` (absent means `ask`).
   */
  validateStartOptions(
    options: RuntimeOptions,
    modelRef: string | null,
    approvalLevel?: ApprovalLevel,
  ): OptionsValidation;
  /**
   * Reads the models the runtime can use now from the runtime itself (ADR 0016). Present exactly
   * when the descriptor's `model_choice` is `listed`. Rejects with `RuntimeActionError` when the
   * runtime cannot list them. The result is never journaled.
   */
  listModels?(): Promise<readonly RuntimeModel[]>;
  /**
   * Whether a start in `directory` on this computer runs held to that folder: its commands write
   * no folder but it and the runtime's own home and temporary folders, so a task may work in a copy
   * of its own there (ADR 0045). The adapter answers from the confinement it applies at the start
   * itself. Absent, or false, the runtime is not held, and its tasks get no copy.
   */
  holdsFolder?(directory: string): boolean;
  /**
   * Whether a start of it on this computer can run with no sandbox of Halcyonic's around it, on
   * some folder, so its commands can read every file the person can: the data folder, every
   * project's secrets with it (ADR 0057). The adapter answers from the confinement it applies.
   * Absent means not known, which is said as can, never as inside.
   */
  runsOutsideSandbox?(): boolean;
  /**
   * Whether `name` is an environment variable the runtime, or the provider it calls, reads for
   * itself, so a project's secret may not take it (ADR 0057): each runtime names its own.
   */
  ownsEnvironmentName?(name: string): boolean;
  startExecution?(request: StartExecutionRequest): Promise<StartExecutionResult>;
  /**
   * `leave_plan` (PLAN), on the request: leave the runtime's plan mode in the same session first,
   * confirmed by the runtime and reported as `runtime.plan.left`, else refuse with nothing sent.
   * Admission passes it only for an execution that started as a plan and has not left it.
   */
  sendInstruction?(request: InstructionRequest): Promise<void>;
  respondToApproval?(request: {
    execution: ExecutionContext;
    approval_id: string;
    decision: ApprovalDecision;
    message: string | null;
  }): Promise<void>;
  /**
   * Carries the person's answers to a question the agent asked (ADR 0022), one per question, as
   * admission checked them against the question. Resolves once the runtime has taken them.
   */
  answerQuestion?(request: {
    execution: ExecutionContext;
    question_id: string;
    answers: readonly QuestionAnswer[];
  }): Promise<void>;
  interrupt?(request: { execution: ExecutionContext }): Promise<void>;
  /**
   * Re-attaches an execution an earlier control plane started (ADR 0046). Present exactly when the
   * descriptor declares `resumes_after_restart`. Resolves once the runtime has answered for the
   * session with the bounds it started with, after emitting `runtime.connection.restored` and, for
   * a turn the restart cut, its end. Rejects with `RuntimeActionError` when it cannot, and the
   * execution is then lost.
   */
  reattach?(request: ReattachRequest): Promise<void>;
  /**
   * Whether this process's runtime still holds the execution's session. An adapter that lets a session at rest go
   * (its server stopped after a quiet time) says false for it, and the control plane re-attaches it, as after a
   * restart, before its next action. Absent: every session this process started stays held.
   */
  hosts?(executionId: string): boolean;
  /** Releases native resources. Observations stop after this resolves. */
  close(): Promise<void>;
}

/**
 * A runtime refused or failed an action. `effect: 'unknown'` means the runtime may have acted
 * anyway, for example when a connection dropped mid-request. Its message comes whole, as an
 * observation's error text does: the control plane takes credentials out, then cuts it.
 */
export class RuntimeActionError extends Error {
  readonly code: string;
  readonly effect: 'none' | 'unknown';

  constructor(code: string, message: string, effect: 'none' | 'unknown' = 'none') {
    super(message);
    this.name = 'RuntimeActionError';
    this.code = code;
    this.effect = effect;
  }
}

const CAPABILITY_METHODS: Readonly<Record<keyof RuntimeCapabilities, keyof RuntimeAdapter>> = {
  start_execution: 'startExecution',
  instruct_at_rest: 'sendInstruction',
  instruct_while_running: 'sendInstruction',
  respond_to_approval: 'respondToApproval',
  answer_question: 'answerQuestion',
  interrupt: 'interrupt',
};

/**
 * Lists declared capabilities whose implementing method is missing, and a model choice declared
 * without its list. Empty when consistent.
 */
export function capabilityProblems(adapter: RuntimeAdapter): string[] {
  const problems: string[] = [];
  for (const [capability, method] of Object.entries(CAPABILITY_METHODS)) {
    const declared = adapter.descriptor.capabilities[capability as keyof RuntimeCapabilities];
    if (declared && typeof adapter[method] !== 'function') {
      problems.push(`capability ${capability} is declared but ${method} is not implemented`);
    }
  }
  if (adapter.descriptor.model_choice === 'listed' && typeof adapter.listModels !== 'function') {
    problems.push('model_choice listed is declared but listModels is not implemented');
  }
  const resumes = adapter.descriptor.resumes_after_restart === true;
  if (resumes !== (typeof adapter.reattach === 'function')) {
    problems.push(
      resumes
        ? 'resumes_after_restart is declared but reattach is not implemented'
        : 'reattach is implemented but resumes_after_restart is not declared',
    );
  }
  return problems;
}

/**
 * The code a turn or start fails with when the model's provider refused the credential the runtime
 * was given (a key it does not know, or one revoked): a host reads it as "the key was refused", so
 * the runtime's hosted models are not offered as ready until another key is given.
 */
export const CREDENTIAL_REFUSED = 'credential_refused';
