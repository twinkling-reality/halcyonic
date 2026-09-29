import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { compileValidator, type EntityChanges, ServerMessage } from '@halcyonic/contracts';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { RealtimeClient } from '../client/realtime-client.ts';
import { ControlPlane } from '../core/control-plane.ts';
import { createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { capturingLogger, startTestServer, TEST_CLIENT } from '../testing/harness.ts';
import {
  DEMONSTRATION_FILE,
  DEMONSTRATION_SOURCE,
  DEMONSTRATION_TRACE,
  type Demonstration,
  REPLAY_MAX_GAP_MS,
  recordDemonstration,
} from './demonstration.ts';
import { parseTrace, replayTrace } from './trace.ts';

const TRACE_TEXT = readFileSync(DEMONSTRATION_TRACE, 'utf8');
const COMMITTED = readFileSync(DEMONSTRATION_FILE, 'utf8');
const DEMONSTRATION = JSON.parse(COMMITTED) as Demonstration;
const validateServerMessage = compileValidator(ServerMessage);

/** The entities a client holds, keyed by id, once it applied a snapshot and then each change. */
function held(sources: readonly EntityChanges[]) {
  const state = {
    projects: new Map<string, unknown>(),
    workstreams: new Map<string, unknown>(),
    executions: new Map<string, unknown>(),
    commands: new Map<string, unknown>(),
  };
  for (const source of sources) {
    for (const project of source.projects) state.projects.set(project.project_id, project);
    for (const workstream of source.workstreams) {
      state.workstreams.set(workstream.workstream_id, workstream);
    }
    for (const execution of source.executions) {
      state.executions.set(execution.execution_id, execution);
    }
    for (const command of source.commands) state.commands.set(command.command_id, command);
  }
  return state;
}

describe('the XR client demonstration', () => {
  test('the committed demonstration is exactly what the current code records', async () => {
    assert.equal(
      await recordDemonstration(TRACE_TEXT, DEMONSTRATION_SOURCE),
      COMMITTED,
      'run pnpm demonstration:record',
    );
  });

  test('every message it holds matches the realtime contract', () => {
    const messages = [
      DEMONSTRATION.welcome,
      DEMONSTRATION.snapshot,
      ...DEMONSTRATION.events.map(({ message }) => message),
    ];
    for (const message of messages) {
      const parsed = validateServerMessage(message);
      assert.ok(parsed.ok, parsed.ok ? '' : JSON.stringify(parsed.issues));
    }
  });

  test('it is labeled as recorded and has no runtime, so no client offers an action', () => {
    const { welcome, snapshot } = DEMONSTRATION;
    assert.equal(welcome.journal.origin, 'fixture');
    assert.deepEqual(snapshot.snapshot.journal, welcome.journal);
    assert.equal(welcome.resumed, false);
    assert.equal(welcome.head, snapshot.snapshot.position);
    assert.deepEqual(snapshot.snapshot.runtimes, []);
    const executions = DEMONSTRATION.events.flatMap(({ message }) => message.changes.executions);
    assert.ok(executions.length > 0);
    assert.ok(executions.every((execution) => execution.runtime.synthetic));
  });

  test('it plays every event of its trace in journal order, at the pace pnpm replay does', () => {
    const trace = parseTrace(TRACE_TEXT, DEMONSTRATION_SOURCE);
    assert.deepEqual(
      DEMONSTRATION.events.map(({ message }) => message.event),
      trace,
    );
    assert.deepEqual(
      DEMONSTRATION.events.map(({ message }) => message.position),
      trace.map((_, index) => index + 1),
    );
    let expected = 0;
    let previous: number | null = null;
    trace.forEach((event, index) => {
      const at = Date.parse(event.ingested_at);
      if (previous !== null && at > previous) {
        expected += Math.min(at - previous, REPLAY_MAX_GAP_MS);
      }
      previous = at;
      assert.equal(DEMONSTRATION.events[index]?.at_ms, expected, `event ${index + 1}`);
    });
  });

  test('applied in order, its changes reach the state the replayed trace reaches', async () => {
    const time = createVirtualTime(new Date('2026-09-27T00:00:00.000Z'));
    const ids = createUuidV7Generator({ now: () => time.now().getTime() });
    const controlPlane = new ControlPlane({
      journal: openSqliteJournal({ path: ':memory:', originIfNew: 'fixture', ids }),
      adapters: [],
      ids,
      clock: time,
      scheduler: time,
      logger: capturingLogger().logger,
      commandTimeoutMs: 30_000,
    });
    try {
      await replayTrace(controlPlane.recorder, parseTrace(TRACE_TEXT, 'trace'), {
        pace: 'instant',
        maxGapMs: 0,
        scheduler: time,
      });
      const played = held([
        DEMONSTRATION.snapshot.snapshot,
        ...DEMONSTRATION.events.map(({ message }) => message.changes),
      ]);
      assert.deepEqual(played, held([controlPlane.snapshot()]));
      assert.equal(played.workstreams.size, 3);
    } finally {
      await controlPlane.close();
    }
  });

  test('its welcome carries what a live realtime connection welcomes a client with', async () => {
    const server = await startTestServer();
    try {
      const client = await RealtimeClient.connect(server.wsUrl, server.token);
      client.hello(TEST_CLIENT);
      const welcome = await client.waitFor((message) => message.type === 'welcome');
      await client.close();
      assert.equal(welcome.type, 'welcome');
      if (welcome.type !== 'welcome') return;
      assert.equal(DEMONSTRATION.welcome.protocol, welcome.protocol);
      assert.deepEqual(DEMONSTRATION.welcome.command_policies, welcome.command_policies);
    } finally {
      await server.stop();
    }
  });

  test('a trace without events records nothing', async () => {
    await assert.rejects(recordDemonstration('\n', 'empty.jsonl'), /empty\.jsonl holds no events/);
  });
});
