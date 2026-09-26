# ADR 0007: Agent control belongs to Halcyonic, through runtime APIs

- Status: Accepted
- Date: 2026-09-26

## Context

Three products by the same owner touch the same agent sessions
([Salidium audit](../validation/salidium-integration-audit.md),
[Seorak audit](../validation/seorak-integration-audit.md)):

- Salidium observes sessions through asynchronous hooks and transcripts and never decides
  anything.
- Seorak observes through global hooks and transcripts. It sends nudges, and it has a parked
  design for approving, denying and stopping sessions through blocking `PreToolUse` and
  `PermissionRequest` hooks. It also has an experimental, read-only glance app for Meta Ray-Ban
  Display.
- Halcyonic exists to direct work: start, instruct, approve and interrupt.

If two products install blocking hooks, both can try to answer the same prompt. Global hooks also
act on every session on the machine, including ones the user never asked either product to
control.

## Decision

- **Halcyonic owns agent control.** It controls only executions it hosts, through each runtime's
  own API (for example Claude Agent SDK callbacks, OpenCode permission replies, Codex app-server
  requests). It never installs global hooks or changes the user's runtime configuration to gain
  control.
- **Seorak and Salidium stay observational.** Seorak keeps nudges and notifications; its glasses
  app remains a read-only glance. Interactive control on glasses belongs to Halcyonic clients.
- Neither Salidium nor Seorak gains Halcyonic-specific features. Halcyonic consumes their
  published, versioned, read-only contracts, and the dependency never runs the other way.

## Alternatives considered

- **Seorak builds control too.** It would need a rule for which product answers each prompt, and
  hook-based control reaches sessions nobody asked it to control.
- **A shared control service used by all three.** It would couple three independently useful
  products to one component for a need only Halcyonic has.

## Consequences

- Halcyonic cannot control sessions a person started elsewhere, such as in a terminal, unless the
  runtime offers a documented way to attach. Those sessions are shown, not steered.
- Seorak's parked agent-control design should be recorded in Seorak as superseded by this
  decision, which its owner does in the Seorak repository.
- Revisit if a runtime offers a documented, shared control channel that multiple clients can use
  safely.
