/**
 * Runs the adapter through the real Agent SDK against a fake Claude Code executable
 * (testing/fake-claude.mjs) in a temporary directory. No model is contacted and no real Claude
 * Code runs. It verifies what the SDK passes to the process and that no process outlives its host.
 */
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import {
  chmodSync,
  copyFileSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, dirname, join } from 'node:path';
import { after, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import type { ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import type { ExecutionContext, RuntimeObservation } from '@halcyonic/runtime-core';
import { ClaudeAgentRuntimeAdapter } from './claude-agent-runtime.ts';

// The SDK writes host-side debug logs under the Claude configuration directory when a debug
// variable is set. Nothing this file runs may write to the developer's configuration.
for (const name of ['DEBUG', 'DEBUG_SDK', 'DEBUG_CLAUDE_AGENT_SDK', 'CLAUDE_CODE_DEBUG_LOGS_DIR']) {
  delete process.env[name];
}

const ROOT = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-claude-process-')));
process.env.CLAUDE_CONFIG_DIR = join(ROOT, 'host-config');
after(() => rmSync(ROOT, { recursive: true, force: true }));

const EXECUTABLE = join(ROOT, 'claude');
copyFileSync(fileURLToPath(new URL('./testing/fake-claude.mjs', import.meta.url)), EXECUTABLE);
chmodSync(EXECUTABLE, 0o755);
const HOST = fileURLToPath(new URL('./testing/exiting-host.mjs', import.meta.url));
const WORKDIR = join(ROOT, 'work');
const HOME = join(ROOT, 'home');
mkdirSync(WORKDIR);
mkdirSync(HOME);

const execution: ExecutionContext = {
  execution_id: '01920000-0000-7000-8000-000000000203' as ExecutionId,
  workstream_id: '01920000-0000-7000-8000-000000000202' as WorkstreamId,
  project_id: '01920000-0000-7000-8000-000000000201' as ProjectId,
};

interface Recorded {
  readonly pid: number;
  readonly args: string[];
  readonly cwd: string;
  readonly env_names: string[];
  readonly home: string | null;
  readonly config_dir: string | null;
  readonly path: string | null;
  readonly received: Array<Record<string, unknown>>;
  readonly input_closed: boolean;
  readonly signal: string | null;
}

function readRecord(path: string): Recorded {
  return JSON.parse(readFileSync(path, 'utf8')) as Recorded;
}

function isAlive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === 'EPERM';
  }
}

async function waitFor(condition: () => boolean, timeoutMs: number, what: string): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!condition()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
}

function adapterFor(record: string, extra: Record<string, string> = {}) {
  return new ClaudeAgentRuntimeAdapter({
    inheritedEnvironment: {
      PATH: '/usr/bin:/bin',
      HOME,
      LANG: 'C',
      ANTHROPIC_API_KEY: 'test-key-not-real',
      SALIDIUM_INTERNAL: '1',
      UNRELATED_SECRET: 'not for agents',
    },
    environment: { FAKE_CLAUDE_RECORD: record, ...extra },
    pathToClaudeCodeExecutable: EXECUTABLE,
  });
}

describe('the adapter through the real Agent SDK', () => {
  test('the process gets the chosen session id, the built environment and every settings source', async () => {
    const record = join(ROOT, 'launch.json');
    const adapter = adapterFor(record);
    const observed: RuntimeObservation[] = [];
    try {
      const { native_id } = await adapter.startExecution({
        execution,
        instruction: 'Say hello.',
        options: { cwd: WORKDIR },
        emit: (observation) => observed.push(observation),
      });
      await waitFor(
        () => observed.some((observation) => observation.type === 'runtime.turn.completed'),
        5000,
        'the turn to complete',
      );
      const recorded = readRecord(record);

      assert.ok(recorded.args.includes(`--session-id=${native_id}`), recorded.args.join(' '));
      for (const [flag, value] of [
        ['--input-format', 'stream-json'],
        ['--output-format', 'stream-json'],
        ['--permission-prompt-tool', 'stdio'],
        ['--permission-mode', 'default'],
      ] as const) {
        assert.equal(recorded.args[recorded.args.indexOf(flag) + 1], value, flag);
      }
      assert.ok(recorded.args.includes('--verbose'));
      assert.ok(!recorded.args.some((arg) => arg.startsWith('--setting-sources')));
      assert.equal(recorded.cwd, WORKDIR);

      // macOS CoreFoundation sets __CF_USER_TEXT_ENCODING inside every process it loads into; it
      // is not passed by the SDK or the adapter.
      const passed = recorded.env_names.filter((name) => name !== '__CF_USER_TEXT_ENCODING');
      assert.deepEqual(passed, [
        'ANTHROPIC_API_KEY',
        'CLAUDE_AGENT_SDK_VERSION',
        'CLAUDE_CODE_ENTRYPOINT',
        'FAKE_CLAUDE_RECORD',
        'HOME',
        'LANG',
        'PATH',
      ]);
      assert.equal(recorded.home, HOME);
      assert.ok((recorded.path ?? '').split(delimiter).includes(dirname(process.execPath)));

      const [initialize, prompt] = recorded.received;
      assert.equal(initialize?.type, 'control_request');
      const request = initialize?.request as Record<string, unknown> | undefined;
      assert.equal(request?.subtype, 'initialize');
      // The preset leaves the system prompt to Claude Code's own default.
      assert.equal(Object.hasOwn(request ?? {}, 'systemPrompt'), false);
      assert.equal(prompt?.type, 'user');
      assert.deepEqual(prompt?.message, {
        role: 'user',
        content: [{ type: 'text', text: 'Say hello.' }],
      });

      assert.deepEqual(
        observed.map((observation) => observation.type),
        ['runtime.execution.started', 'runtime.turn.started', 'runtime.turn.completed'],
      );
      assert.deepEqual(observed[0]?.payload, { native_id });
      assert.deepEqual(observed[1]?.payload, { turn_id: prompt?.uuid });
    } finally {
      await adapter.close();
    }
  });

  test('close stops a Claude Code process that keeps running after its input ends', async () => {
    const record = join(ROOT, 'close.json');
    const adapter = adapterFor(record, { FAKE_CLAUDE_IGNORE_EOF: '1' });
    await adapter.startExecution({
      execution,
      instruction: 'Keep working.',
      options: { cwd: WORKDIR },
      emit: () => {},
    });
    const { pid } = readRecord(record);
    assert.ok(isAlive(pid));
    await adapter.close();
    await waitFor(() => !isAlive(pid), 10_000, 'the process to stop');
    const recorded = readRecord(record);
    assert.equal(recorded.input_closed, true);
    assert.equal(recorded.signal, 'SIGTERM');
  });

  for (const mode of ['exit', 'throw'] as const) {
    test(`a host that ends by ${mode === 'exit' ? 'exiting' : 'crashing'} without closing still stops its Claude Code process`, async () => {
      const record = join(ROOT, `host-${mode}.json`);
      const host = spawn(process.execPath, [HOST, EXECUTABLE, WORKDIR, HOME, record, mode], {
        env: { PATH: process.env.PATH ?? '', HOME, CLAUDE_CONFIG_DIR: join(ROOT, 'host-config') },
        stdio: 'ignore',
      });
      const code = await new Promise<number | null>((resolve) => host.once('exit', resolve));
      assert.equal(code, mode === 'exit' ? 0 : 1);
      const { pid } = readRecord(record);
      await waitFor(() => !isAlive(pid), 5000, 'the orphaned process to stop');
      assert.equal(readRecord(record).signal, 'SIGTERM');
    });
  }

  test('a host that closes its adapter on SIGTERM, as the control plane does, outlives its Claude Code process', async () => {
    const record = join(ROOT, 'host-signal.json');
    const host = spawn(process.execPath, [HOST, EXECUTABLE, WORKDIR, HOME, record, 'signal'], {
      env: { PATH: process.env.PATH ?? '', HOME, CLAUDE_CONFIG_DIR: join(ROOT, 'host-config') },
      stdio: ['ignore', 'pipe', 'ignore'],
    });
    const exited = new Promise<number | null>((resolve) => host.once('exit', resolve));
    await new Promise<void>((resolve) => host.stdout.once('data', () => resolve()));
    const { pid } = readRecord(record);
    host.kill('SIGTERM');
    assert.equal(await exited, 0);
    // The child keeps the host's event loop alive until the SDK has stopped it.
    assert.equal(isAlive(pid), false);
    const recorded = readRecord(record);
    assert.equal(recorded.input_closed, true);
    assert.equal(recorded.signal, 'SIGTERM');
  });
});
