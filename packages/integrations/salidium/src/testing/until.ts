import { setTimeout as sleep } from 'node:timers/promises';

/**
 * Waits until `check` holds, polling, and fails with `what` if it does not within `timeoutMs`. The
 * default is generous, so a loaded machine only makes a test slower: it is how long a failure
 * takes to be reported, not a bound under test.
 */
export async function until(check: () => boolean, what: string, timeoutMs = 30_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!check()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await sleep(5);
  }
}
