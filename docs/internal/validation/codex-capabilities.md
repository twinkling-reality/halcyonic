# Codex capabilities

- **Question:** What can a Halcyonic adapter control and observe through Codex's developer
  surfaces, without assuming parity with Claude Code?
- **Date:** 2026-09-26.
- **Versions:** Installed CLI `codex-cli` 0.151.0. Current release 0.157.1 (npm `@openai/codex`,
  GitHub `rust-v0.157.1`, 2026-09-26); `@openai/codex-sdk` 0.157.1; Python `openai-codex` 0.157.1.
- **Method:** Official documentation (developers.openai.com/codex now redirects to
  learn.chatgpt.com/docs), source at tag `rust-v0.157.1`, release notes, local `--help`, and app-server
  schemas generated locally by the installed binary without contacting any service. No session was
  started. The app-server status and method names below were re-checked directly against
  https://learn.chatgpt.com/docs/app-server.
- **Status:** Documentation and source verified; runtime verified against 0.157.0 with a fake
  provider for both surfaces (2026-09-27, below). Real models, code mode and the daemon are not
  tested. The app-server adapter was built and verified end to end on 2026-09-27 (below).

## Findings

- **The capability split follows stability.** The stable surfaces, `codex exec` (JSONL with
  `--json`) and the TypeScript SDK (which spawns `codex exec` per turn), force the approval policy
  to `never` in headless mode and reject every approval request. They emit item-level events only:
  no deltas, and file changes without diffs.
- **`codex app-server`** (JSON-RPC over stdio, WebSocket or Unix socket) is the only surface with
  approvals, mid-turn steering, interrupt, deltas and per-file diffs. The official page states:
  "The app-server command and WebSocket transport are experimental and aren't supported for
  production workloads."
  - Approvals arrive as server requests (`item/commandExecution/requestApproval`,
    `item/fileChange/requestApproval`, and others), answered with a decision, followed by
    `serverRequest/resolved`.
  - Interrupt: `turn/interrupt`, after which the turn completes with status `interrupted`.
  - Steering: `turn/steer`.
  - Discovery: `thread/list`, `thread/read` and `thread/loaded/list`.
  - While waiting for approval, a thread is `active` with `waitingOnApproval`.
- **Event model.** Thread, turn (`completed`, `interrupted`, `failed`) and item events. Item types
  include agent messages, reasoning, command executions, file changes, MCP tool calls, web
  searches and plans.
- **Goals.** `thread/goal/*` with status `active`, `paused`, `blocked` and others. A goal pause
  acts between turns; it is not a pause of a running turn.
- **Removals and churn.** `codex mcp-server` was removed in 0.154.0 (the installed 0.151.0 still
  has it). There were six minor releases in September 2026, and the app-server's `initialize`
  response carries no protocol version.
- **Identity.** One thread UUID across surfaces. Transcripts live in `~/.codex/sessions` in
  internal, versioned formats that must not be parsed.
- **Authentication.** API keys are the recommended default for automation. No primary source
  states whether a third-party product may drive Codex with an end user's ChatGPT sign-in.

## Capability matrix

| Capability | exec / TS SDK (stable) | app-server (experimental) |
| --- | --- | --- |
| start_execution | Supported | Supported |
| instruct_at_rest | Supported: `exec resume`, `resumeThread` | Supported |
| instruct_while_running | Not supported | Supported: `turn/steer` |
| respond_to_approval | Not supported: approvals are rejected | Supported |
| interrupt | Partial: SIGINT; the SDK kills the process | Supported: `turn/interrupt` |
| pause | Not supported | Not supported (goal pause is between turns) |
| discover existing work | Not supported | Supported |
| diff | Partial: paths only | Supported |
| terminal output | Partial: final output | Supported: output deltas |
| review | `codex exec review --json` | `review/start` |

## Consequences for Halcyonic

- A Codex adapter must choose between depth (the experimental app-server, pinned to an exact CLI
  version with client types generated from that binary's schema) and stability (`exec` without
  approvals). This is an open question, not a default.
- An `exec`-based adapter would honestly declare `respond_to_approval: false` and
  `instruct_while_running: false`. The capability model already represents that.
- The adapter must leave `CODEX_HOME` at the developer's default and never parse rollout files.
  Salidium and Seorak both observe Codex by reading the rollouts under the default `CODEX_HOME`, so
  an isolated one would hide Halcyonic's Codex sessions from both (confirmed with both projects
  on 2026-09-26). Concurrent use of the shared state is the app-server's concern, which is one more
  reason to prefer it over separate `exec` processes. (Corrected 2026-09-26: an earlier version of
  this record recommended an isolated `CODEX_HOME`.)

## Runtime check: thread identity (2026-09-26)

Method: the installed `codex-cli` 0.151.0 with an empty, isolated `CODEX_HOME`, inside a
no-network sandbox with no credentials; no turn could reach a model.

- **`codex exec --json`: verified.** `thread.started.thread_id` equals the uuid in the rollout
  file name and `session_meta.payload.id` (which equals `payload.session_id`). `originator` is
  `codex_exec`, `source` is `exec`.
- **App-server: not directly verified.** `thread/start` needs no authentication and returns a
  UUIDv7 `thread.id` equal to `thread.sessionId`, with a `thread.path` naming
  `rollout-...-<thread.id>.jsonl`. The rollout is written only at the first turn, so a thread that
  never runs a turn leaves nothing for Salidium or Seorak to see.
- `thread/start` alone tried to connect to `wss://api.openai.com/v1/responses`, so it is not
  network-silent even without a turn.
- App-server threads report `source: "vscode"` even with a custom `clientInfo.name`, which
  appeared only in the user agent.
- Every `codex` invocation writes to `CODEX_HOME`, including `--help`.
- `codex exec` does not fail fast on missing credentials; it retries the network indefinitely.

## Needs a runtime smoke test

App-server thread id against its rollout after a first turn; SIGINT behavior on `exec --json`; an
app-server approval round trip with only stable features enabled; `turn/steer` ordering during a
running command; resuming an exec thread through app-server; attaching to the shared local daemon.

## Runtime smoke test (2026-09-27)

Method: `@openai/codex` 0.157.0 (0.157.1 was younger than the one-day minimum release age when
chosen), darwin arm64 binary sha256 `ad0be20d…3714`, installed without install scripts and run
directly under `env -i` with temporary HOME, CODEX_HOME, XDG and TMPDIR, a macOS sandbox profile that
allowed only loopback and denied the real home, a logging proxy with an empty allowlist, and a
socket monitor. A fake provider on loopback spoke the Responses API (0.157.0 accepts no other wire
API) and scripted text, shell calls, patches, slow streams and errors, using the `gpt-5.5` model
metadata because the newest bundled models call tools from JavaScript ("code mode"), which the fake
cannot script. The raw evidence is kept outside the repository.

App-server over stdio, stable API surface only (no `experimentalApi` opt-in):

- **Identity.** `thread/start` returns a UUIDv7 thread id equal to the session id; the rollout file
  appears only after the first turn and carries the same id. `source` is `vscode`; the client's
  `clientInfo.name` becomes the thread's `originator` (also sent to the provider), and a
  client-supplied `threadSource` is persisted.
- **Approvals.** A command needing escalation sets the thread `active` with `waitingOnApproval` and
  sends `item/commandExecution/requestApproval`; `accept` runs it, `decline` refuses it (the model is
  told and the turn completes), `cancel` interrupts the turn. `availableDecisions` omitted `decline`,
  which still worked. File change approvals worked.
- **Interrupt** ends the turn as `interrupted` within milliseconds and closes the model stream, but a
  running command keeps running and streaming output until the server exits; a pending approval is
  resolved without running. An interrupt for a turn that already finished gets no reply until a later
  interrupt.
- **Steer.** `turn/steer` returns at once and is delivered at the next model request, never cutting
  the current stream or command. `turn/start` on a busy thread silently acts as a steer.
- **Errors** end the turn `failed` with an `error` notification; an HTTP 500's provider message is
  replaced by a generic one.
- **Restart.** After SIGKILL or SIGTERM mid-turn, the turn reads `interrupted` after a restart and
  `thread/resume` continues the thread with its history. SIGTERM sends no `turn/completed` first. A
  command writing only to a file survived SIGKILL as an orphan.
- **Shared CODEX_HOME.** A thread loaded by one Codex process cannot be resumed by another ("already
  has an active writer") until that process exits.
- **Egress.** With a custom provider, no model traffic left the machine, but every start tried
  plugin requests to chatgpt.com and GitHub; with the default provider, `thread/start` opened a
  connection to OpenAI. Every provider request carries the installation id, the workspace path, the
  latest commit hash and the sandbox mode.

`exec --json`:

- Forces `approval_policy = never`: commands either run or are refused with a message to the model,
  and no configuration override changes it.
- SIGINT aborts the turn and kills a running command, but the stream ends without a final event
  (exit code 1). Output arrives only at item completion; file changes carry no diff.
- In 0.157.0, exec is itself built on an in-process app-server client.

## Capability matrix (runtime, 0.157.0)

| Capability | app-server, stable surface | exec --json |
| --- | --- | --- |
| start_execution | Observed | Observed |
| instruct_at_rest | Observed, also after a restart via `thread/resume` | Observed (`exec resume`) |
| instruct_while_running | Observed (`turn/steer`, at the next model request) | Not supported |
| respond_to_approval | Observed (accept, decline, cancel) | Not supported (forced `never`) |
| interrupt | Observed; running commands survive it | SIGINT, no final event |
| pause | Not supported | Not supported |
| discovery | Observed (`thread/list`, `thread/read`) | Not supported |
| diff | Observed (per file and unified) | Paths only |
| terminal output | Observed (deltas) | At completion only |

Decision: [ADR 0011](../decisions/0011-codex-app-server-stable-surface.md).

## Adapter build (2026-09-27)

Method: building `packages/integrations/codex` against the same pinned binary (sha256
`ad0be20d…3714`), reading Codex's source at `rust-v0.157.0`, and running the adapter's end to end
tests with temporary HOME, CODEX_HOME, XDG and TMPDIR, a TypeScript port of the fake provider
configured as a custom model provider, a proxy that refuses and records every connection, and a
monitor of every socket the binary's processes hold. The switches below were first tried without
credentials inside a macOS sandbox profile that allowed only loopback. The suite passed five
consecutive runs, and a manual check ran an approval round trip over REST on a control plane
hosting the runtime, in temporary directories.

Findings beyond, or differing from, the smoke test:

- **Commands run in sessions of their own.** Codex starts every command with `setsid`
  (`codex-rs/utils/pty/src/process_group.rs`, `pipe.rs`, `pty.rs`), so a signal to the server's
  process group never reaches a command. What stops them is Codex's own shutdown, which the end of
  its input and SIGTERM both start (`codex-rs/app-server-transport/src/transport/stdio.rs`, bounded
  at 45 s). Verified: with its host killed by SIGKILL while a command ran, the server read the end
  of its input, exited within about 0.4 s, and its command stopped. The adapter stops a server by
  ending its input, then SIGTERM, then SIGKILL for its process group and every descendant, which it
  finds through their parents.
- **Restart.** After the server is killed mid-turn, the adapter relaunches it, resumes the thread
  with `thread/resume` and reads the latest turn with `thread/turns/list`: the killed turn reads
  `interrupted`, reported as inferred (rule `codex.restart.turn_status`), and the thread takes a
  new turn at rest. Verified end to end.
- **Sandboxed commands** under `workspace-write` produce the usual item events; the smoke test
  missed them because its own sandbox prevented Codex's. A patch inside the writable roots is
  applied without asking; one outside them asks with `item/fileChange/requestApproval`, whose
  parameters name no files, so the adapter summarizes the request from the item's changes.
- **Approval routing.** `thread/start` accepts `approvalsReviewer`, and `auto_review` would send
  approvals to a reviewer agent. The adapter always passes `user` and refuses a thread whose
  reported approval policy, reviewer or sandbox differ from what it asked for.
- **Policies.** Under `on-request` with `danger-full-access`, Codex runs every command it does not
  flag as dangerous without asking (`codex-rs/core/src/exec_policy.rs`); `untrusted` asks before
  every command not known to be safe; `never` never asks. The adapter refuses `never`, the granular
  policies, and `danger-full-access` with `on-request`.
- **Server requests.** Codex takes an error answer to one of its requests as a denial, an empty
  permission grant, a declined elicitation or an empty answer to a question
  (`codex-rs/app-server/src/bespoke_event_handling.rs`). The adapter shows the person command and
  file change approvals and refuses every other request that way.
- **Interrupts** that find no running turn are held until the next turn ends, completed or aborted,
  and answered then (`codex-rs/app-server/src/request_processors/turn_processor.rs`), which
  explains the unanswered interrupt of the smoke test.
- **Decline messages** cannot be delivered: the approval response has no field for one, and the
  model sees "rejected by user".
- **Provider metadata.** The turn metadata lists a workspace only when it is a git repository; the
  working directory always reaches the provider in the environment context.
- **Switches**, verified at the source and by attributing every connection attempt:
  `features.plugins = false` removes the startup requests to chatgpt.com, github.com and
  api.github.com; `analytics.enabled = true` adds metrics to ab.chatgpt.com
  (`codex-rs/core/src/otel_init.rs`), and `analytics.enabled = false` also turns off the analytics
  events client, which otherwise runs (`codex-rs/core/src/session/session.rs`);
  `CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED=1`, which the adapter always sets, disables
  remote control (`codex-rs/cli/src/main.rs`; an internal switch, verifiable only in the source).
  With all three, no test attempted a connection beyond loopback.

What a hard kill leaves running:

- The control plane killed: the server's input ends, Codex stops its commands and exits, and the
  next start clears the record left behind. Verified.
- The control plane and the watchdog killed while the server could not act (stopped with SIGSTOP,
  as a hung server would be): the next start stops the server with SIGTERM, then SIGKILL for its
  group and descendants, commands included. Verified.
- The server itself killed with SIGKILL by anything else: its running commands survive as orphans,
  re-parented to launchd, which Halcyonic cannot find afterwards (a command writing only to a file
  ran on in the smoke test). The adapter relaunches the server and reports the turn interrupted,
  never the command stopped.
- On Linux, Codex asks the kernel to send each command SIGTERM when its parent dies
  (`PR_SET_PDEATHSIG`), so such orphans may not survive there. Not tested.

Not verified:

- A real model or provider, a ChatGPT sign-in or an API key, and what the analytics events client
  sends with a real sign-in.
- Linux.
- Refusing permission grants, questions and MCP elicitations with the real binary (a stand-in
  binary and the source only).
- The approval summaries for input to a running command and for network access (constructed
  requests only), and two approvals for one item.
- A decision sent as its turn ends; the adapter reports that race with an unknown effect.
- The npm launcher script used as the configured binary.
- That Salidium and Seorak show a thread Halcyonic started.
- A connection that ignores the proxy variables and lasts less than the socket monitor's 200 ms
  sample (the smoke test's sandbox saw none).

## Large process tables (2026-09-29)

Question: do the adapter and its end to end suite still find processes on a machine running many
processes with long command lines?

Found: the end to end suite failed one test on a Mac running several Unity IL2CPP builds, with "the
socket monitor failed: ps failed: stdout maxBuffer length exceeded". Listing every process with its
command line (`ps -ww -A -o ...,args=`) printed more than 1 MiB, Node's default limit on the output
of `execFile` and `execFileSync`, so the call failed. The adapter's `readDescendants` listed
processes the same way. On such a machine, the watchdog and the next start left a recorded server
that ignored SIGTERM running, because `stopRecordedProcess` failed before its SIGKILL, and at
startup that failure stopped the control plane from starting; closing the adapter killed such a
server's process group but not its commands.

Measured on macOS 26.7 (arm64), where `kern.argmax` is 1 MiB: 32 processes holding 64 KiB of
arguments each made that listing print 2.3 MiB, while `ps -A -o pid=,ppid=` printed 12 KiB for
about 1,050 processes. Under `LC_ALL=C`, macOS `ps` prints a byte beyond ASCII as three or four
characters (see the Claude Code record), so one process with 448 KiB of such arguments printed about
1.3 MiB.

What changed:

- `readDescendants` lists every process with its parent only, then reads pid, parent, group, start
  time and command line for the processes below the server alone (`ps -p <pids>`), and keeps those
  that this second read still shows below the server, so a pid reused between the two reads is never
  taken for a descendant.
- The `ps` reads of the Codex, OpenCode and Claude Code process records accept up to 64 MiB of
  output: one command line can print more than 1 MiB, and a recorded pid can belong to any process
  by the time it is read.
- The end to end suite's socket monitor finds the binary's processes with `pgrep -f`, which matches
  command lines without printing them, and follows their descendants through `ps -A -o pid=,ppid=`.
  The suite's check for a running command, and the Claude Code test's check that a killed CLI is
  gone, use `pgrep -f` too.

Verified: regression tests that start processes holding 2 MiB of command lines, and one whose line
prints 1.3 MiB (macOS only), failed on the same limit before the change and pass after it, for
`readDescendants` and for the identity reads of all three adapters. With 2 MiB of such command lines
running, the end to end test of an interrupt with a running command failed before the change (its
process check hit the same limit), and passed after it, as did the test that stops a hung server
and its command at the next start. Without them, the Codex end to end suite passed three
consecutive runs and the OpenCode suite one.

Left unchanged, since they read little output: `codex --version`, `pgrep -P` in the end to end
tests, and `ps -p <pid>` in tests, for processes they started with short command lines.

## Local models (2026-09-29)

The pinned binary ran threads on open models that Ollama serves on the Mac, through its built-in
`ollama` provider, driven by the adapter through a control plane; the runs are in
[local-models.md](local-models.md).

- `thread/start` and `thread/resume` accept `modelProvider` and a `config` object of overrides for
  the one thread; `model_context_window` and `model_auto_compact_token_limit` there take effect.
  Both answers report the `model` and `modelProvider` the thread got.
- Codex accepts any model name, giving one outside its catalog fallback metadata (272,000 tokens).
- `model/list` returns the catalog built into the binary, OpenAI's models, whatever provider is
  configured; `config/read` names the configured provider and model but lists only the providers
  the configuration defines, not the built-in ones.
- The rollout's `session_meta` records `model_provider` (no model), and each `turn_context` the
  model, an Ollama tag verbatim.
- The adapter now takes `model_provider`, `context_window` and `auto_compact_token_limit`, and
  refuses a thread for which Codex reports another model or provider than asked.
- App-server made no request beyond loopback, and never asked Ollama to pull a model.
