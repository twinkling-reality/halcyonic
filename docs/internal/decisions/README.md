# Architecture decision records

An ADR records one significant decision that is expensive to reverse: its context, the options,
the choice and its consequences. ADRs are historical. When a decision changes, write a new ADR
that supersedes the old one and mark the old one `Superseded by ADR NNNN`; do not rewrite it. The
canonical architecture documents always describe the current state.

Write an ADR when a change alters a dependency rule, a contract's shape or versioning, the
storage or transport model, a security boundary, or the choice of a platform or major dependency.
Do not write one for routine implementation choices.

Name files `NNNN-short-title.md`, numbered in sequence, starting from [TEMPLATE.md](TEMPLATE.md).
(`0018` was skipped and never assigned.)

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](0001-work-is-the-object-runtimes-are-executors.md) | Work is the object; runtimes are capability-declared executors | Accepted |
| [0002](0002-append-only-journal-with-rebuilt-projection.md) | Append-only journal with a projection rebuilt at startup | Accepted; amended by [0050](0050-a-start-restores-the-projection-from-a-checkpoint-and-folds-only-the-journal-after-it.md) |
| [0003](0003-rest-bootstrap-and-websocket-realtime.md) | REST for bootstrap and commands, WebSocket for live state | Accepted |
| [0004](0004-control-plane-stack.md) | Node.js 24 running TypeScript directly, `node:sqlite`, Fastify, pnpm 11 | Accepted |
| [0005](0005-typebox-contracts-as-single-source.md) | TypeBox schemas as the single source of the contracts | Accepted |
| [0006](0006-license-under-apache-2.md) | License under Apache-2.0, copyright Twinkling Reality | Accepted |
| [0007](0007-agent-control-belongs-to-halcyonic.md) | Agent control belongs to Halora, through runtime APIs | Accepted |
| [0008](0008-engine-independent-csharp-client-core.md) | An engine-independent C# client core with generated contract bindings | Accepted |
| [0009](0009-opencode-v2-pinned-and-launched-by-halcyonic.md) | Target OpenCode's v2 API, pinned, on a server Halora launches | Accepted |
| [0010](0010-external-intelligence-is-read-through.md) | External intelligence is read through, not journaled | Accepted |
| [0011](0011-codex-app-server-stable-surface.md) | Target Codex's app-server stable surface, pinned, on a server Halora launches | Accepted |
| [0012](0012-a-labeled-demonstration-runs-on-the-headset.md) | Judges run a labeled demonstration on the headset, rather than reach a hosted control plane | Accepted |
| [0013](0013-characters-are-bots-with-a-living-surface.md) | Characters are bots whose eyes, motion and light carry state | Accepted |
| [0014](0014-hand-interaction-through-the-interaction-sdk.md) | Open work in place with hands, through Meta's Interaction SDK | Accepted |
| [0015](0015-the-stage-stands-on-the-persons-desk.md) | The stage stands on the person's desk, found with MRUK and kept with a spatial anchor | Accepted |
| [0016](0016-a-person-chooses-a-runtimes-model-from-its-own-list.md) | A person chooses a runtime's model from the runtime's own list | Accepted |
| [0017](0017-pair-a-headset-over-the-local-network.md) | Pair a headset over the local network with a code, SRP and a pinned certificate | Accepted; built on the Mac and in every headset build |
| [0019](0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md) | The demonstration reads simulated understanding and evaluation sources through the real contracts and routes, and says so | Accepted |
| [0020](0020-a-project-works-in-one-host-approved-folder.md) | A project works in one folder the host approves, and clients never name a path | Accepted |
| [0021](0021-speech-becomes-a-draft-transcribed-on-the-mac.md) | Speech becomes a draft, transcribed on the Mac, that the person confirms like typed text | Accepted; `Hold to Talk` built in every Unity XR build |
| [0022](0022-agent-questions-reach-the-person.md) | An agent's questions reach the person, who answers them through the runtime's own surface | Accepted |
| [0023](0023-the-headset-interface-is-one-system-of-tokens-and-components.md) | The headset's interface is one system of tokens, components and render-checked rules | Accepted; panels superseded by [0026](0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md) and [0040](0040-the-headset-is-a-room-of-projects.md) |
| [0024](0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md) | The Mac's settings live in one file only its owner can write, and never start paid model use | Accepted; `settings.json` and `pnpm mac-setup` built |
| [0025](0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md) | Create's companion is a local model the control plane asks one reply at a time, and its exchange stays on the headset | Accepted |
| [0026](0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md) | The headset's interface is a game menu of places, sections and prompts, on one plane facing the eyes | Accepted; superseded in part by [0034](0034-the-workspace-is-one-continuous-gentle-curve-centred-on-the-eyes.md) and [0040](0040-the-headset-is-a-room-of-projects.md) |
| [0027](0027-the-headset-moves-only-to-answer-the-person-or-show-a-wait.md) | The headset moves only to answer the person or to show a wait | Accepted |
| [0028](0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md) | OpenCode runs inside a macOS sandbox of Halora's own | Accepted; shared closed profile via [0048](0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md) |
| [0029](0029-workspace-is-complete-without-characters-and-continues-in-a-native-panel.md) | Complete character-free workspace, optional deliberate agent placement and native continuity | Accepted; character placement extended by [0040](0040-the-headset-is-a-room-of-projects.md) |
| [0030](0030-workspace-requests-use-a-journaled-agent-tool-loop.md) | Open-ended workspace requests use a journaled model/tool/result loop with existing runtime authority | Accepted; control plane and headset `AskColumn` built |
| [0031](0031-record-bounded-native-tool-results-in-the-owner-journal.md) | Record bounded final native tool results as owner-journal evidence, with scoped read-only consumers | Accepted; built in the control plane and Codex adapter |
| [0032](0032-recover-the-workspace-when-it-leaves-view.md) | Recover the workspace when it leaves view | Accepted |
| [0033](0033-approvals-declare-whether-commands-leave-the-sandbox.md) | An agent app declares whether approving a command can let it leave the sandbox | Accepted |
| [0034](0034-the-workspace-is-one-continuous-gentle-curve-centred-on-the-eyes.md) | The workspace is one continuous gentle curve centred on the eyes | Accepted; superseded in part by [0040](0040-the-headset-is-a-room-of-projects.md) |
| [0035](0035-a-recorded-real-run-lets-anyone-try-halcyonic-before-connecting-a-computer.md) | A recorded real run lets anyone try Halora before connecting a computer, and plays before the demonstration | Accepted; `pnpm judge:record` and `HalcyonicRecordedRun.json` built |
| [0036](0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md) | Halora undoes a task's changes itself, from checkpoints it keeps outside the project | Accepted; checkpoint store, pruning, `HistoryColumn` and restore review built |
| [0037](0037-new-vr-apps-start-from-a-host-vetted-web-starter.md) | New VR apps start from a web starter the host vets, and their code runs only in the headset's browser | Accepted; `iwsdk-vr` starter, headless Chrome check, loopback preview and Mac play viewer built |
| [0038](0038-the-mac-observes-each-turns-file-changes-itself.md) | The Mac observes each turn's file changes itself | Accepted; Mac file observer, `FileChangeColumn` and `Watch It Work` built |
| [0039](0039-the-projects-gallery-reads-a-catalog-from-omnorium.md) | The projects gallery reads the person's catalog from Omnorium, and Halora never signs in to GitHub | Accepted; `GET /api/catalog`, `Send to GitHub` and `Publish to GitHub Pages` built |
| [0040](0040-the-headset-is-a-room-of-projects.md) | The headset is a room of projects: a gallery home, one screen per project, and moments from the task's character | Accepted; room places, up to three open project screens, bars, moments and reading surfaces built |
| [0041](0041-a-new-apps-agent-is-confined-to-its-project.md) | A new app's agent is confined to its project | Accepted; extended across runtimes by [0048](0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md) |
| [0042](0042-a-person-may-run-a-task-on-a-hosted-model-with-their-own-key.md) | A person may run a task on a hosted model, with their own key, when they turn it on | Accepted; provider declarations ([0061](0061-a-hosted-provider-is-one-declaration-file-the-host-reads.md)), setup-page keys and Seatbelt key gates built |
| [0043](0043-a-person-may-let-a-tasks-agent-work-without-asking-where-its-runtime-keeps-it-in-bounds.md) | A person may let a task's agent work without asking, where its runtime keeps it in bounds | Accepted; built for Codex, confined OpenCode and plan mode |
| [0044](0044-a-failed-check-goes-back-to-its-agent-on-its-own-within-a-cap.md) | A failed check goes back to its agent on its own, within a cap | Accepted |
| [0045](0045-each-task-works-in-a-copy-of-its-own.md) | Each task works in a copy of its own, and its work reaches the project only when the person adds it | Accepted; per-task copies, conflict checks, `Add to Project` and per-task checks/previews built |
| [0046](0046-a-restart-re-attaches-each-tasks-session-where-the-runtime-keeps-it.md) | A restart re-attaches each task's session where the runtime keeps it | Accepted; built for Claude Code, Codex and OpenCode |
| [0047](0047-pairing-starts-from-a-page-on-the-mac.md) | Pairing starts from a page on the Mac, and the listener turns on without a restart | Accepted; Mac pairing page and headset `PairingCards` built |
| [0048](0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md) | Every agent runs inside one closed sandbox of Halora's, whatever its runtime | Accepted; `packages/seatbelt` confines Codex, OpenCode, Claude Code and ACP agent apps on macOS |
| [0049](0049-a-persons-world-is-a-project-whose-description-the-mac-reads.md) | A person's world is a project whose description the Mac reads and the headset draws | Accepted; `world` starter, Mac reader, `WorldScore` and development-build `WorldView` built |
| [0050](0050-a-start-restores-the-projection-from-a-checkpoint-and-folds-only-the-journal-after-it.md) | A start restores the projection from a checkpoint and folds only the journal after it | Accepted |
| [0051](0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md) | A paired device changes a project's files or folder only with the digest of the review it read | Accepted |
| [0052](0052-a-preview-reaches-a-paired-headset-through-a-loopback-relay.md) | A preview reaches a paired headset through a relay on the headset's loopback address | Accepted; Mac relay, `PreviewRelays`, `HeadsetRelay` and `PreviewRelayService` built |
| [0053](0053-the-macs-setup-runs-from-a-page-beside-pairing.md) | The Mac's setup runs from a page beside pairing, and a changed setting applies by a restart the page offers | Accepted |
| [0054](0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md) | What was made is kept and shared only by the person's press | Accepted; `Show in Finder`, `Save a Copy`, `Share a Link`, `Share on This Wi-Fi` and `ShareColumn` built |
| [0055](0055-a-projects-own-check-is-chosen-by-the-person-and-confined-as-an-agent.md) | A project's own check is chosen by the person and runs confined as an agent is | Accepted; `OwnCheck` and `ChooseOwnCheck` built |
| [0056](0056-an-instruction-sent-during-a-turn-waits-for-it-on-a-runtime-that-cannot-steer.md) | An instruction sent during a turn waits for it, on a runtime that cannot steer | Accepted |
| [0057](0057-a-projects-secrets-are-kept-on-the-mac-and-given-to-a-tasks-commands-at-its-start.md) | A project's secrets are kept on the Mac and given to a task's commands at its start | Accepted |
| [0058](0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md) | An instruction's pictures come from the project's folder, and reach the runtime as the bytes the host read | Accepted |
| [0059](0059-a-projects-own-rules-reach-a-task-only-as-the-person-read-and-chose-them.md) | A project's own rules reach a task only as the person read and chose them | Accepted |
| [0060](0060-the-computer-says-what-can-be-done-here.md) | The computer says what can be done here, and buttons, saying it and one review read that one list | Accepted |
| [0061](0061-a-hosted-provider-is-one-declaration-file-the-host-reads.md) | A hosted provider is one declaration file the host reads, never code | Accepted |
| [0062](0062-a-tasks-spending-limit-is-the-apps-own-stop-handed-only-what-is-left.md) | A task's spending limit is the agent app's own stop, handed only what is left of it | Accepted |
| [0063](0063-a-project-leaves-the-list-and-comes-back-by-its-folder.md) | A project leaves the list and comes back by its folder | Accepted |
| [0064](0064-work-inside-a-confined-apps-own-copy-does-not-ask.md) | Work inside a confined app's own copy does not ask | Accepted |
| [0065](0065-a-request-too-long-to-show-whole-is-denied-by-the-host-with-its-reason.md) | A request too long to show whole is denied by the host, with its reason | Accepted |
| [0066](0066-a-persons-photo-or-video-goes-into-the-projects-folder-as-a-copy-without-its-place.md) | A person's photo or video goes into the project's folder as a copy without its place, with stills the host made | Accepted |
| [0067](0067-agent-apps-that-speak-acp-are-directed-through-one-adapter.md) | Agent apps that speak the Agent Client Protocol are directed through one adapter | Accepted; Antigravity 1.5.10, Kiro 3.4.0 and Cursor 2026.11.13 profiles built |
