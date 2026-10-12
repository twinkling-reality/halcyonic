import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import type {
  CommandId,
  CommandOf,
  ExecutionId,
  Principal,
  RuntimeId,
  WorkspaceAgentStatus,
} from '@halcyonic/contracts';
import type { ObservationSink, RuntimeAdapter, RuntimeObservation } from '@halcyonic/runtime-core';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { registerRoutes } from '../http/routes.ts';
import { createHttpServer } from '../http/server.ts';
import { createUuidV7Generator } from '../ids.ts';
import { DeviceAccess } from '../network/devices.ts';
import { capturingLogger, createTestControlPlane } from '../testing/harness.ts';
import type { ToolMessage, ToolTurnResult } from './ollama.ts';
import { WorkspaceRequests } from './requests.ts';

const unavailable = {
  availability: 'unavailable' as const,
  reason: { code: 'synthetic_unavailable', message: 'No provider in synthetic test.' },
};
const providers = {
  understanding: { understand: async () => unavailable },
  evaluation: { evaluate: async () => unavailable },
};
const available: WorkspaceAgentStatus = {
  availability: 'available',
  model: 'synthetic-model',
  served: 'this_mac',
};
const answer = (
  tools: { name: string; arguments: Record<string, unknown> }[] = [],
  text = 'Synthetic reported reply',
): ToolTurnResult => ({
  kind: 'answered',
  content: text,
  toolCalls: tools.map((value) => ({ function: value })),
  model: 'synthetic-model',
  served: 'this_mac',
});
const settle = async () => {
  for (let i = 0; i < 16; i++) await new Promise<void>((resolve) => setImmediate(resolve));
};
async function fixture(
  options: {
    path?: string;
    mode?: 'delayed' | 'immediate' | 'unknown';
    turn?: (messages: readonly ToolMessage[]) => Promise<ToolTurnResult>;
    understand?: () => Promise<typeof unavailable>;
  } = {},
) {
  let sink: ObservationSink | undefined,
    instructions = 0,
    turns = 0;
  const emit = (
    type: RuntimeObservation['type'],
    payload: object,
    sequence: number | null = null,
  ) => {
    sink?.({
      type,
      payload,
      occurred_at: '2026-09-26T10:00:00.000Z',
      native_event_id: null,
      sequence,
      provenance: {
        epistemic: type === 'runtime.agent_message' ? 'reported' : 'observed',
        native_type: null,
      },
    } as RuntimeObservation);
  };
  const adapter: RuntimeAdapter = {
    descriptor: {
      runtime_id: 'synthetic' as RuntimeId,
      kind: 'stub',
      display_name: 'Synthetic controllable runtime',
      synthetic: true,
      capabilities: {
        start_execution: true,
        instruct_at_rest: true,
        instruct_while_running: true,
        respond_to_approval: false,
        answer_question: false,
        interrupt: false,
      },
      model_choice: 'none',
      uses_project_location: false,
      reports_tool_activity: false,
    },
    validateStartOptions: () => ({ ok: true }),
    startExecution: async (request) => {
      sink = request.emit;
      emit('runtime.execution.started', { native_id: 'synthetic-session' });
      emit('runtime.turn.started', { turn_id: 'existing-turn' });
      return { native_id: 'synthetic-session' };
    },
    sendInstruction: async () => {
      instructions++;
      if (options.mode === 'immediate')
        emit('runtime.turn.completed', { turn_id: 'existing-turn' });
      if (options.mode === 'unknown') return new Promise(() => {});
    },
    close: async () => {},
  };
  const logs = capturingLogger(),
    h = createTestControlPlane({
      ...(options.path ? { path: options.path } : {}),
      adapters: () => [adapter],
      logger: logs.logger,
    });
  const ids = createUuidV7Generator({ now: () => h.time.now().getTime() }),
    model = {
      status: async () => available,
      turn: async (messages: readonly ToolMessage[]) => {
        turns++;
        return options.turn
          ? options.turn(messages)
          : turns === 1
            ? answer([
                {
                  name: 'continue_task',
                  arguments: { text: 'Continue the synthetic authorized task' },
                },
              ])
            : answer([], 'Reported after observed native outcome.');
      },
    };
  const coordinator = new WorkspaceRequests({
    controlPlane: h.controlPlane,
    ids,
    model,
    ...providers,
    ...(options.understand ? { understanding: { understand: options.understand } } : {}),
  });
  const scenario = DEMO_WORKSTREAMS[0];
  assert.ok(scenario);
  const p = h.controlPlane.commands.submit(h.commands.createProject('Synthetic'), 'internal');
  if (p.command?.result?.kind !== 'project_created') throw new Error('project missing');
  const w = h.controlPlane.commands.submit(
    h.commands.createWorkstream(p.command.result.project_id, scenario),
    'internal',
  );
  if (w.command?.result?.kind !== 'workstream_created') throw new Error('work missing');
  const projectId = p.command.result.project_id,
    workstreamId = w.command.result.workstream_id;
  h.controlPlane.commands.submit(
    {
      schema_version: 1,
      command_id: ids.next() as CommandId,
      issued_at: h.time.now().toISOString(),
      client: { name: 'synthetic', version: null, device_label: null },
      command_type: 'execution.start',
      payload: {
        workstream_id: workstreamId,
        runtime_id: 'synthetic' as RuntimeId,
        instruction: 'Synthetic setup',
        options: {},
        model_ref: null,
      },
    },
    'internal',
  );
  await settle();
  const execution = h.controlPlane.projection.workstream(w.command.result.workstream_id)
    ?.current_execution_id as ExecutionId;
  const command = (
    type: 'submit' | 'resume' | 'cancel',
    id?: string,
    text: string | null = 'Investigate and carry this task through',
  ): CommandOf<
    'workspace.request.submit' | 'workspace.request.resume' | 'workspace.request.cancel'
  > => ({
    schema_version: 1,
    command_id: ids.next() as CommandId,
    issued_at: h.time.now().toISOString(),
    client: { name: 'synthetic', version: null, device_label: null },
    ...(type === 'submit'
      ? {
          command_type: 'workspace.request.submit' as const,
          payload: {
            project_id: projectId,
            workstream_id: workstreamId,
            execution_id: execution,
            text: text ?? '',
          },
        }
      : type === 'resume'
        ? {
            command_type: 'workspace.request.resume' as const,
            payload: { request_id: id as never, text },
          }
        : {
            command_type: 'workspace.request.cancel' as const,
            payload: { request_id: id as never },
          }),
  });
  const submit = (principal: Principal = { kind: 'local' }) => {
    const outcome = h.controlPlane.commands.submit(command('submit'), 'http', principal);
    if (outcome.command?.result?.kind !== 'workspace_request_created')
      throw new Error('request missing');
    return outcome.command.result.request_id;
  };
  return {
    ...h,
    coordinator,
    model,
    logs,
    ids,
    emit,
    command,
    submit,
    get instructions() {
      return instructions;
    },
    get turns() {
      return turns;
    },
    async close() {
      coordinator.close();
      await h.controlPlane.close();
    },
  };
}

test('real pipeline waits after delivery, observes the already-active turn, then continues without model polling', async () => {
  const f = await fixture();
  try {
    const id = f.submit();
    await settle();
    let request = f.coordinator.read(id, { kind: 'local' });
    assert.equal(request?.status, 'waiting_for_runtime');
    assert.equal(f.instructions, 1);
    assert.equal(f.turns, 1);
    assert.equal(request?.steps[0]?.command_status, 'completed');
    assert.equal(request?.steps[0]?.active_turn_id, 'existing-turn');
    await settle();
    assert.equal(f.turns, 1, 'waiting does not call the model');
    f.emit('runtime.turn.completed', { turn_id: 'unrelated-turn' });
    await settle();
    assert.equal(f.turns, 1, 'a mismatched native turn is not completion evidence');
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' });
    await settle();
    request = f.coordinator.read(id, { kind: 'local' });
    assert.equal(request?.status, 'responded');
    assert.equal(request?.steps[0]?.status, 'confirmed');
    assert.equal(request?.reply?.epistemic, 'reported');
    assert.equal(f.turns, 2);
    assert.equal(f.instructions, 1);
    assert.equal(
      f.logs.entries.some((item) =>
        JSON.stringify(item).includes('Continue the synthetic authorized task'),
      ),
      false,
    );
  } finally {
    await f.close();
  }
});
test('synchronous native completion does not leave a false waiting state or require a new turn-start', async () => {
  const f = await fixture({ mode: 'immediate' });
  try {
    const id = f.submit();
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
    assert.equal(f.turns, 2);
    assert.equal(f.instructions, 1);
  } finally {
    await f.close();
  }
});
test('unsupported/coerced tools and model text cannot approve a human decision or complete runtime work', async () => {
  let turn = 0;
  const f = await fixture({
    turn: async () =>
      ++turn === 1
        ? answer([
            { name: 'execution.respond_to_approval', arguments: { decision: 'approve' } },
            { name: 'continue_task', arguments: { text: 7 } },
            { name: 'continue_task', arguments: { text: 'Synthetic', execution_id: 'foreign' } },
          ])
        : answer([], 'All work completed, according to the model.'),
  });
  try {
    const id = f.submit();
    await settle();
    const request = f.coordinator.read(id, { kind: 'local' });
    assert.equal(request?.status, 'responded');
    assert.equal(request?.steps.length, 3);
    assert.ok(request?.steps.every((step) => step.status === 'rejected'));
    assert.equal(f.instructions, 0);
    assert.equal(
      f.controlPlane.projection.execution(request?.execution_id ?? '')?.status,
      'running',
    );
  } finally {
    await f.close();
  }
});
test('cancellation while the model awaits prevents a late mutation and does not imply runtime interruption', async () => {
  let release: (value: ToolTurnResult) => void = () => {};
  const f = await fixture({
    turn: () =>
      new Promise((resolve) => {
        release = resolve;
      }),
  });
  try {
    const id = f.submit();
    await settle();
    f.controlPlane.commands.submit(f.command('cancel', id), 'http', { kind: 'local' });
    release(answer([{ name: 'continue_task', arguments: { text: 'Late instruction' } }]));
    await settle();
    assert.equal(f.instructions, 0);
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'stopped');
    assert.equal(
      f.controlPlane.projection.execution(
        f.controlPlane.projection.workspaceRequest(id)?.execution_id ?? '',
      )?.status,
      'running',
    );
  } finally {
    await f.close();
  }
});
test('revocation and exact owner checks remain enforced after an awaited model turn', async () => {
  let release: (value: ToolTurnResult) => void = () => {};
  const f = await fixture({
    turn: () =>
      new Promise((resolve) => {
        release = resolve;
      }),
  });
  try {
    const access = new DeviceAccess({
      controlPlane: f.controlPlane,
      ids: f.ids,
      logger: f.logs.logger,
    });
    const device = access.pair('Synthetic device', '0'.repeat(64));
    const owner: Principal = { kind: 'device', device_id: device.deviceId };
    const id = f.submit(owner);
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' }), undefined);
    assert.equal(
      f.controlPlane.commands.submit(f.command('resume', id), 'http', { kind: 'local' })
        .disposition,
      'rejected',
    );
    access.revoke(device.deviceId, { kind: 'local' });
    release(answer([{ name: 'continue_task', arguments: { text: 'Late after revoke' } }]));
    await settle();
    assert.equal(f.instructions, 0);
    assert.equal(f.controlPlane.projection.workspaceRequest(id)?.status, 'interrupted');
    assert.equal(f.coordinator.read(id, owner), undefined);
  } finally {
    await f.close();
  }
});
test('unknown delivery is retained through resume and never retried with another command identity', async () => {
  const f = await fixture({ mode: 'unknown' });
  try {
    const id = f.submit();
    await settle();
    await f.time.advance(30001);
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'waiting_for_human');
    const identity = f.controlPlane.projection.workspacePreparedActions(id)[0]?.command.command_id;
    f.controlPlane.commands.submit(f.command('resume', id), 'http', { kind: 'local' });
    await settle();
    assert.equal(f.instructions, 1);
    assert.equal(f.turns, 1);
    assert.equal(
      f.controlPlane.projection.workspacePreparedActions(id)[0]?.command.command_id,
      identity,
    );
  } finally {
    await f.close();
  }
});
test('restart interrupts journaled coordinator state and explicit resume discovers retained unknown actions', async () => {
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-request-restart-'));
  const path = join(directory, 'journal.db');
  const first = await fixture({ path, mode: 'unknown' });
  const id = first.submit();
  await settle();
  const identity =
    first.controlPlane.projection.workspacePreparedActions(id)[0]?.command.command_id;
  await first.close();
  const second = await fixture({ path });
  try {
    second.controlPlane.reconcile();
    assert.equal(second.coordinator.read(id, { kind: 'local' })?.status, 'interrupted');
    assert.equal(second.turns, 0);
    assert.equal(second.instructions, 0);
    assert.equal(
      second.controlPlane.projection.workspacePreparedActions(id)[0]?.command.command_id,
      identity,
    );
    // Its original execution is retained, while setup created another task; resume still uses exact original scope.
    second.controlPlane.commands.submit(second.command('resume', id), 'http', { kind: 'local' });
    await settle();
    assert.equal(second.instructions, 0);
    assert.equal(second.turns, 0);
    assert.equal(second.coordinator.read(id, { kind: 'local' })?.status, 'waiting_for_human');
  } finally {
    await second.close();
    rmSync(directory, { recursive: true, force: true });
  }
});
test('authenticated routes expose truthful availability and exact request ownership, never newest guessing', async () => {
  const f = await fixture({ turn: async () => answer() });
  const app = await createHttpServer({ token: 'synthetic-access-token', logLevel: 'silent' });
  try {
    registerRoutes(app, f.controlPlane, { ...providers, workspaceRequests: f.coordinator });
    const id = f.submit();
    await settle();
    const status = await app.inject({
      method: 'GET',
      url: '/api/workspace/agent',
      headers: { host: '127.0.0.1:0', authorization: 'Bearer synthetic-access-token' },
    });
    assert.equal(status.statusCode, 200);
    assert.equal(status.json().availability, 'available');
    const read = await app.inject({
      method: 'GET',
      url: `/api/workspace/requests/${id}`,
      headers: { host: '127.0.0.1:0', authorization: 'Bearer synthetic-access-token' },
    });
    assert.equal(read.statusCode, 200);
    assert.equal(read.json().request.request_id, id);
    assert.equal(
      (
        await app.inject({
          method: 'GET',
          url: `/api/workspace/requests/${id}`,
          headers: { host: '127.0.0.1:0' },
        })
      ).statusCode,
      401,
    );
  } finally {
    await app.close();
    await f.close();
  }
});

test('actual empty native tool content dispatches through Recorder and clarification text remains reported until explicit resume', async () => {
  let turn = 0,
    resumed = false;
  const f = await fixture({
    turn: async (messages) => {
      turn++;
      if (turn === 1)
        return answer(
          [{ name: 'ask_person', arguments: { message: 'Which verification should I run?' } }],
          '',
        );
      resumed = messages.some(
        (message) =>
          message.role === 'user' && message.content === 'Run the focused synthetic checks.',
      );
      return answer([], 'Reported response after person direction.');
    },
  });
  try {
    const id = f.submit();
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'waiting_for_human');
    assert.equal(
      f.coordinator.read(id, { kind: 'local' })?.reply?.text,
      'Which verification should I run?',
    );
    f.emit('runtime.agent_message', { text: 'Unrelated report' });
    await settle();
    assert.equal(turn, 1, 'asking the person does not resume on unrelated native activity');
    f.controlPlane.commands.submit(
      f.command('resume', id, 'Run the focused synthetic checks.'),
      'http',
      { kind: 'local' },
    );
    await settle();
    assert.equal(resumed, true);
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
  } finally {
    await f.close();
  }
});
test('empty native assistant content with a real instruction call does not create a blank reported reply', async () => {
  let turn = 0;
  const f = await fixture({
    mode: 'immediate',
    turn: async () =>
      ++turn === 1
        ? answer([{ name: 'continue_task', arguments: { text: 'Synthetic continuation' } }], '')
        : answer([], 'Reported terminal response'),
  });
  try {
    const id = f.submit();
    await settle();
    assert.equal(f.instructions, 1);
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
  } finally {
    await f.close();
  }
});
test('raw stale-sequence and superseded-turn journal records never become applied outcome evidence', async () => {
  const f = await fixture();
  try {
    f.emit('runtime.turn.started', { turn_id: 'existing-turn' }, 10);
    const id = f.submit();
    await settle();
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' }, 9);
    await settle();
    assert.equal(f.turns, 1);
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'waiting_for_runtime');
    f.emit('runtime.turn.started', { turn_id: 'newer-turn' }, 11);
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' }, 12);
    await settle();
    assert.equal(f.turns, 1);
    assert.equal(
      f.controlPlane.projection.execution(
        f.controlPlane.projection.workspaceRequest(id)?.execution_id ?? '',
      )?.status,
      'running',
    );
    f.emit('runtime.turn.completed', { turn_id: 'newer-turn' }, 13);
    await settle();
    assert.equal(f.turns, 2);
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
  } finally {
    await f.close();
  }
});
test('native terminal event in the waiting-to-worker-release gap coalesces a fresh wake', async () => {
  const f = await fixture();
  let emitted = false;
  const unsubscribe = f.controlPlane.publisher.subscribe(({ event }) => {
    if (
      !emitted &&
      event.event_type === 'workspace.request.changed' &&
      event.payload.status === 'waiting_for_runtime'
    ) {
      emitted = true;
      queueMicrotask(() => f.emit('runtime.turn.completed', { turn_id: 'existing-turn' }));
    }
  });
  try {
    const id = f.submit();
    await settle();
    assert.equal(emitted, true);
    assert.equal(f.turns, 2);
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
  } finally {
    unsubscribe();
    await f.close();
  }
});
test('durable accepted-observation cursor crosses a scan bound on explicit Resume without dropping prepared identity', async () => {
  const f = await fixture();
  try {
    const id = f.submit();
    await settle();
    const original = f.controlPlane.projection.workspacePreparedActions(id)[0];
    assert.ok(original);
    for (let i = 0; i < 4100; i++)
      f.emit('runtime.turn.started', { turn_id: `synthetic-turn-${i}` });
    await settle();
    const advanced = f.controlPlane.projection.workspacePreparedActions(id)[0];
    assert.ok(advanced);
    assert.ok(advanced.step.observation_after > original.step.observation_after);
    assert.equal(advanced.step.observation_position, original.step.observation_position);
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'interrupted');
    f.emit('runtime.turn.completed', { turn_id: 'synthetic-turn-4099' });
    f.controlPlane.commands.submit(f.command('resume', id), 'http', { kind: 'local' });
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
    assert.equal(f.instructions, 1);
    assert.equal(
      f.controlPlane.projection.workspacePreparedActions(id)[0]?.command.command_id,
      original.command.command_id,
    );
  } finally {
    await f.close();
  }
});
test('cancelled provider workers retain global leases until their awaited read exits', async () => {
  let release: (value: typeof unavailable) => void = () => {};
  const provider = new Promise<typeof unavailable>((resolve) => {
    release = resolve;
  });
  const f = await fixture({
    turn: async () => answer([{ name: 'read_understanding', arguments: {} }], ''),
    understand: () => provider,
  });
  try {
    const ids = [f.submit(), f.submit(), f.submit(), f.submit()];
    await settle();
    assert.equal(f.turns, 4);
    for (const id of ids)
      f.controlPlane.commands.submit(f.command('cancel', id), 'http', { kind: 'local' });
    const refused = f.controlPlane.commands.submit(f.command('submit'), 'http', { kind: 'local' });
    assert.equal(refused.command?.failure?.code, 'coordinator_busy');
    assert.equal(f.controlPlane.projection.workspaceRequests().length, 4);
    release(unavailable);
    await settle();
    assert.ok(ids.every((id) => f.coordinator.read(id, { kind: 'local' })?.status === 'stopped'));
    assert.equal(f.instructions, 0);
    assert.equal(f.turns, 4);
  } finally {
    release(unavailable);
    await f.close();
  }
});
test('planning starts have a principal rate bound independently of deduplicated command identities', async () => {
  const f = await fixture({ turn: async () => answer() });
  try {
    for (let i = 0; i < 8; i++) {
      f.submit();
      await settle();
    }
    const refused = f.controlPlane.commands.submit(f.command('submit'), 'http', { kind: 'local' });
    assert.equal(refused.command?.failure?.code, 'coordinator_rate_limited');
    assert.equal(f.controlPlane.projection.workspaceRequests().length, 8);
  } finally {
    await f.close();
  }
});

test('filtered event pages advance across older executions instead of looping on an empty cursor', async () => {
  let after = 0,
    seen: { events: unknown[]; next_after: number } | null = null;
  let modelTurns = 0;
  const f = await fixture({
    turn: async (messages) => {
      modelTurns++;
      if (modelTurns === 1) return answer([{ name: 'read_events', arguments: { after } }], '');
      const result = messages.find(
        (message) => message.role === 'tool' && message.tool_name === 'read_events',
      );
      if (result) seen = JSON.parse(result.content);
      return answer();
    },
  });
  try {
    const original = f.controlPlane.projection.executions()[0];
    assert.ok(original);
    after = f.controlPlane.projection.position;
    for (let i = 0; i < 12; i++)
      f.emit('runtime.agent_message', { text: 'Older run reported text.' });
    f.controlPlane.commands.submit(
      {
        schema_version: 1,
        command_id: f.ids.next() as CommandId,
        issued_at: f.time.now().toISOString(),
        client: { name: 'synthetic', version: null, device_label: null },
        command_type: 'execution.start',
        payload: {
          workstream_id: original.workstream_id,
          runtime_id: 'synthetic' as RuntimeId,
          instruction: 'Another synthetic run',
          options: {},
          model_ref: null,
        },
      },
      'internal',
    );
    await settle();
    const current = f.controlPlane.projection.workstream(
      original.workstream_id,
    )?.current_execution_id;
    assert.ok(current);
    assert.notEqual(current, original.execution_id);
    assert.ok(
      f.journal
        .read({ after, limit: 8, workstreamId: original.workstream_id })
        .every((stored) => stored.event.execution_id === original.execution_id),
    );
    const command = f.command('submit');
    assert.equal(command.command_type, 'workspace.request.submit');
    if (command.command_type !== 'workspace.request.submit') throw new Error('wrong command');
    const submitted = f.controlPlane.commands.submit(
      { ...command, payload: { ...command.payload, execution_id: current } },
      'http',
      { kind: 'local' },
    );
    assert.equal(submitted.command?.result?.kind, 'workspace_request_created');
    await settle();
    const observed = seen as { events: unknown[]; next_after: number } | null;
    assert.ok(observed);
    assert.deepEqual(Object.keys(observed).sort(), ['events', 'next_after']);
    assert.deepEqual(observed.events, []);
    assert.ok(observed.next_after > after);
  } finally {
    await f.close();
  }
});
test('a newer execution during the model await expires the original mutation grant', async () => {
  let release: (value: ToolTurnResult) => void = () => {};
  const f = await fixture({
    turn: () =>
      new Promise((resolve) => {
        release = resolve;
      }),
  });
  try {
    const id = f.submit();
    await settle();
    const request = f.controlPlane.projection.workspaceRequest(id);
    assert.ok(request);
    f.controlPlane.commands.submit(
      {
        schema_version: 1,
        command_id: f.ids.next() as CommandId,
        issued_at: f.time.now().toISOString(),
        client: { name: 'synthetic', version: null, device_label: null },
        command_type: 'execution.start',
        payload: {
          workstream_id: request.workstream_id,
          runtime_id: 'synthetic' as RuntimeId,
          instruction: 'Replacement synthetic execution',
          options: {},
          model_ref: null,
        },
      },
      'internal',
    );
    release(
      answer([{ name: 'continue_task', arguments: { text: 'Never deliver to the replacement' } }]),
    );
    await settle();
    assert.equal(f.instructions, 0);
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'interrupted');
  } finally {
    await f.close();
  }
});
test('the bounded global worker queue serializes local inference and rejects fanout above its lease budget', async () => {
  const releases: ((value: ToolTurnResult) => void)[] = [],
    f = await fixture({ turn: () => new Promise((resolve) => releases.push(resolve)) });
  try {
    const ids = [f.submit(), f.submit(), f.submit(), f.submit()];
    await settle();
    assert.equal(f.turns, 1);
    const refused = f.controlPlane.commands.submit(f.command('submit'), 'http', { kind: 'local' });
    assert.equal(refused.command?.failure?.code, 'coordinator_busy');
    for (let i = 0; i < 4; i++) {
      const release = releases[i];
      assert.ok(release);
      release(answer());
      await settle();
      assert.equal(f.turns, Math.min(i + 2, 4));
    }
    assert.ok(ids.every((id) => f.coordinator.read(id, { kind: 'local' })?.status === 'responded'));
  } finally {
    for (const release of releases) release(answer());
    await f.close();
  }
});
