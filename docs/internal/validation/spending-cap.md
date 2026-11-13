# A task's spending limit on Claude Code (2026-11-13)

What the CLI the pinned Claude Agent SDK 0.3.283 bundles does with `maxBudgetUsd`, run against a fake Messages API on
loopback with a dummy key: no request left this Mac, no money was spent. The fake answers every request with 100,000
input tokens, which the CLI priced at $0.401 by its own list prices for its default model. It is the ground for
[ADR 0062](../decisions/0062-a-tasks-spending-limit-is-the-apps-own-stop-handed-only-what-is-left.md).

## Read from the SDK's types (`sdk.d.ts`)
- `maxBudgetUsd?: number`: "Maximum budget in USD for the query. The query will stop if this budget is exceeded,
  returning an `error_max_budget_usd` result."
- `total_cost_usd`: the CLI's own estimate, "not a billing statement"; a resumed session continues from the total its
  transcript saved, and `maxBudgetUsd` counts only the spend since that `query()` call started.

## Seen on the CLI (a probe, then `spending-cap.e2e.test.ts`)
- The stop: a result with `subtype: 'error_max_budget_usd'`, `is_error: true`, `errors: ["Reached maximum budget
  ($0.5)"]`.
- It checks after each model call: with a $0.50 limit, the first call ($0.401) went on, the second took it to $0.802
  and it stopped. A run ends past its limit by up to one call's cost: a limit is not a ceiling.
- Subagents count: a foreground subagent (one call of the main loop, two of its own) stopped at $1.203 of $1.00; a
  background one (two of the main loop, one of its own) the same. No request came after the stop.
- The same query refuses every later instruction at once with the same result, without a model call.
- Resumed in a new query with a new limit, it gets a fresh allowance: under $2.00 it made five more calls and stopped
  ($2.005 more, `total_cost_usd` 2.807 = 0.802 + 2.005). Through the adapter: Carry On under $1.00 made three more
  calls and stopped again, with no connection reported lost.

## What Halcyonic does with it
- Claude Agent declares `offers_spending_cap`. A start's `spending_cap_usd` is handed as `maxBudgetUsd`; the stop
  fails the turn `spending_cap_reached`, and the execution's view says it in the person's own figures, never a span
  they cannot see: "The app stopped this task once its estimate passed your $5.00 limit. It estimates the task spent
  $5.40, an estimate and not your provider's bill." After Carry On: "You let this task spend $2.00 more, and the app
  stopped it once its estimate passed that. It estimates the task spent $2.40 since then and $7.80 in all, an
  estimate and not your provider's bill." Where a sum is not known, the second sentence says so, because the app did
  not report the cost of some of its work.
- The limit counts the task's whole spend since the person set or raised it (the lead's ruling): a re-attach hands the
  limit less the app's known estimates since then, never the whole limit again; where that sum is not known (a turn
  reported none, a turn a restart cut while it ran) the task waits for a limit that counts from now. Carry On closes the spent
  query and resumes the session under the raise.
- OpenCode 2.0.18 and Codex 0.157.0 declare none: a cap sent to them is refused in words, never emulated.

## Not verified
- A real provider's turn (the fake answers the main loop and subagents alike).
- Whether a limit below one call's cost behaves differently from the runs above.
