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
- **Status:** Documentation verified; the adapter's surface runtime verified without a model, then
  with a real model (smoke test, 2026-09-26, below).

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

Superseded by the adapter build section below, which lists what is now verified and what is not.

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
- Start options: `cwd` (required, an existing absolute directory that the host's directory policy
  allows; the control plane allows only directories under `HALCYONIC_PROJECT_ROOTS`); `model` (a Claude model name or
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
  answers, with unknown effect. Since verified: approving it tells the model "The user did not
  answer the questions."; the adapter now reports it as a question and answers it in the tool's
  input ([agent-questions.md](agent-questions.md), 2026-10-01).
- What a CLI orphaned by a hard-killed host does when it next needs a permission.

## Smoke test with a real model (2026-09-26)

Method: `packages/integrations/claude-code/src/smoke.test.ts` with the owner's workspace-scoped API
key, model `claude-sonnet-5`, the bundled CLI 2.1.283, the developer's own HOME and Claude
configuration (so the user's hooks ran), in a temporary working directory. Run from inside a Claude
Code desktop session.

- **Passed in 12 s.** The CLI kept the session id the adapter chose; Claude Code asked for approval
  before running `touch smoke-marker.txt`, and the approved command ran and created the file; a
  second turn at rest completed; a third turn's approval for `touch interrupt-marker.txt && sleep
  120` was left pending and an interrupt ended the turn as interrupted.
- **`ANTHROPIC_BASE_URL` must not be inherited.** The desktop session that ran the test sets it in
  its own environment for its own endpoint. While the adapter inherited it, the CLI sent the API
  key there: one key was refused with 400 (the endpoint demanded a workspace header) and another
  with 401 (invalid key). The adapter no longer inherits it; a gateway can still be configured on
  purpose as an addition (`HALCYONIC_AGENT_ENV`).
- **`sleep 120` alone did not ask for approval** within four minutes, while a command that writes a
  file did. Which commands Claude Code runs without asking is its own policy; the adapter only sees
  the requests it makes.

## End to end through the control plane (2026-09-26)

A real Claude Agent execution driven through the control plane over the realtime protocol, with
`HALCYONIC_CLAUDE_AGENT=1`, a temporary data directory and project root, and model
`claude-sonnet-5`. The snapshot listed `claude-agent` ("Claude Agent") beside the mock runtime. The
execution went `starting`, `running`, then `waiting_for_human` on an approval whose subject was
`{kind: tool_use, tool_name: Bash, summary: "touch e2e-marker.txt"}`; the approval sent as an
`execution.respond_to_approval` command resolved it, the tool ran and created the file, and the turn
completed. Every command (project, workstream, start, approval) ended `completed`, and the execution
kept the native session id. The understanding endpoint answered `unauthorized`
(`credential_missing`), since that data directory held no Salidium credential.

## Process lifetime after a hard kill: record and watchdog (2026-09-26)

Question: can a Claude Code process outlive the process hosting the adapter? The adapter build
section above found that a host killed without running exit handlers leaves a CLI with a turn in
flight running with nobody supervising.

Method: the pinned `@anthropic-ai/claude-agent-sdk` 0.3.283, read in its published types
(`sdk.d.ts`, *types*) and bundled source (`sdk.mjs`, *source*); a static search of the JavaScript
embedded in the pinned CLI 2.1.283 binary (*static*, not run); and runtime checks on macOS 26.7
(arm64) with Node 24.15.0 (*runtime*), using the fake executable (`src/testing/fake-claude.mjs`),
the host helper (`src/testing/exiting-host.mjs`) and stand-in processes. No model, network or
credential was used, the real CLI was not run, and Linux was not tested.

Verified:

- **The SDK lets the host spawn the CLI** (*types*, *source*).
  `Options.spawnClaudeCodeProcess?: (options: SpawnOptions) => SpawnedProcess` is called instead of
  the SDK's own spawn; `SpawnOptions` is `{command, args, cwd?, env, signal}`, and a `ChildProcess`
  satisfies `SpawnedProcess`. The SDK exposes the child's pid no other way. `query()` spawns
  synchronously (only a resume through a `sessionStore` defers the spawn).
- **What the SDK's own spawn does** (*source*). `ProcessTransport.initialize()` builds one
  `{command, args, cwd, env, signal}` object and passes it either to the custom function or to its
  own `spawnLocalProcess`, which calls `child_process.spawn(command, args, {cwd, stdio: ['pipe',
  'pipe', 'pipe'], signal, env, windowsHide: true})`. With a custom function the SDK does not add
  `--debug-file <path>`, which it adds only when the host sets `DEBUG_CLAUDE_AGENT_SDK`; it does not
  read stderr, whose tail it otherwise appends (redacted) to exit errors; and it delivers `exit`
  without waiting for stderr to close. Its close sequence (end stdin, SIGTERM 2 s later, SIGKILL
  5 s after that) and its process exit handler act on the returned object, so they apply unchanged.
  It writes user messages as soon as its initialize request is written, without waiting for the
  answer, so a CLI receives both together.
- **Tool commands and hooks leave the CLI's process group** (*static*). The CLI spawns shell
  commands and hooks with `detached: true`, so signalling the CLI's group would not reach them.
- **The adapter launches Claude Code exactly as the SDK does** (*runtime*). Launched through the
  adapter and through the SDK's own spawn from the same options, the fake received the same
  arguments (apart from the session id), the same environment names and values for HOME, PATH and
  CLAUDE_CONFIG_DIR, the same working directory, and the same kind of stdin, stdout and stderr
  (sockets on macOS), and both ran in the host's process group.
- **What `ps` reports** (*runtime*), with `ps -ww -p <pid> -o lstart=,args=`, `TZ=UTC` and
  `LC_ALL=C`. A native binary shows its final command line on the first read after spawn. A
  `#!/usr/bin/env node` script, like the fake executable, shows `/usr/bin/env node <path> ...` for
  several reads, then `node <path> ...`, with the same pid and start time. Under `LC_ALL=C`, macOS
  `ps` escapes non-ASCII arguments (`ö` prints as `M-CM-6`), so a command line from `ps` cannot be
  compared with the raw arguments.

What the adapter does now:

- `processRecordFile` is a required option. The control plane passes
  `<data dir>/claude-agent-processes.json` and calls `stopStaleProcesses()` at startup.
- Every Claude Code process is launched through `spawnClaudeCodeProcess` with the SDK's own spawn
  call, in the host's process group; stderr is drained so the CLI cannot block on it. It receives
  no input until it is in the record file and the watchdog has the updated list in its pipe. If
  either fails, it is killed with SIGKILL before any input and its query fails with a message saying
  it could not be recorded and watched.
- The record file (mode 0600, replaced atomically) lists each live process: pid, start time and
  command line as `ps` reports them, and the session id Halcyonic chose. The command line holds no
  secret for the options the adapter sets, and is visible to every local user through `ps` anyway.
  It is removed when no process is left.
- A live process is the recorded one when `ps` reports the same start time and a command line that
  carries `--session-id=<the recorded id>`, a random UUID per launch. The rest of the command line
  is not compared, because of the exec and the escaping above.
- One detached watchdog process (`node src/watchdog.ts`, empty environment) runs while the adapter
  has live processes; its stdin is a pipe from the host, and each line is the complete list. When
  its input ends (the host died, however it died, or the adapter closed the pipe) or on SIGTERM,
  SIGINT or SIGHUP, it stops every listed process that is still the recorded one: SIGTERM, then
  SIGKILL after 5 s, the identity checked before each signal, the pid only and never a group.
- `stopStaleProcesses()` stops what an earlier run left in the record, the same way, then removes
  it; the first launch waits for it. `close()` resolves once every launched process has exited, the
  record is removed and the watchdog has exited.

Results (*runtime*; `src/process-record.test.ts`, `src/sdk-process.test.ts` and the control plane's
`runtimes.test.ts`, five consecutive green runs of all 26 tests, and `pnpm check`):

- A host killed with SIGKILL while its CLI ignores the end of its input: the watchdog stopped the
  CLI with SIGTERM; the whole test, host startup included, takes about 0.3 s. With three sessions,
  all three were stopped.
- Host and watchdog both killed with SIGKILL: the CLIs kept running; the next adapter's
  `stopStaleProcesses()` stopped both and removed the record. At control plane startup,
  `stopStaleRuntimeServers` stops a recorded process and reports `stopped` without launching
  anything.
- A recorded pid whose process started at another time, or lacks the recorded session id, is
  reported `not_ours` and never signalled, by `stopStaleProcesses()` and by the watchdog.
- A process that execs another program after launch (`/bin/sh` to `node`) is still recognized and
  stopped. One that ignores SIGTERM is killed with SIGKILL after the grace period.
- Every fake CLI launched through the adapter found itself in the record when its first input
  arrived, including three launched at once; one launched by the SDK's own spawn did not.
- With a read-only record directory, the process was killed before any input, the start failed
  with `runtime_exited`, and no watchdog ran.
- `close()` stopped a CLI that ignores the end of its input with SIGTERM about 2.1 s after closing,
  removed the record and stopped the watchdog. A host that closes on SIGTERM exits only after its
  CLI and watchdog are gone.
- The earlier checks still pass: the session id chosen at launch, the environment rules
  (`settingSources` unset, HOME, PATH with node, no SALIDIUM_INTERNAL, no ANTHROPIC_BASE_URL), and
  hosts that exit or crash.

Still unprotected or unverified:

- **Platforms.** macOS was tested; Linux was not. The mechanism needs a `ps` that accepts
  `-ww -p <pid> -o lstart=,args=` (procps-ng or BSD); with BusyBox `ps` (Alpine, for example)
  every launch fails closed. Windows is not supported.
- **Tool commands and hooks.** Only the CLI is signalled. What it started in its own process groups
  is left to Claude Code's own shutdown on SIGTERM, which was not verified; a CLI that has to be
  killed with SIGKILL may leave them running.
- **The watchdog's delay.** Between a hard kill of the host and the watchdog's SIGTERM, the CLI runs
  unsupervised for a fraction of a second, and up to 5 s more if it ignores SIGTERM.
- **The watchdog alone killed.** Running processes are then protected only by the record until the
  next launch or exit starts a new watchdog; if the host also dies, they run until the next start
  calls `stopStaleProcesses()`.
- **Start times** have one-second resolution; a reused pid is mistaken for a recorded process only
  if it also starts within the same second and carries the recorded session id.
- **Diagnostics.** Exit errors no longer carry the CLI's stderr tail, which the SDK collects only for
  its own spawn; the adapter drains stderr without reporting it, since it may hold secrets. With
  `DEBUG_CLAUDE_AGENT_SDK` set on the host, the CLI no longer receives `--debug-file`.
- **The real CLI** was not run for these checks.

## Listing models (2026-09-30)

Question: how can the adapter list the models Claude Code can run, for the choice of
[ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)?

Method: the pinned `@anthropic-ai/claude-agent-sdk` 0.3.283, read in its published types
(*types*), and unit tests with a scripted query in place of the SDK's. The real CLI was not run:
listing starts a Claude Code process with the key, which may reach Anthropic, and no model or
credential was used.

Verified (*types*):

- `Query.supportedModels(): Promise<ModelInfo[]>` lists the available models. `ModelInfo` carries
  `value` (the identifier to use in API calls), an optional `resolvedModel` (the canonical model id
  an alias such as `sonnet` resolves to), `displayName`, `description`, and effort, thinking, fast
  mode and auto mode flags. It says nothing about tool calling or the context window.
- The SDK documents, for a process claimed from `startup()`, that `supportedModels()` waits for
  the claim's answer and that the folder's `availableModels` setting narrows the list as it does
  after a cold `query()` in that folder.

What the adapter does:

- `listModels()` starts a query with no prompt, in the system's temporary directory, with an
  execution's environment, launched, recorded and watched like any other Claude Code process,
  waits for `supportedModels()`, then ends its input and closes it.
- Each model's `model_ref` is its `resolvedModel`, else its `value`, listed once; its name is the
  display name and who serves it: Anthropic or the cloud provider a switch selects, read as
  Claude Code reads booleans, or a gateway, named by its host and port, when `ANTHROPIC_BASE_URL`
  or the provider's own base URL is passed on purpose. It is served remotely, except behind a
  gateway on this Mac. Tool calling and context are unknown.
- A start with a `model_ref` lists again first, refuses a model no longer listed with
  `model_unavailable`, and passes the reference as the session's model. The model named in the
  session's `system/init` message is reported as `runtime.model.used`, again when it changes.

Not verified:

- Listing against the real CLI: its duration, and whether it reaches the network.
- Whether the model in `system/init` equals the `resolvedModel` the list gave, so that an
  execution's `model_ref` reads as the one chosen.
- Whether the listing process runs the developer's hooks: `settingSources` stays unset, so a
  `SessionStart` hook could show Salidium and Seorak an empty session for each listing.
- The list is read in a temporary directory, so an `availableModels` setting of the execution's
  project does not narrow it; the start would then fail in Claude Code rather than at admission.
