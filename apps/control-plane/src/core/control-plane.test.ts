import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { after, describe, test } from 'node:test';
import type {
  EventEnvelope,
  ExecutionId,
  Principal,
  RuntimeDescriptor,
  RuntimeId,
  WorkstreamId,
} from '@halcyonic/contracts';
import { MockRuntimeAdapter, parseScenario } from '@halcyonic/integration-mock';
import {
  type ObservationSink,
  type ReattachRequest,
  RuntimeActionError,
  type RuntimeAdapter,
  type RuntimeObservation,
  type StartExecutionRequest,
  type StartExecutionResult,
} from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { createUuidV7Generator } from '../ids.ts';
import { DeviceAccess } from '../network/devices.ts';
import { capturingLogger, createTestControlPlane, SCENARIOS } from '../testing/harness.ts';
import { controlPlaneDraft, NO_CAUSE } from './drafts.ts';

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

  test('a start that never asks reaches the runtime, and the view shows the level the runtime recorded (ADR 0043)', async () => {
    const harness = createTestControlPlane({
      adapters: (time) => [
        new MockRuntimeAdapter({
          scenarios: SCENARIOS,
          clock: time,
          scheduler: time,
          offersNeverAsk: true,
        }),
      ],
    });
    const { controlPlane, commands, time } = harness;
    const asking = await createWorkstream(harness);
    const quiet = await createWorkstream(harness);
    const start = (workstreamId: WorkstreamId, level: 'never_ask' | undefined) => {
      const command = commands.startExecution(workstreamId, FEATURE);
      return controlPlane.commands.submit(
        level === undefined
          ? command
          : { ...command, payload: { ...command.payload, approval_level: level } },
        'internal',
      );
    };
    assert.equal(start(quiet, 'never_ask').disposition, 'accepted');
    assert.equal(start(asking, undefined).disposition, 'accepted');
    await time.runUntilIdle();
    const levelOf = (workstreamId: WorkstreamId) =>
      controlPlane.projection.executions().find((each) => each.workstream_id === workstreamId)
        ?.approval_level;
    assert.equal(levelOf(quiet), 'never_ask');
    // A start that chose nothing asks, as before, and the runtime says so.
    assert.equal(levelOf(asking), 'ask');
    await controlPlane.close();
  });

  test("a turn's usage, as the runtime counts it, is journaled as reported and summed on the execution", async () => {
    const tokens = { input: 12, cache_read: 300, cache_write: 40, output: 30, reasoning: null };
    const used = parseScenario(
      {
        format: 1,
        id: 'used',
        description: 'Reports what its one turn used.',
        steps: [
          {
            after_ms: 10,
            emit: {
              type: 'runtime.usage.reported',
              payload: {
                tokens,
                cost: { estimated_usd: 0.02, prices: 'catalog' },
                counted: 'given',
              },
            },
          },
          { after_ms: 10, emit: { type: 'runtime.turn.completed', payload: {} } },
        ],
      },
      'used.json',
    );
    const harness = createTestControlPlane({
      adapters: (time) => [
        new MockRuntimeAdapter({
          scenarios: new Map([...SCENARIOS, ['used', used]]),
          clock: time,
          scheduler: time,
          reportsUsage: true,
        }),
      ],
    });
    const { controlPlane, commands, time, journal } = harness;
    const workstreamId = await createWorkstream(harness);
    const start = commands.startExecution(workstreamId, { ...FEATURE, scenario: 'used' });
    assert.equal(controlPlane.commands.submit(start, 'internal').disposition, 'accepted');
    await time.runUntilIdle();
    const reported = [...journal.readAll()]
      .map(({ event }) => event)
      .filter((event) => event.event_type === 'runtime.usage.reported');
    assert.equal(reported.length, 1);
    assert.equal(reported[0]?.provenance.epistemic, 'reported');
    const [execution] = controlPlane.projection.executions();
    assert.deepEqual(execution?.usage, {
      turns: 1,
      turns_ended: 1,
      tokens,
      cost: { estimated_usd: 0.02, prices: 'catalog' },
    });
    assert.equal(
      controlPlane.snapshot().runtimes.find((runtime) => runtime.runtime_id === 'mock')
        ?.reports_usage,
      true,
    );
    const project = controlPlane
      .snapshot()
      .projects.find((each) => each.project_id === execution?.project_id);
    assert.deepEqual(project?.usage, {
      turns: 1,
      turns_ended: 1,
      tokens,
      cost: { estimated_usd: 0.02, prices: 'catalog' },
    });
    await controlPlane.close();
  });

  test('a runtime that records no level shows none, and refuses a start that never asks (ADR 0043)', async () => {
    const harness = createTestControlPlane();
    const { controlPlane, commands, time } = harness;
    const workstreamId = await createWorkstream(harness);
    const command = commands.startExecution(workstreamId, FEATURE);
    const refused = controlPlane.commands.submit(
      { ...command, payload: { ...command.payload, approval_level: 'never_ask' } },
      'internal',
    );
    assert.equal(refused.command?.rejection?.code, 'capability_unsupported');
    const asking = commands.startExecution(workstreamId, FEATURE);
    assert.equal(controlPlane.commands.submit(asking, 'internal').disposition, 'accepted');
    await time.runUntilIdle();
    const [execution] = controlPlane.projection.executions();
    assert.ok(execution);
    // Not recorded is never shown as asking.
    assert.equal('approval_level' in execution, false);
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

  test('two starts of one task back to back: the second is refused, so one agent works in its folder (ADMIT)', async () => {
    const harness = createTestControlPlane();
    const { controlPlane, commands, time } = harness;
    const workstreamId = await createWorkstream(harness);
    const first = controlPlane.commands.submit(
      commands.startExecution(workstreamId, FEATURE),
      'internal',
    );
    const second = controlPlane.commands.submit(
      commands.startExecution(workstreamId, FEATURE),
      'internal',
    );
    assert.equal(first.disposition, 'accepted');
    assert.equal(second.disposition, 'rejected');
    assert.deepEqual(second.command?.rejection, {
      code: 'invalid_state',
      message: 'This task is starting. Stop it first, or start another task.',
    });
    await time.runUntilIdle();
    assert.equal(controlPlane.projection.executions().length, 1);
    // At rest, the task starts again.
    const again = controlPlane.commands.submit(
      commands.startExecution(workstreamId, FEATURE),
      'internal',
    );
    assert.equal(again.disposition, 'accepted');
    await time.runUntilIdle();
    assert.equal(controlPlane.projection.executions().length, 2);
    await controlPlane.close();
  });

  test('a second Send during a switch: the resting execution is not instructed beside the new one (ADMIT-2)', async () => {
    const harness = createTestControlPlane();
    const { controlPlane, commands, time } = harness;
    const workstreamId = await createWorkstream(harness);
    controlPlane.commands.submit(commands.startExecution(workstreamId, FEATURE), 'internal');
    await time.runUntilIdle();
    const resting = controlPlane.projection.workstream(workstreamId)?.current_execution_id;
    assert.ok(resting);
    assert.equal(controlPlane.projection.execution(resting)?.status, 'completed');

    const switched = controlPlane.commands.submit(
      commands.startExecution(workstreamId, APPROVAL),
      'internal',
    );
    assert.equal(switched.disposition, 'accepted');
    const refusal = (state: string) => ({
      code: 'invalid_state',
      message: `This task is ${state} in another execution. Stop it first, or instruct that one.`,
    });
    const during = controlPlane.commands.submit(commands.instruct(resting, 'Go on.'), 'internal');
    assert.equal(during.disposition, 'rejected');
    assert.deepEqual(during.command?.rejection, refusal('starting'));
    await time.runUntilIdle();
    const waiting = controlPlane.commands.submit(commands.instruct(resting, 'Go on.'), 'internal');
    assert.deepEqual(waiting.command?.rejection, refusal('waiting for you'));
    assert.equal(
      controlPlane.projection.execution(resting)?.status,
      'completed',
      'never set going',
    );
    await controlPlane.close();
  });

  test('a command from a device revoked since it authenticated is rejected and journaled', async () => {
    const { controlPlane, commands } = createTestControlPlane();
    const devices = new DeviceAccess({
      controlPlane,
      ids: createUuidV7Generator(),
      logger: capturingLogger().logger,
    });
    const { deviceId } = devices.pair('Headset', 'c'.repeat(64));
    const device = { kind: 'device', device_id: deviceId } as const;

    const before = controlPlane.commands.submit(commands.createProject('Before'), 'http', device);
    assert.equal(before.disposition, 'accepted');
    devices.revoke(deviceId, { kind: 'local' });

    const after = commands.createProject('After');
    const outcome = controlPlane.commands.submit(after, 'websocket', device);
    assert.equal(outcome.disposition, 'rejected');
    assert.equal(outcome.command?.rejection?.code, 'device_revoked');
    assert.equal(
      controlPlane.projection.projects().length,
      1,
      'the revoked device created nothing',
    );
    const rejected = [...controlPlane.journal.readAll()]
      .map((stored) => stored.event)
      .find(
        (event) =>
          event.event_type === 'command.rejected' &&
          event.payload.command.command_id === after.command_id,
      );
    assert.ok(rejected?.event_type === 'command.rejected');
    assert.deepEqual(rejected.payload.principal, device);
    assert.equal(rejected.payload.received_via, 'websocket');

    const unknown = { kind: 'device', device_id: createUuidV7Generator().next() } as Principal;
    const stranger = controlPlane.commands.submit(
      commands.createProject('Stranger'),
      'http',
      unknown,
    );
    assert.equal(stranger.command?.rejection?.code, 'device_revoked');
    const local = controlPlane.commands.submit(commands.createProject('Owner'), 'http', {
      kind: 'local',
    });
    assert.equal(local.disposition, 'accepted', 'the owner is not affected');
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
      { ...command, payload: { name: 'Twice', location: null } },
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

  test('a refused answer is journaled without what was chosen or typed; resubmitting it is a duplicate', async () => {
    const path = join(directory, 'refused-answer.db');
    const harness = createTestControlPlane({ path });
    const { controlPlane, commands, time } = harness;
    const asking = { ...FEATURE, scenario: 'question_asked' };
    const workstreamId = await createWorkstream(harness, asking);
    controlPlane.commands.submit(commands.startExecution(workstreamId, asking), 'internal');
    await time.runUntilIdle();
    const executionId = controlPlane.projection.workstream(workstreamId)?.current_execution_id;
    assert.ok(executionId !== null && executionId !== undefined);
    const answer = (answers: unknown) =>
      ({
        ...commands.interrupt(executionId),
        command_type: 'execution.answer_question',
        payload: { execution_id: executionId, question_id: 'question-1', answers },
      }) as never;
    // q1 is left unanswered, so the whole answer is refused.
    const refused = answer([{ key: 'q0', selected: [], text: 'my password is hunter2' }]);
    const first = controlPlane.commands.submit(refused, 'internal');
    assert.equal(first.disposition, 'rejected');
    assert.equal(first.command?.rejection?.code, 'invalid_answer');
    const journal = JSON.stringify([...controlPlane.journal.readAll()]);
    assert.equal(journal.includes('hunter2'), false);
    const rejection = [...controlPlane.journal.readAll()].find(
      (stored) => stored.event.event_type === 'command.rejected',
    )?.event;
    assert.deepEqual(
      rejection?.event_type === 'command.rejected' &&
        rejection.payload.command.command_type === 'execution.answer_question' &&
        rejection.payload.command.payload.answers,
      [{ key: 'q0', selected: [], text: null }],
    );
    const head = controlPlane.journal.head();
    assert.equal(controlPlane.commands.submit(refused, 'internal').disposition, 'duplicate');
    // The same id with another answer is a conflict, though the journal kept neither text.
    const reused = {
      ...(refused as { payload: object }),
      payload: {
        ...(refused as { payload: object }).payload,
        answers: [{ key: 'q0', selected: [], text: 'my password is hunter3' }],
      },
    } as never;
    assert.equal(controlPlane.commands.submit(reused, 'internal').disposition, 'conflict');
    assert.equal(controlPlane.journal.head(), head);
    const answered = controlPlane.commands.submit(
      answer([
        { key: 'q0', selected: ['Dark'], text: null },
        { key: 'q1', selected: ['Orders'], text: null },
      ]),
      'internal',
    );
    assert.equal(answered.disposition, 'accepted');
    await time.runUntilIdle();
    assert.equal(controlPlane.projection.workstream(workstreamId)?.status, 'completed');
    await controlPlane.close();

    // After a restart nothing tells a refused answer sent again from another one: a conflict.
    const restarted = createTestControlPlane({ path });
    assert.equal(
      restarted.controlPlane.commands.submit(refused, 'internal').disposition,
      'conflict',
    );
    await restarted.controlPlane.close();
  });

  test('only who sent a refused answer learns whether a resend matches it, a few times a minute', async () => {
    const harness = createTestControlPlane();
    const { controlPlane, commands, time } = harness;
    const asking = { ...FEATURE, scenario: 'question_asked' };
    const workstreamId = await createWorkstream(harness, asking);
    controlPlane.commands.submit(commands.startExecution(workstreamId, asking), 'internal');
    await time.runUntilIdle();
    const executionId = controlPlane.projection.workstream(workstreamId)?.current_execution_id;
    assert.ok(executionId !== null && executionId !== undefined);
    const base = commands.interrupt(executionId);
    const answer = (pin: string) =>
      ({
        ...base,
        command_type: 'execution.answer_question',
        payload: {
          execution_id: executionId,
          question_id: 'question-1',
          answers: [{ key: 'q0', selected: [], text: pin }],
        },
      }) as never;
    const local: Principal = { kind: 'local' };
    const other = { kind: 'device', device_id: createUuidV7Generator().next() } as Principal;
    const submit = (pin: string, principal: Principal) =>
      controlPlane.commands.submit(answer(pin), 'websocket', principal).disposition;
    // Refused: the second question is left unanswered.
    assert.equal(submit('4821', local), 'rejected');
    const head = controlPlane.journal.head();
    // Another principal learns nothing: a correct guess is a conflict like a wrong one.
    assert.equal(submit('4821', other), 'conflict');
    assert.equal(submit('0000', other), 'conflict');
    // Who sent it still gets a duplicate for the same answer, and a conflict for another.
    assert.equal(submit('4821', local), 'duplicate');
    assert.equal(submit('1234', local), 'conflict');
    // A few comparisons a minute: past them, even the same answer is a conflict until the next.
    for (let resend = 0; resend < 4; resend += 1) submit('1111', local);
    assert.equal(submit('4821', local), 'conflict');
    await time.advance(60_000);
    assert.equal(submit('4821', local), 'duplicate');
    assert.equal(controlPlane.journal.head(), head, 'no resend is journaled');
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
    // The mock cannot steer: an instruction while the turn runs waits for it (ADR 0056), and is
    // not sent to the runtime then.
    const whileRunning = controlPlane.commands.submit(instruct('Also check lint.'), 'internal');
    assert.equal(whileRunning.disposition, 'accepted');
    assert.equal(whileRunning.command?.status, 'accepted');
    assert.deepEqual(
      controlPlane.projection
        .execution(executionId)
        ?.queued_instructions?.map((queued) => queued.command_id),
      [whileRunning.command?.command_id],
    );
    await time.runUntilIdle();
    const atRest = controlPlane.commands.submit(
      instruct('Look at the three failures.'),
      'internal',
    );
    assert.equal(atRest.disposition, 'accepted');
    await time.runUntilIdle();
    const execution = controlPlane.projection.execution(executionId);
    // The first turn, the waiting instruction's turn after it, and the one sent at rest.
    assert.equal(execution?.turn_count, 3);
    assert.equal(execution?.status, 'completed');
    assert.equal(
      controlPlane.projection.command(whileRunning.command?.command_id ?? '')?.status,
      'completed',
    );
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
          model_ref: null,
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

  test('every runtime command stays accepted until its runtime confirms, and fails with an unknown effect when it never does (#12)', async () => {
    const commandTypes = [
      'execution.send_instruction',
      'execution.respond_to_approval',
      'execution.answer_question',
      'execution.interrupt',
    ] as const;
    for (const hanging of commandTypes) {
      let emit: ((observation: RuntimeObservation) => void) | undefined;
      const say = (type: string, payload: unknown) =>
        emit?.({
          type,
          payload,
          occurred_at: '2026-09-26T10:00:00.000Z',
          native_event_id: null,
          sequence: null,
          provenance: { epistemic: 'observed', native_type: null },
        } as RuntimeObservation);
      // A runtime that takes every action and never says whether it did.
      const never = () => new Promise<never>(() => {});
      const base = stubRuntime('silent', async (request) => {
        emit = request.emit;
        say('runtime.execution.started', { native_id: 'native-silent' });
        return { native_id: 'native-silent' };
      });
      const silent: RuntimeAdapter = {
        ...base,
        descriptor: {
          ...base.descriptor,
          capabilities: {
            start_execution: true,
            instruct_at_rest: true,
            instruct_while_running: false,
            respond_to_approval: true,
            answer_question: true,
            interrupt: true,
          },
        },
        sendInstruction: never,
        respondToApproval: never,
        answerQuestion: never,
        interrupt: never,
      };
      const harness = createTestControlPlane({ commandTimeoutMs: 5000, adapters: () => [silent] });
      const { controlPlane, commands, time } = harness;
      const workstreamId = await createWorkstream(harness);
      controlPlane.commands.submit(
        {
          ...commands.startExecution(workstreamId, FEATURE),
          payload: {
            workstream_id: workstreamId,
            runtime_id: 'silent' as RuntimeId,
            instruction: 'Go.',
            options: {},
            model_ref: null,
          },
        },
        'internal',
      );
      await time.runUntilIdle();
      const executionId = controlPlane.projection.workstream(workstreamId)
        ?.current_execution_id as ExecutionId;

      // The state each command needs, as the runtime reports it.
      say('runtime.turn.started', { turn_id: 't1' });
      if (hanging === 'execution.send_instruction')
        say('runtime.turn.completed', { turn_id: 't1' });
      if (hanging === 'execution.respond_to_approval')
        say('runtime.approval.requested', {
          approval_id: 'a1',
          subject: { kind: 'tool_use', tool_name: 'bash', summary: 'Run it' },
          complete: true,
        });
      if (hanging === 'execution.answer_question')
        say('runtime.question.asked', {
          question_id: 'q1',
          prompts: [
            {
              key: 'k',
              header: null,
              text: 'Which one?',
              options: [],
              multiple: false,
              free_text: true,
              secret: false,
            },
          ],
          answerable: true,
        });
      const command =
        hanging === 'execution.send_instruction'
          ? commands.instruct(executionId, 'Go on.')
          : hanging === 'execution.respond_to_approval'
            ? commands.approve(executionId, 'a1')
            : hanging === 'execution.answer_question'
              ? commands.answerQuestion(executionId, 'q1', [
                  { key: 'k', selected: [], text: 'This one.' },
                ])
              : commands.interrupt(executionId);
      const submitted = controlPlane.commands.submit(command, 'internal');
      assert.equal(submitted.disposition, 'accepted', hanging);

      await time.advance(4999);
      const pending = controlPlane.projection.command(command.command_id);
      assert.equal(pending?.status, 'accepted', `${hanging}: accepted is not success`);
      await time.advance(1);
      const failed = controlPlane.projection.command(command.command_id);
      assert.deepEqual(
        { status: failed?.status, code: failed?.failure?.code, effect: failed?.failure?.effect },
        { status: 'failed', code: 'timeout', effect: 'unknown' },
        hanging,
      );
      await controlPlane.close();
    }
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
          model_ref: null,
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

  test("an adapter's unexpected error is journaled as its type, never its words, which can quote the instruction", async () => {
    const instruction = 'PRIVATE instruction text';
    for (const [name, failing] of [
      // A parse error quotes what it read.
      ['parsing', (request: StartExecutionRequest) => JSON.parse(request.instruction) as never],
      [
        'echoing',
        (request: StartExecutionRequest) => {
          throw new Error(request.instruction);
        },
      ],
    ] as const) {
      const harness = createTestControlPlane({
        adapters: () => [stubRuntime(name, async (request) => failing(request))],
      });
      const { controlPlane, commands, time, journal } = harness;
      const workstreamId = await createWorkstream(harness);
      controlPlane.commands.submit(
        {
          ...commands.startExecution(workstreamId, FEATURE),
          payload: {
            workstream_id: workstreamId,
            runtime_id: name as RuntimeId,
            instruction,
            options: {},
            model_ref: null,
          },
        },
        'internal',
      );
      await time.runUntilIdle();
      const failures = [...journal.readAll()].filter((stored) =>
        ['command.failed', 'execution.start_failed', 'execution.state_unknown'].includes(
          stored.event.event_type,
        ),
      );
      assert.ok(failures.length > 0, name);
      for (const stored of failures) {
        const text = JSON.stringify(stored.event);
        assert.ok(!text.includes('PRIVATE'), `${name}: ${stored.event.event_type}`);
        assert.match(
          text,
          /The runtime adapter failed unexpectedly \((SyntaxError|Error)\)\./,
          name,
        );
      }
      await controlPlane.close();
    }
  });

  test("a runtime's refusal loses what Halcyonic holds and credential shapes, and keeps the rest word for word", async () => {
    const held = 'my-gateway-secret-42';
    const instruction = 'Add a login page to the settings screen';
    const harness = createTestControlPlane({
      secrets: () => [{ what: 'GATEWAY_KEY', value: held }],
      adapters: () => [
        stubRuntime('gateway', async (request) => {
          throw new RuntimeActionError(
            'runtime_unavailable',
            `unexpected status 401: invalid key sk-proj-AbCdEf0123456789xyzQRS for ${held}; the request was "${request.instruction}"`,
          );
        }),
      ],
    });
    const { controlPlane, commands, time, journal } = harness;
    const workstreamId = await createWorkstream(harness);
    controlPlane.commands.submit(
      {
        ...commands.startExecution(workstreamId, FEATURE),
        payload: {
          workstream_id: workstreamId,
          runtime_id: 'gateway' as RuntimeId,
          instruction,
          options: {},
          model_ref: null,
        },
      },
      'internal',
    );
    await time.runUntilIdle();
    const failures = [...journal.readAll()].filter((stored) =>
      ['command.failed', 'execution.start_failed'].includes(stored.event.event_type),
    );
    assert.ok(failures.length > 0);
    for (const stored of failures) {
      const text = JSON.stringify(stored.event);
      assert.ok(!text.includes(held) && !text.includes('sk-proj'), text);
      assert.ok(
        text.includes(
          `unexpected status 401: invalid key [redacted] for [redacted: GATEWAY_KEY]; the request was \\"${instruction}\\"`,
        ),
        text,
      );
    }
    await controlPlane.close();
  });

  test("an adapter's bug is logged with its type and frames, for the owner to find", async () => {
    const { logger, entries } = capturingLogger();
    const harness = createTestControlPlane({
      logger,
      adapters: () => [
        stubRuntime('buggy', async (request) => JSON.parse(request.instruction) as never),
      ],
    });
    const { controlPlane, commands, time } = harness;
    const workstreamId = await createWorkstream(harness);
    controlPlane.commands.submit(
      {
        ...commands.startExecution(workstreamId, FEATURE),
        payload: {
          workstream_id: workstreamId,
          runtime_id: 'buggy' as RuntimeId,
          instruction: 'PRIVATE words',
          options: {},
          model_ref: null,
        },
      },
      'internal',
    );
    await time.runUntilIdle();
    const logged = entries.find((entry) => entry.message === 'runtime adapter failed unexpectedly');
    assert.ok(logged, 'the bug leaves a trace on the Mac');
    assert.ok(
      (logged.context as { err?: unknown }).err instanceof SyntaxError,
      'the error itself, for the logger to serialize',
    );
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
          model_ref: null,
        },
      },
      'internal',
    );
    await time.runUntilIdle();
    assert.ok(!types(controlPlane.journal.readAll()).includes('runtime.agent_message'));
    assert.ok(entries.some((entry) => entry.message === 'runtime observation rejected'));
    await controlPlane.close();
  });

  test('a native id reused for another execution or event type is dropped with a warning; a re-delivery is not', async () => {
    const { logger, entries } = capturingLogger();
    const sinks: ObservationSink[] = [];
    const harness = createTestControlPlane({
      logger,
      adapters: () => [
        stubRuntime('reusing', async (request) => {
          sinks.push(request.emit);
          return { native_id: null };
        }),
      ],
    });
    const { controlPlane, commands, time } = harness;
    const start = async (): Promise<ExecutionId> => {
      const workstreamId = await createWorkstream(harness);
      controlPlane.commands.submit(
        {
          ...commands.startExecution(workstreamId, FEATURE),
          payload: {
            workstream_id: workstreamId,
            runtime_id: 'reusing' as RuntimeId,
            instruction: 'Go.',
            options: {},
            model_ref: null,
          },
        },
        'internal',
      );
      await time.runUntilIdle();
      const executionId = controlPlane.projection.workstream(workstreamId)?.current_execution_id;
      assert.ok(executionId);
      return executionId;
    };
    const firstExecution = await start();
    const secondExecution = await start();
    const [first, second] = sinks;
    assert.ok(first && second);
    const started: RuntimeObservation = {
      type: 'runtime.execution.started',
      occurred_at: time.now().toISOString(),
      native_event_id: 'native-1',
      sequence: 1,
      provenance: { epistemic: 'observed', native_type: null },
      payload: { native_id: null },
    };
    const reuses = () =>
      entries.filter(
        (entry) =>
          entry.level === 'warn' &&
          entry.message === 'runtime reused a native event id; the event was not journaled',
      );

    first(started);
    first(started);
    assert.equal(reuses().length, 0, 'the same record delivered again is not a reuse');
    assert.ok(entries.some((entry) => entry.message === 'duplicate event ignored'));

    second(started);
    first({
      type: 'runtime.agent_message',
      occurred_at: time.now().toISOString(),
      native_event_id: 'native-1',
      sequence: 2,
      provenance: { epistemic: 'reported', native_type: null },
      payload: { text: 'Private agent text.' },
    });

    const journaled = [...controlPlane.journal.readAll()].filter(
      ({ event }) => event.source_native_id === 'native-1',
    );
    assert.deepEqual(
      journaled.map(({ event }) => [event.execution_id, event.event_type]),
      [[firstExecution, 'runtime.execution.started']],
    );
    const stored = journaled[0];
    assert.ok(stored);
    const reported = reuses().map(({ context }) => {
      const { event_id, ...identifiers } = context as { event_id?: unknown };
      assert.ok(typeof event_id === 'string' && event_id !== stored.event.event_id);
      return identifiers;
    });
    const existing = {
      runtime_id: 'reusing',
      source_native_id: 'native-1',
      existing_event_id: stored.event.event_id,
      existing_event_type: 'runtime.execution.started',
      existing_execution_id: firstExecution,
      existing_position: stored.position,
    };
    assert.deepEqual(reported, [
      { ...existing, event_type: 'runtime.execution.started', execution_id: secondExecution },
      { ...existing, event_type: 'runtime.agent_message', execution_id: firstExecution },
    ]);
    assert.ok(!JSON.stringify(entries).includes('Private agent text.'), 'no payload text logged');
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

/**
 * A runtime that keeps its sessions after its processes end (ADR 0046): a start reports a session
 * and a turn that ends only when the instruction says `Finish`; a re-attach is recorded, and
 * answered as `answer` says.
 */
class ResumableRuntime implements RuntimeAdapter {
  readonly descriptor: RuntimeDescriptor = {
    runtime_id: 'resumable' as RuntimeId,
    kind: 'stub',
    display_name: 'Resumable stub',
    synthetic: true,
    capabilities: {
      start_execution: true,
      instruct_at_rest: true,
      instruct_while_running: false,
      respond_to_approval: false,
      answer_question: false,
      interrupt: false,
    },
    model_choice: 'none',
    uses_project_location: false,
    reports_tool_activity: false,
    resumes_after_restart: true,
  };
  readonly reattached: Omit<ReattachRequest, 'emit'>[] = [];
  readonly instructions: string[] = [];
  readonly #emits = new Map<string, ObservationSink>();
  readonly #answer: 'restore' | 'refuse' | 'hang';

  constructor(answer: 'restore' | 'refuse' | 'hang' = 'restore') {
    this.#answer = answer;
  }

  validateStartOptions() {
    return { ok: true } as const;
  }

  async startExecution(request: StartExecutionRequest): Promise<StartExecutionResult> {
    this.#emits.set(request.execution.execution_id, request.emit);
    const at = new Date().toISOString() as never;
    const observed = { epistemic: 'observed', native_type: null } as const;
    const native = `native-${request.execution.execution_id}`;
    request.emit({
      type: 'runtime.execution.started',
      occurred_at: at,
      native_event_id: `${native}-s`,
      sequence: null,
      provenance: observed,
      payload: { native_id: native },
    });
    request.emit({
      type: 'runtime.turn.started',
      occurred_at: at,
      native_event_id: `${native}-t1`,
      sequence: null,
      provenance: observed,
      payload: { turn_id: 't1' },
    });
    if (request.instruction.includes('Finish')) {
      request.emit({
        type: 'runtime.turn.completed',
        occurred_at: at,
        native_event_id: `${native}-t1c`,
        sequence: null,
        provenance: observed,
        payload: { turn_id: 't1' },
      });
    }
    return { native_id: native };
  }

  async reattach(request: ReattachRequest): Promise<void> {
    const { emit, ...rest } = request;
    this.reattached.push(rest);
    if (this.#answer === 'hang') return new Promise(() => {});
    if (this.#answer === 'refuse') {
      throw new RuntimeActionError('session_not_found', 'The runtime has no such session.');
    }
    this.#emits.set(request.execution.execution_id, emit);
    const at = new Date().toISOString() as never;
    const id = request.native_id;
    emit({
      type: 'runtime.connection.restored',
      occurred_at: at,
      native_event_id: `${id}-r${this.reattached.length}`,
      sequence: null,
      provenance: { epistemic: 'observed', native_type: null },
      payload: { reason: 'Found again after the restart.' },
    });
    if (request.cut_turn !== null) {
      emit({
        type: 'runtime.turn.interrupted',
        occurred_at: at,
        native_event_id: `${id}-cut`,
        sequence: null,
        provenance: { epistemic: 'inferred', native_type: null, rule: 'stub.restart.turn_status' },
        payload: { turn_id: request.cut_turn.turn_id },
      });
    }
  }

  async sendInstruction(request: {
    execution: { execution_id: string };
    text: string;
  }): Promise<void> {
    if (!this.#emits.has(request.execution.execution_id)) {
      throw new RuntimeActionError('execution_unknown_to_runtime', 'No session for it.');
    }
    this.instructions.push(request.text);
  }

  async close(): Promise<void> {}
}

function startOn(
  harness: ReturnType<typeof createTestControlPlane>,
  workstreamId: WorkstreamId,
  runtimeId: string,
  instruction = 'Go.',
) {
  return harness.controlPlane.commands.submit(
    {
      ...harness.commands.startExecution(workstreamId, FEATURE),
      payload: {
        workstream_id: workstreamId,
        runtime_id: runtimeId as RuntimeId,
        instruction,
        options: { lane: 'core' },
        model_ref: null,
      },
    },
    'internal',
  );
}

function stateUnknownCodes(
  harness: ReturnType<typeof createTestControlPlane>,
  executionId: string,
) {
  return [...harness.journal.readAll()].flatMap(({ event }) =>
    event.event_type === 'execution.state_unknown' && event.execution_id === executionId
      ? [event.payload.code]
      : [],
  );
}

describe('closing (core audit 2026-11-03)', () => {
  test('an event that arrives after the journal closed is refused quietly, never thrown at the late caller', async () => {
    const { logger, entries } = capturingLogger();
    const harness = createTestControlPlane({ logger });
    await harness.controlPlane.close();
    const late = harness.controlPlane.recorder.record(
      controlPlaneDraft(
        'project.created',
        {
          project_id: '01920000-0000-7000-8000-000000000901' as never,
          workstream_id: null,
          execution_id: null,
        },
        { name: 'Late', location: null },
        '2026-11-11T00:00:00.000Z' as never,
        NO_CAUSE,
      ),
    );
    assert.deepEqual(late, { status: 'closed' });
    assert.ok(
      entries.some((entry) => entry.message === 'event after the journal closed was not recorded'),
    );
  });

  test("the busy rule says a task's status in words", async () => {
    const harness = createTestControlPlane();
    const workstreamId = await createWorkstream(harness, APPROVAL);
    harness.controlPlane.commands.submit(
      harness.commands.startExecution(workstreamId, APPROVAL),
      'internal',
    );
    await harness.time.advance(10_000);
    const project = harness.controlPlane.projection.workstream(workstreamId)?.project_id ?? '';
    assert.equal(
      harness.controlPlane.projection.projectBusy(project),
      'A task in this project is waiting for you.',
    );
    await harness.controlPlane.close();
  });
});

describe('restart', () => {
  test('a journal written before principals were recorded, at schema version 2, opens and replays', async () => {
    const path = join(directory, 'before-principals.db');
    const first = createTestControlPlane({ path });
    const workstreamId = await createWorkstream(first, FEATURE);
    first.controlPlane.commands.submit(
      first.commands.startExecution(workstreamId, FEATURE),
      'internal',
    );
    await first.time.runUntilIdle();
    const before = first.controlPlane.snapshot();
    await first.controlPlane.close();

    // What a build of schema version 2 wrote: the same events, command events without a principal.
    const db = new DatabaseSync(path);
    db.exec(
      `UPDATE events SET envelope = json_remove(envelope, '$.payload.principal')
         WHERE event_type IN ('command.accepted', 'command.rejected')`,
    );
    db.exec('PRAGMA user_version = 2');
    db.close();

    const second = createTestControlPlane({ path, time: first.time });
    assert.deepEqual(second.controlPlane.snapshot(), before);
    const principals = [...second.journal.readAll()].flatMap((stored) =>
      stored.event.event_type === 'command.accepted' ||
      stored.event.event_type === 'command.rejected'
        ? [stored.event.payload.principal]
        : [],
    );
    assert.ok(principals.length >= 3, 'the project, the workstream and the start');
    assert.ok(principals.every((principal) => principal === null));
    await second.controlPlane.close();
  });

  test('a journal written before projects had folders, at schema version 3, opens and replays', async () => {
    const path = join(directory, 'before-locations.db');
    const first = createTestControlPlane({ path });
    const workstreamId = await createWorkstream(first, FEATURE);
    first.controlPlane.commands.submit(
      first.commands.startExecution(workstreamId, FEATURE),
      'internal',
    );
    await first.time.runUntilIdle();
    const before = first.controlPlane.snapshot();
    await first.controlPlane.close();

    // What a build of schema version 3 wrote: no location on projects or their commands, and no
    // directory on executions.
    const db = new DatabaseSync(path);
    db.exec(
      `UPDATE events SET envelope = json_remove(envelope, '$.payload.location')
         WHERE event_type = 'project.created'`,
    );
    db.exec(
      `UPDATE events SET envelope = json_remove(envelope, '$.payload.command.payload.location')
         WHERE event_type IN ('command.accepted', 'command.rejected')`,
    );
    db.exec(
      `UPDATE events SET envelope = json_remove(envelope, '$.payload.directory')
         WHERE event_type = 'execution.created'`,
    );
    db.exec('PRAGMA user_version = 3');
    db.close();

    const second = createTestControlPlane({ path, time: first.time });
    assert.deepEqual(second.controlPlane.snapshot(), before);
    assert.ok(
      second.controlPlane.snapshot().projects.every((project) => project.location === null),
    );
    assert.ok(
      second.controlPlane.snapshot().executions.every((execution) => execution.directory === null),
    );
    await second.controlPlane.close();
  });

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
    // A task of its own: one task runs one execution at a time (ADMIT).
    const stuckWorkstreamId = await createWorkstream(first, FEATURE);
    const stuck = first.controlPlane.commands.submit(
      {
        ...first.commands.startExecution(stuckWorkstreamId, FEATURE),
        payload: {
          workstream_id: stuckWorkstreamId,
          runtime_id: 'stuck' as RuntimeId,
          instruction: 'Go.',
          options: {},
          model_ref: null,
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
    const cut = second.controlPlane.snapshot();
    assert.ok(cut.executions.every((execution) => execution.status === 'unknown'));
    assert.ok(
      cut.executions.every(
        (execution) => execution.status_reason?.code === 'control_plane_restarted',
      ),
    );
    // Neither runtime keeps a session after a restart (ADR 0046): each is said plainly to be lost.
    await second.controlPlane.reattached;
    const after = second.controlPlane.snapshot();
    assert.ok(after.executions.every((execution) => execution.status === 'unknown'));
    assert.ok(
      after.executions.every((execution) => execution.status_reason?.code === 'session_lost'),
    );
    const pending = second.controlPlane.projection.command(stuck.command?.command_id ?? '');
    assert.deepEqual(
      { status: pending?.status, effect: pending?.failure?.effect },
      { status: 'failed', effect: 'unknown' },
    );
    await second.controlPlane.close();
  });

  test('a task already unknown before a restart reads unknown because of it, and holds its project busy no longer (U5)', async () => {
    const path = join(directory, 'restart-unknown.db');
    const first = createTestControlPlane({
      path,
      adapters: (time) => [
        new MockRuntimeAdapter({ scenarios: SCENARIOS, clock: time, scheduler: time }),
        stubRuntime('stuck', () => new Promise(() => {})),
      ],
    });
    // One whose runtime became unreachable, and one whose start was never confirmed.
    const lost = await createWorkstream(first, { ...FEATURE, scenario: 'agent_disconnect' });
    first.controlPlane.commands.submit(
      first.commands.startExecution(lost, { ...FEATURE, scenario: 'agent_disconnect' }),
      'internal',
    );
    const stuck = await createWorkstream(first);
    first.controlPlane.commands.submit(
      {
        ...first.commands.startExecution(stuck, FEATURE),
        payload: {
          workstream_id: stuck,
          runtime_id: 'stuck' as RuntimeId,
          instruction: 'Go.',
          options: {},
          model_ref: null,
        },
      },
      'internal',
    );
    await first.time.advance(40_000);
    const before = first.controlPlane.snapshot().executions;
    assert.deepEqual(before.map((execution) => execution.status_reason?.code).sort(), [
      'runtime_connection_lost',
      'start_outcome_unknown',
    ]);
    // Before the restart either may still be working, so its project is busy.
    for (const execution of before)
      assert.notEqual(first.controlPlane.projection.projectBusy(execution.project_id), null);
    await first.controlPlane.close();

    const second = createTestControlPlane({ path, time: first.time });
    second.controlPlane.reconcile();
    await second.controlPlane.reattached;
    // Neither runtime keeps a session after a restart, and the start stops what a crash left behind.
    for (const execution of before) {
      const after = second.controlPlane.projection.execution(execution.execution_id);
      assert.equal(after?.status, 'unknown');
      assert.equal(after?.status_reason?.code, 'session_lost');
      assert.equal(second.controlPlane.projection.projectBusy(execution.project_id), null);
    }
    await second.controlPlane.close();
  });

  test('work a restart cut is re-attached with the bounds it started with, its cut turn ended, and takes instructions again (ADR 0046)', async () => {
    const path = join(directory, 'restart-reattach.db');
    const first = createTestControlPlane({ path, adapters: () => [new ResumableRuntime()] });
    const workstreamId = await createWorkstream(first);
    startOn(first, workstreamId, 'resumable');
    await first.time.runUntilIdle();
    const executionId =
      first.controlPlane.projection.workstream(workstreamId)?.current_execution_id;
    assert.ok(executionId);
    assert.equal(first.controlPlane.projection.execution(executionId)?.status, 'running');
    await first.controlPlane.close();

    const runtime = new ResumableRuntime();
    const second = createTestControlPlane({ path, time: first.time, adapters: () => [runtime] });
    second.controlPlane.reconcile();
    assert.equal(second.controlPlane.projection.execution(executionId)?.status, 'unknown');
    await second.controlPlane.reattached;
    const { project_id } = second.controlPlane.projection.execution(executionId) ?? {};
    assert.deepEqual(runtime.reattached, [
      {
        execution: { execution_id: executionId, workstream_id: workstreamId, project_id },
        native_id: `native-${executionId}`,
        options: { lane: 'core' },
        model_ref: null,
        confined: false,
        directory: null,
        cut_turn: { turn_id: 't1' },
        last_sequence: null,
      },
    ]);
    const after = second.controlPlane.projection.execution(executionId);
    assert.equal(after?.status, 'interrupted');
    assert.equal(second.controlPlane.projection.projectBusy(after?.project_id ?? ''), null);

    const sent = second.controlPlane.commands.submit(
      second.commands.instruct(executionId as ExecutionId, 'Carry on.'),
      'internal',
    );
    await second.time.runUntilIdle();
    assert.equal(
      second.controlPlane.projection.command(sent.command?.command_id ?? '')?.status,
      'completed',
    );
    assert.deepEqual(runtime.instructions, ['Carry on.']);
    assert.equal(runtime.reattached.length, 1, 'once re-attached, not again');
    await second.controlPlane.close();
  });

  test('work at rest at a restart is re-attached by the next action on it, not at the start (ADR 0046)', async () => {
    const path = join(directory, 'restart-reattach-rest.db');
    const first = createTestControlPlane({ path, adapters: () => [new ResumableRuntime()] });
    const workstreamId = await createWorkstream(first);
    startOn(first, workstreamId, 'resumable', 'Finish.');
    await first.time.runUntilIdle();
    const executionId =
      first.controlPlane.projection.workstream(workstreamId)?.current_execution_id;
    assert.ok(executionId);
    assert.equal(first.controlPlane.projection.execution(executionId)?.status, 'completed');
    await first.controlPlane.close();

    const runtime = new ResumableRuntime();
    const second = createTestControlPlane({ path, time: first.time, adapters: () => [runtime] });
    second.controlPlane.reconcile();
    await second.controlPlane.reattached;
    assert.equal(runtime.reattached.length, 0);
    assert.equal(second.controlPlane.projection.execution(executionId)?.status, 'completed');

    const sent = second.controlPlane.commands.submit(
      second.commands.instruct(executionId as ExecutionId, 'One more thing.'),
      'internal',
    );
    await second.time.runUntilIdle();
    assert.equal(
      second.controlPlane.projection.command(sent.command?.command_id ?? '')?.status,
      'completed',
    );
    assert.equal(runtime.reattached.length, 1);
    assert.equal(runtime.reattached[0]?.cut_turn, null);
    assert.deepEqual(runtime.instructions, ['One more thing.']);
    await second.controlPlane.close();
  });

  test('a session the runtime cannot find again is lost: said plainly, never busy, and its next action fails having done nothing (ADR 0046)', async () => {
    const path = join(directory, 'restart-reattach-lost.db');
    const first = createTestControlPlane({ path, adapters: () => [new ResumableRuntime()] });
    const cutWorkstream = await createWorkstream(first);
    startOn(first, cutWorkstream, 'resumable');
    const restWorkstream = await createWorkstream(first);
    startOn(first, restWorkstream, 'resumable', 'Finish.');
    await first.time.runUntilIdle();
    await first.controlPlane.close();

    const runtime = new ResumableRuntime('refuse');
    const last = createTestControlPlane({ path, time: first.time, adapters: () => [runtime] });
    last.controlPlane.reconcile();
    await last.controlPlane.reattached;
    const cut = last.controlPlane.projection.workstream(cutWorkstream)?.current_execution_id ?? '';
    const rest =
      last.controlPlane.projection.workstream(restWorkstream)?.current_execution_id ?? '';
    const lost = last.controlPlane.projection.execution(cut);
    assert.equal(lost?.status, 'unknown');
    assert.equal(lost?.status_reason?.code, 'session_lost');
    assert.match(
      lost?.status_reason?.message ?? '',
      /could not be found again: The runtime has no such session\./,
    );
    assert.equal(last.controlPlane.projection.projectBusy(lost?.project_id ?? ''), null);

    const sent = last.controlPlane.commands.submit(
      last.commands.instruct(rest as ExecutionId, 'One more thing.'),
      'internal',
    );
    await last.time.runUntilIdle();
    const failed = last.controlPlane.projection.command(sent.command?.command_id ?? '');
    assert.deepEqual(
      { status: failed?.status, code: failed?.failure?.code, effect: failed?.failure?.effect },
      { status: 'failed', code: 'session_lost', effect: 'none' },
    );
    assert.deepEqual(runtime.instructions, []);
    assert.deepEqual(stateUnknownCodes(last, rest), ['session_lost']);
    assert.equal(last.controlPlane.projection.execution(rest)?.status_reason?.code, 'session_lost');
    // A later restart leaves lost work lost: it is not re-marked or re-attached.
    await last.controlPlane.close();
    const again = new ResumableRuntime();
    const fourth = createTestControlPlane({ path, time: first.time, adapters: () => [again] });
    fourth.controlPlane.reconcile();
    await fourth.controlPlane.reattached;
    assert.deepEqual(again.reattached, []);
    assert.deepEqual(stateUnknownCodes(fourth, cut), ['control_plane_restarted', 'session_lost']);
    await fourth.controlPlane.close();
  });

  test('work whose re-attach a second restart cut short is asked for again at the next start, never recorded twice (ADR 0046, AUDIT2)', async () => {
    for (const answer of ['restore', 'refuse'] as const) {
      const path = join(directory, `restart-reattach-twice-${answer}.db`);
      const first = createTestControlPlane({ path, adapters: () => [new ResumableRuntime()] });
      const workstreamId = await createWorkstream(first);
      startOn(first, workstreamId, 'resumable');
      await first.time.runUntilIdle();
      const executionId =
        first.controlPlane.projection.workstream(workstreamId)?.current_execution_id ?? '';
      await first.controlPlane.close();

      // The second start asks, and stops before the runtime answers.
      const silent = new ResumableRuntime('hang');
      const second = createTestControlPlane({ path, time: first.time, adapters: () => [silent] });
      second.controlPlane.reconcile();
      await second.time.advance(0);
      assert.equal(silent.reattached.length, 1);
      await second.controlPlane.close();

      const runtime = new ResumableRuntime(answer);
      const third = createTestControlPlane({ path, time: first.time, adapters: () => [runtime] });
      third.controlPlane.reconcile();
      await third.controlPlane.reattached;
      assert.equal(runtime.reattached.length, 1, 'the third start asks the runtime once');
      assert.deepEqual(runtime.reattached[0]?.cut_turn, { turn_id: 't1' });
      const after = third.controlPlane.projection.execution(executionId);
      if (answer === 'restore') assert.equal(after?.status, 'interrupted');
      else assert.equal(after?.status_reason?.code, 'session_lost');
      assert.deepEqual(
        stateUnknownCodes(third, executionId),
        answer === 'restore'
          ? ['control_plane_restarted']
          : ['control_plane_restarted', 'session_lost'],
      );
      await third.controlPlane.close();
    }
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
