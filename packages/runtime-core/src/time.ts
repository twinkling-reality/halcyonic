import { setTimeout as delay } from 'node:timers/promises';

export interface Clock {
  now(): Date;
}

export interface Scheduler {
  /** Resolves after `ms`, or rejects with an `AbortError` when `signal` aborts first. */
  sleep(ms: number, signal?: AbortSignal): Promise<void>;
}

export const systemClock: Clock = { now: () => new Date() };

export const systemScheduler: Scheduler = {
  sleep: (ms, signal) => delay(ms, undefined, signal === undefined ? undefined : { signal }),
};

/**
 * A clock and scheduler driven by the caller instead of wall time. Timers fire in time order and,
 * at equal times, in the order they were created, so a run is reproducible: the same inputs
 * produce the same event order and timestamps. Used by tests and fixture recording.
 */
export interface VirtualTime extends Clock, Scheduler {
  /** Fires every timer due within `ms`, letting async work settle after each, then sets the time. */
  advance(ms: number): Promise<void>;
  /** Fires timers until none remain. Throws after `maxSteps` to catch runaway loops. */
  runUntilIdle(options?: { maxSteps?: number }): Promise<void>;
  /** Number of timers still waiting. */
  pending(): number;
}

interface Timer {
  readonly at: number;
  readonly order: number;
  readonly resolve: () => void;
  readonly cleanup: () => void;
}

export function createVirtualTime(start: Date): VirtualTime {
  let now = start.getTime();
  let created = 0;
  const timers: Timer[] = [];

  const remove = (timer: Timer) => {
    const index = timers.indexOf(timer);
    if (index >= 0) timers.splice(index, 1);
  };

  // Promise continuations are microtasks, which Node drains completely before the next
  // macrotask, so yielding to setImmediate lets every chain waiting on fired timers run.
  const settle = async () => {
    for (let i = 0; i < 3; i += 1) {
      await new Promise<void>((resolve) => setImmediate(resolve));
    }
  };

  const nextDue = (limit: number): Timer | undefined => {
    timers.sort((a, b) => a.at - b.at || a.order - b.order);
    const next = timers[0];
    return next !== undefined && next.at <= limit ? next : undefined;
  };

  const fire = async (timer: Timer) => {
    remove(timer);
    now = Math.max(now, timer.at);
    timer.cleanup();
    timer.resolve();
    await settle();
  };

  return {
    now: () => new Date(now),
    sleep(ms, signal) {
      if (signal?.aborted) return Promise.reject(abortError());
      return new Promise<void>((resolve, reject) => {
        const onAbort = () => {
          remove(timer);
          reject(abortError());
        };
        const timer: Timer = {
          at: now + Math.max(0, ms),
          order: created++,
          resolve,
          cleanup: () => signal?.removeEventListener('abort', onAbort),
        };
        signal?.addEventListener('abort', onAbort, { once: true });
        timers.push(timer);
      });
    },
    async advance(ms) {
      const target = now + ms;
      await settle();
      for (let timer = nextDue(target); timer !== undefined; timer = nextDue(target)) {
        await fire(timer);
      }
      now = target;
      await settle();
    },
    async runUntilIdle({ maxSteps = 100_000 } = {}) {
      await settle();
      for (let step = 0; ; step += 1) {
        const timer = nextDue(Number.POSITIVE_INFINITY);
        if (timer === undefined) return;
        if (step >= maxSteps) throw new Error(`virtual time still busy after ${maxSteps} timers`);
        await fire(timer);
      }
    },
    pending: () => timers.length,
  };
}

function abortError(): Error {
  const error = new Error('The operation was aborted');
  error.name = 'AbortError';
  return error;
}

export function isAbortError(error: unknown): boolean {
  return error instanceof Error && error.name === 'AbortError';
}
