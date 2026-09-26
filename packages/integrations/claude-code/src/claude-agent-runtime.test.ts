import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, dirname, join } from 'node:path';
import { after, describe, test } from 'node:test';
import type {
  NonNullableUsage,
  Options,
  PermissionResult,
  SDKAssistantMessage,
  SDKMessage,
  SDKResultError,
  SDKResultMessage,
  SDKResultSuccess,
  SDKSystemMessage,
  SDKUserMessage,
} from '@anthropic-ai/claude-agent-sdk';
import {
  compileValidator,
  type ExecutionId,
  type ProjectId,
  parseEventEnvelope,
  RuntimeDescriptor,
  type RuntimeId,
  type RuntimeOptions,
  type WorkstreamId,
} from '@halcyonic/contracts';
import {
  capabilityProblems,
  createVirtualTime,
  type ExecutionContext,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import {
  ClaudeAgentRuntimeAdapter,
  type ClaudeAgentRuntimeOptions,
  type QueryHandle,
} from './claude-agent-runtime.ts';

const WORKDIR = mkdtempSync(join(tmpdir(), 'halcyonic-claude-agent-'));
after(() => rmSync(WORKDIR, { recursive: true, force: true }));

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

const execution: ExecutionContext = {
  execution_id: '01920000-0000-7000-8000-000000000003' as ExecutionId,
  workstream_id: '01920000-0000-7000-8000-000000000002' as WorkstreamId,
  project_id: '01920000-0000-7000-8000-000000000001' as ProjectId,
};

const INHERITED = {
  PATH: '/usr/bin:/bin',
  HOME: '/home/tester',
  ANTHROPIC_API_KEY: 'test-key-not-real',
  SALIDIUM_INTERNAL: '1',
  UNRELATED_SECRET: 'not for agents',
};

/** A test-controlled stream, standing in for the messages a Claude Code process writes. */
class Channel<T> implements AsyncIterable<T> {
  readonly #items: T[] = [];
  #waiting: {
    resolve: (result: IteratorResult<T>) => void;
    reject: (error: Error) => void;
  } | null = null;
  #ended = false;
  #failure: Error | null = null;

  push(item: T): void {
    const waiting = this.#waiting;
    this.#waiting = null;
    if (waiting !== null) waiting.resolve({ done: false, value: item });
    else this.#items.push(item);
  }

  end(): void {
    this.#ended = true;
    this.#waiting?.resolve({ done: true, value: undefined });
    this.#waiting = null;
  }

  fail(error: Error): void {
    this.#failure = error;
    this.#waiting?.reject(error);
    this.#waiting = null;
  }

  [Symbol.asyncIterator](): AsyncIterator<T> {
    return {
      next: () => {
        const item = this.#items.shift();
        if (item !== undefined) return Promise.resolve({ done: false, value: item });
        if (this.#failure !== null) return Promise.reject(this.#failure);
        if (this.#ended) return Promise.resolve({ done: true, value: undefined });
        return new Promise((resolve, reject) => {
          this.#waiting = { resolve, reject };
        });
      },
    };
  }
}

/** One scripted SDK query: records what the adapter sends and replays what a test emits. */
class ScriptedRun implements QueryHandle {
  readonly options: Options;
  readonly delivered: SDKUserMessage[] = [];
  readonly #output = new Channel<SDKMessage>();
  interrupts = 0;
  interruptFailure: Error | null = null;
  closed = false;

  constructor(params: { prompt: AsyncIterable<SDKUserMessage>; options: Options }) {
    this.options = params.options;
    void this.#read(params.prompt);
  }

  get sessionId(): string {
    const sessionId = this.options.sessionId;
    if (sessionId === undefined) throw new Error('the adapter did not choose a session id');
    return sessionId;
  }

  emit(...messages: SDKMessage[]): void {
    for (const message of messages) this.#output.push(message);
  }

  end(): void {
    this.#output.end();
  }

  fail(error: Error): void {
    this.#output.fail(error);
  }

  /** Calls the adapter's canUseTool the way the SDK does for a permission request. */
  requestPermission(
    toolName: string,
    input: Record<string, unknown>,
    requestId: string,
    signal: AbortSignal = new AbortController().signal,
  ): Promise<PermissionResult | null> {
    const canUseTool = this.options.canUseTool;
    if (canUseTool === undefined) throw new Error('the adapter did not pass canUseTool');
    return canUseTool(toolName, input, { signal, toolUseID: `toolu_${requestId}`, requestId });
  }

  async interrupt(): Promise<undefined> {
    this.interrupts += 1;
    if (this.interruptFailure !== null) throw this.interruptFailure;
    return undefined;
  }

  close(): void {
    this.closed = true;
    this.#output.end();
  }

  [Symbol.asyncIterator](): AsyncIterator<SDKMessage> {
    return this.#output[Symbol.asyncIterator]();
  }

  async #read(prompt: AsyncIterable<SDKUserMessage>): Promise<void> {
    for await (const message of prompt) this.delivered.push(message);
  }
}

const RESULT_USAGE: NonNullableUsage = {
  cache_creation: { ephemeral_1h_input_tokens: 0, ephemeral_5m_input_tokens: 0 },
  cache_creation_input_tokens: 0,
  cache_read_input_tokens: 0,
  fallback_credit: { status: { type: 'not_applied', reason: 'not_enabled' } },
  inference_geo: 'test',
  input_tokens: 10,
  iterations: [],
  output_tokens: 5,
  output_tokens_details: { thinking_tokens: 0 },
  server_tool_use: { web_fetch_requests: 0, web_search_requests: 0 },
  service_tier: 'standard',
  speed: 'standard',
};

function init(sessionId: string): SDKSystemMessage {
  return {
    type: 'system',
    subtype: 'init',
    apiKeySource: 'ANTHROPIC_API_KEY',
    claude_code_version: '2.1.283',
    cwd: WORKDIR,
    tools: ['Bash', 'Edit', 'Read'],
    mcp_servers: [],
    model: 'claude-sonnet-5',
    permissionMode: 'default',
    slash_commands: [],
    output_style: 'default',
    skills: [],
    plugins: [],
    uuid: randomUUID(),
    session_id: sessionId,
  };
}

type ContentBlock = SDKAssistantMessage['message']['content'][number];

function assistant(
  sessionId: string,
  content: ContentBlock[],
  extra: Partial<SDKAssistantMessage> = {},
): SDKAssistantMessage {
  return {
    type: 'assistant',
    message: {
      id: `msg_${randomUUID()}`,
      type: 'message',
      role: 'assistant',
      model: 'claude-sonnet-5',
      content,
      container: null,
      context_management: null,
      diagnostics: null,
      stop_details: null,
      stop_reason: null,
      stop_sequence: null,
      usage: {
        cache_creation: null,
        cache_creation_input_tokens: null,
        cache_read_input_tokens: null,
        fallback_credit: null,
        inference_geo: null,
        input_tokens: 10,
        iterations: null,
        output_tokens: 5,
        output_tokens_details: null,
        server_tool_use: null,
        service_tier: null,
        speed: null,
      },
    },
    parent_tool_use_id: null,
    uuid: randomUUID(),
    session_id: sessionId,
    ...extra,
  };
}

function text(value: string): ContentBlock {
  return { type: 'text', text: value, citations: null };
}

function toolUse(id: string, name: string, input: Record<string, unknown>): ContentBlock {
  return { type: 'tool_use', id, name, input };
}

function toolResult(sessionId: string, toolUseId: string, isError = false): SDKUserMessage {
  return {
    type: 'user',
    message: {
      role: 'user',
      content: [
        { type: 'tool_result', tool_use_id: toolUseId, content: 'output', is_error: isError },
      ],
    },
    parent_tool_use_id: null,
    uuid: randomUUID(),
    session_id: sessionId,
  };
}

function success(sessionId: string, extra: Partial<SDKResultSuccess> = {}): SDKResultMessage {
  return {
    type: 'result',
    subtype: 'success',
    duration_ms: 1200,
    duration_api_ms: 900,
    is_error: false,
    num_turns: 2,
    result: 'Fixed.',
    stop_reason: 'end_turn',
    total_cost_usd: 0,
    usage: RESULT_USAGE,
    modelUsage: {},
    permission_denials: [],
    uuid: randomUUID(),
    session_id: sessionId,
    ...extra,
  };
}

function failure(
  sessionId: string,
  subtype: SDKResultError['subtype'],
  errors: string[],
  extra: Partial<SDKResultError> = {},
): SDKResultMessage {
  return {
    type: 'result',
    subtype,
    duration_ms: 1200,
    duration_api_ms: 900,
    is_error: true,
    num_turns: 3,
    stop_reason: null,
    total_cost_usd: 0,
    usage: RESULT_USAGE,
    modelUsage: {},
    permission_denials: [],
    errors,
    uuid: randomUUID(),
    session_id: sessionId,
    ...extra,
  };
}

/** Lets the adapter's message loop process everything emitted so far. */
async function settle(): Promise<void> {
  for (let i = 0; i < 5; i += 1) await new Promise<void>((resolve) => setImmediate(resolve));
}

function setup(options: Partial<ClaudeAgentRuntimeOptions> = {}) {
  const time = createVirtualTime(new Date('2026-09-26T10:00:00.000Z'));
  const runs: ScriptedRun[] = [];
  const adapter = new ClaudeAgentRuntimeAdapter({
    directoryPolicy: (path: string) => ({ ok: true, directory: path }),
    inheritedEnvironment: INHERITED,
    clock: time,
    query: (params) => {
      const run = new ScriptedRun(params);
      runs.push(run);
      return run;
    },
    ...options,
  });
  const observed: RuntimeObservation[] = [];
  const start = (startOptions: RuntimeOptions = { cwd: WORKDIR }) =>
    adapter.startExecution({
      execution,
      instruction: 'Fix the flaky checkout test.',
      options: startOptions,
      emit: (observation) => observed.push(observation),
    });
  const run = (): ScriptedRun => {
    const latest = runs.at(-1);
    if (latest === undefined) throw new Error('no query was started');
    return latest;
  };
  const types = () => observed.map((observation) => observation.type);
  /** Starts an execution and has Claude Code confirm it. */
  const startConfirmed = async () => {
    const starting = start();
    run().emit(init(run().sessionId));
    await starting;
    return run();
  };
  return { adapter, runs, run, observed, start, startConfirmed, types };
}

function actionError(code: string, effect?: 'none' | 'unknown') {
  return (error: unknown) =>
    error instanceof RuntimeActionError &&
    error.code === code &&
    (effect === undefined || error.effect === effect);
}

function assertContractValid(observations: RuntimeObservation[]): void {
  for (const [index, observation] of observations.entries()) {
    const parsed = parseEventEnvelope({
      schema_version: 1,
      event_id: `01920000-0000-7000-8000-${String(index + 10).padStart(12, '0')}`,
      event_type: observation.type,
      ...execution,
      source: { kind: 'runtime', runtime_id: 'claude-agent' as RuntimeId },
      source_native_id: observation.native_event_id,
      sequence: observation.sequence,
      occurred_at: observation.occurred_at,
      ingested_at: observation.occurred_at,
      correlation_id: null,
      causation_id: null,
      provenance: observation.provenance,
      payload: observation.payload,
    });
    assert.ok(parsed.ok, JSON.stringify(parsed.ok ? null : parsed.issues));
  }
}

describe('starting an execution', () => {
  test('the instruction is delivered, and nothing is claimed until Claude Code confirms the chosen session id', async () => {
    const { start, run, observed, types } = setup();
    const starting = start();
    await settle();
    const scripted = run();
    assert.match(scripted.sessionId, UUID);
    assert.equal(scripted.delivered.length, 1);
    const delivered = scripted.delivered[0];
    assert.deepEqual(delivered?.message, {
      role: 'user',
      content: [{ type: 'text', text: 'Fix the flaky checkout test.' }],
    });
    assert.equal(observed.length, 0);

    scripted.emit(init(scripted.sessionId));
    assert.deepEqual(await starting, { native_id: scripted.sessionId });
    assert.deepEqual(types(), ['runtime.execution.started', 'runtime.turn.started']);
    assert.deepEqual(observed[0]?.payload, { native_id: scripted.sessionId });
    assert.deepEqual(observed[1]?.payload, { turn_id: delivered?.uuid });
    assert.deepEqual(observed[0]?.provenance, {
      epistemic: 'observed',
      native_type: 'claude-agent-sdk/system.init',
    });
  });

  test('when Claude Code reports another session id, that id is the native id', async () => {
    const { start, run, observed } = setup();
    const starting = start();
    const reported = randomUUID();
    run().emit(init(reported));
    assert.deepEqual(await starting, { native_id: reported });
    assert.deepEqual(observed[0]?.payload, { native_id: reported });
  });

  test('a query that ends before Claude Code confirms fails the start with an unknown effect', async () => {
    const { start, run, observed } = setup();
    const starting = start();
    run().fail(new Error('Claude Code process exited with code 1'));
    await assert.rejects(starting, actionError('runtime_exited', 'unknown'));
    assert.deepEqual(observed, []);
  });

  test('a query() that refuses to start fails the start with no effect', async () => {
    const { start } = setup({
      query: () => {
        throw new Error('Native CLI binary for this platform not found.');
      },
    });
    await assert.rejects(start(), actionError('runtime_start_failed', 'none'));
  });

  test('the SDK is asked for streaming input with Halcyonic options and every settings source', async () => {
    const { start, run } = setup({ pathToClaudeCodeExecutable: '/opt/claude/bin/claude' });
    void start({ cwd: WORKDIR, model: 'claude-sonnet-5', permission_mode: 'acceptEdits' });
    const options = run().options;
    assert.equal(options.cwd, WORKDIR);
    assert.match(options.sessionId ?? '', UUID);
    assert.equal(typeof options.canUseTool, 'function');
    assert.equal(options.model, 'claude-sonnet-5');
    assert.equal(options.permissionMode, 'acceptEdits');
    assert.equal(options.pathToClaudeCodeExecutable, '/opt/claude/bin/claude');
    assert.deepEqual(options.systemPrompt, { type: 'preset', preset: 'claude_code' });
    // Leaving settingSources unset loads user settings, so the observers' hooks run.
    assert.equal(options.settingSources, undefined);
    const env = options.env ?? {};
    assert.equal(env.HOME, '/home/tester');
    assert.equal(env.ANTHROPIC_API_KEY, 'test-key-not-real');
    assert.equal(env.SALIDIUM_INTERNAL, undefined);
    assert.equal(env.UNRELATED_SECRET, undefined);
    assert.ok((env.PATH ?? '').split(delimiter).includes(dirname(process.execPath)));
  });

  test('each start chooses a new session id', async () => {
    const { adapter, runs } = setup();
    for (const [index, executionId] of [
      '01920000-0000-7000-8000-000000000013',
      '01920000-0000-7000-8000-000000000023',
    ].entries()) {
      void adapter.startExecution({
        execution: { ...execution, execution_id: executionId as ExecutionId },
        instruction: `Task ${index}`,
        options: { cwd: WORKDIR },
        emit: () => {},
      });
    }
    assert.equal(runs.length, 2);
    assert.notEqual(runs[0]?.sessionId, runs[1]?.sessionId);
  });

  test('starting the same execution twice is refused', async () => {
    const { start } = setup();
    void start();
    await assert.rejects(start(), actionError('duplicate_execution'));
  });
});

describe('observing a turn', () => {
  test('text, tool use and a successful result become agent messages, tool activity and a completed turn', async () => {
    const { startConfirmed, observed, types } = setup();
    const scripted = await startConfirmed();
    const sessionId = scripted.sessionId;
    scripted.emit(
      assistant(sessionId, [text('I will run the tests first.')]),
      assistant(sessionId, [
        toolUse('toolu_1', 'Bash', { command: 'pnpm test', description: 'Run the tests' }),
      ]),
      toolResult(sessionId, 'toolu_1'),
      assistant(sessionId, [
        toolUse('toolu_2', 'Edit', {
          file_path: '/repo/src/checkout.ts',
          old_string: 'a',
          new_string: 'b',
        }),
      ]),
      toolResult(sessionId, 'toolu_2', true),
      success(sessionId),
    );
    await settle();

    assert.deepEqual(types(), [
      'runtime.execution.started',
      'runtime.turn.started',
      'runtime.agent_message',
      'runtime.tool.started',
      'runtime.tool.completed',
      'runtime.tool.started',
      'runtime.tool.completed',
      'runtime.turn.completed',
    ]);
    const message = observed[2];
    assert.equal(message?.type, 'runtime.agent_message');
    assert.deepEqual(message?.payload, { text: 'I will run the tests first.' });
    assert.equal(message?.provenance.epistemic, 'reported');
    assert.deepEqual(observed[3]?.payload, {
      tool_call_id: 'toolu_1',
      tool_name: 'Bash',
      title: 'pnpm test',
    });
    assert.deepEqual(observed[4]?.payload, { tool_call_id: 'toolu_1', outcome: 'succeeded' });
    assert.deepEqual(observed[5]?.payload, {
      tool_call_id: 'toolu_2',
      tool_name: 'Edit',
      title: '/repo/src/checkout.ts',
    });
    assert.deepEqual(observed[6]?.payload, { tool_call_id: 'toolu_2', outcome: 'failed' });
    assert.deepEqual(observed[7]?.payload, { turn_id: scripted.delivered[0]?.uuid });

    assertContractValid(observed);
    assert.deepEqual(
      observed.map((observation) => observation.sequence),
      observed.map((_, index) => index + 1),
    );
    const ids = observed.map((observation) => observation.native_event_id);
    assert.equal(new Set(ids).size, ids.length);
    assert.ok(ids.every((id) => id?.startsWith(`${sessionId}:`)));
    for (const observation of observed) {
      const epistemic = observation.type === 'runtime.agent_message' ? 'reported' : 'observed';
      assert.equal(observation.provenance.epistemic, epistemic);
    }
  });

  test('subagent text, synthetic error text and results for unknown tools are not reported', async () => {
    const { startConfirmed, types } = setup();
    const scripted = await startConfirmed();
    const sessionId = scripted.sessionId;
    scripted.emit(
      assistant(sessionId, [text('A subagent thinking aloud.')], {
        parent_tool_use_id: 'toolu_agent',
      }),
      assistant(sessionId, [text('API Error: overloaded')], { error: 'overloaded' }),
      toolResult(sessionId, 'toolu_never_started'),
      assistant(sessionId, [toolUse('toolu_3', 'Read', { file_path: '/repo/README.md' })], {
        parent_tool_use_id: 'toolu_agent',
      }),
    );
    await settle();
    assert.deepEqual(types().slice(2), ['runtime.tool.started']);
  });

  test('an error result fails the turn with the error it reports', async () => {
    const { startConfirmed, observed } = setup();
    const scripted = await startConfirmed();
    scripted.emit(
      failure(scripted.sessionId, 'error_max_turns', ['Reached maximum number of turns (3)']),
    );
    await settle();
    const last = observed.at(-1);
    assert.equal(last?.type, 'runtime.turn.failed');
    assert.deepEqual(last?.payload, {
      turn_id: scripted.delivered[0]?.uuid,
      error: { code: 'error_max_turns', message: 'Reached maximum number of turns (3)' },
    });
    assertContractValid(observed);
  });

  test('a success result flagged as an error fails the turn with the API error', async () => {
    const { startConfirmed, observed } = setup();
    const scripted = await startConfirmed();
    scripted.emit(success(scripted.sessionId, { is_error: true, result: 'Invalid API key' }));
    await settle();
    const last = observed.at(-1);
    assert.equal(last?.type, 'runtime.turn.failed');
    assert.equal(last?.type === 'runtime.turn.failed' && last.payload.error.code, 'api_error');
    assert.equal(
      last?.type === 'runtime.turn.failed' && last.payload.error.message,
      'Invalid API key',
    );
  });

  test('a query that ends without a result fails the turn with an unknown effect and loses the connection', async () => {
    const { adapter, startConfirmed, observed, types } = setup();
    const scripted = await startConfirmed();
    scripted.end();
    await settle();
    assert.deepEqual(types().slice(-2), ['runtime.turn.failed', 'runtime.connection.lost']);
    const failed = observed.at(-2);
    assert.equal(failed?.type === 'runtime.turn.failed' && failed.payload.error.code, 'no_result');
    assert.deepEqual(failed?.provenance, {
      epistemic: 'inferred',
      native_type: null,
      rule: 'query_ended_without_result',
    });
    assertContractValid(observed);
    await assert.rejects(
      adapter.sendInstruction({ execution, text: 'Try again.' }),
      actionError('runtime_unreachable'),
    );
  });

  test('a turn Claude Code begins by itself is observed without a turn id', async () => {
    const { startConfirmed, observed, types } = setup();
    const scripted = await startConfirmed();
    scripted.emit(
      success(scripted.sessionId),
      init(scripted.sessionId),
      success(scripted.sessionId),
    );
    await settle();
    assert.deepEqual(types().slice(-3), [
      'runtime.turn.completed',
      'runtime.turn.started',
      'runtime.turn.completed',
    ]);
    assert.deepEqual(observed.at(-1)?.payload, { turn_id: null });
    assertContractValid(observed);
  });
});

describe('approvals', () => {
  test('a permission request waits for a person; approval lets the tool run with its input unchanged', async () => {
    const { adapter, startConfirmed, observed } = setup();
    const scripted = await startConfirmed();
    const decision = scripted.requestPermission(
      'Bash',
      { command: 'pnpm db:migrate', description: 'Apply the migration' },
      'req-1',
    );
    await settle();
    const requested = observed.at(-1);
    assert.equal(requested?.type, 'runtime.approval.requested');
    assert.deepEqual(requested?.payload, {
      approval_id: 'req-1',
      subject: { kind: 'tool_use', tool_name: 'Bash', summary: 'pnpm db:migrate' },
    });

    await adapter.respondToApproval({
      execution,
      approval_id: 'req-1',
      decision: 'approve',
      message: null,
    });
    assert.deepEqual(observed.at(-1)?.payload, { approval_id: 'req-1', decision: 'approved' });
    assert.deepEqual(await decision, {
      behavior: 'allow',
      updatedInput: { command: 'pnpm db:migrate', description: 'Apply the migration' },
    });
    await assert.rejects(
      adapter.respondToApproval({
        execution,
        approval_id: 'req-1',
        decision: 'approve',
        message: null,
      }),
      actionError('approval_not_pending'),
    );
    assertContractValid(observed);
  });

  test('a denial tells Claude Code why', async () => {
    const { adapter, startConfirmed, observed } = setup();
    const scripted = await startConfirmed();
    const withReason = scripted.requestPermission(
      'Write',
      { file_path: '/repo/.env', content: 'x' },
      'req-2',
    );
    const withoutReason = scripted.requestPermission(
      'WebFetch',
      { url: 'https://example.com', prompt: 'Read it' },
      'req-3',
    );
    await adapter.respondToApproval({
      execution,
      approval_id: 'req-2',
      decision: 'deny',
      message: 'Do not touch secrets.',
    });
    await adapter.respondToApproval({
      execution,
      approval_id: 'req-3',
      decision: 'deny',
      message: null,
    });
    assert.deepEqual(await withReason, { behavior: 'deny', message: 'Do not touch secrets.' });
    const answer = await withoutReason;
    assert.equal(answer?.behavior, 'deny');
    assert.deepEqual(
      observed.filter((o) => o.type === 'runtime.approval.resolved').map((o) => o.payload),
      [
        { approval_id: 'req-2', decision: 'denied' },
        { approval_id: 'req-3', decision: 'denied' },
      ],
    );
  });

  test('the summary comes from structured input, falling back to the input itself', async () => {
    const { startConfirmed, observed } = setup();
    const scripted = await startConfirmed();
    void scripted.requestPermission(
      'mcp__tracker__create_issue',
      { title: 'Flaky test', labels: ['ci'] },
      'req-4',
    );
    await settle();
    const requested = observed.at(-1);
    assert.equal(
      requested?.type === 'runtime.approval.requested' && requested.payload.subject.summary,
      '{"title":"Flaky test","labels":["ci"]}',
    );
  });
});

describe('interrupting', () => {
  test('an interrupt is confirmed by Claude Code and the aborted turn ends as interrupted', async () => {
    const { adapter, startConfirmed, observed, types } = setup();
    const scripted = await startConfirmed();
    const withdrawal = new AbortController();
    const decision = scripted.requestPermission(
      'Bash',
      { command: 'rm -rf build' },
      'req-5',
      withdrawal.signal,
    );
    await settle();

    await adapter.interrupt({ execution });
    assert.equal(scripted.interrupts, 1);
    // Claude Code withdraws the pending request when it aborts the turn.
    withdrawal.abort();
    await assert.rejects(decision);
    scripted.emit(
      failure(scripted.sessionId, 'error_during_execution', [], {
        terminal_reason: 'aborted_tools',
      }),
    );
    await settle();
    assert.equal(types().at(-1), 'runtime.turn.interrupted');
    assert.deepEqual(observed.at(-1)?.payload, { turn_id: scripted.delivered[0]?.uuid });
    await assert.rejects(
      adapter.respondToApproval({
        execution,
        approval_id: 'req-5',
        decision: 'approve',
        message: null,
      }),
      actionError('approval_not_pending'),
    );
    await assert.rejects(adapter.interrupt({ execution }), actionError('no_running_turn'));
  });

  test('a turn that finished before the interrupt landed still counts as completed', async () => {
    const { adapter, startConfirmed, types } = setup();
    const scripted = await startConfirmed();
    await adapter.interrupt({ execution });
    scripted.emit(success(scripted.sessionId));
    await settle();
    assert.equal(types().at(-1), 'runtime.turn.completed');
  });

  test('an interrupt Claude Code does not confirm fails with an unknown effect', async () => {
    const { adapter, startConfirmed } = setup();
    const scripted = await startConfirmed();
    scripted.interruptFailure = new Error('Query closed');
    await assert.rejects(
      adapter.interrupt({ execution }),
      actionError('interrupt_failed', 'unknown'),
    );
  });
});

describe('instructing', () => {
  test('an execution at rest takes a new turn once Claude Code confirms the new message', async () => {
    const { adapter, startConfirmed, observed, types } = setup();
    const scripted = await startConfirmed();
    scripted.emit(success(scripted.sessionId));
    await settle();

    const sending = adapter.sendInstruction({ execution, text: 'Now update the changelog.' });
    await settle();
    assert.equal(scripted.delivered.length, 2);
    const second = scripted.delivered[1];
    assert.deepEqual(second?.message, {
      role: 'user',
      content: [{ type: 'text', text: 'Now update the changelog.' }],
    });
    assert.notEqual(second?.uuid, scripted.delivered[0]?.uuid);
    assert.equal(types().at(-1), 'runtime.turn.completed');

    scripted.emit(init(scripted.sessionId));
    await sending;
    assert.equal(types().at(-1), 'runtime.turn.started');
    assert.deepEqual(observed.at(-1)?.payload, { turn_id: second?.uuid });
    scripted.emit(success(scripted.sessionId));
    await settle();
    assert.deepEqual(observed.at(-1)?.payload, { turn_id: second?.uuid });
    assert.equal(types().filter((type) => type === 'runtime.execution.started').length, 1);
    assertContractValid(observed);
  });

  test('instructions while a turn runs are refused, matching the declared capability', async () => {
    const { adapter, startConfirmed } = setup();
    await startConfirmed();
    assert.equal(adapter.descriptor.capabilities.instruct_while_running, false);
    await assert.rejects(
      adapter.sendInstruction({ execution, text: 'Also do this.' }),
      actionError('turn_in_progress'),
    );
  });

  test('actions on an execution the runtime never started are refused', async () => {
    const { adapter } = setup();
    await assert.rejects(
      adapter.interrupt({ execution }),
      actionError('execution_unknown_to_runtime'),
    );
  });
});

describe('closing', () => {
  test('close ends every query, fails what is waiting, and reports nothing afterwards', async () => {
    const { adapter, startConfirmed, observed } = setup();
    const scripted = await startConfirmed();
    scripted.emit(success(scripted.sessionId));
    await settle();
    const sending = adapter.sendInstruction({ execution, text: 'One more thing.' });
    const count = observed.length;

    await adapter.close();
    assert.equal(scripted.closed, true);
    await assert.rejects(sending, actionError('runtime_closed', 'unknown'));
    scripted.emit(init(scripted.sessionId));
    await settle();
    assert.equal(observed.length, count);
    await assert.rejects(adapter.interrupt({ execution }), actionError('runtime_closed'));
  });
});

describe('start options', () => {
  test('the host directory policy decides which cwd a session may use, and its real path is used', async () => {
    const elsewhere = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-elsewhere-')));
    try {
      const { adapter, start, runs } = setup({
        directoryPolicy: (path) =>
          path === WORKDIR
            ? { ok: true, directory: WORKDIR }
            : { ok: false, message: `${path} is outside the project roots.` },
      });
      assert.deepEqual(adapter.validateStartOptions({ cwd: elsewhere }), {
        ok: false,
        message: `${elsewhere} is outside the project roots.`,
      });
      await assert.rejects(start({ cwd: elsewhere }), actionError('invalid_runtime_options'));
      assert.equal(runs.length, 0, 'no session was launched');
      assert.equal(adapter.validateStartOptions({ cwd: WORKDIR }).ok, true);
    } finally {
      rmSync(elsewhere, { recursive: true, force: true });
    }
  });

  test('cwd is required and must be an existing absolute directory; unknown options are refused', () => {
    const { adapter } = setup();
    const file = join(WORKDIR, 'a-file.txt');
    writeFileSync(file, 'not a directory');
    const valid = (options: RuntimeOptions) => adapter.validateStartOptions(options).ok;
    assert.equal(valid({ cwd: WORKDIR }), true);
    assert.equal(valid({}), false);
    assert.equal(valid({ cwd: 'relative/path' }), false);
    assert.equal(valid({ cwd: join(WORKDIR, 'missing') }), false);
    assert.equal(valid({ cwd: file }), false);
    assert.equal(valid({ cwd: WORKDIR, scenario: 'successful_feature' }), false);
  });

  test('model and permission_mode are checked', () => {
    const { adapter } = setup();
    const valid = (options: RuntimeOptions) =>
      adapter.validateStartOptions({ cwd: WORKDIR, ...options }).ok;
    assert.equal(valid({ model: 'claude-opus-5-5' }), true);
    assert.equal(valid({ model: 'opus[1m]' }), true);
    assert.equal(valid({ model: 'us.anthropic.claude-sonnet-5:0' }), true);
    assert.equal(valid({ model: '--dangerously-skip-permissions' }), false);
    assert.equal(valid({ model: 'two words' }), false);
    assert.equal(valid({ model: 7 }), false);
    for (const mode of ['default', 'acceptEdits', 'plan', 'dontAsk']) {
      assert.equal(valid({ permission_mode: mode }), true, mode);
    }
    assert.equal(valid({ permission_mode: 'bypassPermissions' }), false);
    assert.equal(valid({ permission_mode: 'auto' }), false);
  });

  test('a start with invalid options is refused before anything runs', async () => {
    const { start, runs } = setup();
    await assert.rejects(start({ cwd: 'relative' }), actionError('invalid_runtime_options'));
    assert.equal(runs.length, 0);
  });
});

describe('descriptor', () => {
  test('the declared capabilities are all implemented, and the descriptor is contract-valid', () => {
    const { adapter } = setup();
    assert.deepEqual(capabilityProblems(adapter), []);
    assert.deepEqual(adapter.descriptor.capabilities, {
      start_execution: true,
      instruct_at_rest: true,
      instruct_while_running: false,
      respond_to_approval: true,
      interrupt: true,
    });
    assert.equal(adapter.descriptor.display_name, 'Claude Agent');
    assert.equal(adapter.descriptor.synthetic, false);
    const validate = compileValidator(RuntimeDescriptor);
    assert.ok(validate(adapter.descriptor).ok);
  });
});
