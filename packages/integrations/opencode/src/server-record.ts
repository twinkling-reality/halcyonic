import { execFile } from 'node:child_process';
import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

/**
 * What identifies an OpenCode server the adapter launched. It is written to a file while the
 * server runs, so that after a crash the next start can stop a server that outlived its adapter.
 * It never contains the server password.
 */
export interface ServerRecord {
  readonly pid: number;
  readonly port: number;
  readonly binaryPath: string;
  /** The command line `ps` reported for the process after launch. */
  readonly command: string;
  /** The start time `ps` reported for the process, in UTC. */
  readonly startedAt: string;
}

export interface ProcessIdentity {
  readonly groupId: number;
  readonly startedAt: string;
  readonly command: string;
}

/** A fixed zone and locale keep the start time `ps` prints comparable across runs. */
const PS_ENV = { PATH: '/bin:/usr/bin:/sbin:/usr/sbin', TZ: 'UTC', LC_ALL: 'C' };

/**
 * Far more than Node's default limit on a command's output, 1 MiB, which one command line can
 * exceed: it can take up to ARG_MAX (1 MiB on macOS), and `ps` prints a byte beyond ASCII as three
 * or four characters. A recorded pid can belong to any process by the time it is read.
 */
const PS_MAX_OUTPUT = 64 * 1024 * 1024;

/**
 * Reads a process's group, start time and command line in one `ps` call, so the three describe
 * the same process. Resolves null when no process has the pid; rejects when `ps` cannot be used.
 */
export function readProcessIdentity(pid: number): Promise<ProcessIdentity | null> {
  return new Promise((resolve, reject) => {
    execFile(
      'ps',
      ['-p', String(pid), '-o', 'pgid=,lstart=,args='],
      { env: PS_ENV, timeout: 5000, maxBuffer: PS_MAX_OUTPUT },
      (error, stdout) => {
        const output = stdout.trim();
        if (error !== null) {
          // ps runs and exits 1, printing nothing, when there is no such process.
          if (typeof error.code === 'number' && output === '') resolve(null);
          else reject(new Error(`ps could not read process ${pid}: ${error.message}`));
          return;
        }
        const fields = output.split(/\s+/);
        const groupId = Number(fields[0]);
        if (fields.length < 7 || !Number.isSafeInteger(groupId)) {
          reject(new Error(`ps printed an unexpected line for process ${pid}`));
          return;
        }
        // lstart is five fields, for example "Sat Sep 26 23:13:45 2026".
        resolve({
          groupId,
          startedAt: fields.slice(1, 6).join(' '),
          command: fields.slice(6).join(' '),
        });
      },
    );
  });
}

/** True when a live process is the recorded server: same start time, same command, that binary. */
export function isRecordedProcess(identity: ProcessIdentity, record: ServerRecord): boolean {
  const binary = record.binaryPath.trim().split(/\s+/).join(' ');
  return (
    identity.startedAt === record.startedAt &&
    identity.command === record.command &&
    identity.command.startsWith(`${binary} `)
  );
}

export type StopOutcome = 'not_running' | 'not_ours' | 'stopped';

/**
 * Stops a recorded server, and the process group it leads, while the process with its pid is
 * still that server. A process that merely reuses the pid is never signalled: the identity is
 * checked before each signal.
 */
export async function stopRecordedProcess(
  record: ServerRecord,
  graceMs = 5000,
): Promise<StopOutcome> {
  const identity = await readProcessIdentity(record.pid);
  if (identity === null) return 'not_running';
  if (!isRecordedProcess(identity, record)) return 'not_ours';
  const target = identity.groupId === record.pid ? -record.pid : record.pid;
  signal(target, 'SIGTERM');
  if (await goneWithin(record, graceMs)) return 'stopped';
  const current = await readProcessIdentity(record.pid);
  if (current !== null && isRecordedProcess(current, record)) signal(target, 'SIGKILL');
  if (await goneWithin(record, 2000)) return 'stopped';
  throw new Error(`OpenCode server process ${record.pid} is still running after SIGKILL.`);
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
  const { pid, port, binaryPath, command, startedAt } = value as Record<string, unknown>;
  if (
    !Number.isSafeInteger(pid) ||
    (pid as number) <= 0 ||
    !Number.isSafeInteger(port) ||
    typeof binaryPath !== 'string' ||
    typeof command !== 'string' ||
    typeof startedAt !== 'string'
  ) {
    return null;
  }
  return { pid: pid as number, port: port as number, binaryPath, command, startedAt };
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
