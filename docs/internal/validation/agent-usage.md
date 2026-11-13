# What each agent app reports of a run's usage (2026-11-13)

First read from each pinned runtime's own types, with no request sent; the runs further down check what each
adapter makes of them, against fake providers on loopback. It is the ground for recording a run's usage per turn.
An estimate is named as one, and unknown stays unknown.

## Claude Agent (`@anthropic-ai/claude-agent-sdk` 0.3.283, `sdk.d.ts`)
- Every result message (`SDKResultSuccess`, `SDKResultError`) carries:
  - `usage` (`NonNullableUsage`): the main agent loop only, per turn in a streaming-input session;
  - `modelUsage: Record<string, ModelUsage>`: per model, every call of the query pipeline, subagents and compaction
    included, cumulative for the query. `ModelUsage` = `{inputTokens, outputTokens, thinkingTokens?,
    cacheReadInputTokens, cacheCreationInputTokens, webSearchRequests, costUSD, contextWindow, maxOutputTokens, ...}`;
  - `total_cost_usd`: cumulative for the query.
- Money: `total_cost_usd` and `costUSD` are Claude Code's own estimate, and its types say "An estimate, not a billing
  statement". The price basis is per model: Claude Code's built-in list prices, the organisation's managed rates, or
  unknown (then "a guess at the default model's rate").
- Per turn, the cumulative figures differ from the previous result's.

## Codex (0.157.0 app-server, its own `generate-ts` bindings)
- `thread/tokenUsage/updated`: `{threadId, turnId, tokenUsage: {total, last, modelContextWindow}}`. `total` is
  cumulative for the thread; `last` is the most recent model response. Each is `TokenUsageBreakdown`: `{totalTokens,
  inputTokens, cachedInputTokens, cacheWriteInputTokens, outputTokens, reasoningOutputTokens}`.
- `turn/completed` carries no usage. A turn's usage is the difference of the thread totals, or the sum of `last`
  within the turn: derived by the adapter from the runtime's own counts, never given per turn.
- Money: none is pushed. `account/usage/read` answers a thread's `estimatedUsageCreditsMicros` and
  `estimatedUsageUsdMicros` (nullable), per thread, named estimated, as a pull. Not verified: how it is computed,
  and whether it needs a sign-in.

## OpenCode (2.0.18; schemas read from the source embedded in its binary, no type files ship)
- The v2 event `session.step.ended` carries `cost` and `tokens: {total?, input, output, reasoning, cache: {read,
  write}}` per model step. The assistant message (`message.updated`) carries the same fields summed.
- Tokens are the provider's own counts (non-cached input, visible output, reasoning, cache read and write).
- Money: `cost` is OpenCode's own estimate, computed from the models.dev catalog's prices (cached and refreshed over
  the network), not a provider figure. A model with no price tier gets cost 0, so 0 does not mean free.

## What Halcyonic records (2026-11-13)
Each adapter declares `reports_usage` and reports a turn's `runtime.usage.reported` at the turn's end,
as `reported`, in the contract's five parts that never overlap: `input` outside the cache,
`cache_read`, `cache_write`, `output` with its reasoning, and `reasoning`. Nothing is reported for a
turn the adapter did not see whole, and the host prices nothing. `counted` says how a turn's
figure came from the runtime's: Claude and Codex `differenced`, OpenCode `summed`.
- Claude Agent: the turn is the difference of this result's per-model totals from the previous
  result's in the same query; a re-attached session's first turn reports nothing, since its first
  result may carry earlier runs, and totals that fall below the last ones (a cleared or zeroed
  result) leave the next turn's start unknown too, so earlier turns are never counted again.
  Tokens a background subagent spends between turns land in the next turn's difference. Cost is the difference of `total_cost_usd`, by `costBasis` (`list`,
  `managed`); none where a model the turn used was priced at a guess (`unknown`) or the turn's
  models were priced by different tables. `reasoning` is `thinkingTokens`, null where absent.
- OpenCode: the turn is the sum of its `session.step.ended` events, seen from the turn's start;
  `output` adds OpenCode's `reasoning` back, since OpenCode's own `output` leaves it out. A turn
  whose start was not seen, or whose stream had a gap (any reconciliation), or whose failed step
  gave no tokens reports nothing. Cost is the sum of the steps' `cost`, by `catalog` prices, and
  none once any step gave 0, which OpenCode gives both for a free model and for one with no price.
- Codex: the turn is how far the thread's running `total` moved from the turn's start to its end,
  reported only where a total came during the turn. A resumed thread's start is unknown until Codex
  gives its total; in the captured restart (`restart.jsonl`) it gives the restored total right after
  `turn/start` and before `turn/started`, so the first resumed turn is measured from it, and it
  would report nothing were that order ever reversed. A total that falls leaves the start unknown.
  `input` is Codex's `inputTokens` less `cachedInputTokens` and `cacheWriteInputTokens`. No cost.

## Verified by a run (2026-11-13)
- Codex 0.157.0 (`codex-runtime.e2e.test.ts`, the pinned binary against the fake Responses
  provider answering `input_tokens` 100 with `cached_tokens` 40, `output_tokens` 10 with
  `reasoning_tokens` 3): the turn reported input 60, cache_read 40, cache_write 0, output 10,
  reasoning 3. So Codex's input holds the cache's reads and its output holds the reasoning.
- Claude Code (`usage.e2e.test.ts`, the CLI the SDK 0.3.283 bundles against a fake Messages API
  answering every request with input 10, cache reads 200, cache writes 40, output 5): two turns
  each reported exactly their own answers' counts, and an estimate by `list` prices above 0. With
  the difference taken from nothing instead of the previous result, the second turn's check fails.
- OpenCode 2.0.18: the streams captured from the pinned binary carry `session.step.ended` with
  tokens and a cost of 0 for the fake provider's model (no price), read as no estimate; a captured
  run on a local model carried cache reads (13208 tokens) apart from input (6641).

## Not verified
- The prices behind any tool's estimate for a real model; an estimate is shown as one, never as a
  bill.
- Whether a provider that counts no cache writes is given 0 by OpenCode and Codex, which then reads
  as 0 rather than unknown; and whether Codex counts cache writes inside its `inputTokens`, as it
  does the cache's reads (the fake provider gives no writes).
- Claude Code's counts on a real provider's turn with subagents (the fake API answers the main loop
  only).
