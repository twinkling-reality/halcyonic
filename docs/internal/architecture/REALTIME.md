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
| `GET /api/snapshot` | `Snapshot`: journal identity, position, projects, workstreams, executions, recent commands, runtimes |
| `GET /api/projects` | Projects at the current position |
| `GET /api/workstreams?project_id=` | Workstreams, optionally for one project |
| `GET /api/runtimes` | Runtime descriptors with capabilities; clients show only supported actions |
| `GET /api/locations` | `LocationsResponse`: where projects may live on the host, read from the file system on request and never journaled ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)). Each project root (`HALCYONIC_PROJECT_ROOTS`, as its real path) with its own `name`, `status` (`available`, or `missing` when it is no longer a folder), the visible folders directly inside it (`{name, path}`, no hidden folders or symbolic links, sorted, at most 200, found among at most the first 10,000 entries the file system returns) and `folders_truncated`, true when either limit was reached. `roots: []` means the host allows no folder yet. Paths and names are for display, and names are untrusted text |
| `GET /api/runtimes/:runtime_id/models` | `RuntimeModelsResponse`: the models the runtime lists now, from its own list, read through and never journaled ([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)); always 200 with an availability (`unavailable` carries the reason in words: `timeout` after 30 s, `invalid_models` for a list outside the contract, or the adapter's own code), 404 `models_not_listed` for a runtime whose `model_choice` is `none`, 404 `runtime_not_found` for an unknown one. Listing may start the runtime, so it can take seconds: fetch it when a person opens the choice, and never poll |
| `GET /api/starters` | `StartersResponse`: the starters a new project can begin from, each with its title and whether its dependencies are installed on this computer, read from disk on request and never journaled ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)) |
| `GET /api/projects/:project_id/checkpoints` | `CheckpointsResponse`: the project's checkpoints for Halcyonic's own undo, oldest first, from the projection ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)); `unavailable` with `undo_not_set_up` when the host has no `HALCYONIC_GIT_BIN`; 404 `project_not_found` |
| `GET /api/projects/:project_id/checkpoints/:checkpoint_id/restore-preview` | `RestorePreviewResponse`: what putting the checkpoint back would change now (at most 100 paths each changed back, removed and returned, with totals, and whether the project's own commit moved), read from the folder on request and never journaled; `unavailable` with a reason when undo is off or the folder cannot be read; 404 `checkpoint_not_found` for a checkpoint not the project's. Fetch it when a person opens Undo, and show it before they confirm |
| `GET /api/events?after=&limit=&workstream_id=` | Journal history after a position (limit 1 to 1000, default 200); a paired device's reads leave device events out |
| `GET /api/executions/:execution_id/understanding` | What Salidium says about the execution's session, read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability, 404 for an unknown execution. An available answer's `source.synthetic` is true only for a stand-in's, as in the recorded demonstration ([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md)) |
| `GET /api/executions/:execution_id/evaluation` | What Seorak measured about the execution's session (estimated cost, outcome, verification runs), read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability, 404 for an unknown execution. Each answer spends three of Seorak's 60 requests a minute, so fetch it on demand, for example when a workstream is opened, and never poll. `source.synthetic` as for understanding |
| `GET /api/usage-limits` | `UsageLimitsResponse`: the provider usage limits Seorak last observed, account wide, read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability. `available` carries at least one reading, and `complete`, false when Seorak read only some limits (it stopped at a result limit): the readings it returned are exact, and a window missing from them is unknown, never zero. Each reading has: the agent (Seorak's open id, and a label to show), the window (`rolling-5h` or `weekly`), the used percentage the provider reported, when it resets and when it was observed, Seorak's freshness flag, and an account that is always `unidentified`. A reading past its reset is never served; with none left the answer is `unavailable` (`no_current_reading`), never 0%. `unauthorized` covers a missing credential, one without `limits:read` (`insufficient_scope`) and a project- or date-restricted one (`outside_credential_restriction`); `unavailable` covers `not_captured`, a stopped or rate-limited Seorak, and `not_configured` where no source serves limits. The recorded demonstration holds one `available` answer, read through this route from its stand-in for Seorak and marked synthetic, with its times moved to the recording's start ([ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md)). One request of Seorak's 60 a minute: read it when a person opens Usage left, never on a timer. Not Workstream status, and not part of starting work |
| `GET /api/catalog` | `CatalogResponse`: every project the person has across their accounts, from their catalog, read through and never journaled ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-phat-porcupine.md)); always 200 with an availability. `available` is `reported` by the source and says how old it is (`sync`); each project's `local.project_ids` are the Halcyonic projects this journal links to one of its repositories, whatever the source says, and `suggestions` stay empty until the host matches folders. Without a catalog source the answer is `unavailable` (`not_configured`); a source that fails is `unavailable` (`source_failed`), and one outside the contract `incompatible`. No live source is built yet; the recorded demonstration's stand-in serves invented projects marked synthetic |
| `POST /api/commands` | Submits a `CommandEnvelope` (JSON only) |
| `POST /api/transcriptions` | Turns one clip of speech into a draft on the Mac ([ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md)); the only route that takes anything but JSON. The body is `audio/wav`, 16-bit mono PCM at 16 kHz, 0.5 to 30 s, at most 960,044 bytes (`TRANSCRIPTION_MAX_BYTES`). Answers `TranscriptionResponse`: `heard` with the text, its language (`en`) and the engine's name and version, or `nothing_heard` when the engine found no speech. The text is untrusted, a draft the person confirms before anything is sent. Refusals: 503 `transcription_unavailable` (voice not set up), 415 for another content type, 400 `invalid_audio` or `audio_too_short`, 413 past the byte limit, 429 `transcription_busy` (this principal's clip is in progress) or `rate_limited` (six clips reach the engine in each fixed minute), 503 `transcription_busy_on_mac` (another clip is being transcribed), 502 `transcription_failed` (the engine failed or ran past 15 s, or its transcript is longer than a draft). Neither clip nor text is journaled, stored or logged |
| `GET /api/companion` | `CompanionStatus`: whether Create's companion can be asked now ([ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)), read from Ollama's model list without asking the model. `available` names the model (an opaque string, served `this_mac`) and the most questions it asks (4); `unavailable` carries `companion_not_set_up` (no model named), `companion_not_running` (Ollama does not answer), `companion_model_missing` (not in Ollama's list) or `companion_model_not_local` (Ollama would pass it to another host: a cloud or remote model). Read it when Create opens, never on a timer |
| `POST /api/companion/replies` | One reply from Create's companion ([ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)). The body is a `CompanionRepliesRequest`: how Create began (`idea` or `help`), what the person wants (`next` or `proposal`), and the whole exchange so far (at most 20 messages and 24,000 characters, 2,000 for a message of the person's), since the host keeps no session. Answers `CompanionReplyResponse`: the reply (a line, the companion's view of the idea, then one question with at most 4 choices or a proposal of a project name and a first task), `provenance: reported` and the model that gave it. After 4 questions, or when the person asked for the recap, only a proposal is accepted. Refusals: 400 `invalid_companion_request` (with issues) or `invalid_exchange` (out of order or too long), 413 past 256 KiB, 429 `companion_busy` (this principal's turn is in progress) or `rate_limited` (12 replies a minute per principal), 503 `companion_not_set_up`, `companion_not_running`, `companion_model_missing`, `companion_model_not_local` or `companion_busy_on_mac` (another principal's turn is in progress), 504 `companion_too_slow` (no first token in 30 s, or no whole reply in 45 s; the request to Ollama is then closed, which stops it), 502 `companion_unreadable` (two replies outside the shape). Model text is untrusted and reported; neither the exchange nor the reply is journaled, stored or logged |
| `POST /api/pairing` | Loopback only. Opens a pairing window: `PairingOpenedResponse`, with the code, the window's status and where devices reach the network listener; 409 `network_off` while the listener is off |
| `GET /api/pairing`, `DELETE /api/pairing` | Loopback only. The window's `PairingStatus` (never the code); `DELETE` closes it |
| `GET /api/devices` | Loopback only. `DevicesResponse`: every device paired, revoked ones included, and which have a realtime connection open |
| `POST /api/devices/:device_id/revoke` | Loopback only. Revokes the device (again is harmless) and answers its `DeviceView`; 404 for a device never paired |
| `POST /api/device/revoke` | Network listener only. The calling device revokes its own credential, as forgetting the control plane does; 204 |

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
  available from `GET /api/events`.
- **Device events stay home.** `device.paired` and `device.revoked` are journaled like every
  event, but no client receives them over this stream, and a paired device does not read them from
  `GET /api/events` either, where `limit` counts only the events returned: which devices are
  paired is the owner's to know, on loopback. The gap they leave in positions is harmless, as any
  gap is.
- **Events carry their effects.** Each `event` message includes `changes`: the current state of
  every project, workstream, execution and command the event changed. Clients replace their copies
  wholesale and never re-implement the projection.
- **Acknowledgements.** `command_ack` reports `accepted`, `rejected`, `duplicate` or `conflict`.
  Because events are recorded while the command is handled, a client may receive the command's
  events before its acknowledgement; correlate by `command_id`.
- **Rejection codes.** A rejected command's record carries a code and a message in words.
  `device_revoked` rejects a command from a paired device revoked after its request or connection
  was authenticated. The code `demonstration` never comes from a control plane: only the XR
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
- **Putting a project back.** `project.restore` `{project_id, checkpoint_id}` is `review_required`:
  a client sends it only after the person has read the preview. It is refused with
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
the REST reads, `POST /api/commands`, `POST /api/device/revoke` and `GET /realtime`, exactly as on
loopback, with the device as every command's principal. It serves nothing that manages devices,
and it never accepts the access token. `Host` must be an IP address or a `.local` name with the
listener's port.

A device refuses any certificate but the one it pinned when it paired, before it sends anything.
A refused upgrade answers with the error shape above, such as 401 `device_revoked`, so a device
can say why.

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
connection/status is separate. Accepted historical evidence remains readable in every case.

`GET /api/executions/:execution_id/results/:event_id` takes the same exact query and returns one
`ToolResultResponse` with full scope, original summary and bounded recorded result. It requires
the projection's accepted observed event, then cross-checks exact journal identity, position,
scope, runtime and provenance. It neither scans source files nor calls a runtime. Recent-list
eviction does not make an exact accepted event disappear. Both routes require the authenticated
owner-journal principal (local owner or still-active paired device), current journal and the task's
current execution; malformed/extra query fields are 400, unavailable/foreign scope or event is
the same 404. Existing network response revocation applies. Missing routes on older hosts are
unavailable; clients must not guess another execution, event or newest result after an await.

These reads grant no command authority. See [ADR 0031](../decisions/0031-record-bounded-native-tool-results-in-the-owner-journal.md)
for privacy, capture and source-memory limits and the coordinator's separate bounded selectors.

## Workspace request upgrade order

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
