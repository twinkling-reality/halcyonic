/**
 * Stopping a server left running by an earlier run, without Codex: stand-in processes play the
 * recorded server and its commands, so the identity checks and signals are exercised on real
 * processes. Like Codex, a stand-in can start a child in a session of its own, which a signal to
 * the server's process group does not reach.
 */
import assert from 'node:assert/strict';
import { type ChildProcess, execFileSync, spawn } from 'node:child_process';
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

const MIB = 1024 * 1024;

interface LongCommand {
  readonly pid: number;
  /** The command line as passed, which `ps` prints unchanged when it is ASCII. */
  readonly command: string;
}

/**
 * Starts `count` children of this test, each with `chunks` arguments of 32 KiB of `character`, and
 * waits until `ps` prints all their command lines, more than 1 MiB together, which is Node's
 * default limit on a command's output. Each is `/bin/sh` waiting in its built-in `read` on an input
 * only this test holds: sh would replace itself and its arguments with a lone external command, and
 * the input ends when this test's process ends, however it ends.
 */
async function longCommands(
  t: TestContext,
  count: number,
  chunks: number,
  character = 'x',
): Promise<LongCommand[]> {
  const chunk = character.repeat((32 * 1024) / Buffer.byteLength(character));
  const args = ['-c', 'read line', 'halcyonic-long-command', ...Array<string>(chunks).fill(chunk)];
  const started: LongCommand[] = [];
  for (let index = 0; index < count; index += 1) {
    const child = spawn('/bin/sh', args, { stdio: ['pipe', 'ignore', 'ignore'], env: {} });
    t.after(() => child.kill('SIGKILL'));
    assert.ok(child.pid !== undefined, 'a process with a long command line did not start');
    started.push({ pid: child.pid, command: ['/bin/sh', ...args].join(' ') });
  }
  const least = Buffer.byteLength(started[0]?.command ?? '');
  const pids = started.map(({ pid }) => pid).join(',');
  for (let attempt = 0; ; attempt += 1) {
    const lines = execFileSync('ps', ['-ww', '-o', 'args=', '-p', pids], {
      env: { PATH: '/bin:/usr/bin:/sbin:/usr/sbin', LC_ALL: 'C' },
      encoding: 'utf8',
      maxBuffer: 64 * MIB,
    })
      .split('\n')
      .filter((line) => line !== '');
    const printed = lines.reduce((total, line) => total + line.length, 0);
    const whole = lines.length === count && lines.every((line) => line.length >= least);
    if (whole && printed > MIB) return started;
    assert.ok(attempt < 50, `ps printed ${printed} bytes of the long command lines`);
    await delay(20);
  }
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

describe('reading processes while long command lines fill the process table', () => {
  test('finds the commands of a server while other command lines print more than 1 MiB', async (t) => {
    await longCommands(t, 32, 2);
    const server = await standIn(t, { WITH_COMMAND: '1' });
    const descendants = await readDescendants(server.pid);
    assert.deepEqual(
      descendants.map((descendant) => [descendant.pid, descendant.groupId]),
      [[server.child, server.child]],
    );
  });

  test('reads descendants whose command lines together print more than 1 MiB', async (t) => {
    const commands = await longCommands(t, 32, 2);
    const descendants = await readDescendants(process.pid);
    for (const { pid, command } of commands) {
      assert.equal(descendants.find((descendant) => descendant.pid === pid)?.command, command);
    }
  });

  test('reads a command whose line alone prints more than 1 MiB', {
    skip:
      process.platform !== 'darwin' &&
      'only macOS ps prints a byte beyond ASCII as several characters, so one command line can print more than 1 MiB',
  }, async (t) => {
    const [long] = await longCommands(t, 1, 14, 'é');
    assert.ok(long !== undefined);
    const identity = await readProcessIdentity(long.pid);
    assert.ok(identity !== null && identity.command.length > MIB);
    const descendants = await readDescendants(process.pid);
    assert.deepEqual(
      descendants.find((descendant) => descendant.pid === long.pid),
      identity,
    );
  });
});
