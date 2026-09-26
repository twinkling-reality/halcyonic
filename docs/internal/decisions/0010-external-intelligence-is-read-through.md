# ADR 0010: External intelligence is read through, not journaled

- Status: Accepted
- Date: 2026-09-26

## Context

Salidium understands what agent work did (verdicts, changed files, verification, model-written
explanations) and Seorak measures it (estimated cost, outcomes, verification rates). Both observe
Claude Code and Codex sessions on the developer's machine independently of Halcyonic, keep their own
histories, update their conclusions as sessions progress, and label every claim with their own
epistemic vocabulary. Salidium's consumer contract v1, a release candidate, was exercised end to end
on 2026-09-26 ([salidium-consumer-contract.md](../validation/salidium-consumer-contract.md)).

Halcyonic's journal records Halcyonic's own facts: commands and what its runtime adapters observed.
Status and attention are derived only from those facts.

## Decision

- The control plane reads external intelligence on request, through each product's read-only,
  versioned contract, and serves it per execution:
  `GET /api/executions/:execution_id/understanding` for Salidium. Seorak's evaluation will follow the
  same pattern when its correlation endpoint is published.
- It is never journaled, never replayed, and never feeds status or attention.
- Every answer states its availability: `available`, or `not_found`, `unavailable`, `incompatible`
  or `unauthorized` with a reason. An execution whose runtime has not reported a session id yet is
  `not_found` without asking the provider.
- Conclusions keep the provider's own epistemic classes (Salidium's `observed`, `reported`,
  `inferred`, `planned`, `explained`) and are shown as the provider's, never restated as Halcyonic's
  own facts.
- Executions are correlated with the provider's sessions only through the runtime kind and the
  runtime's own session id.
- The control plane validates every answer against Halcyonic's contract before serving it, and
  answers `incompatible` rather than pass on anything else.
- XR clients read intelligence only from the control plane, never from the providers.

## Alternatives considered

- **Journal the conclusions as events.** It would duplicate the providers' authority in a second,
  stale copy, grow the journal with large and frequently superseded documents, and make replays
  show conclusions as if they were Halcyonic's.
- **Let XR clients call the providers directly.** Provider credentials would have to reach the
  headset, and each client would re-implement discovery and correlation.
- **Derive attention from the providers' verdicts.** Attractive, but it would make Halcyonic's
  status depend on another product's heuristics and availability. Revisit with evidence from use.

## Consequences

- Understanding and evaluation disappear when their provider is absent, stopped or incompatible, and
  the client says so.
- Replayed traces carry no intelligence; a replay shows the provider's answer for those executions,
  normally `unavailable`.
- Clients refetch while a workspace is open. Pushing changes from Salidium's feed to clients is a
  later improvement, not part of this decision.
