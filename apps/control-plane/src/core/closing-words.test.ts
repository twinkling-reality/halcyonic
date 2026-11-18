import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  CLOSING_WORDS_LIMIT,
  compileValidator,
  type ExecutionId,
  ExecutionView,
  type ProjectId,
  type RuntimeDescriptor,
  type RuntimeId,
  type WorkstreamId,
} from '@halcyonic/contracts';
import type { RuntimeAdapter, StartExecutionRequest } from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { createTestControlPlane } from '../testing/harness.ts';
import { CLOSING_REQUEST, closingPlacesOf, withClosingRequest } from './closing-words.ts';
import { controlPlaneDraft, NO_CAUSE } from './drafts.ts';

const validView = compileValidator(ExecutionView);

describe("the agent's closing words are read from its own lines, nothing made up", () => {
  test('each place from the last line that answers it, a place left out null', () => {
    const message = [
      'All done. The tests pass now.',
      '',
      'Did: I made the order list show 50 orders at a time.',
      '- **Why:** Showing every order at once was slow.',
      'How: It loads the next 50 when you reach the end.',
    ].join('\n');
    assert.deepEqual(closingPlacesOf(message), {
      did: 'I made the order list show 50 orders at a time.',
      why: 'Showing every order at once was slow.',
      how: 'It loads the next 50 when you reach the end.',
      left: null,
    });
  });

  test('a message that answers no place gives nothing, and an empty line counts as left out', () => {
    assert.equal(closingPlacesOf('I changed orders.ts and ran the tests.'), null);
    assert.equal(closingPlacesOf('Did:\nWhy:   '), null);
    assert.deepEqual(closingPlacesOf('Did: one\nLeft:'), {
      did: 'one',
      why: null,
      how: null,
      left: null,
    });
  });

  test("a line that only echoes the request's own words is no answer (AUDIT2 03:20Z)", () => {
    const echoed = [
      'Did: what you did.',
      'Why: Why you did it that way',
      'How: how it works now.',
      'Left: What is left to do.',
    ].join('\n');
    assert.equal(closingPlacesOf(echoed), null);
    assert.equal(
      closingPlacesOf(CLOSING_REQUEST),
      null,
      'the request itself, quoted back, answers nothing',
    );
    assert.deepEqual(closingPlacesOf(`${echoed}\nDid: Added paging.`), {
      did: 'Added paging.',
      why: null,
      how: null,
      left: null,
    });
  });

  test('a later line for a place replaces an earlier one, and long words are cut to the limit', () => {
    const places = closingPlacesOf(`Did: first\nDid: ${'x'.repeat(500)}`);
    assert.equal(places?.did?.length, CLOSING_WORDS_LIMIT);
  });

  test('the request asks for one plain sentence a place and says a place may be left out', () => {
    for (const label of ['Did:', 'Why:', 'How:', 'Left:'])
      assert.ok(CLOSING_REQUEST.includes(`\n${label} `));
    assert.match(CLOSING_REQUEST, /plain sentence for someone who has never written code/);
    assert.match(CLOSING_REQUEST, /end your last message with up to four lines/);
    assert.match(
      CLOSING_REQUEST,
      /Leave out any line you have nothing true to say for\. Never guess\./,
    );
    assert.equal(withClosingRequest('Fix it.'), `Fix it.\n\n${CLOSING_REQUEST}`);
  });
});

/** An agent app that records what it is sent. */
function runtime(sent: string[]): RuntimeAdapter {
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'app' as RuntimeId,
    kind: 'stub',
    display_name: 'App',
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: false,
      instruct_while_running: false,
      respond_to_approval: false,
      answer_question: false,
      interrupt: false,
    },
    model_choice: 'none',
    uses_project_location: false,
    reports_tool_activity: false,
  };
  return {
    descriptor,
    validateStartOptions: () => ({ ok: true }),
    startExecution: async (request: StartExecutionRequest) => {
      sent.push(request.instruction);
      return { native_id: 'n' };
    },
    close: async () => {},
  };
}

async function turn(events: [string, unknown][]) {
  const sent: string[] = [];
  const harness = createTestControlPlane({ adapters: () => [runtime(sent)] });
  const { controlPlane, commands, time } = harness;
  const project = controlPlane.commands.submit(commands.createProject('Garden'), 'internal');
  if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
  const now = '2026-09-26T10:00:00.000Z' as never;
  const scope = {
    project_id: project.command.result.project_id as ProjectId,
    workstream_id: '01a19000-0000-7000-8000-0000000000d1' as WorkstreamId,
    execution_id: '01a19000-0000-7000-8000-0000000000e1' as ExecutionId,
  };
  controlPlane.recorder.record(
    controlPlaneDraft(
      'workstream.created',
      { ...scope, execution_id: null },
      { title: 'Build', objective: null },
      now,
      NO_CAUSE,
    ),
  );
  controlPlane.recorder.record(
    controlPlaneDraft(
      'execution.created',
      scope,
      {
        runtime: { runtime_id: 'app', kind: 'stub', display_name: 'App', synthetic: true },
        instruction: 'Build it.',
        directory: null,
      } as never,
      now,
      NO_CAUSE,
    ),
  );
  let sequence = 0;
  for (const [type, payload] of [
    ['runtime.execution.started', { native_id: 'n', confined: false }],
    ['runtime.turn.started', { turn_id: 't1' }],
    ...events,
  ] as [string, unknown][]) {
    sequence += 1;
    controlPlane.recorder.record({
      ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
      event_type: type,
      source: { kind: 'runtime', runtime_id: 'app' },
      source_native_id: `n:${sequence}`,
      sequence,
      payload,
      ...(type === 'runtime.agent_message' && {
        provenance: { epistemic: 'reported', native_type: null },
      }),
    } as never);
  }
  await time.runUntilIdle();
  const view = controlPlane.projection.execution(scope.execution_id);
  const all = [...controlPlane.journal.readAll()].map((stored) => stored.event);
  await controlPlane.close();
  const closing = all.filter((event) => event.event_type === 'execution.closing_words');
  return { view, closing };
}

const ANSWER = 'Done.\nDid: I added paging.\nWhy: The page was slow.\nLeft: Nothing I know of.';

describe("a turn that ends with the closing lines records them as the agent's claim, reported", () => {
  test("recorded once at the turn's completion, reported, and shown on the execution as the agent's", async () => {
    const { view, closing } = await turn([
      ['runtime.agent_message', { text: 'Working on it.' }],
      ['runtime.agent_message', { text: ANSWER }],
      ['runtime.turn.completed', { turn_id: 't1' }],
    ]);
    assert.equal(closing.length, 1);
    const [event] = closing;
    assert.ok(event?.event_type === 'execution.closing_words');
    assert.deepEqual(event.provenance, { epistemic: 'reported', native_type: null });
    assert.deepEqual(event.payload, {
      turn_id: 't1',
      did: 'I added paging.',
      why: 'The page was slow.',
      how: null,
      left: 'Nothing I know of.',
    });
    assert.ok(view !== undefined);
    assert.deepEqual(view.closing_words, {
      at: event.occurred_at,
      epistemic: 'reported',
      author: 'agent',
      did: 'I added paging.',
      why: 'The page was slow.',
      how: null,
      left: 'Nothing I know of.',
    });
    assert.ok(validView(view).ok);
  });

  test("only the turn's last message counts, and a failed or interrupted turn records nothing", async () => {
    const earlier = await turn([
      ['runtime.agent_message', { text: ANSWER }],
      ['runtime.agent_message', { text: 'One more change, then done.' }],
      ['runtime.turn.completed', { turn_id: 't1' }],
    ]);
    assert.equal(earlier.closing.length, 0);
    assert.equal(earlier.view?.closing_words, undefined);
    const failed = await turn([
      ['runtime.agent_message', { text: ANSWER }],
      ['runtime.turn.failed', { turn_id: 't1', error: { code: 'x', message: 'x' } }],
    ]);
    assert.equal(failed.closing.length, 0);
  });
});

describe("the closing request is sent after the person's words", () => {
  test("a start sends the person's words, then the request", async () => {
    const sent: string[] = [];
    const harness = createTestControlPlane({
      adapters: () => [runtime(sent)],
      closingRequest: true,
    });
    const { controlPlane, commands, time } = harness;
    const project = controlPlane.commands.submit(commands.createProject('Garden'), 'internal');
    if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
    const feature = DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number];
    const workstream = controlPlane.commands.submit(
      commands.createWorkstream(project.command.result.project_id, feature),
      'internal',
    );
    if (workstream.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    const start = commands.startExecution(workstream.command.result.workstream_id, feature);
    controlPlane.commands.submit(
      { ...start, payload: { ...start.payload, runtime_id: 'app' as RuntimeId, options: {} } },
      'internal',
    );
    await time.runUntilIdle();
    await controlPlane.close();
    assert.equal(sent.length, 1);
    assert.ok(sent[0]?.startsWith(start.payload.instruction));
    assert.ok(sent[0]?.endsWith(`\n\n${CLOSING_REQUEST}`));
  });

  test('a control plane without it sends the words as they are', async () => {
    const sent: string[] = [];
    const harness = createTestControlPlane({ adapters: () => [runtime(sent)] });
    const { controlPlane, commands, time } = harness;
    const project = controlPlane.commands.submit(commands.createProject('Garden'), 'internal');
    if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
    const feature = DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number];
    const workstream = controlPlane.commands.submit(
      commands.createWorkstream(project.command.result.project_id, feature),
      'internal',
    );
    if (workstream.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    const start = commands.startExecution(workstream.command.result.workstream_id, feature);
    controlPlane.commands.submit(
      { ...start, payload: { ...start.payload, runtime_id: 'app' as RuntimeId, options: {} } },
      'internal',
    );
    await time.runUntilIdle();
    await controlPlane.close();
    assert.deepEqual(sent, [start.payload.instruction]);
  });
});
