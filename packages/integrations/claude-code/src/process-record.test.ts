/**
 * Recording and stopping launched processes, without Claude Code or the SDK: stand-in processes
 * play the recorded ones, so the identity checks and signals are exercised on real processes.
 */
import assert from 'node:assert/strict';
import { type ChildProcess, execFileSync, spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { mkdtempSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import {
  isRecordedProcess,
  type ProcessRecord,
  readProcessIdentity,
  readProcessRecords,
  stopRecordedProcess,
  writeProcessRecords,
} from './process-record.ts';

const WATCHDOG = fileURLToPath(new URL('./watchdog.ts', import.meta.url));
const READY = `process.stdout.write('ready\\n'); setInterval(() => {}, 1000)`;

interface StandIn {
  readonly pid: number;
  readonly record: ProcessRecord;
  /** Resolves with the signal that ended the stand-in, if one did. */
  readonly exited: Promise<NodeJS.Signals | null>;
}

function directory(t: TestContext): string {
  const path = mkdtempSync(join(tmpdir(), 'halcyonic-claude-record-'));
  t.after(() => rmSync(path, { recursive: true, force: true }));
  return path;
}

function track(t: TestContext, child: ChildProcess): Promise<NodeJS.Signals | null> {
  t.after(() => child.kill('SIGKILL'));
  return new Promise((resolve) => child.once('exit', (_code, signal) => resolve(signal)));
}

/**
 * Starts a process that carries a session id the way a launched Claude Code does, and records it.
 * The script prints a line once set up, so no signal arrives before its handlers exist.
 */
async function standIn(t: TestContext, script = READY): Promise<StandIn> {
  const sessionId = randomUUID();
  // `--` ends node's own options, so the session id reaches the script as an argument.
  const child = spawn(process.execPath, ['-e', script, '--', `--session-id=${sessionId}`], {
    stdio: ['ignore', 'pipe', 'ignore'],
  });
  const exited = track(t, child);
  const pid = child.pid;
  assert.ok(pid !== undefined);
  await new Promise<void>((resolve, reject) => {
    child.stdout?.once('data', () => resolve());
    child.once('exit', (code) => reject(new Error(`the stand-in exited early with ${code}`)));
  });
  const identity = await readProcessIdentity(pid);
  assert.ok(identity !== null);
  return { pid, record: { pid, ...identity, sessionId }, exited };
}

function alive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
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

describe('a recorded process', () => {
  test('is stopped with SIGTERM while it is still the recorded process', async (t) => {
    const recorded = await standIn(t);
    assert.equal(await stopRecordedProcess(recorded.record), 'stopped');
    assert.equal(await within(recorded.exited, 5000, 'the stand-in to exit'), 'SIGTERM');
  });

  test('that ignores SIGTERM is killed after the grace period', async (t) => {
    const stubborn = await standIn(t, `process.on('SIGTERM', () => {}); ${READY}`);
    const started = Date.now();
    assert.equal(await stopRecordedProcess(stubborn.record, 300), 'stopped');
    assert.equal(await within(stubborn.exited, 5000, 'the stand-in to exit'), 'SIGKILL');
    assert.ok(Date.now() - started >= 300, 'SIGKILL came before the grace period ended');
  });

  test('that already exited is reported as not running', async (t) => {
    const gone = await standIn(t);
    process.kill(gone.pid, 'SIGKILL');
    await gone.exited;
    assert.equal(await stopRecordedProcess(gone.record), 'not_running');
    assert.equal(await readProcessIdentity(2 ** 22 + 12_345), null);
  });

  test('is never signalled when its pid now belongs to an unrelated process', async (t) => {
    const other = await standIn(t);
    // A process with the recorded pid that started at another time, or that does not carry the
    // recorded session id, is not the recorded process.
    const startedElsewhen = { ...other.record, startedAt: 'Thu Jan  1 00:00:00 1970' };
    const anotherLaunch = { ...other.record, sessionId: randomUUID() };
    assert.equal(await stopRecordedProcess(startedElsewhen), 'not_ours');
    assert.equal(await stopRecordedProcess(anotherLaunch), 'not_ours');
    await delay(200);
    assert.equal(alive(other.pid), true);
  });

  test('is never signalled when its pid now belongs to a process whose command line prints more than 1 MiB', {
    skip: LONG_COMMAND_SKIP,
  }, async (t) => {
    const pid = await longCommand(t);
    const identity = await readProcessIdentity(pid);
    assert.ok(identity !== null && identity.command.length > MIB);
    const record = { pid, ...identity, command: 'claude', sessionId: randomUUID() };
    assert.equal(await stopRecordedProcess(record), 'not_ours');
    assert.equal(alive(pid), true);
  });

  test('is still recognized after a script executable execs its interpreter', async (t) => {
    const folder = directory(t);
    // Like a script run through `#!/usr/bin/env node`: the launched program execs another one,
    // keeping its pid, start time and arguments.
    const script = join(folder, 'hop');
    writeFileSync(
      script,
      `#!/bin/sh\nsleep 0.5\nexec "${process.execPath}" -e 'setInterval(() => {}, 1000)' -- "$@"\n`,
      { mode: 0o755 },
    );
    const sessionId = randomUUID();
    const child = spawn(script, [`--session-id=${sessionId}`], { stdio: 'ignore' });
    const exited = track(t, child);
    const pid = child.pid;
    assert.ok(pid !== undefined);
    const launched = await readProcessIdentity(pid);
    assert.ok(launched !== null);
    assert.ok(launched.command.startsWith('/bin/sh '), launched.command);
    const record = { pid, ...launched, sessionId };
    let current = launched;
    for (let attempt = 0; attempt < 100 && current.command === launched.command; attempt += 1) {
      await delay(50);
      current = (await readProcessIdentity(pid)) ?? current;
    }
    assert.ok(current.command.startsWith(`${process.execPath} -e`), current.command);
    assert.equal(current.startedAt, launched.startedAt);
    assert.equal(isRecordedProcess(current, record), true);
    assert.equal(await stopRecordedProcess(record), 'stopped');
    assert.equal(await within(exited, 5000, 'the script to exit'), 'SIGTERM');
  });
});

describe('the process record file', () => {
  test('is private to its owner and round-trips the records', async (t) => {
    const file = join(directory(t), 'nested', 'claude-agent-processes.json');
    assert.deepEqual(await readProcessRecords(file), []);
    const records = [
      { pid: 41, startedAt: 'Sat Sep 26 23:13:45 2026', command: 'claude a', sessionId: 'one' },
      { pid: 42, startedAt: 'Sat Sep 26 23:13:46 2026', command: 'claude b', sessionId: 'two' },
    ];
    await writeProcessRecords(file, records);
    assert.equal(statSync(file).mode & 0o777, 0o600);
    assert.deepEqual(await readProcessRecords(file), records);
  });

  test('that cannot be understood counts as empty', async (t) => {
    const file = join(directory(t), 'claude-agent-processes.json');
    writeFileSync(file, '{"processes": [{"pid": "one"}]}');
    assert.deepEqual(await readProcessRecords(file), []);
    writeFileSync(file, 'not json');
    assert.deepEqual(await readProcessRecords(file), []);
  });
});

describe('the watchdog', () => {
  test('leaves a process alone when its pid no longer belongs to the recorded process', async (t) => {
    const other = await standIn(t);
    const watchdog = spawn(process.execPath, [WATCHDOG], { stdio: ['pipe', 'ignore', 'ignore'] });
    const exited = new Promise((resolve) => watchdog.once('exit', resolve));
    t.after(() => watchdog.kill('SIGKILL'));
    const reused = { ...other.record, sessionId: randomUUID() };
    watchdog.stdin.end(`${JSON.stringify({ processes: [reused] })}\n`);
    await within(exited, 10_000, 'the watchdog to exit');
    assert.equal(alive(other.pid), true);
  });
});
