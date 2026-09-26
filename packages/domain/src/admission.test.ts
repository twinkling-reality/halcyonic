import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type {
  CommandEnvelope,
  CommandId,
  RuntimeCapabilities,
  RuntimeDescriptor,
  RuntimeId,
} from '@halcyonic/contracts';
import { admitCommand, type RuntimeCatalog } from './admission.ts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

const FULL: RuntimeCapabilities = {
  start_execution: true,
  instruct_at_rest: true,
  instruct_while_running: false,
  respond_to_approval: true,
  interrupt: true,
};

function catalog(capabilities: RuntimeCapabilities = FULL): RuntimeCatalog {
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'mock' as RuntimeId,
    kind: 'mock',
    display_name: 'Mock',
    synthetic: true,
    capabilities,
  };
  return { get: (id) => (id === 'mock' ? descriptor : undefined) };
}

function setup() {
  const b = new EventBuilder();
  const projection = new Projection();
  const project = b.project();
  const workstream = b.workstream(project.projectId);
  const execution = b.execution(project.projectId, workstream.workstreamId);
  for (const stored of [project.event, workstream.event, execution.event]) projection.apply(stored);
  const scope = {
    projectId: project.projectId,
    workstreamId: workstream.workstreamId,
    executionId: execution.executionId,
  };
  let counter = 0;
  const base = () => {
    counter += 1;
    return {
      schema_version: 1 as const,
      command_id: `00000000-0000-4000-8000-${String(counter).padStart(12, '0')}` as CommandId,
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
    };
  };
  const commands = {
    instruct: (): CommandEnvelope => ({
      ...base(),
      command_type: 'execution.send_instruction',
      payload: { execution_id: execution.executionId, text: 'Continue.' },
    }),
    interrupt: (): CommandEnvelope => ({
      ...base(),
      command_type: 'execution.interrupt',
      payload: { execution_id: execution.executionId },
    }),
    approve: (approvalId: string): CommandEnvelope => ({
      ...base(),
      command_type: 'execution.respond_to_approval',
      payload: {
        execution_id: execution.executionId,
        approval_id: approvalId,
        decision: 'approve',
        message: null,
      },
    }),
    start: (workstreamId: string, runtimeId = 'mock'): CommandEnvelope => ({
      ...base(),
      command_type: 'execution.start',
      payload: {
        workstream_id: workstreamId as never,
        runtime_id: runtimeId as RuntimeId,
        instruction: 'Go.',
        options: {},
      },
    }),
  };
  return { b, projection, scope, commands, workstream };
}

describe('command admission', () => {
  test('starting on a known workstream with a capable runtime is admitted as low consequence', () => {
    const { projection, commands, workstream } = setup();
    const admission = admitCommand(commands.start(workstream.workstreamId), projection, catalog());
    assert.equal(admission.admitted, true);
    assert.equal(admission.admitted && admission.policy, 'low_consequence');
    assert.equal(admission.scope.workstream_id, workstream.workstreamId);
  });

  test('unknown targets and runtimes are rejected with the scope that did resolve', () => {
    const { projection, commands, workstream } = setup();
    const missing = admitCommand(
      commands.start('01920000-0000-7000-8000-00000000ffff'),
      projection,
      catalog(),
    );
    assert.deepEqual(missing.admitted ? null : missing.rejection.code, 'workstream_not_found');
    const noRuntime = admitCommand(
      commands.start(workstream.workstreamId, 'opencode'),
      projection,
      catalog(),
    );
    assert.equal(noRuntime.admitted ? null : noRuntime.rejection.code, 'runtime_not_found');
    assert.equal(noRuntime.scope.workstream_id, workstream.workstreamId);
  });

  test('a declared-unsupported capability is rejected, never emulated', () => {
    const { b, projection, scope, commands } = setup();
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    const admission = admitCommand(commands.instruct(), projection, catalog());
    assert.equal(admission.admitted ? null : admission.rejection.code, 'capability_unsupported');
  });

  test('instructions are admitted at rest when supported, and refused before any session exists', () => {
    const { b, projection, scope, commands } = setup();
    const early = admitCommand(commands.instruct(), projection, catalog());
    assert.equal(early.admitted ? null : early.rejection.code, 'invalid_state');
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.equal(admitCommand(commands.instruct(), projection, catalog()).admitted, true);
  });

  test('interrupt needs a running turn and is review required', () => {
    const { b, projection, scope, commands } = setup();
    const atRest = admitCommand(commands.interrupt(), projection, catalog());
    assert.equal(atRest.admitted ? null : atRest.rejection.code, 'invalid_state');
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    const running = admitCommand(commands.interrupt(), projection, catalog());
    assert.equal(running.admitted && running.policy, 'review_required');
    const unsupported = admitCommand(
      commands.interrupt(),
      projection,
      catalog({ ...FULL, interrupt: false }),
    );
    assert.equal(
      unsupported.admitted ? null : unsupported.rejection.code,
      'capability_unsupported',
    );
  });

  test('approvals must be pending on the execution', () => {
    const { b, projection, scope, commands } = setup();
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    const none = admitCommand(commands.approve('a1'), projection, catalog());
    assert.equal(none.admitted ? null : none.rejection.code, 'approval_not_found');
    projection.apply(
      b.runtimeEvent(scope, 'runtime.approval.requested', {
        approval_id: 'a1',
        subject: { kind: 'tool_use', tool_name: 'bash', summary: 'Run it' },
      }),
    );
    const pending = admitCommand(commands.approve('a1'), projection, catalog());
    assert.equal(pending.admitted && pending.policy, 'review_required');
  });

  test('an approval on an unobservable execution cannot be answered', () => {
    const { b, projection, scope, commands } = setup();
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(
      b.runtimeEvent(scope, 'runtime.approval.requested', {
        approval_id: 'a1',
        subject: { kind: 'tool_use', tool_name: 'bash', summary: 'Run it' },
      }),
    );
    projection.apply(b.runtimeEvent(scope, 'runtime.connection.lost', { reason: 'Gone.' }));
    const admission = admitCommand(commands.approve('a1'), projection, catalog());
    assert.equal(admission.admitted ? null : admission.rejection.code, 'invalid_state');
  });
});
