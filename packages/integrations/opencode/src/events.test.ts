/**
 * Event mapping from OpenCode 2.0.18 events captured from the real binary. The fixtures are the
 * raw `/api/event` bytes of the Halcyonic smoke test of 2026-09-26 (simple turn, approvals,
 * interrupts, a provider retry, a second turn, a reconnect mid-turn, a server killed mid-turn)
 * and of a capture of two failed turns made for this adapter, with host paths replaced.
 */
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import type { RuntimeObservation } from '@halcyonic/runtime-core';
import {
  approvalSubject,
  clip,
  createSessionState,
  decodeEvent,
  formAnswer,
  formQuestion,
  observeEvent,
  replyOutcome,
  type SessionState,
} from './events.ts';
import { SseParser } from './sse.ts';
import { assertValidObservations } from './testing/observations.ts';

const NOW = new Date('2026-09-26T12:00:00.000Z');
/** The session of simple-turn.sse, prompted again in second-turn.sse. */
const SIMPLE = 'ses_f203a445fffeuXBorlhS7f2ATC';

interface Replayed {
  readonly state: SessionState;
  readonly observations: RuntimeObservation[];
}

/**
 * Feeds fixtures through the parser, decoder and mapper as the adapter does. Each session gets a
 * state as it would after its prompt was accepted, unless one is supplied.
 */
function replay(
  fixtures: readonly string[],
  sessions = new Map<string, Replayed>(),
): Map<string, Replayed> {
  for (const name of fixtures) {
    const parser = new SseParser();
    const bytes = readFileSync(new URL(`../fixtures/${name}`, import.meta.url));
    for (const message of [...parser.push(bytes), ...parser.end()]) {
      if (message.kind !== 'data') continue;
      const event = decodeEvent(message.data);
      assert.ok(event !== null, `undecodable event in ${name}: ${message.data.slice(0, 80)}`);
      if (event.sessionId === null) continue;
      let session = sessions.get(event.sessionId);
      if (session === undefined) {
        const state = createSessionState();
        state.awaitingStart = true;
        session = { state, observations: [] };
        sessions.set(event.sessionId, session);
      }
      session.observations.push(...observeEvent(session.state, event, NOW));
    }
  }
  for (const session of sessions.values()) assertValidObservations(session.observations);
  return sessions;
}

function only(sessions: Map<string, Replayed>): Replayed {
  assert.equal(sessions.size, 1);
  return [...sessions.values()][0] as Replayed;
}

function summary(observations: readonly RuntimeObservation[]) {
  return observations.map((item) => ({ type: item.type, payload: item.payload }));
}

describe('OpenCode 2.0.18 event mapping', () => {
  test('a simple turn starts, reports the agent text and completes', () => {
    const { observations, state } = only(replay(['simple-turn.sse']));
    const started = 'evt_0dfc5bcf3001XJABWimzZNEp6V';
    assert.deepEqual(summary(observations), [
      { type: 'runtime.turn.started', payload: { turn_id: started } },
      // The model the step asked, as OpenCode names it: the adapter's model_ref.
      { type: 'runtime.model.used', payload: { model_ref: 'fake/fake-model' } },
      { type: 'runtime.agent_message', payload: { text: 'Fake reply #25: acknowledged.' } },
      { type: 'runtime.turn.completed', payload: { turn_id: started } },
    ]);
    assert.deepEqual(
      observations.map((item) => [item.native_event_id, item.sequence, item.occurred_at]),
      [
        [started, 2, '2026-09-26T22:11:13.267Z'],
        ['evt_0dfc5bd10001d5QVMkOxbBRU17', 5, '2026-09-26T22:11:13.296Z'],
        ['evt_0dfc5bd12001dohKUE1zvaQuDO', 7, '2026-09-26T22:11:13.298Z'],
        ['evt_0dfc5bd21002GO1kOSRbKhLsg5', 10, '2026-09-26T22:11:13.313Z'],
      ],
    );
    assert.deepEqual(
      observations.map((item) => item.provenance),
      [
        { epistemic: 'observed', native_type: 'opencode/session.execution.started' },
        { epistemic: 'observed', native_type: 'opencode/session.step.started' },
        { epistemic: 'reported', native_type: 'opencode/session.text.ended' },
        { epistemic: 'observed', native_type: 'opencode/session.execution.succeeded' },
      ],
    );
    assert.equal(state.model, 'fake/fake-model');
    assert.equal(state.turn, null);
    assert.equal(state.awaitingStart, false);
  });

  test('approvals: allowed once, rejected with a message, rejected without one', () => {
    const [allowed, rejectedWithMessage, rejected] = [...replay(['approvals.sse']).values()];
    const types = (replayed: Replayed | undefined) =>
      replayed?.observations.map((item) => item.type);
    assert.deepEqual(summary(allowed?.observations.slice(2, 6) ?? []), [
      {
        type: 'runtime.tool.started',
        payload: { tool_call_id: 'call_fake_26', tool_name: 'shell', title: null },
      },
      {
        type: 'runtime.approval.requested',
        payload: {
          approval_id: 'per_0dfc705a4001a75UlD3U06fbBY',
          subject: { kind: 'tool_use', tool_name: 'shell', summary: 'echo halcyonic-smoke' },
        },
      },
      {
        type: 'runtime.approval.resolved',
        payload: { approval_id: 'per_0dfc705a4001a75UlD3U06fbBY', decision: 'approved' },
      },
      {
        type: 'runtime.tool.completed',
        payload: { tool_call_id: 'call_fake_26', outcome: 'succeeded' },
      },
    ]);
    assert.deepEqual(types(allowed)?.at(-1), 'runtime.turn.completed');
    // Permission events are volatile: they carry no durable sequence.
    const approval = allowed?.observations.filter((item) =>
      item.type.startsWith('runtime.approval'),
    );
    assert.deepEqual(
      approval?.map((item) => item.sequence),
      [null, null],
    );

    // 2.0.18 continues the turn after a rejection with a message (and drops the message).
    assert.deepEqual(types(rejectedWithMessage), [
      'runtime.turn.started',
      'runtime.model.used',
      'runtime.tool.started',
      'runtime.approval.requested',
      'runtime.approval.resolved',
      'runtime.tool.completed',
      'runtime.agent_message',
      'runtime.turn.completed',
    ]);
    const denied = rejectedWithMessage?.observations[4];
    assert.equal(denied?.type === 'runtime.approval.resolved' && denied.payload.decision, 'denied');
    const failedTool = rejectedWithMessage?.observations[5];
    assert.equal(
      failedTool?.type === 'runtime.tool.completed' && failedTool.payload.outcome,
      'failed',
    );

    // Without a message the execution ends as interrupted, reason "shutdown".
    assert.deepEqual(types(rejected), [
      'runtime.turn.started',
      'runtime.model.used',
      'runtime.tool.started',
      'runtime.approval.requested',
      'runtime.approval.resolved',
      'runtime.tool.completed',
      'runtime.turn.interrupted',
    ]);
  });

  test('an interrupt ends the turn and clears a pending approval without resolving it', () => {
    const [streaming, waiting] = [...replay(['interrupts.sse']).values()];
    assert.deepEqual(summary(streaming?.observations ?? []).slice(2), [
      { type: 'runtime.agent_message', payload: { text: 'tick 1. tick 2. tick 3. ' } },
      { type: 'runtime.turn.interrupted', payload: { turn_id: 'evt_0dfc83d67001hnbnOnopyfWdvp' } },
    ]);
    assert.deepEqual(
      waiting?.observations.map((item) => item.type),
      [
        'runtime.turn.started',
        'runtime.model.used',
        'runtime.tool.started',
        'runtime.approval.requested',
        'runtime.tool.completed',
        'runtime.turn.interrupted',
      ],
    );
    assert.equal(waiting?.state.approvals.size, 0);
    assert.equal(waiting?.state.tools.size, 0);
  });

  test('a provider retry inside a turn is not reported; the turn still completes', () => {
    const retried = [...replay(['provider-retry.sse']).values()][1];
    assert.deepEqual(
      retried?.observations.map((item) => item.type),
      [
        'runtime.turn.started',
        'runtime.model.used',
        'runtime.agent_message',
        'runtime.turn.completed',
      ],
    );
  });

  test('a second turn at rest continues the session sequence with its own turn id', () => {
    const { observations } = only(replay(['simple-turn.sse', 'second-turn.sse']));
    assert.deepEqual(
      observations.map((item) => item.type),
      [
        'runtime.turn.started',
        'runtime.model.used',
        'runtime.agent_message',
        'runtime.turn.completed',
        // The second turn's step asks the same model, so nothing new is reported.
        'runtime.turn.started',
        'runtime.agent_message',
        'runtime.turn.completed',
      ],
    );
    const sequences = observations.map((item) => item.sequence ?? -1);
    assert.deepEqual(sequences, [2, 5, 7, 10, 12, 16, 19]);
    const second = observations[4];
    assert.equal(
      second?.type === 'runtime.turn.started' && second.payload.turn_id,
      'evt_0dfce92ad002YucWiHdjqco5vp',
    );
  });

  test('failed executions become failed turns with the OpenCode error type as code', () => {
    const [refused, unrouted] = [...replay(['failed-turns.sse']).values()];
    const failure = (replayed: Replayed | undefined) => {
      const last = replayed?.observations.at(-1);
      return last?.type === 'runtime.turn.failed' ? last.payload.error : null;
    };
    assert.deepEqual(failure(refused), {
      code: 'provider_invalid_request',
      message: 'Fake provider refused the request.',
    });
    assert.deepEqual(failure(unrouted), {
      code: 'provider_no_route',
      message: 'Model unavailable: fake/no-such-model',
    });
  });

  test('a stream joined mid-turn maps the rest of a turn known to be running', () => {
    const sessionId = 'ses_f2033eb78ffefXSh5mDR8n1JSK';
    const running = createSessionState();
    running.turn = { id: null };
    const { observations } = only(
      replay(
        ['reconnect-mid-turn.sse'],
        new Map([[sessionId, { state: running, observations: [] }]]),
      ),
    );
    assert.deepEqual(
      observations.map((item) => item.type),
      ['runtime.agent_message', 'runtime.turn.completed'],
    );
    // Without a known running turn, a lone ending is not reported.
    const fresh = only(
      replay(
        ['reconnect-mid-turn.sse'],
        new Map([[sessionId, { state: createSessionState(), observations: [] }]]),
      ),
    );
    assert.deepEqual(
      fresh.observations.map((item) => item.type),
      ['runtime.agent_message'],
    );
  });

  test('a stream cut off mid-turn leaves the turn running', () => {
    const { observations, state } = only(replay(['server-killed-mid-turn.sse']));
    assert.deepEqual(
      observations.map((item) => item.type),
      ['runtime.turn.started', 'runtime.model.used'],
    );
    assert.notEqual(state.turn, null);
  });

  test('after a reconciliation settled a turn, older transitions in the stream are ignored', () => {
    // Everything up to the end of the first turn (created 1790460673313) counts as settled.
    const state = createSessionState();
    state.settledThrough = 1790460673313;
    const { observations } = only(
      replay(
        ['simple-turn.sse', 'second-turn.sse'],
        new Map([[SIMPLE, { state, observations: [] }]]),
      ),
    );
    assert.deepEqual(
      observations.map((item) => [item.type, item.sequence]),
      [
        // The settled turn's text is history, so it is still reported.
        ['runtime.agent_message', 7],
        ['runtime.turn.started', 12],
        // The settled turn's step was not seen, so the next one reports the model.
        ['runtime.model.used', 14],
        ['runtime.agent_message', 16],
        ['runtime.turn.completed', 19],
      ],
    );
  });

  test('a turn seen starting stops awaiting its prompt and dates the turn from its start', () => {
    const state = createSessionState();
    state.awaitingStart = true;
    state.pendingInboxId = 'msg_1';
    state.since = 1;
    const event = decodeEvent(
      JSON.stringify({
        id: 'evt_2',
        created: 1790460673267,
        type: 'session.execution.started',
        data: { sessionID: 'ses_1' },
      }),
    );
    assert.ok(event !== null);
    observeEvent(state, event, NOW);
    assert.equal(state.awaitingStart, false);
    assert.equal(state.pendingInboxId, null);
    assert.equal(state.since, 1790460673267);
  });

  test('repeated transitions are reported once', () => {
    const state = createSessionState();
    const event = decodeEvent(
      JSON.stringify({
        id: 'evt_1',
        created: 1,
        type: 'session.execution.started',
        data: { sessionID: 'ses_1' },
        durable: { aggregateID: 'ses_1', seq: 2, version: 1 },
      }),
    );
    assert.ok(event !== null);
    assert.equal(observeEvent(state, event, NOW).length, 1);
    assert.equal(observeEvent(state, event, NOW).length, 0);
  });
});

describe("OpenCode 2.0.18 questions (the question tool's forms)", () => {
  // Captured from the pinned binary on a local model on 2026-10-01 (agent-questions.md).
  const types = (observations: readonly RuntimeObservation[]) =>
    observations
      .map((item) => item.type)
      .filter((type) => type.startsWith('runtime.question') || type.startsWith('runtime.turn'));

  test('a question is asked through a form, answered, and the turn goes on to complete', () => {
    const { observations, state } = only(replay(['question-answered.sse']));
    assert.deepEqual(types(observations), [
      'runtime.turn.started',
      'runtime.question.asked',
      'runtime.question.resolved',
      'runtime.turn.completed',
    ]);
    const asked = observations.find((item) => item.type === 'runtime.question.asked');
    assert.deepEqual(asked?.payload, {
      question_id: 'frm_0f73004380017s9rTrXaGWdsHI',
      prompts: [
        {
          key: 'q0',
          header: 'Colour choice',
          text: 'Which colour should the file mention?',
          options: [
            { label: 'red', description: 'Use the colour red' },
            { label: 'blue', description: 'Use the colour blue' },
          ],
          multiple: false,
          free_text: true,
          secret: false,
        },
      ],
      answerable: true,
    });
    assert.deepEqual(asked?.provenance, {
      epistemic: 'observed',
      native_type: 'opencode/form.created',
    });
    const resolved = observations.find((item) => item.type === 'runtime.question.resolved');
    assert.deepEqual(resolved?.payload, {
      question_id: 'frm_0f73004380017s9rTrXaGWdsHI',
      outcome: 'answered',
    });
    assert.equal(state.questions.size, 0);
  });

  test('several questions, one taking several answers, become prompts in order', () => {
    const { observations } = only(replay(['question-multiple.sse']));
    const asked = observations.find((item) => item.type === 'runtime.question.asked');
    assert.ok(asked?.type === 'runtime.question.asked');
    assert.deepEqual(
      asked.payload.prompts.map(({ key, multiple, free_text, options }) => ({
        key,
        multiple,
        free_text,
        labels: options.map((option) => option.label),
      })),
      [
        { key: 'q0', multiple: true, free_text: true, labels: ['apple', 'pear', 'plum'] },
        { key: 'q1', multiple: false, free_text: true, labels: ['Fruit', 'Basket'] },
      ],
    );
  });

  test('a question dismissed, or a turn interrupted while it waits, is dismissed and the turn ends', () => {
    for (const fixture of ['question-cancelled.sse', 'question-interrupted.sse']) {
      const { observations, state } = only(replay([fixture]));
      assert.deepEqual(
        types(observations),
        [
          'runtime.turn.started',
          'runtime.question.asked',
          'runtime.question.resolved',
          'runtime.turn.interrupted',
        ],
        fixture,
      );
      const resolved = observations.find((item) => item.type === 'runtime.question.resolved');
      assert.deepEqual(
        resolved?.payload && 'outcome' in resolved.payload ? resolved.payload.outcome : null,
        'dismissed',
      );
      assert.equal(state.questions.size, 0);
    }
  });

  test('a form that is not the question tool, or has fields it cannot express, is shown but not answerable', () => {
    const base = {
      id: 'frm_1',
      sessionID: 'ses_1',
      title: 'Connect',
      fields: [{ key: 'q0', type: 'string', description: 'Your name?', custom: true }],
    };
    assert.equal(formQuestion({ ...base, metadata: { kind: 'question' } })?.answerable, true);
    assert.equal(formQuestion({ ...base, metadata: { kind: 'mcp' } })?.answerable, false);
    const numbers = formQuestion({
      ...base,
      metadata: { kind: 'question' },
      fields: [{ key: 'q0', type: 'number', title: 'How many?' }],
    });
    assert.equal(numbers?.answerable, false);
    assert.equal(numbers?.prompts[0]?.text, 'How many?');
    assert.equal(numbers?.prompts[0]?.free_text, false);
    const repeated = formQuestion({
      ...base,
      metadata: { kind: 'question' },
      fields: [
        { key: 'q0', type: 'string', description: 'One?' },
        { key: 'q0', type: 'string', description: 'Two?' },
      ],
    });
    assert.equal(repeated?.answerable, false);
    assert.equal(formQuestion({ ...base, fields: [] }), null);
  });

  test("answers become the form's values: one for a single choice, a list for several, typed text among them", () => {
    const asked = formQuestion({
      id: 'frm_1',
      metadata: { kind: 'question' },
      fields: [
        {
          key: 'q0',
          type: 'multiselect',
          options: [
            { value: 'apple', label: 'apple' },
            { value: 'pear-value', label: 'pear' },
          ],
          custom: true,
        },
        { key: 'q1', type: 'string', options: [{ value: 'Fruit', label: 'Fruit' }], custom: true },
      ],
    });
    assert.ok(asked !== null);
    assert.deepEqual(
      formAnswer(asked.fields, [
        { key: 'q0', selected: ['apple', 'pear'], text: 'quince' },
        { key: 'q1', selected: [], text: 'My basket' },
      ]),
      { q0: ['apple', 'pear-value', 'quince'], q1: 'My basket' },
    );
    assert.deepEqual(formAnswer(asked.fields, [{ key: 'q1', selected: ['Fruit'], text: null }]), {
      q1: 'Fruit',
    });
  });
});

describe('OpenCode event decoding', () => {
  test('rejects payloads without an id and a type, and ignores unknown event types', () => {
    assert.equal(decodeEvent('not json'), null);
    assert.equal(decodeEvent('{"type":"session.created"}'), null);
    assert.equal(decodeEvent('{"id":"evt_1"}'), null);
    const unknown = decodeEvent(
      '{"id":"evt_1","type":"session.brand-new","data":{"sessionID":"ses_1"}}',
    );
    assert.ok(unknown !== null);
    assert.deepEqual(observeEvent(createSessionState(), unknown, NOW), []);
  });

  test('uses a durable sequence only when it orders the event session', () => {
    const decode = (durable: unknown) =>
      decodeEvent(
        JSON.stringify({ id: 'evt_1', type: 't', data: { sessionID: 'ses_1' }, durable }),
      );
    assert.equal(decode({ aggregateID: 'ses_1', seq: 4 })?.seq, 4);
    assert.equal(decode({ aggregateID: 'prj_1', seq: 4 })?.seq, null);
    assert.equal(decode({ aggregateID: 'ses_1', seq: -1 })?.seq, null);
    assert.equal(decode(undefined)?.seq, null);
  });

  test('falls back to the adapter clock when an event time is missing or unusable', () => {
    const state = createSessionState();
    for (const created of [undefined, 'soon', 1e20]) {
      const event = decodeEvent(
        JSON.stringify({
          id: `evt_${String(created)}`,
          created,
          type: 'session.text.ended',
          data: { sessionID: 'ses_1', text: 'hello' },
        }),
      );
      assert.ok(event !== null);
      assert.equal(observeEvent(state, event, NOW)[0]?.occurred_at, NOW.toISOString());
    }
  });

  test('blank agent text is not reported and long text is cut to the contract limit', () => {
    const text = (value: string) => {
      const event = decodeEvent(
        JSON.stringify({
          id: 'evt_t',
          type: 'session.text.ended',
          data: { sessionID: 's', text: value },
        }),
      );
      assert.ok(event !== null);
      return observeEvent(createSessionState(), event, NOW);
    };
    assert.deepEqual(text('  \n '), []);
    const long = text('\u{1F600}'.repeat(40_000));
    assertValidObservations(long);
    const cut = long[0];
    assert.ok(cut?.type === 'runtime.agent_message' && cut.payload.text.endsWith(' [truncated]'));
    assert.equal(clip('short', 10), 'short');
  });

  test('permission replies and subjects map only what they state', () => {
    assert.equal(replyOutcome('once'), 'approved');
    assert.equal(replyOutcome('always'), 'approved');
    assert.equal(replyOutcome('reject'), 'denied');
    assert.equal(replyOutcome('later'), null);
    assert.deepEqual(approvalSubject('shell', ['echo a', 'sleep 1']), {
      kind: 'tool_use',
      tool_name: 'shell',
      summary: 'echo a\nsleep 1',
    });
    assert.deepEqual(approvalSubject('webfetch', []), {
      kind: 'tool_use',
      tool_name: 'webfetch',
      summary: 'webfetch',
    });
    assert.equal(approvalSubject(' ', ['x']), null);
  });

  test('an execution error type that does not start with a letter still yields a valid code', () => {
    const state = createSessionState();
    state.turn = { id: null };
    const event = decodeEvent(
      JSON.stringify({
        id: 'evt_f',
        type: 'session.execution.failed',
        data: { sessionID: 's', error: { type: '9.odd Type', message: '' } },
      }),
    );
    assert.ok(event !== null);
    const [failed] = observeEvent(state, event, NOW);
    assertValidObservations(failed === undefined ? [] : [failed]);
    assert.deepEqual(failed?.type === 'runtime.turn.failed' && failed.payload.error, {
      code: 'opencode_9_odd_type',
      message: 'OpenCode reported the execution as failed without a message.',
    });
  });

  test('a delivered inbox item is no longer a steered instruction waiting, and is not reported', () => {
    const state = createSessionState();
    state.turn = { id: 'evt_turn' };
    state.steered.add('msg_steered');
    state.steered.add('msg_other');
    const delivered = decodeEvent(
      JSON.stringify({
        id: 'evt_d',
        created: 1,
        type: 'session.inbox.delivered',
        data: { sessionID: 's', inboxID: 'msg_steered' },
      }),
    );
    assert.ok(delivered !== null);
    // Even from a stretch a reconciliation already settled.
    state.settledThrough = 5;
    assert.deepEqual(observeEvent(state, delivered, NOW), []);
    assert.deepEqual([...state.steered], ['msg_other']);
    assert.deepEqual(state.turn, { id: 'evt_turn' });
  });
});
