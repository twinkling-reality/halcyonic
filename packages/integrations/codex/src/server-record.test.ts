/**
 * Stopping a server left running by an earlier run, without Codex: stand-in processes play the
 * recorded server and its commands, so the identity checks and signals are exercised on real
 * processes. Like Codex, a stand-in can start a child in a session of its own, which a signal to
 * the server's process group does not reach.
 */
import assert from 'node:assert/strict';
import { type ChildProcess, spawn } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { CodexRuntimeAdapter } from './codex-runtime.ts';
import {
  isRecordedProcess,
  readDescendants,
  readProcessIdentity,
  readServerRecord,
  runsAppServer,
  type ServerRecord,
  stopRecordedProcess,
  writeServerRecord,
} from './server-record.ts';

const WATCHDOG = fileURLToPath(new URL('./watchdog.ts', import.meta.url));

/**
 * Prints the pid of its command (0 without one) once set up, then idles. With WITH_COMMAND the
 * command is its child, in a session of its own as Codex starts one; with WITH_NESTED_COMMAND it
 * is one level down, below an intermediate process in the stand-in's group, as behind a launcher.
 */
const STAND_IN = `
import { spawn } from 'node:child_process';
if (process.env.IGNORE_TERM) process.on('SIGTERM', () => {});
const report = (pid) => {
  process.stdout.write(String(pid) + '\\n');
  setInterval(() => {}, 1000);
};
if (process.env.WITH_COMMAND) {
  const idle = ['-e', 'setInterval(() => {}, 1000)'];
  report(spawn(process.execPath, idle, { detached: true, stdio: 'ignore' }).pid);
} else if (process.env.WITH_NESTED_COMMAND) {
  const env = { ...process.env, WITH_NESTED_COMMAND: '', WITH_COMMAND: '1' };
  const middle = spawn(process.execPath, [process.argv[1], 'app-server'], { env, stdio: ['ignore', 'pipe', 'ignore'] });
  middle.stdout.once('data', (data) => report(String(data).trim()));
} else {
  report(0);
}
`;

interface StandIn {
  readonly pid: number;
  readonly record: ServerRecord;
  /** The pid of the command in a session of its own, or 0. */
  readonly child: number;
  /** Resolves with the signal that ended the stand-in, if one did. */
  readonly exited: Promise<NodeJS.Signals | null>;
}

function temporary(t: TestContext): string {
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-codex-record-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  return directory;
}

/** Starts a process that looks like a launched app-server, in its own group, and records it. */
async function standIn(t: TestContext, env: Record<string, string> = {}): Promise<StandIn> {
  const binaryPath = join(temporary(t), 'codex.mjs');
  writeFileSync(binaryPath, STAND_IN);
  const child: ChildProcess = spawn(process.execPath, [binaryPath, 'app-server'], {
    detached: true,
    stdio: ['ignore', 'pipe', 'ignore'],
    env: { ...process.env, ...env },
  });
  const pid = child.pid;
  assert.ok(pid !== undefined);
  const exited = new Promise<NodeJS.Signals | null>((resolve) =>
    child.once('exit', (_code, signal) => resolve(signal)),
  );
  const grandchild = await new Promise<number>((resolve) =>
    child.stdout?.once('data', (data: Buffer) => resolve(Number(data.toString().trim()))),
  );
  t.after(() => {
    for (const target of [-pid, grandchild]) {
      try {
        if (target !== 0) process.kill(target, 'SIGKILL');
      } catch {
        // Already stopped by the test.
      }
    }
  });
  const identity = await readProcessIdentity(pid);
  assert.ok(identity !== null && runsAppServer(identity.command, binaryPath));
  return {
    pid,
    child: grandchild,
    exited,
    record: { pid, binaryPath, command: identity.command, startedAt: identity.startedAt },
  };
}

function alive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

async function gone(pid: number, what: string): Promise<void> {
  for (let attempt = 0; attempt < 250 && alive(pid); attempt += 1) await delay(20);
  assert.equal(alive(pid), false, `${what} is still running`);
}

/** These tests never start an execution, so their adapters allow no directory. */
function adapterFor(file: string): CodexRuntimeAdapter {
  return new CodexRuntimeAdapter({
    binaryPath: '/unused',
    serverRecordFile: file,
    directoryPolicy: () => ({ ok: false, message: 'No directory is allowed in this test.' }),
  });
}

describe('a Codex server left running by an earlier run', () => {
  test('is stopped by the next start while the recorded process is still that server', async (t) => {
    const server = await standIn(t);
    const file = join(temporary(t), 'nested', 'codex-server.json');
    await writeServerRecord(file, server.record);
    const adapter = adapterFor(file);
    t.after(() => adapter.close());
    assert.deepEqual(await adapter.stopStaleServer(), { outcome: 'stopped', pid: server.pid });
    assert.equal(await server.exited, 'SIGTERM');
    assert.equal(existsSync(file), false);
  });

  test('that ignores SIGTERM is killed with its commands, which live in sessions of their own', async (t) => {
    const server = await standIn(t, { IGNORE_TERM: '1', WITH_COMMAND: '1' });
    assert.ok(server.child > 0 && alive(server.child));
    const descendants = await readDescendants(server.pid);
    assert.deepEqual(
      descendants.map((descendant) => [descendant.pid, descendant.groupId]),
      [[server.child, server.child]],
      'the command leads a group of its own, outside the server group',
    );
    const started = Date.now();
    assert.equal(await stopRecordedProcess(server.record, 300), 'stopped');
    assert.equal(await server.exited, 'SIGKILL');
    assert.ok(Date.now() - started >= 300, 'SIGKILL came before the grace period ended');
    await gone(server.child, 'the command');
  });

  test('that ignores SIGTERM is killed with a command further down its process tree', async (t) => {
    const server = await standIn(t, { IGNORE_TERM: '1', WITH_NESTED_COMMAND: '1' });
    assert.ok(server.child > 0 && alive(server.child));
    const descendants = await readDescendants(server.pid);
    assert.equal(descendants.length, 2);
    assert.equal(descendants.find((item) => item.pid !== server.child)?.groupId, server.pid);
    assert.equal(await stopRecordedProcess(server.record, 300), 'stopped');
    await gone(server.child, 'the nested command');
  });

  test('is never signalled when its pid now belongs to a different process', async (t) => {
    const other = await standIn(t);
    const file = join(temporary(t), 'codex-server.json');
    await writeServerRecord(file, { ...other.record, startedAt: 'Thu Jan  1 00:00:00 1970' });
    const adapter = adapterFor(file);
    t.after(() => adapter.close());
    assert.deepEqual(await adapter.stopStaleServer(), { outcome: 'not_ours', pid: other.pid });
    assert.equal(
      await stopRecordedProcess({ ...other.record, command: `${process.execPath} other` }),
      'not_ours',
    );
    await delay(200);
    assert.equal(alive(other.pid), true);
    assert.equal(existsSync(file), false, 'the stale record was not cleared');
  });

  test('that already exited leaves only its record to clear', async (t) => {
    const done = await standIn(t);
    process.kill(-done.pid, 'SIGKILL');
    await done.exited;
    const file = join(temporary(t), 'codex-server.json');
    await writeServerRecord(file, done.record);
    const adapter = adapterFor(file);
    t.after(() => adapter.close());
    assert.deepEqual(await adapter.stopStaleServer(), { outcome: 'not_running', pid: done.pid });
    assert.equal(existsSync(file), false);
  });

  test('a record must name the binary the process runs as an app-server', async (t) => {
    const server = await standIn(t);
    const identity = await readProcessIdentity(server.pid);
    assert.ok(identity !== null);
    assert.equal(isRecordedProcess(identity, server.record), true);
    assert.equal(
      isRecordedProcess(identity, { ...server.record, binaryPath: '/opt/codex' }),
      false,
    );
    assert.equal(runsAppServer('/opt/codex app-server', '/opt/codex'), true);
    assert.equal(runsAppServer('/opt/codex exec', '/opt/codex'), false);
    assert.equal(runsAppServer('/opt/codex-other app-server', '/opt/codex'), false);
    assert.equal(await readProcessIdentity(2 ** 22 + 12_345), null);
  });

  test('an unreadable record file is treated as no record', async (t) => {
    const file = join(temporary(t), 'codex-server.json');
    assert.equal(await readServerRecord(file), null);
    writeFileSync(file, '{"pid": "one"}');
    assert.equal(await readServerRecord(file), null);
  });
});

describe('the Codex watchdog', () => {
  /** A process that hosts the watchdog the way the adapter does and can be killed like a crash. */
  async function host(t: TestContext, record: ServerRecord) {
    const script = `
      const watchdog = require('node:child_process').spawn(process.execPath, [${JSON.stringify(WATCHDOG)}], { detached: true, stdio: ['pipe', 'ignore', 'ignore'], env: {} });
      watchdog.stdin.write(${JSON.stringify(`${JSON.stringify(record)}\n`)});
      process.stdout.write(String(watchdog.pid) + '\\n');
      setInterval(() => {}, 1000);`;
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
    assert.equal(await server.exited, 'SIGTERM');
    await gone(watchdog, 'the watchdog');
  });

  test('leaves a process alone when it is not the recorded server', async (t) => {
    const other = await standIn(t);
    const { child, watchdog } = await host(t, {
      ...other.record,
      startedAt: 'Thu Jan  1 00:00:00 1970',
    });
    child.kill('SIGKILL');
    await gone(watchdog, 'the watchdog');
    assert.equal(alive(other.pid), true);
  });
});
