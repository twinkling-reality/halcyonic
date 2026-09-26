# Halcyonic

Build software with AI agents in VR.

Halcyonic turns autonomous units of software work into persistent spatial objects. A Workstream
lives in your space as a small character you can ignore, glance at, or open into a focused
workspace to understand the work, evaluate it and direct it. Agent runtimes such as Claude Code,
Codex and OpenCode are interchangeable executors behind that work; none of them is the product.

## Status

Foundation stage. What exists today is the provider-neutral truth layer:

- a local **control plane** that records every command and runtime observation in an
  append-only journal (SQLite) and serves the current state over REST and WebSocket;
- versioned **contracts** (TypeScript, with a generated JSON Schema for other languages);
- a **mock runtime** that plays scripted scenarios for development. Its output is labeled synthetic
  everywhere.

There is no XR client and no real runtime integration yet. `docs/internal/architecture/SYSTEM.md`
describes what is built and what is not.

## Requirements

- Node.js 24.15 or newer (the control plane runs TypeScript directly and uses `node:sqlite`).
- pnpm 10 or newer. The exact version is pinned in `package.json`, and pnpm switches to it
  automatically.
- Developed and tested on macOS. Linux is expected to work; Windows is untested.

## Run it

```bash
pnpm install
pnpm dev     # control plane on 127.0.0.1:47800, data in ~/.halcyonic
pnpm demo    # in a second terminal: three workstreams, streamed live, with one approval
```

Replay a recorded trace instead of running the mock runtime:

```bash
pnpm replay fixtures/traces/multiple_workstreams.jsonl
```

Check everything before committing:

```bash
pnpm check
```

## Documentation

- [Contributing](CONTRIBUTING.md)
- [Agent contract](AGENTS.md) for AI coding agents
- [Documentation map](docs/internal/README.md)
- [System architecture](docs/internal/architecture/SYSTEM.md)
- [Local development runbook](docs/internal/runbooks/LOCAL_DEVELOPMENT.md)

## License

Apache-2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
