import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { ModelUsage } from '@anthropic-ai/claude-agent-sdk';
import { NOTHING_YET, nextTotals, type ResultTotals, turnUsage } from './usage.ts';

function model(fields: Partial<ModelUsage>): ModelUsage {
  return {
    inputTokens: 0,
    outputTokens: 0,
    cacheReadInputTokens: 0,
    cacheCreationInputTokens: 0,
    webSearchRequests: 0,
    costUSD: 0,
    contextWindow: 200_000,
    maxOutputTokens: 32_000,
    ...fields,
  };
}

const FIRST: ResultTotals = {
  total_cost_usd: 0.3,
  modelUsage: {
    main: model({
      inputTokens: 100,
      outputTokens: 50,
      thinkingTokens: 10,
      cacheReadInputTokens: 2000,
      cacheCreationInputTokens: 400,
      costUSD: 0.25,
      costBasis: 'list',
    }),
    helper: model({ inputTokens: 20, outputTokens: 5, thinkingTokens: 0, costUSD: 0.05 }),
  },
};

describe("a Claude turn's usage, from the query's running totals", () => {
  test("a fresh session's first turn is its result's totals, every model counted", () => {
    assert.deepEqual(turnUsage(NOTHING_YET, FIRST), {
      tokens: { input: 120, cache_read: 2000, cache_write: 400, output: 55, reasoning: 10 },
      cost: { estimated_usd: 0.3, prices: 'list' },
    });
  });

  test('a later turn is the difference from the result before it', () => {
    const second: ResultTotals = {
      total_cost_usd: 0.5,
      modelUsage: {
        ...FIRST.modelUsage,
        main: model({
          inputTokens: 130,
          outputTokens: 80,
          thinkingTokens: 15,
          cacheReadInputTokens: 5000,
          cacheCreationInputTokens: 400,
          costUSD: 0.45,
          costBasis: 'list',
        }),
      },
    };
    const turn = turnUsage(FIRST, second);
    assert.ok(turn !== null);
    assert.deepEqual(turn.tokens, {
      input: 30,
      cache_read: 3000,
      cache_write: 0,
      output: 30,
      reasoning: 5,
    });
    assert.equal(turn.cost?.prices, 'list');
    assert.ok(Math.abs((turn.cost?.estimated_usd ?? 0) - 0.2) < 1e-9);
  });

  test('a resumed session, whose earlier total is unknown, says nothing of its first turn', () => {
    assert.equal(turnUsage(null, FIRST), null);
    assert.equal(turnUsage(FIRST, null), null, 'nor of a result without totals');
  });

  test('a total that fell, as after a clear, and a missing part are unknown, never a guess', () => {
    const cleared: ResultTotals = {
      total_cost_usd: 0.01,
      modelUsage: { main: model({ inputTokens: 5, outputTokens: 2, costUSD: 0.01 }) },
    };
    assert.equal(turnUsage(FIRST, cleared), null, 'nothing of it is known, so nothing is said');
    const noThinking: ResultTotals = {
      total_cost_usd: 0.4,
      modelUsage: {
        ...FIRST.modelUsage,
        main: model({
          inputTokens: 150,
          outputTokens: 50,
          cacheReadInputTokens: 2000,
          cacheCreationInputTokens: 400,
          costUSD: 0.35,
        }),
      },
    };
    assert.deepEqual(turnUsage(FIRST, noThinking)?.tokens, {
      input: 50,
      cache_read: 0,
      cache_write: 0,
      output: 0,
      reasoning: null,
    });
  });

  test('totals that fell leave the next turn unknown, never counting the earlier turns again', () => {
    const zeroed: ResultTotals = { total_cost_usd: 0, modelUsage: {} };
    assert.equal(nextTotals(FIRST, zeroed), null);
    assert.equal(nextTotals(FIRST, { ...FIRST, total_cost_usd: 0.1 }), null, 'a cost that fell');
    assert.equal(nextTotals(FIRST, FIRST), FIRST);
    assert.equal(nextTotals(null, FIRST), FIRST, 'a first result sets the start for the next');
    assert.equal(nextTotals(FIRST, null), null);
    // After the fall, the recovered totals are a start again, not a turn.
    assert.equal(turnUsage(nextTotals(FIRST, zeroed), FIRST), null);
  });

  test('no estimate where a model was priced at a guess or the price tables differ', () => {
    const guessed: ResultTotals = {
      total_cost_usd: 0.4,
      modelUsage: {
        ...FIRST.modelUsage,
        other: model({ inputTokens: 1, costUSD: 0.1, costBasis: 'unknown' }),
      },
    };
    assert.equal(turnUsage(FIRST, guessed)?.cost, null);
    const managed: ResultTotals = {
      total_cost_usd: 0.4,
      modelUsage: {
        ...FIRST.modelUsage,
        other: model({ inputTokens: 1, costUSD: 0.1, costBasis: 'managed' }),
      },
    };
    assert.equal(turnUsage(NOTHING_YET, managed)?.cost, null, 'list and managed are not added');
    assert.deepEqual(turnUsage(FIRST, managed)?.cost?.prices, 'managed', 'only the turn counts');
    const newer: ResultTotals = {
      total_cost_usd: 0.4,
      modelUsage: {
        ...FIRST.modelUsage,
        other: model({ inputTokens: 1, costUSD: 0.1, costBasis: 'negotiated' as never }),
      },
    };
    assert.equal(turnUsage(FIRST, newer)?.cost, null, 'a price basis this host does not know');
  });
});
