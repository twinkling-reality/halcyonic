import { type ChildProcessByStdio, spawn } from 'node:child_process';
import { rm } from 'node:fs/promises';
import type { Writable } from 'node:stream';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import {
  type ProcessRecord,
  readProcessIdentity,
  readProcessRecords,
  type StopOutcome,
  stopRecordedProcess,
  writeProcessRecords,
} from './process-record.ts';

const WATCHDOG = fileURLToPath(new URL('./watchdog.ts', import.meta.url));

/** A process that an earlier run recorded, and what stopping it found. */
export interface StaleProcess {
  readonly pid: number;
  readonly outcome: StopOutcome;
}

type Watchdog = ChildProcessByStdio<Writable, null, null>;

/**
 * Makes sure no ACP agent app outlives the process hosting the adapter (AUDIT3 AS1): each launched
 * app is written to the record file and handed to a detached watchdog once it has answered
 * `initialize`, before it is given a folder or a prompt; when either fails, it is stopped and its
 * start refused. The record file lists the live apps, so a later start stops any that survived.
 */
export class AcpProcessGuard {
  readonly #file: string;
  readonly #recorded = new Map<number, ProcessRecord>();
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
   * Stops the apps an earlier run left in the record file, each only while the process with its pid
   * is still that process, then removes the record. Once that has succeeded, later calls return nothing.
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

  /**
   * Records a launched app and has the watchdog watch it, its identity read once it answers. False when
   * it exited first; rejects when it cannot be recorded or watched.
   */
  async admit(pid: number, exited: () => boolean): Promise<boolean> {
    await this.stopStale();
    if (exited()) return false;
    const identity = await readProcessIdentity(pid);
    if (identity === null || exited()) return false;
    this.#recorded.set(pid, { pid, ...identity });
    await this.#publish();
    return !exited();
  }

  /** The app with this pid has exited: it leaves the record. */
  release(pid: number): void {
    if (this.#recorded.delete(pid)) this.#publish().catch(() => undefined);
  }

  /** Resolves once the record is up to date and, with nothing left in it, the watchdog has stopped. */
  async close(): Promise<void> {
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
