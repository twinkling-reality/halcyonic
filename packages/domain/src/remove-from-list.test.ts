import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { describe, test } from 'node:test';
import { deserialize, serialize } from 'node:v8';
import {
  COMMAND_SCHEMA_VERSION,
  type CommandEnvelope,
  type CommandId,
  type HereScope,
  type JournalId,
  type RuntimeDescriptor,
  type RuntimeId,
  type WorkspaceRequestId,
} from '@halcyonic/contracts';
import { admitCommand, OFF_LIST_WORDS, type RuntimeCatalog, removalRefusal } from './admission.ts';
import { hereActions } from './here.ts';
import { PROJECTION_STATE_FORMAT, Projection, type ProjectionState } from './projection.ts';
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

const FOLDER = { path: '/tmp/halcyonic-test/game', name: 'game', created: false };

function command<T extends CommandEnvelope['command_type']>(
  commandType: T,
  payload: Extract<CommandEnvelope, { command_type: T }>['payload'],
): CommandEnvelope {
  return {
    schema_version: COMMAND_SCHEMA_VERSION,
    command_id: randomUUID() as CommandId,
    issued_at: '2026-11-13T12:00:00.000Z',
    client: { name: 'test', version: null, device_label: null },
    command_type: commandType,
    payload,
  } as CommandEnvelope;
}

/** A project with one task whose one execution finished its turn, and a second project beside it. */
function world() {
  const b = new EventBuilder();
  const projection = new Projection();
  const project = b.project('Tide Game', FOLDER);
  const other = b.project('Other', {
    path: '/tmp/halcyonic-test/other',
    name: 'other',
    created: false,
  });
  const task = b.workstream(project.projectId, 'Make it night');
  const run = b.execution(project.projectId, task.workstreamId, FOLDER.path);
  for (const stored of [project.event, other.event, task.event, run.event])
    projection.apply(stored);
  const scope = {
    projectId: project.projectId,
    workstreamId: task.workstreamId,
    executionId: run.executionId,
  };
  const at = { project_id: project.projectId, workstream_id: null, execution_id: null } as const;
  const rest = () => {
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
  };
  const remove = () => projection.apply(b.controlPlane('project.removed_from_list', at, {}));
  const bringBack = (location = FOLDER) =>
    projection.apply(b.controlPlane('project.returned', at, { location }));
  return { b, projection, project, other, task, run, scope, at, rest, remove, bringBack };
}

describe('a project taken off the list (ADR 0063)', () => {
  test('leaves every listed read, and every read of the host stays whole', () => {
    const { projection, project, other, task, run, rest, remove } = world();
    rest();
    remove();
    assert.equal(projection.listed(project.projectId), false);
    assert.equal(projection.listed(other.projectId), true);
    assert.deepEqual(
      projection.listedProjects().map((p) => p.project_id),
      [other.projectId],
    );
    assert.deepEqual(projection.listedWorkstreams(), []);
    assert.deepEqual(projection.listedWorkstreams(project.projectId), []);
    assert.deepEqual(projection.listedExecutions(), []);
    // The host's own reads keep it: its secrets, its folder denied to other agents, a VR version's original.
    assert.equal(projection.project(project.projectId)?.name, 'Tide Game');
    assert.equal(projection.project(project.projectId)?.location?.path, FOLDER.path);
    assert.ok(projection.projects().some((p) => p.project_id === project.projectId));
    assert.equal(projection.workstream(task.workstreamId)?.project_id, project.projectId);
    assert.equal(projection.execution(run.executionId)?.project_id, project.projectId);
  });

  test("the removal's change carries the project once, marked, and none of its tasks; a late event of it carries nothing of it", () => {
    const { b, projection, project, scope, rest, at } = world();
    rest();
    const { changes } = projection.apply(b.controlPlane('project.removed_from_list', at, {}));
    assert.equal(changes.projects.length, 1);
    assert.equal(changes.projects[0]?.project_id, project.projectId);
    assert.ok(changes.projects[0]?.removed_at !== undefined);
    assert.deepEqual(changes.workstreams, []);
    assert.deepEqual(changes.executions, []);
    // A runtime's late output is projected into the kept state, and reaches no client.
    const late = projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't2' }));
    assert.deepEqual(late.changes.workstreams, []);
    assert.deepEqual(late.changes.executions, []);
  });

  test('coming back restores it whole, at the folder bound again, and its change carries every task and execution', () => {
    const { projection, project, task, run, rest, remove, bringBack } = world();
    rest();
    remove();
    const moved = { ...FOLDER, name: 'game' };
    const { changes } = bringBack(moved);
    assert.equal(projection.listed(project.projectId), true);
    assert.equal(projection.project(project.projectId)?.removed_at, undefined);
    assert.deepEqual(projection.project(project.projectId)?.location, moved);
    assert.deepEqual(
      changes.workstreams.map((w) => w.workstream_id),
      [task.workstreamId],
    );
    assert.deepEqual(
      changes.executions.map((e) => e.execution_id),
      [run.executionId],
    );
    assert.equal(changes.projects[0]?.removed_at, undefined);
    assert.deepEqual(
      projection.listedWorkstreams(project.projectId).map((w) => w.workstream_id),
      [task.workstreamId],
    );
  });

  test('a removal of a project already off, a return of one on the list and either for no project are noted, changing nothing', () => {
    const { b, projection, project, rest, remove, bringBack, at } = world();
    rest();
    assert.equal(bringBack().notes[0]?.code, 'out_of_order');
    assert.equal(projection.listed(project.projectId), true);
    remove();
    assert.equal(remove().notes[0]?.code, 'out_of_order');
    assert.equal(projection.listed(project.projectId), false);
    const nobody = { ...at, project_id: b.id() as typeof at.project_id };
    assert.equal(
      projection.apply(b.controlPlane('project.removed_from_list', nobody, {})).notes[0]?.code,
      'unknown_entity',
    );
    assert.equal(
      projection.apply(b.controlPlane('project.returned', nobody, { location: FOLDER })).notes[0]
        ?.code,
      'unknown_entity',
    );
  });

  test('is kept across a start that restores the projection from its checkpoint, at the raised format', () => {
    const { projection, project, rest, remove } = world();
    rest();
    remove();
    const state = deserialize(serialize(projection.exportState())) as ProjectionState;
    assert.equal(state.format, PROJECTION_STATE_FORMAT);
    // 23 brought `removedAt`; a later format keeps it.
    assert.ok(PROJECTION_STATE_FORMAT >= 23);
    const restarted = new Projection();
    restarted.restoreState(state);
    assert.equal(restarted.listed(project.projectId), false);
    assert.equal(restarted.project(project.projectId)?.name, 'Tide Game');
  });
});

describe('admission of a project taken off the list (ADR 0063)', () => {
  test('refuses every command naming it, its task or its execution, in the same words, before their own checks', () => {
    const { projection, project, task, run, rest, remove } = world();
    rest();
    remove();
    const refused = [
      command('project.check', { project_id: project.projectId }),
      command('project.show_in_finder', { project_id: project.projectId }),
      command('project.remove_from_list', {
        project_id: project.projectId,
        review: 'a'.repeat(64),
      }),
      command('workstream.create', {
        project_id: project.projectId,
        title: 'Another',
        objective: null,
      }),
      command('execution.interrupt', { execution_id: run.executionId }),
      command('workstream.fit_to_project', { workstream_id: task.workstreamId }),
    ];
    for (const sent of refused) {
      const admission = admitCommand(sent, projection, RUNTIMES);
      assert.equal(admission.admitted, false, sent.command_type);
      if (admission.admitted) continue;
      assert.deepEqual(admission.rejection, { code: 'project_not_found', message: OFF_LIST_WORDS });
    }
  });

  test('ending a request about it stays open, so nothing about it is left running; continuing one is refused (AUDIT2 S4)', () => {
    const { b, projection, at, rest, remove } = world();
    rest();
    const requestId = b.id() as WorkspaceRequestId;
    projection.apply(
      b.controlPlane('workspace.request.created', at, {
        request: {
          ...at,
          request_id: requestId,
          journal_id: b.id() as JournalId,
          original_text: 'Take this project off my list.',
          status: 'running',
          reply: null,
          steps: [],
          earlier_steps: 0,
          created_at: '2026-11-13T12:00:00.000Z',
          updated_at: '2026-11-13T12:00:00.000Z',
        },
        principal: { kind: 'local' },
      }),
    );
    remove();
    const cancel = admitCommand(
      command('workspace.request.cancel', { request_id: requestId }),
      projection,
      RUNTIMES,
    );
    assert.equal(cancel.admitted, true, cancel.admitted ? '' : cancel.rejection.message);
    const resume = admitCommand(
      command('workspace.request.resume', { request_id: requestId, text: 'And one more thing.' }),
      projection,
      RUNTIMES,
    );
    assert.deepEqual(resume.admitted ? null : resume.rejection, {
      code: 'project_not_found',
      message: OFF_LIST_WORDS,
    });
  });

  test('is admitted at rest, and refused in words while a task is at work, without stopping it', () => {
    const { b, projection, project, scope } = world();
    const remove = command('project.remove_from_list', {
      project_id: project.projectId,
      review: 'a'.repeat(64),
    });
    const starting = admitCommand(remove, projection, RUNTIMES);
    assert.equal(starting.admitted, false);
    assert.match(
      starting.admitted ? '' : starting.rejection.message,
      /Stop it, then remove the project\.$/,
    );
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    assert.equal(admitCommand(remove, projection, RUNTIMES).admitted, false);
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.equal(admitCommand(remove, projection, RUNTIMES).admitted, true);
  });

  test('while a removal waits for its outcome, every other command of the project is refused, and it does not refuse itself', () => {
    const { b, projection, project, at, rest } = world();
    rest();
    const remove = command('project.remove_from_list', {
      project_id: project.projectId,
      review: 'a'.repeat(64),
    });
    projection.apply(
      b.controlPlane('command.accepted', at, {
        command: remove,
        policy: 'review_required',
        received_via: 'internal',
        principal: null,
      }),
    );
    const check = admitCommand(
      command('project.check', { project_id: project.projectId }),
      projection,
      RUNTIMES,
    );
    assert.deepEqual(check.admitted ? null : check.rejection, {
      code: 'invalid_state',
      message: 'This project is being taken off the list. Try again in a moment.',
    });
    const view = projection.project(project.projectId);
    assert.ok(view !== undefined);
    assert.notEqual(removalRefusal(projection, view), null);
    assert.equal(removalRefusal(projection, view, remove.command_id), null);
  });

  test('one GitHub action of a project at a time, each told the one under way by its button (the lead, 20:46Z)', () => {
    const { b, projection, project, at, rest } = world();
    rest();
    const sending = command('project.send', {
      project_id: project.projectId,
      review: 'a'.repeat(64),
    });
    projection.apply(
      b.controlPlane('command.accepted', at, {
        command: sending,
        policy: 'review_required',
        received_via: 'internal',
        principal: null,
      }),
    );
    const again = admitCommand(
      command('project.send', { project_id: project.projectId, review: 'b'.repeat(64) }),
      projection,
      RUNTIMES,
    );
    assert.deepEqual(again.admitted ? null : again.rejection, {
      code: 'invalid_state',
      message:
        'Sending for review in this project is still under way, so Send for Review was not done. Try again once it has finished.',
    });
    // A command under way has no Stop: choosing a check is told to try again, never to stop it (AUDIT2 S1).
    const checking = admitCommand(
      command('project.own_check.choose', { project_id: project.projectId, scripts: [] }),
      projection,
      RUNTIMES,
    );
    assert.deepEqual(checking.admitted ? null : checking.rejection, {
      code: 'invalid_state',
      message:
        'Sending for review in this project is still under way, so Choose Check was not done. Try again once it has finished.',
    });
    for (const [sent, press] of [
      [
        command('project.unpublish', { project_id: project.projectId, commit: 'c'.repeat(40) }),
        'Take Down',
      ],
      [command('project.make_private', { project_id: project.projectId }), 'Make Private'],
    ] as const) {
      const refused = admitCommand(sent, projection, RUNTIMES);
      assert.deepEqual(
        refused.admitted ? null : refused.rejection,
        {
          code: 'invalid_state',
          message: `Sending for review in this project is still under way, so ${press} was not done. Try again once it has finished.`,
        },
        sent.command_type,
      );
    }
  });

  test('a busy task waiting for you is told to answer it or stop it, and a long title is cut at a word (AUDIT2 N1, N2)', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project('Birds', FOLDER);
    const long =
      'Field guide cards with a photo, a range map and a call for every bird we have seen in the valley this year, sorted by season and by how often we saw them, with a print layout for each card and a short'.slice(
        0,
        200,
      );
    const task = b.workstream(project.projectId, long);
    const run = b.execution(project.projectId, task.workstreamId, FOLDER.path);
    for (const stored of [project.event, task.event, run.event]) projection.apply(stored);
    const scope = {
      projectId: project.projectId,
      workstreamId: task.workstreamId,
      executionId: run.executionId,
    };
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(
      b.runtimeEvent(scope, 'runtime.approval.requested', {
        approval_id: 'a1',
        subject: { kind: 'tool_use', tool_name: 'bash', summary: 'Run it' },
        complete: true,
      }),
    );
    const choosing = admitCommand(
      command('project.own_check.choose', { project_id: project.projectId, scripts: [] }),
      projection,
      RUNTIMES,
    );
    assert.deepEqual(choosing.admitted ? null : choosing.rejection, {
      code: 'invalid_state',
      message:
        'The task Field guide cards with a photo, a range map and a call for… is waiting for you in this project, so Choose Check was not done. Answer it or stop it, then try again.',
    });
    // Send too: a task waiting for you does not finish by itself (found beside AUDIT2 S1).
    const sending = admitCommand(
      command('project.send', { project_id: project.projectId, review: 'a'.repeat(64) }),
      projection,
      RUNTIMES,
    );
    assert.equal(
      sending.admitted ? '' : sending.rejection.message,
      'The task Field guide cards with a photo, a range map and a call for… is waiting for you in this project, so Send for Review was not done. Answer it or stop it, then try again.',
    );
    const view = projection.project(project.projectId);
    assert.ok(view !== undefined);
    assert.match(
      removalRefusal(projection, view) ?? '',
      /Answer it or stop it, then remove the project\.$/,
    );
  });

  test('a project with no folder that holds secrets is refused, since it could not come back to remove them', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const bare = b.project('No Folder', null);
    projection.apply(bare.event);
    const at = { project_id: bare.projectId, workstream_id: null, execution_id: null } as const;
    const remove = command('project.remove_from_list', {
      project_id: bare.projectId,
      review: 'a'.repeat(64),
    });
    assert.equal(admitCommand(remove, projection, RUNTIMES).admitted, true);
    projection.apply(
      b.controlPlane('project.secret.stored', at, { name: 'API_TOKEN', replaced: false }),
    );
    const refused = admitCommand(remove, projection, RUNTIMES);
    assert.deepEqual(refused.admitted ? null : refused.rejection, {
      code: 'invalid_state',
      message:
        'Remove its secrets first. It has no folder, so it could not be brought back to remove them later.',
    });
  });
});

describe('the list of what can be done here, for taking a project off (ADR 0060, 0063)', () => {
  test('offers it at rest, last of the project commands, and leaves it out while a task is at work', () => {
    const { b, projection, project, scope, rest } = world();
    const here: HereScope = {
      project_id: project.projectId,
      workstream_id: null,
      execution_id: null,
    };
    const offered = () =>
      hereActions(here, projection, RUNTIMES).actions.some(
        (action) => action.kind === 'command' && action.command_type === 'project.remove_from_list',
      );
    assert.equal(offered(), false);
    rest();
    assert.equal(offered(), true);
    const entry = hereActions(here, projection, RUNTIMES).actions.find(
      (action) => action.kind === 'command' && action.command_type === 'project.remove_from_list',
    );
    assert.ok(entry?.kind === 'command' && entry.sayable && entry.confirm === 'review');
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't2' }));
    assert.equal(offered(), false);
  });

  test('offers nothing for a project taken off', () => {
    const { projection, project, rest, remove } = world();
    rest();
    remove();
    const here: HereScope = {
      project_id: project.projectId,
      workstream_id: null,
      execution_id: null,
    };
    const { actions } = hereActions(here, projection, RUNTIMES);
    assert.deepEqual(actions, []);
  });
});
