import Type, { type Static } from 'typebox';
import {
  CheckpointFailure,
  CheckpointId,
  CheckpointReason,
  CheckpointView,
} from './checkpoints.ts';
import {
  ErrorInfo,
  EventId,
  ExecutionId,
  JournalId,
  Nullable,
  ProjectId,
  Timestamp,
  WorkstreamId,
} from './primitives.ts';

/**
 * A project's history (ADR 0036, the parity map's gap 2): its versions, the ones that could not be
 * kept, and its restores, newest first, read in pages. What changed in a version is read with the
 * project reads, from its `previous` version or to the folder as it is now.
 */

const strict = { additionalProperties: false } as const;
const Count = Type.Integer({ minimum: 0, maximum: Number.MAX_SAFE_INTEGER });

/** The entries one page gives when the query names no limit, and the most it may name. */
export const HISTORY_PAGE = 20;
export const HISTORY_PAGE_MAX = 50;

/**
 * `limit` is a query string's digits, 1 to `HISTORY_PAGE_MAX`; `before` the `entry_id` of an entry
 * the client holds, so the page gives only older ones.
 */
export const ProjectHistoryQuery = Type.Object(
  {
    journal_id: JournalId,
    limit: Type.Optional(Type.String({ pattern: '^(?:[1-9]|[1-4][0-9]|50)$' })),
    before: Type.Optional(EventId),
  },
  strict,
);
export type ProjectHistoryQuery = Static<typeof ProjectHistoryQuery>;

/** Whose folder a version holds: the project's own, or a task's own copy of it (ADR 0045). */
export const HistoryFolder = Type.Union([
  Type.Object({ kind: Type.Literal('project') }, strict),
  Type.Object({ kind: Type.Literal('task_copy'), workstream_id: WorkstreamId }, strict),
]);
export type HistoryFolder = Static<typeof HistoryFolder>;

const at = { entry_id: EventId, at: Timestamp };

export const HistoryEntry = Type.Union([
  /**
   * A version was kept. `changed_files` counts the files that differ from `previous`, the version
   * before it of the same folder; both are null for a folder's first version, and the count is null
   * when the store cannot tell.
   */
  Type.Object(
    {
      kind: Type.Literal('version'),
      ...at,
      checkpoint: CheckpointView,
      folder: HistoryFolder,
      previous: Nullable(CheckpointId),
      changed_files: Nullable(Count),
    },
    strict,
  ),
  /** A version could not be kept, so nothing from this point can be put back. */
  Type.Object(
    {
      kind: Type.Literal('not_kept'),
      ...at,
      reason: CheckpointReason,
      execution_id: Nullable(ExecutionId),
      failure: CheckpointFailure,
    },
    strict,
  ),
  /**
   * A version was put back and read again equal. Putting `before_restore_checkpoint_id` back undoes
   * it. Totals only: the paths are in the restore's event.
   */
  Type.Object(
    {
      kind: Type.Literal('restored'),
      ...at,
      checkpoint_id: CheckpointId,
      before_restore_checkpoint_id: CheckpointId,
      changed_back: Count,
      removed: Count,
      returned: Count,
      project_head_moved: Nullable(Type.Boolean()),
    },
    strict,
  ),
  /** A restore stopped; `written` says part of the folder may already have changed. */
  Type.Object(
    {
      kind: Type.Literal('restore_incomplete'),
      ...at,
      checkpoint_id: CheckpointId,
      before_restore_checkpoint_id: Nullable(CheckpointId),
      written: Type.Boolean(),
      not_restored: Count,
      failure: ErrorInfo,
    },
    strict,
  ),
]);
export type HistoryEntry = Static<typeof HistoryEntry>;

export const ProjectHistoryResult = Type.Union([
  Type.Object(
    {
      availability: Type.Literal('available'),
      /** Newest first, at most the query's limit. */
      entries: Type.Array(HistoryEntry, { maxItems: HISTORY_PAGE_MAX }),
      /** Older entries exist than the last one given. */
      more: Type.Boolean(),
      /** Every entry of the project's history. */
      total: Count,
    },
    strict,
  ),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
]);
export type ProjectHistoryResult = Static<typeof ProjectHistoryResult>;

export const ProjectHistoryResponse = Type.Object(
  { project_id: ProjectId, result: ProjectHistoryResult },
  strict,
);
export type ProjectHistoryResponse = Static<typeof ProjectHistoryResponse>;
