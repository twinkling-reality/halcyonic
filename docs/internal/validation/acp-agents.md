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

## Reads, the person's own settings and a project's (AUDIT3 AC1 and AC2, 2026-11-17)

Run on the stand-in model, each denying first, with a control beside it:

- `read_file` of a file outside the session's folder sends `session/request_permission` (kind `read`, its
  `locations`); denied, the call is `failed` and the file's text never reaches the model. Inside the folder, and in
  the app's own temporary folder (`TMPDIR`), it reads unasked. Its other tools (`manage_task`, `send_message`,
  `schedule`, `invoke_subagent`, `define_subagent`, `manage_subagents`, `search_web`, `run_workflow`, `swarm`,
  `ask_question`) were not tried.
- The person's own settings do apply over ACP: with `"toolPermission": "always-proceed"` in
  `~/.gemini/antigravity-cli/settings.json`, `run_command` ran with no request. `permissions.allow` rules
  (`command`, `command(echo)`, `command(echo *)`, `command(*)`) did not stop it asking.
- `agy acp --gemini-dir <folder>` (an absolute path; `--app-data-dir` must be relative and failed absolute) moves
  both its settings (`<folder>/antigravity-cli/`) and its hooks, tool servers and plugins (`<folder>/config/`):
  with the person's home set to always-proceed, a run with `--gemini-dir` asked again, and nothing was written to
  the person's `~/.gemini`. Its Google sign-in is said to be kept in the system keychain; that it still applies
  under `--gemini-dir` is not yet seen (Antigravity is not signed in here).
- Kiro: `KIRO_HOME` moved `settings/cli.json` out of `~/.kiro`, while its sign-in store stayed in
  `~/Library/Application Support/kiro-cli`.

So the adapter launches each app with a settings home of Halora's own under the person's home (since AC3, one per
launch under `~/.gemini/halora` and `~/.kiro/halora`) and an agent-app profile per launch: the light profile, plus the person's push and cloud
credentials (`~/.ssh`, `~/.config/gh`, `~/.git-credentials`, `~/.netrc`, `~/.config/git/credentials`, `~/.aws`,
`~/.config/gcloud`, `~/.azure`, `~/.kube`, `~/.docker/config.json`) and the project's own settings for the app
(`.agents` and `.gemini` for Antigravity, `.kiro` and `.amazonq` for Kiro, at any depth and in any case) refused,
read and write. End to end: an approved `read_file` of `~/.ssh/id_marker` fails and its text never reaches the model
while the same read of a file beside it does; an approved command that writes `.agents/hooks.json` fails while the
same command's write beside it lands.

## One settings home per launch (AUDIT3 AC3, 2026-11-17)

A settings home made once and reused is the person's-settings hole again, in Halora's own folder: one approved
write there (an allow-everything mode, a hook, a tool server, an agent) would reach every later session. So each
launch gets a fresh settings home, `~/.gemini/halora/<launch>` or `~/.kiro/halora/<launch>`, removed when the app
exits, and the agent-app profile refuses the folder that holds them, read and write, but for the launch's own.
End to end, failing first: an approved command writes `"toolPermission": "always-proceed"` into
`~/.gemini/halora/antigravity-cli/settings.json` (where a shared home would be); it is refused, the folder of homes
is empty once the run ends, and a later launch on the same home still asks before its command, which denied never
runs. With the home shared and that folder open again, the same test fails. Under `sandbox-exec`, another launch's
home and the folder itself are refused while the launch's own is written.

A home's name is the control plane's process id, then the launch's id. Each launch first removes the homes whose
control plane no longer runs (one killed hard never ran their removal), and leaves the others, a live control
plane's and any entry not named as a home (AUDIT3 AC5).

A session's mode is watched for its whole life, not only at its start (AUDIT3 AC4): when the app says it is in
another mode than the asking one (`current_mode_update`, or the `mode` option in `config_option_update`), the
running turn is cancelled, every open request withdrawn, and the turn ends failed with `mode_left` ("... switched
to a mode where it does not ask you first, so Halora stopped the turn."); before the next instruction the mode is
set back with `session/set_mode`, or the session takes no more work. Unit tested on the recorded Antigravity trace
with `auto` announced mid-turn; neither app was seen to change its mode by itself.

Where each app keeps its sign-in, outside every folder the profile refuses, so a fresh home signs in:

- Kiro: `~/Library/Application Support/kiro-cli/` (`data.sqlite3` beside its own runtime); a fresh `KIRO_HOME`
  made `identities/` of its own at its first signed-in launch and listed models.
- Antigravity: the system keychain, by its docs; a fresh `--gemini-dir` opened a signed-in session. Its own
  `~/.gemini/antigravity-cli/` is not needed for it.

## Real runs, signed in (2026-11-17, the owner's sign-ins, bounded by the lead)

The owner signed in to Kiro ("Logged in with Google") and to Antigravity on this Mac. Each run: a scratch folder,
the agent-app profile, the app's settings home of Halora's own, the cheapest listed model, at most ten minutes.

- Kiro's sign-in applies under `KIRO_HOME=~/.kiro/halora` (its store stays in Application Support):
  `kiro-cli chat --list-models --format json` listed 23 models with each one's credit multiplier (default `auto`
  1.0; cheapest `qwen3-coder-next` 0.05, then `claude-haiku-5.5` 0.06).
- Signed in, Kiro's v3 `initialize` names no `agentInfo` (signed out it named 3.4.0), so the profile reads the
  version from `kiro-cli --version` ("kiro-cli 3.4.0") before the launch. It advertises `loadSession`, images,
  `sessionCapabilities` list, close, delete, fork, and many `_kiro/*` extensions; auth methods `aws-builder-id` and
  `aws-iam-identity-center`. `session/new` answers modes `vibe` ("Default", current), `spec`, `quick-spec`: its
  agents, not permission modes.
- Run 1, raw ACP, every request denied (`qwen3-coder-next` asked in `_meta.kiro.modelId`): six
  `session/request_permission` (three writes, `echo "hello" > hello.txt`, `ls -la`, `true`), options `accept`
  (allow_once), `always-accept`, `reject` (reject_once), `always-reject`; nothing was written. `pwd` ran without a
  request, by Kiro's own rules. Each request's `toolCall` carried only `toolCallId`, `status` and `title`: the input
  (`rawInput` `{command, description, run_in_background}` for a command, `{path, text}` for a write) and `kind` came
  on the earlier `tool_call` and `tool_call_update` of the same id, so the adapter shows a request with what the app
  said of that call (ACP updates accumulate). A write carries no `diff`, so it can only be denied. The turn ended
  `stopReason: "end_turn"`; Kiro's own report, as `session_info_update` `_meta.kiro` `turn_completion` and
  `session_usage`: 0.0888 credits, 40.6 s.
- Run 2, through the adapter (`chat.defaultModel` `qwen3-coder-next` in Halora's settings home): the one request,
  `{"command":"echo hello > hello.txt","description":"Write hello to hello.txt","run_in_background":false}`, was
  complete and approved; it ran in the scratch folder (`hello.txt` holds `hello`), the agent said so, and the turn
  completed, 6 s. Kiro's own report: 0.0531 credits. Which model served it is not in what the adapter reads.
- Antigravity's Google sign-in applies under `--gemini-dir ~/.gemini/halora` (kept in the keychain): `session/new`
  opened a session with modes `default` (current), `accept-edits`, `auto`, `plan`, and 22 models (current
  `gemini-4-carbon-medium`). Its settings file takes no model (`model` and `model.name` were ignored);
  `session/set_config_option` set `model` `gemini-3.8-flash` and `thought_level` `low`, mode staying `default`.
- Run 3, through the adapter on `gemini-3.8-flash` low: Antigravity answered at once with the agent message "⚠
  Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 45m35s." and
  `stopReason: "end_turn"`, so the adapter recorded the turn completed with no work done; nothing ran, nothing was
  spent. A used-up quota reaches the person only as the agent's words.

## Kiro 3.4.0 over ACP, signed out

- `kiro-cli acp --agent-engine v3`: `initialize` answers `protocolVersion: 1`, `agentInfo.version: "3.4.0"`,
  `loadSession: false`, no image, and one auth method, `kiro-login` ("Run 'kiro-cli login' in a terminal").
  `session/new` answers `-32000` "Authentication required. Not logged in. Set the KIRO_API_KEY environment variable
  or run `kiro-cli login` first."
- Its home folders: `~/.kiro/` and `~/Library/Application Support/kiro-cli/`; `KIRO_HOME` moves the first.
- From the docs, beyond the signed-in runs above: methods `session/new`,
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
- On macOS the app runs under the agent-app profile (`agentAppProfile`, one file per launch, removed at its end): it
  and everything it runs never read or write the data folder, the task copies, any `~/.halcyonic*` folder, the
  person's push and cloud credentials, or the project's own settings for the app. The end to end suite checks each
  with a control beside it. It is launched with a settings home of Halora's own, so the person's own settings for
  the app never apply. It is recorded `confined: false` with words why: the app talks to its maker's service
  directly, which the closed profile does not allow. `runsOutsideSandbox()` is true, as for Claude Code on a
  connected folder.
- The app signs in its own way and the adapter never passes it a key: the profile's sign-in and endpoint variables
  (`GEMINI_API_KEY`, `GOOGLE_*`, `KIRO_API_KEY`, `KIRO_HOME`, ...) are taken out of every launch.
- A process exit is reported `runtime.connection.lost`; the execution is then lost (no re-attach).
- Registered when `HALCYONIC_ANTIGRAVITY_BIN` or `HALCYONIC_KIRO_BIN` is set in the environment; never from a
  settings file, since each spends the person's own plan.

## Tests

- Unit, `acp-runtime.test.ts` (24, with the signed-in Kiro trace `kiro-signed-in-deny.jsonl`, its long
  `session/new` answer cut by the probe to its id and modes); `profile.test.ts`'s agent-app profile under `sandbox-exec` (2, each with a
  control, two mutations caught): a fake app replays the traces recorded from these runs
  (`fixtures/antigravity-*.jsonl`, paths replaced by `$CWD`, the model list shortened; `kiro-signed-out.jsonl`).
  Four mutations were each caught (a denial taken at `in_progress`, the edit's reach unknown, a cancelled turn
  read as completed, the app's own key passed on).
- End to end, `acp-runtime.e2e.test.ts` (11), under the agent-app profile: on the pinned `agy` and the stand-in
  model, approve, deny, stop during an approval, a second turn, a host folder unreadable, the person's
  always-proceed setting not applied, a read outside the folder asked and denied, a sign-in file refused to an
  approved read, a project's hook folder refused to an approved write (each with its control), signed out; on
  the pinned `kiro-cli`, a start launched as the control plane launches it, refused signed out. Run with
  `HALCYONIC_AGY_BIN` and `HALCYONIC_KIRO_BIN` naming the pinned binaries; each part is skipped without its variable.

## Not verified

- A signed-in Antigravity turn that did work (its quota was used up), so its real model's tool names and
  requests; and how Kiro's writes reach a file (`{path, text}`: a whole new file or not), so its writes stay
  deny-only.
- Which of Antigravity's other tools ask.
- What either app does with its input closed in the middle of a turn, so whether one outlives a control plane that
  is killed hard.
- Kiro's and Antigravity's telemetry beyond `--disable-telemetry` (Antigravity) and Kiro's `telemetry` setting.
