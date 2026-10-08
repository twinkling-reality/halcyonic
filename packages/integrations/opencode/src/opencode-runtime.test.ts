import assert from 'node:assert/strict';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, dirname, join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { compileValidator, RuntimeDescriptor } from '@halcyonic/contracts';
import {
  capabilityProblems,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import type { OpenCodeClient } from './client.ts';
import { createSessionState, shellCallKey } from './events.ts';
import {
  OPENCODE_CAPABILITIES,
  OpenCodeRuntimeAdapter,
  readSnapshot,
  sessionPermissions,
} from './opencode-runtime.ts';
import { buildEnvironment, INHERITED_VARIABLES, launchServer } from './server.ts';
import { allowOnly } from './testing/directory-policy.ts';
import { TEST_EXECUTION } from './testing/observations.ts';

function temporary(t: TestContext): string {
  // A real path, as the host binds a project's folder; macOS's temporary directory is a link.
  const directory = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-opencode-unit-')));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  return directory;
}

/** An adapter whose binary cannot start, whose policy allows only its temporary directory. */
function adapter(t: TestContext) {
  const directory = temporary(t);
  const runtime = new OpenCodeRuntimeAdapter({
    binaryPath: '/nonexistent/opencode',
    serverRecordFile: join(directory, 'server.json'),
    directoryPolicy: allowOnly(directory),
  });
  t.after(() => runtime.close());
  return { runtime, directory };
}

function actionError(code: string, effect: 'none' | 'unknown' = 'none') {
  return (error: unknown) =>
    error instanceof RuntimeActionError && error.code === code && error.effect === effect;
}

describe('OpenCode runtime descriptor', () => {
  test('is contract-valid and declares exactly the capabilities it implements', (t) => {
    const { runtime } = adapter(t);
    assert.ok(compileValidator(RuntimeDescriptor)(runtime.descriptor).ok);
    assert.deepEqual(capabilityProblems(runtime), []);
    assert.equal(runtime.descriptor.kind, 'opencode');
    assert.equal(runtime.descriptor.synthetic, false);
    assert.equal(runtime.descriptor.model_choice, 'listed');
    assert.equal(typeof runtime.listModels, 'function');
    assert.deepEqual(runtime.descriptor.capabilities, OPENCODE_CAPABILITIES);
    assert.deepEqual(OPENCODE_CAPABILITIES, {
      start_execution: true,
      instruct_at_rest: true,
      instruct_while_running: true,
      respond_to_approval: true,
      answer_question: true,
      interrupt: true,
    });
  });
});

describe('OpenCode start options', () => {
  test('allow a provider/model, and no folder: the session works in the project folder', (t) => {
    const { runtime, directory } = adapter(t);
    const invalid: Record<string, unknown>[] = [
      { directory },
      { cwd: directory },
      { scenario: 'x' },
      { model: 'no-slash' },
      { model: '/model' },
      { model: 'provider/' },
      { model: 42 },
    ];
    for (const options of invalid) {
      const result = runtime.validateStartOptions(options, null);
      assert.equal(result.ok, false, JSON.stringify(options));
      assert.ok(!result.ok && result.message.length > 0);
    }
    assert.match(
      JSON.stringify(runtime.validateStartOptions({ directory }, null)),
      /project's folder/,
    );
    for (const options of [{}, { model: null }, { model: 'openrouter/vendor/model-1' }]) {
      assert.deepEqual(
        runtime.validateStartOptions(options, null),
        { ok: true },
        JSON.stringify(options),
      );
    }
  });

  test('a model chosen from the list is taken instead of the model option, never with it', (t) => {
    const { runtime } = adapter(t);
    assert.deepEqual(runtime.validateStartOptions({}, 'ollama/qwen3.6:35b-a3b-nvfp4'), {
      ok: true,
    });
    assert.deepEqual(runtime.validateStartOptions({ model: null }, 'ollama/x:y'), { ok: true });
    assert.deepEqual(runtime.validateStartOptions({ model: 'fake/fake-model' }, 'ollama/x:y'), {
      ok: false,
      message: 'Choose the model either with model_ref or with the "model" option, not both.',
    });
    for (const modelRef of ['no-slash', '/model', 'provider/', 'provider/a b']) {
      assert.deepEqual(runtime.validateStartOptions({}, modelRef), {
        ok: false,
        message: `${modelRef} is not a model OpenCode lists.`,
      });
    }
  });
});

describe('reading a session back after the event stream reconnected', () => {
  test("a pending shell request is matched to its call by message and call id, and shows that call's request", async () => {
    const state = createSessionState();
    state.shellCalls.set(
      shellCallKey('msg_gone', 'call_9') ?? '',
      "[in the task's folder] from the stream",
    );
    const data: Record<string, unknown> = {
      '/api/session/s/permission': [
        ['per_1', 'msg_new', 'call_1'],
        ['per_2', 'msg_old', 'call_1'],
        ['per_3', 'msg_new', 'call_2'],
        ['per_4', 'msg_gone', 'call_9'],
      ].map(([id, messageID, call]) => ({
        id,
        action: 'shell',
        resources: ['parsed'],
        source: { type: 'tool', messageID, id: call },
      })),
      '/api/session/s/form': [],
      '/api/session/active': {},
      '/api/session/s': {},
      // Newest first, as OpenCode lists them.
      '/api/session/s/message': [
        {
          id: 'msg_new',
          type: 'assistant',
          content: [
            {
              type: 'tool',
              id: 'call_1',
              name: 'shell',
              state: { status: 'running', input: { command: 'echo one > f', workdir: 'sub' } },
            },
            {
              type: 'tool',
              id: 'call_2',
              name: 'shell',
              state: { status: 'running', input: { command: 7 } },
            },
          ],
        },
        {
          id: 'msg_old',
          type: 'assistant',
          content: [
            {
              type: 'tool',
              id: 'call_1',
              name: 'shell',
              state: { status: 'completed', input: { command: 'echo old' } },
            },
          ],
        },
      ],
    };
    const client = {
      request: async (_method: string, path: string) =>
        path in data ? { status: 200, body: { data: data[path] } } : { status: 404, body: null },
    } as unknown as Pick<OpenCodeClient, 'request'>;
    const snapshot = await readSnapshot(client, { sessionId: 's', state });
    assert.deepEqual(
      snapshot.permissions.map((permission) => [permission.id, permission.command]),
      [
        ['per_1', '[in "sub"] echo one > f'],
        ['per_2', "[in the task's folder] echo old"],
        ['per_3', null],
        ['per_4', "[in the task's folder] from the stream"],
      ],
    );
  });
});

describe('OpenCode session rules', () => {
  test('deny the network tools and subagents, ask for shell commands, and deny edits to every hidden path and to opencode.json', () => {
    const rules = sessionPermissions({ HOME: '/Users/someone', PATH: '/usr/bin' });
    const denied = (action: string) =>
      rules
        .filter((rule) => rule.action === action && rule.effect === 'deny')
        .map((rule) => rule.resource);
    for (const tool of ['execute', 'webfetch', 'websearch', 'subagent']) {
      assert.deepEqual(denied(tool), ['*'], tool);
    }
    const edits = denied('edit');
    assert.deepEqual(edits.slice(0, 2), ['.*', '*/.*']);
    assert.equal(edits.at(-1), '/Users/someone/.config/opencode/*');
    // opencode.json is not hidden: every spelling by case, with any extension of four letters or more.
    const named = edits.slice(2, -1);
    assert.equal(named.length, 512);
    for (const spelling of ['opencode', 'OpenCode', 'OPENCODE', 'oPeNcOdE']) {
      assert.ok(
        named.includes(`${spelling}.????*`) && named.includes(`*/${spelling}.????*`),
        spelling,
      );
    }
    // Nothing else is asked or allowed: edits keep OpenCode's own rules, so they need no press.
    assert.deepEqual(
      rules.filter((rule) => rule.effect !== 'deny'),
      [{ action: 'shell', resource: '*', effect: 'ask' }],
    );
    // OpenCode's global folder follows XDG_CONFIG_HOME, then OPENCODE_CONFIG_DIR; a file
    // OPENCODE_CONFIG names is denied too.
    assert.ok(denied.call(null, 'edit').includes('/Users/someone/.config/opencode/*'));
    const xdg = sessionPermissions({ HOME: '/h', XDG_CONFIG_HOME: '/x' });
    assert.ok(xdg.some((rule) => rule.action === 'edit' && rule.resource === '/x/opencode/*'));
    const own = sessionPermissions({
      HOME: '/h',
      OPENCODE_CONFIG_DIR: '/d',
      OPENCODE_CONFIG: '/f.json',
    });
    assert.ok(own.some((rule) => rule.action === 'edit' && rule.resource === '/d/*'));
    assert.ok(own.some((rule) => rule.action === 'edit' && rule.resource === '/f.json'));
  });
});

describe('OpenCode start failures', () => {
  test("never carry the server's error output, which may hold a key", async (t) => {
    const directory = temporary(t);
    const secret = 'sk-FAKE-not-real-0123456789';
    // A stand-in that, like a configuration error, prints a provider's key and exits.
    const binary = join(directory, 'opencode');
    writeFileSync(
      binary,
      '#!/usr/bin/env node\nprocess.stderr.write(\'{"apiKey":"\' + process.env.FAKE_SECRET + \'"}\\n\');\nprocess.exit(1);\n',
      { mode: 0o755 },
    );
    const runtime = new OpenCodeRuntimeAdapter({
      binaryPath: binary,
      serverRecordFile: join(directory, 'server.json'),
      directoryPolicy: allowOnly(directory),
      env: {
        PATH: [dirname(process.execPath), process.env.PATH ?? ''].join(delimiter),
        FAKE_SECRET: secret,
      },
    });
    t.after(() => runtime.close());
    await assert.rejects(
      runtime.startExecution({
        execution: TEST_EXECUTION,
        instruction: 'Do the work.',
        options: {},
        model_ref: null,
        directory,
        emit: () => undefined,
      }),
      (error: unknown) => {
        const message = (error as Error).message;
        assert.ok(actionError('runtime_unavailable')(error), message);
        assert.match(message, /exit code 1/);
        assert.match(message, /not reported, since it may hold secrets/);
        assert.ok(!message.includes('sk-FAKE'), message);
        return true;
      },
    );
  });

  test('a stand-in slower to fail than the confirmation by ps still reports its exit, never its output', async (t) => {
    const directory = temporary(t);
    const secret = 'sk-FAKE-not-real-0123456789';
    // As a loaded Mac makes any start slow: the stand-in fails only after 6 s, longer than ps
    // could be asked a hundred times.
    const binary = join(directory, 'opencode');
    writeFileSync(
      binary,
      '#!/usr/bin/env node\nAtomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 6000);\nprocess.stderr.write(\'{"apiKey":"\' + process.env.FAKE_SECRET + \'"}\\n\');\nprocess.exit(1);\n',
      { mode: 0o755 },
    );
    const runtime = new OpenCodeRuntimeAdapter({
      binaryPath: binary,
      serverRecordFile: join(directory, 'server.json'),
      directoryPolicy: allowOnly(directory),
      env: {
        PATH: [dirname(process.execPath), process.env.PATH ?? ''].join(delimiter),
        FAKE_SECRET: secret,
      },
    });
    t.after(() => runtime.close());
    await assert.rejects(
      runtime.startExecution({
        execution: TEST_EXECUTION,
        instruction: 'Do the work.',
        options: {},
        model_ref: null,
        directory,
        emit: () => undefined,
      }),
      (error: unknown) => {
        const message = (error as Error).message;
        assert.ok(actionError('runtime_unavailable')(error), message);
        assert.match(message, /exited during startup \(exit code 1\)/);
        assert.ok(!message.includes('sk-FAKE'), message);
        return true;
      },
    );
  });
});

describe('OpenCode startup time', () => {
  test('one startup time covers confirming the process and waiting for it to answer', async (t) => {
    const directory = temporary(t);
    // A stand-in that ps can confirm only after 2 s, when a shell execs node under the binary's
    // own path; node then runs `serve` from the working folder, which never answers.
    writeFileSync(join(directory, 'serve'), 'setInterval(() => {}, 1000);\n');
    const binary = join(directory, 'opencode');
    writeFileSync(
      binary,
      `#!/bin/bash\nsleep 2\nexec -a "$0" ${JSON.stringify(process.execPath)} "$@"\n`,
      { mode: 0o755 },
    );
    const startupTimeoutMs = 4000;
    const started = Date.now();
    await assert.rejects(
      launchServer({
        binaryPath: binary,
        environment: { PATH: '/usr/bin:/bin' },
        recordFile: join(directory, 'server.json'),
        port: null,
        cwd: directory,
        startupTimeoutMs,
      }),
      /did not become ready within 4000 ms/,
    );
    const elapsed = Date.now() - started;
    // Confirming took about 2 s of the 4: the wait for an answer got only what was left, not
    // another 4 s of its own. A second's margin for stopping the stand-in.
    assert.ok(elapsed >= 2000, `refused after ${elapsed} ms, before the stand-in was confirmed`);
    assert.ok(elapsed < startupTimeoutMs + 1000, `refused after ${elapsed} ms`);
  });
});

describe("OpenCode and the project's folder", () => {
  const start = (runtime: OpenCodeRuntimeAdapter, directory: string | null) =>
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
    const runtime = new OpenCodeRuntimeAdapter({
      binaryPath: '/nonexistent/opencode',
      serverRecordFile: join(directory, 'server.json'),
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

describe('OpenCode server environment', () => {
  test('inherits only the allowlist, then applies additions', () => {
    const environment = buildEnvironment(
      {
        PATH: '/usr/bin',
        HOME: '/home/user',
        XDG_CONFIG_HOME: '/home/user/.config',
        SALIDIUM_INTERNAL: '1',
        OPENCODE_CONFIG: '/elsewhere.json',
        ANTHROPIC_API_KEY: 'not inherited',
        NODE_OPTIONS: '--inspect',
      },
      { HOME: '/tmp/home', OPENAI_API_KEY: 'configured' },
    );
    assert.deepEqual(environment, {
      PATH: '/usr/bin',
      HOME: '/tmp/home',
      XDG_CONFIG_HOME: '/home/user/.config',
      OPENAI_API_KEY: 'configured',
      OPENCODE_DISABLE_AUTOUPDATE: 'true',
      OPENCODE_DISABLE_MODELS_FETCH: 'true',
    });
    assert.ok(INHERITED_VARIABLES.includes('XDG_DATA_HOME'));
    assert.ok(!INHERITED_VARIABLES.includes('SALIDIUM_INTERNAL'));
  });

  test('refuses variables the adapter owns, including SALIDIUM_INTERNAL', (t) => {
    for (const name of [
      'SALIDIUM_INTERNAL',
      'OPENCODE_PASSWORD',
      'OPENCODE_SERVER_PASSWORD',
      'OPENCODE_DISABLE_AUTOUPDATE',
      'OPENCODE_DISABLE_MODELS_FETCH',
    ]) {
      assert.throws(() => buildEnvironment({}, { [name]: 'x' }), new RegExp(name));
      assert.throws(
        () =>
          new OpenCodeRuntimeAdapter({
            binaryPath: '/nonexistent/opencode',
            serverRecordFile: join(temporary(t), 'server.json'),
            directoryPolicy: allowOnly(tmpdir()),
            env: { [name]: 'x' },
          }),
        new RegExp(name),
      );
    }
  });
});

describe('OpenCode runtime without a server', () => {
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

  test('invalid options are refused before anything is launched', async (t) => {
    const { runtime, directory } = adapter(t);
    await assert.rejects(
      runtime.startExecution({
        execution: TEST_EXECUTION,
        instruction: 'Do the work.',
        options: { directory },
        model_ref: null,
        directory,
        emit: () => undefined,
      }),
      actionError('invalid_runtime_options'),
    );
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
      runtime.respondToApproval({
        execution,
        approval_id: 'per_1',
        decision: 'deny',
        message: null,
      }),
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
