# ADR 0062: A task's spending limit is the agent app's own stop, handed only what is left of it

- Status: Accepted
- Date: 2026-11-13

## Context

A person running a task on their own key cannot say "stop this at $5" today. Claude Code has its own stop,
`maxBudgetUsd`, verified on the CLI the pinned SDK bundles ([spending-cap.md](../validation/spending-cap.md)): it
stops after the model call that passes the limit, subagents counted, and a resumed query counts from its own start.
OpenCode 2.0.18 and Codex 0.157.0 show no such stop. What each turn spent is recorded as the app's own estimate
(`runtime.usage.reported`), never priced by the host.

## Decision

1. The agent app does every stop. A runtime declares `offers_spending_cap` only once a run of its pinned version shows
   the stop; where none is declared, a limit is not offered, and a command carrying one is refused in words
   (`capability_unsupported`). The host never stops a running task on its own sums.
2. The limit is the task's whole spend since the person set or raised it (the lead's ruling, 2026-11-13). At every
   launch the host hands the app the limit less the sum of the app's own recorded estimates since then, never more than
   the limit. A start hands the whole limit; a re-attach hands what is left; Carry On hands the raise.
3. That sum counts only while known: every turn in the span reported an estimate, by one kind of prices. Where it is
   not, the host hands nothing: a task re-attached with nothing it could be handed (the sum not known, a turn in
   flight that had not reported) waits (`spending_cap.state` `waiting`) for the person to give a limit that counts
   from now, and every sender, the host's own included, goes through the same
   admission, so nothing launches it before. A process that survived is not relaunched and changes nothing.
4. A turn the app stops fails `spending_cap_reached`; a task at its limit (stopped there, or its known sum at it) reads
   `reached` and takes an instruction only as Carry On: `execution.send_instruction` with `spending_cap_usd`, the
   amount raised, counting from now. Its default is none, and it never adds a press to the start.
5. Words: a limit is not a ceiling (the app stops after the step that passes it); every figure is named the app's
   estimate; Carry On is more from now; a span is the person's own ("since you raised the limit"), or none when it
   is the whole task, never one the person cannot see.

## Alternatives considered

- The limit counts since the session last started, the same limit handed at every launch: no arithmetic, but the
  person would read a limit that resets at a relaunch they cannot see.
- The host stopping a task when the reported estimates pass the limit: arithmetic on estimates deciding a stop, and a
  stop no app confirms.
- Emulating a cap on OpenCode and Codex by interrupting after a turn: not their own stop, so not declared.

## Consequences

- Clients read `ExecutionView.spending_cap` {limit, spent since set or null, state} and offer the raise only where the
  host's list does (ADR 0060).
- A restart that cut a turn under a limit makes the person give a new limit before the task goes on.
- Another runtime offers a cap only after its own verified stop; the host's arithmetic stays the same.
