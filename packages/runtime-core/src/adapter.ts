import type {
  ApprovalDecision,
  EventOf,
  ExecutionId,
  ProjectId,
  Provenance,
  RuntimeCapabilities,
  RuntimeDescriptor,
  RuntimeEventType,
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
 * scope, source and ingestion time, then validates and journals it.
 */
export type RuntimeObservation = {
  [T in RuntimeEventType]: {
    readonly type: T;
    readonly occurred_at: Timestamp;
    /** Identifier of the native record, unique within the runtime instance. Used for deduplication. */
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
  /** Checks runtime-specific start options before anything is recorded. */
  validateStartOptions(options: RuntimeOptions): OptionsValidation;
  startExecution?(request: StartExecutionRequest): Promise<StartExecutionResult>;
  sendInstruction?(request: { execution: ExecutionContext; text: string }): Promise<void>;
  respondToApproval?(request: {
    execution: ExecutionContext;
    approval_id: string;
    decision: ApprovalDecision;
    message: string | null;
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
  interrupt: 'interrupt',
};

/** Lists declared capabilities whose implementing method is missing. Empty when consistent. */
export function capabilityProblems(adapter: RuntimeAdapter): string[] {
  const problems: string[] = [];
  for (const [capability, method] of Object.entries(CAPABILITY_METHODS)) {
    const declared = adapter.descriptor.capabilities[capability as keyof RuntimeCapabilities];
    if (declared && typeof adapter[method] !== 'function') {
      problems.push(`capability ${capability} is declared but ${method} is not implemented`);
    }
  }
  return problems;
}
