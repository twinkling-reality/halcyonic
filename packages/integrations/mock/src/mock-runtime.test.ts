import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  type ExecutionId,
  type ProjectId,
  parseEventEnvelope,
  type RuntimeId,
  type WorkstreamId,
} from '@halcyonic/contracts';
import {
  createVirtualTime,
  type ExecutionContext,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import { MockRuntimeAdapter } from './mock-runtime.ts';
import { loadScenarios, ScenarioError } from './scenario.ts';

const SCENARIOS_DIR = fileURLToPath(new URL('../../../../fixtures/scenarios', import.meta.url));
const SCENARIOS = loadScenarios(SCENARIOS_DIR);

const execution: ExecutionContext = {
  execution_id: '01920000-0000-7000-8000-000000000003' as ExecutionId,
  workstream_id: '01920000-0000-7000-8000-000000000002' as WorkstreamId,
  project_id: '01920000-0000-7000-8000-000000000001' as ProjectId,
};

function setup() {
  const time = createVirtualTime(new Date('2026-09-26T10:00:00.000Z'));
  const runtime = new MockRuntimeAdapter({ scenarios: SCENARIOS, clock: time, scheduler: time });
  const observed: RuntimeObservation[] = [];
  const start = (scenario: string) =>
    runtime.startExecution({
      execution,
      instruction: 'Do the work.',
      options: { scenario },
      emit: (observation) => observed.push(observation),
    });
  const types = () => observed.map((observation) => observation.type);
  return { time, runtime, observed, start, types };
}

describe('mock runtime scenarios', () => {
  test('a successful feature runs its turn to completion', async () => {
    const { time, start, types } = setup();
    const result = await start('successful_feature');
    assert.equal(result.native_id, 'mock-session-1');
    await time.runUntilIdle();
    assert.deepEqual(types(), [
      'runtime.execution.started',
      'runtime.turn.started',
      'runtime.agent_message',
      'runtime.tool.started',
      'runtime.tool.completed',
      'runtime.tool.started',
      'runtime.tool.completed',
      'runtime.test_run.started',
      'runtime.test_run.completed',
      'runtime.agent_message',
      'runtime.turn.completed',
    ]);
  });

  test('every observation is contract-valid, ordered, uniquely identified and honestly labeled', async () => {
    const { time, start, observed } = setup();
    await start('successful_feature');
    await time.runUntilIdle();
    const sequences = observed.map((observation) => observation.sequence);
    assert.deepEqual(
      sequences,
      observed.map((_, index) => index + 1),
    );
    assert.equal(new Set(observed.map((o) => o.native_event_id)).size, observed.length);
    for (const [index, observation] of observed.entries()) {
      const epistemic = observation.type === 'runtime.agent_message' ? 'reported' : 'observed';
      assert.equal(observation.provenance.epistemic, epistemic);
      const envelope = {
        schema_version: 1,
        event_id: `01920000-0000-7000-8000-${String(index + 10).padStart(12, '0')}`,
        event_type: observation.type,
        ...execution,
        source: { kind: 'runtime', runtime_id: 'mock' as RuntimeId },
        source_native_id: observation.native_event_id,
        sequence: observation.sequence,
        occurred_at: observation.occurred_at,
        ingested_at: observation.occurred_at,
        correlation_id: null,
        causation_id: null,
        provenance: observation.provenance,
        payload: observation.payload,
      };
      const parsed = parseEventEnvelope(envelope);
      assert.ok(parsed.ok, JSON.stringify(parsed.ok ? null : parsed.issues));
    }
  });

  test('a runtime error fails the turn with the scripted error', async () => {
    const { time, start, observed } = setup();
    await start('runtime_error');
    await time.runUntilIdle();
    const last = observed.at(-1);
    assert.equal(last?.type, 'runtime.turn.failed');
    assert.equal(last?.type === 'runtime.turn.failed' && last.payload.error.code, 'provider_error');
  });

  test('a disconnect loses the connection and the session refuses further actions', async () => {
    const { time, runtime, start, types } = setup();
    await start('agent_disconnect');
    await time.runUntilIdle();
    assert.equal(types().at(-1), 'runtime.connection.lost');
    await assert.rejects(runtime.interrupt({ execution }), actionError('runtime_unreachable'));
  });
});

describe('mock runtime actions', () => {
  test('an approval blocks the turn until it is answered; approval continues the work', async () => {
    const { time, runtime, start, observed, types } = setup();
    await start('approval_required');
    await time.runUntilIdle();
    assert.equal(types().at(-1), 'runtime.approval.requested');
    await runtime.respondToApproval({ execution, approval_id: 'approval-1', decision: 'approve' });
    const resolved = observed.at(-1);
    assert.equal(
      resolved?.type === 'runtime.approval.resolved' && resolved.payload.decision,
      'approved',
    );
    await time.runUntilIdle();
    assert.equal(types().at(-1), 'runtime.turn.completed');
    assert.ok(types().includes('runtime.test_run.completed'));
  });

  test('a denial takes the denied branch', async () => {
    const { time, runtime, start, types } = setup();
    await start('approval_required');
    await time.runUntilIdle();
    await runtime.respondToApproval({ execution, approval_id: 'approval-1', decision: 'deny' });
    await time.runUntilIdle();
    const afterDecision = types().slice(types().indexOf('runtime.approval.resolved') + 1);
    assert.deepEqual(afterDecision, ['runtime.agent_message', 'runtime.turn.completed']);
  });

  test('interrupting a waiting turn ends it; the execution can then be instructed again', async () => {
    const { time, runtime, start, types } = setup();
    await start('approval_required');
    await time.runUntilIdle();
    await runtime.interrupt({ execution });
    assert.equal(types().at(-1), 'runtime.turn.interrupted');
    await assert.rejects(
      runtime.respondToApproval({ execution, approval_id: 'approval-1', decision: 'approve' }),
      actionError('approval_not_pending'),
    );
    await runtime.sendInstruction({ execution, text: 'Try again later.' });
    await time.runUntilIdle();
    assert.deepEqual(types().slice(-3), [
      'runtime.turn.started',
      'runtime.agent_message',
      'runtime.turn.completed',
    ]);
  });

  test('instructions while a turn runs are refused, matching the declared capability', async () => {
    const { runtime, start } = setup();
    assert.equal(runtime.descriptor.capabilities.instruct_while_running, false);
    await start('successful_feature');
    await assert.rejects(
      runtime.sendInstruction({ execution, text: 'Also do this.' }),
      actionError('turn_in_progress'),
    );
  });

  test('start options are validated before anything starts', async () => {
    const { runtime } = setup();
    assert.deepEqual(runtime.validateStartOptions({}).ok, false);
    assert.deepEqual(runtime.validateStartOptions({ scenario: 'nope' }).ok, false);
    assert.deepEqual(
      runtime.validateStartOptions({ scenario: 'runtime_error', model: 'x' }).ok,
      false,
    );
    assert.deepEqual(runtime.validateStartOptions({ scenario: 'runtime_error' }).ok, true);
  });

  test('actions on an execution the runtime never started are refused', async () => {
    const { runtime } = setup();
    await assert.rejects(
      runtime.interrupt({ execution }),
      actionError('execution_unknown_to_runtime'),
    );
  });
});

describe('scenario files', () => {
  test('every committed scenario loads', () => {
    assert.deepEqual([...SCENARIOS.keys()].sort(), [
      'agent_disconnect',
      'approval_required',
      'failing_tests',
      'runtime_error',
      'successful_feature',
    ]);
  });

  test('a scenario whose id does not match its file, or that emits a mock-owned event, is refused', () => {
    const directory = mkdtempSync(join(tmpdir(), 'halcyonic-scenarios-'));
    try {
      const scenario = (id: string, type: string) => ({
        format: 1,
        id,
        description: 'Test scenario.',
        steps: [{ after_ms: 0, emit: { type, payload: {} } }],
      });
      writeFileSync(
        join(directory, 'one.json'),
        JSON.stringify(scenario('two', 'runtime.turn.completed')),
      );
      assert.throws(() => loadScenarios(directory), ScenarioError);
      writeFileSync(
        join(directory, 'one.json'),
        JSON.stringify(scenario('one', 'runtime.turn.started')),
      );
      assert.throws(() => loadScenarios(directory), ScenarioError);
    } finally {
      rmSync(directory, { recursive: true, force: true });
    }
  });
});

function actionError(code: string) {
  return (error: unknown) => error instanceof RuntimeActionError && error.code === code;
}
