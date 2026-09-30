/**
 * Stopping a server left running by an earlier run, without OpenCode: stand-in processes play the
 * recorded server, so the identity checks and signals are exercised on real processes.
 */
import assert from 'node:assert/strict';
import { type ChildProcess, execFileSync, spawn } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { OpenCodeRuntimeAdapter } from './opencode-runtime.ts';
import {
  isRecordedProcess,
  readProcessIdentity,
  readServerRecord,
  type ServerRecord,
  stopRecordedProcess,
  writeServerRecord,
} from './server-record.ts';

const WATCHDOG = fileURLToPath(new URL('./watchdog.ts', import.meta.url));
const IDLE = 'setInterval(() => {}, 1000)';
const READY = `process.stdout.write('ready\\n'); ${IDLE}`;

interface StandIn {
  readonly pid: number;
  readonly record: ServerRecord;
  /** The first line the stand-in printed once set up. */
  readonly ready: string;
  /** Resolves with the signal that ended the stand-in, if one did. */
  readonly exited: Promise<NodeJS.Signals | null>;
}

/**
 * Starts a process that looks like a launched server, in its own group, and records it. The
 * script must print a line once it is set up, so no signal arrives before its handlers exist.
 */
async function standIn(t: TestContext, script = READY): Promise<StandIn> {
  const child: ChildProcess = spawn(
    process.execPath,
    ['-e', script, 'serve', '--hostname', '127.0.0.1', '--port', '1'],
    { detached: true, stdio: ['ignore', 'pipe', 'ignore'] },
  );
  const pid = child.pid;
  assert.ok(pid !== undefined);
  const exited = new Promise<NodeJS.Signals | null>((resolve) =>
    child.once('exit', (_code, signal) => resolve(signal)),
  );
  t.after(() => {
    try {
      process.kill(-pid, 'SIGKILL');
    } catch {
      // Already stopped by the test.
    }
  });
  const ready = await new Promise<string>((resolve) =>
    child.stdout?.once('data', (data: Buffer) => resolve(data.toString().trim())),
  );
  const identity = await readProcessIdentity(pid);
  if (identity === null) throw new Error('the stand-in process is not running');
  assert.ok(identity.command.startsWith(`${process.execPath} -e`));
  const record = {
    pid,
    port: 1,
    binaryPath: process.execPath,
    command: identity.command,
    startedAt: identity.startedAt,
  };
  return { pid, record, ready, exited };
}

function alive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

/** These tests never start an execution, so their adapters allow no directory. */
function adapterFor(file: string): OpenCodeRuntimeAdapter {
  return new OpenCodeRuntimeAdapter({
    binaryPath: '/unused',
    serverRecordFile: file,
    directoryPolicy: () => ({
      ok: false,
      code: 'location_not_allowed',
      message: 'No directory is allowed in this test.',
    }),
  });
}

function recordFile(t: TestContext): string {
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-opencode-record-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  return join(directory, 'nested', 'opencode-server.json');
}

async function within<T>(promise: Promise<T>, ms: number, what: string): Promise<T> {
  const timer = new AbortController();
  try {
    return await Promise.race([
      promise,
      delay(ms, undefined, { signal: timer.signal }).then(() => {
        throw new Error(`timed out waiting for ${what}`);
      }),
    ]);
  } finally {
    timer.abort();
  }
}

const MIB = 1024 * 1024;

const LONG_COMMAND_SKIP =
  process.platform !== 'darwin' &&
  'only macOS ps prints a byte beyond ASCII as several characters, so one command line can print more than 1 MiB';

/**
 * Starts a child of this test whose command line `ps` prints as more than 1 MiB, Node's default
 * limit on a command's output: 448 KiB of a character beyond ASCII, which macOS `ps` prints as
 * three characters a byte. It is `/bin/sh` waiting in its built-in `read` on an input only this
 * test holds, so it keeps its command line and ends when this test's process ends.
 */
async function longCommand(t: TestContext): Promise<number> {
  const args = [
    '-c',
    'read line',
    'halcyonic-long-command',
    ...Array<string>(14).fill('é'.repeat(16 * 1024)),
  ];
  const child = spawn('/bin/sh', args, { stdio: ['pipe', 'ignore', 'ignore'], env: {} });
  t.after(() => child.kill('SIGKILL'));
  const pid = child.pid;
  assert.ok(pid !== undefined, 'the process with a long command line did not start');
  for (let attempt = 0; ; attempt += 1) {
    const printed = execFileSync('ps', ['-ww', '-o', 'args=', '-p', String(pid)], {
      env: { PATH: '/bin:/usr/bin:/sbin:/usr/sbin', LC_ALL: 'C' },
      encoding: 'utf8',
      maxBuffer: 64 * MIB,
    });
    if (printed.length > MIB) return pid;
    assert.ok(attempt < 50, `ps printed ${printed.length} bytes of the long command line`);
    await delay(20);
  }
}

describe('a server left running by an earlier run', () => {
  test('is stopped with its process group when the recorded process is still that server', async (t) => {
    // The stand-in starts a grandchild in its group, as OpenCode starts shell commands.
    const server = await standIn(
      t,
      `const c = require('node:child_process').spawn(process.execPath, ['-e', '${IDLE}'], { stdio: 'ignore' }); process.stdout.write(String(c.pid) + '\\n'); ${IDLE}`,
    );
    const grandchild = Number(server.ready);
    assert.equal(alive(grandchild), true);
    const file = recordFile(t);
    await writeServerRecord(file, server.record);
    const adapter = adapterFor(file);
    const result = await adapter.stopStaleServer();
    assert.deepEqual(result, { outcome: 'stopped', pid: server.pid, port: 1 });
    await within(server.exited, 5000, 'the stand-in to exit');
    for (let attempt = 0; attempt < 100 && alive(grandchild); attempt += 1) await delay(20);
    assert.equal(alive(grandchild), false, 'the process group was not stopped');
    assert.equal(existsSync(file), false);
    await adapter.close();
  });

  test('is never signalled when its pid now belongs to a different process', async (t) => {
    const other = await standIn(t);
    const file = recordFile(t);
    const reused = { ...other.record, startedAt: 'Thu Jan  1 00:00:00 1970' };
    await writeServerRecord(file, reused);
    const adapter = adapterFor(file);
    assert.deepEqual(await adapter.stopStaleServer(), {
      outcome: 'not_ours',
      pid: other.pid,
      port: 1,
    });
    assert.equal(
      await stopRecordedProcess({ ...other.record, command: `${process.execPath} other` }),
      'not_ours',
    );
    await delay(200);
    assert.equal(alive(other.pid), true);
    assert.equal(existsSync(file), false, 'the stale record was not cleared');
  });

  test('that already exited leaves only its record to clear', async (t) => {
    const gone = await standIn(t);
    process.kill(-gone.pid, 'SIGKILL');
    await gone.exited;
    const file = recordFile(t);
    await writeServerRecord(file, gone.record);
    const adapter = adapterFor(file);
    assert.deepEqual(await adapter.stopStaleServer(), {
      outcome: 'not_running',
      pid: gone.pid,
      port: 1,
    });
    assert.equal(existsSync(file), false);
  });

  test('that ignores SIGTERM is killed after the grace period', async (t) => {
    const stubborn = await standIn(t, `process.on('SIGTERM', () => {}); ${READY}`);
    const started = Date.now();
    assert.equal(await stopRecordedProcess(stubborn.record, 300), 'stopped');
    assert.equal(await within(stubborn.exited, 5000, 'the stand-in to exit'), 'SIGKILL');
    assert.ok(Date.now() - started >= 300, 'SIGKILL came before the grace period ended');
  });

  test('a record must name the binary the process runs', async (t) => {
    const server = await standIn(t);
    const identity = await readProcessIdentity(server.pid);
    assert.ok(identity !== null);
    assert.equal(isRecordedProcess(identity, server.record), true);
    assert.equal(
      isRecordedProcess(identity, { ...server.record, binaryPath: '/opt/opencode' }),
      false,
    );
    assert.equal(await readProcessIdentity(2 ** 22 + 12_345), null);
  });

  test('is never signalled when its pid now belongs to a process whose command line prints more than 1 MiB', {
    skip: LONG_COMMAND_SKIP,
  }, async (t) => {
    const pid = await longCommand(t);
    const identity = await readProcessIdentity(pid);
    assert.ok(identity !== null && identity.command.length > MIB);
    const record = {
      pid,
      port: 1,
      binaryPath: '/opt/opencode',
      command: '/opt/opencode serve',
      startedAt: identity.startedAt,
    };
    assert.equal(await stopRecordedProcess(record), 'not_ours');
    assert.equal(alive(pid), true);
  });

  test('an unreadable record file is treated as no record', async (t) => {
    const file = recordFile(t);
    assert.equal(await readServerRecord(file), null);
    await writeServerRecord(file, {
      pid: 1,
      port: 1,
      binaryPath: '/opt/opencode',
      command: 'x',
      startedAt: 'y',
    });
    writeFileSync(file, '{"pid": "one"}');
    assert.equal(await readServerRecord(file), null);
  });
});

describe('the watchdog', () => {
  /** A process that hosts the watchdog the way the adapter does and can be killed like a crash. */
  async function host(t: TestContext, record: ServerRecord) {
    const script = `
      const watchdog = require('node:child_process').spawn(process.execPath, [${JSON.stringify(WATCHDOG)}], { detached: true, stdio: ['pipe', 'ignore', 'ignore'], env: {} });
      watchdog.stdin.write(${JSON.stringify(`${JSON.stringify(record)}\n`)});
      process.stdout.write(String(watchdog.pid) + '\\n');
      ${IDLE}`;
    const child = spawn(process.execPath, ['-e', script], { stdio: ['ignore', 'pipe', 'ignore'] });
    t.after(() => child.kill('SIGKILL'));
    const watchdog = await new Promise<number>((resolve) =>
      child.stdout?.once('data', (data: Buffer) => resolve(Number(data.toString().trim()))),
    );
    t.after(() => {
      try {
        process.kill(watchdog, 'SIGKILL');
      } catch {
        // Already exited.
      }
    });
    // Let the watchdog start and read the record.
    await delay(500);
    return { child, watchdog };
  }

  test('stops the server when the process that launched it dies, even from SIGKILL', async (t) => {
    const server = await standIn(t);
    const { child, watchdog } = await host(t, server.record);
    child.kill('SIGKILL');
    await within(server.exited, 10_000, 'the watchdog to stop the server');
    for (let attempt = 0; attempt < 100 && alive(watchdog); attempt += 1) await delay(50);
    assert.equal(alive(watchdog), false, 'the watchdog did not exit');
  });

  test('leaves a process alone when it is not the recorded server', async (t) => {
    const other = await standIn(t);
    const { child, watchdog } = await host(t, {
      ...other.record,
      startedAt: 'Thu Jan  1 00:00:00 1970',
    });
    child.kill('SIGKILL');
    for (let attempt = 0; attempt < 100 && alive(watchdog); attempt += 1) await delay(50);
    assert.equal(alive(watchdog), false, 'the watchdog did not exit');
    assert.equal(alive(other.pid), true);
  });
});
