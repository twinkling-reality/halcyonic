import {
  CHECKPOINT_PATHS_LISTED,
  type ChangedFiles,
  type CheckpointId,
  type CheckpointReason,
  type CheckpointView,
  type CommandFailure,
  type CommandOf,
  type ExecutionId,
  type FileChangeKind,
  type PathList,
  type ProjectId,
  type RestorePreview,
  type Timestamp,
} from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import { type Clock, checkProjectLocation } from '@halcyonic/runtime-core';
import { type Cause, causedBy, controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import type { EventPublisher } from '../core/publisher.ts';
import type { Recorder } from '../core/recorder.ts';
import { restoreReview } from '../core/review.ts';
import type { IdGenerator } from '../ids.ts';
import type { HostLocations } from '../locations.ts';
import type { Logger } from '../logger.ts';
import type { CheckpointStore, RestoreHooks } from './store.ts';

export interface CheckpointerDeps {
  readonly store: CheckpointStore;
  readonly recorder: Recorder;
  readonly projection: Projection;
  readonly locations: HostLocations;
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly logger: Logger;
  /** For tests: passed to every restore. */
  readonly restoreHooks?: RestoreHooks;
}

/** A checkpoint's tree and its folder's tree now, or why the folder could not be read. */
type NowTrees =
  | { readonly ok: true; readonly from: string; readonly to: string }
  | { readonly ok: false; readonly code: 'checkpoint_missing' | 'folder_unread' };

/**
 * A read of a folder as it is now: the journal position it was asked at, when it finished (null while
 * it runs), and its result.
 */
interface NowRead {
  readonly position: number;
  readAt: number | null;
  readonly read: Promise<NowTrees>;
}

/** How long a finished read of a folder as it is now is kept while nothing new is journaled. */
const NOW_READ_KEPT_MS = 5_000;

/** How many reads of folders as they are now are kept, one per project and checkpoint. */
const NOW_READS_KEPT = 64;

/** How many tree pairs' changed-file counts are kept: a history page counts at most 50. */
const CHANGED_COUNTS_KEPT = 4_096;

export type RestoreOutcome =
  | { readonly ok: true }
  | { readonly ok: false; readonly failure: CommandFailure };

export type PreviewOutcome =
  | { readonly ok: true; readonly preview: RestorePreview; readonly review: string }
  | { readonly ok: false; readonly code: string; readonly message: string };

/**
 * Halcyonic's own undo (ADR 0036): it takes checkpoints of a project's folder before a task starts,
 * after each turn the runtime reports ended and before any restore, and puts one back on a
 * `project.restore`. No runtime is asked to undo anything, and a restore completes only once the
 * store has read every file again and found the folder equal to the checkpoint.
 *
 * Work on one project's folder runs one step at a time, in the order asked.
 */
export class Checkpointer {
  readonly #deps: CheckpointerDeps;
  readonly #queues = new Map<string, Promise<unknown>>();
  /** Changed-file counts between two trees, oldest dropped past `CHANGED_COUNTS_KEPT`. */
  readonly #counts = new Map<string, number>();
  /** Reads of folders as they are now, by project and checkpoint, with the journal position each was read at. */
  readonly #nowReads = new Map<string, NowRead>();

  constructor(deps: CheckpointerDeps) {
    this.#deps = deps;
  }

  /** Takes a checkpoint after every turn the runtime reports ended in a folder it works in. */
  attach(publisher: EventPublisher): () => void {
    return publisher.subscribe(({ event }) => {
      if (
        event.event_type !== 'runtime.turn.completed' &&
        event.event_type !== 'runtime.turn.failed' &&
        event.event_type !== 'runtime.turn.interrupted'
      )
        return;
      const directory = this.#deps.projection.execution(event.execution_id)?.directory ?? null;
      if (directory === null) return;
      void this.#checkpoint(
        event.project_id,
        directory,
        'after_turn',
        event.execution_id,
        NO_CAUSE,
        event.payload.turn_id,
      ).catch((error: unknown) => this.#unrecorded(error, event.project_id));
    });
  }

  /**
   * Takes the checkpoint a task's start waits for. It never fails the start: when nothing can be
   * kept, the journal says so and the task runs with no undo from before it.
   */
  beforeTask(
    projectId: ProjectId,
    executionId: ExecutionId,
    directory: string,
    cause: Cause,
  ): Promise<void> {
    return this.#checkpoint(projectId, directory, 'before_task', executionId, cause).then(
      () => undefined,
      (error: unknown) => this.#unrecorded(error, projectId),
    );
  }

  /**
   * Keeps the project's folder just before a task's work is added to it (ADR 0045), so the add can
   * be undone: the checkpoint's id, or null when nothing could be kept, and then nothing is added.
   */
  beforeAdd(projectId: ProjectId, folder: string, cause: Cause): Promise<CheckpointId | null> {
    return this.#checkpoint(projectId, folder, 'before_add', null, cause).catch(
      (error: unknown) => {
        this.#unrecorded(error, projectId);
        return null;
      },
    );
  }

  /** A checkpoint or restore whose outcome could not be journaled, as when the control plane stops. */
  #unrecorded(error: unknown, projectId: string): void {
    this.#deps.logger.error(
      { err: error, project_id: projectId },
      'checkpoint outcome not recorded',
    );
  }

  /** What changed between two of the project's checkpoints, by file; reads only the store. */
  compare(projectId: string, fromId: string, toId: string) {
    return this.#deps.store.compare(projectId, fromId, toId);
  }

  /** One file's change between two of the project's checkpoints, at most `maxBytes` of diff. */
  compareFile(projectId: string, fromId: string, toId: string, path: string, maxBytes: number) {
    return this.#deps.store.compareFile(projectId, fromId, toId, path, maxBytes);
  }

  /**
   * What changed between a checkpoint and the folder it was taken of, as that folder is now. Reading
   * the folder hashes every file, so one read per checkpoint is shared by every caller while it runs,
   * whatever is journaled meanwhile, and once read it is kept for `NOW_READ_KEPT_MS` while nothing new
   * is journaled (AUDIT2 S1): a change the journal does not see, such as the person's own edit, shows
   * after that. It waits its turn behind the project's checkpoints and restores, and reads only a
   * folder a checkpoint of it could be taken of now.
   */
  async compareNow(projectId: ProjectId, fromId: CheckpointId) {
    const trees = await this.#treesNow(projectId, fromId);
    if (!trees.ok) return trees;
    return this.#deps.store.compareTrees(projectId, trees.from, trees.to);
  }

  /** One file's change between a checkpoint and its folder as it is now, on `compareNow`'s read. */
  async compareFileNow(projectId: ProjectId, fromId: CheckpointId, path: string, maxBytes: number) {
    const trees = await this.#treesNow(projectId, fromId);
    if (!trees.ok) return trees;
    return this.#deps.store.compareFileTrees(projectId, trees.from, trees.to, path, maxBytes);
  }

  #treesNow(projectId: ProjectId, fromId: CheckpointId): Promise<NowTrees> {
    const key = `${projectId}:${fromId}`;
    const position = this.#deps.projection.position;
    const now = this.#deps.clock.now().getTime();
    const held = this.#nowReads.get(key);
    if (
      held !== undefined &&
      (held.readAt === null ||
        (held.position === position && now - held.readAt <= NOW_READ_KEPT_MS))
    )
      return held.read;
    const entry: NowRead = {
      position,
      readAt: null,
      read: this.#serially(projectId, async (): Promise<NowTrees> => {
        const folder = await this.#folderFor(projectId, fromId);
        if (!folder.ok) return { ok: false, code: 'folder_unread' };
        return this.#deps.store.treesNow(projectId, fromId, folder.path);
      }),
    };
    const read = entry.read;
    this.#nowReads.delete(key);
    this.#nowReads.set(key, entry);
    while (this.#nowReads.size > NOW_READS_KEPT) {
      const oldest = this.#nowReads.keys().next().value;
      if (oldest === undefined) break;
      this.#nowReads.delete(oldest);
    }
    // Kept from when it was read; a failed read is not kept, so the next caller reads again.
    void read.then((result) => {
      entry.readAt = this.#deps.clock.now().getTime();
      if (!result.ok && this.#nowReads.get(key)?.read === read) this.#nowReads.delete(key);
    });
    return read;
  }

  /**
   * How many files differ between two of the project's trees; null when the store cannot tell.
   * Trees are named by their contents, so a count once read never changes and is kept.
   */
  async changedCount(projectId: string, fromTree: string, toTree: string): Promise<number | null> {
    const key = `${projectId}:${fromTree}:${toTree}`;
    const known = this.#counts.get(key);
    if (known !== undefined) return known;
    const counted = await this.#deps.store.countChanged(projectId, fromTree, toTree);
    if (counted === null) return null;
    this.#counts.set(key, counted);
    while (this.#counts.size > CHANGED_COUNTS_KEPT) {
      const oldest = this.#counts.keys().next().value;
      if (oldest === undefined) break;
      this.#counts.delete(oldest);
    }
    return counted;
  }

  /** The project's checkpoints, oldest first. */
  checkpoints(projectId: string): CheckpointView[] {
    return this.#deps.projection.checkpoints(projectId);
  }

  /** What restoring the checkpoint would change now. Reads only. */
  async preview(projectId: ProjectId, checkpointId: CheckpointId): Promise<PreviewOutcome> {
    const folder = await this.#folderFor(projectId, checkpointId);
    if (!folder.ok) return folder;
    const previewed = await this.#deps.store.preview(projectId, folder.path, checkpointId);
    if (!previewed.ok) return previewed;
    const preview = shown(checkpointId, previewed);
    return {
      ok: true,
      preview,
      review: restoreReview(projectId, checkpointId, preview, previewed),
    };
  }

  /**
   * Puts the checkpoint back: first a checkpoint of the folder as it is, holding also what the
   * restore will remove, then the restore, then the store's own check. The outcome is the
   * command's; `project.restored` or `project.restore_incomplete` is journaled before it.
   */
  restore(command: CommandOf<'project.restore'>): Promise<RestoreOutcome> {
    const { project_id: projectId, checkpoint_id: checkpointId } = command.payload;
    return this.#serially(projectId, () => this.#restore(projectId, checkpointId, command));
  }

  async #restore(
    projectId: ProjectId,
    checkpointId: CheckpointId,
    command: CommandOf<'project.restore'>,
  ): Promise<RestoreOutcome> {
    const cause = causedBy(command.command_id);
    const folder = await this.#folderFor(projectId, checkpointId);
    if (!folder.ok)
      return this.#incomplete(projectId, checkpointId, null, false, [], folder, cause);
    const previewed = await this.#deps.store.preview(projectId, folder.path, checkpointId);
    if (!previewed.ok)
      return this.#incomplete(projectId, checkpointId, null, false, [], previewed, cause);
    // A restore carrying the digest of the preview the person read changes nothing once what it
    // would change is no longer that (ADR 0051), checked here, in the restore's own turn.
    const review = command.payload.review;
    if (
      review !== undefined &&
      review !== restoreReview(projectId, checkpointId, shown(checkpointId, previewed), previewed)
    )
      return this.#incomplete(
        projectId,
        checkpointId,
        null,
        false,
        [],
        {
          code: 'review_stale',
          message:
            'What would change is no longer what was read, so nothing was changed. Read it again.',
        },
        cause,
      );

    const before = await this.#take(
      projectId,
      folder.path,
      'before_restore',
      null,
      cause,
      previewed.removed,
    );
    if (before === null)
      return this.#incomplete(
        projectId,
        checkpointId,
        null,
        false,
        [],
        {
          code: 'before_restore_failed',
          message: 'The files as they are now could not be kept first, so nothing was changed.',
        },
        cause,
      );

    const restored = await this.#deps.store.restore(
      projectId,
      folder.path,
      checkpointId,
      this.#deps.restoreHooks,
    );
    if (!restored.ok)
      return this.#incomplete(
        projectId,
        checkpointId,
        before,
        restored.written,
        restored.notRestored,
        restored,
        cause,
      );
    this.#deps.recorder.record(
      controlPlaneDraft(
        'project.restored',
        projectScope(projectId),
        {
          checkpoint_id: checkpointId,
          before_restore_checkpoint_id: before,
          tree: restored.tree,
          files_compared: restored.compared,
          changed_back: pathList(restored.changedBack),
          removed: pathList(restored.removed),
          returned: pathList(restored.returned),
          project_head_moved: restored.projectHeadMoved,
        },
        this.#now(),
        cause,
      ),
    );
    this.#deps.logger.info(
      { project_id: projectId, checkpoint_id: checkpointId, files: restored.compared },
      'project restored and checked',
    );
    return { ok: true };
  }

  #incomplete(
    projectId: ProjectId,
    checkpointId: CheckpointId,
    before: CheckpointId | null,
    written: boolean,
    notRestored: readonly string[],
    failure: { readonly code: string; readonly message: string },
    cause: Cause,
  ): RestoreOutcome {
    const code = /^[a-z][a-z0-9_]{0,63}$/.test(failure.code) ? failure.code : 'restore_failed';
    const message = written
      ? `${failure.message} Some files may already have changed.`
      : `${failure.message} No file was changed.`;
    this.#deps.recorder.record(
      controlPlaneDraft(
        'project.restore_incomplete',
        projectScope(projectId),
        {
          checkpoint_id: checkpointId,
          before_restore_checkpoint_id: before,
          written,
          not_restored: pathList(notRestored),
          failure: { code, message: message.slice(0, 2000) },
        },
        this.#now(),
        cause,
      ),
    );
    this.#deps.logger.warn(
      {
        project_id: projectId,
        checkpoint_id: checkpointId,
        code,
        written,
        differing: notRestored.length,
      },
      'project restore incomplete',
    );
    return {
      ok: false,
      failure: { code, message: message.slice(0, 2000), effect: written ? 'unknown' : 'none' },
    };
  }

  #checkpoint(
    projectId: ProjectId,
    directory: string,
    reason: CheckpointReason,
    executionId: ExecutionId | null,
    cause: Cause,
    turnId: string | null = null,
  ): Promise<CheckpointId | null> {
    return this.#serially(projectId, () =>
      this.#take(projectId, directory, reason, executionId, cause, [], turnId),
    );
  }

  async #take(
    projectId: ProjectId,
    directory: string,
    reason: CheckpointReason,
    executionId: ExecutionId | null,
    cause: Cause,
    keepAlso: readonly string[],
    turnId: string | null = null,
  ): Promise<CheckpointId | null> {
    const checkpointId = this.#deps.ids.next() as CheckpointId;
    const allowed =
      this.#ownCopy(projectId, directory) !== null
        ? { ok: true }
        : checkProjectLocation(this.#deps.locations.policy, directory);
    const taken = allowed.ok
      ? await this.#deps.store.take(projectId, directory, checkpointId, { keepAlso }).catch(() => ({
          ok: false as const,
          code: 'store_failed' as const,
          message: 'The checkpoint store failed.',
        }))
      : ({ ok: false, code: 'location_missing', message: FOLDER_GONE } as const);
    if (!taken.ok) {
      this.#deps.recorder.record(
        controlPlaneDraft(
          'project.checkpoint.failed',
          projectScope(projectId),
          {
            reason,
            execution_id: executionId,
            failure: { code: taken.code, message: taken.message.slice(0, 2000) },
          },
          this.#now(),
          cause,
        ),
      );
      this.#deps.logger.warn(
        { project_id: projectId, reason, code: taken.code },
        'checkpoint not taken',
      );
      return null;
    }
    this.#deps.recorder.record(
      controlPlaneDraft(
        'project.checkpoint.recorded',
        projectScope(projectId),
        {
          checkpoint_id: checkpointId,
          reason,
          execution_id: executionId,
          tree: taken.tree,
          files: taken.files,
          project_head: taken.projectHead,
          ...(turnId === null ? {} : { turn_id: turnId }),
        },
        this.#now(),
        cause,
      ),
    );
    return checkpointId;
  }

  /**
   * The folder a checkpoint goes back into: the one it was taken of, when that is the project's
   * folder, checked against the host's roots again, or the own copy of one of the project's tasks
   * (ADR 0045). A checkpoint of a copy never goes back into the project's folder, nor the other way.
   */
  async #folderFor(
    projectId: ProjectId,
    checkpointId: CheckpointId,
  ): Promise<{ ok: true; path: string } | { ok: false; code: string; message: string }> {
    const projection = this.#deps.projection;
    const path = projection.project(projectId)?.location?.path ?? null;
    const taken = await this.#deps.store.folderOf(projectId, checkpointId);
    const copy = taken === null ? null : this.#ownCopy(projectId, taken);
    if (copy !== null) return { ok: true, path: copy };
    if (path === null)
      return { ok: false, code: 'location_required', message: 'The project has no folder.' };
    const allowed = checkProjectLocation(this.#deps.locations.policy, path);
    return allowed.ok
      ? { ok: true, path }
      : { ok: false, code: allowed.code, message: FOLDER_GONE };
  }

  /**
   * Resolves once every checkpoint and restore under way has settled, or after `timeoutMs`, so a
   * closing control plane journals their outcomes before the journal closes (core audit 2026-11-03).
   */
  async settle(timeoutMs = 10_000): Promise<void> {
    let timer: NodeJS.Timeout | undefined;
    const waited = new Promise<void>((resolve) => {
      timer = setTimeout(resolve, timeoutMs);
      timer.unref();
    });
    try {
      while (this.#queues.size > 0) {
        const settledAll = Promise.all([...this.#queues.values()]).then(() => 'settled' as const);
        if ((await Promise.race([settledAll, waited.then(() => 'waited' as const)])) === 'waited') {
          return;
        }
      }
    } finally {
      clearTimeout(timer);
    }
  }

  /**
   * `directory` when it is the own copy of one of the project's tasks (ADR 0045), a folder of
   * Halcyonic's own outside the project roots, matched exactly as the host recorded it; else null.
   */
  #ownCopy(projectId: ProjectId, directory: string): string | null {
    for (const workstream of this.#deps.projection.workstreams(projectId)) {
      const copy = workstream.copy;
      if (copy?.state === 'own' && copy.directory === directory) return copy.directory;
    }
    return null;
  }

  #serially<T>(projectId: string, step: () => Promise<T>): Promise<T> {
    const previous = this.#queues.get(projectId) ?? Promise.resolve();
    const next = previous.then(step, step);
    const settled = next.catch(() => undefined);
    this.#queues.set(projectId, settled);
    void settled.then(() => {
      if (this.#queues.get(projectId) === settled) this.#queues.delete(projectId);
    });
    return next;
  }

  #now(): Timestamp {
    return this.#deps.clock.now().toISOString();
  }
}

function projectScope(projectId: ProjectId) {
  return { project_id: projectId, workstream_id: null, execution_id: null } as const;
}

/** A store's preview as the restore preview shows it, and as its review digest covers it. */
function shown(
  checkpointId: CheckpointId,
  previewed: Extract<Awaited<ReturnType<CheckpointStore['preview']>>, { ok: true }>,
): RestorePreview {
  return {
    checkpoint_id: checkpointId,
    changed_back: pathList(previewed.changedBack),
    removed: pathList(previewed.removed),
    returned: pathList(previewed.returned),
    project_head_moved: previewed.projectHeadMoved,
  };
}

/**
 * The first paths of a set that the contract can carry, and how many the set holds. A name with a
 * control character is counted but not listed, so recording what happened never fails on it.
 */
export function pathList(paths: readonly string[]): PathList {
  const listable = paths.filter(
    (path) =>
      path.length <= 1024 &&
      [...path].every((character) => {
        const code = character.codePointAt(0) ?? 0;
        return code > 0x1f && code !== 0x7f;
      }),
  );
  return { paths: listable.slice(0, CHECKPOINT_PATHS_LISTED), total: paths.length };
}

/** As `pathList`, with how each file changed: the same files listed, the same total. */
export function changedFileList(
  files: readonly { readonly path: string; readonly change: FileChangeKind }[],
): ChangedFiles {
  const listed = new Set(pathList(files.map((file) => file.path)).paths);
  return {
    files: files
      .filter((file) => listed.has(file.path))
      .slice(0, CHECKPOINT_PATHS_LISTED)
      .map((file) => ({ path: file.path, change: file.change })),
    total: files.length,
  };
}

/** The host's own words for a folder it no longer allows: the policy's message names the path. */
const FOLDER_GONE =
  "The project's folder is not there, or no longer inside a folder this computer allows.";
