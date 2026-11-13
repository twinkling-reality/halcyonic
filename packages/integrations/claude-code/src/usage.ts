import type { ModelUsage, SDKResultMessage } from '@anthropic-ai/claude-agent-sdk';
import type { UsageEstimate, UsagePrices, UsageTokens } from '@halcyonic/contracts';

/**
 * What a result says the session's query used so far: per model, every call of it, subagents and
 * compaction included, and Claude Code's own estimate of its cost. Cumulative across the turns of
 * one query, so a turn's usage is the difference from the previous result's (agent-usage.md).
 */
export type ResultTotals = Pick<SDKResultMessage, 'modelUsage' | 'total_cost_usd'>;

/** What a fresh session has used before its first result: nothing. */
export const NOTHING_YET: ResultTotals = { modelUsage: {}, total_cost_usd: 0 };

/** The totals a result carries, or null where it carries none in the SDK's shape. */
export function totalsOf(result: ResultTotals): ResultTotals | null {
  const { modelUsage, total_cost_usd } = result as Partial<ResultTotals>;
  return typeof total_cost_usd === 'number' && typeof modelUsage === 'object' && modelUsage !== null
    ? { modelUsage, total_cost_usd }
    : null;
}

type Part = keyof UsageTokens;

const PARTS: Record<Part, (model: ModelUsage) => number | undefined> = {
  input: (model) => model.inputTokens,
  cache_read: (model) => model.cacheReadInputTokens,
  cache_write: (model) => model.cacheCreationInputTokens,
  output: (model) => model.outputTokens,
  // Already counted inside outputTokens; absent where the CLI did not record it.
  reasoning: (model) => model.thinkingTokens,
};

const NONE: UsageTokens = {
  input: null,
  cache_read: null,
  cache_write: null,
  output: null,
  reasoning: null,
};

/**
 * One turn's usage from the result that ends it and the one before it in the same query, or null
 * where nothing of it is known: `before` is null where the earlier total is unknown, as for a resumed
 * session, whose first result may carry the turns of earlier runs. A part that is missing or that
 * fell (a cleared or zeroed total) is null, never a guess. The cost is Claude Code's estimate by the
 * price table of the models the turn used, and null where one was priced at a guess or the tables
 * differ.
 */
export function turnUsage(
  before: ResultTotals | null,
  after: ResultTotals | null,
): { tokens: UsageTokens; cost: UsageEstimate | null } | null {
  if (before === null || after === null) return null;
  const tokens = { ...NONE };
  for (const part of Object.keys(PARTS) as Part[]) {
    const now = sum(after.modelUsage, PARTS[part]);
    const was = sum(before.modelUsage, PARTS[part]);
    tokens[part] = now === null || was === null || now < was ? null : now - was;
  }
  const spent = cost(before, after);
  if (spent === null && Object.values(tokens).every((part) => part === null)) return null;
  return { tokens, cost: spent };
}

/**
 * The totals the next turn is measured from: this result's, unless they fell below the ones before
 * (a cleared or zeroed total), which leaves the next turn's start unknown rather than counting the
 * earlier turns again once the totals recover.
 */
export function nextTotals(
  before: ResultTotals | null,
  after: ResultTotals | null,
): ResultTotals | null {
  if (after === null || before === null) return after;
  if (after.total_cost_usd < before.total_cost_usd) return null;
  for (const read of Object.values(PARTS)) {
    const now = sum(after.modelUsage, read);
    const was = sum(before.modelUsage, read);
    if (now !== null && was !== null && now < was) return null;
  }
  return after;
}

function sum(
  models: Readonly<Record<string, ModelUsage>>,
  read: (model: ModelUsage) => number | undefined,
): number | null {
  let total = 0;
  for (const model of Object.values(models)) {
    const value = read(model);
    if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) return null;
    total += value;
  }
  return total;
}

function cost(before: ResultTotals, after: ResultTotals): UsageEstimate | null {
  const spent = after.total_cost_usd - before.total_cost_usd;
  if (!Number.isFinite(spent) || spent < 0) return null;
  const prices = new Set<UsagePrices | 'unknown'>();
  for (const [name, model] of Object.entries(after.modelUsage)) {
    const earlier = before.modelUsage[name];
    if (earlier !== undefined && earlier.costUSD === model.costUSD) continue;
    // Absent until the process priced a request for the model, and on older builds: the SDK's
    // types say to read it as the list prices. Any other value, a newer CLI's among them, is no
    // estimate of a kind this host knows.
    const basis: string = model.costBasis ?? 'list';
    prices.add(basis === 'list' || basis === 'managed' ? basis : 'unknown');
  }
  const [only, ...others] = prices;
  if (only === undefined || only === 'unknown' || others.length > 0) return null;
  return { estimated_usd: spent, prices: only };
}
