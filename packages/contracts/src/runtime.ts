import Type, { type Static } from 'typebox';
import { Nullable, RuntimeId, RuntimeKind, Text } from './primitives.ts';

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

/**
 * Whether a person can choose the runtime's model. `listed`: the runtime lists the models it can
 * use now (`GET /api/runtimes/:runtime_id/models`), and a start may name one by its `model_ref`.
 * `none`: the runtime offers no choice, and a start naming a model is refused.
 */
export const ModelChoice = Type.Union([Type.Literal('none'), Type.Literal('listed')]);
export type ModelChoice = Static<typeof ModelChoice>;

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
    model_choice: ModelChoice,
  },
  { additionalProperties: false },
);
export type RuntimeDescriptor = Static<typeof RuntimeDescriptor>;

/**
 * A model as the runtime that lists it names it. Opaque: the adapter chooses it, and a client
 * sends it back unchanged in `execution.start` and never derives meaning from its format.
 */
export const ModelRef = Type.String({ minLength: 1, maxLength: 256, pattern: '^\\S+$' });

/**
 * Where a model runs, decided from where the runtime sends its requests, never from the model's
 * name: `this_mac` when the runtime reaches it on this machine's loopback, `remote` when it
 * reaches it anywhere else, `unknown` when the runtime does not say.
 */
export const ModelServed = Type.Union([
  Type.Literal('this_mac'),
  Type.Literal('remote'),
  Type.Literal('unknown'),
]);
export type ModelServed = Static<typeof ModelServed>;

/** Whether the runtime's list states that the model calls tools, which an agent needs. */
export const ModelToolCalling = Type.Union([
  Type.Literal('declared'),
  Type.Literal('not_declared'),
  Type.Literal('unknown'),
]);
export type ModelToolCalling = Static<typeof ModelToolCalling>;

/** One model a runtime can use now, as its own list reports it. Never journaled. */
export const RuntimeModel = Type.Object(
  {
    model_ref: ModelRef,
    /** Names the model and what serves it, so a local model named after a hosted one never reads as it. */
    display_name: Text(200),
    served: ModelServed,
    tool_calling: ModelToolCalling,
    /** The context the runtime assumes for the model, in tokens; the server may load less. */
    context_tokens: Nullable(Type.Integer({ minimum: 1 })),
  },
  { additionalProperties: false },
);
export type RuntimeModel = Static<typeof RuntimeModel>;

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
