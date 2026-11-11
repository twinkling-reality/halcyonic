import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type {
  CommandEnvelope,
  CommandId,
  ExecutionId,
  RuntimeCapabilities,
  RuntimeDescriptor,
  RuntimeId,
} from '@halcyonic/contracts';
import { admitCommand, journaledRejection, type RuntimeCatalog } from './admission.ts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

const FULL: RuntimeCapabilities = {
  start_execution: true,
  instruct_at_rest: true,
  instruct_while_running: false,
  respond_to_approval: true,
  answer_question: false,
  interrupt: true,
};

function catalog(
  capabilities: RuntimeCapabilities = FULL,
  modelChoice: RuntimeDescriptor['model_choice'] = 'none',
  usesProjectLocation = false,
): RuntimeCatalog {
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'mock' as RuntimeId,
    kind: 'mock',
    display_name: 'Mock',
    synthetic: true,
    capabilities,
    model_choice: modelChoice,
    uses_project_location: usesProjectLocation,
    reports_tool_activity: false,
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
  // A task with no execution, so a start on it meets no active one.
  const idle = b.workstream(project.projectId, 'Idle');
  projection.apply(idle.event);
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
    approve: (approvalId: string, decision: 'approve' | 'deny' = 'approve'): CommandEnvelope => ({
      ...base(),
      command_type: 'execution.respond_to_approval',
      payload: {
        execution_id: execution.executionId,
        approval_id: approvalId,
        decision,
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
        model_ref: null,
      },
    }),
  };
  return { b, projection, scope, commands, workstream, idle };
}

describe('command admission', () => {
  test('starting on a known workstream with a capable runtime is admitted as low consequence', () => {
    const { projection, commands, idle } = setup();
    const admission = admitCommand(commands.start(idle.workstreamId), projection, catalog());
    assert.equal(admission.admitted, true);
    assert.equal(admission.admitted && admission.policy, 'low_consequence');
    assert.equal(admission.scope.workstream_id, idle.workstreamId);
  });

  test('a chosen model is admitted only for a runtime that lists its models', () => {
    const { projection, commands, idle } = setup();
    const start = commands.start(idle.workstreamId);
    const choosing = {
      ...start,
      payload: { ...(start.payload as object), model_ref: 'ollama/qwen3.6:35b-a3b-nvfp4' },
    } as typeof start;
    const refused = admitCommand(choosing, projection, catalog());
    assert.deepEqual(refused.admitted ? null : refused.rejection, {
      code: 'capability_unsupported',
      message: 'Runtime mock does not offer a choice of model.',
    });
    const admitted = admitCommand(choosing, projection, catalog(FULL, 'listed'));
    // Whether the model is still listed is the runtime's to check, at the start itself.
    assert.equal(admitted.admitted, true);
  });

  test('a runtime that lists its models never chooses one by itself: a start must name one', () => {
    const { projection, commands, idle } = setup();
    const refused = admitCommand(
      commands.start(idle.workstreamId),
      projection,
      catalog(FULL, 'listed'),
    );
    assert.deepEqual(refused.admitted ? null : refused.rejection, {
      code: 'model_required',
      message: 'Choose a model: this runtime lists the models it can use.',
    });
    // A runtime without a list chooses its own model, as before.
    assert.equal(
      admitCommand(commands.start(idle.workstreamId), projection, catalog()).admitted,
      true,
    );
  });

  test('never asking is admitted only for a runtime that offers it, and asking always (ADR 0043)', () => {
    const { projection, commands, idle } = setup();
    const start = commands.start(idle.workstreamId);
    const at = (approval_level: 'ask' | 'never_ask') =>
      ({ ...start, payload: { ...(start.payload as object), approval_level } }) as typeof start;
    const refused = admitCommand(at('never_ask'), projection, catalog());
    assert.deepEqual(refused.admitted ? null : refused.rejection, {
      code: 'capability_unsupported',
      message: 'Runtime mock does not offer working without asking.',
    });
    const offering = catalog();
    const descriptor = offering.get('mock' as RuntimeId);
    assert.ok(descriptor);
    const offers: RuntimeCatalog = {
      get: (id) => (id === 'mock' ? { ...descriptor, offers_never_ask: true } : undefined),
    };
    assert.equal(admitCommand(at('never_ask'), projection, offers).admitted, true);
    assert.equal(admitCommand(at('ask'), projection, catalog()).admitted, true);
  });

  test('a runtime that works in the project folder is refused for a project without one', () => {
    const { b, projection, commands, idle } = setup();
    const refused = admitCommand(
      commands.start(idle.workstreamId),
      projection,
      catalog(FULL, 'none', true),
    );
    assert.equal(refused.admitted ? null : refused.rejection.code, 'location_required');
    assert.equal(refused.scope.workstream_id, idle.workstreamId);
    // A runtime that uses no folder starts anywhere, as before.
    assert.equal(
      admitCommand(commands.start(idle.workstreamId), projection, catalog()).admitted,
      true,
    );

    const located = b.project('Located', { path: '/work/app', name: 'app', created: false });
    const inside = b.workstream(located.projectId);
    projection.apply(located.event);
    projection.apply(inside.event);
    const admitted = admitCommand(
      commands.start(inside.workstreamId),
      projection,
      catalog(FULL, 'none', true),
    );
    // The folder itself is the host's to check; the domain knows only that there is one.
    assert.equal(admitted.admitted, true);
  });

  test('a task runs one execution at a time: a start is refused while one is active or a command to it is under way, and admitted at rest (ADMIT)', () => {
    const { b, projection, scope, commands, workstream } = setup();
    const start = () =>
      admitCommand(commands.start(workstream.workstreamId), projection, catalog());
    const refusal = (state: string) => ({
      code: 'invalid_state',
      message: `This task is ${state}. Stop it first, or start another task.`,
    });
    // Created, and its runtime has not begun a turn: starting.
    const starting = start();
    assert.deepEqual(starting.admitted ? null : starting.rejection, refusal('starting'));
    assert.equal(starting.scope.workstream_id, workstream.workstreamId);
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    const running = start();
    assert.deepEqual(running.admitted ? null : running.rejection, refusal('running'));
    projection.apply(
      b.runtimeEvent(scope, 'runtime.test_run.started', { test_run_id: 'r1', label: null }),
    );
    const verifying = start();
    assert.deepEqual(verifying.admitted ? null : verifying.rejection, refusal('checking its work'));
    projection.apply(
      b.runtimeEvent(scope, 'runtime.approval.requested', {
        approval_id: 'a1',
        subject: { kind: 'tool_use', tool_name: 'bash', summary: 'Run it' },
        complete: true,
      }),
    );
    const waiting = start();
    assert.deepEqual(waiting.admitted ? null : waiting.rejection, refusal('waiting for you'));
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    // At rest another start is admitted, as a switch to another agent is.
    assert.equal(start().admitted, true);

    // An instruction accepted at rest has no turn yet, so the task still reads at rest until the
    // runtime begins one: the start waits for its outcome.
    const instruct = commands.instruct();
    const commandScope = {
      project_id: scope.projectId,
      workstream_id: scope.workstreamId,
      execution_id: scope.executionId,
    };
    projection.apply(
      b.controlPlane('command.accepted', commandScope, {
        command: instruct,
        policy: 'low_consequence',
        received_via: 'internal',
        principal: null,
      }),
    );
    const underWay = start();
    assert.deepEqual(underWay.admitted ? null : underWay.rejection, {
      code: 'invalid_state',
      message:
        'An instruction for this task is still under way. Wait for its outcome, or start another task.',
    });
    projection.apply(
      b.controlPlane('command.completed', commandScope, {
        command_id: instruct.command_id,
        command_type: 'execution.send_instruction',
        result: null,
      }),
    );
    assert.equal(start().admitted, true);

    // One at a time is per task: another task of the project starts while this one runs.
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't2' }));
    const other = b.workstream(scope.projectId, 'Other');
    projection.apply(other.event);
    assert.equal(
      admitCommand(commands.start(other.workstreamId), projection, catalog()).admitted,
      true,
    );
  });

  test('an instruction to a resting execution is refused while another of its task is active or a start for it is under way (ADMIT-2)', () => {
    const { b, projection, scope, commands, workstream } = setup();
    const instruct = () => admitCommand(commands.instruct(), projection, catalog());
    const refusal = (state: string) => ({
      code: 'invalid_state',
      message: `This task is ${state} in another execution. Stop it first, or instruct that one.`,
    });
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.equal(instruct().admitted, true, 'at rest, and alone in its task');

    // A switch: a newer execution of the task, which the instruction would run beside.
    const newer = b.execution(scope.projectId, workstream.workstreamId);
    projection.apply(newer.event);
    const newerScope = { ...scope, executionId: newer.executionId };
    const starting = instruct();
    assert.deepEqual(starting.admitted ? null : starting.rejection, refusal('starting'));
    assert.equal(starting.scope.execution_id, scope.executionId);
    projection.apply(b.runtimeEvent(newerScope, 'runtime.turn.started', { turn_id: 'n1' }));
    const running = instruct();
    assert.deepEqual(running.admitted ? null : running.rejection, refusal('running'));
    projection.apply(b.runtimeEvent(newerScope, 'runtime.turn.completed', { turn_id: 'n1' }));
    assert.equal(instruct().admitted, true, 'both at rest');

    // A start for the task accepted and without its outcome yet.
    const start = commands.start(workstream.workstreamId);
    const startScope = {
      project_id: scope.projectId,
      workstream_id: scope.workstreamId,
      execution_id: null,
    };
    projection.apply(
      b.controlPlane('command.accepted', startScope, {
        command: start,
        policy: 'low_consequence',
        received_via: 'internal',
        principal: null,
      }),
    );
    const pending = instruct();
    assert.deepEqual(pending.admitted ? null : pending.rejection, {
      code: 'invalid_state',
      message: 'A start for this task is still under way. Wait for its outcome first.',
    });
    projection.apply(
      b.controlPlane('command.failed', startScope, {
        command_id: start.command_id,
        command_type: 'execution.start',
        failure: { code: 'runtime_unavailable', message: 'Gone.', effect: 'none' },
      }),
    );
    assert.equal(instruct().admitted, true);
  });

  test('one task at a time in a folder: a start or an instruction is refused while another task works there, inside it or around it (FOLDER)', () => {
    const { b, projection, commands } = setup();
    const folder = (path: string) => ({
      path,
      name: path.split('/').at(-1) ?? path,
      created: false,
    });
    const inFolder = catalog(FULL, 'none', true);
    const project = b.project('App', folder('/work/app'));
    projection.apply(project.event);
    const first = b.workstream(project.projectId, 'First');
    const second = b.workstream(project.projectId, 'Second');
    projection.apply(first.event);
    projection.apply(second.event);
    const running = b.execution(project.projectId, first.workstreamId, '/work/app');
    projection.apply(running.event);
    const runningScope = {
      projectId: project.projectId,
      workstreamId: first.workstreamId,
      executionId: running.executionId,
    };
    projection.apply(b.runtimeEvent(runningScope, 'runtime.turn.started', { turn_id: 't1' }));
    const start = (workstreamId: string) =>
      admitCommand(commands.start(workstreamId), projection, inFolder);
    const refusal = {
      code: 'invalid_state',
      message:
        'Another task is running in this folder. Wait for it to finish or stop it, then try again.',
    };
    const refused = start(second.workstreamId);
    assert.deepEqual(refused.admitted ? null : refused.rejection, refusal);
    assert.equal(refused.scope.workstream_id, second.workstreamId);

    // Another project bound to the same folder, or to one inside it or around it, shares it.
    const taskIn = (path: string) => {
      const other = b.project('Other', folder(path));
      const task = b.workstream(other.projectId, 'Task');
      projection.apply(other.event);
      projection.apply(task.event);
      return task.workstreamId;
    };
    for (const path of ['/work/app', '/work/app/web', '/work']) {
      const sharing = start(taskIn(path));
      assert.deepEqual(sharing.admitted ? null : sharing.rejection, refusal, path);
    }
    // A folder whose name only begins the same way is another folder.
    assert.equal(start(taskIn('/work/apple')).admitted, true);
    // A runtime that works in no folder touches none of it.
    assert.equal(
      admitCommand(commands.start(second.workstreamId), projection, catalog()).admitted,
      true,
    );

    // An instruction that sets a resting execution of another task going in the folder waits too.
    const resting = b.execution(project.projectId, second.workstreamId, '/work/app');
    projection.apply(resting.event);
    const restingScope = {
      projectId: project.projectId,
      workstreamId: second.workstreamId,
      executionId: resting.executionId,
    };
    projection.apply(b.runtimeEvent(restingScope, 'runtime.turn.started', { turn_id: 'r1' }));
    projection.apply(b.runtimeEvent(restingScope, 'runtime.turn.completed', { turn_id: 'r1' }));
    const instruct: CommandEnvelope = {
      ...commands.instruct(),
      command_type: 'execution.send_instruction',
      payload: { execution_id: resting.executionId, text: 'Continue.' },
    };
    const waiting = admitCommand(instruct, projection, inFolder);
    assert.deepEqual(waiting.admitted ? null : waiting.rejection, refusal);

    // Once the first task is at rest the folder is free for either.
    projection.apply(b.runtimeEvent(runningScope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.equal(admitCommand(instruct, projection, inFolder).admitted, true);
    assert.equal(start(second.workstreamId).admitted, true);

    // A start for another task there, accepted and without its outcome, holds the folder.
    const pendingStart = commands.start(first.workstreamId);
    const startScope = {
      project_id: project.projectId,
      workstream_id: first.workstreamId,
      execution_id: null,
    };
    projection.apply(
      b.controlPlane('command.accepted', startScope, {
        command: pendingStart,
        policy: 'low_consequence',
        received_via: 'internal',
        principal: null,
      }),
    );
    // While the host may still be making that task a copy of its own, its start holds nothing
    // here: the host asks again before it falls back to this folder (ADR 0045).
    assert.equal(start(second.workstreamId).admitted, true);
    // Once refused a copy, the task works in this folder, and its start holds it.
    projection.apply(
      b.controlPlane('workstream.copy_refused', startScope, {
        code: 'linked_repository',
        message: "The folder's .git is a file.",
      }),
    );
    const underWay = start(second.workstreamId);
    assert.deepEqual(underWay.admitted ? null : underWay.rejection, {
      code: 'invalid_state',
      message:
        'A start for another task in this folder is still under way. Wait for its outcome, then try again.',
    });
  });

  test('a task whose start may have reached its agent holds the folder; one cut short by a restart does not; a starting one is waited for (FOLDER)', () => {
    const { b, projection, commands } = setup();
    const inFolder = catalog(FULL, 'none', true);
    const project = b.project('App', { path: '/work/app', name: 'app', created: false });
    projection.apply(project.event);
    const first = b.workstream(project.projectId, 'First');
    const second = b.workstream(project.projectId, 'Second');
    projection.apply(first.event);
    projection.apply(second.event);
    const start = () => {
      const admission = admitCommand(commands.start(second.workstreamId), projection, inFolder);
      return admission.admitted ? null : admission.rejection.message;
    };
    const created = b.execution(project.projectId, first.workstreamId, '/work/app');
    projection.apply(created.event);
    assert.equal(
      start(),
      'Another task is starting in this folder. Wait for it to start, then try again.',
      'a starting task has no Stop yet',
    );
    const scope = {
      project_id: project.projectId,
      workstream_id: first.workstreamId,
      execution_id: created.executionId,
    };
    // A start that timed out may still have set its agent to work, as a cold model does.
    projection.apply(
      b.controlPlane('execution.state_unknown', scope, {
        code: 'start_outcome_unknown',
        message: 'The start timed out.',
      }),
    );
    assert.equal(
      start(),
      'Another task in this folder may still be running. Wait for it, then try again.',
    );
    // No runtime session survives a restart, so nothing of that task works there any more.
    const cut = b.execution(project.projectId, first.workstreamId, '/work/app');
    const before = new Projection();
    for (const event of [project.event, first.event, second.event, cut.event]) before.apply(event);
    before.apply(
      b.controlPlane(
        'execution.state_unknown',
        { ...scope, execution_id: cut.executionId },
        { code: 'control_plane_restarted', message: 'Restarted.' },
      ),
    );
    assert.equal(
      admitCommand(commands.start(second.workstreamId), before, inFolder).admitted,
      true,
    );
  });

  test('setting a location is admitted only for a known project', () => {
    const { projection, scope } = setup();
    const setLocation = (projectId: string): CommandEnvelope => ({
      schema_version: 1,
      command_id: '00000000-0000-4000-8000-0000000000aa' as CommandId,
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: 'project.set_location',
      payload: {
        project_id: projectId as never,
        location: { kind: 'existing_folder', root: '/work', folder_name: 'app' },
      },
    });
    const admitted = admitCommand(setLocation(scope.projectId), projection, catalog());
    // It changes where the project's later work runs: the person reads the review first (ADR 0051).
    assert.equal(admitted.admitted && admitted.policy, 'review_required');
    assert.equal(admitted.scope.project_id, scope.projectId);
    const missing = admitCommand(
      setLocation('01920000-0000-7000-8000-00000000ffff'),
      projection,
      catalog(),
    );
    assert.equal(missing.admitted ? null : missing.rejection.code, 'project_not_found');
  });

  test('making a VR version is admitted only for a known project that has a folder (M1.8c-1)', () => {
    const { b, projection, scope } = setup();
    const make = (projectId: string): CommandEnvelope => ({
      schema_version: 1,
      command_id: '00000000-0000-4000-8000-0000000000ab' as CommandId,
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: 'project.make_vr_version',
      payload: { original_project_id: projectId as never, root: '/work', folder_name: 'garden-vr' },
    });
    // The setup project has no folder: there is nothing to read the game from.
    const noFolder = admitCommand(make(scope.projectId), projection, catalog());
    assert.equal(noFolder.admitted ? null : noFolder.rejection.code, 'location_required');
    const garden = b.project('Garden', { path: '/work/garden', name: 'garden', created: false });
    projection.apply(garden.event);
    const admitted = admitCommand(make(garden.projectId), projection, catalog());
    assert.equal(admitted.admitted && admitted.policy, 'low_consequence');
    assert.equal(admitted.scope.project_id, garden.projectId);
    const missing = admitCommand(
      make('01920000-0000-7000-8000-00000000ffff'),
      projection,
      catalog(),
    );
    assert.equal(missing.admitted ? null : missing.rejection.code, 'project_not_found');
  });

  test('linking a catalog repository is admitted for a known project; unlinking needs a link', () => {
    const { b, projection, scope } = setup();
    let n = 0;
    const command = (
      type: 'project.link_catalog' | 'project.unlink_catalog',
      projectId: string,
    ): CommandEnvelope => {
      n += 1;
      const base = {
        schema_version: 1 as const,
        command_id: `00000000-0000-4000-8000-0000000001${String(n).padStart(2, '0')}` as CommandId,
        issued_at: '2026-10-29T10:00:00.000Z',
        client: { name: 'test', version: null, device_label: null },
      };
      return type === 'project.link_catalog'
        ? {
            ...base,
            command_type: type,
            payload: {
              project_id: projectId as never,
              catalog: { source: 'omnorium', repository_id: 7, full_name: 'acme/app' },
            },
          }
        : { ...base, command_type: type, payload: { project_id: projectId as never } };
    };
    const linked = admitCommand(
      command('project.link_catalog', scope.projectId),
      projection,
      catalog(),
    );
    assert.equal(linked.admitted && linked.policy, 'low_consequence');
    assert.equal(linked.scope.project_id, scope.projectId);
    const unknown = '01920000-0000-7000-8000-00000000ffff';
    for (const type of ['project.link_catalog', 'project.unlink_catalog'] as const) {
      const missing = admitCommand(command(type, unknown), projection, catalog());
      assert.equal(missing.admitted ? null : missing.rejection.code, 'project_not_found', type);
    }
    const notLinked = admitCommand(
      command('project.unlink_catalog', scope.projectId),
      projection,
      catalog(),
    );
    assert.equal(notLinked.admitted ? null : notLinked.rejection.code, 'invalid_state');

    const projectScope = { project_id: scope.projectId, workstream_id: null, execution_id: null };
    projection.apply(
      b.controlPlane('project.catalog_linked', projectScope, {
        catalog: { source: 'omnorium', repository_id: 7, full_name: 'acme/app' },
        previous: null,
      }),
    );
    const unlinked = admitCommand(
      command('project.unlink_catalog', scope.projectId),
      projection,
      catalog(),
    );
    assert.equal(unlinked.admitted && unlinked.policy, 'low_consequence');
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
        complete: true,
      }),
    );
    const pending = admitCommand(commands.approve('a1'), projection, catalog());
    assert.equal(pending.admitted && pending.policy, 'review_required');
  });

  test('a request never shown whole can be denied, never approved', () => {
    const { b, projection, scope, commands } = setup();
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(
      b.runtimeEvent(scope, 'runtime.approval.requested', {
        approval_id: 'a1',
        subject: { kind: 'tool_use', tool_name: 'shell', summary: 'echo one' },
        complete: false,
      }),
    );
    const approve = admitCommand(commands.approve('a1'), projection, catalog());
    assert.deepEqual(approve.admitted ? null : approve.rejection, {
      code: 'approval_not_whole',
      message: 'This request was never shown whole, so it can only be denied.',
    });
    const deny = admitCommand(commands.approve('a1', 'deny'), projection, catalog());
    assert.equal(deny.admitted, true);
    // While it cannot be answered at all, that is what is said, never "deny it".
    projection.apply(b.runtimeEvent(scope, 'runtime.connection.lost', { reason: 'Gone.' }));
    const unanswerable = admitCommand(commands.approve('a1'), projection, catalog());
    assert.equal(unanswerable.admitted ? null : unanswerable.rejection.code, 'invalid_state');
  });

  test('an approval on an unobservable execution cannot be answered', () => {
    const { b, projection, scope, commands } = setup();
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(
      b.runtimeEvent(scope, 'runtime.approval.requested', {
        approval_id: 'a1',
        subject: { kind: 'tool_use', tool_name: 'bash', summary: 'Run it' },
        complete: true,
      }),
    );
    projection.apply(b.runtimeEvent(scope, 'runtime.connection.lost', { reason: 'Gone.' }));
    const admission = admitCommand(commands.approve('a1'), projection, catalog());
    assert.equal(admission.admitted ? null : admission.rejection.code, 'invalid_state');
  });
});

describe('answering a question', () => {
  const ANSWERS: RuntimeCapabilities = { ...FULL, answer_question: true };
  const prompts = [
    {
      key: 'q0',
      header: 'Colour',
      text: 'Which colour?',
      options: [
        { label: 'red', description: null },
        { label: 'blue', description: null },
      ],
      multiple: false,
      free_text: true,
      secret: false,
    },
    {
      key: 'q1',
      header: 'Pages',
      text: 'Which pages?',
      options: [
        { label: 'Orders', description: null },
        { label: 'Settings', description: null },
      ],
      multiple: true,
      free_text: false,
      secret: false,
    },
  ];
  const answer = (answers: unknown, questionId = 'frm_1'): CommandEnvelope =>
    ({
      schema_version: 1,
      command_id: '00000000-0000-4000-8000-0000000000b1' as CommandId,
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: 'execution.answer_question',
      payload: { execution_id: undefined as never, question_id: questionId, answers },
    }) as CommandEnvelope;
  function asked(answerable = true, asking = prompts) {
    const context = setup();
    const { b, projection, scope } = context;
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(
      b.runtimeEvent(scope, 'runtime.question.asked', {
        question_id: 'frm_1',
        prompts: asking,
        answerable,
      }),
    );
    const command = (answers: unknown, questionId?: string) => {
      const envelope = answer(answers, questionId);
      (envelope.payload as { execution_id: string }).execution_id = scope.executionId;
      return envelope;
    };
    return { ...context, command };
  }
  const good = [
    { key: 'q0', selected: ['blue'], text: null },
    { key: 'q1', selected: ['Orders', 'Settings'], text: null },
  ];

  test('answers that fit the questions are admitted as low consequence', () => {
    const { projection, command } = asked();
    const admission = admitCommand(command(good), projection, catalog(ANSWERS));
    assert.equal(admission.admitted && admission.policy, 'low_consequence');
    const typed = admitCommand(
      command([
        { key: 'q0', selected: [], text: 'green, as on the poster' },
        { key: 'q1', selected: ['Orders'], text: null },
      ]),
      projection,
      catalog(ANSWERS),
    );
    assert.equal(typed.admitted, true);
  });

  test('a runtime that cannot carry answers, or a question it cannot answer, is refused', () => {
    const { projection, command } = asked();
    const unsupported = admitCommand(command(good), projection, catalog(FULL));
    assert.equal(
      unsupported.admitted ? null : unsupported.rejection.code,
      'capability_unsupported',
    );
    const notHere = asked(false);
    const refused = admitCommand(notHere.command(good), notHere.projection, catalog(ANSWERS));
    assert.equal(refused.admitted ? null : refused.rejection.code, 'capability_unsupported');
    assert.match(refused.admitted ? '' : refused.rejection.message, /Stop the turn/);
  });

  test('a question asking for a secret is refused even if an adapter called it answerable', () => {
    const secret = asked(true, [
      prompts[0] as (typeof prompts)[0],
      { ...(prompts[1] as (typeof prompts)[0]), secret: true },
    ]);
    const admission = admitCommand(secret.command(good), secret.projection, catalog(ANSWERS));
    assert.equal(admission.admitted ? null : admission.rejection.code, 'capability_unsupported');
    assert.match(admission.admitted ? '' : admission.rejection.message, /secret/);
  });

  test('a refused answer is journaled with its keys only', () => {
    const { command } = asked();
    const typed = command([
      { key: 'q0', selected: [], text: 'hunter2' },
      { key: 'q1', selected: ['Orders'], text: null },
    ]);
    const journaled = journaledRejection(typed);
    assert.deepEqual(
      journaled.command_type === 'execution.answer_question' && journaled.payload.answers,
      [
        { key: 'q0', selected: [], text: null },
        { key: 'q1', selected: [], text: null },
      ],
    );
    assert.equal(JSON.stringify(journaled).includes('hunter2'), false);
    // Any other command is journaled whole.
    const interrupt = {
      ...typed,
      command_type: 'execution.interrupt',
      payload: { execution_id: (typed.payload as { execution_id: string }).execution_id },
    } as CommandEnvelope;
    assert.equal(journaledRejection(interrupt), interrupt);
  });

  test('a question that is not pending is not found', () => {
    const { projection, command } = asked();
    const missing = admitCommand(command(good, 'frm_other'), projection, catalog(ANSWERS));
    assert.equal(missing.admitted ? null : missing.rejection.code, 'question_not_found');
  });

  test('answers that do not fit the questions are refused, each with its reason', () => {
    const { projection, command } = asked();
    const cases: [unknown, RegExp][] = [
      [[good[0]], /Every question/],
      [[...good, { key: 'q9', selected: ['x'], text: null }], /does not ask/],
      [[good[0], good[0], good[1]], /answered twice/],
      [[{ key: 'q0', selected: ['green'], text: null }, good[1]], /does not offer/],
      [[{ key: 'q0', selected: ['red', 'blue'], text: null }, good[1]], /takes one/],
      [[good[0], { key: 'q1', selected: ['Orders'], text: 'and more' }], /only its options/],
      [[{ key: 'q0', selected: [], text: null }, good[1]], /unanswered/],
      [[{ key: 'q0', selected: ['red'], text: 'also blue' }, good[1]], /both chooses/],
      [[good[0], { key: 'q1', selected: ['Orders', 'Orders'], text: null }], /same option twice/],
    ];
    for (const [answers, reason] of cases) {
      const admission = admitCommand(command(answers), projection, catalog(ANSWERS));
      assert.equal(admission.admitted ? null : admission.rejection.code, 'invalid_answer');
      assert.match(admission.admitted ? '' : admission.rejection.message, reason);
    }
  });

  test('only a question the view shows can be answered; one behind them waits its turn', () => {
    const { b, projection, scope, command } = asked();
    for (const id of ['frm_2', 'frm_3', 'frm_4']) {
      projection.apply(
        b.runtimeEvent(scope, 'runtime.question.asked', {
          question_id: id,
          prompts,
          answerable: true,
        }),
      );
    }
    const behind = admitCommand(command(good, 'frm_4'), projection, catalog(ANSWERS));
    assert.equal(behind.admitted ? null : behind.rejection.code, 'question_not_found');
    assert.match(behind.admitted ? '' : behind.rejection.message, /waits behind/);
    projection.apply(
      b.runtimeEvent(scope, 'runtime.question.resolved', {
        question_id: 'frm_1',
        outcome: 'answered',
      }),
    );
    assert.equal(admitCommand(command(good, 'frm_4'), projection, catalog(ANSWERS)).admitted, true);
  });

  test('a question asked outside a turn can still be stopped, which withdraws it', () => {
    const { b, projection, scope, commands } = setup();
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    projection.apply(
      b.runtimeEvent(scope, 'runtime.question.asked', {
        question_id: 'req-late',
        prompts,
        answerable: false,
      }),
    );
    assert.equal(projection.executionFacts(scope.executionId)?.status, 'waiting_for_human');
    const stop = admitCommand(commands.interrupt(), projection, catalog(ANSWERS));
    assert.equal(stop.admitted, true);
    projection.apply(
      b.runtimeEvent(scope, 'runtime.question.resolved', {
        question_id: 'req-late',
        outcome: 'dismissed',
      }),
    );
    assert.equal(projection.executionFacts(scope.executionId)?.status, 'completed');
  });

  test('a question on an unobservable execution cannot be answered', () => {
    const { b, projection, scope, command } = asked();
    projection.apply(b.runtimeEvent(scope, 'runtime.connection.lost', { reason: 'Gone.' }));
    const admission = admitCommand(command(good), projection, catalog(ANSWERS));
    assert.equal(admission.admitted ? null : admission.rejection.code, 'invalid_state');
  });
});

describe('checks and previews of a project made from a starter (ADR 0037)', () => {
  const findings = (passed: boolean) => ({
    tsc: { exit: 0, seconds: 1 },
    build: { exit: 0, seconds: 10 },
    page: null,
    unknown: null,
    passed,
    reads_confined: true,
  });
  const setupStarter = (starter: boolean) => {
    const { b, projection } = setup();
    const location = { path: '/Users/someone/dev/garden', name: 'garden', created: true };
    const project = b.project('Garden', location);
    projection.apply(project.event);
    const scope = { project_id: project.projectId, workstream_id: null, execution_id: null };
    if (starter)
      projection.apply(b.controlPlane('project.starter_applied', scope, starterApplied()));
    let n = 0;
    const command = (
      type:
        | 'project.check'
        | 'project.preview.start'
        | 'project.preview.stop'
        | 'project.open_on_mac'
        | 'project.share.start'
        | 'project.share.stop',
    ): CommandEnvelope => {
      n += 1;
      return {
        schema_version: 1,
        command_id: `00000000-0000-4000-9000-${String(n).padStart(12, '0')}` as CommandId,
        issued_at: '2026-10-29T10:00:00.000Z',
        client: { name: 'test', version: null, device_label: null },
        command_type: type,
        payload: { project_id: project.projectId },
      } as CommandEnvelope;
    };
    const checkId = b.id() as never;
    return { b, projection, scope, command, checkId };
  };

  test('only a project made from a starter can be checked', () => {
    const plain = setupStarter(false);
    const refused = admitCommand(plain.command('project.check'), plain.projection, catalog());
    assert.equal(refused.admitted, false);
    assert.equal(!refused.admitted && refused.rejection.code, 'invalid_state');
    const starter = setupStarter(true);
    const admitted = admitCommand(starter.command('project.check'), starter.projection, catalog());
    assert.equal(admitted.admitted, true);
    assert.equal(admitted.admitted && admitted.policy, 'low_consequence');
  });

  test('a preview serves only a passed check, one at a time, and stops only one that runs', () => {
    const { b, projection, scope, command, checkId } = setupStarter(true);
    const admitted = (type: Parameters<typeof command>[0]) =>
      admitCommand(command(type), projection, catalog()).admitted;
    assert.equal(admitted('project.preview.start'), false, 'no check yet');
    projection.apply(
      b.controlPlane('project.check.recorded', scope, {
        check_id: checkId,
        findings: findings(false),
      }),
    );
    assert.equal(admitted('project.preview.start'), false, 'the latest check did not pass');
    assert.equal(projection.latestCheck(scope.project_id)?.findings.passed, false);
    projection.apply(
      b.controlPlane('project.check.recorded', scope, {
        check_id: checkId,
        findings: findings(true),
      }),
    );
    assert.equal(admitted('project.preview.start'), true);
    assert.equal(admitted('project.preview.stop'), false, 'nothing to stop');
    projection.apply(
      b.controlPlane('project.preview.started', scope, { check_id: checkId, port: 47930 }),
    );
    assert.deepEqual(projection.preview(scope.project_id), {
      check_id: checkId,
      port: 47930,
      files: 'current',
    });
    assert.equal(admitted('project.preview.start'), false, 'one already runs');
    assert.equal(admitted('project.preview.stop'), true);
    projection.apply(
      b.controlPlane('project.preview.stopped', scope, { check_id: checkId, reason: 'asked' }),
    );
    assert.equal(projection.preview(scope.project_id), undefined);
  });

  test('a game plays on the Mac only from a passed, laptop-ready, confined check its preview serves, once at a time (P-A)', () => {
    const { b, projection, scope, command } = setupStarter(true);
    const opens = () => {
      const result = admitCommand(command('project.open_on_mac'), projection, catalog());
      return result.admitted ? true : result.rejection.code;
    };
    const record = (checkId: string, extra: Record<string, unknown>) =>
      projection.apply(
        b.controlPlane('project.check.recorded', scope, {
          check_id: checkId as never,
          findings: { ...findings(true), ...extra } as never,
        }),
      );
    const laptopReady = {
      laptop: {
        layer: 'starter',
        targets: 1,
        mouse: 'reached',
        key: 'reached',
        screen_changed: true,
        ready: true,
      },
      ready: true,
    };
    assert.equal(opens(), 'invalid_state', 'no check');
    const first = b.id();
    record(first, { ...laptopReady, passed: false });
    assert.equal(opens(), 'invalid_state', 'not passed');
    record(first, { laptop: { ...laptopReady.laptop, ready: false } });
    assert.equal(opens(), 'invalid_state', 'not laptop ready');
    record(first, { ...laptopReady, reads_confined: false });
    assert.equal(opens(), 'invalid_state', 'not confined');
    record(first, laptopReady);
    assert.equal(opens(), 'invalid_state', 'no preview');
    projection.apply(
      b.controlPlane('project.preview.started', scope, { check_id: first as never, port: 47930 }),
    );
    assert.equal(opens(), true);
    assert.equal(
      admitCommand(command('project.open_on_mac'), projection, catalog()).admitted &&
        'low_consequence',
      'low_consequence',
    );
    // A newer check the preview does not serve yet.
    const second = b.id();
    record(second, laptopReady);
    assert.equal(opens(), 'invalid_state', 'the preview serves an older check');
    projection.apply(
      b.controlPlane('project.preview.stopped', scope, {
        check_id: first as never,
        reason: 'asked',
      }),
    );
    projection.apply(
      b.controlPlane('project.preview.started', scope, { check_id: second as never, port: 47931 }),
    );
    assert.equal(opens(), true);
    projection.apply(
      b.controlPlane('project.mac_play.started', scope, { check_id: second as never }),
    );
    assert.equal(opens(), 'invalid_state', 'one already plays');
    projection.apply(
      b.controlPlane('project.mac_play.stopped', scope, {
        check_id: second as never,
        reason: 'closed',
      }),
    );
    assert.equal(opens(), true);
  });

  test('a game is shared only from a passed, confined check its preview serves, once at a time, by the person (F1)', () => {
    const { b, projection, scope, command } = setupStarter(true);
    const result = (type: 'project.share.start' | 'project.share.stop') => {
      const admission = admitCommand(command(type), projection, catalog());
      return admission.admitted ? admission.policy : admission.rejection.message;
    };
    const record = (checkId: string, extra: Record<string, unknown>) =>
      projection.apply(
        b.controlPlane('project.check.recorded', scope, {
          check_id: checkId as never,
          findings: { ...findings(true), ...extra } as never,
        }),
      );
    assert.equal(result('project.share.stop'), 'This game is not shared.');
    assert.match(result('project.share.start'), /Only a passed check/);
    const first = b.id();
    record(first, { passed: false });
    assert.match(result('project.share.start'), /Only a passed check/);
    record(first, { reads_confined: false });
    assert.match(result('project.share.start'), /Not every agent run/);
    record(first, {});
    assert.match(result('project.share.start'), /Start the preview/);
    projection.apply(
      b.controlPlane('project.preview.started', scope, { check_id: first as never, port: 47930 }),
    );
    assert.equal(result('project.share.start'), 'review_required', 'the person reviews a share');
    const url = 'https://bright-quiet-garden.trycloudflare.com';
    projection.apply(
      b.controlPlane('project.share.started', scope, { check_id: first as never, url }),
    );
    assert.equal(projection.project(scope.project_id)?.share?.url, url);
    assert.equal(result('project.share.start'), 'This game is already shared.');
    assert.equal(result('project.share.stop'), 'low_consequence');
    projection.apply(
      b.controlPlane('project.share.stopped', scope, {
        check_id: first as never,
        reason: 'asked',
      }),
    );
    assert.equal(projection.share(scope.project_id), undefined);
    assert.equal(projection.project(scope.project_id)?.share, undefined);
  });

  test('once a turn changes files or leaves them unknown, neither the preview nor Play on the Mac is admitted until the new files are checked (T2)', () => {
    const { b, projection, scope, command, checkId } = setupStarter(true);
    const workstream = b.workstream(scope.project_id);
    const executionId = b.id() as ExecutionId;
    const execScope = {
      project_id: scope.project_id,
      workstream_id: workstream.workstreamId,
      execution_id: executionId,
    };
    const runtimeScope = {
      projectId: scope.project_id,
      workstreamId: workstream.workstreamId,
      executionId,
    };
    projection.apply(workstream.event);
    projection.apply(
      b.controlPlane('execution.created', execScope, {
        runtime: b.runtime,
        instruction: 'Build it.',
        directory: '/Users/someone/dev/garden',
      }),
    );
    projection.apply(
      b.runtimeEvent(runtimeScope, 'runtime.execution.started', {
        native_id: 's1',
        confined: true,
      }),
    );
    const laptopReady = {
      ...findings(true),
      laptop: {
        layer: 'starter',
        targets: 1,
        mouse: 'reached',
        key: 'reached',
        screen_changed: true,
        ready: true,
      },
      ready: true,
    } as never;
    projection.apply(
      b.controlPlane('project.check.recorded', scope, {
        check_id: checkId,
        findings: laptopReady,
      }),
    );
    projection.apply(
      b.controlPlane('project.preview.started', scope, { check_id: checkId, port: 47930 }),
    );
    assert.equal(
      admitCommand(command('project.open_on_mac'), projection, catalog()).admitted,
      true,
    );

    // Turn ends, before file reading arrives: files are unknown, so Play is refused.
    projection.apply(b.runtimeEvent(runtimeScope, 'runtime.turn.started', { turn_id: 't1' }));
    projection.apply(b.runtimeEvent(runtimeScope, 'runtime.turn.completed', { turn_id: 't1' }));
    const unknownOpen = admitCommand(command('project.open_on_mac'), projection, catalog());
    assert.equal(unknownOpen.admitted, false);
    assert.match(unknownOpen.admitted ? '' : unknownOpen.rejection.message, /may have changed/);

    // File reading reports a changed file: files are changed (stale), so Play and a new preview start are refused.
    const empty = { files: [], total: 0 };
    projection.apply(
      b.controlPlane('execution.files.observed', execScope, {
        turn_id: 't1',
        created: empty,
        changed: {
          files: [{ path: 'src/game.ts', lines_before: 10, lines_after: 14, bytes: 420 }],
          total: 1,
        },
        deleted: empty,
        not_read: 0,
        skipped: null,
      }),
    );
    const staleOpen = admitCommand(command('project.open_on_mac'), projection, catalog());
    assert.equal(staleOpen.admitted, false);
    assert.match(staleOpen.admitted ? '' : staleOpen.rejection.message, /files changed/);

    projection.apply(
      b.controlPlane('project.preview.stopped', scope, { check_id: checkId, reason: 'asked' }),
    );
    const stalePreview = admitCommand(command('project.preview.start'), projection, catalog());
    assert.equal(stalePreview.admitted, false);
    assert.match(stalePreview.admitted ? '' : stalePreview.rejection.message, /files changed/);

    // A fresh passed check of the changed files makes both admissible again.
    const freshId = b.id() as never;
    projection.apply(
      b.controlPlane('project.check.recorded', scope, {
        check_id: freshId,
        findings: laptopReady,
      }),
    );
    assert.equal(
      admitCommand(command('project.preview.start'), projection, catalog()).admitted,
      true,
    );
    projection.apply(
      b.controlPlane('project.preview.started', scope, { check_id: freshId, port: 47930 }),
    );
    assert.equal(
      admitCommand(command('project.open_on_mac'), projection, catalog()).admitted,
      true,
    );
  });
});

function starterApplied() {
  return {
    starter_id: 'iwsdk-vr',
    lockfile: 'a'.repeat(64),
    files: 12,
    template: 'b'.repeat(64),
  } as never;
}
