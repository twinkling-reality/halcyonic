import Type, { type Static } from 'typebox';
import { Nullable, ProjectId, Timestamp } from './primitives.ts';

const strict = { additionalProperties: false } as const;

/**
 * An absolute path on the machine that runs the control plane, where a project's files live. The
 * host lists paths and records the ones it resolved; it never takes one from a client.
 */
export const HostPath = Type.String({ minLength: 1, maxLength: 4096, pattern: '\\S' });

/** A folder's own name as the file system has it, for display: untrusted text. */
export const DisplayName = Type.String({ minLength: 1, maxLength: 255 });

/**
 * The name of a folder the host creates for a project inside one of its project roots: a single
 * path segment that is neither hidden nor relative, in characters every shell and file system
 * takes as they are.
 */
export const FolderName = Type.String({ pattern: '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' });

/**
 * The name of a folder already inside a project root, as the file system has it: one path segment,
 * so no `/` or NUL, and not hidden, which also rules out `.` and `..`.
 */
export const ExistingFolderName = Type.String({
  minLength: 1,
  maxLength: 255,
  pattern: '^[^./\\u0000][^/\\u0000]*$',
});

/**
 * Where a person wants a project's files to live, always named by one of the host's project roots
 * and a folder directly inside it, so a client never sends a path of its own making.
 * `existing_folder`: a folder already there, such as one `GET /api/locations` listed, or the root
 * itself when `folder_name` is null. `new_folder`: a folder the host creates, which must not exist
 * yet.
 */
export const ProjectLocationChoice = Type.Union([
  Type.Object(
    {
      kind: Type.Literal('existing_folder'),
      root: HostPath,
      folder_name: Nullable(ExistingFolderName),
    },
    strict,
  ),
  Type.Object(
    { kind: Type.Literal('new_folder'), root: HostPath, folder_name: FolderName },
    strict,
  ),
]);
export type ProjectLocationChoice = Static<typeof ProjectLocationChoice>;

/**
 * The folder a project is bound to, as the host resolved it when the project was bound: every
 * later start runs there after the host checks it again. `name` is the folder's own name, for
 * display, so a client never takes a path apart. `created` is true when the host made the folder
 * for the project.
 */
export const ProjectLocation = Type.Object(
  { path: HostPath, name: DisplayName, created: Type.Boolean() },
  strict,
);
export type ProjectLocation = Static<typeof ProjectLocation>;

/** The most projects a listed folder names as using it. */
export const MAX_FOLDER_USERS = 100;

/**
 * What the host can tell about a listed folder from the folder's own entry and one name directly
 * inside it, never from inside a file. Each read leaves a link at the end of its path unfollowed,
 * and the facts are kept only while the folder stays the same folder (its device and inode) from
 * before the reads to after them, and its root the one the host allowed, so a folder or root simply
 * swapped meanwhile is not described by what is elsewhere.
 */
const FolderFacts = {
  /**
   * True when a `.git` folder or file sits directly inside it, false when nothing or something else
   * by that name does, null when that could not be read. Nothing more of the repository is read.
   */
  repository: Nullable(Type.Boolean()),
  /**
   * The newer modification time of the folder itself and, for a repository, of its `.git` entry;
   * null when either could not be read. A folder's own time moves when an entry directly inside it
   * is added, removed or renamed, and a repository's `.git` moves with commits and staging, so an
   * edit deeper inside that touched neither is not seen: this is the latest change the host saw,
   * not a promise that nothing changed since.
   */
  changed_at: Nullable(Timestamp),
  /**
   * The projects bound to this very folder, empty when none is: a project counts while its recorded
   * path is still its own real path, the rule every start applies, and is matched to the folder by
   * identity on the file system (device and inode). At most `MAX_FOLDER_USERS`, the oldest first.
   */
  used_by: Type.Array(ProjectId, { maxItems: MAX_FOLDER_USERS }),
};

/** A folder directly inside a project root, as the host found it when asked. */
export const LocationFolder = Type.Object(
  { name: DisplayName, path: HostPath, ...FolderFacts },
  strict,
);
export type LocationFolder = Static<typeof LocationFolder>;

/** The most folders listed for one root. */
export const MAX_LOCATION_FOLDERS = 200;

/**
 * A project root the host lets agents work in, and the folders directly inside it that a project
 * can use, leaving out hidden folders and symbolic links. A project can also use the root itself.
 * `missing` when the root is no longer a folder, so nothing in it can be used until it is back.
 */
export const LocationRoot = Type.Object(
  {
    path: HostPath,
    /** The root's own folder name, as the file system has it: what a project made of it is named. */
    name: DisplayName,
    /**
     * The root's name for people, unlike every other root's: its own name, or, where another root
     * shares it, with a folder above that tells them apart, as "Projects (Work)" or "Work (drive)",
     * or a number where nothing does. It can change when the roots change; a client shows it,
     * re-read from the latest listing, and never keeps it as a choice's identity. Never a path.
     */
    label: DisplayName,
    status: Type.Union([Type.Literal('available'), Type.Literal('missing')]),
    /** The root's own facts, read as a listed folder's are; null and empty while it is missing. */
    ...FolderFacts,
    folders: Type.Array(LocationFolder, { maxItems: MAX_LOCATION_FOLDERS }),
    /** True when the root holds more folders than were listed; the list is sorted by name. */
    folders_truncated: Type.Boolean(),
  },
  strict,
);
export type LocationRoot = Static<typeof LocationRoot>;

/**
 * Where projects may live on the host, read from the file system on request and never journaled.
 * No roots means the host lets agents work nowhere yet: its owner has to allow a folder first.
 */
export const LocationsResponse = Type.Object({ roots: Type.Array(LocationRoot) }, strict);
export type LocationsResponse = Static<typeof LocationsResponse>;
