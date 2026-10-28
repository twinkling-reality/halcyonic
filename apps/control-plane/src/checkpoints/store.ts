import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { lstat, mkdir, readFile, readlink, realpath, rm, writeFile } from 'node:fs/promises';
import { join, sep } from 'node:path';
import type { CheckpointFailureCode } from '@halcyonic/contracts';

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
}

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
}

const ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const OBJECT_ID = /^(?:[0-9a-f]{40}|[0-9a-f]{64})$/;

export class CheckpointStore {
  readonly #git: string;
  readonly #directory: string;
  readonly #limits: CheckpointLimits;

  constructor(options: CheckpointStoreOptions) {
    this.#git = options.git;
    this.#directory = options.directory;
    this.#limits = options.limits ?? DEFAULT_CHECKPOINT_LIMITS;
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
    if (await isFolder(repository)) return repository;
    await mkdir(this.#directory, { recursive: true, mode: 0o700 });
    const init = await this.#spawn(
      ['init', '-q', '--bare', '--object-format=sha256', repository],
      null,
      null,
    );
    if (init.code !== 0) throw new Error('the checkpoint store could not be made');
    await writeFile(join(repository, 'info', 'attributes'), STORE_ATTRIBUTES, { mode: 0o600 });
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
  ): Promise<GitResult> {
    return this.#spawn(
      [
        `--git-dir=${repository}`,
        ...(folder === null ? [] : [`--work-tree=${folder}`]),
        '--literal-pathspecs',
        ...args,
      ],
      folder,
      index,
      input,
    );
  }

  /**
   * Runs git with none of the person's settings: no system or global configuration, a home of the
   * store's own, no hooks, no file system monitor, and no conversion of line endings.
   */
  #spawn(
    args: readonly string[],
    cwd: string | null,
    index: string | null,
    input?: Buffer,
  ): Promise<GitResult> {
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
    return new Promise((resolvePromise, reject) => {
      const child = spawn(this.#git, [...settings, ...args], {
        cwd: cwd ?? this.#directory,
        env: {
          PATH: '/usr/bin:/bin',
          HOME: join(this.#directory, '.home'),
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
      child.stdout.on('data', (chunk: Buffer) => out.push(chunk));
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
  for (const entry of entries) {
    if (await underLink(folder, entry.path, linked)) {
      missing.push(entry.path);
      continue;
    }
    const path = join(folder, entry.path);
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

/** Removes one path inside the folder, a link as a link and a folder with what it holds. */
async function removeInside(folder: string, relative: string): Promise<void> {
  const trimmed = relative.endsWith('/') ? relative.slice(0, -1) : relative;
  if (!isProjectPath(trimmed)) throw new Error('not a path inside the project');
  const path = join(folder, trimmed);
  if (!path.startsWith(folder + sep)) throw new Error('not a path inside the project');
  await rm(path, { recursive: true, force: true });
}

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
function fileBlobId(path: string, size: number): Promise<string> {
  return new Promise((resolvePromise, reject) => {
    const hash = createHash('sha256').update(`blob ${size}\0`);
    let read = 0;
    createReadStream(path)
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
