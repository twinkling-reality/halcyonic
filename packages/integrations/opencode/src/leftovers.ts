import { execFile } from 'node:child_process';
import { sep } from 'node:path';

/**
 * A confined server's leftover commands (AUDIT3's method, 2026-11-14 00:26Z): what an agent started in the background
 * keeps running after its shell exits, re-parented to launchd and in a process group of its own, so the server's stop
 * never reaches it. They end with the server, by themselves, on every stop path. A process is ended only when ALL of
 * these hold, each read again just before TERM and before KILL (same pid, same start time):
 * 1. it is this user's; 2. its working folder is the server's folder or inside it; 3. it started after the server
 * launched; 4. its parent is launchd (an orphan, left when its shell exited: Halcyonic's own confined preview and play
 * servers are the control plane's children and are never ended by this); 5. its environment cannot be read, which
 * macOS does for a sandboxed process (a terminal, a dev server and most editors a person runs are not sandboxed, and
 * the other four must hold too).
 * Nothing of any process is kept or logged. What escapes, the closed profile still holds: a process that leaves the
 * folder, or one not yet orphaned when the server stops (one more sweep a few seconds later catches that).
 */
export interface LeftoverScope {
  /** The server's folder, as its real path. */
  readonly folder: string;
  /** When the server launched, in ms since the epoch. */
  readonly launchedAtMs: number;
}

/** Runs a system tool and gives its standard output, or null where it failed. */
export type Run = (file: string, args: readonly string[]) => Promise<string | null>;

const run: Run = (file, args) =>
  new Promise((resolve) => {
    execFile(
      file,
      [...args],
      {
        encoding: 'utf8',
        maxBuffer: 8 * 1024 * 1024,
        env: { LC_ALL: 'C', PATH: '/usr/bin:/bin:/usr/sbin' },
      },
      (error, stdout) => resolve(error !== null && stdout === '' ? null : stdout),
    );
  });

const LSOF = '/usr/sbin/lsof';
const PS = '/bin/ps';
/** A start time read to the second: one a second before the launch still counts as after it. */
const START_SLACK_MS = 1000;
/** Between TERM and KILL, and before the second sweep. */
const GRACE_MS = 5000;

/** This user's processes whose working folder is `folder` or inside it, by pid. */
async function inFolder(folder: string, exec: Run): Promise<number[]> {
  const uid = process.getuid?.();
  if (uid === undefined) return [];
  const out = await exec(LSOF, ['-a', '-d', 'cwd', '-u', String(uid), '-Fpn']);
  if (out === null) return [];
  const found: number[] = [];
  let pid: number | null = null;
  for (const line of out.split('\n')) {
    if (line.startsWith('p')) pid = Number(line.slice(1));
    else if (line.startsWith('n') && pid !== null) {
      const path = line.slice(1);
      if (path === folder || path.startsWith(folder.endsWith(sep) ? folder : `${folder}${sep}`))
        found.push(pid);
    }
  }
  return found.filter((each) => Number.isInteger(each) && each > 1 && each !== process.pid);
}

/** One field of `pid` from ps, trimmed, or null where it is gone or cannot be read. */
async function field(pid: number, name: string, exec: Run): Promise<string | null> {
  const out = await exec(PS, ['-o', `${name}=`, '-p', String(pid)]);
  const text = out?.trim() ?? '';
  return text === '' ? null : text;
}

/** When `pid` started, in ms since the epoch, or null. */
async function startedAt(pid: number, exec: Run): Promise<number | null> {
  const text = await field(pid, 'lstart', exec);
  const at = text === null ? Number.NaN : Date.parse(text);
  return Number.isNaN(at) ? null : at;
}

/**
 * Whether `pid`'s environment is hidden from this user, as macOS hides a sandboxed process's: `ps -E` gives its
 * command alone, with nothing appended. Only that one process is asked, and nothing of what ps gives is kept.
 */
async function hidden(pid: number, exec: Run): Promise<boolean> {
  const plain = await exec(PS, ['-ww', '-o', 'command=', '-p', String(pid)]);
  const withEnvironment = await exec(PS, ['-E', '-ww', '-o', 'command=', '-p', String(pid)]);
  if (plain === null || withEnvironment === null || plain.trim() === '') return false;
  return withEnvironment.trim() === plain.trim();
}

/** Whether `pid` is a leftover of the server now: in its folder, started after it, orphaned, its environment hidden. */
async function leftover(
  pid: number,
  scope: LeftoverScope,
  exec: Run,
  folderPids: readonly number[],
): Promise<number | null> {
  if (!folderPids.includes(pid)) return null;
  const start = await startedAt(pid, exec);
  if (start === null || start < scope.launchedAtMs - START_SLACK_MS) return null;
  if ((await field(pid, 'ppid', exec)) !== '1') return null;
  if (!(await hidden(pid, exec))) return null;
  return start;
}

/** One sweep: TERM each leftover, then KILL any still the same leftover after the grace. Gives how many were TERMed. */
async function sweep(
  scope: LeftoverScope,
  exec: Run,
  signal: (pid: number, name: NodeJS.Signals) => void,
  wait: (ms: number) => Promise<void>,
): Promise<number> {
  const found: { pid: number; start: number }[] = [];
  const pids = await inFolder(scope.folder, exec);
  for (const pid of pids) {
    const start = await leftover(pid, scope, exec, pids);
    if (start !== null) found.push({ pid, start });
  }
  const again = async (each: { pid: number; start: number }) =>
    (await leftover(each.pid, scope, exec, await inFolder(scope.folder, exec))) === each.start;
  let ended = 0;
  for (const each of found) {
    if (!(await again(each))) continue;
    try {
      signal(each.pid, 'SIGTERM');
      ended++;
    } catch {
      // Gone already.
    }
  }
  if (ended === 0) return 0;
  await wait(GRACE_MS);
  for (const each of found) {
    if (!(await again(each))) continue;
    try {
      signal(each.pid, 'SIGKILL');
    } catch {
      // Gone between the check and the signal.
    }
  }
  return ended;
}

/**
 * Ends the server's leftover commands, after the server: one sweep now, and one more after a few seconds for a
 * command whose shell had not yet exited when the server stopped. Gives how many were ended (a count, for the log).
 */
export async function endLeftovers(
  scope: LeftoverScope,
  options: {
    readonly exec?: Run;
    readonly signal?: (pid: number, signal: NodeJS.Signals) => void;
    readonly wait?: (ms: number) => Promise<void>;
  } = {},
): Promise<number> {
  const exec = options.exec ?? run;
  const signal = options.signal ?? ((pid, name) => process.kill(pid, name));
  const wait = options.wait ?? ((ms) => new Promise<void>((resolve) => setTimeout(resolve, ms)));
  const first = await sweep(scope, exec, signal, wait);
  await wait(GRACE_MS);
  return first + (await sweep(scope, exec, signal, wait));
}
