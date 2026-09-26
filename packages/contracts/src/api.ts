import Type, { type Static } from 'typebox';
import { StoredEvent } from './events.ts';
import { ExecutionId, Position, WorkstreamId } from './primitives.ts';
import { RuntimeDescriptor } from './runtime.ts';
import { UnderstandingResult } from './understanding.ts';
import { CommandView, JournalInfo, ProjectView, WorkstreamView } from './views.ts';

const strict = { additionalProperties: false } as const;

export const HealthResponse = Type.Object({ status: Type.Literal('ok') }, strict);
export type HealthResponse = Static<typeof HealthResponse>;

export const ProjectsResponse = Type.Object(
  { journal: JournalInfo, position: Position, projects: Type.Array(ProjectView) },
  strict,
);
export type ProjectsResponse = Static<typeof ProjectsResponse>;

export const WorkstreamsResponse = Type.Object(
  { journal: JournalInfo, position: Position, workstreams: Type.Array(WorkstreamView) },
  strict,
);
export type WorkstreamsResponse = Static<typeof WorkstreamsResponse>;

export const RuntimesResponse = Type.Object({ runtimes: Type.Array(RuntimeDescriptor) }, strict);
export type RuntimesResponse = Static<typeof RuntimesResponse>;

export const EventsQuery = Type.Object(
  {
    after: Position,
    limit: Type.Integer({ minimum: 1, maximum: 1000 }),
    workstream_id: Type.Union([WorkstreamId, Type.Null()]),
  },
  strict,
);
export type EventsQuery = Static<typeof EventsQuery>;

export const EventsResponse = Type.Object(
  { journal: JournalInfo, head: Position, events: Type.Array(StoredEvent) },
  strict,
);
export type EventsResponse = Static<typeof EventsResponse>;

/**
 * Result of submitting a command. `accepted` means admitted and dispatched, not that the action
 * succeeded: completion arrives later as `command.completed` or `command.failed`.
 */
export const CommandSubmissionResponse = Type.Object(
  {
    disposition: Type.Union([
      Type.Literal('accepted'),
      Type.Literal('rejected'),
      Type.Literal('duplicate'),
    ]),
    command: CommandView,
  },
  strict,
);
export type CommandSubmissionResponse = Static<typeof CommandSubmissionResponse>;

/**
 * What the understanding source (Salidium) says about one execution, read through on request and
 * never journaled (ADR 0010).
 */
export const UnderstandingResponse = Type.Object(
  { execution_id: ExecutionId, result: UnderstandingResult },
  strict,
);
export type UnderstandingResponse = Static<typeof UnderstandingResponse>;

export const ValidationIssueSchema = Type.Object(
  { path: Type.String(), message: Type.String() },
  strict,
);

export const ErrorResponse = Type.Object(
  {
    error: Type.Object(
      {
        code: Type.String({ pattern: '^[a-z][a-z0-9_]{0,63}$' }),
        message: Type.String({ minLength: 1 }),
        issues: Type.Array(ValidationIssueSchema),
      },
      strict,
    ),
  },
  strict,
);
export type ErrorResponse = Static<typeof ErrorResponse>;
