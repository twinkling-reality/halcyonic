# ADR 0033: An agent app declares whether approving a command can let it leave the sandbox

- Status: Accepted
- Date: 2026-10-16
- Complements ADR 0011, ADR 0028 and ADR 0031

## Context

A person approves a command by reading the request Halcyonic shows: the command and its folder. With
Codex 0.157.0 that is not enough to know what approving does. Verified on 2026-10-16 with the pinned
binary through the adapter and its fake provider, and in a bare app-server client recording every
message ([codex-capabilities.md](../validation/codex-capabilities.md), "Sandbox builds, process
control and approvals"):

- an escalated command (`sandbox_permissions: "require_escalated"`), once accepted, runs outside the
  sandbox: it wrote outside the workspace;
- a plain command Codex judges dangerous (`rm -rf`) asks the same way and, once accepted, still runs
  inside the sandbox: it could not delete outside;
- the two requests carry the same fields except `reason`, the model's own optional justification,
  so nothing reliable in a request tells them apart.

OpenCode runs inside a sandbox of Halcyonic's own on macOS, where an approved command is still
refused a write outside the project (ADR 0028, [opencode-sandbox.md](../validation/opencode-sandbox.md)).
Claude Code's behaviour has not been verified. AGENTS.md requires capabilities to be declared,
never inferred, and Halcyonic's own words to stay apart from a runtime's text.

## Decision

`RuntimeDescriptor` carries an optional boolean, `approval_may_leave_sandbox`, declared by an adapter
only once verified:

- true when approving a command can let it run outside any sandbox, with the person's own access;
  Codex declares it;
- false when an approved command still runs inside a sandbox; OpenCode declares it only when it runs
  inside Halcyonic's sandbox;
- absent when not verified: Claude Code, the mock, and OpenCode without the sandbox.

Absent means unknown, never false. A client must never show an absent declaration as a command
staying inside a sandbox.

The headset reads the declaration, never the request, and says it in Halcyonic's own words under a
command approval that can be approved, before what Approve does: "If you approve, it may run outside
its sandbox, with your access to your computer." or "If you approve, it still runs inside a
sandbox." It says nothing when nothing is declared, for a request that runs no command, and for a
request that can only be denied. The runtime's request stays exactly as the runtime reported it.

## Alternatives considered

- A sentence in Halcyonic's words inside the request's summary, as the Codex adapter's folder line
  is: quick, but it mixes Halcyonic's words into the runtime's text, which the headset is separating
  (WORDS.md), and it would be undone with the next approval design.
- A per-request field: no reliable signal exists in Codex 0.157.0's requests; `reason` is the
  model's own and optional.
- A three-valued enum (stays, may leave, no sandbox): the C# generator allows only a plain string or
  a boolean to be optional, and no verified runtime needs a third value yet.
- A stricter per-turn approval policy so that every command asks: Halcyonic's instruction carries no
  policy, Codex applies one to all later turns, and even `untrusted` lets commands it calls safe run
  without asking.

## Consequences

- A person sees, before approving a Codex command, that it may get their access to their computer.
- The note is as good as the declaration: a runtime upgrade, or a change to how Halcyonic starts it,
  needs the declaration verified again; the Codex end to end suite pins it.
- Claude Code needs its own verification before it declares anything.
- Revisit when a runtime marks escalations in its requests, which would allow a per-request note,
  or when a verified runtime needs a value beyond the two.
