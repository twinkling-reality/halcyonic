# Meta's models

- **Question:** What do Meta's hosted models (Meta Model API), its coding agent (Muse Code) and its
  open-weight model (Muse Glimmer) offer Halcyonic, as Meta's own pages describe them, and what would
  each need under Halcyonic's rules?
- **Date:** 2026-10-29; Meta's pages fetched between 23:37 and 23:41 UTC.
- **Status:** Documentation read, nothing run. No account, no key, no request to Meta's API, nothing
  installed. Two things were read on this Mac: the strings of the pinned OpenCode 2.0.18 binary
  (darwin arm64, sha256 `6759c7f8…`), as [local-models.md](local-models.md) did, and Muse Glimmer's
  figures, which come from runs recorded there on 2026-09-29 and 2026-09-30.
- **Method:** Meta's public pages at dev.meta.ai/docs (the Model API, protocols, coding agents, Muse
  Code and Muse Glimmer), the Muse Code developer docs Meta links to
  (meta-models.github.io/muse-code-sdk, version 1.4), the Muse Wonder page and model card
  (research.meta.ai), the Model API Terms of Service and the Zero Data Retention help page, read as
  saved text; the Hugging Face API's record of `meta-models/Muse-Glimmer-30B`.

In what follows, **Meta states** marks what a page says as of the date, which no run here has checked;
**claimed** marks Meta's own judgement of quality or performance; **not found** means the pages read
do not say; **inference** is ours.

## Meta Model API

Meta states:

- **Address and key.** Base URL `https://api.meta.ai/v1`. Every request carries a Bearer key in
  `Authorization`. Keys are made in the Model API dashboard, shown once, and start with `LLM_`. The
  docs keep the key in `MODEL_API_KEY` by convention and note that no SDK reads it by itself. Keys of
  one team share its rate limits.
- **Three surfaces.** The Responses API (`POST /v1/responses`), with server-managed conversation
  state and video input. Chat Completions (`/v1/chat/completions`), which Meta calls the surface every
  OpenAI-compatible agent supports, with tool calling and streaming. The Messages API
  (`/v1/messages`) in Anthropic's wire format, with tool calling and streaming, stateless, reached at
  the host `https://api.meta.ai` with the client adding the path. Parallel tool calls and streamed
  tool-call arguments are listed for Muse Spark.
- **Models.**

  | Model | Id | Notes |
  | --- | --- | --- |
  | Muse Wonder | `muse-wonder-1.0` | Preview; released 2026-10-12 (model card); 1,048,576-token context; reasoning effort from `minimal` to `max`; Standard mode only on the API; not offered in South Korea |
  | Muse Wonder Full | none on the API | Muse Code subscriptions only (Power Usage and Maximum Usage plans) |
  | Muse Spark | `muse-spark-1.3`, `muse-spark-1.2`, `muse-spark-1.1` | 1,048,576-token context; 1.3 recommended; audio input on 1.3 not fully supported |
  | Muse Spark, Contributor mode | `muse-spark-1.3-contributor`, `muse-spark-1.2-contributor` | The same models in exchange for training on the content |
  | Muse Image | `muse-image-1.0` | Generation and editing; `/v1/images/generations`, `/v1/images/edits`, or Responses |
  | Muse Voice Transcribe | `muse-voice-transcribe-1.0` | Speech to text only; `wss://api.meta.ai/v1/asr/realtime` or `POST /v1/asr/transcribe`; turn-level timestamps, not word-level |
  | Segment Anything | `sam-3.1` | Boxes and masks from a text prompt, image or video, over Responses |

- **Prices,** pay as you go, per million tokens unless stated:

  | | Cached input | Input | Output |
  | --- | --- | --- | --- |
  | `muse-wonder-1.0` | $0.20 | $4.00 | $20.00 |
  | `muse-spark-1.3`, `1.2`, `1.1` | $0.15 | $1.25 | $4.25 |
  | Contributor mode Muse Spark | $0.002 | $0.10 | $0.20 |

  Web search grounding costs $2.50 per 1,000 searches on top of tokens. Muse Image is $0.01 an image,
  Muse Voice Transcribe $0.18 an hour of audio, SAM 3.1 $2.50 per 1,000 images or $0.20 per 1,000
  video frames (at least 50 frames a request). There is no long-context premium.
- **Rate limits,** per team and per model: 3,000 requests and 4,000,000 tokens a minute for
  `muse-wonder-1.0` and each Standard Muse Spark; 150 requests and 3,000,000 tokens for the
  Contributor ids; 150 requests a minute for Muse Image; 256 concurrent and 16,000 hourly streams for
  Muse Voice Transcribe; 600 background responses a minute. Over a limit the API answers 429, with
  `Retry-After` for background submissions, and token responses carry `x-ratelimit-*` headers.
- **Meta adds text of its own.** "Meta injects a small amount of steering context into every prompt (a
  system prompt and related scaffolding)", not billed and not counted in usage.
- **What happens to the content.** In Standard mode prompts, code, files and responses are not used to
  train Meta's models; the Terms of Service (section 4) add that Meta retains content and usage data as
  needed to provide the service, to comply with law, to exercise its rights under the terms, and to
  detect and respond to violations of its Acceptable Use Policy. Contributor mode lets Meta use the
  content to train, develop, evaluate and improve its models (section 6), after steps to separate it
  from the account. Zero Data Retention exists for qualified organizations only, enabled by Meta on
  request, never from the web interface, and not for a personal account; with it on, stored responses,
  background requests, file uploads, web search and Contributor mode are refused, and inputs and
  outputs a request flagged for a policy violation may still be kept up to two years.
- **Where.** Availability varies by country under Meta's Geographic Use Policy.

Claimed: the Muse Wonder page and model card rank Muse Wonder and Muse Wonder Full against other
frontier models on Meta's chosen benchmarks. The model card lists its own limits, among them errors in
multi-step reasoning and that "visual and spatial reasoning, especially 3D tasks, remains a known
limitation", and recommends human confirmation before irreversible actions in agentic use.

Not stated in words: whether the Responses API keeps a response unless told `store: false`. Its example
answer to a request that does not set `store` shows `"store": true`, and `store: false` is the way the
docs give for the server to keep no copy. Not found: how long stored responses and uploaded files are
kept in Standard mode.

## Naming a Meta model in Halcyonic's runtimes

Meta's coding-agents guide gives a configuration for each of the three runtimes Halcyonic drives:

- **OpenCode:** a provider `meta` with the npm adapter `@ai-sdk/openai` (the Responses API), base URL
  `https://api.meta.ai/v1`, the model `muse-spark-1.3` with a 1,048,576 context limit and a 131,072
  output limit, and `include: ["reasoning.encrypted_content"]`, which Meta says carries Muse Spark's
  reasoning across turns; `@ai-sdk/openai-compatible` (Chat Completions) as a fallback that loses it.
- **Codex:** `model_providers.meta` with `base_url = "https://api.meta.ai/v1"`, `env_key` naming the
  key's variable and `wire_api = "responses"`, the model `muse-wonder-1.0` or `muse-spark-1.3`.
- **Claude Code:** `ANTHROPIC_BASE_URL=https://api.meta.ai`, the key as `ANTHROPIC_AUTH_TOKEN` (a
  Bearer key, not `ANTHROPIC_API_KEY`'s `x-api-key`), and every model alias set to a Meta model.

What the pinned OpenCode 2.0.18 already holds, from its strings (inference, nothing run): a built-in
provider `meta` with four endpoints on `https://api.meta.ai/v1`: `meta-responses` (`/responses`,
whose defaults send `store: false` and ask for `reasoning.encrypted_content`), `meta-chat`
(`/chat/completions`), `meta-messages` (`/messages`) and `meta-images`. It takes its key from
`META_API_KEY`, the variable Muse Code reads, not the `MODEL_API_KEY` Meta's API docs name. No `muse-`
model id appears in the binary, and Halcyonic's adapter turns OpenCode's catalog fetches off
([local-models.md](local-models.md)), so a Meta model would have to be named in OpenCode's
configuration. Its table of npm adapters maps `@ai-sdk/openai` to a module
bundled in the binary (`@opencode/ai/providers/openai`), so Meta's configuration should need no
package download; not run.

Halcyonic today keeps Codex on models served on this Mac only, from a home of its own, never signed
in, with web search off ([INTEGRATIONS.md](../architecture/INTEGRATIONS.md)), and OpenCode's local
setup from `pnpm mac-setup local-model` refuses `webfetch` and `websearch` and names Ollama models
([local-models.md](local-models.md)). A Meta model would be the first hosted model for either; the
adapters would list it as served `remote`, since the address it is sent to is Meta's
([INTEGRATIONS.md](../architecture/INTEGRATIONS.md), rule 11). What the rules would need is a
decision, recorded in [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md) and not made here:

- whether a hosted model may run Codex or OpenCode work at all, and for which projects, since a
  project's files and commands' output then go to Meta;
- where the key lives and who may set it (the Mac's settings file only its owner writes,
  [ADR 0024](../decisions/0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md)), and
  that it never reaches the journal, a client or a log;
- Standard mode only: refuse the `-contributor` ids, which send the content to training, and a Muse
  Code subscription's Contributor mode, on by default;
- that responses are not kept on Meta's side (`store: false`), since Zero Data Retention is not
  available to a person's own account;
- that the money is real: a turn has a price the person should see before and after;
- that Meta's own steering text is part of every prompt, so the instructions a model followed are never
  only Halcyonic's.

## Muse Code

Meta states, for Muse Code 1.4 (the changelog's latest release on the date is 1.4.13):

- **What it is.** Meta's coding agent for the terminal and CI, built on Muse Spark, installed by
  `curl -fsSL https://dev.meta.ai/install.sh | sh`; the default model is `muse-spark-1.2`. It signs in
  with a browser or a key (`META_API_KEY`, then a stored key, then a browser session).
- **Headless.** `muse exec "<prompt>"` runs one prompt to the end; `muse exec --json` writes JSONL
  events to stdout. The exit code says how the run ended, not whether the work is right: 0 when the
  turn completes, 1 when it fails or is cancelled, 2 for a usage error, 130 or 143 on a signal.
  `muse exec --session-id <uuid>` continues a session, `muse export --session <uuid>` writes it out,
  and `--no-session-log` keeps nothing (and so no resume).
- **A structured surface.** `muse serve` runs the Muse Session Protocol (MSP): JSON-RPC 2.0 over stdio,
  "the only public protocol surface"; `muse schema` prints its schema, and the server reports a schema
  fingerprint when a client starts. There are SDKs for TypeScript (`@muse-code/sdk`, no runtime
  dependencies) and Python (`muse-code-sdk`), a Rust SDK page in the reference, a recorded
  corpus of real NDJSON exchanges to test a client against, and an experimental `muse acp` for editors that speak the Agent Client Protocol.
  The methods include `session/start`, `session/resume`, `turn/start`, `turn/steer`, `turn/interrupt`,
  `turn/cancel`, `approval/decide`, `approval/listPending`, `userInput/answer`, `model/list` and
  `session/setApprovalMode`, and the server streams `turn/started`, `turn/completed`,
  `item/started`, `item/delta`, `item/completed`, `approval/requested`, `approval/reviewing`,
  `approval/resolved` and `session/tokenUsage`.
- **Its model of the work.** "The log is the session": a client's state is a fold of a durable event
  log. "An ack is not an outcome": a command's answer says only what was admitted. "Requests carry no
  authority": an approval request is shown, and the decision is a separate idempotent command naming a
  choice from the server's own menu. Approvals do not expire in this version; a pending one parks its
  turn, even across restarts. A client may switch among the approval modes the person's configuration
  defines (`allowAll`, `promptUnmatched`, `onRequest`, `denyUnmatched`) but never write a policy.
- **Stability, two pages disagreeing.** The Muse Code overview calls MSP "a stable, versioned session
  protocol"; the protocol guide says it "is pre-1.0 and may change non-additively until v1 is
  declared", with no protocol changelog yet, the fingerprint being the way to notice a change.
- **Approvals and the sandbox.** A session starts in the Auto-review profile, where "an automated
  reviewer" (the approval judge) decides eligible approvals and asks the person only when it is
  unavailable; Ask me sends every one to the person (`--approval-judge off`), and Unrestricted
  (`--yolo`) drops approval and the sandbox. Compound shell commands are reviewed stage by stage. Every
  shell command runs in an OS sandbox, Seatbelt on macOS, writable only in the workspace and a temp
  folder, with the workspace's `.git`, `.muse` and `.agents` read-only, and Muse Code refuses to run a
  command when it cannot confirm the sandbox. Network goes through a proxy that asks per destination by
  default; `restricted` allows none.
- **Not everywhere.** Workflows are missing from the public Apple silicon macOS build.

Inference: MSP is a structured surface an adapter could drive without reading a terminal, close in
kind to Codex's app-server, which Halcyonic already drives
([ADR 0011](../decisions/0011-codex-app-server-stable-surface.md)): sessions, turns, steering,
interrupts, approvals with idempotent decisions, questions and resume, each with a typed event. For
Halcyonic it would need at least: the approval judge off, so that the person decides (Halcyonic's rule,
and `approval/reviewing` shows when a judge is at work); Contributor mode off; a pinned version with its
schema fingerprint checked at start, since the protocol may still change; and a decision on the hosted
model behind it, as above. Not found: whether Muse Code can run on a model served on this Mac, which
would keep work local.

## Muse Glimmer

Meta states: a 30-billion-parameter dense multimodal model (text and image in, text out), distilled
from Muse Spark, built for local agentic work; a 128K-token context by default, longer supported; more
than 100 languages; weights under the Apache License 2.0, with "no API key and no gated access". The
Hugging Face API agrees on 2026-10-29: `meta-models/Muse-Glimmer-30B` has the license `apache-2.0`,
is not gated, was created on 2026-08-09 and last changed on 2026-10-22. The published builds: bf16
safetensors (about 60 GB), GGUF Q4_K_M (about 17 GB) and Q4_K_XL (about 20 GB) with a 1.4 GB vision
projector and a 1.6 GB draft model for speculative decoding, and ExecuTorch exports. Ollama is among
the launch partners.

What Halcyonic measured, from [local-models.md](local-models.md) (2026-09-29, the model choice
2026-09-30; Ollama 0.34.4, MLX engine, `muse-glimmer:30b-mlx`, 19.1 GB, on an M5 Max with 64 GB):
OpenCode and Codex both completed every flow through the control plane (a first turn with an approval
in 62.1 s and 60.0 s); under Codex it met a denial by asking for another command, twelve times, before
completing in 155 s; it loaded in 4.9 and 8.8 s, wrote 21 to 26 tokens a second, read a prompt of
about 25,000 tokens at 187 tokens a second, held 17.8 GiB loaded and peaked at 19.2 GiB.
`qwen3.6:35b-a3b-nvfp4` became the default for about twice the output speed and five times the prompt
processing.

## What this does not show

Nothing here was run against Meta. Whether the API, its tool calls and streaming behave as written,
what OpenCode's built-in provider actually sends, and what Muse Code's protocol carries are all
unchecked. Prices, limits, models and terms are as published on the date, and the benchmark results
are Meta's.
