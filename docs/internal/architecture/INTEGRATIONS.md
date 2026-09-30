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
- `startExecution`, `sendInstruction`, `respondToApproval`, `interrupt`: each is present exactly
  when its capability is declared; registration refuses an inconsistent adapter. Each resolves
  when the runtime has confirmed the action and rejects with `RuntimeActionError` otherwise,
  stating whether the action may have taken effect anyway. `startExecution` checks a chosen
  model against a fresh list before anything runs and refuses one no longer listed with
  `model_unavailable`.
- Observations: the adapter reports what the runtime does as normalized `RuntimeObservation`s
  (the `runtime.*` events), with the native record id for deduplication, the native sequence when
  one exists, and honest provenance. It never throws into the sink and never invents state. The
  journal outlives the process, so a native record id is never reused, not even after a restart;
  the journal would drop the new record as a duplicate ([EVENTS.md](EVENTS.md)). The model the
  runtime says it runs on is reported as `runtime.model.used`, from the runtime's own report and
  never from the choice.
- `close()`.

## Capabilities

| Capability | Meaning |
| --- | --- |
| `start_execution` | Create an execution and run its first turn from an instruction |
| `instruct_at_rest` | Start a new turn on an execution whose last turn ended |
| `instruct_while_running` | Deliver an instruction while a turn runs |
| `respond_to_approval` | Answer an approval the runtime raised |
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
| pause | no | no | no | no |

Records: [OpenCode](../validation/opencode-capabilities.md),
[Claude Code](../validation/claude-code-capabilities.md), [Codex](../validation/codex-capabilities.md).

## Integration status

| Integration | Status | Blocking question |
| --- | --- | --- |
| Mock runtime | Built. Synthetic, labeled everywhere. Its session ids derive from the execution id, so its native ids stay unique across control plane restarts; the sessions themselves do not survive one. A start without a scenario uses `simulated_start`, which says the request was received and no software work was performed; a named scenario still plays its own script. A scenario may script the turn an instruction starts, by its exact text; any other instruction gets a turn that says it performs no work. An instance may be given the name clients show, as the recorded demonstration's two are, and stays synthetic whatever its name. Given models, as the development control plane gives it three synthetic ones, it lists them and reports the chosen one as used; the recorded demonstration's runtimes offer no choice. | None |
| OpenCode | Built: `packages/integrations/opencode`, the v2 API pinned to `@opencode/cli` 2.0.18 on a server Halcyonic launches and supervises ([ADR 0009](../decisions/0009-opencode-v2-pinned-and-launched-by-halcyonic.md)); end to end tests against the real binary and a fake provider. Declares all five capabilities: instructions while a turn runs use OpenCode's `steer` delivery and reach the model when the running step ends. A start that names a model waits up to 10 s for OpenCode to list it in the execution's directory, and is refused in words when it never is. Lists its models from `GET /api/model` at the server's own location, so as OpenCode's global configuration has them, never from `/api/provider`, which carries settings and headers; a start checks the chosen model against the list for its own directory, whose configuration may disable it. `model_ref` is `provider/model`, `served` follows the provider's base URL (an Ollama model whose tag ends in `cloud` is remote), and the model each step runs on is reported as used. Registered when `HALCYONIC_OPENCODE_BIN` is set. Verified with three open models that Ollama serves on the Mac, through the control plane ([record](../validation/local-models.md)). | How the pinned binary is installed for users; whether sessions should carry permission rules that ask ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)) |
| Claude Code | Built: `packages/integrations/claude-code`, runtime kind `claude-agent` ("Claude Agent"), on the Agent SDK 0.3.283 with streaming input and a session id chosen at launch; API key or cloud provider authentication only. Registered when `HALCYONIC_CLAUDE_AGENT=1`. Verified against the real CLI, without a model and then with one: approval round trip, second turn, interrupt ([record](../validation/claude-code-capabilities.md)). Lists its models with the SDK's `supportedModels()` through a short-lived Claude Code process, served remotely by Anthropic or the selected cloud provider, or, with a gateway passed on purpose, named for the gateway and served where its address is, and reports the model the session starts on; verified only against the SDK's types and a fake, because listing may reach Anthropic with the key. | Attaching to terminal sessions |
| Codex | Built: `packages/integrations/codex`, runtime kind `codex` ("Codex 0.157.0"), on app-server's stable surface over stdio, pinned to `codex-cli` 0.157.0, on a server Halcyonic launches and supervises ([ADR 0011](../decisions/0011-codex-app-server-stable-surface.md)). Declares all five capabilities: instructions while a turn runs go through `turn/steer` and reach the model at its next request; approve and deny are `accept` and `decline`; an interrupt ends the turn but not the commands it started. One execution is one thread, tagged `halcyonic`, whose id is the native id; a server that exits is relaunched and its threads resumed. A start may name the model provider (`ollama` for a local model), the model, the context window and the compaction threshold, and a thread Codex runs on another model or provider than asked is refused. Lists the model its configuration names under the configured provider, from `config/read`, and the `model/list` catalog only when that catalog is the provider's (the built-in one is OpenAI's); `model_ref` is `provider/model`, and the model and provider Codex reports for the thread, or reroutes it to, are reported as used. End to end tests against the real binary and a fake provider ([record](../validation/codex-capabilities.md)); verified with three open models that Ollama serves on the Mac, through the control plane ([record](../validation/local-models.md)). Registered when `HALCYONIC_CODEX_BIN` is set. | How the pinned binary is installed for users; whether a ChatGPT sign-in may drive it ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)) |
| Salidium | Built: a client for Salidium's versioned, read-only consumer contract v1, which `salidium@0.6.0` serves; exercised against the release candidate and the released daemon ([record](../validation/salidium-consumer-contract.md)); served per execution at `GET /api/executions/:execution_id/understanding`. | None |
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
10. Declare `model_choice: 'listed'` only for a runtime that lists its own models. Read the list
    field by field, so a provider's settings, keys and headers never reach it, and say where a
    model is served from the address the runtime sends it to, never from its name: a local model
    may carry a hosted model's name.
