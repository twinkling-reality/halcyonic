import { setTimeout as sleep } from 'node:timers/promises';

/** Waits until `check` holds, polling, and fails with `what` if it does not within `timeoutMs`. */
export async function until(check: () => boolean, what: string, timeoutMs = 5_000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  while (!check()) {
    if (Date.now() > deadline) throw new Error(`timed out waiting for ${what}`);
    await sleep(5);
  }
}
