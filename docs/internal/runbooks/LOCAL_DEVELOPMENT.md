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

A program that runs the control plane as its child, such as a test harness, can have it stop when
the program exits, however it exits: set `HALCYONIC_EXIT_ON_STDIN_END=1`, give the control plane a
pipe as its standard input, and hold the pipe open without writing to it. When the program exits,
even by SIGKILL or a crash, the operating system closes the pipe and the control plane shuts down
as it does on SIGTERM. Leave it unset otherwise: input that has already ended, such as
`/dev/null`, stops the control plane as soon as it is ready.

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

## Run real agents

Real runtimes work only inside directories you list, and are off until enabled. The Claude Agent
runtime needs an Anthropic API key: keep it in the data directory, readable only by you, rather than
in your shell environment. Copy the key to the clipboard, then:

```bash
mkdir -p ~/.halcyonic && chmod 700 ~/.halcyonic
(umask 077 && pbpaste > ~/.halcyonic/anthropic-api-key)
```

`ANTHROPIC_API_KEY` in the environment, or a cloud provider's variables, work too and take
precedence over the file. Then:

```bash
export HALCYONIC_PROJECT_ROOTS="$HOME/dev"   # directories agents may work in, separated by :
export HALCYONIC_CLAUDE_AGENT=1              # register the Claude Agent runtime
pnpm dev
```

Start an execution on it with the runtime id `claude-agent` and options such as
`{"cwd": "/Users/you/dev/app", "permission_mode": "default"}`; `model` is optional. A `cwd` outside
the project roots is rejected. Every run spends model credit. The runtime's sessions appear in
Salidium and Seorak like any other Claude Code session, because Halcyonic keeps your home and Claude
configuration directories.

### OpenCode

Halcyonic runs only its pinned OpenCode version, from a binary you install for it, never the
`opencode` on your PATH ([ADR 0009](../decisions/0009-opencode-v2-pinned-and-launched-by-halcyonic.md)).
Install it without running its install scripts, which would run the binary in your environment:

```bash
npm install --prefix ~/.halcyonic/runtimes/opencode-2.0.18 @opencode/cli@2.0.18 --ignore-scripts
```

Then point the control plane at the binary for your platform, for example on an Apple silicon Mac:

```bash
export HALCYONIC_OPENCODE_BIN="$HOME/.halcyonic/runtimes/opencode-2.0.18/node_modules/@opencode/cli-darwin-arm64/bin/opencode"
```

Start executions with the runtime id `opencode` and options such as
`{"directory": "/Users/you/dev/app", "model": "provider/model"}`; the directory must be under
`HALCYONIC_PROJECT_ROOTS`. OpenCode uses your own OpenCode configuration and providers.
Its end to end tests run against a binary and a fake provider when `OPENCODE_BIN` is set:

```bash
OPENCODE_BIN="$HALCYONIC_OPENCODE_BIN" node --test packages/integrations/opencode/src/opencode-runtime.e2e.test.ts
```

### Codex

Halcyonic runs only its pinned Codex version, from a native binary you install for it, never the
`codex` on your PATH ([ADR 0011](../decisions/0011-codex-app-server-stable-surface.md)). Install it
without running its install scripts:

```bash
npm install --prefix ~/.halcyonic/runtimes/codex-0.157.0 @openai/codex@0.157.0 --ignore-scripts
```

Then point the control plane at the native binary for your platform, not the npm `codex` launcher
script, for example on an Apple silicon Mac:

```bash
export HALCYONIC_CODEX_BIN="$HOME/.halcyonic/runtimes/codex-0.157.0/node_modules/@openai/codex-darwin-arm64/vendor/aarch64-apple-darwin/bin/codex"
shasum -a 256 "$HALCYONIC_CODEX_BIN"   # ad0be20d04e2ba6146ecdb51d7f8b7b0fe15420a15dc9b0057518d858f1f3714 on darwin arm64
```

Start executions with the runtime id `codex` and options such as
`{"cwd": "/Users/you/dev/app", "sandbox": "workspace-write", "approval_policy": "on-request"}`, the
defaults of the last two; `model` is optional. The `cwd` must be under `HALCYONIC_PROJECT_ROOTS`.
`approval_policy` `never` is refused, and `danger-full-access` needs `untrusted`. Codex uses your own
`CODEX_HOME` (`~/.codex` by default): your configuration, your sign-in or API key, and your model
providers. It writes each thread's rollout there like any other Codex session, where Salidium and
Seorak read Codex sessions; that they show a thread Halcyonic started is not yet verified. A
provider that reads its key from an environment variable (`env_key` in `config.toml`) needs that
variable named in `HALCYONIC_AGENT_ENV`. Every run spends model credit.

Its end to end tests run the binary against a fake provider when `CODEX_BIN` is set. They use
temporary homes, never your `~/.codex`, and fail if Codex tries to reach anything beyond loopback:

```bash
CODEX_BIN="$HALCYONIC_CODEX_BIN" node --test packages/integrations/codex/src/codex-runtime.e2e.test.ts
```

## Connect Salidium

With Salidium 0.6.0 or later running (earlier versions do not serve the consumer contract), create
a credential for Halcyonic and
store only the token in the data directory, readable only by you. `--json` makes Salidium print the
token as a field instead of in prose, and `umask 077` creates the file private from the start:

```bash
(umask 077 && salidium consumer create halcyonic --json | node -e 'let s="";process.stdin.on("data",(d)=>(s+=d)).on("end",()=>process.stdout.write(JSON.parse(s).token+"\n"))' > ~/.halcyonic/salidium-credential)
```

`GET /api/executions/:execution_id/understanding` then answers from Salidium for executions on
runtimes it observes (Claude Code and Codex). Every answer states its availability, so a missing
credential, a stopped Salidium or an unobserved runtime reads as such rather than as an error.
Salidium's real-wire tests run against its consumer test daemon from a built Salidium checkout:

```bash
SALIDIUM_CHECKOUT=/path/to/salidium node --test packages/integrations/salidium/src/live-salidium.test.ts
```

## Connect Seorak

With Seorak running its local plane on 127.0.0.1:4317 (Halcyonic is verified against 0.3.0),
issue an API integration credential for Halcyonic in Seorak's local dashboard
(<http://127.0.0.1:4317/dashboard>): audience `http://127.0.0.1:4317/api/v1`, not the MCP one,
with the scopes `sessions:read` and `replay:read`. Seorak shows the token once. Store only the
token in the data directory, readable only by you:

```bash
(umask 077 && cat > ~/.halcyonic/seorak-credential)   # paste the token, then press Ctrl-D
```

`GET /api/executions/:execution_id/evaluation` then answers from Seorak for executions on runtimes
it observes (Claude Code and Codex). Every answer states its availability, so a missing credential,
a stopped Seorak or an unobserved runtime reads as such rather than as an error. An evaluation
spends three of the credential's 60 requests a minute: fetch it on demand, never on a timer.
The live test reads the credential file in its own process, never prints it, and makes at most six
requests; name a Claude Code session Seorak captured to evaluate one:

```bash
HALCYONIC_SEORAK_CREDENTIAL_FILE=~/.halcyonic/seorak-credential \
HALCYONIC_SEORAK_SESSION_ID=<session id> \
node --test packages/integrations/seorak/src/live-seorak.test.ts
```

## Replay a recorded trace

```bash
pnpm replay fixtures/traces/multiple_workstreams.jsonl            # at recorded pace
pnpm replay fixtures/traces/multiple_workstreams.jsonl --instant  # all at once
pnpm replay fixtures/traces/failure_modes.jsonl                   # failed, unknown, interrupted
```

The replay server uses an in-memory journal marked `fixture` and registers no runtimes, so
commands against replayed executions are rejected. It uses the normal port, so stop `pnpm dev`
first or set `HALCYONIC_PORT`.

## Regenerate generated files

| After changing | Run | Then |
| --- | --- | --- |
| A contract in `packages/contracts` | `pnpm contracts:emit` | Review the schema and C# diffs, then `pnpm test:csharp` |
| Contracts, the pipeline, the mock runtime or a scenario | `pnpm fixtures:record` | Review the trace diff |
| Contracts, the pipeline, the mock runtime, a scenario it plays, its plan, the routes, the Salidium or Seorak clients, or the stand-ins' stories | `pnpm demonstration:record` | Review the demonstration diff, then `pnpm test:csharp` |

`pnpm check` fails when any generated file is stale. `node apps/control-plane/src/cli/record-fixtures.ts --check`
checks the trace alone, and `node apps/control-plane/src/cli/record-demonstration.ts --check` the
demonstration alone.

The demonstration is what the XR client plays when no control plane is configured or reachable,
and it follows the answers a person gives ([XR_CLIENT.md](../architecture/XR_CLIENT.md)). Its plan,
in `apps/control-plane/src/fixtures/demonstration.ts`, names a project, three workstreams, the mock
scenarios they play (`order_history_pagination` and `order_confirmation_email` beside the story,
`sign_in_rate_limit` for it) and the instructions offered once the story's first turn has ended.
The recorder runs the real control plane with the mock runtime under virtual time with seeded
identifiers, once for the beginning and again from the start for every answer the recording offers:
approve, deny, and stop the turn wherever a workspace would offer them, and each recorded
instruction. The runs share their beginning exactly, and it writes them as one tree into
`apps/xr/Assets/Halcyonic/Resources/HalcyonicDemonstration.json`, about 720 KiB. Beside the tree it
records the control plane's answers about each execution's understanding and evaluation wherever
the playback can stand: for each node, a control plane without runtimes is given the journal up to
there and serves its routes on loopback, reading through Halcyonic's Salidium and Seorak clients
from stand-ins that speak the products' contracts with content written for the story
(`apps/control-plane/src/fixtures/demonstration-sources.ts`), and every answer is marked synthetic
([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md)).
Where a path would
leave work open to an action it has no answer for, it ends with its control plane started again
without runtimes, as a replay serves a journal. The recorder names the two runtimes for the
demonstration, "Simulated agent (demonstration)" and "Simulated agent (demonstration, watch only)";
both are the synthetic mock runtime, and the second declares nothing but starting work. Every
answer multiplies what follows it, so a longer story or another directed workstream grows the
file quickly; its tests check that the workspace offers exactly the answers recorded.

## Test the C# client

```bash
pnpm test:csharp
```

This builds the generated contracts and the XR client core the way Unity constrains them (.NET
Standard 2.1, C# 9, warnings as errors) and runs their NUnit tests on .NET 10. Three tests start
real control plane processes with Node.js on free ports and temporary data directories. Each runs
with `HALCYONIC_EXIT_ON_STDIN_END=1` and a standard input only the test host holds, so none outlives
a test host that is killed or crashes. Filter them out with
`dotnet test apps/xr/dotnet/Halcyonic.Client.Tests --filter "TestCategory!=ControlPlane"`.

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
- **`runtime reused a native event id` in the log**: a runtime adapter gave a different record the
  native id of one already journaled, and the journal dropped it. It is an adapter defect; the
  warning names both events ([EVENTS.md](../architecture/EVENTS.md)).
- **`holds a fixture journal`**: the data directory contains a journal created as a fixture. Use a
  different `HALCYONIC_DATA_DIR`.
- **pnpm fails to switch versions**: pnpm 10 cannot install pnpm 12's native binary. The
  repository pins pnpm 11 for this reason; see ADR 0004.
