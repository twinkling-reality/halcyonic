import Type, { type Static } from 'typebox';
import { RuntimeId, RuntimeKind } from './primitives.ts';

/**
 * What a runtime instance can actually do. Adapters report only verified behavior, and clients
 * offer only the actions whose capability is true. A missing capability is never emulated.
 *
 * Pause is deliberately absent: no verified runtime supports suspending a turn and resuming it
 * later (see docs/internal/architecture/INTEGRATIONS.md).
 */
export const RuntimeCapabilities = Type.Object(
  {
    /** Create an execution and run its first turn from an instruction. */
    start_execution: Type.Boolean(),
    /** Start a new turn on an execution whose previous turn has ended. */
    instruct_at_rest: Type.Boolean(),
    /** Deliver an instruction while a turn is still running. */
    instruct_while_running: Type.Boolean(),
    /** Answer an approval request raised by the runtime. */
    respond_to_approval: Type.Boolean(),
    /** Stop the running turn. The execution remains and may be instructed again. */
    interrupt: Type.Boolean(),
  },
  { additionalProperties: false },
);
export type RuntimeCapabilities = Static<typeof RuntimeCapabilities>;

export const RuntimeDescriptor = Type.Object(
  {
    runtime_id: RuntimeId,
    kind: RuntimeKind,
    display_name: Type.String({ minLength: 1, maxLength: 120 }),
    /**
     * True when the runtime produces fabricated activity for development, such as the mock
     * runtime. Clients must label synthetic work visibly.
     */
    synthetic: Type.Boolean(),
    capabilities: RuntimeCapabilities,
  },
  { additionalProperties: false },
);
export type RuntimeDescriptor = Static<typeof RuntimeDescriptor>;

/** The identity of the runtime an execution was started on, captured when it was created. */
export const RuntimeRef = Type.Object(
  {
    runtime_id: RuntimeId,
    kind: RuntimeKind,
    display_name: Type.String({ minLength: 1, maxLength: 120 }),
    synthetic: Type.Boolean(),
  },
  { additionalProperties: false },
);
export type RuntimeRef = Static<typeof RuntimeRef>;

/**
 * Runtime-specific start options. The control plane passes them through untouched and the
 * selected adapter validates them, so no vendor option ever becomes part of the core contract.
 */
export const RuntimeOptions = Type.Record(
  Type.String({ minLength: 1, maxLength: 64 }),
  Type.Unknown(),
  {
    maxProperties: 32,
  },
);
export type RuntimeOptions = Static<typeof RuntimeOptions>;
