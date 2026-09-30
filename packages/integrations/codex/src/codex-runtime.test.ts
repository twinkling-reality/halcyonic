import assert from 'node:assert/strict';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, dirname, join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { compileValidator, RuntimeDescriptor } from '@halcyonic/contracts';
import {
  capabilityProblems,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import {
  CODEX_CAPABILITIES,
  CodexRuntimeAdapter,
  type CodexRuntimeOptions,
} from './codex-runtime.ts';
import { APPROVAL_METHODS, METHODS_USED } from './protocol.ts';
import { buildEnvironment, INHERITED_VARIABLES } from './server.ts';
import { allowOnly } from './testing/directory-policy.ts';
import { assertValidObservations, TEST_EXECUTION } from './testing/observations.ts';

const FAKE_CODEX = fileURLToPath(new URL('./testing/fake-codex.mjs', import.meta.url));

function temporary(t: TestContext): string {
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-codex-unit-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  return directory;
}

function actionError(code: string, effect: 'none' | 'unknown' = 'none') {
  return (error: unknown) =>
    error instanceof RuntimeActionError && error.code === code && error.effect === effect;
}

/** An adapter whose binary cannot start, whose policy allows only its temporary directory. */
function adapter(t: TestContext) {
  const directory = temporary(t);
  const runtime = new CodexRuntimeAdapter({
    binaryPath: '/nonexistent/codex',
    serverRecordFile: join(directory, 'server.json'),
    directoryPolicy: allowOnly(directory),
  });
  t.after(() => runtime.close());
  return { runtime, directory };
}

/** An adapter running the stand-in binary with the given behavior, and what it received. */
function fake(t: TestContext, mode: string[] = [], options: Partial<CodexRuntimeOptions> = {}) {
  const directory = temporary(t);
  const log = join(directory, 'received.jsonl');
  const recordFile = join(directory, 'server.json');
  const runtime = new CodexRuntimeAdapter({
    binaryPath: FAKE_CODEX,
    serverRecordFile: recordFile,
    directoryPolicy: allowOnly(directory),
    env: {
      PATH: [dirname(process.execPath), process.env.PATH ?? ''].join(delimiter),
      FAKE_CODEX_MODE: mode.join(','),
      FAKE_CODEX_LOG: log,
    },
    ...options,
  });
  t.after(() => runtime.close());
  const observations: RuntimeObservation[] = [];
  const received = (): Record<string, unknown>[] =>
    existsSync(log)
      ? readFileSync(log, 'utf8')
          .split('\n')
          .filter((line) => line !== '')
          .map((line) => JSON.parse(line) as Record<string, unknown>)
      : [];
  const start = (instruction = 'Do the work.') =>
    runtime.startExecution({
      execution: TEST_EXECUTION,
      instruction,
      options: { cwd: directory },
      emit: (observation) => observations.push(observation),
    });
  return { runtime, directory, recordFile, observations, received, start };
}

async function until(condition: () => boolean, what: string, timeoutMs = 10_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!condition()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
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

describe('Codex runtime descriptor', () => {
  test('is contract-valid and declares exactly the capabilities it implements', (t) => {
    const { runtime } = adapter(t);
    assert.ok(compileValidator(RuntimeDescriptor)(runtime.descriptor).ok);
    assert.deepEqual(capabilityProblems(runtime), []);
    assert.equal(runtime.descriptor.kind, 'codex');
    assert.equal(runtime.descriptor.display_name, 'Codex 0.157.0');
    assert.equal(runtime.descriptor.synthetic, false);
    assert.deepEqual(runtime.descriptor.capabilities, CODEX_CAPABILITIES);
    assert.deepEqual(CODEX_CAPABILITIES, {
      start_execution: true,
      instruct_at_rest: true,
      instruct_while_running: true,
      respond_to_approval: true,
      interrupt: true,
    });
  });

  test('uses only methods on the stable surface the pinned binary generated', () => {
    const stable = JSON.parse(
      readFileSync(new URL('../fixtures/methods-stable.json', import.meta.url), 'utf8'),
    ) as Record<string, string[]>;
    const client = [...(stable.ClientRequest ?? []), ...(stable.ClientNotification ?? [])];
    for (const method of METHODS_USED) assert.ok(client.includes(method), method);
    for (const method of APPROVAL_METHODS)
      assert.ok(stable.ServerRequest?.includes(method), method);
    for (const method of ['turn/started', 'turn/completed', 'item/started', 'item/completed']) {
      assert.ok(stable.ServerNotification?.includes(method), method);
    }
    assert.ok(stable.ServerNotification?.includes('serverRequest/resolved'));
  });
});

describe('Codex start options', () => {
  test('require an existing absolute cwd and accept a model, sandbox and approval policy', (t) => {
    const { runtime, directory } = adapter(t);
    const file = join(directory, 'a-file');
    writeFileSync(file, 'x');
    const invalid: Record<string, unknown>[] = [
      {},
      { cwd: '' },
      { cwd: 'relative/path' },
      { cwd: join(directory, 'missing') },
      { cwd: file },
      { cwd: directory, directory },
      { cwd: directory, model: '' },
      { cwd: directory, model: '-flag' },
      { cwd: directory, model: 42 },
      { cwd: directory, sandbox: 'none' },
      { cwd: directory, sandbox: null },
      { cwd: directory, approval_policy: 'on-failure' },
      { cwd: directory, approval_policy: { granular: { sandbox_approval: false } } },
      { cwd: directory, model_provider: '' },
      { cwd: directory, model_provider: 'ollama/local' },
      { cwd: directory, model_provider: 7 },
      { cwd: directory, context_window: 0 },
      { cwd: directory, context_window: 65536.5 },
      { cwd: directory, context_window: '65536' },
      { cwd: directory, auto_compact_token_limit: -1 },
      { cwd: directory, context_window: 65536, auto_compact_token_limit: 65536 },
    ];
    for (const options of invalid) {
      const result = runtime.validateStartOptions(options);
      assert.equal(result.ok, false, JSON.stringify(options));
      assert.ok(!result.ok && result.message.length > 0);
    }
    for (const options of [
      { cwd: directory },
      { cwd: directory, model: 'gpt-5.5' },
      { cwd: directory, model: 'openrouter/vendor/model:free' },
      { cwd: directory, sandbox: 'read-only', approval_policy: 'on-request' },
      { cwd: directory, sandbox: 'workspace-write', approval_policy: 'untrusted' },
      { cwd: directory, sandbox: 'danger-full-access', approval_policy: 'untrusted' },
      { cwd: directory, model_provider: 'ollama', model: 'qwen3.6:35b-a3b-nvfp4' },
      { cwd: directory, model_provider: 'my-gateway_2' },
      { cwd: directory, context_window: 65536, auto_compact_token_limit: 52000 },
      { cwd: directory, auto_compact_token_limit: 52000 },
    ]) {
      assert.deepEqual(
        runtime.validateStartOptions(options),
        { ok: true },
        JSON.stringify(options),
      );
    }
  });

  test('refuse any combination that would stop approvals reaching the person', (t) => {
    const { runtime, directory } = adapter(t);
    assert.deepEqual(runtime.validateStartOptions({ cwd: directory, approval_policy: 'never' }), {
      ok: false,
      message:
        'Option "approval_policy" cannot be "never": the person supervising the execution would never be asked.',
    });
    for (const approval of [undefined, 'on-request']) {
      const result = runtime.validateStartOptions({
        cwd: directory,
        sandbox: 'danger-full-access',
        ...(approval !== undefined && { approval_policy: approval }),
      });
      assert.match(result.ok ? '' : result.message, /needs "approval_policy" "untrusted"/);
    }
  });

  test('a directory the host policy refuses is refused, with the policy message', async (t) => {
    const { runtime } = adapter(t);
    const outside = temporary(t);
    const message = `${outside} is outside the directories this test allows.`;
    assert.deepEqual(runtime.validateStartOptions({ cwd: outside }), { ok: false, message });
    await assert.rejects(
      runtime.startExecution({
        execution: TEST_EXECUTION,
        instruction: 'Do the work.',
        options: { cwd: outside },
        emit: () => undefined,
      }),
      (error: unknown) =>
        actionError('invalid_runtime_options')(error) && (error as Error).message === message,
    );
  });

  test('the policy is asked for the path as given, and a policy that throws refuses', (t) => {
    const directory = temporary(t);
    const asked: string[] = [];
    const runtime = new CodexRuntimeAdapter({
      binaryPath: '/nonexistent/codex',
      serverRecordFile: join(directory, 'server.json'),
      directoryPolicy: (path) => {
        asked.push(path);
        if (path.endsWith('boom')) throw new Error('policy failure');
        return { ok: true, directory: path };
      },
    });
    t.after(() => runtime.close());
    mkdirSync(join(directory, 'sub'));
    const given = `${directory}/sub/..`;
    assert.deepEqual(runtime.validateStartOptions({ cwd: given }), { ok: true });
    assert.deepEqual(asked, [given]);
    const boom = join(directory, 'boom');
    mkdirSync(boom);
    const refused = runtime.validateStartOptions({ cwd: boom });
    assert.match(refused.ok ? '' : refused.message, /policy failure/);
  });
});

describe('Codex server environment', () => {
  test('inherits only the allowlist, keeps HOME and CODEX_HOME, and disables remote control', () => {
    const environment = buildEnvironment(
      {
        PATH: '/usr/bin',
        HOME: '/home/user',
        CODEX_HOME: '/home/user/.codex',
        SALIDIUM_INTERNAL: '1',
        OPENAI_API_KEY: 'not inherited',
        CODEX_INTERNAL_ORIGINATOR_OVERRIDE: 'someone else',
        NODE_OPTIONS: '--inspect',
      },
      { CODEX_API_KEY: 'configured' },
    );
    assert.deepEqual(environment, {
      PATH: '/usr/bin',
      HOME: '/home/user',
      CODEX_HOME: '/home/user/.codex',
      CODEX_API_KEY: 'configured',
      CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED: '1',
    });
    assert.ok(!INHERITED_VARIABLES.includes('SALIDIUM_INTERNAL'));
  });

  test('refuses a binary path that would be looked up on PATH', () => {
    assert.throws(
      () =>
        new CodexRuntimeAdapter({
          binaryPath: 'codex',
          serverRecordFile: join(tmpdir(), 'unused.json'),
          directoryPolicy: allowOnly(tmpdir()),
        }),
      /must be absolute/,
    );
  });

  test('refuses variables the adapter owns, including SALIDIUM_INTERNAL', (t) => {
    for (const name of [
      'SALIDIUM_INTERNAL',
      'CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED',
      'CODEX_INTERNAL_ORIGINATOR_OVERRIDE',
    ]) {
      assert.throws(() => buildEnvironment({}, { [name]: 'x' }), new RegExp(name));
      assert.throws(
        () =>
          new CodexRuntimeAdapter({
            binaryPath: '/nonexistent/codex',
            serverRecordFile: join(temporary(t), 'server.json'),
            directoryPolicy: allowOnly(tmpdir()),
            env: { [name]: 'x' },
          }),
        new RegExp(name),
      );
    }
  });
});

describe('Codex runtime without a server', () => {
  test('a binary that cannot be started fails the start with no effect and no observation', async (t) => {
    const { runtime, directory } = adapter(t);
    const observed: RuntimeObservation[] = [];
    await assert.rejects(
      runtime.startExecution({
        execution: TEST_EXECUTION,
        instruction: 'Do the work.',
        options: { cwd: directory },
        emit: (observation) => observed.push(observation),
      }),
      actionError('runtime_unavailable'),
    );
    assert.deepEqual(observed, []);
    assert.equal(runtime.serverPid, null);
    assert.equal(existsSync(join(directory, 'server.json')), false);
  });

  test('actions on an execution it never started are refused', async (t) => {
    const { runtime } = adapter(t);
    const execution = TEST_EXECUTION;
    await assert.rejects(
      runtime.interrupt({ execution }),
      actionError('execution_unknown_to_runtime'),
    );
    await assert.rejects(
      runtime.sendInstruction({ execution, text: 'More.' }),
      actionError('execution_unknown_to_runtime'),
    );
    await assert.rejects(
      runtime.respondToApproval({ execution, approval_id: 'a', decision: 'deny', message: null }),
      actionError('execution_unknown_to_runtime'),
    );
  });

  test('after close every action is refused, and closing again is harmless', async (t) => {
    const { runtime, directory } = adapter(t);
    assert.deepEqual(await runtime.stopStaleServer(), { outcome: 'none' });
    await runtime.close();
    await runtime.close();
    await assert.rejects(
      runtime.startExecution({
        execution: TEST_EXECUTION,
        instruction: 'Do the work.',
        options: { cwd: directory },
        emit: () => undefined,
      }),
      actionError('runtime_closed'),
    );
    await assert.rejects(
      runtime.interrupt({ execution: TEST_EXECUTION }),
      actionError('runtime_closed'),
    );
  });
});

describe('Codex runtime against a stand-in binary', () => {
  test('refuses a binary that reports another version, before starting a server', async (t) => {
    const { start, observations, recordFile, received } = fake(t, ['version=0.156.0']);
    await assert.rejects(start(), (error: unknown) => {
      assert.ok(actionError('runtime_version_unsupported')(error));
      assert.match((error as Error).message, /reports version 0\.156\.0; .* only 0\.157\.0/);
      return true;
    });
    assert.deepEqual(observations, []);
    assert.deepEqual(received(), []);
    assert.equal(existsSync(recordFile), false);
  });

  test('refuses a server whose user agent reports another version, and stops it', async (t) => {
    const { runtime, start, recordFile } = fake(t, ['agent=0.158.0']);
    await assert.rejects(start(), actionError('runtime_version_unsupported'));
    assert.equal(runtime.serverPid, null);
    assert.equal(existsSync(recordFile), false);
  });

  test('tags every thread as Halcyonic and asks for the settings explicitly', async (t) => {
    const { start, received, directory, observations } = fake(t);
    const { native_id } = await start('COMPLETE the work.');
    const sent = received();
    const initialize = sent.find((message) => message.method === 'initialize');
    assert.deepEqual(initialize?.params, {
      clientInfo: { name: 'halcyonic', title: 'Halcyonic', version: '0.0.0' },
      capabilities: null,
    });
    // The thread works in the real path the directory policy returned.
    const threadStart = sent.find((message) => message.method === 'thread/start');
    assert.deepEqual(threadStart?.params, {
      cwd: realpathSync(directory),
      approvalPolicy: 'on-request',
      approvalsReviewer: 'user',
      sandbox: 'workspace-write',
      threadSource: 'halcyonic',
    });
    assert.deepEqual(
      sent.map((message) => message.method),
      ['initialize', 'initialized', 'thread/start', 'turn/start'],
    );
    await until(() => observations.length === 3, 'the turn');
    assert.deepEqual(
      observations.map((item) => item.type),
      ['runtime.execution.started', 'runtime.turn.started', 'runtime.turn.completed'],
    );
    assert.deepEqual(observations[0]?.payload, { native_id });
    assertValidObservations(observations);
  });

  test('asks for the model provider, model and context settings of a local model', async (t) => {
    const { runtime, received, directory, observations } = fake(t);
    await runtime.startExecution({
      execution: TEST_EXECUTION,
      instruction: 'COMPLETE the work.',
      options: {
        cwd: directory,
        model_provider: 'ollama',
        model: 'qwen3.6:35b-a3b-nvfp4',
        context_window: 65536,
        auto_compact_token_limit: 52000,
      },
      emit: (observation) => observations.push(observation),
    });
    const threadStart = received().find((message) => message.method === 'thread/start');
    assert.deepEqual(threadStart?.params, {
      cwd: realpathSync(directory),
      approvalPolicy: 'on-request',
      approvalsReviewer: 'user',
      sandbox: 'workspace-write',
      model: 'qwen3.6:35b-a3b-nvfp4',
      modelProvider: 'ollama',
      config: { model_context_window: 65536, model_auto_compact_token_limit: 52000 },
      threadSource: 'halcyonic',
    });
    await until(() => observations.length === 3, 'the turn');
  });

  test('refuses a thread Codex does not run on the requested model and provider', async (t) => {
    const { runtime, directory, observations } = fake(t, ['other-model']);
    for (const options of [
      { model_provider: 'ollama', model: 'qwen3.6:35b-a3b-nvfp4' },
      { model_provider: 'ollama' },
      { model: 'qwen3.6:35b-a3b-nvfp4' },
    ]) {
      await assert.rejects(
        runtime.startExecution({
          execution: TEST_EXECUTION,
          instruction: 'COMPLETE the work.',
          options: { cwd: directory, ...options },
          emit: (observation) => observations.push(observation),
        }),
        (error: unknown) => {
          assert.ok(actionError('runtime_refused')(error));
          assert.match(
            (error as Error).message,
            /reports model "gpt-5\.5" from provider "openai"\. The thread is not used\./,
          );
          return true;
        },
        JSON.stringify(options),
      );
    }
    assert.deepEqual(observations, []);
  });

  test('refuses a thread Codex did not give the requested approval policy', async (t) => {
    const { start, observations } = fake(t, ['never']);
    await assert.rejects(start(), (error: unknown) => {
      assert.ok(actionError('runtime_refused')(error));
      assert.match((error as Error).message, /approval policy "never"/);
      return true;
    });
    assert.deepEqual(observations, []);
  });

  test('steers a running turn with its id and starts a turn only at rest', async (t) => {
    const { runtime, start, received, observations } = fake(t);
    await start();
    await runtime.sendInstruction({ execution: TEST_EXECUTION, text: 'Also this.' });
    const [started, turn] = observations;
    assert.ok(
      started?.type === 'runtime.execution.started' && turn?.type === 'runtime.turn.started',
    );
    const steer = received().find((message) => message.method === 'turn/steer');
    assert.deepEqual(steer?.params, {
      threadId: started.payload.native_id,
      expectedTurnId: turn.payload.turn_id,
      input: [{ type: 'text', text: 'Also this.', text_elements: [] }],
    });
    await runtime.interrupt({ execution: TEST_EXECUTION });
    await until(
      () => observations.some((item) => item.type === 'runtime.turn.interrupted'),
      'the end',
    );
    await assert.rejects(
      runtime.interrupt({ execution: TEST_EXECUTION }),
      actionError('no_running_turn'),
    );
    await runtime.sendInstruction({ execution: TEST_EXECUTION, text: 'A new turn.' });
    assert.deepEqual(
      received()
        .map((message) => message.method)
        .filter((method) => typeof method === 'string' && method.startsWith('turn/')),
      ['turn/start', 'turn/steer', 'turn/interrupt', 'turn/start'],
    );
  });

  test('an unanswered interrupt is reported with an unknown effect', async (t) => {
    const { runtime, start } = fake(t, ['silent-interrupt'], { interruptTimeoutMs: 300 });
    await start();
    await assert.rejects(
      runtime.interrupt({ execution: TEST_EXECUTION }),
      actionError('interrupt_unconfirmed', 'unknown'),
    );
  });

  test('a request Halcyonic does not show the person is refused with an error', async (t) => {
    const { start, received } = fake(t, ['ask']);
    await start();
    await until(
      () => received().some((message) => message.id === 0 && message.error !== undefined),
      'the refusal',
    );
    const refusal = received().find((message) => message.id === 0 && message.method === undefined);
    assert.deepEqual(refusal?.error, {
      code: -32601,
      message: 'Halcyonic does not handle item/tool/requestUserInput requests.',
    });
  });

  test('after a restart, a thread another Codex process holds is refused clearly', async (t) => {
    const { runtime, start, observations } = fake(t, ['writer-held']);
    await start();
    const pid = runtime.serverPid;
    assert.ok(pid !== null);
    process.kill(pid, 'SIGKILL');
    await until(
      () => observations.some((item) => item.type === 'runtime.connection.lost'),
      'the loss',
    );
    const lost = observations.at(-1);
    assert.equal(lost?.type, 'runtime.connection.lost');
    assert.match(
      lost?.type === 'runtime.connection.lost' ? lost.payload.reason : '',
      /^The Codex server exited unexpectedly \(signal SIGKILL\)\. After it was started again, the thread could not be resumed because another Codex process holds it\.$/,
    );
    assert.notEqual(runtime.serverPid, pid);
    await assert.rejects(
      runtime.sendInstruction({ execution: TEST_EXECUTION, text: 'More.' }),
      actionError('runtime_unreachable'),
    );
    assertValidObservations(observations);
  });

  test('a running turn Codex has no record of after a restart is reported lost', async (t) => {
    const { runtime, start, observations } = fake(t);
    await start();
    process.kill(runtime.serverPid ?? 0, 'SIGKILL');
    await until(
      () => observations.some((item) => item.type === 'runtime.connection.lost'),
      'the loss',
    );
    const lost = observations.at(-1);
    assert.match(
      lost?.type === 'runtime.connection.lost' ? lost.payload.reason : '',
      /Codex had no final record of the turn that was running\.$/,
    );
  });

  test('a resting thread survives a restart; a relaunched server that dies at once is given up', async (t) => {
    const { runtime, start, observations, received } = fake(t);
    await start('COMPLETE the work.');
    await until(() => observations.length === 3, 'the turn');
    const first = runtime.serverPid;
    process.kill(first ?? 0, 'SIGKILL');
    await until(
      () => received().some((message) => message.method === 'thread/turns/list'),
      'the thread to be resumed',
    );
    await until(() => runtime.serverPid !== null && runtime.serverPid !== first, 'the relaunch');
    const resume = received().find((message) => message.method === 'thread/resume');
    assert.deepEqual(Object.keys(resume?.params as object).sort(), [
      'approvalPolicy',
      'approvalsReviewer',
      'cwd',
      'excludeTurns',
      'sandbox',
      'threadId',
    ]);
    await runtime.sendInstruction({ execution: TEST_EXECUTION, text: 'COMPLETE again.' });
    await until(() => observations.length === 5, 'the second turn');
    process.kill(runtime.serverPid ?? 0, 'SIGKILL');
    await until(
      () => observations.some((item) => item.type === 'runtime.connection.lost'),
      'the loss',
    );
    const lost = observations.at(-1);
    assert.match(
      lost?.type === 'runtime.connection.lost' ? lost.payload.reason : '',
      /started again moments before, so it was given up\.$/,
    );
    assertValidObservations(observations);
  });

  test('close stops the server and its watchdog and removes the record', async (t) => {
    const { runtime, start, recordFile } = fake(t);
    await start();
    const pid = runtime.serverPid;
    assert.ok(pid !== null);
    assert.ok(existsSync(recordFile));
    await runtime.close();
    assert.equal(alive(pid), false);
    assert.equal(existsSync(recordFile), false);
    await assert.rejects(
      runtime.sendInstruction({ execution: TEST_EXECUTION, text: 'More.' }),
      actionError('runtime_closed'),
    );
  });
});
