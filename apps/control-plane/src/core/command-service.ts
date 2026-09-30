import type {
  CommandEnvelope,
  CommandFailure,
  CommandResult,
  CommandView,
  ExecutionId,
  Principal,
  ProjectId,
  ReceivedVia,
  RuntimeDescriptor,
  RuntimeRef,
  Timestamp,
  WorkstreamId,
} from '@halcyonic/contracts';
import {
  type Admission,
  admitCommand,
  type CommandScope,
  type Projection,
} from '@halcyonic/domain';
import {
  type Clock,
  type ExecutionContext,
  RuntimeActionError,
  type RuntimeAdapter,
  type Scheduler,
} from '@halcyonic/runtime-core';
import type { IdGenerator } from '../ids.ts';
import type { Logger } from '../logger.ts';
import {
  type Cause,
  causedBy,
  controlPlaneDraft,
  createObservationSink,
  NO_CAUSE,
} from './drafts.ts';
import type { Recorder } from './recorder.ts';
import type { RuntimeRegistry } from './runtime-registry.ts';

export type SubmitOutcome =
  | { readonly disposition: 'accepted' | 'rejected' | 'duplicate'; readonly command: CommandView }
  | { readonly disposition: 'conflict'; readonly command: null };

export interface CommandServiceDeps {
  readonly projection: Projection;
  readonly recorder: Recorder;
  readonly registry: RuntimeRegistry;
  readonly ids: IdGenerator;
  readonly clock: Clock;
  readonly scheduler: Scheduler;
  readonly logger: Logger;
  /** How long to wait for a runtime to confirm an action before recording it as failed. */
  readonly commandTimeoutMs: number;
}

type Admitted = Admission & { admitted: true };

interface ActionOutcome {
  succeeded(): void;
  failed(failure: CommandFailure): void;
}

/**
 * Carries a command from submission to a recorded outcome. Every command is journaled as
 * accepted or rejected before anything happens. Completion is recorded only when the runtime
 * confirms the action, and a timeout is recorded as a failure whose effect is unknown, because
 * the runtime may still have acted.
 */
export class CommandService {
  readonly #deps: CommandServiceDeps;

  constructor(deps: CommandServiceDeps) {
    this.#deps = deps;
  }

  /**
   * `principal` is who the control plane authenticated: the local access token or a paired device.
   * It is null only for commands from inside the control plane, such as a fixture recorder.
   *
   * A device is authorized here, where the command would act, and not only when its request or
   * connection was opened: a device revoked since then has its command rejected (ADR 0017). A
   * duplicate changes nothing, so it is answered as one whoever sends it.
   */
  submit(
    command: CommandEnvelope,
    via: ReceivedVia,
    principal: Principal | null = null,
  ): SubmitOutcome {
    const known = this.#deps.projection.commandEnvelope(command.command_id);
    if (known !== undefined) {
      return canonicalJson(known) === canonicalJson(command)
        ? { disposition: 'duplicate', command: this.#view(command) }
        : { disposition: 'conflict', command: null };
    }

    const admission = this.#authorize(principal, this.#admit(command));
    const cause = causedBy(command.command_id);
    if (!admission.admitted) {
      this.#deps.recorder.record(
        controlPlaneDraft(
          'command.rejected',
          admission.scope,
          { command, rejection: admission.rejection, received_via: via, principal },
          this.#now(),
          cause,
        ),
      );
      this.#deps.logger.info(
        {
          command_id: command.command_id,
          command_type: command.command_type,
          rejection: admission.rejection.code,
        },
        'command rejected',
      );
      return { disposition: 'rejected', command: this.#view(command) };
    }

    this.#deps.recorder.record(
      controlPlaneDraft(
        'command.accepted',
        admission.scope,
        { command, policy: admission.policy, received_via: via, principal },
        this.#now(),
        cause,
      ),
    );
    this.#dispatch(command, admission, cause);
    return { disposition: 'accepted', command: this.#view(command) };
  }

  /**
   * After a restart nothing that was in flight can still be observed: the mock runtime's sessions
   * died with the process, and no adapter can reattach yet. Record that honestly instead of
   * leaving executions "running" and commands "pending" forever.
   */
  reconcileAfterRestart(): { executions: number; commands: number } {
    const executions = this.#deps.projection.activeExecutions();
    for (const facts of executions) {
      this.#deps.recorder.record(
        controlPlaneDraft(
          'execution.state_unknown',
          {
            project_id: facts.projectId,
            workstream_id: facts.workstreamId,
            execution_id: facts.executionId,
          },
          {
            code: 'control_plane_restarted',
            message:
              'The control plane restarted and is no longer connected to the runtime session of this execution.',
          },
          this.#now(),
          NO_CAUSE,
        ),
      );
    }
    const commands = this.#deps.projection.pendingCommands();
    for (const command of commands) {
      this.#deps.recorder.record(
        controlPlaneDraft(
          'command.failed',
          {
            project_id: command.project_id,
            workstream_id: command.workstream_id,
            execution_id: command.execution_id,
          },
          {
            command_id: command.command_id,
            command_type: command.command_type,
            failure: {
              code: 'control_plane_restarted',
              message:
                'The control plane restarted before the runtime confirmed this command. It may or may not have taken effect.',
              effect: 'unknown',
            },
          },
          this.#now(),
          causedBy(command.command_id),
        ),
      );
    }
    return { executions: executions.length, commands: commands.length };
  }

  /** Rejects a command from a device that is no longer paired, whatever its admission. */
  #authorize(principal: Principal | null, admission: Admission): Admission {
    if (principal?.kind !== 'device') return admission;
    const device = this.#deps.projection.device(principal.device_id);
    if (device !== undefined && device.revoked_at === null) return admission;
    return {
      admitted: false,
      scope: admission.scope,
      rejection: {
        code: 'device_revoked',
        message:
          'This device is not, or no longer, paired with the control plane, so its commands are not carried out.',
      },
    };
  }

  #admit(command: CommandEnvelope): Admission {
    const admission = admitCommand(command, this.#deps.projection, this.#deps.registry);
    if (!admission.admitted || command.command_type !== 'execution.start') return admission;
    const adapter = this.#deps.registry.adapter(command.payload.runtime_id);
    const options = adapter?.validateStartOptions(
      command.payload.options,
      command.payload.model_ref,
    );
    if (options === undefined || options.ok) return admission;
    return {
      admitted: false,
      scope: admission.scope,
      rejection: { code: 'invalid_runtime_options', message: options.message },
    };
  }

  #dispatch(command: CommandEnvelope, admission: Admitted, cause: Cause): void {
    const { recorder } = this.#deps;
    switch (command.command_type) {
      case 'project.create': {
        const projectId = this.#deps.ids.next() as ProjectId;
        const scope = { project_id: projectId, workstream_id: null, execution_id: null };
        recorder.record(
          controlPlaneDraft(
            'project.created',
            scope,
            { name: command.payload.name },
            this.#now(),
            cause,
          ),
        );
        this.#complete(command, scope, { kind: 'project_created', project_id: projectId });
        return;
      }
      case 'workstream.create': {
        const workstreamId = this.#deps.ids.next() as WorkstreamId;
        const scope = {
          project_id: command.payload.project_id,
          workstream_id: workstreamId,
          execution_id: null,
        };
        recorder.record(
          controlPlaneDraft(
            'workstream.created',
            scope,
            { title: command.payload.title, objective: command.payload.objective },
            this.#now(),
            cause,
          ),
        );
        this.#complete(command, scope, { kind: 'workstream_created', workstream_id: workstreamId });
        return;
      }
      case 'execution.start': {
        const { runtime, adapter } = this.#runtimeFor(admission);
        const { project_id, workstream_id } = admission.scope;
        if (project_id === null || workstream_id === null) {
          throw new Error('admitted execution.start without a resolved workstream');
        }
        const execution: ExecutionContext = {
          execution_id: this.#deps.ids.next() as ExecutionId,
          workstream_id,
          project_id,
        };
        const scope = { project_id, workstream_id, execution_id: execution.execution_id };
        recorder.record(
          controlPlaneDraft(
            'execution.created',
            scope,
            { runtime: toRuntimeRef(runtime), instruction: command.payload.instruction },
            this.#now(),
            cause,
          ),
        );
        const start = adapter.startExecution;
        if (start === undefined) {
          this.#recordStartFailure(command, scope, unimplemented('startExecution'));
          return;
        }
        const emit = createObservationSink(
          recorder,
          this.#deps.logger,
          execution,
          runtime.runtime_id,
        );
        void this.#perform(
          command,
          () =>
            start.call(adapter, {
              execution,
              instruction: command.payload.instruction,
              options: command.payload.options,
              model_ref: command.payload.model_ref,
              emit,
            }),
          {
            succeeded: () =>
              this.#complete(command, scope, {
                kind: 'execution_created',
                execution_id: execution.execution_id,
              }),
            failed: (failure) => this.#recordStartFailure(command, scope, failure),
          },
        );
        return;
      }
      case 'execution.send_instruction':
      case 'execution.respond_to_approval':
      case 'execution.interrupt': {
        const { adapter } = this.#runtimeFor(admission);
        const execution = toExecutionContext(admission.scope);
        const action = actionFor(command, adapter, execution);
        const outcome: ActionOutcome = {
          succeeded: () => this.#complete(command, admission.scope, null),
          failed: (failure) => this.#fail(command, admission.scope, failure),
        };
        if (action === undefined) {
          outcome.failed(unimplemented(command.command_type));
          return;
        }
        void this.#perform(command, action, outcome);
        return;
      }
      default: {
        const unhandled: never = command;
        throw new Error(`unhandled command ${JSON.stringify(unhandled)}`);
      }
    }
  }

  /** Waits for the runtime to confirm the action, bounded by the command timeout. */
  async #perform(
    command: CommandEnvelope,
    action: () => Promise<unknown>,
    outcome: ActionOutcome,
  ): Promise<void> {
    const deadline = new AbortController();
    const work = Promise.resolve()
      .then(action)
      .then(
        () => ({ kind: 'succeeded' }) as const,
        (error: unknown) => ({ kind: 'failed', error }) as const,
      );
    const timer = this.#deps.scheduler.sleep(this.#deps.commandTimeoutMs, deadline.signal).then(
      () => ({ kind: 'timed_out' }) as const,
      () => ({ kind: 'cancelled' }) as const,
    );
    const first = await Promise.race([work, timer]);
    deadline.abort();
    try {
      switch (first.kind) {
        case 'succeeded':
          outcome.succeeded();
          return;
        case 'failed':
          outcome.failed(toFailure(first.error));
          return;
        case 'timed_out':
          outcome.failed({
            code: 'timeout',
            message: `The runtime did not confirm the action within ${this.#deps.commandTimeoutMs} ms. It may still take effect.`,
            effect: 'unknown',
          });
          void work.then((late) =>
            this.#deps.logger.warn(
              { command_id: command.command_id, late_outcome: late.kind },
              'runtime answered after the command timed out',
            ),
          );
          return;
        case 'cancelled':
          return;
        default: {
          const unhandled: never = first;
          throw new Error(`unhandled outcome ${JSON.stringify(unhandled)}`);
        }
      }
    } catch (error) {
      this.#deps.logger.error(
        { err: error, command_id: command.command_id },
        'failed to record a command outcome',
      );
    }
  }

  #recordStartFailure(
    command: CommandEnvelope,
    scope: CommandScope,
    failure: CommandFailure,
  ): void {
    const { project_id, workstream_id, execution_id } = scope;
    if (project_id !== null && workstream_id !== null && execution_id !== null) {
      const executionScope = { project_id, workstream_id, execution_id };
      // A failure whose effect is unknown may have started the runtime anyway, so the
      // execution becomes unknown rather than failed.
      this.#deps.recorder.record(
        failure.effect === 'none'
          ? controlPlaneDraft(
              'execution.start_failed',
              executionScope,
              { error: { code: failure.code, message: failure.message } },
              this.#now(),
              causedBy(command.command_id),
            )
          : controlPlaneDraft(
              'execution.state_unknown',
              executionScope,
              { code: 'start_outcome_unknown', message: failure.message },
              this.#now(),
              causedBy(command.command_id),
            ),
      );
    }
    this.#fail(command, scope, failure);
  }

  #complete(command: CommandEnvelope, scope: CommandScope, result: CommandResult | null): void {
    this.#deps.recorder.record(
      controlPlaneDraft(
        'command.completed',
        scope,
        { command_id: command.command_id, command_type: command.command_type, result },
        this.#now(),
        causedBy(command.command_id),
      ),
    );
  }

  #fail(command: CommandEnvelope, scope: CommandScope, failure: CommandFailure): void {
    this.#deps.recorder.record(
      controlPlaneDraft(
        'command.failed',
        scope,
        { command_id: command.command_id, command_type: command.command_type, failure },
        this.#now(),
        causedBy(command.command_id),
      ),
    );
    this.#deps.logger.info(
      { command_id: command.command_id, command_type: command.command_type, failure: failure.code },
      'command failed',
    );
  }

  #runtimeFor(admission: Admitted): { runtime: RuntimeDescriptor; adapter: RuntimeAdapter } {
    const runtime = admission.runtime;
    const adapter = runtime === null ? undefined : this.#deps.registry.adapter(runtime.runtime_id);
    if (runtime === null || adapter === undefined) {
      throw new Error('admitted a runtime command without a registered runtime');
    }
    return { runtime, adapter };
  }

  #view(command: CommandEnvelope): CommandView {
    const view = this.#deps.projection.command(command.command_id);
    if (view === undefined)
      throw new Error(`command ${command.command_id} is missing from the projection`);
    return view;
  }

  #now(): Timestamp {
    return this.#deps.clock.now().toISOString();
  }
}

function actionFor(
  command: CommandEnvelope,
  adapter: RuntimeAdapter,
  execution: ExecutionContext,
): (() => Promise<void>) | undefined {
  switch (command.command_type) {
    case 'execution.send_instruction': {
      const send = adapter.sendInstruction;
      return send && (() => send.call(adapter, { execution, text: command.payload.text }));
    }
    case 'execution.respond_to_approval': {
      const respond = adapter.respondToApproval;
      return (
        respond &&
        (() =>
          respond.call(adapter, {
            execution,
            approval_id: command.payload.approval_id,
            decision: command.payload.decision,
            message: command.payload.message,
          }))
      );
    }
    case 'execution.interrupt': {
      const interrupt = adapter.interrupt;
      return interrupt && (() => interrupt.call(adapter, { execution }));
    }
    default:
      return undefined;
  }
}

function toExecutionContext(scope: CommandScope): ExecutionContext {
  const { project_id, workstream_id, execution_id } = scope;
  if (project_id === null || workstream_id === null || execution_id === null) {
    throw new Error('admitted an execution command without a resolved execution');
  }
  return { project_id, workstream_id, execution_id };
}

function toRuntimeRef(runtime: RuntimeDescriptor): RuntimeRef {
  return {
    runtime_id: runtime.runtime_id,
    kind: runtime.kind,
    display_name: runtime.display_name,
    synthetic: runtime.synthetic,
  };
}

function unimplemented(action: string): CommandFailure {
  return {
    code: 'capability_unimplemented',
    message: `The runtime adapter declares support for ${action} but does not implement it.`,
    effect: 'none',
  };
}

const CODE_PATTERN = /^[a-z][a-z0-9_]{0,63}$/;

function toFailure(error: unknown): CommandFailure {
  if (error instanceof RuntimeActionError) {
    return {
      code: CODE_PATTERN.test(error.code) ? error.code : 'runtime_error',
      message: clip(error.message, 'The runtime refused the action.'),
      effect: error.effect,
    };
  }
  // An unexpected exception says nothing about what the runtime did.
  return {
    code: 'adapter_error',
    message: clip(
      error instanceof Error ? error.message : '',
      'The runtime adapter failed unexpectedly.',
    ),
    effect: 'unknown',
  };
}

function clip(message: string, fallback: string): string {
  const trimmed = message.trim();
  return trimmed.length === 0 ? fallback : trimmed.slice(0, 2000);
}

/** JSON with object keys sorted, so equal commands compare equal regardless of key order. */
function canonicalJson(value: unknown): string {
  return JSON.stringify(value, (_key, inner: unknown) =>
    inner !== null && typeof inner === 'object' && !Array.isArray(inner)
      ? Object.fromEntries(Object.entries(inner).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)))
      : inner,
  );
}
