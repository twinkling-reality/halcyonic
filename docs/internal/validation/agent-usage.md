# What each agent app reports of a run's usage (2026-11-13)

Read from each pinned runtime's own types, no run made and no request sent. It is the ground for recording a
hosted run's usage per turn (CORE, the lead's 04:40Z). An estimate is named as one, and unknown stays unknown.

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

## Not verified
- What a running server or CLI actually emits on a real turn (no run was made).
- The prices behind either tool's estimate for the pinned models.
