# Salidium integration audit

- **Question:** What does Salidium actually provide, and where is the honest integration boundary
  with Halcyonic?
- **Date:** 2026-09-26.
- **Versions:** `twinkling-reality/salidium` at `27c9dbb` (release 0.5.0); `salidium-cloud` at
  `0cb176c`. Paths below are relative to the Salidium repository.
- **Method:** Read-only audit of source and documentation. Nothing was built, run or fetched.
- **Status:** Repository verified. No integration built.

## Findings

- **What it is.** An MIT-licensed, local-first tool that turns Claude Code and Codex sessions into
  evidence-linked reports that update while the agent works. One Node.js 24 daemon
  (`node:sqlite`), a CLI, and a React UI served by the daemon on `127.0.0.1:47822`
  (`packages/daemon/src/daemon.ts:727`, `config/daemonConfig.ts:24`).
- **It observes and never controls.** It uses asynchronous hooks that "never block or decide
  anything", plus tailing of the providers' session files, which it treats as the durable source.
  A permission request is recorded, not answered. There is no API to start, instruct, approve or
  cancel a run (`docs/architecture.md:32-34`, `packages/adapters/claude-code/src/hookPayloads.ts`).
- **Canonical events.** Twenty kinds with provenance classes
  `observed | reported | inferred | planned | explained`; `reported` is "never upgraded to observed
  by parsing" (`packages/protocol/src/provenance.ts:3-16`). Session ids are
  `<provider>:<providerSessionId>` (`packages/protocol/src/ids.ts:7-9`). There is no Project,
  Workstream or Command concept.
- **Evidence model.** A file counts as verified when a later full-scope passing check exists. The
  "needs you" rules are deterministic and cite events. Written Why and How diagrams are model
  output, off by default, stored as structured data (lanes, steps, chains) and drawn with HTML
  and CSS only (`packages/protocol/src/events.ts:431-482`, `packages/ui/src/components/FlowDiagram.tsx`).
- **Local API.** HTTP and SSE only, with a bearer token regenerated on every daemon start, loopback
  Host checks, Origin matching and cross-site refusal (`packages/daemon/src/server/httpServer.ts:137-166`).
  Endpoints have no version in their paths. The report view is a TypeScript type with no runtime
  schema, and fields have been removed from it without a version change.
- **Salidium's own position on third parties.**
  - ADR 0001: "The local protocol is private, session-oriented, and contains sensitive fields."
    Publishing `@salidium/protocol` was rejected because it would create "unsafe coupling".
  - `docs/architecture.md:292-293`: external consumers "should therefore consume a versioned outbox,
    export, or replication stream while SQLite retains local authority".
  - Only the local operations documents carry a stable contract version.
- **Capabilities against the Halcyonic specification's assumptions.**

| Capability | Status |
| --- | --- |
| Session and activity understanding, changed files, test state, blockers, reports, rewind, live updates | Present |
| Execution history | Partial: per session; nothing groups sessions into workstreams |
| Diagrams | Present on the web; Why and How require model calls |
| Summaries | Partial: a deterministic verdict; prose requires model calls |
| Architecture views, file and component relationships | Absent |

## Consequences for Halcyonic

- No Salidium adapter is built. Building one now would couple Halcyonic to an interface Salidium
  explicitly calls private and unversioned.
- The durable path is upstream: a released, versioned, read-only contract for sessions, events and
  reports, plus a scoped credential the user consents to, published from the Salidium repository.
  Until then the Understand surface shows Salidium as unavailable.
- Correlation is cheap once a contract exists: an execution's `native_id` gives Salidium's session
  id `<provider>:<native_id>`.
- Halcyonic keeps Salidium's provenance classes when it displays Salidium conclusions, and never
  reads its database, imports its private packages or calls its write endpoints.
- A process Halcyonic launches must not inherit `SALIDIUM_INTERNAL`, which makes Salidium silently
  drop its hooks (`packages/daemon/src/daemon.ts:978-981`).
