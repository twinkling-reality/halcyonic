# Local models

- **Question:** Can Halcyonic's real runtimes run agents on open models that Ollama serves on the
  developer's Mac, through the control plane, with approvals, steering, interrupts and later turns;
  what leaves the machine while they do; and how fast and how large are they?
- **Date:** 2026-09-29.
- **Versions:** Ollama 0.34.4 (Homebrew, MLX engine); OpenCode `@opencode/cli` 2.0.18, darwin arm64
  sha256 `6759c7f8…6bf`; models `qwen3.6:35b-a3b-nvfp4` (23.6 GB, a mixture of experts with 3B
  active parameters), `qwen3.8:27b-nvfp4` (18.2 GB, dense) and `muse-glimmer:30b-mlx` (19.1 GB);
  macOS 26.7 on an Apple M5 Max with 64 GB.
- **Method:** Scratch control planes on loopback ports other than 47800, each with its own data
  directory, HOME and XDG directories, driven over REST by a small client. A monitor listed the
  internet sockets of the runtime's processes and of Ollama's every 200 ms (`lsof -nP -i`), and
  Ollama's own log recorded every request it served. The pinned binary was also run directly,
  behind a proxy that records and refuses every request, to see what it tries to reach. Code paths
  were read from the strings of the pinned binary, which embeds OpenCode's JavaScript. Nothing was
  pulled or deleted, and no hosted model was called.
- **Status:** Runtime verified for OpenCode with all three models, for the scenarios below. Long
  runs, several executions at once, and a Mac under memory pressure from other work are not tested.

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

## Network

What left, or tried to leave, the Mac while OpenCode ran:

- **OpenCode's model catalog.** At every launch whose cached catalog is older than five minutes,
  OpenCode fetches `https://models.opencode.ai/api.json`, and again every five minutes while it
  runs. Behind the refusing proxy, a fresh launch tried `CONNECT models.opencode.ai:443`.
  `OPENCODE_DISABLE_MODELS_FETCH=true` stops it: no attempt in 70 s, and OpenCode falls back to
  the catalog built into the binary. The control plane passes it to OpenCode when it is named in
  `HALCYONIC_AGENT_ENV`, which the runs above did.
- **ripgrep.** When `rg` is neither on OpenCode's PATH nor in `$XDG_CACHE_HOME/opencode/bin`,
  OpenCode downloads ripgrep 15.1.0 from `github.com/BurntSushi/ripgrep/releases` the first time an
  agent searches files. Observed: the agent's first `glob` opened connections from the OpenCode
  process to 140.82.114.4 and 185.199.109.133 on port 443 (GitHub). There is no switch; a ripgrep on
  the PATH Halcyonic passes to OpenCode, for example Homebrew's, prevents it. This Mac has none.
- **Tools.** `webfetch` and `websearch` are allowed by default (above); the runs denied them.
- Nothing else. With the catalog fetch off and ripgrep in place, the monitor saw no socket beyond
  loopback from OpenCode's processes in any run, and none from Ollama's server once the model
  downloads had finished (during them, it held connections to Cloudflare addresses, which serve
  Ollama's registry).

## Speed and memory

Measured through Ollama's own API with thinking off: a short prompt with 256 tokens of output, and
a prompt of about 25,000 tokens (19,322 for Muse Glimmer's tokenizer) with a short answer. Memory is
what Ollama's MLX engine reports holding with the model loaded and at its peak.

| | `qwen3.6:35b-a3b-nvfp4` | `qwen3.8:27b-nvfp4` | `muse-glimmer:30b-mlx` |
| --- | --- | --- | --- |
| Load | 6.4 s | 6.2 s | 8.8 s |
| Output, short prompt | 30 to 59 tokens a second | 20 to 31 | 16 |
| Prompt processing, about 25,000 tokens | 910 tokens a second (28 s) | 175 (145 s) | 187 (103 s) |
| Output after that prompt | 30 tokens a second | 15 | 16 |
| Memory held, loaded | 22.1 GiB | 17.2 GiB | 17.8 GiB |
| Memory at the long prompt, peak and held after | 26.6 and 23.2 GiB | 24.5 and 20.1 GiB | 19.2 and 18.5 GiB |

## Consequences

- OpenCode runs local models through Halcyonic with no change to how the control plane is used:
  name the model as `ollama/<tag>`. For work that stays on the Mac, the person also needs, in
  OpenCode's configuration: a context limit for each local model, permissions that ask before
  shell commands and refuse `webfetch` and `websearch`, and ideally a default local model; and, in
  Halcyonic's environment, `OPENCODE_DISABLE_MODELS_FETCH=true` named in `HALCYONIC_AGENT_ENV`, and
  a ripgrep on the PATH. The runbook says how.
- `qwen3.6:35b-a3b-nvfp4` is the fast default: about three times the output speed and five times
  the prompt processing of the dense models. Long contexts are slow with every model, because the
  prefix cache rarely survives OpenCode's rewriting of earlier turns.
- A client should expect the first turn on a model to take tens of seconds.

## Not verified

- OpenCode compacting a local model's context before Ollama's memory runs out without a configured
  limit.
- Whether OpenCode's `providers.ollama.settings.baseURL` works in a later version.
- Long runs, several executions at once, and memory pressure with Unity or other large processes
  open.
- The overflow and removed-model runs with the two dense models.
