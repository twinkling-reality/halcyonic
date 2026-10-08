# Local models

- **Question:** Can Halcyonic's real runtimes run agents on open models that Ollama serves on the
  developer's Mac, through the control plane, with approvals, steering, interrupts and later turns;
  what leaves the machine while they do; and how fast and how large are they?
- **Date:** 2026-09-29; the choice of model checked on 2026-09-30.
- **Versions:** Ollama 0.34.4 (Homebrew, MLX engine); OpenCode `@opencode/cli` 2.0.18, darwin arm64
  sha256 `6759c7f8…6bf`; Codex `codex-cli` 0.157.0, darwin arm64 sha256 `ad0be20d…3714`; models
  `qwen3.6:35b-a3b-nvfp4` (23.6 GB, a mixture of experts with 3B active parameters),
  `qwen3.8:27b-nvfp4` (18.2 GB, dense) and `muse-glimmer:30b-mlx` (19.1 GB); macOS 26.7 on an Apple
  M5 Max with 64 GB.
- **Method:** Scratch control planes on loopback ports other than 47800, each with its own data
  directory, HOME and XDG directories, driven over REST by a small client. A monitor listed the
  internet sockets of the runtime's processes and of Ollama's every 200 ms (`lsof -nP -i`), and
  Ollama's own log recorded every request it served. The pinned binary was also run directly,
  behind a proxy that records and refuses every request, to see what it tries to reach. Code paths
  were read from the strings of the pinned binary, which embeds OpenCode's JavaScript. Nothing was
  pulled or deleted, and no hosted model was called.
- **Status:** Runtime verified for OpenCode and for Codex with all three models, for the scenarios
  below. Long runs, several executions at once, and a Mac under memory pressure from other work are
  not tested.

## The Ollama server

Ollama ran as one server on 127.0.0.1:11434 with `OLLAMA_CONTEXT_LENGTH=65536`,
`OLLAMA_MAX_LOADED_MODELS=1`, `OLLAMA_NO_CLOUD=1`, `OLLAMA_FLASH_ATTENTION=1` and
`OLLAMA_KV_CACHE_TYPE=q8_0`. All three models run on its MLX engine. With one model loaded at a
time, switching models unloads the other, and a model left idle for five minutes is unloaded.

- **The MLX engine does not enforce the context length.** It processed prompts of 68,491 and 72,038
  tokens without truncating or failing, although the server's context length is 65,536. The limit
  that matters is the model's own (262,144 for `qwen3.6:35b-a3b-nvfp4`) and the Mac's memory.
- Its log warns that structured output is unavailable (the Homebrew build lacks `xgrammar`). Tool
  calls worked with all three models.

## OpenCode 2.0.18 with Ollama

### What OpenCode does with Ollama

- **Discovery, no configuration needed.** OpenCode's Ollama plugin reads `GET /api/tags` and
  `POST /api/show` on 127.0.0.1:11434, each with a 1 s timeout, at startup and every 30 s, and lists
  every model whose capabilities include completion. A model's tool calling comes from Ollama's
  capabilities (`smollm2:135m` is listed without tools), and its context from the model's own
  metadata (262,144 for `qwen3.6:35b-a3b-nvfp4`), not from the context Ollama loads.
- **Configuration can correct both.** `providers.ollama.models["<tag>"].limit` in `opencode.json`
  sets the context and output limits OpenCode assumes for a discovered model, and `disabled: true`
  removes a model from OpenCode's list. A project's own `opencode.json` does the same for that
  directory only. `providers.ollama.settings.baseURL`, which the OpenCode documentation gives for a
  remote Ollama, made 2.0.18 list no Ollama model at all within 40 s, and reach neither the given
  address nor the default one.
- **The list precedes discovery.** `GET /api/model` "may precede initial plugin settlement", as
  OpenCode's own API document says, and each directory settles separately: read from a fresh
  server, the list was empty, then held only OpenCode's hosted models, and held the Ollama models
  about 2.5 s after launch. A session prompted 750 ms after launch, with an Ollama model, failed
  its turn with `provider_no_route` ("Unsupported package for ollama/..."), and so did a second
  prompt 0.3 s later. The adapter now waits for a named model to be listed in the execution's
  directory, for up to 10 s, and refuses the start in words when it never is (below).
- **With no configuration, the default model is hosted.** OpenCode lists seven free models of its
  own hosted service, OpenCode Zen (`https://opencode.ai/zen/v1`), from a catalog built into the
  binary, and without a configured model its default is one of them (`opencode/space-bunny-free`),
  even with Ollama's models listed. An execution started without a model would send its prompt,
  and the code it reads, to opencode.ai. Before discovery settles the default is none.
- **Everything is allowed by default.** OpenCode's default agent allows every action
  (`action: "*", effect: "allow"`) except reading `.env` files and working outside the project
  directory, which ask. Shell commands, edits, `webfetch` and `websearch` run without asking, so
  with a default configuration Halcyonic never shows an approval. The runs below used an
  `opencode.json` that asks before `shell` and denies `webfetch` and `websearch`. A session can
  carry its own permission rules (`POST /api/session` accepts `permissions`, and the last matching
  rule wins); whether Halcyonic should impose some is an open question
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

### Flows through the control plane

Each row is one execution through the scratch control plane, with the model named as
`ollama/<tag>`, OpenCode's context limit for the model set to 65,536 and its output limit to
16,384. Times are from the command to the event, including loading the model when it was not
loaded.

| Flow | `qwen3.6:35b-a3b-nvfp4` | `qwen3.8:27b-nvfp4` | `muse-glimmer:30b-mlx` |
| --- | --- | --- | --- |
| Start, approve a shell command, answer | Completed in 32.2 s (31.5 s to the approval, the model loading) | Completed in 47.1 s | Completed in 62.1 s |
| Second turn at rest (write a file) | Completed; the model then checked with `ls`, which asked again | Completed in 10.1 s | Completed in 9.3 s |
| Deny, with a message and then without | With a message: the tool failed and the model asked for the same command 15 s later. Without: the turn ended interrupted | Interrupted after 49.2 s; one denial settled two parallel requests | Interrupted after 45.6 s |
| Interrupt a long answer after 12 s | Confirmed in 264 ms, turn interrupted; the next turn completed with the context kept | 291 ms, interrupted; next turn completed | 258 ms, interrupted; next turn completed |
| Steer while running (add a file) | Accepted in 267 ms; one turn; the file was created | 271 ms; one turn; created | 257 ms; one turn; created |
| Model disabled for the directory | Refused before any session, `model_unavailable`, after the 10 s wait | Not run | Not run |
| Model disabled between turns | The next turn failed at once: `provider_no_route`, "Model unavailable" | Not run | Not run |

Observed along the way:

- **A denial without a message settles every pending request.** With `qwen3.8:27b-nvfp4`, which
  asked for two commands at once, one denial resolved both as denied and ended the turn as
  interrupted. The earlier record listed this cascade as untested.
- A denial's message still reaches neither the model nor any event, as recorded for 2.0.18: the
  model asked again for the command it was refused.
- A steered instruction reached the model at the next step of the same turn; two of the three
  models then treated "also create delta.txt" as replacing the file not yet written.
- The first turn after a model loads spends 20 to 60 s before its first tool call: loading takes
  6 to 9 s, and OpenCode's opening prompt is about 6,500 tokens.

### Context overflow

`qwen3.6:35b-a3b-nvfp4` was asked to read a 3,000 line file (282 KB, about 80,000 tokens) in
full, 500 lines at a time, with OpenCode told the model's context is 65,536 tokens. Approvals were
answered automatically.

- OpenCode sent prompts of 26,851, 47,203, 68,491 and 72,038 tokens: it did not compact before
  passing its own 65,536 limit. Ollama processed each without error (above).
- Each long prompt was processed almost from scratch: Ollama's prefix cache matched 6,529 of the
  68,491 tokens, so about 62,000 tokens took 144 s (430 tokens a second), and the request of
  72,038 tokens took 10 min 25 s in all. Memory peaked at 35.2 GiB.
- OpenCode then compacted: its next request held 7,082 tokens. The model had lost its working
  directory, asked to read `/tmp/*` (an approval for a directory outside the project) and searched
  for the file again. The run was interrupted at 15 minutes, in 9 tool calls, without an answer.
- The adapter reported the turn faithfully throughout; OpenCode's compaction emits nothing the
  adapter maps, so a client sees a long turn with tool activity.

Without a configured limit OpenCode assumes 262,144 tokens, so it would not compact before the
Mac's memory ran out; at 68,000 tokens Ollama already held 26 GiB and peaked at 35 GiB for this
model (inference, not run).

### Changes to the adapter

- **Steering.** `instruct_while_running` is now declared: an instruction sent while a turn runs is
  posted with OpenCode's `steer` delivery, the command completes when OpenCode accepts it, and it
  reaches the model when the running step ends, in the same turn. An instruction still waiting
  when the turn is interrupted stays in the session's inbox and reaches the model with the next
  instruction; steering never starts a turn by itself. End to end tests cover both with the fake
  provider.
- **Waiting for the model.** A start that names a model waits, for up to 10 s, until OpenCode lists
  it for the execution's directory; one still missing is refused with `model_unavailable` before
  any session exists. Without a named model, the start waits for OpenCode to have a default.

## Codex 0.157.0 with Ollama

Versions: `codex-cli` 0.157.0, darwin arm64 sha256 `ad0be20d…3714`, `codex app-server` over stdio,
stable surface only. Its scratch `CODEX_HOME` held a `config.toml` that sets `model_provider =
"ollama"` and a local model as defaults, turns off plugins and analytics, and no sign-in, so no
thread could reach a hosted model.

### What Codex does with Ollama

- **The built-in `ollama` provider** speaks the Responses API: every turn was a `POST /v1/responses`
  to Ollama on port 11434. `thread/start` takes `modelProvider` and `model`, and a `config` object
  of overrides for the one thread; its answer, like `thread/resume`'s, reports the `model` and
  `modelProvider` the thread got.
- **Any model name is accepted.** Codex checks nothing at `thread/start`. A model outside its
  catalog gets fallback metadata, a context of 272,000 tokens, and a warning that this "can degrade
  performance". The per-thread overrides `model_context_window` and
  `model_auto_compact_token_limit` apply: the token counts of a thread given 65,536 report a
  window of 62,259, the 95% Codex uses.
- **Its lists do not describe a local provider.** `model/list` returned the 11 models of the
  catalog built into the binary, all OpenAI's, with `model_provider = "ollama"` configured just as
  without it, and Codex never asked Ollama for its models. `config/read` names the configured
  provider and model, but `model_providers` holds only the providers the configuration defines
  (`{}` here): the built-in `ollama` provider's address is not reported.
- **No pull.** The binary contains Ollama's pull code for `codex --oss`; app-server never called
  `/api/pull` in any run (Ollama's log), and no run named a model Ollama does not have.

### Changes to the adapter

Three start options: `model_provider`, and `context_window` and `auto_compact_token_limit` in
tokens, sent as the thread's `modelProvider` and `config` overrides, on start and on resume. A
thread for which Codex reports another model or provider than asked is refused, as a thread
with other approval settings already is, so a thread meant for a local provider can never run on a
hosted one.

### Flows through the control plane

Each execution named `model_provider: "ollama"`, the model, `context_window: 65536`,
`auto_compact_token_limit: 52000` and `approval_policy: "untrusted"`, under which Codex asks before
any command it does not know to be safe.

| Flow | `qwen3.6:35b-a3b-nvfp4` | `qwen3.8:27b-nvfp4` | `muse-glimmer:30b-mlx` |
| --- | --- | --- | --- |
| Start, approve a command, answer | Completed in 20.3 s | Completed in 64.4 s | Completed in 60.0 s |
| Second turn at rest (write a file) | Completed in 3.9 s | Completed in 10.9 s | Completed in 13.6 s |
| Deny | Two denials, then the turn completed, 14.5 s | One denial, then completed, 19.2 s | Twelve denials, each met with another command, then completed, 155 s |
| Interrupt a long answer after 12 s | Confirmed in 264 ms, interrupted; the next turn completed | 257 ms, interrupted; next completed | 256 ms, interrupted; next completed |
| Steer while running (add a file) | Accepted in 258 ms; one turn; all four files | 254 ms; one turn; all four | 254 ms; one turn; all four |

Unlike OpenCode's denial without a message, Codex's `decline` refuses only the one command: the
turn goes on, and the model may ask for another. Muse Glimmer asked twelve times before it gave up.

### Context overflow

`qwen3.6:35b-a3b-nvfp4` was asked to print the same 3,000 line file with `sed` and report two
facts from it.

- **In chunks of 500 lines**, each about 13,000 tokens, Codex gave the model about 4,000 tokens
  of each output. The thread stayed under 33,000 tokens and finished in 205 s: the line it was
  asked for was right, and the count it gave was wrong (1,500 for 1,250), extrapolated from the
  parts it saw.
- **In chunks of 100 lines**, which fit, the thread grew by about 3,800 tokens a step, and Codex
  compacted three times: twice once the thread passed 51,000 tokens, and once only at 65,509,
  after a single command added about 15,000 tokens. Each compaction sent Ollama a prompt with a new
  prefix, 49,922 to 65,509 tokens, which took 62 s to 2 min 38 s; the thread then resumed from
  about 9,000 tokens. It finished in 780 s after 42 commands, with both answers right.
- Unlike OpenCode's, Codex's prompts kept their prefix from step to step, so Ollama's prefix cache
  matched all but the newest 4,000 tokens and a step took 6 to 15 s even at 47,000 tokens.
- Ollama never refused a prompt, so the open defect openai/codex#48870 (a local server that refuses
  an oversized prompt with HTTP 400 leaves auto-compaction unable to recover) did not arise.

### Rollouts

Every thread's rollout, under the scratch `CODEX_HOME`, carried in its `session_meta`
`thread_source: "halcyonic"`, `source: "vscode"`, `originator: "halcyonic"`,
`model_provider: "ollama"` and `cli_version: "0.157.0"`, no `forked_from_id` or
`parent_thread_id`, and no model; each `turn_context` names the model, `qwen3.6:35b-a3b-nvfp4`
verbatim. `token_count` rows carry the context window Codex was given and cumulative totals that
never decreased, even across the three compactions; their `rate_limits` are all null.

- **Salidium** discovers rollouts under `$CODEX_HOME/sessions` and `archived_sessions` of its own
  environment (`~/.codex` by default), with no filter on `thread_source` or `originator`, takes the
  model from `turn_context` as any string and records compactions (its Codex rollout parser, read
  at `e663354`). So it showed a Halcyonic thread on a local model once the thread was written to the
  developer's `CODEX_HOME`, which the adapter kept until 2026-10-07; since then the adapter uses a
  home of its own, which Salidium does not read (below). Not run: these runs used a scratch
  `CODEX_HOME`.
- **Seorak** before its commit `7934de5e` skipped every such thread (any `thread_source` other than
  `"user"`) and refused model ids with `:`. Its predicates at `7934de5e`, applied to these
  rollouts, capture them and keep the model.

## Choosing a model

Checked on 2026-09-30 with the build that lets a person choose a runtime's model
([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)), on the
same scratch control planes. Their journals, written by the earlier build, were migrated on start:
the 16 and 14 `execution.start` commands they held gained `model_ref: null`, and both replayed.

- **OpenCode's list** (`GET /api/runtimes/opencode/models`) answered in 4.4 s the first time, which
  launched the server and waited for its list to settle, and in 4 ms after. It held 14 models: the
  seven Ollama models, all served on this Mac, and seven free models of OpenCode's own hosted
  service (OpenCode Zen), all remote, which OpenCode offers even with its catalog fetch off. The
  two aliases read as what they are: `ollama/gpt-4o:latest`, named "gpt-4o:latest (Ollama)" and
  `ollama/gpt-3.5-turbo:latest`, both served on this Mac. `smollm2:135m` is listed without tool
  calling, with 8,192 tokens of context; the three configured models carry their configured
  65,536, and the `llama3.2:1b` weights, under all three names, the 131,072 OpenCode reports for
  them. Nothing in the answer resembled a provider's settings: no address, key or header.
- **A start from OpenCode's list** on `ollama/qwen3.6:35b-a3b-nvfp4` ran its turn to completion in
  17.5 s, the model's load included. OpenCode reported the model at the first step, recorded as
  `runtime.model.used` with `observed` provenance, and the execution's `model_ref` held it. A start
  on `ollama/not-a-listed-model` failed with `model_unavailable` after 10.3 s, the adapter's wait
  for a model OpenCode has not discovered yet, before any session existed.
- **Codex's list** answered in 262 ms the first time, launching the server, and 5 ms after. It held
  one model, the one `config.toml` names: `ollama/qwen3.6:35b-a3b-nvfp4`, named
  "qwen3.6:35b-a3b-nvfp4 (Ollama)", served on this Mac, tool calling unknown and context unknown,
  since the configuration sets no `model_context_window`. The OpenAI catalog built into Codex was
  left out.
- **A start from Codex's list** with the context options ran its turn to completion in 8.3 s, and
  Codex's answer to `thread/start` was recorded as the model used. A start on
  `ollama/not-a-listed-model` failed with `model_unavailable` in 256 ms, before any thread existed.
- The socket monitors saw nothing beyond loopback while the lists were read and the turns ran.

## Network

What left, or tried to leave, the Mac while OpenCode ran:

- **OpenCode's model catalog.** At every launch whose cached catalog is older than five minutes,
  OpenCode fetches `https://models.opencode.ai/api.json`, and again every five minutes while it
  runs. Behind the refusing proxy, a fresh launch tried `CONNECT models.opencode.ai:443`.
  `OPENCODE_DISABLE_MODELS_FETCH=true` stops it: no attempt in 70 s, and OpenCode falls back to
  the catalog built into the binary. The runs above passed it through `HALCYONIC_AGENT_ENV`.
  The adapter now sets it to `true` on every launch and rejects an override; its environment test
  verifies this. A post-change socket monitor run has not been made.
- **ripgrep.** When `rg` is neither on OpenCode's PATH nor in `$XDG_CACHE_HOME/opencode/bin`,
  OpenCode downloads ripgrep 15.1.0 from `github.com/BurntSushi/ripgrep/releases` the first time an
  agent searches files. Observed: the agent's first `glob` opened connections from the OpenCode
  process to 140.82.114.4 and 185.199.109.133 on port 443 (GitHub). There is no switch; a ripgrep on
  the PATH Halcyonic passes to OpenCode, for example Homebrew's, prevents it. Homebrew's
  `/opt/homebrew/bin/rg` is installed on this Mac as of 2026-09-30.
- **Tools.** `webfetch` and `websearch` are allowed by default (above); the runs denied them.
- Nothing else. With the catalog fetch off and ripgrep in place, the monitor saw no socket beyond
  loopback from OpenCode's processes in any run, and none from Ollama's server once the model
  downloads had finished (during them, it held connections to Cloudflare addresses, which serve
  Ollama's registry).

While Codex ran, with plugins and analytics off and no sign-in: nothing. The monitor saw no socket
beyond loopback from Codex's processes in any run. Its model requests went to Ollama on the same
Mac, and so did the metadata Codex adds to them (the installation id and, for a git workspace, its
path and latest commit). That held for that home's settings, `features.plugins = false` and
`analytics.enabled = false`; without the first, Codex reaches GitHub at startup (below).

### Codex's plugin sync at startup (2026-10-03)

A runtime test of the pinned 0.157.0 `codex app-server` over stdio, with a fresh scratch
`CODEX_HOME` holding no sign-in, one `initialize` request, its stderr read with anything
token-like masked, and its process tree's internet sockets listed every 0.5 s (`lsof -a -n -P -i`),
stopping it at once on any socket beyond loopback:

- **Without `features.plugins = false`:** within the second after `initialize` answered, app-server
  held an established connection to 140.82.113.3:443 (`lb-140-82-113-3-iad.github.com`), and
  stopped there. A second try, with all four of the documented settings that might cover it off
  (`check_for_update_on_startup = false`, `analytics.enabled = false`, `features.remote_plugin =
  false`, `features.apps = false`, from Codex's configuration reference at
  developers.openai.com/codex/config-reference), connected to 140.82.114.4:443 (GitHub) the same
  way. The binary carries a plugin marketplace that fetches from the GitHub API; that this is the
  plugin sync is an inference.
- **With `features.plugins = false`** (and the update check and analytics off): `initialize`
  answered in 0.2 s, nothing on stderr, and no internet socket at all in 75 s through startup and
  idle. A full run then held only 127.0.0.1:11434 (Ollama) for its five minutes, sampled every
  second.
- **The key is undocumented:** `features.plugins` is not in today's configuration reference; the
  pinned binary's own text names it ("features.plugins", and "Plugins are disabled. Enable the
  plugins feature to use /plugins."). Any Codex upgrade must repeat this test.
- **A home with its own settings:** a run with the developer's own `CODEX_HOME`, where plugins are
  not off, probably made the same request; inferred, not captured. A fresh home whose `sessions`
  folder is all of the developer's history also first indexes that history into a state database
  (stderr: "state db backfill is running"), long enough that `initialize` was not answered in time.
- Whether Halcyonic's adapter should start every local-model thread with these settings was open
  when this was recorded; the adapter now does, in a home of its own (2026-10-07, below).

### Codex in a home of its own, local models only (2026-10-07)

The decision ([ADR 0011](../decisions/0011-codex-app-server-stable-surface.md), note of
2026-10-07): the adapter launches Codex with `CODEX_HOME` set to `<data dir>/codex-home`, mode
700, never the person's `~/.codex`, refuses a home that is a link, another user's, open to others
or holding `auth.json`, and passes the switches as `-c` overrides on every launch, above the
home's `config.toml`. A thread runs only on a provider served on this Mac.

- **The overrides take effect** (runtime test, the pinned 0.157.0 `codex app-server` over stdio, a
  fresh scratch `CODEX_HOME` and `HOME` with no configuration and no sign-in, one `initialize`):
  launched with `-c features.plugins=false -c check_for_update_on_startup=false
  -c analytics.enabled=false -c web_search="disabled" -c cli_auth_credentials_store="file"`,
  `config/read` reported `features.plugins: false`, `check_for_update_on_startup: false`,
  `analytics.enabled: false`, `web_search: "disabled"` and `cli_auth_credentials_store: "file"`,
  with `model_provider` and `model` null. Nothing reached stderr. lsof listed the server's internet
  sockets every 200 ms for 30 s through startup and idle: none, and lsof could see the process in
  all 149 samples. The home then held Codex's SQLite databases (`state_5`, `logs_2`, `goals_1`,
  `memories_1`, `queue_1`), `installation_id`, a `skills` folder and `tmp`.
- **The full set, after the security review** (the same scratch run, 2026-10-07): with every
  feature of 0.157.0 that is on by default and reaches the network or another app set false by
  `-c` (`plugins`, `apps`, `remote_plugin`, `in_app_updates`, `image_generation`, `browser_use`,
  `browser_use_external`, `computer_use`, `skill_mcp_dependency_install`, `tool_suggest`,
  `daemon_auto_start`; each `default_enabled: true` in `codex-rs/features/src/lib.rs` at
  rust-v0.157.0, where `plugin_sharing` and `system_proxy_fallback` are too, and were added), and
  `mcp_oauth_credentials_store="file"`, `config/read` reported each as set and nothing reached
  stderr or the network in its short run. `config/read`'s `features` echoes any key it is given,
  `zz_not_a_feature` included, so that each feature exists was read from the source, not from
  the answer; an unknown top-level key is left out of the answer.
- **`app-server` takes `-c`**, as its `--help` says: "Override a configuration value that would
  otherwise be loaded from `~/.codex/config.toml`." `--disable <FEATURE>` is the same as
  `-c features.<name>=false`.
- **Sign-in:** the binary's text names `cli_auth_credentials_store` with `"file"`, and a keyring
  store; with `"file"`, a sign-in can only come from `auth.json` in the home, which the adapter
  refuses. It names `OPENAI_API_KEY`, `CODEX_API_KEY`, `CODEX_ACCESS_TOKEN`,
  `OPENAI_IDENTITY_TOKEN_FILE`, `CODEX_CONNECTORS_TOKEN` and `CODEX_GITHUB_PERSONAL_ACCESS_TOKEN`
  among the variables it reads; the adapter refuses them, and the control plane leaves them out of
  what `HALCYONIC_AGENT_ENV` passes to Codex. Which of them app-server reads at startup was not
  tested.
- **Web search:** the binary's text names `web_search` with `disabled`, `cached`, `indexed` and
  `live`, and says that when it is on "the native Responses `web_search` tool is available to the
  model". It was turned off so no local model is offered a tool that a provider might run remotely;
  whether Ollama's Responses API would run one was not tested.
- **Codex's source, read for the security review's findings** (github.com/openai/codex at tag
  `rust-v0.157.0`, 2026-10-07; read, not built):
  - `codex-rs/features/src/lib.rs`: each feature turned off has `default_enabled: true`.
  - `codex-rs/model-provider-info/src/lib.rs`, `merge_configured_model_providers`: a configured
    entry under a built-in provider's id is ignored, except Amazon Bedrock's (`amazon-bedrock`,
    `amazon-bedrock-runtime`); `create_oss_provider` reads `CODEX_OSS_PORT` and
    `CODEX_OSS_BASE_URL`.
  - `codex-rs/config/src/config_layer_source.rs`, `precedence()`: device profile 0, system
    `config.toml` 10, enterprise-managed 15, the home 20 (21 with a profile), a project 25, `-c`
    overrides 30, `managed_config.toml` 40 and its device-profile form 50.
  - `codex-rs/core/src/config/managed_features.rs`, `normalize_candidate`: requirements' pinned
    features are set over the configuration; `codex-rs/config/src/config_requirements.rs`,
    `apply_exact_to_config`, projects only some requirement fields into `config/read`, not pinned
    features.
  - `codex-rs/app-server-protocol/src/protocol/common.rs` and `v2/config.rs`:
    `configRequirements/read` takes no parameters and answers `requirements`, "Null if no
    requirements are configured"; the method is in the pinned binary's stable method list
    (`fixtures/methods-stable.json`). On this Mac it answered `{"requirements": null}` (runtime
    test, a scratch home).
  - `codex-rs/app-server/src/request_processors/thread_processor.rs`, `thread/start`: when a
    folder's trust is unknown, it is not projectless and the thread's sandbox can write it, Codex
    records the folder, or its git root, as trusted in the home's `config.toml`;
    `codex-rs/config/src/loader/mod.rs` decides projectless (no root marker, `.git` by default, no
    checkout root and no project layer).
- **The network probe**, the check to re-run on every Codex upgrade, is the end to end test
  "nothing leaves loopback through startup, idle and a full run on a local model"
  (`packages/integrations/codex/src/codex-runtime.e2e.test.ts`). Its runs are recorded here as
  they are made.
  - **2026-10-07, at `ae9df92c`**, on `qwen3:4b-instruct` (2.5 GB, the coordinator's choice for a
    shared Mac; the sockets watched don't depend on the model), Ollama the macOS app as it ran,
    1-minute load about 17, swap 12.7 of 13.3 GB in use from other work: passed in 84 s. The
    probe watched every process below the test from before the launch, every 200 ms: 201 samples
    with the server running, 148 of them through startup and the minute idle, lsof seeing the
    server in all of them. No socket beyond loopback in any sample. No loopback socket while idle;
    during the run, two connections from Codex to `127.0.0.1:11434` (Ollama), the probe's proof
    that it sees sockets at all. The model answered and Codex ended the turn
    (`runtime.turn.completed`). The thread's `rollout-*-<thread id>.jsonl` was already in the
    home's local-date folder when the start returned, and the person's `HOME` gained no `.codex`.
    The model was unloaded afterwards (`keep_alive` 0; Ollama's `/api/ps` listed nothing).
  - The same gate ran the fake-provider suite at that commit, 22 tests, all passing: the switches
    and the local-only check hold against the real binary, which reported every setting in
    `config/read` and `{"requirements": null}`.
  - **2026-10-07, at `160477a7`** (before Codex is registered on the owner's Mac), on
    `qwen3:4b-instruct` again, in a project that is a git repository whose own
    `.codex/config.toml` starts an MCP server that leaves a mark: passed in 76 s. 184 samples,
    148 through startup and idle, nothing beyond loopback, one connection to Ollama during the
    run; the turn completed; the MCP server never started and the home recorded no trust. This
    time the rollout was written just after the start returned. The probe now also watches every
    process running the binary, wherever it was started from.

  - **2026-10-07, at `dced6316`** (after the review's last findings), on `qwen3:4b-instruct`,
    1-minute load about 40 and swap 8.9 of 10.2 GB in use from other work: passed in 92 s. 133
    samples, 104 through startup and idle (fewer than before, the Mac being busy, still more
    than one a second), nothing beyond loopback, two connections to Ollama, the turn completed,
    no MCP server, no trust; the model was unloaded afterwards.

### Before Codex is registered on a Mac (2026-10-07)

End to end tests on the pinned 0.157.0, in the suite, at `160477a7` and `77110f68`, and all of
them again at `dced6316` (26 tests, all passing), with the fake provider on loopback unless said:

- **E1, the settings applied:** in a fresh home with no configuration, launched with the adapter's
  arguments, `config/read` reported every local-only setting and `configRequirements/read`
  answered `{"requirements": null}`.
- **E3, a control for the probe:** a bare app-server in a fresh home without the settings held an
  established connection from `codex` to 104.18.32.47:443, a Cloudflare address, within half a
  second, and the probe saw it; at `dced6316` it saw `git-remote-https`, a child of Codex, holding
  a connection to 140.82.113.4:443 (GitHub) within two seconds, the plugin sync the switches stop. So a probe that sees nothing beyond loopback could have seen
  something. Skipped when `github.com` does not resolve.
- **E2, a project's own settings:** in a git repository whose `.codex/config.toml` starts an MCP
  server that appends to a file, with the project trusted in the home, a bare app-server started
  the server: the control shows the trap works. The same home, with the thread's overrides
  marking the folder and every folder above it untrusted, never started it: a thread's overrides
  outrank the home's trust. The adapter refused that home before any thread. With a home that
  records nothing, a turn completed, the server never started, and no `trust_level = "trusted"`
  was written.
- **E4, a provider under a built-in id:** a home whose `config.toml` defines `[model_providers.openai]`
  or `[model_providers.ollama]` with a loopback address is refused by Codex itself:
  `thread/start` fails with "model_providers contains reserved built-in provider IDs: `openai`.
  Built-in providers cannot be overridden." No request reached the address, nothing went through
  the proxy trap, and no socket left loopback. So the security review's scenario cannot arise
  from the home's file on 0.157.0; the adapter still judges `openai` by `openai_base_url` alone,
  in case another layer or a later version takes such an entry.
- **System configuration:** Codex reads `/etc/codex/config.toml`,
  `/etc/codex/managed_config.toml` and `/etc/codex/requirements.toml` whatever the home (named in
  the binary's text, and ranked as the source read above shows; no such file was run, since
  `/etc/codex` does not exist on this Mac, 2026-10-07). The adapter refuses Codex right after
  launch when `config/read` misses a local-only setting, as under a managed file that turns
  plugins back on, or `configRequirements/read` reports any requirements, and refuses a thread
  Codex reports on another provider, model or approval setting than asked.
- **A limit of that check, and its fix:** it runs after `initialize`, so under a managed file that
  turns plugins back on, Codex's startup connection could begin before the server is stopped.
  Since 2026-10-07 the adapter also refuses before any launch while `/etc/codex` exists (Codex
  reads managed hooks, `hooks.json`, and skills there too: `codex-rs/hooks/src/engine/discovery.rs`
  and `codex-rs/ext/skills/src/host_roots.rs`) or a `com.openai.codex` file under
  `/Library/Managed Preferences`, machine-wide or for the user (a forced device profile's
  `config_toml_base64` and `requirements_toml_base64`, `codex-rs/config/src/loader/macos.rs`);
  refuses after launch when `config/read`'s layers include a device-profile, enterprise,
  managed-file or non-empty system layer; and remembers only those after-launch refusals, so no
  list or start launches Codex again under them, while a refusal before launch lifts once the
  path is gone.
- **Skills:** 0.157.0 discovers skills from an untrusted project (`.codex/skills`,
  `.agents/skills`) and from `~/.agents/skills` (`codex-rs/config/src/state.rs`,
  `codex-rs/ext/skills/src/host_roots.rs`), and its `skills` settings
  (`codex-rs/config/src/skills_config.rs`: `bundled`, `include_instructions`,
  `max_context_tokens`, per-skill rules) have none that turns discovery off. What reaches the
  model was read from the fake provider's requests (runtime test, 2026-10-07, a bare app-server
  with the adapter's arguments, a git project with a skill in `.codex/skills` and one in
  `.agents/skills`, one in the home's `~/.agents/skills`, the project untrusted as the adapter
  marks it):
  - **By default** the one request of a turn listed all three probe skills, untrusted project
    and all, with Codex's bundled ones (`imagegen`, `openai-docs`, `plugin-creator`,
    `skill-creator`, `skill-installer`), under "A skill is a set of local instructions to follow
    that is stored in a `SKILL.md` file"; the word "skill" appeared 59 times.
  - **With `skills.include_instructions = false`** none of them, and the word "skill" not once,
    in the request; `config/read` reported `skills: {"include_instructions": false}`. Adding
    `features.skill_search = false` changed nothing further, so it is not used.
  - The adapter now passes `skills.include_instructions=false` on every launch and checks it;
    the end to end test E5 plants the same three skills and fails if any name, `SKILL.md` or
    `skill-installer` reaches the provider.

## Speed and memory

Measured through Ollama's own API with thinking off: three different short prompts with 256 tokens
of output each, after a first request that loaded the model, and a prompt of about 25,000 tokens
(19,322 for Muse Glimmer's tokenizer) with a short answer. Loads were measured twice, and depend on
what the file cache holds. Memory is what Ollama's MLX engine reports holding with the model loaded,
and at its peak. Sending the same short prompt a second time gave slower output, 30, 20 and 16
tokens a second; the cause was not found.

| | `qwen3.6:35b-a3b-nvfp4` | `qwen3.8:27b-nvfp4` | `muse-glimmer:30b-mlx` |
| --- | --- | --- | --- |
| Load | 6.0 and 6.4 s | 3.4 and 6.2 s | 4.9 and 8.8 s |
| Output, short prompts (three runs) | 48 to 59 tokens a second | 31 to 36 | 21 to 26 |
| Prompt processing, about 25,000 tokens | 910 tokens a second (28 s) | 175 (145 s) | 187 (103 s) |
| Output after that prompt | 30 tokens a second | 15 | 16 |
| Memory held, loaded | 22.1 GiB | 17.2 GiB | 17.8 GiB |
| Memory at the long prompt, peak and held after | 26.6 and 23.2 GiB | 24.5 and 20.1 GiB | 19.2 and 18.5 GiB |

## Consequences

- OpenCode runs local models through Halcyonic with no change to how the control plane is used:
  name the model as `ollama/<tag>`. For work that stays on the Mac, the person also needs, in
  OpenCode's configuration: a context limit for each local model, permissions that ask before
  shell commands and refuse `webfetch` and `websearch`, and ideally a default local model. The
  adapter disables catalog fetches. A ripgrep on Halcyonic's PATH prevents OpenCode downloading it;
  `/opt/homebrew/bin/rg` is installed on this Mac as of 2026-09-30. The runbook says how.
- `qwen3.6:35b-a3b-nvfp4` is the fast default: about twice the output speed and five times the
  prompt processing of the other two. Long contexts are slow with every model, because the
  prefix cache rarely survives OpenCode's rewriting of earlier turns.
- A client should expect the first turn on a model to take tens of seconds.
- Through Halcyonic, Codex runs only on models served on this Mac, from a home of its own with
  plugins, the update check, analytics and web search off on every launch (2026-10-07, above).
- Codex runs local models through the adapter's new options, and compacts on time when told the
  window; without `context_window` it assumes 272,000 tokens. Its lists cannot tell which models a
  local provider serves, so a model list for Codex has to come from its configuration, not from
  `model/list` alone, which is how the adapter lists them.
- Codex truncates a command's output before the model sees it, so an agent that reads a large file
  in large pieces answers from part of it without saying so.

## Not verified

- OpenCode compacting a local model's context before Ollama's memory runs out without a configured
  limit.
- Whether OpenCode's `providers.ollama.settings.baseURL` works in a later version.
- Long runs, several executions at once, and memory pressure with Unity or other large processes
  open.
- The overflow and removed-model runs with the two dense models.
- Codex with a model name Ollama does not have: not run against the real Ollama, to rule out any
  path to a pull.
- openai/codex#48870, which needs a server that refuses an oversized prompt.
- The initial local-model runs kept Codex away from the developer's `~/.codex`. A later scratch
  control plane using the normal Codex home produced one local-model thread that both Salidium and
  Seorak returned through Halcyonic's routes
  ([understanding-and-evaluation.md](understanding-and-evaluation.md)); its Seorak cost estimate
  was null, so the token accounting remains unverified.
- Claude Code's list of models against the real CLI, which starts a Claude Code process that may
  reach Anthropic: checked only against the Agent SDK's types and a fake.
