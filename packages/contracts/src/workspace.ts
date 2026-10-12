import Type, { type Static } from 'typebox';
import {
  ExecutionId,
  JournalId,
  Nullable,
  ProjectId,
  Uuid,
  WorkspaceRequestId,
  WorkstreamId,
} from './primitives.ts';

const strict = { additionalProperties: false } as const;

/** App-private continuation, never a command or a server-side source of truth. */
export const WORKSPACE_CONTINUATION_MAX_BYTES = 256 * 1024;

export const WorkspaceSection = Type.Union([
  Type.Literal('waiting'),
  Type.Literal('activity'),
  Type.Literal('changes'),
  Type.Literal('checks'),
]);
export type WorkspaceSection = Static<typeof WorkspaceSection>;

/** The selected view is independent of either retained ordinary draft. */
export const WorkspaceSurface = Type.Union([Type.Literal('task'), Type.Literal('request')]);
export type WorkspaceSurface = Static<typeof WorkspaceSurface>;

/** Ordinary unsent instruction only. Reading and confirmation authority are never persisted. */
export const WorkspaceInstructionDraft = Type.Object(
  {
    text: Type.String({ maxLength: 32000 }),
    spoken: Type.Boolean(),
    part: Type.Integer({ minimum: 0, maximum: 100000 }),
  },
  strict,
);
export type WorkspaceInstructionDraft = Static<typeof WorkspaceInstructionDraft>;

/** An ordinary request editor and explicit host request reference, never restored action authority. */
export const WorkspaceRequestContinuation = Type.Object(
  {
    request_id: Nullable(WorkspaceRequestId),
    text: Type.String({ maxLength: 32000 }),
    spoken: Type.Boolean(),
    part: Type.Integer({ minimum: 0, maximum: 100000 }),
  },
  strict,
);
export type WorkspaceRequestContinuation = Static<typeof WorkspaceRequestContinuation>;

/**
 * Selection exchanged by the immersive workspace and native panel through app-private storage.
 * Consumers must additionally verify connection identity, journal and entity relationships against
 * a fresh projection. This carries no approval, answer, read proof, callback or automatic action.
 */
export const WorkspaceContinuation = Type.Object(
  {
    version: Type.Literal(1),
    handoff_id: Uuid,
    connection_key: Type.String({ pattern: '^[0-9a-f]{64}$' }),
    journal_id: Nullable(JournalId),
    project_id: Nullable(ProjectId),
    workstream_id: Nullable(WorkstreamId),
    execution_id: Nullable(ExecutionId),
    surface: WorkspaceSurface,
    section: WorkspaceSection,
    page: Type.Integer({ minimum: 0, maximum: 100000 }),
    instruction: Nullable(WorkspaceInstructionDraft),
    // Legacy app-private v1 readers migrate this field's absence to null before validation.
    request: Nullable(WorkspaceRequestContinuation),
  },
  strict,
);
export type WorkspaceContinuation = Static<typeof WorkspaceContinuation>;

/** Aggregate UTF-8 storage bound; filling it must preserve the previous saved views. */
export const WORKSPACE_VIEW_STORE_MAX_BYTES = 4 * 1024 * 1024;

/**
 * Local Unity view storage, separate from the selected native-panel handoff. Consumers must reject
 * duplicate handoff IDs and a selected ID absent from views, and enforce the aggregate byte bound.
 * Old-run drafts may be read for recovery but can only be imported against their exact binding.
 */
export const WorkspaceViewStore = Type.Object(
  {
    version: Type.Literal(1),
    selected: Nullable(Uuid),
    views: Type.Array(WorkspaceContinuation),
  },
  strict,
);
export type WorkspaceViewStore = Static<typeof WorkspaceViewStore>;
