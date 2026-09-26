# Local development

## Prerequisites

- Node.js 24.15 or newer: `node --version`.
- pnpm 10 or newer. It switches to the pinned version (see `packageManager` in `package.json`).
- `pnpm install` from the repository root.
- For C# work only: the .NET 10 SDK. A user-local install works:
  `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0`, then
  `export PATH="$HOME/.dotnet:$PATH"`.

## Run the control plane

```bash
pnpm dev
```

This listens on `127.0.0.1:47800`, stores its journal in `~/.halcyonic/control-plane.db`, and
creates the access token `~/.halcyonic/access-token` on first start. Logs are JSON on stdout; the
`control plane ready` line shows the address, the journal id and the registered runtimes. The
token never appears in logs. Use `pnpm start` for a run without file watching.

Use another data directory or port through the environment:

```bash
HALCYONIC_DATA_DIR=/tmp/halcyonic-dev HALCYONIC_PORT=47801 pnpm dev
```

## Drive it

`pnpm demo` connects over the realtime protocol, creates a project with three workstreams on the
mock runtime, prints every state change as it streams in, approves the one approval request after
three seconds, and prints the final state. It exits non-zero if any server message fails
contract validation.

For manual REST calls:

```bash
TOKEN="$(cat ~/.halcyonic/access-token)"
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:47800/api/snapshot
curl -s -H "Authorization: Bearer $TOKEN" 'http://127.0.0.1:47800/api/events?after=0&limit=20'
```

Commands are posted as JSON `CommandEnvelope`s to `/api/commands`. The shape is in
`packages/contracts/schema/halcyonic-contracts.schema.json` under `$defs/CommandEnvelope`.

## Replay a recorded trace

```bash
pnpm replay fixtures/traces/multiple_workstreams.jsonl            # at recorded pace
pnpm replay fixtures/traces/multiple_workstreams.jsonl --instant  # all at once
```

The replay server uses an in-memory journal marked `fixture` and registers no runtimes, so
commands against replayed executions are rejected. It uses the normal port, so stop `pnpm dev`
first or set `HALCYONIC_PORT`.

## Regenerate generated files

| After changing | Run | Then |
| --- | --- | --- |
| A contract in `packages/contracts` | `pnpm contracts:emit` | Review the schema and C# diffs, then `pnpm test:csharp` |
| Contracts, the pipeline, the mock runtime or a scenario | `pnpm fixtures:record` | Review the trace diff |

`pnpm check` fails when any generated file is stale. `node apps/control-plane/src/cli/record-fixtures.ts --check`
checks the trace alone.

## Test the C# client

```bash
pnpm test:csharp
```

This builds the generated contracts and the XR client core the way Unity constrains them (.NET
Standard 2.1, C# 9, warnings as errors) and runs their NUnit tests on .NET 10. Two tests start real
control plane processes with Node.js on free ports and temporary data directories. Filter them out
with `dotnet test apps/xr/dotnet/Halcyonic.Client.Tests --filter "TestCategory!=ControlPlane"`.

## Inspect the journal

```bash
sqlite3 ~/.halcyonic/control-plane.db \
  "SELECT position, event_type, ingested_at FROM events ORDER BY position DESC LIMIT 20"
```

Read only. Never edit the journal by hand: stored events are validated on read, and the control
plane refuses to start from an event that no longer matches the contract.

## Reset

Stop the control plane, then delete the data directory (`~/.halcyonic` by default). This removes
all history and the access token; clients need the new token afterwards.

## Troubleshooting

- **`HALCYONIC_HOST ... is not a loopback address`**: intended. Serving beyond this machine needs
  device pairing, which does not exist yet.
- **`EADDRINUSE`**: another control plane or replay is running on the port. Stop it or set
  `HALCYONIC_PORT`.
- **Executions show `unknown` after a restart**: intended. Their runtime sessions did not survive
  the restart, so their true state cannot be known.
- **`holds a fixture journal`**: the data directory contains a journal created as a fixture. Use a
  different `HALCYONIC_DATA_DIR`.
- **pnpm fails to switch versions**: pnpm 10 cannot install pnpm 12's native binary. The
  repository pins pnpm 11 for this reason; see ADR 0004.
