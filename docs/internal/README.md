# Documentation map

Documentation for people and agents building Halcyonic. The repository is public, so everything
tracked here may be published. Private material lives in `docs/private/`, which git ignores (see
below).

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
| See current priorities | [docs/private/ROADMAP.md](../private/ROADMAP.md) | Private. Never copied into a tracked file |

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
| Product | [product/PRODUCT.md](product/PRODUCT.md) | What Halcyonic is, is not, and must prove |
| Product | [product/OPEN_QUESTIONS.md](product/OPEN_QUESTIONS.md) | What is unresolved and must not silently become architecture |
| Product | [product/WORDS.md](product/WORDS.md) | How Halcyonic speaks on the headset: its voice, a word for each state, the words a person sees |
| Product | [product/STYLE.md](product/STYLE.md) | How Halcyonic looks on the headset: the plane, type, colour, grid, glass, selection, rows, the footer, the light line, icons, and the checks that hold them |
| Architecture | [architecture/SYSTEM.md](architecture/SYSTEM.md) | Components, dependency rules, what is and is not built |
| Architecture | [architecture/DOMAIN_MODEL.md](architecture/DOMAIN_MODEL.md) | Project, Workstream, Execution, statuses, attention, commands |
| Architecture | [architecture/EVENTS.md](architecture/EVENTS.md) | Journal, event envelope and catalog, provenance, versioning, traces |
| Architecture | [architecture/REALTIME.md](architecture/REALTIME.md) | REST endpoints and the WebSocket protocol |
| Architecture | [architecture/SECURITY.md](architecture/SECURITY.md) | Trust boundaries, controls, and what is not yet protected |
| Architecture | [architecture/INTEGRATIONS.md](architecture/INTEGRATIONS.md) | Runtime adapter contract, capabilities, integration boundaries |
| Architecture | [architecture/XR_CLIENT.md](architecture/XR_CLIENT.md) | The Unity client's layers, C# contracts, session, threading and verification |
| Operations | [runbooks/LOCAL_DEVELOPMENT.md](runbooks/LOCAL_DEVELOPMENT.md) | Running, replaying, recording, resetting |
| Operations | [runbooks/XR_DEVELOPMENT.md](runbooks/XR_DEVELOPMENT.md) | Installing Unity, opening the XR project, running it against the control plane |
| Operations | [runbooks/HEADSET_SESSION.md](runbooks/HEADSET_SESSION.md) | One session on a Meta Quest, in order, for what only the headset can show after the same work has passed on the Mac. The ordered checks, and `pnpm quest:check` for what needs no person, are in the runbook |

## Decision records

[decisions/](decisions/README.md) holds Architecture Decision Records for significant, hard to
reverse decisions. They are historical by design: a superseded ADR stays, marked superseded,
while the canonical documents move on.

## Validation records

[validation/](validation/) holds dated evidence that constrains the architecture: vendor surface
reviews, repository audits, platform facts, and hardware or runtime experiments. Each records the
question, environment and versions, method, evidence, result and consequence. External facts
change, so a validation record states when it was true; re-verify before relying on an old one.

### Runtimes

| Record | Subject |
| --- | --- |
| [opencode-capabilities.md](validation/opencode-capabilities.md) | OpenCode server API |
| [opencode-permissions.md](validation/opencode-permissions.md) | OpenCode permission rules, what they cover, and what still reaches the network |
| [opencode-sandbox.md](validation/opencode-sandbox.md) | OpenCode inside Halcyonic's macOS sandbox |
| [running-agent-work.md](validation/running-agent-work.md) | Whether work started outside Halcyonic can be connected through each runtime's structured surface |
| [claude-code-capabilities.md](validation/claude-code-capabilities.md) | Claude Code CLI and Agent SDK |
| [codex-capabilities.md](validation/codex-capabilities.md) | Codex exec, SDKs and app-server |
| [local-models.md](validation/local-models.md) | Open models served by Ollama on the Mac, through the real runtimes and the control plane: flows, network, speed and memory, and choosing a model from a runtime's list |
| [companion-model.md](validation/companion-model.md) | Where Create's companion runs, and what a local model keeps to |
| [agent-questions.md](validation/agent-questions.md) | How OpenCode, Codex and the Claude Agent SDK ask the person questions, how each is answered, dismissed and interrupted, on the pinned binaries and a local model |

### Salidium and Seorak

| Record | Subject |
| --- | --- |
| [salidium-integration-audit.md](validation/salidium-integration-audit.md) | Salidium repository audit |
| [salidium-consumer-contract.md](validation/salidium-consumer-contract.md) | Salidium consumer contract v1, exercised end to end |
| [salidium-project-map.md](validation/salidium-project-map.md) | Salidium experimental committed-repository map and execution links v0, source-verified; consumer access and evidence limits |
| [seorak-integration-api.md](validation/seorak-integration-api.md) | Seorak integration API v1, checked against the running plane |

### Headset platform

| Record | Subject |
| --- | --- |
| [meta-xr-platform.md](validation/meta-xr-platform.md) | Unity, OpenXR, Meta XR SDK and Simulator, Horizon OS |
| [interaction-sdk-editor-shutdown.md](validation/interaction-sdk-editor-shutdown.md) | macOS batch editor telemetry lifecycle workaround and validation limits |
| [quest-3-device.md](validation/quest-3-device.md) | The XR client on a Meta Quest 3: build, install, WebSocket under IL2CPP, milestone 2 checks |
| [quest-headset-in-the-loop.md](validation/quest-headset-in-the-loop.md) | Checking a Quest without wearing it: state, install and restore, launch, captures, recording, logs, the adb servers kept apart, and Meta XR Operator on 8720 |
| [quest-3-performance.md](validation/quest-3-performance.md) | What the headset interface costs to draw, measured off the device and left for the headset |
| [horizon-os-multitasking.md](validation/horizon-os-multitasking.md) | Windows over immersive apps, Virtual Display, hybrid 2D activities, background limits |
| [native-panel-voice.md](validation/native-panel-voice.md) | Development-only native held dictation, consent/editor lifetime, bounded audio disposal and source/API evidence versus device acceptance |
| [native-project-output.md](validation/native-project-output.md) | Explicit headset-local project links to exact installed launchers, private persistence, authority and build correspondence limits |
| [recorded-tool-results.md](validation/recorded-tool-results.md) | Observed final native command/file-change fields, owner-journal privacy, exact scoped readers, bounded model excerpts and native source versus device evidence |
| [native-agent-panel.md](validation/native-agent-panel.md) | Production native hybrid panel, paired and explicit development USB transports, reviewed commands, private continuity and source-versus-headset acceptance |
| [horizon-store-release.md](validation/horizon-store-release.md) | Meta Horizon Store requirements for a Quest build, and the release APK checked against them |
| [competition-judge-build.md](validation/competition-judge-build.md) | The judge build against the competition's rules and today's product: the demonstration's path and timings, names a judge reads, field of view, frame rate, pause and the release APK |
| [mixed-reality-room.md](validation/mixed-reality-room.md) | Passthrough, the scene model through MRUK, spatial anchors and their permissions |

### Interface

| Record | Subject |
| --- | --- |
| [outside-text-audit.md](validation/outside-text-audit.md) | Every line on the headset that can carry text Halcyonic did not write: the rule and limit on each path, whether the reader can tell whose words they are, and the gaps |
| [voice-transcription.md](validation/voice-transcription.md) | Apple's SpeechTranscriber against whisper.cpp large-v3-turbo on the Mac, on synthetic clips: accuracy, latency, memory, network, and what each returns when nothing is said |
| [contextual-voice.md](validation/contextual-voice.md) | Voice interaction references, implemented dictation boundary and contextual voice acceptance limits |
| [character-rendering.md](validation/character-rendering.md) | How the characters render and place themselves, and their cost on a Quest 3 |
| [theme-settings.md](validation/theme-settings.md) | Interface presets and Settings categories: preview lifetime, local preferences, provenance, consumers and the composition checks still required |
| [initial-workspace-placement.md](validation/initial-workspace-placement.md) | Initial Android workspace placement waits for wearer presence, tracked head pose and focus; source and regression proof, pending renderer/device acceptance |
| [workspace-interaction.md](validation/workspace-interaction.md) | Interaction SDK rig and targets, TextMeshPro and the system keyboard for the workspace, and what TextMeshPro and TextMesh make of text from outside |
| [understanding-and-evaluation.md](validation/understanding-and-evaluation.md) | The workspace's Understanding and Evaluation sections: real sources read only, the demonstration's simulated answers, TextMeshPro escapes, renders |
| [sound-rendering.md](validation/sound-rendering.md) | The Glaze sound's port checked against the soundbook page, and what rendering it costs |
| [meta-xr-audio.md](validation/meta-xr-audio.md) | Whether Meta XR Audio's spatializer fits the stage sounds |
| [headset-motion-guidance.md](validation/headset-motion-guidance.md) | Meta's and Apple's current pages against the headset motion decision |
| [headset-ui-guidance.md](validation/headset-ui-guidance.md) | Meta's guidance for hand targets, type, distance, contrast, feedback, icons and system windows, and whether the Interaction SDK UI Set and open icon sets fit this project |
| [headset-redesign-research.md](validation/headset-redesign-research.md) | The headset redesign's references (visionOS, Horizon OS, Android XR, agent tools, game menus, Star Citizen's mobiGlas) and what its renders measured: a plane below the eyes, text as the eyes see it, a Quest 3S's field |
| [boundary-visibility.md](validation/boundary-visibility.md) | Hiding the headset's boundary while the room shows, in development builds only, and what brings it back on every way out |
| [heads-up-guidance.md](validation/heads-up-guidance.md) | Meta's rules for status that follows the head, walking with a boundary, outdoor use and another app in front, and the references for a heads-up mode |
| [material-symbols.md](validation/material-symbols.md) | The headset's icons: the Material Symbols source, its licence and hash, and the icon font and atlas made from it, made again exactly |

### Security and host

| Record | Subject |
| --- | --- |
| [headset-token-storage.md](validation/headset-token-storage.md) | The access token a development build uses over USB, kept in app-private storage: the move from shared storage, the run-as write, and what still needs a headset |
| [logging-audit.md](validation/logging-audit.md) | What leaves the processes through logs, errors, the journal and printed lines: every site checked, the leaks fixed, the scan that guards them, and what is open |
| [xr-loopback-proof.md](validation/xr-loopback-proof.md) | The headset asks the control plane to prove it holds the access token, for the address and port it dialled, before sending it: impostors, relays, redirects and a stale token |
| [mac-host-setup.md](validation/mac-host-setup.md) | `pnpm mac-setup` and the settings file on the owner's Mac: the read-only check, every setup step in a scratch data directory, a control plane started from the file alone, and what was not tried |
| [project-location.md](validation/project-location.md) | Binding projects to host-approved folders: real OpenCode and Codex starts on a local model in a new folder, refusals, a moved folder and an unknown effect, through the control plane |
| [network-pairing.md](validation/network-pairing.md) | Pairing a headset over the local network: certificate callbacks in Unity's Android class libraries, SRP, the pinned transports off the headset |

## Private documents

`docs/private/` is ignored by git: it exists only on the owner's machine, is never pushed, and has
no git history or backup. It holds:

- `ROADMAP.md`: the plan of record. Milestones with evidence-based exit criteria, work in flight
  and who owns it, decision gates and risks. Updated in place when work lands.
- Competition strategy.
- Records about private repositories, such as the Seorak audit.

Tracked documents may say that a private record exists but must never quote it.

## What does not belong here

Status reports, TODO lists, debugging diaries, meeting notes and AI scratch output. Keep them in
the roadmap, pull requests or the git-ignored `/.private/` directory. When temporary work
establishes something durable, write the conclusion into the canonical document it belongs to.
