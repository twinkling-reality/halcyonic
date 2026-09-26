import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { RuntimeObservation } from '@halcyonic/runtime-core';
import { createSessionState, type SessionState } from './events.ts';
import { reconcileSession, type SessionSnapshot } from './reconcile.ts';
import { assertValidObservations } from './testing/observations.ts';

const NOW = new Date('2026-09-26T12:00:00.000Z');

function snapshot(overrides: Partial<SessionSnapshot> = {}): SessionSnapshot {
  return {
    running: true,
    outcome: null,
    idleAt: null,
    permissions: [],
    toolStatus: new Map(),
    ...overrides,
  };
}

function running(turnId: string | null = 'evt_start'): SessionState {
  const state = createSessionState();
  state.turn = { id: turnId };
  return state;
}

function reconciled(state: SessionState, read: SessionSnapshot): RuntimeObservation[] {
  const result = reconcileSession(state, read, NOW);
  assert.ok(result.ok, result.ok ? '' : result.reason);
  assertValidObservations(result.observations);
  return result.observations;
}

function failure(state: SessionState, read: SessionSnapshot): string {
  const result = reconcileSession(state, read, NOW);
  assert.ok(!result.ok, 'expected the reconciliation to fail');
  return result.reason;
}

describe('reconciling a session after the event stream reconnected', () => {
  test('nothing is reported when the session is as it was', () => {
    const state = running();
    state.approvals.add('per_1');
    assert.deepEqual(
      reconciled(
        state,
        snapshot({ permissions: [{ id: 'per_1', action: 'shell', resources: [] }] }),
      ),
      [],
    );
    assert.deepEqual(reconciled(createSessionState(), snapshot({ running: false })), []);
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
      const [ended, ...rest] = reconciled(
        state,
        snapshot({ running: false, outcome, idleAt: Date.parse('2026-09-26T11:59:00.000Z') }),
      );
      assert.deepEqual(rest, []);
      assert.equal(ended?.type, type);
      assert.equal(ended?.occurred_at, '2026-09-26T11:59:00.000Z');
      assert.deepEqual(ended?.provenance, {
        epistemic: 'inferred',
        native_type: 'opencode/session',
        rule: 'opencode.reconnect.session_state',
      });
      assert.equal(
        ended !== undefined && 'turn_id' in ended.payload && ended.payload.turn_id,
        'evt_start',
      );
      assert.equal(state.turn, null);
      assert.equal(state.approvals.size + state.tools.size, 0);
    }
  });

  test('a turn that ended without a recorded outcome cannot be settled', () => {
    assert.match(failure(running(), snapshot({ running: false })), /does not say how/);
  });

  test('a prompt accepted but never seen starting is settled only if its turn is still running', () => {
    const waiting = createSessionState();
    waiting.awaitingStart = true;
    assert.match(failure(waiting, snapshot({ running: false, outcome: 'succeeded' })), /prompt/);
    const started = createSessionState();
    started.awaitingStart = true;
    assert.deepEqual(
      reconciled(started, snapshot()).map((item) => item.type),
      ['runtime.turn.started'],
    );
    assert.deepEqual(started.turn, { id: null });
    assert.equal(started.awaitingStart, false);
  });

  test('a running session with a new pending permission reports the turn and the approval', () => {
    const observations = reconciled(
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
      reconciled(replied, snapshot()).map((item) => [item.type, item.payload]),
      [['runtime.approval.resolved', { approval_id: 'per_1', decision: 'denied' }]],
    );
    assert.equal(replied.approvals.size, 0);

    const unexplained = running();
    unexplained.approvals.add('per_1');
    assert.match(failure(unexplained, snapshot()), /per_1/);
  });

  test('active tools are completed from their recorded status, and kept while still running', () => {
    const state = running();
    state.tools.add('call_ok');
    state.tools.add('call_err');
    state.tools.add('call_busy');
    const observations = reconciled(
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
});
