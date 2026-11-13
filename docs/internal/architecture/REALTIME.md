# REST and realtime

REST bootstraps, pages history and accepts commands. The WebSocket carries live state, events,
commands and acknowledgements. Why both, and why not SSE, polling, WebRTC or MQTT:
[ADR 0003](../decisions/0003-rest-bootstrap-and-websocket-realtime.md).

All payloads are defined in `packages/contracts` (`api.ts`, `realtime.ts`, `devices.ts`) and
published in the generated JSON Schema. On loopback, every request needs
`Authorization: Bearer <token>` except `/api/health`; on the network listener, a paired device's
credential instead (below, and [SECURITY.md](SECURITY.md)).

## REST

| Method and path | Returns |
| --- | --- |
| `GET /api/health` | `{status: 'ok'}`; no token needed, reveals nothing |
| `GET /api/snapshot` | `Snapshot`: journal identity, position, projects, workstreams, executions, commands (every pending one and the 50 whose outcomes came last, in journal order), runtimes |
| `GET /api/projects` | Projects at the current position |
| `GET /api/workstreams?project_id=` | Workstreams, optionally for one project |
| `GET /api/runtimes` | Runtime descriptors with capabilities; clients show only supported actions |
| `GET /api/locations` | `LocationsResponse`: where projects may live on the host, read from the file system on request and never journaled ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)). Each project root (`HALCYONIC_PROJECT_ROOTS`, as its real path) with its own `name`, `status` (`available`, or `missing` when it is no longer a folder), the visible folders directly inside it (`{name, path}`, no hidden folders or symbolic links, sorted, at most 200, found among at most the first 10,000 entries the file system returns) and `folders_truncated`, true when either limit was reached. `roots: []` means the host allows no folder yet. Paths and names are for display, and names are untrusted text |
| `GET /api/runtimes/:runtime_id/models` | `RuntimeModelsResponse`: the models the runtime lists now, from its own list, read through and never journaled ([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)); always 200 with an availability (`unavailable` carries the reason in words: `timeout` after 30 s, `invalid_models` for a list outside the contract, or the adapter's own code), 404 `models_not_listed` for a runtime whose `model_choice` is `none`, 404 `runtime_not_found` for an unknown one. Listing may start the runtime, so it can take seconds: fetch it when a person opens the choice, and never poll |
| `GET /api/starters` | `StartersResponse`: the starters a new project can begin from, each with its title and whether its dependencies are installed on this computer, read from disk on request and never journaled ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)) |
| `GET /api/projects/:project_id/checkpoints` | `CheckpointsResponse`: the project's checkpoints for Halcyonic's own undo, oldest first, from the projection ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)); `unavailable` with `undo_not_set_up` when the host has no `HALCYONIC_GIT_BIN`; 404 `project_not_found` |
| `GET /api/projects/:project_id/checkpoints/:checkpoint_id/restore-preview` | `RestorePreviewResponse`: what putting the checkpoint back would change now (at most 100 paths each changed back, removed and returned, with totals, and whether the project's own commit moved), read from the folder on request and never journaled; `unavailable` with a reason when undo is off or the folder cannot be read; 404 `checkpoint_not_found` for a checkpoint not the project's. Fetch it when a person opens Undo, and show it before they confirm; `review` in an available result is the digest a `project.restore` carries back ([ADR 0051](../decisions/0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md)) |
| `POST /api/projects/:project_id/location-review` | `LocationReviewResponse` for a `LocationReviewRequest`: the folder the project works in now, the one chosen, and `review`, the digest a `project.set_location` carries back; a paired device's move of a project that has a folder needs it (ADR 0051). Reads only; 404 for an unknown project |
| `GET /api/events?after=&limit=&workstream_id=` | Journal history after a position (limit 1 to 1000, default 200); a paired device's reads leave device events out |
| `GET /api/executions/:execution_id/understanding` | What Salidium says about the execution's session, read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability, 404 for an unknown execution. An available answer's `source.synthetic` is true only for a stand-in's, as in the recorded demonstration ([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md)) |
| `GET /api/executions/:execution_id/evaluation` | What Seorak measured about the execution's session (estimated cost, outcome, verification runs, and its activity when Seorak reports all of it: start, end, elapsed seconds, tool calls, prompts; `activity` is absent otherwise, unknown and never zero), read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability, 404 for an unknown execution. Each answer spends three of Seorak's 60 requests a minute, so fetch it on demand, for example when a workstream is opened, and never poll. `source.synthetic` as for understanding |
| `GET /api/usage-limits` | `UsageLimitsResponse`: the provider usage limits Seorak last observed, account wide, read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability. `available` carries at least one reading, and `complete`, false when Seorak read only some limits (it stopped at a result limit): the readings it returned are exact, and a window missing from them is unknown, never zero. Each reading has: the agent (Seorak's open id, and a label to show), the window (`rolling-5h` or `weekly`), the used percentage the provider reported, when it resets and when it was observed, Seorak's freshness flag, and an account that is always `unidentified`. A reading past its reset is never served; with none left the answer is `unavailable` (`no_current_reading`), never 0%. `unauthorized` covers a missing credential, one without `limits:read` (`insufficient_scope`) and a project- or date-restricted one (`outside_credential_restriction`); `unavailable` covers `not_captured`, a stopped or rate-limited Seorak, and `not_configured` where no source serves limits. The recorded demonstration holds one `available` answer, read through this route from its stand-in for Seorak and marked synthetic, with its times moved to the recording's start ([ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md)). One request of Seorak's 60 a minute: read it when a person opens Usage left, never on a timer. Not Workstream status, and not part of starting work |
| `GET /api/catalog` | `CatalogResponse`: every project the person has across their accounts, from their catalog, read through and never journaled ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)); always 200 with an availability. `available` is `reported` by the source and says how old it is (`sync`); each project's `local.project_ids` are the Halcyonic projects this journal links to one of its repositories, whatever the source says, and `suggestions` stay empty until the host matches folders. Without a catalog source the answer is `unavailable` (`not_configured`); a source that fails is `unavailable` (`source_failed`), and one outside the contract `incompatible`. No live source is built yet; the recorded demonstration's stand-in serves invented projects marked synthetic |
| `POST /api/commands` | Submits a `CommandEnvelope` (JSON only) |
| `POST /api/transcriptions` | Turns one clip of speech into a draft on the Mac ([ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md)); the only route that takes anything but JSON. The body is `audio/wav`, 16-bit mono PCM at 16 kHz, 0.5 to 30 s, at most 960,044 bytes (`TRANSCRIPTION_MAX_BYTES`). Answers `TranscriptionResponse`: `heard` with the text, its language (`en`) and the engine's name and version, or `nothing_heard` when the engine found no speech. The text is untrusted, a draft the person confirms before anything is sent. Refusals: 503 `transcription_unavailable` (voice not set up), 415 for another content type, 400 `invalid_audio` or `audio_too_short`, 413 past the byte limit, 429 `transcription_busy` (this principal's clip is in progress) or `rate_limited` (six clips reach the engine in each fixed minute), 503 `transcription_busy_on_mac` (another clip is being transcribed), 502 `transcription_failed` (the engine failed or ran past 15 s, or its transcript is longer than a draft). Neither clip nor text is journaled, stored or logged |
| `GET /api/companion` | `CompanionStatus`: whether Create's companion can be asked now ([ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)), read from Ollama's model list without asking the model. `available` names the model (an opaque string, served `this_mac`) and the most questions it asks (4); `unavailable` carries `companion_not_set_up` (no model named), `companion_not_running` (Ollama does not answer), `companion_model_missing` (not in Ollama's list) or `companion_model_not_local` (Ollama would pass it to another host: a cloud or remote model). Read it when Create opens, never on a timer |
| `POST /api/companion/replies` | One reply from Create's companion ([ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)). The body is a `CompanionRepliesRequest`: how Create began (`idea` or `help`), what the person wants (`next` or `proposal`), and the whole exchange so far (at most 20 messages and 24,000 characters, 2,000 for a message of the person's), since the host keeps no session. Answers `CompanionReplyResponse`: the reply (a line, the companion's view of the idea, then one question with at most 4 choices or a proposal of a project name and a first task), `provenance: reported` and the model that gave it. After 4 questions, or when the person asked for the recap, only a proposal is accepted. Refusals: 400 `invalid_companion_request` (with issues) or `invalid_exchange` (out of order or too long), 413 past 256 KiB, 429 `companion_busy` (this principal's turn is in progress) or `rate_limited` (12 replies a minute per principal), 503 `companion_not_set_up`, `companion_not_running`, `companion_model_missing`, `companion_model_not_local` or `companion_busy_on_mac` (another principal's turn is in progress), 504 `companion_too_slow` (no first token in 30 s, or no whole reply in 45 s; the request to Ollama is then closed, which stops it), 502 `companion_unreadable` (two replies outside the shape). Model text is untrusted and reported; neither the exchange nor the reply is journaled, stored or logged |
| `POST /api/pairing` | Loopback only. Opens a pairing window: `PairingOpenedResponse`, with the code, the window's status and where devices reach the network listener; 409 `network_off` while the listener is off |
| `POST /api/pairing/page` | Loopback only. `PairingPageResponse`: a link that opens the pairing page once, within a minute ([ADR 0047](../decisions/0047-pairing-starts-from-a-page-on-the-mac.md)); it opens no window and turns nothing on; 409 `page_off` where no page serves |
| `GET /api/pairing`, `DELETE /api/pairing` | Loopback only. The window's `PairingStatus` (never the code); `DELETE` closes it |
| `GET /api/devices` | Loopback only. `DevicesResponse`: every device paired, revoked ones included, and which have a realtime connection open |
| `POST /api/devices/:device_id/revoke` | Loopback only. Revokes the device (again is harmless) and answers its `DeviceView`; 404 for a device never paired |
| `POST /api/device/revoke` | Network listener only. The calling device revokes its own credential, as forgetting the control plane does; 204 |
| `GET /api/projects/:project_id/preview/...` | Network listener only, to a paired device. The project's preview relayed from its own port on 127.0.0.1, GET and HEAD only, while it serves the latest check, passed, with `reads_confined` ([ADR 0052](../decisions/0052-a-preview-reaches-a-paired-headset-through-a-loopback-relay.md)); 409 with the reason's code otherwise; only the file's own headers come back, with `Cache-Control: no-store` |
| `GET /api/device/listener` | Network listener only, to a paired device. `NetworkListener`: where it can reach the listener now, the computer's `.local` name first, so it finds the computer again at a new address (below); 503 `unavailable` before the listener listens |

Command submission answers honestly:

| Status | Disposition | Meaning |
| --- | --- | --- |
| 202 | `accepted` | Admitted and dispatched. Not yet done unless `command.status` says so. |
| 422 | `rejected` | Well formed but not admissible; the reason is journaled |
| 200 | `duplicate` | Already received; returns its current state |
| 409 | error `command_id_conflict` | The id was used for a different command |
| 400 | error `invalid_command` | Does not match the contract; `issues` name the fields |
| 415 | error `unsupported_media_type` | Not `application/json` |

Errors use one shape: `{error: {code, message, issues: [{path, message}]}}`.

## WebSocket: `GET /realtime`

JSON text messages, protocol version 1.

```text
client                                   server
  │ hello {protocol, client, resume}  ──>  │
  │ <── welcome {journal, head, resumed,   │
  │              command_policies}         │
  │ <── snapshot {snapshot}                │  unless resumed
  │ <── event {position, event, changes}   │  for every journaled event, in order
  │ command {command}                 ──>  │
  │ <── event ... (command.accepted, ...)  │
  │ <── command_ack {command_id, disposition, command}
  │ ping {nonce}                      ──>  │
  │ <── pong {nonce}                       │
```

- **hello first.** Anything else before `hello` is a fatal protocol error. A `hello` with another
  protocol version gets `unsupported_protocol` and the connection closes.
- **Command policies.** `welcome` lists the consequence category of every command type. Clients
  require an explicit, deliberate action before sending a `review_required` or
  `high_consequence` command, and treat a missing entry as requiring one. Every client gets the
  same policies until per-device authorization exists.
- **Snapshot and subscription are atomic.** The server takes the snapshot and subscribes in one
  synchronous step, so no event can fall between them.
- **Resume.** If the client's cursor names this journal and the current head, the server answers
  `resumed: true` and sends no snapshot. Otherwise it sends a fresh snapshot. Missed history is
  available from `GET /api/events`. Sending the missed events instead was weighed on 2026-11-10 and
  not built: each `event` carries the `changes` the projection computed when it was recorded, which
  are not kept, so a replayed event could not carry its own; and a snapshot is small and quick (352
  KiB built in 0.7 ms at 50,000 events, [control-plane-scale-2026-11-03.md](../validation/control-plane-scale-2026-11-03.md)).
- **Device events stay home.** `device.paired` and `device.revoked` are journaled like every
  event, but no client receives them over this stream, and a paired device does not read them from
  `GET /api/events` either, where `limit` counts only the events returned: which devices are
  paired is the owner's to know, on loopback. The gap they leave in positions is harmless, as any
  gap is.
- **Events carry their effects.** Each `event` message includes `changes`: the current state of
  every project, workstream, execution and command the event changed. Clients replace their copies
  wholesale and never re-implement the projection.
- **A project's view holds its check, preview and play (M1.8e, T2).** A project's view carries, each
  only when there is one, `check` (its latest check: `check_id`, `recorded_at`, the journal
  `position` at which it was recorded, `files`, and the findings as recorded), `preview` (the check
  its running preview serves, the loopback port and that check's `files`), `mac_play` (the check
  whose game plays on the Mac now and that check's `files`) and `share` (the check shared at a
  public address, the address, and that check's `files`; [ADR 0054](../decisions/0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md)). `files` is `'current'` when recorded
  while no turn is running in the project (a turn a restart cut short runs no longer, since no
  runtime session survives a restart; #13), `'changed'` once a turn's file reading or a restore
  changed files in the project's folder or the project was moved to another folder, and `'unknown'`
  while a turn is running or when a turn ended without a complete file reading, an incomplete
  restore stopped, or an execution's state became unknown. A check whose `files` is `'changed'` or
  `'unknown'` is stale or unknown, never treated as a passed check of the current files. After a
  turn that changed files in a starter project (`execution.files.observed` with files created,
  changed or deleted), the host submits `project.check` on its own (`received_via: 'internal'`),
  one check at a time per project, journaled like a client's check (A1); never for a world
  (ADR 0049), which has no build and whose `project.check` is refused. They leave
  the view when their stop does, so a headset that connects after a check still shows it. An absent
  member means there is none, never false. Nothing of a play's viewer (its link, code or session)
  is in the view.
- **A world project's view holds its world (ADR 0049).** `world` is there once the Mac has read the
  project's `world.json`: `description` (the newest valid one, kept while a later reading is
  refused, or null until one was valid), `refused` (why the file as last read is not a description,
  or null) and `observed_at`. A client validates the description again before it draws, since the
  generated bindings do not enforce ranges or patterns. Since 2026-11-12 a description is format 2:
  its scene, the scene's lights and air as the headset mixes them, and which of the scene's places
  is which project's; format 1 readings are no longer taken into the view (the Mac brings such a
  world forward and reads it again).
- **Acknowledgements.** `command_ack` reports `accepted`, `rejected`, `duplicate` or `conflict`.
  Because events are recorded while the command is handled, a client may receive the command's
  events before its acknowledgement; correlate by `command_id`.
- **Rejection codes.** A rejected command's record carries a code and a message in words.
  `device_revoked` rejects a command from a paired device revoked after its request or connection
  was authenticated. `review_required` rejects a paired device's `project.restore`, or its
  `project.set_location` of a project that has a folder, that carries no `review`; `review_stale`
  one whose `review` no longer describes what is true (ADR 0051). The code `demonstration` never comes from a control plane: only the XR
  client's recorded demonstration, which stands in for one on a device without one, answers with
  it. It means the
  command was sent to no control plane and no agent, however admissible it was in the recording,
  and the message says how the recording continues. It was added without a new protocol version
  because no control plane sends it, so no client of an older version can receive it
  ([XR_CLIENT.md](XR_CLIENT.md)).
- **Choosing where a project lives.** `project.create` carries `location`: null, or a choice of
  `{kind: 'existing_folder', root, folder_name}` (`folder_name` null for the root itself) or
  `{kind: 'new_folder', root, folder_name}` or `{kind: 'new_from_starter', root, folder_name,
  starter_id}`, a new folder that begins with a starter, refused with `capability_unsupported` for a
  starter not installed and for `project.set_location`, where `root` is a root's `path` exactly as
  `GET /api/locations` gave it and `folder_name` a folder directly inside it. A new folder's name
  matches `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`; an existing folder's is one visible path segment.
  Anything else is a 400 `invalid_command`. `project.set_location` `{project_id, location}` binds a
  project to another folder. The project's `location` then reads `{path, name, created}`. The host
  refuses a location with `location_not_allowed`, `location_missing` or `location_exists`, and a
  runtime whose descriptor has `uses_project_location` refuses a start in a project without one with
  `location_required`, or whose folder has gone with `location_missing`. Runtimes take no folder in
  their options; the execution's `directory` says where it runs ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)).
  `location` was added as an explicit null without a new protocol or schema version: a development
  build generated before it sends `project.create` without it and gets 400 until it is rebuilt.
- **An instruction while a turn runs, to a runtime that cannot steer** (Claude Code today, ADR 0056).
  `execution.send_instruction` is accepted and waits; it is not sent to the runtime then. Only on the
  person's own journal (`origin: live`), where the queue runs; on a recording's journal it is refused
  with `capability_unsupported`, as before.
  - At most 3 wait per execution; a fourth is refused with `invalid_state`.
  - The host says it on the runtime's descriptor: `RuntimeDescriptor.holds_instructions` is true where the
    queue runs (a live journal) for a runtime that cannot steer but can be instructed at rest, and is never
    taken from an adapter.
  - `execution.instruction.queued` (`command_id`, `position`) records it. `ExecutionView.queued_instructions`
    lists the waiting ones, oldest first, as `{command_id, queued_at}` without the words, and is absent when
    none waits.
  - After a turn that completed, the first is sent as an instruction at rest, and its command completes once
    the runtime confirms. The next waits for that turn. `execution.instruction.dequeued` (`command_id`) marks
    its send begun: it leaves `queued_instructions` then, and can no longer be dropped or failed unsent. A
    restart fails what still waits as `queued_stopped`, effect `none`.
  - Every one still waiting fails unsent, effect `none`: `queued_turn_failed` after a failed turn;
    `queued_stopped` after a Stop, or a turn interrupted or cut by a restart; `queued_session_lost`;
    `queued_files_restored` after a restore is accepted.
  - `execution.drop_queued_instruction` `{execution_id, command_id}` (low consequence) fails one that waits,
    with `queued_dropped`; one not waiting is refused with `invalid_state`.
  - While one waits, the project has a pending command, so the automatic check and the fix loop wait for the
    turn that takes the last one. A runtime that steers (Codex, OpenCode) takes the instruction in its running
    turn, as before.
- **A picture the person gives** (ADR 0058, amended). `POST /api/pictures` with `content-type` image/png, image/jpeg
  or image/webp and the bytes (at most 5 MiB) answers 201 `PictureGivenResponse` `{picture: {id, media_type,
  size_bytes}}`, 422 for what is not a whole picture, 413 past the size, 503 where none are kept. An attachment
  `{kind: 'given', picture: id}` then goes with an instruction as a folder's picture does, with no folder needed.
- **Pictures with an instruction** (ADR 0058). `execution.start` and `execution.send_instruction` take
  `attachments`: at most 4, each `{path, kind: "image"}`, a path of the project's folder.
  - Admission refuses them with `capability_unsupported` for a runtime whose descriptor lacks
    `takes_pictures`, and with `location_required` for a project without a folder.
  - The host reads each once when the instruction is sent: PNG, JPEG or WebP by its first bytes, whole by its
    structure, at most 5 MB, never through a link, a withheld name or Halcyonic's own data.
  - One it cannot read fails the command, effect `none`, nothing sent, with `picture_not_found`,
    `picture_withheld`, `picture_too_large` or `picture_not_a_picture` and words that name it.
  - `execution.instruction.pictures_read` (`command_id`, and each picture's `path`, `media_type`, `size_bytes`,
    `sha256`) records what was read, never the bytes, before the runtime is given them.
  - `RuntimeModel.picture_input` says whether the runtime's list says a model takes pictures; OpenCode refuses,
    with no effect, a model it lists as text only.
- **What can be done here** (ADR 0060). `GET /api/actions?project_id=&workstream_id=&execution_id=` (all
  absent for all work; a task's scope names its project and its current execution) answers `HereResponse`
  `{scope, position, actions}`: what the host would admit there now, in its order, the main action first.
  - Each `command` entry: `command_type`, `title`, `description`, `confirm` (`none`, `undo` with its `undo`
    command, or `review`), `sayable`, and `needs` (`name`, `kind`, `label`, `required`, `list`, `max`). A `list`
    entry opens one of the host's lists (a project's tasks, its versions).
  - It is computed by the one admission in a probe mode (`admitHere`): only what the scope binds is given, so a
    check of a value the person still gives waits for the whole admission, and the list never offers what that
    admission refuses for the state. The command service then leaves out what this host cannot do at all.
  - A scope whose ids do not hold together lists nothing; ids that are not ids are refused with 400. A client reads
    it again once the journal passes `position`.
  - `GET /api/actions/choices?list=&<the scope's ids>` answers `ChoicesResponse` `{scope, list, position, digest,
    rows, total}`: one of the host's lists, at most 32 rows, each `{index, kind, label, detail, theirs, actions}`,
    and never the id a row stands for. A row of `versions`, `secrets`, `queued_instructions`, `approvals`,
    `runtimes` or `catalog` names only the commands the whole admission takes it for, the host's checks included
    (an agent not kept to one folder starts no task that works in its own copy); a refusal about another need, such
    as a model still to choose, is not the row's. A row left with none says why in `detail`, the admission's words.
    Today: `tasks`, `versions`, `runtimes`, `approval_levels`, `share_ways`,
    `secrets`, `queued_instructions`, `approvals`, `decisions`, `questions`, and `pictures` (the folder's pictures
    as the pictures read finds them, each given to an instruction as `{path, kind: 'image'}`), and `models` (every
    model of every runtime that lists its own, each row's `detail` naming its runtime; the start's admission and
    the runtime check the pair against a fresh list. Where the runtime's latest execution failed with
    `credential_refused` since this host read its credentials, the hosted models (`served: remote`) of the same
    declared provider (`RuntimeModel.provider`; where the runtime names none, all its hosted models) are shown with
    no action and the detail says the key was refused, while another provider's and its models on this Mac stay
    offered; a refusal from before that is unknown, so they are offered and the detail says it. A model that names
    its provider is also asked of the provider itself before any start, by the same no-spend metadata read the
    assistant makes (`KeyChecks`, kept ten minutes per key): a refusal shows at once, and a key the provider takes
    settles a refusal from before), and `own_check_scripts` (each script by name with its command
    as read now; the choice carries the command's digest), and `folders` (first a new folder in each available root,
    then the roots' folders no project uses, newest change first; a used folder is never offered, and New Project's
    new folder takes its name from the project's, made folder-safe by the host as
    the field's `completes` in `COMMAND_FIELDS` says; Make VR Version takes a new
    folder row's root, as its field's `takes` says, and an existing folder is no place for one).
    `catalog` is every repository of the person's catalog as read now (archived ones left out, each row's detail
    its project's name), for Link to Catalog; a sample's rows offer nothing and say why, and with no catalog set up,
    or one that cannot be read, it answers 404 `read_unavailable`.
  - One preparation (`CommandService.prepare`) builds a command from `{scope, command_type, values, read}` for a
    person's press and the assistant's proposal alike: only a command the scope offers now; the scope's ids and the
    host's fills (`options: {}`); each need's value, a choice by its index in a list whose digest is still the one
    read (`choices_changed` otherwise); an id given for any other field is refused. A review field is left for the
    review. The command meets its own validator there, and the whole admission when submitted.
  - One review (`buildReview` in core/reviews.ts) for every command whose `confirm` is `review`: `ActionReview`
    `{command_type, digest, confirm_words, what_happens, facts (at most 4), items, items_total, warnings}`, every
    word the host's, kinds from `ReviewKind`. One reader per command type: Send and Publish read through the same
    functions their routes answer with, Restore through its preview, Add from the task's copy's reading, Change
    Folder, Project Rules, Share and Choose Check from their own facts. The digest is the one the button's own
    review gives (ADR 0051), so a command carrying it back is admitted exactly as from a button. Add's review warns
    (`provider_declaration`) for each changed file that is a hosted provider's declaration the host would read
    (ADR 0061): a new address its key goes to, a key kept or sent another way, a provider added or removed; where
    more files changed than were listed, it says a provider among them is not looked for
    (`provider_declaration_unread`).
  - Take Down is one press (R1): the preparation fills its `commit` from the host's own record of what it
    published, never from a proposer, and its admission refuses one published again since. Remove Secret is
    reviewed (`review_required`, confirm `review`), since its value cannot be brought back; Save a Copy is done on
    its press (`low_consequence`). A reviewed policy with one press stays only where the press is the decision:
    Stop, Approve or Deny, a secret typed in its own field, and Take Down.
  - A starter the host's catalog cannot read has no build to check (AUDIT2 S1).
- **A project's own rules** (ADR 0059). `GET /api/projects/:project_id/rules` reads the project's `AGENTS.md`,
  else its `CLAUDE.md`, at its folder's top: `file`, `state` (`none`, `off`, `on`, `changed`, `too_large`,
  `unreadable`), `size_bytes`, the `text` whole (at most 32 KB) and its `review`.
  - `project.rules.set` `{project_id, on, review?}` (review required) turns them on with the review of the text
    as it reads now (`review_required` without one, `review_stale` with an old one), or off.
    - A project without a folder is refused with `location_required`; one without a readable rules file with
      `invalid_state`.
  - `project.rules.updated` (`on`, `file`, `sha256`, `changed`) records it, never the text. `ProjectView.rules`
    `{file, sha256}` is there while on.
  - At each `execution.start` the host reads the file again. While its digest is the one chosen, the text goes
    before the person's words, marked as the project's own, and `execution.rules_given` (`file`, `sha256`) records
    it.
  - A changed or missing file turns them off (`project.rules.updated` with `changed: true`), and the task starts
    without them.
- **Putting a project back.** `project.restore` `{project_id, checkpoint_id, review?}` is
  `review_required`: a client sends it only after the person has read the preview, with the
  preview's `review`, which a paired device must carry; a restore whose preview no longer matches its
  `review` changes nothing and ends as `project.restore_incomplete`, failure `review_stale`. It is refused with
  `capability_unsupported` when undo is off, `checkpoint_not_found` for a checkpoint not the
  project's, and `invalid_state` while a task in the project is starting, running, verifying or
  waiting, is in an unknown state other than after a restart, or another command in the project
  waits for its outcome; while it runs, `execution.start` and `execution.send_instruction` in the
  project are refused with `invalid_state`. It completes, with `{kind: 'project_restored',
  checkpoint_id}`, only after `project.restored`, which the host journals once it has read every
  file again and found the folder equal; otherwise `project.restore_incomplete` and a failure,
  effect `unknown` once a file was written
  ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)).
- **Checking a new app.** `project.check` `{project_id}` asks the host to check a project made from a
  starter: type check, build, and the built copy opened for 10 s in its sandboxed browser. It
  completes with `{kind: 'project_checked', check_id, passed, ready}` once `project.check.recorded` is
  journaled, whatever the check found; `passed` never means the game works, and `ready` (absent on
  checks from before M1.8b) adds the laptop and VR-entry steps, never a headset. `project.preview.start`
  `{project_id}` serves the built copy of the latest passed check on a loopback port and completes
  with `{kind: 'preview_started', check_id, port}` only once the server itself answers with that copy;
  clients open `http://127.0.0.1:port`, never `localhost`. One start per project at a time, and none
  while its check runs; `project.preview.stop` stops it. Both are refused
  with `capability_unsupported` when checks are off and `invalid_state` otherwise as DOMAIN_MODEL.md
  says ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)).
- **Playing on the Mac.** `project.open_on_mac` `{project_id}` plays the copy the running preview
  serves in the host's contained browser and completes once `project.mac_play.started` is journaled.
  The host then opens the play's viewer in the Mac's default browser: a server of its own on
  127.0.0.1, at a port of 47940 to 47949, apart from this protocol, whose link carries a one-time
  code and never the access token. The viewer streams the frames over its own WebSocket and takes
  only mouse and key input. A stream that drops, as on a reload, may come back within 5 s, with the
  same cookie and from the same origin, and the game plays on; only a longer absence ends it. Before
  the viewer closes it tells its page why the game ended, and it tells the page the game's sound as
  the computer shows it: playing, paused, or none at all.
  It is refused with `capability_unsupported` when no browser for it is set up, and `invalid_state`
  otherwise as DOMAIN_MODEL.md says. The play stops, and `project.mac_play.stopped` says why, when
  its viewer closes, when no viewer comes within 60 s, with the preview, on a newer check and with
  the host (P-A).
- **Sharing a game.** `project.share.start` `{project_id, way}` gives the project's running preview
  an address: `link`, a temporary public `https://` one; `wifi`, the Mac's own on its local network,
  over plain `http://`, from which a game cannot enter VR. It completes with `share_started`
  (`check_id`, `url`, `way`) once the address answered; `project.share.stop` ends it. It fails
  `unavailable` when no previews or, for a link, no pinned tunnel program is set up, `no_network`
  off any network, and is refused `invalid_state` otherwise as DOMAIN_MODEL.md says. The share stops
  with the preview, its tunnel or listener and the host too, and `project.share.stopped` says why.
- **Choosing a model.** `execution.start` carries `model_ref`: null, or a model's reference exactly
  as `GET /api/runtimes/:runtime_id/models` gave it. A start without one on a runtime whose
  `model_choice` is `listed` is rejected with `model_required`, so a model the person did not
  choose never runs. A choice for a runtime whose `model_choice` is
  `none` is rejected with `capability_unsupported`, and one the runtime does not list with
  `invalid_runtime_options`; a model that leaves the runtime's list before the start fails it with
  `model_unavailable`, before anything runs. The field was added as an explicit null without a new
  protocol or schema version, because no client in the field sends `execution.start`; commands
  stored before it read as `model_ref: null`
  ([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)).
- **Answering an agent's question.** An execution's view lists `pending_questions`, each
  `{question_id, prompts, answerable, asked_at}`; a prompt is `{key, header, text, options:
  [{label, description}], multiple, free_text, secret}`, all of it the agent's words. A view lists
  at most three pending questions, the answerable ones first, then the others, each oldest first;
  each other one keeps the execution waiting and shows once one shown is resolved. Only questions
  shown can be answered, and only they are named in the attention reasons. A question whose text,
  keys and options together pass 16,000 characters arrives shortened, each cut marked, and cannot
  be answered.
  `execution.answer_question` `{execution_id, question_id, answers: [{key, selected, text}]}`
  answers every prompt once, by its `key`: `selected` holds offered labels (at most one unless
  `multiple`), `text` typed words (only when `free_text`), and a question that takes one answer gets
  a label or text, not both. It is rejected with `question_not_found` when the question is not
  pending, `invalid_answer` when the answers do not fit the prompts, and `capability_unsupported`
  when the runtime cannot take answers, the question is not `answerable`, or any prompt is marked
  `secret`. A refused answer is journaled, and sent to clients, with its keys only. There is no command to dismiss a question: `execution.interrupt` stops
  the turn and withdraws it. A client sends an answer only when the person presses the control that
  sends it ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)).
- **Errors.** `error {error: {code, message, issues}, fatal}`. Invalid JSON or an invalid message
  after `hello` is not fatal, nor is a message nested more than 32 levels deep. A message the
  control plane fails to handle at all is answered with the non-fatal `message_not_handled`, so no
  message can stop it. Fatal errors close with code 1008. A command the control plane fails
  to handle is answered with the non-fatal `command_not_handled` instead of an acknowledgement;
  whatever it recorded before failing stays journaled, so a client reads the command's record
  before sending it again. A rejection's or failure's message quotes at most the start of what the
  client sent, and is cut to the contract's 2000 characters with an ellipsis.
- **Liveness.** The server pings every 15 seconds and drops a client that misses a pong.
- **Backpressure.** A client more than 8 MiB behind is closed with code 1013 and must reconnect
  and resynchronize.
- **Limits.** Messages over 256 KiB are refused.

## Client reconnection

Networks fail, headsets sleep and focus changes. A reconnected socket does not mean the client is
synchronized. After any disconnect:

1. Reconnect with backoff.
2. Authenticate (bearer token on the upgrade request).
3. Send `hello` with the last applied `{journal_id, position}`.
4. Apply the snapshot if one arrives; otherwise keep the current state.
5. Continue applying `event` messages in position order; a gap in positions is harmless.

A different `journal_id` means a different journal (for example a fresh data directory or a
fixture replay): discard local state and apply the snapshot.

A client applies a snapshot whenever one arrives, not only after `welcome`. A control plane sends
one only there, but the recorded demonstration sends its beginning's snapshot again, on the same
connection, each time it starts again. That snapshot names the same journal at an earlier position:
the client's history and commands from after that position no longer apply, and the XR client
core reports it as a rewind (`StateChanges.Rewound`).

## The network listener

With `HALCYONIC_NETWORK_HOST` set, the control plane also listens for paired devices, TLS only,
with its own certificate ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)).
It serves `GET /api/health`, the pairing WebSocket `GET /pair`, and to a paired device's credential
the REST reads, `POST /api/commands`, `POST /api/device/revoke`, `GET /api/device/listener` and `GET /realtime`, exactly as on
loopback, with the device as every command's principal. It serves nothing that manages devices,
and it never accepts the access token. `Host` must be an IP address or a `.local` name with the
listener's port.

A device refuses any certificate but the one it pinned when it paired, before it sends anything.
A refused upgrade answers with the error shape above, such as 401 `device_revoked`, so a device
can say why.

**Finding the computer again.** A pairing keeps the address the person typed. The listener's
addresses (`NetworkListener.addresses`, in the pairing window's answer and in
`GET /api/device/listener`) are read anew each time: for a wildcard host the computer's `.local`
name first, its Bonjour name (`scutil --get LocalHostName`) in lower case, when it has one, then
the interfaces' addresses as they are now; for a listener bound to one address, that address. After
each realtime connection opens, and at most every ten minutes, the XR client core asks
`GET /api/device/listener` and keeps what it says with the pairing, IP addresses and `.local` names
only, at most eight. Each connection, realtime or REST, tries the address that answered last, the
typed one, then those, in order, each under the same pin, three seconds each while another is left
to try, and names the address it dialled in `Host`. Something else at an old address gets a TLS
handshake it cannot complete and nothing more. Whether Horizon OS resolves `.local` names is not
yet checked on a headset ([network-pairing.md](../validation/network-pairing.md)).

Revoking a device applies to what it already has open, not only to its next request:

- Its realtime connections handle nothing more from the moment the revocation is journaled, not
  even messages already received, and are sent nothing more but a close frame with code 1008. A
  second later they are cut off, answered or not.
- A command it sent in a request or on a connection authenticated before the revocation is
  rejected where it would act, journaled as `command.rejected` with `device_revoked`.
- Whatever a route answers to such a request, such as one whose body arrived after the
  revocation, is replaced by 401 `device_revoked`. Only `POST /api/device/revoke` answers the
  device that revoked itself.

The listener waits 5 seconds for a TLS handshake and 10 for a whole request, headers and body,
closes a connection on which nothing moves for 60 seconds, which outlasts the slowest route's
answer, or idle between requests for 5, and holds 32 connections at once, 8 of them from any one
address. A WebSocket it closes waits a second for the client's answer. A device holds
at most 4 realtime connections; another is refused with the fatal error `too_many_connections`.

## Pairing: `GET /pair`

JSON text messages on the network listener, pairing protocol version 1, one exchange per
connection, which the control plane closes after its answer. Only while the owner has a window
open (`pnpm pair`); otherwise the upgrade is refused with 403 `pairing_closed`, and a flood from one
address with 429. The window derives the salt and the SRP verifier from the code once, when it
opens; each exchange draws only its own secret `b`, so its work does not depend on the code.

```text
device                                            control plane
  │ pair_request {protocol, device_label}   ──>     │
  │ <── pair_challenge {salt, server_public}        │  SRP-6a: the window's salt, a fresh B
  │ pair_proof {client_public, proof}       ──>     │  A, and the device's proof
  │ <── pair_accepted {device_id, credential, proof}│  or pair_refused {error, attempts_left}
```

- SRP-6a as RFC 5054 section 2 defines it, in its 3072-bit group with SHA-256, the user name
  `halcyonic pairing` and the eight-digit code as the password. `A` and `B` are sent padded to
  384 bytes, every binary value in standard base64.
- Both proofs are HMAC-SHA-256 under `K = H(PAD(S))` over a transcript: SHA-256 of
  `"halcyonic pairing 1\0"`, the label's length (4 bytes, big-endian) and UTF-8, the salt,
  `PAD(A)`, `PAD(B)` and the SHA-256 of the TLS certificate (the device's as it saw it, the
  control plane's its own). The device's proof is keyed with the label `client proof`, the control
  plane's with `server proof` and also covers the device's proof, the device id and the sealed
  credential. `credential` is the 32 credential bytes XORed with the HMAC labeled `credential`.
- A wrong code, an intercepted connection, or an `A` of 0 mod N all answer `wrong_code` with the
  attempts the window still allows; three close it. Other refusals: `pairing_closed`, `busy`,
  `too_many_requests`, `timeout` (30 seconds per exchange), `unsupported_protocol`,
  `invalid_message`. None of them spends an attempt; the window counts them by address and reason,
  with exchanges that closed before a proof (`abandoned`), in `refusals` of `GET /api/pairing`, and
  `pnpm pair` prints each, so the owner sees something holding pairing up.
- The device keeps the credential as `hlcd_` and its base64url, and presents it as a bearer token.
- `fixtures/pairing/vectors.json` holds a complete exchange for fixed inputs, which both
  implementations reproduce.


## Scoped recorded tool results

`GET /api/executions/:execution_id/results` takes exactly `journal_id`, `project_id` and
`workstream_id`. Its `ToolResultsResponse` contains that full execution tuple, support availability,
total accepted count, earlier count and at most sixteen body-free summaries, newest journal position
first. `total - earlier` equals the returned count. `available` means the registered descriptor
declares result reporting, not that its connection is live; `unsupported` is a false declaration,
`unknown` is an absent legacy declaration, and `unavailable` means no registered adapter. Execution
connection/status is separate. Accepted historical evidence remains readable in every case, by id
within the bound below.

`GET /api/executions/:execution_id/results/:event_id` takes the same exact query and returns one
`ToolResultResponse` with full scope, original summary and bounded recorded result. It requires
the projection's accepted observed event, then cross-checks exact journal identity, position,
scope, runtime and provenance. It neither scans source files nor calls a runtime. Recent-list
eviction does not make an exact accepted event disappear, but only the last 256 accepted results of an
execution stay readable by id (`TOOL_RESULT_HISTORY`, oldest dropped): an older id is the same 404, as
an unknown one. The list only ever names the last sixteen. Both routes require the authenticated
owner-journal principal (local owner or still-active paired device), current journal and the task's
current execution; malformed/extra query fields are 400, unavailable/foreign scope or event is
the same 404. Existing network response revocation applies. Missing routes on older hosts are
unavailable; clients must not guess another execution, event or newest result after an await.

These reads grant no command authority. See [ADR 0031](../decisions/0031-record-bounded-native-tool-results-in-the-owner-journal.md)
for privacy, capture and source-memory limits and the coordinator's separate bounded selectors.

## Project reads

Five read-only routes let the headset see a project's work and history for any runtime. Each takes `journal_id`
(the current journal) and requires an owner-journal principal; a malformed id, path or extra query
field is 400, and anything not found, not allowed or not set up is the same 404 `read_unavailable`.
Every text in them is outside words, from the project's files or its build, as `source` says
(`project_files`, `check_output`), and loses exact copies of the secrets Halcyonic holds (SECURITY.md).

- `GET /api/projects/:project_id/history?limit&before`: the project's history, newest first, at
  most `limit` entries (1 to 50, 20 when absent), older than the entry `before` names (an unknown one
  is the 404); `more` says older ones exist and `total` counts them all. An entry is a `version` (its
  `CheckpointView`, now with the `turn_id` of an `after_turn` one; whose `folder`, the project's or a
  task's own copy; the `previous` version of that folder and `changed_files` since it, null for a
  folder's first version or when the store cannot tell), `not_kept` (a checkpoint that failed),
  `restored` (with totals and the `before_restore_checkpoint_id` that undoes it) or
  `restore_incomplete`. Kept in the projection, so a headset that connects later still sees the
  restores. A version the host no longer keeps (`project.checkpoints.pruned`, ADR 0036) stays in the
  history with `pruned_at`, and is gone from `/checkpoints`: it can no longer be put back or compared.
  `unavailable` with `undo_not_set_up` without undo.
- `GET /api/projects/:project_id/changes?from&to`: the files that changed between two of the
  project's own checkpoints (both in its journal), each `added`, `changed` or `removed` with its
  lines added and removed (null for a binary file), sorted by path, at most 200, `files_total` and
  `complete` saying when there were more. A task's change is its `before_task` checkpoint to its
  last `after_turn` one. With `to` left out (the response's `to` is then null), `from` is compared
  with the folder it was taken of as it is now: that folder is read into a tree in Halcyonic's
  checkpoint store, as a checkpoint is and within a checkpoint's file and byte bounds, after any
  checkpoint or restore of the project already under way, and no checkpoint is recorded. A folder
  no longer there, no longer allowed or past the bounds is the 404. That read hashes every file, so
  it is shared: callers while one runs get its answer, and a finished one is kept for five seconds
  while nothing new is journaled (a change the journal does not see shows after that). Ask for it on
  the person's press, never on each live event. Off without undo (`HALCYONIC_GIT_BIN`).
- `GET /api/projects/:project_id/changes/file?from&to&path`: one of those files as a unified diff, at
  most 64 KiB and 2,000 lines, `complete` false when cut; null for a binary file and for a file
  whose name says it may hold secrets (`withheld`). A file that did not change is 404. `to` left out
  compares with the file as it is now, as above.
- `GET /api/projects/:project_id/file?path`: one file of the project's folder as it is now, a
  project-relative path: `text` (valid UTF-8 without NUL, at most 256 KiB, given whole), `binary`,
  `too_large` or `withheld`, with its size and modification time; never through a link, outside the
  folder or inside Halcyonic's own data. It reads the project's own folder only, never a task's copy
  (ADR 0045): a task's work not yet added is not there, so a screen must not show this file as the
  task's.
- `GET /api/projects/:project_id/pictures`: the pictures of the project's folder an instruction may carry
  (ADR 0058), sorted by path: the first 200 with their path, `media_type` (PNG, JPEG or WebP by their first
  bytes), size and modification time, and `pictures_total`. Never larger than 5 MB, through a link, a hidden
  or withheld name, `node_modules`, or Halcyonic's own data.
- `GET /api/projects/:project_id/checks/:check_id/output?step=typecheck|build`: a step of one of the
  project's checks as its own folder holds it, its exit (null when it did not run) and at most 200
  lines of at most 300 characters, cleaned as the agent's copy is: no terminal sequences or unseen
  characters, the project's folder as `.`, Halcyonic's data folder and the home folder by name.

## Send to GitHub's review

- `POST /api/projects/:project_id/send-review` [ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md), with the body
  `{title?, visibility?, repository_name?}` the press will send (an empty one takes the host's own): what a send would do, read
  before the press, with the title as it goes and, for a folder that is no repository, `creates` (the new
  repository's name and who can see it): `available` with the repository (owner/name only), the branch and its last
  commit, the new branch, the files with how each changed, `links_left_out` (links in the folder
  that point outside it, never sent), whether a review request can be opened there (`review_request`), and the `review` digest `project.send` carries back; or `unavailable` with why, in the
  product's words. Nothing leaves the Mac.

- `POST /api/projects/:project_id/publish-review`, with the body `{make_public?}`: what Publish would do, with
  who can see the repository as far as this Mac knows and whether the press makes it public first: `available` with the
  repository (owner/name), the check, how many of its built files go, `links_left_out` (links in the
  built copy that point outside it, never published), the commit of the last publish
  it builds on, the expected address and the `review` digest `project.publish` carries back; or
  `unavailable` with why. Nothing leaves the Mac.
- `POST /api/projects/:project_id/secrets` (ADR 0057): sets a project secret, write-only, from this Mac
  or a paired device. Body `ProjectSecretSetRequest` (`command_id`, `issued_at`, `client`, `name`, and
  `value`, 8 to 8192 characters). The Mac stages the value and submits the name-only `project.secret.set`
  as the caller; the answer is a `CommandSubmissionResponse` (202, 200 for a retried id, 422 refused or
  invalid in words, 400 not the contract, never repeating the value), and 503 where no secrets are kept.
  No route reads a value; names are on the project's view.
- `GET /api/projects/:project_id/published/code`: the published address as a code to scan
  (`{ check_id, address, code }`), which the headset draws, never the address as text. Not
  published, or no address known: 404 `not_published`.

## Sharing reads

Two read-only routes for the headset's Share card ([ADR 0054](../decisions/0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md)), for the Mac's own client and a paired
device alike:

- `GET /api/sharing`: which ways of sharing work on this Mac now, `link` and `wifi`, each `ready`,
  `not_set_up` (no pinned tunnel program for a link, or no previews at all), `no_network` (the Mac
  is on no private network) or `unknown` (the host cannot tell). Read when the card opens. Never a
  promise: a start that fails still says why.
- `GET /api/projects/:project_id/share/code`: the project's live share as a code to scan,
  `{ check_id, way, code: { size, rows } }`. The code is a QR code at error correction level M,
  `size` cells on a side (21 to 69), each row a string of `1` (dark) and `0` (light). The headset
  draws it with a light margin of 4 cells. It never draws a link's address as text: the address
  carries the tunnel service's name. No share is 404 `share_not_found`, a malformed id 400.

## Workspace request upgrade order

Since 2026-11-10 `workspace.request.submit` may name a project alone (`workstream_id` and
`execution_id` both null) for a read-only whole-project request, or nothing (all three null) for a
read-only request about all of the person's work; any other mix is refused. A request view's and request events' task fields are then null, so clients that read them
need the regenerated bindings first.

The journaled general request coordinator adds `workspace.request.submit`, `.resume` and `.cancel`
to every welcome policy catalogue, and adds request lifecycle events to realtime/history. An older
generated C# client can reject these enum variants during welcome before a request is submitted.
These additions are coordinated development changes, not a promise of backward compatibility.
Install and validate the regenerated client against an isolated updated host first, then update the
live control plane. Do not restart the current live host underneath an older installed client.

`GET /api/workspace/agent` verifies configured local model/tool availability without inference.
A missing route on an older host is unavailable, and request words remain an ordinary local draft.
`GET /api/workspace/requests/:request_id` reads the exact original-principal request; no newest
request is guessed. Clients compare the returned journal/project/task/execution tuple with their
fresh bound selection before offering Resume/Cancel. Restoration never submits or grants input,
reading or confirmation authority. Command acceptance creates direction, not task completion.


`GET /api/workspace/commands/:command_id` recovers one workspace request command for its original
journaled principal, returning `{journal_id, command}` with the existing `CommandView` only.
It works after the command leaves the snapshot's recent-command window and after journal replay,
even without an active coordinator. It never returns the submitted envelope or text. A malformed
ID is 400; unknown, foreign, legacy unowned and non-workspace-command IDs share the same 404.
Device reads require the original still-active device; the loopback principal cannot read a
device-owned command through this route. Existing authenticated journal visibility is unchanged.
A missing command or lost response does not authorize resubmission or a replacement command ID.
Clients must verify command ID/type and the returned journal/project/task/execution tuple before
using its request reference; this GET grants no action authority.

### Delegated request direction

Each `continue_task` instruction includes the original person request, all completed human Resume
messages in their recorded order, and a separately labelled coordinator follow-up. The complete
composition receives the existing held-secret replacement and must validate against the unchanged
runtime instruction contract. Human direction is never silently cut to make a model follow-up fit.
When the composition does not fit, no runtime command is prepared or dispatched; the request waits
for a person with an explicit receipt asking for a shorter new request. Runtime observations do not
automatically resume that refusal. These words preserve instruction context, not an enforced file
write boundary or a guarantee that the runtime follows them.

Request Actions distinguish a confirmed read, recorded person direction, and a finished native
turn. A finished-turn heading explicitly leaves software changes unverified. Tool outcomes and
recorded evidence remain separate: a completed turn can contain a failed command, and no native
patch result does not establish that a command left the working tree unchanged.
