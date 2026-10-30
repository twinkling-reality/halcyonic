# Events and the journal

## The journal

Every fact Halcyonic knows is an event in an append-only journal (SQLite, one file per control
plane). Events are never changed or deleted. Current state is the projection of the journal, so
history, replay, debugging and audit all come from the same record.

- **Position.** The journal assigns each event an increasing position. Positions may have gaps;
  clients track the last position they applied.
- **Idempotency.** An event is journaled once per `event_id`, and a runtime record is journaled
  once per `(source, source_native_id)`, so a runtime record delivered twice is not duplicated.
  A runtime's native ids must therefore stay unique for as long as the journal lives, across
  control plane restarts: the journal drops a new record whose id it already holds.
- **Reused native ids.** A dropped record whose execution and event type match the stored one is
  a re-delivery and is logged at debug level. Anything else means the runtime reused an id for a
  different record, which is lost: the control plane logs a warning with both events' ids,
  executions and types and the stored event's position, never payload text.
- **Identity.** Each journal has a `journal_id` and an `origin`: `live` for a running control plane,
  `fixture` for one loaded from a trace. Clients show fixture data as such.
- **Durability.** WAL mode with `synchronous = FULL`; files are readable by their owner only.
- **Validation.** Events are validated when written and again when read back.

## Envelope

| Field | Meaning |
| --- | --- |
| `schema_version` | Envelope version, currently `1` |
| `event_id` | UUIDv7, assigned by the control plane |
| `event_type` | See the catalog below |
| `project_id`, `workstream_id`, `execution_id` | Scope. Null where not applicable, never invented. |
| `source` | `{kind: 'control_plane'}` or `{kind: 'runtime', runtime_id}` |
| `source_native_id` | Id of the native record, for deduplication and traceability |
| `sequence` | Source-supplied order within an execution, when the source has one |
| `occurred_at` | When it happened, by the observing source's clock (UTC, `...sss Z`) |
| `ingested_at` | When the control plane journaled it |
| `correlation_id` | The command that started this chain of work |
| `causation_id` | The command or event that directly caused this one |
| `provenance` | How the fact is known; see below |
| `payload` | Type-specific data |

Ordering is by journal position. `occurred_at` is informative only, because clocks differ and
runtimes may deliver late; the projection ignores runtime events whose `sequence` is not after the
last one it applied for that execution.

## Provenance

Every event says how its fact is known, using the same classes as Salidium:

- `observed`: read from structured runtime or control plane state.
- `reported`: a claim in agent or user text. Agent messages are always `reported`; the contract
  rejects anything else. A claim never becomes an observation.
- `inferred`: derived by a named deterministic rule, which the event records.

Assessments from other systems (Salidium, Seorak) are not events: they are read through on
request and never journaled (see [What is not journaled](#what-is-not-journaled)).

## Catalog

| Event | Scope | Source | Meaning |
| --- | --- | --- | --- |
| `project.created` | project | control plane | A project exists; carries its location, the folder the host bound it to, or null |
| `project.location_set` | project | control plane | The project's work runs in another folder from now on |
| `project.catalog_linked` | project | control plane | The person linked the project to one repository of their catalog ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-phat-porcupine.md)), replacing the link it had (`previous`). Observed, as the person's command; the repository's `owner/name` in it is as the client sent it, taken from the catalog's answer; checked against the catalog this computer last read, never against GitHub, and it may name a private repository or an organisation, so the journal then holds that name, and the person's own clients receive it in the realtime stream |
| `project.catalog_unlinked` | project | control plane | The person removed the project's link; carries the link removed, with its `owner/name` as it was recorded when linked |
| `project.starter_applied` | project | control plane | The project's new folder began with a starter's files ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)): the starter, the SHA-256 of the lockfile its shared dependencies were installed from, how many files were copied, and a SHA-256 over their paths and contents |
| `project.made_from` | project | control plane | The project was made as a VR version of another (M1.8c-1): `original_project_id`, which its agent may read but never change ([ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md)), and `instruction`, the text the host wrote for that agent, naming the original's folder; the client starts work with it |
| `project.checkpoint.recorded` | project | control plane | The host kept every file of the project's folder as a checkpoint for its own undo ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)): why (`before_task`, `after_turn`, `before_restore`), the execution it follows, the tree hash and file count, and the project's own commit. Never contents |
| `project.checkpoint.failed` | project | control plane | No checkpoint could be kept (`location_missing`, `nested_repository`, `too_large`, `unreadable`, `holds_host_data`, `store_failed`); nothing from that point can be put back |
| `project.restored` | project | control plane | The host put a checkpoint back and read every file again: all equal. Carries the checkpoint taken just before, the files compared, and at most 100 paths each changed back, removed and returned, relative to the folder |
| `project.restore_incomplete` | project | control plane | A restore did not end equal to the checkpoint: whether anything was written, and what still differs |
| `project.check.recorded` | project | control plane | The host checked a project made from a starter: the type check's and build's exits, and what the built copy did for 10 s in the sandboxed browser (uncaught exceptions, console errors, edges in two pictures, `drawn`), `passed`, `reads_confined` (every execution in the project recorded at its start that it ran confined to its folder and, for a VR version, its original read only, with the original's version control and secret-shaped files and Halcyonic's data refused), and since M1.8b `laptop` (the layer's report of a real mouse press and keys, the layer unchanged, the screen change observed in the host, and since M1.8g `response_seen` against a stepped-clock twin and `exceptions_during`, and since M1.8g-b `response_unknown`, why a twin read was unknown), `vr` (offered `immersive-vr` to a stand-in WebXR), `notes` (the source scan) and `ready`, each absent on earlier checks. `drawn` and `passed` mean something is drawn and nothing threw, never that the game works; `ready` never that it plays well or on a headset; the pictures stay on the computer, by SHA-256 ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)) |
| `project.preview.started` | project | control plane | The host serves the built copy of the project's latest passed check on 127.0.0.1, at a port of 47930 to 47939; never the live folder. Recorded only once the server itself listens on that port (its own pid, by lsof) and answers `/index.html` with the copy's bytes; a server that exits, or a port another process answers, records nothing and fails the command |
| `project.preview.stopped` | project | control plane | The preview stopped: `asked`, `new_check`, `server_exited` or `host_restarted` |
| `workstream.created` | workstream | control plane | A workstream exists |
| `execution.created` | execution | control plane | A start was accepted; the runtime has not confirmed yet. Carries the folder the runtime was given (`directory`), null for a runtime that uses none |
| `execution.files.observed` | execution | control plane | What changed in the execution's folder by the end of a turn, as the Mac read it ([ADR 0038](../decisions/0038-the-mac-observes-each-turns-file-changes-itself.md)): files created, changed and deleted, with sizes and line counts, at most 100 of each with totals; never contents; `not_read`, the count of entries found and not read (links, FIFOs, sockets, devices; the top-level `.git` and `node_modules` are never read and not counted); or `skipped` with a reason (`no_baseline`, `too_large`, `location_missing`, `unreadable`, `holds_host_data`). Provenance `observed`; it cannot tell who changed a file |
| `execution.start_failed` | execution | control plane | The runtime refused to start |
| `execution.state_unknown` | execution | control plane | The execution can no longer be observed (`control_plane_restarted`, `start_outcome_unknown`) |
| `command.accepted` | as resolved | control plane | Admitted; carries the full command, its policy, how it arrived and its principal |
| `command.rejected` | as resolved | control plane | Refused; carries the full command, the reason and its principal, except that a refused `execution.answer_question` keeps only the keys it answered: what was chosen or typed is not recorded |
| `command.completed` | as resolved | control plane | Done, confirmed by the runtime where one was involved |
| `command.failed` | as resolved | control plane | Not done; `effect` says whether it may have happened anyway |
| `runtime.execution.started` | execution | runtime | The native session or thread exists; since S2, `confined`, observed by the adapter: whether it started the execution inside the host's confinement of the project ([ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md)); absent where a runtime does not record it and on earlier events, which read as not confined |
| `runtime.turn.started` | execution | runtime | A turn began |
| `runtime.turn.completed` | execution | runtime | The turn ended normally |
| `runtime.turn.failed` | execution | runtime | The turn ended with an error |
| `runtime.turn.interrupted` | execution | runtime | The turn was stopped on request |
| `runtime.approval.requested` | execution | runtime | The runtime is blocked on a human decision; carries the tool and what it asks for (`subject.summary`), the tool's input (below), and whether that is `complete` (below) |
| `runtime.approval.resolved` | execution | runtime | The runtime applied a decision |
| `runtime.question.asked` | execution | runtime | The agent waits for the person to answer a question; carries the runtime's question id, its prompts (the agent's words) and whether Halcyonic can answer it ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)) |
| `runtime.question.resolved` | execution | runtime | The runtime took an answer (`answered`) or withdrew the question (`dismissed`); a turn's end withdraws it too |
| `runtime.tool.started` / `.completed` | execution | runtime | Tool activity; a start carries the tool's name and a `title` from its input, or null (below); the execution view's `tool_activity` follows them (below) |
| `runtime.tool.result` | execution | runtime | Observed final command or file-change fields from a supported adapter, correlated to the exact observed tool start and turn; bounded owner work content, not a current-tree or correctness claim |
| `runtime.agent_message` | execution | runtime | Agent text (`reported`) |
| `runtime.test_run.started` / `.completed` | execution | runtime | A test run and its outcome, with the runtime's label and summary; only the mock reports test runs today |
| `runtime.connection.lost` | execution | runtime | The adapter lost contact with the runtime |
| `runtime.connection.restored` | execution | runtime | The adapter can observe the runtime again after a loss; what changed meanwhile follows as its own events |
| `runtime.model.used` | execution | runtime | The model the runtime says the execution runs on (`observed`), by the `model_ref` of the runtime's list; reported when it starts and whenever it changes |
| `device.paired` | none | control plane | A device proved it saw the pairing code; carries its id, its self-declared label, the SHA-256 of its credential and of the certificate it pinned ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)) |
| `device.revoked` | none | control plane | The device's credential is no longer accepted; carries who revoked it |

The command events carry the full command, which is the audit record: who asked, as the client
declared itself and as the control plane authenticated it (`principal`: `local` for the access
token, `device` with the device's id, null from inside the control plane), through which
transport, under which policy, and with what outcome.

Device events belong to no project, so their scope is null throughout. The device registry is part
of the projection, and a credential is known only by its SHA-256. Realtime clients never receive
device events, and paired devices do not read them from the history ([REALTIME.md](REALTIME.md)).
A command from a device revoked after its request or connection was authenticated is journaled as
`command.rejected` with `device_revoked`.

A tool's `title` and what an approval asks for carry the tool's input, as the runtime gives it:
Codex's whole command line, with where it runs, or the files a change touches; Claude Code's
command, path, URL, query or pattern, or for an approval with none of those, its whole input as
JSON, which for an MCP tool can hold a credential argument; for an OpenCode shell command, where it
runs and the command as the model gave it, and otherwise OpenCode's resources for a permission.
A question's texts and a test run's label and summary are the runtime's own words. Adapters report
all of them whole, and the control plane takes every exact copy of a secret it holds out of them,
named, then cuts each to the contract before it is journaled. A credential it doesn't hold stays,
so the person reads the command as it would run ([SECURITY.md](SECURITY.md), "Logs and error
text").

An execution view's `tool_activity` says whether a tool call runs, from these events alone:
`running` when a turn is active, its status is not `unknown`, and a tool call is open (started,
not completed) or a test run is; `none` when no turn is active (it then says only that no turn
runs: a Claude Code subagent can work on after its turn), or one is with nothing open on a
runtime whose descriptor declares `reports_tool_activity`, that its adapter reports every tool
call the runtime makes; `unknown` when a turn is active on a runtime that does not declare it, or
the status is `unknown`, since a lost connection leaves the open calls stale. A turn's start and
end close every call left open. OpenCode (whose adapter reports a call that started while its
event stream was down once it reads the session back) and the mock declare it. Claude Code and
Codex do not: their subagents work on after the call that launched them completes, unreported
(Claude Code's run in the background by default; Codex's on threads the adapter does not
follow), so with nothing open they read `unknown`, never `none` ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
Codex still reports every tool item of its 0.157.0 `ThreadItem`, not only commands and file
changes, which keeps Activity truthful. Status wins over it: an execution waiting for the person
is waiting, whatever `tool_activity` says, since a call raised for an approval or a question is
open while it waits. Two imprecisions: OpenCode starts a tool when the model begins writing its input, before it runs, so 'running' comes slightly early.
A background shell (OpenCode background, Claude run_in_background) completes its call at once, while the process runs on: 'none' then means no tool call is open, not that nothing runs.
Nothing is journaled for it: the descriptor is served live, and the field is derived.

An approval request carries `complete`, defined as: complete: the summary shows in full every
command that would run, with how and where it runs, and names every path a change would write. It
does not include a change's content. Each adapter says it for what it reports. OpenCode: a shell
command whose call it knows; a request for paths (an edit, a read, a search, a folder outside the
task's, an edit there asking again by its own path); nothing else. Codex: a command with its folder
(input to a running command needs none), any network access it also asks for, and an environment
that is this computer's; network access by its host; a change whose every file it named, unless it
also asks for write access under a folder for the rest of the session. Claude Code: its whole input
as JSON, or one of its own tools described by one field whose other inputs do not act; a Bash
command, whose input names no folder, follows a bracket that always comes first, "[in its shell,
which started in the task's folder]" (an earlier command may have moved it), with ", in the
background" inside it when it runs there. A tool's name cut
to the contract is not complete. A secret Halcyonic holds still shows as "[redacted: ...]" in a
complete summary, and an agent could type the same words. The control plane makes it false when it
cuts the summary. The projection copies it to `ApprovalView.approvable`,
and admission answers an approve of a request that is not approvable with `approval_not_whole`
("This request was never shown whole, so it can only be denied."); a deny is admitted.

Where an approved command runs is declared by the runtime, never read from a request
([ADR 0033](../decisions/0033-approvals-declare-whether-commands-leave-the-sandbox.md)). A
descriptor's optional `approval_may_leave_sandbox` is true when approving a command can let it run
outside any sandbox with the person's own access: Codex, whose approved escalation runs outside its
sandbox while an approved dangerous plain command stays inside, with nothing in the request to tell
them apart. It is false when an approved command still runs inside a sandbox: OpenCode inside
Halcyonic's ([ADR 0028](../decisions/0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md)). It
is absent when not verified: Claude Code, the mock, and OpenCode without the sandbox. Absent means
unknown, never false, and a client must never show it as the command staying inside a sandbox.

## Contracts: one source of truth

Native tool results ([ADR 0031](../decisions/0031-record-bounded-native-tool-results-in-the-owner-journal.md))
retain final completion and optional command exit code separately from output or per-file patches.
Only the observed started tool's execution/turn/runtime and full project/workstream scope admit
the result. Foreign or stale tool completion cannot clear that correlation. New completion events
carry an optional `turn_id`; absent legacy values do not fabricate a result correlation. Replay
rebuilds accepted result summaries; a matching raw journal event that the projection rejected is
never served as evidence. Sixteen recent summaries retain no bodies; a lightweight accepted-event
index and indexed exact journal read keep older selected evidence readable.

Each native source text field above 64 KiB UTF-8 is omitted whole. Exact held values are replaced
before the admitted field is cut to 4,096 codepoints/16 KiB UTF-8. The serialized result payload is
at most 64 KiB, with at most 32 file entries; aggregate omission and file coverage remain explicit.
Paths retain at most 1,024 codepoints, with shortened-path flags. Unknown/outside original or rename
destination paths omit the patch; lexical inclusion does not resolve symlinks. `complete` describes
only retention of the admitted native field after replacement, not complete stdout, a complete
source patch, applied file bytes or current working-tree freshness. Native completion says whether
the runtime completed/failed/declined the item; patch text alone proves no application. No result
creates a test run, build status, installed-app association or semantic understanding.

Results use the established owner-journal work-content policy: held-value replacement is explicit,
actual replacement counts are recorded, unknown secrets may remain, and result bodies are never
operational log content. No raw field hash, source spool or terminal/message parsing is introduced.
The adapter still receives an unbounded RPC frame before admission; field and journal payload
bounds do not bound that transport frame or overall process memory.

The schemas are TypeBox definitions in `packages/contracts`. TypeBox schemas are JSON Schema, so
the TypeScript types are inferred from them and `pnpm contracts:emit` serializes them, with shared
definitions referenced by name, into `packages/contracts/schema/halcyonic-contracts.schema.json`
for other languages. The same command generates the C# bindings for the Unity client
([XR_CLIENT.md](XR_CLIENT.md)). An optional property (`Type.Optional`), of any type, becomes a
nullable C# member that reads null when the property is absent and is left out when null, so a
field added to a stored event this way keeps earlier events valid without a migration; a constant
cannot be optional. A test fails if either is stale, or if the document validates
anything differently from the TypeScript validators. See [ADR 0005](../decisions/0005-typebox-contracts-as-single-source.md).

A few rules cannot be written in JSON Schema and are checked by the TypeScript parsers alone:
an event naming an execution names its workstream, one naming a workstream names its project,
agent text is `reported`, and a client message (a command above all) nests at most 32 levels and
holds only well-formed Unicode strings, keys included. The first bounds whatever walks a value a
field takes as any JSON, such as start options; the second keeps a lone surrogate from a runtime
(Codex drops a message holding one, [agent-questions.md](../validation/agent-questions.md)). Both
are checked first, without recursion, before any schema. A command breaking one is answered 400
`invalid_command` over REST, and `invalid_message` over the realtime socket, as for any other
contract violation.

To change a contract:

1. Edit the schema in `packages/contracts`.
2. Update the projection, adapters and clients that use it.
3. Run `pnpm contracts:emit` and `pnpm fixtures:record`, review the diffs, and run
   `pnpm test:csharp`.
4. For an incompatible change, bump the relevant version in `versions.ts` and add a journal
   migration if stored events are affected.
5. Update this document.

## Versioning and migration

- Envelope, command and realtime protocol versions are independent integers.
- The journal schema is versioned by `PRAGMA user_version` with forward-only migrations, each in
  its own transaction. A journal written by a newer build is refused rather than misread.
- Migration 2 gives every stored `execution.start` command, in `command.accepted` and
  `command.rejected`, the `model_ref: null` its contract gained, which is what it meant; no
  version changed, because no client in the field sent the command
  ([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)).
- Migration 3 gives every stored `command.accepted` and `command.rejected` the `principal: null`
  its contract gained: who sent a command journaled before principals were recorded is not known
  ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)). It also runs migration
  2's update again, because builds of the pairing work from before the two met stamped version 2
  without it; both change only events without the field. A build from before either migration
  refuses a journal a newer one has opened.
- Migration 4 gives every stored `project.created` event and `project.create` command the
  `location: null` their contracts gained, and every stored `execution.created` event
  `directory: null`, which there means not recorded rather than no folder
  ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)). No version
  changed, because the only client in the field is a development build: one generated before
  this change sends `project.create` without `location` and gets 400 `invalid_command` until it is
  rebuilt, since the command's schema requires the explicit null.
- Migration 5 gives every stored `runtime.approval.requested` the `complete` its contract gained
  on 2026-10-08: `true`, as it was offered for approval, unless the headset then offered only Deny
  for it, its summary ending in " [truncated]" or starting with OpenCode's "[whole command not
  known]", which become `false`. It runs on the owner's journal the next time the control plane starts. No version
  changed: `ApprovalView.approvable` and the `approval_not_whole` rejection are additions, and the
  only client in the field is a development build, regenerated with them.
- The question events, `runtime.connection.restored`, `execution.answer_question` and the
  `answer_question` capability are additions that no stored event or command lacks, so they need
  no migration and change no version
  ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)). A client generated before
  them must be regenerated to read execution views, which now carry `pending_questions`.
- Until there are external users, breaking changes are acceptable when coordinated: migrate
  fixtures, the journal schema, generated bindings and documentation together.

## What is not journaled

Conclusions from Salidium and Seorak are read through on request and never journaled or replayed
([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)). Neither is the person's
catalog from Phat Porcupine ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-phat-porcupine.md)): only the links the person makes between a project and one
of its repositories are journaled, with the repository's id and its `owner/name` as the client sent it. Neither is the list of
folders the host allows (`GET /api/locations`), nor the list of
models a runtime offers, which changes whenever a model is pulled, removed or configured; only the
choice, in the `execution.start` command, and the model the runtime reports using are journaled
([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)).

The files a checkpoint holds stay in the host's checkpoint store, outside the journal; so does what
a restore would change before it is confirmed (`GET .../restore-preview`), which is read on request
([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)).

## Traces

A trace is a journal exported as JSON Lines: one envelope per line, in position order.
`pnpm fixtures:record` records each plan in `apps/control-plane/src/demo-plan.ts` by driving the
real control plane and the mock runtime under virtual time with seeded identifiers, including a
scripted operator who approves, denies, instructs or interrupts. The same code therefore always
produces the same files, and a test fails when a committed trace no longer matches.

| Trace | Shows |
| --- | --- |
| `fixtures/traces/multiple_workstreams.jsonl` | The demo: a finished feature, a turn whose tests failed, an approval granted |
| `fixtures/traces/failure_modes.jsonl` | A failed turn, a runtime that became unreachable (`unknown`), an interrupted turn with a refused mid-turn instruction, a denied approval |
 Replaying a trace goes through the normal write path, so replayed events are validated,
projected and streamed exactly like live ones, and replaying twice changes nothing.

Traces are development fixtures. They must never contain credentials or private repository
content.

### Recorded runs

A recorded run is one real run's journal prepared to be shown: how a person tries Halcyonic before
connecting a computer, first used by the judge build ([ADR 0035](../decisions/0035-a-recorded-real-run-lets-anyone-try-halcyonic-before-connecting-a-computer.md),
amending [ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md)).
`pnpm judge:record <trace.jsonl> --root <project folder> --home <home folder> --deny <file>`
reads the run exported as a trace (read only: `sqlite3 'file:control-plane.db?immutable=1'
"select envelope from events order by position"`) and changes only:

- paths under the project's folder, which become relative to it ("the project folder" for the
  folder itself);
- the runtime's identity, replaced by placeholders: id `recorded-agent`, kind `recorded`, name
  "Coding agent", also in native types and in the start command;
- the model the runtime reported, replaced by the placeholder `local-model` (a model reference has
  no spaces), the client that sent the commands, and the start command's options, which become
  `{}` because their keys are the runtime's own and the playback starts nothing;
- the output of commands, left out as `omitted_scope` with exit codes kept, because tools print
  their makers' names.

The project folder's path is rewritten wherever it appears, in what the person and the agent said
too. Nothing else in instructions, agent messages, approval requests, tool titles, patches,
provenance, identifiers or times is edited. Every string and every object key is scanned. If any
still names a product, company or platform (the same list as the headset's `JudgeWordsTests`, and
runtime, model and product names in any case), a home or user folder, a private folder, the local
host, an address, a URL, an email, a credential shape or a word from the deny file, nothing is
written: the tool lists each event and field, never the text or the key, and the run is recorded
again. The deny file is required and lists at least the owner's names, matched as whole words in any
case, since an agent quoting a commit's author carries them. A run whose runtime is synthetic is
refused; the demonstration is the place for simulated work. A journal holding an event type the
recorder is not written for is refused whole, naming the type, since a new type may carry words no
rule knows, such as a private repository's name.

The recording is the demonstration's format at version 3, kind `recorded_run`
([XR_CLIENT.md](XR_CLIENT.md)). A control plane without runtimes is given the sanitized run
through its normal write path under virtual time, so every message is one its projection and
publisher computed. Waits between events are shortened to 2.5 seconds at most; every event keeps
the time it really happened. Where someone answered an approval, the recording holds before the
answer's command and holds only that answer. The runtime is listed under its neutral name, real,
able only to answer approvals. A run that asked the person a question is refused for now.

The recorder's rules find only what they list, so a person (AUDIT, then the owner) reads the whole
recording before it is bundled or shared. The run's journal stays private, kept outside the
repository. After a change to the event format, the contracts or the pipeline, the run is recorded
again from that journal, and `pnpm judge:record ... --check` says whether the bundled file is still
that run's recording. Unlike the demonstration's check, `pnpm check` cannot run it, because the
journal never enters the repository; without the journal, a new run is recorded.

## General workspace request lifecycle

`workspace.request.created`, `.changed` and `.step_recorded` are control-plane lifecycle facts
recorded through `Recorder`. They retain the original principal and exact work tuple. Model replies
remain explicitly `reported`; a `responded` request means the conversation settled, not that software
work completed. Prepared mutation records admit only the supported send-instruction command and
retain its immutable identity/payload before dispatch. Delivery status stays separate from the
subsequent native turn outcome, which must be an observed lifecycle event the projection applied.
Ignored stale-sequence or mismatched-turn journal rows are never outcome evidence.

The original observation position/active turn remains immutable; a separate monotonic observation
cursor makes bounded scans resumable. The latest 64 public receipts are bounded while prepared
action identities remain discoverable through journal replay. A control-plane restart interrupts
planning and requires explicit Resume with fresh original-principal/context checks; it never retries
a pending or unknown mutation. Runtime work may continue independently.

These event/command enum additions require regenerated consumers before updating the live host,
as described in [REALTIME.md](REALTIME.md#workspace-request-upgrade-order). Existing journals with no
request events replay unchanged; this does not make old clients accept new welcome catalogues.
