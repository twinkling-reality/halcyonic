/**
 * Runs the control plane as a real process, launched the way a test harness launches it, and
 * checks when it stops. Each test starts its own processes, with a data directory and home of their
 * own and an ephemeral port, and ends them and removes the directory even when it fails.
 */
import assert from 'node:assert/strict';
import { type ChildProcessWithoutNullStreams, spawn } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createInterface } from 'node:readline';
import type { Readable } from 'node:stream';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { SETTINGS_FILE, writeHostSettings } from './settings.ts';
import { tlsRequest } from './testing/tls-client.ts';

const MAIN = fileURLToPath(new URL('./main.ts', import.meta.url));
const LAUNCHER = fileURLToPath(new URL('./testing/launcher.ts', import.meta.url));
const EXIT_ON_STDIN_END = { HALCYONIC_EXIT_ON_STDIN_END: '1' };

/** A line of output: a log line, the launcher's report, or `{ text }` for anything else. */
type Line = Record<string, unknown>;

interface Output {
  readonly lines: Line[];
  /** True once every process writing to stdout has closed it. */
  ended(): boolean;
  stderr(): string;
}

function collect(stdout: Readable, stderr: Readable): Output {
  const lines: Line[] = [];
  let ended = false;
  let errors = '';
  createInterface({ input: stdout })
    .on('line', (line) => lines.push(parse(line)))
    .on('close', () => {
      ended = true;
    });
  stderr.setEncoding('utf8');
  stderr.on('data', (chunk: string) => {
    errors += chunk;
  });
  return { lines, ended: () => ended, stderr: () => errors };
}

function parse(line: string): Line {
  try {
    const value: unknown = JSON.parse(line);
    if (typeof value === 'object' && value !== null) return value as Line;
  } catch {
    // Not JSON.
  }
  return { text: line };
}

/** Why each `shutting down` line says the control plane is stopping. */
function shutdowns(output: Output): Line[] {
  return output.lines
    .filter((line) => line.msg === 'shutting down')
    .map((line) => ('signal' in line ? { signal: line.signal } : { reason: line.reason }));
}

function alive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === 'EPERM';
  }
}

function exited(child: ChildProcessWithoutNullStreams): boolean {
  return child.exitCode !== null || child.signalCode !== null;
}

async function until(condition: () => boolean, what: string, timeoutMs = 10_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!condition()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await delay(20);
  }
}

/** A directory of the test's own, and an environment that inherits nothing else. */
function environment(extra: Record<string, string>) {
  const root = mkdtempSync(join(tmpdir(), 'halcyonic-main-'));
  const env = {
    PATH: process.env.PATH ?? '',
    HOME: root,
    HALCYONIC_DATA_DIR: join(root, 'data'),
    HALCYONIC_PORT: '0',
    HALCYONIC_LOG_LEVEL: 'info',
    ...extra,
  };
  return { root, env };
}

/** Waits for the `control plane ready` line and returns it. */
async function ready(output: Output): Promise<Line> {
  const find = () => output.lines.find((line) => line.msg === 'control plane ready');
  await until(() => find() !== undefined || output.ended(), 'the control plane to start', 30_000);
  const line = find();
  assert.ok(line, `the control plane stopped before it was ready:\n${output.stderr()}`);
  return line;
}

function portOf(line: Line): number {
  const [address] = line.addresses as { port: number }[];
  assert.ok(address !== undefined);
  return address.port;
}

/** Starts the control plane with piped stdio, as a harness does, and waits until it serves. */
async function start(
  t: TestContext,
  extra: Record<string, string>,
  prepare?: (paths: { root: string; dataDir: string }) => void,
) {
  const { root, env } = environment(extra);
  prepare?.({ root, dataDir: env.HALCYONIC_DATA_DIR });
  const child = spawn(process.execPath, [MAIN], { env, stdio: 'pipe' });
  const output = collect(child.stdout, child.stderr);
  t.after(async () => {
    // Does nothing once the process has exited, so it never signals a reused pid.
    child.kill('SIGKILL');
    await until(() => exited(child), 'the control plane to be killed');
    rmSync(root, { recursive: true, force: true });
  });
  const line = await ready(output);
  return { child, output, port: portOf(line), ready: line, dataDir: env.HALCYONIC_DATA_DIR };
}

/**
 * Starts testing/launcher.ts, which starts the control plane, and waits until the control plane
 * serves. The control plane writes to the launcher's stdout, so that output ends only once both
 * have exited.
 */
async function launch(t: TestContext, extra: Record<string, string>) {
  const { root, env } = environment(extra);
  const launcher = spawn(process.execPath, [LAUNCHER], { env, stdio: 'pipe' });
  const output = collect(launcher.stdout, launcher.stderr);
  let launched: number | undefined;
  t.after(async () => {
    launcher.kill('SIGKILL');
    await until(() => exited(launcher), 'the launcher to be killed');
    // Until the output ends, the control plane holds it open, so the pid is still its own.
    if (launched !== undefined && !output.ended()) {
      try {
        process.kill(launched, 'SIGKILL');
      } catch {
        // It exited just now.
      }
    }
    await until(() => output.ended(), 'the control plane to be killed');
    rmSync(root, { recursive: true, force: true });
  });
  await until(
    () => output.lines.some((line) => 'launched' in line) || output.ended(),
    'the launcher to start the control plane',
  );
  launched = output.lines.find((line) => 'launched' in line)?.launched as number | undefined;
  assert.ok(typeof launched === 'number', `the launcher did not start:\n${output.stderr()}`);
  const line = await ready(output);
  assert.equal(line.pid, launched, 'the process that serves is the one the launcher started');
  return { launcher, pid: launched, output };
}

describe('the control plane process', () => {
  test('with HALCYONIC_EXIT_ON_STDIN_END=1, it shuts down cleanly when its stdin ends', async (t) => {
    const { child, output } = await start(t, EXIT_ON_STDIN_END);
    child.stdin.end();
    await until(() => exited(child) && output.ended(), 'the control plane to exit');
    assert.equal(child.exitCode, 0, `exit status; stderr: ${output.stderr()}`);
    // Stdin ends and then closes; the second cause neither repeats the shutdown nor hurries it.
    assert.deepEqual(shutdowns(output), [{ reason: 'stdin ended' }]);
  });

  test('with HALCYONIC_EXIT_ON_STDIN_END=1, it stops when the process that launched it is killed', async (t) => {
    const { launcher, pid, output } = await launch(t, EXIT_ON_STDIN_END);
    launcher.kill('SIGKILL');
    // Orphaned and adopted by init (launchd on macOS), so only its pid tells whether it runs.
    await until(() => !alive(pid), 'the control plane to stop after its launcher was killed');
    await until(() => output.ended(), 'the control plane output to end');
    assert.deepEqual(shutdowns(output), [{ reason: 'stdin ended' }]);
  });

  test('without HALCYONIC_EXIT_ON_STDIN_END, the end of its stdin does not stop it', async (t) => {
    const { child, port } = await start(t, {});
    child.stdin.end();
    // With the switch, it has stopped within milliseconds of this.
    await delay(500);
    assert.equal(exited(child), false);
    const health = await fetch(`http://127.0.0.1:${port}/api/health`);
    assert.deepEqual(await health.json(), { status: 'ok' });
  });

  test('with HALCYONIC_EXIT_ON_STDIN_END=1, a signal still shuts it down cleanly while stdin stays open', async (t) => {
    const { child, output } = await start(t, EXIT_ON_STDIN_END);
    child.kill('SIGTERM');
    // Reading stdin must not keep the process alive once the shutdown is done.
    await until(() => exited(child) && output.ended(), 'the control plane to exit');
    assert.equal(child.exitCode, 0, `exit status; stderr: ${output.stderr()}`);
    assert.deepEqual(shutdowns(output), [{ signal: 'SIGTERM' }]);
  });

  test('serves nothing on the network unless HALCYONIC_NETWORK_HOST is set', async (t) => {
    const { ready: line } = await start(t, {});
    assert.equal(line.network, null);
  });

  test('with HALCYONIC_NETWORK_HOST, it also serves TLS for devices, with an identity it keeps', async (t) => {
    const network = { HALCYONIC_NETWORK_HOST: '127.0.0.1', HALCYONIC_NETWORK_PORT: '0' };
    const { child, output, ready: line, dataDir } = await start(t, network);
    const listener = line.network as { port: number; certificate_sha256: string };
    const health = await tlsRequest(
      { host: '127.0.0.1', port: listener.port },
      { method: 'GET', path: '/api/health', pin: listener.certificate_sha256 },
    );
    assert.equal(health.status, 200);
    assert.ok(dataDir !== undefined);
    for (const file of ['network-key.pem', 'network-certificate.pem']) {
      assert.equal(statSync(join(dataDir, file)).mode & 0o777, 0o600, file);
    }
    assert.ok(
      output.lines.some((entry) => entry.msg === 'created the network listener certificate'),
    );
    child.kill('SIGTERM');
    await until(() => exited(child) && output.ended(), 'the control plane to exit');

    // The same data directory keeps the same certificate, so paired devices still trust it.
    const again = spawn(process.execPath, [MAIN], {
      env: {
        PATH: process.env.PATH ?? '',
        HOME: join(dataDir, '..'),
        HALCYONIC_DATA_DIR: dataDir,
        HALCYONIC_PORT: '0',
        HALCYONIC_LOG_LEVEL: 'info',
        ...network,
      },
      stdio: 'pipe',
    });
    t.after(async () => {
      again.kill('SIGKILL');
      await until(() => exited(again), 'the second control plane to be killed');
    });
    const restarted = await ready(collect(again.stdout, again.stderr));
    const kept = restarted.network as { certificate_sha256: string };
    assert.equal(kept.certificate_sha256, listener.certificate_sha256);
    // Stopped here, before the first start's cleanup removes the directory it uses.
    again.kill('SIGTERM');
    await until(() => exited(again), 'the second control plane to exit');
  });

  test('takes what the environment leaves unset from the settings file pnpm mac-setup writes', async (t) => {
    let projects = '';
    const { ready: line } = await start(t, { HALCYONIC_NETWORK_PORT: '0' }, ({ root, dataDir }) => {
      projects = join(root, 'projects');
      mkdirSync(projects);
      writeHostSettings(dataDir, {
        HALCYONIC_PROJECT_ROOTS: projects,
        HALCYONIC_NETWORK_HOST: '127.0.0.1',
      });
    });
    assert.deepEqual(line.project_roots, [projects]);
    assert.notEqual(line.network, null);
    assert.deepEqual((line.settings as { used: string[] }).used, [
      'HALCYONIC_PROJECT_ROOTS',
      'HALCYONIC_NETWORK_HOST',
    ]);
  });

  test('a variable in the environment wins over the settings file', async (t) => {
    const { ready: line } = await start(t, { HALCYONIC_PROJECT_ROOTS: '' }, ({ root, dataDir }) => {
      const projects = join(root, 'projects');
      mkdirSync(projects);
      writeHostSettings(dataDir, { HALCYONIC_PROJECT_ROOTS: projects });
    });
    assert.deepEqual(line.project_roots, []);
    assert.deepEqual((line.settings as { used: string[] }).used, []);
  });

  test('refuses to start from a settings file other users can read', async (t) => {
    const { root, env } = environment({});
    mkdirSync(env.HALCYONIC_DATA_DIR, { mode: 0o700 });
    const path = join(env.HALCYONIC_DATA_DIR, SETTINGS_FILE);
    writeFileSync(path, '{"format": 1}');
    chmodSync(path, 0o644);
    const child = spawn(process.execPath, [MAIN], { env, stdio: 'pipe' });
    const output = collect(child.stdout, child.stderr);
    t.after(async () => {
      child.kill('SIGKILL');
      await until(() => exited(child), 'the control plane to be killed');
      rmSync(root, { recursive: true, force: true });
    });
    await until(() => exited(child) && output.ended(), 'the control plane to stop');
    assert.equal(child.exitCode, 1);
    assert.match(output.stderr(), /chmod 600/);
    assert.ok(!output.lines.some((entry) => entry.msg === 'control plane ready'));
  });
});
