import Type, { type Static } from 'typebox';
import { CheckpointId, ProjectPath } from './checkpoints.ts';
import { CheckId } from './previews.ts';
import { JournalId, Nullable, ProjectId, Text, Timestamp } from './primitives.ts';

/**
 * Reads of a project's files for the headset (the parity map's gaps 5, 10 and 4): what changed
 * between two checkpoints, one file's change, one file as it is now, and a failed check step's
 * output. Read only, for any runtime, bounded, and every text in them is outside words: from the
 * project's own files or its build, never Halcyonic's and never the person's, as `source` says.
 */

const strict = { additionalProperties: false } as const;
const Count = Type.Integer({ minimum: 0, maximum: Number.MAX_SAFE_INTEGER });

/** The most files a change list names; `files_total` counts them all. */
export const PROJECT_CHANGE_FILES = 200;
/** The longest one file's change is given whole: bytes and lines. */
export const PROJECT_FILE_DIFF_BYTES = 65_536;
export const PROJECT_FILE_DIFF_LINES = 2_000;
/** The largest file read as text. */
export const PROJECT_FILE_BYTES = 262_144;
/** A failed check step's output: lines, and characters a line. */
export const CHECK_STEP_OUTPUT_LINES = 200;
export const CHECK_STEP_OUTPUT_LINE_CHARS = 300;

export const ProjectReadQuery = Type.Object({ journal_id: JournalId }, strict);
export type ProjectReadQuery = Static<typeof ProjectReadQuery>;

export const ProjectChangesQuery = Type.Object(
  { journal_id: JournalId, from: CheckpointId, to: CheckpointId },
  strict,
);
export type ProjectChangesQuery = Static<typeof ProjectChangesQuery>;

export const ProjectFileChangeQuery = Type.Object(
  { journal_id: JournalId, from: CheckpointId, to: CheckpointId, path: ProjectPath },
  strict,
);
export type ProjectFileChangeQuery = Static<typeof ProjectFileChangeQuery>;

export const ProjectFileQuery = Type.Object({ journal_id: JournalId, path: ProjectPath }, strict);
export type ProjectFileQuery = Static<typeof ProjectFileQuery>;

export const CheckOutputStep = Type.Union([Type.Literal('typecheck'), Type.Literal('build')]);
export type CheckOutputStep = Static<typeof CheckOutputStep>;

export const CheckStepOutputQuery = Type.Object(
  { journal_id: JournalId, step: CheckOutputStep },
  strict,
);
export type CheckStepOutputQuery = Static<typeof CheckStepOutputQuery>;

export const FileChangeKind = Type.Union([
  Type.Literal('added'),
  Type.Literal('changed'),
  Type.Literal('removed'),
]);
export type FileChangeKind = Static<typeof FileChangeKind>;

export const ProjectChangedFile = Type.Object(
  {
    path: ProjectPath,
    change: FileChangeKind,
    /** Null for a binary file, whose lines are not counted. */
    added_lines: Nullable(Count),
    removed_lines: Nullable(Count),
    binary: Type.Boolean(),
  },
  strict,
);
export type ProjectChangedFile = Static<typeof ProjectChangedFile>;

/** What changed between two checkpoints of a project, by file. */
export const ProjectChangesResponse = Type.Object(
  {
    project_id: ProjectId,
    from: CheckpointId,
    to: CheckpointId,
    /** At most `PROJECT_CHANGE_FILES`, sorted by path. */
    files: Type.Array(ProjectChangedFile, { maxItems: PROJECT_CHANGE_FILES }),
    files_total: Count,
    /** False when more files changed than are listed. */
    complete: Type.Boolean(),
    source: Type.Literal('project_files'),
  },
  strict,
);
export type ProjectChangesResponse = Static<typeof ProjectChangesResponse>;

/** One file's change between two checkpoints, as a unified diff. */
export const ProjectFileChangeResponse = Type.Object(
  {
    project_id: ProjectId,
    from: CheckpointId,
    to: CheckpointId,
    path: ProjectPath,
    change: FileChangeKind,
    binary: Type.Boolean(),
    /** True for a file whose name says it may hold secrets: its diff is not given. */
    withheld: Type.Boolean(),
    /** Null for a binary or withheld file; otherwise at most `PROJECT_FILE_DIFF_BYTES` and `PROJECT_FILE_DIFF_LINES`. */
    diff: Nullable(Text(PROJECT_FILE_DIFF_BYTES)),
    /** False when the diff was cut at its bound. */
    complete: Type.Boolean(),
    source: Type.Literal('project_files'),
  },
  strict,
);
export type ProjectFileChangeResponse = Static<typeof ProjectFileChangeResponse>;

export const ProjectFileKind = Type.Union([
  /** Valid UTF-8 without NUL, at most `PROJECT_FILE_BYTES`: given whole. */
  Type.Literal('text'),
  Type.Literal('binary'),
  /** Larger than `PROJECT_FILE_BYTES`: not read. */
  Type.Literal('too_large'),
  /**
   * A file whose name says it may hold secrets (`.env*`, keys, credentials, version control's
   * own): not read, as agents are never shown them either.
   */
  Type.Literal('withheld'),
]);
export type ProjectFileKind = Static<typeof ProjectFileKind>;

/** One file of the project's folder as it is now. */
export const ProjectFileResponse = Type.Object(
  {
    project_id: ProjectId,
    path: ProjectPath,
    kind: ProjectFileKind,
    size_bytes: Count,
    /** Only for `text`. */
    text: Nullable(Text(PROJECT_FILE_BYTES)),
    lines: Nullable(Count),
    modified_at: Timestamp,
    source: Type.Literal('project_files'),
  },
  strict,
);
export type ProjectFileResponse = Static<typeof ProjectFileResponse>;

/** A step of a check's output, cleaned as the agent is given it: no terminal sequences, no host paths. */
export const CheckStepOutputResponse = Type.Object(
  {
    project_id: ProjectId,
    check_id: CheckId,
    step: CheckOutputStep,
    /** Null when the step did not run. */
    exit: Nullable(Type.Integer()),
    lines: Type.Array(Text(CHECK_STEP_OUTPUT_LINE_CHARS), { maxItems: CHECK_STEP_OUTPUT_LINES }),
    lines_total: Count,
    complete: Type.Boolean(),
    source: Type.Literal('check_output'),
  },
  strict,
);
export type CheckStepOutputResponse = Static<typeof CheckStepOutputResponse>;
