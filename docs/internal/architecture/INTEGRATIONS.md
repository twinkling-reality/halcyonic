# Integrations

## Final recorded tool results

An adapter's optional `reports_tool_results` declaration describes verified final structured result
support, independent of `reports_tool_activity`, model choice and connection health. True supports
an empty history; false means unsupported, and absent legacy declarations are unknown. A missing
registered adapter is unavailable; accepted journal evidence remains readable. Codex 0.157.0
declares true for final command/file-change notifications; OpenCode, Claude Code and the mock
declare false and receive no emulated results. Codex 0.157.0 leaves out what a command printed before
it outlived its startup wait, so the adapter never calls such an output whole: a command whose
`durationMs` is above 0 keeps its text with `complete: false` and truncation `source_cut`
([record](../validation/codex-capabilities.md)). No other app declares `source_cut` until it is seen.

The optional `approval_may_leave_sandbox` declaration says whether approving a command can let it
run outside any sandbox, with the person's own access
([ADR 0033](../decisions/0033-approvals-declare-whether-commands-leave-the-sandbox.md)). Absent
means unknown, never false, and a client never shows an absent declaration as staying inside.
Codex 0.157.0 declares true: an approved escalation runs outside its sandbox, and the approval
request does not say whether it is one. OpenCode declares false only inside Halora's sandbox.
Claude Code, ACP agent apps, the mock and OpenCode without that sandbox leave it absent.

The optional `secret_environment` declaration says whether a start can give the agent's commands the
project's secrets as variables, and the adapter does ([ADR 0057](../decisions/0057-a-projects-secrets-are-kept-on-the-mac-and-given-to-a-tasks-commands-at-its-start.md), [record](../validation/project-secrets.md)).
Absent means a task on that runtime starts without them, and the setup page says some agents do. Claude
Code declares it (the session's process environment), OpenCode (the session's own command environment,
no server restart) and Codex 0.157.0 (`shell_environment_policy.set` in the thread's `config`, at its start
and every resume). A secret never replaces an adapter's own variable. The mock and ACP agent apps declare nothing.

The optional `offers_never_ask` declaration says whether a start may choose the approval level
`never_ask`, under which the agent's actions run without a request to the person inside the same
bounds as `ask` ([ADR 0043](../decisions/0043-a-person-may-let-a-tasks-agent-work-without-asking-where-its-runtime-keeps-it-in-bounds.md),
[record](../validation/approval-levels.md)). Absent means not offered, and admission refuses
`never_ask` for such a runtime; each adapter refuses it again at the start. Codex 0.157.0 declares
it: its policy `never` with `workspace-write` and the reviewer `user`, every thread pinned to no
network and no writable folder but the project, refused while its home names an MCP server.
OpenCode declares it inside Halora's sandbox, where each folder's server writes that folder alone: its session rules allow shell commands and turn every other ask into a refusal. Claude Code and the ACP agent apps do not, and refuse it in words. Each adapter records in
`runtime.execution.started` the level the runtime confirmed: Codex from the policy it reports for
the thread, `never_ask` on a confined project whatever was chosen; OpenCode the level only when it
reports the session with exactly the rules it was given; Claude Code none.

The live adapter correlates an exact observed started item and turn before emitting
`runtime.tool.result` and then tool completion with monotonic sequences and distinct native event
IDs. Duplicate, foreign-turn or malformed overbound item/turn identifiers cannot fabricate a
correlation by clipping. Reconnect reconstruction or a completed turn does not invent missing
output. Command completion/exit and file-change completion are native fields; neither names nor
agent prose upgrade them into test/build evidence. File patches are native item fields, not a
current-tree diff or a complete source snapshot. Lexical path and storage bounds, held-value
replacement and owner-journal privacy are defined in [EVENTS.md](EVENTS.md) and
[ADR 0031](../decisions/0031-record-bounded-native-tool-results-in-the-owner-journal.md).

The pinned app-server's existing transport still receives unbounded raw frames before result
field admission. The 64 KiB field/payload limits are storage and read-contract limits, not a
claim that the RPC process or source capture is bounded.

## The runtime adapter contract

A runtime integration implements `RuntimeAdapter` (`packages/runtime-core/src/adapter.ts`):

- `descriptor`: runtime id, kind, display name, whether it is `synthetic`, its capabilities, and
  its `model_choice`: `listed` when the runtime lists the models it can run, `none` otherwise.
- `validateStartOptions(options, modelRef)`: checks runtime-specific start options, and the
  chosen model's reference, before anything is recorded. Options are opaque to the rest of the
  system, so no vendor option enters the core.
- `listModels()`: present exactly when `model_choice` is `listed`. Reads the runtime's own list at
  each call and maps each model to `RuntimeModel`: an opaque `model_ref`, a name that says what
  serves the model, where it is served (`this_mac`, `remote` or `unknown`, from where the runtime
  sends its requests, never from the model's name), whether it calls tools as the runtime states
  it, and its context. The control plane serves it read through at
  `GET /api/runtimes/:runtime_id/models` and never journals it
  ([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)).
- `startExecution`, `sendInstruction`, `respondToApproval`, `answerQuestion`, `interrupt`: each is
  present exactly
  when its capability is declared; registration refuses an inconsistent adapter. Each resolves
  when the runtime has confirmed the action and rejects with `RuntimeActionError` otherwise,
  stating whether the action may have taken effect anyway. `startExecution` checks a chosen
  model against a fresh list before anything runs and refuses one no longer listed with
  `model_unavailable`.
- Observations: the adapter reports what the runtime does as normalized `RuntimeObservation`s
  (the `runtime.*` events), with the native record id for deduplication, the native sequence when
  one exists, and honest provenance. It never throws into the sink and never invents state. A
  tool's title, an approval's summary, a question's texts, a test run's label and summary, and
  error text (a turn's failure, a lost connection's reason, a refusal's message) are reported
  whole, never cut: the control plane takes the secrets it holds out of them, which it finds only
  whole, then cuts them to the contract ([SECURITY.md](SECURITY.md), "Logs and error text"). The
  journal outlives the process, so a native record id is never reused, not even after a restart;
  the journal would drop the new record as a duplicate ([EVENTS.md](EVENTS.md)). The model the
  runtime says it runs on is reported as `runtime.model.used`, from the runtime's own report and
  never from the choice. A question the agent asks through the runtime's structured surface is
  reported as `runtime.question.asked`, whole, marked unanswerable when the adapter cannot carry
  an answer back faithfully or a prompt asks for a secret; the control plane fits it once with
  `fitQuestion`, after redaction, and marks it unanswerable when anything had to be cut. It is
  followed by `runtime.question.resolved` once the runtime
  took the answer or withdrew the question, unless the turn's end came first and withdrew it
  ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)). After
  `runtime.connection.lost`, an adapter that can observe the runtime again reports
  `runtime.connection.restored` before what changed meanwhile.
- `close()`.

## Capabilities

| Capability | Meaning |
| --- | --- |
| `start_execution` | Create an execution and run its first turn from an instruction |
| `instruct_at_rest` | Start a new turn on an execution whose last turn ended |
| `instruct_while_running` | Deliver an instruction while a turn runs. Without it, a runtime with `instruct_at_rest` has the instruction held by the host and sent after a turn that completed ([ADR 0056](../decisions/0056-an-instruction-sent-during-a-turn-waits-for-it-on-a-runtime-that-cannot-steer.md)); nothing is emulated in the runtime |
| `respond_to_approval` | Answer an approval the runtime raised |
| `answer_question` | Answer a question the agent asked through the runtime's own surface |
| `interrupt` | Stop the running turn; the execution remains |

Clients show only actions whose capability is true. Admission rejects the rest with
`capability_unsupported`; nothing is emulated.

**Why these and not more.** Each capability maps onto at least one verified runtime surface.
There is no `pause`: no verified runtime can suspend a turn and resume it later (Codex goals can be
paused between turns, which is not the same). There is no `cancel` distinct from `interrupt`:
every runtime stops the turn and keeps the session. Discovery of existing work, diffs, terminal
output and review requests are verified on some runtimes but will be added with the first adapter
that needs them, not before.

## Evidence snapshot

The adapter contract maps onto verified runtime surfaces. Vendor surfaces change; dated verification
details and sources live in `docs/internal/validation/` and must be re-verified whenever an adapter
is added or upgraded.

| Capability | OpenCode server | Claude Code Agent SDK | Codex app-server (experimental) | Codex exec / TS SDK (stable) |
| --- | --- | --- | --- | --- |
| start_execution | yes | yes | yes | yes |
| instruct_at_rest | yes | yes (resume) | yes | yes (resume) |
| instruct_while_running | v2 steer or queue, at the next step | queued (streaming input) | yes (`turn/steer`) | no |
| respond_to_approval | yes | yes (`canUseTool`) | yes (server requests) | no, approvals are rejected |
| interrupt | yes (abort) | yes (streaming input) | yes (`turn/interrupt`) | process signal only |
| answer_question | yes (form reply) | yes (`AskUserQuestion` through `canUseTool`) | yes (`item/tool/requestUserInput`), with an under-development feature switched on per thread | not checked |
| pause | no | no | no | no |

Records: [OpenCode](../validation/opencode-capabilities.md),
[Claude Code](../validation/claude-code-capabilities.md), [Codex](../validation/codex-capabilities.md),
[questions](../validation/agent-questions.md).

The column "Codex app-server (experimental)" is the vendor's label for that command in the check above, set beside exec, which that check called stable. [ADR 0011](../decisions/0011-codex-app-server-stable-surface.md) selects only the stable methods and refuses `experimentalApi`. The column does not mean the adapter opts into the experimental API ([record](../validation/codex-capabilities.md)).

## Integration status

| Integration | Status | Blocking question |
| --- | --- | --- |
| Mock runtime | Built. Synthetic, labeled everywhere. Its session ids derive from the execution id, so its native ids stay unique across control plane restarts; the sessions themselves do not survive one. A start without a scenario uses `simulated_start`, which says the request was received and no software work was performed; a named scenario still plays its own script. A scenario may script the turn an instruction starts, by its exact text; any other instruction gets a turn that says it performs no work. An instance may be given the name clients show, as the recorded demonstration's two are, and stays synthetic whatever its name. Given models, as the development control plane gives it three synthetic ones, it lists them and reports the chosen one as used; the recorded demonstration's runtimes offer no choice. Answers the scripted questions of `question_asked` and repeats the answers back, labeled simulated; the recorded demonstration's runtimes take no answers. | None |
| OpenCode | Built: `packages/integrations/opencode`, the v2 API pinned to `@opencode/cli` 2.0.18 on a server Halora launches and supervises ([ADR 0009](../decisions/0009-opencode-v2-pinned-and-launched-by-halcyonic.md)); end to end tests against the real binary and a fake provider. Offers a plan (`offers_plan`): a start with `plan` runs on OpenCode's built-in `plan` agent, which refuses the edit tools unasked while shell commands still ask, never with `never_ask`, recorded `plan: true` from the agent OpenCode reports; an instruction with `leave_plan` switches the same session to `build`, confirmed and recorded as `runtime.plan.left`, before it is sent ([record](../validation/agent-plan-modes.md)). Declares all six capabilities: instructions while a turn runs use OpenCode's `steer` delivery and reach the model when the running step ends. A question tool's form is reported as a question and answered with a form reply; a form it cannot answer faithfully is shown as unanswerable. A shell call OpenCode completes that did not exit 0 (a non-zero exit, a signal, a timeout in `metadata`) is reported as a failed tool call, even though OpenCode marks the call completed; a call moved to the background is reported when it moves and its later end is not journaled. After the event stream reconnects, the adapter reads each session back, retrying up to five-minute intervals if unreadable and reporting it restored once it answers. A server exit while the control plane runs marks its executions lost; after a control plane restart, sessions re-attach by launching the folder's server over the same store and taking the session only when OpenCode holds it in the same folder with the recorded permission rules and not running, reporting the cut turn interrupted (inferred) ([ADR 0046](../decisions/0046-a-restart-re-attaches-each-tasks-session-where-the-runtime-keeps-it.md)). Lists models from `GET /api/model` (never `/api/provider`), checks the chosen model in the execution's directory (waiting up to 10 s at start), sets `model_ref` to `provider/model` and `served` from the provider's base URL (`cloud`-tagged Ollama models are `remote`), and reports the model each step uses. Every session denies `execute`, `webfetch`, `websearch`, `subagent`, hidden paths and `opencode.json`, asks before shell commands, skips repository OpenCode configuration, and stops on external rule changes or foreign sessions ([record](../validation/opencode-permissions.md)). On macOS each execution folder runs on its own server inside Halora's Seatbelt sandbox (with a separate no-write server for model listing), reaching only its own port and Halora's loopback Ollama gate (`Origin`-free JSON chat/model reads only) ([ADR 0028](../decisions/0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md)). Accepts instruction pictures (`takes_pictures`) as `data:` files and refuses models OpenCode lists as text-only ([ADR 0058](../decisions/0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md), [record](../validation/picture-and-file-input.md)). Registered when `HALCYONIC_OPENCODE_BIN` is set; verified with three local Ollama models ([record](../validation/local-models.md)). | How the pinned binary is installed for users; whether sessions should carry permission rules that ask ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)) |
| Claude Code | Built: `packages/integrations/claude-code`, runtime kind `claude-agent` ("Claude Agent"), on the Agent SDK 0.3.283 with streaming input and a session id chosen at launch; API key or cloud provider authentication only. Loads no project settings (`CLAUDE.md`, `AGENTS.md`, `.claude` settings and hooks, `.mcp.json`), proven on the fake host ([record](../validation/claude-code-project-settings.md)). Offers a plan (`offers_plan`): a start with `plan` runs in Claude Code's `plan` permission mode, recorded from `system/init`; `leave_plan` calls `setPermissionMode('default')` and dispatches once `system/status` confirms it as `runtime.plan.left` ([record](../validation/agent-plan-modes.md)). In a starter project on macOS it runs inside Halora's closed Seatbelt profile with an isolated home and reaches its API through the adapter's loopback key gate (the gate holds the key and issues a per-run credential; recorded confined; [ADR 0048](../decisions/0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md), [record](../validation/claude-code-key-gate.md), [record](../validation/claude-code-sandbox.md)); on connected folders and cloud providers it runs under the lighter connected-folder profile (key-gated on Anthropic, Salidium and Seorak hooks passed explicitly, recorded not confined). Accepts instruction pictures (`takes_pictures`) as base64 image blocks ([ADR 0058](../decisions/0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md), [record](../validation/picture-and-file-input.md)). Registered when `HALCYONIC_CLAUDE_AGENT=1` or an Anthropic key is stored; verified against the real CLI ([record](../validation/claude-code-capabilities.md)). Lists models via `supportedModels()` in a short-lived process. Re-attaches after a control plane restart when `getSessionInfo` finds the session in the folder's store with matching options, model, folder, confinement and `ask` level, resuming via `--resume` ([ADR 0046](../decisions/0046-a-restart-re-attaches-each-tasks-session-where-the-runtime-keeps-it.md), [record](../validation/restart-reattach.md)). Reports `AskUserQuestion` as a structured question and dismisses unanswered questions when a turn ends or is stopped. | Attaching to terminal sessions |
| Codex | Built: `packages/integrations/codex`, runtime kind `codex` ("Codex 0.157.0"), on app-server's stable surface over stdio, pinned to `codex-cli` 0.157.0, on a server Halora launches and supervises ([ADR 0011](../decisions/0011-codex-app-server-stable-surface.md)): from a home of its own (`<data dir>/codex-home` or `<data dir>/codex-confined/<key>`), never the person's `~/.codex`, never signed in to ChatGPT, with plugins, the update check, analytics and web search off on every launch. Declares all six capabilities: instructions while a turn runs go through `turn/steer` and reach the model at its next request; questions arrive as `item/tool/requestUserInput` because every thread starts with the under-development feature `default_mode_request_user_input`, an exception to ADR 0011 that an end to end test guards and the adapter option `answerQuestions: false` withdraws ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)), secret questions are shown as unanswerable, and an answer Codex does not confirm within 10 s fails with an unknown effect and leaves the question open to another answer, and once more than one was sent, Codex's confirmation fails the last with an unknown effect too, because it does not say which it took; approve and deny are `accept` and `decline`; an interrupt ends the turn but not the commands it started. One execution is one thread, tagged `halcyonic`, whose id is the native id; a server that exits is relaunched and its threads resumed. After the control plane restarts, a thread is re-attached on the server its recorded folder chooses, refused where its recorded confinement differs from its folder's now (a confined thread never resumes on the open server), resumed with the settings it started with and the level it was recorded at, which Codex must report back as at a start, with the never-ask MCP check a resume makes; its cut turn is settled from `thread/turns/list` (inferred, `codex.restart.turn_status`), and its observations are numbered on from the journal's last ([ADR 0046](../decisions/0046-a-restart-re-attaches-each-tasks-session-where-the-runtime-keeps-it.md)). A start may name the model provider, the model, the context window and the compaction threshold: models served on loopback (Ollama, refusing Ollama's remote cloud tags) or, on a confined server, any declared hosted provider in `providers/<id>/provider.json` that serves the Responses wire (`CODEX_KEY_READER`, `halcyonic_<id>` behind a loopback key gate holding the key; [ADR 0061](../decisions/0061-a-hosted-provider-is-one-declaration-file-the-host-reads.md), [record](../validation/agent-apps-hosted-providers.md)); a thread Codex runs on another model or provider than asked is refused. `model_ref` is `provider/model`, and the model and provider Codex reports for the thread, or reroutes it to, are reported as used. End to end tests against the real binary and a fake provider ([record](../validation/codex-capabilities.md)); verified with three open models that Ollama serves on the Mac, through the control plane ([record](../validation/local-models.md)). The end to end suite's network probe, re-run on every Codex upgrade, watches the server's process tree through startup, idle and a full run on a local model. Released builds of Salidium (0.8.3 and older) and Seorak do not read Halora's Codex homes (`<data dir>/codex-home` and `<data dir>/codex-confined/<key>`) by default: for a Codex execution whose rollout is in either, Understand queries Salidium only when its version is newer than 0.8.3 (which reads `SALIDIUM_EXTRA_CODEX_HOMES`), returning `runtime_not_observed` otherwise; Checks queries Seorak (builds with `SEORAK_CODEX_EXTRA_DIRS` support tail those folders); a miss in either returns `own_sources_not_set_up`. On macOS, every execution's folder runs on a server of its own inside Halora's Seatbelt profile (a project made from a starter asks nothing; any other asks under `untrusted` when its start asks), its turns under Codex's `externalSandbox` policy, its commands' temporary files (`TMPDIR` and zsh's `TMPPREFIX`) in a folder of its own, made anew at each launch and removed at close ([ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md)). Takes an instruction's pictures (`takes_pictures`) as `image` items with a `data:` URL, in `turn/start` and `turn/steer`, which reach the provider as input images of the same bytes for a model outside Codex's catalog too; a picture Codex cannot decode it replaces with a line saying so, which is why the host refuses one not whole ([ADR 0058](../decisions/0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md), [record](../validation/picture-and-file-input.md)). Registered when `HALCYONIC_CODEX_BIN` is set. | How the pinned binary is installed for users; whether a ChatGPT sign-in may drive it; releasing Salidium and Seorak support for Halora's Codex homes ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)) |
| Antigravity, Kiro and Cursor | Built: `packages/integrations/acp`, one adapter for every agent app that speaks the Agent Client Protocol version 1 over stdio, a profile per app ([ADR 0067](../decisions/0067-agent-apps-that-speak-acp-are-directed-through-one-adapter.md)): Antigravity's `agy acp` pinned to 1.5.10 (kind `antigravity`, "Antigravity 1.5.10"), Kiro's `kiro-cli acp --agent-engine v3` pinned to 3.4.0 (kind `kiro`, "Kiro 3.4.0", its version from its own `--version` once signed in) and Cursor's `cursor-agent acp` pinned to 2026.11.13 (kind `cursor`, "Cursor Agent 2026.11.13", its version from `--version`); an app reporting another version is refused before a session opens. Their headless modes refuse every ask unseen, so ACP is the only surface that can ask the person. One app process per execution, in the task's folder (on macOS its own copy of the project, [ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)), checked again right before `session/new`; the session id is the native id. Declares `start_execution`, `instruct_at_rest`, `respond_to_approval` and `interrupt` (`session/cancel`, confirmed by the prompt's `cancelled` answer), nothing else: no steer, no questions, no `never_ask`, no re-attach. A model is chosen from the app's own list (Antigravity's and Cursor's `session/new` `model` option, Kiro's `chat --list-models --format json`), set first at the start; then the asking mode and held options (Kiro's `autopilot` off and `contentCollection` disabled), each read back before the task runs; a session that leaves its asking mode or a held option has its turn stopped; the model is set and read back at the start only. Usage only where the app reports tokens: Antigravity's prompt answer (`reports_usage`); Kiro gives credits only and Cursor nothing, so neither reports. An approval is answered with the app's once-only option, never an `always` one, and counted taken only when the call next shows it (running or ended for an approval; `failed` for a denial, or `completed` for Cursor, whose refused call never reads as a success), else it fails after 15 s with an unknown effect; an ask whose input changes before the answer is refused. A command (Antigravity's `run_command`, Kiro's and Cursor's `execute`) is carried in its parts from the call's own command field: Antigravity's said to run in the task's folder or in the folder its own `Cwd` names, Kiro's and Cursor's where unknown; any other request reads "Use <tool>" then each input on a line, and carries each input literally (`inputs`); the host writes its own marks in all of it (`‹U+XXXX›`, `⇥`, `↵`, [ADR 0065](../decisions/0065-a-request-too-long-to-show-whole-is-denied-by-the-host-with-its-reason.md)). A file request is complete only with its change carried whole (a new file's text, Antigravity's `edit_file` at every occurrence, Kiro's `str_replace` from its input, and Kiro's turn-level review of its staged changes). The app signs in its own way on the Mac and is never given a key: its own sign-in and endpoint variables are taken out of every launch, and it is launched with a settings home of Halora's own (`--gemini-dir`, `KIRO_HOME`, `CURSOR_CONFIG_DIR` with an allowlist that allows nothing), fresh for each launch and removed at its end (a crashed launch's swept at the next), every other one refused to it. On macOS it runs under the agent-app profile, never reading, writing or running in the host's own folders, the person's push and cloud credentials or the project's own settings for the app, recorded not confined with words why; in a task's copy, the copy alone is written and every other task's copy refused. Each app is recorded in the data folder and watched by a detached watchdog once it answers `initialize`, its whole process group stopped with it, so none outlives the control plane. Registered by `HALCYONIC_ANTIGRAVITY_BIN`, `HALCYONIC_KIRO_BIN` or `HALCYONIC_CURSOR_BIN` in the environment, or by the person's own `Turn On` on the setup page, which installs the vendor's archive under `<data dir>/runtimes/` checked by its sum first (Cursor's whole unpacked `dist-package` folder pinned; each copy's own program folder only run and read); never by a settings file. Tested against Antigravity (stand-in Gemini API and signed in), Kiro (signed in and signed out) and Cursor (bounded signed-in runs and signed out) ([record](../validation/acp-agents.md)). | Stopping a Cursor turn, and what Cursor does after a refused question or plan; Kiro's `fs_write` over a file already there (deny-only); Kiro and Cursor in a task's copy ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)) |
| Salidium | Built: a client for Salidium's versioned, read-only consumer contract v1, including optional 1.1 revision anchors and repository paths; exercised against released daemons ([record](../validation/salidium-consumer-contract.md)); served per execution at `GET /api/executions/:execution_id/understanding`, and its comprehension brief (consumer contract 1.2, served when the running daemon advertises minor 2 or newer) at `GET /api/executions/:execution_id/brief`. Observes Claude Code and Codex sessions, plus OpenCode sessions where a running Salidium lists its `salidium/opencode` provider; sessions kept in Halora's sandboxed OpenCode store (`<data dir>/opencode-sandbox/data/opencode/opencode.db`, where every OpenCode session Halora starts on macOS lives) require a Salidium newer than 0.8.3 configured with `SALIDIUM_EXTRA_OPENCODE_STORES` (0.8.3 and older answer `runtime_not_observed` from version metadata alone; newer versions answer `own_sources_not_set_up` on a miss). Salidium's source also implements experimental project-map and execution-links v0, which Halora does not consume ([source validation](../validation/salidium-project-map.md)). | A map connection must preserve Halora's Project folder boundary as well as Salidium's repository opt-in; execution-links v0 has no repository filter; whether Salidium publishes comprehension-brief deltas to consumers ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)) |
| Seorak | Built: a client for Seorak's versioned, read-only integration API v1 on its local plane, which `seorak` 0.3.0 serves, written from the published `@seorak/types` 0.2.0. It resolves the runtime's own session id to Seorak's session, which answers the correlation question and gives the estimated cost and, when Seorak reports all of it, the session's activity (start, end, elapsed seconds, tool calls, prompts; no token counts, which v1 does not give), then reads the outcome, verification, tool-mix and cadence lenses; verified against the running plane ([record](../validation/seorak-integration-api.md)); served per execution at `GET /api/executions/:execution_id/evaluation`. An evaluation costs five of the credential's 60 requests a minute (at most 12 evaluations a minute), so clients fetch it on demand and never poll. Also reads, account wide, the provider usage limits Seorak last observed (`GET /api/v1/usage-limits`, scope `limits:read`), served at `GET /api/usage-limits` for the headset's `Usage Left` glance; tested against a stub and verified against a pre-release Seorak build ([record](../validation/seorak-integration-api.md#provider-usage-limits-2026-09-30)). | Verification, tool-mix and cadence lens rows, 403 and 429 are not yet observed live against a released plane. The pre-release usage-limits check does not establish released daemon behavior or headset rendering; account identity remains unavailable |
| Omnorium | The control plane's `GET /api/catalog` and the recorded demonstration's stand-in, invented projects marked synthetic, are built; so is `packages/integrations/omnorium`, a client for Omnorium's published catalog contract v1 (`omnorium.catalog` v1, with dual support for `~/.phat-porcupine`, `phat-porcupine.catalog` and `ppc_` credentials), tested against a stand-in and against its published fixtures ([record](../validation/omnorium-catalog.md)). The control plane reads it on request, with the credential in `<data dir>/omnorium-credential`; without that file the gallery is told none is set up. It suggests folders by their own git origin, and admits a link only to a repository the catalog it last read holds. Not yet read live. The projects gallery's catalog is read through and never journaled ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)) | Whether the real collection lives in a local or a hosted instance ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)) |

## Salidium and Seorak boundary

- Halora owns Projects, Workstreams, Executions, Commands and runtime control
  ([ADR 0007](../decisions/0007-agent-control-belongs-to-halcyonic.md)). Salidium and Seorak own
  understanding and performance and stay observational; Halora displays their conclusions with
  their provenance and never restates them as its own facts.
- The dependency runs one way: Halora depends on their published, versioned, read-only
  contracts, and neither gains Halora-specific features. Sessions are correlated through the
  neutral key every orchestrator already has: the runtime kind and the runtime's own session id.
- Halora never reads their databases, never imports their private packages, and never calls
  their write endpoints.
- They are optional. When one is absent, paused or incompatible, the relevant surface says
  unavailable.
- Omnorium, the owner's GitHub portfolio app,
  supplies the projects gallery's catalog the same way: a versioned, read-only, loopback contract
  (`omnorium.catalog` v1, with dual support for `phat-porcupine.catalog` v1) with a credential the
  person creates, read through and never journaled, everything in it `reported` by the catalog
  source. Halora reads it and never writes to it, and never signs in to GitHub itself
  ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md),
  [record](../validation/omnorium-catalog.md)). Built and tested against the stand-in and the
  published fixtures; not yet read live.
- Their conclusions are read through on request and never journaled; they keep the provider's own
  epistemic classes and never feed status or attention
  ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)).
- Every answer says whether a stand-in produced it (`source.synthetic`). Only the recorded
  demonstration's stand-ins do: the integrations' test doubles, speaking the products' contracts
  with invented content, read through the control plane's routes and marked synthetic, so no
  surface takes them for Salidium's or Seorak's
  ([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md)).
- Both already observe Claude Code and Codex sessions on the machine, including ones Halora
  starts. Link rather than merge: keep each execution's `native_id` so their records can be
  correlated.

## Additional reads and their limits

- **Provider usage left.** Halora's client and route are built against Seorak's account-wide
  usage-limits contract and verified against a pre-release build
  ([record](../validation/seorak-integration-api.md#provider-usage-limits-2026-09-30)); released
  service availability and live headset rendering are not established by that record. Without the
  source, the headset says `Usage Left` is not set up. Each reading states the agent, window,
  used percentage, reset, observation time and freshness; Seorak refuses it to a project- or
  date-restricted credential, and Halora reads it only when the person opens `Usage Left`. Claude
  Code yields no reading: its token counts have no limit denominator, so Halora shows no
  Claude percentage. Seorak has no account or profile identity in a reading, so Halora marks
  every reading `unidentified` and never associates it with a selected model or signed-in
  account.
- **Committed repository structure.** Salidium's consumer contract v1 explains and reports one
  execution; its separate, experimental `salidium.project-map` v0 maps one committed repository:
  files, declared modules, JavaScript and TypeScript imports, and C# assembly/project membership.
  Edges carry provenance, a resolution rule and bounded file/line evidence. Coverage names what
  was omitted or not analyzed; absent edges do not prove absent behavior. Components,
  responsibilities, data flow and uncommitted contents are outside the contract. The separate
  `salidium.execution-links` v0 links recorded changes to exact paths at observed revisions,
  without semantic or guaranteed change-impact claims. Halora consumes neither surface; the
  contract is source-verified without a live map request or headset render
  ([record](../validation/salidium-project-map.md)).
- **Map access.** A map request names an exact Salidium-opted-in main repository root and full
  commit id. That opt-in does not bind the repository to a Halora Project. Execution links can
  request maps of every repository recorded in a session; their handler ignores query parameters,
  so filtering the result after reading it cannot constrain those producer reads to one Project.
  Halora's Understanding drops host repository roots and retains repository-relative
  paths: those paths alone cannot establish a cross-repository change overlay. A future consumer
  must validate access and repository identity on the host before joining or exposing evidence.

None of these reads is a runtime capability, a journal event or a source of Workstream status.

## Rules for a new adapter

1. Verify the runtime's current official surface and write a validation record, including a
   runtime smoke test, before writing the adapter.
2. Use official structured surfaces. Never scrape a terminal UI.
3. Never install global hooks or change the user's runtime configuration to gain control;
   control executions through the runtime's own API.
4. Declare only verified capabilities. Map native states to normalized events only where the
   mapping is defensible; otherwise emit nothing and let the status be `unknown`.
5. Keep native ids as opaque references; never expose vendor objects through Halora APIs.
   Prefer choosing the native id at launch where the runtime allows it (Claude Code's
   `--session-id`), so the execution can be correlated before the runtime reports anything.
6. Launch agents with an explicitly built environment. Leave the runtime's home and
   configuration directories at the developer's defaults so Salidium and Seorak can observe the
   sessions, and never pass `SALIDIUM_INTERNAL` to a launched agent: it makes Salidium drop the
   session's hooks.
7. Record fixtures from real runs, sanitized, for contract tests.
8. Take the working directory from the project, never from start options. Declare
   `uses_project_location` for a runtime whose agents work in a folder; the control plane then
   refuses a start in a project without one and passes the project's folder, a real path the host
   resolved, as `StartExecutionRequest.directory`. Call `confirmProjectLocation` with the host's
   `DirectoryPolicy` (`packages/runtime-core/src/adapter.ts`) before launching anything, so a
   folder that has gone, left the roots or now leads elsewhere through a symbolic link fails the
   start with effect `none`; call it again right before handing the folder to the runtime, after
   any wait such as a server launch or a model listing, and before sending it again on a resume;
   and run the agent there and nowhere else. Where the runtime reports the folder a session works
   in, refuse a session it reports elsewhere. The control plane allows
   only folders under `HALCYONIC_PROJECT_ROOTS`, so a client cannot point an agent anywhere else on
   the machine ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)).
9. Make sure an agent process cannot outlive the control plane unsupervised: stop it on close and on
   the control plane's exit, and say plainly in the validation record what a hard kill leaves
   running.
10. Declare `reports_tool_activity` only when the adapter reports every tool call the runtime
    makes as `runtime.tool.started` and `runtime.tool.completed`: the execution view then reads no
    open call as no tool running, which is otherwise `unknown` ([EVENTS.md](EVENTS.md)).
11. Declare `model_choice: 'listed'` only for a runtime that lists its own models. Read the list
    field by field, so a provider's settings, keys and headers never reach it, and say where a
    model is served from the address the runtime sends it to, never from its name: a local model
    may carry a hosted model's name.
12. Declare `approval_may_leave_sandbox` only once verified, by a runtime test of an approved
    command against the pinned version: true when an approved command can run outside any sandbox,
    false only when every approved command still runs inside one. Leave it absent otherwise; absent
    is unknown, and a client never shows it as staying inside.
13. Declare `reports_usage` only once the runtime's own types and a run of the pinned version show
    where its counts come from, and report a turn's `runtime.usage.reported` from them alone: map
    the runtime's parts onto the contract's, which never overlap, leave a part null where it is not
    given, and report nothing for a turn the adapter did not see whole (a resumed session's first
    turn, a stream with a gap). Give a cost only where the runtime estimates one, by the kind of
    prices it names; never compute one. Claude Code, OpenCode, Codex and Antigravity declare it
    ([agent-usage.md](../validation/agent-usage.md), [acp-agents.md](../validation/acp-agents.md)).
14. Declare `offers_spending_cap` only once a run of the pinned version shows the runtime's own stop at a limit, and
    hand it the allowance the request carries at every launch: the start's limit, a re-attach's
    `spending_allowance_usd` (null: launch nothing until Carry On), Carry On's raise. End the stopped turn
    `spending_cap_reached`; never stop a task on the host's own sums (ADR 0062). Claude Agent declares it
    ([spending-cap.md](../validation/spending-cap.md)).
