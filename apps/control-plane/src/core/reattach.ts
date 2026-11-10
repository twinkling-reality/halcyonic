import type { ExecutionId, ExecutionView, Timestamp } from '@halcyonic/contracts';
import type { Projection } from '@halcyonic/domain';
import {
  type Clock,
  type ExecutionContext,
  RuntimeActionError,
  type Scheduler,
} from '@halcyonic/runtime-core';
import type { Logger } from '../logger.ts';
import { controlPlaneDraft, createObservationSink, NO_CAUSE } from './drafts.ts';
import type { Recorder } from './recorder.ts';
import { type Redaction, withinLimit } from './redaction.ts';
import type { RuntimeRegistry } from './runtime-registry.ts';

export interface ReattacherDeps {
  readonly projection: Projection;
  readonly recorder: Recorder;
  readonly registry: RuntimeRegistry;
  readonly clock: Clock;
  readonly scheduler: Scheduler;
  readonly logger: Logger;
  readonly redaction: Redaction;
  /** How long a runtime may take to answer for a session before it counts as lost. */
  readonly timeoutMs: number;
}

/** The code an execution's state, and an action on it, carry once its session is gone (ADR 0046). */
export const SESSION_LOST = 'session_lost';

/**
 * Re-attaches the executions an earlier control plane started (ADR 0046). Those a restart cut are
 * re-attached at start; those at rest when the next action on them comes. A runtime that keeps no
 * session after its processes end, a session the runtime cannot find again, or one it would bring
 * back with other bounds than it started with, is lost: recorded `session_lost`, said plainly,
 * and never holding its project busy.
 */
export class Reattacher {
  readonly #deps: ReattacherDeps;
  /** Executions this process started or re-attached: their runtimes already host them. */
  readonly #attached = new Set<ExecutionId>();
  /** Executions this process found lost: the action on one fails as lost again. */
  readonly #lost = new Map<ExecutionId, string>();
  /** Re-attaches under way, so an action waits for its own execution's. */
  readonly #pending = new Map<ExecutionId, Promise<boolean>>();
  /** One re-attach at a time per runtime, in the order they were asked. */
  readonly #queues = new Map<string, Promise<unknown>>();

  constructor(deps: ReattacherDeps) {
    this.#deps = deps;
  }

  /** An execution this process started: its runtime hosts it from its start. */
  started(executionId: ExecutionId): void {
    this.#attached.add(executionId);
  }

  /** Whether a runtime keeps its sessions after a restart, as its descriptor declares. */
  resumes(runtimeId: string): boolean {
    const adapter = this.#deps.registry.adapter(runtimeId);
    return (
      adapter?.descriptor.resumes_after_restart === true && typeof adapter.reattach === 'function'
    );
  }

  /** Re-attaches each execution the restart cut, recorded unknown until its runtime answers. */
  reattachCut(executions: readonly ExecutionView[]): Promise<void> {
    return Promise.all(executions.map((view) => this.#attach(view.execution_id))).then(
      () => undefined,
    );
  }

  /**
   * Re-attaches an execution before an action on it, when this process has not, and throws the
   * action's failure, which says nothing happened, when its session is lost.
   */
  async ensure(execution: ExecutionContext): Promise<void> {
    const id = execution.execution_id;
    if (this.#attached.has(id)) return;
    const attached = await (this.#pending.get(id) ?? this.#attach(id));
    if (attached) return;
    throw new RuntimeActionError(
      SESSION_LOST,
      this.#lost.get(id) ?? 'The session of this task did not survive the restart.',
    );
  }

  #attach(id: ExecutionId): Promise<boolean> {
    const pending = this.#pending.get(id);
    if (pending !== undefined) return pending;
    if (this.#attached.has(id)) return Promise.resolve(true);
    if (this.#lost.has(id)) return Promise.resolve(false);
    const runtimeId = this.#deps.projection.execution(id)?.runtime.runtime_id ?? '';
    const before = this.#queues.get(runtimeId) ?? Promise.resolve();
    const attempt = before.then(() => this.#tryAttach(id));
    this.#queues.set(
      runtimeId,
      attempt.catch(() => undefined),
    );
    this.#pending.set(id, attempt);
    attempt.finally(() => this.#pending.delete(id)).catch(() => undefined);
    return attempt;
  }

  async #tryAttach(id: ExecutionId): Promise<boolean> {
    const { projection, registry } = this.#deps;
    const view = projection.execution(id);
    if (view === undefined) return false;
    const adapter = registry.adapter(view.runtime.runtime_id);
    const reattach = adapter?.reattach;
    if (adapter === undefined || reattach === undefined || !this.resumes(view.runtime.runtime_id)) {
      this.#lose(
        view,
        'This agent app keeps no session after the control plane restarts, so the task cannot go on.',
      );
      return false;
    }
    const bounds = projection.reattachBounds(id);
    if (bounds === null) {
      this.#lose(
        view,
        'The runtime never reported a session for this task, so there is none to find again.',
      );
      return false;
    }
    const execution: ExecutionContext = {
      execution_id: view.execution_id,
      workstream_id: view.workstream_id,
      project_id: view.project_id,
    };
    const halt = new AbortController();
    try {
      const timedOut = this.#deps.scheduler.sleep(this.#deps.timeoutMs, halt.signal).then(() => {
        throw new RuntimeActionError(
          'timeout',
          `The runtime did not answer for the session within ${this.#deps.timeoutMs} ms.`,
        );
      });
      await Promise.race([
        reattach.call(adapter, {
          execution,
          native_id: bounds.native_id,
          options: bounds.options,
          model_ref: bounds.model_ref,
          ...(bounds.approval_level !== undefined && { approval_level: bounds.approval_level }),
          confined: bounds.confined,
          directory: bounds.directory,
          cut_turn: bounds.cut_turn,
          last_sequence: bounds.last_sequence,
          emit: createObservationSink(
            this.#deps.recorder,
            this.#deps.logger,
            execution,
            view.runtime.runtime_id,
            this.#deps.redaction,
          ),
        }),
        timedOut,
      ]);
    } catch (error) {
      const why = error instanceof Error ? error.message : String(error);
      this.#lose(view, `The session could not be found again: ${why}`);
      return false;
    } finally {
      halt.abort();
    }
    this.#attached.add(id);
    this.#deps.logger.info({ execution_id: id }, 're-attached execution after restart');
    return true;
  }

  #lose(view: ExecutionView, why: string): void {
    const message = withinLimit(this.#deps.redaction.errorText(why), 2000);
    this.#lost.set(view.execution_id, message);
    this.#deps.recorder.record(
      controlPlaneDraft(
        'execution.state_unknown',
        {
          project_id: view.project_id,
          workstream_id: view.workstream_id,
          execution_id: view.execution_id,
        },
        { code: SESSION_LOST, message },
        this.#deps.clock.now().toISOString() as Timestamp,
        NO_CAUSE,
      ),
    );
    this.#deps.logger.warn({ execution_id: view.execution_id }, 'execution lost after restart');
  }
}
