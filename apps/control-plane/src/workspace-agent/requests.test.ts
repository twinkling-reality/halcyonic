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
  WorkstreamId,
} from '@halcyonic/contracts';
import type { ObservationSink, RuntimeAdapter, RuntimeObservation } from '@halcyonic/runtime-core';
import { controlPlaneDraft, NO_CAUSE } from '../core/drafts.ts';
import { reviewDigest } from '../core/review.ts';
import { DEMO_WORKSTREAMS } from '../demo-plan.ts';
import { registerRoutes } from '../http/routes.ts';
import { createHttpServer } from '../http/server.ts';
import { createUuidV7Generator } from '../ids.ts';
import { DeviceAccess } from '../network/devices.ts';
import { capturingLogger, createTestControlPlane } from '../testing/harness.ts';
import type { ToolDefinition, ToolMessage, ToolTurnResult } from './ollama.ts';
import {
  ANSWER_AGAIN,
  ANSWER_IN_WORDS,
  ANSWER_LAST,
  ANSWER_NOW,
  ANSWERING,
  LIST_GUIDE,
  looksLikeToolCall,
  PLAIN_WORDS,
  QUOTED_PREFIX,
  READ_FOR_IT,
  UNREAD_WAIT_MS,
  unreadWaitMs,
  WorkspaceRequests,
} from './requests.ts';

/**
 * What the model was given of a tool's result: its tool message on a turn with tools, or its line in the quoted block of
 * what was read on an answering turn, which carries no tool message.
 */
function readOf(messages: readonly ToolMessage[], name: string): string[] {
  const own = messages.flatMap((message) =>
    message.role === 'tool' && message.tool_name === name ? [message.content] : [],
  );
  const block = messages.find(
    (message) => message.role === 'user' && message.content.startsWith(QUOTED_PREFIX + READ_FOR_IT),
  );
  const lines = (block?.content.split('\n') ?? [])
    .filter((line) => line.startsWith(`${name}: `))
    .map((line) => line.slice(name.length + 2));
  return [...own, ...lines];
}

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
    turn?: (
      messages: readonly ToolMessage[],
      tools?: readonly ToolDefinition[],
    ) => Promise<ToolTurnResult>;
    projectName?: string;
    requestText?: string;
    secrets?: () => Iterable<{ what: string; value: string }>;
    title?: string;
    objective?: string;
    understand?: () => Promise<typeof unavailable>;
  } = {},
) {
  let sink: ObservationSink | undefined,
    instructions = 0,
    instructionTexts: string[] = [],
    turns = 0;
  const emit = (
    type: RuntimeObservation['type'],
    payload: object,
    sequence: number | null = null,
    epistemic: 'observed' | 'inferred' = 'observed',
  ) => {
    sink?.({
      type,
      payload,
      occurred_at: '2026-09-26T10:00:00.000Z',
      native_event_id: null,
      sequence,
      provenance:
        epistemic === 'inferred'
          ? { epistemic, rule: 'synthetic.reconstructed_turn', native_type: null }
          : {
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
    sendInstruction: async (request) => {
      instructions++;
      instructionTexts.push(request.text);
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
      ...(options.secrets ? { secrets: options.secrets } : {}),
    });
  const ids = createUuidV7Generator({ now: () => h.time.now().getTime() }),
    model = {
      status: async () => available,
      turn: async (messages: readonly ToolMessage[], offered?: readonly ToolDefinition[]) => {
        turns++;
        return options.turn
          ? options.turn(messages, offered)
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
    scheduler: h.time,
  });
  const initial = DEMO_WORKSTREAMS[0];
  assert.ok(initial);
  const scenario = {
    ...initial,
    title: options.title ?? initial.title,
    objective: options.objective ?? initial.objective,
  };
  const p = h.controlPlane.commands.submit(
    h.commands.createProject(options.projectName ?? 'Synthetic'),
    'internal',
  );
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
    const outcome = h.controlPlane.commands.submit(
      command('submit', undefined, options.requestText),
      'http',
      principal,
    );
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
    projectId,
    workstreamId,
    execution,
    get instructionTexts() {
      return instructionTexts;
    },
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
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' });
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
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' });
    f.controlPlane.commands.submit(
      {
        schema_version: 1,
        command_id: f.ids.next() as CommandId,
        issued_at: f.time.now().toISOString(),
        client: { name: 'synthetic', version: null, device_label: null },
        command_type: 'execution.start',
        payload: {
          workstream_id: request.workstream_id as WorkstreamId,
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

test('accepted anonymous terminal resolves the actual named active-turn association', async () => {
  const f = await fixture();
  try {
    const id = f.submit();
    await settle();
    f.emit('runtime.turn.completed', { turn_id: null });
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
    assert.equal(f.turns, 2);
  } finally {
    await f.close();
  }
});
test('legitimate inferred reconstructed outcome interrupts explicitly instead of becoming observed or waiting forever', async () => {
  const f = await fixture();
  try {
    const id = f.submit();
    await settle();
    f.emit('runtime.turn.started', { turn_id: 'reconstructed-turn' }, null, 'inferred');
    f.emit('runtime.turn.completed', { turn_id: 'reconstructed-turn' }, null, 'inferred');
    await settle();
    const request = f.coordinator.read(id, { kind: 'local' });
    assert.equal(request?.status, 'interrupted');
    assert.match(request?.steps[0]?.summary ?? '', /has not observed that outcome/);
    assert.equal(request?.steps[0]?.status, 'unknown');
    assert.equal(f.turns, 1);
    f.controlPlane.commands.submit(f.command('resume', id), 'http', { kind: 'local' });
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'interrupted');
    assert.equal(f.instructions, 1);
    assert.equal(f.turns, 1);
  } finally {
    await f.close();
  }
});

test('compact workspace context preserves essential state and all declared capabilities while shortening only outside text', async () => {
  let turns = 0,
    seen: Record<string, unknown> | null = null;
  const f = await fixture({
    projectName: '😀'.repeat(200),
    title: 'Long task title '.repeat(10),
    objective: '🙂'.repeat(4000),
    turn: async (messages) => {
      if (++turns === 1) return answer([{ name: 'read_workspace', arguments: {} }], '');
      const tool = messages.find(
        (value) => value.role === 'tool' && value.tool_name === 'read_workspace',
      );
      if (tool) seen = JSON.parse(tool.content);
      return answer();
    },
  });
  try {
    f.submit();
    await settle();
    const value = seen as Record<string, unknown> | null;
    assert.ok(value);
    const execution = value.execution as Record<string, unknown>;
    assert.equal(execution.status, 'running');
    assert.equal(execution.synthetic, true);
    assert.equal(execution.native_session_observed, true);
    assert.equal((execution.capabilities as Record<string, unknown>).instruct_while_running, true);
    assert.equal((execution.capabilities as Record<string, unknown>).start_execution, true);
    assert.equal(
      ((value.task as Record<string, unknown>).objective as Record<string, unknown>).complete,
      false,
    );
    assert.equal((value.project as Record<string, unknown>).name !== undefined, true);
    assert.ok(Buffer.byteLength(JSON.stringify(value)) < 2048);
  } finally {
    await f.close();
  }
});
test('bounded event text preserves page cursor and explicit completeness flags', async () => {
  let after = 0,
    turns = 0,
    seen: Record<string, unknown> | null = null;
  const f = await fixture({
    turn: async (messages) => {
      if (++turns === 1) return answer([{ name: 'read_events', arguments: { after } }], '');
      const tool = messages.find(
        (value) => value.role === 'tool' && value.tool_name === 'read_events',
      );
      if (tool) seen = JSON.parse(tool.content);
      return answer();
    },
  });
  try {
    after = f.controlPlane.projection.position;
    f.emit('runtime.agent_message', { text: 'Synthetic '.repeat(3000) });
    f.emit('runtime.agent_message', { text: 'Another synthetic '.repeat(1500) });
    f.submit();
    await settle();
    const value = seen as Record<string, unknown> | null;
    assert.ok(value);
    assert.equal(typeof value.next_after, 'number');
    assert.ok((value.next_after as number) > after);
    const events = value.events as Record<string, unknown>[];
    assert.equal(events.length, 2);
    assert.equal(events[0]?.complete, false);
    assert.equal(events[1]?.complete, false);
    assert.ok(Buffer.byteLength(JSON.stringify(value)) < 2048);
  } finally {
    await f.close();
  }
});
test('complete assistant/tool batches pack under the existing input bound instead of retaining two oversized batches', async () => {
  let turns = 0;
  const bytes: number[] = [];
  const f = await fixture({
    turn: async (messages, tools) => {
      bytes.push(Buffer.byteLength(JSON.stringify({ messages, tools })));
      turns++;
      return turns < 8
        ? answer([{ name: 'read_workspace', arguments: {} }], 'x'.repeat(3900))
        : answer([], 'Reported final response');
    },
  });
  try {
    const id = f.submit();
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
    assert.equal(turns, 8);
    console.log('BYTES', JSON.stringify(bytes));
    assert.ok(bytes.every((value) => value <= 11264));
  } finally {
    await f.close();
  }
});

test('exact command recovery survives snapshot eviction and restart without a coordinator or submitted words', async () => {
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-command-recovery-'));
  const path = join(directory, 'journal.db');
  const first = await fixture({ path, turn: async () => answer() });
  const command = first.command('submit', undefined, 'Synthetic words excluded from recovery');
  const submitted = first.controlPlane.commands.submit(command, 'http', { kind: 'local' });
  await settle();
  assert.equal(submitted.command?.result?.kind, 'workspace_request_created');
  for (let i = 0; i < 55; i++)
    first.controlPlane.commands.submit(first.commands.createProject('Synthetic churn'), 'internal');
  assert.equal(
    first.controlPlane.projection.commands(50).some((c) => c.command_id === command.command_id),
    false,
  );
  const journalId = first.journal.info.journal_id;
  await first.close();
  const replay = createTestControlPlane({ path });
  const app = await createHttpServer({ token: 'synthetic-access-token', logLevel: 'silent' });
  try {
    registerRoutes(app, replay.controlPlane, providers);
    const headers = { host: '127.0.0.1:0', authorization: 'Bearer synthetic-access-token' };
    const read = await app.inject({
      method: 'GET',
      url: `/api/workspace/commands/${command.command_id}`,
      headers,
    });
    assert.equal(read.statusCode, 200);
    assert.equal(read.json().journal_id, journalId);
    assert.deepEqual(read.json().command, submitted.command);
    assert.equal(read.body.includes('Synthetic words excluded from recovery'), false);
    assert.equal('payload' in read.json().command, false);
    const unknown = await app.inject({
      method: 'GET',
      url: '/api/workspace/commands/550e8400-e29b-41d4-a716-446655440000',
      headers,
    });
    assert.equal(unknown.statusCode, 404);
    const other = replay.controlPlane.commands.submit(
      replay.commands.createProject('Synthetic'),
      'http',
      { kind: 'local' },
    );
    const nonrequest = await app.inject({
      method: 'GET',
      url: `/api/workspace/commands/${other.command?.command_id}`,
      headers,
    });
    assert.equal(nonrequest.statusCode, 404);
    assert.deepEqual(nonrequest.json(), unknown.json());
    assert.equal(
      (await app.inject({ method: 'GET', url: '/api/workspace/commands/bad', headers })).statusCode,
      400,
    );
    assert.equal(
      (
        await app.inject({
          method: 'GET',
          url: `/api/workspace/commands/${command.command_id}`,
          headers: { host: '127.0.0.1:0' },
        })
      ).statusCode,
      401,
    );
  } finally {
    await app.close();
    await replay.controlPlane.close();
    rmSync(directory, { recursive: true, force: true });
  }
});

test('new anonymous turn never inherits the preceding named turn identity', async () => {
  const f = await fixture();
  try {
    const id = f.submit();
    await settle();
    f.emit('runtime.turn.started', { turn_id: null });
    await settle();
    assert.equal(
      f.controlPlane.projection.workspacePreparedActions(id)[0]?.step.observed_turn_id,
      null,
    );
    f.emit('runtime.turn.completed', { turn_id: null });
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
  } finally {
    await f.close();
  }
});
test('runtime wait preserves bounded prior unavailable reads and includes fresh reported native output without forcing a final answer', async () => {
  let turn = 0,
    sawUnavailable = false,
    sawReport = false;
  const f = await fixture({
    turn: async (messages) => {
      turn++;
      if (turn === 1) return answer([{ name: 'read_understanding', arguments: {} }], '');
      if (turn === 2)
        return answer(
          [{ name: 'continue_task', arguments: { text: 'Synthetic authorized continuation' } }],
          '',
        );
      sawUnavailable = readOf(messages, 'read_understanding').some((content) =>
        content.includes('synthetic_unavailable'),
      );
      sawReport = messages.some(
        (message) =>
          message.role === 'user' &&
          message.content.startsWith(QUOTED_PREFIX) &&
          message.content.includes('Synthetic reported native result') &&
          message.content.includes('reported'),
      );
      const fresh = messages.filter(
        (message) => message.role === 'system' && message.content.startsWith('Fresh host receipts'),
      );
      assert.equal(fresh.length, 1);
      const snapshot = fresh[0];
      assert.ok(snapshot);
      const receipts = JSON.parse(snapshot.content.slice(snapshot.content.indexOf('{')));
      assert.equal(receipts.actions.total_actions, turn === 3 ? 1 : 2);
      assert.equal(receipts.actions.unsettled_actions, 0);
      assert.equal(receipts.actions.complete, true);
      if (turn === 3)
        return answer(
          [{ name: 'continue_task', arguments: { text: 'A second authorized continuation' } }],
          '',
        );
      return answer([], 'Reported response informed by native output.');
    },
  });
  try {
    const id = f.submit();
    await settle();
    assert.equal(f.turns, 2);
    f.emit('runtime.agent_message', { text: 'Synthetic reported native result' });
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' });
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'waiting_for_runtime');
    f.emit('runtime.turn.started', { turn_id: 'second-turn' });
    f.emit('runtime.agent_message', {
      text: 'Synthetic reported native result from the second turn',
    });
    f.emit('runtime.turn.completed', { turn_id: 'second-turn' });
    await settle();
    assert.equal(sawUnavailable, true);
    assert.equal(sawReport, true);
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
    assert.equal(f.instructions, 2);
  } finally {
    await f.close();
  }
});
test('runtime wake preserves the exact observe tool name and opaque assistant call identity', async () => {
  let turn = 0;
  const f = await fixture({
    turn: async (messages) => {
      turn++;
      if (turn === 1)
        return answer(
          [{ name: 'continue_task', arguments: { text: 'Authorized continuation' } }],
          '',
        );
      if (turn === 2)
        return {
          ...answer([], ''),
          toolCalls: [
            { id: 'synthetic-observe-id', function: { name: 'observe_task', arguments: {} } },
          ],
        };
      assert.equal(
        messages.findLast((message) => message.role === 'tool')?.tool_name,
        'observe_task',
      );
      assert.equal(
        messages.findLast((message) => message.role === 'assistant')?.tool_calls?.[0]?.id,
        'synthetic-observe-id',
      );
      return answer([], 'Reported outcome after the exact observation.');
    },
  });
  try {
    const id = f.submit();
    await settle();
    f.controlPlane.commands.submit(f.command('resume', id), 'http', { kind: 'local' });
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'waiting_for_runtime');
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' });
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
  } finally {
    await f.close();
  }
});

test('a pause mid native multi-call batch reports remaining calls as unexecuted without replaying them', async () => {
  let turn = 0;
  const f = await fixture({
    turn: async (messages) => {
      turn++;
      if (turn === 1)
        return answer(
          [
            { name: 'continue_task', arguments: { text: 'Authorized continuation' } },
            { name: 'read_events', arguments: { after: 0 } },
          ],
          '',
        );
      // An answering turn: what was read stands in its quoted block, in order, with no tool message to copy.
      assert.equal(readOf(messages, 'continue_task').length, 1);
      assert.deepEqual(JSON.parse(readOf(messages, 'read_events')[0] ?? 'null'), {
        executed: false,
        reason: 'prior_call_paused_batch',
      });
      assert.equal(
        messages.some((message) => message.role === 'assistant' || message.role === 'tool'),
        false,
      );
      return answer([], 'Reported after the pending action.');
    },
  });
  try {
    const id = f.submit();
    await settle();
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' });
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
    assert.equal(
      f.coordinator
        .read(id, { kind: 'local' })
        ?.steps.some((step) => step.tool_name === 'read_events'),
      false,
    );
  } finally {
    await f.close();
  }
});

test('rejected busy Resume preserves the existing continuation and does not add new human direction', async () => {
  let turn = 0;
  let release: () => void = () => {};
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  const f = await fixture({
    turn: async (messages) => {
      turn++;
      if (turn === 1) return answer([{ name: 'read_understanding', arguments: {} }], '');
      if (turn === 2)
        return answer(
          [{ name: 'continue_task', arguments: { text: 'Authorized continuation' } }],
          '',
        );
      await gate;
      assert.equal(
        readOf(messages, 'read_understanding').some((content) =>
          content.includes('synthetic_unavailable'),
        ),
        true,
      );
      assert.equal(
        messages.some(
          (message) => message.role === 'user' && message.content === 'Rejected busy direction',
        ),
        false,
      );
      return answer([], 'Reported with original continuation.');
    },
  });
  try {
    const id = f.submit();
    await settle();
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' });
    await settle();
    const command = f.command('resume', id);
    if (command.command_type === 'workspace.request.resume')
      command.payload.text = 'Rejected busy direction';
    f.controlPlane.commands.submit(command, 'http', { kind: 'local' });
    await settle();
    assert.equal(f.controlPlane.projection.command(command.command_id)?.status, 'failed');
    release();
    await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
  } finally {
    release();
    await f.close();
  }
});

test('runtime delegation retains the complete person request even when the model follow-up only asks to read', async () => {
  const direction = 'Change only the synthetic shell colour. Preserve every other byte and file.';
  const followup = 'Read the current synthetic colour.';
  const f = await fixture({
    requestText: direction,
    turn: async () => answer([{ name: 'continue_task', arguments: { text: followup } }]),
  });
  try {
    f.submit();
    await settle();
    assert.equal(f.instructionTexts.length, 1);
    const delivered = f.instructionTexts[0];
    assert.ok(delivered);
    assert.ok(delivered.includes(`Original person request:\n\n${direction}`));
    assert.ok(
      delivered.includes(`Coordinator follow-up within that person direction:\n\n${followup}`),
    );
    assert.ok(delivered.indexOf(direction) < delivered.indexOf(followup));
  } finally {
    await f.close();
  }
});

test('runtime delegation includes ordered completed human continuations without letting model text replace them', async () => {
  const original = 'Work only in the synthetic project.';
  const first = 'Keep the synthetic eye colour unchanged.';
  const second = 'Change the shell only; the earlier boundary still applies.';
  let modelTurn = 0;
  const f = await fixture({
    requestText: original,
    turn: async () =>
      ++modelTurn < 3
        ? answer([])
        : answer([
            {
              name: 'continue_task',
              arguments: { text: 'Synthetic model follow-up: inspect first.' },
            },
          ]),
  });
  try {
    const id = f.submit();
    await settle();
    f.controlPlane.commands.submit(f.command('resume', id, first), 'http', { kind: 'local' });
    await settle();
    f.controlPlane.commands.submit(f.command('resume', id, second), 'http', { kind: 'local' });
    await settle();
    assert.equal(f.instructionTexts.length, 1);
    const delivered = f.instructionTexts[0];
    assert.ok(delivered);
    assert.ok(delivered.includes(`Person continuation 1:\n\n${first}`));
    assert.ok(delivered.includes(`Person continuation 2:\n\n${second}`));
    assert.ok(delivered.indexOf(original) < delivered.indexOf(first));
    assert.ok(delivered.indexOf(first) < delivered.indexOf(second));
    assert.ok(
      delivered.indexOf(second) <
        delivered.indexOf('Coordinator follow-up within that person direction:'),
    );
  } finally {
    await f.close();
  }
});

test('a request of the full 32,000 characters goes to the task whole, alone, without the host framing', async () => {
  const words = 'Make the garden path wind past the pond. '.repeat(800).slice(0, 32000);
  const bodies: number[] = [];
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    requestText: words,
    turn: async (messages, tools) => {
      seen.push([...messages]);
      const body = Buffer.byteLength(JSON.stringify({ messages, tools: tools ?? [] }));
      bodies.push(body);
      // As ollama.ts refuses a body over its bound.
      if (body > 12_288)
        return {
          kind: 'failed',
          code: 'context_limit',
          message: 'This request needs less context before it can continue.',
        };
      return seen.length === 1
        ? answer([{ name: 'continue_task', arguments: { text: 'Do what the person asked.' } }])
        : answer([], 'The path change was sent.');
    },
  });
  try {
    const id = f.submit();
    for (let i = 0; i < 6; i++) await settle();
    assert.ok(
      bodies.every((body) => body <= 12_288),
      JSON.stringify(bodies),
    );
    // The model read the beginning and the end, with the gap marked; nothing of its own retypes the words.
    const shown = seen[0]?.find((message) => message.role === 'user')?.content ?? '';
    assert.match(shown, /\[The computer left out \d+ of these 32000 characters here/);
    assert.ok(shown.startsWith(words.slice(0, 200)));
    assert.ok(shown.endsWith(words.slice(-100)));
    // The task got every character, exactly: the person's words alone, without the framing or the model's note.
    assert.equal(f.instructions, 1);
    assert.equal(f.instructionTexts[0], words);
    const request = f.coordinator.read(id, { kind: 'local' });
    assert.notEqual(request?.status, 'failed');
  } finally {
    await f.close();
  }
});

test('a long all-work request is answered, and the answer says it read only part of the words', async () => {
  const words = 'Tell me what is going on with all my work today. '.repeat(700).slice(0, 32000);
  const bodies: number[] = [];
  const f = await fixture({
    turn: async (messages, tools) => {
      const body = Buffer.byteLength(JSON.stringify({ messages, tools: tools ?? [] }));
      bodies.push(body);
      if (body > 12_288)
        return {
          kind: 'failed',
          code: 'context_limit',
          message: 'This request needs less context before it can continue.',
        };
      return bodies.length === 1
        ? answer([{ name: 'read_projects', arguments: {} }])
        : answer([], 'One project; nothing runs.');
    },
  });
  try {
    const outcome = submitAllWork(f, words);
    assert.equal(outcome.disposition, 'accepted', JSON.stringify(outcome.command?.rejection));
    for (let i = 0; i < 8; i++) await settle();
    const created = outcome.command?.result;
    const request = f.controlPlane.projection.workspaceRequest(
      created?.kind === 'workspace_request_created' ? created.request_id : '',
    );
    assert.equal(request?.status, 'responded', JSON.stringify(bodies));
    assert.ok(
      bodies.every((body) => body <= 12_288),
      JSON.stringify(bodies),
    );
    assert.match(
      request?.reply?.text ?? '',
      /^One project; nothing runs\. Your words were long, so this answer comes from \d+ of their 32000 characters, the beginning and the end\.$/,
    );
    // Nothing was sent anywhere, so the reply does not say the words went to an agent.
    assert.doesNotMatch(request?.reply?.text ?? '', /went to the agent/);
  } finally {
    await f.close();
  }
});

test('a request with many long continuations is never refused for its length: older ones are left out under a marked line (AUDIT2 F1)', async () => {
  const bodies: number[] = [];
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    requestText: 'Make the pond bigger.',
    turn: async (messages, tools) => {
      seen.push([...messages]);
      const body = Buffer.byteLength(JSON.stringify({ messages, tools: tools ?? [] }));
      bodies.push(body);
      if (body > 12_288)
        return {
          kind: 'failed',
          code: 'context_limit',
          message: 'This request needs less context before it can continue.',
        };
      return answer([], 'Noted.');
    },
  });
  try {
    const id = f.submit();
    for (let i = 0; i < 4; i++) await settle();
    // Eight continuations of 900 characters each, a dictated paragraph each, as AUDIT2's probe sent.
    for (let n = 1; n <= 8; n++) {
      // A person speaks a paragraph every few seconds, within the coordinator's starts per minute.
      await f.time.advance(8_000);
      const more =
        `Continuation ${n}: ${'and put more lilies around the edge of the water '.repeat(30)}`.slice(
          0,
          900,
        );
      const outcome = f.controlPlane.commands.submit(f.command('resume', id, more), 'http', {
        kind: 'local',
      });
      assert.equal(outcome.disposition, 'accepted', JSON.stringify(outcome.command?.rejection));
      for (let i = 0; i < 4; i++) await settle();
      const request = f.coordinator.read(id, { kind: 'local' });
      assert.equal(
        request?.status,
        'responded',
        `after continuation ${n}: ${JSON.stringify(bodies)}`,
      );
    }
    assert.ok(
      bodies.every((body) => body <= 12_288),
      JSON.stringify(bodies),
    );
    // The newest continuation is read whole; the earliest are said to be left out, by count and characters.
    const last = seen.at(-1) ?? [];
    const users = last
      .filter((message) => message.role === 'user')
      .map((message) => message.content);
    assert.ok(users.some((text) => text.startsWith('Continuation 8: ')));
    assert.ok(
      users.some((text) =>
        /^\[The computer left out the person's \d+ earliest continuations here, \d+ characters/.test(
          text,
        ),
      ),
    );
    assert.ok(!users.some((text) => text.startsWith('Continuation 1: ')));
    const request = f.coordinator.read(id, { kind: 'local' });
    assert.match(
      request?.reply?.text ?? '',
      /^Noted\. Your words were long, so this answer comes from \d+ of their \d+ characters/,
    );
    assert.doesNotMatch(request?.reply?.text ?? '', /went to the agent/);
  } finally {
    await f.close();
  }
});

test('the host says all the words went to the agent only when an instruction was confirmed after the last of them (AUDIT2 F2)', async () => {
  const words = 'Make the garden path wind past the pond, with stepping stones. '
    .repeat(100)
    .slice(0, 6000);
  let modelTurn = 0;
  const f = await fixture({
    mode: 'immediate',
    requestText: words,
    turn: async () => {
      modelTurn++;
      return modelTurn === 1
        ? answer([{ name: 'continue_task', arguments: { text: 'Do what the person asked.' } }])
        : answer([], modelTurn === 2 ? 'The path change was sent.' : 'Noted, nothing sent.');
    },
  });
  try {
    const id = f.submit();
    for (let i = 0; i < 8; i++) await settle();
    let request = f.coordinator.read(id, { kind: 'local' });
    assert.equal(request?.status, 'responded');
    assert.equal(f.instructions, 1);
    // Sent after the person's only words: true, and said.
    assert.match(request?.reply?.text ?? '', /All of them went to the agent\.$/);
    const outcome = f.controlPlane.commands.submit(
      f.command('resume', id, 'Also put a red bench by the pond, and tell me if it fits.'),
      'http',
      { kind: 'local' },
    );
    assert.equal(outcome.disposition, 'accepted');
    for (let i = 0; i < 8; i++) await settle();
    request = f.coordinator.read(id, { kind: 'local' });
    assert.equal(f.instructions, 1, 'nothing more was sent');
    assert.match(request?.reply?.text ?? '', /^Noted, nothing sent\. Your words were long/);
    // The bench never went to the agent, so the host does not say every word did.
    assert.doesNotMatch(request?.reply?.text ?? '', /went to the agent/);
  } finally {
    await f.close();
  }
});

test('words with a continuation past the instruction limit pause for a person without truncation, dispatch or an automatic retry', async () => {
  let modelTurn = 0;
  const f = await fixture({
    requestText: 'a'.repeat(32000),
    turn: async () =>
      ++modelTurn === 1
        ? answer([], 'Noted.')
        : answer([
            { name: 'continue_task', arguments: { text: 'Read the synthetic file.' } },
            { name: 'read_workspace', arguments: {} },
          ]),
  });
  try {
    const id = f.submit();
    await settle();
    f.controlPlane.commands.submit(f.command('resume', id, 'b'.repeat(40)), 'http', {
      kind: 'local',
    });
    await settle();
    const request = f.coordinator.read(id, { kind: 'local' });
    assert.ok(request);
    assert.equal(request.status, 'waiting_for_human');
    assert.equal(f.instructions, 0);
    assert.equal(f.turns, 2);
    assert.ok(
      request.steps.some(
        (s) =>
          s.tool_name === 'continue_task' &&
          s.status === 'waiting' &&
          s.command_id === null &&
          s.summary.includes("the person's words together exceed the runtime instruction limit"),
      ),
    );
    assert.ok(!request.steps.some((s) => s.tool_name === 'read_workspace'));
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' });
    await settle();
    assert.equal(f.instructions, 0);
    assert.equal(f.turns, 2);
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'waiting_for_human');
  } finally {
    await f.close();
  }
});

test('delegated instruction uses the full contract Unicode bound and retains existing held-secret replacement', async () => {
  const secret = 'synthetic-held-secret-value';
  const original = '界'.repeat(15000) + secret;
  const followup = '😀'.repeat(15000) + secret;
  const f = await fixture({
    requestText: original,
    secrets: () => [{ what: 'synthetic credential', value: secret }],
    turn: async () => answer([{ name: 'continue_task', arguments: { text: followup } }]),
  });
  try {
    f.submit();
    await settle();
    assert.equal(f.instructions, 1);
    const delivered = f.instructionTexts[0];
    assert.ok(delivered);
    assert.ok(
      delivered.length > 32000,
      'UTF-16 length must not incorrectly refuse valid Unicode codepoints',
    );
    assert.ok(!delivered.includes(secret));
    assert.equal(delivered.split('[redacted: synthetic credential]').length - 1, 2);
    assert.ok(delivered.includes('界'.repeat(15000)) && delivered.includes('😀'.repeat(15000)));
  } finally {
    await f.close();
  }
});

/** A whole-project request (ADR 0030, amended 2026-11-10): no task, no execution. */
function submitProject(
  f: Awaited<ReturnType<typeof fixture>>,
  text = 'What is going on in this project?',
) {
  return f.controlPlane.commands.submit(
    {
      schema_version: 1,
      command_id: f.ids.next() as CommandId,
      issued_at: f.time.now().toISOString(),
      client: { name: 'synthetic', version: null, device_label: null },
      command_type: 'workspace.request.submit',
      payload: { project_id: f.projectId, workstream_id: null, execution_id: null, text },
    },
    'http',
    { kind: 'local' },
  );
}

test('a whole-project request reads two tasks by the host list and cannot instruct either', async () => {
  const offered: string[][] = [];
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    turn: async (messages, tools) => {
      offered.push((tools ?? []).map((tool) => tool.function.name));
      seen.push([...messages]);
      switch (offered.length) {
        case 1:
          return answer([{ name: 'read_project', arguments: {} }]);
        case 2:
          return answer([
            { name: 'read_task', arguments: { index: 0 } },
            { name: 'read_task', arguments: { index: 1 } },
          ]);
        case 3:
          return answer([{ name: 'continue_task', arguments: { text: 'Change everything' } }]);
        default:
          return answer([], 'Two tasks: one ran, one has not started.');
      }
    },
  });
  try {
    const second = f.controlPlane.commands.submit(
      f.commands.createWorkstream(f.projectId, {
        ...(DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number]),
        title: 'Second synthetic task',
      }),
      'internal',
    );
    assert.equal(second.disposition, 'accepted');
    const outcome = submitProject(f);
    assert.equal(outcome.disposition, 'accepted', JSON.stringify(outcome.command?.rejection));
    for (let i = 0; i < 10; i++) await settle();
    const created = outcome.command?.result;
    assert.equal(created?.kind, 'workspace_request_created');
    const request = f.controlPlane.projection.workspaceRequest(
      created?.kind === 'workspace_request_created' ? created.request_id : '',
    );
    assert.equal(request?.workstream_id, null);
    assert.equal(request?.execution_id, null);
    assert.equal(request?.status, 'responded');
    // A project request may propose from the host's list (ADR 0060); it still cannot instruct a task.
    assert.deepEqual([...new Set(offered.flat())].sort(), [
      'ask_person',
      'propose_command',
      'read_choices',
      'read_project',
      'read_task',
    ]);
    assert.equal(f.instructions, 0);
    const toolResults = (seen.at(-1) ?? []).filter((message) => message.role === 'tool');
    const text = toolResults.map((message) => message.content).join('\n');
    assert.match(text, /Second synthetic task/);
    assert.match(text, /"status":"not started"/);
    assert.match(
      text,
      /"evidence":\{"said_by":"the Mac, not the agent","in_words":\["The task is /,
    );
    assert.match(text, /tool_not_offered/);
    assert.ok(
      request?.steps.some((step) => step.tool_name === 'tool_call' && step.status === 'rejected'),
    );
  } finally {
    await f.close();
  }
});

test('read_task refuses an index before the host list and one outside it', async () => {
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    turn: async (messages) => {
      seen.push([...messages]);
      switch (seen.length) {
        case 1:
          return answer([{ name: 'read_task', arguments: { index: 0 } }]);
        case 2:
          return answer([{ name: 'read_project', arguments: {} }]);
        case 3:
          return answer([{ name: 'read_task', arguments: { index: 9 } }]);
        default:
          return answer([], 'Done reading.');
      }
    },
  });
  try {
    assert.equal(submitProject(f).disposition, 'accepted');
    for (let i = 0; i < 10; i++) await settle();
    const results = (seen.at(-1) ?? [])
      .filter((message) => message.role === 'tool')
      .map((message) => message.content);
    assert.match(results[0] ?? '', /project_not_read/);
    assert.match(results[2] ?? '', /index_not_in_list/);
  } finally {
    await f.close();
  }
});

test('a request naming a task without its execution, or the reverse, is refused', async () => {
  const f = await fixture({ turn: async () => answer([], 'unused') });
  try {
    for (const payload of [
      { project_id: f.projectId, workstream_id: f.workstreamId, execution_id: null },
      { project_id: f.projectId, workstream_id: null, execution_id: f.execution },
    ]) {
      const outcome = f.controlPlane.commands.submit(
        {
          schema_version: 1,
          command_id: f.ids.next() as CommandId,
          issued_at: f.time.now().toISOString(),
          client: { name: 'synthetic', version: null, device_label: null },
          command_type: 'workspace.request.submit',
          payload: { ...payload, text: 'Half a binding' },
        },
        'http',
        { kind: 'local' },
      );
      assert.equal(outcome.disposition, 'rejected');
    }
  } finally {
    await f.close();
  }
});

test('the host context gives what the Mac saw a task change, and says not checked without a check', async () => {
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    turn: async (messages) => {
      seen.push([...messages]);
      return seen.length === 1
        ? answer([{ name: 'read_workspace', arguments: {} }])
        : answer([], 'It changed src/game.ts; nothing has checked it yet.');
    },
  });
  try {
    const view = f.controlPlane.projection.execution(f.execution);
    assert.ok(view);
    f.controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.files.observed',
        {
          project_id: view.project_id,
          workstream_id: view.workstream_id,
          execution_id: view.execution_id,
        },
        {
          turn_id: null,
          created: { files: [{ path: 'src/new.ts', lines: 4, bytes: 80 }], total: 1 },
          changed: {
            files: [{ path: 'src/game.ts', lines_before: 10, lines_after: 12, bytes: 300 }],
            total: 1,
          },
          deleted: { files: [], total: 0 },
          not_read: 0,
          skipped: null,
        },
        f.time.now().toISOString() as never,
        NO_CAUSE,
      ),
    );
    f.submit();
    for (let i = 0; i < 10; i++) await settle();
    const fresh = (seen[0] ?? []).find(
      (message) => message.role === 'user' && message.content.startsWith(QUOTED_PREFIX),
    );
    assert.ok(
      !(seen[0] ?? []).some(
        (message) => message.role === 'system' && message.content.includes('src/game.ts'),
      ),
      'no path the agent wrote reaches a system message',
    );
    const data = JSON.parse(fresh?.content.slice(QUOTED_PREFIX.length) ?? '{}') as {
      observed_by_the_mac: {
        in_words: string[];
        files_newest_first: { path: string; how: string }[];
      };
    };
    const evidence = data.observed_by_the_mac;
    // The facts in sentences (the lead, 2026-11-12): no field name or code for the model to repeat.
    assert.ok(
      evidence.in_words.includes(
        'The Mac saw 1 turn change files: 1 created, 1 changed and 0 deleted in all.',
      ),
      JSON.stringify(evidence.in_words),
    );
    assert.ok(evidence.in_words.includes('The computer has not checked these files.'));
    assert.deepEqual(
      evidence.files_newest_first.map((file) => [file.path, file.how]),
      [
        ['src/new.ts', 'created'],
        ['src/game.ts', 'changed'],
      ],
    );
  } finally {
    await f.close();
  }
});

test("after its own work, a request waits for the Mac's check of the project and answers once it ends", async () => {
  let turns = 0;
  const f = await fixture({
    mode: 'immediate',
    turn: async () => {
      turns++;
      return turns === 1
        ? answer([{ name: 'continue_task', arguments: { text: 'Make it blue.' } }])
        : answer([], 'Answered after the check.');
    },
  });
  try {
    // A check of the project under way, as the host's automatic check after a turn that changed files.
    const check = {
      schema_version: 1 as const,
      command_id: f.ids.next() as CommandId,
      issued_at: f.time.now().toISOString(),
      client: { name: 'halcyonic-control-plane', version: null, device_label: null },
      command_type: 'project.check' as const,
      payload: { project_id: f.projectId },
    };
    const scope = { project_id: f.projectId, workstream_id: null, execution_id: null };
    f.controlPlane.recorder.record(
      controlPlaneDraft(
        'command.accepted',
        scope,
        { command: check, policy: 'low_consequence', received_via: 'internal', principal: null },
        f.time.now().toISOString() as never,
        NO_CAUSE,
      ),
    );
    const id = f.submit();
    for (let i = 0; i < 10; i++) await settle();
    assert.equal(f.instructions, 1);
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'waiting_for_runtime');
    assert.equal(turns, 1);
    f.controlPlane.recorder.record(
      controlPlaneDraft(
        'command.failed',
        scope,
        {
          command_id: check.command_id,
          command_type: 'project.check',
          failure: { code: 'synthetic_check', message: 'Synthetic check ended.', effect: 'none' },
        },
        f.time.now().toISOString() as never,
        NO_CAUSE,
      ),
    );
    for (let i = 0; i < 10; i++) await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
    assert.equal(turns, 2);
  } finally {
    await f.close();
  }
});

test('a tool the request was not offered is refused, even one that exists (AUDIT2 S1)', async () => {
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    turn: async (messages) => {
      seen.push([...messages]);
      return seen.length === 1
        ? answer([{ name: 'read_project', arguments: {} }])
        : answer([], 'Done.');
    },
  });
  try {
    f.submit();
    for (let i = 0; i < 10; i++) await settle();
    const result = (seen.at(-1) ?? []).find((message) => message.role === 'tool')?.content ?? '';
    assert.match(result, /tool_not_offered/);
  } finally {
    await f.close();
  }
});

/** A request about all of the person's work (ADR 0030, amended 2026-11-10): no project, no task. */
function submitAllWork(
  f: Awaited<ReturnType<typeof fixture>>,
  text = 'What is going on with my work?',
) {
  return f.controlPlane.commands.submit(
    {
      schema_version: 1,
      command_id: f.ids.next() as CommandId,
      issued_at: f.time.now().toISOString(),
      client: { name: 'synthetic', version: null, device_label: null },
      command_type: 'workspace.request.submit',
      payload: { project_id: null, workstream_id: null, execution_id: null, text },
    },
    'http',
    { kind: 'local' },
  );
}

test('a request about all work lists the projects, reads a task of the second, and cannot instruct', async () => {
  const offered: string[][] = [];
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    projectName: 'First garden',
    turn: async (messages, tools) => {
      offered.push((tools ?? []).map((tool) => tool.function.name));
      seen.push([...messages]);
      switch (offered.length) {
        case 1:
          return answer([{ name: 'read_projects', arguments: {} }]);
        case 2:
          return answer([{ name: 'read_project_at', arguments: { index: 1 } }]);
        case 3:
          return answer([
            { name: 'read_task', arguments: { index: 0 } },
            { name: 'continue_task', arguments: { text: 'Change everything' } },
          ]);
        default:
          return answer([], 'Two projects; the second has one task not started.');
      }
    },
  });
  try {
    const second = f.controlPlane.commands.submit(
      f.commands.createProject('Second garden'),
      'internal',
    );
    assert.equal(second.command?.result?.kind, 'project_created');
    const secondId =
      second.command?.result?.kind === 'project_created' ? second.command.result.project_id : '';
    f.controlPlane.commands.submit(
      f.commands.createWorkstream(secondId as never, {
        ...(DEMO_WORKSTREAMS[0] as (typeof DEMO_WORKSTREAMS)[number]),
        title: 'Second garden task',
      }),
      'internal',
    );
    const outcome = submitAllWork(f);
    assert.equal(outcome.disposition, 'accepted', JSON.stringify(outcome.command?.rejection));
    for (let i = 0; i < 12; i++) await settle();
    const created = outcome.command?.result;
    const request = f.controlPlane.projection.workspaceRequest(
      created?.kind === 'workspace_request_created' ? created.request_id : '',
    );
    assert.equal(request?.project_id, null);
    assert.equal(request?.status, 'responded');
    // About all work it only reads, and answers: never a question back (ask_person is not offered).
    // A request about all work may propose from the host's list (ADR 0060); it still cannot instruct a task.
    assert.deepEqual([...new Set(offered.flat())].sort(), [
      'propose_command',
      'read_choices',
      'read_project_at',
      'read_projects',
      'read_task',
    ]);
    assert.equal(f.instructions, 0);
    const text = (seen.at(-1) ?? [])
      .filter((message) => message.role === 'tool')
      .map((message) => message.content)
      .join('\n');
    assert.match(text, /First garden/);
    assert.match(text, /Second garden task/);
    // The lists give their whole counts.
    assert.match(text, /"projects_total":2/);
    assert.match(text, /"tasks_total":1/);
    assert.match(text, /tool_not_offered/);
  } finally {
    await f.close();
  }
});

test('a request about thirteen projects reads every row whole, not a shortened excerpt', async () => {
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    projectName: 'Garden 0',
    turn: async (messages) => {
      seen.push([...messages]);
      return seen.length === 1
        ? answer([{ name: 'read_projects', arguments: {} }])
        : answer([], 'Thirteen projects; none runs.');
    },
  });
  try {
    for (let i = 1; i < 13; i++)
      f.controlPlane.commands.submit(f.commands.createProject(`Garden ${i}`), 'internal');
    const outcome = submitAllWork(f);
    assert.equal(outcome.disposition, 'accepted', JSON.stringify(outcome.command?.rejection));
    for (let i = 0; i < 8; i++) await settle();
    const created = outcome.command?.result;
    const request = f.controlPlane.projection.workspaceRequest(
      created?.kind === 'workspace_request_created' ? created.request_id : '',
    );
    assert.equal(request?.status, 'responded');
    const read = (seen[1] ?? []).find(
      (message) => message.role === 'tool' && message.tool_name === 'read_projects',
    );
    assert.ok(read, 'the second turn holds the projects list');
    const list = JSON.parse(read.content) as {
      projects_total: number;
      projects: { index: number; name: { text: string } }[];
      more_projects: number;
      availability?: string;
    };
    assert.equal(list.availability, undefined);
    assert.equal(list.projects_total, 13);
    assert.equal(list.projects.length, 13);
    assert.equal(list.more_projects, 0);
    assert.deepEqual(
      list.projects.map((project) => project.name.text).sort(),
      Array.from({ length: 13 }, (_, i) => `Garden ${i}`).sort(),
    );
  } finally {
    await f.close();
  }
});

test('a projects list past the host-list bound drops its last rows and keeps the totals', async () => {
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    projectName: 'A garden with a long name '.repeat(3),
    turn: async (messages) => {
      seen.push([...messages]);
      return seen.length === 1
        ? answer([{ name: 'read_projects', arguments: {} }])
        : answer([], 'Seventy projects.');
    },
  });
  try {
    for (let i = 1; i < 70; i++)
      f.controlPlane.commands.submit(
        f.commands.createProject(`${'A garden with a long name '.repeat(3)}${i}`),
        'internal',
      );
    const outcome = submitAllWork(f);
    assert.equal(outcome.disposition, 'accepted', JSON.stringify(outcome.command?.rejection));
    for (let i = 0; i < 8; i++) await settle();
    const created = outcome.command?.result;
    const request = f.controlPlane.projection.workspaceRequest(
      created?.kind === 'workspace_request_created' ? created.request_id : '',
    );
    assert.equal(request?.status, 'responded');
    const read = (seen[1] ?? []).find(
      (message) => message.role === 'tool' && message.tool_name === 'read_projects',
    );
    assert.ok(read);
    assert.ok(Buffer.byteLength(read.content) <= 4096);
    const list = JSON.parse(read.content) as {
      projects_total: number;
      projects: unknown[];
      more_projects: number;
    };
    assert.equal(list.projects_total, 70);
    assert.ok(list.projects.length > 0 && list.projects.length < 64);
    // Every project is counted: the rows shown and the rows left out make the whole.
    assert.equal(list.projects.length + list.more_projects, 70);
  } finally {
    await f.close();
  }
});

test('a projects list fits the room long words leave, so the transport never refuses the next turn (AUDIT2)', async () => {
  const bodies: number[] = [];
  const seen: ToolMessage[][] = [];
  const name = 'A garden with a long name '.repeat(3);
  const words = 'Tell me what is going on with all my work today. '.repeat(98).slice(0, 4800);
  const f = await fixture({
    projectName: name,
    turn: async (messages, tools) => {
      seen.push([...messages]);
      const body = Buffer.byteLength(JSON.stringify({ messages, tools: tools ?? [] }));
      bodies.push(body);
      // As ollama.ts refuses a body over its bound.
      if (body > 12_288)
        return {
          kind: 'failed',
          code: 'context_limit',
          message: 'This request needs less context before it can continue.',
        };
      return seen.length === 1
        ? answer([{ name: 'read_projects', arguments: {} }])
        : answer([], 'Seventy projects.');
    },
  });
  try {
    for (let i = 1; i < 70; i++)
      f.controlPlane.commands.submit(f.commands.createProject(`${name}${i}`), 'internal');
    const outcome = submitAllWork(f, words);
    assert.equal(outcome.disposition, 'accepted', JSON.stringify(outcome.command?.rejection));
    for (let i = 0; i < 8; i++) await settle();
    const created = outcome.command?.result;
    const request = f.controlPlane.projection.workspaceRequest(
      created?.kind === 'workspace_request_created' ? created.request_id : '',
    );
    assert.equal(request?.status, 'responded', JSON.stringify(bodies));
    assert.ok(
      bodies.every((body) => body <= 12_288),
      JSON.stringify(bodies),
    );
    const read = (seen[1] ?? []).find(
      (message) => message.role === 'tool' && message.tool_name === 'read_projects',
    );
    assert.ok(read);
    const list = JSON.parse(read.content) as {
      projects_total: number;
      projects: unknown[];
      more_projects: number;
    };
    assert.equal(list.projects_total, 70);
    assert.ok(list.projects.length > 0);
    assert.equal(list.projects.length + list.more_projects, 70);
  } finally {
    await f.close();
  }
});

test("the wait for the Mac's reading of a turn ends a minute after the task's last activity (AUDIT2 S2)", () => {
  const at = Date.parse('2026-11-10T10:00:00.000Z');
  const facts = { files: 'unknown', status: 'completed', lastActivity: '2026-11-10T10:00:00.000Z' };
  assert.equal(unreadWaitMs({ ...facts, now: at + 30_000 }), UNREAD_WAIT_MS - 30_000);
  assert.equal(unreadWaitMs({ ...facts, now: at + UNREAD_WAIT_MS }), 0);
  assert.equal(unreadWaitMs({ ...facts, files: 'current', now: at }), 0);
  assert.equal(unreadWaitMs({ ...facts, status: 'running', now: at }), 0);
  assert.equal(unreadWaitMs({ ...facts, lastActivity: 'not a time', now: at }), 0);
});

test('read_project_at refuses an index before the list and one outside it; half a binding is refused', async () => {
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    turn: async (messages) => {
      seen.push([...messages]);
      switch (seen.length) {
        case 1:
          return answer([{ name: 'read_project_at', arguments: { index: 0 } }]);
        case 2:
          return answer([{ name: 'read_projects', arguments: {} }]);
        case 3:
          return answer([{ name: 'read_project_at', arguments: { index: 7 } }]);
        default:
          return answer([], 'Done.');
      }
    },
  });
  try {
    assert.equal(submitAllWork(f).disposition, 'accepted');
    for (let i = 0; i < 10; i++) await settle();
    const results = (seen.at(-1) ?? [])
      .filter((message) => message.role === 'tool')
      .map((message) => message.content);
    assert.match(results[0] ?? '', /projects_not_read/);
    assert.match(results[2] ?? '', /index_not_in_list/);
    const half = f.controlPlane.commands.submit(
      {
        schema_version: 1,
        command_id: f.ids.next() as CommandId,
        issued_at: f.time.now().toISOString(),
        client: { name: 'synthetic', version: null, device_label: null },
        command_type: 'workspace.request.submit',
        payload: {
          project_id: null,
          workstream_id: f.workstreamId,
          execution_id: f.execution,
          text: 'No project but a task',
        },
      },
      'http',
      { kind: 'local' },
    );
    assert.equal(half.disposition, 'rejected');
  } finally {
    await f.close();
  }
});

test('once the work it sent has its outcome, the host context says so, so the model answers', async () => {
  const flags: (boolean | undefined)[] = [];
  const offered: string[][] = [];
  const f = await fixture({
    turn: async (messages, tools) => {
      offered.push((tools ?? []).map((tool) => tool.function.name));
      const fresh = messages.find(
        (message) => message.role === 'system' && message.content.startsWith('Fresh host receipts'),
      );
      flags.push(
        fresh
          ? (
              JSON.parse(fresh.content.slice(fresh.content.indexOf('{'))) as {
                work_outcome_in?: boolean;
              }
            ).work_outcome_in
          : undefined,
      );
      return flags.length === 1
        ? answer([{ name: 'continue_task', arguments: { text: 'Make it blue.' } }])
        : answer([], 'It is blue now, the agent says.');
    },
  });
  try {
    const id = f.submit();
    for (let i = 0; i < 10; i++) await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'waiting_for_runtime');
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' });
    for (let i = 0; i < 10; i++) await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
    assert.deepEqual(flags, [false, true]);
    // The first turn of a task request goes straight to the work: the change or a question, never only a look.
    // With the host's list of what can be done here beside it (ADR 0060).
    assert.deepEqual([...(offered[0] ?? [])].sort(), [
      'ask_person',
      'continue_task',
      'propose_command',
      'read_choices',
    ]);
    // Every read and the observation come from the second turn on.
    assert.ok(offered[1] !== undefined);
    // Once the outcome is in, that turn is offered no tool, so the model answers.
    assert.deepEqual(offered[1], []);
  } finally {
    await f.close();
  }
});

test('a tool call written as text is recognised; plain replies are not', () => {
  // As the 30B local model wrote it on the turn offered no tool (2026-11-11).
  assert.equal(
    looksLikeToolCall(
      '<atem:function_calls>\n<atem:invoke name="observe_task">\n</atem:invoke>\n</atem:function_calls>',
    ),
    true,
  );
  assert.equal(looksLikeToolCall('{"name": "observe_task", "arguments": {}}'), true);
  assert.equal(looksLikeToolCall('The sphere is blue now, and the check passed.'), false);
  assert.equal(looksLikeToolCall('I could invoke the check again if you like.'), false);
  // Answers, not calls (AUDIT2 S1): JSON the person asked for, and markup or code quoted in a sentence.
  assert.equal(looksLikeToolCall('{"name": "my-app", "version": "1.0.0"}'), false);
  assert.equal(looksLikeToolCall('I changed `<button onclick=invoke()>` to use a handler.'), false);
  assert.equal(looksLikeToolCall('Use a `<tool_call>` tag? No.'), false);
  assert.equal(looksLikeToolCall('The package says {"name": "app"} in its first line.'), false);
  assert.equal(looksLikeToolCall('  {"tool": "read_projects"}'), true);
});

test('on the turn offered no tool, the host says to answer, and a written tool call is never the reply', async () => {
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    turn: async (messages) => {
      seen.push([...messages]);
      return seen.length === 1
        ? answer([{ name: 'continue_task', arguments: { text: 'Make it blue.' } }])
        : answer(
            [],
            '<atem:function_calls>\n<atem:invoke name="observe_task">\n</atem:invoke>\n</atem:function_calls>',
          );
    },
  });
  try {
    const id = f.submit();
    for (let i = 0; i < 10; i++) await settle();
    f.emit('runtime.turn.completed', { turn_id: 'existing-turn' });
    for (let i = 0; i < 10; i++) await settle();
    assert.ok(
      (seen[1] ?? []).some(
        (message) => message.role === 'system' && message.content === ANSWER_NOW,
      ),
    );
    const request = f.coordinator.read(id, { kind: 'local' });
    assert.equal(request?.status, 'interrupted');
    assert.ok(
      !(request?.reply?.text ?? '').includes('function_calls'),
      'the markup is not the reply',
    );
    // Asked once more, offered no tool and told why; a second written call ends it.
    assert.equal(seen.length, 3);
    assert.ok(
      (seen[2] ?? []).some(
        (message) => message.role === 'system' && message.content === ANSWER_AGAIN,
      ),
    );
    assert.equal(
      request?.steps.filter((step) => step.tool_name === 'model' && step.status === 'rejected')
        .length,
      2,
    );
  } finally {
    await f.close();
  }
});

test("on the planning budget's last turn the model is offered no tool and answers, instead of reaching the bound", async () => {
  const offered: string[][] = [];
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    turn: async (messages, tools) => {
      offered.push((tools ?? []).map((tool) => tool.function.name));
      seen.push([...messages]);
      // A model that would read for ever, as the 30B did on an all-work request.
      return (tools ?? []).length > 0
        ? answer([{ name: 'read_workspace', arguments: {} }], '')
        : answer([], 'Here is what I read so far.');
    },
  });
  try {
    const id = f.submit();
    for (let i = 0; i < 20; i++) await settle();
    const request = f.coordinator.read(id, { kind: 'local' });
    // The last two turns are answers: the first answer ends it.
    assert.equal(offered.length, 7);
    assert.deepEqual(offered.at(-1), []);
    assert.ok((offered.at(-2) ?? []).length > 0);
    assert.ok(
      (seen.at(-1) ?? []).some(
        (message) => message.role === 'system' && message.content === ANSWER_LAST,
      ),
    );
    assert.equal(request?.status, 'responded');
    assert.equal(request?.reply?.text, 'Here is what I read so far.');
  } finally {
    await f.close();
  }
});

test('a tool call written as text on an answer turn is asked again once, and the answer then is the reply', async () => {
  const offered: string[][] = [];
  const f = await fixture({
    turn: async (_messages, tools) => {
      offered.push((tools ?? []).map((tool) => tool.function.name));
      if ((tools ?? []).length > 0) return answer([{ name: 'read_workspace', arguments: {} }], '');
      // The 30B model on the last turns of an all-work request and a continue request (2026-11-11).
      return offered.filter((names) => names.length === 0).length === 1
        ? answer([], '<function_calls><invoke name="read_projects"></invoke></function_calls>')
        : answer([], 'The blue change is in, and the check passed.');
    },
  });
  try {
    const id = f.submit();
    for (let i = 0; i < 20; i++) await settle();
    const request = f.coordinator.read(id, { kind: 'local' });
    assert.equal(offered.length, 8);
    assert.deepEqual(offered.slice(-2), [[], []]);
    assert.equal(request?.status, 'responded');
    assert.equal(request?.reply?.text, 'The blue change is in, and the check passed.');
    assert.ok(
      request?.steps.some((step) => step.tool_name === 'model' && step.status === 'rejected'),
    );
  } finally {
    await f.close();
  }
});

test('every answer is asked for in plain words: no field names, ids or timestamps, on a turn with tools and on the answering turn', async () => {
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    turn: async (messages, tools) => {
      seen.push([...messages]);
      return (tools ?? []).length > 0 && seen.length === 1
        ? answer([{ name: 'read_workspace', arguments: {} }], '')
        : answer([], 'The check passed.');
    },
  });
  try {
    f.submit();
    for (let i = 0; i < 20; i++) await settle();
    assert.ok(seen[0]?.[0]?.content.endsWith(PLAIN_WORDS), 'the first turn');
    // The list's guidance goes only beside a list (ADR 0060): a task here has one.
    assert.equal(
      seen[0]?.some((message) => message.content === LIST_GUIDE),
      (seen[0] ?? []).some((message) => message.content.includes('"can_be_done_here"')),
    );
    assert.ok(ANSWERING.includes(PLAIN_WORDS), 'the answering turn');
    assert.match(PLAIN_WORDS, /never field names, ids, timestamps/);
  } finally {
    await f.close();
  }
});

test('a turn offered no tool is given an answering conversation: no tool call to copy, what was read as quoted data', async () => {
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    turn: async (messages, tools) => {
      seen.push([...messages]);
      return (tools ?? []).length > 0
        ? answer([{ name: 'read_workspace', arguments: {} }], '')
        : answer([], 'Here is what I read.');
    },
  });
  try {
    const id = f.submit();
    for (let i = 0; i < 20; i++) await settle();
    const last = seen.at(-1) ?? [];
    assert.equal(last[0]?.role, 'system');
    assert.equal(last[0]?.content, ANSWERING);
    assert.equal(
      last.some((message) => message.role === 'assistant' || message.role === 'tool'),
      false,
    );
    assert.ok(readOf(last, 'read_workspace').length > 0, 'what was read reaches the answer');
    assert.ok(last.some((message) => message.role === 'system' && message.content === ANSWER_LAST));
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.reply?.text, 'Here is what I read.');
  } finally {
    await f.close();
  }
});

test('one result is offered only after the list read_results gives, and never on the first turn', async () => {
  const offered: string[][] = [];
  const f = await fixture({
    turn: async (_messages, tools) => {
      offered.push((tools ?? []).map((tool) => tool.function.name));
      if (offered.length === 1) return answer([{ name: 'observe_task', arguments: {} }], '');
      if (offered.length === 2) return answer([{ name: 'read_results', arguments: {} }], '');
      return answer([], 'Read.');
    },
  });
  try {
    f.submit();
    for (let i = 0; i < 20; i++) await settle();
    assert.ok(!offered[0]?.includes('read_results'));
    assert.ok(offered[1]?.includes('read_results'));
    assert.ok(!offered[1]?.includes('read_result'));
    assert.ok(offered[2]?.includes('read_result'));
  } finally {
    await f.close();
  }
});

test('a request that ends with no reply carries what the computer saw, and one that answers does not', async () => {
  const text = '<function_calls><invoke name="read_workspace"></invoke></function_calls>';
  const f = await fixture({
    turn: async (_messages, tools) =>
      (tools ?? []).length > 0
        ? answer([{ name: 'observe_task', arguments: {} }], '')
        : answer([], text),
  });
  try {
    const id = f.submit();
    for (let i = 0; i < 30; i++) await settle();
    const ended = f.coordinator.read(id, { kind: 'local' });
    assert.equal(ended?.status, 'interrupted');
    assert.equal(ended?.reply, null);
    const observed = f.controlPlane.projection.workspaceRequest(id)?.observed;
    assert.ok(observed?.task, 'the task facts');
    assert.equal(observed?.task?.sent, false, "the person's words did not reach the agent");
    assert.equal(typeof observed?.task?.turns, 'number');
    assert.equal(observed?.tasks, null);
    // Facts only: nothing the model wrote.
    assert.ok(!JSON.stringify(observed).includes('function_calls'));
  } finally {
    await f.close();
  }
  const g = await fixture({ turn: async () => answer([], 'It works, the agent says.') });
  try {
    const id = g.submit();
    for (let i = 0; i < 10; i++) await settle();
    assert.equal(g.coordinator.read(id, { kind: 'local' })?.status, 'responded');
    assert.equal(
      g.controlPlane.projection.workspaceRequest(id)?.observed,
      undefined,
      'never beside a reply',
    );
  } finally {
    await g.close();
  }
});

test('a request about all work that ends with no reply carries the projects and tasks as counts', async () => {
  const f = await fixture({
    turn: async (_messages, tools) =>
      (tools ?? []).length > 0
        ? answer([{ name: 'read_projects', arguments: {} }], '')
        : answer([], '{"name": "read_projects"}'),
  });
  try {
    const outcome = submitAllWork(f);
    for (let i = 0; i < 30; i++) await settle();
    const created = outcome.command?.result;
    const id = created?.kind === 'workspace_request_created' ? created.request_id : '';
    assert.equal(f.controlPlane.projection.workspaceRequest(id)?.status, 'interrupted');
    const observed = f.controlPlane.projection.workspaceRequest(id)?.observed;
    assert.equal(observed?.task, null);
    assert.equal(observed?.projects?.total, f.controlPlane.projection.projects().length);
    assert.equal(typeof observed?.tasks?.total, 'number');
  } finally {
    await f.close();
  }
});

test("the computer's facts give a check only where it is this task's own folder's, never another task's copy (AUDIT2 1)", async () => {
  const text = '<function_calls><invoke name="read_workspace"></invoke></function_calls>';
  const f = await fixture({
    turn: async (_messages, tools) =>
      (tools ?? []).length > 0
        ? answer([{ name: 'observe_task', arguments: {} }], '')
        : answer([], text),
  });
  const findings = (passed: boolean) => ({
    tsc: { exit: passed ? 0 : 2, seconds: 1 },
    build: { exit: 0, seconds: 1 },
    page: null,
    unknown: null,
    passed,
    reads_confined: true,
  });
  const check = (passed: boolean, workstreamId?: string) =>
    f.controlPlane.recorder.record(
      controlPlaneDraft(
        'project.check.recorded',
        { project_id: f.projectId, workstream_id: null, execution_id: null },
        {
          check_id: f.ids.next() as never,
          findings: findings(passed) as never,
          ...(workstreamId === undefined ? {} : { workstream_id: workstreamId as never }),
        },
        '2026-11-11T10:00:00.000Z' as never,
        NO_CAUSE,
      ),
    );
  try {
    // Another task's own copy passed its check: not this task's.
    check(true, '01a19000-0000-7000-8000-0000000000e1');
    const first = f.submit();
    for (let i = 0; i < 30; i++) await settle();
    assert.equal(f.controlPlane.projection.workspaceRequest(first)?.observed?.task?.check, null);
    // The project folder's own check, where this task works: this task's.
    check(false);
    const second = f.submit();
    for (let i = 0; i < 30; i++) await settle();
    assert.deepEqual(f.controlPlane.projection.workspaceRequest(second)?.observed?.task?.check, {
      passed: false,
      files: f.controlPlane.projection.latestCheck(f.projectId)?.files,
    });
  } finally {
    await f.close();
  }
});

test("the computer's facts never stand beside a reply, an earlier one included (AUDIT2 2)", async () => {
  let resumed = false;
  const f = await fixture({
    turn: async (_messages, tools) => {
      if (!resumed) return answer([], 'The first answer.');
      return (tools ?? []).length > 0
        ? answer([{ name: 'observe_task', arguments: {} }], '')
        : answer([], '{"name": "read_workspace"}');
    },
  });
  try {
    const id = f.submit();
    for (let i = 0; i < 10; i++) await settle();
    assert.equal(f.coordinator.read(id, { kind: 'local' })?.status, 'responded');
    resumed = true;
    f.controlPlane.commands.submit(f.command('resume', id, 'And whether it works?'), 'http', {
      kind: 'local',
    });
    for (let i = 0; i < 30; i++) await settle();
    const ended = f.controlPlane.projection.workspaceRequest(id);
    assert.equal(ended?.status, 'interrupted');
    assert.equal(ended?.reply?.text, 'The first answer.');
    assert.equal(
      ended?.observed,
      undefined,
      'answered, resumed, then interrupted: no facts beside the reply',
    );
  } finally {
    await f.close();
  }
});

// ADR 0060: what can be done here, read from the host's own list (CORE's `CommandService.here`).
const VERSION = '01a1ce13-0000-7000-8000-000000000001';

function submitIn(
  f: Awaited<ReturnType<typeof fixture>>,
  text: string,
  extra: Record<string, unknown> = {},
) {
  const outcome = f.controlPlane.commands.submit(
    {
      schema_version: 1,
      command_id: f.ids.next() as CommandId,
      issued_at: f.time.now().toISOString(),
      client: { name: 'synthetic', version: null, device_label: null },
      command_type: 'workspace.request.submit',
      payload: {
        project_id: f.projectId,
        workstream_id: null,
        execution_id: null,
        text,
        ...extra,
      } as never,
    },
    'http',
    { kind: 'local' },
  );
  assert.equal(outcome.disposition, 'accepted', JSON.stringify(outcome.command?.rejection));
  const result = outcome.command?.result;
  return result?.kind === 'workspace_request_created' ? result.request_id : '';
}
const offeredTypes = (tools: readonly ToolDefinition[] | undefined) =>
  (
    tools?.find((tool) => tool.function.name === 'propose_command')?.function.parameters as
      | { properties: { command_type: { enum: string[] } } }
      | undefined
  )?.properties.command_type.enum ?? [];

test("a model proposes New Task from the host's list in a project: the host prepares it, and it waits for one press (ADR 0060)", async () => {
  const offered: (readonly ToolDefinition[])[] = [];
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    turn: async (messages, tools) => {
      offered.push(tools ?? []);
      seen.push([...messages]);
      return answer([
        {
          name: 'propose_command',
          arguments: { command_type: 'workstream.create', values: { title: 'Night Sky' } },
        },
      ]);
    },
  });
  try {
    const tasksBefore = f.controlPlane.projection.workstreams(f.projectId).length;
    const id = submitIn(f, 'Start a task called Night Sky.');
    for (let i = 0; i < 8; i++) await settle();
    // The tool offers the list's sayable entries, from the host.
    assert.ok(offeredTypes(offered[0]).includes('workstream.create'));
    assert.match(
      seen[0]?.find((m) => m.content.startsWith('Host context data'))?.content ?? '',
      /"can_be_done_here":\[\{"command_type":"workstream.create","title":"New Task"/,
    );
    assert.ok(seen[0]?.some((m) => m.role === 'system' && m.content === LIST_GUIDE));
    const request = f.controlPlane.projection.workspaceRequest(id);
    assert.equal(request?.status, 'waiting_for_human');
    const step = request?.steps.find((candidate) => candidate.tool_name === 'propose_command');
    assert.equal(step?.status, 'waiting');
    assert.equal(step?.proposal?.command_type, 'workstream.create');
    assert.equal(step?.proposal?.confirm, 'none');
    // The words that go out are on the proposal, by their labels, exactly as they will be sent (AUDIT2's N2).
    assert.deepEqual(step?.proposal?.said, [{ label: 'Title', words: 'Night Sky' }]);
    // Nothing happened on the model's word, and the model is not asked again while it waits.
    assert.equal(f.controlPlane.projection.workstreams(f.projectId).length, tasksBefore);
    assert.equal(f.turns, 1);
    const recorded = [...f.journal.readAll()]
      .map((stored) => stored.event)
      .find(
        (event) =>
          event.event_type === 'workspace.request.step_recorded' &&
          event.payload.step.tool_name === 'propose_command',
      );
    assert.ok(recorded?.event_type === 'workspace.request.step_recorded');
    const proposed = recorded.payload.proposed_command;
    assert.equal(proposed?.command_type, 'workstream.create');
    assert.equal(
      (proposed?.payload as { project_id: string } | undefined)?.project_id,
      f.projectId,
    );
    assert.equal((proposed?.payload as { title: string } | undefined)?.title, 'Night Sky');
    assert.equal(proposed?.command_id, step?.command_id);
    // What the press will carry is the digest of exactly what was prepared.
    assert.equal(step?.proposal?.review, reviewDigest('workstream.create', proposed?.payload, {}));
    f.controlPlane.commands.submit(f.command('cancel', id), 'http', { kind: 'local' });
    await settle();
    assert.equal(
      f.controlPlane.projection.workspaceRequest(id)?.steps.find((s) => s.step_id === step?.step_id)
        ?.status,
      'rejected',
    );
  } finally {
    await f.close();
  }
});

test('a proposal never carries an id of the model, an entry not listed here, or one never sayable (ADR 0060, R6)', async () => {
  let turn = 0;
  const offered: (readonly ToolDefinition[])[] = [];
  const f = await fixture({
    turn: async (_messages, tools) => {
      offered.push(tools ?? []);
      turn++;
      if (turn === 1)
        return answer([
          {
            name: 'propose_command',
            arguments: {
              command_type: 'workstream.create',
              values: { title: 'X', project_id: VERSION },
            },
          },
        ]);
      if (turn === 2)
        return answer([
          {
            name: 'propose_command',
            arguments: { command_type: 'execution.answer_question', values: {} },
          },
        ]);
      if (turn === 3)
        return answer([
          { name: 'propose_command', arguments: { command_type: 'workstream.create', values: {} } },
        ]);
      return answer([], 'Nothing was prepared.');
    },
  });
  try {
    const id = submitIn(f, 'Start a task.');
    for (let i = 0; i < 10; i++) await settle();
    assert.ok(!offeredTypes(offered[0]).includes('execution.answer_question'));
    const steps = f.controlPlane.projection
      .workspaceRequest(id)
      ?.steps.filter((s) => s.tool_name === 'propose_command');
    assert.deepEqual(
      steps?.map((s) => s.status),
      ['rejected', 'rejected', 'rejected'],
    );
    assert.equal(steps?.[1]?.summary, 'That is not one of the things that can be done here now.');
    assert.equal(
      steps?.some((s) => s.proposal),
      false,
    );
  } finally {
    await f.close();
  }
});

test('read_choices reads a host list by its name, and a choice is never made from a list unread (ADR 0060)', async () => {
  let turn = 0;
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    turn: async (messages) => {
      seen.push([...messages]);
      turn++;
      if (turn === 1)
        return answer([
          {
            name: 'propose_command',
            arguments: {
              command_type: 'workstream.create',
              values: { title: 'Y', from_checkpoint: 0 },
            },
          },
        ]);
      if (turn === 2) return answer([{ name: 'read_choices', arguments: { list: 'tasks' } }]);
      return answer([], 'There is one task.');
    },
  });
  try {
    const id = submitIn(f, 'Which tasks are there, and start one from a version.');
    for (let i = 0; i < 10; i++) await settle();
    const steps = f.controlPlane.projection.workspaceRequest(id)?.steps ?? [];
    assert.equal(steps[0]?.summary, 'Read the list versions first, then choose from it.');
    assert.equal(steps[1]?.tool_name, 'read_choices');
    assert.equal(steps[1]?.status, 'confirmed');
    const tool = (seen[2] ?? []).find((m) => m.role === 'tool' && m.tool_name === 'read_choices');
    assert.match(tool?.content ?? '', /"list":"tasks"/);
  } finally {
    await f.close();
  }
});

test('go_to picks one of the places the client named, by index, and the client goes at once (ADR 0060)', async () => {
  let turn = 0;
  const f = await fixture({
    turn: async () => {
      turn++;
      return turn === 1
        ? answer([{ name: 'go_to', arguments: { index: 1 } }])
        : answer([], 'Taking you to your world.');
    },
  });
  try {
    const id = submitIn(f, 'Show my world.', { places: ['My Projects', 'My World', 'Settings'] });
    for (let i = 0; i < 6; i++) await settle();
    const request = f.controlPlane.projection.workspaceRequest(id);
    const step = request?.steps.find((candidate) => candidate.tool_name === 'go_to');
    assert.equal(step?.place, 1);
    assert.equal(step?.status, 'confirmed');
    assert.equal(request?.status, 'responded');
  } finally {
    await f.close();
  }
});

test("the person's own press goes through the host's one preparation: done at once, with no model asked (ADR 0060, section 2)", async () => {
  const f = await fixture();
  try {
    const before = f.controlPlane.projection.workstreams(f.projectId).length;
    const id = submitIn(f, 'New Task', {
      act: { command_type: 'workstream.create', values: { title: 'Night Sky' }, lists: {} },
    });
    for (let i = 0; i < 6; i++) await settle();
    assert.equal(f.turns, 0, 'no model is asked for a press');
    const request = f.controlPlane.projection.workspaceRequest(id);
    assert.equal(request?.status, 'responded');
    const step = request?.steps.find((candidate) => candidate.tool_name === 'act');
    assert.equal(step?.proposal?.confirm, 'none');
    assert.equal(step?.status, 'confirmed');
    const tasks = f.controlPlane.projection.workstreams(f.projectId);
    assert.equal(tasks.length, before + 1);
    assert.ok(tasks.some((task) => task.title === 'Night Sky'));
    // An entry the host does not list here does nothing.
    const absent = submitIn(f, 'Check', {
      act: { command_type: 'project.check', values: {}, lists: {} },
    });
    for (let i = 0; i < 6; i++) await settle();
    assert.equal(
      f.controlPlane.projection.workspaceRequest(absent)?.steps[0]?.summary,
      'That is not one of the things that can be done here now.',
    );
    assert.equal(f.turns, 0);
  } finally {
    await f.close();
  }
});

test("the person's one press confirms a waiting proposal through resume: only its owner, only with the digest it was shown (ADR 0060)", async () => {
  let turn = 0;
  const f = await fixture({
    turn: async () => {
      turn++;
      return turn === 1
        ? answer([
            {
              name: 'propose_command',
              arguments: { command_type: 'workstream.create', values: { title: 'Night Sky' } },
            },
          ])
        : answer([], 'Night Sky has started.');
    },
  });
  try {
    const id = submitIn(f, 'Start a task called Night Sky.');
    for (let i = 0; i < 8; i++) await settle();
    const step = f.controlPlane.projection.workspaceRequest(id)?.steps.find((s) => s.proposal);
    assert.equal(step?.status, 'waiting');
    const digest = step?.proposal?.review ?? '';
    const confirm = (review: string, principal: Principal = { kind: 'local' }) =>
      f.controlPlane.commands.submit(
        {
          ...f.command('resume', id, null),
          payload: {
            request_id: id as never,
            text: null,
            confirm: { step_id: step?.step_id as never, review },
          },
        } as never,
        'http',
        principal,
      );
    const before = f.controlPlane.projection.workstreams(f.projectId).length;
    // Another principal cannot confirm what this person was shown.
    assert.equal(
      confirm(digest, { kind: 'device', device_id: VERSION as never }).disposition,
      'rejected',
    );
    // A digest that is not the one shown does nothing.
    assert.notEqual(confirm('c'.repeat(64)).command?.status, 'completed');
    for (let i = 0; i < 4; i++) await settle();
    assert.equal(f.controlPlane.projection.workstreams(f.projectId).length, before);
    // The owner's press with the digest as shown: the task starts, and the model says so.
    const pressed = confirm(digest);
    assert.equal(pressed.disposition, 'accepted', JSON.stringify(pressed.command?.rejection));
    for (let i = 0; i < 8; i++) await settle();
    assert.equal(f.controlPlane.projection.workstreams(f.projectId).length, before + 1);
    const after = f.controlPlane.projection.workspaceRequest(id);
    assert.equal(after?.steps.find((s) => s.step_id === step?.step_id)?.status, 'confirmed');
    assert.equal(after?.status, 'responded');
    assert.equal(after?.reply?.text, 'Night Sky has started.');
    // Confirmed once: a second press finds nothing waiting.
    assert.notEqual(confirm(digest).command?.status, 'completed');
    for (let i = 0; i < 4; i++) await settle();
    assert.equal(f.controlPlane.projection.workstreams(f.projectId).length, before + 1);
  } finally {
    await f.close();
  }
});

test('new words from the person before the press replace the waiting proposal, and the model is asked again (AUDIT2, 1111-b77)', async () => {
  let turn = 0;
  const f = await fixture({
    turn: async () => {
      turn++;
      const title = turn === 1 ? 'Night Sky' : 'Day Sky';
      return answer([
        {
          name: 'propose_command',
          arguments: { command_type: 'workstream.create', values: { title } },
        },
      ]);
    },
  });
  try {
    const id = submitIn(f, 'Start a task called Night Sky.');
    for (let i = 0; i < 8; i++) await settle();
    const first = f.controlPlane.projection.workspaceRequest(id)?.steps.find((s) => s.proposal);
    assert.equal(first?.status, 'waiting');
    await f.time.advance(8_000);
    const words = f.controlPlane.commands.submit(
      f.command('resume', id, 'No, call it Day Sky instead.'),
      'http',
      {
        kind: 'local',
      },
    );
    assert.equal(words.disposition, 'accepted');
    for (let i = 0; i < 8; i++) await settle();
    const request = f.controlPlane.projection.workspaceRequest(id);
    assert.equal(request?.steps.find((s) => s.step_id === first?.step_id)?.status, 'rejected');
    assert.equal(turn, 2);
    const waiting = request?.steps.filter((s) => s.proposal && s.status === 'waiting') ?? [];
    assert.equal(waiting.length, 1);
    assert.notEqual(waiting[0]?.step_id, first?.step_id);
    const old = f.controlPlane.commands.submit(
      {
        ...f.command('resume', id, null),
        payload: {
          request_id: id as never,
          text: null,
          confirm: { step_id: first?.step_id as never, review: first?.proposal?.review ?? '' },
        },
      } as never,
      'http',
      { kind: 'local' },
    );
    assert.notEqual(old.command?.status, 'completed');
    for (let i = 0; i < 4; i++) await settle();
    assert.ok(
      !f.controlPlane.projection
        .workstreams(f.projectId)
        .some((task) => task.title === 'Night Sky'),
    );
  } finally {
    await f.close();
  }
});

test("an answer that reads like the computer's records is asked for once more in sentences, and only that one is kept", async () => {
  const seen: ToolMessage[][] = [];
  const f = await fixture({
    requestText: 'Did its check pass?',
    turn: async (messages) => {
      seen.push([...messages]);
      return seen.length === 1
        ? answer(
            [],
            'Yes.\n* check state: passed\n* type_check: passed\n* first_errors []\nRecorded at 2026-11-12T04:44:49.108Z.',
          )
        : answer([], 'Yes. It type-checks, it builds, and the page drew with no errors.');
    },
  });
  try {
    const id = f.submit();
    for (let i = 0; i < 10; i++) await settle();
    const request = f.coordinator.read(id, { kind: 'local' });
    assert.equal(seen.length, 2);
    assert.ok(
      seen[1]?.some((message) => message.role === 'system' && message.content === ANSWER_IN_WORDS),
    );
    assert.equal(
      request?.reply?.text,
      'Yes. It type-checks, it builds, and the page drew with no errors.',
    );
    assert.ok(
      request?.steps.some(
        (step) =>
          step.tool_name === 'model' &&
          step.status === 'rejected' &&
          step.summary.includes('a time'),
      ),
    );
  } finally {
    await f.close();
  }
});

test("a proposal the domain's admission refuses is refused when it is made, never shown for a press (the 30B window at 06:36Z)", async () => {
  let turn = 0;
  const f = await fixture({
    turn: async () => {
      turn++;
      if (turn === 1) return answer([{ name: 'read_choices', arguments: { list: 'versions' } }]);
      if (turn === 2)
        return answer([
          {
            name: 'propose_command',
            arguments: {
              command_type: 'workstream.create',
              values: { title: 'Night Sky', from_checkpoint: 0 },
            },
          },
        ]);
      return answer([], 'That version cannot be used.');
    },
  });
  try {
    // A version of another task's own copy, after a turn in it, whose work was never added to the project (AUDIT3 S2).
    const other = f.controlPlane.commands.submit(
      f.commands.createWorkstream(f.projectId, { title: 'Paths', objective: null } as never),
      'internal',
    );
    const otherId =
      other.command?.result?.kind === 'workstream_created'
        ? other.command.result.workstream_id
        : (undefined as never);
    const folder = '/tasks/synthetic/paths/app';
    const at = f.time.now().toISOString();
    const scope = { project_id: f.projectId, workstream_id: otherId, execution_id: null };
    f.controlPlane.recorder.record(
      controlPlaneDraft(
        'workstream.copy_made',
        scope,
        { directory: folder, base_tree: 'a'.repeat(64), files: 1 },
        at,
        NO_CAUSE,
      ),
    );
    const executionId = f.ids.next() as ExecutionId;
    const runtime = f.controlPlane.projection.execution(f.execution)?.runtime as never;
    f.controlPlane.recorder.record(
      controlPlaneDraft(
        'execution.created',
        { ...scope, execution_id: executionId },
        { runtime, instruction: 'Do the work.', directory: folder },
        at,
        NO_CAUSE,
      ),
    );
    f.controlPlane.recorder.record(
      controlPlaneDraft(
        'project.checkpoint.recorded',
        { project_id: f.projectId, workstream_id: null, execution_id: null },
        {
          checkpoint_id: VERSION as never,
          reason: 'after_turn',
          execution_id: executionId,
          tree: 'b'.repeat(64),
          files: 1,
          project_head: null,
        },
        at,
        NO_CAUSE,
      ),
    );
    const id = submitIn(f, 'Start a task called Night Sky from the newest version.');
    for (let i = 0; i < 10; i++) await settle();
    const request = f.controlPlane.projection.workspaceRequest(id);
    const proposed = request?.steps.filter((s) => s.tool_name === 'propose_command') ?? [];
    assert.equal(
      proposed.length,
      1,
      JSON.stringify(request?.steps.map((s) => [s.tool_name, s.status, s.summary])),
    );
    assert.equal(proposed[0]?.status, 'rejected');
    // Refused in the host's words: by the one preparation, since CORE judges each row by the whole admission
    // (cee89ab88), or else by the admission run at the proposal. Either way it never waits for a press.
    assert.match(
      proposed[0]?.summary ?? '',
      /cannot be the from version for this|never added to the project/,
    );
    assert.equal(proposed[0]?.proposal, undefined);
    assert.ok(!request?.steps.some((s) => s.status === 'waiting'));
    assert.notEqual(request?.status, 'waiting_for_human');
  } finally {
    await f.close();
  }
});
