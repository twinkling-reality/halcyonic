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
- **Status:** Documentation and source verified. Runtime smoke test not yet performed.

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

## Needs a runtime smoke test

Whether the `exec` thread id matches the rollout file; SIGINT behavior on `exec --json`; an
app-server approval round trip with only stable features enabled; `turn/steer` ordering during a
running command; resuming an exec thread through app-server; attaching to the shared local daemon.
