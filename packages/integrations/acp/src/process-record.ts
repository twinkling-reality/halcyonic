import { execFile } from 'node:child_process';
import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

/**
 * An ACP agent app process the adapter launched, recorded while it runs so that it can still be
 * stopped when the adapter cannot stop it: by the watchdog when the adapter's process dies, or by
 * the next start when both died (AUDIT3 AS1). It holds no secrets: a pid, a start time and the
 * command line `ps` shows, which names the app and its settings home, never a key.
 */
export interface ProcessRecord {
  readonly pid: number;
  /** The start time `ps` reported for the process, in UTC. */
  readonly startedAt: string;
  /** The command line `ps` reported for the process once it answered `initialize`. */
  readonly command: string;
}

export interface ProcessIdentity {
  readonly startedAt: string;
  readonly command: string;
}

export type StopOutcome = 'not_running' | 'not_ours' | 'stopped';

/** A fixed zone and locale keep what `ps` prints comparable across runs. */
const PS_ENV = { PATH: '/bin:/usr/bin:/sbin:/usr/sbin', TZ: 'UTC', LC_ALL: 'C' };

/**
 * Far more than Node's default limit on a command's output, 1 MiB, which one command line can
 * exceed: it can take up to ARG_MAX (1 MiB on macOS), and `ps` prints a byte beyond ASCII as three
 * or four characters. A recorded pid can belong to any process by the time it is read.
 */
const PS_MAX_OUTPUT = 64 * 1024 * 1024;

/**
 * Reads a process's start time and command line in one `ps` call, so both describe the same
 * process. Resolves null when no process has the pid; rejects when `ps` cannot be used.
 */
export function readProcessIdentity(pid: number): Promise<ProcessIdentity | null> {
  return new Promise((resolve, reject) => {
    execFile(
      'ps',
      ['-ww', '-p', String(pid), '-o', 'lstart=,args='],
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
        if (fields.length < 6) {
          reject(new Error(`ps printed an unexpected line for process ${pid}`));
          return;
        }
        // lstart is five fields, for example "Sat Sep 26 23:13:45 2026".
        resolve({ startedAt: fields.slice(0, 5).join(' '), command: fields.slice(5).join(' ') });
      },
    );
  });
}

/**
 * True when a live process is the recorded one: the same start time and the same command line. A
 * Kiro launch's command line names no launch of its own, so the start time to the second and the
 * whole command together tell it from any later process that reuses the pid.
 */
export function isRecordedProcess(identity: ProcessIdentity, record: ProcessRecord): boolean {
  return identity.startedAt === record.startedAt && identity.command === record.command;
}

/**
 * Stops a recorded process while the process with its pid is still that process: SIGTERM, then
 * SIGKILL after `graceMs`. A process that merely reuses the pid is never signalled: the identity
 * is checked before each signal. Only the pid is signalled, never a process group: the app runs in
 * the group of the process that launched it.
 */
export async function stopRecordedProcess(
  record: ProcessRecord,
  graceMs = 5000,
): Promise<StopOutcome> {
  const identity = await readProcessIdentity(record.pid);
  if (identity === null) return 'not_running';
  if (!isRecordedProcess(identity, record)) return 'not_ours';
  // A recorded app leads its own process group (AUDIT3 SW-g): the group is signalled, its commands with it.
  const group = (await leadsGroup(record.pid)) ? -record.pid : record.pid;
  signal(group, 'SIGTERM');
  if (await goneWithin(record, graceMs)) return 'stopped';
  const current = await readProcessIdentity(record.pid);
  if (current !== null && isRecordedProcess(current, record)) signal(group, 'SIGKILL');
  if (await goneWithin(record, 2000)) return 'stopped';
  throw new Error(`The agent app process ${record.pid} is still running after SIGKILL.`);
}

async function goneWithin(record: ProcessRecord, ms: number): Promise<boolean> {
  const deadline = Date.now() + ms;
  for (;;) {
    const identity = await readProcessIdentity(record.pid);
    if (identity === null || !isRecordedProcess(identity, record)) return true;
    if (Date.now() >= deadline) return false;
    await delay(100);
  }
}

/** Whether the process leads its own process group, as `ps` reports its group id. */
function leadsGroup(pid: number): Promise<boolean> {
  return new Promise((resolve) => {
    execFile(
      'ps',
      ['-p', String(pid), '-o', 'pgid='],
      { env: PS_ENV, timeout: 5000 },
      (error, stdout) => {
        resolve(error === null && Number(stdout.trim()) === pid);
      },
    );
  });
}

function signal(pid: number, name: NodeJS.Signals): void {
  try {
    process.kill(pid, name);
  } catch {
    // Already gone. Whether it stopped is checked afterwards.
  }
}

/** Parses a record file, or one line the watchdog receives. Null when it cannot be understood. */
export function parseProcessRecords(text: string): ProcessRecord[] | null {
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    return null;
  }
  const processes: unknown =
    typeof value === 'object' && value !== null ? Reflect.get(value, 'processes') : undefined;
  if (!Array.isArray(processes)) return null;
  const records: ProcessRecord[] = [];
  for (const entry of processes) {
    if (typeof entry !== 'object' || entry === null) return null;
    const { pid, startedAt, command } = entry as Record<string, unknown>;
    if (
      !Number.isSafeInteger(pid) ||
      (pid as number) <= 0 ||
      typeof startedAt !== 'string' ||
      typeof command !== 'string' ||
      command === ''
    ) {
      return null;
    }
    records.push({ pid: pid as number, startedAt, command });
  }
  return records;
}

/** Reads the record file. Empty when there is none or it cannot be understood. */
export async function readProcessRecords(file: string): Promise<ProcessRecord[]> {
  try {
    return parseProcessRecords(await readFile(file, 'utf8')) ?? [];
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return [];
    throw error;
  }
}

/** Replaces the record file atomically, readable by its owner only. */
export async function writeProcessRecords(
  file: string,
  records: readonly ProcessRecord[],
): Promise<void> {
  await mkdir(dirname(file), { recursive: true, mode: 0o700 });
  const temporary = `${file}.${process.pid}.tmp`;
  await writeFile(temporary, `${JSON.stringify({ processes: records }, null, 2)}\n`, {
    mode: 0o600,
  });
  await rename(temporary, file);
}
