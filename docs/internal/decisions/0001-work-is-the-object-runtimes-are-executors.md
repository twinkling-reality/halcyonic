# ADR 0001: Work is the object; runtimes are capability-declared executors

- Status: Accepted
- Date: 2026-09-26

## Context

Halcyonic must outlive any particular agent runtime or model. The product represents units of
work as characters, and a user's work must keep its identity when the runtime behind it changes.

Verified on 2026-09-26 ([INTEGRATIONS.md](../architecture/INTEGRATIONS.md)): OpenCode, Claude
Code and Codex all organize work as a persistent session or thread containing turns that end as
completed, failed or interrupted. Their control surfaces differ sharply. Codex's stable surfaces
reject approvals, Claude Code queues instructions sent mid-turn while Codex can steer, and none of
them can pause a turn.

## Decision

- The core aggregate is the **Workstream**. An **Execution** is one runtime's attempt at it and
  corresponds to the runtime's session or thread; the native id is an opaque reference.
- Runtime activity is normalized to turn-level events (`runtime.turn.started`, `.completed`,
  `.failed`, `.interrupted`) plus approvals, tools, test runs, agent text and connection loss.
- Execution status is derived from observed facts, never stored, with `unknown` whenever the
  facts cannot be known.
- Runtimes are adapters behind `RuntimeAdapter`. Each declares capabilities; admission rejects
  unsupported commands instead of emulating them. The capability set contains only what at least
  one verified runtime supports: no `pause`, and `interrupt` instead of `cancel`.
- Commands are journaled when admitted or rejected, and complete only on runtime confirmation.

## Alternatives considered

- **Runtime sessions as the primary object** (Claude session, Codex thread). Rejected: vendor
  identity would leak into clients, and work would lose its identity across runtimes.
- **A turn as the Execution.** Terminal states would be simpler, but one attempt with follow-up
  instructions would fragment into many executions, and discovered sessions would not map one to
  one.
- **The specification's full status list** (`queued`, `paused`, `reviewing`, `cancelled`).
  Rejected for now: nothing produces those states, and a status with no producer invites clients
  to fake it.

## Consequences

- Clients render capabilities, so the same XR interface works across runtimes of different depth.
- `completed` means "the turn ended normally", not "the work is correct". Verification and
  evaluation must stay visible as separate signals.
- Adding `pause`, discovery, diffs or reviews requires first verifying a runtime that supports it.
