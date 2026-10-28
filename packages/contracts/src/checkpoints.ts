import Type, { type Static } from 'typebox';
import { Sha256Hex } from './devices.ts';
import { ErrorInfo, ExecutionId, Nullable, ProjectId, Timestamp } from './primitives.ts';

const strict = { additionalProperties: false } as const;

export type CheckpointId = string & { readonly __brand: 'CheckpointId' };

/** A checkpoint Halcyonic took of a project's files (ADR 0036); UUIDv7, as its other ids. */
export const CheckpointId = Type.Unsafe<CheckpointId>(
  Type.String({ pattern: '^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' }),
);

/**
 * Why the host took a checkpoint: `before_task` once a start is admitted and before the runtime
 * is asked, `after_turn` once the runtime reported a turn ended, `before_restore` just before a
 * restore writes anything, so the restore can itself be undone.
 */
export const CheckpointReason = Type.Union([
  Type.Literal('before_task'),
  Type.Literal('after_turn'),
  Type.Literal('before_restore'),
]);
export type CheckpointReason = Static<typeof CheckpointReason>;

/**
 * A path inside the project's folder, relative to it, as git lists it: never absolute, never a `.`
 * or `..` segment, no control characters. A trailing `/` names a whole folder. The journal holds
 * no other kind of path from a checkpoint or restore.
 */
export const ProjectPath = Type.String({
  minLength: 1,
  maxLength: 1024,
  pattern: '^(?!/)(?!(?:.*/)?\\.\\.?(?:/|$))[^\\u0000-\\u001f\\u007f]+$',
});

/** The most paths one list carries; `total` still counts every one. */
export const CHECKPOINT_PATHS_LISTED = 100;

/** Some paths of a set, in git's order, and how many the set holds. */
export const PathList = Type.Object(
  {
    paths: Type.Array(ProjectPath, { maxItems: CHECKPOINT_PATHS_LISTED }),
    total: Type.Integer({ minimum: 0 }),
  },
  strict,
);
export type PathList = Static<typeof PathList>;

/** A commit id of the project's own repository, SHA-1 or SHA-256, read from its files only. */
export const GitObjectId = Type.String({ pattern: '^(?:[0-9a-f]{40}|[0-9a-f]{64})$' });

/**
 * One checkpoint as the journal records it. `tree` is the SHA-256 git tree of every file the
 * checkpoint holds; `files` counts them. `project_head` is the commit the project's own repository
 * pointed at, or null when it has none or it could not be read.
 */
export const CheckpointView = Type.Object(
  {
    checkpoint_id: CheckpointId,
    project_id: ProjectId,
    reason: CheckpointReason,
    execution_id: Nullable(ExecutionId),
    recorded_at: Timestamp,
    tree: Sha256Hex,
    files: Type.Integer({ minimum: 0 }),
    project_head: Nullable(GitObjectId),
  },
  strict,
);
export type CheckpointView = Static<typeof CheckpointView>;

/** Why a checkpoint could not be taken; the task then has no undo from that point. */
export const CheckpointFailureCode = Type.Union([
  /** The folder is not there, not a folder, or no longer inside a project root. */
  Type.Literal('location_missing'),
  /** Another git repository sits inside the folder; its files cannot be kept whole. */
  Type.Literal('nested_repository'),
  /** More files or bytes than the host keeps for one checkpoint. */
  Type.Literal('too_large'),
  /** A file could not be read. */
  Type.Literal('unreadable'),
  /** The checkpoint store refused or failed; nothing was kept. */
  Type.Literal('store_failed'),
]);
export type CheckpointFailureCode = Static<typeof CheckpointFailureCode>;

export const CheckpointFailure = Type.Object(
  { code: CheckpointFailureCode, message: Type.String({ minLength: 1, maxLength: 2000 }) },
  strict,
);
export type CheckpointFailure = Static<typeof CheckpointFailure>;

/**
 * What putting a checkpoint back would do now, read before the person confirms: files that would
 * change back, files the checkpoint lacks that would be removed, files it has that would come back.
 * `project_head_moved` is null when either commit is unknown.
 */
export const RestorePreview = Type.Object(
  {
    checkpoint_id: CheckpointId,
    changed_back: PathList,
    removed: PathList,
    returned: PathList,
    project_head_moved: Nullable(Type.Boolean()),
  },
  strict,
);
export type RestorePreview = Static<typeof RestorePreview>;

/** A read that undo is not set up for, or that cannot be answered now, says why. */
const Unavailable = Type.Object(
  { availability: Type.Literal('unavailable'), reason: ErrorInfo },
  strict,
);

export const CheckpointsResult = Type.Union([
  Type.Object(
    { availability: Type.Literal('available'), checkpoints: Type.Array(CheckpointView) },
    strict,
  ),
  Unavailable,
]);
export type CheckpointsResult = Static<typeof CheckpointsResult>;

export const CheckpointsResponse = Type.Object(
  { project_id: ProjectId, result: CheckpointsResult },
  strict,
);
export type CheckpointsResponse = Static<typeof CheckpointsResponse>;

export const RestorePreviewResult = Type.Union([
  Type.Object({ availability: Type.Literal('available'), preview: RestorePreview }, strict),
  Unavailable,
]);
export type RestorePreviewResult = Static<typeof RestorePreviewResult>;

export const RestorePreviewResponse = Type.Object(
  { project_id: ProjectId, result: RestorePreviewResult },
  strict,
);
export type RestorePreviewResponse = Static<typeof RestorePreviewResponse>;
