import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type {
  CommandEnvelope,
  CommandId,
  DeviceId,
  ExecutionId,
  ProjectId,
  StoredEvent,
  UsageTokens,
} from '@halcyonic/contracts';
import { fitQuestion, QUESTION_TEXT_LIMIT, questionTextLength } from '@halcyonic/contracts';
import { capStopWords, newSpendingCap } from './execution-state.ts';
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

  test("each turn's reported usage is summed, and a part any turn left unknown stays unknown", () => {
    const { b, apply, execution, scope, projection, status } = setup();
    apply(execution.event);
    const view = () => projection.execution(execution.executionId);
    apply(b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'native-1' }));
    apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    assert.equal(view()?.usage, undefined, 'no usage before a turn reports one, never zero');
    apply(
      b.runtimeEvent(scope, 'runtime.usage.reported', {
        turn_id: 't1',
        tokens: { input: 10, cache_read: 200, cache_write: 40, output: 30, reasoning: 5 },
        cost: { estimated_usd: 0.25, prices: 'list' },
        counted: 'given',
      }),
      b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }),
    );
    assert.equal(status(), 'completed', 'a usage report changes no status');
    assert.deepEqual(view()?.usage, {
      turns: 1,
      turns_ended: 1,
      tokens: { input: 10, cache_read: 200, cache_write: 40, output: 30, reasoning: 5 },
      cost: { estimated_usd: 0.25, prices: 'list' },
    });
    apply(
      b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't2' }),
      b.runtimeEvent(scope, 'runtime.usage.reported', {
        turn_id: 't2',
        tokens: { input: 7, cache_read: 300, cache_write: 0, output: 12, reasoning: null },
        cost: { estimated_usd: 0.5, prices: 'list' },
        counted: 'given',
      }),
    );
    assert.deepEqual(view()?.usage, {
      // t2 runs, yet its report is in: it counts as ended, never "2 of 1 turns".
      turns: 2,
      turns_ended: 2,
      tokens: { input: 17, cache_read: 500, cache_write: 40, output: 42, reasoning: null },
      cost: { estimated_usd: 0.75, prices: 'list' },
    });
    apply(
      b.runtimeEvent(scope, 'runtime.usage.reported', {
        turn_id: 't3',
        tokens: { input: 1, cache_read: 1, cache_write: 1, output: 1, reasoning: 1 },
        cost: { estimated_usd: 0.1, prices: 'managed' },
        counted: 'given',
      }),
    );
    assert.equal(view()?.usage?.cost, null, 'estimates by different prices are not added');
    assert.equal(view()?.usage?.tokens.reasoning, null, 'unknown once stays unknown');
    apply(
      b.runtimeEvent(scope, 'runtime.usage.reported', {
        turn_id: 't4',
        tokens: { input: 1, cache_read: 1, cache_write: 1, output: 1, reasoning: 1 },
        cost: { estimated_usd: 0.1, prices: 'managed' },
        counted: 'given',
      }),
    );
    assert.equal(view()?.usage?.cost, null, 'a sum that lost a turn is never resumed');
    assert.equal(view()?.usage?.turns, 4);
    assert.ok((view()?.usage?.turns_ended ?? 0) >= 4, 'never fewer ended than reported');
  });

  test('a turn that used nothing costs nothing, so it never voids the estimate', () => {
    const { b, apply, execution, scope, projection } = setup();
    apply(execution.event);
    const view = () => projection.execution(execution.executionId);
    const nothing = { input: 0, cache_read: 0, cache_write: 0, output: 0, reasoning: 0 };
    const some = { input: 5, cache_read: 0, cache_write: 0, output: 2, reasoning: 0 };
    const report = (turn: string, tokens: UsageTokens, cost: number | null) =>
      b.runtimeEvent(scope, 'runtime.usage.reported', {
        turn_id: turn,
        tokens,
        cost: cost === null ? null : { estimated_usd: cost, prices: 'list' },
        counted: 'given',
      });
    // A first turn stopped before any request, with no estimate of its own.
    apply(report('t1', nothing, null));
    assert.equal(view()?.usage?.cost, null);
    apply(report('t2', some, 0.25), report('t3', nothing, null), report('t4', some, 0.5));
    assert.deepEqual(view()?.usage?.cost, { estimated_usd: 0.75, prices: 'list' });
    assert.equal(view()?.usage?.turns, 4);
    apply(report('t5', { ...some, output: null }, null));
    assert.equal(view()?.usage?.cost, null, 'a turn whose use is not all known still voids it');
  });

  test("a project's usage adds up its runs' reports and counts every ended turn of them", () => {
    const { b, apply, execution, scope, projection } = setup();
    const other = b.execution(scope.projectId, scope.workstreamId);
    const otherScope = { ...scope, executionId: other.executionId };
    apply(execution.event, other.event);
    const usage = () => projection.project(scope.projectId)?.usage;
    const report = (at: typeof scope, turn: string, input: number, usd: number | null) =>
      b.runtimeEvent(at, 'runtime.usage.reported', {
        turn_id: turn,
        tokens: { input, cache_read: 0, cache_write: 0, output: 2, reasoning: null },
        cost: usd === null ? null : { estimated_usd: usd, prices: 'list' },
        counted: 'differenced',
      });
    const changed = (event: StoredEvent) =>
      projection.apply(event).changes.projects.map((view) => view.project_id);
    apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    assert.equal(usage(), undefined, 'nothing reported, so absent, never zero');
    assert.deepEqual(changed(report(scope, 't1', 10, 0.25)), [scope.projectId]);
    assert.deepEqual(changed(b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' })), [
      scope.projectId,
    ]);
    // The other run's turn ends with no report, and a third turn runs on the first.
    apply(
      b.runtimeEvent(otherScope, 'runtime.turn.started', { turn_id: 'o1' }),
      b.runtimeEvent(otherScope, 'runtime.turn.completed', { turn_id: 'o1' }),
      b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't2' }),
    );
    assert.deepEqual(usage(), {
      turns: 1,
      turns_ended: 2,
      tokens: { input: 10, cache_read: 0, cache_write: 0, output: 2, reasoning: null },
      cost: { estimated_usd: 0.25, prices: 'list' },
    });
    apply(
      report(scope, 't2', 5, 0.5),
      b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't2' }),
      b.runtimeEvent(otherScope, 'runtime.turn.started', { turn_id: 'o2' }),
      report(otherScope, 'o2', 7, null),
      b.runtimeEvent(otherScope, 'runtime.turn.completed', { turn_id: 'o2' }),
    );
    assert.deepEqual(usage(), {
      turns: 3,
      turns_ended: 4,
      tokens: { input: 22, cache_read: 0, cache_write: 0, output: 6, reasoning: null },
      cost: null,
    });
    assert.deepEqual(projection.execution(execution.executionId)?.usage?.cost, {
      estimated_usd: 0.75,
      prices: 'list',
    });
  });

  test("a task's usage is its own runs' by the project's rule, and two tasks' add up to their project's", () => {
    const { b, apply, execution, scope, projection } = setup();
    const second = b.workstream(scope.projectId);
    const run = b.execution(scope.projectId, second.workstreamId);
    const runScope = { ...scope, workstreamId: second.workstreamId, executionId: run.executionId };
    apply(execution.event, second.event, run.event);
    const report = (at: typeof scope, turn: string, input: number, usd: number) =>
      b.runtimeEvent(at, 'runtime.usage.reported', {
        turn_id: turn,
        tokens: { input, cache_read: 0, cache_write: 0, output: 2, reasoning: null },
        cost: { estimated_usd: usd, prices: 'list' },
        counted: 'given',
      });
    assert.equal(
      projection.workstreamUsage(scope.workstreamId),
      null,
      'nothing reported, never zero',
    );
    assert.equal(projection.workstreamUsage('no-such-task'), null);
    apply(
      b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }),
      report(scope, 't1', 10, 0.25),
      b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }),
      b.runtimeEvent(runScope, 'runtime.turn.started', { turn_id: 'r1' }),
      report(runScope, 'r1', 5, 0.5),
      b.runtimeEvent(runScope, 'runtime.turn.completed', { turn_id: 'r1' }),
      b.runtimeEvent(runScope, 'runtime.turn.started', { turn_id: 'r2' }),
      b.runtimeEvent(runScope, 'runtime.turn.completed', { turn_id: 'r2' }),
    );
    assert.deepEqual(projection.workstreamUsage(scope.workstreamId), {
      turns: 1,
      turns_ended: 1,
      tokens: { input: 10, cache_read: 0, cache_write: 0, output: 2, reasoning: null },
      cost: { estimated_usd: 0.25, prices: 'list' },
    });
    assert.deepEqual(projection.workstreamUsage(second.workstreamId), {
      turns: 1,
      turns_ended: 2,
      tokens: { input: 5, cache_read: 0, cache_write: 0, output: 2, reasoning: null },
      cost: { estimated_usd: 0.5, prices: 'list' },
    });
    assert.deepEqual(projection.project(scope.projectId)?.usage, {
      turns: 2,
      turns_ended: 3,
      tokens: { input: 15, cache_read: 0, cache_write: 0, output: 4, reasoning: null },
      cost: { estimated_usd: 0.75, prices: 'list' },
    });
  });

  test("a summed estimate past the contract's bound is unknown, so the view keeps to its schema", () => {
    const { b, apply, execution, scope, projection } = setup();
    apply(execution.event);
    const some = { input: 5, cache_read: 0, cache_write: 0, output: 2, reasoning: 0 };
    const report = (turn: string, usd: number) =>
      b.runtimeEvent(scope, 'runtime.usage.reported', {
        turn_id: turn,
        tokens: some,
        cost: { estimated_usd: usd, prices: 'list' },
        counted: 'given',
      });
    apply(report('t1', 600_000));
    assert.deepEqual(projection.execution(execution.executionId)?.usage?.cost, {
      estimated_usd: 600_000,
      prices: 'list',
    });
    apply(report('t2', 600_000));
    assert.equal(projection.execution(execution.executionId)?.usage?.cost, null);
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
      [ids[0], ids[1]],
      'the command that finished last is kept alongside the pending one',
    );
    assert.deepEqual(
      projection.pendingCommands().map((view) => view.command_id),
      [ids[1]],
    );
  });

  test('a snapshot keeps the commands that finished last, however long they waited, in journal order (U6)', () => {
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
    const scope = { project_id: null, workstream_id: null, execution_id: null };
    const complete = (id: string) =>
      projection.apply(
        b.controlPlane('command.completed', scope, {
          command_id: id as CommandId,
          command_type: 'project.create',
          result: null,
        }),
      );
    const [waited, early, later, pending] = [b.id(), b.id(), b.id(), b.id()];
    projection.apply(b.command(command(waited), 'accepted'));
    for (const id of [early, later]) {
      projection.apply(b.command(command(id), 'accepted'));
      complete(id);
    }
    projection.apply(b.command(command(pending), 'accepted'));
    complete(waited);

    // The one that waited finished last, so it stays and the earliest to finish goes; the list
    // keeps the order the commands were journaled in.
    assert.deepEqual(
      projection.commands(2).map((view) => view.command_id),
      [waited, later, pending],
    );
    assert.deepEqual(
      projection.commands(0).map((view) => view.command_id),
      [pending],
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

describe('a VR version and the original it was made from (M1.8c-1)', () => {
  test('the projection knows the original, keeps it out of the view, and reports one for an unknown project', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const original = b.project('Garden', { path: '/work/garden', name: 'garden', created: false });
    const vr = b.project('Garden VR', {
      path: '/work/garden-vr',
      name: 'garden-vr',
      created: true,
    });
    projection.apply(original.event);
    projection.apply(vr.event);
    const scope = { project_id: vr.projectId, workstream_id: null, execution_id: null };
    projection.apply(
      b.controlPlane('project.made_from', scope, {
        original_project_id: original.projectId,
        instruction: 'Make a VR version of the web app in /work/garden.',
      }),
    );
    assert.equal(projection.madeFrom(vr.projectId), original.projectId);
    assert.equal(projection.madeFrom(original.projectId), null);
    assert.equal(Object.hasOwn(projection.project(vr.projectId) ?? {}, 'made_from'), false);
    const unknown = { project_id: b.id() as ProjectId, workstream_id: null, execution_id: null };
    const { notes } = projection.apply(
      b.controlPlane('project.made_from', unknown, {
        original_project_id: original.projectId,
        instruction: 'Make a VR version.',
      }),
    );
    assert.deepEqual(
      notes.map((entry) => entry.code),
      ['unknown_entity'],
    );
  });
});

describe('the template a starter project began from (DRAWN)', () => {
  test('the projection knows its digest, keeps it out of the view, and has none for a project without a starter', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const game = b.project('Garden VR', {
      path: '/work/garden-vr',
      name: 'garden-vr',
      created: true,
    });
    const plain = b.project('Notes', { path: '/work/notes', name: 'notes', created: false });
    projection.apply(game.event);
    projection.apply(plain.event);
    const scope = { project_id: game.projectId, workstream_id: null, execution_id: null };
    projection.apply(
      b.controlPlane('project.starter_applied', scope, {
        starter_id: 'iwsdk-vr',
        lockfile: 'a'.repeat(64),
        files: 12,
        template: 'b'.repeat(64),
      }),
    );
    assert.equal(projection.starterTemplate(game.projectId), 'b'.repeat(64));
    assert.equal(projection.project(game.projectId)?.starter, 'iwsdk-vr');
    assert.equal(projection.starterTemplate(plain.projectId), null);
    assert.equal(projection.starterTemplate(b.id()), null);
    assert.deepEqual(
      Object.keys(projection.project(game.projectId) ?? {}).filter((key) => /template/.test(key)),
      [],
    );
  });
});

describe('a checked game playing on the Mac (P-A)', () => {
  test('a play holds from its start until the stop of that same check, and stays out of the view', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project('Garden', { path: '/work/garden', name: 'garden', created: true });
    projection.apply(project.event);
    const scope = { project_id: project.projectId, workstream_id: null, execution_id: null };
    const first = b.id() as never;
    const second = b.id() as never;
    assert.equal(projection.macPlay(project.projectId), undefined);
    projection.apply(b.controlPlane('project.mac_play.started', scope, { check_id: first }));
    assert.equal(projection.macPlay(project.projectId), first);
    // A stop of another check's play leaves this one playing.
    projection.apply(
      b.controlPlane('project.mac_play.stopped', scope, { check_id: second, reason: 'closed' }),
    );
    assert.equal(projection.macPlay(project.projectId), first);
    projection.apply(
      b.controlPlane('project.mac_play.stopped', scope, { check_id: first, reason: 'closed' }),
    );
    assert.equal(projection.macPlay(project.projectId), undefined);
  });
});

describe("a failed check's output given to an agent, once per check (FX1)", () => {
  test('the project remembers which check it was given for, and an unknown project is noted', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project('Garden', { path: '/work/garden', name: 'garden', created: true });
    projection.apply(project.event);
    const scope = {
      project_id: project.projectId,
      workstream_id: b.id() as never,
      execution_id: b.id() as never,
    };
    const first = b.id() as never;
    const second = b.id() as never;
    const given = (check_id: never, at = scope) =>
      b.controlPlane('execution.check_output_given', at, {
        check_id,
        parts: ['typecheck', 'build'],
        lines: 3,
        bytes: 180,
      });
    assert.equal(projection.checkOutputGiven(project.projectId), undefined);
    projection.apply(given(first));
    assert.equal(projection.checkOutputGiven(project.projectId), first);
    projection.apply(given(second));
    assert.equal(projection.checkOutputGiven(project.projectId), second);
    const unknown = { ...scope, project_id: b.id() as ProjectId };
    const { notes } = projection.apply(given(first, unknown as never));
    assert.deepEqual(
      notes.map((entry) => entry.code),
      ['unknown_entity'],
    );
    assert.equal(projection.checkOutputGiven(project.projectId), second);
  });
});

describe("a project's view holds its latest check, its preview and its play (M1.8e)", () => {
  test('each appears in the view and in the event changes as it is recorded, and is absent, never false, when there is none', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project('Garden', { path: '/work/garden', name: 'garden', created: true });
    projection.apply(project.event);
    const scope = { project_id: project.projectId, workstream_id: null, execution_id: null };
    const view = () => projection.project(project.projectId) ?? {};
    for (const member of ['check', 'preview', 'mac_play'])
      assert.equal(Object.hasOwn(view(), member), false, `${member} absent before anything`);
    const checkId = b.id() as never;
    const findings = {
      tsc: { exit: 0, seconds: 1 },
      build: { exit: 0, seconds: 2 },
      page: null,
      unknown: null,
      passed: false,
      reads_confined: false,
    } as never;
    const changed = (event: Parameters<typeof projection.apply>[0]) =>
      projection.apply(event).changes.projects.map((p) => p.project_id);
    const checkEvent = b.controlPlane('project.check.recorded', scope, {
      check_id: checkId,
      findings,
    });
    assert.deepEqual(changed(checkEvent), [project.projectId]);
    assert.deepEqual((view() as { check?: unknown }).check, {
      check_id: checkId,
      recorded_at: checkEvent.event.occurred_at,
      position: checkEvent.position,
      files: 'current',
      findings,
    });
    // reads_confined false stays false; a member the check did not record stays absent.
    const recorded = (view() as { check: { findings: Record<string, unknown> } }).check.findings;
    assert.equal(recorded.reads_confined, false);
    assert.equal(Object.hasOwn(recorded, 'own_permissions'), false);
    assert.deepEqual(
      changed(b.controlPlane('project.preview.started', scope, { check_id: checkId, port: 47931 })),
      [project.projectId],
    );
    assert.deepEqual((view() as { preview?: unknown }).preview, {
      check_id: checkId,
      port: 47931,
      files: 'current',
    });
    assert.deepEqual(
      changed(b.controlPlane('project.mac_play.started', scope, { check_id: checkId })),
      [project.projectId],
    );
    assert.deepEqual((view() as { mac_play?: unknown }).mac_play, {
      check_id: checkId,
      files: 'current',
    });
    assert.deepEqual(
      changed(
        b.controlPlane('project.mac_play.stopped', scope, { check_id: checkId, reason: 'closed' }),
      ),
      [project.projectId],
    );
    assert.equal(Object.hasOwn(view(), 'mac_play'), false);
    assert.deepEqual(
      changed(
        b.controlPlane('project.preview.stopped', scope, { check_id: checkId, reason: 'asked' }),
      ),
      [project.projectId],
    );
    assert.equal(Object.hasOwn(view(), 'preview'), false);
    assert.equal(Object.hasOwn(view(), 'check'), true, 'the check outlives its preview');
    // A stop of another check's preview changes nothing, so no change is sent.
    projection.apply(
      b.controlPlane('project.preview.started', scope, { check_id: checkId, port: 47931 }),
    );
    assert.deepEqual(
      changed(
        b.controlPlane('project.preview.stopped', scope, {
          check_id: b.id() as never,
          reason: 'asked',
        }),
      ),
      [],
    );
    // Nothing else is in the view: no viewer link, code or token, only what the events carried.
    assert.deepEqual(Object.keys(view()).sort(), [
      'check',
      'created_at',
      'location',
      'name',
      'preview',
      'project_id',
      'starter',
      'updated_at',
    ]);
  });

  test('a check, its preview and its play stop reading as current once a turn or restore changes files or leaves them unknown (T2)', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project('Garden', { path: '/work/garden', name: 'garden', created: true });
    const workstream = b.workstream(project.projectId);
    const executionId = b.id() as ExecutionId;
    const projectScope = { project_id: project.projectId, workstream_id: null, execution_id: null };
    const execScope = {
      project_id: project.projectId,
      workstream_id: workstream.workstreamId,
      execution_id: executionId,
    };
    const runtimeScope = {
      projectId: project.projectId,
      workstreamId: workstream.workstreamId,
      executionId,
    };
    projection.apply(project.event);
    projection.apply(workstream.event);
    projection.apply(
      b.controlPlane('execution.created', execScope, {
        runtime: b.runtime,
        instruction: 'Build it.',
        directory: '/work/garden',
      }),
    );
    projection.apply(
      b.runtimeEvent(runtimeScope, 'runtime.execution.started', {
        native_id: 's1',
        confined: true,
      }),
    );

    const first = b.id() as never;
    const findings = {
      tsc: { exit: 0, seconds: 1 },
      build: { exit: 0, seconds: 2 },
      page: null,
      unknown: null,
      passed: true,
      reads_confined: true,
    } as never;
    const checkRecorded = b.controlPlane('project.check.recorded', projectScope, {
      check_id: first,
      findings,
    });
    projection.apply(checkRecorded);
    projection.apply(
      b.controlPlane('project.preview.started', projectScope, { check_id: first, port: 47931 }),
    );
    projection.apply(b.controlPlane('project.mac_play.started', projectScope, { check_id: first }));

    const filesStates = () => {
      const current = projection.project(project.projectId);
      return {
        check: current?.check?.files,
        preview: current?.preview?.files,
        play: current?.mac_play?.files,
        latest: projection.latestCheck(project.projectId)?.files,
      };
    };
    assert.deepEqual(filesStates(), {
      check: 'current',
      preview: 'current',
      play: 'current',
      latest: 'current',
    });
    assert.equal(projection.latestCheck(project.projectId)?.position, checkRecorded.position);

    // While a turn runs in the project's folder, or before its file reading arrives, the check is unknown.
    const started = projection.apply(
      b.runtimeEvent(runtimeScope, 'runtime.turn.started', { turn_id: 't1' }),
    );
    assert.deepEqual(
      started.changes.projects.map((p) => p.project_id),
      [project.projectId],
    );
    assert.deepEqual(filesStates(), {
      check: 'unknown',
      preview: 'unknown',
      play: 'unknown',
      latest: 'unknown',
    });
    projection.apply(b.runtimeEvent(runtimeScope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.deepEqual(filesStates(), {
      check: 'unknown',
      preview: 'unknown',
      play: 'unknown',
      latest: 'unknown',
    });

    // A turn that changed no files (and read every entry) restores current.
    const empty = { files: [], total: 0 };
    const unchanged = projection.apply(
      b.controlPlane('execution.files.observed', execScope, {
        turn_id: 't1',
        created: empty,
        changed: empty,
        deleted: empty,
        not_read: 0,
        skipped: null,
      }),
    );
    assert.deepEqual(
      unchanged.changes.projects.map((p) => p.project_id),
      [project.projectId],
    );
    assert.deepEqual(filesStates(), {
      check: 'current',
      preview: 'current',
      play: 'current',
      latest: 'current',
    });

    // A turn that changes a file marks the check, preview and play as changed (stale).
    projection.apply(b.runtimeEvent(runtimeScope, 'runtime.turn.started', { turn_id: 't2' }));
    projection.apply(b.runtimeEvent(runtimeScope, 'runtime.turn.completed', { turn_id: 't2' }));
    const edited = projection.apply(
      b.controlPlane('execution.files.observed', execScope, {
        turn_id: 't2',
        created: empty,
        changed: {
          files: [{ path: 'src/game.ts', lines_before: 10, lines_after: 12, bytes: 300 }],
          total: 1,
        },
        deleted: empty,
        not_read: 0,
        skipped: null,
      }),
    );
    assert.deepEqual(
      edited.changes.projects.map((p) => p.project_id),
      [project.projectId],
    );
    assert.deepEqual(filesStates(), {
      check: 'changed',
      preview: 'changed',
      play: 'changed',
      latest: 'changed',
    });

    // A new check describes the new files (current), while a preview/play still serving the old check stays changed.
    const second = b.id() as never;
    projection.apply(
      b.controlPlane('project.check.recorded', projectScope, { check_id: second, findings }),
    );
    assert.deepEqual(filesStates(), {
      check: 'current',
      preview: 'changed',
      play: 'changed',
      latest: 'current',
    });

    // An incomplete restore that wrote no file leaves current alone; one that wrote files makes it unknown;
    // a completed restore that changed files makes it changed.
    const cp = b.id() as never;
    projection.apply(
      b.controlPlane('project.restore_incomplete', projectScope, {
        checkpoint_id: cp,
        before_restore_checkpoint_id: null,
        written: false,
        not_restored: { paths: [], total: 0 },
        failure: { code: 'before_restore_failed', message: 'Stopped before writing.' },
      }),
    );
    assert.equal(projection.latestCheck(project.projectId)?.files, 'current');
    projection.apply(
      b.controlPlane('project.restore_incomplete', projectScope, {
        checkpoint_id: cp,
        before_restore_checkpoint_id: cp,
        written: true,
        not_restored: { paths: ['src/game.ts'], total: 1 },
        failure: { code: 'restore_failed', message: 'Stopped mid-restore.' },
      }),
    );
    assert.equal(projection.latestCheck(project.projectId)?.files, 'unknown');
    projection.apply(
      b.controlPlane('project.restored', projectScope, {
        checkpoint_id: cp,
        before_restore_checkpoint_id: cp,
        tree: 'a'.repeat(64),
        files_compared: 5,
        changed_back: { paths: ['src/game.ts'], total: 1 },
        removed: { paths: [], total: 0 },
        returned: { paths: [], total: 0 },
        project_head_moved: false,
      }),
    );
    assert.equal(projection.latestCheck(project.projectId)?.files, 'changed');
  });

  test('a round visits only the checks whose files can still change, so a check after every changing round costs each round the same (#8a)', () => {
    let visits = 0;
    const b = new EventBuilder();
    const projection = new Projection({
      onCheckVisited: () => {
        visits += 1;
      },
    });
    const project = b.project('Garden', { path: '/work/garden', name: 'garden', created: true });
    const workstream = b.workstream(project.projectId);
    const executionId = b.id() as ExecutionId;
    const projectScope = { project_id: project.projectId, workstream_id: null, execution_id: null };
    const execScope = {
      project_id: project.projectId,
      workstream_id: workstream.workstreamId,
      execution_id: executionId,
    };
    const runtimeScope = {
      projectId: project.projectId,
      workstreamId: workstream.workstreamId,
      executionId,
    };
    projection.apply(project.event);
    projection.apply(workstream.event);
    projection.apply(
      b.controlPlane('execution.created', execScope, {
        runtime: b.runtime,
        instruction: 'Build it.',
        directory: '/work/garden',
      }),
    );
    projection.apply(
      b.runtimeEvent(runtimeScope, 'runtime.execution.started', {
        native_id: 's1',
        confined: true,
      }),
    );
    const findings = {
      tsc: { exit: 0, seconds: 1 },
      build: { exit: 0, seconds: 2 },
      page: null,
      unknown: null,
      passed: true,
      reads_confined: true,
    } as never;
    const empty = { files: [], total: 0 };
    const edit = {
      files: [{ path: 'src/game.ts', lines_before: 10, lines_after: 12, bytes: 300 }],
      total: 1,
    };
    const rounds = 200;
    const checks: string[] = [];
    for (let round = 1; round <= rounds; round += 1) {
      const turn = `t${round}`;
      projection.apply(b.runtimeEvent(runtimeScope, 'runtime.turn.started', { turn_id: turn }));
      projection.apply(b.runtimeEvent(runtimeScope, 'runtime.turn.completed', { turn_id: turn }));
      projection.apply(
        b.controlPlane('execution.files.observed', execScope, {
          turn_id: turn,
          created: empty,
          changed: edit,
          deleted: empty,
          not_read: 0,
          skipped: null,
        }),
      );
      // As the automatic check after a turn that changed files (A1).
      const checkId = b.id();
      projection.apply(
        b.controlPlane('project.check.recorded', projectScope, {
          check_id: checkId as never,
          findings,
        }),
      );
      checks.push(checkId);
    }
    // Each round's start, end and reading visit only the check before it, which the change settles.
    assert.equal(visits, 3 * (rounds - 1), `${rounds} rounds visited checks ${visits} times`);
    // Every check still says what became of its files, settled ones included.
    assert.equal(projection.latestCheck(project.projectId)?.files, 'current');
    projection.apply(
      b.controlPlane('project.preview.started', projectScope, {
        check_id: checks[0] as never,
        port: 47931,
      }),
    );
    assert.equal(projection.preview(project.projectId)?.files, 'changed');
  });

  test("the busy rule and the restore check read each project's own executions and pending commands (core audit 2026-11-03)", () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const garden = b.project('Garden', { path: '/work/garden', name: 'garden', created: true });
    const pond = b.project('Pond', { path: '/work/pond', name: 'pond', created: true });
    const workstream = b.workstream(pond.projectId);
    for (const event of [garden.event, pond.event, workstream.event]) projection.apply(event);
    const restoreId = b.id() as CommandId;
    const gardenScope = { project_id: garden.projectId, workstream_id: null, execution_id: null };
    projection.apply(
      b.controlPlane('command.accepted', gardenScope, {
        command: {
          schema_version: 1,
          command_id: restoreId,
          issued_at: '2026-11-10T10:00:00.000Z',
          client: { name: 'test', version: null, device_label: null },
          command_type: 'project.restore',
          payload: { project_id: garden.projectId, checkpoint_id: b.id() },
        } as never,
        policy: 'low_consequence',
        received_via: 'internal',
        principal: null,
      }),
    );
    const executionId = b.id() as ExecutionId;
    const execScope = {
      project_id: pond.projectId,
      workstream_id: workstream.workstreamId,
      execution_id: executionId,
    };
    projection.apply(
      b.controlPlane('execution.created', execScope, {
        runtime: b.runtime,
        instruction: 'Go.',
        directory: '/work/pond',
      }),
    );
    const runtimeScope = {
      projectId: pond.projectId,
      workstreamId: workstream.workstreamId,
      executionId,
    };
    projection.apply(b.runtimeEvent(runtimeScope, 'runtime.execution.started', { native_id: 'n' }));
    projection.apply(b.runtimeEvent(runtimeScope, 'runtime.turn.started', { turn_id: 't1' }));

    assert.match(
      projection.projectBusy(garden.projectId) ?? '',
      /^Putting files back in this project is still under way, so this was not done\.$/,
    );
    assert.equal(projection.restoreInProgress(garden.projectId), true);
    assert.match(projection.projectBusy(pond.projectId) ?? '', /running/);
    assert.equal(projection.restoreInProgress(pond.projectId), false);

    projection.apply(
      b.controlPlane('command.failed', gardenScope, {
        command_id: restoreId,
        command_type: 'project.restore',
        failure: { code: 'x', message: 'No.', effect: 'none' },
      }),
    );
    projection.apply(b.runtimeEvent(runtimeScope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.equal(projection.projectBusy(garden.projectId), null);
    assert.equal(projection.restoreInProgress(garden.projectId), false);
    assert.equal(projection.projectBusy(pond.projectId), null);
    assert.deepEqual(projection.pendingCommands(), []);
  });

  test('a turn a restart cut short holds no later check unknown, while one checked before it stays unknown (#13)', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project('Garden', { path: '/work/garden', name: 'garden', created: true });
    const workstream = b.workstream(project.projectId);
    projection.apply(project.event);
    projection.apply(workstream.event);
    const projectScope = { project_id: project.projectId, workstream_id: null, execution_id: null };
    const findings = {
      tsc: { exit: 0, seconds: 1 },
      build: { exit: 0, seconds: 2 },
      page: null,
      unknown: null,
      passed: true,
      reads_confined: true,
    } as never;
    const task = (name: string) => {
      const executionId = b.id() as ExecutionId;
      const execScope = {
        project_id: project.projectId,
        workstream_id: workstream.workstreamId,
        execution_id: executionId,
      };
      const runtimeScope = {
        projectId: project.projectId,
        workstreamId: workstream.workstreamId,
        executionId,
      };
      projection.apply(
        b.controlPlane('execution.created', execScope, {
          runtime: b.runtime,
          instruction: `${name}.`,
          directory: '/work/garden',
        }),
      );
      projection.apply(
        b.runtimeEvent(runtimeScope, 'runtime.execution.started', { native_id: name }),
      );
      return { executionId, execScope, runtimeScope };
    };
    const check = () => {
      const checkId = b.id() as never;
      projection.apply(
        b.controlPlane('project.check.recorded', projectScope, { check_id: checkId, findings }),
      );
      return checkId;
    };
    const files = (checkId: string) => {
      projection.apply(
        b.controlPlane('project.preview.started', projectScope, {
          check_id: checkId as never,
          port: 47931,
        }),
      );
      return projection.preview(project.projectId)?.files;
    };

    const cut = task('Cut short');
    const before = check();
    projection.apply(b.runtimeEvent(cut.runtimeScope, 'runtime.turn.started', { turn_id: 't1' }));
    // What the control plane records at its start for each task it was running (U5).
    projection.apply(
      b.controlPlane('execution.state_unknown', cut.execScope, {
        code: 'control_plane_restarted',
        message: 'The control plane restarted.',
      }),
    );
    assert.equal(projection.projectBusy(project.projectId), null);
    // The cut turn may have changed files before the restart, unread.
    assert.equal(files(before), 'unknown');
    // No runtime session survives a restart, so nothing of that turn runs after it.
    assert.equal(files(check()), 'current');

    // A later task's change is read and checked like any other.
    const next = task('Next');
    projection.apply(b.runtimeEvent(next.runtimeScope, 'runtime.turn.started', { turn_id: 'n1' }));
    projection.apply(
      b.runtimeEvent(next.runtimeScope, 'runtime.turn.completed', { turn_id: 'n1' }),
    );
    const empty = { files: [], total: 0 };
    projection.apply(
      b.controlPlane('execution.files.observed', next.execScope, {
        turn_id: 'n1',
        created: empty,
        changed: {
          files: [{ path: 'src/game.ts', lines_before: 10, lines_after: 12, bytes: 300 }],
          total: 1,
        },
        deleted: empty,
        not_read: 0,
        skipped: null,
      }),
    );
    assert.equal(files(check()), 'current');
    assert.equal(files(before), 'changed');

    // A turn whose runtime only lost touch may still run, so it still holds a check unknown.
    const lost = task('Lost');
    projection.apply(b.runtimeEvent(lost.runtimeScope, 'runtime.turn.started', { turn_id: 'l1' }));
    projection.apply(
      b.runtimeEvent(lost.runtimeScope, 'runtime.connection.lost', { reason: 'Gone.' }),
    );
    assert.equal(files(check()), 'unknown');
  });
});

describe('whether an execution ran confined, as its start recorded it (S2)', () => {
  test('confined only when the start says so; a start without the fact, or no start, reads not confined', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project('Garden VR', {
      path: '/work/garden-vr',
      name: 'garden-vr',
      created: true,
    });
    const workstream = b.workstream(project.projectId);
    projection.apply(project.event);
    projection.apply(workstream.event);
    const started = (confined: boolean | undefined) => {
      const execution = b.execution(project.projectId, workstream.workstreamId);
      projection.apply(execution.event);
      const scope = {
        projectId: project.projectId,
        workstreamId: workstream.workstreamId,
        executionId: execution.executionId,
      };
      projection.apply(
        b.runtimeEvent(
          scope,
          'runtime.execution.started',
          confined === undefined ? { native_id: 't' } : { native_id: 't', confined },
        ),
      );
      return execution.executionId;
    };
    assert.equal(projection.executionConfined(started(true)), true);
    assert.equal(projection.executionConfined(started(false)), false);
    // Recorded before the fact existed.
    assert.equal(projection.executionConfined(started(undefined)), false);
    const never = b.execution(project.projectId, workstream.workstreamId);
    projection.apply(never.event);
    assert.equal(projection.executionConfined(never.executionId), false);
    assert.equal(projection.executionConfined(b.id()), false);
  });
});

describe('catalog links (ADR 0039)', () => {
  const first = { source: 'omnorium' as const, repository_id: 7, full_name: 'acme/app' };
  const second = { source: 'omnorium' as const, repository_id: 8, full_name: 'acme/app-api' };

  test('a project holds the catalog repository the person last linked, until they unlink it', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project('App');
    projection.apply(project.event);
    const scope = { project_id: project.projectId, workstream_id: null, execution_id: null };
    assert.equal(projection.catalogLink(project.projectId), null);

    const { changes } = projection.apply(
      b.controlPlane('project.catalog_linked', scope, { catalog: first, previous: null }),
    );
    assert.deepEqual(projection.catalogLink(project.projectId), first);
    assert.deepEqual(
      changes.projects.map((view) => view.project_id),
      [project.projectId],
      'clients hear that the project changed',
    );
    projection.apply(
      b.controlPlane('project.catalog_linked', scope, { catalog: second, previous: first }),
    );
    assert.deepEqual(projection.catalogLink(project.projectId), second);
    projection.apply(b.controlPlane('project.catalog_unlinked', scope, { previous: second }));
    assert.equal(projection.catalogLink(project.projectId), null);
  });

  test("the link stays out of the project's view, so views and recordings are unchanged", () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project('App');
    projection.apply(project.event);
    const before = structuredClone(projection.project(project.projectId));
    const scope = { project_id: project.projectId, workstream_id: null, execution_id: null };
    projection.apply(
      b.controlPlane('project.catalog_linked', scope, { catalog: first, previous: null }),
    );
    const after = projection.project(project.projectId);
    assert.deepEqual(Object.keys(after ?? {}), Object.keys(before ?? {}));
    assert.deepEqual({ ...after, updated_at: null }, { ...before, updated_at: null });
  });

  test('a link for an unknown project changes nothing and is reported', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const projectId = b.id() as ProjectId;
    const scope = { project_id: projectId, workstream_id: null, execution_id: null };
    for (const event of [
      b.controlPlane('project.catalog_linked', scope, { catalog: first, previous: null }),
      b.controlPlane('project.catalog_unlinked', scope, { previous: first }),
    ]) {
      const { changes, notes } = projection.apply(event);
      assert.deepEqual(changes.projects, []);
      assert.deepEqual(
        notes.map((entry) => entry.code),
        ['unknown_entity'],
      );
    }
    assert.equal(projection.catalogLink(projectId), undefined);
  });
});

test('command recovery retains the original journaled principal for accepted and rejected records', () => {
  const b = new EventBuilder();
  const projection = new Projection();
  const scope = { project_id: null, workstream_id: null, execution_id: null };
  const deviceId = b.id() as DeviceId;
  for (const outcome of ['accepted', 'rejected'] as const) {
    const command: CommandEnvelope = {
      schema_version: 1,
      command_id: b.id() as CommandId,
      command_type: 'project.create',
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'synthetic', version: null, device_label: null },
      payload: { name: 'Synthetic', location: null },
    };
    const principal = { kind: 'device' as const, device_id: deviceId };
    const event =
      outcome === 'accepted'
        ? b.controlPlane('command.accepted', scope, {
            command,
            principal,
            policy: 'low_consequence',
            received_via: 'http',
          })
        : b.controlPlane('command.rejected', scope, {
            command,
            principal,
            rejection: { code: 'invalid_state', message: 'Synthetic rejection' },
            received_via: 'http',
          });
    projection.apply(event);
    principal.device_id = b.id() as DeviceId;
    const read = projection.commandOwner(command.command_id);
    assert.deepEqual(read, { kind: 'device', device_id: deviceId });
    if (read?.kind === 'device') read.device_id = b.id() as DeviceId;
    assert.deepEqual(projection.commandOwner(command.command_id), {
      kind: 'device',
      device_id: deviceId,
    });
    const legacy = { ...command, command_id: b.id() as CommandId };
    projection.apply(b.command(legacy, outcome));
    assert.equal(projection.commandOwner(legacy.command_id), null);
  }
  assert.equal(projection.commandOwner(b.id()), null);
});

describe("a task's spending limit counts the app's own estimates since it was set (lead's ruling, 2026-11-13)", () => {
  function capped(limit: number) {
    const b = new EventBuilder();
    const projection = new Projection();
    const apply = (...events: StoredEvent[]) => events.map((event) => projection.apply(event));
    const project = b.project();
    const workstream = b.workstream(project.projectId);
    const start: CommandEnvelope = {
      schema_version: 1,
      command_id: '00000000-0000-4000-8000-0000000000c1' as CommandId,
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: 'execution.start',
      payload: {
        workstream_id: workstream.workstreamId,
        runtime_id: 'mock' as never,
        instruction: 'Go.',
        options: {},
        model_ref: null,
        spending_cap_usd: limit,
      },
    };
    apply(project.event, workstream.event, b.command(start, 'accepted'));
    const execution = b.execution(project.projectId, workstream.workstreamId);
    apply({
      ...execution.event,
      event: { ...execution.event.event, causation_id: start.command_id },
    });
    const scope = {
      projectId: project.projectId,
      workstreamId: workstream.workstreamId,
      executionId: execution.executionId,
    };
    const cap = () => projection.execution(execution.executionId)?.spending_cap;
    const allowance = () =>
      projection.reattachBounds(execution.executionId)?.spending_allowance_usd;
    const report = (turn: string, usd: number | null, input = 5) =>
      b.runtimeEvent(scope, 'runtime.usage.reported', {
        turn_id: turn,
        tokens: { input, cache_read: 0, cache_write: 0, output: 1, reasoning: null },
        cost: usd === null ? null : { estimated_usd: usd, prices: 'list' },
        counted: 'differenced',
      });
    return { b, apply, projection, scope, cap, allowance, report, start };
  }

  test('the start sets it; each reported estimate adds; what is left is handed at a re-attach', () => {
    const { b, apply, scope, cap, allowance, report } = capped(5);
    assert.deepEqual(cap(), { limit_usd: 5, spent_usd: 0, state: 'open' });
    apply(b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'ses_1' }));
    assert.equal(allowance(), 5, 'nothing spent: the whole limit');
    apply(b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }));
    assert.equal(allowance(), null, 'a turn in flight that has not reported: not known');
    apply(report('t1', 1.5), b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }));
    assert.deepEqual(cap(), { limit_usd: 5, spent_usd: 1.5, state: 'open' });
    assert.equal(allowance(), 3.5, 'the limit less the known sum, never the whole limit again');
  });

  test('a turn that reports nothing, or no estimate, leaves the sum unknown; a restore then waits', () => {
    const { b, apply, scope, cap, allowance } = capped(5);
    apply(
      b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'ses_1' }),
      b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }),
      b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }),
    );
    assert.deepEqual(cap(), { limit_usd: 5, spent_usd: null, state: 'open' });
    assert.equal(allowance(), null);
    apply(b.runtimeEvent(scope, 'runtime.connection.restored', { reason: 'Re-attached.' }));
    assert.equal(cap()?.state, 'waiting', 'a relaunch would hand out allowance nobody can count');
    const other = capped(5);
    other.apply(
      other.b.runtimeEvent(other.scope, 'runtime.execution.started', { native_id: 'ses_2' }),
      other.b.runtimeEvent(other.scope, 'runtime.turn.started', { turn_id: 't1' }),
      other.report('t1', null),
      other.b.runtimeEvent(other.scope, 'runtime.turn.completed', { turn_id: 't1' }),
    );
    assert.equal(other.cap()?.spent_usd, null, 'a turn that used tokens and gave no estimate');
    const nothing = capped(5);
    nothing.apply(
      nothing.b.runtimeEvent(nothing.scope, 'runtime.turn.started', { turn_id: 't1' }),
      nothing.report('t1', null, 0),
      nothing.b.runtimeEvent(nothing.scope, 'runtime.turn.completed', { turn_id: 't1' }),
    );
    // input 0 but output 1: something was used, so still not known.
    assert.equal(nothing.cap()?.spent_usd, null);
  });

  test('after a raise the words carry the whole task, or say the part before is not known', () => {
    const base = newSpendingCap(5);
    base.spentUsd = 5.8;
    const raised = newSpendingCap(2, base);
    raised.spentUsd = 2.4;
    assert.equal(
      capStopWords(raised),
      "You let this task spend $2.00 more, and the app stopped it once its estimate passed that. It estimates the task spent $2.40 since then and $8.20 in all, an estimate and not your provider's bill.",
    );
    const unknown = newSpendingCap(5);
    unknown.spentUsd = null;
    const after = newSpendingCap(2, unknown);
    after.spentUsd = 2.4;
    assert.equal(
      capStopWords(after),
      "You let this task spend $2.00 more, and the app stopped it once its estimate passed that. It estimates the task spent $2.40 since then but cannot say what it spent before, an estimate and not your provider's bill.",
    );
    // A state kept before the earlier sum was: not known, never NaN.
    const older = { ...newSpendingCap(5), spentUsd: 1 } as Partial<
      ReturnType<typeof newSpendingCap>
    >;
    delete older.earlierUsd;
    const fromOlder = newSpendingCap(2, older as ReturnType<typeof newSpendingCap>);
    assert.equal(fromOlder.earlierUsd, null);
    after.spentUsd = null;
    assert.equal(
      capStopWords(after),
      'You let this task spend $2.00 more, and the app stopped it once its estimate passed that. How much it spent since then is not known, because the app did not report the cost of some of its work.',
    );
  });

  test('a restart with a turn in flight leaves the task waiting, so Carry On is its way on (AUDIT2)', () => {
    const { b, apply, scope, cap, allowance, report, projection } = capped(0.5);
    apply(
      b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'ses_1' }),
      b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }),
      report('t1', 0.2),
      b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't1' }),
      b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't2' }),
    );
    assert.equal(allowance(), null, 'the turn in flight has not reported');
    // The adapter reports the restore first, then the turn the restart cut.
    apply(
      b.runtimeEvent(scope, 'runtime.connection.restored', { reason: 'Re-attached.' }),
      b.runtimeEvent(scope, 'runtime.turn.interrupted', { turn_id: 't2' }),
    );
    assert.deepEqual(cap(), { limit_usd: 0.5, spent_usd: null, state: 'waiting' });
    assert.equal(projection.executionFacts(scope.executionId)?.spendingCap, 'waiting');
  });

  test("the stop is said in the person's own figures: the whole task, a raise, and a sum not known", () => {
    const stopAt = (limit: number, spent: number | null) => {
      const t = capped(limit);
      t.apply(
        t.b.runtimeEvent(t.scope, 'runtime.execution.started', { native_id: 'ses_1' }),
        t.b.runtimeEvent(t.scope, 'runtime.turn.started', { turn_id: 't1' }),
        ...(spent === null ? [] : [t.report('t1', spent)]),
        t.b.runtimeEvent(t.scope, 'runtime.turn.failed', {
          turn_id: 't1',
          error: {
            code: 'spending_cap_reached',
            message: 'The app stopped this task at its spending limit.',
          },
        }),
      );
      return t;
    };
    const reason = (t: ReturnType<typeof capped>) =>
      t.projection.execution(t.scope.executionId)?.status_reason;
    assert.deepEqual(reason(stopAt(5, 5.4)), {
      code: 'spending_cap_reached',
      message:
        "The app stopped this task once its estimate passed your $5.00 limit. It estimates the task spent $5.40, an estimate and not your provider's bill.",
    });
    assert.equal(
      reason(stopAt(5, null))?.message,
      'The app stopped this task once its estimate passed your $5.00 limit. How much the task spent in all is not known, because the app did not report the cost of some of its work.',
    );
    const raised = stopAt(1, 1.2);
    const raise: CommandEnvelope = {
      schema_version: 1,
      command_id: '00000000-0000-4000-8000-0000000000c3' as CommandId,
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: 'execution.send_instruction',
      payload: { execution_id: raised.scope.executionId, text: 'Carry on.', spending_cap_usd: 2 },
    };
    const where = {
      project_id: raised.scope.projectId,
      workstream_id: raised.scope.workstreamId,
      execution_id: raised.scope.executionId,
    };
    raised.apply(
      raised.b.command(raise, 'accepted'),
      raised.b.runtimeEvent(raised.scope, 'runtime.turn.started', { turn_id: 't2' }),
      raised.b.controlPlane('command.completed', where, {
        command_id: raise.command_id,
        command_type: raise.command_type,
        result: null,
      }),
      raised.report('t2', 2.4),
      raised.b.runtimeEvent(raised.scope, 'runtime.turn.failed', {
        turn_id: 't2',
        error: {
          code: 'spending_cap_reached',
          message: 'The app stopped this task at its spending limit.',
        },
      }),
    );
    assert.equal(
      reason(raised)?.message,
      "You let this task spend $2.00 more, and the app stopped it once its estimate passed that. It estimates the task spent $2.40 since then and $3.60 in all, an estimate and not your provider's bill.",
    );
  });

  test('the known sum at the limit, or the app stopping it there, reads reached; Carry On starts a new span', () => {
    const { b, apply, scope, cap, allowance, report, projection } = capped(2);
    apply(
      b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'ses_1' }),
      b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }),
      report('t1', 2.4),
      b.runtimeEvent(scope, 'runtime.turn.failed', {
        turn_id: 't1',
        error: { code: 'spending_cap_reached', message: 'Stopped at its limit.' },
      }),
    );
    assert.deepEqual(cap(), { limit_usd: 2, spent_usd: 2.4, state: 'reached' });
    assert.equal(allowance(), null, 'nothing left: no launch');
    const raise: CommandEnvelope = {
      schema_version: 1,
      command_id: '00000000-0000-4000-8000-0000000000c2' as CommandId,
      issued_at: '2026-09-26T10:00:00.000Z',
      client: { name: 'test', version: null, device_label: null },
      command_type: 'execution.send_instruction',
      payload: { execution_id: scope.executionId, text: 'Carry on.', spending_cap_usd: 5 },
    };
    apply(b.command(raise, 'accepted'));
    assert.equal(cap()?.limit_usd, 2, 'accepted is not success: the old span holds');
    apply(
      b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't2' }),
      b.controlPlane(
        'command.completed',
        {
          project_id: scope.projectId,
          workstream_id: scope.workstreamId,
          execution_id: scope.executionId,
        },
        { command_id: raise.command_id, command_type: raise.command_type, result: null },
      ),
    );
    assert.deepEqual(cap(), { limit_usd: 5, spent_usd: 0, state: 'open' }, 'more from now');
    apply(report('t2', 1), b.runtimeEvent(scope, 'runtime.turn.completed', { turn_id: 't2' }));
    assert.deepEqual(cap(), { limit_usd: 5, spent_usd: 1, state: 'open' });
    assert.equal(projection.executionFacts(scope.executionId)?.spendingCap, 'open');
  });
});

describe('an interrupted turn spent what it spent (lead, 2026-11-13, VRIFY run b)', () => {
  test('its usage counts and it counts as ended, so a screen reads 1 of 1 turns, never 1 of 0', () => {
    const b = new EventBuilder();
    const projection = new Projection();
    const project = b.project();
    const workstream = b.workstream(project.projectId);
    const execution = b.execution(project.projectId, workstream.workstreamId);
    for (const event of [project.event, workstream.event, execution.event]) projection.apply(event);
    const scope = {
      projectId: project.projectId,
      workstreamId: workstream.workstreamId,
      executionId: execution.executionId,
    };
    const usage = () => projection.execution(execution.executionId)?.usage;
    for (const event of [
      b.runtimeEvent(scope, 'runtime.execution.started', { native_id: 'ses_1' }),
      b.runtimeEvent(scope, 'runtime.turn.started', { turn_id: 't1' }),
      // As OpenCode reported the stopped turn of run (b): its usage first, then its end.
      b.runtimeEvent(scope, 'runtime.usage.reported', {
        turn_id: 't1',
        tokens: { input: 16350, cache_read: 5108, cache_write: 0, output: 380, reasoning: 105 },
        cost: null,
        counted: 'summed',
      }),
    ])
      projection.apply(event);
    assert.equal(usage()?.turns, 1);
    assert.equal(usage()?.turns_ended, 1, 'between the report and the end: already ended');
    projection.apply(b.runtimeEvent(scope, 'runtime.turn.interrupted', { turn_id: 't1' }));
    assert.deepEqual(
      [
        usage()?.turns,
        usage()?.turns_ended,
        projection.execution(execution.executionId)?.turn_count,
      ],
      [1, 1, 1],
    );
  });
});
