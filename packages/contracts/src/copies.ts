import Type, { type Static } from 'typebox';
import { CHECKPOINT_PATHS_LISTED, PathList, ProjectPath, ReviewDigest } from './checkpoints.ts';
import { Sha256Hex } from './devices.ts';
import { HostPath } from './locations.ts';
import { Nullable, Timestamp } from './primitives.ts';
import { FileChangeKind } from './project-reads.ts';

const strict = { additionalProperties: false } as const;

/**
 * Why a task could not have a copy of its own (ADR 0045), so it works in the project's own folder,
 * one task at a time there. `linked_repository`: the folder's `.git` is a file, a git worktree or a
 * submodule, so a copy would still act on the person's repository. `nested_repository`: another
 * repository sits inside the folder. `too_large`: more files or bytes than one checkpoint keeps.
 * `cannot_clone`: the folder's volume cannot make a copy that shares its blocks. `location_missing`:
 * the folder is not there. `holds_host_data`: the folder holds Halcyonic's own data.
 * `copy_failed`: making the copy or recording its start failed. `runtime_unconfined`: the task's
 * runtime is not held to the folder it is given on this computer, so in a copy it could still write
 * other tasks' copies and the project's folder.
 */
export const CopyRefusalCode = Type.Union([
  Type.Literal('linked_repository'),
  Type.Literal('nested_repository'),
  Type.Literal('too_large'),
  Type.Literal('cannot_clone'),
  Type.Literal('location_missing'),
  Type.Literal('holds_host_data'),
  Type.Literal('copy_failed'),
  Type.Literal('runtime_unconfined'),
]);
export type CopyRefusalCode = Static<typeof CopyRefusalCode>;

/**
 * How a file's changes met when a task's work was to be added to its project. `text`: both changed
 * the same lines, and the combined file marks both. `binary`: both changed a file that cannot be
 * marked. `deleted`: one side deleted a file the other changed. `other`: anything else git reports,
 * such as a file on one side and a folder on the other.
 */
export const ConflictKind = Type.Union([
  Type.Literal('text'),
  Type.Literal('binary'),
  Type.Literal('deleted'),
  Type.Literal('other'),
]);
export type ConflictKind = Static<typeof ConflictKind>;

export const ConflictedFile = Type.Object({ path: ProjectPath, kind: ConflictKind }, strict);
export type ConflictedFile = Static<typeof ConflictedFile>;

/** Files changed in both the project and a task, in git's order, and how many there are in all. */
export const ConflictedFiles = Type.Object(
  {
    files: Type.Array(ConflictedFile, { minItems: 1, maxItems: CHECKPOINT_PATHS_LISTED }),
    total: Type.Integer({ minimum: 1 }),
  },
  strict,
);
export type ConflictedFiles = Static<typeof ConflictedFiles>;

/**
 * A file of a task's copy and how it differs from the copy's base, as an add would bring it to the
 * project, in the words a checkpoint's changes use (`FileChangeKind`): `added` a file the base did
 * not have, `changed` one it had (its content or its kind of file), `removed` one it had that the
 * copy no longer has.
 */
export const ChangedFile = Type.Object({ path: ProjectPath, change: FileChangeKind }, strict);
export type ChangedFile = Static<typeof ChangedFile>;

/** Some files of a set and how each changed, in git's order, and how many the set holds. */
export const ChangedFiles = Type.Object(
  {
    files: Type.Array(ChangedFile, { maxItems: CHECKPOINT_PATHS_LISTED }),
    total: Type.Integer({ minimum: 0 }),
  },
  strict,
);
export type ChangedFiles = Static<typeof ChangedFiles>;

/**
 * A task's own copy of its project's folder (ADR 0045), as its view shows it. `own`: the task works
 * in `directory`, a folder of Halcyonic's own, and its work reaches the project only when the person
 * adds it. `to_add` is what the host last read as differing from the copy's base, null until read;
 * `to_add_files` the same files with how each changed, null until read and for a reading recorded
 * before readings carried it; `to_add_review` the digest of that reading (ADR 0051), which an add
 * from a paired device carries back, null where there is none.
 * `conflict` is the latest add's overlap, until the agent fitted the work or an add went through.
 * `refused`: the task works in the project's own folder, one task at a time, for `code`.
 */
export const WorkstreamCopy = Type.Union([
  Type.Object(
    {
      state: Type.Literal('own'),
      directory: HostPath,
      made_at: Timestamp,
      base_tree: Sha256Hex,
      to_add: Nullable(PathList),
      to_add_files: Nullable(ChangedFiles),
      to_add_review: Nullable(ReviewDigest),
      to_add_read_at: Nullable(Timestamp),
      last_added_at: Nullable(Timestamp),
      conflict: Nullable(ConflictedFiles),
    },
    strict,
  ),
  Type.Object(
    { state: Type.Literal('refused'), code: CopyRefusalCode, refused_at: Timestamp },
    strict,
  ),
]);
export type WorkstreamCopy = Static<typeof WorkstreamCopy>;
