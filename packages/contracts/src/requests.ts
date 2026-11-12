import Type, { type Static } from 'typebox';
import { ActionReviewSaid } from './actions.ts';
import { ReviewDigest } from './checkpoints.ts';
import { CommandType, WorkspaceRequestPlaces } from './commands.ts';
import {
  CommandId,
  ErrorInfo,
  ExecutionId,
  JournalId,
  NativeId,
  Nullable,
  Position,
  ProjectId,
  Text,
  Timestamp,
  WorkspaceRequestId,
  WorkstreamId,
} from './primitives.ts';
import { CommandStatus, ExecutionStatus } from './statuses.ts';

const strict = { additionalProperties: false } as const;

export { WorkspaceRequestId } from './primitives.ts';
export const WorkspaceRequestStatus = Type.Enum([
  'running',
  'waiting_for_human',
  'waiting_for_runtime',
  'responded',
  'stopped',
  'interrupted',
  'failed',
]);
export const WorkspaceRequestStepStatus = Type.Enum([
  'prepared',
  'running',
  'waiting',
  'confirmed',
  'rejected',
  'failed',
  'unknown',
]);
/**
 * Where the assistant's model ran: `this_mac` for the local model, `remote` for a hosted provider the
 * product declares, used only when the person stored that provider's key (ADR 0030, 2026-11-12).
 */
export const AssistantServed = Type.Union([Type.Literal('this_mac'), Type.Literal('remote')]);
export type AssistantServed = Static<typeof AssistantServed>;
export const WorkspaceRequestReply = Type.Object(
  {
    text: Text(4000),
    epistemic: Type.Literal('reported'),
    model: Text(200),
    /** `this_mac` for the local model; `remote` for a hosted provider the product declares (ADR 0030, amendment of 2026-11-12). */
    served: AssistantServed,
    /** Where a `remote` reply ran, by the name its provider declares; absent on this Mac. */
    provider: Type.Optional(Text(80)),
  },
  strict,
);
/**
 * A command the assistant proposed from what can be done here (ADR 0060). `confirm` is the command's own: `none` and
 * `undo` are done at once, `review` waits for the person's one press, which carries `review` back. `what_happens` is
 * the host's own words, never the model's.
 */
export const WorkspaceRequestProposal = Type.Object(
  {
    command_type: CommandType,
    confirm: Type.Enum(['none', 'undo', 'review']),
    what_happens: Text(2000),
    review: Nullable(ReviewDigest),
    /**
     * The words given that go out with the command, each by the host's label for its field and exactly as it will be
     * sent (AUDIT2's N2): the card shows them whole, as the proposer's, for every proposal, reviewed or not.
     */
    said: Type.Array(ActionReviewSaid, { maxItems: 12 }),
  },
  strict,
);
export type WorkspaceRequestProposal = Static<typeof WorkspaceRequestProposal>;
export const WorkspaceRequestStep = Type.Object(
  {
    step_id: WorkspaceRequestId,
    tool_name: Text(64),
    status: WorkspaceRequestStepStatus,
    command_id: Nullable(CommandId),
    command_status: Nullable(CommandStatus),
    observation_position: Position,
    active_turn_id: Nullable(NativeId),
    had_active_turn: Type.Boolean(),
    observation_after: Position,
    observed_turn_id: Nullable(NativeId),
    runtime_status: Nullable(ExecutionStatus),
    /** Host-authored receipt/availability only; model text belongs in reported reply. */
    summary: Text(2000),
    /** Only on a `propose_command` step: what was proposed (ADR 0060). */
    proposal: Type.Optional(WorkspaceRequestProposal),
    /** Only on a `go_to` step: the index of the client's place the assistant picked; the client goes there. */
    place: Type.Optional(Type.Integer({ minimum: 0, maximum: 15 })),
  },
  strict,
);
/** Counts of the person's tasks by where they stand, as the host lists them: no names, no words. */
export const WorkspaceRequestTaskCounts = Type.Object(
  {
    total: Type.Integer({ minimum: 0 }),
    running: Type.Integer({ minimum: 0 }),
    waiting_on_you: Type.Integer({ minimum: 0 }),
    failed: Type.Integer({ minimum: 0 }),
  },
  strict,
);
export type WorkspaceRequestTaskCounts = Static<typeof WorkspaceRequestTaskCounts>;

/**
 * What the computer itself saw, recorded only when a request ends (interrupted, failed or stopped) holding no reply
 * at all, never beside one, an earlier one included, so the person is never left with nothing (the lead, 2026-11-11). Facts, never sentences: the clients
 * choose the words, and draw them apart from the assistant's reply, as the computer's own. No model text, no path,
 * no agent words. For a task: its execution's state, its turns, the files the Mac saw its turns change, the
 * check of this task's own folder (its own copy's, or the project folder's where it works there; null for another
 * task's), and whether the person's words reached the agent (`sent`). For a project: its tasks by
 * where they stand, and whether its latest check failed. For all work: the projects and the tasks across them.
 */
export const WorkspaceRequestObserved = Type.Object(
  {
    task: Nullable(
      Type.Object(
        {
          status: ExecutionStatus,
          turns: Type.Integer({ minimum: 0 }),
          files: Nullable(
            Type.Object(
              {
                created: Type.Integer({ minimum: 0 }),
                changed: Type.Integer({ minimum: 0 }),
                deleted: Type.Integer({ minimum: 0 }),
              },
              strict,
            ),
          ),
          check: Nullable(
            Type.Object(
              {
                passed: Type.Boolean(),
                files: Type.Union([
                  Type.Literal('unknown'),
                  Type.Literal('current'),
                  Type.Literal('changed'),
                ]),
              },
              strict,
            ),
          ),
          sent: Type.Boolean(),
        },
        strict,
      ),
    ),
    tasks: Nullable(WorkspaceRequestTaskCounts),
    projects: Nullable(
      Type.Object(
        { total: Type.Integer({ minimum: 0 }), checks_failed: Type.Integer({ minimum: 0 }) },
        strict,
      ),
    ),
  },
  strict,
);
export type WorkspaceRequestObserved = Static<typeof WorkspaceRequestObserved>;

export const WorkspaceRequestView = Type.Object(
  {
    request_id: WorkspaceRequestId,
    journal_id: JournalId,
    /** Null, with the task fields, for a request about all of the person's work, read-only. */
    project_id: Nullable(ProjectId),
    /** Null, with `execution_id`, for a request about the whole project, which is read-only. */
    workstream_id: Nullable(WorkstreamId),
    execution_id: Nullable(ExecutionId),
    original_text: Text(32000),
    /** The places the client said it can go (ADR 0060), in its own words, by index. */
    places: Type.Optional(WorkspaceRequestPlaces),
    status: WorkspaceRequestStatus,
    reply: Nullable(WorkspaceRequestReply),
    /**
     * What the computer saw, where the request ended holding no reply at all; absent otherwise, and never beside a
     * reply. The computer's own facts, never the assistant's.
     */
    observed: Type.Optional(WorkspaceRequestObserved),
    steps: Type.Array(WorkspaceRequestStep, { maxItems: 64 }),
    /** Earlier receipts remain in the journal; the view holds the latest 64. */
    earlier_steps: Type.Integer({ minimum: 0 }),
    created_at: Timestamp,
    updated_at: Timestamp,
  },
  strict,
);
export type WorkspaceRequestView = Static<typeof WorkspaceRequestView>;
export type WorkspaceRequestStep = Static<typeof WorkspaceRequestStep>;
export type WorkspaceRequestReply = Static<typeof WorkspaceRequestReply>;
export type WorkspaceRequestStatus = Static<typeof WorkspaceRequestStatus>;
export const WorkspaceRequestResponse = Type.Object({ request: WorkspaceRequestView }, strict);
export type WorkspaceRequestResponse = Static<typeof WorkspaceRequestResponse>;

/** Capability read, never inference or permission to silently choose another model. */
export const WorkspaceAgentStatus = Type.Union([
  Type.Object(
    {
      availability: Type.Literal('available'),
      model: Text(200),
      served: AssistantServed,
      /** The declared provider's name when `remote`; absent on this Mac. */
      provider: Type.Optional(Text(80)),
    },
    strict,
  ),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
]);
export type WorkspaceAgentStatus = Static<typeof WorkspaceAgentStatus>;
