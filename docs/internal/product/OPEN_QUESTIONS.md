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
| Claude Code: Agent SDK streaming input for hosted executions; how to attach to sessions a person started in a terminal? | Discovering existing work | Runtime smoke test; documented surfaces only |
| How does Halcyonic observe work it did not start (discovery and attach), per runtime? | The "existing project" entry path | Per-runtime validation |
| Should a project bind to one directory, and should parallel workstreams get their own git worktrees? Agents already work only under host-configured project roots, and clients name a directory per execution. | Starting real work from the XR client without typing paths | Product and security design |
| How is the pinned OpenCode binary installed and updated for users, and should the control plane verify its checksum? Today the adapter checks the version it reports. | The OpenCode adapter runs only a pinned binary ([ADR 0009](../decisions/0009-opencode-v2-pinned-and-launched-by-halcyonic.md)) | Packaging decision |
| How are test runs identified in real runtimes? Only an inferred rule from commands is available. | `verifying` status and verification attention | Adapter design with an `inferred` provenance rule |
| Claude Code: can a background task raise an approval after its turn ended? The domain clears pending approvals when a turn ends. | Approvals must never vanish | A smoke test with a real model |
| Claude Code: `AskUserQuestion` arrives as an ordinary approval, and approving it gives no answers. How should questions reach the person? | Clarifying questions from agents | Adapter design after a real-model smoke test |
| Seorak: how does a Halcyonic execution correlate with a Seorak session? The published API (`@seorak/types` 0.1.0) exposes only opaque session references. | The Evaluate surface | A published correlation endpoint in Seorak |

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

## Legal

| Question | Why it matters | Resolved by |
| --- | --- | --- |
| Name, trademark and domain clearance for "Halcyonic" | Branding | Legal review |
