# System

How Halcyonic is built today, what depends on what, and what is not built yet.

## Components

| Component | Location | Status |
| --- | --- | --- |
| Contracts: wire schemas, validators, generated JSON Schema | `packages/contracts` | Built |
| Domain: projection, status and attention rules, command admission | `packages/domain` | Built |
| Runtime port: adapter interface, capability checks, clocks | `packages/runtime-core` | Built |
| Mock runtime: scripted scenarios, synthetic | `packages/integrations/mock` | Built |
| Control plane: journal, commands, REST, WebSocket, CLIs | `apps/control-plane` | Built, loopback only |
| Scenarios and recorded traces | `fixtures/` | Built |
| Architecture boundary tests | `tooling/` | Built |
| XR client (Unity, OpenXR, Meta XR SDK) | `apps/xr` | Not started |
| OpenCode, Claude Code and Codex adapters | `packages/integrations/*` | Not started; see [INTEGRATIONS.md](INTEGRATIONS.md) |
| Salidium and Seorak integration | none | Blocked on contracts; see [INTEGRATIONS.md](INTEGRATIONS.md) |
| Device pairing, LAN serving, remote relay | none | Not started; see [SECURITY.md](SECURITY.md) |

## Dependency rules

```text
apps/control-plane ──> integrations/mock ──> runtime-core ──> contracts ──> typebox
        │                                         ▲               ▲
        ├──> domain ──────────────────────────────┼───────────────┘
        ├──> runtime-core ────────────────────────┘
        └──> fastify, @fastify/websocket, node:sqlite
```

- `contracts` depends only on TypeBox. It is the vocabulary every other part shares.
- `domain` depends only on `contracts`. It is pure: no I/O, no clock, no storage.
- `runtime-core` depends only on `contracts`. It defines what a runtime adapter must be.
- Integrations depend on `runtime-core` and `contracts`, never on the control plane or on each
  other.
- The control plane is the composition root. It is the only place that chooses storage,
  transport and which adapters run.

pnpm's isolated `node_modules` makes an undeclared import fail at runtime, and
`tooling/architecture.test.ts` fails the build if a core package declares or imports anything
outside its allowlist.

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

## Process model

One Node.js process. The journal (`node:sqlite`) is synchronous, so append, projection and
publication happen without interleaving: a reader never sees a journal and projection that
disagree, and a WebSocket client subscribing in the same step as its snapshot cannot miss an
event. Current state is held in memory and rebuilt from the journal at startup
([ADR 0002](../decisions/0002-append-only-journal-with-rebuilt-projection.md)).

On startup the control plane also reconciles: every execution that was in flight, and every
command still awaiting a runtime, is recorded as unknown, because no runtime session survives a
restart yet.

## Configuration

Environment variables, all optional:

| Variable | Default | Meaning |
| --- | --- | --- |
| `HALCYONIC_HOST` | `127.0.0.1` | Must be a loopback address; anything else is refused |
| `HALCYONIC_PORT` | `47800` | HTTP and WebSocket port |
| `HALCYONIC_DATA_DIR` | `~/.halcyonic` | Journal, access token |
| `HALCYONIC_LOG_LEVEL` | `info` | `fatal` to `trace`, or `silent` |
| `HALCYONIC_COMMAND_TIMEOUT_MS` | `30000` | How long to wait for a runtime to confirm an action |
| `HALCYONIC_MOCK_SCENARIOS_DIR` | `fixtures/scenarios` | Mock runtime scenarios |

## Toolchain

Node.js 24.15 or newer runs the TypeScript sources directly (type stripping is stable in Node 24;
`node:sqlite` is a release candidate from 24.15). TypeScript 7 type-checks only. Tests use
`node:test`; lint and format use Biome; pnpm 11 manages the workspace. Reasons and evidence:
[ADR 0004](../decisions/0004-control-plane-stack.md).

## Not built, and deliberately so

- **Snapshots of the projection on disk.** Rebuilding from the journal is fast at local scale;
  add snapshots when startup time says so.
- **Per-client subscriptions.** Every client receives every event; fine for one person's work.
- **OpenAPI document.** REST responses are typed by the contracts; generate OpenAPI 3.1 when the
  XR client needs generated HTTP bindings.
- **Separate persistence, realtime or security packages.** They live in the control plane until a
  second consumer needs them.
