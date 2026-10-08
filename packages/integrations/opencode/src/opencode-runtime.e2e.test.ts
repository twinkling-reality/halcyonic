/**
 * End to end tests against the real OpenCode 2.0.18 binary named by OPENCODE_BIN, driving turns
 * through the fake provider inside a sandbox: private HOME, XDG and TMPDIR directories, loopback
 * only, and a proxy trap that records any attempt to leave the machine. Without OPENCODE_BIN
 * these tests are skipped and the unit tests still run.
 */
import assert from 'node:assert/strict';
import { type ChildProcess, execFileSync, spawn } from 'node:child_process';
import { lookup } from 'node:dns/promises';
import { existsSync, readFileSync, realpathSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';
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
import { OpenCodeClient } from './client.ts';
import { OpenCodeRuntimeAdapter, type OpenCodeRuntimeOptions } from './opencode-runtime.ts';
import { buildEnvironment, launchServer } from './server.ts';
import { readProcessIdentity } from './server-record.ts';
import { allowOnly } from './testing/directory-policy.ts';
import { FAKE_SHELL_COMMAND, type FakeProviderOptions } from './testing/fake-provider.ts';
import { probeNetwork } from './testing/network-probe.ts';
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
        options: { ...options },
        directory: sandbox.project,
        model_ref: null,
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

/**
 * Runs one execution in a separate process that plays a control plane about to crash. Only this
 * process holds the host's stdin, so the host exits when this process dies, however it dies.
 */
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
    { stdio: ['pipe', 'pipe', 'inherit'] },
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
      'runtime.model.used',
      'runtime.agent_message',
      'runtime.turn.completed',
    ]);
    const [started, turn, model, message, completed] = execution.observations;
    assert.ok(started?.type === 'runtime.execution.started');
    assert.match(started.payload.native_id ?? '', /^ses/);
    assert.equal(turnId(completed), turnId(turn));
    // The model OpenCode says the step runs on, not the one asked for.
    assert.deepEqual(model?.payload, { model_ref: 'fake/fake-model' });
    assert.equal(model?.provenance.epistemic, 'observed');
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

  /**
   * Starts an execution whose folder the host's policy allows `allowed` times and then refuses, as
   * when the folder is swapped meanwhile, and records every request the adapter sends OpenCode.
   */
  async function swappedAfter(t: TestContext, allowed: number) {
    let asked = 0;
    const { runtime, sandbox } = await harness(t, {
      runtime: {
        directoryPolicy: (path) => {
          asked += 1;
          return asked <= allowed
            ? { ok: true, directory: path }
            : { ok: false, code: 'location_missing', message: `${path} was swapped.` };
        },
      },
    });
    const requests: string[] = [];
    const original = globalThis.fetch;
    globalThis.fetch = ((input: Parameters<typeof fetch>[0], init?: RequestInit) => {
      requests.push(`${init?.method ?? 'GET'} ${String(input)}`);
      return original(input, init);
    }) as typeof fetch;
    t.after(() => {
      globalThis.fetch = original;
    });
    const execution = new Execution();
    await assert.rejects(
      runtime.startExecution({
        execution: execution.context,
        instruction: 'Say hello.',
        options: {},
        directory: sandbox.project,
        model_ref: null,
        emit: execution.emit,
      }),
      actionError('location_missing'),
    );
    assert.equal(asked, allowed + 1);
    assert.ok(runtime.serverPid !== null, 'the server was launched first');
    assert.deepEqual(execution.observations, [], 'no session exists');
    assert.ok(
      !requests.some((request) => request.startsWith('POST') && request.endsWith('/api/session')),
    );
    return requests.filter((request) => request.includes('location%5Bdirectory%5D'));
  }

  test(
    'a folder swapped before OpenCode reads its models there is refused, with nothing read from it',
    SLOW_TEST,
    async (t) => {
      assert.deepEqual(await swappedAfter(t, 1), [], 'no model read carried the folder');
    },
  );

  test(
    'a folder swapped while OpenCode lists its models is refused before a session is made there',
    SLOW_TEST,
    async (t) => {
      assert.ok((await swappedAfter(t, 2)).length > 0, 'the models were read in the folder first');
    },
  );

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
      'runtime.model.used',
      'runtime.tool.started',
      'runtime.approval.requested',
      'runtime.approval.resolved',
      'runtime.tool.completed',
      'runtime.agent_message',
      'runtime.turn.completed',
    ]);
    const payloads = execution.observations.map((observation) => observation.payload);
    assert.deepEqual(payloads[5], {
      approval_id: requested.payload.approval_id,
      decision: 'approved',
    });
    assert.equal((payloads[6] as { outcome: string }).outcome, 'succeeded');
    assert.equal((payloads[7] as { text: string }).text, 'Tool result received: halcyonic-e2e');
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
    'an instruction while a turn runs is steered into it, and one at rest starts a new turn',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t, { provider: { slowChunks: 12 } });
      const execution = await start('SLOW turn that is steered.');
      const started = await execution.next('runtime.turn.started');
      await until(() => sandbox.provider.requests.length === 1, 10_000, 'the model request');
      await runtime.sendInstruction({ execution: execution.context, text: 'Steered marker 5d2e.' });
      const completed = await execution.next('runtime.turn.completed');
      // One turn: the steered instruction waited for the streaming step to end, without cutting
      // it, and reached the model in the next request of the same turn.
      assert.equal(turnId(completed), turnId(started));
      // Both steps run on one model, reported once.
      assert.deepEqual(execution.types(), [
        'runtime.execution.started',
        'runtime.turn.started',
        'runtime.model.used',
        'runtime.agent_message',
        'runtime.agent_message',
        'runtime.turn.completed',
      ]);
      assert.equal(sandbox.provider.requests.length, 2);
      assert.equal(sandbox.provider.requests[0]?.body.includes('marker 5d2e'), false);
      assert.equal(sandbox.provider.requests[1]?.lastUserText, 'Steered marker 5d2e.');

      await runtime.sendInstruction({ execution: execution.context, text: 'Hello again.' });
      await execution.next('runtime.turn.completed', 2);
      const turns = execution.observations.filter((item) => item.type === 'runtime.turn.started');
      assert.equal(new Set(turns.map(turnId)).size, 2);
      assert.equal(sandbox.provider.requests.at(-1)?.lastUserText, 'Hello again.');
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'an instruction steered into a turn that is then interrupted reaches the model with the next one',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('SLOW turn that is steered, then interrupted.');
      await execution.next('runtime.turn.started');
      await until(() => sandbox.provider.requests.length === 1, 10_000, 'the model request');
      await runtime.sendInstruction({ execution: execution.context, text: 'Steered marker 9b4c.' });
      await runtime.interrupt({ execution: execution.context });
      await execution.next('runtime.turn.interrupted');
      assert.equal(
        sandbox.provider.requests.length,
        1,
        'the steered instruction started a request',
      );
      await delay(500);
      assert.equal(execution.types().filter((type) => type === 'runtime.turn.started').length, 1);

      await runtime.sendInstruction({ execution: execution.context, text: 'Hello after it.' });
      await execution.next('runtime.turn.completed');
      assert.equal(sandbox.provider.requests.length, 2);
      const next = sandbox.provider.requests[1];
      assert.equal(next?.lastUserText, 'Hello after it.');
      assert.ok(next?.body.includes('Steered marker 9b4c.'), 'the steered instruction was lost');
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

  test(
    'lists the models OpenCode offers; a start from the list runs on that model and reports it',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox } = await harness(t);
      const models = await runtime.listModels();
      assert.ok(models.every((model) => compileValidator(RuntimeModel)(model).ok));
      const fake = models.filter((model) => model.model_ref.startsWith('fake/'));
      assert.deepEqual(
        fake.map((model) => [model.model_ref, model.display_name, model.served]),
        [
          ['fake/fake-model', 'Fake model (fake)', 'this_mac'],
          ['fake/fake-model-2', 'Fake model 2 (fake)', 'this_mac'],
        ],
      );
      // The listing carries no provider settings: the fake provider's address stays out of it.
      assert.equal(JSON.stringify(models).includes(sandbox.provider.baseUrl), false);

      const execution = new Execution();
      await runtime.startExecution({
        execution: execution.context,
        instruction: 'Hello.',
        options: {},
        directory: sandbox.project,
        model_ref: 'fake/fake-model-2',
        emit: execution.emit,
      });
      await execution.next('runtime.turn.completed');
      assert.equal(sandbox.provider.requests.at(-1)?.model, 'fake-model-2');
      const used = execution.observations.filter((item) => item.type === 'runtime.model.used');
      assert.deepEqual(
        used.map((item) => item.payload),
        [{ model_ref: 'fake/fake-model-2' }],
      );
      assertValidObservations(execution.observations, execution.context);

      const gone = new Execution();
      await assert.rejects(
        runtime.startExecution({
          execution: gone.context,
          instruction: 'Hello.',
          options: {},
          directory: sandbox.project,
          model_ref: 'fake/removed-model',
          emit: gone.emit,
        }),
        actionError('model_unavailable'),
      );
      assert.deepEqual(gone.types(), []);
    },
  );

  test('the model option selects the model OpenCode calls', SLOW_TEST, async (t) => {
    const { sandbox, start } = await harness(t);
    const execution = await start('Hello.', { model: 'fake/fake-model-2' });
    await execution.next('runtime.turn.completed');
    assert.equal(sandbox.provider.requests.at(-1)?.model, 'fake-model-2');
  });

  test(
    'a model the configuration names under Ollama runs its first turn though discovery answers late',
    SLOW_TEST,
    async (t) => {
      // As `pnpm mac-setup local-model` writes it: the model named, with its limits, here large
      // enough that OpenCode's own prompt needs no compaction first. OpenCode lists such a model
      // before its Ollama discovery has answered, with an empty package, and a session prompted
      // with it then fails with provider.no-route ("Unsupported package").
      const tag = 'stand-in:1b';
      const { runtime, sandbox } = await harness(t, {
        provider: { ollama: { model: tag, answerMs: 900 } },
      });
      const file = join(sandbox.root, 'config/opencode/opencode.json');
      const config = JSON.parse(readFileSync(file, 'utf8')) as { providers: object };
      config.providers = {
        ...config.providers,
        ollama: {
          settings: { baseURL: sandbox.provider.baseUrl },
          models: { [tag]: { limit: { context: 128000, output: 4096 } } },
        },
      };
      writeFileSync(file, JSON.stringify(config));
      // No listing first: the start launches the server and loads the folder itself.
      const execution = new Execution();
      await runtime.startExecution({
        execution: execution.context,
        instruction: 'Hello.',
        options: {},
        directory: sandbox.project,
        model_ref: `ollama/${tag}`,
        emit: execution.emit,
      });
      const ended = (type: RuntimeEventType) =>
        type === 'runtime.turn.completed' || type === 'runtime.turn.failed';
      await until(() => execution.types().some(ended), 30_000, 'the first turn to end');
      const failed = execution.observations.filter((item) => item.type === 'runtime.turn.failed');
      assert.deepEqual(
        failed.map((item) => item.payload),
        [],
      );
      assert.equal(sandbox.provider.requests.at(-1)?.model, tag);
    },
  );

  test(
    'a model OpenCode does not offer, or that the directory disables, is refused before any session exists',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox } = await harness(t, { runtime: { modelWaitMs: 1500 } });
      // The project's own configuration takes a model off OpenCode's list for that directory.
      writeFileSync(
        join(sandbox.project, 'opencode.json'),
        JSON.stringify({ providers: { fake: { models: { 'fake-model-2': { disabled: true } } } } }),
      );
      for (const model of ['fake/not-configured', 'fake/fake-model-2', 'elsewhere/fake-model']) {
        const execution = new Execution();
        const started = Date.now();
        await assert.rejects(
          runtime.startExecution({
            execution: execution.context,
            instruction: 'Hello.',
            options: { model },
            directory: sandbox.project,
            model_ref: null,
            emit: execution.emit,
          }),
          (error: unknown) =>
            actionError('model_unavailable')(error) &&
            (error as Error).message.startsWith(`OpenCode does not offer the model ${model} in `),
        );
        assert.ok(Date.now() - started >= 1500, 'the start did not wait for the model');
        assert.deepEqual(execution.types(), [], model);
      }
      assert.equal(sandbox.provider.requests.length, 0);
      // The model it still offers there runs.
      const execution = new Execution();
      await runtime.startExecution({
        execution: execution.context,
        instruction: 'Hello.',
        options: { model: 'fake/fake-model' },
        directory: sandbox.project,
        model_ref: null,
        emit: execution.emit,
      });
      await execution.next('runtime.turn.completed');
      assert.equal(sandbox.provider.requests.at(-1)?.model, 'fake-model');
    },
  );

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
    'a crash host exits when its stdin ends, as when its test dies, and its watchdog stops the server',
    SLOW_TEST,
    async (t) => {
      const sandbox = await createSandbox();
      t.after(() => sandbox.cleanup());
      const { host, serverPid } = await crashHost(t, sandbox);
      const [watchdog] = childPids(host.pid ?? 0, 'watchdog.ts');
      assert.ok(watchdog !== undefined);
      const exited = new Promise((resolve) => host.once('exit', (code) => resolve(code)));
      // What the operating system does to the host's stdin when this process dies.
      host.stdin?.end();
      assert.equal(await exited, 0);
      await until(() => !alive(serverPid), 15_000, 'the watchdog to stop the orphaned server');
      await until(() => !alive(watchdog), 10_000, 'the watchdog to exit');
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
        options: {},
        directory: sandbox.project,
        model_ref: null,
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
      // five seconds later. The approval is answered three seconds after it is known, and the
      // turn finishes, inside that gap. Whether the approval was seen live or learned by an
      // earlier reconnect, nothing but the turn's end can be reported after it.
      const { runtime, sandbox, start } = await harness(t, {
        runtime: { streamSilenceTimeoutMs: 1000, reconnectDelaysMs: [5000, 200, 200, 200] },
      });
      const execution = await start('Please RUN_SHELL for the end to end test.');
      const requested = await execution.next('runtime.approval.requested');
      assert.ok(requested.type === 'runtime.approval.requested');
      await delay(3000);
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

  test(
    'a read that times out right after a reconnect, as when the Mac wakes, is retried rather than losing the session',
    SLOW_TEST,
    async (t) => {
      const { runtime, start } = await harness(t, {
        runtime: {
          streamSilenceTimeoutMs: 1000,
          reconnectDelaysMs: Array.from({ length: 10 }, () => 200),
          snapshotRetryDelaysMs: [300, 300],
        },
      });
      const execution = await start('Please RUN_SHELL for the end to end test.');
      const requested = await execution.next('runtime.approval.requested');
      assert.ok(requested.type === 'runtime.approval.requested');
      // The next two reads of the session's permissions time out, as the overnight run's did.
      let failures = 2;
      const original = globalThis.fetch;
      globalThis.fetch = ((input: Parameters<typeof fetch>[0], init?: RequestInit) => {
        if (failures > 0 && String(input).endsWith('/permission')) {
          failures -= 1;
          return Promise.reject(
            new DOMException('The operation was aborted due to timeout', 'TimeoutError'),
          );
        }
        return original(input, init);
      }) as typeof fetch;
      t.after(() => {
        globalThis.fetch = original;
      });
      await until(() => failures === 0, 10_000, 'a reconnect to read the session');
      await delay(1500);
      assert.deepEqual(
        execution.types().filter((type) => type === 'runtime.connection.lost'),
        [],
      );
      await runtime.respondToApproval({
        execution: execution.context,
        approval_id: requested.payload.approval_id,
        decision: 'approve',
        message: null,
      });
      await execution.next('runtime.turn.completed', 1, 20_000);
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'a session lost to reads that keep failing is restored once its server answers again',
    SLOW_TEST,
    async (t) => {
      const { runtime, start } = await harness(t, {
        runtime: {
          streamSilenceTimeoutMs: 1000,
          reconnectDelaysMs: Array.from({ length: 10 }, () => 200),
          snapshotRetryDelaysMs: [100, 100],
          recoveryDelaysMs: [500],
        },
      });
      const execution = await start('Please RUN_SHELL for the end to end test.');
      const requested = await execution.next('runtime.approval.requested');
      assert.ok(requested.type === 'runtime.approval.requested');
      // Every read of the session's permissions times out until the server "wakes".
      let asleep = true;
      const original = globalThis.fetch;
      globalThis.fetch = ((input: Parameters<typeof fetch>[0], init?: RequestInit) => {
        if (asleep && String(input).endsWith('/permission')) {
          return Promise.reject(
            new DOMException('The operation was aborted due to timeout', 'TimeoutError'),
          );
        }
        return original(input, init);
      }) as typeof fetch;
      t.after(() => {
        globalThis.fetch = original;
      });
      const lost = await execution.next('runtime.connection.lost', 1, 20_000);
      assert.match(
        lost.type === 'runtime.connection.lost' ? lost.payload.reason : '',
        /could not be read/,
      );
      await assert.rejects(
        runtime.respondToApproval({
          execution: execution.context,
          approval_id: requested.payload.approval_id,
          decision: 'approve',
          message: null,
        }),
        (error: unknown) =>
          error instanceof RuntimeActionError && error.code === 'runtime_unreachable',
      );
      asleep = false;
      await execution.next('runtime.connection.restored', 1, 20_000);
      // Nothing changed while it could not be read: the approval still waits, and is answered.
      assert.deepEqual(
        execution
          .typesAfter('runtime.connection.restored')
          .filter((type) => type === 'runtime.approval.requested'),
        [],
      );
      await runtime.respondToApproval({
        execution: execution.context,
        approval_id: requested.payload.approval_id,
        decision: 'approve',
        message: null,
      });
      await execution.next('runtime.turn.completed', 1, 20_000);
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'a question whose form cannot be read back does not cost the session',
    SLOW_TEST,
    async (t) => {
      const { runtime, start } = await harness(t, {
        runtime: {
          streamSilenceTimeoutMs: 1000,
          reconnectDelaysMs: Array.from({ length: 10 }, () => 200),
          snapshotRetryDelaysMs: [100, 100],
          recoveryDelaysMs: [500],
        },
      });
      const execution = await start('Please ASK_QUESTION for the end to end test.');
      const asked = await execution.next('runtime.question.asked');
      assert.ok(asked.type === 'runtime.question.asked');
      // After each reconnect the form is no longer listed as pending, and reading it answers 404,
      // as for a form OpenCode no longer keeps.
      let unreadable = 0;
      const original = globalThis.fetch;
      globalThis.fetch = ((input: Parameters<typeof fetch>[0], init?: RequestInit) => {
        const url = String(input);
        if (url.endsWith('/form')) {
          return Promise.resolve(Response.json({ data: [] }));
        }
        if (url.endsWith(`/form/${asked.payload.question_id}`)) {
          unreadable += 1;
          return Promise.resolve(Response.json({ error: 'not found' }, { status: 404 }));
        }
        return original(input, init);
      }) as typeof fetch;
      t.after(() => {
        globalThis.fetch = original;
      });
      // More reads than one snapshot's retries make, so a failing read would have cost the session.
      await until(() => unreadable >= 4, 15_000, 'the form to be read back after reconnects');
      globalThis.fetch = original;
      assert.deepEqual(
        execution.types().filter((type) => type.startsWith('runtime.connection.')),
        [],
      );
      await runtime.answerQuestion({
        execution: execution.context,
        question_id: asked.payload.question_id,
        answers: [{ key: 'q0', selected: ['blue'], text: null }],
      });
      await execution.next('runtime.turn.completed', 1, 20_000);
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'a question the agent asks is answered, and the agent goes on with the answer',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('Please ASK_QUESTION for the end to end test.');
      const asked = await execution.next('runtime.question.asked');
      assert.ok(asked.type === 'runtime.question.asked');
      assert.equal(asked.payload.answerable, true);
      assert.deepEqual(
        asked.payload.prompts.map(({ header, text, options, free_text }) => ({
          header,
          text,
          labels: options.map((option) => option.label),
          free_text,
        })),
        [
          {
            header: 'Colour',
            text: 'Which colour should the file mention?',
            labels: ['red', 'blue'],
            free_text: true,
          },
        ],
      );
      await assert.rejects(
        runtime.answerQuestion({
          execution: execution.context,
          question_id: 'frm_not_asked',
          answers: [{ key: 'q0', selected: ['blue'], text: null }],
        }),
        (error: unknown) =>
          error instanceof RuntimeActionError && error.code === 'question_not_pending',
      );
      await runtime.answerQuestion({
        execution: execution.context,
        question_id: asked.payload.question_id,
        answers: [{ key: 'q0', selected: ['blue'], text: null }],
      });
      const resolved = await execution.next('runtime.question.resolved');
      assert.deepEqual(resolved.payload, {
        question_id: asked.payload.question_id,
        outcome: 'answered',
      });
      await execution.next('runtime.turn.completed', 1, 20_000);
      // The model was given the answer as the question tool's result.
      assert.match(sandbox.provider.requests.at(-1)?.body ?? '', /blue/);
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test(
    'interrupting while a question waits dismisses it and ends the turn',
    SLOW_TEST,
    async (t) => {
      const { runtime, start } = await harness(t);
      const execution = await start('Please ASK_QUESTION for the end to end test.');
      const asked = await execution.next('runtime.question.asked');
      assert.ok(asked.type === 'runtime.question.asked');
      await runtime.interrupt({ execution: execution.context });
      await execution.next('runtime.turn.interrupted', 1, 20_000);
      await assert.rejects(
        runtime.answerQuestion({
          execution: execution.context,
          question_id: asked.payload.question_id,
          answers: [{ key: 'q0', selected: ['red'], text: null }],
        }),
        (error: unknown) =>
          error instanceof RuntimeActionError && error.code === 'question_not_pending',
      );
      assertValidObservations(execution.observations, execution.context);
    },
  );

  test('reconnecting while nothing changed reports nothing twice', SLOW_TEST, async (t) => {
    // A second of silence drops the stream again and again: during the first prompt, which
    // blocks a fresh OpenCode project for more than a second, while the approval waits, and
    // around the turn's end. The assertions hold whichever of these a reconnect lands in.
    const { runtime, start } = await harness(t, {
      runtime: {
        streamSilenceTimeoutMs: 1000,
        reconnectDelaysMs: Array.from({ length: 10 }, () => 200),
      },
    });
    const execution = await start('Please RUN_SHELL for the end to end test.');
    const requested = await execution.next('runtime.approval.requested');
    assert.ok(requested.type === 'runtime.approval.requested');
    await delay(1500);
    // Several reconnects while the approval waits re-read a session that did not change.
    const known = execution.observations.length;
    await delay(4000);
    assert.deepEqual(execution.types().slice(known), []);
    await runtime.respondToApproval({
      execution: execution.context,
      approval_id: requested.payload.approval_id,
      decision: 'approve',
      message: null,
    });
    await execution.next('runtime.turn.completed', 1, 20_000);
    await delay(1500);
    const count = (type: RuntimeEventType) =>
      execution.types().filter((item) => item === type).length;
    assert.equal(count('runtime.turn.started'), 1);
    assert.equal(count('runtime.approval.requested'), 1);
    assert.equal(count('runtime.turn.completed'), 1);
    assert.ok(count('runtime.tool.started') <= 1);
    assert.ok(count('runtime.approval.resolved') <= 1);
    assert.equal(count('runtime.connection.lost'), 0);
    assertValidObservations(execution.observations, execution.context);
  });
});

/** The server the adapter launched, reached the way the adapter reaches it, for what it does not do. */
function serverOf(runtime: OpenCodeRuntimeAdapter, sandbox: OpenCodeSandbox): OpenCodeClient {
  const record = JSON.parse(readFileSync(sandbox.recordFile, 'utf8')) as { port: number };
  const [password] = runtime.secrets();
  assert.ok(password !== undefined, 'the server holds a password');
  return new OpenCodeClient(`http://127.0.0.1:${record.port}`, password);
}

const call = (tool: string, args: Record<string, unknown>) =>
  `CALL ${tool} b64:${Buffer.from(JSON.stringify(args)).toString('base64')}`;

describe('OpenCode on a model on this Mac: its network tools are denied, and the probe sees nothing leave', {
  skip: SKIP,
}, () => {
  test(
    'every session denies the tools that reach the network, and the model is offered none of them',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const execution = await start('Hello.');
      await execution.next('runtime.turn.completed');
      const offered = sandbox.provider.requests[0]?.toolNames ?? [];
      assert.ok(offered.includes('shell') && offered.includes('read'), offered.join(','));
      for (const tool of ['execute', 'webfetch', 'websearch', 'subagent']) {
        assert.ok(!offered.includes(tool), `${tool} was offered: ${offered.join(',')}`);
      }
      // The rules travelled with the session, as Halcyonic created it.
      const started = execution.observations[0];
      assert.ok(started?.type === 'runtime.execution.started');
      const read = await serverOf(runtime, sandbox).request(
        'GET',
        `/api/session/${encodeURIComponent(started.payload.native_id ?? '')}`,
      );
      t.diagnostic(`session as read back: ${JSON.stringify(read.body).slice(0, 600)}`);
      const text = JSON.stringify(read.body);
      for (const action of ['execute', 'webfetch', 'websearch', 'subagent']) {
        assert.ok(
          text.includes(`{"action":"${action}","resource":"*","effect":"deny"}`),
          `${action} deny is not on the session`,
        );
      }
    },
  );

  test(
    'a write to a path OpenCode reads its configuration from is refused; any other is not',
    SLOW_TEST,
    async (t) => {
      const { sandbox, start } = await harness(t);
      for (const path of [
        '.opencode/plugin/planted.ts',
        'opencode.json',
        'sub/.opencode/plugins/p.ts',
        '.agents/skills/x/SKILL.md',
      ]) {
        const execution = await start(call('write', { path, content: 'export default {}\n' }));
        await execution.next('runtime.turn.completed');
        assert.equal(existsSync(join(sandbox.project, path)), false, `${path} was written`);
      }
      // The control: an ordinary file is written.
      const control = await start(call('write', { path: 'notes.txt', content: 'hello\n' }));
      await control.next('runtime.turn.completed');
      assert.equal(readFileSync(join(sandbox.project, 'notes.txt'), 'utf8'), 'hello\n');
    },
  );

  test(
    "Code Mode's fetch reaches a listener from a session without the rules, never from Halcyonic's",
    SLOW_TEST,
    async (t) => {
      const hits: string[] = [];
      const listener = createServer((request, response) => {
        hits.push(`${request.method} ${request.url}`);
        response.end('reached');
      });
      await new Promise<void>((resolve) => listener.listen(0, '127.0.0.1', () => resolve()));
      t.after(() => new Promise<void>((resolve) => listener.close(() => resolve())));
      const url = `http://127.0.0.1:${(listener.address() as AddressInfo).port}/code-mode`;
      const code = `const r = await fetch(${JSON.stringify(url)}, { method: 'POST', body: 'x' }); return await r.text();`;

      const { runtime, sandbox, start } = await harness(t);
      const ours = await start(call('execute', { code }));
      await ours.next('runtime.turn.completed');
      assert.deepEqual(hits, [], "Code Mode's fetch ran in Halcyonic's session");

      // The control: a session without Halcyonic's rules, on a server of its own in the same
      // sandbox (one opened on Halcyonic's server would stop it, as it should).
      await runtime.close();
      const bare = await launchServer({
        binaryPath: BINARY,
        environment: buildEnvironment(process.env, sandbox.env),
        recordFile: join(sandbox.root, 'control-server.json'),
        port: null,
        cwd: sandbox.project,
        startupTimeoutMs: 30_000,
      });
      t.after(() => bare.stop());
      const client = bare.client;
      const location = `?location%5Bdirectory%5D=${encodeURIComponent(sandbox.project)}`;
      for (let i = 0; i < 100; i += 1) {
        const models = await client.request('GET', `/api/model${location}`);
        if (JSON.stringify(models.body).includes('fake-model')) break;
        await delay(100);
      }
      const created = await client.request('POST', '/api/session', {
        body: { title: 'control', location: { directory: sandbox.project } },
      });
      const id = (created.body as { data: { id: string } }).data.id;
      await client.request('POST', `/api/session/${encodeURIComponent(id)}/prompt`, {
        body: { text: call('execute', { code }) },
      });
      await until(() => hits.length > 0, 30_000, "Code Mode's fetch in the control session");
      assert.deepEqual(hits, ['POST /code-mode']);
    },
  );

  test(
    "the probe's negative control: without its catalog switch, OpenCode reaches beyond loopback at launch, and the probe sees it",
    SLOW_TEST,
    async (t) => {
      try {
        await lookup('models.opencode.ai');
      } catch {
        t.skip('offline: models.opencode.ai does not resolve, so the control cannot show anything');
        return;
      }
      const sandbox = await createSandbox();
      t.after(() => sandbox.cleanup());
      // The sandbox's own folders, without its proxy variables or the adapter's switches.
      const keep = [
        'HOME',
        'XDG_CONFIG_HOME',
        'XDG_DATA_HOME',
        'XDG_STATE_HOME',
        'XDG_CACHE_HOME',
        'XDG_RUNTIME_DIR',
        'TMPDIR',
      ];
      const environment: Record<string, string> = { PATH: '/usr/bin:/bin' };
      for (const name of keep) {
        const value = sandbox.env[name];
        if (value !== undefined) environment[name] = value;
      }
      // Sampled often: a catalog request is short, and one shorter than a sample is missed.
      const probe = probeNetwork(process.pid, BINARY, 25);
      const server = await launchServer({
        binaryPath: BINARY,
        environment,
        recordFile: sandbox.recordFile,
        port: null,
        cwd: sandbox.project,
        startupTimeoutMs: 30_000,
      });
      try {
        // Listing a folder's models loads it, which fetches the catalog when its copy is stale.
        const location = `?location%5Bdirectory%5D=${encodeURIComponent(sandbox.project)}`;
        void server.client.request('GET', `/api/model${location}`).catch(() => undefined);
        await until(() => probe.beyondLoopback.length > 0, 30_000, 'a socket beyond loopback');
      } finally {
        await server.stop();
        await probe.stop();
      }
      assert.deepEqual(probe.blind, []);
      t.diagnostic(`seen: ${[...new Set(probe.beyondLoopback)].join('; ')}`);
    },
  );

  test('the network probe: nothing leaves loopback through startup, idle and a full run', {
    timeout: 300_000,
  }, async (t) => {
    const idleMs = Number(process.env.OPENCODE_E2E_PROBE_IDLE_MS ?? 60_000);
    const probe = probeNetwork(process.pid, BINARY);
    t.after(() => probe.stop());
    const { runtime, sandbox, start } = await harness(t);
    // Startup: listing the models launches the server.
    await runtime.listModels();
    await delay(idleMs);
    const afterIdle = probe.samples;
    const execution = await start(`RUN_SHELL then answer.`);
    const asked = await execution.next('runtime.approval.requested');
    assert.ok(asked.type === 'runtime.approval.requested');
    await runtime.respondToApproval({
      execution: execution.context,
      approval_id: asked.payload.approval_id,
      decision: 'approve',
      message: null,
    });
    await execution.next('runtime.turn.completed');
    await delay(3000);
    await probe.stop();
    const providerPort = new URL(sandbox.provider.baseUrl).port;
    assert.ok(afterIdle > idleMs / 1000, `only ${afterIdle} samples through startup and idle`);
    assert.deepEqual(probe.blind, [], 'the probe could not see the server');
    assert.ok(
      [...probe.loopback].some((name) => name.endsWith(`->127.0.0.1:${providerPort}`)),
      `the probe never saw the connection to the provider: ${[...probe.loopback].join(', ')}`,
    );
    assert.deepEqual(probe.beyondLoopback, [], 'an OpenCode process held a socket beyond loopback');
    t.diagnostic(`probe: ${probe.samples} samples, ${afterIdle} through startup and idle`);
  });
});

describe('OpenCode tasks stop when something other than Halcyonic changes what they may do', {
  skip: SKIP,
}, () => {
  const tampered = (execution: Execution) =>
    execution.observations.find(
      (item) =>
        item.type === 'runtime.turn.failed' && item.payload.error.code === 'runtime_tampered',
    );
  const tamperedMessage = (execution: Execution): string => {
    const failure = tampered(execution);
    return failure?.type === 'runtime.turn.failed' ? failure.payload.error.message : '';
  };
  const sessionOf = (execution: Execution): string => {
    const started = execution.observations[0];
    return started?.type === 'runtime.execution.started' ? (started.payload.native_id ?? '') : '';
  };

  test("replacing a running task's rules fails its turn as tampered", SLOW_TEST, async (t) => {
    const { runtime, sandbox, start } = await harness(t);
    const execution = await start('SLOW reply, please.');
    await execution.next('runtime.turn.started');
    const id = sessionOf(execution);
    await serverOf(runtime, sandbox).request('PATCH', `/api/session/${encodeURIComponent(id)}`, {
      body: { permissions: [{ action: '*', resource: '*', effect: 'allow' }] },
    });
    await until(() => tampered(execution) !== undefined, 15_000, 'the tampered failure');
    assert.match(tamperedMessage(execution), /changed this task's permission rules/);
    await assert.rejects(
      runtime.sendInstruction({ execution: execution.context, text: 'More.' }),
      RuntimeActionError,
    );
  });

  for (const decision of ['always', 'once'] as const) {
    test(
      `an approval answered "${decision}" by something else fails the task as tampered`,
      SLOW_TEST,
      async (t) => {
        const { runtime, sandbox, start } = await harness(t);
        const execution = await start('RUN_SHELL please.');
        const asked = await execution.next('runtime.approval.requested');
        assert.ok(asked.type === 'runtime.approval.requested');
        const id = sessionOf(execution);
        await serverOf(runtime, sandbox).request(
          'POST',
          `/api/session/${encodeURIComponent(id)}/permission/${encodeURIComponent(asked.payload.approval_id)}/reply`,
          { body: { decision } },
        );
        await until(() => tampered(execution) !== undefined, 15_000, 'the tampered failure');
        assert.match(tamperedMessage(execution), /approved a request on this task/);
      },
    );
  }

  test(
    'a session Halcyonic did not open on its server stops every task there, and the server',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      const first = await start('SLOW reply, please.');
      await first.next('runtime.turn.started');
      const second = await start('Hello.');
      await second.next('runtime.turn.completed');
      const pid = runtime.serverPid;
      assert.ok(pid !== null);
      await serverOf(runtime, sandbox).request('POST', '/api/session', {
        body: { title: 'not Halcyonic', location: { directory: sandbox.project } },
      });
      await until(
        () => tampered(first) !== undefined,
        15_000,
        "the running task's tampered failure",
      );
      await until(
        () => second.observations.some((item) => item.type === 'runtime.connection.lost'),
        15_000,
        'the resting task reported lost',
      );
      await until(() => !alive(pid), 15_000, 'the server to stop');
    },
  );

  test(
    'a shell command does not find the server password in its environment',
    SLOW_TEST,
    async (t) => {
      const { runtime, sandbox, start } = await harness(t);
      // Prints only the length, never the value.
      const execution = await start(
        call('shell', {
          command: 'printf "len=%s" "$(printf %s "$OPENCODE_PASSWORD" | wc -c | tr -d " ")"',
        }),
      );
      const asked = await execution.next('runtime.approval.requested');
      assert.ok(asked.type === 'runtime.approval.requested');
      await runtime.respondToApproval({
        execution: execution.context,
        approval_id: asked.payload.approval_id,
        decision: 'approve',
        message: null,
      });
      await execution.next('runtime.turn.completed');
      assert.equal(tampered(execution), undefined, 'its own approval was taken as tampering');
      const sent = sandbox.provider.requests.map((request) => request.body).join('\n');
      assert.ok(sent.includes('len=0'), 'the command saw the password');
    },
  );
});
