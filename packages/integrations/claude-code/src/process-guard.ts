import {
  type ChildProcessByStdio,
  type ChildProcessWithoutNullStreams,
  spawn,
} from 'node:child_process';
import { rm } from 'node:fs/promises';
import { PassThrough, pipeline, type Writable } from 'node:stream';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import type { SpawnedProcess, SpawnOptions } from '@anthropic-ai/claude-agent-sdk';
import {
  type ProcessIdentity,
  type ProcessRecord,
  readProcessIdentity,
  readProcessRecords,
  type StopOutcome,
  stopRecordedProcess,
  writeProcessRecords,
} from './process-record.ts';

const WATCHDOG = fileURLToPath(new URL('./watchdog.ts', import.meta.url));

/** How long `close()` waits for launched processes. The SDK kills one 7 s after its query closes. */
const CLOSE_TIMEOUT_MS = 15_000;

/** A process that an earlier run recorded, and what stopping it found. */
export interface StaleProcess {
  readonly pid: number;
  readonly outcome: StopOutcome;
}

type Watchdog = ChildProcessByStdio<Writable, null, null>;
type ExitListener = (code: number | null, signal: NodeJS.Signals | null) => void;
type ErrorListener = (error: Error) => void;

/**
 * Launches the Claude Code processes the Agent SDK asks for, and makes sure none outlives the
 * process hosting the adapter. A launched process receives no input until it is written to the
 * record file and handed to the watchdog; when either fails, it is killed and its query fails. The
 * record file lists the live processes, so a later start can stop any that survived.
 */
export class ProcessGuard {
  readonly #file: string;
  /** Launched processes that are recorded and watched, by pid. */
  readonly #recorded = new Map<number, ProcessRecord>();
  /** One promise per launched process, settled when it exits. */
  readonly #running = new Set<Promise<void>>();
  readonly #watchdogExits = new Set<Promise<void>>();
  #watchdog: Watchdog | null = null;
  #publishing: Promise<void> = Promise.resolve();
  #stale: Promise<readonly StaleProcess[]> | null = null;
  #staleHandled = false;

  constructor(file: string) {
    this.#file = file;
  }

  get watchdogPid(): number | null {
    return this.#watchdog?.pid ?? null;
  }

  /**
   * Stops the processes an earlier run left in the record file, each only while the process with
   * its pid is still that process, then removes the record. Once that has succeeded there is
   * nothing left to stop, and later calls return nothing.
   */
  stopStale(): Promise<readonly StaleProcess[]> {
    if (this.#staleHandled) return Promise.resolve([]);
    this.#stale ??= this.#stopStale().then(
      (stopped) => {
        this.#staleHandled = true;
        this.#stale = null;
        return stopped;
      },
      (error: unknown) => {
        this.#stale = null;
        throw error;
      },
    );
    return this.#stale;
  }

  /** The SDK's `spawnClaudeCodeProcess`: launches Claude Code exactly as the SDK itself would. */
  spawn(options: SpawnOptions, sessionId: string): SpawnedProcess {
    // The same call as the SDK's own spawn (ProcessTransport.spawnLocalProcess in 0.3.283).
    const child = spawn(options.command, options.args, {
      cwd: options.cwd,
      env: options.env,
      signal: options.signal,
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true,
    });
    // The SDK reads stderr only from processes it spawns itself. Draining it keeps Claude Code
    // from blocking on a full pipe.
    child.stderr.on('error', () => undefined);
    child.stderr.resume();
    child.stdin.on('error', () => undefined);
    const input = new PassThrough();
    const pid = child.pid;
    // Without a pid nothing started; the SDK reports the spawn error.
    if (pid !== undefined) {
      let exited = false;
      const exit = new Promise<void>((resolve) => child.once('exit', () => resolve()));
      this.#running.add(exit);
      void exit.then(() => {
        exited = true;
        this.#running.delete(exit);
        if (this.#recorded.delete(pid)) this.#publish().catch(() => undefined);
      });
      this.#admit(pid, sessionId, () => exited).then(
        (admitted) => {
          if (admitted) pipeline(input, child.stdin, () => undefined);
        },
        (error: unknown) => {
          // Reported the way the SDK reports a failed launch. The process has received no input.
          if (child.listenerCount('error') > 0) {
            child.emit(
              'error',
              new Error(
                `Claude Code was stopped before it received any input because it could not be recorded and watched: ${error instanceof Error ? error.message : String(error)}`,
              ),
            );
          }
          child.kill('SIGKILL');
        },
      );
    }
    return launched(child, input);
  }

  /**
   * Resolves once every launched process has exited, the record file is removed and the watchdog
   * has stopped. The SDK stops each process after its query closes; one that is still running
   * when the wait ends keeps its record and its watchdog.
   */
  async close(): Promise<void> {
    await settlesWithin(Promise.all(this.#running), CLOSE_TIMEOUT_MS);
    await this.#publishing;
    await settlesWithin(Promise.all(this.#watchdogExits), 5000);
  }

  async #stopStale(): Promise<readonly StaleProcess[]> {
    const records = await readProcessRecords(this.#file);
    const stopped = await Promise.all(
      records.map(async (record) => ({
        pid: record.pid,
        outcome: await stopRecordedProcess(record),
      })),
    );
    await rm(this.#file, { force: true });
    return stopped;
  }

  /** Records a launched process and has the watchdog watch it. False when it exited first. */
  async #admit(pid: number, sessionId: string, exited: () => boolean): Promise<boolean> {
    // The record an earlier run left is dealt with before this guard writes its own.
    await this.stopStale();
    const identity = await identify(pid, sessionId, exited);
    if (identity === null || exited()) return false;
    this.#recorded.set(pid, { pid, ...identity, sessionId });
    await this.#publish();
    return !exited();
  }

  /** Brings the record file and the watchdog up to date, one update at a time. */
  #publish(): Promise<void> {
    const update = this.#publishing.then(() => this.#sync());
    this.#publishing = update.catch(() => undefined);
    return update;
  }

  async #sync(): Promise<void> {
    const records = [...this.#recorded.values()];
    if (records.length === 0) {
      this.#retireWatchdog();
      await rm(this.#file, { force: true });
      return;
    }
    await writeProcessRecords(this.#file, records);
    const watchdog = this.#watchdog ?? this.#startWatchdog();
    // Resolves once the list is in the pipe, where the watchdog reads it even if this process dies.
    await new Promise<void>((resolve, reject) =>
      watchdog.stdin.write(`${JSON.stringify({ processes: records })}\n`, (error) =>
        error ? reject(error) : resolve(),
      ),
    );
  }

  #startWatchdog(): Watchdog {
    // Detached, so it outlives this process's group; its stdin is its only link to this process.
    const watchdog = spawn(process.execPath, [WATCHDOG], {
      detached: true,
      stdio: ['pipe', 'ignore', 'ignore'],
      env: {},
    });
    watchdog.on('error', () => undefined);
    watchdog.stdin.on('error', () => undefined);
    if (watchdog.pid === undefined) throw new Error('the watchdog process could not be started');
    const exit: Promise<void> = new Promise((resolve) =>
      watchdog.once('exit', () => {
        if (this.#watchdog === watchdog) this.#watchdog = null;
        this.#watchdogExits.delete(exit);
        resolve();
      }),
    );
    this.#watchdogExits.add(exit);
    this.#watchdog = watchdog;
    return watchdog;
  }

  /** Ends the watchdog with an empty list: nothing is left for it to stop. */
  #retireWatchdog(): void {
    const watchdog = this.#watchdog;
    this.#watchdog = null;
    watchdog?.stdin.end(`${JSON.stringify({ processes: [] })}\n`);
  }
}

/**
 * Reads a launched process's identity once `ps` shows it carrying its session id, which proves
 * it is the launched program. Null when the process exits first.
 */
async function identify(
  pid: number,
  sessionId: string,
  exited: () => boolean,
): Promise<ProcessIdentity | null> {
  const argument = `--session-id=${sessionId}`;
  for (let attempt = 0; attempt < 100; attempt += 1) {
    if (exited()) return null;
    const identity = await readProcessIdentity(pid);
    if (identity === null) return null;
    if (identity.command.split(' ').includes(argument)) return identity;
    await delay(20);
  }
  throw new Error(`ps never showed process ${pid} with its session id`);
}

/** The launched process as the SDK sees it. Its input goes through `stdin`, held back until it is watched. */
function launched(child: ChildProcessWithoutNullStreams, stdin: Writable): SpawnedProcess {
  return {
    stdin,
    stdout: child.stdout,
    get killed() {
      return child.killed;
    },
    get exitCode() {
      return child.exitCode;
    },
    get signalCode() {
      return child.signalCode;
    },
    kill: (signal) => child.kill(signal),
    on: (event: 'exit' | 'error', listener: ExitListener | ErrorListener) => {
      child.on(event, listener);
    },
    once: (event: 'exit' | 'error', listener: ExitListener | ErrorListener) => {
      child.once(event, listener);
    },
    off: (event: 'exit' | 'error', listener: ExitListener | ErrorListener) => {
      child.off(event, listener);
    },
  };
}

/** Waits for a promise to settle, for at most `ms`. Resolves whether it settled in time. */
async function settlesWithin(promise: Promise<unknown>, ms: number): Promise<boolean> {
  const timer = new AbortController();
  try {
    return await Promise.race([
      promise.then(() => true),
      delay(ms, false, { signal: timer.signal }).catch(() => false),
    ]);
  } finally {
    timer.abort();
  }
}
