# Local development

## Prerequisites

- Node.js 24.15 or newer: `node --version`.
- pnpm 10 or newer. It switches to the pinned version (see `packageManager` in `package.json`).
- `pnpm install` from the repository root.
- For `pnpm check`: a JDK and Android's platform jar, which the glance's tests (`tooling/glance`)
  compile against. Unity 6000.3.25f1 with Android Build Support brings both; otherwise set
  `JAVA_HOME` and `ANDROID_JAR`. Without them `pnpm check` fails there, rather than skip it.
- For C# work only: the .NET 10 SDK. A user-local install works:
  `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0`, then
  `export PATH="$HOME/.dotnet:$PATH"`.

## First run on a Mac

What a person new to Halcyonic does on their Mac before the headset can do real work. It is a
prototype: it has been run on the owner's Mac, not yet by someone new
([mac-host-setup.md](../validation/mac-host-setup.md)). How Halcyonic will be packaged is not
decided ([ADR 0024](../decisions/0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md)),
so today it starts from this repository, with the prerequisites above.

At any point, see where you are:

```bash
pnpm mac-setup
```

It checks each step below, says what is ready, what each step allows, and the next command, and
changes nothing. It reads no access token unless you add `--with-token`, which asks the running
Halcyonic which folders, agent apps and headsets it has; the token is then sent only after
Halcyonic proves it holds it, so nothing else listening on its port gets it. Every command that changes something says what it will do first; the ones that
open something (a folder, pairing) ask before they do it. They write `~/.halcyonic/settings.json`,
which only you can read or change, and Halcyonic reads it when it starts
([ADR 0024](../decisions/0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md)). A
`HALCYONIC_` variable set in the environment still wins over the file.

1. **Allow a folder for projects.** Agents may read and change anything in a folder you allow, and
   in every folder inside it. A paired headset sees the names of the folders directly inside it, can
   make new empty folders there with no limit on how many, and can move any project to another
   folder there. So allow one folder you keep for projects, never your whole home folder:

   ```bash
   pnpm mac-setup allow
   ```

   makes `~/HalcyonicProjects`, empty, once you say yes. `pnpm mac-setup allow <folder>` allows a
   folder that is already there; it refuses your home folder and any folder holding it, macOS's,
   its apps' and other people's folders, a whole drive, Halcyonic's own data, the hidden folders and
   Library in your home folder, and a folder another user owns or any user can change. Halcyonic
   itself refuses to start with such a folder, however it was allowed. `pnpm mac-setup disallow
   <folder>` takes one back; nothing in it is deleted.
2. **Get an agent app ready.** Halcyonic runs only the OpenCode and Codex versions it was checked
   with. Installing them downloads them from npm, without running their install scripts:

   ```bash
   npm install --prefix ~/.halcyonic/runtimes/opencode-2.0.18 @opencode/cli@2.0.18 --ignore-scripts
   npm install --prefix ~/.halcyonic/runtimes/codex-0.157.0 @openai/codex@0.157.0 --ignore-scripts
   pnpm mac-setup agent-apps
   ```

   The last command records each one only if its SHA-256 is the one Halcyonic was checked with (on
   Apple silicon; Intel Macs have no checksums yet). OpenCode searches files with ripgrep and
   downloads it from GitHub when none is on the PATH: `brew install ripgrep`.
3. **Choose where work runs, and what it costs.** A model on your Mac costs nothing per task and
   keeps your code and instructions on the Mac; a model on a remote service sends them there, and
   some cost money. Halcyonic never picks a model by itself: the headset lists the Mac's models
   first, and a remote one takes a second press. For work on the Mac, install
   [Ollama](https://ollama.com), start it with `OLLAMA_CONTEXT_LENGTH=65536` (and
   `OLLAMA_NO_CLOUD=1` to hide Ollama's own remote models), download a model that fits your Mac's
   memory, and give OpenCode settings of its own on it:

   ```bash
   ollama pull qwen3.6:35b-a3b-nvfp4     # 23.6 GB; qwen3.8:27b-nvfp4 is 18.2 GB
   pnpm mac-setup local-model qwen3.6:35b-a3b-nvfp4
   ```

   OpenCode then starts on that model when none is chosen, asks you before every shell command, which
   is how its approvals reach the headset, and cannot fetch from the web; your own OpenCode settings
   are left as they are. Codex gets the same model in a home of its own, `~/.halcyonic/codex-home`:
   it runs only on models this Mac serves, never signed in, and your own `~/.codex` is left as it
   is ([Codex](#codex)). Claude Agent runs only on Anthropic's remote service and is paid with your API
   key, so the setup never turns it on; see [Run real agents](#run-real-agents).
4. **Optional: voice, the companion, Usage left, and what changed and why.** Voice turns Hold to talk
   into a draft on the Mac: build it as in [Turn on voice](#turn-on-voice), then `pnpm mac-setup
   voice`, which records the files once their checksums match. Create's companion asks a model of its
   own on the Mac: `ollama pull qwen3.5:9b` (6.6 GB), start Ollama with
   `OLLAMA_MAX_LOADED_MODELS=2` so it stays loaded beside the agents' model, then `pnpm mac-setup
   companion qwen3.5:9b`, which records it only if Ollama lists it as running on this Mac; `pnpm
   mac-setup companion off` turns it off ([Turn on Create's companion](#turn-on-creates-companion)). Usage left needs Seorak and a credential, and the Understand
   tab's explanations need Salidium and a credential ([Connect Seorak](#connect-seorak),
   [Connect Salidium](#connect-salidium)). Credentials move only as files with mode 600, never
   through the clipboard, a prompt or a chat; the setup checks that they exist and that no one else
   can read them, and never opens them.
5. **Start Halcyonic**, and leave its window open while you use the headset:

   ```bash
   pnpm start
   ```

   Settings take effect when it starts. To use changed settings, stop it with Ctrl-C and start it
   again; restarting stops any agent at work, and its task then shows Can't tell yet.
6. **Connect the headset.** A development build connects over USB
   ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "Install and connect"). Pairing over Wi-Fi is your choice
   ([Pair a headset over Wi-Fi](#pair-a-headset-over-wi-fi)): `pnpm mac-setup pairing on` says what it
   opens and asks first; then restart Halcyonic, run `pnpm pair`, and in the headset choose Settings,
   Your computer, Pair with a computer. Until the headset reaches your Mac it plays the recorded
   demo, labelled as one; nothing in it reaches an agent.

### When the headset says something is wrong

The headset calls the Mac "your computer" ([WORDS.md](../product/WORDS.md)).

| The headset says | On the Mac |
| --- | --- |
| Can't reach your computer; trying again. | Start Halcyonic (`pnpm start`). Over USB, run `adb reverse tcp:47800 tcp:47800` again; over Wi-Fi, check pairing is on, the headset is on the same network, and the firewall (`pnpm mac-setup` checks it) |
| This headset's access code doesn't match your computer's, or something else is answering in its place | Start Halcyonic if it isn't running. Over USB: write the current access token into the app's private storage again with `run-as` ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "Install and connect"). If it still says so, something else holds port 47800 (`lsof -nP -iTCP:47800 -sTCP:LISTEN`), or `adb reverse` maps another headset port onto it: use `adb reverse tcp:47800 tcp:47800` |
| Your computer refused this headset's access code | Over USB: write the current access token into the app's private storage again with `run-as` ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "Install and connect") |
| Your computer no longer accepts this headset's pairing. | It was revoked: forget the computer on the headset, then `pnpm pair` |
| Your computer doesn't allow any folder yet. | `pnpm mac-setup allow`, then restart Halcyonic |
| Your computer can't use that folder right now | The folder moved or can't be read: put it back or choose another. If a folder you allowed is gone, Halcyonic won't start until it is back or you `pnpm mac-setup disallow` it |
| No agent app on your computer can start work right now. | `pnpm mac-setup agent-apps`, then restart Halcyonic |
| Voice isn't set up on your computer. Type instead. | Step 4, or keep typing |
| Usage left isn't set up on your computer yet. | Step 4; `pnpm mac-setup` says which part is missing |
| A task shows Can't tell yet after a restart | Its agent stopped with Halcyonic: open it and start it again |

If Halcyonic itself won't start, its last line says why, and `pnpm mac-setup` puts it first: a
settings file others can read (`chmod 600 ~/.halcyonic/settings.json`), Halcyonic's own OpenCode
settings naming a model that isn't on the Mac (run `pnpm mac-setup local-model` again), or a folder
you allowed that is gone.

## Run the control plane

```bash
pnpm dev
```

This listens on `127.0.0.1:47800`, stores its journal in `~/.halcyonic/control-plane.db`, and
creates the access token `~/.halcyonic/access-token` on first start. Logs are JSON on stdout; the
`control plane ready` line shows the address, the journal id, the registered runtimes, which
settings it took from `~/.halcyonic/settings.json` (`settings.used`), and each OpenCode and Codex
binary with whether it is the pinned copy (`agent_binaries`: `matches`, `differs`, or `no_pin` where
Halcyonic has no checksum for the processor). The token never appears in
logs. Use `pnpm start` for a run without file watching.

Every `HALCYONIC_` variable below can be set in the environment. `pnpm mac-setup` keeps the ones a
person sets up once (`HALCYONIC_PROJECT_ROOTS`, `HALCYONIC_OPENCODE_BIN`,
`HALCYONIC_OPENCODE_CONFIG_HOME`, `HALCYONIC_CODEX_BIN`, the three `HALCYONIC_WHISPER_` files,
`HALCYONIC_COMPANION_MODEL` and `HALCYONIC_COMPANION_OLLAMA_URL`, and `HALCYONIC_NETWORK_HOST`) in `settings.json` in the data directory, mode 600, which the control
plane reads for whatever its environment leaves unset; a variable present in the environment wins,
even empty. The file can never hold `HALCYONIC_CLAUDE_AGENT`, `HALCYONIC_CLAUDE_EXECUTABLE` or
`HALCYONIC_AGENT_ENV`, so paid model use and pass-through variables stay in the environment
([ADR 0024](../decisions/0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md)).

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
inside it, each with whether it is a repository (a `.git` entry directly inside), when it last
changed at its top level, and the projects already bound to it (`used_by`). Give a project its
folder when you create it:

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

`pnpm mac-setup local-model <tag>` writes these settings for you, with a `limit` for every model
Ollama lists, into a directory of Halcyonic's own, `~/.halcyonic/opencode-config`, which the control
plane gives OpenCode alone as its `XDG_CONFIG_HOME` (`HALCYONIC_OPENCODE_CONFIG_HOME`), so your own
`~/.config/opencode` stays as it is. The control plane refuses to start if those settings name a
default `model` or `small_model` that is not an Ollama model on this Mac, or if another OpenCode
settings file sits beside them.

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
`{"sandbox": "workspace-write", "approval_policy": "on-request"}`, their defaults.
A new, empty folder that is not a git repository works
([project-location.md](../validation/project-location.md)).
`approval_policy` `never` is refused, and `danger-full-access` needs `untrusted`.

Codex runs only on models served on this Mac, in a home of its own, `CODEX_HOME` set to
`~/.halcyonic/codex-home` (the data directory's `codex-home`), never your own `~/.codex`: your Codex
settings, sign-in, plugins and MCP servers don't apply, and nothing signs Codex in. Hosted Codex
models are not offered while it is open whether a ChatGPT sign-in may drive Codex
([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)). `pnpm mac-setup local-model <name>` writes the
home's `config.toml`, mode 600: the `ollama` provider, the model, a context of 65,536 tokens and
compaction at 52,000. Halcyonic makes the folder with mode 700 and won't start Codex in one that is
a link, another user's, open to others, or holding a sign-in (`auth.json`). Every launch turns off
plugins, the update check, analytics and web search with `-c` settings that outrank the file
([ADR 0011](../decisions/0011-codex-app-server-stable-surface.md), note of 2026-10-07). Variables
Codex signs in with (`OPENAI_API_KEY`, `CODEX_API_KEY` and the like) are left out of what
`HALCYONIC_AGENT_ENV` passes to Codex.

Each thread's rollout is written under that home's `sessions`, not where Salidium and Seorak read
Codex sessions, so Understand and Checks say they don't follow Codex tasks
([understanding-and-evaluation.md](../validation/understanding-and-evaluation.md)).

#### Local models through Ollama

Codex reaches Ollama through its built-in `ollama` provider, on port 11434 of this Mac, over the
Responses API. `GET /api/runtimes/codex/models` lists the model the home's `config.toml` names, and
a start carries it as `"model_ref": "ollama/qwen3.6:35b-a3b-nvfp4"`. A start may instead name the
model with options, and tell Codex another context than the file's:

```json
{
  "model_provider": "ollama",
  "model": "qwen3.6:35b-a3b-nvfp4",
  "context_window": 65536,
  "auto_compact_token_limit": 52000
}
```

The provider must serve its models on this Mac, and a model Ollama runs on its own remote service
(`:cloud`, `-cloud`) is refused. The thread is refused if Codex reports another provider or model for
it. Codex takes any model name without checking it: a name Ollama does not have fails the first
turn. The runs, the rollouts and what reaches the network are in
[local-models.md](../validation/local-models.md).

Its end to end tests run the binary against a fake provider when `CODEX_BIN` is set. They use
temporary homes, never your `~/.codex`, and fail if Codex tries to reach anything beyond loopback:

```bash
CODEX_BIN="$HALCYONIC_CODEX_BIN" node --test packages/integrations/codex/src/codex-runtime.e2e.test.ts
```

**On every Codex upgrade, run the network probe** too. It runs Codex as the control plane does, in
a fresh home with only what `local-model` writes, on an Ollama model this Mac already has (nothing
is pulled; the model is unloaded afterwards), and fails if any process of the Codex server's tree
holds a socket beyond loopback through startup, a minute idle and a full run, if lsof can't see the
server, or if it never sees the connection to Ollama:

```bash
CODEX_BIN="$HALCYONIC_CODEX_BIN" CODEX_E2E_OLLAMA_MODEL=qwen3.6:35b-a3b-nvfp4 node --test --test-name-pattern="nothing leaves loopback" packages/integrations/codex/src/codex-runtime.e2e.test.ts
```

It samples every 200 ms, so a connection shorter than that can be missed, and DNS lookups, which
macOS makes for the process, are not seen. On 0.157.0 the setting that keeps Codex off GitHub at
startup is undocumented (`features.plugins`), so a new version may need another.

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
yet, so until it runs the headset says "Usage left isn't set up on your computer yet. Set it up there to
see it here." and nothing more.
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

## Turn on Create's companion

Create's companion helps a person shape an idea into a project name and a first task, on a local
model this Mac's Ollama serves ([ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md),
[record](../validation/companion-model.md)). It is off until you name its model; nothing is pulled
because someone asked it something. Run Ollama on loopback with its cloud features off
(`OLLAMA_NO_CLOUD=1`, as for the agents' local models above), pull the model yourself, then start the
control plane with it:

```bash
ollama pull qwen3.5:9b
OLLAMA_MAX_LOADED_MODELS=2 OLLAMA_NO_CLOUD=1 ollama serve
HALCYONIC_COMPANION_MODEL=qwen3.5:9b pnpm dev
```

`qwen3.5:9b` (Apache-2.0, 6.6 GB, about 5.3 GiB loaded) is the companion's recommended model: a model
of its own, on Ollama's GGUF engine, which keeps to the reply's schema and answers beside a task that
generates on the agents' model, where the agents' own model would make it wait for each of the
task's steps ([record](../validation/companion-model.md)). Ollama must be able to keep two models
loaded (`OLLAMA_MAX_LOADED_MODELS=2`). The name is the model's as `ollama list` shows it; one with a
`cloud` tag is refused. Ollama's address defaults to `http://127.0.0.1:11434`; set
`HALCYONIC_COMPANION_OLLAMA_URL` only for another loopback port. `pnpm mac-setup companion
qwen3.5:9b` keeps the model in the Mac's settings file instead, once Ollama lists it as running on
this Mac, and `pnpm mac-setup` says whether the companion can run. Check that it can be asked, without
asking the model:

```bash
curl -s -H "Authorization: Bearer $(cat ~/.halcyonic/access-token)" http://127.0.0.1:47800/api/companion
```

The demonstration plays one exchange recorded from the companion. Record it again after changing
the prompt or the model, review the diff, and commit it; `--check` holds the committed file to its
rules (no brand a judge may not read, answers that were offered, a proposal at the end):

```bash
HALCYONIC_COMPANION_MODEL=qwen3.5:9b pnpm companion:record
```

## Pair a headset over Wi-Fi

Devices on the local network reach the control plane through a second listener, TLS only, which
is off unless you turn it on ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md),
[SECURITY.md](../architecture/SECURITY.md)). Loopback serves as before.

```bash
HALCYONIC_NETWORK_HOST=0.0.0.0 pnpm dev
```

`0.0.0.0` listens on every IPv4 interface; name one address, such as `192.168.1.23`, to listen on
that one only. `pnpm mac-setup pairing on` keeps `0.0.0.0` in the settings file instead, after
saying what it opens and asking, and `pnpm mac-setup pairing off` takes it out; either takes effect
at the next start. The port is 47801 (`HALCYONIC_NETWORK_PORT`). The first start creates the listener's
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
demonstration, "Practice agent" and "Practice agent, watch only"; both are the synthetic mock
runtime, and the second declares nothing but starting work. A question the directed scenario asks
(ADR 0022) must have one prompt, one choice among at least two options and nothing to type, so the
recording holds a continuation for each option; the recorder refuses any other. Every
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
- **`pnpm devices` or `pnpm pair` says something "can't prove it holds this Mac's access token"**:
  what answers on the port is not this data directory's control plane, so the token was not sent.
  `lsof -nP -iTCP:47800 -sTCP:LISTEN` shows what listens; start the control plane, or run the
  command with the same `HALCYONIC_DATA_DIR` and `HALCYONIC_PORT` as it.
- **Executions show `unknown` after a restart**: intended. Their runtime sessions did not survive
  the restart, so their true state cannot be known.
- **`runtime reused a native event id` in the log**: a runtime adapter gave a different record the
  native id of one already journaled, and the journal dropped it. It is an adapter defect; the
  warning names both events ([EVENTS.md](../architecture/EVENTS.md)).
- **`holds a fixture journal`**: the data directory contains a journal created as a fixture. Use a
  different `HALCYONIC_DATA_DIR`.
- **pnpm fails to switch versions**: pnpm 10 cannot install pnpm 12's native binary. The
  repository pins pnpm 11 for this reason; see ADR 0004.
