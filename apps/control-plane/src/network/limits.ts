import type { Clock } from '@halcyonic/runtime-core';

/**
 * Counts events per key, such as a remote address, in fixed windows of time, so that one address
 * cannot flood the network listener. Keys whose window has ended are forgotten as it grows.
 */
export class WindowCounter {
  readonly #clock: Clock;
  readonly #limit: number;
  readonly #windowMs: number;
  readonly #windows = new Map<string, { startedAt: number; count: number }>();

  constructor(clock: Clock, limit: number, windowMs: number) {
    this.#clock = clock;
    this.#limit = limit;
    this.#windowMs = windowMs;
  }

  /** Whether the key has used up its window. */
  exhausted(key: string): boolean {
    const window = this.#current(key, this.#clock.now().getTime());
    return window !== undefined && window.count >= this.#limit;
  }

  /** Counts one event for the key. Returns false, counting nothing, once the window is used up. */
  take(key: string): boolean {
    const now = this.#clock.now().getTime();
    const window = this.#current(key, now);
    if (window !== undefined) {
      if (window.count >= this.#limit) return false;
      window.count += 1;
      return true;
    }
    if (this.#windows.size >= 1024) this.#forgetEnded(now);
    this.#windows.set(key, { startedAt: now, count: 1 });
    return true;
  }

  #current(key: string, now: number): { startedAt: number; count: number } | undefined {
    const window = this.#windows.get(key);
    if (window === undefined) return undefined;
    if (now - window.startedAt < this.#windowMs) return window;
    this.#windows.delete(key);
    return undefined;
  }

  #forgetEnded(now: number): void {
    for (const [key, window] of this.#windows) {
      if (now - window.startedAt >= this.#windowMs) this.#windows.delete(key);
    }
  }
}
