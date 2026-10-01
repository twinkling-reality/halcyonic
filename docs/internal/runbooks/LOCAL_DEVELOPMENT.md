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
contract validation. It approves only its own workstreams' requests, and starts each with a model
the mock lists, since a runtime that lists models refuses a start without one (`model_required`).

For a device check, `pnpm demo --scenario <name>` starts one mock scenario in a project of its own,
named "Scenario: <name>", and leaves whatever it asks waiting for the person: nothing answers it.
The names are the files in `fixtures/scenarios`, for example:

```bash
pnpm demo --scenario question_asked
```

`approval_required` leaves an approval waiting instead. An unknown name is refused with the list of
those there are.

For manual REST calls:

```bash
TOKEN="$(cat ~/.halcyonic/access-token)"
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:47800/api/snapshot
curl -s -H "Authorization: Bearer $TOKEN" 'http://127.0.0.1:47800/api/events?after=0&limit=20'
```

Commands are posted as JSON `CommandEnvelope`s to `/api/commands`. The shape is in
`packages/contracts/schema/halcyonic-contracts.schema.json` under `$defs/CommandEnvelope`.

A runtime whose descriptor says `"model_choice": "listed"` lists the models it can run now; the
mock runtime of `pnpm dev` lists three synthetic ones (`mock/fast`, `mock/hosted`, `mock/no-tools`):

```bash
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:47800/api/runtimes/mock/models
```

To start work on one, put its `model_ref` in the `execution.start` payload exactly as listed; the
payload's `model_ref` is null otherwise, and a runtime's own `model` option cannot be combined with
it. The execution's `model_ref` then shows the model the runtime reports running on
([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)).

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

Real runtimes work in the project's folder, so first give the project one (below). Then start an
execution on it with the runtime id `claude-agent` and options such as
`{"permission_mode": "default"}`; `model` is optional. Every run spends model credit. The runtime's sessions appear in
Salidium and Seorak like any other Claude Code session, because Halcyonic keeps your home and Claude
configuration directories.

### Where projects live

Every real execution runs in its project's folder, a folder inside one of the project roots
([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)). No start option
names a folder. `GET /api/locations` lists each root, as its real path, and the folders directly
inside it. Give a project its folder when you create it:

```json
{"name": "Storefront", "location": {"kind": "existing_folder", "root": "/Users/you/dev", "folder_name": "storefront"}}
```

or have the control plane make a new, empty folder for it, which needs no git repository:

```json
{"name": "Greeting card", "location": {"kind": "new_folder", "root": "/Users/you/dev", "folder_name": "greeting-card"}}
```

`folder_name: null` with `existing_folder` binds the root itself. A project created with
`location: null` can run only on the mock runtime until it gets one:
`project.set_location` with `{"project_id": "...", "location": {...}}` binds it, and also rebinds
a project whose folder you moved or renamed. Refusals: `location_not_allowed` (not a root, or not
directly inside one), `location_missing` (not there, or the folder moved), `location_exists` (a new
folder's name is taken; choose it as an existing folder instead), and `location_required` (a start
in a project without a folder).

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

Start executions with the runtime id `opencode` in a project with a folder, and options such as
`{"model": "provider/model"}`. OpenCode uses your own OpenCode configuration and providers.

#### Local models through Ollama

OpenCode lists the models an Ollama server on 127.0.0.1:11434 offers without any configuration;
name one as `ollama/<tag>`, for example `ollama/qwen3.6:35b-a3b-nvfp4`. A start waits up to 10 s
for OpenCode to discover it. OpenCode's defaults do not keep work on the Mac, so for local-only
work put this in `~/.config/opencode/opencode.json` (one `models` entry per model you use):

```json
{
  "model": "ollama/qwen3.6:35b-a3b-nvfp4",
  "permissions": [
    { "action": "shell", "resource": "*", "effect": "ask" },
    { "action": "webfetch", "resource": "*", "effect": "deny" },
    { "action": "websearch", "resource": "*", "effect": "deny" }
  ],
  "providers": {
    "ollama": {
      "models": {
        "qwen3.6:35b-a3b-nvfp4": { "limit": { "context": 65536, "output": 16384 } }
      }
    }
  }
}
```

The `model` makes a start without one use the local model instead of OpenCode's free hosted
default. The permissions make shell commands ask the person, which is how approvals reach
Halcyonic, and keep the agent from fetching the web. The `limit` tells OpenCode the context Ollama
actually gives the model (`OLLAMA_CONTEXT_LENGTH`), where it would otherwise assume the model's full
context. The adapter disables OpenCode's catalog fetch by default. Ensure ripgrep is on the PATH
passed to the control plane, so OpenCode does not download it when it searches files:

```bash
export PATH="/opt/homebrew/bin:$PATH"
command -v rg
```

What each does, and the speed and memory of the models tried, are in
[local-models.md](../validation/local-models.md).

The local models appear in `GET /api/runtimes/opencode/models` beside OpenCode Zen's hosted ones,
for example as `ollama/qwen3.6:35b-a3b-nvfp4`, named "qwen3.6:35b-a3b-nvfp4 (Ollama)" and served on
this Mac, and a start may carry one as its `model_ref` in place of the `model` option.
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

Start executions with the runtime id `codex` in a project with a folder, and options such as
`{"sandbox": "workspace-write", "approval_policy": "on-request"}`, their defaults; `model` is
optional. A new, empty folder that is not a git repository works
([project-location.md](../validation/project-location.md)).
`approval_policy` `never` is refused, and `danger-full-access` needs `untrusted`. Codex uses your own
`CODEX_HOME` (`~/.codex` by default): your configuration, your sign-in or API key, and your model
providers. It writes each thread's rollout there like any other Codex session, where Salidium and
Seorak read Codex sessions; a local Ollama thread started by Halcyonic was read through both on
2026-09-30 ([validation record](../validation/understanding-and-evaluation.md)). A provider that
reads its key from an environment variable (`env_key` in `config.toml`) needs that variable named
in `HALCYONIC_AGENT_ENV`. Hosted models spend model credit; the local Ollama models below do not.

#### Local models through Ollama

Codex reaches Ollama through its built-in `ollama` provider, on port 11434 of this Mac, over the
Responses API. Name the provider and the model as start options, and tell Codex the context Ollama
gives the model, since Codex has no metadata for it and assumes 272,000 tokens:

```json
{
  "model_provider": "ollama",
  "model": "qwen3.6:35b-a3b-nvfp4",
  "context_window": 65536,
  "auto_compact_token_limit": 52000
}
```

The thread is refused if Codex reports another provider or model for it. Codex takes any model
name without checking it: a name Ollama does not have fails the first turn. Codex cannot list what
Ollama serves, so `GET /api/runtimes/codex/models` holds the model your `config.toml` names, under
its provider, and OpenAI's catalog only when that provider is OpenAI's: with
`model_provider = "ollama"` and `model = "qwen3.6:35b-a3b-nvfp4"` there, a start may carry
`"model_ref": "ollama/qwen3.6:35b-a3b-nvfp4"` in place of the two options, with the context
options as before. The runs, the rollouts
and what reaches the network are in [local-models.md](../validation/local-models.md). Your own
`config.toml` still applies: turn off `features.plugins` and `analytics` there for work that stays
on the Mac.

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
with the scopes `sessions:read`, `replay:read` and `limits:read`, and no project or date
restriction. One credential serves every Seorak read. Seorak shows the token once. Store only the
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

### Usage left

The headset's Usage left glance reads `GET /api/usage-limits`, which reads Seorak's account-wide
provider usage limits with `limits:read`. That read is in a Seorak build that is not released
yet, so until it runs the headset says "Usage left isn't set up on your Mac." and nothing more.
The control plane's reason code says why:

| Reason | Meaning | Fix |
| --- | --- | --- |
| `credential_missing` | No `~/.halcyonic/seorak-credential` | Issue the credential above and store it |
| `insufficient_scope` | The credential lacks `limits:read`, as one issued before 2026-09-30 does | Issue a new credential with all three scopes, replace the file, then revoke the old one in Seorak's dashboard |
| `outside_credential_restriction` | The credential is restricted to a project or dates; Seorak serves limits only to an unrestricted one | Issue an unrestricted credential |
| `credential_rejected` | Unknown, expired, revoked, or for another audience | Issue a new one |
| `limits_not_served` | Seorak answers 404: it predates the limits read | Merge the limits build and restart Seorak |
| `not_running`, `unreachable` | Nothing answers on 127.0.0.1:4317; the headset says Usage left can't be read right now | Start Seorak |
| `not_captured` | Seorak has no provider reading yet; the headset says "No usage reading yet." | Use Codex; Claude Code never yields one |

To enable it: merge Seorak's usage limits build and restart its daemon (its new history schema
cannot be read by older Seorak builds), then replace the credential as above. The file is read on
every request, so the control plane needs no restart. Check with the control plane's access token:

```bash
curl -s -H "Authorization: Bearer $(cat ~/.halcyonic/access-token)" http://127.0.0.1:47800/api/usage-limits
```

## Turn on voice

A development build of the headset can turn a held clip of speech into a draft, transcribed on this
Mac by whisper.cpp ([ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md),
[record](../validation/voice-transcription.md)). Voice is off until you set it up; nothing is
downloaded because someone spoke. Build the pinned whisper.cpp from its release archive (needs
Xcode's command-line tools and CMake), with Metal:

```bash
mkdir -p ~/.halcyonic/speech/src ~/.halcyonic/speech/models
cd ~/.halcyonic/speech/src
curl -sSLo whisper.cpp-1.9.4.tar.gz https://github.com/ggml-org/whisper.cpp/archive/refs/tags/v1.9.4.tar.gz
echo "57e280cee375ab02425b806ad5146b99f6eb9357e3c2b31357c8a6af2e2e44ae  whisper.cpp-1.9.4.tar.gz" | shasum -a 256 -c -
tar xzf whisper.cpp-1.9.4.tar.gz && cd whisper.cpp-1.9.4
cmake -B build -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DWHISPER_BUILD_TESTS=OFF -DWHISPER_SDL2=OFF
cmake --build build --config Release -j --target whisper-cli
mkdir -p ~/.halcyonic/speech/whisper.cpp-1.9.4/bin && cp build/bin/whisper-cli ~/.halcyonic/speech/whisper.cpp-1.9.4/bin/
```

Then the speech model (547 MiB) and the voice activity model (864 KiB), each pinned to a commit and
checked against its SHA-256:

```bash
cd ~/.halcyonic/speech/models
curl -sSLo ggml-large-v3-turbo-q5_0.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo-q5_0.bin
curl -sSLo ggml-silero-v6.2.0.bin https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/ggml-silero-v6.2.0.bin
shasum -a 256 -c - <<'SUMS'
394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2  ggml-large-v3-turbo-q5_0.bin
2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987  ggml-silero-v6.2.0.bin
SUMS
```

Point the control plane at all three before starting it:

```bash
export HALCYONIC_WHISPER_BIN="$HOME/.halcyonic/speech/whisper.cpp-1.9.4/bin/whisper-cli"
export HALCYONIC_WHISPER_MODEL="$HOME/.halcyonic/speech/models/ggml-large-v3-turbo-q5_0.bin"
export HALCYONIC_WHISPER_VAD_MODEL="$HOME/.halcyonic/speech/models/ggml-silero-v6.2.0.bin"
```

At startup the log names the engine (`speech: {name: 'whisper.cpp', version: '1.9.4-dev'}`; the
archive build says `-dev`), then `speech engine warmed up`. The first warm-up after building
compiles the GPU's shaders and took 23.5 s here; later ones take under a second. Try a clip made
with `say`:

```bash
say -o /tmp/hello.wav --file-format=WAVE --data-format=LEI16@16000 "Add a contact form to the home page."
curl -s -H "Authorization: Bearer $(cat ~/.halcyonic/access-token)" -H "content-type: audio/wav" \
  --data-binary @/tmp/hello.wav http://127.0.0.1:47800/api/transcriptions
```

It answers `"outcome":"heard"` with the text in about half a second; a silent clip answers
`nothing_heard`. Each clip holds about 920 MiB of memory for the half
second whisper.cpp runs.

## Pair a headset over Wi-Fi

Devices on the local network reach the control plane through a second listener, TLS only, which
is off unless you turn it on ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md),
[SECURITY.md](../architecture/SECURITY.md)). Loopback serves as before.

```bash
HALCYONIC_NETWORK_HOST=0.0.0.0 pnpm dev
```

`0.0.0.0` listens on every IPv4 interface; name one address, such as `192.168.1.23`, to listen on
that one only. The port is 47801 (`HALCYONIC_NETWORK_PORT`). The first start creates the listener's
TLS identity, `network-key.pem` and `network-certificate.pem` in the data directory, and logs its
certificate's SHA-256, which is not secret; the `control plane ready` line names the listener. If
macOS asks whether `node` may accept incoming connections, allow it: the listener serves nothing
without a paired credential or an open pairing window. With the firewall set to block all incoming
connections, no device can reach it.

Then, with the control plane running:

```bash
pnpm pair                          # the Mac's addresses and an eight-digit code
pnpm devices                       # every device paired: id, label, when, connected or revoked
pnpm devices revoke <device id>    # stops accepting it at once and ends what it has open
```

`pnpm pair` opens a pairing window for five minutes, for one device; three wrong codes close it.
Enter the address and the code on the headset ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md)). It prints
each refused code, ends when a device pairs, naming it, and closes pairing on `Ctrl-C`. The code
appears only in its output, never in a log or the journal. Running it again replaces the window
and its code.

It also prints each pairing connection it turned away or cut short without checking a code, with
the address it came from: while another exchange was in progress, too many from one address in a
minute, an exchange that sent no code within 30 seconds or closed before sending one, or one that
did not follow the protocol. None of them costs an attempt, but a device other than your headset
that keeps appearing there is holding pairing up: `Ctrl-C`, and pair when it has gone.

- Pairing again adds another device; revoke the one it replaces.
- A device that forgets the Mac revokes itself when it can reach it.
- To replace the TLS identity, stop the control plane and delete both files; every device then
  pairs again. Keep them with the rest of the data directory, readable only by you.
- The CLI reads the data directory and port like the control plane (`HALCYONIC_DATA_DIR`,
  `HALCYONIC_PORT`), so run it with the same environment.

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

- **`HALCYONIC_HOST ... is not a loopback address`**: intended. The main listener is loopback
  only; devices on the network use the network listener (`HALCYONIC_NETWORK_HOST`).
- **`The network listener is off`** from `pnpm pair`: start the control plane with
  `HALCYONIC_NETWORK_HOST` set.
- **A headset cannot reach the Mac over Wi-Fi**: same network, and not one that isolates its
  clients; the address as `pnpm pair` printed it; the macOS firewall; `curl -k
  https://<address>:47801/api/health` from another machine answers `{"status":"ok"}`.
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
