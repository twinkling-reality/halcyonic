import { execFile } from 'node:child_process';
import { lstat, mkdir, readdir, realpath, rmdir } from 'node:fs/promises';
import { basename, join } from 'node:path';
import type { ChangedFile, CopyRefusalCode, Timestamp } from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import type { EventPublisher } from '../core/publisher.ts';
import type { Recorder } from '../core/recorder.ts';
import { changedFileList, pathList } from './checkpointer.ts';
import type { CheckpointStore, MergeConflict } from './store.ts';
import { removeInside, within } from './store.ts';

/**
 * Every task's own copy of its project's folder (ADR 0045): an APFS clone, made just before the
 * task's first start, in a folder of Halcyonic's own beside its data folder,
 * `<directory>/<project id>/<task id>/<the folder's own name>`. Ignored files come too, so
 * dependencies and builds work. The copy's base is a tree in the project's checkpoint store, taken
 * of the copy as it was made, so the store's limits, its own git and its confinement decide what a
 * copy may hold. Nothing is written to the project's folder or its `.git`.
 */

export interface TaskCopiesOptions {
  /** The folder copies live in. Never inside a project's folder nor Halcyonic's data folder. */
  readonly directory: string;
  readonly store: Pick<
    CheckpointStore,
    'take' | 'treeOf' | 'keep' | 'changedPaths' | 'changedFiles' | 'merge' | 'apply'
  >;
  /** A UUID naming the base's tree in the store, as a checkpoint's id does. */
  readonly nextId: () => string;
  /** Folders no copied project may hold, such as Halcyonic's data folder. */
  readonly protect?: readonly string[];
  /** How long one copy may take before it is stopped and refused. */
  readonly timeoutMs?: number;
  /**
   * Whether the runtime is held to `directory` on this computer, so that in a copy it writes only
   * that copy. Absent, no runtime is, and no task gets a copy.
   */
  readonly heldToFolder?: (runtimeId: string, directory: string) => boolean;
  /** The git that edits a copy's own settings, by absolute path; the system's by default. */
  readonly git?: string;
}

export type PrepareResult =
  | {
      readonly ok: true;
      readonly directory: string;
      readonly baseTree: string;
      readonly files: number;
    }
  | { readonly ok: false; readonly code: CopyRefusalCode; readonly message: string };

const ID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const CP = '/bin/cp';
const DEFAULT_TIMEOUT_MS = 10 * 60_000;

export class TaskCopies {
  readonly #directory: string;
  readonly #store: TaskCopiesOptions['store'];
  readonly #queues = new Map<string, Promise<unknown>>();
  readonly #nextId: () => string;
  readonly #protect: readonly string[];
  readonly #timeoutMs: number;
  readonly #heldToFolder: (runtimeId: string, directory: string) => boolean;
  readonly #git: string;

  constructor(options: TaskCopiesOptions) {
    this.#directory = options.directory;
    this.#store = options.store;
    this.#nextId = options.nextId;
    this.#protect = [options.directory, ...(options.protect ?? [])];
    this.#timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
    this.#heldToFolder = options.heldToFolder ?? (() => false);
    this.#git = options.git ?? '/usr/bin/git';
  }

  /**
   * Whether a task of `runtimeId` may work in a copy of `directory` (ADR 0045): only where that
   * runtime is held to the folder it is given, so no agent writes another task's copy.
   */
  holds(runtimeId: string, directory: string): boolean {
    return this.#heldToFolder(runtimeId, directory);
  }

  /** The folder copies live in, as configured. */
  get directory(): string {
    return this.#directory;
  }

  /**
   * Makes the task's copy of `folder`, the project's folder as the host resolved it, and keeps its
   * base. A copy left by an earlier attempt that never recorded one is replaced. Refused, with
   * nothing left behind, where a copy could still act on the person's repository, could hold
   * Halcyonic's own data, or is more than a checkpoint keeps.
   */
  async prepare(projectId: string, workstreamId: string, folder: string): Promise<PrepareResult> {
    if (!ID_PATTERN.test(projectId) || !ID_PATTERN.test(workstreamId))
      throw new Error('not a project or task id');
    const source = await lstat(folder).catch(() => null);
    if (source === null || !source.isDirectory())
      return refuse('location_missing', "The project's folder is not there.");
    const git = await lstat(join(folder, '.git')).catch(() => null);
    if (git !== null && !git.isDirectory())
      return refuse(
        'linked_repository',
        "The folder's .git is a file, so a copy would still change the repository it points to.",
      );
    const real = await realpath(folder);
    for (const path of this.#protect) {
      if (within(real, await resolvedOrSelf(path)))
        return refuse(
          'holds_host_data',
          "The folder holds Halcyonic's own data, which is never copied.",
        );
    }

    await mkdir(this.#directory, { recursive: true, mode: 0o700 });
    const root = await realpath(this.#directory);
    const parent = join(root, projectId, workstreamId);
    await mkdir(parent, { recursive: true, mode: 0o700 });
    // The task's own folder must be ours all the way down, never a link out of the copies' folder.
    if ((await realpath(parent)) !== parent)
      return refuse('copy_failed', "The task's folder for its copy is not Halcyonic's own.");
    const target = join(parent, basename(real));
    // Whatever an earlier attempt left here goes, removed only inside the copies' folder.
    await removeInside(root, `${projectId}/${workstreamId}`);
    await mkdir(parent, { recursive: true, mode: 0o700 });

    const copied = await this.#clone(real, target);
    if (!copied.ok) {
      await removeInside(root, `${projectId}/${workstreamId}`);
      return copied;
    }
    if (!(await this.#withoutCredentials(target))) {
      await removeInside(root, `${projectId}/${workstreamId}`);
      return refuse('copy_failed', "The copy's git settings could not be made safe.");
    }
    const base = await this.#store.take(projectId, target, this.#nextId());
    if (!base.ok) {
      await removeInside(root, `${projectId}/${workstreamId}`);
      return refuse(refusalOf(base.code), base.message);
    }
    return { ok: true, directory: target, baseTree: base.tree, files: base.files };
  }

  /**
   * Removes every copy no task holds (ADR 0045): what a start left when it stopped before its copy
   * was recorded, or what a journal that no longer names it left behind. Only a folder at
   * `<project id>/<task id>` inside the copies' folder, both names ids and both real folders, is
   * ever removed, and only when no path in `kept` lies inside it; removal runs confined to the
   * copies' folder. Anything else there is left as it is and counted. Run before any start can
   * make a copy, so a copy being made is never taken for one no task holds.
   */
  async sweep(kept: ReadonlySet<string>): Promise<{ removed: number; left: number }> {
    let root: string;
    try {
      root = await realpath(this.#directory);
    } catch {
      return { removed: 0, left: 0 };
    }
    let removed = 0;
    let left = 0;
    const realDirectory = async (path: string) =>
      (await lstat(path).catch(() => null))?.isDirectory() === true;
    for (const project of await readdir(root)) {
      const projectPath = join(root, project);
      if (!ID_PATTERN.test(project) || !(await realDirectory(projectPath))) {
        left += 1;
        continue;
      }
      for (const task of await readdir(projectPath)) {
        const taskPath = join(projectPath, task);
        if (!ID_PATTERN.test(task) || !(await realDirectory(taskPath))) {
          left += 1;
          continue;
        }
        if ([...kept].some((path) => within(taskPath, path))) continue;
        await removeInside(root, `${project}/${task}`);
        removed += 1;
      }
      // An empty project folder goes too; one still holding anything stays.
      await rmdir(projectPath).catch(() => undefined);
    }
    return { removed, left };
  }

  /**
   * What the copy now differs by from its base: its tree, read with the base as the index's start,
   * and the files that differ, with how each changed. Null when it cannot be read.
   */
  readChanges(
    projectId: string,
    workstreamId: string,
    copy: string,
    base: string,
  ): Promise<{ tree: string; changed: string[]; files: ChangedFile[] } | null> {
    return this.#serially(workstreamId, async () => {
      const task = await this.#store.treeOf(projectId, copy, base);
      if (!task.ok) return null;
      const files = await this.#store.changedFiles(projectId, base, task.tree);
      return files === null
        ? null
        : { tree: task.tree, changed: files.map((file) => file.path), files };
    });
  }

  /**
   * Combines the task's work in `copy` with the project's `folder` over `base` (ADR 0045), and,
   * when they meet nowhere, has `keepFirst` keep the folder (its checkpoint id, or null to stop),
   * then writes the combined files into the folder and reads it again. Nothing is written to the
   * folder when the two met, when the task changed nothing, or when the folder changed meanwhile.
   */
  add(
    projectId: string,
    workstreamId: string,
    request: { copy: string; base: string; folder: string; reviewed: string | null },
    keepFirst: () => Promise<string | null>,
  ): Promise<AddOutcome> {
    return this.#serially(workstreamId, async (): Promise<AddOutcome> => {
      const read = await this.#both(projectId, request);
      if (read.kind !== 'read') return read;
      const { task, project, merged } = read;
      if (task === request.base) return { kind: 'nothing' };
      // Only what the person reviewed is ever added: a copy changed since its last reading, by its
      // agent or anything else, is read again and shown before anything is written.
      if (task !== request.reviewed) {
        const files = await this.#store.changedFiles(projectId, request.base, task);
        if (files === null) return failed('store_failed', 'The changes could not be read.');
        return {
          kind: 'unreviewed',
          taskTree: task,
          changed: files.map((file) => file.path),
          files,
        };
      }
      if (merged.conflicts.length > 0)
        return {
          kind: 'conflicted',
          taskTree: task,
          projectTree: project,
          conflicts: merged.conflicts,
        };
      const added = await this.#store.changedPaths(projectId, project, merged.tree);
      if (added === null) return failed('store_failed', 'The changes could not be read.');
      if (added.length === 0) return { kind: 'nothing' };
      const checkpointId = await keepFirst();
      if (checkpointId === null)
        return failed(
          'before_add_failed',
          "The project's files could not be kept first, so nothing was added.",
        );
      const applied = await this.#store.apply(projectId, request.folder, project, merged.tree);
      if (!applied.ok)
        return {
          kind: 'failed',
          code: applied.code,
          message: applied.message,
          written: applied.written,
        };
      // The next add brings what the task changes from here on.
      await this.#store.keep(projectId, `${workstreamId}/base`, task);
      return {
        kind: 'added',
        checkpointId,
        taskTree: task,
        projectTree: project,
        mergedTree: merged.tree,
        added,
      };
    });
  }

  /**
   * Writes the task's work combined with the project's, both sides marked where they met, into the
   * task's own copy (ADR 0045); the copy's base becomes the project as it was read. The project's
   * folder is never written.
   */
  fit(
    projectId: string,
    workstreamId: string,
    request: { copy: string; base: string; folder: string },
  ): Promise<FitOutcome> {
    return this.#serially(workstreamId, async (): Promise<FitOutcome> => {
      const read = await this.#both(projectId, request);
      if (read.kind !== 'read') return read;
      const { task, project, merged } = read;
      const written = await this.#store.changedPaths(projectId, task, merged.tree);
      if (written === null) return failed('store_failed', 'The changes could not be read.');
      const applied = await this.#store.apply(projectId, request.copy, task, merged.tree);
      if (!applied.ok)
        return {
          kind: 'failed',
          code: applied.code,
          message: applied.message,
          written: applied.written,
        };
      await this.#store.keep(projectId, `${workstreamId}/base`, project);
      return {
        kind: 'fitted',
        projectTree: project,
        fittedTree: merged.tree,
        written,
        conflicts: merged.conflicts,
      };
    });
  }

  async #both(
    projectId: string,
    request: { copy: string; base: string; folder: string },
  ): Promise<
    | {
        kind: 'read';
        task: string;
        project: string;
        merged: { tree: string; conflicts: readonly MergeConflict[] };
      }
    | { kind: 'failed'; code: string; message: string; written: false }
  > {
    // The task's files are read with its base as the index's start, so an ignore rule the agent
    // wrote cannot hide a file the base holds; the project's as the person's own rules say.
    const task = await this.#store.treeOf(projectId, request.copy, request.base);
    if (!task.ok) return failed(task.code, "The task's copy could not be read.");
    const project = await this.#store.treeOf(projectId, request.folder, null);
    if (!project.ok) return failed(project.code, "The project's folder could not be read.");
    const merged = await this.#store.merge(projectId, request.base, project.tree, task.tree);
    if (!merged.ok) return failed(merged.code, merged.message);
    return { kind: 'read', task: task.tree, project: project.tree, merged };
  }

  /** One read or write of a task's copy at a time, so a reading never lands after an add it predates. */
  #serially<T>(workstreamId: string, step: () => Promise<T>): Promise<T> {
    const previous = this.#queues.get(workstreamId) ?? Promise.resolve();
    const next = previous.then(step, step);
    const settled = next.catch(() => undefined);
    this.#queues.set(workstreamId, settled);
    void settled.then(() => {
      if (this.#queues.get(workstreamId) === settled) this.#queues.delete(workstreamId);
    });
    return next;
  }

  /**
   * Leaves no token of the person's in the copy's own git settings (ADR 0045), reading every key
   * of the copy's `.git/config` only, with none of the person's settings:
   * - every `http.extraheader` and `http.<url>.extraheader` goes (an `Authorization` header);
   * - every `credential` and `credential.<url>` section goes (a helper may hold or print one);
   * - every `include` and `includeIf` section goes (it pulls in a file elsewhere that may hold one);
   * - every address of a key ending in `.url`, `.pushurl` or `.proxy` (remotes, submodules, LFS, a
   *   proxy) loses its user part,
   *   where the URL form `scheme://user[:password]@host` keeps one; `user@host:path` names an
   *   ssh login and stays;
   * - a `url.<base>` rewrite whose base or value carries a user part goes.
   * True when there is nothing to do or it is done; false leaves the copy to be removed.
   */
  async #withoutCredentials(copy: string): Promise<boolean> {
    const gitDir = join(copy, '.git');
    if (!((await lstat(gitDir).catch(() => null))?.isDirectory() === true)) return true;
    const file = join(gitDir, 'config');
    if ((await lstat(file).catch(() => null))?.isFile() !== true) return true;
    const listed = await this.#gitConfig(file, ['--null', '--list']);
    if (listed.code !== 0) return false;
    const entries = listed.stdout
      .split('\0')
      .filter((entry) => entry !== '')
      .map((entry) => {
        const newline = entry.indexOf('\n');
        return newline === -1
          ? { key: entry, value: '' }
          : { key: entry.slice(0, newline), value: entry.slice(newline + 1) };
      });
    // Keys are unset one by one: git matches their names whatever their case, and drops a section
    // left empty, where `--remove-section` takes only the spelling the file uses.
    const unset = new Set<string>();
    const rewrites: { key: string; from: string; to: string }[] = [];
    for (const { key, value } of entries) {
      const lower = key.toLowerCase();
      if (/^http\.(.*\.)?extraheader$/.test(lower)) unset.add(key);
      else if (/^(credential|include|includeif)\./.test(lower)) unset.add(key);
      else if (/^url\..*\.(insteadof|pushinsteadof)$/.test(lower)) {
        const base = key.slice(4, key.lastIndexOf('.'));
        if (withoutUser(base) !== base || withoutUser(value) !== value) unset.add(key);
      } else if (/\.(url|pushurl|proxy)$/.test(lower)) {
        const safe = withoutUser(value);
        if (safe !== value) rewrites.push({ key, from: value, to: safe });
      }
    }
    for (const key of unset) {
      const done = await this.#gitConfig(file, ['--unset-all', key]);
      if (done.code !== 0 && done.code !== 5) return false;
    }
    for (const { key, from, to } of rewrites) {
      const done = await this.#gitConfig(file, ['--fixed-value', '--replace-all', key, to, from]);
      if (done.code !== 0) return false;
    }
    return true;
  }

  #gitConfig(file: string, args: readonly string[]): Promise<{ code: number; stdout: string }> {
    return new Promise((resolvePromise) => {
      execFile(
        this.#git,
        ['config', '--file', file, ...args],
        {
          env: {
            PATH: '/usr/bin:/bin',
            LC_ALL: 'C',
            GIT_CONFIG_NOSYSTEM: '1',
            GIT_CONFIG_GLOBAL: '/dev/null',
            HOME: '/dev/null',
          },
          timeout: 30_000,
        },
        (error, stdout) =>
          resolvePromise({
            code: error === null ? 0 : typeof error.code === 'number' ? error.code : -1,
            stdout: String(stdout),
          }),
      );
    });
  }

  /** `cp -c` clones with clonefile(2), keeping modes, times and links; it fails rather than copy bytes. */
  #clone(
    source: string,
    target: string,
  ): Promise<{ ok: true } | { ok: false; code: CopyRefusalCode; message: string }> {
    return new Promise((resolvePromise) => {
      execFile(
        CP,
        ['-c', '-p', '-R', source, target],
        { env: { PATH: '/usr/bin:/bin', LC_ALL: 'C' }, timeout: this.#timeoutMs },
        (error, _stdout, stderr) => {
          if (error === null) return resolvePromise({ ok: true });
          const said = String(stderr);
          resolvePromise(
            /clonefile|not supported|cross-device/i.test(said)
              ? {
                  ok: false,
                  code: 'cannot_clone',
                  message: "The folder's disk cannot make a copy that shares its files.",
                }
              : { ok: false, code: 'copy_failed', message: 'The copy of the folder failed.' },
          );
        },
      );
    });
  }
}

export type AddOutcome =
  | { readonly kind: 'nothing' }
  /** The copy is not the tree the person was shown: `changed` is what it differs by now. */
  | {
      readonly kind: 'unreviewed';
      readonly taskTree: string;
      readonly changed: readonly string[];
      readonly files: readonly ChangedFile[];
    }
  | {
      readonly kind: 'conflicted';
      readonly taskTree: string;
      readonly projectTree: string;
      readonly conflicts: readonly MergeConflict[];
    }
  | {
      readonly kind: 'added';
      readonly checkpointId: string;
      readonly taskTree: string;
      readonly projectTree: string;
      readonly mergedTree: string;
      readonly added: readonly string[];
    }
  | {
      readonly kind: 'failed';
      readonly code: string;
      readonly message: string;
      readonly written: boolean;
    };

export type FitOutcome =
  | {
      readonly kind: 'fitted';
      readonly projectTree: string;
      readonly fittedTree: string;
      readonly written: readonly string[];
      readonly conflicts: readonly MergeConflict[];
    }
  | {
      readonly kind: 'failed';
      readonly code: string;
      readonly message: string;
      readonly written: boolean;
    };

function failed(
  code: string,
  message: string,
): { kind: 'failed'; code: string; message: string; written: false } {
  return { kind: 'failed', code, message, written: false };
}

/** An address without its user part where it is a URL that has one; anything else as it is. */
export function withoutUser(address: string): string {
  const match = /^([a-z][a-z0-9+.-]*:\/\/)([^/]*@)(.*)$/i.exec(address);
  return match === null ? address : `${match[1]}${match[3]}`;
}

function refuse(code: CopyRefusalCode, message: string): PrepareResult {
  return { ok: false, code, message };
}

/** What a failed base checkpoint means for the copy. */
function refusalOf(code: string): CopyRefusalCode {
  switch (code) {
    case 'nested_repository':
    case 'too_large':
    case 'holds_host_data':
    case 'location_missing':
      return code;
    default:
      return 'copy_failed';
  }
}

async function resolvedOrSelf(path: string): Promise<string> {
  try {
    return await realpath(path);
  } catch {
    return path;
  }
}

/**
 * After every turn that ended in a task's own copy, reads what the copy differs by from its base
 * and records it (ADR 0045): what "ready to add" counts. A reading that fails records nothing.
 */
export function readCopiesAfterTurns(
  copies: TaskCopies,
  deps: {
    readonly publisher: EventPublisher;
    readonly recorder: Recorder;
    readonly projection: Projection;
    readonly now: () => Timestamp;
  },
): () => void {
  return deps.publisher.subscribe(({ event }) => {
    if (
      event.event_type !== 'runtime.turn.completed' &&
      event.event_type !== 'runtime.turn.failed' &&
      event.event_type !== 'runtime.turn.interrupted'
    )
      return;
    const directory = deps.projection.execution(event.execution_id)?.directory ?? null;
    const copy = deps.projection.workstream(event.workstream_id)?.copy;
    if (directory === null || copy?.state !== 'own' || copy.directory !== directory) return;
    void copies
      .readChanges(event.project_id, event.workstream_id, copy.directory, copy.base_tree)
      .then((read) => {
        // A base that moved while it was read makes the reading another base's.
        const now = deps.projection.workstream(event.workstream_id)?.copy;
        if (read === null || now?.state !== 'own' || now.base_tree !== copy.base_tree) return;
        deps.recorder.record(
          controlPlaneDraft(
            'workstream.copy_changes_read',
            {
              project_id: event.project_id,
              workstream_id: event.workstream_id,
              execution_id: null,
            },
            {
              tree: read.tree,
              changed: pathList(read.changed),
              files: changedFileList(read.files),
            },
            deps.now(),
            NO_CAUSE,
          ),
        );
      })
      .catch(() => undefined);
  });
}
