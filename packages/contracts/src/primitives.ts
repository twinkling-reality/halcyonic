import Type, { type Static, type TSchema } from 'typebox';

/**
 * Wire contracts use explicit `null` rather than absent keys, so every consumer (TypeScript,
 * C#, fixtures) sees a single shape for each object.
 */
export function Nullable<T extends TSchema>(schema: T) {
  return Type.Union([schema, Type.Null()]);
}

const UUID_PATTERN = '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$';
/** RFC 9562 version 7 (time ordered) with the standard variant bits, lowercase. */
const UUID_V7_PATTERN = '^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$';

/** Any lowercase UUID. Used for identifiers that clients generate, such as command ids. */
export const Uuid = Type.String({ pattern: UUID_PATTERN });
export type Uuid = Static<typeof Uuid>;

export type ProjectId = string & { readonly __brand: 'ProjectId' };
export type WorkstreamId = string & { readonly __brand: 'WorkstreamId' };
export type ExecutionId = string & { readonly __brand: 'ExecutionId' };
export type EventId = string & { readonly __brand: 'EventId' };
export type CommandId = string & { readonly __brand: 'CommandId' };
export type JournalId = string & { readonly __brand: 'JournalId' };
export type RuntimeId = string & { readonly __brand: 'RuntimeId' };

/** Identifiers Halcyonic generates are UUIDv7 so they sort by creation time. */
export const ProjectId = Type.Unsafe<ProjectId>(Type.String({ pattern: UUID_V7_PATTERN }));
export const WorkstreamId = Type.Unsafe<WorkstreamId>(Type.String({ pattern: UUID_V7_PATTERN }));
export const ExecutionId = Type.Unsafe<ExecutionId>(Type.String({ pattern: UUID_V7_PATTERN }));
export const EventId = Type.Unsafe<EventId>(Type.String({ pattern: UUID_V7_PATTERN }));
export const JournalId = Type.Unsafe<JournalId>(Type.String({ pattern: UUID_V7_PATTERN }));
/** Commands are identified by the client that issues them, which makes retries idempotent. */
export const CommandId = Type.Unsafe<CommandId>(Type.String({ pattern: UUID_PATTERN }));

/** A configured runtime instance, for example `mock` or `opencode-local`. */
export const RuntimeId = Type.Unsafe<RuntimeId>(Type.String({ pattern: '^[a-z][a-z0-9-]{0,62}$' }));

/** The adapter type behind a runtime instance, for example `mock` or `opencode`. */
export const RuntimeKind = Type.String({ pattern: '^[a-z][a-z0-9-]{0,62}$' });

/**
 * An identifier that originates in a runtime (session, turn, approval, tool call). Halcyonic
 * treats it as opaque and never derives meaning from its format.
 */
export const NativeId = Type.String({ minLength: 1, maxLength: 512 });

/**
 * UTC instant in the canonical form produced by `Date.prototype.toISOString()`:
 * exactly three fractional digits and a `Z` suffix. One form keeps ordering and equality
 * checks on the wire unambiguous.
 */
export const Timestamp = Type.String({
  format: 'date-time',
  pattern: '^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{3}Z$',
});
export type Timestamp = Static<typeof Timestamp>;

/** A journal position. Positions increase monotonically but may contain gaps. */
export const Position = Type.Integer({ minimum: 0 });

/** Machine-readable error code in snake_case, with a human-readable message. */
export const ErrorInfo = Type.Object(
  {
    code: Type.String({ pattern: '^[a-z][a-z0-9_]{0,63}$' }),
    message: Type.String({ minLength: 1, maxLength: 2000 }),
  },
  { additionalProperties: false },
);
export type ErrorInfo = Static<typeof ErrorInfo>;

/** Free text that must contain at least one non-whitespace character. */
export function Text(maxLength: number) {
  return Type.String({ minLength: 1, maxLength, pattern: '\\S' });
}

/** Self-declared identity of a client. It is recorded for audit but never trusted for authorization. */
export const ClientInfo = Type.Object(
  {
    name: Type.String({ minLength: 1, maxLength: 64 }),
    version: Nullable(Type.String({ minLength: 1, maxLength: 64 })),
    device_label: Nullable(Type.String({ minLength: 1, maxLength: 120 })),
  },
  { additionalProperties: false },
);
export type ClientInfo = Static<typeof ClientInfo>;
