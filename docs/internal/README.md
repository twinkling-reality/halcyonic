# Documentation map

Internal documentation for people and agents building Halcyonic. Content suitable for public
readers will live in `docs/public/` once there is any.

## Canonical documents

Canonical documents describe **current truth**: how the system works now and why. When the code
changes, the matching document changes in the same commit.

| Area | Document | Answers |
| --- | --- | --- |
| Product | [product/PRODUCT.md](product/PRODUCT.md) | What Halcyonic is, is not, and must prove |
| Product | [product/OPEN_QUESTIONS.md](product/OPEN_QUESTIONS.md) | What is unresolved and must not silently become architecture |
| Product | [product/COMPETITION.md](product/COMPETITION.md) | Verified competition constraints |
| Architecture | [architecture/SYSTEM.md](architecture/SYSTEM.md) | Components, dependency rules, what is and is not built |
| Architecture | [architecture/DOMAIN_MODEL.md](architecture/DOMAIN_MODEL.md) | Project, Workstream, Execution, statuses, attention, commands |
| Architecture | [architecture/EVENTS.md](architecture/EVENTS.md) | Journal, event envelope and catalog, provenance, versioning, traces |
| Architecture | [architecture/REALTIME.md](architecture/REALTIME.md) | REST endpoints and the WebSocket protocol |
| Architecture | [architecture/SECURITY.md](architecture/SECURITY.md) | Trust boundaries, controls, and what is not yet protected |
| Architecture | [architecture/INTEGRATIONS.md](architecture/INTEGRATIONS.md) | Runtime adapter contract, capabilities, integration boundaries |
| Operations | [runbooks/LOCAL_DEVELOPMENT.md](runbooks/LOCAL_DEVELOPMENT.md) | Running, replaying, recording, resetting |

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
| [salidium-integration-audit.md](validation/salidium-integration-audit.md) | Salidium repository audit |
| [seorak-integration-audit.md](validation/seorak-integration-audit.md) | Seorak repository audit |
| [meta-xr-platform.md](validation/meta-xr-platform.md) | Unity, OpenXR, Meta XR SDK and Simulator, Horizon OS |

## What does not belong here

Plans, status reports, TODO lists, debugging diaries, meeting notes and AI scratch output. Keep
them in issues, pull requests or the git-ignored `/.private/` directory. When temporary work
establishes something durable, write the conclusion into the canonical document it belongs to.
