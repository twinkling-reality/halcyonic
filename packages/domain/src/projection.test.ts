import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type {
  CommandEnvelope,
  CommandId,
  ExecutionId,
  ProjectId,
  StoredEvent,
} from '@halcyonic/contracts';
import { fitQuestion, QUESTION_TEXT_LIMIT, questionTextLength } from '@halcyonic/contracts';
import { Projection } from './projection.ts';
import { SHOWN_QUESTIONS } from './questions.ts';
import { EventBuilder } from './testing/events.ts';

function setup(reportsToolActivity = false) {
  const b = new EventBuilder();
  const projection = new Projection({ reportsToolActivity: () => reportsToolActivity });
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

describe('whether a tool call runs is said only from the facts', () => {
  test('running while a call is open, none between calls only where every call is reported, else unknown', () => {
    for (const reports of [true, false]) {
      const { b, apply, execution, projection, scope } = setup(reports);
      const activity = () => projection.execution(execution.executionId)?.tool_activity;
      apply(execution.event);
      apply(b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'native-1' }));
      assert.equal(activity(), 'none', 'no turn, nothing runs');
      apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
      assert.equal(activity(), reports ? 'none' : 'unknown', 'a turn with no call open');
      apply(
        b.runtimeEvent(scope, 'runtime.tool.started', {
          tool_call_id: 'c1',
          tool_name: 'shell',
          title: null,
        }),
      );
      assert.equal(activity(), 'running');
      // A lost connection makes the open call stale.
      apply(b.runtimeEvent(scope, 'runtime.connection.lost', { reason: 'Gone.' }));
      assert.equal(activity(), 'unknown');
      apply(b.runtimeEvent(scope, 'runtime.connection.restored', { reason: 'Back.' }));
      assert.equal(activity(), 'running');
      apply(
        b.runtimeEvent(scope, 'runtime.tool.completed', {
          tool_call_id: 'c1',
          outcome: 'succeeded',
        }),
      );
      assert.equal(activity(), reports ? 'none' : 'unknown');
      apply(
        b.runtimeEvent(scope, 'runtime.tool.started', {
          tool_call_id: 'c2',
          tool_name: 'shell',
          title: null,
        }),
      );
      // A turn that ends closes every call it left open.
      apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
      assert.equal(activity(), 'none');
    }
  });

  test("a test run is work under way, and a call opened between turns is not the next turn's", () => {
    const { b, apply, execution, projection, scope } = setup(true);
    const activity = () => projection.execution(execution.executionId)?.tool_activity;
    apply(execution.event);
    apply(b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'native-1' }));
    apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    apply(b.runtimeEvent(scope, 'runtime.test_run.started', { test_run_id: 'r1', label: null }));
    assert.equal(activity(), 'running', 'a test run with no tool call open');
    apply(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    // Reported after its turn ended, as a background call can be: its end may never come.
    apply(
      b.runtimeEvent(scope, 'runtime.tool.started', {
        tool_call_id: 'late',
        tool_name: 'shell',
        title: null,
      }),
    );
    assert.equal(activity(), 'none', 'no turn');
    apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't2' }));
    assert.equal(activity(), 'none', 'the next turn starts with nothing open');
  });
});

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
    const { b, apply, execution, projection, scope, status, workstreamView } = setup();
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
        complete: true,
      }),
    );
    assert.equal(status(), 'waiting_for_human');
    // A complete request can be approved; the view says so.
    assert.deepEqual(
      projection.execution(execution.executionId)?.pending_approvals.map((each) => each.approvable),
      [true],
    );
    apply(
      b.runtimeEvent(scope, 'runtime.approval.requested', {
        approval_id: 'a2',
        subject: { kind: 'tool_use', tool_name: 'shell', summary: 'echo one' },
        complete: false,
      }),
    );
    assert.deepEqual(
      projection
        .execution(execution.executionId)
        ?.pending_approvals.map((each) => [each.approval_id, each.approvable]),
      [
        ['a1', true],
        ['a2', false],
      ],
    );
    apply(
      b.runtimeEvent(scope, 'runtime.approval.resolved', { approval_id: 'a2', decision: 'denied' }),
    );
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
        complete: true,
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

  test('a restored connection ends unknown, and the facts that follow it set the status', () => {
    const { b, apply, execution, scope, status, workstreamView } = setup();
    apply(execution.event, b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    apply(b.runtimeEvent(scope, 'runtime.connection.lost', { reason: 'The read timed out.' }));
    assert.equal(status(), 'unknown');
    apply(
      b.runtimeEvent(scope, 'runtime.connection.restored', { reason: 'Read again after waking.' }),
    );
    assert.equal(status(), 'running');
    assert.deepEqual(workstreamView()?.attention.reasons, []);
    apply(b.runtimeEvent(scope, 'runtime.turn.interrupted', { turn_id: 't1' }));
    assert.equal(status(), 'interrupted');
  });

  test('a question the agent asks waits for the person until answered or its turn ends', () => {
    const { b, apply, execution, scope, status, workstreamView, projection } = setup();
    const prompts = [
      {
        key: 'q0',
        header: 'Colour',
        text: 'Which colour?',
        options: [
          { label: 'red', description: null },
          { label: 'blue', description: 'The calm one' },
        ],
        multiple: false,
        free_text: true,
        secret: false,
      },
    ];
    apply(execution.event, b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    apply(
      b.runtimeEvent(scope, 'runtime.question.asked', {
        question_id: 'frm_1',
        prompts,
        answerable: true,
      }),
    );
    assert.equal(status(), 'waiting_for_human');
    const view = () => projection.execution(execution.executionId);
    assert.deepEqual(
      view()?.pending_questions.map(({ question_id, answerable }) => ({ question_id, answerable })),
      [{ question_id: 'frm_1', answerable: true }],
    );
    assert.deepEqual(workstreamView()?.attention, {
      level: 'action_required',
      reasons: [
        { kind: 'question_pending', execution_id: execution.executionId, question_id: 'frm_1' },
      ],
    });
    apply(
      b.runtimeEvent(scope, 'runtime.question.resolved', {
        question_id: 'frm_1',
        outcome: 'answered',
      }),
    );
    assert.equal(status(), 'running');
    assert.deepEqual(view()?.pending_questions, []);
    assert.equal(workstreamView()?.attention.level, 'none');

    // A question nobody answers is cleared by the end of its turn, as an approval is.
    apply(
      b.runtimeEvent(scope, 'runtime.question.asked', {
        question_id: 'frm_2',
        prompts,
        answerable: false,
      }),
    );
    assert.equal(status(), 'waiting_for_human', 'even one that cannot be answered here');
    apply(b.runtimeEvent(scope, 'runtime.turn.interrupted', { turn_id: 't1' }));
    assert.equal(status(), 'interrupted');
    assert.deepEqual(view()?.pending_questions, []);
  });

  test('a question too long for clients is shortened, each cut marked, and cannot be answered', () => {
    const { b, apply, execution, scope, projection } = setup();
    const prompt = {
      key: 'q0',
      header: 'h'.repeat(200),
      text: 't'.repeat(4000),
      options: Array.from({ length: 20 }, (_, index) => ({
        label: `${index}${'l'.repeat(190)}`,
        description: 'd'.repeat(1000),
      })),
      multiple: false,
      free_text: true,
      secret: false,
    };
    const prompts = Array.from({ length: 10 }, (_, index) => ({ ...prompt, key: `q${index}` }));
    assert.ok(questionTextLength(prompts) > QUESTION_TEXT_LIMIT);
    apply(execution.event, b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    apply(
      b.runtimeEvent(scope, 'runtime.question.asked', {
        question_id: 'frm_1',
        prompts,
        answerable: true,
      }),
    );
    const shown = projection.execution(execution.executionId)?.pending_questions[0];
    assert.equal(shown?.answerable, false);
    assert.ok(questionTextLength(shown?.prompts ?? []) <= QUESTION_TEXT_LIMIT);
    assert.equal(shown?.prompts.length, 4);
    assert.ok(shown?.prompts.every((each) => each.text.endsWith(' [truncated]')));
    assert.ok(shown?.prompts[0]?.options[0]?.description?.endsWith(' [truncated]'));
    // One within the limit is kept whole, with its own answerability.
    const within = { ...prompt, options: prompt.options.slice(0, 2) };
    const small = fitQuestion([within], true);
    assert.equal(small.answerable, true);
    assert.equal(small.prompts[0], within);
  });

  test('a view shows the oldest pending questions, answerable ones first; the others wait their turn', () => {
    const { b, apply, execution, scope, status, workstreamView, projection } = setup();
    const prompts = [
      {
        key: 'q0',
        header: null,
        text: 'Which?',
        options: [],
        multiple: false,
        free_text: true,
        secret: false,
      },
    ];
    apply(execution.event, b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    for (let index = 1; index <= 5; index += 1) {
      apply(
        b.runtimeEvent(scope, 'runtime.question.asked', {
          question_id: `frm_${index}`,
          prompts,
          answerable: true,
        }),
      );
    }
    const shown = () =>
      projection
        .execution(execution.executionId)
        ?.pending_questions.map((question) => question.question_id);
    assert.deepEqual(shown(), ['frm_1', 'frm_2', 'frm_3']);
    assert.equal(SHOWN_QUESTIONS, 3);
    // The reasons name only the questions shown, so they stay as few as the view's.
    assert.deepEqual(
      workstreamView()?.attention.reasons.map((reason) =>
        reason.kind === 'question_pending' ? reason.question_id : null,
      ),
      ['frm_1', 'frm_2', 'frm_3'],
    );
    apply(
      b.runtimeEvent(scope, 'runtime.question.resolved', {
        question_id: 'frm_1',
        outcome: 'answered',
      }),
    );
    assert.deepEqual(shown(), ['frm_2', 'frm_3', 'frm_4']);
    for (const id of ['frm_2', 'frm_3', 'frm_4']) {
      apply(
        b.runtimeEvent(scope, 'runtime.question.resolved', {
          question_id: id,
          outcome: 'answered',
        }),
      );
    }
    assert.deepEqual(shown(), ['frm_5']);
    assert.equal(status(), 'waiting_for_human');
  });

  test('questions that cannot be answered never crowd out one that can', () => {
    const { b, apply, execution, scope, projection } = setup();
    const prompts = [
      {
        key: 'q0',
        header: null,
        text: 'Which?',
        options: [],
        multiple: false,
        free_text: true,
        secret: false,
      },
    ];
    apply(execution.event, b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    for (const [id, answerable] of [
      ['frm_1', false],
      ['frm_2', false],
      ['frm_3', false],
      ['frm_4', true],
    ] as const) {
      apply(
        b.runtimeEvent(scope, 'runtime.question.asked', { question_id: id, prompts, answerable }),
      );
    }
    assert.deepEqual(
      projection
        .execution(execution.executionId)
        ?.pending_questions.map((question) => question.question_id),
      ['frm_4', 'frm_1', 'frm_2'],
    );
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
      payload: { name: 'P', location: null },
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

describe('project locations', () => {
  test('a project keeps the folder it was bound to until it is bound to another', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const first = { path: '/work/app', name: 'app', created: true };
    const project = b.project('App', first);
    projection.apply(project.event);
    assert.deepEqual(projection.project(project.projectId)?.location, first);

    const moved = { path: '/work/app-renamed', name: 'app-renamed', created: false };
    const scope = { project_id: project.projectId, workstream_id: null, execution_id: null };
    const { changes } = projection.apply(
      b.controlPlane('project.location_set', scope, { location: moved }),
    );
    assert.deepEqual(projection.project(project.projectId)?.location, moved);
    assert.deepEqual(
      changes.projects.map((view) => view.location),
      [moved],
      'clients receive the project with its new folder',
    );
  });

  test('a location for an unknown project changes nothing and is reported', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const scope = { project_id: b.id() as ProjectId, workstream_id: null, execution_id: null };
    const { changes, notes } = projection.apply(
      b.controlPlane('project.location_set', scope, {
        location: { path: '/work/app', name: 'app', created: false },
      }),
    );
    assert.deepEqual(changes.projects, []);
    assert.deepEqual(
      notes.map((entry) => entry.code),
      ['unknown_entity'],
    );
  });

  test('an execution shows the folder it was given, and keeps it when the project moves', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project('App', { path: '/work/app', name: 'app', created: false });
    const workstream = b.workstream(project.projectId);
    const executionId = b.id() as ExecutionId;
    const projectScope = { project_id: project.projectId, workstream_id: null, execution_id: null };
    projection.apply(project.event);
    projection.apply(workstream.event);
    projection.apply(
      b.controlPlane(
        'execution.created',
        {
          project_id: project.projectId,
          workstream_id: workstream.workstreamId,
          execution_id: executionId,
        },
        { runtime: b.runtime, instruction: 'Do the work.', directory: '/work/app' },
      ),
    );
    projection.apply(
      b.controlPlane('project.location_set', projectScope, {
        location: { path: '/work/other', name: 'other', created: false },
      }),
    );
    assert.equal(projection.execution(executionId)?.directory, '/work/app');
    assert.equal(projection.project(project.projectId)?.location?.path, '/work/other');
  });
});
