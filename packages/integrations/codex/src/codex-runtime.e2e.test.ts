/**
 * End to end tests against the real Codex 0.157.0 binary named by CODEX_BIN, driving turns through
 * the fake provider inside a sandbox: private HOME, CODEX_HOME, XDG and TMPDIR directories, the
 * switches that stop Codex's own traffic, a proxy trap that records and refuses every connection,
 * and a monitor of every socket the binary's processes hold. Each test fails if anything tried to
 * leave loopback. Without CODEX_BIN these tests are skipped and the unit tests still run.
 */
import assert from 'node:assert/strict';
import { type ChildProcess, execFileSync, spawn, spawnSync } from 'node:child_process';
import { lookup } from 'node:dns/promises';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { mkdir, mkdtemp, realpath, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import {
  compileValidator,
  type ExecutionId,
  type RuntimeEventType,
  RuntimeModel,
} from '@halcyonic/contracts';
import {
  type ExecutionContext,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import {
  CodexRuntimeAdapter,
  type CodexRuntimeOptions,
  QUESTION_FEATURE,
  untrustedFrom,
} from './codex-runtime.ts';
import { isRecord } from './events.ts';
import { RpcConnection } from './rpc.ts';
import { APP_SERVER_ARGUMENTS, buildEnvironment, unappliedSettings } from './server.ts';
import { readProcessIdentity } from './server-record.ts';
import { allowOnly } from './testing/directory-policy.ts';
import { FAKE_QUESTION, type FakeProviderOptions, PATCH_CONTENT } from './testing/fake-provider.ts';
import { probeNetwork } from './testing/network-probe.ts';
import { assertValidObservations, TEST_EXECUTION } from './testing/observations.ts';
import { type CodexSandbox, createSandbox, literalPattern } from './testing/sandbox.ts';

const BINARY = process.env.CODEX_BIN ?? '';
const SKIP =
  BINARY === ''
    ? 'CODEX_BIN is not set; point it at the codex-cli 0.157.0 native binary to run these tests'
    : existsSync(BINARY)
      ? false
      : `CODEX_BIN=${BINARY} does not exist; these tests are skipped`;
const CRASH_HOST = fileURLToPath(new URL('./testing/crash-host.ts', import.meta.url));
const SLOW_TEST = { timeout: 120_000 };

let executionCount = 0;

/** Collects the observations of one execution. */
class Execution {
  readonly context: ExecutionContext;
  readonly observations: RuntimeObservation[] = [];
  readonly emit = (observation: RuntimeObservation) => {
    this.observations.push(observation);
  };

  constructor() {
    executionCount += 1;
    this.context = {
      ...TEST_EXECUTION,
      execution_id:
        `01920000-0000-7000-8000-${String(executionCount).padStart(12, '0')}` as ExecutionId,
    };
  }

  types(): RuntimeEventType[] {
    return this.observations.map((observation) => observation.type);
  }

  /** Types observed after the first observation of `type`. */
  typesAfter(type: RuntimeEventType): RuntimeEventType[] {
    const types = this.types();
    return types.slice(types.indexOf(type) + 1);
  }

  async next(type: RuntimeEventType, count = 1, timeoutMs = 30_000): Promise<RuntimeObservation> {
    const matching = () => this.observations.filter((observation) => observation.type === type);
    await until(
      () => matching().length >= count,
      timeoutMs,
      () => `${type} #${count}; observed ${this.types().join(', ')}`,
    );
    return matching()[count - 1] as RuntimeObservation;
  }
}

async function until(
  condition: () => boolean,
  timeoutMs: number,
  what: string | (() => string),
): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!condition()) {
    if (Date.now() > deadline) {
      throw new Error(`timed out waiting for ${typeof what === 'string' ? what : what()}`);
    }
    await delay(20);
  }
}

function alive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

function actionError(code: string, effect: 'none' | 'unknown' = 'none') {
  return (error: unknown) =>
    error instanceof RuntimeActionError && error.code === code && error.effect === effect;
}

function turnId(observation: RuntimeObservation | undefined): string | null | undefined {
  return observation !== undefined && 'turn_id' in observation.payload
    ? observation.payload.turn_id
    : undefined;
}

function approvalId(observation: RuntimeObservation): string {
  assert.ok(observation.type === 'runtime.approval.requested');
  return observation.payload.approval_id;
}

/**
 * Pids of processes whose command line contains `text`. pgrep matches every command line without
 * printing them, so its output stays small however long the command lines on the machine are.
 */
function processesWith(text: string): number[] {
  const result = spawnSync('pgrep', ['-f', '--', literalPattern(text)], { encoding: 'utf8' });
  // pgrep exits 1 when no process matches.
  if (result.status === 1) return [];
  if (result.status !== 0) {
    const ending = result.error?.message ?? `${result.status ?? result.signal} ${result.stderr}`;
    throw new Error(`pgrep failed: ${ending}`);
  }
  return result.stdout.trim().split('\n').map(Number);
}

function markerLines(file: string): number {
  return existsSync(file) ? readFileSync(file, 'utf8').split('\n').length - 1 : 0;
}

/** Fails a test that let anything try to leave loopback. */
function assertStayedLocal(sandbox: CodexSandbox): void {
  assert.deepEqual(sandbox.egress, [], 'Codex tried to reach the network through the proxy');
  assert.deepEqual(sandbox.sockets, [], 'a Codex process held a socket beyond loopback');
}

interface Harness {
  readonly sandbox: CodexSandbox;
  readonly runtime: CodexRuntimeAdapter;
  start(
    instruction: string,
    options?: Record<string, unknown>,
    modelRef?: string | null,
  ): Promise<Execution>;
}

async function harness(
  t: TestContext,
  setup: { runtime?: Partial<CodexRuntimeOptions>; provider?: FakeProviderOptions } = {},
): Promise<Harness> {
  const sandbox = await createSandbox(BINARY, setup.provider);
  const runtime = new CodexRuntimeAdapter({
    binaryPath: BINARY,
    codexHome: sandbox.codexHome,
    serverRecordFile: sandbox.recordFile,
    directoryPolicy: allowOnly(sandbox.project),
    env: sandbox.env,
    ...setup.runtime,
  });
  t.after(async () => {
    await runtime.close();
    await sandbox.cleanup();
    assertStayedLocal(sandbox);
  });
  return {
    sandbox,
    runtime,
    async start(instruction, options = {}, modelRef = null) {
      const execution = new Execution();
      const result = await runtime.startExecution({
        execution: execution.context,
        instruction,
        options: { ...options },
        directory: sandbox.project,
        model_ref: modelRef,
        emit: execution.emit,
      });
      assert.match(result.native_id ?? '', /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-/);
      return execution;
    },
  };
}

/** Reads a thread back through a second app-server, the way any Codex client would. */
async function readThread(
  sandbox: CodexSandbox,
  threadId: string,
): Promise<Record<string, unknown>> {
  const child = spawn(BINARY, ['app-server'], {
    cwd: sandbox.root,
    env: buildEnvironment(process.env, sandbox.env, sandbox.codexHome),
    stdio: ['pipe', 'pipe', 'ignore'],
  });
  const exited = new Promise((resolve) => child.once('exit', resolve));
  assert.ok(child.stdout !== null && child.stdin !== null);
  const rpc = new RpcConnection(child.stdout, child.stdin, {
    onNotification: () => undefined,
    onRequest: (id, _method, _params, connection) => connection.respondError(id, -32601, 'no'),
  });
  try {
    const clientInfo = { name: 'halcyonic_e2e_reader', title: null, version: '0.0.0' };
    await rpc.request('initialize', { clientInfo, capabilities: null }, 20_000);
    rpc.notify('initialized');
    const result = await rpc.request('thread/read', { threadId, includeTurns: false }, 20_000);
    assert.ok(isRecord(result) && isRecord(result.thread));
    return result.thread;
  } finally {
    rpc.end();
    await exited;
  }
}

interface RawQuestionRun {
  /** The `warning` notifications Codex sent, by message. */
  readonly warnings: readonly string[];
  /** The `item/tool/requestUserInput` requests Codex sent. */
  readonly questions: readonly Record<string, unknown>[];
  /** How many `serverRequest/resolved` notifications Codex sent. */
  readonly resolved: number;
}

/**
 * Starts a thread through a bare app-server client, with `config` as its configuration, and runs
 * one `ASK_QUESTION` turn, answering any question with its second option, after first sending
 * `unreadable` as the answer when given. This pins what the adapter relies on below its own code:
 * what the under-development feature switch does, what Codex says when it is on, and that a
 * request whose answer Codex could not take can be answered again.
 */
async function rawQuestionTurn(
  sandbox: CodexSandbox,
  config: Record<string, unknown> | null,
  unreadable: string | null = null,
): Promise<RawQuestionRun> {
  const child = spawn(BINARY, ['app-server'], {
    cwd: sandbox.root,
    env: buildEnvironment(process.env, sandbox.env, sandbox.codexHome),
    stdio: ['pipe', 'pipe', 'ignore'],
  });
  const exited = new Promise((resolve) => child.once('exit', resolve));
  assert.ok(child.stdout !== null && child.stdin !== null);
  const warnings: string[] = [];
  const questions: Record<string, unknown>[] = [];
  let completed = false;
  let resolved = 0;
  const rpc = new RpcConnection(child.stdout, child.stdin, {
    onNotification: (method, params) => {
      if (method === 'warning' && isRecord(params)) warnings.push(String(params.message));
      if (method === 'turn/completed') completed = true;
      if (method === 'serverRequest/resolved') resolved += 1;
    },
    onRequest: (id, method, params, connection) => {
      if (method !== 'item/tool/requestUserInput' || !isRecord(params)) {
        connection.respondError(id, -32601, 'no');
        return;
      }
      questions.push(params);
      const answer = (text: string) =>
        connection.respond(id, { answers: { [FAKE_QUESTION.id]: { answers: [text] } } });
      if (unreadable === null) {
        answer('blue');
        return;
      }
      answer(unreadable);
      setTimeout(() => answer('blue'), 1500);
    },
  });
  try {
    const clientInfo = { name: 'halcyonic_e2e_flag', title: null, version: '0.0.0' };
    await rpc.request('initialize', { clientInfo, capabilities: null }, 20_000);
    rpc.notify('initialized');
    const started = await rpc.request(
      'thread/start',
      { cwd: sandbox.project, ...(config === null ? {} : { config }) },
      20_000,
    );
    assert.ok(isRecord(started) && isRecord(started.thread));
    const input = [{ type: 'text', text: 'ASK_QUESTION for the feature test.', text_elements: [] }];
    await rpc.request('turn/start', { threadId: started.thread.id, input }, 20_000);
    await until(() => completed, 30_000, 'the turn to complete');
    return { warnings, questions, resolved };
  } finally {
    rpc.end();
    await exited;
  }
}

/**
 * Runs one execution in a separate process that plays a control plane about to crash. Only this
 * process holds the host's stdin, so the host exits when this process dies, however it dies.
 */
async function crashHost(t: TestContext, sandbox: CodexSandbox, marker: string) {
  const host: ChildProcess = spawn(
    process.execPath,
    [
      CRASH_HOST,
      JSON.stringify({
        binaryPath: BINARY,
        codexHome: sandbox.codexHome,
        recordFile: sandbox.recordFile,
        env: sandbox.env,
        directory: sandbox.project,
        instruction: `CMD:for i in $(seq 1 300); do echo tick >> ${marker}; sleep 0.2; done`,
      }),
    ],
    { stdio: ['pipe', 'pipe', 'inherit'] },
  );
  t.after(() => host.kill('SIGKILL'));
  const line = await Promise.race([
    new Promise<string>((resolve) =>
      host.stdout?.once('data', (data: Buffer) => resolve(data.toString())),
    ),
    delay(30_000).then(() => {
      throw new Error('the crash host never started its command');
    }),
  ]);
  const { serverPid } = JSON.parse(line) as { serverPid: number };
  // Safety nets that signal only what still runs the binary or the marker command.
  t.after(async () => {
    const identity = await readProcessIdentity(serverPid).catch(() => null);
    if (identity?.command.startsWith(`${BINARY} `)) process.kill(-serverPid, 'SIGKILL');
    for (const pid of processesWith(marker)) process.kill(pid, 'SIGKILL');
  });
  const record = JSON.parse(readFileSync(sandbox.recordFile, 'utf8')) as { pid: number };
  assert.equal(record.pid, serverPid);
  await until(() => markerLines(marker) > 2, 10_000, 'the command to run');
  return { host, serverPid };
}

function watchdogsOf(parent: number): number[] {
  try {
    return execFileSync('pgrep', ['-P', String(parent), '-f', 'codex/src/watchdog.ts'], {
      encoding: 'utf8',
    })
      .trim()
      .split('\n')
      .map(Number);
  } catch {
    return [];
  }
}

describe('Codex 0.157.0 end to end', { skip: SKIP }, () => {
  test(
    'a turn runs to completion on a server launched for it, tagged as Halcyonic',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('Hello from the Halcyonic end to end test.');
      await execution.next('runtime.turn.completed');
      assert.deepEqual(execution.types(), [
        'runtime.execution.started',
        'runtime.model.used',
        'runtime.turn.started',
        'runtime.agent_message',
        'runtime.turn.completed',
      ]);
      const [started, model, turn, message, completed] = execution.observations;
      assert.ok(started?.type === 'runtime.execution.started');
      const threadId = started.payload.native_id ?? '';
      assert.equal(turnId(completed), turnId(turn));
      // The model and provider Codex says the thread runs on, from the configuration.
      assert.deepEqual(model?.payload, { model_ref: 'fake/gpt-5.5' });
      assert.equal(model?.provenance.epistemic, 'observed');
      assert.match(
        message?.type === 'runtime.agent_message' ? message.payload.text : '',
        /^Fake reply \d+: acknowledged\.$/,
      );
      assertValidObservations(execution.observations, execution.context);

      // The provider saw Halcyonic as the originator, analytics off, and the policy's real path.
      const request = sandbox.provider.requests[0];
      assert.equal(request?.headers.originator, 'halcyonic');
      assert.match(request?.headers['user-agent'] ?? '', /^halcyonic\/0\.157\.0 /);
      const metadata = JSON.parse(request?.headers['x-codex-turn-metadata'] ?? '{}') as {
        analytics_enabled?: unknown;
        thread_id?: unknown;
      };
      assert.equal(metadata.analytics_enabled, false);
      assert.equal(metadata.thread_id, threadId);
      assert.ok(JSON.stringify(request?.body).includes(`<cwd>${sandbox.project}</cwd>`));

      // The server is the configured binary, and its record names it.
      const pid = runtime.serverPid;
      assert.ok(pid !== null);
      const identity = await readProcessIdentity(pid);
      assert.equal(identity?.command, [BINARY, ...APP_SERVER_ARGUMENTS].join(' '));
      assert.equal(identity?.groupId, pid, 'the server leads its own process group');
      const record = JSON.parse(readFileSync(sandbox.recordFile, 'utf8')) as Record<
        string,
        unknown
      >;
      assert.deepEqual(Object.keys(record).sort(), ['binaryPath', 'command', 'pid', 'startedAt']);
      assert.equal(record.pid, pid);

      // Codex recorded the thread as Halcyonic's, in a rollout named by the native id.
      await runtime.close();
      const thread = await readThread(sandbox, threadId);
      assert.equal(thread.threadSource, 'halcyonic');
      assert.equal(thread.originator, 'halcyonic');
      assert.equal(thread.id, threadId);
      assert.ok(typeof thread.path === 'string' && thread.path.endsWith(`-${threadId}.jsonl`));
      assert.ok(existsSync(thread.path));
    },
  );

  test(
    'a thread on a named provider and model runs there, and its rollout records both',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('Hello from a model Codex has no metadata for.', {
        model_provider: 'fake',
        model: 'local-model:tag',
        context_window: 65536,
        auto_compact_token_limit: 52000,
      });
      await execution.next('runtime.turn.completed');
      assert.equal(sandbox.provider.requests.at(-1)?.body?.model, 'local-model:tag');
      assert.deepEqual(
        execution.observations
          .filter((item) => item.type === 'runtime.model.used')
          .map((item) => item.payload),
        [{ model_ref: 'fake/local-model:tag' }],
      );
      const started = execution.observations[0];
      assert.ok(started?.type === 'runtime.execution.started');
      const threadId = started.payload.native_id ?? '';
      await runtime.close();

      // What Salidium and Seorak read: the rollout's session_meta names the provider, and each
      // turn_context the model; the token counts carry the context window the thread was given.
      const thread = await readThread(sandbox, threadId);
      assert.ok(typeof thread.path === 'string');
      const rows = readFileSync(thread.path, 'utf8')
        .split('\n')
        .filter((line) => line !== '')
        .map((line) => JSON.parse(line) as { type: string; payload: Record<string, unknown> });
      const meta = rows.find((row) => row.type === 'session_meta')?.payload ?? {};
      assert.equal(meta.id, threadId);
      assert.equal(meta.model_provider, 'fake');
      assert.equal(meta.thread_source, 'halcyonic');
      assert.equal(meta.originator, 'halcyonic');
      assert.equal('forked_from_id' in meta && meta.forked_from_id !== null, false);
      const context = rows.find((row) => row.type === 'turn_context')?.payload ?? {};
      assert.equal(context.model, 'local-model:tag');
      const counts = rows
        .filter((row) => row.type === 'event_msg' && row.payload.type === 'token_count')
        .map((row) => row.payload.info)
        .filter(isRecord);
      assert.ok(counts.length > 0, 'no token count');
      // Codex keeps 5% of the window in reserve (0.157.0 reports 258,400 of 272,000).
      assert.equal(counts.at(-1)?.model_context_window, Math.floor(65536 * 0.95));
    },
  );

  test('an approved command runs, and the turn finishes', SLOW_TEST, async (t) => {
    const { runtime, sandbox, start } = await harness(t);
    const execution = await start('CMD_ESC:touch approved.txt && echo created-approved');
    const requested = await execution.next('runtime.approval.requested');
    assert.ok(requested.type === 'runtime.approval.requested');
    assert.equal(requested.payload.subject.tool_name, 'commandExecution');
    assert.match(
      requested.payload.subject.summary,
      new RegExp(`touch approved\\.txt && echo created-approved'\\nin ${sandbox.project}$`),
    );
    assert.doesNotMatch(requested.payload.subject.summary, /end to end test needs/);
    await runtime.respondToApproval({
      execution: execution.context,
      approval_id: approvalId(requested),
      decision: 'approve',
      message: null,
    });
    await execution.next('runtime.turn.completed');
    assert.deepEqual(execution.types(), [
      'runtime.execution.started',
      'runtime.model.used',
      'runtime.turn.started',
      'runtime.tool.started',
      'runtime.approval.requested',
      'runtime.approval.resolved',
      'runtime.tool.completed',
      'runtime.agent_message',
      'runtime.turn.completed',
    ]);
    const payloads = execution.observations.map((observation) => observation.payload);
    assert.deepEqual(payloads[5], { approval_id: approvalId(requested), decision: 'approved' });
    assert.equal((payloads[6] as { outcome: string }).outcome, 'succeeded');
    assert.match((payloads[7] as { text: string }).text, /Output: created-approved/);
    assert.ok(existsSync(`${sandbox.project}/approved.txt`));
    assertValidObservations(execution.observations, execution.context);
  });

  test(
    'a declined command does not run; the model is told and the turn completes',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('CMD_ESC:touch declined.txt && echo created-declined');
      const requested = await execution.next('runtime.approval.requested');
      await runtime.respondToApproval({
        execution: execution.context,
        approval_id: approvalId(requested),
        decision: 'deny',
        message: 'Denied by the Halcyonic end to end test, marker 7c1f.',
      });
      await execution.next('runtime.turn.completed');
      assert.deepEqual(execution.typesAfter('runtime.approval.requested'), [
        'runtime.approval.resolved',
        'runtime.tool.completed',
        'runtime.agent_message',
        'runtime.turn.completed',
      ]);
      const resolved = execution.observations.find(
        (item) => item.type === 'runtime.approval.resolved',
      );
      assert.equal(
        resolved?.type === 'runtime.approval.resolved' && resolved.payload.decision,
        'denied',
      );
      const tool = execution.observations.find((item) => item.type === 'runtime.tool.completed');
      assert.equal(tool?.type === 'runtime.tool.completed' && tool.payload.outcome, 'failed');
      assert.equal(existsSync(`${sandbox.project}/declined.txt`), false);
      const bodies = sandbox.provider.requests.map((request) => JSON.stringify(request.body));
      assert.match(bodies.at(-1) ?? '', /rejected by user/);
      // The approval answer has no field for a message: the model never sees it.
      assert.equal(
        bodies.some((body) => body.includes('marker 7c1f')),
        false,
      );
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'a file change outside the workspace asks first, and is applied once approved',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const outside = `${sandbox.root}/outside.txt`;
      const execution = await start(`PATCH_ADD:${outside}`);
      const requested = await execution.next('runtime.approval.requested');
      assert.ok(requested.type === 'runtime.approval.requested');
      assert.deepEqual(requested.payload.subject, {
        kind: 'tool_use',
        tool_name: 'fileChange',
        summary: `add ${outside}`,
      });
      await runtime.respondToApproval({
        execution: execution.context,
        approval_id: approvalId(requested),
        decision: 'approve',
        message: null,
      });
      await execution.next('runtime.turn.completed');
      assert.deepEqual(execution.typesAfter('runtime.turn.started'), [
        'runtime.tool.started',
        'runtime.approval.requested',
        'runtime.approval.resolved',
        'runtime.tool.completed',
        'runtime.agent_message',
        'runtime.turn.completed',
      ]);
      assert.equal(readFileSync(outside, 'utf8'), `${PATCH_CONTENT}\n`);
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'an interrupt stops a slow model stream, and a second finds no running turn',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('SLOW_TEXT for the interrupt test.');
      const started = await execution.next('runtime.turn.started');
      await until(() => sandbox.provider.streaming === 1, 10_000, 'the model stream');
      await delay(400);
      await runtime.interrupt({ execution: execution.context });
      const interrupted = await execution.next('runtime.turn.interrupted');
      assert.equal(turnId(interrupted), turnId(started));
      assert.equal(execution.types().includes('runtime.turn.completed'), false);
      await until(() => sandbox.provider.streaming === 0, 10_000, 'Codex to close the stream');
      await assert.rejects(
        runtime.interrupt({ execution: execution.context }),
        actionError('no_running_turn'),
      );
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'an interrupt while an approval is pending ends the turn and drops the approval',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('CMD_ESC:touch pending.txt');
      const requested = await execution.next('runtime.approval.requested');
      await runtime.interrupt({ execution: execution.context });
      await execution.next('runtime.turn.interrupted');
      assert.deepEqual(execution.typesAfter('runtime.approval.requested'), [
        'runtime.turn.interrupted',
      ]);
      await assert.rejects(
        runtime.respondToApproval({
          execution: execution.context,
          approval_id: approvalId(requested),
          decision: 'approve',
          message: null,
        }),
        actionError('approval_not_pending'),
      );
      await delay(500);
      assert.equal(existsSync(`${sandbox.project}/pending.txt`), false);
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'a question the agent asks reaches the person, and their answer reaches the agent',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('ASK_QUESTION for the end to end test.');
      const asked = await execution.next('runtime.question.asked');
      assert.ok(asked.type === 'runtime.question.asked');
      assert.equal(asked.payload.answerable, true);
      assert.deepEqual(asked.payload.prompts, [
        {
          key: 'colour',
          header: 'Colour',
          text: 'Which colour should the file mention?',
          options: [
            { label: 'red', description: 'The warm one' },
            { label: 'blue', description: 'The calm one' },
          ],
          multiple: false,
          // Codex 0.157.0 marks every question its tool asks as taking a free answer too.
          free_text: true,
          secret: false,
        },
      ]);
      assert.equal(asked.provenance.epistemic, 'observed');
      await runtime.answerQuestion({
        execution: execution.context,
        question_id: asked.payload.question_id,
        answers: [{ key: 'colour', selected: ['blue'], text: null }],
      });
      await execution.next('runtime.turn.completed');
      assert.deepEqual(execution.types(), [
        'runtime.execution.started',
        'runtime.model.used',
        'runtime.turn.started',
        'runtime.question.asked',
        'runtime.question.resolved',
        'runtime.agent_message',
        'runtime.turn.completed',
      ]);
      assert.deepEqual(execution.observations[4]?.payload, {
        question_id: asked.payload.question_id,
        outcome: 'answered',
      });
      // What the model was given back is the answer, and nothing else.
      const input = sandbox.provider.requests.at(-1)?.body?.input;
      assert.ok(Array.isArray(input));
      const reply = input.find((item) => isRecord(item) && item.type === 'function_call_output');
      assert.ok(isRecord(reply));
      assert.deepEqual(JSON.parse(String(reply.output)), {
        answers: { colour: { answers: ['blue'] } },
      });
      await assert.rejects(
        runtime.answerQuestion({
          execution: execution.context,
          question_id: asked.payload.question_id,
          answers: [{ key: 'colour', selected: ['red'], text: null }],
        }),
        actionError('question_not_pending'),
      );
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'an interrupt while a question is pending ends the turn and withdraws the question',
    SLOW_TEST,
    async (t) => {
      const { runtime, start } = await harness(t);
      const execution = await start('ASK_QUESTION for the interrupt test.');
      const asked = await execution.next('runtime.question.asked');
      assert.ok(asked.type === 'runtime.question.asked');
      await runtime.interrupt({ execution: execution.context });
      await execution.next('runtime.turn.interrupted');
      await delay(300);
      // Codex ends the turn before it settles the request, so the turn's end is what withdraws
      // the question, as it does a pending approval.
      assert.deepEqual(execution.typesAfter('runtime.question.asked'), [
        'runtime.turn.interrupted',
      ]);
      await assert.rejects(
        runtime.answerQuestion({
          execution: execution.context,
          question_id: asked.payload.question_id,
          answers: [{ key: 'colour', selected: ['red'], text: null }],
        }),
        actionError('question_not_pending'),
      );
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'questions rely on an under-development Codex feature; this fails when it changes',
    SLOW_TEST,
    async (t) => {
      const sandbox = await createSandbox(BINARY);
      t.after(async () => {
        await sandbox.cleanup();
        assertStayedLocal(sandbox);
      });
      // Off, as in Codex's default: the tool is offered but refused, and nobody is asked.
      const off = await rawQuestionTurn(sandbox, null);
      assert.deepEqual(off, { warnings: [], questions: [], resolved: 0 });
      assert.match(
        JSON.stringify(sandbox.provider.requests.at(-1)?.body),
        /request_user_input is unavailable in Default mode/,
      );
      // On, as the adapter starts every thread: the question arrives, with a start warning that
      // the adapter does not show the person.
      const on = await rawQuestionTurn(sandbox, { [QUESTION_FEATURE]: true });
      assert.equal(on.questions.length, 1);
      assert.deepEqual(on.questions[0]?.questions, [
        { ...FAKE_QUESTION, isOther: true, isSecret: false },
      ]);
      assert.deepEqual(on.warnings.length, 1);
      assert.match(
        on.warnings[0] ?? '',
        /^Under-development features enabled: default_mode_request_user_input\. Under-development features are incomplete and may behave unpredictably\./,
      );
    },
  );

  test(
    'an answer Codex cannot read is dropped, and the question can still be answered',
    SLOW_TEST,
    async (t) => {
      const sandbox = await createSandbox(BINARY);
      t.after(async () => {
        await sandbox.cleanup();
        assertStayedLocal(sandbox);
      });
      // The adapter refuses such text itself; this pins why it may answer again after a timeout.
      const run = await rawQuestionTurn(sandbox, { [QUESTION_FEATURE]: true }, 'navy \udc00');
      assert.equal(run.questions.length, 1);
      assert.equal(run.resolved, 1);
      const input = sandbox.provider.requests.at(-1)?.body?.input;
      assert.ok(Array.isArray(input));
      const reply = input.find((item) => isRecord(item) && item.type === 'function_call_output');
      assert.ok(isRecord(reply));
      assert.deepEqual(JSON.parse(String(reply.output)), {
        answers: { colour: { answers: ['blue'] } },
      });
    },
  );

  test(
    'an interrupt leaves a running command running, unreported as stopped, until close',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const marker = `${sandbox.project}/marker.log`;
      const execution = await start(
        `CMD:for i in $(seq 1 300); do echo tick >> ${marker}; sleep 0.2; done`,
      );
      await execution.next('runtime.tool.started');
      await until(() => markerLines(marker) > 1, 10_000, 'the command to run');
      await runtime.interrupt({ execution: execution.context });
      await execution.next('runtime.turn.interrupted');
      const before = markerLines(marker);
      await delay(1200);
      assert.ok(markerLines(marker) > before, 'Codex 0.157.0 keeps the command running');
      assert.equal(execution.types().includes('runtime.tool.completed'), false);
      assert.equal(processesWith(marker).length > 0, true);
      await runtime.close();
      await until(() => processesWith(marker).length === 0, 10_000, 'the command to stop');
      const after = markerLines(marker);
      await delay(600);
      assert.equal(markerLines(marker), after);
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'an instruction while a turn runs steers it, and the model sees it at its next request',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t, {
        provider: { slowChunkMs: 100, slowChunks: 15 },
      });
      const execution = await start('SLOW_TEXT that the steer follows.');
      const started = await execution.next('runtime.turn.started');
      await until(() => sandbox.provider.streaming === 1, 10_000, 'the model stream');
      await runtime.sendInstruction({ execution: execution.context, text: 'STEER: wrap it up' });
      const completed = await execution.next('runtime.turn.completed');
      assert.equal(turnId(completed), turnId(started));
      assert.equal(execution.types().filter((type) => type === 'runtime.turn.started').length, 1);
      const texts = execution.observations
        .filter((item) => item.type === 'runtime.agent_message')
        .map((item) => (item.payload as { text: string }).text);
      assert.equal(texts.at(-1), 'Steer received: STEER: wrap it up');
      assert.deepEqual(
        sandbox.provider.requests.map((request) => request.lastUserText),
        ['SLOW_TEXT that the steer follows.', 'STEER: wrap it up'],
      );
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'an instruction at rest starts a second turn that carries the history',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('Hello, first turn.');
      await execution.next('runtime.turn.completed');
      await runtime.sendInstruction({ execution: execution.context, text: 'Hello, second turn.' });
      await execution.next('runtime.turn.completed', 2);
      const turns = execution.observations.filter((item) => item.type === 'runtime.turn.started');
      assert.equal(new Set(turns.map(turnId)).size, 2);
      const second = JSON.stringify(sandbox.provider.requests[1]?.body?.input);
      assert.ok(second.includes('Hello, first turn.') && second.includes('Hello, second turn.'));
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test('a provider error fails the turn with the error Codex reports', SLOW_TEST, async (t) => {
    const { start } = await harness(t);
    const execution = await start('Please FAIL.');
    const failed = await execution.next('runtime.turn.failed');
    assert.deepEqual(failed.type === 'runtime.turn.failed' && failed.payload.error, {
      code: 'codex_other',
      message: 'Fake provider scripted failure.',
    });
    assertValidObservations(execution.observations, execution.context);
  });

  test('the model option selects the model Codex asks the provider for', SLOW_TEST, async (t) => {
    const { sandbox, start } = await harness(t);
    const execution = await start('Hello.', { model: 'gpt-5.4' });
    await execution.next('runtime.turn.completed');
    assert.equal(sandbox.provider.requests.at(-1)?.body?.model, 'gpt-5.4');
  });

  test(
    'the list holds the configured model under the configured provider, and a start from it runs there',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      // The built-in catalog is OpenAI's, so it stays out of a list for another provider.
      const models = await runtime.listModels();
      assert.deepEqual(models, [
        {
          model_ref: 'fake/gpt-5.5',
          display_name: 'gpt-5.5 (Fake provider (Halcyonic tests))',
          served: 'this_mac',
          tool_calling: 'unknown',
          context_tokens: null,
        },
      ]);
      assert.ok(models.every((model) => compileValidator(RuntimeModel)(model).ok));
      assert.equal(JSON.stringify(models).includes(sandbox.provider.baseUrl), false);

      const execution = await start('Hello.', {}, 'fake/gpt-5.5');
      await execution.next('runtime.turn.completed');
      assert.equal(sandbox.provider.requests.at(-1)?.body?.model, 'gpt-5.5');
      assert.deepEqual(
        execution.observations
          .filter((item) => item.type === 'runtime.model.used')
          .map((item) => item.payload),
        [{ model_ref: 'fake/gpt-5.5' }],
      );

      // A model the list no longer holds, or one of another provider, starts nothing.
      const requests = sandbox.provider.requests.length;
      for (const modelRef of ['fake/removed-model', 'openai/gpt-5.5']) {
        const gone = new Execution();
        await assert.rejects(
          runtime.startExecution({
            execution: gone.context,
            instruction: 'Hello.',
            options: {},
            directory: sandbox.project,
            model_ref: modelRef,
            emit: gone.emit,
          }),
          (error: unknown) =>
            error instanceof RuntimeActionError &&
            error.code === 'model_unavailable' &&
            error.effect === 'none',
        );
        assert.deepEqual(gone.types(), [], modelRef);
      }
      assert.equal(sandbox.provider.requests.length, requests);
    },
  );

  test(
    'a server killed mid-turn is relaunched: the turn reads interrupted and the thread goes on',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('SLOW_TEXT that the kill cuts short.');
      const started = await execution.next('runtime.turn.started');
      await until(() => sandbox.provider.streaming === 1, 10_000, 'the model stream');
      const pid = runtime.serverPid;
      assert.ok(pid !== null);
      process.kill(pid, 'SIGKILL');
      const interrupted = await execution.next('runtime.turn.interrupted', 1, 30_000);
      assert.equal(turnId(interrupted), turnId(started));
      assert.deepEqual(interrupted.provenance, {
        epistemic: 'inferred',
        native_type: 'codex/thread/turns/list',
        rule: 'codex.restart.turn_status',
      });
      const relaunched = runtime.serverPid;
      assert.ok(relaunched !== null && relaunched !== pid);
      const record = JSON.parse(readFileSync(sandbox.recordFile, 'utf8')) as { pid: number };
      assert.equal(record.pid, relaunched);
      await runtime.sendInstruction({
        execution: execution.context,
        text: 'Hello after the restart.',
      });
      await execution.next('runtime.turn.completed', 1, 30_000);
      assert.equal(execution.types().includes('runtime.connection.lost'), false);
      assert.equal(sandbox.provider.requests.at(-1)?.lastUserText, 'Hello after the restart.');
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test('close stops the server and its watchdog and removes the record', SLOW_TEST, async (t) => {
    const { runtime, sandbox, start } = await harness(t);
    const execution = await start('SLOW_TEXT still running at close.');
    await execution.next('runtime.turn.started');
    const pid = runtime.serverPid;
    assert.ok(pid !== null);
    assert.equal(watchdogsOf(process.pid).length, 1);
    await runtime.close();
    assert.equal(alive(pid), false);
    await until(() => watchdogsOf(process.pid).length === 0, 10_000, 'the watchdog');
    assert.equal(existsSync(sandbox.recordFile), false);
  });

  test(
    'a crashed control plane leaves nothing running: the server stops its command and exits',
    SLOW_TEST,
    async (t) => {
      const sandbox = await createSandbox(BINARY);
      t.after(() => sandbox.cleanup());
      const marker = `${sandbox.project}/marker.log`;
      const { host, serverPid } = await crashHost(t, sandbox, marker);
      host.kill('SIGKILL');
      await until(() => !alive(serverPid), 15_000, 'the server to exit');
      await until(() => processesWith(marker).length === 0, 10_000, 'the command to stop');
      const after = markerLines(marker);
      await delay(600);
      assert.equal(markerLines(marker), after);
      const runtime = new CodexRuntimeAdapter({
        binaryPath: BINARY,
        codexHome: sandbox.codexHome,
        serverRecordFile: sandbox.recordFile,
        directoryPolicy: allowOnly(sandbox.project),
        env: sandbox.env,
      });
      t.after(() => runtime.close());
      assert.deepEqual(await runtime.stopStaleServer(), { outcome: 'not_running', pid: serverPid });
      assert.equal(existsSync(sandbox.recordFile), false);
      assertStayedLocal(sandbox);
    },
  );

  test(
    'a crash host exits when its stdin ends, as when its test dies, and leaves nothing running',
    SLOW_TEST,
    async (t) => {
      const sandbox = await createSandbox(BINARY);
      t.after(() => sandbox.cleanup());
      const marker = `${sandbox.project}/marker.log`;
      const { host, serverPid } = await crashHost(t, sandbox, marker);
      const [watchdog] = watchdogsOf(host.pid ?? 0);
      assert.ok(watchdog !== undefined);
      const exited = new Promise((resolve) => host.once('exit', (code) => resolve(code)));
      // What the operating system does to the host's stdin when this process dies.
      host.stdin?.end();
      assert.equal(await exited, 0);
      await until(() => !alive(serverPid), 15_000, 'the server to exit');
      await until(() => processesWith(marker).length === 0, 10_000, 'the command to stop');
      await until(() => !alive(watchdog), 15_000, 'the watchdog to exit');
      assertStayedLocal(sandbox);
    },
  );

  test(
    'a server outliving its control plane and its watchdog is stopped with its command by the next start',
    SLOW_TEST,
    async (t) => {
      const sandbox = await createSandbox(BINARY);
      t.after(() => sandbox.cleanup());
      const marker = `${sandbox.project}/marker.log`;
      const { host, serverPid } = await crashHost(t, sandbox, marker);
      const watchdogs = watchdogsOf(host.pid ?? 0);
      assert.equal(watchdogs.length, 1);
      // Stopped, the server cannot act on the end of its input: it stays, as a hung one would.
      process.kill(serverPid, 'SIGSTOP');
      t.after(() => {
        if (alive(serverPid)) process.kill(serverPid, 'SIGCONT');
      });
      process.kill(watchdogs[0] as number, 'SIGKILL');
      host.kill('SIGKILL');
      await delay(1000);
      assert.equal(alive(serverPid), true, 'the orphaned server should still be there');
      assert.ok(processesWith(marker).length > 0, 'its command should still run');

      const runtime = new CodexRuntimeAdapter({
        binaryPath: BINARY,
        codexHome: sandbox.codexHome,
        serverRecordFile: sandbox.recordFile,
        directoryPolicy: allowOnly(sandbox.project),
        env: sandbox.env,
      });
      t.after(() => runtime.close());
      assert.deepEqual(await runtime.stopStaleServer(), { outcome: 'stopped', pid: serverPid });
      // An orphan is reaped by launchd, not by this process, so it may linger briefly as a zombie.
      await until(() => !alive(serverPid), 5000, 'the orphaned server to be gone');
      await until(() => processesWith(marker).length === 0, 5000, 'the command to stop');
      const execution = new Execution();
      await runtime.startExecution({
        execution: execution.context,
        instruction: 'Hello after the orphan was stopped.',
        options: {},
        directory: sandbox.project,
        model_ref: null,
        emit: execution.emit,
      });
      await execution.next('runtime.turn.completed');
      assert.notEqual(runtime.serverPid, serverPid);
      assertStayedLocal(sandbox);
    },
  );
});

const OLLAMA_MODEL = process.env.CODEX_E2E_OLLAMA_MODEL ?? '';
const OLLAMA = 'http://127.0.0.1:11434';
const PROBE_IDLE_MS = Number(process.env.CODEX_E2E_PROBE_IDLE_MS ?? 60_000);

/**
 * The check to re-run on every Codex upgrade (local-models.md): on 0.157.0 the undocumented
 * `features.plugins = false` was the only setting that stopped a connection to GitHub at startup,
 * so a new version may need another. It runs Codex as the control plane does, in a home of its own
 * holding what `pnpm mac-setup local-model` writes and no sign-in, on a model the Ollama of this
 * Mac serves, and watches the sockets of the server's own process tree through startup, idle and a
 * full run. It needs CODEX_BIN and CODEX_E2E_OLLAMA_MODEL, an Ollama model with tool calling that
 * is already downloaded; nothing is pulled, and the model is unloaded afterwards.
 */
describe('Codex network probe, re-run on every Codex upgrade', {
  skip:
    SKIP ||
    (OLLAMA_MODEL === ''
      ? 'CODEX_E2E_OLLAMA_MODEL is not set; name an Ollama model on this Mac to run the network probe'
      : false),
}, () => {
  test('nothing leaves loopback through startup, idle and a full run on a local model', {
    timeout: PROBE_IDLE_MS + 600_000,
  }, async (t) => {
    const tags = (await (await fetch(`${OLLAMA}/api/tags`)).json()) as {
      models: { name: string }[];
    };
    assert.ok(
      tags.models.some((model) => model.name === OLLAMA_MODEL),
      `Ollama on this Mac does not have ${OLLAMA_MODEL}`,
    );
    assert.doesNotMatch(OLLAMA_MODEL, /[:-]cloud$/, 'a model Ollama runs remotely is no probe');
    const root = await realpath(await mkdtemp(join(tmpdir(), 'halcyonic-codex-probe-')));
    const path = (name: string) => join(root, name);
    for (const name of ['home', 'config', 'data', 'state', 'cache', 'tmp', 'project']) {
      await mkdir(path(name), { recursive: true, mode: 0o700 });
    }
    // A git repository whose own Codex settings would start an MCP server (E2).
    const marker = await projectWithItsOwnSettings(path('project'));
    // What `pnpm mac-setup local-model` writes, and nothing else: the switches come from the
    // adapter, as for the control plane.
    await mkdir(path('codex-home'), { mode: 0o700 });
    await writeFile(
      path('codex-home/config.toml'),
      [
        'model_provider = "ollama"',
        `model = ${JSON.stringify(OLLAMA_MODEL)}`,
        'model_context_window = 65536',
        'model_auto_compact_token_limit = 52000',
        '',
      ].join('\n'),
      { mode: 0o600 },
    );
    const runtime = new CodexRuntimeAdapter({
      binaryPath: BINARY,
      codexHome: path('codex-home'),
      serverRecordFile: path('codex-server.json'),
      directoryPolicy: allowOnly(path('project')),
      env: {
        HOME: path('home'),
        XDG_CONFIG_HOME: path('config'),
        XDG_DATA_HOME: path('data'),
        XDG_STATE_HOME: path('state'),
        XDG_CACHE_HOME: path('cache'),
        TMPDIR: `${path('tmp')}/`,
        SHELL: '/bin/zsh',
      },
      requestTimeoutMs: 120_000,
    });
    // Watching from before the launch: everything this test's process starts is below it.
    const probe = probeNetwork(process.pid, BINARY);
    t.after(async () => {
      await runtime.close();
      await probe.stop();
      await fetch(`${OLLAMA}/api/generate`, {
        method: 'POST',
        body: JSON.stringify({ model: OLLAMA_MODEL, keep_alive: 0 }),
      }).catch(() => undefined);
      await rm(root, { recursive: true, force: true });
    });

    // Startup: listing the models launches the server.
    assert.deepEqual(
      (await runtime.listModels()).map((model) => [model.model_ref, model.served]),
      [[`ollama/${OLLAMA_MODEL}`, 'this_mac']],
    );
    const serverPid = runtime.serverPid;
    assert.ok(serverPid !== null);

    // Idle.
    await delay(PROBE_IDLE_MS);
    const afterIdle = probe.samples;
    const idleLoopback = [...probe.loopback];

    // A full run on the local model, approving whatever it asks.
    const execution = new Execution();
    const approved = new Set<string>();
    const emit = (observation: RuntimeObservation) => {
      execution.emit(observation);
      if (observation.type === 'runtime.approval.requested') {
        const approval = observation.payload.approval_id;
        if (approved.has(approval)) return;
        approved.add(approval);
        void runtime
          .respondToApproval({
            execution: execution.context,
            approval_id: approval,
            decision: 'approve',
            message: null,
          })
          .catch(() => undefined);
      }
    };
    const started = new Date();
    const { native_id: threadId } = await runtime.startExecution({
      execution: execution.context,
      instruction: 'Run the shell command `ls` once, then reply with the single word done.',
      options: { approval_policy: 'untrusted' },
      directory: path('project'),
      model_ref: `ollama/${OLLAMA_MODEL}`,
      emit,
    });
    // The control plane finds a thread's rollout by this name, in the folder of the local date
    // (apps/control-plane/src/intelligence/codex-home.ts); whether it is there at once is noted.
    const day = path(
      `codex-home/sessions/${started.getFullYear()}/${String(started.getMonth() + 1).padStart(2, '0')}/${String(started.getDate()).padStart(2, '0')}`,
    );
    const named = () =>
      existsSync(day) &&
      readdirSync(day).some(
        (name) => name.startsWith('rollout-') && name.endsWith(`-${threadId}.jsonl`),
      );
    const rolloutAtStart = named();
    await execution.next('runtime.turn.completed', 1, 480_000);
    assert.ok(named(), `no rollout-*-${threadId}.jsonl in ${day}`);
    assert.equal(runtime.serverPid, serverPid, 'the server was relaunched during the probe');
    assert.deepEqual([...probe.servers], [serverPid]);
    await delay(5000);
    await probe.stop();

    assert.ok(afterIdle > PROBE_IDLE_MS / 1000, `only ${afterIdle} samples before the run`);
    assert.deepEqual(probe.blind, [], 'the probe could not see the server in some samples');
    assert.ok(
      [...probe.loopback].some((name) => /->127\.0\.0\.1:11434$/.test(name)),
      `the probe never saw the connection to Ollama: ${[...probe.loopback].join(', ')}`,
    );
    assert.deepEqual(probe.beyondLoopback, [], 'a Codex process held a socket beyond loopback');
    // The rollout went to Halcyonic's home, and the person's home never gained a Codex folder.
    assert.ok(existsSync(path('codex-home/sessions')));
    assert.equal(existsSync(path('home/.codex')), false);
    // The project's own settings never loaded, and Codex recorded no trust for it.
    assert.equal(existsSync(marker), false, "the project's MCP server started");
    assert.doesNotMatch(
      readFileSync(path('codex-home/config.toml'), 'utf8'),
      /trust_level\s*=\s*"trusted"/,
    );
    t.diagnostic(
      `rollout ${rolloutAtStart ? 'there when the start returned' : 'written after the start returned'}; probe: ${probe.samples} samples, ${afterIdle} through startup and idle; loopback while idle ${idleLoopback.join(', ') || 'none'}, in all ${[...probe.loopback].join(', ')}`,
    );
  });
});

/** A bare app-server client, for what the adapter does not do itself. */
async function bareServer(
  args: readonly string[],
  env: Readonly<Record<string, string>>,
  cwd: string,
): Promise<{
  readonly rpc: RpcConnection;
  readonly notifications: { method: string; params: unknown }[];
  stop(): Promise<void>;
}> {
  const child = spawn(BINARY, [...args], { cwd, env, stdio: ['pipe', 'pipe', 'ignore'] });
  const exited = new Promise((resolve) => child.once('exit', resolve));
  assert.ok(child.stdout !== null && child.stdin !== null);
  const notifications: { method: string; params: unknown }[] = [];
  const rpc = new RpcConnection(child.stdout, child.stdin, {
    onNotification: (method, params) => notifications.push({ method, params }),
    onRequest: (id, _method, _params, connection) => connection.respondError(id, -32601, 'no'),
  });
  const clientInfo = { name: 'halcyonic_e2e_bare', title: null, version: '0.0.0' };
  await rpc.request('initialize', { clientInfo, capabilities: null }, 20_000);
  rpc.notify('initialized');
  return {
    rpc,
    notifications,
    stop: async () => {
      rpc.end();
      await exited;
    },
  };
}

/** A fresh home with no configuration and no sign-in, and the environment to run Codex in it. */
async function freshHome(t: TestContext): Promise<{ root: string; env: Record<string, string> }> {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'halcyonic-codex-fresh-')));
  for (const name of ['home', 'codex-home', 'tmp']) {
    await mkdir(join(root, name), { mode: 0o700 });
  }
  t.after(() => rm(root, { recursive: true, force: true }));
  const env = buildEnvironment(
    process.env,
    { HOME: join(root, 'home'), TMPDIR: `${join(root, 'tmp')}/` },
    join(root, 'codex-home'),
  );
  return { root, env };
}

/** A project, a git repository whose own Codex settings start an MCP server that leaves a mark. */
async function projectWithItsOwnSettings(folder: string): Promise<string> {
  execFileSync('git', ['init', '-q'], { cwd: folder, env: { PATH: '/usr/bin:/bin' } });
  await mkdir(join(folder, '.codex'), { mode: 0o700 });
  const marker = join(folder, 'mcp-started.log');
  await writeFile(
    join(folder, '.codex/config.toml'),
    [
      '[mcp_servers.trap]',
      'command = "/bin/sh"',
      `args = ["-c", ${JSON.stringify(`echo started >> '${marker}'`)}]`,
      '',
    ].join('\n'),
  );
  return marker;
}

const trusted = (folder: string) =>
  `\n[projects.${JSON.stringify(folder)}]\ntrust_level = "trusted"\n`;

/**
 * What must hold before Codex is registered on a person's Mac, beyond the probe above: the
 * settings Codex applies, a control that shows the probe sees traffic beyond loopback, a project's
 * own settings never loading, and a provider id Codex treats as OpenAI's whatever it is given.
 */
describe('Codex before registration on a Mac', { skip: SKIP }, () => {
  test(
    'E1: Codex applies every local-only setting and has no managed requirements',
    SLOW_TEST,
    async (t) => {
      const { root, env } = await freshHome(t);
      const server = await bareServer(APP_SERVER_ARGUMENTS, env, root);
      try {
        const read = await server.rpc.request('config/read', { includeLayers: false }, 20_000);
        assert.ok(isRecord(read) && isRecord(read.config));
        assert.deepEqual(unappliedSettings(read.config), []);
        assert.deepEqual(await server.rpc.request('configRequirements/read', undefined, 20_000), {
          requirements: null,
        });
      } finally {
        await server.stop();
      }
    },
  );

  test(
    'E3: without its local-only settings Codex reaches beyond loopback at startup, and the probe sees it',
    SLOW_TEST,
    async (t) => {
      try {
        await lookup('github.com');
      } catch {
        t.skip('offline: github.com does not resolve, so the control cannot show anything');
        return;
      }
      const { root, env } = await freshHome(t);
      const probe = probeNetwork(process.pid, BINARY);
      const server = await bareServer(['app-server'], env, root);
      try {
        await until(() => probe.beyondLoopback.length > 0, 30_000, 'a socket beyond loopback');
      } finally {
        await server.stop();
        await probe.stop();
      }
      assert.deepEqual(probe.blind, []);
      t.diagnostic(`seen: ${[...new Set(probe.beyondLoopback)].join('; ')}`);
    },
  );

  test(
    "E2: a project's own Codex settings never load and Codex records no trust; a home that trusts it is refused",
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const marker = await projectWithItsOwnSettings(sandbox.project);
      const settings = join(sandbox.codexHome, 'config.toml');
      const plain = readFileSync(settings, 'utf8');

      // The control: with the project trusted in the home, Codex starts the project's MCP server.
      await writeFile(settings, plain + trusted(sandbox.project));
      const env = buildEnvironment(process.env, sandbox.env, sandbox.codexHome);
      const run = async (config: Record<string, unknown> | null) => {
        const server = await bareServer(APP_SERVER_ARGUMENTS, env, sandbox.root);
        try {
          const started = await server.rpc.request(
            'thread/start',
            { cwd: sandbox.project, ...(config === null ? {} : { config }) },
            20_000,
          );
          assert.ok(isRecord(started) && isRecord(started.thread));
          const input = [{ type: 'text', text: 'Hello.', text_elements: [] }];
          await server.rpc.request('turn/start', { threadId: started.thread.id, input }, 20_000);
          await until(
            () => server.notifications.some((n) => n.method === 'turn/completed'),
            30_000,
            'the turn to complete',
          );
        } finally {
          await server.stop();
        }
      };
      await run(null);
      assert.ok(
        existsSync(marker),
        "a trusted project's MCP server did not start: the control shows nothing",
      );
      await rm(marker);

      // The thread's own overrides outrank the home's trust: the same home, the server never starts.
      await run({ projects: untrustedFrom(sandbox.project) });
      assert.equal(
        existsSync(marker),
        false,
        "the project's MCP server started despite the override",
      );

      // The adapter refuses a folder the home trusts, before any thread.
      await assert.rejects(start('Hello.'), actionError('runtime_refused'));
      assert.equal(existsSync(marker), false);

      // A home that records nothing: the turn runs, no MCP server starts, and no trust is written,
      // from the repository's root and from a folder below it.
      await writeFile(settings, plain);
      await runtime.close();
      const below = join(sandbox.project, 'src');
      await mkdir(below);
      for (const folder of [sandbox.project, below]) {
        const fresh = new CodexRuntimeAdapter({
          binaryPath: BINARY,
          codexHome: sandbox.codexHome,
          serverRecordFile: sandbox.recordFile,
          directoryPolicy: allowOnly(folder),
          env: sandbox.env,
        });
        t.after(() => fresh.close());
        const execution = new Execution();
        await fresh.startExecution({
          execution: execution.context,
          instruction: 'Hello.',
          options: {},
          directory: folder,
          model_ref: null,
          emit: execution.emit,
        });
        await execution.next('runtime.turn.completed');
        await fresh.close();
        assert.equal(existsSync(marker), false, `the project's MCP server started from ${folder}`);
        assert.doesNotMatch(readFileSync(settings, 'utf8'), /trust_level\s*=\s*"trusted"/);
      }
    },
  );

  test(
    "E4: Codex refuses a home that defines a provider under a built-in provider's id, so no thread reaches the address it names",
    SLOW_TEST,
    async (t) => {
      const sandbox = await createSandbox(BINARY);
      t.after(() => sandbox.cleanup());
      const env = buildEnvironment(process.env, sandbox.env, sandbox.codexHome);
      for (const id of ['openai', 'ollama']) {
        await writeFile(
          join(sandbox.codexHome, 'config.toml'),
          [
            'model = "gpt-5.5"',
            `model_provider = "${id}"`,
            `[model_providers.${id}]`,
            `name = "Loopback under ${id}'s id"`,
            `base_url = "${sandbox.provider.baseUrl}"`,
            'wire_api = "responses"',
            '',
          ].join('\n'),
        );
        const server = await bareServer(APP_SERVER_ARGUMENTS, env, sandbox.root);
        try {
          await assert.rejects(
            server.rpc.request(
              'thread/start',
              { cwd: sandbox.project, sandbox: 'read-only' },
              20_000,
            ),
            (error: unknown) =>
              error instanceof Error &&
              /reserved built-in provider IDs: `/.test(error.message) &&
              error.message.includes(id),
            id,
          );
        } finally {
          await server.stop();
        }
      }
      assert.deepEqual(sandbox.provider.requests, []);
      assert.deepEqual(sandbox.egress, [], 'Codex tried to reach the network through the proxy');
      assert.deepEqual(sandbox.sockets, [], 'a Codex process held a socket beyond loopback');
    },
  );

  test('E5: no discovered skill reaches what a thread sends the provider', SLOW_TEST, async (t) => {
    const { sandbox, start } = await harness(t);
    execFileSync('git', ['init', '-q'], { cwd: sandbox.project, env: { PATH: '/usr/bin:/bin' } });
    const skill = (name: string) =>
      `---\nname: ${name}\ndescription: Probe skill ${name}, never to be used.\n---\n\nSay ${name}.\n`;
    const planted: [string, string][] = [
      [join(sandbox.project, '.codex/skills/probe-codex-dir'), 'probe-codex-dir'],
      [join(sandbox.project, '.agents/skills/probe-agents-dir'), 'probe-agents-dir'],
      [join(sandbox.env.HOME as string, '.agents/skills/probe-home'), 'probe-home'],
    ];
    for (const [folder, name] of planted) {
      await mkdir(folder, { recursive: true });
      await writeFile(join(folder, 'SKILL.md'), skill(name));
    }
    const execution = await start('Hello.');
    await execution.next('runtime.turn.completed');
    const sent = JSON.stringify(sandbox.provider.requests.map((request) => request.body));
    assert.ok(sandbox.provider.requests.length > 0);
    for (const [, name] of planted) assert.ok(!sent.includes(name), `${name} reached the provider`);
    // Nor Codex's own bundled skills, among them one that installs skills from other repositories.
    assert.doesNotMatch(sent, /SKILL\.md|skill-installer/);
  });
});
