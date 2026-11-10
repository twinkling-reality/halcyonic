import { execFile } from 'node:child_process';
import { lstat, mkdir, realpath, rm } from 'node:fs/promises';
import { basename, join } from 'node:path';
import type { CopyRefusalCode, Timestamp } from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import type { EventPublisher } from '../core/publisher.ts';
import type { Recorder } from '../core/recorder.ts';
import { pathList } from './checkpointer.ts';
import type { CheckpointStore, MergeConflict } from './store.ts';
import { within } from './store.ts';

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
    'take' | 'treeOf' | 'keep' | 'changedPaths' | 'merge' | 'apply'
  >;
  /** A UUID naming the base's tree in the store, as a checkpoint's id does. */
  readonly nextId: () => string;
  /** Folders no copied project may hold, such as Halcyonic's data folder. */
  readonly protect?: readonly string[];
  /** How long one copy may take before it is stopped and refused. */
  readonly timeoutMs?: number;
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

  constructor(options: TaskCopiesOptions) {
    this.#directory = options.directory;
    this.#store = options.store;
    this.#nextId = options.nextId;
    this.#protect = [options.directory, ...(options.protect ?? [])];
    this.#timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
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
    await rm(target, { recursive: true, force: true });

    const copied = await this.#clone(real, target);
    if (!copied.ok) {
      await rm(target, { recursive: true, force: true });
      return copied;
    }
    const base = await this.#store.take(projectId, target, this.#nextId());
    if (!base.ok) {
      await rm(target, { recursive: true, force: true });
      return refuse(refusalOf(base.code), base.message);
    }
    return { ok: true, directory: target, baseTree: base.tree, files: base.files };
  }

  /**
   * What the copy now differs by from its base: its tree, read with the base as the index's start,
   * and the paths that differ. Null when it cannot be read.
   */
  readChanges(
    projectId: string,
    workstreamId: string,
    copy: string,
    base: string,
  ): Promise<{ tree: string; changed: string[] } | null> {
    return this.#serially(workstreamId, async () => {
      const task = await this.#store.treeOf(projectId, copy, base);
      if (!task.ok) return null;
      const changed = await this.#store.changedPaths(projectId, base, task.tree);
      return changed === null ? null : { tree: task.tree, changed };
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
    request: { copy: string; base: string; folder: string },
    keepFirst: () => Promise<string | null>,
  ): Promise<AddOutcome> {
    return this.#serially(workstreamId, async (): Promise<AddOutcome> => {
      const read = await this.#both(projectId, request);
      if (read.kind !== 'read') return read;
      const { task, project, merged } = read;
      if (task === request.base) return { kind: 'nothing' };
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
            { tree: read.tree, changed: pathList(read.changed) },
            deps.now(),
            NO_CAUSE,
          ),
        );
      })
      .catch(() => undefined);
  });
}
