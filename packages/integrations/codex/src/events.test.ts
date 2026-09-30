/**
 * Event mapping from Codex 0.157.0 app-server messages captured from the real binary. The fixtures
 * are the JSON-RPC exchanges of the Codex smoke test of 2026-09-27 (a simple turn, approvals,
 * interrupts, steering, failed turns, a server killed mid-turn and its restart), one message per
 * line with its direction, with host paths, installation ids, commit and object hashes, host names
 * and temporary directory names replaced, and the initialize and discovery exchanges removed.
 */
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import type { RuntimeObservation } from '@halcyonic/runtime-core';
import {
  acceptTurnStart,
  clip,
  createThreadState,
  isRecord,
  observe,
  type PendingApproval,
  settleResumed,
  type ThreadState,
} from './events.ts';
import type { RequestId } from './protocol.ts';
import { assertValidObservations } from './testing/observations.ts';

const NOW = new Date('2026-09-27T12:00:00.000Z');

interface Replayed {
  readonly state: ThreadState;
  readonly observations: RuntimeObservation[];
  readonly requested: PendingApproval[];
}

interface Line {
  readonly dir: 'in' | 'out';
  readonly message: Record<string, unknown>;
}

function lines(name: string): Line[] {
  return readFileSync(new URL(`../fixtures/${name}`, import.meta.url), 'utf8')
    .split('\n')
    .filter((line) => line !== '')
    .map((line) => JSON.parse(line) as Line);
}

/**
 * Feeds a fixture through the mapping as the adapter does: server messages by thread, `turn/start`
 * answers through `acceptTurnStart`, and the client's approval answers as the person's decisions.
 * Codex's `cancel`, which Halcyonic never sends, counts as no answer.
 */
function replay(name: string, threads = new Map<string, Replayed>()): Map<string, Replayed> {
  const starts = new Map<RequestId, { threadId: string; running: string | null }>();
  const thread = (threadId: string): Replayed => {
    let replayed = threads.get(threadId);
    if (replayed === undefined) {
      replayed = { state: createThreadState(), observations: [], requested: [] };
      threads.set(threadId, replayed);
    }
    return replayed;
  };
  for (const { dir, message } of lines(name)) {
    const params = isRecord(message.params) ? message.params : {};
    const id = message.id as RequestId | undefined;
    if (dir === 'out') {
      if (message.method === 'turn/start' && id !== undefined) {
        const threadId = String(params.threadId);
        starts.set(id, { threadId, running: thread(threadId).state.activeTurnId });
      }
      const decision = isRecord(message.result) ? message.result.decision : undefined;
      if (message.method === undefined && (decision === 'accept' || decision === 'decline')) {
        for (const replayed of threads.values()) {
          for (const approval of replayed.state.approvals.values()) {
            if (approval.requestId === id)
              approval.answer = decision === 'accept' ? 'approved' : 'denied';
          }
        }
      }
      continue;
    }
    if (typeof message.method !== 'string') {
      const start = id === undefined ? undefined : starts.get(id);
      const turn =
        isRecord(message.result) && isRecord(message.result.turn) ? message.result.turn : {};
      if (start !== undefined && typeof turn.id === 'string') {
        acceptTurnStart(thread(start.threadId).state, turn.id, start.running);
      }
      continue;
    }
    if (typeof params.threadId !== 'string') continue;
    const replayed = thread(params.threadId);
    const observed = observe(
      replayed.state,
      id === undefined
        ? {
            kind: 'notification',
            method: message.method,
            params,
            emittedAtMs: typeof message.emittedAtMs === 'number' ? message.emittedAtMs : null,
          }
        : { kind: 'request', id, method: message.method, params },
      NOW,
    );
    replayed.observations.push(...observed.observations);
    if (observed.requested !== undefined) replayed.requested.push(observed.requested);
  }
  for (const replayed of threads.values()) assertValidObservations(replayed.observations);
  return threads;
}

function only(threads: Map<string, Replayed>, suffix: string): Replayed {
  const found = [...threads.entries()].find(([threadId]) => threadId.endsWith(suffix));
  assert.ok(found, `no thread ending in ${suffix}`);
  return found[1];
}

function summary(observations: readonly RuntimeObservation[]) {
  return observations.map((item) => ({ type: item.type, payload: item.payload }));
}

function types(observations: readonly RuntimeObservation[]) {
  return observations.map((item) => item.type);
}

describe('Codex 0.157.0 event mapping', () => {
  test('a simple turn starts, reports the agent text and completes', () => {
    const { observations, state } = only(replay('simple-turn.jsonl'), '28e1');
    const turn = '01a0e0ad-9402-7c83-9ef9-7e74390bc326';
    assert.deepEqual(summary(observations), [
      { type: 'runtime.turn.started', payload: { turn_id: turn } },
      { type: 'runtime.agent_message', payload: { text: 'Fake reply #2: acknowledged.' } },
      { type: 'runtime.turn.completed', payload: { turn_id: turn } },
    ]);
    assert.deepEqual(
      observations.map((item) => [item.native_event_id, item.sequence, item.occurred_at]),
      [
        [`${turn}:turn/started`, 1, '2026-09-27T02:24:27.204Z'],
        [`${turn}:msg_fake_2:item/completed`, 2, '2026-09-27T02:24:27.248Z'],
        [`${turn}:turn/completed`, 3, '2026-09-27T02:24:27.256Z'],
      ],
    );
    assert.deepEqual(
      observations.map((item) => item.provenance),
      [
        { epistemic: 'observed', native_type: 'codex/turn/started' },
        { epistemic: 'reported', native_type: 'codex/item/completed' },
        { epistemic: 'observed', native_type: 'codex/turn/completed' },
      ],
    );
    assert.equal(state.activeTurnId, null);
  });

  test('approvals: accepted, declined, cancelled by Codex, and a file change', () => {
    const threads = replay('approvals.jsonl');
    const { observations, requested, state } = only(threads, '18ce');
    const turns = observations.filter((item) => item.type === 'runtime.turn.started').length;
    assert.equal(turns, 5);
    const [accepted, declined, cancelled] = requested;
    assert.ok(accepted && declined && cancelled);
    const at = (approval: PendingApproval) =>
      observations.findIndex(
        (item) =>
          item.type === 'runtime.approval.requested' &&
          item.payload.approval_id === approval.approvalId,
      );
    assert.deepEqual(summary(observations.slice(at(accepted) - 1, at(accepted) + 5)), [
      {
        type: 'runtime.tool.started',
        payload: {
          tool_call_id: 'call_fake_10',
          tool_name: 'commandExecution',
          title: "/bin/zsh -lc 'touch approved.txt && echo created-approved'",
        },
      },
      {
        type: 'runtime.approval.requested',
        payload: {
          approval_id: '01a0e0b1-5f29-75d1-9709-3e7a68e1cc4f:0',
          subject: {
            kind: 'tool_use',
            tool_name: 'commandExecution',
            summary:
              "/bin/zsh -lc 'touch approved.txt && echo created-approved'\nin /workspace/as-b",
          },
        },
      },
      {
        type: 'runtime.approval.resolved',
        payload: { approval_id: '01a0e0b1-5f29-75d1-9709-3e7a68e1cc4f:0', decision: 'approved' },
      },
      {
        type: 'runtime.tool.completed',
        payload: { tool_call_id: 'call_fake_10', outcome: 'succeeded' },
      },
      {
        type: 'runtime.agent_message',
        payload: {
          text: 'Tool result received: Chunk ID: 49733b Wall time: 0.0000 seconds Process exited with code 0 Original token count: 5 Output: created-approved ',
        },
      },
      {
        type: 'runtime.turn.completed',
        payload: { turn_id: '01a0e0b1-5f29-75d1-9709-3e7a68e1cc4f' },
      },
    ]);
    // Declined: the command never ran, and Codex told the model so.
    assert.deepEqual(types(observations.slice(at(declined) + 1, at(declined) + 5)), [
      'runtime.approval.resolved',
      'runtime.tool.completed',
      'runtime.agent_message',
      'runtime.turn.completed',
    ]);
    const deniedAt = observations[at(declined) + 1];
    assert.equal(
      deniedAt?.type === 'runtime.approval.resolved' && deniedAt.payload.decision,
      'denied',
    );
    const failed = observations[at(declined) + 2];
    assert.equal(failed?.type === 'runtime.tool.completed' && failed.payload.outcome, 'failed');
    // Cancelled by Codex's own decision, not a person's: no resolution is reported, the turn is
    // interrupted and the approval is gone.
    assert.deepEqual(types(observations.slice(at(cancelled) + 1, at(cancelled) + 3)), [
      'runtime.tool.completed',
      'runtime.turn.interrupted',
    ]);
    // A file change approval is summarized from the item's changes, not the model's reason.
    const fileChange = observations.find(
      (item) =>
        item.type === 'runtime.approval.requested' &&
        item.payload.subject.tool_name === 'fileChange',
    );
    assert.deepEqual(fileChange?.payload, {
      approval_id: '01a0e0b1-6c91-7083-9fd7-eac158606e4e:3',
      subject: {
        kind: 'tool_use',
        tool_name: 'fileChange',
        summary: 'add /workspace/as-b/hello.txt',
      },
    });
    assert.equal(state.approvals.size, 0);
    assert.equal(state.tools.size, 0);
    // The second thread approves its command too.
    assert.deepEqual(types(only(threads, 'c4fa').observations), [
      'runtime.turn.started',
      'runtime.tool.started',
      'runtime.approval.requested',
      'runtime.approval.resolved',
      'runtime.tool.completed',
      'runtime.agent_message',
      'runtime.turn.completed',
    ]);
  });

  test('interrupts end the turn; a command left running is not reported stopped', () => {
    const threads = replay('interrupts.jsonl');
    const running = only(threads, '1902');
    assert.deepEqual(types(running.observations), [
      // During a slow model stream.
      'runtime.turn.started',
      'runtime.turn.interrupted',
      // While a command runs: it keeps running, so it is not reported completed.
      'runtime.turn.started',
      'runtime.tool.started',
      'runtime.turn.interrupted',
      // A file change applied without approval.
      'runtime.turn.started',
      'runtime.tool.started',
      'runtime.tool.completed',
      'runtime.agent_message',
      'runtime.turn.completed',
    ]);
    assert.deepEqual([...running.state.tools], ['call_fake_23']);
    // Interrupted with an approval pending: the approval is dropped, never resolved, and the late
    // answer finds nothing.
    const pending = only(threads, '1753');
    assert.deepEqual(types(pending.observations), [
      'runtime.turn.started',
      'runtime.tool.started',
      'runtime.approval.requested',
      'runtime.turn.interrupted',
    ]);
    assert.equal(pending.state.approvals.size, 0);
    assert.equal(pending.requested[0]?.answer, null);
  });

  test('steering keeps one turn; turn/start on a busy thread is a steer, not a turn', () => {
    const { observations, state } = only(replay('steer.jsonl'), '55da');
    const started = observations.filter((item) => item.type === 'runtime.turn.started');
    const ended = observations.filter((item) => item.type === 'runtime.turn.completed');
    assert.deepEqual(
      started.map((item) => (item.payload as { turn_id: string }).turn_id),
      ended.map((item) => (item.payload as { turn_id: string }).turn_id),
    );
    assert.equal(started.length, 4);
    const texts = observations
      .filter((item) => item.type === 'runtime.agent_message')
      .map((item) => (item.payload as { text: string }).text);
    assert.deepEqual(texts.slice(1, 3), [
      'Steer received: STEER: please wrap up now',
      'Steer received: STEER: during the command',
    ]);
    assert.ok(texts.includes('Steer received: STEER: sent with turn/start while running'));
    assert.equal(state.activeTurnId, null);
  });

  test('failed turns carry the error Codex reports', () => {
    const { observations } = only(replay('failed-turns.jsonl'), 'cbc5');
    assert.deepEqual(
      observations
        .filter((item) => item.type === 'runtime.turn.failed')
        .map((item) => (item.payload as { error: unknown }).error),
      [
        { code: 'codex_other', message: 'fake provider scripted failure (invalid_prompt)' },
        {
          code: 'codex_internal_server_error',
          message: 'We’re currently experiencing high demand, which may cause temporary errors.',
        },
      ],
    );
    assert.equal(observations.at(-1)?.type, 'runtime.turn.completed');
  });

  test('after a restart, the killed turn is settled from Codex record, as an inference', () => {
    const threads = replay('server-killed-mid-turn.jsonl');
    const killed = only(threads, 'd1fc');
    assert.equal(killed.state.activeTurnId, '01a0e0b7-e195-7bc2-bb7e-2a198d7e01e9');
    const resume = lines('restart.jsonl').find(
      ({ message }) => isRecord(message.result) && isRecord(message.result.thread),
    );
    assert.ok(resume !== undefined);
    const recorded = (resume.message.result as { thread: { turns: unknown[] } }).thread.turns;
    const settled = settleResumed(killed.state, recorded.at(-1), NOW);
    assert.equal(settled.unsettled, null);
    assert.deepEqual(settled.observations, [
      {
        type: 'runtime.turn.interrupted',
        payload: { turn_id: '01a0e0b7-e195-7bc2-bb7e-2a198d7e01e9' },
        native_event_id: '01a0e0b7-e195-7bc2-bb7e-2a198d7e01e9:thread/turns/list',
        sequence: killed.observations.length + 1,
        occurred_at: NOW.toISOString(),
        provenance: {
          epistemic: 'inferred',
          native_type: 'codex/thread/turns/list',
          rule: 'codex.restart.turn_status',
        },
      },
    ]);
    killed.observations.push(...settled.observations);
    // The resumed thread takes a new turn at rest.
    replay('restart.jsonl', threads);
    assert.deepEqual(types(killed.observations).slice(-4), [
      'runtime.turn.interrupted',
      'runtime.turn.started',
      'runtime.agent_message',
      'runtime.turn.completed',
    ]);
  });

  test('a restart cannot be settled without a final record of the running turn', () => {
    const state = createThreadState();
    state.activeTurnId = 'turn-1';
    assert.match(
      settleResumed(state, { id: 'turn-0', status: 'completed' }, NOW).unsettled ?? '',
      /no final record/,
    );
    const resting = createThreadState();
    assert.equal(
      settleResumed(resting, { id: 'turn-0', status: 'completed' }, NOW).unsettled,
      null,
    );
    assert.match(
      settleResumed(resting, { id: 'turn-9', status: 'inProgress' }, NOW).unsettled ?? '',
      /did not start/,
    );
  });
});

describe('Codex approval summaries', () => {
  const request = (method: string, params: Record<string, unknown>) => {
    const state = createThreadState();
    state.activeTurnId = 'turn-1';
    const observed = observe(
      state,
      {
        kind: 'request',
        id: 7,
        method,
        params: { threadId: 't', turnId: 'turn-1', itemId: 'call-1', ...params },
      },
      NOW,
    );
    return { state, observed };
  };

  test('come from the structured request: command, input, network access or nothing', () => {
    const summaryOf = (params: Record<string, unknown>) => {
      const item = request('item/commandExecution/requestApproval', params).observed
        .observations[0];
      return item?.type === 'runtime.approval.requested' ? item.payload.subject.summary : null;
    };
    assert.equal(
      summaryOf({ kind: 'command', command: 'ls', cwd: '/w', reason: 'The model says: trust me.' }),
      'ls\nin /w',
    );
    assert.equal(
      summaryOf({ kind: 'writeStdin', command: 'yes', cwd: null }),
      'Send input to a running command: yes',
    );
    assert.equal(
      summaryOf({ networkApprovalContext: { host: 'example.com', protocol: 'https' } }),
      'Network access to example.com (https)',
    );
    assert.equal(summaryOf({}), 'Codex did not say which command it wants to run.');
    const long = summaryOf({ command: 'x'.repeat(3000) }) ?? '';
    assert.equal(Array.from(long).length, 2000);
    assert.ok(long.endsWith(' [truncated]'));
  });

  test('a file change names its files and any write access asked for', () => {
    const { observed } = request('item/fileChange/requestApproval', { grantRoot: '/w' });
    const item = observed.observations[0];
    assert.equal(
      item?.type === 'runtime.approval.requested' && item.payload.subject.summary,
      'Codex did not say which files it wants to change.\nand write access under /w for the rest of the session',
    );
  });

  test('other requests raise no approval, so the adapter refuses them', () => {
    for (const method of [
      'item/tool/requestUserInput',
      'item/permissions/requestApproval',
      'mcpServer/elicitation/request',
      'execCommandApproval',
    ]) {
      const { observed, state } = request(method, {});
      assert.equal(observed.requested, undefined, method);
      assert.deepEqual(observed.observations, [], method);
      assert.equal(state.approvals.size, 0);
    }
  });

  test('an approval for a turn that already ended is not raised', () => {
    const state = createThreadState();
    state.endedTurns.push('turn-0');
    const observed = observe(
      state,
      {
        kind: 'request',
        id: 1,
        method: 'item/commandExecution/requestApproval',
        params: { threadId: 't', turnId: 'turn-0', itemId: 'call-1', command: 'ls' },
      },
      NOW,
    );
    assert.equal(observed.requested, undefined);
  });
});

describe('Codex event mapping edge cases', () => {
  const notify = (state: ThreadState, method: string, params: Record<string, unknown>) =>
    observe(
      state,
      { kind: 'notification', method, params: { threadId: 't', ...params }, emittedAtMs: null },
      NOW,
    );

  test('a repeated notification is not reported twice, and an unknown turn is not ended', () => {
    const state = createThreadState();
    const turn = { id: 'turn-1', status: 'inProgress' };
    assert.equal(notify(state, 'turn/started', { turn }).observations.length, 1);
    assert.equal(notify(state, 'turn/started', { turn }).observations.length, 0);
    const item = { type: 'commandExecution', id: 'call-1', command: 'ls', status: 'inProgress' };
    assert.equal(notify(state, 'item/started', { turnId: 'turn-1', item }).observations.length, 1);
    assert.equal(notify(state, 'item/started', { turnId: 'turn-1', item }).observations.length, 0);
    const done = { ...item, status: 'completed' };
    assert.equal(
      notify(state, 'item/completed', { turnId: 'turn-1', item: done }).observations.length,
      1,
    );
    assert.equal(
      notify(state, 'item/completed', { turnId: 'turn-1', item: done }).observations.length,
      0,
    );
    const end = { turn: { id: 'turn-1', status: 'completed' } };
    assert.equal(notify(state, 'turn/completed', end).observations.length, 1);
    assert.equal(notify(state, 'turn/completed', end).observations.length, 0);
    assert.equal(notify(state, 'turn/started', { turn }).observations.length, 0);
    assert.deepEqual(
      notify(state, 'turn/completed', { turn: { id: 'turn-2', status: 'completed' } }).observations,
      [],
    );
  });

  test('an error without a code, and one keyed by an object, get codes; items it does not map are ignored', () => {
    const state = createThreadState();
    notify(state, 'turn/started', { turn: { id: 'a', status: 'inProgress' } });
    const failed = notify(state, 'turn/completed', {
      turn: {
        id: 'a',
        status: 'failed',
        error: {
          message: 'Stream ended.',
          codexErrorInfo: { responseStreamDisconnected: { httpStatusCode: null } },
        },
      },
    }).observations[0];
    assert.deepEqual(failed?.type === 'runtime.turn.failed' && failed.payload.error, {
      code: 'codex_response_stream_disconnected',
      message: 'Stream ended.',
    });
    notify(state, 'turn/started', { turn: { id: 'b', status: 'inProgress' } });
    const bare = notify(state, 'turn/completed', {
      turn: { id: 'b', status: 'failed', error: null },
    }).observations[0];
    assert.deepEqual(bare?.type === 'runtime.turn.failed' && bare.payload.error, {
      code: 'codex_turn_failed',
      message: 'Codex reported the turn as failed without a message.',
    });
    for (const type of ['reasoning', 'mcpToolCall', 'webSearch', 'plan', 'userMessage']) {
      assert.deepEqual(
        notify(state, 'item/started', { turnId: 'b', item: { type, id: type } }).observations,
        [],
      );
    }
    assert.deepEqual(
      notify(state, 'item/completed', {
        turnId: 'b',
        item: { type: 'agentMessage', id: 'm', text: '  ' },
      }).observations,
      [],
    );
  });

  test('the answer to turn/start names the running turn only when Codex steered it', () => {
    const state = createThreadState();
    acceptTurnStart(state, 'turn-1', null);
    assert.equal(state.activeTurnId, 'turn-1');
    acceptTurnStart(state, 'turn-1', 'turn-1');
    assert.equal(state.activeTurnId, 'turn-1');
    notify(state, 'turn/started', { turn: { id: 'turn-1', status: 'inProgress' } });
    notify(state, 'turn/completed', { turn: { id: 'turn-1', status: 'completed' } });
    // An answer read after its turn already ended changes nothing.
    acceptTurnStart(state, 'turn-1', null);
    assert.equal(state.activeTurnId, null);
  });

  test('a reroute to another model is reported under the provider of the thread, once', () => {
    const state = createThreadState();
    // Before Codex reported the thread's provider, a reroute cannot be named.
    assert.deepEqual(
      notify(state, 'model/rerouted', { turnId: 't1', fromModel: 'a', toModel: 'b' }).observations,
      [],
    );
    state.provider = 'ollama';
    state.model = 'qwen3.6:35b-a3b-nvfp4';
    const rerouted = notify(state, 'model/rerouted', {
      turnId: 't1',
      fromModel: 'qwen3.6:35b-a3b-nvfp4',
      toModel: 'qwen3.8:27b-nvfp4',
      reason: 'highRiskCyberActivity',
    }).observations;
    assert.deepEqual(
      rerouted.map((item) => [item.type, item.payload, item.native_event_id, item.provenance]),
      [
        [
          'runtime.model.used',
          { model_ref: 'ollama/qwen3.8:27b-nvfp4' },
          't1:model/rerouted:qwen3.8:27b-nvfp4',
          { epistemic: 'observed', native_type: 'codex/model/rerouted' },
        ],
      ],
    );
    assertValidObservations(rerouted);
    assert.equal(state.model, 'qwen3.8:27b-nvfp4');
    assert.deepEqual(
      notify(state, 'model/rerouted', { turnId: 't2', toModel: 'qwen3.8:27b-nvfp4' }).observations,
      [],
    );
  });

  test('clip keeps whole characters', () => {
    assert.equal(clip('short', 10), 'short');
    assert.equal(Array.from(clip('😀'.repeat(20), 15)).length, 15);
  });
});
