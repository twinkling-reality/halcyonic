import {
  type CommandEnvelope,
  type EventEnvelope,
  type EventOf,
  type ExecutionId,
  type ProjectId,
  parseEventEnvelope,
  type RuntimeEventType,
  type RuntimeId,
  type RuntimeRef,
  type StoredEvent,
  type WorkstreamId,
} from '@halcyonic/contracts';

/** Test support: builds contract-valid events with increasing positions. Not part of the API. */
export class EventBuilder {
  #position = 0;
  #ids = 0;
  #time = Date.parse('2026-09-26T10:00:00.000Z');

  readonly runtime: RuntimeRef = {
    runtime_id: 'mock' as RuntimeId,
    kind: 'mock',
    display_name: 'Mock runtime (development fixture)',
    synthetic: true,
  };

  id(): string {
    this.#ids += 1;
    return `01920000-0000-7000-8000-${this.#ids.toString(16).padStart(12, '0')}`;
  }

  project(name = 'Project'): { projectId: ProjectId; event: StoredEvent } {
    const projectId = this.id() as ProjectId;
    return {
      projectId,
      event: this.#controlPlane({
        event_type: 'project.created',
        project_id: projectId,
        workstream_id: null,
        execution_id: null,
        payload: { name },
      }),
    };
  }

  workstream(
    projectId: ProjectId,
    title = 'Workstream',
  ): { workstreamId: WorkstreamId; event: StoredEvent } {
    const workstreamId = this.id() as WorkstreamId;
    return {
      workstreamId,
      event: this.#controlPlane({
        event_type: 'workstream.created',
        project_id: projectId,
        workstream_id: workstreamId,
        execution_id: null,
        payload: { title, objective: null },
      }),
    };
  }

  execution(
    projectId: ProjectId,
    workstreamId: WorkstreamId,
  ): { executionId: ExecutionId; event: StoredEvent } {
    const executionId = this.id() as ExecutionId;
    return {
      executionId,
      event: this.#controlPlane({
        event_type: 'execution.created',
        project_id: projectId,
        workstream_id: workstreamId,
        execution_id: executionId,
        payload: { runtime: this.runtime, instruction: 'Do the work.' },
      }),
    };
  }

  controlPlane<T extends EventEnvelope['event_type']>(
    eventType: T,
    scope: Pick<EventOf<T>, 'project_id' | 'workstream_id' | 'execution_id'>,
    payload: EventOf<T>['payload'],
  ): StoredEvent {
    return this.#controlPlane({ event_type: eventType, ...scope, payload });
  }

  runtimeEvent<T extends RuntimeEventType>(
    scope: { projectId: ProjectId; workstreamId: WorkstreamId; executionId: ExecutionId },
    eventType: T,
    payload: EventOf<T>['payload'],
    sequence: number | null = null,
  ): StoredEvent {
    return this.#store({
      event_type: eventType,
      project_id: scope.projectId,
      workstream_id: scope.workstreamId,
      execution_id: scope.executionId,
      source: { kind: 'runtime', runtime_id: this.runtime.runtime_id },
      source_native_id: null,
      sequence,
      provenance:
        eventType === 'runtime.agent_message'
          ? { epistemic: 'reported', native_type: null }
          : { epistemic: 'observed', native_type: null },
      payload,
    });
  }

  command(command: CommandEnvelope, outcome: 'accepted' | 'rejected'): StoredEvent {
    const scope = { project_id: null, workstream_id: null, execution_id: null };
    return outcome === 'accepted'
      ? this.controlPlane('command.accepted', scope, {
          command,
          policy: 'low_consequence',
          received_via: 'internal',
          principal: null,
        })
      : this.controlPlane('command.rejected', scope, {
          command,
          rejection: { code: 'invalid_state', message: 'Rejected in a test.' },
          received_via: 'internal',
          principal: null,
        });
  }

  #controlPlane(fields: Record<string, unknown>): StoredEvent {
    return this.#store({
      source: { kind: 'control_plane' },
      source_native_id: null,
      sequence: null,
      provenance: { epistemic: 'observed', native_type: null },
      ...fields,
    });
  }

  #store(fields: Record<string, unknown>): StoredEvent {
    this.#time += 1000;
    const at = new Date(this.#time).toISOString();
    const candidate = {
      schema_version: 1,
      event_id: this.id(),
      occurred_at: at,
      ingested_at: at,
      correlation_id: null,
      causation_id: null,
      ...fields,
    };
    const parsed = parseEventEnvelope(candidate);
    if (!parsed.ok) {
      throw new Error(`test event is invalid: ${JSON.stringify(parsed.issues)}`);
    }
    this.#position += 1;
    return { position: this.#position, event: parsed.value };
  }
}
