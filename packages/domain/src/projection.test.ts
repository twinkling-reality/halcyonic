import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { CommandEnvelope, CommandId, StoredEvent } from '@halcyonic/contracts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

function setup() {
  const b = new EventBuilder();
  const projection = new Projection();
  const apply = (...events: StoredEvent[]) => events.map((event) => projection.apply(event));
  const project = b.project();
  const workstream = b.workstream(project.projectId);
  const execution = b.execution(project.projectId, workstream.workstreamId);
  apply(project.event, workstream.event);
  const scope = {
    projectId: project.projectId,
    workstreamId: workstream.workstreamId,
    executionId: execution.executionId,
  };
  const status = () => projection.execution(execution.executionId)?.status;
  const workstreamView = () => projection.workstream(workstream.workstreamId);
  return { b, projection, apply, execution, scope, status, workstreamView };
}

describe('execution status is derived from observed facts', () => {
  test('a workstream is created until it has an execution, then reports that execution', () => {
    const { apply, execution, status, workstreamView } = setup();
    assert.equal(workstreamView()?.status, 'created');
    apply(execution.event);
    assert.equal(status(), 'starting');
    assert.equal(workstreamView()?.status, 'starting');
    assert.equal(workstreamView()?.current_execution_id, execution.executionId);
  });

  test('turn, test run, approval and completion move through the expected statuses', () => {
    const { b, apply, execution, scope, status, workstreamView } = setup();
    apply(execution.event);
    apply(b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'native-1' }));
    assert.equal(status(), 'starting', 'a session alone is not a running turn');
    apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    assert.equal(status(), 'running');
    apply(
      b.runtimeEvent(scope, 'runtime.test_run.started', { test_run_id: 'r1', label: 'pnpm test' }),
    );
    assert.equal(status(), 'verifying');
    apply(
      b.runtimeEvent(scope, 'runtime.test_run.completed', {
        test_run_id: 'r1',
        outcome: 'passed',
        summary: null,
      }),
    );
    assert.equal(status(), 'running');
    apply(
      b.runtimeEvent(scope, 'runtime.approval.requested', {
        approval_id: 'a1',
        subject: { kind: 'tool_use', tool_name: 'bash', summary: 'Run the migration' },
      }),
    );
    assert.equal(status(), 'waiting_for_human');
    assert.deepEqual(workstreamView()?.attention, {
      level: 'action_required',
      reasons: [
        { kind: 'approval_pending', execution_id: execution.executionId, approval_id: 'a1' },
      ],
    });
    apply(
      b.runtimeEvent(scope, 'runtime.approval.resolved', {
        approval_id: 'a1',
        decision: 'approved',
      }),
    );
    assert.equal(status(), 'running');
    assert.equal(workstreamView()?.attention.level, 'none');
    apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.equal(status(), 'completed');
    assert.equal(workstreamView()?.status, 'completed');
  });

  test('completion after a failing test run is completed but flagged for attention', () => {
    const { b, apply, execution, scope, status, workstreamView } = setup();
    apply(execution.event, b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    apply(b.runtimeEvent(scope, 'runtime.test_run.started', { test_run_id: 'r1', label: null }));
    apply(
      b.runtimeEvent(scope, 'runtime.test_run.completed', {
        test_run_id: 'r1',
        outcome: 'failed',
        summary: '3 failed',
      }),
    );
    apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.equal(status(), 'completed');
    assert.deepEqual(workstreamView()?.attention, {
      level: 'notice',
      reasons: [
        { kind: 'verification_failed', execution_id: execution.executionId, test_run_id: 'r1' },
      ],
    });
  });

  test('a failed turn is failed with its reason and draws attention', () => {
    const { b, apply, execution, scope, projection, workstreamView } = setup();
    apply(execution.event, b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    apply(
      b.runtimeEvent(scope, 'runtime.turn.failed', {
        turn_id: 't1',
        error: { code: 'provider_error', message: 'Rate limited.' },
      }),
    );
    const view = projection.execution(execution.executionId);
    assert.equal(view?.status, 'failed');
    assert.deepEqual(view?.status_reason, { code: 'provider_error', message: 'Rate limited.' });
    assert.equal(workstreamView()?.attention.reasons[0]?.kind, 'execution_failed');
  });

  test('ending a turn clears everything that was in flight', () => {
    const { b, apply, execution, scope, projection } = setup();
    apply(execution.event, b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    apply(
      b.runtimeEvent(scope, 'runtime.tool.started', {
        tool_call_id: 'c1',
        tool_name: 'bash',
        title: null,
      }),
    );
    apply(
      b.runtimeEvent(scope, 'runtime.approval.requested', {
        approval_id: 'a1',
        subject: { kind: 'tool_use', tool_name: 'bash', summary: 'Run it' },
      }),
    );
    apply(b.runtimeEvent(scope, 'runtime.turn.interrupted', { turn_id: 't1' }));
    const view = projection.execution(execution.executionId);
    assert.equal(view?.status, 'interrupted');
    assert.deepEqual(view?.pending_approvals, []);
    assert.deepEqual(view?.active_tools, []);
  });

  test('the model the runtime reports is kept as observed, and the latest report wins', () => {
    const { b, apply, execution, scope, projection, status } = setup();
    apply(execution.event);
    const view = () => projection.execution(execution.executionId);
    assert.equal(view()?.model_ref, null, 'no model before the runtime reports one');
    apply(b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'native-1' }));
    apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    apply(
      b.runtimeEvent(scope, 'runtime.model.used', { model_ref: 'ollama/qwen3.6:35b-a3b-nvfp4' }),
    );
    assert.equal(view()?.model_ref, 'ollama/qwen3.6:35b-a3b-nvfp4');
    assert.equal(status(), 'running', 'a reported model changes no status');
    apply(b.runtimeEvent(scope, 'runtime.model.used', { model_ref: 'ollama/qwen3.8:27b-nvfp4' }));
    assert.equal(view()?.model_ref, 'ollama/qwen3.8:27b-nvfp4');
    apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.equal(view()?.model_ref, 'ollama/qwen3.8:27b-nvfp4', 'the model outlives the turn');
  });

  test('lost contact makes the execution unknown until the runtime reports again', () => {
    const { b, apply, execution, scope, status, workstreamView } = setup();
    apply(execution.event, b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    apply(b.runtimeEvent(scope, 'runtime.connection.lost', { reason: 'Process exited.' }));
    assert.equal(status(), 'unknown');
    assert.equal(workstreamView()?.attention.reasons[0]?.kind, 'execution_state_unknown');
    apply(b.runtimeEvent(scope, 'runtime.agent_message', { text: 'Back.' }));
    assert.equal(status(), 'running');
  });

  test('a start failure is failed and an unknown start outcome is unknown', () => {
    const { b, apply, execution, scope: ids, status } = setup();
    const scope = {
      project_id: ids.projectId,
      workstream_id: ids.workstreamId,
      execution_id: execution.executionId,
    };
    apply(execution.event);
    apply(
      b.controlPlane('execution.state_unknown', scope, {
        code: 'start_outcome_unknown',
        message: 'Timed out.',
      }),
    );
    assert.equal(status(), 'unknown');
    apply(
      b.controlPlane('execution.start_failed', scope, {
        error: { code: 'runtime_closed', message: 'Closed.' },
      }),
    );
    assert.equal(status(), 'unknown', 'unknown takes precedence over a recorded start failure');
  });
});

describe('the projection defends against imperfect input', () => {
  test('a runtime event with a stale sequence is ignored and reported', () => {
    const { b, apply, execution, scope, status } = setup();
    apply(execution.event, b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }, 2));
    const [result] = apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }, 1));
    assert.equal(status(), 'running');
    assert.equal(result?.notes[0]?.code, 'out_of_order');
  });

  test('ending a different turn than the active one changes nothing', () => {
    const { b, apply, execution, scope, status } = setup();
    apply(execution.event, b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't2' }));
    const [result] = apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.equal(status(), 'running');
    assert.equal(result?.notes[0]?.code, 'turn_mismatch');
  });

  test('events for unknown entities are reported, not applied', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const orphan = b.workstream(b.project().projectId);
    const result = projection.apply({ ...orphan.event, position: 1 });
    assert.deepEqual(projection.workstreams(), []);
    assert.equal(result.notes[0]?.code, 'unknown_entity');
    assert.deepEqual(result.changes.workstreams, []);
  });

  test('positions must increase', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const first = b.project().event;
    projection.apply(first);
    assert.throws(() => projection.apply(first), /journal order/);
  });

  test('the same journal always produces the same state', () => {
    const b = new EventBuilder();
    const project = b.project();
    const workstream = b.workstream(project.projectId);
    const execution = b.execution(project.projectId, workstream.workstreamId);
    const scope = {
      projectId: project.projectId,
      workstreamId: workstream.workstreamId,
      executionId: execution.executionId,
    };
    const journal = [
      project.event,
      workstream.event,
      execution.event,
      b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }, 1),
      b.runtimeEvent(scope, 'runtime.agent_message', { text: 'Working.' }, 2),
      b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }, 3),
    ];
    const rebuild = () => {
      const projection = new Projection();
      for (const stored of journal) projection.apply(stored);
      return JSON.stringify([
        projection.projects(),
        projection.workstreams(),
        projection.executions(),
      ]);
    };
    const first = rebuild();
    assert.equal(rebuild(), first);
    assert.match(first, /"status":"completed"/);
  });
});

describe('commands', () => {
  test('accepted and finished commands are tracked, and snapshots keep pending ones', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const command = (id: string): CommandEnvelope => ({
      schema_version: 1,
      command_id: id as CommandId,
      command_type: 'project.create',
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      payload: { name: 'P' },
    });
    const ids = [b.id(), b.id(), b.id()] as [string, string, string];
    const scope = { project_id: null, workstream_id: null, execution_id: null };
    projection.apply(b.command(command(ids[0]), 'accepted'));
    projection.apply(b.command(command(ids[1]), 'accepted'));
    projection.apply(b.command(command(ids[2]), 'rejected'));
    projection.apply(
      b.controlPlane('command.completed', scope, {
        command_id: ids[0] as CommandId,
        command_type: 'project.create',
        result: null,
      }),
    );
    assert.equal(projection.command(ids[0])?.status, 'completed');
    assert.equal(projection.command(ids[2])?.status, 'rejected');
    assert.deepEqual(
      projection.commands(1).map((view) => view.command_id),
      [ids[1], ids[2]],
      'one finished command is kept alongside the pending one',
    );
    assert.deepEqual(
      projection.pendingCommands().map((view) => view.command_id),
      [ids[1]],
    );
  });
});
