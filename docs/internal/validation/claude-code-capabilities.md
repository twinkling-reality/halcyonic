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
- **Status:** Partly documentation verified. SDK argument and environment handling runtime
  verified (below); no real Claude Code session has been run for Halcyonic yet.

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

## Runtime check: SDK arguments and environment (2026-09-26)

Method: `@anthropic-ai/claude-agent-sdk` 0.3.283 (bundling CLI 2.1.283) pointed through
`pathToClaudeCodeExecutable` at a fake executable that logged its arguments and environment variable
names, inside a no-network sandbox with no credentials. No model was contacted.

- **Choosing the session id works.** The SDK's own `sessionId` option is sent as
  `--session-id=<uuid>`; `extraArgs: {'session-id': <uuid>}` is sent as `--session-id <uuid>`.
  Setting both sends the flag twice with no warning. Use `sessionId`.
- **An explicit `env` replaces the whole environment**; the SDK adds only
  `CLAUDE_CODE_ENTRYPOINT` and `CLAUDE_AGENT_SDK_VERSION`. Without `env`, the host environment is
  inherited.
- **Settings load from every source by default**: no `--setting-sources` flag is sent unless
  `settingSources` is set, so the user's hooks (including Salidium's and Seorak's) run for SDK
  sessions.
- **Default arguments:** `--output-format stream-json --verbose --input-format stream-json
  --permission-mode default`.
- **The SDK validates nothing before spawning.** An invalid session id passes through, and a child
  that exits 0 without output ends `query()` with no error. An adapter must treat a query that ends
  without a result message as a failure of unknown effect.
- The SDK sets `NoDefaultCurrentDirectoryInExePath=1` in the host process when it loads.

Not yet verified: whether the real CLI honors `--session-id`.

## Needs a runtime smoke test

Whether the real CLI honors `--session-id`; the stream message types and fields; how queued input
behaves during a running turn; interrupt timing; `canUseTool` with a long pending wait; resume
across processes; `listSessions` output.

## Adapter build: SDK surface and runtime checks without a model (2026-09-26)

Method: `@anthropic-ai/claude-agent-sdk` 0.3.283 (bundling CLI 2.1.283, darwin-arm64) installed with
pnpm 11.27.1 for `packages/integrations/claude-code`. Sources, marked on each finding: the pinned
package's published types (`sdk.d.ts`, `sdk-tools.d.ts`) and bundled source (`sdk.mjs`); a static
search of the JavaScript embedded in the pinned CLI binary (*static*, the binary was not run for
this); and runtime checks that contact no model (*runtime*). The runtime checks are the package's
process tests, which drive the real SDK against a fake executable (`src/testing/fake-claude.mjs`),
and two runs of the real CLI under a Seatbelt profile that denied all network except loopback, all
writes under the real home, reads of `~/.claude`, `~/.claude.json` and the keychains, and running
`/usr/bin/security`. HOME and CLAUDE_CONFIG_DIR were temporary directories, the API key a dummy, and
`ANTHROPIC_BASE_URL` pointed at a fake API on loopback that either held every request open or
answered with scripted `tool_use` responses. The official documentation was not re-read in this
pass; the cloud providers covered (Amazon Bedrock, Claude Platform on AWS, Google Vertex AI,
Microsoft Foundry) are those whose endpoint variables the third-party integrations page lists, as
recorded by the documentation pass.

- **Install.** pnpm 11 defaults `minimumReleaseAge` to one day. 0.3.283 was published
  2026-09-25T18:49Z, more than a day before the install, so nothing was blocked and the workspace
  configuration is unchanged. The CLI binary (225 MB) arrives as an optional platform dependency.
  The SDK's peers (`@anthropic-ai/sdk`, `@modelcontextprotocol/sdk`, `zod`) are installed
  automatically; the adapter needs them only for types.
- **The real CLI honors `--session-id`** (*runtime*). In both runs `system/init.session_id` equaled
  the id the adapter chose, and the transcript was written as
  `projects/<encoded cwd>/<session id>.jsonl` under CLAUDE_CONFIG_DIR.
- **API key authentication is what the session uses** (*runtime*, *static*).
  `system/init.apiKeySource` was `ANTHROPIC_API_KEY`. The CLI source turns claude.ai OAuth off when
  ANTHROPIC_API_KEY or an apiKeyHelper is the credential of a local non-interactive session, and
  whenever a cloud provider is selected. It reads its boolean variables as true for `1`, `true`,
  `yes` and `on`, and reads the macOS keychain through `/usr/bin/security`.
- **Turns** (*runtime*). `system/init` is emitted at the start of every turn, about 2.4 s after
  launch for the first. Each turn ends with one `result`: `success` with terminal reason
  `completed`. Messages of type `command_lifecycle`, absent from the published message union, also
  arrive; the adapter maps nothing from them.
- **Approvals** (*runtime*, *source*). In the default permission mode a Bash `tool_use` reaches
  `canUseTool` with a UUID `requestId`, after the assistant message carrying the `tool_use`. A
  request waited 2 s for its answer. `{behavior: 'allow', updatedInput}` ran the command;
  `{behavior: 'deny', message}` made the tool result an error whose content is the message, which
  the model receives. The SDK aborts the callback's `signal` when the CLI cancels a request and when
  the query closes, and writes no answer after close.
- **Interrupt** (*runtime*). With the turn waiting on the API, `Query.interrupt()` resolved in
  14 ms. A synthetic user message and a `result` with subtype `error_during_execution` and terminal
  reason `aborted_streaming` followed about 40 ms later.
- **Environment** (*source*, *runtime*, *static*). The SDK copies an explicit `env`, adds
  `CLAUDE_CODE_ENTRYPOINT=sdk-ts` and `CLAUDE_AGENT_SDK_VERSION`, and removes `NODE_OPTIONS` and
  `DEBUG`. It also sets `CLAUDE_AGENT_SDK_VERSION` in the host process's own environment. The fake
  executable saw exactly the adapter's variables plus those two (macOS CoreFoundation adds
  `__CF_USER_TEXT_ENCODING` inside every process). Every name in the adapter's allowlist appears in
  the CLI source.
- **System prompt** (*source*, *runtime*). Without `systemPrompt` the SDK sends an empty system
  prompt in its initialize request. The preset `{type: 'preset', preset: 'claude_code'}` sends
  none, leaving Claude Code's own; the fake executable's initialize request had no `systemPrompt`.
- **Process lifetime** (*source*, *runtime*). The SDK spawns the CLI in the host's process group
  with piped stdio. `Query.close()` ends stdin, sends SIGTERM after 2 s and SIGKILL 5 s later, and a
  process `exit` handler sends SIGTERM to every child still running. The process tests show that
  `close()` stops a child that ignores the end of its input with SIGTERM about 2.1 s later; that a
  host calling `process.exit` or dying of an uncaught exception still has its child stopped; and
  that a host closing the adapter on SIGTERM, as the control plane does, stays alive until its
  child is gone. With the real CLI, an idle process exited 0 within 0.4 s of stdin EOF, and
  `close()` ended one within 0.8 s. **A real CLI whose turn was waiting on the API had not exited
  60 s after stdin EOF.** A host killed without running exit handlers (SIGKILL, out of memory, a
  second signal during shutdown) therefore leaves the CLI running its turn with nobody supervising.
- **Failures** (*source*). `query()` throws synchronously when the platform binary cannot be
  resolved; other spawn failures surface through the message stream.
- **Debug logs** (*source*). The SDK writes host-side debug logs under the Claude configuration
  directory only when DEBUG, DEBUG_SDK or DEBUG_CLAUDE_AGENT_SDK is set. The process tests clear
  them.

Of the items under "Needs a runtime smoke test" above, the session id, the stream message types
used, interrupt timing and the approval round trip (with a short wait) are now verified without a
model.

### Adapter decisions

- Display name "Claude Agent"; runtime kind and default runtime id `claude-agent`.
- Capabilities: start_execution, instruct_at_rest, respond_to_approval and interrupt.
  `instruct_while_running` is false because queued input during a turn is not verified.
- Start options: `cwd` (required, an existing absolute directory); `model` (a Claude model name or
  id; a leading `-` is refused because the SDK passes it as a CLI argument); `permission_mode`
  (`default`, `acceptEdits`, `plan` or `dontAsk`; `bypassPermissions` and `auto` are refused
  because they take decisions away from the person supervising).
- Settings sources stay unset, so every source loads and the user's hooks run. HOME and
  CLAUDE_CONFIG_DIR pass through unchanged, and PATH always includes the directory of the running
  node so the observers' `node` hooks can run. Configuration cannot add SALIDIUM_INTERNAL, HOME,
  CLAUDE_CONFIG_DIR or CLAUDE_CODE_OAUTH_TOKEN, and the adapter refuses to start without
  ANTHROPIC_API_KEY or a cloud provider.
- `runtime.execution.started` is emitted at the runtime's first evidence of the session (normally
  `system/init`), with the session id the CLI reports. A turn starts when such evidence follows a
  message the adapter delivered; its id is the uuid the adapter put on that message. Approval ids
  are the SDK's request ids. Native event ids are prefixed with the chosen session id.
- A query that ends without a result fails the running turn (provenance inferred, code
  `no_result`) and loses the connection. Sessions are not resumed.

### Smoke test

`packages/integrations/claude-code/src/smoke.test.ts` runs a real session only when asked, because
it spends API credit:

```bash
HALCYONIC_CLAUDE_SMOKE=1 ANTHROPIC_API_KEY=... \
  node --test packages/integrations/claude-code/src/smoke.test.ts
```

`HALCYONIC_CLAUDE_SMOKE_MODEL` optionally chooses the model. The session uses the developer's HOME
and Claude Code configuration, like every execution Halcyonic starts. The test checks the chosen
session id, an approval before a file change, a second turn at rest, and an interrupt while an
approval is pending.

### Still unverified

- Behavior with a real model: thinking and text blocks, subagents, compaction, background tasks,
  and approvals left pending for minutes or hours.
- Whether a background task can raise an approval after its turn's result. The domain model clears
  pending approvals when a turn ends.
- How the CLI treats a message queued while a turn runs.
- Cloud provider authentication end to end; only the variable names were checked.
- `AskUserQuestion` reaches the adapter as an ordinary approval; approving it gives Claude Code no
  answers, with unknown effect.
- What a CLI orphaned by a hard-killed host does when it next needs a permission.
