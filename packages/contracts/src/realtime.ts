import Type, { type Static } from 'typebox';
import { ValidationIssueSchema } from './api.ts';
import { CommandEnvelope } from './commands.ts';
import { EventEnvelope } from './events.ts';
import { ClientInfo, CommandId, JournalId, Nullable, Position, Timestamp } from './primitives.ts';
import { REALTIME_PROTOCOL_VERSION } from './versions.ts';
import { CommandView, EntityChanges, JournalInfo, Snapshot } from './views.ts';

const strict = { additionalProperties: false } as const;

// Client to server ---------------------------------------------------------------------------

/** The last journal position the client applied, for skipping the snapshot when nothing changed. */
export const ResumeCursor = Type.Object({ journal_id: JournalId, position: Position }, strict);
export type ResumeCursor = Static<typeof ResumeCursor>;

export const HelloMessage = Type.Object(
  {
    type: Type.Literal('hello'),
    protocol: Type.Literal(REALTIME_PROTOCOL_VERSION),
    client: ClientInfo,
    resume: Nullable(ResumeCursor),
  },
  strict,
);

export const CommandMessage = Type.Object(
  { type: Type.Literal('command'), command: CommandEnvelope },
  strict,
);

export const PingMessage = Type.Object(
  { type: Type.Literal('ping'), nonce: Nullable(Type.String({ minLength: 1, maxLength: 64 })) },
  strict,
);

export const CLIENT_MESSAGE_VARIANTS = [HelloMessage, CommandMessage, PingMessage] as const;
export const ClientMessage = Type.Union([...CLIENT_MESSAGE_VARIANTS]);
export type ClientMessage = Static<typeof ClientMessage>;

// Server to client ---------------------------------------------------------------------------

export const WelcomeMessage = Type.Object(
  {
    type: Type.Literal('welcome'),
    protocol: Type.Literal(REALTIME_PROTOCOL_VERSION),
    journal: JournalInfo,
    head: Position,
    /** True when the client's cursor was current, so no snapshot follows. */
    resumed: Type.Boolean(),
    server_time: Timestamp,
  },
  strict,
);

export const SnapshotMessage = Type.Object(
  { type: Type.Literal('snapshot'), snapshot: Snapshot },
  strict,
);

export const EventMessage = Type.Object(
  {
    type: Type.Literal('event'),
    position: Type.Integer({ minimum: 1 }),
    event: EventEnvelope,
    changes: EntityChanges,
  },
  strict,
);

export const CommandAckMessage = Type.Object(
  {
    type: Type.Literal('command_ack'),
    command_id: CommandId,
    /**
     * `accepted` means admitted, not done. `conflict` means the id was already used for a
     * different command, which is a client bug.
     */
    disposition: Type.Union([
      Type.Literal('accepted'),
      Type.Literal('rejected'),
      Type.Literal('duplicate'),
      Type.Literal('conflict'),
    ]),
    command: Nullable(CommandView),
  },
  strict,
);

export const PongMessage = Type.Object(
  { type: Type.Literal('pong'), nonce: Nullable(Type.String({ minLength: 1, maxLength: 64 })) },
  strict,
);

export const ErrorMessage = Type.Object(
  {
    type: Type.Literal('error'),
    error: Type.Object(
      {
        code: Type.String({ pattern: '^[a-z][a-z0-9_]{0,63}$' }),
        message: Type.String({ minLength: 1 }),
        issues: Type.Array(ValidationIssueSchema),
      },
      strict,
    ),
    /** When true the server closes the connection after sending this message. */
    fatal: Type.Boolean(),
  },
  strict,
);

export const SERVER_MESSAGE_VARIANTS = [
  WelcomeMessage,
  SnapshotMessage,
  EventMessage,
  CommandAckMessage,
  PongMessage,
  ErrorMessage,
] as const;
export const ServerMessage = Type.Union([...SERVER_MESSAGE_VARIANTS]);
export type ServerMessage = Static<typeof ServerMessage>;
