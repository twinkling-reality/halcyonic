# Documentation map

Documentation for people and agents building **Halora** (in the `halcyonic` repository). People see
the product name **Halora** on the headset and on the Mac's pages; code keeps `halcyonic` in package
names (`@halcyonic/*`), C# namespaces (`Halcyonic.*`), Android/Unity package ids (`com.halcyonic.*`),
environment variables (`HALCYONIC_*`), and data paths (`~/.halcyonic`).

The repository is public, so everything tracked here may be published. Private material lives in
`docs/private/`, which git ignores (see below).

## Documentation standard

Tracked documents are public. `docs/internal/` is contributor and agent documentation, not a
confidential archive. Private priorities stay in `docs/private/`, which git ignores. Tracked files
may say that a private record exists. They must not quote it.

Write to the need in front of you. The distinction comes from Diátaxis
(https://diataxis.fr/start-here/): action for learning or for a task, and knowledge for lookup or
for understanding. Do not create four new directories.

| Need | Home | Write it as |
| --- | --- | --- |
| Learn the product and run it once | [Root README](../../README.md) | A short entry: intent, what this checkout does, how to start |
| Complete a task | [docs/internal/runbooks/](runbooks/) | Steps, prerequisites, and what failure looks like |
| Look up current behavior | [docs/internal/architecture/](architecture/) plus [product/WORDS.md](product/WORDS.md) and [product/STYLE.md](product/STYLE.md) | Facts a change must keep true |
| Understand why | [product/PRODUCT.md](product/PRODUCT.md) and [docs/internal/decisions/](decisions/) | The thesis, or one hard-to-reverse decision |
| See what was true on a date | [docs/internal/validation/](validation/) | Question, environment, method, result, and the limit of the check |
| See what is unresolved | [product/OPEN_QUESTIONS.md](product/OPEN_QUESTIONS.md) | The question and what would resolve it |
| See current priorities | `docs/private/ROADMAP.md` (local only, git-ignored) | Private. Never copied into a tracked file |

One fact has one home. Other documents link to it.

- Current behavior is what the code on this checkout does. The matching architecture or product
  reference states it. A plan, another branch, or a validation result is not current behavior.
- ADRs stay, including superseded ones. Canonical docs follow the accepted decision and link to the
  ADR.
- Evidence stays dated. Cite the record when a claim depends on a vendor, a device, or a past run.
- Domain words are Project, Workstream and Execution
  ([DOMAIN_MODEL.md](architecture/DOMAIN_MODEL.md)). Headset words are in
  [WORDS.md](product/WORDS.md). Synthetic examples say they are synthetic.
- Source comments explain why code exists, its invariants, and how it fails. Do not restate
  TypeScript types or C# signatures. Use a short TSDoc summary on a public TypeScript function when
  the behavior is not obvious (https://tsdoc.org/). Use a C# `summary` on a public type when the
  behavior is not obvious
  (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/recommended-tags).
  Never edit `packages/contracts/schema/` or `packages/contracts/csharp/` by hand.
- Change the canonical document in the same commit as the behavior. When an external claim is
  re-checked, add a dated validation note that says what was not run. Remove an open question when
  an ADR or validation record resolves it.

## Canonical documents

Canonical documents describe **current truth**: how the system works now and why. When the code
changes, the matching document changes in the same commit.

| Area | Document | Answers |
| --- | --- | --- |
| Product | [product/PRODUCT.md](product/PRODUCT.md) | What Halora is, is not, and must prove |
| Product | [product/OPEN_QUESTIONS.md](product/OPEN_QUESTIONS.md) | What is unresolved and must not silently become architecture |
| Product | [product/WORDS.md](product/WORDS.md) | How Halora speaks on the headset: its voice, a word for each state, the words a person sees |
| Product | [product/STYLE.md](product/STYLE.md) | How Halora looks on the headset: room glass, buttons, code colours, spatial ramp, typography, motion, sound and automated render checks |
| Product | [product/BRAND.md](product/BRAND.md) | The name people see, Halora, and its mark, colours, type and ground outside the headset's panels |
| Architecture | [architecture/SYSTEM.md](architecture/SYSTEM.md) | Components, dependency rules, what is and is not built |
| Architecture | [architecture/DOMAIN_MODEL.md](architecture/DOMAIN_MODEL.md) | Project, Workstream, Execution, statuses, attention, commands |
| Architecture | [architecture/EVENTS.md](architecture/EVENTS.md) | Journal, event envelope and catalog, provenance, versioning, traces |
| Architecture | [architecture/REALTIME.md](architecture/REALTIME.md) | REST endpoints and the WebSocket protocol |
| Architecture | [architecture/SECURITY.md](architecture/SECURITY.md) | Trust boundaries, controls, and what is not yet protected |
| Architecture | [architecture/INTEGRATIONS.md](architecture/INTEGRATIONS.md) | Runtime adapter contract, capabilities, integration boundaries |
| Architecture | [architecture/XR_CLIENT.md](architecture/XR_CLIENT.md) | The Unity client's layers, C# contracts, session, threading and verification |
| Operations | [runbooks/LOCAL_DEVELOPMENT.md](runbooks/LOCAL_DEVELOPMENT.md) | Running, setting up the Mac, pairing, replaying, recording, resetting |
| Operations | [runbooks/XR_DEVELOPMENT.md](runbooks/XR_DEVELOPMENT.md) | Installing Unity, opening the XR project, rendering checks, building APKs, running against the control plane |
| Operations | [runbooks/HEADSET_SESSION.md](runbooks/HEADSET_SESSION.md) | One session on a Meta Quest, in order, for what only the headset can show after the same work has passed on the Mac, plus `pnpm quest:check` and `pnpm quest:device` |

## Decision records

[decisions/](decisions/README.md) holds Architecture Decision Records for significant, hard to
reverse decisions. They are historical by design: a superseded ADR stays, marked superseded,
while the canonical documents move on.

## Validation records

[validation/](validation/) holds dated evidence that constrains the architecture: vendor surface
reviews, repository audits, platform facts, and hardware or runtime experiments. Each records the
question, environment and versions, method, evidence, result and consequence. External facts
change, so a validation record states when it was true; re-verify before relying on an old one.

### Runtimes, models and assistant

| Record | Subject |
| --- | --- |
| [acp-agents.md](validation/acp-agents.md) | Agent apps directed over the Agent Client Protocol (Antigravity 1.5.10, Kiro 3.4.0, Cursor 2026.11.13): manifests, permissions, per-launch settings homes, watchdog and setup-page install |
| [agent-app-start-reads.md](validation/agent-app-start-reads.md) | What each agent app reads beyond its project folder at startup, documented from vendor pages and verified source |
| [agent-apps-hosted-providers.md](validation/agent-apps-hosted-providers.md) | Confined agent apps reaching declared hosted providers only through per-launch Seatbelt key gates |
| [agent-plan-modes.md](validation/agent-plan-modes.md) | What each runtime offers that changes no file: Claude Code's plan mode, Codex's read-only sandbox, OpenCode's Plan agent |
| [agent-questions.md](validation/agent-questions.md) | How OpenCode, Codex and the Claude Agent SDK ask the person questions, and how each is answered, dismissed and interrupted |
| [agent-usage.md](validation/agent-usage.md) | Per-turn token and cost usage fields reported by Claude Code, OpenCode, Codex and Antigravity |
| [approval-levels.md](validation/approval-levels.md) | What each pinned runtime offers for working without asking (`never_ask`), and where confinement is required |
| [claude-code-capabilities.md](validation/claude-code-capabilities.md) | Claude Code CLI and Agent SDK surface and capability checks |
| [claude-code-connected-folders.md](validation/claude-code-connected-folders.md) | Claude Code on connected folders: key held in a loopback gate and host folders kept out of reach |
| [claude-code-key-gate.md](validation/claude-code-key-gate.md) | Confined Claude Code runs with zero provider key in the agent process, routed through the loopback key gate |
| [claude-code-project-settings.md](validation/claude-code-project-settings.md) | Which project-local files Claude Code loads under Halora, and isolation from untrusted repository hooks |
| [claude-code-sandbox.md](validation/claude-code-sandbox.md) | Bundled Claude Code CLI running whole turns inside `packages/seatbelt` |
| [claude-code-settings-isolation.md](validation/claude-code-settings-isolation.md) | Isolating Claude Code from wholesale personal settings while preserving verified Salidium and Seorak hooks |
| [claude-code-unasked.md](validation/claude-code-unasked.md) | Which Bash commands the bundled Claude Code CLI runs without asking in default mode |
| [codex-capabilities.md](validation/codex-capabilities.md) | Codex exec, SDKs, app-server stable surface and approval behavior |
| [codex-thread-environment.md](validation/codex-thread-environment.md) | Per-thread shell environment variables in Codex app-server and whether values persist on disk |
| [companion-model.md](validation/companion-model.md) | Where Create's companion runs on Ollama, memory and latency beside agent turns, and schema adherence |
| [hosted-assistant.md](validation/hosted-assistant.md) | Running the workspace assistant against declared hosted providers using a stored provider key |
| [local-models.md](validation/local-models.md) | Open models served by Ollama on the Mac through OpenCode, Codex and the control plane: flows, network, speed and memory |
| [meta-models.md](validation/meta-models.md) | Meta's Model API, Muse Code and Muse Glimmer from Meta's documentation: models, prices, limits, data terms and session protocol |
| [opencode-capabilities.md](validation/opencode-capabilities.md) | OpenCode v2 server API and event stream |
| [opencode-permissions.md](validation/opencode-permissions.md) | OpenCode permission rules, what they cover, and what still reaches the network without Seatbelt |
| [opencode-sandbox.md](validation/opencode-sandbox.md) | OpenCode inside Halora's macOS Seatbelt sandbox |
| [picture-and-file-input.md](validation/picture-and-file-input.md) | Image and file inputs accepted by each runtime and vision behavior on local models |
| [project-rules.md](validation/project-rules.md) | How each runtime reads `AGENTS.md`, `CLAUDE.md` and project settings, and host-mediated rule review |
| [recorded-tool-results.md](validation/recorded-tool-results.md) | Observed final native command and file-change fields, owner-journal privacy, scoped readers and bounded excerpts |
| [restart-reattach.md](validation/restart-reattach.md) | Whether each runtime's session outlives its process so a control-plane restart can re-attach it |
| [running-agent-work.md](validation/running-agent-work.md) | Whether work started outside Halora can be connected through each runtime's structured surface |
| [spending-cap.md](validation/spending-cap.md) | Per-task spending limits via Claude Code's `maxBudgetUsd`, verified against a loopback stand-in API |
| [workspace-requests.md](validation/workspace-requests.md) | End-to-end verification of the journaled workspace request tool loop on local models and real runtimes |

### Salidium, Seorak and Omnorium

| Record | Subject |
| --- | --- |
| [omnorium-catalog.md](validation/omnorium-catalog.md) | Omnorium catalog contract v1, source- and fixture-verified against published types and fixtures; not yet read live |
| [salidium-consumer-contract.md](validation/salidium-consumer-contract.md) | Salidium consumer contract v1, exercised end to end |
| [salidium-integration-audit.md](validation/salidium-integration-audit.md) | Salidium repository audit |
| [salidium-project-map.md](validation/salidium-project-map.md) | Salidium experimental committed-repository map and execution links v0, source-verified |
| [seorak-integration-api.md](validation/seorak-integration-api.md) | Seorak integration API v1, checked against the running local plane |

### Starters, checks, previews and sharing

| Record | Subject |
| --- | --- |
| [game-quality-rehearsal.md](validation/game-quality-rehearsal.md) | End-to-end VR game rehearsal on the Mac using `iwsdk-vr`, confined Codex on Ollama, automatic checks and the check-fix loop |
| [iwsdk-pages.md](validation/iwsdk-pages.md) | Readable text and image page panels (`pages()`) in the Immersive Web SDK VR starter |
| [iwsdk-starter.md](validation/iwsdk-starter.md) | The vetted Immersive Web SDK starter on the Mac: offline type check, build, headless browser check and Mac play viewer |
| [keeping-a-copy.md](validation/keeping-a-copy.md) | Show in Finder and Save a Copy via `git archive` with secret-shaped files excluded |
| [own-checks.md](validation/own-checks.md) | Running a project's own `package.json` and Go checks inside the closed Seatbelt profile |
| [quest-browser-preview.md](validation/quest-browser-preview.md) | Opening a checked preview in Quest Browser at `127.0.0.1`: confinement gates and device commands |
| [realtime-journey.md](validation/realtime-journey.md) | End-to-end creation, check, preview, catalog linking and concurrent intelligence journey over the realtime protocol |
| [sending-to-github.md](validation/sending-to-github.md) | Send to GitHub, Send One Change and Publish to GitHub Pages verified against scratch git remotes and `gh` stand-ins |
| [sharing-a-link.md](validation/sharing-a-link.md) | Temporary public preview links through pinned `cloudflared` quick tunnels on macOS |
| [showcase-finals.md](validation/showcase-finals.md) | Three VR games built end to end on a Mac from one instruction each |
| [starter-music-and-light.md](validation/starter-music-and-light.md) | Procedural WebAudio instruments, score playback, lighting and photo-derived scenes in the VR starter |
| [task-copies.md](validation/task-copies.md) | Per-task isolated working copies, conflict checks and adding changes back to the project folder |
| [vrify-real-projects.md](validation/vrify-real-projects.md) | `project.make_vr_version` tested on larger non-game web applications |

### Headset platform and builds

| Record | Subject |
| --- | --- |
| [boundary-visibility.md](validation/boundary-visibility.md) | Hiding the Quest boundary while passthrough shows in development builds, and restoring it on every exit |
| [demonstration-build.md](validation/demonstration-build.md) | The labeled demonstration build: demonstration timeline, visible names, field of view, frame rate, pause and release APK |
| [horizon-os-multitasking.md](validation/horizon-os-multitasking.md) | Windows over immersive apps, Virtual Display, hybrid 2D activities, the development glance and background limits |
| [horizon-store-release.md](validation/horizon-store-release.md) | Meta Horizon Store requirements for a Quest build and release APK verification |
| [interaction-sdk-editor-shutdown.md](validation/interaction-sdk-editor-shutdown.md) | macOS batch Unity Editor Interaction SDK telemetry shutdown workaround and limits |
| [meta-xr-audio.md](validation/meta-xr-audio.md) | Whether Meta XR Audio's spatializer fits the stage sounds |
| [meta-xr-platform.md](validation/meta-xr-platform.md) | Unity, OpenXR, Meta XR SDK and Simulator, Horizon OS and isolated Editor ADB servers |
| [mixed-reality-room.md](validation/mixed-reality-room.md) | Passthrough, the scene model through MRUK, spatial anchors and permissions |
| [native-agent-panel.md](validation/native-agent-panel.md) | Production native Android hybrid panel, paired and debuggable USB transports, reviewed commands and continuity |
| [native-panel-voice.md](validation/native-panel-voice.md) | Debuggable-only native panel held dictation, consent and editor leases, and bounded audio disposal |
| [native-project-output.md](validation/native-project-output.md) | Explicit headset-local project links to installed Android launchers and authority limits |
| [quest-3-device.md](validation/quest-3-device.md) | The XR client on a Meta Quest 3: build, install, WebSocket under IL2CPP and dated headset sessions |
| [quest-3-performance.md](validation/quest-3-performance.md) | What the headset interface costs to draw, measured in the Editor and on Quest |
| [quest-headset-in-the-loop.md](validation/quest-headset-in-the-loop.md) | Checking a Quest without wearing it: state, install and restore, captures, logs, isolated ADB servers and Meta XR Operator |
| [xr-menu-input.md](validation/xr-menu-input.md) | Quest controller and hand menu buttons reserved by Horizon OS versus inputs available to open the app's menu |

### Interface and spatial composition

| Record | Subject |
| --- | --- |
| [character-rendering.md](validation/character-rendering.md) | How characters render and place themselves, and their cost on a Quest 3 |
| [contextual-voice.md](validation/contextual-voice.md) | Voice interaction references, implemented dictation boundary and contextual voice acceptance limits |
| [default-arms.md](validation/default-arms.md) | Audit and automated guard (`tooling/default-arms.test.ts`) over switch fallback arms in headset presentation code |
| [entry-journey-renders.md](validation/entry-journey-renders.md) | Editor render witnesses for empty-computer and recorded-demonstration entry flows |
| [fonts-2026-10-30.md](validation/fonts-2026-10-30.md) | Bundled room fonts (Archivo Expanded, Inter, JetBrains Mono), licences and TextMeshPro atlases |
| [heads-up-guidance.md](validation/heads-up-guidance.md) | Meta's guidance for head-followed status, walking with a boundary, outdoor use and heads-up references |
| [headset-motion-guidance.md](validation/headset-motion-guidance.md) | Meta's and Apple's current motion guidance against the headset motion decision |
| [headset-redesign-research.md](validation/headset-redesign-research.md) | References and optical render measurements for the headset interface redesign on Quest 3 and Quest 3S |
| [headset-ui-guidance.md](validation/headset-ui-guidance.md) | Meta's guidance for hand targets, type, distance, contrast, feedback, icons and system windows |
| [initial-workspace-placement.md](validation/initial-workspace-placement.md) | Waiting for wearer presence, tracked head pose and input focus before initial Android workspace placement |
| [instrument-material-study.md](validation/instrument-material-study.md) | Coated reading surface and material comparison renders for the spatial workspace |
| [live-walk.md](validation/live-walk.md) | Editor live-walk harness (`WorkspaceRender.LiveWalkCheck`) comparing headset press counts against terminal sessions |
| [material-symbols.md](validation/material-symbols.md) | Material Symbols Rounded icon subset, licence, SHA-256 and reproducible font/atlas generation |
| [outside-text-audit.md](validation/outside-text-audit.md) | Every headset label carrying untrusted outside text: escaping rules, length caps, provenance treatment and gaps |
| [responsive-workspace.md](validation/responsive-workspace.md) | Out-of-view workspace recovery and responsive placement checks in the Editor |
| [sound-rendering.md](validation/sound-rendering.md) | Glaze procedural audio synthesizer port, soundbook parity and render cost |
| [spatial-project-journey.md](validation/spatial-project-journey.md) | Editor render checks for spatial project selection and task creation |
| [spatial-title-type.md](validation/spatial-title-type.md) | Principal title typography (`Rajdhani Medium`) and saved-font verification |
| [spatial-workspace-composition.md](validation/spatial-workspace-composition.md) | Spatial workspace composition Editor renders, multi-project witness and larger-text shelf clearance |
| [task-character-placement.md](validation/task-character-placement.md) | Selected-task character placement scene fixture (`WorkspaceRender.ShowTaskHereCheck`) |
| [theme-settings.md](validation/theme-settings.md) | Appearance and code-colour presets in Settings: preview lifetime, local preferences and provenance preservation |
| [understanding-and-evaluation.md](validation/understanding-and-evaluation.md) | Workspace Understanding and Evaluation views: read-only live sources, simulated demo answers and escaping |
| [voice-transcription.md](validation/voice-transcription.md) | Apple SpeechTranscriber versus `whisper.cpp` `large-v3-turbo` on the Mac: accuracy, latency, memory and silence handling |
| [workspace-draw-lifecycle.md](validation/workspace-draw-lifecycle.md) | Workspace view draw lifecycle and frame allocation checks in the Unity Editor |
| [workspace-interaction.md](validation/workspace-interaction.md) | Interaction SDK rig and targets, TextMeshPro and system keyboard behavior, and hostile-text rendering |

### Security, scale and host

| Record | Subject |
| --- | --- |
| [control-plane-scale-2026-11-03.md](validation/control-plane-scale-2026-11-03.md) | Memory, wire cost and bounded history growth on the Mac and in the headset client |
| [control-plane-scale-2026-11-10.md](validation/control-plane-scale-2026-11-10.md) | Startup time at 50,000 and one million journal events, whole and from a projection checkpoint |
| [core-audit-2026-11-03.md](validation/core-audit-2026-11-03.md) | Audit of the event-sourced core and control plane: findings fixed and open items |
| [headset-token-storage.md](validation/headset-token-storage.md) | Storing the USB development access token in app-private storage via `run-as` and migrating legacy shared storage |
| [logging-audit.md](validation/logging-audit.md) | Audit and automated guard (`tooling/log-calls.test.ts`) preventing secrets or prompt text from reaching logs |
| [mac-host-setup.md](validation/mac-host-setup.md) | `pnpm mac-setup`, `settings.json`, LaunchAgent login start and scratch-directory verification on macOS |
| [media-in-a-confined-project.md](validation/media-in-a-confined-project.md) | Stripping location metadata and generating stills with `ffmpeg` when adding photos or videos to a project |
| [network-pairing.md](validation/network-pairing.md) | Local-network headset pairing: SRP-6a, certificate pinning under Unity Android/Mono and transport checks |
| [project-location.md](validation/project-location.md) | Binding projects to host-approved folders: OpenCode and Codex runs, refusals and moved folders |
| [project-secrets.md](validation/project-secrets.md) | Per-project secrets stored on the Mac, injected at task start and redacted from runtime output |
| [xr-loopback-proof.md](validation/xr-loopback-proof.md) | Challenge-response loopback proof before sending the access token over USB `adb reverse` |

## Private documents

`docs/private/` is ignored by git: it exists only on a local machine, is never pushed, and has
no git history or backup. It holds:

- `ROADMAP.md`: the plan of record. Milestones with evidence-based exit criteria, work in flight
  and who owns it, decision gates and risks. Updated in place when work lands.
- Records about private repositories, such as the Seorak audit.

Tracked documents may say that a private record exists but must never quote it.

## What does not belong here

Status reports, TODO lists, debugging diaries, meeting notes and AI scratch output. Keep them in
the private roadmap, pull requests or the git-ignored `/.private/` directory. When temporary work
establishes something durable, write the conclusion into the canonical document it belongs to.
