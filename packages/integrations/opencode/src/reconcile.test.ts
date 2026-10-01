import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { RuntimeObservation } from '@halcyonic/runtime-core';
import { createSessionState, type SessionState } from './events.ts';
import { reconcileSession, type SessionSnapshot } from './reconcile.ts';
import { assertValidObservations } from './testing/observations.ts';

const NOW = new Date('2026-09-26T12:00:00.000Z');
const ENQUEUED = Date.parse('2026-09-26T11:58:00.000Z');
const IDLE = Date.parse('2026-09-26T11:59:00.000Z');
const TURN_RULE = {
  epistemic: 'inferred',
  native_type: 'opencode/session',
  rule: 'opencode.reconnect.session_state',
};

function snapshot(overrides: Partial<SessionSnapshot> = {}): SessionSnapshot {
  return {
    inbox: new Set(),
    permissions: [],
    running: true,
    outcome: null,
    idleAt: null,
    toolStatus: new Map(),
    forms: [],
    settledForms: new Map(),
    ...overrides,
  };
}

function running(turnId: string | null = 'evt_start'): SessionState {
  const state = createSessionState();
  state.turn = { id: turnId };
  state.since = ENQUEUED;
  return state;
}

/** A session whose prompt OpenCode accepted as inbox item `msg_1`, not yet seen starting. */
function awaiting(): SessionState {
  const state = createSessionState();
  state.awaitingStart = true;
  state.pendingInboxId = 'msg_1';
  state.since = ENQUEUED;
  return state;
}

function settled(state: SessionState, read: SessionSnapshot): RuntimeObservation[] {
  const result = reconcileSession(state, read, NOW);
  assert.equal(result.kind, 'settled', JSON.stringify(result));
  const observations = result.kind === 'settled' ? result.observations : [];
  assertValidObservations(observations);
  return observations;
}

function unsettled(state: SessionState, read: SessionSnapshot): string {
  const result = reconcileSession(state, read, NOW);
  assert.equal(result.kind, 'unsettled', JSON.stringify(result));
  return result.kind === 'unsettled' ? result.reason : '';
}

function types(observations: readonly RuntimeObservation[]) {
  return observations.map((item) => item.type);
}

describe('reconciling a session after the event stream reconnected', () => {
  test('nothing is reported when the session is as it was', () => {
    const state = running();
    state.approvals.add('per_1');
    assert.deepEqual(
      settled(state, snapshot({ permissions: [{ id: 'per_1', action: 'shell', resources: [] }] })),
      [],
    );
    assert.deepEqual(settled(createSessionState(), snapshot({ running: false })), []);
  });

  test('a turn that ended while disconnected ends with the outcome OpenCode recorded', () => {
    const cases = [
      ['succeeded', 'runtime.turn.completed'],
      ['interrupted', 'runtime.turn.interrupted'],
      ['failed', 'runtime.turn.failed'],
    ] as const;
    for (const [outcome, type] of cases) {
      const state = running('evt_start');
      state.approvals.add('per_1');
      state.tools.add('call_1');
      const [ended, ...rest] = settled(state, snapshot({ running: false, outcome, idleAt: IDLE }));
      assert.deepEqual(rest, []);
      assert.equal(ended?.type, type);
      assert.equal(ended?.occurred_at, '2026-09-26T11:59:00.000Z');
      assert.deepEqual(ended?.provenance, TURN_RULE);
      assert.equal(
        ended !== undefined && 'turn_id' in ended.payload && ended.payload.turn_id,
        'evt_start',
      );
      assert.equal(state.turn, null);
      assert.equal(state.approvals.size + state.tools.size, 0);
      // Events up to the end are settled; the stream must not report that turn again.
      assert.equal(state.settledThrough, IDLE);
    }
  });

  test('an outcome older than the turn is transitional, and leaves the state as it was', () => {
    const state = running();
    const before = structuredClone(state);
    const stale = snapshot({ running: false, outcome: 'succeeded', idleAt: ENQUEUED - 1 });
    assert.deepEqual(reconcileSession(state, stale, NOW), { kind: 'transitional' });
    assert.deepEqual(reconcileSession(state, snapshot({ running: false }), NOW), {
      kind: 'transitional',
    });
    assert.deepEqual(state, before);
  });

  test('an outcome this adapter does not know cannot be settled', () => {
    const reason = unsettled(
      running(),
      snapshot({ running: false, outcome: 'paused', idleAt: IDLE }),
    );
    assert.match(reason, /paused/);
  });

  test('a prompt still in the inbox has not started: nothing is reported yet', () => {
    const state = awaiting();
    assert.deepEqual(settled(state, snapshot({ running: false, inbox: new Set(['msg_1']) })), []);
    assert.equal(state.awaitingStart, true);
    assert.equal(state.turn, null);
  });

  test('a prompt taken from the inbox started a turn, still running or already ended', () => {
    for (const inbox of [new Set<string>(), new Set(['msg_1'])]) {
      const state = awaiting();
      const [started] = settled(state, snapshot({ inbox }));
      assert.equal(started?.type, 'runtime.turn.started');
      assert.deepEqual(started?.provenance, TURN_RULE);
      assert.equal(started?.occurred_at, '2026-09-26T11:58:00.000Z');
      assert.deepEqual(state.turn, { id: null });
      assert.equal(state.awaitingStart, false);
    }

    const ran = awaiting();
    const observations = settled(
      ran,
      snapshot({ running: false, outcome: 'succeeded', idleAt: IDLE }),
    );
    assert.deepEqual(types(observations), ['runtime.turn.started', 'runtime.turn.completed']);
    assert.ok((observations[0]?.occurred_at ?? '') < (observations[1]?.occurred_at ?? ''));
    assert.equal(ran.turn, null);
    assert.equal(ran.settledThrough, IDLE);
  });

  test('a delivered prompt whose outcome is not recorded yet is transitional', () => {
    const state = awaiting();
    const read = snapshot({ running: false, outcome: 'succeeded', idleAt: ENQUEUED - 60_000 });
    assert.deepEqual(reconcileSession(state, read, NOW), { kind: 'transitional' });
    assert.equal(state.awaitingStart, true);
  });

  test('a prompt that OpenCode did not identify cannot be traced', () => {
    const state = awaiting();
    state.pendingInboxId = null;
    assert.match(unsettled(state, snapshot()), /did not identify/);
  });

  test('a running session with a new pending permission reports the turn and the approval', () => {
    const observations = settled(
      createSessionState(),
      snapshot({ permissions: [{ id: 'per_2', action: 'shell', resources: ['echo hi'] }] }),
    );
    assert.deepEqual(
      observations.map((item) => [item.type, item.provenance.epistemic]),
      [
        ['runtime.turn.started', 'inferred'],
        ['runtime.approval.requested', 'observed'],
      ],
    );
    assert.deepEqual(observations[1]?.payload, {
      approval_id: 'per_2',
      subject: { kind: 'tool_use', tool_name: 'shell', summary: 'echo hi' },
    });
  });

  test('an approval that disappeared is resolved only by a decision OpenCode confirmed', () => {
    const replied = running();
    replied.approvals.add('per_1');
    replied.replies.set('per_1', 'denied');
    assert.deepEqual(
      settled(replied, snapshot()).map((item) => [item.type, item.payload]),
      [['runtime.approval.resolved', { approval_id: 'per_1', decision: 'denied' }]],
    );
    assert.equal(replied.approvals.size, 0);

    const unexplained = running();
    unexplained.approvals.add('per_1');
    const before = structuredClone(unexplained);
    assert.match(unsettled(unexplained, snapshot()), /per_1/);
    assert.deepEqual(unexplained, before);
  });

  test('an approval gone because the turn ended is settled by the ending', () => {
    const state = running();
    state.approvals.add('per_1');
    const observations = settled(
      state,
      snapshot({ running: false, outcome: 'interrupted', idleAt: IDLE }),
    );
    assert.deepEqual(types(observations), ['runtime.turn.interrupted']);
  });

  test('active tools are completed from their recorded status, and kept while still running', () => {
    const state = running();
    state.tools.add('call_ok');
    state.tools.add('call_err');
    state.tools.add('call_busy');
    const observations = settled(
      state,
      snapshot({
        toolStatus: new Map([
          ['call_ok', 'completed'],
          ['call_err', 'error'],
          ['call_busy', 'running'],
        ]),
      }),
    );
    assert.deepEqual(
      observations.map((item) => item.payload),
      [
        { tool_call_id: 'call_ok', outcome: 'succeeded' },
        { tool_call_id: 'call_err', outcome: 'failed' },
      ],
    );
    assert.deepEqual([...state.tools], ['call_busy']);
  });

  test('a question asked while disconnected is learned from the pending forms', () => {
    const state = running();
    const form = {
      id: 'frm_1',
      sessionID: 'ses_1',
      title: 'Questions',
      metadata: { kind: 'question' },
      fields: [
        {
          key: 'q0',
          title: 'Colour',
          description: 'Which colour?',
          type: 'string',
          options: [{ value: 'red', label: 'red' }],
          custom: true,
        },
      ],
    };
    const observations = settled(state, snapshot({ forms: [form] }));
    assert.deepEqual(
      observations.map((item) => [item.type, item.provenance]),
      [
        [
          'runtime.question.asked',
          { epistemic: 'observed', native_type: 'opencode/session.form.list' },
        ],
      ],
    );
    assert.deepEqual([...state.questions.keys()], ['frm_1']);
    // Read again unchanged, it is not reported twice.
    assert.deepEqual(settled(state, snapshot({ forms: [form] })), []);
  });

  test('a question settled while disconnected is resolved as its form records it', () => {
    const fields = [{ key: 'q0', multiple: false, values: new Map() }];
    const state = running();
    state.questions.set('frm_answered', fields);
    state.questions.set('frm_cancelled', fields);
    state.questions.set('frm_unread', fields);
    state.questions.set('frm_ours', fields);
    state.answered.add('frm_ours');
    const observations = settled(
      state,
      snapshot({
        settledForms: new Map([
          ['frm_answered', 'answered'],
          ['frm_cancelled', 'cancelled'],
        ]),
      }),
    );
    assert.deepEqual(
      observations.map((item) => item.payload),
      [
        { question_id: 'frm_answered', outcome: 'answered' },
        { question_id: 'frm_cancelled', outcome: 'dismissed' },
        { question_id: 'frm_ours', outcome: 'answered' },
      ],
    );
    // A question whose state could not be read stays pending until it is, or the turn ends.
    assert.deepEqual([...state.questions.keys()], ['frm_unread']);
  });

  test('questions end with a turn that ended while disconnected, without a resolution', () => {
    const state = running();
    state.questions.set('frm_1', [{ key: 'q0', multiple: false, values: new Map() }]);
    const observations = settled(
      state,
      snapshot({ running: false, outcome: 'interrupted', idleAt: IDLE }),
    );
    assert.deepEqual(
      observations.map((item) => item.type),
      ['runtime.turn.interrupted'],
    );
    assert.equal(state.questions.size, 0);
  });
});
