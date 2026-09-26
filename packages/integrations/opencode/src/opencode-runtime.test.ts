import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { compileValidator, RuntimeDescriptor } from '@halcyonic/contracts';
import {
  capabilityProblems,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import { OPENCODE_CAPABILITIES, OpenCodeRuntimeAdapter } from './opencode-runtime.ts';
import { buildEnvironment, INHERITED_VARIABLES } from './server.ts';
import { TEST_EXECUTION } from './testing/observations.ts';

function temporary(t: TestContext): string {
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-opencode-unit-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  return directory;
}

function adapter(t: TestContext, binaryPath = '/nonexistent/opencode') {
  const directory = temporary(t);
  const runtime = new OpenCodeRuntimeAdapter({
    binaryPath,
    serverRecordFile: join(directory, 'server.json'),
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
    assert.deepEqual(runtime.descriptor.capabilities, OPENCODE_CAPABILITIES);
    assert.deepEqual(OPENCODE_CAPABILITIES, {
      start_execution: true,
      instruct_at_rest: true,
      instruct_while_running: false,
      respond_to_approval: true,
      interrupt: true,
    });
  });
});

describe('OpenCode start options', () => {
  test('require an absolute path of an existing directory, and allow a provider/model', (t) => {
    const { runtime, directory } = adapter(t);
    const file = join(directory, 'a-file');
    writeFileSync(file, 'x');
    const invalid: Record<string, unknown>[] = [
      {},
      { directory: '' },
      { directory: 'relative/path' },
      { directory: join(directory, 'missing') },
      { directory: file },
      { directory, scenario: 'x' },
      { directory, model: 'no-slash' },
      { directory, model: '/model' },
      { directory, model: 'provider/' },
      { directory, model: 42 },
    ];
    for (const options of invalid) {
      const result = runtime.validateStartOptions(options);
      assert.equal(result.ok, false, JSON.stringify(options));
      assert.ok(!result.ok && result.message.length > 0);
    }
    for (const options of [
      { directory },
      { directory, model: null },
      { directory, model: 'openrouter/vendor/model-1' },
    ]) {
      assert.deepEqual(
        runtime.validateStartOptions(options),
        { ok: true },
        JSON.stringify(options),
      );
    }
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
    ]) {
      assert.throws(() => buildEnvironment({}, { [name]: 'x' }), new RegExp(name));
      assert.throws(
        () =>
          new OpenCodeRuntimeAdapter({
            binaryPath: '/nonexistent/opencode',
            serverRecordFile: join(temporary(t), 'server.json'),
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
        options: { directory },
        emit: (observation) => observed.push(observation),
      }),
      actionError('runtime_unavailable'),
    );
    assert.deepEqual(observed, []);
    assert.equal(runtime.serverPid, null);
    assert.equal(existsSync(join(directory, 'server.json')), false);
  });

  test('invalid options are refused before anything is launched', async (t) => {
    const { runtime } = adapter(t);
    await assert.rejects(
      runtime.startExecution({
        execution: TEST_EXECUTION,
        instruction: 'Do the work.',
        options: { directory: '/definitely/not/here' },
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
        options: { directory },
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
