# ADR 0019: The demonstration reads simulated sources through the real flow, and says so

- Status: Accepted on 2026-09-29, a decision the owner delegated within their direction for the
  demonstration: the same data types and flow, invented data, labeled simulated and recorded.
- Date: 2026-09-29

## Context

Milestone 5 puts in the workspace what the understanding source concluded about an execution and
what the evaluation source measured, each with its provenance and availability. The control plane
reads both through on request and never journals them
([ADR 0010](0010-external-intelligence-is-read-through.md)): Salidium's consumer contract v1 and
Seorak's integration API v1, mapped field by field onto Halcyonic's `UnderstandingResult` and
`EvaluationResult` and validated before they are served.

Judges run a recorded demonstration on the headset, with no control plane, no network and neither
product ([ADR 0012](0012-judges-run-a-labeled-demonstration-on-the-headset.md)). The owner decided
on 2026-09-29 that the main experience calls the two capabilities Understanding and Evaluation, and
names the products only in provenance lines and an about line; and that the demonstration shows
"the same data type and flow and everything, just with fake data", clearly labeled as simulated
and recorded.

Facts that bound the options:

- The demonstration's runtimes are the mock runtime (kind `mock`). Salidium and Seorak observe only
  Claude Code and Codex sessions, so a real control plane answers `unavailable`
  (`runtime_not_observed`) about every execution of the demonstration.
- The integrations' test doubles (`FakeSalidium`, `FakeSeorak`) serve the real wire contracts over
  loopback HTTP, with the products' Host, credential and status rules, and are what the clients are
  tested against.
- The recording format (version 2) is read by readers that ignore top-level keys they do not know.
- `UnderstandingSource` named Salidium's version and instance, and `EvaluationSource` only the API
  version: nothing in either said whether a real daemon produced the answer.
- A real Salidium or Seorak recording of real sessions needs model spend and real work, and would
  put a person's sessions into a public APK.

## Decision

- **Stand-ins speak the real contracts.** `pnpm demonstration:record` starts the two test doubles
  with documents written for the demonstration's story: a stand-in Salidium daemon and a stand-in
  Seorak plane (`apps/control-plane/src/fixtures/demonstration-sources.ts`). They observe each
  demonstration session through the control plane's journal of it, the mock runtime's events, and
  derive what the products would conclude and measure, with the story adding what the events do not
  carry: each edit's lines, a step of the agent's plan, the work the agent said remains, and the
  explanations a model would write. The control plane's own routes read them through Halcyonic's
  real clients, mappings and contract validation. Only the content is invented.
- **Recorded wherever the playback can stand.** For each node of the recording's tree, a control
  plane without runtimes is given the journal up to the node's start through its recorder, as
  `pnpm replay` gives one a trace, and serves its routes on loopback. After each instant, every
  execution that instant changed is asked about. An answer is kept where it differs from the one in
  force, so the recording holds what the control plane would answer at every point of every path.
  The answers are top-level `understanding` and `evaluation` maps keyed by execution id; each entry
  names the node and the number of its events played from which it holds, and when the recording's
  control plane read it. The format stays version 2, since a reader of version 2 ignores the new
  keys and still plays the rest.
- **The contract says who produced an answer.** `UnderstandingSource` and `EvaluationSource` gain a
  required boolean, `synthetic`: true only for an answer a stand-in Halcyonic wrote produced, false
  for everything read from Salidium or Seorak. The integrations' mappings set it false; the
  demonstration's sources set it true after the real client has mapped the stand-in's documents. The
  stand-in for Salidium also reports the version `simulated` and an instance id of zeros, so no field
  names a Salidium release or a real daemon's instance.
- **Only simulated answers are recorded, and only they are played.** The recorder refuses, and the
  client refuses a recording holding, any answer that is neither available and synthetic nor the
  control plane's own `not_found` (`native_id_unknown`). A stand-in's refusal would name a product
  without saying it is simulated, so the stand-ins observe every demonstration session from its
  start.
- **The workspace says so.** The provenance line of an answer from a synthetic source begins
  "Simulated, not from Salidium" or "Simulated, not from Seorak", and a recorded answer says when it
  was recorded, by the recording's clock, instead of how long ago; its relative times and its
  staleness are judged as of that moment. The line above the stage already says the whole
  demonstration is recorded and simulated. On the headset, `DemonstrationReads` answers the
  workspace's reads from the recording where the playback stands, through the same interface as
  `ControlPlaneApi`, and marks every read recorded.
- **A demonstration session is asked about as a Claude Code session.** The demonstration's sources
  pass the clients the runtime kind `claude-agent` for its mock runtime, so an answer takes the
  requests a Claude Code session's takes. Halcyonic's contracts carry no provider, and every such
  answer is synthetic, so nothing claims the work was Claude Code's.

## Alternatives considered

- **Hand-written answers in the recording**, without routes or clients: the same shape, but not the
  same flow. The recording could hold answers the real mapping and validation would never produce,
  and a change to either would not reach the demonstration.
- **One answer per execution, at the end of its path**: much smaller, but a judge who opens a
  character while it works would read conclusions about work that has not happened yet.
- **Labeling from context alone** (a fixture journal, a synthetic runtime), with no contract field:
  the source fields would still name a Salidium version and instance, claiming a real daemon
  produced them.
- **Only sentinel values in existing fields**, such as the version `simulated`: every client would
  have to recognize a string. The boolean is what a client checks; the version is a second guard for
  a person reading the data.
- **A source system named `simulated`**: `system` names the contract an answer follows, which a
  stand-in follows too.
- **Recording real Salidium and Seorak answers about real sessions**: model spend, real work, and a
  person's sessions in a public APK.
- **No sections in the demonstration**, as ADR 0010 expected of a replay: judges would never see the
  milestone.

## Consequences

- Judges see both sections through the same contracts, routes, clients and client code as a person
  with a control plane; only the stand-ins' content is invented, and every surface says so.
- The recording grows from about 490 to about 720 KiB and reads in about 0.1 s on the development
  Mac, on a background thread on the headset.
- `pnpm demonstration:record` regenerates the answers with the rest; a change to the contracts, the
  integrations' clients or mappings, the routes or the stand-ins' stories shows as a stale
  recording in `pnpm check`.
- `synthetic` is required in two response objects. An older client ignores it; a client of this
  version fails to read an available answer from an older control plane. They ship together.
- The stand-ins are test code that the recorder uses: the integrations export them as
  `@halcyonic/integration-salidium/testing` and `@halcyonic/integration-seorak/testing`, never used by
  the running control plane.
- Revisit once a real execution has been read end to end (with Codex on a local model), if a judge
  could still take a section for a real product's answer, or if the recording's size matters on a
  headset.
