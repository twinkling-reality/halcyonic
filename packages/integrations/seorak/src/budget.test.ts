import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { RequestBudget, WINDOW_MS } from './budget.ts';

/** Reserves and starts `count` requests at `now`. */
function send(budget: RequestBudget, count: number, now: number): void {
  assert.equal(budget.reserve(count, now), null);
  for (let request = 0; request < count; request++) budget.start(now);
}

describe('the request budget', () => {
  test('lets at most the limit start in any rolling minute', () => {
    const budget = new RequestBudget(3);
    send(budget, 1, 0);
    send(budget, 2, 10_000);
    // Full: the next slot frees when the first request is a minute old.
    assert.equal(budget.reserve(1, 20_000), WINDOW_MS);
    assert.equal(budget.reserve(1, WINDOW_MS - 1), WINDOW_MS);
    assert.equal(budget.reserve(1, WINDOW_MS), null);
    // Two more need the requests of 10 s to age out as well.
    assert.equal(budget.reserve(2, WINDOW_MS), 10_000 + WINDOW_MS);
  });

  test('counts reserved requests before they start, and frees released ones at once', () => {
    const budget = new RequestBudget(3);
    assert.equal(budget.reserve(3, 0), null);
    assert.equal(budget.reserve(1, 0), WINDOW_MS);
    budget.start(0);
    budget.release(2);
    assert.equal(budget.reserve(2, 1), null);
  });

  test('starts nothing before the instant Seorak named, then resumes', () => {
    const budget = new RequestBudget(60);
    budget.pauseUntil(5_000);
    budget.pauseUntil(2_000);
    assert.equal(budget.reserve(1, 1_000), 5_000);
    assert.equal(budget.reserve(1, 4_999), 5_000);
    assert.equal(budget.reserve(1, 5_000), null);
  });
});
