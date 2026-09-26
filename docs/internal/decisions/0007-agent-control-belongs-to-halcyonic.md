# ADR 0007: Agent control belongs to Halcyonic, through runtime APIs

- Status: Accepted
- Date: 2026-09-26

## Context

Three products by the same owner can touch the same agent sessions:

- Salidium observes sessions through asynchronous hooks and transcripts and never decides anything
  ([Salidium audit](../validation/salidium-integration-audit.md)).
- Seorak observes sessions through hooks and transcripts to measure them and send nudges.
- Halcyonic exists to direct work: start, instruct, approve and interrupt.

Blocking hooks are installed once, in the developer's agent configuration, and fire for every
session on the machine. If more than one product answered prompts that way, two products could
race to answer the same prompt, and control would reach sessions nobody asked either product to
control.

## Decision

- **Halcyonic owns agent control.** It controls only executions it hosts, through each runtime's
  own API (for example Claude Agent SDK callbacks, OpenCode permission replies, Codex app-server
  requests). It never installs global hooks or changes the user's runtime configuration to gain
  control.
- **Salidium and Seorak stay observational.** They measure, explain, report and notify. Neither
  answers permission prompts or stops, steers or approves work, on any device. Interactive
  control belongs to Halcyonic clients. Each project records the matching decision in its own
  repository.
- Neither Salidium nor Seorak gains Halcyonic-specific features. Halcyonic consumes their
  published, versioned, read-only contracts, and the dependency never runs the other way.

## Alternatives considered

- **An observer also controls, through blocking hooks.** It would need a rule for which product
  answers each prompt, and hook-based control reaches sessions nobody asked it to control.
- **A shared control service used by all three.** It would couple three independently useful
  products to one component for a need only Halcyonic has.

## Consequences

- Halcyonic cannot control sessions a person started elsewhere, such as in a terminal, unless the
  runtime offers a documented way to attach. Those sessions are shown, not steered.
- Revisit if a runtime offers a documented, shared control channel that multiple clients can use
  safely.
