import { execFile, spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { constants } from 'node:fs';
import { lstat, mkdir, open, readFile, readlink, realpath, rm, writeFile } from 'node:fs/promises';
import { basename, dirname, isAbsolute, join, sep } from 'node:path';
import type { CheckpointFailureCode, FileChangeKind } from '@halcyonic/contracts';
import { toolFoldersFor } from '@halcyonic/seatbelt';
import { NO_LINK } from '../observer/file-observer.ts';
import { ancestors, resolved, SANDBOX_EXEC, SYSTEM_READS, subpaths } from '../seatbelt.ts';

/**
 * Checkpoints of project folders, kept by the host in a git repository of its own per project
 * (ADR 0036): SHA-256 objects, the project's folder as the work tree, nothing written to the
 * project's own `.git`. Contents stay in the store; callers get hashes, counts and paths relative
 * to the folder.
 *
 * Git never decides what is equal. A restore writes with git, then this module reads every file
 * again itself and compares it with the checkpoint's blob ids, so a restore is complete only when
 * the folder holds exactly the checkpoint's files. What the project ignored when the checkpoint was
 * taken is recorded with it and left alone; ignore files the folder has now are not trusted, since
 * an agent can write them.
 */

export interface CheckpointLimits {
  /** The most files one checkpoint keeps. */
  readonly files: number;
  /** The most bytes of file content one checkpoint keeps. */
  readonly bytes: number;
}

export const DEFAULT_CHECKPOINT_LIMITS: CheckpointLimits = {
  files: 200_000,
  bytes: 2 * 1024 ** 3,
};

/** How long one git command may run before it is stopped and the operation fails. */
const GIT_TIMEOUT_MS = 10 * 60_000;

/**
 * Keeps bytes exact both ways: the project's `.gitattributes` cannot ask for line-ending
 * conversion, filters or encodings, since the store's own attributes come first.
 */
const STORE_ATTRIBUTES = '* -text -filter -ident -working-tree-encoding -eol\n';

export interface CheckpointStoreOptions {
  /** The git executable, by absolute path; the store never looks one up on PATH. */
  readonly git: string;
  /** The folder the store keeps its repositories in, inside Halcyonic's data directory. */
  readonly directory: string;
  readonly limits?: CheckpointLimits;
  /**
   * Folders no checkpointed project may contain, such as Halcyonic's data directory with its journal
   * and access token; the store's own directory is always one.
   */
  readonly protect?: readonly string[];
}

/** A tree of a folder in the store, without a checkpoint's name or record (ADR 0045). */
export type TreeResult =
  | { readonly ok: true; readonly tree: string; readonly files: number }
  | { readonly ok: false; readonly code: string; readonly message: string };

/** How a file's changes met in a merge, as the merge said it (ADR 0045). */
export interface MergeConflict {
  readonly path: string;
  readonly kind: 'text' | 'binary' | 'deleted' | 'other';
}

export type MergeResult =
  | { readonly ok: true; readonly tree: string; readonly conflicts: readonly MergeConflict[] }
  | { readonly ok: false; readonly code: string; readonly message: string };

export type ApplyResult =
  | { readonly ok: true }
  | {
      readonly ok: false;
      /** `folder_changed`: the folder is no longer the tree it was read as; nothing was written. */
      readonly code: string;
      readonly message: string;
      readonly written: boolean;
    };

export type TakeResult =
  | {
      readonly ok: true;
      readonly tree: string;
      readonly files: number;
      readonly projectHead: string | null;
    }
  | { readonly ok: false; readonly code: CheckpointFailureCode; readonly message: string };

export type PreviewResult =
  | {
      readonly ok: true;
      readonly tree: string;
      readonly changedBack: string[];
      readonly removed: string[];
      readonly returned: string[];
      readonly projectHeadMoved: boolean | null;
    }
  | { readonly ok: false; readonly code: string; readonly message: string };

export type RestoreResult =
  | {
      readonly ok: true;
      readonly tree: string;
      readonly compared: number;
      readonly changedBack: string[];
      readonly removed: string[];
      readonly returned: string[];
      readonly projectHeadMoved: boolean | null;
    }
  | {
      readonly ok: false;
      /** Whether any file had been changed when the restore stopped. */
      readonly written: boolean;
      /** What still differs from the checkpoint, as far as the last check could tell. */
      readonly notRestored: string[];
      readonly code: string;
      readonly message: string;
    };

/** One file that changed between two checkpoints. */
export interface ComparedFile {
  readonly path: string;
  readonly change: 'added' | 'changed' | 'removed';
  /** Null for a binary file. */
  readonly addedLines: number | null;
  readonly removedLines: number | null;
  readonly binary: boolean;
}

export interface RestoreHooks {
  /** For tests: runs after every write and before the check, as another writer might. */
  readonly onWritten?: () => void | Promise<void>;
}

interface TreeEntry {
  readonly mode: string;
  readonly oid: string;
  readonly path: string;
}

interface Meta {
  readonly format: 1;
  /** The folder as the file system resolved it when the checkpoint was taken. */
  readonly folder: string;
  /** What `ls-files --others --ignored --exclude-standard --directory` listed then. */
  readonly ignored: string[];
  readonly head: string | null;
}

interface GitResult {
  readonly code: number;
  readonly stdout: Buffer;
  readonly stderr: string;
  /** Set when git was stopped past the most of its output the caller reads. */
  readonly truncated?: boolean;
}

const ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const OBJECT_ID = /^(?:[0-9a-f]{40}|[0-9a-f]{64})$/;

/** Git as the store runs it: its own binary and, on macOS, the folders it reads to run. */
interface RunnableGit {
  readonly git: string;
  /** Null where no Seatbelt profile confines it: anywhere but macOS. */
  readonly tools: readonly string[] | null;
}

/**
 * The binary to run for the configured git. Apple's tools in `/usr/bin` are shims that find the real
 * tool through xcrun, which reads Xcode's own files and writes a cache in the user's temporary folder;
 * the store runs the tool the shim would, by xcrun's own answer, so its profile opens none of that.
 */
async function runnableGit(configured: string): Promise<RunnableGit> {
  if (process.platform !== 'darwin') return { git: configured, tools: null };
  const real = await realpath(configured);
  const git = real.startsWith('/usr/bin/') ? await realpath(await xcrunFind('git')) : real;
  return { git, tools: toolFoldersFor(git) };
}

/** The developer tool xcrun names, by absolute path; it never looks the tool up on PATH. */
function xcrunFind(tool: string): Promise<string> {
  return new Promise((resolvePromise, reject) => {
    execFile(
      '/usr/bin/xcrun',
      ['--find', tool],
      { env: { PATH: '/usr/bin:/bin' }, timeout: 30_000 },
      (error, stdout) => {
        const path = String(stdout).trim();
        if (error === null && isAbsolute(path)) resolvePromise(path);
        else reject(error ?? new Error(`xcrun names no ${tool}`));
      },
    );
  });
}

/**
 * The Seatbelt profile every git of the store runs inside on macOS (U2): reads closed by default but
 * for the system's own paths, git's own folders, the project's repository, the store's home for git
 * and the project; writes only in the repository and the project. The store's checks before it acts
 * only narrow the time in which a folder, swapped for a link by a process an agent left running, could
 * send git elsewhere; under this profile the kernel refuses any read or write the link leads outside,
 * since it judges the path each one resolves to. Git's own folders must not hold the project, or
 * reading them would open what is around it. Exported for its tests.
 */
export function gitProfile(scope: {
  readonly tools: readonly string[];
  readonly repository: string;
  readonly home: string;
  readonly project: string | null;
}): string {
  const writable = [scope.repository, ...(scope.project === null ? [] : [scope.project])];
  for (const tool of scope.tools)
    for (const path of writable)
      if (within(resolved(tool), resolved(path)))
        throw new Error("git's own folder holds the project or its checkpoints, so git is not run");
  const readable = [...writable, scope.home];
  return [
    '(version 1)',
    '(allow default)',
    '(deny network-outbound)',
    '(deny file-write*)',
    `(allow file-write* ${subpaths(writable)} (literal "/dev/null"))`,
    '(deny file-read*)',
    ...SYSTEM_READS,
    `(allow file-read* ${subpaths(scope.tools)})`,
    `(allow file-read-metadata ${ancestors(scope.tools)})`,
    `(allow file-read* ${subpaths(readable)})`,
    `(allow file-read-metadata ${ancestors(readable)})`,
    '(deny lsopen)',
    '(deny appleevent-send)',
    '',
  ].join('\n');
}

/**
 * The Seatbelt profile a removal runs inside on macOS (U2): it writes only under the project's real
 * path, so a folder swapped for a link between the store's check and the removal cannot send it out.
 * Exported for its tests.
 */
export function removalProfile(project: string): string {
  return [
    '(version 1)',
    '(allow default)',
    '(deny network-outbound)',
    '(deny file-write*)',
    `(allow file-write* ${subpaths([project])})`,
    '',
  ].join('\n');
}

export class CheckpointStore {
  readonly #git: string;
  readonly #directory: string;
  readonly #limits: CheckpointLimits;
  readonly #protect: readonly string[];
  #found: Promise<RunnableGit> | null = null;

  constructor(options: CheckpointStoreOptions) {
    this.#git = options.git;
    this.#directory = options.directory;
    this.#limits = options.limits ?? DEFAULT_CHECKPOINT_LIMITS;
    this.#protect = [options.directory, ...(options.protect ?? [])];
  }

  /**
   * Keeps every file of `folder` the project does not ignore, and also `keepAlso`, paths it does
   * ignore that a restore is about to remove, so putting this checkpoint back returns them.
   */
  async take(
    projectId: string,
    folder: string,
    checkpointId: string,
    options: { readonly keepAlso?: readonly string[] } = {},
  ): Promise<TakeResult> {
    if (!(await isFolder(folder)))
      return { ok: false, code: 'location_missing', message: "The project's folder is not there." };
    if (await this.#holdsHostData(folder)) return HOLDS_HOST_DATA;
    let repository: string;
    try {
      repository = await this.#repository(projectId);
    } catch {
      return storeFailed('The checkpoint store could not be opened.');
    }
    const index = this.#indexFile(repository, checkpointId);
    try {
      const added = await this.#run(repository, folder, index, ['add', '-A', '--', '.']);
      if (added.code !== 0) return failedAdd(added.stderr);
      const keep = (options.keepAlso ?? []).filter(isProjectPath);
      if (keep.length > 0) {
        const forced = await this.#run(
          repository,
          folder,
          index,
          ['add', '-f', '--pathspec-from-file=-', '--pathspec-file-nul'],
          Buffer.from(keep.join('\0')),
        );
        if (forced.code !== 0) return failedAdd(forced.stderr);
      }
      const staged = await this.#run(repository, folder, index, ['ls-files', '-s', '-z']);
      if (staged.code !== 0) return storeFailed("The checkpoint's file list could not be read.");
      const entries = splitNul(staged.stdout).map(parseStageLine);
      if (entries.some((entry) => entry.mode === '160000'))
        return {
          ok: false,
          code: 'nested_repository',
          message: 'Another git repository sits inside the folder, so its files cannot be kept.',
        };
      if (entries.length > this.#limits.files)
        return {
          ok: false,
          code: 'too_large',
          message: `The folder holds ${entries.length} files, more than the ${this.#limits.files} one checkpoint keeps.`,
        };
      const bytes = await this.#totalSize(repository, entries);
      if (bytes === null) return storeFailed('The size of the kept files could not be read.');
      if (bytes > this.#limits.bytes)
        return {
          ok: false,
          code: 'too_large',
          message: `The folder holds more than the ${formatBytes(this.#limits.bytes)} one checkpoint keeps.`,
        };
      const written = await this.#run(repository, folder, index, ['write-tree']);
      const tree = written.stdout.toString().trim();
      if (written.code !== 0 || !OBJECT_ID.test(tree))
        return storeFailed('The checkpoint could not be written.');
      const ignored = await this.#run(repository, folder, index, [
        'ls-files',
        '-z',
        '--others',
        '--ignored',
        '--exclude-standard',
        '--directory',
      ]);
      if (ignored.code !== 0) return storeFailed('What the project ignores could not be listed.');
      const meta: Meta = {
        format: 1,
        folder: await realpath(folder),
        ignored: splitNul(ignored.stdout),
        head: await readProjectHead(folder),
      };
      const blob = await this.#run(
        repository,
        null,
        null,
        ['hash-object', '-w', '--stdin'],
        Buffer.from(JSON.stringify(meta)),
      );
      const metaId = blob.stdout.toString().trim();
      if (blob.code !== 0 || !OBJECT_ID.test(metaId))
        return storeFailed('The checkpoint could not be written.');
      for (const [ref, id] of [
        [treeRef(checkpointId), tree],
        [metaRef(checkpointId), metaId],
      ] as const) {
        const updated = await this.#run(repository, null, null, ['update-ref', ref, id]);
        if (updated.code !== 0) return storeFailed('The checkpoint could not be named.');
      }
      return { ok: true, tree, files: entries.length, projectHead: meta.head };
    } catch {
      return storeFailed('The checkpoint store failed.');
    } finally {
      await rm(index, { force: true });
    }
  }

  /**
   * The folder the checkpoint was taken of, as the file system resolved it then: the project's, or
   * a task's own copy (ADR 0045). Null when the checkpoint is not kept here.
   */
  async folderOf(projectId: string, checkpointId: string): Promise<string | null> {
    if (!ID_PATTERN.test(checkpointId)) return null;
    const repository = this.#repositoryPath(projectId);
    if (!(await isFolder(repository))) return null;
    const blob = await this.#run(repository, null, null, [
      'cat-file',
      'blob',
      metaRef(checkpointId),
    ]);
    if (blob.code !== 0) return null;
    try {
      const meta = JSON.parse(blob.stdout.toString()) as Meta;
      return meta.format === 1 && typeof meta.folder === 'string' ? meta.folder : null;
    } catch {
      return null;
    }
  }

  /**
   * The tree of every file of `folder` it does not ignore, through a throwaway index, kept in the
   * project's repository but named nowhere. With `seed`, the index starts as that tree, so a file it
   * holds stays in the tree though an ignore file now names it: an agent cannot hide a file from an
   * add by ignoring it (ADR 0045).
   */
  async treeOf(projectId: string, folder: string, seed: string | null): Promise<TreeResult> {
    if (!(await isFolder(folder)))
      return { ok: false, code: 'location_missing', message: 'The folder is not there.' };
    if (await this.#holdsHostData(folder)) return HOLDS_HOST_DATA;
    if (seed !== null && !OBJECT_ID.test(seed)) throw new Error('not a tree');
    let repository: string;
    try {
      repository = await this.#repository(projectId);
    } catch {
      return storeFailed('The checkpoint store could not be opened.');
    }
    const index = this.#indexFile(repository, 'tree');
    try {
      if (seed !== null) {
        const seeded = await this.#run(repository, folder, index, ['read-tree', seed]);
        if (seeded.code !== 0) return storeFailed('The earlier files could not be read.');
      }
      const added = await this.#run(repository, folder, index, ['add', '-A', '--', '.']);
      if (added.code !== 0) return failedAdd(added.stderr);
      const staged = await this.#run(repository, folder, index, ['ls-files', '-s', '-z']);
      if (staged.code !== 0) return storeFailed("The folder's file list could not be read.");
      const entries = splitNul(staged.stdout).map(parseStageLine);
      if (entries.some((entry) => entry.mode === '160000'))
        return {
          ok: false,
          code: 'nested_repository',
          message: 'Another git repository sits inside the folder, so its files cannot be kept.',
        };
      const written = await this.#run(repository, folder, index, ['write-tree']);
      const tree = written.stdout.toString().trim();
      if (written.code !== 0 || !OBJECT_ID.test(tree))
        return storeFailed('The tree could not be written.');
      return { ok: true, tree, files: entries.length };
    } catch {
      return storeFailed('The checkpoint store failed.');
    } finally {
      await rm(index, { force: true });
    }
  }

  /**
   * A zip of the files a checkpoint of `folder` keeps (keeping what was made, F3), written by git
   * into the project's own repository as `file`, for the caller to move: never `.git`, never what
   * the project ignores, and never a path `leaveOut` names (relative to the folder, `/` between its
   * parts). The tree is filtered whole first, so nothing it leaves out reaches git's archive by a
   * pattern git reads otherwise. `files` counts what it holds.
   */
  async archive(
    projectId: string,
    folder: string,
    leaveOut: (path: string) => boolean,
  ): Promise<
    | { readonly ok: true; readonly file: string; readonly files: number }
    | { readonly ok: false; readonly code: string; readonly message: string }
  > {
    const tree = await this.treeOf(projectId, folder, null);
    if (!tree.ok) return tree;
    const repository = await this.#repository(projectId);
    const failed = {
      ok: false,
      code: 'store_failed',
      message: 'The copy could not be made.',
    } as const;
    const listed = await this.#run(repository, null, null, [
      'ls-tree',
      '-r',
      '-z',
      '--full-tree',
      tree.tree,
    ]);
    if (listed.code !== 0) return failed;
    // Each entry is `<mode> <type> <object>\t<path>`, the form `update-index --index-info` reads.
    const kept = splitNul(listed.stdout).filter((entry) => {
      const tab = entry.indexOf('\t');
      return tab > 0 && !leaveOut(entry.slice(tab + 1));
    });
    const stamp = `${process.pid}-${Date.now()}`;
    const index = join(repository, `copy-${stamp}.index`);
    const file = join(repository, `copy-${stamp}.zip`);
    try {
      if ((await this.#run(repository, null, index, ['read-tree', '--empty'])).code !== 0)
        return failed;
      if (
        kept.length > 0 &&
        (
          await this.#run(
            repository,
            null,
            index,
            ['update-index', '-z', '--index-info'],
            Buffer.from(`${kept.join('\0')}\0`),
          )
        ).code !== 0
      )
        return failed;
      const written = await this.#run(repository, null, index, ['write-tree']);
      const filtered = written.stdout.toString().trim();
      if (written.code !== 0 || !OBJECT_ID.test(filtered)) return failed;
      const zipped = await this.#run(repository, null, null, [
        'archive',
        '--format=zip',
        '-o',
        file,
        filtered,
      ]);
      if (zipped.code !== 0) {
        await rm(file, { force: true });
        return failed;
      }
    } finally {
      await rm(index, { force: true });
    }
    const files = zipFileCount(await readFile(file));
    if (files === null) {
      await rm(file, { force: true });
      return { ok: false, code: 'store_failed', message: 'The copy could not be read back.' };
    }
    return { ok: true, file, files };
  }

  /** Names `tree` by `name` under the store's own refs, so it is kept; false when it could not. */
  async keep(projectId: string, name: string, tree: string): Promise<boolean> {
    if (!/^[a-z0-9-]+(?:\/[a-z0-9-]+)*$/.test(name) || !OBJECT_ID.test(tree)) return false;
    const repository = await this.#repository(projectId);
    const updated = await this.#run(repository, null, null, [
      'update-ref',
      `refs/halcyonic/copies/${name}`,
      tree,
    ]);
    return updated.code === 0;
  }

  /** The paths that differ between two trees, in git's order; null when they cannot be read. */
  async changedPaths(projectId: string, from: string, to: string): Promise<string[] | null> {
    if (!OBJECT_ID.test(from) || !OBJECT_ID.test(to)) return null;
    const repository = await this.#repository(projectId);
    const diff = await this.#run(repository, null, null, [
      'diff-tree',
      '-r',
      '-z',
      '--name-only',
      '--no-renames',
      from,
      to,
    ]);
    return diff.code === 0 ? splitNul(diff.stdout) : null;
  }

  /**
   * The files that differ between two trees and how each changed, in git's order (`--name-status`):
   * `added`, `removed` (deleted), anything else (content or kind of file) `changed`. Null when they
   * cannot be read.
   */
  async changedFiles(
    projectId: string,
    from: string,
    to: string,
  ): Promise<{ path: string; change: FileChangeKind }[] | null> {
    if (!OBJECT_ID.test(from) || !OBJECT_ID.test(to)) return null;
    const repository = await this.#repository(projectId);
    const diff = await this.#run(repository, null, null, [
      'diff-tree',
      '-r',
      '-z',
      '--name-status',
      '--no-renames',
      from,
      to,
    ]);
    if (diff.code !== 0) return null;
    const parts = splitNul(diff.stdout);
    const files: { path: string; change: FileChangeKind }[] = [];
    for (let i = 0; i + 1 < parts.length; i += 2) {
      const status = parts[i] as string;
      const path = parts[i + 1] as string;
      files.push({
        path,
        change: status === 'A' ? 'added' : status === 'D' ? 'removed' : 'changed',
      });
    }
    return files;
  }

  /**
   * The three-way merge of `ours` and `theirs` over `base` (ADR 0045), as trees only: nothing is
   * written to any folder. The merged tree holds both sides, marked, where text met; `conflicts`
   * names every path where the two met, and how.
   */
  async merge(projectId: string, base: string, ours: string, theirs: string): Promise<MergeResult> {
    for (const tree of [base, ours, theirs])
      if (!OBJECT_ID.test(tree)) throw new Error('not a tree');
    const repository = await this.#repository(projectId);
    const merged = await this.#run(repository, null, null, [
      'merge-tree',
      '--write-tree',
      '--name-only',
      '-z',
      `--merge-base=${base}`,
      ours,
      theirs,
    ]);
    if (merged.code !== 0 && merged.code !== 1)
      return { ok: false, code: 'merge_failed', message: 'The two could not be combined.' };
    const parts = merged.stdout.toString().split('\0');
    const tree = parts[0] ?? '';
    if (!OBJECT_ID.test(tree))
      return { ok: false, code: 'merge_failed', message: 'The two could not be combined.' };
    if (merged.code === 0) return { ok: true, tree, conflicts: [] };
    const separator = parts.indexOf('', 1);
    const paths = parts.slice(1, separator === -1 ? parts.length : separator);
    // Then messages: a count, that many paths, a type, a message.
    const kinds = new Map<string, MergeConflict['kind']>();
    let at = separator + 1;
    while (separator !== -1 && at < parts.length) {
      const count = Number(parts[at]);
      if (!Number.isInteger(count) || count < 1) break;
      const named = parts.slice(at + 1, at + 1 + count);
      const type = parts[at + 1 + count] ?? '';
      at += count + 3;
      const kind =
        type === 'CONFLICT (binary)'
          ? 'binary'
          : type === 'CONFLICT (modify/delete)'
            ? 'deleted'
            : type === 'CONFLICT (contents)'
              ? 'text'
              : type.startsWith('CONFLICT')
                ? 'other'
                : null;
      if (kind === null) continue;
      for (const path of named) {
        const before = kinds.get(path);
        // A binary file's contents conflict is still a binary one.
        if (before === undefined || (before === 'text' && kind !== 'text')) kinds.set(path, kind);
      }
    }
    return {
      ok: true,
      tree,
      conflicts: paths.map((path) => ({ path, kind: kinds.get(path) ?? 'other' })),
    };
  }

  /**
   * Makes `folder`, which holds `from`, hold `to` instead (ADR 0045): git compares the folder with
   * `from` first and writes nothing if any file differs, then writes only what differs between the
   * two trees, and the folder is read again and must hold exactly `to`.
   */
  async apply(projectId: string, folder: string, from: string, to: string): Promise<ApplyResult> {
    if (!OBJECT_ID.test(from) || !OBJECT_ID.test(to)) throw new Error('not a tree');
    if (!(await isFolder(folder)))
      return {
        ok: false,
        code: 'location_missing',
        message: 'The folder is not there.',
        written: false,
      };
    if (await this.#holdsHostData(folder)) return { ...HOLDS_HOST_DATA, written: false };
    const repository = await this.#repository(projectId);
    const index = this.#indexFile(repository, 'apply');
    try {
      const seeded = await this.#run(repository, folder, index, ['read-tree', from]);
      const added = await this.#run(repository, folder, index, ['add', '-A', '--', '.']);
      const now = await this.#run(repository, folder, index, ['write-tree']);
      if (seeded.code !== 0 || added.code !== 0 || now.code !== 0)
        return {
          ok: false,
          code: 'store_failed',
          message: 'The folder could not be read.',
          written: false,
        };
      if (now.stdout.toString().trim() !== from)
        return {
          ok: false,
          code: 'folder_changed',
          message: 'The folder changed since it was read, so nothing was written. Try again.',
          written: false,
        };
      const moved = await this.#run(repository, folder, index, ['read-tree', '-m', '-u', from, to]);
      if (moved.code !== 0) {
        // git refuses before writing; a failure while writing could leave part written, so the
        // folder is read again before saying nothing was (AUDIT2 S3).
        const now = await this.treeOf(projectId, folder, from);
        return now.ok && now.tree === from
          ? {
              ok: false,
              code: 'folder_changed',
              message:
                'A file changed while the work was being added, so nothing was written. Try again.',
              written: false,
            }
          : {
              ok: false,
              code: 'incomplete',
              message: 'The add stopped part way: the folder may hold some of the work.',
              written: true,
            };
      }
    } finally {
      await rm(index, { force: true });
    }
    const after = await this.treeOf(projectId, folder, to);
    if (!after.ok || after.tree !== to)
      return {
        ok: false,
        code: 'incomplete',
        message: 'The folder does not hold every file as it should after the add.',
        written: true,
      };
    return { ok: true };
  }

  /**
   * What changed between two of a project's checkpoints, file by file, with the lines each gained
   * and lost (null for a binary file), sorted by path; reads only the store.
   */
  async compare(
    projectId: string,
    fromId: string,
    toId: string,
  ): Promise<
    | { readonly ok: true; readonly files: readonly ComparedFile[] }
    | { readonly ok: false; readonly code: 'checkpoint_missing' | 'store_failed' }
  > {
    const trees = await this.#trees(projectId, fromId, toId);
    if (!trees.ok) return trees;
    const files = await this.#diffTrees(trees.repository, trees.from, trees.to);
    return files === null ? { ok: false, code: 'store_failed' } : { ok: true, files };
  }

  /**
   * What changed between a checkpoint and its folder as it is now (History's "since this version"):
   * the folder is read into a tree in the store, as a checkpoint's is, but no checkpoint is named or
   * recorded. A file the checkpoint holds stays compared though an ignore file now names it.
   */
  async compareNow(
    projectId: string,
    fromId: string,
    folder: string,
  ): Promise<
    | { readonly ok: true; readonly files: readonly ComparedFile[] }
    | { readonly ok: false; readonly code: 'checkpoint_missing' | 'store_failed' | 'folder_unread' }
  > {
    const trees = await this.#treesNow(projectId, fromId, folder);
    if (!trees.ok) return trees;
    const files = await this.#diffTrees(trees.repository, trees.from, trees.to);
    return files === null ? { ok: false, code: 'store_failed' } : { ok: true, files };
  }

  /**
   * How many files differ between two trees of the project's store, by their ids as checkpoints
   * record them; null when the store cannot tell.
   */
  async countChanged(projectId: string, fromTree: string, toTree: string): Promise<number | null> {
    if (!ID_PATTERN.test(projectId) || !OBJECT_ID.test(fromTree) || !OBJECT_ID.test(toTree))
      return null;
    if (fromTree === toTree) return 0;
    const repository = this.#repositoryPath(projectId);
    if (!(await isFolder(repository))) return null;
    const listed = await this.#run(repository, null, null, [
      'diff-tree',
      '-r',
      '-z',
      '--no-renames',
      '--name-only',
      fromTree,
      toTree,
    ]);
    return listed.code === 0 ? splitNul(listed.stdout).length : null;
  }

  async #treesNow(
    projectId: string,
    fromId: string,
    folder: string,
  ): Promise<
    | { readonly ok: true; readonly repository: string; readonly from: string; readonly to: string }
    | { readonly ok: false; readonly code: 'checkpoint_missing' | 'folder_unread' }
  > {
    const held = await this.#trees(projectId, fromId, fromId);
    if (!held.ok) return held;
    const now = await this.treeOf(projectId, folder, held.from);
    if (!now.ok) return { ok: false, code: 'folder_unread' };
    return { ok: true, repository: held.repository, from: held.from, to: now.tree };
  }

  /** Files that differ between two trees, sorted by path, or null when git fails. */
  async #diffTrees(repository: string, from: string, to: string): Promise<ComparedFile[] | null> {
    const base = ['diff-tree', '-r', '-z', '--no-renames', '--no-ext-diff', '--no-textconv'];
    const [statuses, counts] = await Promise.all([
      this.#run(repository, null, null, [...base, '--name-status', from, to]),
      this.#run(repository, null, null, [...base, '--numstat', from, to]),
    ]);
    if (statuses.code !== 0 || counts.code !== 0) return null;
    const lines = new Map<string, { added: number | null; removed: number | null }>();
    // With -z, numstat writes "added<TAB>removed<TAB>path<NUL>", "-" for a binary file's counts.
    for (const record of splitNul(counts.stdout)) {
      const [added, removed, ...rest] = record.split('\t');
      const path = rest.join('\t');
      lines.set(path, {
        added: added === '-' ? null : Number(added),
        removed: removed === '-' ? null : Number(removed),
      });
    }
    const parts = splitNul(statuses.stdout);
    const files: ComparedFile[] = [];
    for (let index = 0; index + 1 < parts.length; index += 2) {
      const status = parts[index] as string;
      const path = parts[index + 1] as string;
      const counted = lines.get(path) ?? { added: null, removed: null };
      files.push({
        path,
        change: status === 'A' ? 'added' : status === 'D' ? 'removed' : 'changed',
        addedLines: counted.added,
        removedLines: counted.removed,
        binary: counted.added === null,
      });
    }
    files.sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));
    return files;
  }

  /**
   * One file's change between two checkpoints as a unified diff, at most `maxBytes`, or null when
   * the file is binary; reads only the store. Not found when the file did not change between them.
   */
  async compareFile(
    projectId: string,
    fromId: string,
    toId: string,
    path: string,
    maxBytes: number,
  ): Promise<
    | {
        readonly ok: true;
        readonly file: ComparedFile;
        readonly diff: string | null;
        readonly complete: boolean;
      }
    | {
        readonly ok: false;
        readonly code: 'checkpoint_missing' | 'not_changed' | 'store_failed';
      }
  > {
    if (!isProjectPath(path)) return { ok: false, code: 'not_changed' };
    const trees = await this.#trees(projectId, fromId, toId);
    if (!trees.ok) return trees;
    return this.#diffFile(trees.repository, trees.from, trees.to, path, maxBytes);
  }

  /** One file's change between a checkpoint and its folder as it is now, as `compareFile` gives it. */
  async compareFileNow(
    projectId: string,
    fromId: string,
    folder: string,
    path: string,
    maxBytes: number,
  ): Promise<
    | {
        readonly ok: true;
        readonly file: ComparedFile;
        readonly diff: string | null;
        readonly complete: boolean;
      }
    | {
        readonly ok: false;
        readonly code: 'checkpoint_missing' | 'not_changed' | 'store_failed' | 'folder_unread';
      }
  > {
    if (!isProjectPath(path)) return { ok: false, code: 'not_changed' };
    const trees = await this.#treesNow(projectId, fromId, folder);
    if (!trees.ok) return trees;
    return this.#diffFile(trees.repository, trees.from, trees.to, path, maxBytes);
  }

  async #diffFile(
    repository: string,
    from: string,
    to: string,
    path: string,
    maxBytes: number,
  ): Promise<
    | {
        readonly ok: true;
        readonly file: ComparedFile;
        readonly diff: string | null;
        readonly complete: boolean;
      }
    | { readonly ok: false; readonly code: 'not_changed' | 'store_failed' }
  > {
    const files = await this.#diffTrees(repository, from, to);
    if (files === null) return { ok: false, code: 'store_failed' };
    const file = files.find((item) => item.path === path);
    if (file === undefined) return { ok: false, code: 'not_changed' };
    if (file.binary) return { ok: true, file, diff: null, complete: true };
    const trees = { repository, from, to };
    const patch = await this.#run(
      trees.repository,
      null,
      null,
      [
        'diff-tree',
        '-p',
        '--no-renames',
        '--no-ext-diff',
        '--no-textconv',
        '--no-color',
        trees.from,
        trees.to,
        '--',
        path,
      ],
      undefined,
      maxBytes,
    );
    if (patch.code !== 0 && patch.truncated !== true) return { ok: false, code: 'store_failed' };
    const whole = patch.truncated !== true && patch.stdout.length <= maxBytes;
    // Cut at the last whole line inside the bound.
    let cut = whole ? patch.stdout : patch.stdout.subarray(0, maxBytes);
    if (!whole) {
      const lastBreak = cut.lastIndexOf(0x0a);
      if (lastBreak > 0) cut = cut.subarray(0, lastBreak + 1);
    }
    return { ok: true, file, diff: cut.toString('utf8'), complete: whole };
  }

  async #trees(
    projectId: string,
    fromId: string,
    toId: string,
  ): Promise<
    | { readonly ok: true; readonly repository: string; readonly from: string; readonly to: string }
    | { readonly ok: false; readonly code: 'checkpoint_missing' }
  > {
    if (!ID_PATTERN.test(projectId) || !ID_PATTERN.test(fromId) || !ID_PATTERN.test(toId))
      return { ok: false, code: 'checkpoint_missing' };
    const repository = this.#repositoryPath(projectId);
    if (!(await isFolder(repository))) return { ok: false, code: 'checkpoint_missing' };
    const trees: string[] = [];
    for (const id of [fromId, toId]) {
      const resolved = await this.#run(repository, null, null, [
        'rev-parse',
        '--verify',
        '-q',
        `${treeRef(id)}^{tree}`,
      ]);
      const tree = resolved.stdout.toString().trim();
      if (resolved.code !== 0 || !OBJECT_ID.test(tree))
        return { ok: false, code: 'checkpoint_missing' };
      trees.push(tree);
    }
    return { ok: true, repository, from: trees[0] as string, to: trees[1] as string };
  }

  /** What putting the checkpoint back would change now; reads only. */
  async preview(projectId: string, folder: string, checkpointId: string): Promise<PreviewResult> {
    const loaded = await this.#load(projectId, folder, checkpointId);
    if (!loaded.ok) return loaded;
    const { repository, tree, meta, entries } = loaded;
    const index = this.#indexFile(repository, `${checkpointId}-preview`);
    try {
      const state = await compare(folder, entries);
      const extras = await this.#extras(repository, folder, index, tree, meta);
      if (extras === null)
        return { ok: false, code: 'store_failed', message: 'The folder could not be listed.' };
      return {
        ok: true,
        tree,
        changedBack: state.differs,
        removed: extras,
        returned: state.missing,
        projectHeadMoved: headMoved(meta.head, await readProjectHead(folder)),
      };
    } finally {
      await rm(index, { force: true });
    }
  }

  /**
   * Puts every file back as the checkpoint holds it, removes what it lacks, then reads every file
   * again. Only an equal folder is a completed restore; anything else says what still differs.
   */
  async restore(
    projectId: string,
    folder: string,
    checkpointId: string,
    hooks: RestoreHooks = {},
  ): Promise<RestoreResult> {
    const loaded = await this.#load(projectId, folder, checkpointId);
    if (!loaded.ok) return { ...loaded, written: false, notRestored: [] };
    const { repository, tree, meta, entries } = loaded;
    const index = this.#indexFile(repository, `${checkpointId}-restore`);
    let written = false;
    try {
      const before = await compare(folder, entries);
      const removed = await this.#extras(repository, folder, index, tree, meta);
      if (removed === null)
        return incomplete(false, [], 'store_failed', 'The folder could not be listed.');

      written = true;
      await clearWay(folder, entries);
      const read = await this.#run(repository, folder, index, ['read-tree', tree]);
      if (read.code !== 0)
        return incomplete(true, [], 'store_failed', 'The checkpoint could not be read.');
      // Exits non-zero when a file cannot be written; the check below names which.
      await this.#run(repository, folder, index, ['checkout-index', '-a', '-f', '-q']);
      const extras = await this.#extras(repository, folder, index, tree, meta);
      for (const extra of extras ?? []) await removeInside(folder, extra);
      await hooks.onWritten?.();

      const after = await compare(folder, entries);
      const left = await this.#extras(repository, folder, index, tree, meta);
      const differing = sorted([...after.differs, ...after.missing, ...(left ?? [])]);
      if (left === null || differing.length > 0)
        return incomplete(
          true,
          differing,
          'not_equal',
          differing.length === 1
            ? '1 file is not back as it was.'
            : `${differing.length} files are not back as they were.`,
        );
      return {
        ok: true,
        tree,
        compared: entries.length,
        changedBack: before.differs,
        removed,
        returned: before.missing,
        projectHeadMoved: headMoved(meta.head, await readProjectHead(folder)),
      };
    } catch {
      return incomplete(written, [], 'store_failed', 'The restore stopped on an error.');
    } finally {
      await rm(index, { force: true });
    }
  }

  /** Every path that differs from the checkpoint now: changed, missing, or not in it. */
  async verify(
    projectId: string,
    folder: string,
    checkpointId: string,
  ): Promise<{ compared: number; differing: string[] }> {
    const loaded = await this.#load(projectId, folder, checkpointId);
    if (!loaded.ok) throw new Error(loaded.message);
    const { repository, tree, meta, entries } = loaded;
    const index = this.#indexFile(repository, `${checkpointId}-verify`);
    try {
      const state = await compare(folder, entries);
      const extras = await this.#extras(repository, folder, index, tree, meta);
      if (extras === null) throw new Error('The folder could not be listed.');
      return {
        compared: entries.length,
        differing: sorted([...state.differs, ...state.missing, ...extras]),
      };
    } finally {
      await rm(index, { force: true });
    }
  }

  /**
   * Whether the project's folder holds the store or a folder the host protects: a checkpoint would
   * keep its journal and token, and a restore would write them back or remove them.
   */
  async #holdsHostData(folder: string): Promise<boolean> {
    const real = await realpath(folder);
    for (const path of this.#protect) {
      const protectedPath = await resolvedThroughExisting(path);
      if (within(real, protectedPath)) return true;
    }
    return false;
  }

  async #load(
    projectId: string,
    folder: string,
    checkpointId: string,
  ): Promise<
    | { ok: true; repository: string; tree: string; meta: Meta; entries: TreeEntry[] }
    | { ok: false; code: string; message: string }
  > {
    if (!(await isFolder(folder)))
      return {
        ok: false,
        code: 'location_missing',
        message: "The project's folder is not there, or is not a folder.",
      };
    if (await this.#holdsHostData(folder)) return HOLDS_HOST_DATA;
    const repository = this.#repositoryPath(projectId);
    if (!ID_PATTERN.test(checkpointId) || !(await isFolder(repository)))
      return { ok: false, code: 'checkpoint_missing', message: 'The checkpoint is not kept here.' };
    const resolved = await this.#run(repository, null, null, [
      'rev-parse',
      '--verify',
      '-q',
      `${treeRef(checkpointId)}^{tree}`,
    ]);
    const tree = resolved.stdout.toString().trim();
    const blob = await this.#run(repository, null, null, [
      'cat-file',
      'blob',
      metaRef(checkpointId),
    ]);
    if (resolved.code !== 0 || !OBJECT_ID.test(tree) || blob.code !== 0)
      return { ok: false, code: 'checkpoint_missing', message: 'The checkpoint is not kept here.' };
    const meta = JSON.parse(blob.stdout.toString()) as Meta;
    if (meta.format !== 1 || meta.folder !== (await realpath(folder)))
      return {
        ok: false,
        code: 'location_moved',
        message: "The checkpoint was taken of another folder than the project's folder now.",
      };
    const listed = await this.#run(repository, null, null, [
      'ls-tree',
      '-r',
      '-z',
      '--full-tree',
      tree,
    ]);
    if (listed.code !== 0)
      return { ok: false, code: 'store_failed', message: 'The checkpoint could not be read.' };
    return {
      ok: true,
      repository,
      tree,
      meta,
      entries: splitNul(listed.stdout).map(parseTreeLine),
    };
  }

  /**
   * Untracked paths the checkpoint did not ignore: listed with no ignore rules at all, then the
   * paths ignored when it was taken left out, by exact entry or by an ignored folder above them.
   */
  async #extras(
    repository: string,
    folder: string,
    index: string,
    tree: string,
    meta: Meta,
  ): Promise<string[] | null> {
    const read = await this.#run(repository, folder, index, ['read-tree', tree]);
    if (read.code !== 0) return null;
    const listed = await this.#run(repository, folder, index, [
      'ls-files',
      '-z',
      '--others',
      '--directory',
    ]);
    if (listed.code !== 0) return null;
    const ignored = new Set(meta.ignored);
    const ignoredFolders = meta.ignored.filter((entry) => entry.endsWith('/'));
    const isIgnored = (path: string) =>
      ignored.has(path) || ignoredFolders.some((entry) => path.startsWith(entry));
    const extras: string[] = [];
    for (const path of splitNul(listed.stdout)) {
      if (isIgnored(path)) continue;
      const within = meta.ignored.some((entry) => entry !== path && entry.startsWith(path));
      if (!path.endsWith('/') || !within) {
        extras.push(path);
        continue;
      }
      // A folder untracked as a whole that held ignored paths: judge its files one by one.
      const files = await this.#run(repository, folder, index, [
        'ls-files',
        '-z',
        '--others',
        '--',
        path,
      ]);
      if (files.code !== 0) return null;
      for (const file of splitNul(files.stdout)) if (!isIgnored(file)) extras.push(file);
    }
    return sorted(extras);
  }

  async #totalSize(repository: string, entries: readonly TreeEntry[]): Promise<number | null> {
    if (entries.length === 0) return 0;
    const sizes = await this.#run(
      repository,
      null,
      null,
      ['cat-file', '--batch-check=%(objectsize)'],
      Buffer.from(`${entries.map((entry) => entry.oid).join('\n')}\n`),
    );
    if (sizes.code !== 0) return null;
    return sizes.stdout
      .toString()
      .trim()
      .split('\n')
      .reduce((sum, line) => sum + Number(line), 0);
  }

  async #repository(projectId: string): Promise<string> {
    const repository = this.#repositoryPath(projectId);
    const attributes = join(repository, 'info', 'attributes');
    if ((await isFolder(repository)) && (await present(attributes))) return repository;
    await mkdir(this.#directory, { recursive: true, mode: 0o700 });
    const init = await this.#spawn(
      ['init', '-q', '--bare', '--object-format=sha256', repository],
      { repository, project: null },
      null,
    );
    // A task's copy and a checkpoint may make the project's repository at once (ADR 0045): the
    // one that lost finds it made, and writes the same attributes before any git reads the files.
    if (init.code !== 0 && !(await isFolder(repository)))
      throw new Error('the checkpoint store could not be made');
    await mkdir(join(repository, 'info'), { recursive: true, mode: 0o700 });
    await writeFile(attributes, STORE_ATTRIBUTES, { mode: 0o600 });
    return repository;
  }

  #repositoryPath(projectId: string): string {
    if (!ID_PATTERN.test(projectId)) throw new Error('not a project id');
    return join(this.#directory, `${projectId}.git`);
  }

  #indexFile(repository: string, name: string): string {
    return join(repository, `index-${name}-${process.pid}-${Date.now()}`);
  }

  #run(
    repository: string,
    folder: string | null,
    index: string | null,
    args: readonly string[],
    input?: Buffer,
    maxStdout?: number,
  ): Promise<GitResult> {
    return this.#spawn(
      [
        `--git-dir=${repository}`,
        ...(folder === null ? [] : [`--work-tree=${folder}`]),
        '--literal-pathspecs',
        ...args,
      ],
      { repository, project: folder },
      index,
      input,
      maxStdout,
    );
  }

  /** Git as the store runs it, found once; a git that could not be found is looked for again next time. */
  #runnable(): Promise<RunnableGit> {
    this.#found ??= runnableGit(this.#git);
    const finding = this.#found;
    finding.catch(() => {
      if (this.#found === finding) this.#found = null;
    });
    return finding;
  }

  /**
   * Runs git with none of the person's settings: no system or global configuration, a home of the
   * store's own, no hooks, no file system monitor, and no conversion of line endings. On macOS it runs
   * inside `gitProfile`, reading and writing only the project's repository and the project.
   */
  async #spawn(
    args: readonly string[],
    scope: { readonly repository: string; readonly project: string | null },
    index: string | null,
    input?: Buffer,
    maxStdout?: number,
  ): Promise<GitResult> {
    const { git, tools } = await this.#runnable();
    const home = join(this.#directory, '.home');
    const settings = [
      'core.fsmonitor=false',
      'core.hooksPath=/dev/null',
      'core.autocrlf=false',
      'core.safecrlf=false',
      'core.symlinks=true',
      'core.filemode=true',
      'core.untrackedCache=false',
      'gc.auto=0',
    ].flatMap((setting) => ['-c', setting]);
    const [file, ...argv] =
      tools === null
        ? [git, ...settings, ...args]
        : [
            SANDBOX_EXEC,
            '-p',
            gitProfile({ tools, repository: scope.repository, home, project: scope.project }),
            git,
            ...settings,
            ...args,
          ];
    // Git reads the folder it runs in, so it runs in one its profile reads: the project, else the
    // repository, except for the init that makes the repository.
    const cwd = scope.project ?? (args[0] === 'init' ? this.#directory : scope.repository);
    return new Promise((resolvePromise, reject) => {
      const child = spawn(file as string, argv, {
        cwd,
        env: {
          PATH: '/usr/bin:/bin',
          HOME: home,
          LC_ALL: 'C',
          GIT_CONFIG_NOSYSTEM: '1',
          GIT_CONFIG_GLOBAL: '/dev/null',
          GIT_TERMINAL_PROMPT: '0',
          GIT_OPTIONAL_LOCKS: '0',
          ...(index === null ? {} : { GIT_INDEX_FILE: index }),
        },
        stdio: ['pipe', 'pipe', 'pipe'],
      });
      // A timer of our own, cleared however git ends: spawn's `timeout` option is cleared only on
      // `exit`, so a git that never started would keep the process alive until it fired.
      const timer = setTimeout(() => child.kill('SIGKILL'), GIT_TIMEOUT_MS);
      const out: Buffer[] = [];
      const err: Buffer[] = [];
      let outBytes = 0;
      let truncated = false;
      child.stdout.on('data', (chunk: Buffer) => {
        if (truncated) return;
        out.push(chunk);
        outBytes += chunk.length;
        // Past the most the caller reads, git is stopped rather than read to its end.
        if (maxStdout !== undefined && outBytes > maxStdout) {
          truncated = true;
          child.kill('SIGKILL');
        }
      });
      child.stderr.on('data', (chunk: Buffer) => err.push(chunk));
      // Writing to a git that already exited is that git's failure, which `close` reports.
      child.stdin.on('error', () => {});
      child.on('error', (error) => {
        clearTimeout(timer);
        reject(error);
      });
      child.on('close', (code) => {
        clearTimeout(timer);
        resolvePromise({
          code: code ?? -1,
          stdout: Buffer.concat(out),
          stderr: Buffer.concat(err).toString(),
          ...(truncated && { truncated: true }),
        });
      });
      child.stdin.end(input ?? Buffer.alloc(0));
    });
  }
}

/** Whether the path is a folder itself, not a link to one. */
async function isFolder(path: string): Promise<boolean> {
  try {
    return (await lstat(path)).isDirectory();
  } catch {
    return false;
  }
}

/** Each checkpoint path's file as it is now: equal, different, or missing. */
async function compare(
  folder: string,
  entries: readonly TreeEntry[],
): Promise<{ differs: string[]; missing: string[] }> {
  const differs: string[] = [];
  const missing: string[] = [];
  const linked = new Map<string, boolean>();
  // Each file is read refusing a link anywhere in its path (U4), so the reads start from the
  // folder's real path: a link above it, such as /var, is the system's, not a swap.
  let base: string;
  try {
    base = await realpath(folder);
  } catch {
    base = folder;
  }
  for (const entry of entries) {
    if (await underLink(base, entry.path, linked)) {
      missing.push(entry.path);
      continue;
    }
    const path = join(base, entry.path);
    let stats: Awaited<ReturnType<typeof lstat>>;
    try {
      stats = await lstat(path);
    } catch {
      missing.push(entry.path);
      continue;
    }
    if (entry.mode === '120000') {
      const same =
        stats.isSymbolicLink() &&
        blobId(await readlink(path, { encoding: 'buffer' })) === entry.oid;
      if (!same) differs.push(entry.path);
      continue;
    }
    const executable = entry.mode === '100755';
    const same =
      stats.isFile() &&
      ((stats.mode & 0o100) !== 0) === executable &&
      (await fileBlobId(path, stats.size)) === entry.oid;
    if (!same) differs.push(entry.path);
  }
  return { differs: sorted(differs), missing: sorted(missing) };
}

/** Whether a folder above the path, inside the project's folder, is a link or not a folder. */
async function underLink(
  folder: string,
  relative: string,
  cache: Map<string, boolean>,
): Promise<boolean> {
  const parts = relative.split('/');
  let current = '';
  for (const part of parts.slice(0, -1)) {
    current = current === '' ? part : `${current}/${part}`;
    let blocked = cache.get(current);
    if (blocked === undefined) {
      try {
        blocked = !(await lstat(join(folder, current))).isDirectory();
      } catch {
        blocked = false;
      }
      cache.set(current, blocked);
    }
    if (blocked) return true;
  }
  return false;
}

/**
 * Before git writes: whatever stands where the checkpoint needs a folder (a link, a file) is
 * removed, never followed, and a folder where it needs a file is removed, so no write goes through
 * a link out of the project.
 */
async function clearWay(folder: string, entries: readonly TreeEntry[]): Promise<void> {
  const cleared = new Set<string>();
  for (const entry of entries) {
    const parts = entry.path.split('/');
    let current = '';
    for (const part of parts.slice(0, -1)) {
      current = current === '' ? part : `${current}/${part}`;
      if (cleared.has(current)) continue;
      cleared.add(current);
      try {
        const stats = await lstat(join(folder, current));
        if (!stats.isDirectory()) await removeInside(folder, current);
      } catch {
        // Not there: git makes it.
      }
    }
    try {
      if ((await lstat(join(folder, entry.path))).isDirectory())
        await removeInside(folder, entry.path);
    } catch {
      // Not there, or not a folder: git writes it.
    }
  }
}

/**
 * Removes one path inside the folder, a link as a link and a folder with what it holds. Immediately
 * before, every folder above it must still be a real folder inside the project, not a link a process
 * left running put there since the path was listed; otherwise nothing is removed and it throws. That
 * check only narrows the time a link could be put there; on macOS the removal itself runs inside
 * `removalProfile`, so a link put there after it leads nowhere the removal may write (U2), and a path
 * still there afterwards throws.
 */
export async function removeInside(folder: string, relative: string): Promise<void> {
  const trimmed = relative.endsWith('/') ? relative.slice(0, -1) : relative;
  if (!isProjectPath(trimmed)) throw new Error('not a path inside the project');
  const path = join(folder, trimmed);
  if (!path.startsWith(folder + sep)) throw new Error('not a path inside the project');
  const parts = trimmed.split('/');
  let current = folder;
  for (const part of parts.slice(0, -1)) {
    current = join(current, part);
    if (!(await lstat(current)).isDirectory())
      throw new Error(
        'a folder in the project changed to something else while files were put back',
      );
  }
  const realFolder = await realpath(folder);
  const realParent = await realpath(dirname(path));
  if (realParent !== realFolder && !realParent.startsWith(realFolder + sep))
    throw new Error('a folder in the project now leads outside it');
  if (process.platform !== 'darwin') {
    await rm(path, { recursive: true, force: true });
    return;
  }
  await removeConfined(realFolder, path);
  if (await present(path))
    throw new Error('a folder in the project changed while files were put back, so a path stayed');
}

/** Removes `path` inside `removalProfile(project)`, whatever is there, as `rm -rf` does. */
function removeConfined(project: string, path: string): Promise<void> {
  return new Promise((resolvePromise, reject) => {
    execFile(
      SANDBOX_EXEC,
      ['-p', removalProfile(project), '/bin/rm', '-rf', '--', path],
      { env: { PATH: '/usr/bin:/bin' }, timeout: GIT_TIMEOUT_MS },
      (error) => (error === null ? resolvePromise() : reject(error)),
    );
  });
}

/** Whether anything is at the path, a link included, as the path resolves now. */
async function present(path: string): Promise<boolean> {
  try {
    await lstat(path);
    return true;
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code;
    if (code === 'ENOENT' || code === 'ENOTDIR') return false;
    throw error;
  }
}

/** Whether `path` is `base` or inside it, `/` included. Exported for its tests. */
export function within(base: string, path: string): boolean {
  return path === base || path.startsWith(base.endsWith(sep) ? base : base + sep);
}

/** The path as the file system resolves it, through its nearest folder that exists. */
async function resolvedThroughExisting(path: string): Promise<string> {
  const rest: string[] = [];
  let existing = path;
  while (true) {
    try {
      return join(await realpath(existing), ...rest.reverse());
    } catch {
      const parent = dirname(existing);
      if (parent === existing) return path;
      rest.push(basename(existing));
      existing = parent;
    }
  }
}

const HOLDS_HOST_DATA = {
  ok: false,
  code: 'holds_host_data',
  message:
    "The project's folder holds Halcyonic's own data, so it cannot be checkpointed or put back.",
} as const;

function isProjectPath(path: string): boolean {
  return (
    path.length > 0 &&
    !path.startsWith('/') &&
    !path.includes('\0') &&
    path.split('/').every((part) => part !== '..' && part !== '.')
  );
}

/** Git's SHA-256 blob id of content held in memory. */
function blobId(content: Buffer): string {
  return createHash('sha256').update(`blob ${content.length}\0`).update(content).digest('hex');
}

/** Git's SHA-256 blob id of a file, read in full from disk. */
async function fileBlobId(path: string, size: number): Promise<string> {
  let handle: Awaited<ReturnType<typeof open>>;
  try {
    handle = await open(path, constants.O_RDONLY | constants.O_NONBLOCK | NO_LINK);
  } catch (error) {
    // A link where the file or, on macOS, a folder above it stands: not this checkpoint's file, so
    // never equal to it, whatever the link leads to (U4).
    if ((error as NodeJS.ErrnoException).code === 'ELOOP') return 'not-a-file';
    throw error;
  }
  return new Promise((resolvePromise, reject) => {
    const hash = createHash('sha256').update(`blob ${size}\0`);
    let read = 0;
    handle
      .createReadStream()
      .on('data', (chunk: Buffer | string) => {
        const bytes = typeof chunk === 'string' ? Buffer.from(chunk) : chunk;
        read += bytes.length;
        hash.update(bytes);
      })
      .on('error', reject)
      // A file that changed size while read cannot match the size its header named.
      .on('end', () => resolvePromise(read === size ? hash.digest('hex') : 'size-changed'));
  });
}

/**
 * The commit the project's own repository points at, read from its files without running git
 * there (its configuration could run programs), or null when there is none or it cannot be told.
 */
async function readProjectHead(folder: string): Promise<string | null> {
  const gitDir = join(folder, '.git');
  if (!(await isFolder(gitDir))) return null;
  try {
    const head = (await readFile(join(gitDir, 'HEAD'), 'utf8')).trim();
    if (OBJECT_ID.test(head)) return head;
    const ref = /^ref: (refs\/[A-Za-z0-9._\-/]+)$/.exec(head)?.[1];
    if (ref === undefined || ref.split('/').includes('..')) return null;
    try {
      const loose = (await readFile(join(gitDir, ref), 'utf8')).trim();
      if (OBJECT_ID.test(loose)) return loose;
    } catch {
      // Not loose: look in packed-refs.
    }
    const packed = await readFile(join(gitDir, 'packed-refs'), 'utf8').catch(() => '');
    for (const line of packed.split('\n')) {
      const [id, name] = line.split(' ');
      if (name === ref && id !== undefined && OBJECT_ID.test(id)) return id;
    }
    return null;
  } catch {
    return null;
  }
}

function headMoved(then: string | null, now: string | null): boolean | null {
  return then === null || now === null ? null : then !== now;
}

function treeRef(checkpointId: string): string {
  return `refs/halcyonic/trees/${checkpointId}`;
}

function metaRef(checkpointId: string): string {
  return `refs/halcyonic/meta/${checkpointId}`;
}

function splitNul(output: Buffer): string[] {
  return output
    .toString()
    .split('\0')
    .filter((part) => part.length > 0);
}

/** `<mode> <oid> <stage>\t<path>` from `ls-files -s`. */
function parseStageLine(line: string): TreeEntry {
  const tab = line.indexOf('\t');
  const [mode = '', oid = ''] = line.slice(0, tab).split(' ');
  return { mode, oid, path: line.slice(tab + 1) };
}

/** `<mode> <type> <oid>\t<path>` from `ls-tree`. */
function parseTreeLine(line: string): TreeEntry {
  const tab = line.indexOf('\t');
  const [mode = '', , oid = ''] = line.slice(0, tab).split(' ');
  return { mode, oid, path: line.slice(tab + 1) };
}

function failedAdd(stderr: string): TakeResult {
  if (/does not have a commit checked out|embedded git repository/.test(stderr))
    return {
      ok: false,
      code: 'nested_repository',
      message: 'Another git repository sits inside the folder, so its files cannot be kept.',
    };
  if (/Permission denied|unable to (?:read|open|index)|failed to (?:read|open)/.test(stderr))
    return { ok: false, code: 'unreadable', message: 'A file in the folder could not be read.' };
  return storeFailed('The checkpoint could not be taken.');
}

function storeFailed(message: string): TakeResult {
  return { ok: false, code: 'store_failed', message };
}

function incomplete(
  written: boolean,
  notRestored: string[],
  code: string,
  message: string,
): RestoreResult {
  return { ok: false, written, notRestored, code, message };
}

/** Git's own order: bytewise. */
function sorted(paths: readonly string[]): string[] {
  return [...new Set(paths)].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
}

function formatBytes(bytes: number): string {
  return bytes >= 1024 ** 3 ? `${bytes / 1024 ** 3} GB` : `${Math.round(bytes / 1024 ** 2)} MB`;
}

/**
 * The files a zip holds, folders left out, from its own central directory; null where it is not a
 * zip this reads (no end record, or ZIP64). Exported for its tests.
 */
export function zipFileCount(zip: Buffer): number | null {
  const end = zip.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  if (end < 0 || end + 22 > zip.length) return null;
  const entries = zip.readUInt16LE(end + 10);
  const offset = zip.readUInt32LE(end + 16);
  if (entries === 0xffff || offset === 0xffffffff) return null;
  let at = offset;
  let files = 0;
  for (let i = 0; i < entries; i += 1) {
    if (at + 46 > zip.length || zip.readUInt32LE(at) !== 0x02014b50) return null;
    const nameLength = zip.readUInt16LE(at + 28);
    const extra = zip.readUInt16LE(at + 30);
    const comment = zip.readUInt16LE(at + 32);
    const name = zip.subarray(at + 46, at + 46 + nameLength).toString('utf8');
    if (!name.endsWith('/')) files += 1;
    at += 46 + nameLength + extra + comment;
  }
  return files;
}
