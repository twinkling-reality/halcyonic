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
  tested.

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
