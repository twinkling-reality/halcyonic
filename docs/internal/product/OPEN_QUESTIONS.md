# Open questions

Unresolved questions that must not silently become architecture. Resolving one means recording
the evidence (a validation record or an ADR) and removing it from this list in the same change.

## Product

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| Are characters useful after repeated use, or decoration? | A kill condition | Hardware user sessions |
| What exactly belongs in the compressed state? | Defines the character presenter | XR shell prototyping |
| How much direct coding belongs in the expanded workspace? | Avoids rebuilding an IDE | Usage in the workbench |
| Does idea inception belong in the first release? | Scope | Product decision after the core mechanic works |
| When does guided learning help rather than distract? | Future policy design | Research after the base product works |

## Integrations

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| Which OpenCode API to target: v1 (`opencode-ai` 1.18, default install, declared superseded by the vendor) or v2 (`@opencode/cli` 2.0, API labeled experimental 0.0.1)? | The first real runtime | Runtime smoke test of both, then an ADR |
| Codex: build on the experimental `codex app-server` (the only surface with approvals, steering, interrupt and diffs) or the stable `codex exec` (no approvals)? | Codex control depth | Runtime smoke test, pinned CLI version |
| Claude Code: Agent SDK streaming input for hosted executions; how to attach to sessions a person started in a terminal? | Discovering existing work | Runtime smoke test; documented surfaces only |
| How does Halcyonic observe work it did not start (discovery and attach), per runtime? | The "existing project" entry path | Per-runtime validation |
| How are test runs identified in real runtimes? Only an inferred rule from commands is available. | `verifying` status and verification attention | Adapter design with an `inferred` provenance rule |
| Salidium: will Salidium publish a versioned, read-only report and event contract with a scoped, consented credential? | No honest integration exists without it | Decision in the Salidium repository |
| Seorak: how does a Halcyonic execution correlate with a Seorak session (v1 exposes only opaque refs)? | The Evaluate surface | Decision in the Seorak repository |

## Platform and security

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| Does a sustained WebSocket survive Quest focus changes, sleep and headset removal? | Multitasking is a kill condition | Hardware validation record |
| Does `System.Net.WebSockets.ClientWebSocket` work on Quest (Android, IL2CPP), including `wss://`? | The XR transport | First-week device test; keep a native fallback |
| Microphone behavior during multitasking and media playback | Voice interaction | Hardware validation |
| How do XR clients find the control plane on a LAN? | Local mode | Design plus hardware test |
| Device pairing and per-device identity | Required before serving beyond loopback | Security design and ADR |
| Remote relay provider and design | Remote mode | Later; not needed for the local slice |
| Scope of camera and environmental context | Privacy and permissions | Product and security design |

## Competition and legal

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| New or Adapted division, given Salidium and Seorak predate the competition? | Eligibility | Written organizer ruling |
| How does a judge operate a build that needs a control plane on a computer? | "Should not require a third-party device" | Product decision (hosted or demonstration mode) |
| Name, trademark and domain clearance for "Halcyonic" | Branding | Legal review |
