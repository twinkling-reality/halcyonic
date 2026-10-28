import {
  CHECKPOINT_PATHS_LISTED,
  type CheckpointId,
  type CheckpointReason,
  type CheckpointView,
  type CommandFailure,
  type CommandOf,
  type ExecutionId,
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

export type RestoreOutcome =
  | { readonly ok: true }
  | { readonly ok: false; readonly failure: CommandFailure };

export type PreviewOutcome =
  | { readonly ok: true; readonly preview: RestorePreview }
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

  /** A checkpoint or restore whose outcome could not be journaled, as when the control plane stops. */
  #unrecorded(error: unknown, projectId: string): void {
    this.#deps.logger.error(
      { err: error, project_id: projectId },
      'checkpoint outcome not recorded',
    );
  }

  /** The project's checkpoints, oldest first. */
  checkpoints(projectId: string): CheckpointView[] {
    return this.#deps.projection.checkpoints(projectId);
  }

  /** What restoring the checkpoint would change now. Reads only. */
  async preview(projectId: ProjectId, checkpointId: CheckpointId): Promise<PreviewOutcome> {
    const folder = this.#folder(projectId);
    if (!folder.ok) return folder;
    const previewed = await this.#deps.store.preview(projectId, folder.path, checkpointId);
    if (!previewed.ok) return previewed;
    return {
      ok: true,
      preview: {
        checkpoint_id: checkpointId,
        changed_back: pathList(previewed.changedBack),
        removed: pathList(previewed.removed),
        returned: pathList(previewed.returned),
        project_head_moved: previewed.projectHeadMoved,
      },
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
    const folder = this.#folder(projectId);
    if (!folder.ok)
      return this.#incomplete(projectId, checkpointId, null, false, [], folder, cause);
    const previewed = await this.#deps.store.preview(projectId, folder.path, checkpointId);
    if (!previewed.ok)
      return this.#incomplete(projectId, checkpointId, null, false, [], previewed, cause);

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
  ): Promise<CheckpointId | null> {
    return this.#serially(projectId, () =>
      this.#take(projectId, directory, reason, executionId, cause, []),
    );
  }

  async #take(
    projectId: ProjectId,
    directory: string,
    reason: CheckpointReason,
    executionId: ExecutionId | null,
    cause: Cause,
    keepAlso: readonly string[],
  ): Promise<CheckpointId | null> {
    const checkpointId = this.#deps.ids.next() as CheckpointId;
    const allowed = checkProjectLocation(this.#deps.locations.policy, directory);
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
        },
        this.#now(),
        cause,
      ),
    );
    return checkpointId;
  }

  /** The project's folder now, checked against the host's roots again. */
  #folder(
    projectId: ProjectId,
  ): { ok: true; path: string } | { ok: false; code: string; message: string } {
    const path = this.#deps.projection.project(projectId)?.location?.path ?? null;
    if (path === null)
      return { ok: false, code: 'location_required', message: 'The project has no folder.' };
    const allowed = checkProjectLocation(this.#deps.locations.policy, path);
    return allowed.ok
      ? { ok: true, path }
      : { ok: false, code: allowed.code, message: FOLDER_GONE };
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

/** The first paths of a set, and how many it holds. */
/**
 * The first paths of a set that the contract can carry, and how many the set holds. A name with a
 * control character is counted but not listed, so recording what happened never fails on it.
 */
function pathList(paths: readonly string[]): PathList {
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

/** The host's own words for a folder it no longer allows: the policy's message names the path. */
const FOLDER_GONE =
  "The project's folder is not there, or no longer inside a folder this computer allows.";
