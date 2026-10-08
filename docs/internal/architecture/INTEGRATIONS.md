# Integrations

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
| `instruct_while_running` | Deliver an instruction while a turn runs |
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

The contract was shaped by the surfaces verified on 2026-09-26. Vendor facts change: the dated
details and sources are in the validation records, and they must be re-verified before an adapter
is built.

| Capability | OpenCode server | Claude Code Agent SDK | Codex app-server (experimental) | Codex exec / TS SDK (stable) |
| --- | --- | --- | --- | --- |
| start_execution | yes | yes | yes | yes |
| instruct_at_rest | yes | yes (resume) | yes | yes (resume) |
| instruct_while_running | v2 steer or queue, at the next step | queued (streaming input) | yes (`turn/steer`) | no |
| respond_to_approval | yes | yes (`canUseTool`) | yes (server requests) | no, approvals are rejected |
| interrupt | yes (abort) | yes (streaming input) | yes (`turn/interrupt`) | process signal only |
| answer_question (2026-10-01) | yes (form reply) | yes (`AskUserQuestion` through `canUseTool`) | yes (`item/tool/requestUserInput`), with an under-development feature switched on per thread | not checked |
| pause | no | no | no | no |

Records: [OpenCode](../validation/opencode-capabilities.md),
[Claude Code](../validation/claude-code-capabilities.md), [Codex](../validation/codex-capabilities.md),
[questions](../validation/agent-questions.md).

## Integration status

| Integration | Status | Blocking question |
| --- | --- | --- |
| Mock runtime | Built. Synthetic, labeled everywhere. Its session ids derive from the execution id, so its native ids stay unique across control plane restarts; the sessions themselves do not survive one. A start without a scenario uses `simulated_start`, which says the request was received and no software work was performed; a named scenario still plays its own script. A scenario may script the turn an instruction starts, by its exact text; any other instruction gets a turn that says it performs no work. An instance may be given the name clients show, as the recorded demonstration's two are, and stays synthetic whatever its name. Given models, as the development control plane gives it three synthetic ones, it lists them and reports the chosen one as used; the recorded demonstration's runtimes offer no choice. Answers the scripted questions of `question_asked` and repeats the answers back, labeled simulated; the recorded demonstration's runtimes take no answers. | None |
| OpenCode | Built: `packages/integrations/opencode`, the v2 API pinned to `@opencode/cli` 2.0.18 on a server Halcyonic launches and supervises ([ADR 0009](../decisions/0009-opencode-v2-pinned-and-launched-by-halcyonic.md)); end to end tests against the real binary and a fake provider. Declares all six capabilities: instructions while a turn runs use OpenCode's `steer` delivery and reach the model when the running step ends. A question tool's form is reported as a question and answered with a form reply; a form it cannot answer faithfully is shown as unanswerable. After the event stream reconnects it reads each session back, again after a failed read; a session it still cannot read while the server runs is reported lost but read again, with growing delays up to five minutes, and reported restored once it answers. A server that exits loses its sessions for good. A start that names a model waits up to 10 s for OpenCode to list it in the execution's directory, and is refused in words when it never is. Lists its models from `GET /api/model` at the server's own location, so as OpenCode's global configuration has them, never from `/api/provider`, which carries settings and headers; a start checks the chosen model against the list for its own directory, whose configuration may disable it. `model_ref` is `provider/model`, `served` follows the provider's base URL (an Ollama model whose tag ends in `cloud` is remote), and the model each step runs on is reported as used. Every session it creates denies `execute` (Code Mode), `webfetch`, `websearch` and `subagent`, and edits to every hidden path and to `opencode.json`, and asks before shell commands (a safeguard, not a boundary: the ask depends on OpenCode's parse of the command), which outrank the person's own rules; its server never loads the project's own OpenCode configuration, plugins or MCP servers; a task stops when something else changes its rules or approves its requests, and the server stops when a session it did not open appears; an end to end probe, with positive and negative controls, checks nothing leaves loopback in the scenarios it runs ([record](../validation/opencode-permissions.md)). Not local-only: the ask before a shell command depends on OpenCode's parse of it. Registered when `HALCYONIC_OPENCODE_BIN` is set. Verified with three open models that Ollama serves on the Mac, through the control plane ([record](../validation/local-models.md)). | How the pinned binary is installed for users; whether sessions should carry permission rules that ask ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)) |
| Claude Code | Built: `packages/integrations/claude-code`, runtime kind `claude-agent` ("Claude Agent"), on the Agent SDK 0.3.283 with streaming input and a session id chosen at launch; API key or cloud provider authentication only. Registered when `HALCYONIC_CLAUDE_AGENT=1`. Verified against the real CLI, without a model and then with one: approval round trip, second turn, interrupt ([record](../validation/claude-code-capabilities.md)). Lists its models with the SDK's `supportedModels()` through a short-lived Claude Code process, served remotely by Anthropic or the selected cloud provider, or, with a gateway passed on purpose, named for the gateway and served where its address is, and reports the model the session starts on; verified only against the SDK's types and a fake, because listing may reach Anthropic with the key. `AskUserQuestion` is reported as a question, never an approval, and answered in the tool's input, keyed by each question's text; one still waiting when a turn ends, or asked outside a turn when the person stops the work, is denied and reported dismissed, so no agent waits on a question the person can no longer see. | Attaching to terminal sessions |
| Codex | Built: `packages/integrations/codex`, runtime kind `codex` ("Codex 0.157.0"), on app-server's stable surface over stdio, pinned to `codex-cli` 0.157.0, on a server Halcyonic launches and supervises ([ADR 0011](../decisions/0011-codex-app-server-stable-surface.md)), only on models served on this Mac: from a home of its own, `<data dir>/codex-home`, never the person's `~/.codex`, never signed in, with plugins, the update check, analytics and web search off on every launch (note of 2026-10-07). Declares all six capabilities: instructions while a turn runs go through `turn/steer` and reach the model at its next request; questions arrive as `item/tool/requestUserInput` because every thread starts with the under-development feature `default_mode_request_user_input`, an exception to ADR 0011 that an end to end test guards and the adapter option `answerQuestions: false` withdraws ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)), secret questions are shown as unanswerable, and an answer Codex does not confirm within 10 s fails with an unknown effect and leaves the question open to another answer, and once more than one was sent, Codex's confirmation fails the last with an unknown effect too, since it does not say which it took; approve and deny are `accept` and `decline`; an interrupt ends the turn but not the commands it started. One execution is one thread, tagged `halcyonic`, whose id is the native id; a server that exits is relaunched and its threads resumed. A start may name the model provider, the model, the context window and the compaction threshold; the provider must serve its models from a loopback address (Ollama in practice), a model Ollama runs on its own remote service is refused, and a thread Codex runs on another model or provider than asked is refused. Lists the model its configuration names under the configured provider, from `config/read`, and the `model/list` catalog only when that catalog is the provider's (the built-in one is OpenAI's), keeping only models served on this Mac; `model_ref` is `provider/model`, and the model and provider Codex reports for the thread, or reroutes it to, are reported as used. End to end tests against the real binary and a fake provider ([record](../validation/codex-capabilities.md)); verified with three open models that Ollama serves on the Mac, through the control plane ([record](../validation/local-models.md)). The end to end suite's network probe, re-run on every Codex upgrade, watches the server's process tree through startup, idle and a full run on a local model. Neither Salidium nor Seorak reads Halcyonic's Codex home, so Understand and Checks answer `runtime_not_observed` for a Codex execution whose rollout is there. Registered when `HALCYONIC_CODEX_BIN` is set. | How the pinned binary is installed for users; whether a ChatGPT sign-in may drive it, and until then no hosted model; whether Salidium and Seorak should read Halcyonic's Codex home ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)) |
| Salidium | Built: a client for Salidium's versioned, read-only consumer contract v1, which `salidium@0.6.0` serves; exercised against the release candidate and the released daemon ([record](../validation/salidium-consumer-contract.md)); served per execution at `GET /api/executions/:execution_id/understanding`. Claude Code and Codex sessions; OpenCode sessions only where a running Salidium lists its `salidium/opencode` provider (contract 1.1, unreleased). | None |
| Seorak | Built: a client for Seorak's versioned, read-only integration API v1 on its local plane, which `seorak` 0.3.0 serves, written from the published `@seorak/types` 0.2.0. It resolves the runtime's own session id to Seorak's session, which answers the correlation question, then reads the estimated cost, outcome and verification lens; verified against the running plane ([record](../validation/seorak-integration-api.md)); served per execution at `GET /api/executions/:execution_id/evaluation`. An evaluation costs three of the credential's 60 requests a minute, so clients fetch it on demand and never poll. Also reads, account wide, the provider usage limits Seorak last observed (`GET /api/v1/usage-limits`, scope `limits:read`), served at `GET /api/usage-limits` for the headset's Usage left glance; tested against a stub Seorak only ([record](../validation/seorak-integration-api.md#provider-usage-limits-2026-09-30)). | Verification lens rows, 403 and 429 are documented in Seorak's ADR 007 but not yet observed live. The usage limits read is not observed live: Seorak's read is unreleased, and the owner still has to merge it, restart the daemon and issue a credential with `limits:read` |

## Salidium and Seorak boundary

- Halcyonic owns Projects, Workstreams, Executions, Commands and runtime control
  ([ADR 0007](../decisions/0007-agent-control-belongs-to-halcyonic.md)). Salidium and Seorak own
  understanding and performance and stay observational; Halcyonic displays their conclusions with
  their provenance and never restates them as its own facts.
- The dependency runs one way: Halcyonic depends on their published, versioned, read-only
  contracts, and neither gains Halcyonic-specific features. Sessions are correlated through the
  neutral key every orchestrator already has: the runtime kind and the runtime's own session id.
- Halcyonic never reads their databases, never imports their private packages, and never calls
  their write endpoints.
- They are optional. When one is absent, paused or incompatible, the relevant surface says
  unavailable.
- Their conclusions are read through on request and never journaled; they keep the provider's own
  epistemic classes and never feed status or attention
  ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)).
- Every answer says whether a stand-in produced it (`source.synthetic`). Only the recorded
  demonstration's stand-ins do: the integrations' test doubles, speaking the products' contracts
  with invented content, read through the control plane's routes and marked synthetic, so no
  surface takes them for Salidium's or Seorak's
  ([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md)).
- Both already observe Claude Code and Codex sessions on the machine, including ones Halcyonic
  starts. Link rather than merge: keep each execution's `native_id` so their records can be
  correlated.

## Requested reads that do not exist yet

- **Provider usage left.** Halcyonic's client and route are built against Seorak's account-wide
  usage limits read as Seorak's team specified it on 2026-09-30, which Seorak has not released;
  until it runs, the headset says Usage left isn't set up. Each reading states the agent, window,
  used percentage, reset, observation time and freshness; Seorak refuses it to a project- or
  date-restricted credential, and Halcyonic reads it only when the person opens Usage left. Claude
  Code yields no reading: its token counts have no limit denominator, so Halcyonic shows no
  Claude percentage. Seorak has no account or profile identity in a reading, so Halcyonic marks
  every reading `unidentified` and never associates it with a selected model or signed-in
  account.
- **Codebase architecture.** Salidium's released consumer contract v1 explains and reports one
  execution. It has no project-wide graph. A future codebase map belongs to Salidium only after
  validating repository nodes, links and evidence at a pinned revision; Halcyonic would render
  that versioned read without making its own parallel index.

Neither read is a runtime capability, a journal event or a source of Workstream status.

## Rules for a new adapter

1. Verify the runtime's current official surface and write a validation record, including a
   runtime smoke test, before writing the adapter.
2. Use official structured surfaces. Never scrape a terminal UI.
3. Never install global hooks or change the user's runtime configuration to gain control;
   control executions through the runtime's own API.
4. Declare only verified capabilities. Map native states to normalized events only where the
   mapping is defensible; otherwise emit nothing and let the status be `unknown`.
5. Keep native ids as opaque references; never expose vendor objects through Halcyonic APIs.
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
