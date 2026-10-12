import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  type CommandId,
  type CommandOf,
  compileValidator,
  type JournalId,
  type WorkspaceRequestId,
  WorkspaceRequestResponse,
  type WorkspaceRequestView,
} from '@halcyonic/contracts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

function setup() {
  const b = new EventBuilder(),
    projection = new Projection();
  const project = b.project(),
    task = b.workstream(project.projectId),
    run = b.execution(project.projectId, task.workstreamId);
  const events = [project.event, task.event, run.event];
  for (const event of events) projection.apply(event);
  const scope = {
    project_id: project.projectId,
    workstream_id: task.workstreamId,
    execution_id: run.executionId,
  };
  const view: WorkspaceRequestView = {
    ...scope,
    request_id: b.id() as WorkspaceRequestId,
    journal_id: b.id() as JournalId,
    original_text: 'Inspect and continue this synthetic task.',
    status: 'running',
    reply: null,
    steps: [],
    earlier_steps: 0,
    created_at: '2026-10-12T12:00:00.000Z',
    updated_at: '2026-10-12T12:00:00.000Z',
  };
  const created = b.controlPlane('workspace.request.created', scope, {
    request: view,
    principal: { kind: 'local' },
  });
  events.push(created);
  projection.apply(created);
  return { b, projection, scope, view, events };
}
test('workspace request facts replay and public reads cannot mutate their binding or reported reply', () => {
  const { b, projection, scope, view, events } = setup();
  const update = b.controlPlane('workspace.request.changed', scope, {
    request_id: view.request_id,
    status: 'responded',
    reply: {
      text: 'The agent reports a pending decision.',
      epistemic: 'reported',
      model: 'synthetic-local-planner',
      served: 'this_mac',
    },
  });
  events.push(update);
  projection.apply(update);
  const publicView = projection.workspaceRequest(view.request_id);
  assert.ok(publicView);
  publicView.original_text = 'changed outside projection';
  assert.equal(projection.workspaceRequest(view.request_id)?.original_text, view.original_text);
  assert.equal(
    compileValidator(WorkspaceRequestResponse)({
      request: projection.workspaceRequest(view.request_id),
    }).ok,
    true,
  );
  const replay = new Projection();
  for (const event of events) replay.apply(event);
  assert.deepEqual(
    replay.workspaceRequest(view.request_id),
    projection.workspaceRequest(view.request_id),
  );
  assert.equal(
    projection.execution(scope.execution_id)?.status,
    'starting',
    'reported response did not complete native work',
  );
});
test('bounded latest receipt view retains prepared command identity beyond 64 steps and through replay', () => {
  const { b, projection, scope, view, events } = setup();
  let firstStep = '',
    firstCommand = '';
  for (let i = 0; i < 70; i++) {
    const step = b.id() as WorkspaceRequestId,
      id = b.id() as CommandId;
    const command: CommandOf<'execution.send_instruction'> = {
      schema_version: 1,
      command_id: id,
      issued_at: '2026-10-12T12:00:00.000Z',
      client: { name: 'synthetic-request', version: null, device_label: null },
      command_type: 'execution.send_instruction',
      payload: { execution_id: scope.execution_id, text: 'Continue the authorized task.' },
    };
    if (i === 0) {
      firstStep = step;
      firstCommand = id;
    }
    const event = b.controlPlane('workspace.request.step_recorded', scope, {
      request_id: view.request_id,
      step: {
        step_id: step,
        tool_name: 'continue_task',
        status: 'prepared',
        command_id: id,
        command_status: null,
        observation_position: projection.position,
        active_turn_id: null,
        runtime_status: null,
        summary: 'Instruction prepared; runtime outcome not yet observed.',
      },
      prepared_command: command,
    });
    events.push(event);
    projection.apply(event);
  }
  assert.equal(projection.workspaceRequest(view.request_id)?.steps.length, 64);
  assert.equal(projection.workspaceRequest(view.request_id)?.earlier_steps, 6);
  assert.equal(
    projection.workspacePreparedCommand(view.request_id, firstStep)?.command_id,
    firstCommand,
  );
  const replay = new Projection();
  for (const event of events) replay.apply(event);
  assert.equal(
    replay.workspacePreparedCommand(view.request_id, firstStep)?.command_id,
    firstCommand,
  );
  assert.equal(replay.workspacePreparedActions(view.request_id).length, 70);
  assert.equal(
    replay.workspacePreparedActions(view.request_id)[0]?.command.command_id,
    firstCommand,
  );
});
test('prepared request record cannot carry approval or recursive workspace command data', () => {
  const { b, scope, view } = setup();
  assert.throws(() =>
    b.controlPlane('workspace.request.step_recorded', scope, {
      request_id: view.request_id,
      step: {
        step_id: b.id() as WorkspaceRequestId,
        tool_name: 'continue_task',
        status: 'prepared',
        command_id: null,
        command_status: null,
        observation_position: 0,
        active_turn_id: null,
        runtime_status: null,
        summary: 'Synthetic unsupported record.',
      },
      prepared_command: {
        schema_version: 1,
        command_id: b.id(),
        issued_at: '2026-10-12T12:00:00.000Z',
        client: { name: 'synthetic-request', version: null, device_label: null },
        command_type: 'execution.respond_to_approval',
        payload: {
          execution_id: scope.execution_id,
          approval_id: 'private-answer',
          decision: 'approve',
          message: null,
        },
      } as unknown as CommandOf<'execution.send_instruction'>,
    }),
  );
});

test('request creation refuses cross-project execution and immutable action identities survive invalid replay updates', () => {
  const { b, projection, scope, view, events } = setup();
  const otherProject = b.project(),
    otherTask = b.workstream(otherProject.projectId);
  projection.apply(otherProject.event);
  projection.apply(otherTask.event);
  events.push(otherProject.event, otherTask.event);
  const bad = b.controlPlane(
    'workspace.request.created',
    {
      project_id: otherProject.projectId,
      workstream_id: otherTask.workstreamId,
      execution_id: scope.execution_id,
    },
    {
      request: {
        ...view,
        request_id: b.id() as WorkspaceRequestId,
        project_id: otherProject.projectId,
        workstream_id: otherTask.workstreamId,
      },
      principal: { kind: 'local' },
    },
  );
  assert.equal(projection.apply(bad).notes.length, 1);
  events.push(bad);
  assert.equal(projection.workspaceRequests().length, 1);
  const step = {
    step_id: b.id() as WorkspaceRequestId,
    tool_name: 'continue_task',
    status: 'prepared' as const,
    command_id: b.id() as CommandId,
    command_status: null,
    observation_position: projection.position,
    active_turn_id: 'active-original',
    runtime_status: null,
    summary: 'Prepared instruction.',
  };
  const command: CommandOf<'execution.send_instruction'> = {
    schema_version: 1,
    command_id: step.command_id,
    issued_at: view.created_at,
    client: { name: 'synthetic', version: null, device_label: null },
    command_type: 'execution.send_instruction',
    payload: { execution_id: scope.execution_id, text: 'Original instruction' },
  };
  const record = (changed: typeof step, prepared: typeof command | null) =>
    b.controlPlane('workspace.request.step_recorded', scope, {
      request_id: view.request_id,
      step: changed,
      prepared_command: prepared,
    });
  const first = record(step, command);
  projection.apply(first);
  events.push(first);
  for (const invalid of [
    record({ ...step, command_id: b.id() as CommandId }, null),
    record(step, { ...command, payload: { ...command.payload, text: 'Changed payload' } }),
    record({ ...step, observation_position: 0 }, command),
    record({ ...step, active_turn_id: 'another' }, command),
  ]) {
    assert.equal(projection.apply(invalid).notes.length, 1);
    events.push(invalid);
  }
  const replay = new Projection();
  for (const event of events) replay.apply(event);
  assert.deepEqual(replay.workspacePreparedActions(view.request_id), [{ command, step }]);
});
