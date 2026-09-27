/** Seorak budgets each integration credential per minute. */
export const WINDOW_MS = 60_000;

/**
 * This client's side of Seorak's per-credential request budget: at most `limit` requests start in
 * any rolling minute, which never overdraws a token bucket that refills `limit` a minute from
 * `limit` tokens, and none start before a time Seorak named in a 429. Seorak may also allow a
 * smaller burst; its 429 covers that.
 *
 * An evaluation reserves every request it may make before it makes the first, so it never stops
 * halfway for want of budget, and releases the ones it did not need. Times are milliseconds on one
 * clock, passed in so the rules can be tested without waiting.
 */
export class RequestBudget {
  readonly #limit: number;
  /** When each request of the last minute started, oldest first. */
  readonly #started: number[] = [];
  #reserved = 0;
  #pausedUntil = 0;

  constructor(limit: number) {
    this.#limit = limit;
  }

  /** Reserves `count` requests. Returns null when they are reserved, else the earliest time to retry. */
  reserve(count: number, now: number): number | null {
    if (now < this.#pausedUntil) return this.#pausedUntil;
    while (this.#started.length > 0 && (this.#started[0] ?? now) <= now - WINDOW_MS)
      this.#started.shift();
    const excess = this.#started.length + this.#reserved + count - this.#limit;
    if (excess <= 0) {
      this.#reserved += count;
      return null;
    }
    // Only started requests age out; reserved ones start within moments and age out a minute later.
    return (this.#started[excess - 1] ?? now) + WINDOW_MS;
  }

  /** One reserved request starts. */
  start(now: number): void {
    this.#reserved--;
    this.#started.push(now);
  }

  /** Reserved requests that will not start. */
  release(count: number): void {
    this.#reserved -= count;
  }

  /** Seorak asked for no request before `until`. */
  pauseUntil(until: number): void {
    this.#pausedUntil = Math.max(this.#pausedUntil, until);
  }
}
