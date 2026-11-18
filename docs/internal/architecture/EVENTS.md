# Events and the journal

## The journal

Every fact Halora (`halcyonic`) knows is an event in an append-only journal (SQLite, one file per
control plane). Events are never changed or deleted. Current state is the projection of the journal,
so history, replay, debugging and audit all come from the same record.

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
| `schema_version` | Envelope version (`1`) |
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
| `project.created` | project | control plane | A project exists; carries its `name` and `location`, the folder the host bound it to, or null |
| `project.location_set` | project | control plane | The project's work runs in another folder from now on |
| `project.catalog_linked` | project | control plane | The person linked the project to one repository of their catalog ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)), replacing the link it had (`previous`). Observed, as the person's command; the repository's `owner/name` in it is as the client sent it, taken from the catalog's answer; checked against the catalog this computer last read, never against GitHub, and it may name a private repository or an organisation, so the journal then holds that name, and the person's own clients receive it in the realtime stream |
| `project.catalog_unlinked` | project | control plane | The person removed the project's link; carries the link removed (`previous`), with its `owner/name` as recorded when linked |
| `project.removed_from_list` | project | control plane | The person took the project off the list on this computer ([ADR 0063](../decisions/0063-a-project-leaves-the-list-and-comes-back-by-its-folder.md)); its folder and every record of it stay untouched, and client reads that filter by `listed` omit it |
| `project.returned` | project | control plane | Adding a folder brought a previously unlisted project back onto the list with its `location`, tasks and version history intact ([ADR 0063](../decisions/0063-a-project-leaves-the-list-and-comes-back-by-its-folder.md)) |
| `project.starter_applied` | project | control plane | The project's new folder began with a starter's files ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)): `starter_id`, the SHA-256 `lockfile` its shared dependencies were installed from, how many `files` were copied, and a SHA-256 `template` over their paths and contents |
| `project.made_from` | project | control plane | The project was made as a VR version of another: `original_project_id`, which its agent may read but never change ([ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md)), and `instruction`, the text the host wrote for that agent naming the original's folder |
| `project.checkpoint.recorded` | project | control plane | The host kept every file of the project's folder as a checkpoint for undo ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)): `reason` (`before_task`, `after_turn`, `before_restore`, `before_add`), `execution_id`, `tree` hash and `files` count, and `project_head`. Never contents |
| `project.checkpoint.failed` | project | control plane | No checkpoint could be kept (`location_missing`, `nested_repository`, `too_large`, `unreadable`, `holds_host_data`, `store_failed`); nothing from that point can be put back |
| `project.checkpoints.pruned` | project | control plane | The host pruned old checkpoints from the project's checkpoint store while keeping every checkpoint still needed: `checkpoint_ids` removed and `kept` count ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)) |
| `project.restored` | project | control plane | The host put a checkpoint back and read every file again: all equal. Carries `before_restore_checkpoint_id`, `files_compared`, and at most 100 paths each changed back, removed and returned, relative to the folder |
| `project.restore_incomplete` | project | control plane | A restore did not end equal to the checkpoint: whether anything was written, and what still differs |
| `project.check.recorded` | project | control plane | The host checked a project made from a starter ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)): type-check and build exits; 10 s sandboxed browser findings (uncaught exceptions, console errors, edge counts across two screenshots, `drawn`, and `own` pixel share outside the welcome card region against the starter reference screenshot); `passed`, `reads_confined`, `laptop` (input response against a stepped-clock twin, `reading`, `fit`, `sound`), `vr` (`immersive-vr` offered to a stand-in WebXR), `notes`, `ready`, and optional `verdict` (`passed`, `failed`, `not_known` or `starter_only`; historical check events recorded without `verdict` keep their stored fields). `drawn` and `passed` mean something of the project's own rendered without throwing, never that the app works; screenshots stay on the host by SHA-256. Optional `workstream_id` names the task whose copy was checked ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)) |
| `project.own_check.chosen` | project | control plane | The person chose scripts of a non-starter project's `package.json` (`manager`, `scripts`, optional `open_ports` allowing loopback ports not listening when the check starts and outside Halora's own range; [ADR 0055](../decisions/0055-a-projects-own-check-is-chosen-by-the-person-and-confined-as-an-agent.md)) |
| `project.own_check.recorded` | project | control plane | The host ran a non-starter project's chosen scripts inside the closed Seatbelt profile: `check_id`, `first`, `findings` (each script's exit and bounded output), and optional `workstream_id` when a task copy was checked ([ADR 0055](../decisions/0055-a-projects-own-check-is-chosen-by-the-person-and-confined-as-an-agent.md)) |
| `project.preview.started` | project | control plane | The host serves the built copy of the target folder's latest servable check on `127.0.0.1` at the first free port of 47980 to 48019 (for the 47800 port family), never the live folder; recorded only once the server itself listens on that port and answers `/index.html` with the copy's bytes. Optional `workstream_id` names a task copy's preview (`WorkstreamView.preview`), beside the project's own preview and other tasks' |
| `project.preview.stopped` | project | control plane | The preview stopped: `asked`, `new_check`, `server_exited` or `host_restarted`. Optional `workstream_id` scopes the stop to that task copy's preview alone |
| `project.mac_play.started` | project | control plane | A checked game plays on the Mac: the contained browser has opened the page its running preview serves for `check_id`, and its viewer listens on `127.0.0.1` at a port of 48020 to 48039. The viewer's link is never journaled |
| `project.mac_play.stopped` | project | control plane | The play stopped: `closed` (its last viewer left, or none came within 60 s), `preview_stopped`, `newer_check`, `host_stopped` or `failed`. Recorded only for a play recorded started |
| `project.share.started` | project | control plane | A checked game is shared for `check_id`: by `way` `link` at a temporary public `https://` address (`url`), or by `wifi` at `http://` on the Mac's private IPv4 address and a port of 48040 to 48059. Recorded only once the address answered 200, a link's over verified TLS ([ADR 0054](../decisions/0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md)) |
| `project.share.stopped` | project | control plane | The share stopped: `asked`, `preview_stopped`, `ended` or `host_stopped`. Recorded only for a share recorded started |
| `project.sent` | project | control plane | The project's work was pushed to the folder's own remote ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)): `remote` (host and path), `branch`, `base_commit` (null for a first send's `main`), `new_branch`, `commit`, and `review_request_url`, or null where no review request was opened |
| `project.sent_to_github` | project | control plane | Legacy GitHub-only send event retained so existing journals validate and project: `repository` (`owner/name`), `branch`, `base_commit`, `new_branch`, `commit`, and pull request address or null. Nothing `git` or `gh` printed is recorded |
| `project.repository_made` | project | control plane | A first send of a folder that was no repository made `repository` in the person's own account with their `gh`, with `visibility`, recorded the moment GitHub made it ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)) |
| `project.repository_made_public` | project | control plane | A publish made `repository` public with the person's `gh` before asking for Pages, recorded the moment GitHub did it ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)) |
| `project.published` | project | control plane | A checked game's built copy was published to the person's own GitHub Pages ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)): `repository`, `check_id`, `branch` (`halcyonic-pages`), `commit`, and `address`, null where GitHub did not return it |
| `project.repository_made_private` | project | control plane | `Make Private` made `repository`, which Halora created and made public, private again with the person's `gh` ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)) |
| `project.unpublished` | project | control plane | The published game was taken down: `branch` of `repository` deleted |
| `project.secret.stored` | project | control plane | The person set a project secret ([ADR 0057](../decisions/0057-a-projects-secrets-are-kept-on-the-mac-and-given-to-a-tasks-commands-at-its-start.md)): its `name`, and whether it `replaced` one of that name. Its value stays in the data directory only, never in an event |
| `project.secret.removed` | project | control plane | A project secret (`name`) was removed: its value deleted from the store |
| `project.media.added` | project | control plane | A photo or video the person added on the Mac's page is in the project's folder ([ADR 0066](../decisions/0066-a-persons-photo-or-video-goes-into-the-projects-folder-as-a-copy-without-its-place.md)): `path` under `media/`, `media_type`, `size_bytes`, `sha256` and `command_id`. Never the original file name, EXIF metadata or bytes |
| `project.media.prepared` | project | control plane | What the host made from an added photo or video ([ADR 0066](../decisions/0066-a-persons-photo-or-video-goes-into-the-projects-folder-as-a-copy-without-its-place.md)): `stills` or `stills_not_made`, for a video `play` or `play_not_made`, and `width`, `height` and `duration_ms` where read |
| `project.rules.updated` | project | control plane | The person turned a project's own rules file (`AGENTS.md` or `CLAUDE.md`) on or off: `on`, `file`, `sha256` and `changed` ([ADR 0059](../decisions/0059-a-projects-own-rules-reach-a-task-only-as-the-person-read-and-chose-them.md)) |
| `project.world.observed` | project | control plane | Legacy format 1 world reading (`WorldDescriptionFormat1`), retained in the contract so existing journals validate; not emitted or projected |
| `project.world.read` | project | control plane | The Mac read a world project's `world.json` ([ADR 0049](../decisions/0049-a-persons-world-is-a-project-whose-description-the-mac-reads.md)): validated `description` (format 2) or `refused` (`missing`, `not_a_file`, `too_large`, `not_text`, `not_json`, `not_a_description`, with `where` by member names and indices only, never the file's words); exactly one is null |
| `project.world.brought_forward` | project | control plane | The Mac backed up a format 1 `world.json` (`kept`) and wrote its format 2 equivalent (`scene`, `guide`: `replaced` or `kept`), followed by `project.world.read` |
| `workstream.created` | workstream | control plane | A workstream exists; optional `from_checkpoint` names the project version its task copy starts from ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)) |
| `workstream.copy_made` | workstream | control plane | The task has its own copy of the project's folder ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)): `directory`, `base_tree` SHA-256 and `files` count. Once per task |
| `workstream.copy_refused` | workstream | control plane | The task could not be given a copy (`linked_repository`, `nested_repository`, `too_large`, `cannot_clone`, `location_missing`, `holds_host_data`, `copy_failed` or `runtime_unconfined`), so its start works in the project's folder when permitted |
| `workstream.copy_missing` | workstream | control plane | At startup, the task copy's `directory` was missing on disk; `WorkstreamView.copy` carries `missing_at` ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)) |
| `workstream.copy_found` | workstream | control plane | A later startup found the task copy's `directory` on disk again, clearing `missing_at` ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)) |
| `workstream.copy_changes_read` | workstream | control plane | The host read the task copy (`tree`), finding up to 100 `changed` paths and typed `files` (`added`, `changed`, `removed`) against its base, plus the `review` digest ([ADR 0051](../decisions/0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md)) |
| `workstream.added` | workstream | control plane | The task copy's changes were merged into the project's folder (`merged_tree`, `task_tree`, `project_tree`, `checkpoint_id` for `before_add`, `added` files); the copy's base becomes `task_tree` |
| `workstream.add_conflicted` | workstream | control plane | An add found overlapping changes between the task copy and the project folder; nothing was written (`conflicted` files with `text`, `binary`, `deleted` or `other`) |
| `workstream.fitted` | workstream | control plane | The host wrote merged conflict-marked files into the task copy (`fitted_tree`, `written`); the copy's base becomes `project_tree` |
| `execution.created` | execution | control plane | A start was accepted; carries `directory` (null when none) and optional `secret_names` handed to the runtime ([ADR 0057](../decisions/0057-a-projects-secrets-are-kept-on-the-mac-and-given-to-a-tasks-commands-at-its-start.md)) |
| `execution.rules_given` | execution | control plane | The host prepended the project's chosen rules file (`file`, `sha256`) before a task's first instruction while the file still matched that digest ([ADR 0059](../decisions/0059-a-projects-own-rules-reach-a-task-only-as-the-person-read-and-chose-them.md)) |
| `execution.instruction.queued` | execution | control plane | An instruction (`command_id`) sent during a running turn on a runtime that cannot steer was queued at `position` (1 to 3; [ADR 0056](../decisions/0056-an-instruction-sent-during-a-turn-waits-for-it-on-a-runtime-that-cannot-steer.md)) |
| `execution.instruction.dequeued` | execution | control plane | A queued instruction (`command_id`) left the queue to be dispatched after a completed turn, or when dropped or cleared ([ADR 0056](../decisions/0056-an-instruction-sent-during-a-turn-waits-for-it-on-a-runtime-that-cannot-steer.md)) |
| `execution.instruction.pictures_read` | execution | control plane | The host read the pictures an instruction carries (`command_id`, up to 4 `pictures` with path, media type, byte size and SHA-256; never bytes) from the project's folder before handing those bytes to the runtime ([ADR 0058](../decisions/0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md)) |
| `execution.instruction.media_given` | execution | control plane | The project's photos and videos an instruction carries ([ADR 0066](../decisions/0066-a-persons-photo-or-video-goes-into-the-projects-folder-as-a-copy-without-its-place.md)): each item's `path`, `media_type` and `stills_given`, plus `line_added` (never journaled as text) |
| `execution.files.observed` | execution | control plane | What changed in the execution's folder during a turn, read by the Mac between turn start and turn end ([ADR 0038](../decisions/0038-the-mac-observes-each-turns-file-changes-itself.md)): created, changed and deleted files with sizes and line counts (up to 100 each with totals), `not_read`, or `skipped` (`no_baseline`, `too_large`, `location_missing`, `unreadable`, `holds_host_data`). Provenance `observed` |
| `execution.files.writing` | execution | control plane | Files the host saw change in the execution's folder while its turn runs (`Watch It Work`): up to 20 newest `paths`, `paths_total` (up to 10,000) and `total_complete`, at most once every 2 s when changed; projected on `ExecutionView.writing` until the next turn starts |
| `execution.check_output_given` | execution | control plane | The host appended bounded lines from the folder's latest failed check (`check_id`, `parts`, `lines`, `bytes`; never text) to the next start or instruction ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)) |
| `execution.auto_fix.sent` | execution | control plane | The host sent a failed check back to this agent as its next instruction: `check_id`, `attempt`, `cap` and `command_id` ([ADR 0044](../decisions/0044-a-failed-check-goes-back-to-its-agent-on-its-own-within-a-cap.md)) |
| `execution.auto_fix.ended` | execution | control plane | Automatic check retry ended or did not start: `check_id`, `attempts`, `cap` and `reason` (`passed`, `cap_reached`, `person_stopped`, `person_instructed`, `waiting_on_person`, `project_busy`, `runtime_cannot`, `turn_failed`, `no_change`, `nothing_to_send`, `check_unavailable`, `send_failed`, `several_tasks`, `host_restarted`; [ADR 0044](../decisions/0044-a-failed-check-goes-back-to-its-agent-on-its-own-within-a-cap.md)) |
| `execution.approval.answered_by_host` | execution | control plane | In an execution recorded confined, the host approved a request itself before sending the decision: `approval_id`, `because` (`inside_copy`, `reads_original`, `confined_command`, `run_temp_read`), resolved `paths`, and optional `cut` ([ADR 0064](../decisions/0064-work-inside-a-confined-apps-own-copy-does-not-ask.md)) |
| `execution.approval.host_answer_withdrawn` | execution | control plane | A host approval (`approval_id`) could not be dispatched because its approve command was not accepted, so the request reaches the person ([ADR 0064](../decisions/0064-work-inside-a-confined-apps-own-copy-does-not-ask.md)) |
| `execution.approval.denied_by_host` | execution | control plane | The host denied an approval request (`approval_id`, `because`: `too_long_to_show`, `not_recorded`, `another_task_copy` or `host_folder`, optional `length` and `tool_name`, `delivered`; [ADR 0065](../decisions/0065-a-request-too-long-to-show-whole-is-denied-by-the-host-with-its-reason.md)) |
| `execution.question.ended_by_host` | execution | control plane | A question the journal could not record (`question_id`, `because: 'not_recorded'`) had its turn interrupted once where the runtime declares `interrupt` (`interrupt_requested`; [ADR 0065](../decisions/0065-a-request-too-long-to-show-whole-is-denied-by-the-host-with-its-reason.md)) |
| `execution.closing_words` | execution | control plane | Plain-language closing sentences from a completed turn's last agent message answering `CLOSING_REQUEST`: `did`, `why`, `how`, `left` (each up to 300 characters or null) and `turn_id`. Provenance `reported`, never `observed`; projected on `ExecutionView.closing_words` |
| `execution.start_failed` | execution | control plane | The runtime refused to start |
| `execution.state_unknown` | execution | control plane | The execution can no longer be observed (`control_plane_restarted`, `start_outcome_unknown`, `session_lost`) |
| `command.accepted` | as resolved | control plane | Admitted; carries the full command, its policy, how it arrived and its principal |
| `command.rejected` | as resolved | control plane | Refused; carries the full command, the reason and its principal, except that a refused `execution.answer_question` keeps only the keys it answered: what was chosen or typed is not recorded |
| `command.completed` | as resolved | control plane | Done, confirmed by the runtime where one was involved |
| `command.failed` | as resolved | control plane | Not done; `effect` says whether it may have happened anyway |
| `assistant.model.chosen` | none | control plane | The person chose a hosted model (`model`: `{ provider_id, model_id }`) or this computer's local model (`null`) for workspace assistant requests ([ADR 0030](../decisions/0030-workspace-requests-use-a-journaled-agent-tool-loop.md), [ADR 0061](../decisions/0061-a-hosted-provider-is-one-declaration-file-the-host-reads.md)) |
| `workspace.request.created` | as resolved | control plane | A workspace assistant request exists (`request`, `principal`; [ADR 0030](../decisions/0030-workspace-requests-use-a-journaled-agent-tool-loop.md)) |
| `workspace.request.changed` | as resolved | control plane | A workspace request changed `status`, carrying optional `reply` (`reported`) or `observed` when ending without a model reply |
| `workspace.request.step_recorded` | as resolved | control plane | One tool or proposal step of a workspace request (`step`, optional `prepared_command`, `proposed_command` and `proposed_before`; [ADR 0060](../decisions/0060-the-computer-says-what-can-be-done-here.md)) |
| `runtime.execution.started` | execution | runtime | The native session or thread exists; carries optional `confined` (`observed` by the adapter: whether it started inside the host's confinement of the folder, [ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md); events without `confined` project as not confined) |
| `runtime.turn.started` | execution | runtime | A turn began |
| `runtime.turn.completed` | execution | runtime | The turn ended normally |
| `runtime.turn.failed` | execution | runtime | The turn ended with an error |
| `runtime.turn.interrupted` | execution | runtime | The turn was stopped on request |
| `runtime.approval.requested` | execution | runtime | The runtime is blocked on a human decision; carries the tool and what it asks for (`subject.summary`), structured `command`, `changes` or `inputs` where present, and whether that is `complete`; `too_long` and `cut` are the control plane's own facts |
| `runtime.approval.resolved` | execution | runtime | The runtime applied a decision |
| `runtime.question.asked` | execution | runtime | The agent waits for the person to answer a question; carries the runtime's question id, its prompts (the agent's words) and whether Halora can answer it ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)) |
| `runtime.question.resolved` | execution | runtime | The runtime took an answer (`answered`) or withdrew the question (`dismissed`); a turn's end withdraws it too |
| `runtime.tool.started` | execution | runtime | A tool call started; carries the tool's name and a `title` from its input, or null; drives `ExecutionView.tool_activity` |
| `runtime.tool.completed` | execution | runtime | A tool call completed, carrying optional `turn_id` |
| `runtime.tool.result` | execution | runtime | Observed final command or file-change fields from a supported adapter, correlated to the exact observed tool start and turn; bounded owner work content, not a current-tree or correctness claim |
| `runtime.agent_message` | execution | runtime | Agent text (`reported`) |
| `runtime.test_run.started` | execution | runtime | A test run started, with the runtime's label; reported only by the mock runtime |
| `runtime.test_run.completed` | execution | runtime | A test run ended with its outcome, label and summary |
| `runtime.connection.lost` | execution | runtime | The adapter lost contact with the runtime |
| `runtime.connection.restored` | execution | runtime | The adapter can observe the runtime again after a loss; what changed meanwhile follows as its own events |
| `runtime.model.used` | execution | runtime | The model the runtime says the execution runs on (`observed`), by the `model_ref` of the runtime's list; reported when it starts and whenever it changes |
| `runtime.usage.reported` | execution | runtime | What one turn used, as the runtime counts it (`reported`): tokens in five non-overlapping parts (uncached input, cache reads, cache writes, output including reasoning, reasoning), each null where not given, the runtime's own cost estimate in US dollars (0 to 1,000,000) by price kind (`list`, `managed` or `catalog`) or null, and `counted` (`given`, `summed` or `differenced`); at most one per turn from an adapter declaring `reports_usage` ([agent-usage.md](../validation/agent-usage.md)) |
| `runtime.plan.left` | execution | runtime | The runtime left its plan mode in the execution's session (`observed`), confirmed before the instruction that carries the plan out was sent ([agent-plan-modes.md](../validation/agent-plan-modes.md)) |
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

### Projected runtime and approval details

- **Tool titles, approval summaries and redaction.** A tool's `title` and an approval subject carry
  the tool's input as the runtime gives it: Codex's full command line and working directory, or the
  files a change touches; Claude Code's command, path, URL, query or pattern, or whole JSON input
  when none of those applies (which for an MCP tool can hold a credential argument); OpenCode's
  shell command and working directory, or permission resources otherwise. Question texts, test run
  labels and summaries, and agent messages are the runtime's own words. Adapters report all of them
  whole; the control plane replaces exact copies of secrets it holds with named markers and cuts
  each field to its contract bound before journaling. Credentials the host does not hold remain so
  the person reads the command as it would run ([SECURITY.md](SECURITY.md), "Logs and error text").
- **Structured approval commands and inputs.**
  - When an approval request runs a command, `subject.command` carries its parts: `text` (exact
    command text), `runs_in` (`session_shell` for Claude Code's Bash, whose shell starts in the
    task's folder and keeps directory changes across commands; `task_folder` or `folder` with `path`
    for OpenCode, Codex and Antigravity; `unknown` when unspecified), `background`,
    `input_to_running` (Codex input to a running command), and `network` (host and protocol requested
    by a Codex command). Adapters build the summary from these parts alone; the control plane
    redacts held secrets and marks non-printable characters (`‹U+XXXX›`, `⇥` for tab, `↵` before a
    line break, escaping literal mark characters as code points;
    [ADR 0065](../decisions/0065-a-request-too-long-to-show-whole-is-denied-by-the-host-with-its-reason.md)).
    Any part cut to its contract bound makes `complete` false.
  - When an ACP agent app request carries no structured command, `subject.inputs` lists each
    top-level input in order (`name`, `value`, `kind`: `text` or `json`), while the summary still
    lists `"Use <tool>"` followed by `name: value` lines. More than 32 inputs, a name over 200
    code points, or a value over 32,000 code points is carried cut and marks `complete` false. File
    tools, fetches, network-only requests and events recorded without `inputs` carry none, and cards
    draw the summary.
- **Usage (`ExecutionView.usage`, `WorkstreamView.usage`, `ProjectView.usage`).** Sums reported
  turns (`RunUsage`):
  - `turns` counts turns with a `runtime.usage.reported` event; `turns_ended` counts every ended
    turn (completed, failed or interrupted), reported or not, and is never below `turns`. When
    `turns < turns_ended`, part of the usage is uncounted and clients state so ("reported for 2 of 3
    turns"), never presenting a partial sum as the whole.
  - Each token bucket sums across reported turns and becomes null once any reported turn leaves that
    bucket null. `cost` sums the runtime's own USD estimates while every reported turn used the same
    price kind, becoming null once any non-zero turn lacks an estimate or changes price kind (a turn
    with zero across all counted buckets costs zero without voiding `cost`).
  - Usage stays absent until at least one turn reports, which never reads as zero used. Interrupted
    turns count. The host never prices tokens itself; clients state whose estimate `cost` is and
    that it is not a provider bill.
- **Spending limits (`ExecutionView.spending_cap`).** Holds the active limit (`limit_usd`, set at
  start or raised by `Carry On`) and `spent_usd`, the sum of the runtime's estimates within the
  active cap span ([ADR 0062](../decisions/0062-a-tasks-spending-limit-is-the-apps-own-stop-handed-only-what-is-left.md)):
  - Any turn in the span without a report, without an estimate, or under different prices makes
    `spent_usd` null (not known).
  - `state` is `reached` once a turn fails with `spending_cap_reached` or `spent_usd` reaches the
    limit, `waiting` when a restored connection cannot compute a remaining allowance (unknown sum,
    unreported turn in flight, or nothing left), and `open` otherwise.
  - A confirmed `Carry On` instruction opens a new span, and `status_reason` states in host words
    that the runtime stopped at its estimate (not a bill), alongside total task figures after a
    raise or an explicit unknown-sum notice.
- **Tool activity (`ExecutionView.tool_activity`).** Derived from turn and tool events plus the live
  `RuntimeDescriptor`:
  - `running`: a turn is active, execution status is not `unknown`, and at least one tool call or
    test run is open.
  - `none`: no turn is active (Claude Code background subagents may still run after a turn), or a
    turn is active with nothing open on a runtime declaring `reports_tool_activity` (OpenCode and
    the mock runtime).
  - `unknown`: a turn is active with nothing open on a runtime that does not declare
    `reports_tool_activity` (Claude Code, Codex and ACP agent apps;
    [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)), or execution status is `unknown`.
  - Turn start and turn end close any open calls. Execution status takes precedence (`waiting_for_human`
    stays waiting while a tool call blocked on an approval or question remains open). OpenCode opens
    tool calls as input streaming begins, and background shells (`background`, `run_in_background`)
    complete their tool call immediately while the child process continues.
- **Whole approval requests (`complete` and `ApprovalView.approvable`).** `complete` is true only
  when the summary states every command that would run in full (with how and where it runs) and
  every path a file change would write:
  - *OpenCode*: known shell commands and path requests (edits, reads, searches, external folders);
    false for anything else.
  - *Codex*: commands with known folder (or input to a running command), requested network access
    and local host environment; host network requests; file changes naming every file without
    session-wide folder write grants.
  - *Claude Code*: whole JSON input, single-field built-in tools whose other inputs do not act, or
    Bash commands prefixed with `[in its shell, which started in the task's folder]` (with `, in the
    background` when backgrounded).
  - Any cut tool name or cut summary forces `complete` false (redacted held secrets still show as
    `[redacted: ...]` in a complete summary). `ApprovalView.approvable` mirrors `complete`;
    approving a non-approvable request is rejected with `approval_not_whole`, while denying remains
    admitted.
- **Sandbox boundary on approval (`approval_may_leave_sandbox`).** Declared on `RuntimeDescriptor`,
  never read from a request ([ADR 0033](../decisions/0033-approvals-declare-whether-commands-leave-the-sandbox.md)):
  - `true` on Codex, where an approved escalation runs outside its sandbox with the person's access
    and cannot be distinguished from an in-sandbox dangerous command in the request.
  - `false` on OpenCode when running inside Halora's Seatbelt profile
    ([ADR 0028](../decisions/0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md),
    [ADR 0048](../decisions/0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md)).
  - Absent (`unknown`, never `false`) on Claude Code, ACP agent apps, the mock runtime, and
    unsandboxed OpenCode; clients must never present an absent declaration as staying inside a
    sandbox.

## Contracts: one source of truth

Native tool results ([ADR 0031](../decisions/0031-record-bounded-native-tool-results-in-the-owner-journal.md))
retain final completion and optional command exit code separately from output or per-file patches.
Only the observed started tool's execution/turn/runtime and full project/workstream scope admit
the result. Foreign or stale tool completion cannot clear that correlation. New completion events
carry an optional `turn_id`; absent legacy values do not fabricate a result correlation. Replay
rebuilds accepted result summaries; a matching raw journal event that the projection rejected is
never served as evidence. Sixteen recent summaries retain no bodies; a lightweight accepted-event
index and indexed exact journal read keep older selected evidence readable.

Each native source text field above 64 KiB UTF-8 is omitted whole (`source_cut`). Exact held values are replaced
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
agent text is `reported`, `project.world.read` (and format 1's `project.world.observed`) holds a
description or why there is none and never both, a description gives each light of its scene once and each of its places at most once, and a client message (a command above all) nests at most 32 levels and
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

- Envelope (`EVENT_SCHEMA_VERSION = 1`), command (`COMMAND_SCHEMA_VERSION = 1`) and realtime
  protocol (`REALTIME_PROTOCOL_VERSION = 3`) versions in `packages/contracts/src/versions.ts` are
  independent integers.
- The journal schema is versioned by `PRAGMA user_version` (`6`) with forward-only migrations, each
  in its own transaction. A journal with a higher `user_version` is refused rather than misread.
- Migration 2 populates `model_ref: null` on stored `execution.start` commands inside
  `command.accepted` and `command.rejected` where absent
  ([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)).
- Migration 3 populates `principal: null` on stored `command.accepted` and `command.rejected` events
  where absent ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)) and
  re-applies migration 2's idempotent update for journals stamped at version 2 without `model_ref`.
- Migration 4 populates `location: null` on stored `project.created` events and `project.create`
  commands where absent, and `directory: null` (meaning not recorded) on stored `execution.created`
  events where absent ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)).
- Migration 5 populates `complete` on stored `runtime.approval.requested` events lacking it: `false`
  when the summary ends in ` [truncated]` or starts with OpenCode's `[whole command not known]`, and
  `true` otherwise.
- Migration 6 adds the `projection_checkpoint` table beside `events` so startup restores a verified
  projection checkpoint and folds only events after it, leaving `events` append-only
  ([ADR 0050](../decisions/0050-a-start-restores-the-projection-from-a-checkpoint-and-folds-only-the-journal-after-it.md)).
- Realtime protocol version 3 covers workspace requests, nullable check `drawn` and `verdict:
  'not_known'`, `source_cut` on tool results, host-answered and host-denied approval events
  (`run_temp_read`, `not_recorded`, `tool_name`), `execution.question.ended_by_host`, project and
  execution media (`project.media.*`, `execution.instruction.media_given`,
  [ADR 0066](../decisions/0066-a-persons-photo-or-video-goes-into-the-projects-folder-as-a-copy-without-its-place.md)),
  `Watch It Work` (`execution.files.writing`, `ExecutionView.writing`), and `WorkstreamView.usage`.
- Coordinated breaking changes migrate fixtures, the journal schema, generated bindings and
  documentation together.

## What is not journaled

Conclusions from Salidium and Seorak are read through on request and never journaled or replayed
([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)). Neither is the person's
catalog from Omnorium ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)): only the links the person makes between a project and one
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

A recorded run is one real run's journal prepared to be shown: how a person tries Halora without a
connected computer, including in the build for first-time users
([ADR 0035](../decisions/0035-a-recorded-real-run-lets-anyone-try-halcyonic-before-connecting-a-computer.md),
[ADR 0012](../decisions/0012-a-labeled-demonstration-runs-on-the-headset.md)).
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
  their makers' names;
- a here-document's body in a command's tool title (a tool call whose result is a command's): where
  the title's first line opens one (`<<` or `<<-` after white space, then its delimiter), the lines up
  to its terminator become `(the here-document is left out)`, and the terminator and what follows it
  are kept, because the body is the file the command writes (a starter's licence header names its
  maker), not what the agent said; a title that ends before its terminator keeps only its first line;
- the starter a project began from, whose id becomes `recorded-starter`, because a starter's id can
  name the kit it is built on.

The project folder's path is rewritten wherever it appears, in what the person and the agent said
too. Nothing else in instructions, agent messages, approval requests, tool titles, patches,
provenance, identifiers or times is edited. Every string and every object key is scanned. If any
still names a product, company or platform (the same list as the headset's `JudgeWordsTests`, and
runtime, model and product names in any case), a home or user folder, a private folder, the local
host, an address, a URL, an email, a credential shape or a word from the deny file, nothing is
written: the tool lists each event and field, never the text or the key, and the run is recorded
again. The deny file is required and lists at least the names of the person whose run it is, matched as whole words in any
case, since an agent quoting a commit's author carries them. Names that must not be published even in
the recorder's own list, such as other projects not yet public, stay out of the repository: the recorder
reads them from `.private/recorded-run-names` (one a line, `#` starting a comment), which git ignores, and
refuses them as it does the listed names, in any case at a word's start, saying only that a name kept out
of the repository was found. Where a checkout keeps that file, a test fails on any tracked file that
names one, listing the files and never the names. A run whose runtime is synthetic is
refused; the demonstration is the place for simulated work. A journal holding an event type the
recorder is not written for is refused whole, naming the type, since a new type may carry words no
rule knows, such as a private repository's name.

The recording is the demonstration's format at version 3, kind `recorded_run`
([XR_CLIENT.md](XR_CLIENT.md)). A control plane without runtimes is given the sanitized run
through its normal write path under virtual time, so every message is one its projection and
publisher computed. Waits between events are shortened to 2.5 seconds at most; every event keeps
the time it really happened. Where someone answered an approval, the recording holds before the
answer's command and holds only that answer. The runtime is listed under its neutral name, real,
able only to answer approvals. A run that asked the person a question is refused by the recorder.

The recorder's rules find only what they list, so a human reviewer reads the whole recording before
it is bundled or shared. The run's journal stays private, kept outside the repository. After a
change to the event format, the contracts or the pipeline, the run is recorded again from that
journal, and `pnpm judge:record ... --check` says whether the bundled file is still that run's
recording. Unlike the demonstration's check, `pnpm check` cannot run it, because the journal never
enters the repository; without the journal, a new run is recorded.

## General workspace request lifecycle

`workspace.request.created`, `workspace.request.changed` and `workspace.request.step_recorded` are
control-plane lifecycle facts recorded through `Recorder`. They retain the original principal and
exact work tuple: a project, a task and its execution, a project alone with `workstream_id` and
`execution_id` null for a read-only whole-project request, or nothing at all (all three null) for a
read-only request about all of the person's work
([ADR 0030](../decisions/0030-workspace-requests-use-a-journaled-agent-tool-loop.md)). Model replies
remain explicitly `reported`; a `responded` request means the conversation settled, not that software
work completed. Prepared mutation records admit only the supported send-instruction command and
retain its immutable identity/payload before dispatch, while `propose_command` steps record the
prepared `proposed_command` and any prerequisite `proposed_before` commands before anything acts
([ADR 0060](../decisions/0060-the-computer-says-what-can-be-done-here.md)). Delivery status stays
separate from the subsequent native turn outcome, which must be an observed lifecycle event the
projection applied. Ignored stale-sequence or mismatched-turn journal rows are never outcome
evidence.

The original observation position/active turn remains immutable; a separate monotonic observation
cursor makes bounded scans resumable. The latest 64 public receipts are bounded while prepared
action identities remain discoverable through journal replay. A control-plane restart interrupts
planning and requires explicit Resume with fresh original-principal/context checks; it never retries
a pending or unknown mutation. Runtime work may continue independently.

These event/command enum additions require regenerated consumers before updating the live host,
as described in [REALTIME.md](REALTIME.md#workspace-request-upgrade-order). Existing journals with no
request events replay unchanged; this does not make old clients accept new welcome catalogues.
