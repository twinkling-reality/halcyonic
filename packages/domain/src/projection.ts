import type {
  ApprovalView,
  CatalogRepositoryRef,
  CheckFindings,
  CheckId,
  CheckpointView,
  CommandEnvelope,
  CommandFailure,
  CommandId,
  CommandOf,
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
  Principal,
  ProjectId,
  ProjectLocation,
  ProjectView,
  QuestionView,
  RuntimeEvent,
  StoredEvent,
  Timestamp,
  ToolResultSummary,
  WorkspaceRequestStep,
  WorkspaceRequestView,
  WorkstreamId,
  WorkstreamView,
} from '@halcyonic/contracts';
import { fitQuestion, TOOL_RESULT_RECENT } from '@halcyonic/contracts';
import { deriveAttention } from './attention.ts';
import { DeviceRegistry } from './devices.ts';
import {
  createExecutionState,
  deriveExecutionStatus,
  type ExecutionState,
  endTurn,
  toExecutionView,
} from './execution-state.ts';
import { shownQuestions } from './questions.ts';

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
  activeTurnId: string | null;
  hasActiveTurn: boolean;
  pendingApprovals: readonly ApprovalView[];
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
  starter: string | null;
  /** The catalog repository the person linked (ADR 0039); kept out of the project's view. */
  catalog: CatalogRepositoryRef | null;
  /** The project this one was made as a VR version of (M1.8c-1); kept out of the project's view. */
  madeFrom: ProjectId | null;
  readonly createdAt: Timestamp;
  updatedAt: Timestamp;
}

/** A project's latest check, as the host recorded it. */
export interface ProjectCheck {
  readonly check_id: CheckId;
  readonly findings: CheckFindings;
  readonly recorded_at: Timestamp;
}

/** A project's running preview. */
export interface ProjectPreview {
  readonly check_id: CheckId;
  readonly port: number;
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
  readonly principal: Principal | null;
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

export interface ProjectionOptions {
  /**
   * Whether the runtime with this id reports every tool call it makes (its descriptor's
   * `reports_tool_activity`). Without it, or for a runtime it does not know, none does, so a
   * running execution's `tool_activity` is `unknown` rather than `none`.
   */
  readonly reportsToolActivity?: (runtimeId: string) => boolean;
}

/**
 * Current state, rebuilt by applying journaled events in position order. Applying the same
 * journal always produces the same state; that is what makes the journal the source of truth.
 */
export class Projection {
  #position = 0;
  readonly #reportsToolActivity: (runtimeId: string) => boolean;
  readonly #projects = new Map<ProjectId, ProjectState>();
  readonly #workstreams = new Map<WorkstreamId, WorkstreamState>();
  readonly #executions = new Map<ExecutionId, ExecutionState>();
  readonly #commands = new Map<CommandId, CommandState>();
  /** Checkpoints the host took, by project, oldest first (ADR 0036). */
  readonly #checkpoints = new Map<ProjectId, CheckpointView[]>();
  /** Each project's latest check (ADR 0037). */
  readonly #checks = new Map<ProjectId, ProjectCheck>();
  /** Each project's running preview, by the check it serves. */
  readonly #previews = new Map<ProjectId, ProjectPreview>();
  readonly #devices = new DeviceRegistry();
  readonly #resultStarts = new Map<
    string,
    Map<string, { turn: string | null; title: string | null }>
  >();
  readonly #results = new Map<
    string,
    { total: number; recent: ToolResultSummary[]; history: Map<string, ToolResultSummary> }
  >();
  readonly #inferredTurnOutcomes = new Map<
    string,
    { position: number; turnId: string | null; startPosition: number | null; rule: string }[]
  >();
  readonly #turnStarts = new Map<string, number>();
  readonly #turnObservations = new Map<
    string,
    { position: number; type: string; turnId: string | null; startPosition: number | null }[]
  >();
  readonly #requests = new Map<
    string,
    {
      view: WorkspaceRequestView;
      principal: Principal | null;
      prepared: Map<
        string,
        { command: CommandOf<'execution.send_instruction'>; step: WorkspaceRequestStep }
      >;
      bindings: Map<
        string,
        {
          tool: string;
          command: CommandId | null;
          position: number;
          turn: string | null;
          active: boolean;
        }
      >;
    }
  >();

  constructor(options: ProjectionOptions = {}) {
    this.#reportsToolActivity = options.reportsToolActivity ?? (() => false);
  }

  #view = (state: ExecutionState): ExecutionView =>
    toExecutionView(state, this.#reportsToolActivity(state.runtime.runtime_id));

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
    const beforeTurn = this.#executions.get(stored.event.execution_id as ExecutionId)?.activeTurn;
    const hadActive = Boolean(
      this.#executions.get(stored.event.execution_id as ExecutionId)?.activeTurn,
    );
    this.#dispatch(stored.event, changes, notes);
    const event = stored.event;
    if (
      notes.length === 0 &&
      event.event_type === 'runtime.tool.started' &&
      event.provenance.epistemic === 'observed'
    ) {
      const starts =
        this.#resultStarts.get(event.execution_id) ??
        new Map<string, { turn: string | null; title: string | null }>();
      starts.set(event.payload.tool_call_id, {
        turn: beforeTurn?.turnId ?? null,
        title: event.payload.title,
      });
      // Missing native completions grant no evidence; retain a bounded set of possible correlations.
      const oldest = starts.keys().next().value;
      if (starts.size > 128 && oldest !== undefined) starts.delete(oldest);
      this.#resultStarts.set(event.execution_id, starts);
    }
    if (notes.length === 0 && event.event_type === 'runtime.tool.result') {
      const result = event.payload;
      const started = this.#resultStarts.get(event.execution_id)?.get(result.tool_call_id);
      const bodies =
        result.kind === 'command' ? [result.output] : result.files.map((file) => file.patch);
      const state = this.#results.get(event.execution_id) ?? {
        total: 0,
        recent: [] as ToolResultSummary[],
        history: new Map<string, ToolResultSummary>(),
      };
      state.total++;
      const summary: ToolResultSummary = {
        event_id: event.event_id,
        position: stored.position,
        occurred_at: event.occurred_at,
        runtime_id: event.source.runtime_id,
        epistemic: 'observed',
        native_type: event.provenance.native_type,
        title:
          started?.title == null
            ? null
            : Array.from(started.title).length <= 180
              ? started.title
              : Array.from(started.title).slice(0, 179).join('') + '…',
        kind: result.kind,
        completion: result.completion,
        exit_code: result.kind === 'command' ? result.exit_code : null,
        files_total: result.kind === 'file_changes' ? result.files_total : null,
        body_available: bodies.some((body) => body.availability === 'available'),
        retained_complete:
          bodies.every((body) => body.complete) &&
          (result.kind === 'command' ||
            (result.list_complete &&
              result.files.every(
                (file) =>
                  file.path_complete &&
                  (file.move_path_complete === null || file.move_path_complete),
              ))),
      };
      state.recent.unshift(summary);
      state.history.set(event.event_id, summary);
      state.recent.length = Math.min(state.recent.length, TOOL_RESULT_RECENT);
      this.#results.set(event.execution_id, state);
    }
    if (
      notes.length === 0 &&
      event.event_type === 'runtime.tool.completed' &&
      event.payload.turn_id != null &&
      this.#resultStarts.get(event.execution_id)?.get(event.payload.tool_call_id)?.turn ===
        event.payload.turn_id
    )
      this.#resultStarts.get(event.execution_id)?.delete(event.payload.tool_call_id);
    if (
      notes.length === 0 &&
      event.event_type === 'runtime.turn.started' &&
      event.provenance.epistemic !== 'reported'
    )
      this.#turnStarts.set(event.execution_id, stored.position);
    if (
      notes.length === 0 &&
      event.provenance.epistemic === 'observed' &&
      (event.event_type === 'runtime.turn.started' ||
        event.event_type === 'runtime.turn.completed' ||
        event.event_type === 'runtime.turn.failed' ||
        event.event_type === 'runtime.turn.interrupted')
    ) {
      if (event.event_type === 'runtime.turn.started')
        this.#turnStarts.set(event.execution_id, stored.position);
      const list = this.#turnObservations.get(event.execution_id) ?? [];
      list.push({
        position: stored.position,
        type: event.event_type,
        turnId:
          event.event_type === 'runtime.turn.started'
            ? event.payload.turn_id
            : (event.payload.turn_id ?? beforeTurn?.turnId ?? null),
        startPosition:
          event.event_type === 'runtime.turn.started'
            ? stored.position
            : hadActive
              ? (this.#turnStarts.get(event.execution_id) ?? null)
              : null,
      });
      this.#turnObservations.set(event.execution_id, list);
    }
    if (
      notes.length === 0 &&
      event.provenance.epistemic === 'inferred' &&
      (event.event_type === 'runtime.turn.completed' ||
        event.event_type === 'runtime.turn.failed' ||
        event.event_type === 'runtime.turn.interrupted')
    ) {
      const list = this.#inferredTurnOutcomes.get(event.execution_id) ?? [];
      list.push({
        position: stored.position,
        turnId: event.payload.turn_id ?? beforeTurn?.turnId ?? null,
        startPosition: hadActive ? (this.#turnStarts.get(event.execution_id) ?? null) : null,
        rule: event.provenance.rule,
      });
      this.#inferredTurnOutcomes.set(event.execution_id, list);
    }
    return { changes: this.#materialize(changes), notes };
  }

  // Reads -----------------------------------------------------------------------------------

  project(projectId: string): ProjectView | undefined {
    const state = this.#projects.get(projectId as ProjectId);
    return state === undefined ? undefined : toProjectView(state);
  }

  /** The project this one was made as a VR version of (M1.8c-1), or null. */
  madeFrom(projectId: string): ProjectId | null {
    return this.#projects.get(projectId as ProjectId)?.madeFrom ?? null;
  }

  /**
   * The catalog repository the person linked the project to, null when none, undefined for an
   * unknown project. Only the control plane reads it: clients are to see links through its catalog
   * read (ADR 0039), not in the project's view.
   */
  catalogLink(projectId: string): CatalogRepositoryRef | null | undefined {
    const state = this.#projects.get(projectId as ProjectId);
    if (state === undefined) return undefined;
    return state.catalog === null ? null : { ...state.catalog };
  }

  workstream(workstreamId: string): WorkstreamView | undefined {
    const state = this.#workstreams.get(workstreamId as WorkstreamId);
    return state === undefined ? undefined : this.#workstreamView(state);
  }

  execution(executionId: string): ExecutionView | undefined {
    const state = this.#executions.get(executionId as ExecutionId);
    return state === undefined ? undefined : this.#view(state);
  }
  executionResultSummaries(executionId: string): { total: number; summaries: ToolResultSummary[] } {
    const state = this.#results.get(executionId);
    return { total: state?.total ?? 0, summaries: structuredClone(state?.recent ?? []) };
  }
  executionResultSummary(executionId: string, eventId: string): ToolResultSummary | undefined {
    const summary = this.#results.get(executionId)?.history.get(eventId);
    return summary ? structuredClone(summary) : undefined;
  }

  workspaceRequest(id: string): WorkspaceRequestView | undefined {
    const request = this.#requests.get(id);
    return request ? structuredClone(request.view) : undefined;
  }
  workspaceRequests(): WorkspaceRequestView[] {
    return [...this.#requests.values()].map((request) => structuredClone(request.view));
  }
  workspaceRequestOwner(id: string): Principal | null | undefined {
    const owner = this.#requests.get(id)?.principal;
    return owner ? { ...owner } : owner;
  }
  workspacePreparedCommand(
    id: string,
    step: string,
  ): CommandOf<'execution.send_instruction'> | undefined {
    const command = this.#requests.get(id)?.prepared.get(step)?.command;
    return command ? structuredClone(command) : undefined;
  }

  /** All action receipts remain discoverable when the bounded public view evicts them. */
  workspacePreparedActions(
    id: string,
  ): { command: CommandOf<'execution.send_instruction'>; step: WorkspaceRequestStep }[] {
    return [...(this.#requests.get(id)?.prepared.values() ?? [])].map((value) =>
      structuredClone(value),
    );
  }

  /** Only lifecycle observations the projection actually applied; ignored journal events are not outcome evidence. */
  executionTurnObservations(
    id: string,
    after: number,
    limit: number,
  ): { position: number; type: string; turnId: string | null; startPosition: number | null }[] {
    return (this.#turnObservations.get(id) ?? [])
      .filter((value) => value.position > after)
      .slice(0, limit)
      .map((value) => ({ ...value }));
  }

  /** Accepted reconstruction is kept as inferred, never promoted to an observed outcome. */
  executionInferredTurnOutcomes(
    id: string,
    after: number,
  ): { position: number; turnId: string | null; startPosition: number | null; rule: string }[] {
    return (this.#inferredTurnOutcomes.get(id) ?? [])
      .filter((value) => value.position > after)
      .map((value) => ({ ...value }));
  }

  /** Completed Resume commands are durable human direction, not fabricated model replies. */
  workspaceRequestDirections(id: string): string[] {
    return [...this.#commands.values()].flatMap((value) =>
      value.status === 'completed' &&
      value.envelope.command_type === 'workspace.request.resume' &&
      value.envelope.payload.request_id === id &&
      value.envelope.payload.text !== null
        ? [value.envelope.payload.text]
        : [],
    );
  }

  command(commandId: string): CommandView | undefined {
    const state = this.#commands.get(commandId as CommandId);
    return state === undefined ? undefined : toCommandView(state);
  }

  commandEnvelope(commandId: string): CommandEnvelope | undefined {
    return this.#commands.get(commandId as CommandId)?.envelope;
  }

  /** Original journaled sender, including a rejected command; legacy absent principals grant no read. */
  commandOwner(commandId: string): Principal | null {
    const owner = this.#commands.get(commandId as CommandId)?.principal;
    return owner ? { ...owner } : null;
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
    return [...this.#executions.values()].map(this.#view);
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

  /** The project's checkpoints, oldest first. */
  checkpoints(projectId: string): CheckpointView[] {
    return [...(this.#checkpoints.get(projectId as ProjectId) ?? [])];
  }

  /** The project's latest check, or undefined when it has none. */
  latestCheck(projectId: string): ProjectCheck | undefined {
    return this.#checks.get(projectId as ProjectId);
  }

  /** The project's running preview, or undefined when none runs. */
  preview(projectId: string): ProjectPreview | undefined {
    return this.#previews.get(projectId as ProjectId);
  }

  /** One of the project's checkpoints, or undefined when it is another project's or none. */
  checkpoint(projectId: string, checkpointId: string): CheckpointView | undefined {
    return this.#checkpoints
      .get(projectId as ProjectId)
      ?.find((checkpoint) => checkpoint.checkpoint_id === checkpointId);
  }

  /**
   * Why the project's files may be changing now, or null when nothing of Halcyonic's can be writing
   * them: an execution that is starting, running, verifying or waiting; one whose state became
   * unknown other than by a restart, after which no runtime session survives; or a command in the
   * project still waiting for its outcome, such as a start or a restore.
   */
  projectBusy(projectId: string): string | null {
    for (const state of this.#executions.values()) {
      if (state.projectId !== projectId) continue;
      const status = deriveExecutionStatus(state);
      if (ACTIVE_STATUSES.has(status)) return `A task in this project is ${status}.`;
      if (status === 'unknown' && state.unknownReason?.code !== 'control_plane_restarted')
        return 'A task in this project may still be running: its state is unknown.';
    }
    for (const command of this.#commands.values()) {
      if (command.status === 'accepted' && command.projectId === projectId)
        return `A ${command.envelope.command_type} command in this project is still in progress.`;
    }
    return null;
  }

  /** Whether a restore of the project's files has been accepted and has no outcome yet. */
  restoreInProgress(projectId: string): boolean {
    for (const command of this.#commands.values()) {
      if (
        command.status === 'accepted' &&
        command.envelope.command_type === 'project.restore' &&
        command.projectId === projectId
      )
        return true;
    }
    return false;
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
      case 'workspace.request.created': {
        const view = event.payload.request;
        if (this.#requests.has(view.request_id)) {
          notes.push(note('duplicate_entity', event, 'request already exists'));
          return;
        }
        const execution = this.#executions.get(view.execution_id);
        if (
          view.steps.length !== 0 ||
          view.earlier_steps !== 0 ||
          view.status !== 'running' ||
          view.reply !== null ||
          execution?.projectId !== view.project_id ||
          execution?.workstreamId !== view.workstream_id ||
          view.project_id !== event.project_id ||
          view.workstream_id !== event.workstream_id ||
          view.execution_id !== event.execution_id ||
          !this.#executions.has(event.execution_id)
        ) {
          notes.push(note('unknown_entity', event, 'request has inconsistent work binding'));
          return;
        }
        this.#requests.set(view.request_id, {
          view: structuredClone(view),
          principal: event.payload.principal ? { ...event.payload.principal } : null,
          prepared: new Map(),
          bindings: new Map(),
        });
        return;
      }
      case 'workspace.request.changed':
      case 'workspace.request.step_recorded': {
        const request = this.#requests.get(event.payload.request_id);
        if (
          !request ||
          request.view.project_id !== event.project_id ||
          request.view.workstream_id !== event.workstream_id ||
          request.view.execution_id !== event.execution_id
        ) {
          notes.push(note('unknown_entity', event, 'request binding does not match'));
          return;
        }
        if (event.event_type === 'workspace.request.changed') {
          request.view.status = event.payload.status;
          request.view.reply = structuredClone(event.payload.reply);
        } else {
          const step = event.payload.step,
            command = event.payload.prepared_command;
          if (
            command &&
            (command.payload.execution_id !== request.view.execution_id ||
              command.command_id !== step.command_id)
          ) {
            notes.push(note('unknown_entity', event, 'prepared request command does not match'));
            return;
          }
          const binding = request.bindings.get(step.step_id),
            retained = request.prepared.get(step.step_id);
          if (
            (binding &&
              (binding.tool !== step.tool_name ||
                binding.command !== step.command_id ||
                binding.position !== step.observation_position ||
                binding.turn !== step.active_turn_id ||
                binding.active !== step.had_active_turn)) ||
            step.observation_after <
              (retained?.step.observation_after ?? step.observation_position) ||
            step.observation_after > this.#position ||
            (command && retained && JSON.stringify(retained.command) !== JSON.stringify(command)) ||
            (step.command_id !== null && !command && !retained) ||
            (command &&
              [...request.prepared.entries()].some(
                ([id, action]) =>
                  id !== step.step_id && action.command.command_id === command.command_id,
              ))
          ) {
            notes.push(note('out_of_order', event, 'request step identity cannot change'));
            return;
          }
          request.bindings.set(step.step_id, {
            tool: step.tool_name,
            command: step.command_id,
            position: step.observation_position,
            turn: step.active_turn_id,
            active: step.had_active_turn,
          });
          const at = request.view.steps.findIndex((previous) => previous.step_id === step.step_id);
          if (at < 0 && request.view.steps.length >= 64) {
            request.view.steps.shift();
            request.view.earlier_steps++;
          }
          if (at < 0) request.view.steps.push(structuredClone(step));
          else request.view.steps[at] = structuredClone(step);
          const prepared = retained?.command ?? command;
          if (prepared)
            request.prepared.set(step.step_id, {
              command: structuredClone(prepared),
              step: structuredClone(step),
            });
        }
        request.view.updated_at = event.ingested_at;
        return;
      }
      case 'project.created': {
        if (this.#projects.has(event.project_id)) {
          notes.push(note('duplicate_entity', event, `project ${event.project_id} already exists`));
          return;
        }
        this.#projects.set(event.project_id, {
          projectId: event.project_id,
          name: event.payload.name,
          location: event.payload.location,
          starter: null,
          catalog: null,
          madeFrom: null,
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
      case 'project.catalog_linked':
      case 'project.catalog_unlinked': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        project.catalog =
          event.event_type === 'project.catalog_linked' ? { ...event.payload.catalog } : null;
        project.updatedAt = event.ingested_at;
        changes.projects.add(event.project_id);
        return;
      }
      case 'project.starter_applied': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        project.starter = event.payload.starter_id;
        project.updatedAt = event.ingested_at;
        changes.projects.add(event.project_id);
        return;
      }
      case 'project.made_from': {
        const project = this.#projects.get(event.project_id);
        if (project === undefined) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        project.madeFrom = event.payload.original_project_id;
        project.updatedAt = event.ingested_at;
        return;
      }
      case 'project.checkpoint.recorded': {
        if (!this.#projects.has(event.project_id)) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        const list = this.#checkpoints.get(event.project_id) ?? [];
        list.push({
          checkpoint_id: event.payload.checkpoint_id,
          project_id: event.project_id,
          reason: event.payload.reason,
          execution_id: event.payload.execution_id,
          recorded_at: event.occurred_at,
          tree: event.payload.tree,
          files: event.payload.files,
          project_head: event.payload.project_head,
        });
        this.#checkpoints.set(event.project_id, list);
        return;
      }
      case 'project.check.recorded':
        if (!this.#projects.has(event.project_id)) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        this.#checks.set(event.project_id, {
          check_id: event.payload.check_id,
          findings: event.payload.findings,
          recorded_at: event.occurred_at,
        });
        return;
      case 'project.preview.started':
        if (!this.#projects.has(event.project_id)) {
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
          return;
        }
        this.#previews.set(event.project_id, {
          check_id: event.payload.check_id,
          port: event.payload.port,
        });
        return;
      case 'project.preview.stopped':
        if (this.#previews.get(event.project_id)?.check_id === event.payload.check_id) {
          this.#previews.delete(event.project_id);
        }
        return;
      case 'project.checkpoint.failed':
      case 'project.restored':
      case 'project.restore_incomplete':
        // Facts for the record and for clients; current state keeps only the checkpoints.
        if (!this.#projects.has(event.project_id))
          notes.push(note('unknown_entity', event, `project ${event.project_id} does not exist`));
        return;
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
      case 'execution.files.observed':
        // A record of what the Mac saw in the folder; clients read it from the events.
        if (!this.#executions.has(event.execution_id))
          notes.push(
            note('unknown_entity', event, `execution ${event.execution_id} does not exist`),
          );
        return;
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
          principal: event.payload.principal ? { ...event.payload.principal } : null,
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
      case 'runtime.tool.result':
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
        throw new Error(
          `unhandled event ${String((unhandled as { event_type?: unknown }).event_type)}`,
        );
      }
    }
  }

  #applyRuntime(event: RuntimeEvent, changes: ChangeSet, notes: ProjectionNote[]): void {
    const execution = this.#executions.get(event.execution_id);
    if (execution === undefined) {
      notes.push(note('unknown_entity', event, `execution ${event.execution_id} does not exist`));
      return;
    }
    if (
      (event.event_type === 'runtime.tool.started' ||
        event.event_type === 'runtime.tool.completed') &&
      (event.project_id !== execution.projectId ||
        event.workstream_id !== execution.workstreamId ||
        event.source.runtime_id !== execution.runtime.runtime_id)
    ) {
      notes.push(note('turn_mismatch', event, 'Tool activity does not match its execution scope.'));
      return;
    }
    if (event.event_type === 'runtime.tool.completed' && event.payload.turn_id != null) {
      const start = this.#resultStarts.get(event.execution_id)?.get(event.payload.tool_call_id);
      if (start && start.turn !== event.payload.turn_id) {
        notes.push(
          note('turn_mismatch', event, 'Tool completion does not match its observed start.'),
        );
        return;
      }
    }
    if (event.event_type === 'runtime.tool.result') {
      const start = this.#resultStarts.get(event.execution_id)?.get(event.payload.tool_call_id);
      if (
        event.provenance.epistemic !== 'observed' ||
        !start ||
        start.turn === null ||
        start.turn !== event.payload.turn_id ||
        event.source.runtime_id !== execution.runtime.runtime_id ||
        event.project_id !== execution.projectId ||
        event.workstream_id !== execution.workstreamId
      ) {
        notes.push(
          note(
            'turn_mismatch',
            event,
            'Native result does not match its observed tool start and execution scope.',
          ),
        );
        return;
      }
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
        // A call opened between turns, whose end may never come, is not this turn's.
        execution.activeTools.clear();
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
          approvable: event.payload.complete,
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
      case 'runtime.tool.result':
        // Read-only recorded evidence; never a test event or semantic task outcome.
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
        throw new Error(
          `unhandled runtime event ${String((unhandled as { event_type?: unknown }).event_type)}`,
        );
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
      executions: pick(changes.executions, this.#executions, this.#view),
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
    starter: state.starter,
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
    activeTurnId: state.activeTurn?.turnId ?? null,
    hasActiveTurn: state.activeTurn !== null,
    pendingApprovals: [...state.pendingApprovals.values()],
    // Only those shown can be answered: one behind them waits its turn.
    pendingQuestions: shownQuestions(state.pendingQuestions),
  };
}
