import Type, { type Static } from 'typebox';
import { CheckpointId, ProjectPath, ReviewDigest } from './checkpoints.ts';
import { Sha256Hex } from './devices.ts';
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

/** `to` left out compares `from` with its folder as it is now. */
export const ProjectChangesQuery = Type.Object(
  { journal_id: JournalId, from: CheckpointId, to: Type.Optional(CheckpointId) },
  strict,
);
export type ProjectChangesQuery = Static<typeof ProjectChangesQuery>;

/** `to` left out compares the file in `from` with the file as it is now. */
export const ProjectFileChangeQuery = Type.Object(
  { journal_id: JournalId, from: CheckpointId, to: Type.Optional(CheckpointId), path: ProjectPath },
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

/** What changed between two checkpoints of a project, or since one, by file. */
export const ProjectChangesResponse = Type.Object(
  {
    project_id: ProjectId,
    from: CheckpointId,
    /** Null: compared with the folder as it is now. */
    to: Nullable(CheckpointId),
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

/** One file's change between two checkpoints, or since one, as a unified diff. */
export const ProjectFileChangeResponse = Type.Object(
  {
    project_id: ProjectId,
    from: CheckpointId,
    /** Null: compared with the file as it is now. */
    to: Nullable(CheckpointId),
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

/** The most pictures one instruction carries (A4). */
export const PICTURES_PER_INSTRUCTION = 4;
/** The largest picture an instruction carries, in bytes. */
export const PICTURE_BYTES = 5_242_880;
/** The most pictures the project's list names; `pictures_total` counts them all. */
export const PROJECT_PICTURES = 200;

/** What a picture is, read from its first bytes, never from its name. */
export const PictureMediaType = Type.Union([
  Type.Literal('image/png'),
  Type.Literal('image/jpeg'),
  Type.Literal('image/webp'),
]);
export type PictureMediaType = Static<typeof PictureMediaType>;

/**
 * A picture that goes with an instruction (A4): `image`, one of the project's folder, read once as the
 * project's own read does; or `given`, one the person gave the host (what they see, a frame of the
 * running game) by its id from `POST /api/pictures`. Either way the host hands the runtime the bytes
 * it read and checked, never a path, never a file outside (ADR 0058, amended).
 */
export const InstructionAttachment = Type.Union([
  Type.Object({ path: ProjectPath, kind: Type.Literal('image') }, strict),
  Type.Object({ picture: Sha256Hex, kind: Type.Literal('given') }, strict),
]);
export type InstructionAttachment = Static<typeof InstructionAttachment>;

/**
 * A picture the person gave the host, kept by its SHA-256 (its `id`) in the host's data folder for a
 * while, to go with an instruction: what `POST /api/pictures` answers.
 */
export const GivenPicture = Type.Object(
  { id: Sha256Hex, media_type: PictureMediaType, size_bytes: Count },
  strict,
);
export type GivenPicture = Static<typeof GivenPicture>;

export const PictureGivenResponse = Type.Object({ picture: GivenPicture }, strict);
export type PictureGivenResponse = Static<typeof PictureGivenResponse>;

export const InstructionAttachments = Type.Array(InstructionAttachment, {
  minItems: 1,
  maxItems: PICTURES_PER_INSTRUCTION,
});
export type InstructionAttachments = Static<typeof InstructionAttachments>;

/**
 * One picture as the host read it for an instruction: what the journal keeps, never the bytes. `path`
 * is the folder's picture's, or null for one the person gave, which its `sha256` names.
 */
export const PictureRead = Type.Object(
  {
    path: Nullable(ProjectPath),
    media_type: PictureMediaType,
    size_bytes: Count,
    sha256: Sha256Hex,
  },
  strict,
);
export type PictureRead = Static<typeof PictureRead>;

/** One picture of the project's folder, as the list names it. */
export const ProjectPicture = Type.Object(
  { path: ProjectPath, media_type: PictureMediaType, size_bytes: Count, modified_at: Timestamp },
  strict,
);
export type ProjectPicture = Static<typeof ProjectPicture>;

/**
 * The pictures of a project's folder that an instruction may carry: PNG, JPEG or WebP by their first
 * bytes, at most `PICTURE_BYTES` each, never a link, a hidden or withheld name, or a folder the host
 * keeps out (`node_modules`, version control's own). Sorted by path; at most `PROJECT_PICTURES`.
 */
export const ProjectPicturesResponse = Type.Object(
  {
    project_id: ProjectId,
    pictures: Type.Array(ProjectPicture, { maxItems: PROJECT_PICTURES }),
    pictures_total: Count,
    source: Type.Literal('project_files'),
  },
  strict,
);
export type ProjectPicturesResponse = Static<typeof ProjectPicturesResponse>;

/** The largest rules file that can be passed on with a task's first instruction (E5). */
export const PROJECT_RULES_BYTES = 32_768;

/** The project's own rules file at its folder's top: its `AGENTS.md`, else its `CLAUDE.md`. */
export const ProjectRulesFile = Type.Union([Type.Literal('AGENTS.md'), Type.Literal('CLAUDE.md')]);
export type ProjectRulesFile = Static<typeof ProjectRulesFile>;

/**
 * Where a project's rules stand (E5): `none`, no rules file; `off`, a file not passed on; `on`, passed on
 * with each task's first instruction exactly as the person read it; `changed`, chosen once but the file
 * has changed since, so it is off until read again; `too_large`, larger than `PROJECT_RULES_BYTES`;
 * `unreadable`, not valid UTF-8, or not a file that can be read without following a link.
 */
export const ProjectRulesState = Type.Union([
  Type.Literal('none'),
  Type.Literal('off'),
  Type.Literal('on'),
  Type.Literal('changed'),
  Type.Literal('too_large'),
  Type.Literal('unreadable'),
]);
export type ProjectRulesState = Static<typeof ProjectRulesState>;

/** The project's rules while on: the file, and the digest of the text the person chose. */
export const ProjectRulesOn = Type.Object({ file: ProjectRulesFile, sha256: Sha256Hex }, strict);
export type ProjectRulesOn = Static<typeof ProjectRulesOn>;

/**
 * The project's own rules as they are now, for the person to read before choosing them (E5): the
 * file's text whole, outside words, and the `review` a `project.rules.set` that turns them on carries
 * back (ADR 0051). `text` and `review` only where the file can be passed on.
 */
export const ProjectRulesResponse = Type.Object(
  {
    project_id: ProjectId,
    file: Nullable(ProjectRulesFile),
    state: ProjectRulesState,
    size_bytes: Nullable(Count),
    text: Nullable(Text(PROJECT_RULES_BYTES)),
    review: Nullable(ReviewDigest),
    source: Type.Literal('project_files'),
  },
  strict,
);
export type ProjectRulesResponse = Static<typeof ProjectRulesResponse>;
