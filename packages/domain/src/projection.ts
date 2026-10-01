import type {
  CommandEnvelope,
  CommandFailure,
  CommandId,
  CommandRejection,
  CommandResult,
  CommandStatus,
  CommandView,
  DeviceView,
  EntityChanges,
  EventEnvelope,
  EventId,
  ExecutionId,
  ExecutionStatus,
  ExecutionView,
  ProjectId,
  ProjectLocation,
  ProjectView,
  QuestionView,
  RuntimeEvent,
  StoredEvent,
  Timestamp,
  WorkstreamId,
  WorkstreamView,
} from '@halcyonic/contracts';
import { deriveAttention } from './attention.ts';
import { DeviceRegistry } from './devices.ts';
import {
  createExecutionState,
  deriveExecutionStatus,
  type ExecutionState,
  endTurn,
  toExecutionView,
} from './execution-state.ts';
import { fitQuestion } from './questions.ts';

/** Something in the journal that the projection could not apply as written. */
export interface ProjectionNote {
  code: 'unknown_entity' | 'duplicate_entity' | 'out_of_order' | 'turn_mismatch';
  event_id: EventId;
  message: string;
}

export interface ApplyResult {
  /** Current state of every entity the event changed. */
  changes: EntityChanges;
  notes: ProjectionNote[];
}

/** What command admission and restart reconciliation need to know about an execution. */
export interface ExecutionFacts {
  executionId: ExecutionId;
  projectId: ProjectId;
  workstreamId: WorkstreamId;
  runtimeId: string;
  status: ExecutionStatus;
  hasNativeSession: boolean;
  pendingApprovalIds: readonly string[];
  pendingQuestions: readonly QuestionView[];
}

const ACTIVE_STATUSES: ReadonlySet<ExecutionStatus> = new Set([
  'starting',
  'running',
  'verifying',
  'waiting_for_human',
]);

interface ProjectState {
  readonly projectId: ProjectId;
  readonly name: string;
  location: ProjectLocation | null;
  readonly createdAt: Timestamp;
  updatedAt: Timestamp;
}

interface WorkstreamState {
  readonly workstreamId: WorkstreamId;
  readonly projectId: ProjectId;
  readonly title: string;
  readonly objective: string | null;
  readonly executionIds: ExecutionId[];
  readonly createdAt: Timestamp;
  updatedAt: Timestamp;
}

interface CommandState {
  readonly envelope: CommandEnvelope;
  status: CommandStatus;
  projectId: ProjectId | null;
  workstreamId: WorkstreamId | null;
  executionId: ExecutionId | null;
  updatedAt: Timestamp;
  rejection: CommandRejection | null;
  failure: CommandFailure | null;
  result: CommandResult | null;
}

class ChangeSet {
  readonly projects = new Set<ProjectId>();
  readonly workstreams = new Set<WorkstreamId>();
  readonly executions = new Set<ExecutionId>();
  readonly commands = new Set<CommandId>();
}

/**
 * Current state, rebuilt by applying journaled events in position order. Applying the same
 * journal always produces the same state; that is what makes the journal the source of truth.
 */
export class Projection {
  #position = 0;
  readonly #projects = new Map<ProjectId, ProjectState>();
  readonly #workstreams = new Map<WorkstreamId, WorkstreamState>();
  readonly #executions = new Map<ExecutionId, ExecutionState>();
  readonly #commands = new Map<CommandId, CommandState>();
  readonly #devices = new DeviceRegistry();

  /** Position of the last applied event, 0 before any. */
  get position(): number {
    return this.#position;
  }

  apply(stored: StoredEvent): ApplyResult {
    if (stored.position <= this.#position) {
      throw new Error(
        `projection received position ${stored.position} after ${this.#position}; events must be applied in journal order`,
      );
    }
    this.#position = stored.position;
    const changes = new ChangeSet();
    const notes: ProjectionNote[] = [];
    this.#dispatch(stored.event, changes, notes);
    return { changes: this.#materialize(changes), notes };
  }

  // Reads -----------------------------------------------------------------------------------

  project(projectId: string): ProjectView | undefined {
    const state = this.#projects.get(projectId as ProjectId);
    return state === undefined ? undefined : toProjectView(state);
  }

  workstream(workstreamId: string): WorkstreamView | undefined {
    const state = this.#workstreams.get(workstreamId as WorkstreamId);
    return state === undefined ? undefined : this.#workstreamView(state);
  }

  execution(executionId: string): ExecutionView | undefined {
    const state = this.#executions.get(executionId as ExecutionId);
    return state === undefined ? undefined : toExecutionView(state);
  }

  command(commandId: string): CommandView | undefined {
    const state = this.#commands.get(commandId as CommandId);
    return state === undefined ? undefined : toCommandView(state);
  }

  commandEnvelope(commandId: string): CommandEnvelope | undefined {
    return this.#commands.get(commandId as CommandId)?.envelope;
  }

  projects(): ProjectView[] {
    return [...this.#projects.values()].map(toProjectView);
  }

  workstreams(projectId?: string): WorkstreamView[] {
    const all = [...this.#workstreams.values()];
    const selected = projectId === undefined ? all : all.filter((ws) => ws.projectId === projectId);
    return selected.map((ws) => this.#workstreamView(ws));
  }

  executions(): ExecutionView[] {
    return [...this.#executions.values()].map(toExecutionView);
  }

  /** Commands still awaiting an outcome, plus the most recent `finishedLimit` finished ones. */
  commands(finishedLimit: number): CommandView[] {
    const all = [...this.#commands.values()];
    const finished = all.filter((command) => command.status !== 'accepted');
    const recentFinished = new Set(finished.slice(Math.max(0, finished.length - finishedLimit)));
    return all
      .filter((command) => command.status === 'accepted' || recentFinished.has(command))
      .map(toCommandView);
  }

  executionFacts(executionId: string): ExecutionFacts | undefined {
    const state = this.#executions.get(executionId as ExecutionId);
    return state === undefined ? undefined : toFacts(state);
  }

  /** Executions the projection believes are doing something right now. */
  activeExecutions(): ExecutionFacts[] {
    return [...this.#executions.values()]
      .filter((state) => ACTIVE_STATUSES.has(deriveExecutionStatus(state)))
      .map(toFacts);
  }

  pendingCommands(): CommandView[] {
    return [...this.#commands.values()]
      .filter((command) => command.status === 'accepted')
      .map(toCommandView);
  }

  /** Every device ever paired, revoked ones included. */
  devices(): DeviceView[] {
    return this.#devices.devices();
  }

  device(deviceId: string): DeviceView | undefined {
    return this.#devices.device(deviceId);
  }

  /** The device a credential was issued to, revoked or not, by the credential's SHA-256. */
  deviceForCredential(credentialSha256: string): DeviceView | undefined {
    return this.#devices.deviceForCredential(credentialSha256);
  }

  // Application ------------------------------------------------------------------------------

  #dispatch(event: EventEnvelope, changes: ChangeSet, notes: ProjectionNote[]): void {
    switch (event.event_type) {
      case 'project.created': {
        if (this.#projects.has(event.project_id)) {
          notes.push(note('duplicate_entity', event, `project ${event.project_id} already exists`));
          return;
        }
        this.#projects.set(event.project_id, {
          projectId: event.project_id,
          name: event.payload.name,
          location: event.payload.location,
          createdAt: event.occurred_at,
          updatedAt: event.ingested_at,
        });
        changes.projects.add(event.project_id);
        return;
      }
      case 'project.location_set': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        project.location = event.payload.location;
        project.updatedAt = event.ingested_at;
        changes.projects.add(event.project_id);
        return;
      }
      case 'workstream.created': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        if (this.#workstreams.has(event.workstream_id)) {
          notes.push(
            note('duplicate_entity', event, `workstream ${event.workstream_id} already exists`),
          );
          return;
        }
        this.#workstreams.set(event.workstream_id, {
          workstreamId: event.workstream_id,
          projectId: event.project_id,
          title: event.payload.title,
          objective: event.payload.objective,
          executionIds: [],
          createdAt: event.occurred_at,
          updatedAt: event.ingested_at,
        });
        project.updatedAt = event.ingested_at;
        changes.workstreams.add(event.workstream_id);
        changes.projects.add(event.project_id);
        return;
      }
      case 'execution.created': {
        const workstream = this.#workstreams.get(event.workstream_id);
        if (workstream === undefined || workstream.projectId !== event.project_id) {
          notes.push(
            note('unknown_entity', event, `workstream ${event.workstream_id} does not exist`),
          );
          return;
        }
        if (this.#executions.has(event.execution_id)) {
          notes.push(
            note('duplicate_entity', event, `execution ${event.execution_id} already exists`),
          );
          return;
        }
        this.#executions.set(
          event.execution_id,
          createExecutionState({
            executionId: event.execution_id,
            workstreamId: event.workstream_id,
            projectId: event.project_id,
            runtime: event.payload.runtime,
            instruction: event.payload.instruction,
            directory: event.payload.directory,
            createdAt: event.occurred_at,
          }),
        );
        workstream.executionIds.push(event.execution_id);
        workstream.updatedAt = event.ingested_at;
        changes.executions.add(event.execution_id);
        changes.workstreams.add(event.workstream_id);
        return;
      }
      case 'execution.start_failed':
      case 'execution.state_unknown': {
        const execution = this.#executions.get(event.execution_id);
        if (execution === undefined) {
          notes.push(
            note('unknown_entity', event, `execution ${event.execution_id} does not exist`),
          );
          return;
        }
        if (event.event_type === 'execution.start_failed') {
          execution.startFailure = event.payload.error;
        } else {
          execution.unknownReason = { code: event.payload.code, message: event.payload.message };
        }
        this.#touchExecution(execution, event.ingested_at, changes);
        return;
      }
      case 'command.accepted':
      case 'command.rejected': {
        const commandId = event.payload.command.command_id;
        if (this.#commands.has(commandId)) {
          notes.push(note('duplicate_entity', event, `command ${commandId} already recorded`));
          return;
        }
        const accepted = event.event_type === 'command.accepted';
        this.#commands.set(commandId, {
          envelope: event.payload.command,
          status: accepted ? 'accepted' : 'rejected',
          projectId: event.project_id,
          workstreamId: event.workstream_id,
          executionId: event.execution_id,
          updatedAt: event.ingested_at,
          rejection: accepted ? null : event.payload.rejection,
          failure: null,
          result: null,
        });
        changes.commands.add(commandId);
        return;
      }
      case 'command.completed':
      case 'command.failed': {
        const command = this.#commands.get(event.payload.command_id);
        if (command === undefined) {
          notes.push(
            note('unknown_entity', event, `command ${event.payload.command_id} was never accepted`),
          );
          return;
        }
        if (event.event_type === 'command.completed') {
          command.status = 'completed';
          command.result = event.payload.result;
        } else {
          command.status = 'failed';
          command.failure = event.payload.failure;
        }
        command.projectId = event.project_id ?? command.projectId;
        command.workstreamId = event.workstream_id ?? command.workstreamId;
        command.executionId = event.execution_id ?? command.executionId;
        command.updatedAt = event.ingested_at;
        changes.commands.add(event.payload.command_id);
        return;
      }
      case 'device.paired':
      case 'device.revoked':
        // Devices are no project's entity, so the event changes none that clients hold.
        notes.push(...this.#devices.apply(event));
        return;
      case 'runtime.execution.started':
      case 'runtime.turn.started':
      case 'runtime.turn.completed':
      case 'runtime.turn.failed':
      case 'runtime.turn.interrupted':
      case 'runtime.approval.requested':
      case 'runtime.approval.resolved':
      case 'runtime.question.asked':
      case 'runtime.question.resolved':
      case 'runtime.tool.started':
      case 'runtime.tool.completed':
      case 'runtime.agent_message':
      case 'runtime.test_run.started':
      case 'runtime.test_run.completed':
      case 'runtime.connection.lost':
      case 'runtime.connection.restored':
      case 'runtime.model.used':
        this.#applyRuntime(event, changes, notes);
        return;
      default: {
        const unhandled: never = event;
        throw new Error(`unhandled event ${JSON.stringify(unhandled)}`);
      }
    }
  }

  #applyRuntime(event: RuntimeEvent, changes: ChangeSet, notes: ProjectionNote[]): void {
    const execution = this.#executions.get(event.execution_id);
    if (execution === undefined) {
      notes.push(note('unknown_entity', event, `execution ${event.execution_id} does not exist`));
      return;
    }
    if (event.sequence !== null) {
      if (execution.lastSequence !== null && event.sequence <= execution.lastSequence) {
        notes.push(
          note(
            'out_of_order',
            event,
            `sequence ${event.sequence} is not after ${execution.lastSequence}; state unchanged`,
          ),
        );
        return;
      }
      execution.lastSequence = event.sequence;
    }

    // Any observation other than a lost connection proves the runtime is observable again.
    if (event.event_type !== 'runtime.connection.lost') {
      execution.unknownReason = null;
      execution.runtimeStarted = true;
    }

    switch (event.event_type) {
      case 'runtime.execution.started':
        execution.nativeId = event.payload.native_id ?? execution.nativeId;
        execution.startedAt ??= event.occurred_at;
        break;
      case 'runtime.turn.started':
        execution.activeTurn = { turnId: event.payload.turn_id };
        execution.turnCount += 1;
        execution.startedAt ??= event.occurred_at;
        break;
      case 'runtime.turn.completed':
      case 'runtime.turn.failed':
      case 'runtime.turn.interrupted': {
        const active = execution.activeTurn?.turnId ?? null;
        if (active !== null && event.payload.turn_id !== null && active !== event.payload.turn_id) {
          notes.push(
            note(
              'turn_mismatch',
              event,
              `turn ${event.payload.turn_id} ended while turn ${active} is active; state unchanged`,
            ),
          );
          return;
        }
        if (event.event_type === 'runtime.turn.completed') {
          endTurn(execution, 'completed', null);
        } else if (event.event_type === 'runtime.turn.failed') {
          endTurn(execution, 'failed', event.payload.error);
        } else {
          endTurn(execution, 'interrupted', null);
        }
        break;
      }
      case 'runtime.approval.requested':
        execution.pendingApprovals.set(event.payload.approval_id, {
          approval_id: event.payload.approval_id,
          subject: event.payload.subject,
          requested_at: event.occurred_at,
        });
        break;
      case 'runtime.approval.resolved':
        execution.pendingApprovals.delete(event.payload.approval_id);
        break;
      case 'runtime.question.asked': {
        const fitted = fitQuestion(event.payload.prompts, event.payload.answerable);
        execution.pendingQuestions.set(event.payload.question_id, {
          question_id: event.payload.question_id,
          prompts: [...fitted.prompts],
          answerable: fitted.answerable,
          asked_at: event.occurred_at,
        });
        break;
      }
      case 'runtime.question.resolved':
        execution.pendingQuestions.delete(event.payload.question_id);
        break;
      case 'runtime.tool.started':
        execution.activeTools.set(event.payload.tool_call_id, {
          tool_call_id: event.payload.tool_call_id,
          tool_name: event.payload.tool_name,
          title: event.payload.title,
          started_at: event.occurred_at,
        });
        break;
      case 'runtime.tool.completed':
        execution.activeTools.delete(event.payload.tool_call_id);
        break;
      case 'runtime.agent_message':
        // Agent text is history and a claim, not state. It changes nothing but `updated_at`.
        break;
      case 'runtime.test_run.started':
        execution.activeTestRun = {
          test_run_id: event.payload.test_run_id,
          label: event.payload.label,
          started_at: event.occurred_at,
        };
        break;
      case 'runtime.test_run.completed': {
        const started =
          execution.activeTestRun?.test_run_id === event.payload.test_run_id
            ? execution.activeTestRun
            : null;
        if (started !== null) execution.activeTestRun = null;
        execution.lastTestRun = {
          test_run_id: event.payload.test_run_id,
          label: started?.label ?? null,
          outcome: event.payload.outcome,
          summary: event.payload.summary,
          completed_at: event.occurred_at,
        };
        break;
      }
      case 'runtime.connection.lost':
        execution.unknownReason = {
          code: 'runtime_connection_lost',
          message: event.payload.reason,
        };
        break;
      case 'runtime.connection.restored':
        // Observable again, which every runtime event other than a loss already records above;
        // what changed meanwhile arrives in the events that follow.
        break;
      case 'runtime.model.used':
        execution.modelRef = event.payload.model_ref;
        break;
      default: {
        const unhandled: never = event;
        throw new Error(`unhandled runtime event ${JSON.stringify(unhandled)}`);
      }
    }
    this.#touchExecution(execution, event.ingested_at, changes);
  }

  #touchExecution(execution: ExecutionState, at: Timestamp, changes: ChangeSet): void {
    execution.updatedAt = at;
    changes.executions.add(execution.executionId);
    const workstream = this.#workstreams.get(execution.workstreamId);
    if (workstream !== undefined) {
      workstream.updatedAt = at;
      changes.workstreams.add(workstream.workstreamId);
    }
  }

  #workstreamView(state: WorkstreamState): WorkstreamView {
    const executions = state.executionIds
      .map((id) => this.#executions.get(id))
      .filter((execution): execution is ExecutionState => execution !== undefined);
    const current = executions.at(-1);
    return {
      workstream_id: state.workstreamId,
      project_id: state.projectId,
      title: state.title,
      objective: state.objective,
      status: current === undefined ? 'created' : deriveExecutionStatus(current),
      attention: deriveAttention(executions, current),
      current_execution_id: current?.executionId ?? null,
      execution_ids: [...state.executionIds],
      created_at: state.createdAt,
      updated_at: state.updatedAt,
    };
  }

  #materialize(changes: ChangeSet): EntityChanges {
    const pick = <K, S, V>(ids: Set<K>, map: Map<K, S>, view: (state: S) => V): V[] =>
      [...ids].flatMap((id) => {
        const state = map.get(id);
        return state === undefined ? [] : [view(state)];
      });
    return {
      projects: pick(changes.projects, this.#projects, toProjectView),
      workstreams: pick(changes.workstreams, this.#workstreams, (ws) => this.#workstreamView(ws)),
      executions: pick(changes.executions, this.#executions, toExecutionView),
      commands: pick(changes.commands, this.#commands, toCommandView),
    };
  }
}

function note(code: ProjectionNote['code'], event: EventEnvelope, message: string): ProjectionNote {
  return { code, event_id: event.event_id, message };
}

function toProjectView(state: ProjectState): ProjectView {
  return {
    project_id: state.projectId,
    name: state.name,
    location: state.location,
    created_at: state.createdAt,
    updated_at: state.updatedAt,
  };
}

function toCommandView(state: CommandState): CommandView {
  return {
    command_id: state.envelope.command_id,
    command_type: state.envelope.command_type,
    status: state.status,
    project_id: state.projectId,
    workstream_id: state.workstreamId,
    execution_id: state.executionId,
    issued_at: state.envelope.issued_at,
    updated_at: state.updatedAt,
    rejection: state.rejection,
    failure: state.failure,
    result: state.result,
  };
}

function toFacts(state: ExecutionState): ExecutionFacts {
  return {
    executionId: state.executionId,
    projectId: state.projectId,
    workstreamId: state.workstreamId,
    runtimeId: state.runtime.runtime_id,
    status: deriveExecutionStatus(state),
    hasNativeSession: state.runtimeStarted,
    pendingApprovalIds: [...state.pendingApprovals.keys()],
    pendingQuestions: [...state.pendingQuestions.values()],
  };
}
