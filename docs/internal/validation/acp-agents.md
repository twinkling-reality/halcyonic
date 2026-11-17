# Agent apps over the Agent Client Protocol: Antigravity and Kiro

Verified 2026-11-17 on this Mac (Apple silicon, macOS 26), by the AGENTS lane. Each binary was taken from its
vendor's own download host and checked against the vendor's own manifest. Every run here used an isolated home,
no sign-in and no key: Antigravity's turns were served by a stand-in Gemini API on loopback, and Kiro was run
signed out. What a signed-in run of either would show beyond this is marked unverified.

## Binaries

| App | Version | Source | Check | Signed by |
| --- | --- | --- | --- | --- |
| Antigravity CLI (`agy`) | 1.5.10, built 2026-11-16 | `storage.googleapis.com/antigravity-public/antigravity-cli/1.5.10-5253791805734912/darwin-arm/cli_mac_arm64.tar.gz`, named by the manifest its install script reads (`antigravity-cli-auto-updater-974169037036.us-central1.run.app/manifests/darwin_arm64.json`) | sha512 from that manifest, matched | Developer ID Application: Google LLC (EQHXZ8M8AV) |
| Kiro CLI (`kiro-cli`) | 3.4.0, built 2026-11-16 (`BUILD_HASH` 9de4a162) | `prod.download.cli.kiro.dev/stable/3.4.0/kirocli-universal-darwin.tar.gz`, named by `stable/latest/manifest.json` | sha256 from that manifest, matched | Developer ID Application: AMZN Mobile LLC (94KV3E626L) |

Neither install script was run: each would install into the person's home and, for Kiro, `/Applications`. Neither
CLI is open source. Antigravity updates itself in the background unless `AGY_CLI_DISABLE_AUTO_UPDATE=true`, which
the adapter always sets.

Docs read: kiro.dev/docs/cli/headless, /cli/acp, /cli/v3/acp-migration, /reference/cli-commands, /permissions,
/hooks/types, /sandbox, /reference/settings, /changelog/cli/3-4; antigravity.google/docs/cli/headless, /cli/acp,
/cli/reference, /permissions, /hooks, /sandbox, /settings, /changelog; agentclientprotocol.com (schema,
initialization, prompt turn, tool calls).

## Why ACP and not the headless modes

Both apps have a headless mode with JSON Lines output, and neither can ask the person in it:

- Kiro: "every `ask` is treated as a deny" with `--no-interactive` (kiro.dev/docs/permissions).
- Antigravity: actions that need approval are "soft-denied: the run continues, exits `0`" (antigravity.google/docs/cli/headless).

Both apps serve the Agent Client Protocol, version 1, JSON-RPC 2.0 over stdio: `agy acp` and
`kiro-cli acp --agent-engine v3`. Over ACP a tool call that needs the person comes as a
`session/request_permission` request the client answers, the turn ends when `session/prompt` is answered with a
`stopReason`, and `session/cancel` stops it. So one adapter speaks ACP for both
([ADR 0067](../decisions/0067-agent-apps-that-speak-acp-are-directed-through-one-adapter.md)).

## Antigravity 1.5.10 over ACP (run, on the stand-in model)

- `initialize` (signed out too) answers `protocolVersion: 1`, `agentInfo.version: "1.5.10"`, capabilities
  `loadSession`, `promptCapabilities.image`, `sessionCapabilities` resume, close, load, list, and auth methods
  `oauth-personal`, `oauth-business`, `gemini-api-key`, `agent-platform`.
- Signed out, `session/new` answers error `-32000` "Authentication required". The adapter fails the start
  `sign_in_required`, effect none.
- With `{"modelProvider":"gemini"}` in `~/.gemini/antigravity-cli/settings.json`, `GEMINI_API_KEY` set to a dummy
  and `GOOGLE_GEMINI_BASE_URL` pointing at the stand-in, `authenticate {methodId: "gemini-api-key"}` answers `{}`
  and whole turns run: the app asks `POST /v1beta/models/<model>:streamGenerateContent?alt=sse`, once with tools for
  the turn and once without for a title. This is the end to end suite's setup only; the control plane never passes
  these variables.
- `session/new {cwd, mcpServers: []}` answers `sessionId` (a UUID), `models.availableModels` (21 on the stand-in)
  with `currentModelId`, `modes` (`default`, `accept-edits`, `plan`) and `configOptions` (`model`,
  `thought_level`, `mode`).
- A turn: `session/update` notifications `agent_message_chunk`, `tool_call`, `tool_call_update`, `usage_update`
  (`used`, `size`), `available_commands_update`, `session_info_update` (a title, after the turn), then the
  `session/prompt` answer `{"stopReason": "end_turn", "usage": {"inputTokens", "outputTokens", "cachedReadTokens",
  "cachedWriteTokens", "thoughtTokens", "totalTokens"}}`.
- A tool call carries `toolCallId`, `name` and `args` (Antigravity's own), `kind`, `title`, `status`, `rawInput`,
  `locations` for file tools, and at its end `rawOutput` (a command's `exit_code`) and `content`.
- Asking: in the default mode `run_command` (`rawInput` `CommandLine`, `IsDaemon`, `WaitMsBeforeAsync`,
  `toolSummary`), `write_file` and `edit_file` each send `session/request_permission` with options
  `{"optionId": "allow", "kind": "allow_once"}` and `{"optionId": "deny", "kind": "reject_once"}`, and wait.
  The command ran in the session's `cwd`.
- Allowed: the call goes `in_progress`, then `completed`. Denied: `in_progress`, then `failed` with empty output,
  and the command did not run. So a denial is taken only once the call is `failed`.
- `write_file` of a new file puts a `diff` on the request with `newText`, the whole file, and no `oldText`.
- `edit_file` puts a `diff` with `oldText` and `newText` as the agent's target and replacement, not the file; with
  the target twice in the file, both were replaced (`x\nsame\nsame\n` became `x\nchanged\nchanged\n`). So its
  change is a replacement at every occurrence, which the request itself does not say.
- `session/cancel` while a request waits: the call is settled `failed`, the prompt is answered
  `stopReason: "cancelled"`, and the command did not run; the client then answers the request `cancelled`, as ACP
  requires.
- A second `session/prompt` on the same session after the first ended runs a second turn.
- The app exits when its input ends, at rest. Whether it does in the middle of a turn was not tried.
- Its own home folders: `~/.gemini/antigravity-cli/` (settings, conversations, logs, brain) and `~/.gemini/config/`.

## Kiro 3.4.0 over ACP (run signed out only)

- `kiro-cli acp --agent-engine v3`: `initialize` answers `protocolVersion: 1`, `agentInfo.version: "3.4.0"`,
  `loadSession: false`, no image, and one auth method, `kiro-login` ("Run 'kiro-cli login' in a terminal").
  `session/new` answers `-32000` "Authentication required. Not logged in. Set the KIRO_API_KEY environment variable
  or run `kiro-cli login` first."
- Its home folders: `~/.kiro/` and `~/Library/Application Support/kiro-cli/`; `KIRO_HOME` moves the first.
- From the docs, unverified here: signed in, v3 advertises `loadSession` and images; methods `session/new`,
  `session/load`, `session/list`, `session/prompt`, `session/cancel`, `session/set_mode`,
  `session/set_config_option`; permission requests with `allow_once`, `allow_always`, `reject_once`,
  `reject_always`; models as `configOptions`; usage only as `_meta.kiro` in `session_info_update`. `KIRO_API_KEY` is
  for paid plans only, and an active `kiro-cli login` takes precedence over it.

## What the adapter does with it

`packages/integrations/acp`: one app process per execution, started in the project's folder, the folder asked
again right before `session/new`; the app's version must be the profile's, or the start is refused with effect none.

- Declared: `start_execution`, `instruct_at_rest`, `respond_to_approval`, `interrupt`. Not declared:
  `instruct_while_running` (ACP has no steer), `answer_question` (no question surface), `never_ask`, plan, pictures,
  usage, tool activity, model choice, re-attach after a restart. Each is open until a run shows it.
- An approval is answered with the app's own `allow_once` or `reject_once` option, never an `always` one, and
  counted taken when the call next reports `in_progress` or `completed` (approved) or `failed` (denied), else it
  fails after 15 s with effect unknown.
- A request is shown as its whole `rawInput`; Antigravity's `run_command` as its command, "in its shell, which
  started in the task's folder", complete only beside inputs that do not act. A file request is complete only with
  its change carried whole: a new file's whole text, or a profile-known replacement (`edit_file` at every
  occurrence). A Kiro edit, a delete, a move and a request without its input can only be denied.
- On macOS the app runs under the light profile (`hostFoldersDeniedProfile`): it and everything it runs never read
  or write the data folder, the task copies or any `~/.halcyonic*` folder. The end to end suite checks this with a
  control: an approved `cat` of a file in a host folder fails, the same `cat` of a file beside it succeeds. It is
  recorded `confined: false` with words why: the app reaches its maker's service directly, which the closed profile
  does not let through. `runsOutsideSandbox()` is true, as for Claude Code on a connected folder.
- The app signs in its own way and the adapter never passes it a key: the profile's sign-in and endpoint variables
  (`GEMINI_API_KEY`, `GOOGLE_*`, `KIRO_API_KEY`, `KIRO_HOME`, ...) are taken out of every launch.
- A process exit is reported `runtime.connection.lost`; the execution is then lost (no re-attach).
- Registered when `HALCYONIC_ANTIGRAVITY_BIN` or `HALCYONIC_KIRO_BIN` is set in the environment; never from a
  settings file, since each spends the person's own plan.

## Tests

- Unit, `acp-runtime.test.ts` (18): a fake app replays the traces recorded from these runs
  (`fixtures/antigravity-*.jsonl`, paths replaced by `$CWD`, the model list shortened; `kiro-signed-out.jsonl`).
  Four mutations were each caught (a denial taken at `in_progress`, the edit's reach unknown, a cancelled turn
  read as completed, the app's own key passed on).
- End to end, `acp-runtime.e2e.test.ts` (6), on the pinned `agy` and the stand-in model, under the light profile:
  approve, deny, stop during an approval, a second turn, a host folder unreadable with its control, signed out.
  Run with `HALCYONIC_AGY_BIN=<the pinned agy>`; skipped without it.

## Not verified

- Any signed-in run of either app, so the real model's tool names, Kiro's tool call shapes and whether Kiro sends
  `rawInput` at all. Until Kiro is seen signed in, every Kiro request is shown as its whole input or title, and its
  edits only denied.
- Whether the person's own Antigravity permission rules (`permissions.allow`) let a call run unasked through ACP.
- What either app does with its input closed in the middle of a turn, so whether one outlives a control plane that
  is killed hard.
- Kiro's and Antigravity's telemetry beyond `--disable-telemetry` (Antigravity) and Kiro's `telemetry` setting.
