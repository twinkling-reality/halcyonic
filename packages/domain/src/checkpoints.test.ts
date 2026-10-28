import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type {
  CheckpointId,
  CommandEnvelope,
  CommandId,
  ProjectId,
  RuntimeDescriptor,
  RuntimeId,
} from '@halcyonic/contracts';
import { admitCommand, type RuntimeCatalog } from './admission.ts';
import { COMMAND_POLICY } from './policy.ts';
import { Projection } from './projection.ts';
import { EventBuilder } from './testing/events.ts';

const TREE = 'a'.repeat(64);

const RUNTIMES: RuntimeCatalog = {
  get: (id): RuntimeDescriptor | undefined =>
    id === 'mock'
      ? {
          runtime_id: 'mock' as RuntimeId,
          kind: 'mock',
          display_name: 'Mock',
          synthetic: true,
          capabilities: {
            start_execution: true,
            instruct_at_rest: true,
            instruct_while_running: false,
            respond_to_approval: true,
            answer_question: false,
            interrupt: true,
          },
          model_choice: 'none',
          uses_project_location: false,
          reports_tool_activity: false,
        }
      : undefined,
};

let commands = 0;
function command<T extends CommandEnvelope['command_type']>(
  commandType: T,
  payload: Extract<CommandEnvelope, { command_type: T }>['payload'],
): CommandEnvelope {
  commands += 1;
  return {
    schema_version: 1,
    command_id: `00000000-0000-4000-8000-${String(commands).padStart(12, '0')}` as CommandId,
    issued_at: '2026-10-28T10:00:00.000Z',
    client: { name: 'test', version: null, device_label: null },
    command_type: commandType,
    payload,
  } as CommandEnvelope;
}

function setup() {
  const b = new EventBuilder();
  const projection = new Projection();
  const project = b.project();
  const workstream = b.workstream(project.projectId);
  const execution = b.execution(project.projectId, workstream.workstreamId);
  for (const stored of [project.event, workstream.event, execution.event]) projection.apply(stored);
  const executionScope = {
    projectId: project.projectId,
    workstreamId: workstream.workstreamId,
    executionId: execution.executionId,
  };
  const projectScope = { project_id: project.projectId, workstream_id: null, execution_id: null };
  const checkpoint = (projectId: ProjectId = project.projectId): CheckpointId => {
    const checkpointId = b.id() as CheckpointId;
    projection.apply(
      b.controlPlane(
        'project.checkpoint.recorded',
        { project_id: projectId, workstream_id: null, execution_id: null },
        {
          checkpoint_id: checkpointId,
          reason: 'before_task',
          execution_id: execution.executionId,
          tree: TREE,
          files: 3,
          project_head: null,
        },
      ),
    );
    return checkpointId;
  };
  const runtime = (type: 'runtime.execution.started' | 'runtime.turn.started') =>
    projection.apply(
      b.runtimeEvent(
        executionScope,
        type,
        type === 'runtime.execution.started' ? { native_id: 'session' } : { turn_id: 'turn-1' },
      ),
    );
  const finishTurn = () =>
    projection.apply(
      b.runtimeEvent(executionScope, 'runtime.turn.completed', { turn_id: 'turn-1' }),
    );
  const accept = (envelope: CommandEnvelope) =>
    projection.apply(
      b.controlPlane('command.accepted', projectScope, {
        command: envelope,
        policy: COMMAND_POLICY[envelope.command_type],
        received_via: 'internal',
        principal: null,
      }),
    );
  const restore = (checkpointId: string, projectId: string = project.projectId) =>
    command('project.restore', {
      project_id: projectId as ProjectId,
      checkpoint_id: checkpointId as CheckpointId,
    });
  return {
    b,
    projection,
    project,
    workstream,
    execution,
    checkpoint,
    runtime,
    finishTurn,
    accept,
    restore,
  };
}

describe('checkpoints in the projection', () => {
  test('a recorded checkpoint is the project’s, read back whole and oldest first', () => {
    const { projection, project, checkpoint } = setup();
    const first = checkpoint();
    const second = checkpoint();
    const list = projection.checkpoints(project.projectId);
    assert.deepEqual(
      list.map((view) => view.checkpoint_id),
      [first, second],
    );
    assert.equal(list[0]?.tree, TREE);
    assert.equal(list[0]?.files, 3);
    assert.equal(projection.checkpoint(project.projectId, first)?.reason, 'before_task');
  });

  test('a checkpoint of another project is not this project’s', () => {
    const { b, projection, project, checkpoint } = setup();
    const other = b.project('Other');
    projection.apply(other.event);
    const theirs = checkpoint(other.projectId);
    assert.equal(projection.checkpoint(project.projectId, theirs), undefined);
  });
});

describe('admitting a restore', () => {
  test('is review required: the person reads what would change before confirming', () => {
    assert.equal(COMMAND_POLICY['project.restore'], 'review_required');
  });

  test('a checkpoint of the project with nothing running is admitted', () => {
    const { projection, checkpoint, restore, runtime, finishTurn } = setup();
    runtime('runtime.execution.started');
    runtime('runtime.turn.started');
    finishTurn();
    const admission = admitCommand(restore(checkpoint()), projection, RUNTIMES);
    assert.equal(admission.admitted, true);
    assert.equal(admission.admitted && admission.policy, 'review_required');
  });

  test('an unknown project or another project’s checkpoint is refused', () => {
    const { b, projection, checkpoint, restore } = setup();
    const other = b.project('Other');
    projection.apply(other.event);
    const theirs = checkpoint(other.projectId);
    const missing = admitCommand(
      restore(theirs, '01920000-0000-7000-8000-0000000000ff'),
      projection,
      RUNTIMES,
    );
    assert.equal(!missing.admitted && missing.rejection.code, 'project_not_found');
    const foreign = admitCommand(restore(theirs), projection, RUNTIMES);
    assert.equal(!foreign.admitted && foreign.rejection.code, 'checkpoint_not_found');
  });

  test('is refused while a task in the project is starting or running, and admitted after', () => {
    const { projection, checkpoint, restore, runtime, finishTurn } = setup();
    const saved = checkpoint();
    const starting = admitCommand(restore(saved), projection, RUNTIMES);
    assert.equal(!starting.admitted && starting.rejection.code, 'invalid_state');
    runtime('runtime.execution.started');
    runtime('runtime.turn.started');
    const running = admitCommand(restore(saved), projection, RUNTIMES);
    assert.equal(!running.admitted && running.rejection.code, 'invalid_state');
    assert.match(!running.admitted ? running.rejection.message : '', /Stop it before/);
    finishTurn();
    assert.equal(admitCommand(restore(saved), projection, RUNTIMES).admitted, true);
  });

  test('is refused while a state is unknown, but not after a restart ended every session', () => {
    const {
      b,
      projection,
      execution,
      project,
      workstream,
      checkpoint,
      restore,
      runtime,
      finishTurn,
    } = setup();
    runtime('runtime.execution.started');
    runtime('runtime.turn.started');
    finishTurn();
    const saved = checkpoint();
    const scope = {
      project_id: project.projectId,
      workstream_id: workstream.workstreamId,
      execution_id: execution.executionId,
    };
    projection.apply(
      b.controlPlane('execution.state_unknown', scope, {
        code: 'start_outcome_unknown',
        message: 'The start timed out.',
      }),
    );
    const unknown = admitCommand(restore(saved), projection, RUNTIMES);
    assert.equal(!unknown.admitted && unknown.rejection.code, 'invalid_state');
    projection.apply(
      b.controlPlane('execution.state_unknown', scope, {
        code: 'control_plane_restarted',
        message: 'Restarted.',
      }),
    );
    assert.equal(admitCommand(restore(saved), projection, RUNTIMES).admitted, true);
  });

  test('is refused while another restore or a start in the project waits for its outcome', () => {
    const { projection, checkpoint, restore, runtime, finishTurn, accept } = setup();
    runtime('runtime.execution.started');
    runtime('runtime.turn.started');
    finishTurn();
    const saved = checkpoint();
    accept(restore(saved));
    const second = admitCommand(restore(saved), projection, RUNTIMES);
    assert.equal(!second.admitted && second.rejection.code, 'invalid_state');
  });
});

describe('work refused while files are put back', () => {
  test('a start and an instruction in the project wait for the restore', () => {
    const { projection, workstream, execution, checkpoint, restore, runtime, finishTurn, accept } =
      setup();
    runtime('runtime.execution.started');
    runtime('runtime.turn.started');
    finishTurn();
    accept(restore(checkpoint()));
    const start = admitCommand(
      command('execution.start', {
        workstream_id: workstream.workstreamId,
        runtime_id: 'mock' as RuntimeId,
        instruction: 'Go on.',
        options: {},
        model_ref: null,
      }),
      projection,
      RUNTIMES,
    );
    assert.equal(!start.admitted && start.rejection.code, 'invalid_state');
    const instruct = admitCommand(
      command('execution.send_instruction', { execution_id: execution.executionId, text: 'More.' }),
      projection,
      RUNTIMES,
    );
    assert.equal(!instruct.admitted && instruct.rejection.code, 'invalid_state');
  });
});
