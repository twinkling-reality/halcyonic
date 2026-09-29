import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type {
  EventEnvelope,
  RuntimeDescriptor,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import {
  RuntimeActionError,
  type RuntimeAdapter,
  type RuntimeObservation,
  type StartExecutionRequest,
  type StartExecutionResult,
} from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { capturingLogger, createTestControlPlane, SCENARIOS } from '../testing/harness.ts';

const [FEATURE, FAILING, APPROVAL] = DEMO_WORKSTREAMS as unknown as [
  (typeof DEMO_WORKSTREAMS)[0],
  (typeof DEMO_WORKSTREAMS)[0],
  (typeof DEMO_WORKSTREAMS)[0],
];

const directory = mkdtempSync(join(tmpdir(), 'halcyonic-control-plane-'));
after(() => rmSync(directory, { recursive: true, force: true }));

function types(events: Iterable<{ event: EventEnvelope }>): string[] {
  return [...events].map((stored) => stored.event.event_type);
}

function stubRuntime(
  runtimeId: string,
  startExecution: NonNullable<RuntimeAdapter['startExecution']>,
): RuntimeAdapter {
  const descriptor: RuntimeDescriptor = {
    runtime_id: runtimeId as RuntimeId,
    kind: 'stub',
    display_name: `Stub ${runtimeId}`,
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: false,
      instruct_while_running: false,
      respond_to_approval: false,
      interrupt: false,
    },
  };
  return {
    descriptor,
    validateStartOptions: () => ({ ok: true }),
    startExecution,
    close: async () => {},
  };
}

/** The mock runtime, keeping every observation it reports. */
class ObservedMockRuntime extends MockRuntimeAdapter {
  readonly observations: RuntimeObservation[] = [];

  override startExecution(request: StartExecutionRequest): Promise<StartExecutionResult> {
    return super.startExecution({
      ...request,
      emit: (observation) => {
        this.observations.push(observation);
        request.emit(observation);
      },
    });
  }
}

async function createWorkstream(
  harness: ReturnType<typeof createTestControlPlane>,
  workstream = FEATURE,
): Promise<WorkstreamId> {
  const { controlPlane, commands } = harness;
  const project = controlPlane.commands.submit(commands.createProject('P'), 'internal');
  assert.equal(project.command?.result?.kind, 'project_created');
  if (project.command?.result?.kind !== 'project_created') throw new Error('no project');
  const created = controlPlane.commands.submit(
    commands.createWorkstream(project.command.result.project_id, workstream),
    'internal',
  );
  if (created.command?.result?.kind !== 'workstream_created') throw new Error('no workstream');
  return created.command.result.workstream_id;
}

describe('command lifecycle', () => {
  test('a start command completes only once the runtime confirms, and the work streams in', async () => {
    const harness = createTestControlPlane();
    const { controlPlane, commands, time, journal } = harness;
    const workstreamId = await createWorkstream(harness);
    const start = controlPlane.commands.submit(
      commands.startExecution(workstreamId, FEATURE),
      'internal',
    );
    assert.equal(start.disposition, 'accepted');
    assert.equal(start.command?.status, 'accepted', 'accepted is not success');

    await time.runUntilIdle();
    const command = controlPlane.projection.command(start.command?.command_id ?? '');
    assert.equal(command?.status, 'completed');
    assert.equal(command?.result?.kind, 'execution_created');
    const workstream = controlPlane.projection.workstream(workstreamId);
    assert.equal(workstream?.status, 'completed');
    const sequence = types(journal.readAll());
    assert.deepEqual(
      sequence.slice(
        sequence.indexOf('execution.created'),
        sequence.indexOf('execution.created') + 4,
      ),
      [
        'execution.created',
        'runtime.execution.started',
        'runtime.turn.started',
        'command.completed',
      ],
    );
    await controlPlane.close();
  });

  test('rejected commands are journaled with a reason and create nothing', async () => {
    const harness = createTestControlPlane();
    const { controlPlane, commands } = harness;
    const workstreamId = await createWorkstream(harness);
    const badOptions = commands.startExecution(workstreamId, { ...FEATURE, scenario: 'nope' });
    const rejected = controlPlane.commands.submit(badOptions, 'internal');
    assert.equal(rejected.disposition, 'rejected');
    assert.equal(rejected.command?.rejection?.code, 'invalid_runtime_options');
    assert.deepEqual(controlPlane.projection.executions(), []);

    const unknownWorkstream = controlPlane.commands.submit(
      commands.startExecution('01920000-0000-7000-8000-00000000ffff' as WorkstreamId, FEATURE),
      'internal',
    );
    assert.equal(unknownWorkstream.command?.rejection?.code, 'workstream_not_found');
    const rejections = [...controlPlane.journal.readAll()].filter(
      (stored) => stored.event.event_type === 'command.rejected',
    );
    assert.equal(rejections.length, 2);
    await controlPlane.close();
  });

  test('resubmitting a command is idempotent; reusing its id for another command is a conflict', async () => {
    const { controlPlane, commands } = createTestControlPlane();
    const command = commands.createProject('Once');
    assert.equal(controlPlane.commands.submit(command, 'internal').disposition, 'accepted');
    const again = controlPlane.commands.submit({ ...command }, 'internal');
    assert.equal(again.disposition, 'duplicate');
    const head = controlPlane.journal.head();
    const conflict = controlPlane.commands.submit(
      { ...command, payload: { name: 'Twice' } },
      'internal',
    );
    assert.equal(conflict.disposition, 'conflict');
    assert.equal(controlPlane.journal.head(), head, 'a conflict is not journaled');
    assert.equal(controlPlane.projection.projects().length, 1);
    await controlPlane.close();
  });

  test('an approval round trip is confirmed by the runtime before the command completes', async () => {
    const harness = createTestControlPlane();
    const { controlPlane, commands, time } = harness;
    const workstreamId = await createWorkstream(harness, APPROVAL);
    controlPlane.commands.submit(commands.startExecution(workstreamId, APPROVAL), 'internal');
    await time.runUntilIdle();
    const waiting = controlPlane.projection.workstream(workstreamId);
    assert.equal(waiting?.status, 'waiting_for_human');
    assert.equal(waiting?.attention.level, 'action_required');
    const executionId = waiting?.current_execution_id ?? '';
    const approve = controlPlane.commands.submit(
      commands.approve(executionId as never, 'approval-1'),
      'internal',
    );
    assert.equal(approve.disposition, 'accepted');
    await time.runUntilIdle();
    const sequence = types(controlPlane.journal.readAll());
    assert.ok(
      sequence.lastIndexOf('runtime.approval.resolved') < sequence.lastIndexOf('command.completed'),
    );
    assert.equal(controlPlane.projection.workstream(workstreamId)?.status, 'completed');
    assert.equal(
      controlPlane.projection.command(approve.command?.command_id ?? '')?.status,
      'completed',
    );
    await controlPlane.close();
  });

  test('instructions follow the declared capabilities', async () => {
    const harness = createTestControlPlane();
    const { controlPlane, commands, time } = harness;
    const workstreamId = await createWorkstream(harness, FAILING);
    controlPlane.commands.submit(commands.startExecution(workstreamId, FAILING), 'internal');
    await time.advance(700);
    const executionId =
      controlPlane.projection.workstream(workstreamId)?.current_execution_id ?? '';
    const instruct = (text: string) => ({
      ...commands.createProject('unused'),
      command_type: 'execution.send_instruction' as const,
      payload: { execution_id: executionId as never, text },
    });
    const whileRunning = controlPlane.commands.submit(instruct('Also check lint.'), 'internal');
    assert.equal(whileRunning.command?.rejection?.code, 'capability_unsupported');
    await time.runUntilIdle();
    const atRest = controlPlane.commands.submit(
      instruct('Look at the three failures.'),
      'internal',
    );
    assert.equal(atRest.disposition, 'accepted');
    await time.runUntilIdle();
    const execution = controlPlane.projection.execution(executionId);
    assert.equal(execution?.turn_count, 2);
    assert.equal(execution?.status, 'completed');
    await controlPlane.close();
  });
});

describe('truthful failure handling', () => {
  test('a runtime that never answers times out with an unknown effect', async () => {
    const harness = createTestControlPlane({
      commandTimeoutMs: 5000,
      adapters: () => [stubRuntime('stuck', () => new Promise(() => {}))],
    });
    const { controlPlane, commands, time } = harness;
    const workstreamId = await createWorkstream(harness);
    const start = controlPlane.commands.submit(
      {
        ...commands.startExecution(workstreamId, FEATURE),
        payload: {
          workstream_id: workstreamId,
          runtime_id: 'stuck' as RuntimeId,
          instruction: 'Go.',
          options: {},
        },
      },
      'internal',
    );
    await time.advance(5000);
    const command = controlPlane.projection.command(start.command?.command_id ?? '');
    assert.equal(command?.status, 'failed');
    assert.deepEqual(
      { code: command?.failure?.code, effect: command?.failure?.effect },
      { code: 'timeout', effect: 'unknown' },
    );
    const execution = controlPlane.projection.executions()[0];
    assert.equal(execution?.status, 'unknown', 'it may have started, so it is not failed');
    await controlPlane.close();
  });

  test('a runtime that refuses to start fails the execution with its reason', async () => {
    const harness = createTestControlPlane({
      adapters: () => [
        stubRuntime('refusing', async () => {
          throw new RuntimeActionError('provider_unavailable', 'No provider is configured.');
        }),
      ],
    });
    const { controlPlane, commands, time } = harness;
    const workstreamId = await createWorkstream(harness);
    controlPlane.commands.submit(
      {
        ...commands.startExecution(workstreamId, FEATURE),
        payload: {
          workstream_id: workstreamId,
          runtime_id: 'refusing' as RuntimeId,
          instruction: 'Go.',
          options: {},
        },
      },
      'internal',
    );
    await time.runUntilIdle();
    const execution = controlPlane.projection.executions()[0];
    assert.equal(execution?.status, 'failed');
    assert.deepEqual(execution?.status_reason, {
      code: 'provider_unavailable',
      message: 'No provider is configured.',
    });
    await controlPlane.close();
  });

  test('an invalid observation from an adapter is logged and never journaled', async () => {
    const { logger, entries } = capturingLogger();
    const harness = createTestControlPlane({
      logger,
      adapters: () => [
        stubRuntime('sloppy', async (request) => {
          request.emit({
            type: 'runtime.agent_message',
            occurred_at: '2026-09-26T10:00:00.000Z',
            native_event_id: null,
            sequence: 1,
            provenance: { epistemic: 'observed', native_type: null },
            payload: { text: 'Tests pass.' },
          });
          return { native_id: null };
        }),
      ],
    });
    const { controlPlane, commands, time } = harness;
    const workstreamId = await createWorkstream(harness);
    controlPlane.commands.submit(
      {
        ...commands.startExecution(workstreamId, FEATURE),
        payload: {
          workstream_id: workstreamId,
          runtime_id: 'sloppy' as RuntimeId,
          instruction: 'Go.',
          options: {},
        },
      },
      'internal',
    );
    await time.runUntilIdle();
    assert.ok(!types(controlPlane.journal.readAll()).includes('runtime.agent_message'));
    assert.ok(entries.some((entry) => entry.message === 'runtime observation rejected'));
    await controlPlane.close();
  });

  test('an adapter that claims a capability it lacks is refused at registration', () => {
    const broken = stubRuntime('broken', async () => ({ native_id: null }));
    const claims = {
      ...broken,
      descriptor: {
        ...broken.descriptor,
        capabilities: { ...broken.descriptor.capabilities, interrupt: true },
      },
    };
    assert.throws(
      () => createTestControlPlane({ adapters: () => [claims] }),
      /interrupt is not implemented/,
    );
  });
});

describe('restart', () => {
  test('state is rebuilt from the journal and in-flight work becomes unknown, not assumed', async () => {
    const path = join(directory, 'restart.db');
    const first = createTestControlPlane({
      path,
      adapters: (time) => [
        new MockRuntimeAdapter({ scenarios: SCENARIOS, clock: time, scheduler: time }),
        stubRuntime('stuck', () => new Promise(() => {})),
      ],
    });
    const workstreamId = await createWorkstream(first, APPROVAL);
    first.controlPlane.commands.submit(
      first.commands.startExecution(workstreamId, APPROVAL),
      'internal',
    );
    const stuck = first.controlPlane.commands.submit(
      {
        ...first.commands.startExecution(workstreamId, FEATURE),
        payload: {
          workstream_id: workstreamId,
          runtime_id: 'stuck' as RuntimeId,
          instruction: 'Go.',
          options: {},
        },
      },
      'internal',
    );
    await first.time.advance(10_000);
    const before = first.controlPlane.snapshot();
    assert.ok(before.executions.some((execution) => execution.status === 'waiting_for_human'));
    await first.controlPlane.close();

    const second = createTestControlPlane({
      path,
      time: first.time,
      adapters: (time) => [
        new MockRuntimeAdapter({ scenarios: SCENARIOS, clock: time, scheduler: time }),
        stubRuntime('stuck', () => new Promise(() => {})),
      ],
    });
    const rebuilt = second.controlPlane.snapshot();
    assert.deepEqual(
      { ...rebuilt, runtimes: [] },
      { ...before, runtimes: [] },
      'the journal alone reproduces the state',
    );

    second.controlPlane.reconcile();
    const after = second.controlPlane.snapshot();
    assert.ok(after.executions.every((execution) => execution.status === 'unknown'));
    assert.ok(
      after.executions.every(
        (execution) => execution.status_reason?.code === 'control_plane_restarted',
      ),
    );
    const pending = second.controlPlane.projection.command(stuck.command?.command_id ?? '');
    assert.deepEqual(
      { status: pending?.status, effect: pending?.failure?.effect },
      { status: 'failed', effect: 'unknown' },
    );
    await second.controlPlane.close();
  });

  test('a mock runtime started after a restart has every observation journaled', async () => {
    const path = join(directory, 'restart-mock.db');
    const first = createTestControlPlane({ path });
    const earlier = await createWorkstream(first);
    first.controlPlane.commands.submit(first.commands.startExecution(earlier, FEATURE), 'internal');
    await first.time.runUntilIdle();
    assert.equal(first.controlPlane.projection.workstream(earlier)?.status, 'completed');
    await first.controlPlane.close();

    // A new control plane process on the same journal, with a new mock runtime.
    const { time } = first;
    const restarted = new ObservedMockRuntime({
      scenarios: SCENARIOS,
      clock: time,
      scheduler: time,
    });
    const second = createTestControlPlane({ path, time, adapters: () => [restarted] });
    second.controlPlane.reconcile();
    const workstreamId = await createWorkstream(second);
    second.controlPlane.commands.submit(
      second.commands.startExecution(workstreamId, FEATURE),
      'internal',
    );
    await time.runUntilIdle();

    const executionId =
      second.controlPlane.projection.workstream(workstreamId)?.current_execution_id;
    assert.ok(restarted.observations.length > 0);
    const journaled = [...second.journal.readAll()].filter(
      ({ event }) => event.execution_id === executionId && event.source.kind === 'runtime',
    );
    assert.deepEqual(
      journaled.map(({ event }) => [event.event_type, event.source_native_id]),
      restarted.observations.map((observation) => [observation.type, observation.native_event_id]),
    );
    assert.equal(second.controlPlane.projection.workstream(workstreamId)?.status, 'completed');
    await second.controlPlane.close();
  });
});
