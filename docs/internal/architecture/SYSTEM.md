# System

How Halcyonic is built today, what depends on what, and what is not built yet.

## Components

| Component | Location | Status |
| --- | --- | --- |
| Contracts: wire schemas, validators, generated JSON Schema and C# bindings | `packages/contracts` | Built |
| Domain: projection, status and attention rules, command admission | `packages/domain` | Built |
| Runtime port: adapter interface, capability checks, clocks | `packages/runtime-core` | Built |
| Mock runtime: scripted scenarios, synthetic | `packages/integrations/mock` | Built |
| Control plane: journal, commands, REST, WebSocket, CLIs | `apps/control-plane` | Built; loopback, and an opt-in TLS listener for paired devices |
| Scenarios and recorded traces | `fixtures/` | Built |
| The XR client's demonstration, recorded by the control plane from mock scenarios with a branch for every answer a person can give, and its understanding and evaluation answers read through its routes from simulated stand-ins for Salidium and Seorak | `apps/xr/Assets/Halcyonic/Resources` | Built; [ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md), [ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md) |
| Architecture boundary tests | `tooling/` | Built |
| XR client core: realtime session, client projection, character and workspace presentation, steering, recorded demonstration, room placement choices, pairing and pinned transports (C#) | `apps/xr/Packages/com.halcyonic.client` | Built, tested on .NET; see [XR_CLIENT.md](XR_CLIENT.md) |
| XR client Unity layer (Unity, OpenXR, Meta XR SDK, MR Utility Kit) | `apps/xr` | Placeholder characters that run on a Meta Quest 3 against a live control plane. The layer includes the workspace (peek, open, act and collapse by hand, with its Understanding and Evaluation sections) and the room (passthrough, the characters on the person's desk, kept with a spatial anchor). Characters, the workspace and the room have dated Quest sessions in [quest-3-device.md](../validation/quest-3-device.md), from 2026-09-29 through a partial menu session on 2026-10-04. Wi-Fi pairing is verified off the headset only ([network-pairing.md](../validation/network-pairing.md)); the pairing panel is in every build since 2026-11-10, release builds included. The Simulator renders nothing on the development Mac |
| Claude Agent runtime (Claude Code through the Agent SDK) | `packages/integrations/claude-code` | Built; registered when enabled |
| OpenCode runtime (v2 server API, pinned 2.0.18) | `packages/integrations/opencode` | Built; registered when its binary is configured |
| Codex runtime (app-server, stable surface, pinned 0.157.0) | `packages/integrations/codex` | Built; registered when its binary is configured |
| Halcyonic's closed-by-default Seatbelt profile for one execution's folder, shared by the runtimes that confine their agents (BOX-3b) | `packages/seatbelt` | Built; used by Codex and OpenCode on macOS, and by Claude Code in a new app's project |
| Salidium client: consumer contract v1, understanding per execution | `packages/integrations/salidium` | Built, exercised against released daemons and re-verified against Salidium 0.8.3 (`@salidium/consumer-contract` 1.1.0, [record](../validation/salidium-consumer-contract.md)); optional 1.1 revision anchors and repository paths. Salidium's experimental project-map and execution-links v0 are source-verified but not consumed ([record](../validation/salidium-project-map.md)) |
| Seorak client: integration API v1, evaluation per execution and account-wide provider usage limits | `packages/integrations/seorak` | Built; evaluation verified against `seorak` 0.3.0, usage limits read live once against an unreleased Seorak build (2026-09-30) |
| Omnorium client: catalog contract v1 (`omnorium.catalog` and `phat-porcupine.catalog`), the person's catalog for the projects gallery, with discovery the instance proves | `packages/integrations/omnorium` | Built and tested against a stand-in; checked against Omnorium's published v1 types and fixtures, accepting `~/.omnorium` and `~/.phat-porcupine`, `omnorium.catalog` and `phat-porcupine.catalog`, and `omc_` and `ppc_` credentials; the control plane reads it for `GET /api/catalog` once its credential is set up; not yet read live ([record](../validation/omnorium-catalog.md)) |
| Device pairing and serving paired devices on the local network: SRP with a code, pinned TLS, per-device credentials, the pairing page on the Mac (ADR 0047), `pnpm pair` and `pnpm devices`, the Mac announcing itself while a window is open and the client core finding it (ADR 0017, amendment of 2026-11-11); the setup page beside it, which runs `pnpm mac-setup`'s checks, actions and pinned downloads on the person's press and offers Restart to Apply (ADR 0053) | `apps/control-plane/src/network`, the client core, `apps/xr/Assets/Halcyonic/Pairing` | Built, verified off the headset; the setup page tested with a faked runner and a scratch Mac, no real download pressed yet; [ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md), [SECURITY.md](SECURITY.md) |
| Voice: a held clip of speech becomes a draft, transcribed on the Mac by whisper.cpp and tidied (hesitation dropped, Halcyonic's and the projects' names spelled as written); hold to talk in every build, offered while a computer is connected (ADR 0021, amended 2026-11-11) | `apps/control-plane/src/speech`, the client core (`SpeechClip`, `VoiceText`), `apps/xr/Assets/Halcyonic/Workspace/HoldToTalk.cs` | Control plane verified on this Mac with synthetic speech; client core tested against a real control plane; the headset's capture built and rendered, not run on a Quest; [ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md), [record](../validation/voice-transcription.md) |
| Undo: checkpoints of project folders before a task, after each turn and before a restore, and a restore the host checks file by file; a project's history read in pages (its versions with their turn and changed files, the ones not kept, its restores) and any version compared with its folder as it is now; a project's old checkpoints let go by a rule that keeps every one still needed; off unless `HALCYONIC_GIT_BIN` is set | `apps/control-plane/src/checkpoints` | Built and tested against the system's git in temporary folders; [ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md). Run with a real runtime on 2026-11-03 (Codex on a local model, a restore between two turns: [local-models.md](../validation/local-models.md)); not yet measured on a large project; the history read tested against the system's git, its headset screen DEVICE's |
| Starters: a new project's folder begins with a vetted starter's files and a link to its shared, read-only dependencies | `starters/` (tracked), `apps/control-plane/src/starters.ts` | Built and tested; the Immersive Web SDK VR starter is tracked, its dependencies installed on the Mac with the npm commands `pnpm mac-setup starter` prints, from the public registry with no install scripts, and recorded by it once every package is the version the lockfile pins ([iwsdk-starter.md](../validation/iwsdk-starter.md)). Its input layer (`template/src/play-input.ts`) lets every game play with a mouse and keyboard and in VR: a click, a drag, W A S D and Space tried in a sandboxed headless browser on this Mac; controllers and hands not yet tried on the headset; [ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md). A starter's `starter.json` may also declare what its check refuses beyond building and drawing (`check_refuses`; the VR starter refuses its original's share picture, ADR 0044), and the failed check goes back to the agent in the starter's words. A VR version of a project (`project.make_vr_version`) begins from the starter whose `starter.json` says it makes VR versions (`"vr_version"`, naming its own instruction file, `starters/iwsdk-vr/vr-version.txt`; no starter is named in code since 2026-11-12) and is made beside the original, which its agent reads but cannot change, without the original's `.git` or secret-shaped files: built and tested on this Mac (command, journal, the real profile on marker files); not yet run with a model or driven from the headset (M1.8c-1) |
| A person's world: a project begun from the `world` starter holds one description file, `world.json` (format 2: its scene, how far night has come, each of the scene's lights with a brightness and a tint, the haze, and which of the scene's places is which project's; data only; each scene's manifest in `starters/world/scenes/` names its lights and places), which the Mac reads and validates itself after each turn and carries on the project's view | `packages/contracts/src/world.ts`, `apps/control-plane/src/observer/world-reader.ts`, `starters/world/` | Contract, the Mac's reading, the event, the view and the starter built and tested with a stand-in runtime; run once end to end with a local model (a spoken change to the night); the headset re-checks it and composes its sound in code (`Runtime/WorldOf.cs`, `WorldScore.cs`), and shows a scene rendered offline in Blender as separate light layers on a depth-shaped panorama, each light, the night, the air and each place mixed live by the description (`apps/xr/Assets/Halcyonic/World/`, development builds only, checked in Editor renders); a world made before format 2 is brought forward once by the Mac; the build holds no scene's pictures yet, so Virtual Space is still the engine's stock sky; [ADR 0049](../decisions/0049-a-persons-world-is-a-project-whose-description-the-mac-reads.md) |
| Checks and previews of a new app: type check, build, the built copy opened for 10 s in a sandboxed copy of chrome-headless-shell, started through Halcyonic's launcher so it has none of the app's macOS permissions (S1) (exceptions, console errors, whether anything is drawn), and the checked copy served on loopback | `apps/control-plane/src/preview` | Built and tested; the check run end to end on this Mac against an installed starter and Chrome (opt-in tests); not yet driven from the headset, and "drawn" is not "works" ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)) |
| A project's own check, for a project not made from a starter: scripts of its own `package.json` (`test`, `check`, `typecheck`, `lint`, `build`) the person chose, run by the host inside the agents' closed Seatbelt profile, what each exited, and what the check itself wrote | `apps/control-plane/src/own-check` | Built and tested on this Mac with npm in real runs inside the profile (a pass, a failure sent back, a never-passed and an already-failing check not sent, a changed command not run, a refused network, write and port); pnpm probed; not yet driven from the headset, and "passed" is only the scripts' exits ([ADR 0055](../decisions/0055-a-projects-own-check-is-chosen-by-the-person-and-confined-as-an-agent.md)) |
| Playing a checked game on the Mac: the copy the preview serves runs in the same contained chrome-headless-shell, through the launcher, its frames streamed to a viewer server of its own on 127.0.0.1 (47940 to 47949) and only mouse and key input sent back | `apps/control-plane/src/preview/play.ts`, `play-viewer.ts` | Built and tested with a stand-in browser (start, frames through the viewer, input filter, every stop path, a crash's leftover stopped at start; the viewer's code, cookie, Host and Origin rules over real HTTP and WebSocket); the browser's profile and settings probed on this Mac; the viewer page (P-B) shows the frames, takes a mouse and keys, comes back after a reload within 5 s, says why a game ended and whether it plays sound; an opt-in test runs the real browser end to end ([iwsdk-starter.md](../validation/iwsdk-starter.md)) (P-A, P-B, [ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)) |
| Keeping and sharing what was made: Show in Finder, Save a Copy (a zip in Downloads without the files the secret-file list names), and a checked game shared at a temporary public link (the pinned tunnel) or on this Wi-Fi (a listener on the Mac's own address passing only reads to the preview), each by the person's press; which ways work (`GET /api/sharing`) and a live share's code to scan (`GET /api/projects/:project_id/share/code`) for the headset ([ADR 0054](../decisions/0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md)) | `apps/control-plane/src/checkpoints/keeping.ts`, `apps/control-plane/src/preview/share.ts`, `share-code.ts` | Built and tested off the headset; the headset's cards pending |
| Project secrets: a key typed on the Mac's setup page, kept in the data folder by name, given to the commands of a task started after it on a runtime that declares `secret_environment` (Claude Code, OpenCode, Codex), and taken out of what runtimes say (ADR 0057) | `apps/control-plane/src/secrets`, `network/setup-page` | Built; tested with stand-in runtimes, the fake Claude Code, and OpenCode and Codex end to end on the pinned binaries; a paired headset's write-only route built, not yet drawn on it |
| What each agent run used: each turn's tokens as its agent app counts them, and the app's own cost estimate where it gives one, journaled as `runtime.usage.reported` and summed on the execution's view and the project's (`usage`); Claude Code, OpenCode and Codex report it, Codex with no money | `packages/integrations/*`, `packages/domain` | Built and tested: unit tests from each app's types and captured streams, and a run of the bundled Claude Code CLI and the pinned Codex binary against fake providers ([agent-usage.md](../validation/agent-usage.md)); not yet shown on the headset |
| The Mac's reading of each turn's file changes: created, changed and deleted files with line counts, for every execution started in a folder | `apps/control-plane/src/observer` | Built and tested with a stub runtime, and run with a real runtime on 2026-11-03 (the same Codex run, [local-models.md](../validation/local-models.md)), which showed a restore between two turns credited to the second turn until the reading before a turn from rest (OBS); not yet shown on the headset; [ADR 0038](../decisions/0038-the-mac-observes-each-turns-file-changes-itself.md) |
| Remote relay | none | Not started; see [SECURITY.md](SECURITY.md) |

## Dependency rules

```text
apps/control-plane ──> integrations/mock, claude-code, opencode, codex ──> runtime-core ──> contracts ──> typebox
        │               integrations/salidium, seorak ─────────────────────────────┘
        ├──> domain ─────────────────────────────────────────────────────────────────┘
        ├──> runtime-core
        └──> fastify, @fastify/websocket, node:sqlite

integrations/claude-code ──> @anthropic-ai/claude-agent-sdk (pinned)
integrations/codex, opencode, claude-code, apps/control-plane ──> seatbelt ──> runtime-core, node:crypto, node:fs, node:path
```

- `contracts` depends only on TypeBox. It is the vocabulary every other part shares.
- `domain` depends only on `contracts`. It is pure: no I/O, no clock, no storage.
- `runtime-core` depends only on `contracts`. It defines what a runtime adapter must be.
- Integrations depend on `runtime-core` and `contracts`, never on the control plane or on each
  other.
- `seatbelt` depends on nothing but `runtime-core`, for the secret-shaped files' rules, and Node's `crypto`, `fs`
  and `path`. It is the closed sandbox profile every runtime's agent runs inside on macOS
  ([ADR 0048](../decisions/0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md)), and the core packages
  never depend on it.
- The control plane is the composition root. It is the only place that chooses storage,
  transport and which adapters run.

pnpm's isolated `node_modules` makes an undeclared import fail at runtime, and
`tooling/architecture.test.ts` fails the build if a core package or `seatbelt` declares or imports
anything outside its allowlist.

The C# side mirrors this: the Unity layer depends on the client core, which depends on the
generated contracts, which depend only on Newtonsoft.Json. Neither C# package may reference
`UnityEngine` (`noEngineReferences`), and XR clients reach only the control plane.

## Data flow

```text
client ── command ──> CommandService ── admit (domain) ──> Recorder ──> journal (SQLite)
                             │                                  │
                             │ dispatch                         ├──> Projection (domain)
                             ▼                                  │
                      RuntimeAdapter ── observations ──> Recorder └──> Publisher ──> WebSocket clients
```

1. A command arrives over REST or WebSocket and is validated against the contract.
2. `admitCommand` decides, purely from the projection and the declared runtime capabilities,
   whether it may run. The result is journaled as `command.accepted` or `command.rejected`.
3. Accepted commands are dispatched. Local ones (create project, create workstream) complete
   immediately. Runtime ones wait for the adapter to confirm, bounded by a timeout.
4. Adapters report what happens as normalized observations, which become `runtime.*` events.
5. `Recorder` is the only write path. It validates each event, appends it, applies it to the
   in-memory projection and publishes the changed entities, as one synchronous step.
6. When checks are set up on the host and a turn in a starter project, or in a project whose own
   check the person chose ([ADR 0055](../decisions/0055-a-projects-own-check-is-chosen-by-the-person-and-confined-as-an-agent.md)), ends with file changes
   (`execution.files.observed` with at least one file created, changed or deleted), the control
   plane submits `project.check` on its own (`received_via: 'internal'`), one check at a time per
   project, waiting if a check or another task in the project is still running (A1). When that
   check fails and exactly one task's turn changed the files, the control plane sends the failure
   back to that task's agent as its next instruction (`received_via: 'internal'`), up to two tries,
   journaling each try and why it stopped; the person's Stop or their own words end it at once
   ([ADR 0044](../decisions/0044-a-failed-check-goes-back-to-its-agent-on-its-own-within-a-cap.md)). An instruction the person sends
   while a turn runs, to a runtime that cannot steer, waits for the turn (at most three), is sent
   after a turn that completed and fails unsent after any other end, a Stop or a restore; while one
   waits, the check and the fix loop wait too
   ([ADR 0056](../decisions/0056-an-instruction-sent-during-a-turn-waits-for-it-on-a-runtime-that-cannot-steer.md)).
   A project's own check is sent back only when the run before the turn passed, and never when a
   script timed out, met the sandbox's refusal, or its command changed.
   An instruction may carry up to four pictures of the project's folder, for a runtime that declares
   `takes_pictures` (Claude Code, Codex, OpenCode): the host reads each once, refuses one that is not a
   whole PNG, JPEG or WebP, and hands the runtime those bytes, never a path; the journal keeps each
   picture's digest, never its bytes
   ([ADR 0058](../decisions/0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md)).
   A project's own `AGENTS.md` (else `CLAUDE.md`) reaches a task only after the person read it and chose it:
   then the host puts it before each task's first instruction, marked as the project's words, the same for
   every runtime, while the file still reads as chosen
   ([ADR 0059](../decisions/0059-a-projects-own-rules-reach-a-task-only-as-the-person-read-and-chose-them.md)).
7. Before starting a turn on a local `ollama/<model>` execution, if no active execution in
   Halcyonic is already using that model, the control plane reads `GET /api/ps` on loopback Ollama
   (`127.0.0.1:11434`); when the execution's turn ends (or fails, is interrupted, becomes unknown
   or the control plane closes) and no other active execution uses that model, it sends
   `POST /api/generate` with `{ model, keep_alive: 0 }` only when `/api/ps` had answered and showed
   the model was not already loaded before Halcyonic started using it (UL1).

## Process model

One Node.js process. The journal (`node:sqlite`) is synchronous, so append, projection and
publication happen without interleaving: a reader never sees a journal and projection that
disagree, and a WebSocket client subscribing in the same step as its snapshot cannot miss an
event. Current state is held in memory and rebuilt from the journal at startup
([ADR 0002](../decisions/0002-append-only-journal-with-rebuilt-projection.md)): from a checkpoint
of the projection kept beside the events, then only the events after it, when the checkpoint was
written for this journal by this code at a position the journal still holds with the same event
there and its bytes are unchanged; otherwise from the whole journal. A checkpoint is written at a
clean close and every 50,000 events; the journal itself is never cut
([ADR 0050](../decisions/0050-a-start-restores-the-projection-from-a-checkpoint-and-folds-only-the-journal-after-it.md)).

On startup the control plane also reconciles: every execution that was in flight, and every
command still awaiting a runtime, is recorded as unknown, because the start stopped every runtime
process the last control plane left, and with it any turn that was running. So is one already
unknown for another reason, a lost connection or a start whose outcome is unknown: it is now unknown
because of the restart, which the rule for a busy project reads as stopped, so it no longer holds
Undo, a check or the automatic check back for good (U5). Then each execution in flight is
re-attached where its runtime keeps its session (`resumes_after_restart`): the adapter finds the
session again by its native id with the bounds it started with, reports it observable again and the
cut turn ended, and the task takes instructions again; an execution at rest is re-attached by the
next action on it. One that cannot be, on a runtime that keeps no session, or one its runtime
refuses or does not answer for, is recorded `session_lost`, which the busy rule also reads as
stopped, and an action on it fails having done nothing
([ADR 0046](../decisions/0046-a-restart-re-attaches-each-tasks-session-where-the-runtime-keeps-it.md)).
Nor does a turn the restart cut short hold the project's later checks unknown: a check recorded
after the restart starts current, while one recorded before it stays unknown, since the cut turn
may have changed files unread (#13).

## Ports

What listens on the Mac, and where. Test suites use their own ports, on loopback only. Every port
below is the 47800 family's. A control plane on another `HALCYONIC_PORT` keeps the same layout moved to
its own port, its previews', viewers' and shares' ranges 1000 higher (`portFamily`, ADR 0047 amended):
on 47900, 47901, 47902 to 47909, 49030 to 49039, 49040 to 49049 and 49070 to 49079. So no second plane
takes one of 47800's, and none reaches the ports the system hands out (49152 up). Each port is found
free in its range and recorded where a client reads it.

| Ports | Interface | What |
| --- | --- | --- |
| 47800 (`HALCYONIC_PORT`) | loopback | The control plane: HTTP and WebSocket |
| 47801 (`HALCYONIC_NETWORK_PORT`, else the plane's port plus 1) | `HALCYONIC_NETWORK_HOST` | The TLS listener for paired devices, when turned on |
| 47802 to 47809 | loopback | The pairing page ([ADR 0047](../decisions/0047-pairing-starts-from-a-page-on-the-mac.md)): the control plane's port plus 2 to plus 9, first free, so a plane on another port keeps to its own; a plane on port 0 gets a free port for its page too |
| 47930 to 47939 | loopback | A project's preview, and a check's own server |
| 47940 to 47949 | loopback | The viewer of a game played on the Mac |
| 47970 to 47979 | the Mac's own Wi-Fi address, never every interface | A game shared on this Wi-Fi, while it is shared ([ADR 0054](../decisions/0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md)) |

## Configuration

Environment variables, all optional:

| Variable | Default | Meaning |
| --- | --- | --- |
| `HALCYONIC_HOST` | `127.0.0.1` | Must be a loopback address; anything else is refused |
| `HALCYONIC_PORT` | `47800` | HTTP and WebSocket port |
| `HALCYONIC_NETWORK_HOST` | none | An IP address, such as `0.0.0.0` for every interface, turns on the TLS listener for paired devices ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)); unset, nothing listens beyond loopback |
| `HALCYONIC_NETWORK_PORT` | `HALCYONIC_PORT` plus 1 (`47801`) | The network listener's port; must differ from `HALCYONIC_PORT` |
| `HALCYONIC_DATA_DIR` | `~/.halcyonic` | Journal, access token |
| `HALCYONIC_TASK_COPIES` | `1` | Every task whose runtime is held to its folder works in its own copy of its project's folder ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)), and nothing its agent does reaches the project until the person adds it, on the headset's Add to Project or the Mac's. `0` turns copies off: every task works in its project's folder, one at a time. On by default since the headset offers Add to Project |
| `HALCYONIC_TASKS_DIR` (also in the settings file) | the data folder's path plus `-tasks` (`~/.halcyonic-tasks` beside `~/.halcyonic`), so a plane on another data folder never reaches these copies | Every task's own copy of its project's folder, `<project id>/<task id>/<folder name>` ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)); beside the data folder, never inside it or around it (startup refuses either); mode 0700; only the runtimes are given it, never a client. Copies are made where undo is on (`HALCYONIC_GIT_BIN`); at start, before listening, the host removes nothing there that its journal does not hold, only counts it (logged at warn), and records each of its own copies whose folder is gone (`workstream.copy_missing`) |
| (none) | the account's `~/Downloads` for the plane on `~/.halcyonic`, else the data folder's path plus `-downloads` (made at start) | Where Save a Copy writes, so a test's or a scratch plane's press never writes where the person keeps their files |
| `HALCYONIC_LOG_LEVEL` | `info` | `fatal` to `trace`, or `silent` |
| `HALCYONIC_EXIT_ON_STDIN_END` | `0` | `1` shuts the control plane down, as SIGTERM does, when its standard input ends; for a launcher that holds that input open, so that the control plane stops when the launcher exits, however it exits |
| `HALCYONIC_COMMAND_TIMEOUT_MS` | `30000` | How long to wait for a runtime to confirm an action |
| `HALCYONIC_MOCK_SCENARIOS_DIR` | `fixtures/scenarios` | Mock runtime scenarios |
| `SALIDIUM_HOME` | `~/.salidium` | Where Salidium publishes its discovery file, as Salidium itself resolves it |
| `OMNORIUM_HOME` | `~/.omnorium` | Where Omnorium publishes its discovery file, as Omnorium itself resolves it |
| `HALCYONIC_PROJECT_ROOTS` | none | Directories agents may work in, with everything below them, separated by `:`. Clients bind projects to a root or a folder directly inside one, and the host may make new folders there ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)); with none, no real runtime can start |
| `HALCYONIC_CLAUDE_AGENT` | `0` | `1` registers the Claude Agent runtime; it then needs `ANTHROPIC_API_KEY`, a cloud provider in the environment, or `<data dir>/anthropic-api-key`. A key stored from the setup page registers it with no switch (ADR 0053, amendment of 2026-11-12) |
| `HALCYONIC_CLAUDE_EXECUTABLE` | bundled | A Claude Code executable to use instead of the one the Agent SDK bundles |
| `HALCYONIC_AGENT_ENV` | none | Names of variables, separated by commas, copied into every launched agent's environment (for example `SSH_AUTH_SOCK`) |
| `HALCYONIC_OPENCODE_BIN` | none | Absolute path of the pinned OpenCode 2.0.18 binary; registers the OpenCode runtime |
| `HALCYONIC_CODEX_BIN` | none | Absolute path of the pinned Codex 0.157.0 native binary; registers the Codex runtime |
| `HALCYONIC_WHISPER_BIN`, `HALCYONIC_WHISPER_MODEL`, `HALCYONIC_WHISPER_VAD_MODEL` | none | Absolute paths of whisper.cpp's `whisper-cli`, its large-v3-turbo q5_0 model and its Silero voice activity model; all three or none. With them, `POST /api/transcriptions` turns clips of speech into drafts ([ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md)) |
| `HALCYONIC_BROWSER` (`HALCYONIC_BROWSER_SHA256`) | none | A chrome-headless-shell folder, by absolute path: the browser the check of a new app and a game played on the Mac run (S1). `pnpm mac-setup browser` finds it in Playwright's cache and records it in the settings file. Its hash is always the repository's pin (`pins.ts`), on Apple silicon only; `HALCYONIC_BROWSER_SHA256` may only repeat the pin, and any other value stops the start naming it. The host copies the folder into `<data dir>/browser/current` and runs only the copy, after checking the whole folder (every file and folder, no link) against the pin at every start, through the launcher; never the person's Chrome. Unset, a check type checks and builds but cannot pass, and `project.open_on_mac` is refused. `HALCYONIC_CHECK_CHROME` and `HALCYONIC_PLAY_BROWSER` are no longer used and stop the start with a message to run `pnpm mac-setup browser`. Checks and previews run on macOS only |
| `HALCYONIC_TUNNEL_BIN` | none | Absolute path of the pinned `cloudflared` 2026.10.7 binary for macOS on Apple silicon. With it, on macOS, and with previews on, Share a Link gives a checked game's preview a temporary public address ([ADR 0054](../decisions/0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md)). Its bytes must hash to the pin (`TUNNEL_PIN`); any other file turns sharing off with a warning. It runs under Seatbelt, writing only `<data dir>/share-home` and reading nothing else of the data folder. The settings file may hold it: the setup page's Get Link Sharing downloads the checked release into `<data dir>/tools/cloudflared-2026.10.7/` (the archive checked by its SHA-256 before it is unpacked) and `pnpm mac-setup tunnel` records it only when the program matches the pin |
| `HALCYONIC_GH_BIN` | none | The person's own `gh`, by absolute path, never looked up on PATH: Send to GitHub opens a pull request with it, and makes the repository on a first send of a folder that has none, run in their environment with their sign-in, for their reviewed command only [ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md). Unset, a send pushes only. Send to GitHub itself needs `HALCYONIC_GIT_BIN`. The settings file may hold it |
| `HALCYONIC_GIT_BIN` | none | Absolute path of the git Halcyonic's undo runs (`/usr/bin/git` on a Mac); unset, undo is off: no checkpoint is taken and `project.restore` is refused ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)) |
| `HALCYONIC_COMPANION_MODEL` | none | The local Ollama model Create's companion asks, exactly as Ollama lists it; a name with a `cloud` tag is refused. Unset, the companion is off and Help me figure it out asks the fixed questions ([ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)) |
| `HALCYONIC_COMPANION_OLLAMA_URL` | `http://127.0.0.1:11434` | Where Ollama answers: `http://` on a loopback address with a port and nothing else; needs `HALCYONIC_COMPANION_MODEL` |

The assistant's requests (Ask, [ADR 0030](../decisions/0030-workspace-requests-use-a-journaled-agent-tool-loop.md)) run on the person's chosen local model: the `model` of Halcyonic's own OpenCode settings in `HALCYONIC_OPENCODE_CONFIG_HOME`, which `pnpm mac-setup local-model` writes. Where none is set, or it can't be read, they run on `HALCYONIC_COMPANION_MODEL`; with neither, the assistant is off. A hosted provider the product declares, once its key is stored (its private file in the data folder; the environment is never read for this), only offers its models: the assistant runs on one when the person presses Use Model (`assistant.model.choose`, from the host's list `assistant_models`), a choice the journal remembers. A refused or missing chosen key is said in words, never sent again while the plane runs, and the host offers this computer's model as one press. (ADR 0030, amendment of 2026-11-12). Every hosted provider is one declaration, `providers/<id>/provider.json`, read once at the plane's start into the host's one list (`HostProviders`, [ADR 0061](../decisions/0061-a-hosted-provider-is-one-declaration-file-the-host-reads.md)); a declaration that does not hold stops the start. The model rows and the assistant read it; Add Key, the key gate and the held secrets move onto it in the same stack. New Project's quick questions stay on the companion's model.

Files in the data directory besides the journal and the access token, all optional and mode 0600:
`salidium-credential`, the consumer credential the owner created for Halcyonic, and
`omnorium-credential`, the consumer credential the owner created in Omnorium for Halcyonic, and
`seorak-credential`, the integration credential the owner issued for Halcyonic, and
`anthropic-api-key` (the file Anthropic's provider declaration names; the runtime reads no file by a name of its own), used by the Claude Agent runtime when `ANTHROPIC_API_KEY` is not set, written by the setup page's Add Key and turning that runtime on when it holds a key only its owner can read, and
`checkpoints/`, the store of Halcyonic's undo when it is set up, and `secrets/<project>/<name>`, each
project secret's value the person typed on the setup page, mode 0600 in folders of 0700, never read by a
check, a copy or a share (ADR 0057), and `starters/<id>/`, each
starter's installed dependencies with `installed.json`, the SHA-256 of the lockfile they came from, and
`opencode-server.json`, `codex-server.json` and `claude-agent-processes.json`, the records of the
running OpenCode and Codex servers and Claude Code processes (no secrets), and `previews/`, those of
each preview's server and, while a check runs, of the check's own server (AUDIT S1), that let the next
start stop anything a crash left behind, `announcing.pid`, the `dns-sd` announcing the computer while a
pairing window is open (ADR 0017), ended by the next start when a crash left it, and `network-key.pem` and `network-certificate.pem`, the network
listener's TLS identity, created when the listener is first turned on; paired devices pin the
certificate, so deleting the two makes every device pair again.

## Toolchain

Node.js 24.15 or newer runs the TypeScript sources directly (type stripping is stable in Node 24;
`node:sqlite` is a release candidate from 24.15). TypeScript 7 type-checks only. Tests use
`node:test`; lint and format use Biome; pnpm 11 manages the workspace. Reasons and evidence:
[ADR 0004](../decisions/0004-control-plane-stack.md). The C# packages are compiled and tested
outside Unity with the .NET 10 SDK ([ADR 0008](../decisions/0008-engine-independent-csharp-client-core.md)).

## Not built, and deliberately so

- **Snapshots of the projection on disk.** Rebuilding from the journal is fast at local scale;
  add snapshots when startup time says so.
- **Per-client subscriptions.** Every client receives every event; fine for one person's work.
- **OpenAPI document.** REST responses are typed by the contracts; generate OpenAPI 3.1 when the
  XR client needs generated HTTP bindings.
- **Separate persistence, realtime or security packages.** They live in the control plane until a
  second consumer needs them.
