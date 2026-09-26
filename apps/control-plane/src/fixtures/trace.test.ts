import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import type { ExecutionId } from '@halcyonic/contracts';
import { createVirtualTime } from '@halcyonic/runtime-core';
import { ControlPlane } from '../core/control-plane.ts';
import { TRACE_PLANS } from '../demo-plan.ts';
import { createUuidV7Generator } from '../ids.ts';
import { openSqliteJournal } from '../journal/sqlite-journal.ts';
import { capturingLogger, SCENARIOS_DIR, TEST_CLIENT } from '../testing/harness.ts';
import { recordTrace } from './record.ts';
import { parseTrace, replayTrace, TraceError } from './trace.ts';

const TRACES_DIR = new URL('../../../../fixtures/traces/', import.meta.url);
const readTrace = (file: string) => readFileSync(new URL(file, TRACES_DIR), 'utf8');
const TRACE_TEXT = readTrace('multiple_workstreams.jsonl');

function fixtureControlPlane() {
  const time = createVirtualTime(new Date('2026-09-27T00:00:00.000Z'));
  const ids = createUuidV7Generator({ now: () => time.now().getTime() });
  return {
    time,
    controlPlane: new ControlPlane({
      journal: openSqliteJournal({ path: ':memory:', originIfNew: 'fixture', ids }),
      adapters: [],
      ids,
      clock: time,
      scheduler: time,
      logger: capturingLogger().logger,
      commandTimeoutMs: 30_000,
    }),
  };
}

describe('recorded traces', () => {
  for (const plan of TRACE_PLANS) {
    test(`the committed ${plan.file} is exactly what the current code records`, async () => {
      assert.equal(
        await recordTrace(plan, SCENARIOS_DIR),
        readTrace(plan.file),
        'run pnpm fixtures:record',
      );
    });
  }

  test('replaying the trace reconstructs the recorded outcome through the normal write path', async () => {
    const { time, controlPlane } = fixtureControlPlane();
    const events = parseTrace(TRACE_TEXT, 'trace');
    const result = await replayTrace(controlPlane.recorder, events, {
      pace: 'instant',
      maxGapMs: 0,
      scheduler: time,
    });
    assert.deepEqual(result, { recorded: events.length, duplicates: 0 });

    const snapshot = controlPlane.snapshot();
    assert.equal(snapshot.journal.origin, 'fixture');
    assert.equal(snapshot.workstreams.length, 3);
    assert.ok(snapshot.workstreams.every((workstream) => workstream.status === 'completed'));
    assert.ok(snapshot.executions.every((execution) => execution.runtime.synthetic));
    const flagged = snapshot.workstreams.filter((ws) => ws.attention.level !== 'none');
    assert.deepEqual(
      flagged.map((ws) => [ws.title, ws.attention.reasons.map((reason) => reason.kind)]),
      [['Fix flaky checkout tests', ['verification_failed']]],
    );
    assert.ok(snapshot.commands.every((command) => command.status === 'completed'));
    await controlPlane.close();
  });

  test('the failure trace holds every state a client must present when work goes wrong', async () => {
    const { time, controlPlane } = fixtureControlPlane();
    await replayTrace(
      controlPlane.recorder,
      parseTrace(readTrace('failure_modes.jsonl'), 'trace'),
      {
        pace: 'instant',
        maxGapMs: 0,
        scheduler: time,
      },
    );
    const snapshot = controlPlane.snapshot();
    assert.deepEqual(
      snapshot.workstreams.map((workstream) => [
        workstream.title,
        workstream.status,
        workstream.attention.reasons.map((reason) => reason.kind),
      ]),
      [
        ['Speed up the dashboard render', 'failed', ['execution_failed']],
        ['Run the integration suite', 'unknown', ['execution_state_unknown']],
        ['Refactor the session store', 'interrupted', []],
        ['Drop the legacy sessions table', 'completed', []],
      ],
    );
    const rejected = snapshot.commands.filter((command) => command.status === 'rejected');
    assert.deepEqual(
      rejected.map((command) => [command.command_type, command.rejection?.code]),
      [['execution.send_instruction', 'capability_unsupported']],
    );
    await controlPlane.close();
  });

  test('replaying the same trace twice changes nothing the second time', async () => {
    const { time, controlPlane } = fixtureControlPlane();
    const events = parseTrace(TRACE_TEXT, 'trace');
    const options = { pace: 'instant' as const, maxGapMs: 0, scheduler: time };
    await replayTrace(controlPlane.recorder, events, options);
    const head = controlPlane.journal.head();
    assert.deepEqual(await replayTrace(controlPlane.recorder, events, options), {
      recorded: 0,
      duplicates: events.length,
    });
    assert.equal(controlPlane.journal.head(), head);
    await controlPlane.close();
  });

  test('replayed executions are history: commands to them are refused, not faked', async () => {
    const { time, controlPlane } = fixtureControlPlane();
    await replayTrace(controlPlane.recorder, parseTrace(TRACE_TEXT, 'trace'), {
      pace: 'instant',
      maxGapMs: 0,
      scheduler: time,
    });
    const execution = controlPlane.snapshot().executions[0];
    const outcome = controlPlane.commands.submit(
      {
        schema_version: 1,
        command_id: '0192f000-0000-4000-8000-000000000001' as never,
        command_type: 'execution.send_instruction',
        issued_at: time.now().toISOString(),
        client: TEST_CLIENT,
        payload: { execution_id: execution?.execution_id as ExecutionId, text: 'Continue.' },
      },
      'internal',
    );
    assert.equal(outcome.command?.rejection?.code, 'runtime_not_found');
    await controlPlane.close();
  });

  test('a malformed trace line is reported with its line number', () => {
    const lines = TRACE_TEXT.trim().split('\n');
    const broken = [lines[0], '{"event_type":"project.created"}', lines[1]].join('\n');
    assert.throws(
      () => parseTrace(broken, 'broken.jsonl'),
      (error: unknown) => {
        return error instanceof TraceError && error.message.startsWith('broken.jsonl:2:');
      },
    );
  });
});
