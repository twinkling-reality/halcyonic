/**
 * End to end tests against the real OpenCode 2.0.18 binary named by OPENCODE_BIN, driving turns
 * through the fake provider inside a sandbox: private HOME, XDG and TMPDIR directories, loopback
 * only, and a proxy trap that records any attempt to leave the machine. Without OPENCODE_BIN
 * these tests are skipped and the unit tests still run.
 */
import assert from 'node:assert/strict';
import { type ChildProcess, execFileSync, spawn } from 'node:child_process';
import { existsSync, readFileSync, realpathSync } from 'node:fs';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import type { ExecutionId, RuntimeEventType } from '@halcyonic/contracts';
import {
  type ExecutionContext,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import { OpenCodeRuntimeAdapter, type OpenCodeRuntimeOptions } from './opencode-runtime.ts';
import { readProcessIdentity } from './server-record.ts';
import { allowOnly } from './testing/directory-policy.ts';
import { FAKE_SHELL_COMMAND, type FakeProviderOptions } from './testing/fake-provider.ts';
import { assertValidObservations, TEST_EXECUTION } from './testing/observations.ts';
import { createSandbox, type OpenCodeSandbox } from './testing/sandbox.ts';

const BINARY = process.env.OPENCODE_BIN ?? '';
const SKIP =
  BINARY === ''
    ? 'OPENCODE_BIN is not set; point it at the @opencode/cli 2.0.18 binary to run these tests'
    : existsSync(BINARY)
      ? false
      : `OPENCODE_BIN=${BINARY} does not exist; these tests are skipped`;
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
      () => {
        return `${type} #${count}; observed ${this.types().join(', ')}`;
      },
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

interface Harness {
  readonly sandbox: OpenCodeSandbox;
  readonly runtime: OpenCodeRuntimeAdapter;
  start(instruction: string, options?: Record<string, unknown>): Promise<Execution>;
}

async function harness(
  t: TestContext,
  setup: { runtime?: Partial<OpenCodeRuntimeOptions>; provider?: FakeProviderOptions } = {},
): Promise<Harness> {
  const sandbox = await createSandbox(setup.provider);
  const runtime = new OpenCodeRuntimeAdapter({
    binaryPath: BINARY,
    serverRecordFile: sandbox.recordFile,
    directoryPolicy: allowOnly(sandbox.project),
    env: sandbox.env,
    ...setup.runtime,
  });
  t.after(async () => {
    await runtime.close();
    await sandbox.cleanup();
    assert.deepEqual(sandbox.egress, [], 'OpenCode tried to reach the network');
  });
  return {
    sandbox,
    runtime,
    async start(instruction, options = {}) {
      const execution = new Execution();
      const result = await runtime.startExecution({
        execution: execution.context,
        instruction,
        options: { directory: sandbox.project, ...options },
        emit: execution.emit,
      });
      assert.match(result.native_id ?? '', /^ses/);
      return execution;
    },
  };
}

function turnId(observation: RuntimeObservation | undefined): string | null | undefined {
  return observation !== undefined && 'turn_id' in observation.payload
    ? observation.payload.turn_id
    : undefined;
}

/** Runs one execution in a separate process that plays a control plane about to crash. */
async function crashHost(t: TestContext, sandbox: OpenCodeSandbox) {
  const host: ChildProcess = spawn(
    process.execPath,
    [
      CRASH_HOST,
      JSON.stringify({
        binaryPath: BINARY,
        recordFile: sandbox.recordFile,
        env: sandbox.env,
        directory: sandbox.project,
      }),
    ],
    { stdio: ['ignore', 'pipe', 'inherit'] },
  );
  t.after(() => host.kill('SIGKILL'));
  const line = await Promise.race([
    new Promise<string>((resolve) =>
      host.stdout?.once('data', (data: Buffer) => resolve(data.toString())),
    ),
    delay(30_000).then(() => {
      throw new Error('the crash host never started its turn');
    }),
  ]);
  const { serverPid } = JSON.parse(line) as { serverPid: number };
  // A safety net that signals only a process still running the OpenCode binary.
  t.after(async () => {
    const identity = await readProcessIdentity(serverPid).catch(() => null);
    if (identity?.command.startsWith(`${BINARY} `)) process.kill(-serverPid, 'SIGKILL');
  });
  const record = JSON.parse(readFileSync(sandbox.recordFile, 'utf8')) as {
    pid: number;
    port: number;
  };
  assert.equal(record.pid, serverPid);
  return { host, serverPid, port: record.port };
}

function childPids(parent: number, pattern: string): number[] {
  try {
    return execFileSync('pgrep', ['-P', String(parent), '-f', pattern], { encoding: 'utf8' })
      .trim()
      .split('\n')
      .map(Number);
  } catch {
    return [];
  }
}

describe('OpenCode 2.0.18 end to end', { skip: SKIP }, () => {
  test('a turn runs to completion on a server launched for it', SLOW_TEST, async (t) => {
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
    assert.match(started.payload.native_id ?? '', /^ses/);
    assert.equal(turnId(completed), turnId(turn));
    assert.match(
      message?.type === 'runtime.agent_message' ? message.payload.text : '',
      /^Fake reply \d+: acknowledged\.$/,
    );
    assertValidObservations(execution.observations, execution.context);
    assert.equal(sandbox.provider.requests.at(-1)?.model, 'fake-model');
    // OpenCode works in the real path the directory policy returned; its prompt names it.
    assert.ok(
      sandbox.provider.requests[0]?.body.includes(
        `Working directory: ${realpathSync(sandbox.project)}`,
      ),
    );

    // The server is the configured binary, and its record names it without any secret.
    const pid = runtime.serverPid;
    assert.ok(pid !== null);
    const identity = await readProcessIdentity(pid);
    assert.ok(identity?.command.startsWith(`${BINARY} serve --hostname 127.0.0.1 --port `));
    const record = JSON.parse(readFileSync(sandbox.recordFile, 'utf8')) as Record<string, unknown>;
    assert.deepEqual(Object.keys(record).sort(), [
      'binaryPath',
      'command',
      'pid',
      'port',
      'startedAt',
    ]);
    assert.equal(record.pid, pid);
  });

  test('an approved request lets the tool run and the turn finish', SLOW_TEST, async (t) => {
    const { runtime, start } = await harness(t);
    const execution = await start('Please RUN_SHELL for the end to end test.');
    const requested = await execution.next('runtime.approval.requested');
    assert.ok(requested.type === 'runtime.approval.requested');
    assert.deepEqual(requested.payload.subject, {
      kind: 'tool_use',
      tool_name: 'shell',
      summary: FAKE_SHELL_COMMAND,
    });
    await runtime.respondToApproval({
      execution: execution.context,
      approval_id: requested.payload.approval_id,
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
    assert.deepEqual(payloads[4], {
      approval_id: requested.payload.approval_id,
      decision: 'approved',
    });
    assert.equal((payloads[5] as { outcome: string }).outcome, 'succeeded');
    assert.equal((payloads[6] as { text: string }).text, 'Tool result received: halcyonic-e2e');
    assertValidObservations(execution.observations, execution.context);
  });

  test(
    'a denial with a message fails the tool; 2.0.18 delivers the message nowhere',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('Please RUN_SHELL for the end to end test.');
      const requested = await execution.next('runtime.approval.requested');
      assert.ok(requested.type === 'runtime.approval.requested');
      const reason = 'Denied by the Halcyonic end to end test, marker 7c1f.';
      await runtime.respondToApproval({
        execution: execution.context,
        approval_id: requested.payload.approval_id,
        decision: 'deny',
        message: reason,
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
      // The known 2.0.18 defect: the model never sees the reason. If this fails, OpenCode changed.
      assert.equal(
        sandbox.provider.requests.some((request) => request.body.includes('marker 7c1f')),
        false,
      );
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test('a denial without a message ends the turn as interrupted', SLOW_TEST, async (t) => {
    const { runtime, start } = await harness(t);
    const execution = await start('Please RUN_SHELL for the end to end test.');
    const requested = await execution.next('runtime.approval.requested');
    assert.ok(requested.type === 'runtime.approval.requested');
    await runtime.respondToApproval({
      execution: execution.context,
      approval_id: requested.payload.approval_id,
      decision: 'deny',
      message: null,
    });
    await execution.next('runtime.turn.interrupted');
    assert.deepEqual(execution.typesAfter('runtime.approval.requested'), [
      'runtime.approval.resolved',
      'runtime.tool.completed',
      'runtime.turn.interrupted',
    ]);
    assertValidObservations(execution.observations, execution.context);
  });

  test(
    'an interrupt stops a running turn, and a second one finds nothing to stop',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('SLOW turn for the interrupt test.');
      const started = await execution.next('runtime.turn.started');
      await until(() => sandbox.provider.requests.length === 1, 10_000, 'the model request');
      await delay(600);
      await runtime.interrupt({ execution: execution.context });
      const interrupted = await execution.next('runtime.turn.interrupted');
      assert.equal(turnId(interrupted), turnId(started));
      assert.equal(execution.types().includes('runtime.turn.completed'), false);
      await assert.rejects(
        runtime.interrupt({ execution: execution.context }),
        actionError('no_running_turn'),
      );
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'an interrupt while an approval is pending clears it without resolving it',
    SLOW_TEST,
    async (t) => {
      const { runtime, start } = await harness(t);
      const execution = await start('Please RUN_SHELL for the end to end test.');
      const requested = await execution.next('runtime.approval.requested');
      assert.ok(requested.type === 'runtime.approval.requested');
      await runtime.interrupt({ execution: execution.context });
      await execution.next('runtime.turn.interrupted');
      assert.deepEqual(execution.typesAfter('runtime.approval.requested'), [
        'runtime.tool.completed',
        'runtime.turn.interrupted',
      ]);
      await assert.rejects(
        runtime.respondToApproval({
          execution: execution.context,
          approval_id: requested.payload.approval_id,
          decision: 'approve',
          message: null,
        }),
        actionError('approval_not_pending'),
      );
    },
  );

  test(
    'instructions start new turns at rest and are refused while a turn runs',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('Hello.');
      await execution.next('runtime.turn.completed');
      await runtime.sendInstruction({ execution: execution.context, text: 'SLOW second turn.' });
      await execution.next('runtime.turn.started', 2);
      await until(() => sandbox.provider.requests.length === 2, 10_000, 'the second model request');
      await assert.rejects(
        runtime.sendInstruction({ execution: execution.context, text: 'Also this.' }),
        actionError('turn_in_progress'),
      );
      await runtime.interrupt({ execution: execution.context });
      await execution.next('runtime.turn.interrupted');
      await runtime.sendInstruction({ execution: execution.context, text: 'Hello again.' });
      await execution.next('runtime.turn.completed', 2);
      assert.equal(sandbox.provider.requests.length, 3, 'a refused instruction reached the model');
      const turns = execution.observations.filter((item) => item.type === 'runtime.turn.started');
      assert.equal(new Set(turns.map(turnId)).size, 3);
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test('a provider error fails the turn with the OpenCode error', SLOW_TEST, async (t) => {
    const { start } = await harness(t);
    const execution = await start('Please FAIL.');
    const failed = await execution.next('runtime.turn.failed');
    assert.deepEqual(failed.type === 'runtime.turn.failed' && failed.payload.error, {
      code: 'provider_invalid_request',
      message: 'Fake provider refused the request.',
    });
    assertValidObservations(execution.observations, execution.context);
  });

  test('the model option selects the model OpenCode calls', SLOW_TEST, async (t) => {
    const { sandbox, start } = await harness(t);
    const execution = await start('Hello.', { model: 'fake/fake-model-2' });
    await execution.next('runtime.turn.completed');
    assert.equal(sandbox.provider.requests.at(-1)?.model, 'fake-model-2');
  });

  test(
    'a server that dies takes its executions with it; the next start launches another',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const running = await start('SLOW turn that the crash interrupts.');
      const resting = await start('Hello.');
      await running.next('runtime.turn.started');
      await resting.next('runtime.turn.completed');
      const pid = runtime.serverPid;
      assert.ok(pid !== null);
      process.kill(pid, 'SIGKILL');
      const lost = await running.next('runtime.connection.lost');
      await resting.next('runtime.connection.lost');
      assert.match(lost.type === 'runtime.connection.lost' ? lost.payload.reason : '', /SIGKILL/);
      await assert.rejects(
        runtime.interrupt({ execution: running.context }),
        actionError('runtime_unreachable'),
      );
      await assert.rejects(
        runtime.sendInstruction({ execution: resting.context, text: 'More.' }),
        actionError('runtime_unreachable'),
      );
      const counts = [running.observations.length, resting.observations.length];
      await delay(500);
      assert.deepEqual([running.observations.length, resting.observations.length], counts);

      const next = await start('Hello after the crash.');
      await next.next('runtime.turn.completed');
      assert.notEqual(runtime.serverPid, pid);
      const record = JSON.parse(readFileSync(sandbox.recordFile, 'utf8')) as { pid: number };
      assert.equal(record.pid, runtime.serverPid);
    },
  );

  test('close stops the server and its watchdog and removes the record', SLOW_TEST, async (t) => {
    const { runtime, sandbox, start } = await harness(t);
    const execution = await start('SLOW turn still running at close.');
    await execution.next('runtime.turn.started');
    const pid = runtime.serverPid;
    assert.ok(pid !== null);
    assert.equal(childPids(process.pid, 'watchdog.ts').length, 1);
    await runtime.close();
    assert.equal(alive(pid), false);
    await until(() => childPids(process.pid, 'watchdog.ts').length === 0, 5000, 'the watchdog');
    assert.equal(existsSync(sandbox.recordFile), false);
  });

  test(
    'a crashed control plane does not leave its server running: the watchdog stops it',
    SLOW_TEST,
    async (t) => {
      const sandbox = await createSandbox();
      t.after(() => sandbox.cleanup());
      const { host, serverPid, port } = await crashHost(t, sandbox);
      assert.equal(alive(serverPid), true);
      host.kill('SIGKILL');
      await until(() => !alive(serverPid), 15_000, 'the watchdog to stop the orphaned server');
      const runtime = new OpenCodeRuntimeAdapter({
        binaryPath: BINARY,
        serverRecordFile: sandbox.recordFile,
        directoryPolicy: allowOnly(sandbox.project),
        env: sandbox.env,
      });
      t.after(() => runtime.close());
      assert.deepEqual(await runtime.stopStaleServer(), {
        outcome: 'not_running',
        pid: serverPid,
        port,
      });
      assert.equal(existsSync(sandbox.recordFile), false);
      assert.deepEqual(sandbox.egress, []);
    },
  );

  test(
    'a server outliving both its control plane and its watchdog is stopped by the next start',
    SLOW_TEST,
    async (t) => {
      const sandbox = await createSandbox();
      t.after(() => sandbox.cleanup());
      const { host, serverPid, port } = await crashHost(t, sandbox);
      const watchdogs = childPids(host.pid ?? 0, 'watchdog.ts');
      assert.equal(watchdogs.length, 1);
      process.kill(watchdogs[0] as number, 'SIGKILL');
      host.kill('SIGKILL');
      await delay(1500);
      assert.equal(alive(serverPid), true, 'the orphaned server should still be running');

      const runtime = new OpenCodeRuntimeAdapter({
        binaryPath: BINARY,
        serverRecordFile: sandbox.recordFile,
        directoryPolicy: allowOnly(sandbox.project),
        env: sandbox.env,
      });
      t.after(() => runtime.close());
      assert.deepEqual(await runtime.stopStaleServer(), {
        outcome: 'stopped',
        pid: serverPid,
        port,
      });
      await until(() => !alive(serverPid), 5000, 'the orphaned server to exit');
      const execution = new Execution();
      await runtime.startExecution({
        execution: execution.context,
        instruction: 'Hello after the orphan was stopped.',
        options: { directory: sandbox.project },
        emit: execution.emit,
      });
      await execution.next('runtime.turn.completed');
      assert.notEqual(runtime.serverPid, serverPid);
      assert.deepEqual(sandbox.egress, []);
    },
  );

  test(
    'after the event stream reconnects, a turn that ended meanwhile is reconciled',
    SLOW_TEST,
    async (t) => {
      // A second of silence drops the stream (OpenCode's heartbeat comes every 15 s); it reopens
      // five seconds later. The approval is answered and the turn finishes inside that gap.
      const { runtime, sandbox, start } = await harness(t, {
        runtime: { streamSilenceTimeoutMs: 1000, reconnectDelaysMs: [5000] },
      });
      const execution = await start('Please RUN_SHELL for the end to end test.');
      const requested = await execution.next('runtime.approval.requested');
      assert.ok(requested.type === 'runtime.approval.requested');
      await delay(2500);
      await runtime.respondToApproval({
        execution: execution.context,
        approval_id: requested.payload.approval_id,
        decision: 'approve',
        message: null,
      });
      await until(() => sandbox.provider.requests.length === 2, 10_000, 'the turn to finish');
      const completed = await execution.next('runtime.turn.completed', 1, 20_000);
      assert.deepEqual(execution.typesAfter('runtime.approval.requested'), [
        'runtime.turn.completed',
      ]);
      assert.deepEqual(completed.provenance, {
        epistemic: 'inferred',
        native_type: 'opencode/session',
        rule: 'opencode.reconnect.session_state',
      });
      assert.equal(turnId(completed), turnId(execution.observations[1]));
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test('reconnecting while nothing changed reports nothing twice', SLOW_TEST, async (t) => {
    const { runtime, start } = await harness(t, {
      runtime: { streamSilenceTimeoutMs: 1000, reconnectDelaysMs: [200] },
    });
    const execution = await start('Please RUN_SHELL for the end to end test.');
    const requested = await execution.next('runtime.approval.requested');
    assert.ok(requested.type === 'runtime.approval.requested');
    // Several drops and reconnects happen while the approval waits.
    await delay(4000);
    assert.deepEqual(execution.typesAfter('runtime.approval.requested'), []);
    await runtime.respondToApproval({
      execution: execution.context,
      approval_id: requested.payload.approval_id,
      decision: 'approve',
      message: null,
    });
    await execution.next('runtime.turn.completed', 1, 20_000);
    await delay(1500);
    const types = execution.types();
    assert.equal(types.filter((type) => type === 'runtime.approval.requested').length, 1);
    assert.equal(types.filter((type) => type === 'runtime.turn.started').length, 1);
    assert.equal(types.filter((type) => type === 'runtime.turn.completed').length, 1);
    assert.equal(types.includes('runtime.connection.lost'), false);
    assertValidObservations(execution.observations, execution.context);
  });
});
