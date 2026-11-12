import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  COMMAND_VARIANTS,
  type CommandEnvelope,
  type HereAction,
  type HereScope,
  type RuntimeDescriptor,
  type RuntimeId,
} from '@halcyonic/contracts';
import { admitCommand, type RuntimeCatalog } from './admission.ts';
import { COMMAND_FIELDS } from './command-fields.ts';
import { boundCommand, hereActions } from './here.ts';
import { COMMAND_POLICY, COMMAND_SAYING } from './policy.ts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

const MOCK: RuntimeDescriptor = {
  runtime_id: 'mock' as RuntimeId,
  kind: 'mock',
  display_name: 'Mock',
  synthetic: true,
  capabilities: {
    start_execution: true,
    instruct_at_rest: true,
    instruct_while_running: true,
    respond_to_approval: true,
    answer_question: true,
    interrupt: true,
  },
  model_choice: 'none',
  uses_project_location: false,
  reports_tool_activity: false,
};
const RUNTIMES: RuntimeCatalog = { get: (id) => (id === 'mock' ? MOCK : undefined) };

function world() {
  const b = new EventBuilder();
  const projection = new Projection();
  const project = b.project('Game', { path: '/tmp/game', name: 'game', created: false });
  const task = b.workstream(project.projectId, 'Make it night');
  const run = b.execution(project.projectId, task.workstreamId);
  for (const stored of [project.event, task.event, run.event]) projection.apply(stored);
  const ids = {
    projectId: project.projectId,
    workstreamId: task.workstreamId,
    executionId: run.executionId,
  };
  const scope: HereScope = {
    project_id: project.projectId,
    workstream_id: task.workstreamId,
    execution_id: run.executionId,
  };
  return { b, projection, ids, scope };
}

const types = (actions: readonly HereAction[]) =>
  actions.map((action) =>
    action.kind === 'command' ? action.command_type : `list:${action.list}`,
  );

describe('what can be done here (ADR 0060)', () => {
  test('all work offers a new project, and nothing of any project', () => {
    const { projection } = world();
    const all = { project_id: null, workstream_id: null, execution_id: null };
    assert.deepEqual(types(hereActions(all, projection, RUNTIMES).actions), ['project.create']);
  });

  test('a task offers what its state allows, its own first, from the same admission', () => {
    const { b, projection, ids, scope } = world();
    // Started by its runtime, at rest after a turn: it takes an instruction, and nothing runs to stop.
    projection.apply(b.runtimeEvent(ids, 'runtime.execution.started', { native_id: 'ses_1' }));
    projection.apply(b.runtimeEvent(ids, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(b.runtimeEvent(ids, 'runtime.turn.completed', { turn_id: 't1' }));
    const resting = hereActions(scope, projection, RUNTIMES);
    assert.equal(types(resting.actions)[0], 'execution.send_instruction', 'the main action first');
    assert.ok(!types(resting.actions).includes('execution.interrupt'));
    assert.ok(
      resting.refused.some(
        (refusal) =>
          refusal.command_type === 'execution.interrupt' && refusal.code === 'invalid_state',
      ),
      'left out with admission’s own code',
    );
    // The project's commands are reachable from the task too.
    assert.ok(types(resting.actions).includes('workstream.create'));
    // Running: Stop is offered.
    projection.apply(b.runtimeEvent(ids, 'runtime.turn.started', { turn_id: 't2' }));
    assert.ok(
      types(hereActions(scope, projection, RUNTIMES).actions).includes('execution.interrupt'),
    );
  });

  test('a project offers its lists, and leaves out what its state refuses', () => {
    const { projection, scope } = world();
    const project = { ...scope, workstream_id: null, execution_id: null };
    const { actions, refused } = hereActions(project, projection, RUNTIMES);
    assert.equal(types(actions)[0], 'workstream.create');
    assert.deepEqual(types(actions).slice(-2), ['list:tasks', 'list:versions']);
    // Nothing to restore yet, no preview running, nothing published.
    for (const commandType of ['project.restore', 'project.preview.stop', 'project.unpublish'])
      assert.ok(
        refused.some((refusal) => refusal.command_type === commandType),
        `${commandType} is left out`,
      );
    assert.ok(!types(actions).some((type) => type.startsWith('execution.')), 'no task’s commands');
  });

  test('every command offered with nothing left to give is one the whole admission admits', () => {
    const { b, projection, ids, scope } = world();
    projection.apply(b.runtimeEvent(ids, 'runtime.execution.started', { native_id: 'ses_1' }));
    projection.apply(b.runtimeEvent(ids, 'runtime.turn.started', { turn_id: 't1' }));
    for (const here of [scope, { ...scope, workstream_id: null, execution_id: null }]) {
      for (const action of hereActions(here, projection, RUNTIMES).actions) {
        if (action.kind !== 'command' || action.needs.some((need) => need.required)) continue;
        const bound = boundCommand(action.command_type as keyof typeof COMMAND_FIELDS, here);
        assert.ok(bound !== null);
        const whole = {
          ...bound,
          schema_version: 1,
          command_id: '00000000-0000-4000-8000-000000000001',
          issued_at: '2026-11-12T05:00:00.000Z',
          client: { name: 'test', version: null, device_label: null },
        } as CommandEnvelope;
        const admission = admitCommand(whole, projection, RUNTIMES);
        assert.ok(admission.admitted, `${action.command_type} is admitted once sent`);
      }
    }
  });

  test('never offers to say a secret, an approval, a question’s answers or a share (R6)', () => {
    const { b, projection, ids, scope } = world();
    projection.apply(b.runtimeEvent(ids, 'runtime.execution.started', { native_id: 'ses_1' }));
    const said = hereActions(scope, projection, RUNTIMES).actions.filter(
      (action) => action.kind === 'command' && action.sayable,
    );
    for (const action of said) {
      assert.ok(action.kind === 'command');
      assert.ok(
        ![
          'execution.respond_to_approval',
          'execution.answer_question',
          'project.share.start',
          'project.secret.set',
        ].includes(action.command_type),
        action.command_type,
      );
      assert.ok(!action.needs.some((need) => need.kind === 'secret' || need.kind === 'answers'));
    }
  });
});

describe('the declarations beside the policy', () => {
  test('every payload field of every command has its source, field for field with the contracts', () => {
    for (const variant of COMMAND_VARIANTS) {
      const type = variant.properties.command_type.const as string;
      if (type.startsWith('workspace.request.')) continue;
      const declared = Object.keys(COMMAND_FIELDS[type as keyof typeof COMMAND_FIELDS]).sort();
      const schema = Object.keys(variant.properties.payload.properties).sort();
      assert.deepEqual(declared, schema, type);
    }
  });

  test('a review confirm is only where the policy asks for one, and an undo names its undoing', () => {
    for (const [type, saying] of Object.entries(COMMAND_SAYING)) {
      if (saying.confirm === 'review')
        assert.equal(COMMAND_POLICY[type as keyof typeof COMMAND_POLICY], 'review_required', type);
      assert.equal(saying.confirm === 'undo', saying.undo !== undefined, type);
    }
    assert.equal(COMMAND_SAYING['project.preview.start'].undo, 'project.preview.stop');
  });

  test('every title is one to three words in Title Case', () => {
    for (const [type, { title }] of Object.entries(COMMAND_SAYING))
      assert.match(title, /^[A-Z][A-Za-z]*( (a|to|on|for|[A-Z][A-Za-z]*)){0,2}$/, type);
  });
});
