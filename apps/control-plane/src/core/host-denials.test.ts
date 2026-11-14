import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type {
  ExecutionId,
  ProjectId,
  RuntimeDescriptor,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import type { ExecutionContext, RuntimeAdapter, RuntimeObservation } from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { capturingLogger, createTestControlPlane } from '../testing/harness.ts';
import { APPROVABLE_LIMIT, controlPlaneDraft, NO_CAUSE } from './drafts.ts';
import { NOT_RECORDED_WORDS, requestOutcome, tooLongWords } from './host-denials.ts';

/** An agent app that can be sent decisions, declaring whether a denial's words reach its agent. */
function runtime(delivers: boolean): RuntimeAdapter {
  const descriptor: RuntimeDescriptor = {
    runtime_id: 'app' as RuntimeId,
    kind: 'stub',
    display_name: 'App',
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: false,
      instruct_while_running: false,
      respond_to_approval: true,
      answer_question: false,
      interrupt: false,
    },
    model_choice: 'none',
    uses_project_location: false,
    reports_tool_activity: false,
    ...(delivers && { delivers_denial_message: true }),
  };
  return {
    descriptor,
    validateStartOptions: () => ({ ok: true }),
    startExecution: async () => ({ native_id: 'n' }),
    respondToApproval: async () => {},
    close: async () => {},
  };
}

async function run(delivers: boolean, confined: boolean) {
  const harness = createTestControlPlane({ adapters: () => [runtime(delivers)] });
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
  const requests = [
    { approval_id: 'short', summary: 'npx tsc --noEmit', complete: true },
    {
      approval_id: 'long',
      summary: `node -e "${'x'.repeat(5000)}"`,
      complete: false,
      length: 5011,
    },
  ];
  let sequence = 0;
  for (const [type, payload] of [
    ['runtime.execution.started', { native_id: 'n', confined }],
    ['runtime.turn.started', { turn_id: 't1' }],
    ...requests.map((each) => [
      'runtime.approval.requested',
      {
        approval_id: each.approval_id,
        subject: { kind: 'tool_use', tool_name: 'Bash', summary: each.summary },
        complete: each.complete,
        ...(each.length !== undefined && {
          too_long: { length: each.length, reported_complete: true },
        }),
      },
    ]),
  ] as [string, unknown][]) {
    sequence += 1;
    controlPlane.recorder.record({
      ...controlPlaneDraft('execution.created', scope, {} as never, now, NO_CAUSE),
      event_type: type,
      source: { kind: 'runtime', runtime_id: 'app' },
      source_native_id: `n:${sequence}`,
      sequence,
      payload,
    } as never);
  }
  await time.runUntilIdle();
  const events = [...controlPlane.journal.readAll()].map((stored) => stored.event);
  await controlPlane.close();
  const denied = events.filter((event) => event.event_type === 'execution.approval.denied_by_host');
  const sent = events.flatMap((event) => {
    if (event.event_type !== 'command.accepted') return [];
    const { command } = event.payload;
    if (command.command_type !== 'execution.respond_to_approval') return [];
    return [{ ...command.payload, at: events.indexOf(event) }];
  });
  return { events, denied, sent };
}

describe('a request too long to show a person whole is denied by the host, in every run (AUDIT3, 2026-11-13)', () => {
  for (const confined of [true, false]) {
    test(`recorded first, then the same deny a press sends, with words the agent can act on (${confined ? 'confined' : 'not confined'})`, async () => {
      const { events, denied, sent } = await run(true, confined);
      assert.equal(denied.length, 1);
      const first = denied[0];
      assert.ok(first?.event_type === 'execution.approval.denied_by_host');
      assert.deepEqual(first.payload, {
        approval_id: 'long',
        because: 'too_long_to_show',
        length: 5011,
        delivered: true,
      });
      assert.deepEqual(
        sent.map(({ approval_id, decision, message }) => ({ approval_id, decision, message })),
        [{ approval_id: 'long', decision: 'deny', message: tooLongWords(5011) }],
      );
      assert.ok(events.indexOf(first) < (sent[0]?.at ?? -1), 'recorded before the deny');
    });
  }

  test("where the agent's app carries a denial's words nowhere, the record says it was not told", async () => {
    const { denied } = await run(false, true);
    const first = denied[0];
    assert.ok(first?.event_type === 'execution.approval.denied_by_host');
    assert.equal(first.payload.delivered, false);
  });

  test('the words say why, name the bound and suggest no way round it (the lead, 19:46Z)', () => {
    const words = tooLongWords(5011);
    assert.match(words, /5011 characters/);
    assert.ok(
      words.includes(`a person can be shown at most ${APPROVABLE_LIMIT} whole, so it was not done`),
    );
    // No road round the guard while a write's approval shows only its path.
    assert.doesNotMatch(words, /file|script|several|Send it/);
    assert.doesNotMatch(words, /Halcyonic/, 'no product name in what the agent reads');
  });
});

describe('one decision for each request (AUDIT3 M1)', () => {
  const request = (tooLong: boolean) =>
    ({
      event_type: 'runtime.approval.requested',
      payload: {
        approval_id: 'a',
        subject: { kind: 'tool_use', tool_name: 'Bash', summary: 'x' },
        complete: !tooLong,
        ...(tooLong && { too_long: { length: 2001, reported_complete: true } }),
      },
    }) as never;
  test('the host answers what it answers, denies what is too long for a person, and leaves the rest to the person', () => {
    assert.equal(requestOutcome(request(true), true), 'answer');
    assert.equal(requestOutcome(request(false), true), 'answer');
    assert.equal(requestOutcome(request(true), false), 'deny');
    assert.equal(requestOutcome(request(false), false), 'person');
    assert.equal(requestOutcome({ event_type: 'runtime.turn.started' } as never, false), 'person');
  });
});

describe('a request the journal cannot record is denied by the host, never left waiting unseen (ADR 0065)', () => {
  /** Words only the agent's request holds, so a log that held the request would show them. */
  const ASKED = 'cat ~/notes-of-the-person.txt';
  type Sent = { approval_id: string; decision: string; message: string | null };

  async function run(
    requests: { approval_id: string; summary: string; tool?: string }[],
    delivers = true,
  ) {
    const { logger, entries } = capturingLogger();
    const sent: Sent[] = [];
    let at = 0;
    const observe = (type: string, payload: unknown): RuntimeObservation =>
      ({
        type,
        payload,
        native_event_id: `n:${++at}`,
        sequence: at,
        occurred_at: '2026-09-26T10:00:00.000Z',
        provenance: { epistemic: 'observed', native_type: null },
      }) as unknown as RuntimeObservation;
    const adapter: RuntimeAdapter = {
      ...runtime(delivers),
      startExecution: async (request) => {
        request.emit(observe('runtime.execution.started', { native_id: 'n', confined: true }));
        request.emit(observe('runtime.turn.started', { turn_id: 't1' }));
        for (const each of requests)
          request.emit(
            observe('runtime.approval.requested', {
              approval_id: each.approval_id,
              subject: { kind: 'tool_use', tool_name: each.tool ?? 'Bash', summary: each.summary },
              complete: true,
            }),
          );
        return { native_id: 'n' };
      },
      respondToApproval: async (request: {
        execution: ExecutionContext;
        approval_id: string;
        decision: string;
        message: string | null;
      }) => {
        sent.push({
          approval_id: request.approval_id,
          decision: request.decision,
          message: request.message,
        });
      },
    };
    const harness = createTestControlPlane({ logger, adapters: () => [adapter] });
    const { controlPlane, commands, time } = harness;
    const project = controlPlane.commands.submit(commands.createProject('Garden'), 'internal');
    if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
    const feature = DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number];
    const created = controlPlane.commands.submit(
      commands.createWorkstream(project.command.result.project_id, feature),
      'internal',
    );
    if (created.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    const workstreamId = created.command.result.workstream_id;
    controlPlane.commands.submit(
      {
        ...commands.startExecution(workstreamId, feature),
        payload: {
          workstream_id: workstreamId,
          runtime_id: 'app' as RuntimeId,
          instruction: 'Build it.',
          options: {},
          model_ref: null,
        },
      },
      'internal',
    );
    await time.runUntilIdle();
    const events = [...controlPlane.journal.readAll()].map((stored) => stored.event);
    const execution = events.find(
      (event) => event.event_type === 'execution.created',
    )?.execution_id;
    const view = execution == null ? undefined : controlPlane.projection.execution(execution);
    await controlPlane.close();
    const denied = events.filter(
      (event) => event.event_type === 'execution.approval.denied_by_host',
    );
    return { events, denied, sent, view, entries };
  }

  test('an approval request the contract refuses is recorded as denied, then denied through the agent app', async () => {
    // An empty summary, as an agent app's malformed request the journal refuses.
    const { denied, sent, view, entries, events } = await run([
      { approval_id: 'bad', summary: '' },
    ]);
    assert.ok(
      !events.some((event) => event.event_type === 'runtime.approval.requested'),
      'the request itself was never recorded',
    );
    assert.equal(denied.length, 1);
    const first = denied[0];
    assert.ok(first?.event_type === 'execution.approval.denied_by_host');
    assert.deepEqual(first.payload, {
      approval_id: 'bad',
      because: 'not_recorded',
      delivered: true,
    });
    assert.deepEqual(sent, [{ approval_id: 'bad', decision: 'deny', message: NOT_RECORDED_WORDS }]);
    assert.deepEqual(view?.pending_approvals, []);
    assert.ok(entries.some((entry) => entry.message === 'denied a request that was not recorded'));
    assert.ok(
      !events.some(
        (event) =>
          event.event_type === 'command.accepted' &&
          event.payload.command.command_type === 'execution.respond_to_approval',
      ),
      'not through the command a press sends',
    );
  });

  test("where the agent's app carries a denial's words nowhere, the record says it was not told", async () => {
    const { denied, sent } = await run([{ approval_id: 'bad', summary: '' }], false);
    const first = denied[0];
    assert.ok(first?.event_type === 'execution.approval.denied_by_host');
    assert.equal(first.payload.delivered, false);
    assert.equal(sent.length, 1);
  });

  test('a request that is recorded is never denied by the guard: it waits for the person', async () => {
    const { denied, sent, view } = await run([
      { approval_id: 'good', summary: ASKED },
      { approval_id: 'bad', summary: '' },
    ]);
    assert.deepEqual(
      denied.map((event) => (event.payload as { approval_id: string }).approval_id),
      ['bad'],
    );
    assert.deepEqual(
      sent.map((each) => each.approval_id),
      ['bad'],
    );
    assert.deepEqual(
      view?.pending_approvals.map((each) => each.approval_id),
      ['good'],
    );
  });

  test("no log line holds the request's text", async () => {
    // Refused for its empty tool name, with the request's words beside it.
    const { entries, sent } = await run([{ approval_id: 'bad', summary: ASKED, tool: '' }]);
    assert.equal(sent.length, 1, 'refused, so denied');
    const logged = JSON.stringify(entries);
    assert.ok(!logged.includes('notes-of-the-person'), logged);
  });

  test('the words say it was not shown and not done, with no advice and no product name', () => {
    assert.equal(
      NOT_RECORDED_WORDS,
      'This request could not be shown to a person, so it was not done.',
    );
    assert.doesNotMatch(NOT_RECORDED_WORDS, /Halcyonic|shorter|simpler|again/);
  });
});

describe('a question the journal cannot record ends its turn, asked once (ADR 0065, AUDIT3 F1)', () => {
  /** An id only the agent chose, so a log that held it would show it. */
  const SECRET_ID = 'q-words-of-the-agent';
  const PROMPT = {
    key: 'k',
    header: null,
    text: 'Which one?',
    options: [],
    multiple: false,
    free_text: true,
    secret: false,
  };
  /** A question the contract refuses: it carries a field the contract does not have. */
  const refused = (
    question_id: unknown,
    extra: Record<string, unknown> = { asked_by: 'the agent' },
  ) => ({
    type: 'runtime.question.asked',
    payload: { question_id, prompts: [PROMPT], answerable: true, ...extra },
  });
  const recordedQuestion = (question_id: string) => ({
    type: 'runtime.question.asked',
    payload: { question_id, prompts: [PROMPT], answerable: true },
  });
  const turn = (turn_id: string) => ({ type: 'runtime.turn.started', payload: { turn_id } });
  type Step = { type: string; payload: unknown };

  async function run(steps: Step[], interrupt: 'none' | 'resolves' | 'rejects' = 'resolves') {
    const { logger, entries } = capturingLogger();
    const interrupts: string[] = [];
    const answered: string[] = [];
    let at = 0;
    const base = runtime(true);
    const adapter: RuntimeAdapter = {
      ...base,
      descriptor: {
        ...base.descriptor,
        capabilities: {
          ...base.descriptor.capabilities,
          answer_question: true,
          interrupt: interrupt !== 'none',
        },
      },
      startExecution: async (request) => {
        for (const step of [
          { type: 'runtime.execution.started', payload: { native_id: 'n', confined: true } },
          ...steps,
        ])
          request.emit({
            ...step,
            native_event_id: `n:${++at}`,
            sequence: at,
            occurred_at: '2026-09-26T10:00:00.000Z',
            provenance: { epistemic: 'observed', native_type: null },
          } as unknown as RuntimeObservation);
        return { native_id: 'n' };
      },
      answerQuestion: async (request) => {
        answered.push(request.question_id);
      },
      ...(interrupt !== 'none' && {
        interrupt: async (request: { execution: ExecutionContext }) => {
          interrupts.push(request.execution.execution_id);
          if (interrupt === 'rejects') throw new Error(`refused ${SECRET_ID}`);
        },
      }),
    };
    const { controlPlane, commands, time } = createTestControlPlane({
      logger,
      adapters: () => [adapter],
    });
    const project = controlPlane.commands.submit(commands.createProject('Garden'), 'internal');
    if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
    const feature = DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number];
    const created = controlPlane.commands.submit(
      commands.createWorkstream(project.command.result.project_id, feature),
      'internal',
    );
    if (created.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
    const workstreamId = created.command.result.workstream_id;
    controlPlane.commands.submit(
      {
        ...commands.startExecution(workstreamId, feature),
        payload: {
          workstream_id: workstreamId,
          runtime_id: 'app' as RuntimeId,
          instruction: 'Build it.',
          options: {},
          model_ref: null,
        },
      },
      'internal',
    );
    await time.runUntilIdle();
    const events = [...controlPlane.journal.readAll()].map((stored) => stored.event);
    const execution = events.find((event) => event.event_type === 'execution.created')
      ?.execution_id as string;
    const view = controlPlane.projection.execution(execution as ExecutionId);
    await controlPlane.close();
    const ended = events.flatMap((event) =>
      event.event_type === 'execution.question.ended_by_host'
        ? [{ execution_id: event.execution_id, ...event.payload }]
        : [],
    );
    const logs = entries.filter((entry) => entry.message === 'a question that was not recorded');
    return { events, ended, interrupts, answered, view, execution, entries, logs };
  }

  test('the turn interrupted is the one the host routed the question to, never one its payload names (F1.1)', async () => {
    const { ended, interrupts, execution } = await run([
      turn('t1'),
      refused('q1', { execution_id: '01a19000-0000-7000-8000-0000000000e9' }),
    ]);
    assert.deepEqual(interrupts, [execution]);
    assert.deepEqual(ended, [
      {
        execution_id: execution,
        question_id: 'q1',
        because: 'not_recorded',
        interrupt_requested: true,
      },
    ]);
  });

  test("a question id the contract's own check refuses is kept nowhere and asks nothing (F1.2)", async () => {
    const { ended, interrupts, logs, events } = await run([
      turn('t1'),
      refused(''),
      refused('x'.repeat(513)),
      refused(42),
      refused(undefined),
    ]);
    assert.deepEqual(ended, []);
    assert.deepEqual(interrupts, []);
    assert.deepEqual(
      logs.map((entry) => (entry.context as { outcome: string }).outcome),
      ['no_valid_id', 'no_valid_id', 'no_valid_id', 'no_valid_id'],
    );
    assert.ok(!events.some((event) => event.event_type === 'runtime.question.asked'));
  });

  test('the host asks at most once in a turn, and again only in a later turn (F1.3)', async () => {
    const { ended, interrupts } = await run([
      turn('t1'),
      refused('q1'),
      refused('q2'),
      turn('t2'),
      refused('q3'),
    ]);
    assert.equal(interrupts.length, 2, 'once in t1, once in t2');
    const byId = Object.fromEntries(ended.map((each) => [each.question_id, each]));
    assert.equal(ended.length, 3);
    assert.equal(byId.q1?.interrupt_requested, true);
    assert.equal(byId.q2?.interrupt_requested, false, 'recorded, never asked a second time');
    assert.equal(byId.q3?.interrupt_requested, true);
  });

  test('an ask the agent app refuses is recorded as not requested and never asked again in the turn (F1.3, F1.4)', async () => {
    const { ended, interrupts, logs } = await run(
      [turn('t1'), refused('q1'), refused('q2')],
      'rejects',
    );
    assert.equal(interrupts.length, 1);
    assert.deepEqual(ended.map((each) => [each.question_id, each.interrupt_requested]).sort(), [
      ['q1', false],
      ['q2', false],
    ]);
    assert.deepEqual(logs.map((entry) => (entry.context as { outcome: string }).outcome).sort(), [
      'interrupt_already_asked',
      'interrupt_refused',
    ]);
  });

  test("an ask taken never claims the turn ended: only the runtime's own interrupted does (F1.4)", async () => {
    const { ended, events, view } = await run([turn('t1'), refused('q1')]);
    assert.equal(ended[0]?.interrupt_requested, true);
    assert.ok(!events.some((event) => event.event_type === 'runtime.turn.interrupted'));
    assert.notEqual(view?.status, 'interrupted');
    const positions = events.map((event) => event.event_type);
    assert.ok(
      positions.indexOf('execution.question.ended_by_host') >
        positions.indexOf('runtime.turn.started'),
    );
  });

  test('an agent app that declares no interrupt is recorded as not asked, with nothing invented', async () => {
    const { ended, interrupts, logs } = await run([turn('t1'), refused('q1')], 'none');
    assert.deepEqual(interrupts, []);
    assert.deepEqual(
      ended.map((each) => each.interrupt_requested),
      [false],
    );
    assert.equal(
      (logs[0]?.context as { outcome?: string } | undefined)?.outcome,
      'no_interrupt_declared',
    );
  });

  test("logs carry the execution's and runtime's ids and the outcome, never the question's id or words (F1.5)", async () => {
    const { logs, entries, execution } = await run([turn('t1'), refused(SECRET_ID)], 'rejects');
    assert.ok(logs.length > 0);
    for (const entry of logs)
      assert.deepEqual(Object.keys(entry.context).sort(), [
        'execution_id',
        'outcome',
        'runtime_id',
      ]);
    assert.equal(
      (logs[0]?.context as { execution_id?: string } | undefined)?.execution_id,
      execution,
    );
    const logged = JSON.stringify(entries);
    assert.ok(!logged.includes(SECRET_ID), logged);
    assert.ok(!logged.includes('Which one?'), logged);
  });

  test('no question is answered, resolved or dismissed, and an open one with the same id is left to the person (F1.6)', async () => {
    const { ended, interrupts, answered, events, view } = await run([
      turn('t1'),
      recordedQuestion('open'),
      refused('open'),
      refused('q1'),
    ]);
    assert.deepEqual(
      ended.map((each) => each.question_id),
      ['q1'],
      'the open one was recorded after all',
    );
    assert.equal(interrupts.length, 1);
    assert.deepEqual(answered, []);
    assert.ok(!events.some((event) => event.event_type === 'runtime.question.resolved'));
    assert.deepEqual(
      view?.pending_questions.map((each) => each.question_id),
      ['open'],
    );
  });
});
