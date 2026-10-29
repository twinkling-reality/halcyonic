import Type, { type Static } from 'typebox';
import { ProjectPath } from './checkpoints.ts';

const strict = { additionalProperties: false } as const;

/** The most files one list of an observation carries; `total` still counts every one. */
export const OBSERVED_FILES_LISTED = 100;

/** A line count the Mac read, or null for a file it treats as binary or that is too large to count. */
const Lines = Type.Union([Type.Integer({ minimum: 0 }), Type.Null()]);

export const FileCreated = Type.Object(
  { path: ProjectPath, lines: Lines, bytes: Type.Integer({ minimum: 0 }) },
  strict,
);
export type FileCreated = Static<typeof FileCreated>;

export const FileChanged = Type.Object(
  {
    path: ProjectPath,
    lines_before: Lines,
    lines_after: Lines,
    bytes: Type.Integer({ minimum: 0 }),
  },
  strict,
);
export type FileChanged = Static<typeof FileChanged>;

export const FileDeleted = Type.Object({ path: ProjectPath, lines_before: Lines }, strict);
export type FileDeleted = Static<typeof FileDeleted>;

/**
 * Why the Mac could not say what changed: no earlier reading of the folder (a restart lost it),
 * more files than it reads, the folder gone, or a file it could not read.
 */
export const FilesObservedSkip = Type.Object(
  {
    code: Type.Union([
      Type.Literal('no_baseline'),
      Type.Literal('too_large'),
      Type.Literal('location_missing'),
      Type.Literal('unreadable'),
    ]),
    message: Type.String({ minLength: 1, maxLength: 2000 }),
  },
  strict,
);
export type FilesObservedSkip = Static<typeof FilesObservedSkip>;

/**
 * The files of a project's folder that changed between two readings by the Mac: before the task's
 * runtime was asked, or the end of the turn before, and the end of this turn (ADR 0038). Paths
 * relative to the folder, sizes and line counts; never contents. Whoever changed them, the agent or
 * a person, the Mac cannot tell.
 */
export const FilesObserved = Type.Object(
  {
    turn_id: Type.Union([Type.String({ minLength: 1, maxLength: 512 }), Type.Null()]),
    created: Type.Object(
      {
        files: Type.Array(FileCreated, { maxItems: OBSERVED_FILES_LISTED }),
        total: Type.Integer({ minimum: 0 }),
      },
      strict,
    ),
    changed: Type.Object(
      {
        files: Type.Array(FileChanged, { maxItems: OBSERVED_FILES_LISTED }),
        total: Type.Integer({ minimum: 0 }),
      },
      strict,
    ),
    deleted: Type.Object(
      {
        files: Type.Array(FileDeleted, { maxItems: OBSERVED_FILES_LISTED }),
        total: Type.Integer({ minimum: 0 }),
      },
      strict,
    ),
    skipped: Type.Union([FilesObservedSkip, Type.Null()]),
  },
  strict,
);
export type FilesObserved = Static<typeof FilesObserved>;
