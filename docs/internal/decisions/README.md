# Architecture decision records

An ADR records one significant decision that is expensive to reverse: its context, the options,
the choice and its consequences. ADRs are historical. When a decision changes, write a new ADR
that supersedes the old one and mark the old one `Superseded by ADR NNNN`; do not rewrite it. The
canonical architecture documents always describe the current state.

Write an ADR when a change alters a dependency rule, a contract's shape or versioning, the
storage or transport model, a security boundary, or the choice of a platform or major dependency.
Do not write one for routine implementation choices.

Name files `NNNN-short-title.md`, numbered in sequence, starting from [TEMPLATE.md](TEMPLATE.md).

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](0001-work-is-the-object-runtimes-are-executors.md) | Work is the object; runtimes are capability-declared executors | Accepted |
| [0002](0002-append-only-journal-with-rebuilt-projection.md) | Append-only journal with a projection rebuilt at startup | Accepted |
| [0003](0003-rest-bootstrap-and-websocket-realtime.md) | REST for bootstrap and commands, WebSocket for live state | Accepted |
| [0004](0004-control-plane-stack.md) | Node.js 24 running TypeScript directly, `node:sqlite`, Fastify, pnpm 11 | Accepted |
| [0005](0005-typebox-contracts-as-single-source.md) | TypeBox schemas as the single source of the contracts | Accepted |
| [0006](0006-license-under-apache-2.md) | License under Apache-2.0, copyright Twinkling Reality | Accepted |
| [0007](0007-agent-control-belongs-to-halcyonic.md) | Agent control belongs to Halcyonic, through runtime APIs | Accepted |
| [0008](0008-engine-independent-csharp-client-core.md) | An engine-independent C# client core with generated contract bindings | Accepted |
| [0009](0009-opencode-v2-pinned-and-launched-by-halcyonic.md) | Target OpenCode's v2 API, pinned, on a server Halcyonic launches | Accepted |
| [0010](0010-external-intelligence-is-read-through.md) | External intelligence is read through, not journaled | Accepted |
| [0011](0011-codex-app-server-stable-surface.md) | Target Codex's app-server stable surface, pinned, on a server Halcyonic launches | Accepted |
| [0012](0012-judges-run-a-labeled-demonstration-on-the-headset.md) | Judges run a labeled demonstration on the headset, rather than reach a hosted control plane | Accepted |
| [0013](0013-characters-are-bots-with-a-living-surface.md) | Characters are bots whose eyes, motion and light carry state | Accepted; amended 2026-10-08 |
| [0014](0014-hand-interaction-through-the-interaction-sdk.md) | Open work in place with hands, through Meta's Interaction SDK | Accepted |
| [0015](0015-the-stage-stands-on-the-persons-desk.md) | The stage stands on the person's desk, found with MRUK and kept with a spatial anchor | Accepted |
| [0016](0016-a-person-chooses-a-runtimes-model-from-its-own-list.md) | A person chooses a runtime's model from the runtime's own list | Accepted |
| [0017](0017-pair-a-headset-over-the-local-network.md) | Pair a headset over the local network with a code, SRP and a pinned certificate | Proposed |
| [0019](0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md) | The demonstration reads simulated understanding and evaluation sources through the real contracts and routes, and says so | Accepted |
| [0020](0020-a-project-works-in-one-host-approved-folder.md) | A project works in one folder the host approves, and clients never name a path | Accepted |
| [0021](0021-speech-becomes-a-draft-transcribed-on-the-mac.md) | Speech becomes a draft, transcribed on the Mac, that the person confirms like typed text | Accepted |
| [0022](0022-agent-questions-reach-the-person.md) | An agent's questions reach the person, who answers them through the runtime's own surface | Accepted |
| [0023](0023-the-headset-interface-is-one-system-of-tokens-and-components.md) | The headset's interface is one system of tokens, components and render-checked rules | Accepted; its panels and workspace superseded by 0026 |
| [0024](0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md) | The Mac's settings live in one file only its owner can write, and never start paid model use | Proposed |
| [0025](0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md) | Create's companion is a local model the control plane asks one reply at a time, and its exchange stays on the headset | Accepted |
| [0026](0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md) | The headset's interface is a game menu of places, sections and prompts, on one plane facing the eyes | Accepted |
| [0027](0027-the-headset-moves-only-to-answer-the-person-or-show-a-wait.md) | The headset moves only to answer the person or to show a wait | Accepted; amended 2026-10-08 |
| [0028](0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md) | OpenCode runs inside a macOS sandbox of Halcyonic's own | Accepted; amended 2026-10-08 |
