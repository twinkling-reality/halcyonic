# Claude Code capabilities

- **Question:** How can Halcyonic start, observe, instruct, approve and interrupt Claude Code work
  through documented surfaces, and what does it not control?
- **Date:** 2026-09-26.
- **Versions:** Claude Code CLI 2.1.263 (installed). `@anthropic-ai/claude-agent-sdk` 0.3.283
  (npm, governed by Anthropic's Commercial Terms).
- **Method:** Official documentation at code.claude.com, the npm registry, and `--help` output of
  the installed CLI. The facts marked *checked* below were read directly from the official pages
  listed. The rest come from a documentation review pass and must be confirmed when the adapter
  is built. During that pass, one headless run was made
  (`claude -p "echo test" --output-format stream-json --verbose --include-partial-messages`); it
  is the only runtime observation so far.
- **Status:** Partly documentation verified. Runtime smoke test not yet performed.

## Findings

Checked against https://code.claude.com/docs/en/agent-sdk/streaming-vs-single-mode,
`.../agent-sdk/permissions`, `.../agent-sdk/user-input` and `.../agent-sdk/overview`:

- **Streaming input mode is the control surface.** With `prompt` as an async iterable of user
  messages, the SDK supports "queued messages: send multiple messages that process sequentially,
  with ability to interrupt". Single message mode does not support "dynamic message queueing" or
  "real-time interruption".
- **Approvals** go through `canUseTool(toolName, input, {signal, suggestions})`, which returns
  `{behavior: "allow", updatedInput}` or `{behavior: "deny", message}`. "The callback can stay
  pending indefinitely", so an approval can wait for a person in XR. A `PreToolUse` hook returning
  `defer` lets the process exit and resume later from the persisted session. Clarifying questions
  (`AskUserQuestion`) arrive through the same callback.
- **Evaluation order:** hooks, deny rules, ask rules, permission mode, allow rules, then
  `canUseTool`. Tools approved earlier never reach the callback.
- **Permission modes:** `default`, `dontAsk`, `acceptEdits`, `bypassPermissions`, `plan`, `auto`.
  `setPermissionMode()` changes the mode during a streaming session.
- **Authentication:** "Unless previously approved, Anthropic does not allow third party developers
  to offer claude.ai login or rate limits for their products, including agents built on the Claude
  Agent SDK." Use API key authentication (or a cloud provider).
- **Branding:** products built on the SDK may call it "Claude Agent" or "{Name} Powered by Claude",
  and may not use "Claude Code" or "Claude Code Agent", or appear to be an Anthropic product.

From the review pass, not yet re-checked:

- Sessions persist under `~/.claude/projects/<encoded path>/<session id>.jsonl` in an internal,
  undocumented format. The SDK documents `listSessions`, `getSessionMessages` and
  `getSessionInfo`, and `resume`, `continue` and `forkSession` options.
- `-p --output-format stream-json` requires `--verbose`. Messages include `system/init`,
  `assistant`, `user`, `result` (with `session_id` and `total_cost_usd`), and `stream_event` with
  `--include-partial-messages`.
- Hooks include `PreToolUse`, `PermissionRequest`, `PostToolUse`, `Stop`, `SessionStart`,
  `SessionEnd`, `Notification` and more. A `PermissionRequest` hook can decide approvals in the
  CLI as well.
- Remote Control lets a phone or browser message a session only if the session was started with
  it. There is no documented way for an external program to inject instructions into a terminal
  session a person started.
- `claude agents`, `--bg`, `claude attach` and related background-agent commands appear in the CLI
  help but no documentation page for them was found. **Unverified** as a supported surface.

## Capability matrix

| Capability | Status | Surface |
| --- | --- | --- |
| start_execution | Supported | Agent SDK `query()` |
| instruct_at_rest | Supported | streaming input, or `resume` with a session id |
| instruct_while_running | Supported as a queue: processed after the current work, not steering | streaming input |
| respond_to_approval | Supported | `canUseTool` |
| interrupt | Supported | streaming input mode only |
| pause | Not supported | none documented |
| discover existing work | Supported for listing past sessions | `listSessions` (review pass) |
| attach to a terminal session a person started | Not supported | none documented |
| diff, terminal output | Not provided as such; derivable from tool events | message stream |

## Consequences for Halcyonic

- A Claude Code adapter hosts executions itself through the Agent SDK in streaming input mode, and
  keeps the session id as `native_id`. It declares `instruct_while_running` only after the smoke
  test shows how queued messages behave.
- It authenticates with an API key or cloud provider credentials that stay on the machine running
  the control plane, never a user's claude.ai login.
- Its display name must follow Anthropic's branding rules, for example "Claude Agent", not
  "Claude Code".
- Sessions a person starts in a terminal can be listed, but not steered. The XR client must not
  offer control actions for them.
- Salidium and Seorak observe these sessions through their own hooks and transcripts. Halcyonic
  must not install global hooks; it controls only what it hosts.

## Needs a runtime smoke test

The stream message types and fields; how queued input behaves during a running turn; interrupt
timing; `canUseTool` with a long pending wait; resume across processes; `listSessions` output.
