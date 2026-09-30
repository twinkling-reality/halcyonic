# Documentation map

Documentation for people and agents building Halcyonic. The repository is public, so everything
tracked here may be published. Private material lives in `docs/private/`, which git ignores (see
below).

## Canonical documents

Canonical documents describe **current truth**: how the system works now and why. When the code
changes, the matching document changes in the same commit.

| Area | Document | Answers |
| --- | --- | --- |
| Product | [product/PRODUCT.md](product/PRODUCT.md) | What Halcyonic is, is not, and must prove |
| Product | [product/OPEN_QUESTIONS.md](product/OPEN_QUESTIONS.md) | What is unresolved and must not silently become architecture |
| Architecture | [architecture/SYSTEM.md](architecture/SYSTEM.md) | Components, dependency rules, what is and is not built |
| Architecture | [architecture/DOMAIN_MODEL.md](architecture/DOMAIN_MODEL.md) | Project, Workstream, Execution, statuses, attention, commands |
| Architecture | [architecture/EVENTS.md](architecture/EVENTS.md) | Journal, event envelope and catalog, provenance, versioning, traces |
| Architecture | [architecture/REALTIME.md](architecture/REALTIME.md) | REST endpoints and the WebSocket protocol |
| Architecture | [architecture/SECURITY.md](architecture/SECURITY.md) | Trust boundaries, controls, and what is not yet protected |
| Architecture | [architecture/INTEGRATIONS.md](architecture/INTEGRATIONS.md) | Runtime adapter contract, capabilities, integration boundaries |
| Architecture | [architecture/XR_CLIENT.md](architecture/XR_CLIENT.md) | The Unity client's layers, C# contracts, session, threading and verification |
| Operations | [runbooks/LOCAL_DEVELOPMENT.md](runbooks/LOCAL_DEVELOPMENT.md) | Running, replaying, recording, resetting |
| Operations | [runbooks/XR_DEVELOPMENT.md](runbooks/XR_DEVELOPMENT.md) | Installing Unity, opening the XR project, running it against the control plane |

## Decision records

[decisions/](decisions/README.md) holds Architecture Decision Records for significant, hard to
reverse decisions. They are historical by design: a superseded ADR stays, marked superseded,
while the canonical documents move on.

## Validation records

[validation/](validation/) holds dated evidence that constrains the architecture: vendor surface
reviews, repository audits, platform facts, and hardware or runtime experiments. Each records the
question, environment and versions, method, evidence, result and consequence. External facts
change, so a validation record states when it was true; re-verify before relying on an old one.

| Record | Subject |
| --- | --- |
| [opencode-capabilities.md](validation/opencode-capabilities.md) | OpenCode server API |
| [claude-code-capabilities.md](validation/claude-code-capabilities.md) | Claude Code CLI and Agent SDK |
| [codex-capabilities.md](validation/codex-capabilities.md) | Codex exec, SDKs and app-server |
| [local-models.md](validation/local-models.md) | Open models served by Ollama on the Mac, through the real runtimes and the control plane: flows, network, speed and memory |
| [salidium-integration-audit.md](validation/salidium-integration-audit.md) | Salidium repository audit |
| [salidium-consumer-contract.md](validation/salidium-consumer-contract.md) | Salidium consumer contract v1, exercised end to end |
| [seorak-integration-api.md](validation/seorak-integration-api.md) | Seorak integration API v1, checked against the running plane |
| [meta-xr-platform.md](validation/meta-xr-platform.md) | Unity, OpenXR, Meta XR SDK and Simulator, Horizon OS |
| [quest-3-device.md](validation/quest-3-device.md) | The XR client on a Meta Quest 3: build, install, WebSocket under IL2CPP, milestone 2 checks |
| [horizon-os-multitasking.md](validation/horizon-os-multitasking.md) | Windows over immersive apps, Virtual Display, hybrid 2D activities, background limits |
| [horizon-store-release.md](validation/horizon-store-release.md) | Meta Horizon Store requirements for a Quest build, and the release APK checked against them |
| [mixed-reality-room.md](validation/mixed-reality-room.md) | Passthrough, the scene model through MRUK, spatial anchors and their permissions |
| [character-rendering.md](validation/character-rendering.md) | How the characters render and place themselves, and their cost on a Quest 3 |
| [workspace-interaction.md](validation/workspace-interaction.md) | Interaction SDK rig and targets, TextMeshPro and the system keyboard for the workspace |

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
