/**
 * End to end tests against the real Codex 0.157.0 binary named by CODEX_BIN, driving turns through
 * the fake provider inside a sandbox: private HOME, CODEX_HOME, XDG and TMPDIR directories, the
 * switches that stop Codex's own traffic, a proxy trap that records and refuses every connection,
 * and a monitor of every socket the binary's processes hold. Each test fails if anything tried to
 * leave loopback. Without CODEX_BIN these tests are skipped and the unit tests still run.
 */
import assert from 'node:assert/strict';
import { type ChildProcess, execFileSync, spawn, spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import type { ExecutionId, RuntimeEventType } from '@halcyonic/contracts';
import {
  type ExecutionContext,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import { CodexRuntimeAdapter, type CodexRuntimeOptions } from './codex-runtime.ts';
import { isRecord } from './events.ts';
import { RpcConnection } from './rpc.ts';
import { buildEnvironment } from './server.ts';
import { readProcessIdentity } from './server-record.ts';
import { allowOnly } from './testing/directory-policy.ts';
import { type FakeProviderOptions, PATCH_CONTENT } from './testing/fake-provider.ts';
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
  start(instruction: string, options?: Record<string, unknown>): Promise<Execution>;
}

async function harness(
  t: TestContext,
  setup: { runtime?: Partial<CodexRuntimeOptions>; provider?: FakeProviderOptions } = {},
): Promise<Harness> {
  const sandbox = await createSandbox(BINARY, setup.provider);
  const runtime = new CodexRuntimeAdapter({
    binaryPath: BINARY,
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
    async start(instruction, options = {}) {
      const execution = new Execution();
      const result = await runtime.startExecution({
        execution: execution.context,
        instruction,
        options: { cwd: sandbox.project, ...options },
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
    env: buildEnvironment(process.env, sandbox.env),
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
        'runtime.turn.started',
        'runtime.agent_message',
        'runtime.turn.completed',
      ]);
      const [started, turn, message, completed] = execution.observations;
      assert.ok(started?.type === 'runtime.execution.started');
      const threadId = started.payload.native_id ?? '';
      assert.equal(turnId(completed), turnId(turn));
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
      assert.equal(identity?.command, `${BINARY} app-server`);
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
      'runtime.turn.started',
      'runtime.tool.started',
      'runtime.approval.requested',
      'runtime.approval.resolved',
      'runtime.tool.completed',
      'runtime.agent_message',
      'runtime.turn.completed',
    ]);
    const payloads = execution.observations.map((observation) => observation.payload);
    assert.deepEqual(payloads[4], { approval_id: approvalId(requested), decision: 'approved' });
    assert.equal((payloads[5] as { outcome: string }).outcome, 'succeeded');
    assert.match((payloads[6] as { text: string }).text, /Output: created-approved/);
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
      await execution.next('runtime.turn.started');
      await until(() => sandbox.provider.streaming === 1, 10_000, 'the model stream');
      await runtime.sendInstruction({ execution: execution.context, text: 'STEER: wrap it up' });
      const completed = await execution.next('runtime.turn.completed');
      assert.equal(turnId(completed), turnId(execution.observations[1]));
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
        options: { cwd: sandbox.project },
        emit: execution.emit,
      });
      await execution.next('runtime.turn.completed');
      assert.notEqual(runtime.serverPid, serverPid);
      assertStayedLocal(sandbox);
    },
  );
});
