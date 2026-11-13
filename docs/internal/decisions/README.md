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
| [0002](0002-append-only-journal-with-rebuilt-projection.md) | Append-only journal with a projection rebuilt at startup | Accepted; amended by 0050 |
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
| [0026](0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md) | The headset's interface is a game menu of places, sections and prompts, on one plane facing the eyes | Accepted; main presentation superseded by 0040 |
| [0027](0027-the-headset-moves-only-to-answer-the-person-or-show-a-wait.md) | The headset moves only to answer the person or to show a wait | Accepted; amended 2026-10-08 |
| [0028](0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md) | OpenCode runs inside a macOS sandbox of Halcyonic's own | Accepted; amended 2026-10-08; one server per folder (BOX-3); reads closed with Codex's profile (BOX-3b) |
| [0029](0029-workspace-is-complete-without-characters-and-continues-in-a-native-panel.md) | Complete character-free workspace, optional deliberate agent placement and native continuity | Accepted; device acceptance pending; off by default superseded by 0040 |
| [0030](0030-workspace-requests-use-a-journaled-agent-tool-loop.md) | Open-ended workspace requests use a journaled model/tool/result loop with existing runtime authority | Accepted; built and run end to end on local models with a real runtime; all-work requests, quoted data and the reused context not yet shown on a real model; the headset shows no whole-project or all-work request yet; headset request UI and device evidence pending |
| [0031](0031-record-bounded-native-tool-results-in-the-owner-journal.md) | Record bounded final native tool results as owner-journal evidence, with scoped read-only consumers | Accepted design; device acceptance unverified |

| [0032](0032-recover-the-workspace-when-it-leaves-view.md) | Recover the workspace when it leaves view | Accepted direction; candidate thresholds require wearer validation |
| [0033](0033-approvals-declare-whether-commands-leave-the-sandbox.md) | An agent app declares whether approving a command can let it leave the sandbox | Accepted |
| [0034](0034-the-workspace-is-one-continuous-gentle-curve-centred-on-the-eyes.md) | The workspace is one continuous gentle curve centred on the eyes | Accepted as direction on Editor renders; superseded in part by 0040 |
| [0035](0035-a-recorded-real-run-lets-anyone-try-halcyonic-before-connecting-a-computer.md) | A recorded real run lets anyone try Halcyonic before connecting a computer, and plays before the demonstration | Accepted; no recorded run bundled yet |
| [0036](0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md) | Halcyonic undoes a task's changes itself, from checkpoints it keeps outside the project | Accepted; headset screens pending |
| [0037](0037-new-vr-apps-start-from-a-host-vetted-web-starter.md) | New VR apps start from a web starter the host vets, and their code runs only in the headset's browser | Accepted; control plane, preview and headset screens pending |
| [0038](0038-the-mac-observes-each-turns-file-changes-itself.md) | The Mac observes each turn's file changes itself | Accepted; headset build view pending |
| [0039](0039-the-projects-gallery-reads-a-catalog-from-omnorium.md) | The projects gallery reads the person's catalog from Omnorium, and Halcyonic never signs in to GitHub | Accepted; contract, link, gallery core, route and client built; live read not wired |
| [0040](0040-the-headset-is-a-room-of-projects.md) | The headset is a room of projects: a gallery home, one screen per project, and moments from the task's character | Accepted on a storyboard; the kit's tokens, the moment model, the screen and its bar, and the moment card built and checked in Editor renders; the screen and its bar placed in the room; the gallery, the reading surface, heads-up items and compact not yet; headset acceptance pending |
| [0041](0041-a-new-apps-agent-is-confined-to-its-project.md) | A new app's agent is confined to its project | Accepted; built for Codex on macOS, not yet run with a real model |
| [0042](0042-a-person-may-run-a-task-on-a-hosted-model-with-their-own-key.md) | A person may run a task on a hosted model, with their own key, when they turn it on | Proposed; nothing built |
| [0043](0043-a-person-may-let-a-tasks-agent-work-without-asking-where-its-runtime-keeps-it-in-bounds.md) | A person may let a task's agent work without asking, where its runtime keeps it in bounds | Accepted; contract, admission and Codex built, OpenCode and Claude Code refuse it; headset control pending |
| [0044](0044-a-failed-check-goes-back-to-its-agent-on-its-own-within-a-cap.md) | A failed check goes back to its agent on its own, within a cap | Accepted; control plane built and tested through the mock; headset view pending |
| [0045](0045-each-task-works-in-a-copy-of-its-own.md) | Each task works in a copy of its own, and its work reaches the project only when the person adds it | Accepted; contracts, views, admission, the copies made at a task's first start, adding, its conflicts and the fit built, run with Codex and OpenCode at once on 2026-11-10; on by default since the headset offers Add to Project (`HALCYONIC_TASK_COPIES=0` turns it off); a check of a task's own copy and the automatic check after a turn in it, which a failed check's fix follows, built |
| [0046](0046-a-restart-re-attaches-each-tasks-session-where-the-runtime-keeps-it.md) | A restart re-attaches each task's session where the runtime keeps it | Accepted; built for Claude Code, Codex and OpenCode |
| [0047](0047-pairing-starts-from-a-page-on-the-mac.md) | Pairing starts from a page on the Mac, and the listener turns on without a restart | Accepted; built and tested off the headset |
| [0048](0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md) | Every agent runs inside one closed sandbox of Halcyonic's, whatever its runtime | Accepted; the profile in `packages/seatbelt`, Codex and OpenCode confined on macOS, Claude Code in a new app's project (fake host only) |
| [0049](0049-a-persons-world-is-a-project-whose-description-the-mac-reads.md) | A person's world is a project whose description the Mac reads and the headset draws | Proposed; contract, the Mac's reading and the world starter built; the headset does not draw it yet |
| [0050](0050-a-start-restores-the-projection-from-a-checkpoint-and-folds-only-the-journal-after-it.md) | A start restores the projection from a checkpoint and folds only the journal after it | Accepted; built, measured at 50,000 and about a million events |
| [0051](0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md) | A paired device changes a project's files or folder only with the digest of the review it read | Accepted; control plane and client core built; the entry panel's read of the review is XR's |
| [0052](0052-a-preview-reaches-a-paired-headset-through-a-loopback-relay.md) | A preview reaches a paired headset through a relay on the headset's loopback address | Accepted; Mac relay, headset relay and foreground service built, tested off the headset; the action that opens it is DEVICE's, the manifest XR's |
| [0053](0053-the-macs-setup-runs-from-a-page-beside-pairing.md) | The Mac's setup runs from a page beside pairing, and a changed setting applies by a restart the page offers | Accepted; built and tested with a faked runner, not yet pressed for real |
| [0054](0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md) | What was made is kept and shared only by the person's press | Accepted; Show in Finder, Save a Copy, Share a Link and Share on This Wi-Fi built off the headset; headset pending |
| [0055](0055-a-projects-own-check-is-chosen-by-the-person-and-confined-as-an-agent.md) | A project's own check is chosen by the person and runs confined as an agent is | Accepted; control plane built and tested with npm inside the profile on this Mac; headset pending |
| [0056](0056-an-instruction-sent-during-a-turn-waits-for-it-on-a-runtime-that-cannot-steer.md) | An instruction sent during a turn waits for it, on a runtime that cannot steer | Accepted; built and tested with the mock runtime; not run with Claude Code; headset pending |
| [0057](0057-a-projects-secrets-are-kept-on-the-mac-and-given-to-a-tasks-commands-at-its-start.md) | A project's secrets are kept on the Mac and given to a task's commands at its start | Accepted; store, page, redaction and the three adapters built, the OpenCode and Codex end to end tests run on the pinned binaries; the write-only route a paired headset uses built, not yet drawn on the headset |
| [0058](0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md) | An instruction's pictures come from the project's folder, and reach the runtime as the bytes the host read | Accepted; built; each runtime's send tested end to end on a fake provider; no model run; headset pending |
| [0059](0059-a-projects-own-rules-reach-a-task-only-as-the-person-read-and-chose-them.md) | A project's own rules reach a task only as the person read and chose them | Accepted; built and tested with the mock runtime; headset pending |
| [0060](0060-the-computer-says-what-can-be-done-here.md) | The computer says what can be done here, and buttons, saying it and one review read that one list | Accepted; the assistant's part, the press, the confirm and the host's list as admission allows it today built and tested with a synthetic model; CORE's `offered()` and review not built; no model run; headset pending |
| [0061](0061-a-hosted-provider-is-one-declaration-file-the-host-reads.md) | A hosted provider is one declaration file the host reads, never code | Accepted; the shape, the place and the host's list built with the model rows reading it; the assistant, Add Key, the gate and the held secrets move onto it in the 1111-b110 stack |
| [0062](0062-a-tasks-spending-limit-is-the-apps-own-stop-handed-only-what-is-left.md) | A task's spending limit is the agent app's own stop, handed only what is left of it | Accepted; built for Claude Agent (start, re-attach, Carry On) and tested on the bundled CLI against a fake API; no real provider run; headset pending |
| [0063](0063-a-project-leaves-the-list-and-comes-back-by-its-folder.md) | A project leaves the list and comes back by its folder | Accepted; the Mac side built and tested with the mock runtime; headset pending |
| [0064](0064-work-inside-a-confined-apps-own-copy-does-not-ask.md) | Work inside a confined app's own copy does not ask | Accepted; the host's answers built for Claude Agent and OpenCode's session rules, tested with a scripted runtime; seen with OpenCode on the proving plane (run (b3)) not yet; headset pending |
| [0065](0065-a-request-too-long-to-show-whole-is-denied-by-the-host-with-its-reason.md) | A request too long to show whole is denied by the host, with its reason | Accepted; built and tested with a scripted runtime; the approvable bound stays 2000 until the headset's paged card is tried at length |
