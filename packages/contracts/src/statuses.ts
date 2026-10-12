import Type, { type Static } from 'typebox';

const EXECUTION_STATUS_LITERALS = [
  /** Accepted by the control plane; the runtime has not started a turn yet. */
  Type.Literal('starting'),
  /** A turn is in progress. */
  Type.Literal('running'),
  /** A turn is in progress and a test run reported by the runtime is in progress. */
  Type.Literal('verifying'),
  /** The runtime is blocked on a human decision, such as an approval. */
  Type.Literal('waiting_for_human'),
  /** The last turn ended successfully. This says nothing about whether the work is correct. */
  Type.Literal('completed'),
  /** The last turn failed, or the execution could not be started. */
  Type.Literal('failed'),
  /** The last turn was stopped on request. */
  Type.Literal('interrupted'),
  /** Halcyonic cannot currently observe the execution. Shown instead of a guess. */
  Type.Literal('unknown'),
] as const;

export const ExecutionStatus = Type.Union([...EXECUTION_STATUS_LITERALS]);
export type ExecutionStatus = Static<typeof ExecutionStatus>;

/** A workstream reports `created` until its first execution, then its current execution's status. */
export const WorkstreamStatus = Type.Union([Type.Literal('created'), ...EXECUTION_STATUS_LITERALS]);
export type WorkstreamStatus = Static<typeof WorkstreamStatus>;

export const CommandStatus = Type.Union([
  Type.Literal('accepted'),
  Type.Literal('rejected'),
  Type.Literal('completed'),
  Type.Literal('failed'),
]);
export type CommandStatus = Static<typeof CommandStatus>;
