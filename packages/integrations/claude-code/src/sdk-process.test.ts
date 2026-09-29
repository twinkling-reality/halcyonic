/**
 * Runs the adapter through the real Agent SDK against a fake Claude Code executable
 * (testing/fake-claude.mjs) in a temporary directory. No model is contacted and no real Claude
 * Code runs. It verifies how the adapter launches the process, and that no process outlives its
 * host, however the host ends. Each test keeps its files in a directory of its own.
 */
import assert from 'node:assert/strict';
import { type ChildProcessByStdio, execFileSync, spawn } from 'node:child_process';
import {
  chmodSync,
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  statSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, dirname, join } from 'node:path';
import type { Readable, Writable } from 'node:stream';
import { after, describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { query } from '@anthropic-ai/claude-agent-sdk';
import type { ExecutionId, ProjectId, WorkstreamId } from '@halcyonic/contracts';
import {
  type ExecutionContext,
  RuntimeActionError,
  type RuntimeObservation,
} from '@halcyonic/runtime-core';
import { ClaudeAgentRuntimeAdapter, type QueryFunction } from './claude-agent-runtime.ts';
import { readProcessIdentity, readProcessRecords, writeProcessRecords } from './process-record.ts';

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
  readonly stdio: string[];
  /** Whether the process record already listed the process when its first input arrived. */
  readonly listed_before_input: boolean | null;
  readonly received: Array<Record<string, unknown>>;
  readonly input_closed: boolean;
  readonly signal: string | null;
}

/** A directory of its own for one test: the fake's records and the adapter's process record. */
function scratch(name: string): string {
  const directory = mkdtempSync(join(ROOT, `${name}-`));
  mkdirSync(join(directory, 'fake'));
  return directory;
}

function recordFile(directory: string): string {
  return join(directory, 'claude-agent-processes.json');
}

/** What every fake Claude Code process launched for this directory recorded, by pid. */
function fakeRecords(directory: string): Recorded[] {
  return readdirSync(join(directory, 'fake'))
    .filter((name) => /^\d+\.json$/.test(name))
    .map((name) => JSON.parse(readFileSync(join(directory, 'fake', name), 'utf8')) as Recorded)
    .sort((a, b) => a.pid - b.pid);
}

function onlyRecord(directory: string): Recorded {
  const records = fakeRecords(directory);
  assert.equal(records.length, 1);
  return records[0] as Recorded;
}

function isAlive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === 'EPERM';
  }
}

function kill(pid: number): void {
  try {
    process.kill(pid, 'SIGKILL');
  } catch {
    // Already gone.
  }
}

async function waitFor(condition: () => boolean, timeoutMs: number, what: string): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!condition()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await delay(25);
  }
}

function processGroup(pid: number): string {
  return execFileSync('ps', ['-p', String(pid), '-o', 'pgid='], { encoding: 'utf8' }).trim();
}

function adapterFor(
  directory: string,
  extra: Record<string, string> = {},
  queryFunction?: QueryFunction,
) {
  return new ClaudeAgentRuntimeAdapter({
    directoryPolicy: (path: string) => ({ ok: true, directory: path }),
    processRecordFile: recordFile(directory),
    inheritedEnvironment: {
      PATH: '/usr/bin:/bin',
      HOME,
      LANG: 'C',
      ANTHROPIC_API_KEY: 'test-key-not-real',
      SALIDIUM_INTERNAL: '1',
      UNRELATED_SECRET: 'not for agents',
    },
    environment: { ...fakeEnvironment(directory), ...extra },
    pathToClaudeCodeExecutable: EXECUTABLE,
    ...(queryFunction !== undefined && { query: queryFunction }),
  });
}

/** Where the fake Claude Code keeps its records, and the process record it checks. */
function fakeEnvironment(directory: string): Record<string, string> {
  return {
    FAKE_CLAUDE_RECORD_DIR: join(directory, 'fake'),
    FAKE_CLAUDE_PROCESS_RECORD: recordFile(directory),
  };
}

/** Starts an execution and waits until the fake Claude Code has completed its turn. */
async function runTurn(
  adapter: ClaudeAgentRuntimeAdapter,
): Promise<{ nativeId: string | null; observed: RuntimeObservation[] }> {
  const observed: RuntimeObservation[] = [];
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
  return { nativeId: native_id, observed };
}

interface Host {
  readonly child: ChildProcessByStdio<Writable, Readable, null>;
  readonly exited: Promise<number | null>;
  readonly watchdog: number;
}

/**
 * Runs testing/exiting-host.mjs: a process that starts `count` executions whose fake Claude Code
 * ignores the end of its input, like a CLI with a turn in flight. Resolves once they have started.
 * Only this process holds the host's stdin, so the host exits when this process dies, however it
 * dies.
 */
async function startHost(
  t: TestContext,
  directory: string,
  mode: 'exit' | 'throw' | 'signal' | 'wait',
  count = 1,
): Promise<Host> {
  const options = {
    executable: EXECUTABLE,
    cwd: WORKDIR,
    home: HOME,
    processRecordFile: recordFile(directory),
    environment: { ...fakeEnvironment(directory), FAKE_CLAUDE_IGNORE_EOF: '1' },
    mode,
    count,
  };
  const child = spawn(process.execPath, [HOST, JSON.stringify(options)], {
    env: { PATH: process.env.PATH ?? '', HOME, CLAUDE_CONFIG_DIR: join(ROOT, 'host-config') },
    stdio: ['pipe', 'pipe', 'ignore'],
  });
  const exited = new Promise<number | null>((resolve) => child.once('exit', resolve));
  // Nothing may outlive a test, even a failed one.
  t.after(() => {
    child.kill('SIGKILL');
    for (const record of fakeRecords(directory)) kill(record.pid);
  });
  const line = await new Promise<string>((resolve, reject) => {
    let text = '';
    child.stdout.setEncoding('utf8');
    child.stdout.on('data', (chunk: string) => {
      text += chunk;
      if (text.includes('\n')) resolve(text.slice(0, text.indexOf('\n')));
    });
    child.stdout.once('end', () =>
      reject(new Error(`the host ended before it was ready: ${text}`)),
    );
  });
  const { watchdog } = JSON.parse(line) as { watchdog: number | null };
  assert.ok(watchdog !== null, 'the host runs no watchdog');
  t.after(() => kill(watchdog));
  return { child, exited, watchdog };
}

describe('the adapter through the real Agent SDK', () => {
  test('the process gets the chosen session id, the built environment and every settings source', async () => {
    const directory = scratch('launch');
    const adapter = adapterFor(directory);
    try {
      const { nativeId, observed } = await runTurn(adapter);
      const recorded = onlyRecord(directory);

      assert.ok(recorded.args.includes(`--session-id=${nativeId}`), recorded.args.join(' '));
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
        'FAKE_CLAUDE_PROCESS_RECORD',
        'FAKE_CLAUDE_RECORD_DIR',
        'HOME',
        'LANG',
        'PATH',
      ]);
      assert.equal(recorded.home, HOME);
      assert.ok((recorded.path ?? '').split(delimiter).includes(dirname(process.execPath)));
      // No input reached the process before it was in the process record.
      assert.equal(recorded.listed_before_input, true);

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
      assert.deepEqual(observed[0]?.payload, { native_id: nativeId });
      assert.deepEqual(observed[1]?.payload, { turn_id: prompt?.uuid });
    } finally {
      await adapter.close();
    }
  });

  test('Claude Code is launched exactly as the SDK launches it itself', async () => {
    const guarded = scratch('guarded');
    const direct = scratch('direct');
    const viaGuard = adapterFor(guarded);
    // The same adapter, except that the SDK launches Claude Code with its own spawn.
    const viaSdk = adapterFor(direct, {}, (params) => {
      const { spawnClaudeCodeProcess, ...options } = params.options;
      assert.equal(typeof spawnClaudeCodeProcess, 'function');
      return query({ prompt: params.prompt, options });
    });
    try {
      await runTurn(viaGuard);
      await runTurn(viaSdk);
      const [ours, sdk] = [onlyRecord(guarded), onlyRecord(direct)];
      const withoutSessionId = (args: string[]) =>
        args.map((arg) => (arg.startsWith('--session-id=') ? '--session-id=<id>' : arg));
      assert.deepEqual(withoutSessionId(ours.args), withoutSessionId(sdk.args));
      assert.deepEqual(ours.env_names, sdk.env_names);
      assert.deepEqual(
        [ours.home, ours.config_dir, ours.path, ours.cwd],
        [sdk.home, sdk.config_dir, sdk.path, sdk.cwd],
      );
      assert.deepEqual(ours.stdio, sdk.stdio);
      assert.deepEqual(ours.received.length, sdk.received.length);
      // Both run in the process group of the process that launched them.
      assert.equal(processGroup(ours.pid), processGroup(process.pid));
      assert.equal(processGroup(sdk.pid), processGroup(process.pid));
      // The one difference: input reaches the adapter's process only once it is recorded.
      assert.equal(ours.listed_before_input, true);
      assert.equal(sdk.listed_before_input, false);
    } finally {
      await Promise.all([viaGuard.close(), viaSdk.close()]);
    }
  });

  test('close stops a Claude Code process that keeps running after its input ends, removes the record and stops the watchdog', async () => {
    const directory = scratch('close');
    const adapter = adapterFor(directory, { FAKE_CLAUDE_IGNORE_EOF: '1' });
    let closed = false;
    try {
      const { nativeId } = await runTurn(adapter);
      const { pid } = onlyRecord(directory);
      assert.ok(isAlive(pid));
      const file = recordFile(directory);
      const records = await readProcessRecords(file);
      assert.deepEqual(
        records.map((record) => [record.pid, record.sessionId]),
        [[pid, nativeId]],
      );
      assert.equal(statSync(file).mode & 0o777, 0o600);
      const text = readFileSync(file, 'utf8');
      assert.ok(!text.includes('test-key-not-real') && !text.includes('not for agents'));
      const watchdog = adapter.watchdogPid;
      assert.ok(watchdog !== null && isAlive(watchdog));

      await adapter.close();
      closed = true;
      assert.equal(isAlive(pid), false);
      const recorded = onlyRecord(directory);
      assert.equal(recorded.input_closed, true);
      assert.equal(recorded.signal, 'SIGTERM');
      assert.equal(existsSync(file), false);
      assert.equal(isAlive(watchdog), false);
    } finally {
      if (!closed) await adapter.close();
    }
  });

  test('a Claude Code process that cannot be recorded is killed before any input and fails the start', async (t) => {
    const directory = scratch('unrecordable');
    // The process record cannot be written: its directory is read-only.
    chmodSync(directory, 0o500);
    t.after(() => chmodSync(directory, 0o700));
    let sessionId: string | undefined;
    const adapter = adapterFor(directory, {}, (params) => {
      sessionId = params.options.sessionId;
      return query(params);
    });
    try {
      await assert.rejects(
        runTurn(adapter),
        (error: unknown) =>
          error instanceof RuntimeActionError &&
          error.code === 'runtime_exited' &&
          error.message.includes('could not be recorded and watched'),
      );
      assert.equal(adapter.watchdogPid, null);
    } finally {
      await adapter.close();
    }
    assert.ok(sessionId !== undefined);
    const running = execFileSync('ps', ['-A', '-ww', '-o', 'args='], { encoding: 'utf8' });
    assert.ok(!running.includes(`--session-id=${sessionId}`), 'the process is still running');
    // Killed before it could start, or started and never given any input.
    for (const recorded of fakeRecords(directory)) assert.deepEqual(recorded.received, []);
  });

  for (const mode of ['exit', 'throw'] as const) {
    test(`a host that ends by ${mode === 'exit' ? 'exiting' : 'crashing'} without closing still stops its Claude Code process`, async (t) => {
      const directory = scratch(`host-${mode}`);
      const host = await startHost(t, directory, mode);
      assert.equal(await host.exited, mode === 'exit' ? 0 : 1);
      const { pid } = onlyRecord(directory);
      await waitFor(() => !isAlive(pid), 5000, 'the orphaned process to stop');
      assert.equal(onlyRecord(directory).signal, 'SIGTERM');
      await waitFor(() => !isAlive(host.watchdog), 10_000, 'the watchdog to exit');
    });
  }

  test('a host that closes its adapter on SIGTERM, as the control plane does, outlives its Claude Code process and its watchdog', async (t) => {
    const directory = scratch('host-signal');
    const host = await startHost(t, directory, 'signal');
    const { pid } = onlyRecord(directory);
    host.child.kill('SIGTERM');
    assert.equal(await host.exited, 0);
    // close() waits until the process and the watchdog are gone and the record is removed.
    assert.equal(isAlive(pid), false);
    assert.equal(isAlive(host.watchdog), false);
    assert.equal(existsSync(recordFile(directory)), false);
    const recorded = onlyRecord(directory);
    assert.equal(recorded.input_closed, true);
    assert.equal(recorded.signal, 'SIGTERM');
  });

  test('a host whose stdin ends, as when its test dies, exits and still stops its Claude Code process', async (t) => {
    const directory = scratch('host-stdin');
    const host = await startHost(t, directory, 'wait');
    const { pid } = onlyRecord(directory);
    assert.ok(isAlive(pid));
    // What the operating system does to the host's stdin when this process dies.
    host.child.stdin.end();
    assert.equal(await host.exited, 0);
    await waitFor(() => !isAlive(pid), 5000, 'the orphaned process to stop');
    assert.equal(onlyRecord(directory).signal, 'SIGTERM');
    await waitFor(() => !isAlive(host.watchdog), 10_000, 'the watchdog to exit');
  });
});

describe('a host killed without running exit handlers', () => {
  test('has its Claude Code process stopped by the watchdog', async (t) => {
    const directory = scratch('sigkill');
    const host = await startHost(t, directory, 'wait');
    const { pid } = onlyRecord(directory);
    assert.ok(isAlive(pid));
    host.child.kill('SIGKILL');
    await host.exited;
    await waitFor(() => !isAlive(pid), 15_000, 'the watchdog to stop the process');
    assert.equal(onlyRecord(directory).signal, 'SIGTERM');
    await waitFor(() => !isAlive(host.watchdog), 5000, 'the watchdog to exit');
  });

  test('running several sessions has every Claude Code process stopped by the watchdog', async (t) => {
    const directory = scratch('several');
    const host = await startHost(t, directory, 'wait', 3);
    const children = fakeRecords(directory);
    assert.equal(children.length, 3);
    assert.ok(children.every((child) => child.listed_before_input === true));
    const recorded = await readProcessRecords(recordFile(directory));
    assert.deepEqual(
      recorded.map((record) => record.pid).sort((a, b) => a - b),
      children.map((child) => child.pid),
    );
    assert.equal(new Set(recorded.map((record) => record.sessionId)).size, 3);
    host.child.kill('SIGKILL');
    await waitFor(
      () => children.every((child) => !isAlive(child.pid)),
      15_000,
      'the watchdog to stop every process',
    );
    assert.deepEqual(
      fakeRecords(directory).map((child) => child.signal),
      ['SIGTERM', 'SIGTERM', 'SIGTERM'],
    );
    await waitFor(() => !isAlive(host.watchdog), 5000, 'the watchdog to exit');
  });

  test('and whose watchdog is killed too has its Claude Code processes stopped by the next start', async (t) => {
    const directory = scratch('both-killed');
    const host = await startHost(t, directory, 'wait', 2);
    const children = fakeRecords(directory);
    assert.equal(children.length, 2);
    process.kill(host.watchdog, 'SIGKILL');
    await waitFor(() => !isAlive(host.watchdog), 5000, 'the watchdog to die');
    host.child.kill('SIGKILL');
    await host.exited;
    // Nobody is left to stop them, and they ignore the end of their input.
    await delay(500);
    assert.ok(children.every((child) => isAlive(child.pid)));

    const next = adapterFor(directory);
    try {
      const stale = await next.stopStaleProcesses();
      assert.deepEqual(
        [...stale].sort((a, b) => a.pid - b.pid),
        children.map((child) => ({ pid: child.pid, outcome: 'stopped' })),
      );
      // They exited; their new parent, init or launchd, reaps them shortly after.
      await waitFor(
        () => children.every((child) => !isAlive(child.pid)),
        5000,
        'the stopped processes to be reaped',
      );
      assert.deepEqual(
        fakeRecords(directory).map((child) => child.signal),
        ['SIGTERM', 'SIGTERM'],
      );
      assert.equal(existsSync(recordFile(directory)), false);
      assert.deepEqual(await next.stopStaleProcesses(), [], 'nothing is left to stop');
    } finally {
      await next.close();
    }
  });

  test('never has a recorded pid signalled once it belongs to an unrelated process', async (t) => {
    const directory = scratch('reused');
    const unrelated = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], {
      stdio: 'ignore',
    });
    t.after(() => unrelated.kill('SIGKILL'));
    const pid = unrelated.pid;
    assert.ok(pid !== undefined);
    const identity = await readProcessIdentity(pid);
    assert.ok(identity !== null);
    // The recorded launch was this pid at this second, but with a session id this process lacks.
    await writeProcessRecords(recordFile(directory), [
      { pid, ...identity, sessionId: '0f0e0d0c-0000-4000-8000-000000000000' },
    ]);
    const next = adapterFor(directory);
    try {
      assert.deepEqual(await next.stopStaleProcesses(), [{ pid, outcome: 'not_ours' }]);
      await delay(200);
      assert.equal(isAlive(pid), true);
      assert.equal(existsSync(recordFile(directory)), false);
    } finally {
      await next.close();
    }
  });
});
