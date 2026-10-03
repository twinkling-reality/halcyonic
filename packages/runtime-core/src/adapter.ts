import type {
  ApprovalDecision,
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
 * summary, a question's texts and a test run's label and summary come whole, however long: the
 * control plane takes the secrets it holds out of them, which it finds only whole, then cuts them
 * to the contract, a question with `fitQuestion`, leaving one with anything cut unanswerable.
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

export interface StartExecutionRequest {
  readonly execution: ExecutionContext;
  readonly instruction: string;
  readonly options: RuntimeOptions;
  /**
   * A model from the adapter's own list, or null to leave the choice to the runtime. The adapter
   * checks it against a fresh list before anything runs and refuses one no longer listed with
   * `model_unavailable`.
   */
  readonly model_ref: string | null;
  /**
   * The project's location: the real path the host's directory policy returned for it when the
   * control plane dispatched the start, or null when the project has none or the runtime does not
   * use one. An adapter whose descriptor declares `uses_project_location` runs its agent here and
   * nowhere else, after asking the policy again (`confirmProjectLocation`).
   */
  readonly directory: string | null;
  /** Where the adapter sends every observation about this execution, for as long as it can observe it. */
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
   * anything is recorded. Whether the model is still listed is checked at the start itself.
   */
  validateStartOptions(options: RuntimeOptions, modelRef: string | null): OptionsValidation;
  /**
   * Reads the models the runtime can use now from the runtime itself (ADR 0016). Present exactly
   * when the descriptor's `model_choice` is `listed`. Rejects with `RuntimeActionError` when the
   * runtime cannot list them. The result is never journaled.
   */
  listModels?(): Promise<readonly RuntimeModel[]>;
  startExecution?(request: StartExecutionRequest): Promise<StartExecutionResult>;
  sendInstruction?(request: { execution: ExecutionContext; text: string }): Promise<void>;
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
  /** Releases native resources. Observations stop after this resolves. */
  close(): Promise<void>;
}

/**
 * A runtime refused or failed an action. `effect: 'unknown'` means the runtime may have acted
 * anyway, for example when a connection dropped mid-request.
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
  return problems;
}
