import assert from 'node:assert/strict';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  statSync,
  symlinkSync,
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
import {
  APP_SERVER_ARGUMENTS,
  buildEnvironment,
  INHERITED_VARIABLES,
  SIGN_IN_VARIABLES,
} from './server.ts';
import { allowOnly } from './testing/directory-policy.ts';
import { assertValidObservations, TEST_EXECUTION } from './testing/observations.ts';

const FAKE_CODEX = fileURLToPath(new URL('./testing/fake-codex.mjs', import.meta.url));

/** The configuration `pnpm mac-setup local-model` gives Codex's home: Ollama on this Mac. */
const LOCAL_CONFIG = { model_provider: 'ollama', model: 'qwen3.6:35b-a3b-nvfp4' };

function temporary(t: TestContext): string {
  // A real path, as the host binds a project's folder; macOS's temporary directory is a link.
  const directory = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-codex-unit-')));
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
    codexHome: join(temporary(t), 'codex-home'),
    serverRecordFile: join(directory, 'server.json'),
    directoryPolicy: allowOnly(directory),
  });
  t.after(() => runtime.close());
  return { runtime, directory };
}

/** An adapter running the stand-in binary with the given behavior, and what it received. */
function fake(
  t: TestContext,
  mode: string[] = [],
  options: Partial<CodexRuntimeOptions> = {},
  env: Readonly<Record<string, string>> = {},
) {
  const directory = temporary(t);
  const log = join(directory, 'received.jsonl');
  const recordFile = join(directory, 'server.json');
  // Outside the project's folder, as the control plane keeps it in its data directory.
  const codexHome = join(temporary(t), 'codex-home');
  const launches = join(directory, 'launches.jsonl');
  const runtime = new CodexRuntimeAdapter({
    binaryPath: FAKE_CODEX,
    codexHome,
    serverRecordFile: recordFile,
    directoryPolicy: allowOnly(directory),
    env: {
      PATH: [dirname(process.execPath), process.env.PATH ?? ''].join(delimiter),
      FAKE_CODEX_MODE: mode.join(','),
      FAKE_CODEX_LOG: log,
      FAKE_CODEX_LAUNCHES: launches,
      FAKE_CODEX_CONFIG: JSON.stringify(LOCAL_CONFIG),
      ...env,
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
      options: {},
      model_ref: null,
      directory,
      emit: (observation) => observations.push(observation),
    });
  const launched = (): { argv: string[]; codexHome: string | null }[] =>
    existsSync(launches)
      ? readFileSync(launches, 'utf8')
          .split('\n')
          .filter((line) => line !== '')
          .map((line) => JSON.parse(line) as { argv: string[]; codexHome: string | null })
      : [];
  return { runtime, directory, recordFile, codexHome, observations, received, launched, start };
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
      answer_question: true,
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
  test('accept a model, sandbox and approval policy, and no folder: the thread works in the project folder', (t) => {
    const { runtime, directory } = adapter(t);
    const invalid: Record<string, unknown>[] = [
      { cwd: directory },
      { directory },
      { model: '' },
      { model: '-flag' },
      { model: 42 },
      { sandbox: 'none' },
      { sandbox: null },
      { approval_policy: 'on-failure' },
      { approval_policy: { granular: { sandbox_approval: false } } },
      { model_provider: '' },
      { model_provider: 'ollama/local' },
      { model_provider: 7 },
      { context_window: 0 },
      { context_window: 65536.5 },
      { context_window: '65536' },
      { auto_compact_token_limit: -1 },
      { context_window: 65536, auto_compact_token_limit: 65536 },
    ];
    for (const options of invalid) {
      const result = runtime.validateStartOptions(options, null);
      assert.equal(result.ok, false, JSON.stringify(options));
      assert.ok(!result.ok && result.message.length > 0);
    }
    assert.match(
      JSON.stringify(runtime.validateStartOptions({ cwd: directory }, null)),
      /project's folder/,
    );
    for (const options of [
      {},
      { model: 'gpt-5.5' },
      { model: 'openrouter/vendor/model:free' },
      { sandbox: 'read-only', approval_policy: 'on-request' },
      { sandbox: 'workspace-write', approval_policy: 'untrusted' },
      { sandbox: 'danger-full-access', approval_policy: 'untrusted' },
      { model_provider: 'ollama', model: 'qwen3.6:35b-a3b-nvfp4' },
      { model_provider: 'my-gateway_2' },
      { context_window: 65536, auto_compact_token_limit: 52000 },
      { auto_compact_token_limit: 52000 },
    ]) {
      assert.deepEqual(
        runtime.validateStartOptions(options, null),
        { ok: true },
        JSON.stringify(options),
      );
    }
  });

  test('refuse any combination that would stop approvals reaching the person', (t) => {
    const { runtime } = adapter(t);
    assert.deepEqual(runtime.validateStartOptions({ approval_policy: 'never' }, null), {
      ok: false,
      message:
        'Option "approval_policy" cannot be "never": the person supervising the execution would never be asked.',
    });
    for (const approval of [undefined, 'on-request']) {
      const result = runtime.validateStartOptions(
        {
          sandbox: 'danger-full-access',
          ...(approval !== undefined && { approval_policy: approval }),
        },
        null,
      );
      assert.match(result.ok ? '' : result.message, /needs "approval_policy" "untrusted"/);
    }
  });
});

describe("Codex and the project's folder", () => {
  const start = (runtime: CodexRuntimeAdapter, directory: string | null) =>
    runtime.startExecution({
      execution: TEST_EXECUTION,
      instruction: 'Do the work.',
      options: {},
      model_ref: null,
      directory,
      emit: () => undefined,
    });

  test('declares that it works in the project folder', (t) => {
    assert.equal(adapter(t).runtime.descriptor.uses_project_location, true);
  });

  // The binary cannot start, so a refusal about the folder proves nothing was launched first.
  test('the host policy is asked again before anything is launched, and its refusal is the failure', async (t) => {
    const { runtime, directory } = adapter(t);
    await assert.rejects(start(runtime, null), actionError('location_required'));
    const outside = temporary(t);
    await assert.rejects(
      start(runtime, outside),
      (error: unknown) =>
        actionError('location_not_allowed')(error) &&
        (error as Error).message === `${outside} is outside the directories this test allows.`,
    );
    await assert.rejects(
      start(runtime, join(directory, 'missing')),
      actionError('location_missing'),
    );
    assert.equal(runtime.serverPid, null);
  });

  test('a folder whose path now leads elsewhere through a symbolic link is refused', async (t) => {
    const { runtime, directory } = adapter(t);
    const other = join(directory, 'other');
    mkdirSync(other);
    const bound = join(directory, 'bound');
    // The project was bound to `bound`; since then it was replaced by a link to another folder.
    symlinkSync(other, bound);
    await assert.rejects(
      start(runtime, bound),
      (error: unknown) =>
        actionError('location_missing')(error) && (error as Error).message.includes(other),
    );
  });

  test('a policy that throws refuses', async (t) => {
    const directory = temporary(t);
    const runtime = new CodexRuntimeAdapter({
      binaryPath: '/nonexistent/codex',
      serverRecordFile: join(directory, 'server.json'),
      codexHome: join(directory, 'codex-home'),
      directoryPolicy: () => {
        throw new Error('policy failure');
      },
    });
    t.after(() => runtime.close());
    await assert.rejects(
      start(runtime, directory),
      (error: unknown) =>
        actionError('location_not_allowed')(error) &&
        /policy failure/.test((error as Error).message),
    );
  });
});

describe('Codex start failures', () => {
  test("never carry the server's error output, which may hold a key", async (t) => {
    const secret = 'sk-FAKE-not-real-0123456789';
    for (const [mode, said] of [
      // Whichever it sees first: the exit, or the connection ending before initialize was answered.
      [
        'startup-fails',
        /^(?:Codex exited during startup \(exit code 1\)|Could not start Codex: [^.]+)\. Its/,
      ],
      ['version-fails', /^Could not read the version of the Codex binary at .+ \(exit code 3\)\./],
    ] as const) {
      const { start } = fake(t, [mode], {}, { FAKE_CODEX_SECRET: secret });
      await assert.rejects(start(), (error: unknown) => {
        const message = (error as Error).message;
        assert.ok(actionError('runtime_unavailable')(error), message);
        assert.match(message, said);
        assert.match(message, /not reported, since it may hold secrets/);
        assert.ok(!message.includes('sk-FAKE'), message);
        return true;
      });
    }
  });
});

describe('Codex server environment', () => {
  test("inherits only the allowlist, keeps HOME, gives Codex Halcyonic's home, and disables remote control", () => {
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
      { GIT_AUTHOR_NAME: 'configured' },
      '/home/user/.halcyonic/codex-home',
    );
    assert.deepEqual(environment, {
      PATH: '/usr/bin',
      HOME: '/home/user',
      GIT_AUTHOR_NAME: 'configured',
      CODEX_HOME: '/home/user/.halcyonic/codex-home',
      CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED: '1',
    });
    assert.ok(!INHERITED_VARIABLES.includes('SALIDIUM_INTERNAL'));
    assert.ok(!INHERITED_VARIABLES.includes('CODEX_HOME'));
  });

  test('refuses a binary path that would be looked up on PATH', () => {
    assert.throws(
      () =>
        new CodexRuntimeAdapter({
          binaryPath: 'codex',
          codexHome: join(tmpdir(), 'codex-home'),
          serverRecordFile: join(tmpdir(), 'unused.json'),
          directoryPolicy: allowOnly(tmpdir()),
        }),
      /must be absolute/,
    );
  });

  test('refuses variables the adapter owns, including SALIDIUM_INTERNAL, CODEX_HOME and every sign-in', (t) => {
    for (const name of [
      'SALIDIUM_INTERNAL',
      'CODEX_HOME',
      'CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED',
      'CODEX_INTERNAL_ORIGINATOR_OVERRIDE',
      ...SIGN_IN_VARIABLES,
    ]) {
      assert.throws(() => buildEnvironment({}, { [name]: 'x' }, '/home'), new RegExp(name));
      assert.throws(
        () =>
          new CodexRuntimeAdapter({
            binaryPath: '/nonexistent/codex',
            codexHome: join(tmpdir(), 'codex-home'),
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
        options: {},
        model_ref: null,
        directory,
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
        options: {},
        model_ref: null,
        directory,
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
      // The configured provider, once Codex's configuration shows it on this Mac, by name.
      modelProvider: 'ollama',
      // The model may ask the person questions (ADR 0022).
      config: { 'features.default_mode_request_user_input': true },
      threadSource: 'halcyonic',
    });
    assert.deepEqual(
      sent.map((message) => message.method),
      ['initialize', 'initialized', 'config/read', 'thread/start', 'turn/start'],
    );
    await until(() => observations.length === 4, 'the turn');
    assert.deepEqual(
      observations.map((item) => item.type),
      [
        'runtime.execution.started',
        'runtime.model.used',
        'runtime.turn.started',
        'runtime.turn.completed',
      ],
    );
    assert.deepEqual(observations[0]?.payload, { native_id });
    // The model the thread got, as Codex reported it in its answer to thread/start.
    assert.deepEqual(observations[1]?.payload, { model_ref: 'ollama/qwen3.6:35b-a3b-nvfp4' });
    assertValidObservations(observations);
  });

  test('asks for the model provider, model and context settings of a local model', async (t) => {
    const { runtime, received, directory, observations } = fake(t);
    await runtime.startExecution({
      execution: TEST_EXECUTION,
      instruction: 'COMPLETE the work.',
      options: {
        model_provider: 'ollama',
        model: 'qwen3.6:35b-a3b-nvfp4',
        context_window: 65536,
        auto_compact_token_limit: 52000,
      },
      model_ref: null,
      directory,
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
      config: {
        model_context_window: 65536,
        model_auto_compact_token_limit: 52000,
        'features.default_mode_request_user_input': true,
      },
      threadSource: 'halcyonic',
    });
    await until(() => observations.length === 4, 'the turn');
    assert.deepEqual(observations[1]?.payload, { model_ref: 'ollama/qwen3.6:35b-a3b-nvfp4' });
  });

  test("launches Codex in Halcyonic's own home, with every local-only setting above its configuration", async (t) => {
    const { start, launched, codexHome } = fake(t);
    await start('COMPLETE the work.');
    assert.deepEqual(launched(), [{ argv: [...APP_SERVER_ARGUMENTS], codexHome }]);
    assert.deepEqual(APP_SERVER_ARGUMENTS, [
      'app-server',
      '-c',
      'features.plugins=false',
      '-c',
      'check_for_update_on_startup=false',
      '-c',
      'analytics.enabled=false',
      '-c',
      'web_search="disabled"',
      '-c',
      'cli_auth_credentials_store="file"',
    ]);
    // Made for Codex, closed to other users.
    assert.equal(statSync(codexHome).mode & 0o777, 0o700);
  });

  test('refuses to launch Codex in a home that is a link, open to others, or holds a sign-in', async (t) => {
    const cases: [string, (home: string) => void, RegExp][] = [
      [
        'open',
        (home) => mkdirSync(home, { mode: 0o755 }),
        /Other users can open Codex's home .* Run chmod 700 on it\./,
      ],
      [
        'link',
        (home) => {
          const target = join(dirname(home), 'elsewhere');
          mkdirSync(target, { mode: 0o700 });
          symlinkSync(target, home);
        },
        /is not a folder, or is a link\./,
      ],
      [
        'signed in',
        (home) => {
          mkdirSync(home, { mode: 0o700 });
          writeFileSync(join(home, 'auth.json'), '{}', { mode: 0o600 });
        },
        /holds a sign-in \(auth\.json\)/,
      ],
    ];
    for (const [what, prepare, why] of cases) {
      const { start, launched, codexHome, observations } = fake(t);
      prepare(codexHome);
      chmodSync(codexHome, what === 'open' ? 0o755 : 0o700);
      await assert.rejects(start('COMPLETE the work.'), (error: unknown) => {
        assert.ok(actionError('runtime_unavailable')(error), what);
        assert.match((error as Error).message, why, what);
        return true;
      });
      assert.deepEqual(launched(), [], `${what}: Codex was launched`);
      assert.deepEqual(observations, []);
    }
  });

  test('starts a thread only on a provider served on this Mac, asked for by name', async (t) => {
    const remote = [
      [{}, {}, /the provider openai is a remote service/],
      [LOCAL_CONFIG, { model_provider: 'openai' }, /the provider openai is a remote service/],
      [{ model_provider: 'amazon-bedrock' }, {}, /the provider amazon-bedrock is a remote service/],
      [
        {
          model_provider: 'gateway',
          model_providers: { gateway: { base_url: 'https://gateway.example/v1' } },
        },
        {},
        /the provider gateway is a remote service/,
      ],
      [{ model_provider: 'mystery' }, {}, /the provider mystery is not known to be on this Mac/],
    ] as const;
    for (const [config, options, why] of remote) {
      const { runtime, received, directory, observations } = fake(
        t,
        [],
        {},
        {
          FAKE_CODEX_CONFIG: JSON.stringify(config),
        },
      );
      await assert.rejects(
        runtime.startExecution({
          execution: TEST_EXECUTION,
          instruction: 'COMPLETE the work.',
          options,
          model_ref: null,
          directory,
          emit: (observation) => observations.push(observation),
        }),
        (error: unknown) => {
          assert.ok(actionError('model_unavailable')(error), JSON.stringify(config));
          assert.match((error as Error).message, why);
          return true;
        },
      );
      assert.ok(!received().some((message) => message.method === 'thread/start'));
      assert.deepEqual(observations, []);
    }
    // A provider the configuration defines on loopback is on this Mac; the thread asks for it.
    const gateway = {
      model_provider: 'gateway',
      model: 'local-model',
      model_providers: { gateway: { base_url: 'http://127.0.0.1:8080/v1' } },
    };
    const { start, received } = fake(t, [], {}, { FAKE_CODEX_CONFIG: JSON.stringify(gateway) });
    await start('COMPLETE the work.');
    const threadStart = received().find((message) => message.method === 'thread/start');
    const params = (threadStart?.params ?? {}) as { modelProvider?: unknown };
    assert.equal(params.modelProvider, 'gateway');
  });

  test('refuses a model Ollama runs on its own remote service, chosen, configured or reported', async (t) => {
    const { runtime } = adapter(t);
    for (const [options, modelRef] of [
      [{ model: 'gpt-oss:120b-cloud' }, null],
      [{ model_provider: 'ollama', model: 'qwen3-coder:480b-cloud' }, null],
      [{}, 'ollama/gpt-oss:120b-cloud'],
      [{}, 'ollama/deepseek-v3.1:671b-cloud'],
    ] as const) {
      const validation = runtime.validateStartOptions(options, modelRef);
      assert.equal(validation.ok, false, JSON.stringify([options, modelRef]));
      assert.match(
        validation.ok ? '' : validation.message,
        /runs on Ollama's remote service, not on this Mac/,
      );
    }
    const configured = fake(
      t,
      [],
      {},
      {
        FAKE_CODEX_CONFIG: JSON.stringify({
          model_provider: 'ollama',
          model: 'gpt-oss:120b-cloud',
        }),
      },
    );
    await assert.rejects(configured.start('COMPLETE the work.'), (error: unknown) => {
      assert.ok(actionError('runtime_refused')(error));
      assert.match(
        (error as Error).message,
        /reports model "gpt-oss:120b-cloud" from provider "ollama"/,
      );
      return true;
    });
    assert.ok(!configured.received().some((message) => message.method === 'turn/start'));
  });

  test("refuses a thread Codex reports working in another folder than the project's", async (t) => {
    const { start, observations } = fake(t, ['other-cwd']);
    await assert.rejects(
      start(),
      (error: unknown) =>
        actionError('runtime_refused')(error) &&
        /working in "\/somewhere\/else", not in the project's folder/.test(
          (error as Error).message,
        ),
    );
    assert.deepEqual(observations, []);
  });

  test('the folder is asked about again right before it is handed to Codex', async (t) => {
    let asked = 0;
    const { start, received } = fake(t, [], {
      directoryPolicy: (path) => {
        asked += 1;
        return asked === 1
          ? { ok: true, directory: path }
          : { ok: false, code: 'location_missing', message: `${path} was removed.` };
      },
    });
    await assert.rejects(start(), actionError('location_missing'));
    assert.equal(asked, 2);
    assert.ok(!received().some((message) => message.method === 'thread/start'));
  });

  test('a thread whose folder has gone since is not resumed after a restart', async (t) => {
    let removed = false;
    const { runtime, start, observations, received } = fake(t, [], {
      directoryPolicy: (path) =>
        removed
          ? { ok: false, code: 'location_missing', message: `${path} was removed.` }
          : { ok: true, directory: path },
    });
    await start('COMPLETE the work.');
    await until(() => observations.length === 4, 'the turn');
    removed = true;
    process.kill(runtime.serverPid ?? 0, 'SIGKILL');
    await until(
      () => observations.some((item) => item.type === 'runtime.connection.lost'),
      'the loss',
    );
    const lost = observations.at(-1);
    assert.match(
      lost?.type === 'runtime.connection.lost' ? lost.payload.reason : '',
      /the thread could not be resumed: .* was removed\.$/,
    );
    assert.ok(!received().some((message) => message.method === 'thread/resume'));
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
          options,
          model_ref: null,
          directory,
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

  test('lists only models served on this Mac: the configured local model, never a remote catalog', async (t) => {
    const catalog = [
      { id: 'gpt-5.5', model: 'gpt-5.5', displayName: 'GPT-5.5', hidden: false },
      { id: 'gpt-hidden', model: 'gpt-hidden', displayName: 'Hidden', hidden: true },
      { id: 'gpt-6-sol', model: 'gpt-6-sol', displayName: 'GPT-6-Sol', hidden: false },
    ];
    // Configured for OpenAI, Codex's default: its catalog is OpenAI's, served remotely, and
    // through Halcyonic Codex offers no remote model.
    const hosted = fake(
      t,
      [],
      {},
      { FAKE_CODEX_CATALOG: JSON.stringify(catalog), FAKE_CODEX_CONFIG: '{}' },
    );
    assert.deepEqual(await hosted.runtime.listModels(), []);
    // Every page was read, and only the stable methods were used.
    assert.deepEqual(
      hosted
        .received()
        .map((message) => message.method)
        .filter((method) => method === 'config/read' || method === 'model/list'),
      ['config/read', 'model/list', 'model/list', 'model/list'],
    );

    const local = fake(
      t,
      [],
      {},
      {
        FAKE_CODEX_CATALOG: JSON.stringify(catalog),
        FAKE_CODEX_CONFIG: JSON.stringify({
          model_provider: 'ollama',
          model: 'gpt-4o:latest',
          model_context_window: 65536,
        }),
      },
    );
    // A local model named after a hosted one reads as the local provider's, and the built-in
    // catalog, which is OpenAI's, is not offered as Ollama's.
    assert.deepEqual(await local.runtime.listModels(), [
      {
        model_ref: 'ollama/gpt-4o:latest',
        display_name: 'gpt-4o:latest (Ollama)',
        served: 'this_mac',
        tool_calling: 'unknown',
        context_tokens: 65536,
      },
    ]);

    // A model Ollama runs on its own remote service is not offered, although Ollama serves it.
    const cloud = fake(
      t,
      [],
      {},
      {
        FAKE_CODEX_CONFIG: JSON.stringify({
          model_provider: 'ollama',
          model: 'gpt-oss:120b-cloud',
        }),
      },
    );
    assert.deepEqual(await cloud.runtime.listModels(), []);
  });

  test('a start from the list runs on the chosen provider and model, checked again first', async (t) => {
    const config = { model_provider: 'ollama', model: 'qwen3.6:35b-a3b-nvfp4' };
    const { runtime, received, directory, observations } = fake(
      t,
      [],
      {},
      { FAKE_CODEX_CONFIG: JSON.stringify(config) },
    );
    await runtime.startExecution({
      execution: TEST_EXECUTION,
      instruction: 'COMPLETE the work.',
      options: { context_window: 65536 },
      model_ref: 'ollama/qwen3.6:35b-a3b-nvfp4',
      directory,
      emit: (observation) => observations.push(observation),
    });
    const sent = received();
    const threadStart = sent.find((message) => message.method === 'thread/start');
    const params = (threadStart?.params ?? {}) as { model?: unknown; modelProvider?: unknown };
    assert.equal(params.model, 'qwen3.6:35b-a3b-nvfp4');
    assert.equal(params.modelProvider, 'ollama');
    // The list was read before the thread started.
    const methods = sent.map((message) => message.method);
    assert.ok(methods.indexOf('config/read') < methods.indexOf('thread/start'));
    await until(() => observations.length === 4, 'the turn');
    assert.deepEqual(observations[1]?.payload, { model_ref: 'ollama/qwen3.6:35b-a3b-nvfp4' });

    const other = { ...TEST_EXECUTION, execution_id: '01920000-0000-7000-8000-0000000000f1' };
    await assert.rejects(
      runtime.startExecution({
        execution: other as typeof TEST_EXECUTION,
        instruction: 'COMPLETE the work.',
        options: {},
        model_ref: 'ollama/removed:model',
        directory,
        emit: () => undefined,
      }),
      (error: unknown) =>
        actionError('model_unavailable')(error) &&
        (error as Error).message.startsWith('Codex does not list the model ollama/removed:model'),
    );
    assert.equal(
      received().filter((message) => message.method === 'thread/start').length,
      1,
      'a thread was started for a model no longer listed',
    );
  });

  test('a model chosen from the list replaces the model options, never goes with them', (t) => {
    const { runtime } = adapter(t);
    assert.deepEqual(runtime.validateStartOptions({}, 'ollama/qwen3.6:35b'), {
      ok: true,
    });
    for (const options of [{ model: 'gpt-5.5' }, { model_provider: 'ollama' }]) {
      assert.deepEqual(runtime.validateStartOptions(options, 'ollama/x'), {
        ok: false,
        message:
          'Choose the model either with model_ref or with the "model" and "model_provider" options, not both.',
      });
    }
    for (const modelRef of ['no-provider', 'ollama/', '/model', 'bad provider/model']) {
      assert.deepEqual(runtime.validateStartOptions({}, modelRef), {
        ok: false,
        message: `${modelRef} is not a model Codex lists.`,
      });
    }
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
    const [started, , turn] = observations;
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
    const { start, received } = fake(t, ['elicit']);
    await start();
    await until(
      () => received().some((message) => message.id === 0 && message.error !== undefined),
      'the refusal',
    );
    const refusal = received().find((message) => message.id === 0 && message.method === undefined);
    assert.deepEqual(refusal?.error, {
      code: -32601,
      message: 'Halcyonic does not handle mcpServer/elicitation/request requests.',
    });
  });

  test('a question Codex asks is shown, and the answer goes back as Codex takes it', async (t) => {
    const { runtime, start, observations, received } = fake(t, ['ask']);
    await start();
    await until(
      () => observations.some((item) => item.type === 'runtime.question.asked'),
      'the question',
    );
    const asked = observations.find((item) => item.type === 'runtime.question.asked');
    assert.ok(asked?.type === 'runtime.question.asked');
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
        free_text: true,
        secret: false,
      },
    ]);
    assert.equal(asked.payload.answerable, true);
    await runtime.answerQuestion({
      execution: TEST_EXECUTION,
      question_id: asked.payload.question_id,
      answers: [{ key: 'colour', selected: ['blue'], text: 'navy, if you can' }],
    });
    const answer = received().find((message) => message.id === 0 && message.method === undefined);
    assert.deepEqual(answer?.result, {
      answers: { colour: { answers: ['blue', 'navy, if you can'] } },
    });
    await until(
      () => observations.some((item) => item.type === 'runtime.question.resolved'),
      'the resolution',
    );
    assert.deepEqual(observations.at(-1)?.payload, {
      question_id: asked.payload.question_id,
      outcome: 'answered',
    });
    await assert.rejects(
      runtime.answerQuestion({
        execution: TEST_EXECUTION,
        question_id: asked.payload.question_id,
        answers: [{ key: 'colour', selected: ['red'], text: null }],
      }),
      actionError('question_not_pending'),
    );
    assertValidObservations(observations);
  });

  test('an answer Codex never confirms may be sent again, then which was taken is unknown', async (t) => {
    const { runtime, start, observations, received } = fake(t, ['ask', 'deaf-once'], {
      approvalTimeoutMs: 300,
    });
    await start();
    await until(
      () => observations.some((item) => item.type === 'runtime.question.asked'),
      'the question',
    );
    const asked = observations.find((item) => item.type === 'runtime.question.asked');
    assert.ok(asked?.type === 'runtime.question.asked');
    const answer = (selected: string) =>
      runtime.answerQuestion({
        execution: TEST_EXECUTION,
        question_id: asked.payload.question_id,
        answers: [{ key: 'colour', selected: [selected], text: null }],
      });
    const first = answer('red');
    await assert.rejects(answer('blue'), actionError('answer_in_flight'));
    await assert.rejects(first, actionError('question_unconfirmed', 'unknown'));
    assert.equal(
      observations.some((item) => item.type === 'runtime.question.resolved'),
      false,
    );
    // The stand-in ignored the first, but Codex might have taken it late: it confirms only the request.
    await assert.rejects(answer('blue'), actionError('answer_ambiguous', 'unknown'));
    assert.deepEqual(observations.at(-1)?.payload, {
      question_id: asked.payload.question_id,
      outcome: 'answered',
    });
    const sent = received().filter((message) => message.id === 0 && message.method === undefined);
    assert.deepEqual(
      sent.map((message) => message.result),
      [
        { answers: { colour: { answers: ['red'] } } },
        { answers: { colour: { answers: ['blue'] } } },
      ],
    );
  });

  test('an answer holding a lone surrogate is refused before it reaches Codex', async (t) => {
    const { runtime, start, observations, received } = fake(t, ['ask']);
    await start();
    await until(
      () => observations.some((item) => item.type === 'runtime.question.asked'),
      'the question',
    );
    const asked = observations.find((item) => item.type === 'runtime.question.asked');
    assert.ok(asked?.type === 'runtime.question.asked');
    await assert.rejects(
      runtime.answerQuestion({
        execution: TEST_EXECUTION,
        question_id: asked.payload.question_id,
        answers: [{ key: 'colour', selected: [], text: 'navy \udc00' }],
      }),
      actionError('invalid_answer'),
    );
    assert.equal(
      received().some((message) => message.id === 0 && message.method === undefined),
      false,
    );
    await runtime.answerQuestion({
      execution: TEST_EXECUTION,
      question_id: asked.payload.question_id,
      answers: [{ key: 'colour', selected: [], text: 'navy' }],
    });
  });

  test('a secret question is shown as unanswerable and left for the person to stop', async (t) => {
    const { runtime, start, observations, received } = fake(t, ['ask-secret']);
    await start();
    await until(
      () => observations.some((item) => item.type === 'runtime.question.asked'),
      'the question',
    );
    const asked = observations.find((item) => item.type === 'runtime.question.asked');
    assert.ok(asked?.type === 'runtime.question.asked');
    assert.equal(asked.payload.answerable, false);
    assert.equal(asked.payload.prompts[0]?.secret, true);
    // Not refused: the agent waits, and stopping the turn withdraws the question.
    assert.equal(
      received().some((message) => message.id === 0 && message.method === undefined),
      false,
    );
    await runtime.interrupt({ execution: TEST_EXECUTION });
    await until(
      () => observations.some((item) => item.type === 'runtime.turn.interrupted'),
      'the interrupt',
    );
    assert.equal(
      observations.some((item) => item.type === 'runtime.question.resolved'),
      false,
      'the turn ending settles it',
    );
  });

  test('with questions switched off, threads leave the feature off and declare no answers', async (t) => {
    const { runtime, start, received } = fake(t, [], { answerQuestions: false });
    assert.equal(runtime.descriptor.capabilities.answer_question, false);
    await start();
    const threadStart = received().find((message) => message.method === 'thread/start');
    assert.ok(threadStart !== undefined);
    assert.equal((threadStart.params as { config?: unknown }).config, undefined);
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
    await until(() => observations.length === 4, 'the turn');
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
      'config',
      'cwd',
      'excludeTurns',
      'modelProvider',
      'sandbox',
      'threadId',
    ]);
    await runtime.sendInstruction({ execution: TEST_EXECUTION, text: 'COMPLETE again.' });
    // The resumed thread runs on the same model, so none is reported again.
    await until(() => observations.length === 6, 'the second turn');
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
