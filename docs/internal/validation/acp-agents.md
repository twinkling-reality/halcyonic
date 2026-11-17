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
with `auto` announced mid-turn; neither app was seen to change its mode by itself. What stays open is the app's own race: an
action it takes unasked between announcing the mode and receiving the cancel is not stopped by the watch.

Where each app keeps its sign-in, outside every folder the profile refuses, so a fresh home signs in:

- Kiro: `~/Library/Application Support/kiro-cli/` (`data.sqlite3` beside its own runtime); a fresh `KIRO_HOME`
  made `identities/` of its own at its first signed-in launch and listed models.
- Antigravity: the system keychain, by its docs; a fresh `--gemini-dir` opened a signed-in session. Its own
  `~/.gemini/antigravity-cli/` is not needed for it.

## No app outlives the control plane (AUDIT3 AS1, 2026-11-17)

Measured on the pinned Antigravity on the stand-in model: with a command (`sleep 45; ...`) running after an
approval, the process that launched `agy acp` was killed with SIGKILL; within four seconds `agy` and its command had
both ended, and the command's file was never written. Kiro was not measured (a turn needs its sign-in), and an app
need not end with its input.

So each adapter keeps a process record in the data folder (`acp-<runtime>-processes.json`, which the app cannot
reach) and a detached watchdog, as Claude Code's adapter does: once an app has answered `initialize`, before it is
given a folder or a prompt, its pid, the start time and the command line `ps` shows are recorded and handed to the
watchdog. An app that cannot be recorded is stopped and its start refused. When the control plane dies however it
dies, the watchdog's input ends and it stops each recorded app that is still that process (the same start time and
command line; a Kiro command line names no launch, so both are compared whole); the control plane's next start
stops any that outlived both, before anything else. Tested with a stand-in control plane killed with SIGKILL while
its stand-in app ignores the end of its input: the app is gone within seconds, and without the record (the control)
it stays. The sweep stops a recorded live process and leaves one whose command line differs. Mutations (identity
always matching, no watchdog) are each caught.

## Kiro's autopilot (2026-11-17)

Signed in, Kiro's `session/new` reports `configOptions` with `autopilot` on, described "Agent executes tools without
confirmation" (Off: "Supervised"), and a `model` option (current `auto`). The signed-in runs above ran with it on
and still asked before every write, `ls -la` and `true`; only `pwd` ran unasked. Halora does not rely on that: Kiro's
profile names `autopilot: off` as an asking option. Every session sets it with `session/set_config_option` before
anything else, whether or not `session/new` reported it (AUDIT3 AK1), and starts only once Kiro reads it back off, in
the set's answer or its report since (AK2); Kiro answers with its options, autopilot off, and reports the same in a `config_option_update` (seen,
no prompt sent). A start where Kiro does not take it is refused. A later report of autopilot on, like a mode other
than the asking one, cancels the turn, which ends `mode_left`, and the option is set again before the next
instruction. Kiro reports autopilot on once before the set, at the session's start, which the watch ignores. The
recorded Kiro traces have the real answer to the set spliced in after `session/new`, with the later reports off, as
Kiro makes them; a test keeps one report on mid-turn and sees the turn stopped. Not yet seen: a Kiro turn under
Supervised, so whether it asks the same way or adds a confirmation of its own.

Kiro run 4 (18:46Z, through the adapter, autopilot off, `qwen3-coder-next` by `session/set_config_option`): Kiro
still asked before the new file (`fs_write`, carried whole and complete) and before `ls`, as with autopilot on, and
then asked once more, a turn-level "Review changes" (`toolCallId` `turn_approval_...`, options accept and reject,
`_meta.kiro` `{type: "turn_approval", files: [{path, toolCallId}]}` naming the write's call). Its write's call showed
no `in_progress` after its approval: Kiro holds staged changes until that review. The run script approved only
complete requests, so it denied the review (shown then as its title alone): the write was not made, and the turn
ended `cancelled`. Kiro's own report: 0.0107 credits, 3 s. Before this run, the per-launch settings home's folder
stopped Kiro's engine (`kas`: `EPERM ... stat '~/.kiro/halora'`), before any prompt; the agent-app profile now
lets that folder be looked at, never listed or opened.

So the Kiro profile names the review (`turnReview`): it is shown as "Review changes: <files>", with each listed
call's change as it was carried on that call's own approval, complete only where every one was; and Kiro's own
`interaction_resolved` report, with the option chosen, counts as Kiro taking an answer (`answerReport`), since a
held write's call shows nothing until the review. Tested on the recorded edit trace with the review as Kiro asked
it; an approved review of a whole Supervised turn is not yet seen live.

## Nothing in a refused folder runs (AUDIT3, 2026-11-17)

The kernel runs a binary it may not read: both apps started from a folder the agent-app profile refused to read. So
the profile also refuses `process-exec` in the host's folders, every `.halcyonic*` home, the credential folders, the
project's own settings for the app and the settings homes' folder. Under `sandbox-exec`, a copy of `/usr/bin/true` in
each of those fails to run while the same copy in the project runs; without the rule the test fails.

## Turned on from the setup page (2026-11-17)

Install Kiro and Install Antigravity run the page's own commands, as its tunnel install does: `curl -q` over HTTPS
only, a size cap, the archive checked by the vendor's own sum (Kiro's SHA-256, Antigravity's SHA-512) before `tar`
unpacks it into `<data>/runtimes/kiro-3.4.0` or `antigravity-1.5.10`. Run by hand with exactly those commands, both
archives matched, and the programs unpacked at the pinned paths with the pinned SHA-256 (`kirocli/bin/kiro-cli`
65902eb1..., `antigravity` 780ee763...). Turn Kiro On or Turn Antigravity On (two presses, as every press) checks that
copy again and writes `<data>/agent-apps-on.json`; Turn Off removes it; the change waits for a restart, which the page
says. Installing turns nothing on. Each press is drawn only where the app's state allows it: Install before it is installed
(or when its copy no longer matches), then Turn On while off or Turn Off while on, never both (AUDIT's look).

The copy sits in the data folder, which the agent-app profile refuses read and write. That stops neither app: the
kernel runs a program from a folder the profile refuses to read (Kiro and Antigravity both started from one, signed
out, with and without the folder opened), and a whole Antigravity turn ran from a copy there, end to end on the
stand-in model. So the profile opens nothing for it.

## Cursor's agent CLI (2026-11-17, signed out)

Docs read: cursor.com/docs/cli/installation, /cli/acp, /cli/reference/parameters, /cli/headless,
/cli/reference/authentication, /cli/reference/configuration, /cli/reference/permissions, /agent/security/run-modes,
/cli/changelog. The installer's own package, `downloads.cursor.com/lab/2026.11.13-eacade1/darwin/arm64/
agent-cli-package.tar.gz` (174926275 bytes), matched the `.sha256` beside it on the same server
(087220ccf1e124dcdf2753601422892042a45a2c577ef0ea25b8a5b0e47bb691; not in the docs). It unpacks a launcher script,
`cursor-agent`, beside its own Node runtime (Developer ID Node.js Foundation).

- `cursor-agent --version` prints `2026.11.13-eacade1`. `cursor-agent acp` speaks ACP v1: `initialize` (signed out)
  names no `agentInfo`, advertises `loadSession`, images, `sessionCapabilities.list`, and one auth method,
  `cursor_login` ("Run 'agent login' first"). `session/new` answers `-32000` "Authentication required ... call
  authenticate() with methodId 'cursor_login'"; `authenticate` signed out never answers.
- Its headless mode (`-p`) refuses every ask unseen (docs); `--force` / `--yolo` run everything not denied, off by
  default. Its permissions live in `cli-config.json` (`approvalMode` `allowlist`, `auto-review` or `unrestricted`,
  `permissions.allow` / `deny`), under `CURSOR_CONFIG_DIR`; a project's `.cursor/cli.json` can add permissions.
- A fresh `CURSOR_CONFIG_DIR` got, on its first launch, `{"version": 1, "permissions": {"allow": ["Shell(ls)"],
  "deny": []}, "approvalMode": "allowlist", "sandbox": {"mode": "disabled", ...}}`.

So Cursor is a third profile: `cursor-agent acp`, its version from `--version`, a fresh settings home per launch as
`CURSOR_CONFIG_DIR` with Halora's own `cli-config.json` written into it first (allowlist, nothing allowed, its own
sandbox off: the agent-app profile holds it), `.cursor` refused in the project, its key variables dropped, and
`cursor_login` asked only after a session is refused as not signed in, with an 8 s wait. Tested on the recorded
signed-out handshake and end to end on the real binary, signed out. Not seen: anything signed in (its modes, which
the profile expects to hold `agent`; its permission options' kinds; its blocking `cursor/ask_question` and
`cursor/create_plan` requests, which the adapter answers as unknown methods).

## An ask that changes, and an app's own commands (AUDIT3 KW1, SW-g, 2026-11-17)

An app that reports another input for a call after the person was asked about it and before they answered has the
ask refused by Halora at once (its once-only refusal), recorded as denied with `withdrawn.input_changed` as the
resolution's native type; the person's answer then finds it answered. And each app is launched as the leader of a
process group of its own: closing it, the watchdog and the next start's sweep signal the whole group (the sweep only
after the identity check), so the commands it started end with it. Both tested; each fails without its rule.

## Each task in a copy of its own (the limits audit's #3, 2026-11-17)

On macOS the adapter holds a task to its own copy (ADR 0045) where the host names its folders and its task copies:
`holdsFolder` is true for a recorded copy and for a folder outside the host's own, never for a host folder that is
no recorded copy, and false off macOS or with copies off. In a copy the agent-app profile gains the copy rules (see
[task-copies.md](task-copies.md)) and the launch drops `SSH_AUTH_SOCK`. Seen on this Mac:
- `profile.test.ts`, a layout of two tasks' copies, the person's folder and home under `sandbox-exec`: the copy read,
  written (its `.git` too) and run in; the person's folder read, never written (directly, its `.git`, or through a link
  in the copy); the other copy neither read, written nor run in; `.agents` inside the copy neither read, made (in any
  case) nor run in; the data folder, `~/.ssh` and a `git-credential-*` helper refused; a program in the home, a write
  in the home and in the launch's own settings home open. Each copy rule removed in turn (the copy's opening, the
  write refusal on the person's folder, the copy's exec opening, the settings' exec refusal in the copy, the helper
  refusal) lets through what it held, or closes what it opened. Both tests fail on the profile without the copy rules.
- The real `agy` 1.5.10 in a task's copy, on the stand-in model, one approved command: it wrote the copy, read the
  person's folder, and was refused writing the person's folder, writing or reading the other task's copy and making
  `.agents/hooks.json` in the copy; `SSH_AUTH_SOCK`, given in its environment, did not reach the command. With the
  drop removed the address reached it; with the adapter's copy support removed the start failed in the copy.
- Two tasks of one project at once on the real `agy` (AUDIT3 C6b): one adapter, two copies, the second task started
  while the first waited on its ask, both approved; each command ran in its own copy (`pwd -P`), and was refused
  writing and reading the other's. With the copy's opening widened to the copies' folder, the test fails.
- The app's settings folders in the person's folder are refused in a copy too (AUDIT3 SC1): `.Agents` under a
  subfolder of the person's folder is unreadable from the copy's profile, and readable without the rule.
- `runtimes.test.ts`: the three apps, registered as the control plane registers them, are held to a connected folder
  and its copy and to a starter's project and its copy on macOS, never to an unrecorded folder in the copies' folder or
  the data folder, and to nothing without task copies.

## A command ask in its parts (AUDIT3 CP1 to CP5, 2026-11-17)

On the real `agy` 1.5.10 (stand-in model), two approved `run_command` calls in one turn: `mkdir -p sub && cd sub &&
pwd -P > first.txt`, then `pwd -P > where.txt`; `where.txt` named the task's folder, so a `cd` carries over to no
later command, and each ask says the task's folder. On the recorded Kiro run, each command's ask carries the command
Kiro reported for the call, where it runs unknown. Cursor's calls carry none until a signed-in run shows its shell
tool.

## Cursor signed in (2026-11-17, three bounded runs, the cheapest model)

The owner signed in to Cursor (19:13Z). Each run: a scratch folder, the agent-app profile, a fresh
`CURSOR_CONFIG_DIR` seeded with `cli-config.json` (allowlist, nothing allowed), `NO_OPEN_BROWSER=1` (AUDIT3 CU2),
`gpt-5.4-nano[reasoning=medium]` (the cheapest by name of its 48), one turn, under ten minutes.
- No prompt (20:11Z, CU1): `initialize` names no `agentInfo`, auth `cursor_login`; `session/new` answered without
  `authenticate` (the sign-in applies under Halora's config folder), modes `agent` (current), `plan` ("Read-only"),
  `ask` ("no edits or command execution"), and `configOptions` `mode` and `model` only: no option that would run
  tools unasked, so nothing is added to its asking options. Its 48 models are the `model` option's choices, `default[]`
  ("Auto") current; ids carry their settings in brackets. It does not end when its input ends; the adapter's signal
  after 2 s does.
- Run 1 (20:12Z, through the adapter, every request denied): asked to run `echo hello > hello.txt`, Cursor asked once,
  `kind: execute`, input `{command}`, the request's content "Shell allowlist is empty", options `allow-once`,
  `allow-always`, `reject-once`; denied, nothing was written (CU3), and Cursor said the approval was rejected. Found:
  Cursor ends a refused call `completed`, with no output, which the adapter had reported as a tool that succeeded and
  never counted as the denial taken. Mended: `deniedStatuses` (Cursor's `failed` or `completed`), and a call whose ask
  was denied never reads as succeeded. 13 s.
- Run 2 (20:14Z, raw ACP, rejected once): the same, with `session/set_config_option` `model` taken and read back;
  recorded as `fixtures/cursor-signed-in-deny.jsonl` (the model list shortened to two).
- Run 3 (20:19Z, through the mended adapter, every request denied): the command asked in its parts, denied, taken as
  denied when Cursor ended it `completed`, the tool reported failed, nothing written. 11 s.
- Use: Cursor's answers to `session/prompt` carried no usage, so it reports none.

## Each app's own models (2026-11-17, no prompt, nothing spent)

- Antigravity: `session/new` answers its models as the `model` option (`configOptions`, ACP's `model` category; on the
  stand-in, two, `gemini-4-carbon` and `gemini-4-hydrogen`, while `models.availableModels` lists their effort
  variants), and `session/set_config_option` `model` `gemini-4-carbon` was answered with it current. The adapter lists
  that option's choices, the one current first, and a start with a model sets it and goes on only once the app reads
  it back. The model is set first, before the asking mode and the held options (AUDIT3 MC1): Kiro's answer to a model
  set lists every option again (and adds `effortLevel` and `thinking`), so a model change could reset one; each held
  option is then set and read back last, and the asking mode is taken from the app's latest report. End to end on the pinned binary: the listing asks the model nothing, and a turn started on a listed model
  asked the stand-in for that model.
- Kiro: its `session/new` named its 23 models in one probe and none in another under a fresh settings home, so the
  adapter lists them with Kiro's own `kiro-cli chat --list-models --format json` (under the same profile and a fresh
  settings home): `{models: [{model_id, model_name, description, context_window_tokens, rate_multiplier, rate_unit}],
  default_model}`, 23 models, `auto` first, each with its context. `session/set_config_option` `model`
  `qwen3-coder-next` and `claude-haiku-5.5` were each taken and read back. Each credit rate is in the list but not
  carried: the contract has no place for a model's price.
- Cursor: its `session/new` `model` option, 48 models (signed in, above); `set_config_option` took one and read it back.
- Every model is said `remote`: the app reaches its maker's service, its endpoint variables taken out; `tool_calling`
  is unknown, since neither list says.

## What a turn used (2026-11-17)

Antigravity answers `session/prompt` with `usage` `{inputTokens, outputTokens, cachedReadTokens, cachedWriteTokens,
thoughtTokens, totalTokens}`, summed over the turn's model calls. On the stand-in, three calls each of 100 input (40
from the cache) and 10 output with 5 more thinking gave input 180, cachedRead 120, output 45, thought 15, total 345:
input is the part not read from the cache, and output includes the thinking, as Halora's parts are. So it is reported
as `runtime.usage.reported`, `counted: given`, `cost` null (no price), epistemic `reported`, before the turn's end; end
to end, one call reported input 60, cache_read 40, cache_write 0, output 15, reasoning 5. Kiro reports credits only
(`turn_completion`, `session_usage`), which the contract does not carry, and Cursor reports nothing, so neither
declares `reports_usage`.

## Kiro's content collection held off (the lead's ruling, 2026-11-17)

In Halora's fresh settings home Kiro's `session/new` reports `contentCollection` `enabled`, "Allow content to be used
for service improvement": Kiro's default, not the person's choice (their own `~/.kiro/settings/cli.json` names no
setting). Every Kiro session sets it `disabled` after `autopilot`, starts only once Kiro reads it back (seen taken and
read back, 19:39Z, no prompt), and a later report of it enabled stops the turn (`sharing_on`, "Kiro 3.4.0 turned back
on sharing your work to improve its service, so Halora stopped the turn."), set again before the next instruction. The
recorded Kiro traces have Kiro's real answer to that set spliced in after the autopilot one, with the later reports
disabled, as Kiro makes them.

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
- Kiro run 3 (18:16Z, raw ACP, the probe approving file edits only, `qwen3-coder-next`): asked to change "line two"
  to "line 2" in an existing `notes.txt`, Kiro read it unasked (inside the folder) and asked once for `str_replace`,
  input `{path, oldStr: "line two", newStr: "line 2", replace_all: false}`, reported on the call (with a diff of the
  whole file) before the request; approved, only that line changed. Kiro's own report: 0.0324 credits, 15 s. So a
  Kiro replacement is carried from its input (shape `replace`, `every` from `replace_all`), and a write
  `{path, text}` as a new file's whole text where nothing is at that path yet; a write over a file already there is
  not seen, so it stays deny-only.
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
- A request that is no command is shown in words and then each input on a line of its own ("Use fetch", "url:
  ..."), never as JSON (AUDIT2), a name or text holding a line break, a control character or a space at either end
  quoted, so no input forges a line read as another (AUDIT3 IS1); a profile's shell tool (Antigravity's `run_command`, Kiro's and Cursor's `execute`
  call) as its command in its parts (`ApprovalCommand`, AUDIT3 CP1 to CP5), taken from the command field of the
  call's own input, never its title, uncut, its summary built from the parts alone: Antigravity's said to run in the
  task's folder (seen: each command starts there, a `cd` in one reaching no later one), or in the folder its own `Cwd`
  names (the field its own prompt's example of `run_command` uses), quoted and resolved against the task's folder;
  Kiro's and Cursor's where it runs unknown (not seen). It is complete only beside inputs that do not act; a shell call
  with any other input (Antigravity's `Blocking`) is shown in words and each input, with no command in parts, and can
  only be denied. A change to the call's command
  after the ask is shown withdraws the ask (KW1, tested on each app's trace). A file request is complete only with
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
- A process exit is reported `runtime.connection.lost`; the execution is then lost (no re-attach). Every app is
  recorded and watched, so none outlives the control plane (see below).
- Registered when `HALCYONIC_ANTIGRAVITY_BIN` or `HALCYONIC_KIRO_BIN` is set in the environment, or when the
  person turned it on from the setup page (below); never from a settings file, since each spends the person's own plan.

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
- Which settings key, if any, is the person's own choice behind Kiro's `contentCollection` (its binary names
  `codeWhisperer.shareCodeWhispererContentWithAWS`; not tried), so a person who chose to share is not followed yet.
- Kiro and Cursor in a task's copy (the same profile code, not run there), and two tasks of one project side by side
  through a control plane on an ACP app (the host's copy flow is the one Codex, OpenCode and Claude Code use, its
  journey tested on a stand-in runtime).
- What Kiro does with its input closed in the middle of a turn (the watchdog stops it either way).
- Kiro's and Antigravity's telemetry beyond `--disable-telemetry` (Antigravity) and Kiro's `telemetry` setting.
