import { execFile } from 'node:child_process';
import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

/**
 * What identifies a Codex app-server the adapter launched. It is written to a file while the
 * server runs, so that after a crash the next start can stop a server that outlived its adapter.
 */
export interface ServerRecord {
  readonly pid: number;
  readonly binaryPath: string;
  /** The command line `ps` reported for the process after launch. */
  readonly command: string;
  /** The start time `ps` reported for the process, in UTC. */
  readonly startedAt: string;
}

export interface ProcessIdentity {
  readonly pid: number;
  readonly groupId: number;
  readonly startedAt: string;
  readonly command: string;
}

/** A fixed zone and locale keep the start time `ps` prints comparable across runs. */
const PS_ENV = { PATH: '/bin:/usr/bin:/sbin:/usr/sbin', TZ: 'UTC', LC_ALL: 'C' };

/**
 * Far more than Node's default limit on a command's output, 1 MiB, which one command line can
 * exceed: it can take up to ARG_MAX (1 MiB on macOS), and `ps` prints a byte beyond ASCII as three
 * or four characters. Command lines are read only for the processes asked about, never for every
 * process running, so the output stays well below this.
 */
const PS_MAX_OUTPUT = 64 * 1024 * 1024;

function ps(args: readonly string[]): Promise<string | null> {
  return new Promise((resolve, reject) => {
    execFile(
      'ps',
      ['-ww', ...args],
      { env: PS_ENV, timeout: 5000, maxBuffer: PS_MAX_OUTPUT },
      (error, stdout) => {
        const output = stdout.trim();
        if (error === null) resolve(output);
        // ps runs and exits 1, printing nothing, when no process matches.
        else if (typeof error.code === 'number' && output === '') resolve(null);
        else reject(new Error(`ps failed: ${error.message}`));
      },
    );
  });
}

/** Parses `pgid lstart args`, where lstart is five fields such as "Sat Sep 26 23:13:45 2026". */
function parseIdentity(pid: number, fields: readonly string[]): ProcessIdentity | null {
  const groupId = Number(fields[0]);
  if (fields.length < 7 || !Number.isSafeInteger(groupId)) return null;
  return {
    pid,
    groupId,
    startedAt: fields.slice(1, 6).join(' '),
    command: fields.slice(6).join(' '),
  };
}

/**
 * Reads a process's group, start time and command line in one `ps` call, so the three describe
 * the same process. Resolves null when no process has the pid; rejects when `ps` cannot be used.
 */
export async function readProcessIdentity(pid: number): Promise<ProcessIdentity | null> {
  const output = await ps(['-p', String(pid), '-o', 'pgid=,lstart=,args=']);
  if (output === null) return null;
  const identity = parseIdentity(pid, output.split(/\s+/));
  if (identity === null) throw new Error(`ps printed an unexpected line for process ${pid}`);
  return identity;
}

/**
 * The live descendants of a process. Codex starts each command in a session of its own, so a
 * signal to the server's process group does not reach them; they are found through their parents
 * instead. Every process is listed with its parent only, which stays small however long the
 * command lines on the machine are; identities are then read for the processes found that way,
 * and each counts only if that read still shows its parents leading to `pid`, so a pid reused in
 * between is never taken for a descendant.
 */
export async function readDescendants(pid: number): Promise<ProcessIdentity[]> {
  const links: Link[] = [];
  for (const line of ((await ps(['-A', '-o', 'pid=,ppid='])) ?? '').split('\n')) {
    const fields = line.trim().split(/\s+/);
    const child = Number(fields[0]);
    const parent = Number(fields[1]);
    if (Number.isSafeInteger(child) && Number.isSafeInteger(parent)) {
      links.push({ pid: child, parent });
    }
  }
  const candidates = below(pid, links).map((link) => link.pid);
  if (candidates.length === 0) return [];
  const output = await ps(['-p', candidates.join(','), '-o', 'pid=,ppid=,pgid=,lstart=,args=']);
  const rows: (Link & { readonly identity: ProcessIdentity })[] = [];
  for (const line of (output ?? '').split('\n')) {
    const fields = line.trim().split(/\s+/);
    const identity = parseIdentity(Number(fields[0]), fields.slice(2));
    if (identity !== null && Number.isSafeInteger(identity.pid)) {
      rows.push({ pid: identity.pid, parent: Number(fields[1]), identity });
    }
  }
  return below(pid, rows).map((row) => row.identity);
}

interface Link {
  readonly pid: number;
  readonly parent: number;
}

/** The links below `root`, followed through their parents, in the order they were found. */
function below<T extends Link>(root: number, links: readonly T[]): T[] {
  const found = new Set([root]);
  const result: T[] = [];
  for (let grew = true; grew; ) {
    grew = false;
    for (const link of links) {
      if (found.has(link.parent) && !found.has(link.pid)) {
        found.add(link.pid);
        result.push(link);
        grew = true;
      }
    }
  }
  return result;
}

/** True when a live process is the recorded server: same start time, same command, that binary. */
export function isRecordedProcess(identity: ProcessIdentity, record: ServerRecord): boolean {
  return (
    identity.startedAt === record.startedAt &&
    identity.command === record.command &&
    runsAppServer(identity.command, record.binaryPath)
  );
}

/**
 * True when a command line, as `ps` prints it, runs `<binary> app-server`, with or without the
 * settings after it that the adapter passes since 2026-10-07 (a server an earlier build launched
 * has none). A script shows its interpreter first, which the stand-in binary of the unit tests
 * needs.
 */
export function runsAppServer(command: string, binaryPath: string): boolean {
  const expected = `${binaryPath.trim().split(/\s+/).join(' ')} app-server`;
  const runs = (text: string) => text === expected || text.startsWith(`${expected} `);
  const after = command.indexOf(` ${expected}`);
  return runs(command) || (after >= 0 && runs(command.slice(after + 1)));
}

/**
 * Kills a process group with SIGKILL, then the descendants found before that were outside it:
 * each process group such a descendant leads, or else the descendant itself, only while the
 * process with its pid is still that descendant. They are the commands, in sessions of their own.
 */
export async function killWithDescendants(
  groupId: number,
  descendants: readonly ProcessIdentity[],
): Promise<void> {
  signal(-groupId, 'SIGKILL');
  for (const descendant of descendants) {
    if (descendant.groupId === groupId) continue;
    const current = await readProcessIdentity(descendant.pid).catch(() => null);
    if (current === null || current.startedAt !== descendant.startedAt) continue;
    signal(current.groupId === descendant.pid ? -descendant.pid : descendant.pid, 'SIGKILL');
  }
}

export type StopOutcome = 'not_running' | 'not_ours' | 'stopped';

/**
 * Stops a recorded server, the process group it leads and its commands, while the process with
 * its pid is still that server: SIGTERM to the group, which Codex treats like the end of its
 * input and shuts down cleanly, then after `graceMs` SIGKILL to the group and to the descendants
 * it still has. A process that merely reuses the pid is never signalled.
 */
export async function stopRecordedProcess(
  record: ServerRecord,
  graceMs = 10_000,
): Promise<StopOutcome> {
  const identity = await readProcessIdentity(record.pid);
  if (identity === null) return 'not_running';
  if (!isRecordedProcess(identity, record)) return 'not_ours';
  const group = identity.groupId === record.pid;
  signal(group ? -record.pid : record.pid, 'SIGTERM');
  if (await goneWithin(record, graceMs)) return 'stopped';
  const current = await readProcessIdentity(record.pid);
  if (current !== null && isRecordedProcess(current, record)) {
    const descendants = await readDescendants(record.pid);
    if (group) await killWithDescendants(record.pid, descendants);
    else signal(record.pid, 'SIGKILL');
  }
  if (await goneWithin(record, 2000)) return 'stopped';
  throw new Error(`Codex app-server process ${record.pid} is still running after SIGKILL.`);
}

async function goneWithin(record: ServerRecord, ms: number): Promise<boolean> {
  const deadline = Date.now() + ms;
  for (;;) {
    const identity = await readProcessIdentity(record.pid);
    if (identity === null || !isRecordedProcess(identity, record)) return true;
    if (Date.now() >= deadline) return false;
    await delay(100);
  }
}

function signal(target: number, name: NodeJS.Signals): void {
  try {
    process.kill(target, name);
  } catch {
    // Already gone. Whether it stopped is checked afterwards.
  }
}

export function parseServerRecord(text: string): ServerRecord | null {
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    return null;
  }
  if (typeof value !== 'object' || value === null) return null;
  const { pid, binaryPath, command, startedAt } = value as Record<string, unknown>;
  if (
    !Number.isSafeInteger(pid) ||
    (pid as number) <= 0 ||
    typeof binaryPath !== 'string' ||
    typeof command !== 'string' ||
    typeof startedAt !== 'string'
  ) {
    return null;
  }
  return { pid: pid as number, binaryPath, command, startedAt };
}

/** Reads the record file. Null when there is none or it cannot be understood. */
export async function readServerRecord(file: string): Promise<ServerRecord | null> {
  try {
    return parseServerRecord(await readFile(file, 'utf8'));
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return null;
    throw error;
  }
}

/** Replaces the record file atomically, readable by its owner only. */
export async function writeServerRecord(file: string, record: ServerRecord): Promise<void> {
  await mkdir(dirname(file), { recursive: true, mode: 0o700 });
  const temporary = `${file}.${process.pid}.tmp`;
  await writeFile(temporary, `${JSON.stringify(record, null, 2)}\n`, { mode: 0o600 });
  await rename(temporary, file);
}

/** Removes the record file unless it now describes a different process. */
export async function removeServerRecord(file: string, pid: number): Promise<void> {
  const record = await readServerRecord(file).catch(() => null);
  if (record !== null && record.pid !== pid) return;
  await rm(file, { force: true });
}
