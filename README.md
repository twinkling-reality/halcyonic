# Halcyonic

Halcyonic explores VR as a working environment for agentic software development. A person should be able to see and move among several projects and repositories, work with a project's actual native VR output or the computer's real tools, direct agents, and understand changes and performance without rebuilding context when focus changes. Salidium contributes understanding and evidence. Seorak contributes performance measurements. Those belong in the same working experience.

A character represents a Workstream. Claude Code, Codex and OpenCode are interchangeable executors behind that work.

That is the product intent. This checkout is an early local system that implements part of it. [Product](docs/internal/product/PRODUCT.md) keeps the intent and the build apart. [System](docs/internal/architecture/SYSTEM.md) is the inventory of what is implemented.

## This checkout

- A local control plane records every command and runtime observation in an append-only SQLite journal and serves current state over REST and WebSocket. It listens on loopback. An optional TLS listener can serve paired headsets on the local network.
- Versioned contracts live in TypeScript. JSON Schema and C# bindings are generated from them.
- Runtime adapters: a mock runtime that plays labeled synthetic scenarios, and Claude Code (through the Agent SDK), OpenCode and Codex. Each real runtime is registered only when it is configured. OpenCode and Codex run only as pinned binaries.
- Optional read-through clients for Salidium (understanding of one execution) and Seorak (measurements for one execution, and a usage-limits read). A past check of either service is recorded in `docs/internal/validation/` and does not mean that service is running.
- A Unity client for Meta Quest: characters, a workspace, and a labeled recorded demonstration when the control plane cannot be reached. Worn sessions are the record in [quest-3-device.md](docs/internal/validation/quest-3-device.md). Wi-Fi pairing is verified off the headset only.

A project binds to one folder the host has approved. A spatial overview that relates several repositories is part of the intent. This checkout has no cross-repository relationship.

## Requirements

- Node.js 24.15 or newer. The control plane runs TypeScript directly and uses `node:sqlite`.
- pnpm. The repository pins 11.27.1 in `package.json` (`packageManager`). pnpm 10 switches to that pin. pnpm 12 cannot be installed by that switcher ([ADR 0004](docs/internal/decisions/0004-control-plane-stack.md)).
- Developed and tested on macOS. Linux is expected to work for the control plane. Windows is untested.
- The Quest client needs Unity. See the [XR development runbook](docs/internal/runbooks/XR_DEVELOPMENT.md).

`pnpm check` also needs a JDK and Android's platform jar, because a glance test compiles against them. Unity 6000.3.25f1 with Android Build Support supplies both. Otherwise set `JAVA_HOME` and `ANDROID_JAR`. The [local development runbook](docs/internal/runbooks/LOCAL_DEVELOPMENT.md) has the details.

## Run the control plane

```bash
pnpm install
pnpm dev     # 127.0.0.1:47800, data in ~/.halcyonic
pnpm demo    # second terminal: three mock workstreams, one waiting for approval
```

Replay a recorded trace:

```bash
pnpm replay fixtures/traces/multiple_workstreams.jsonl
```

Real agents, a headset, and Salidium or Seorak need the [local development runbook](docs/internal/runbooks/LOCAL_DEVELOPMENT.md). `pnpm dev` alone does not start them.

Before committing:

```bash
pnpm check
```

## Documentation

- [Contributing](CONTRIBUTING.md)
- [Agent contract](AGENTS.md)
- [Documentation map](docs/internal/README.md), including how documents stay current
- [System architecture](docs/internal/architecture/SYSTEM.md)
- [Local development](docs/internal/runbooks/LOCAL_DEVELOPMENT.md)

## License

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
