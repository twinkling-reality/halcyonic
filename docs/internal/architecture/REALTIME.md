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
| `GET /api/events?after=&limit=&workstream_id=` | Journal history after a position (limit 1 to 1000, default 200); a paired device's reads leave device events out |
| `GET /api/executions/:execution_id/understanding` | What Salidium says about the execution's session, read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability, 404 for an unknown execution. An available answer's `source.synthetic` is true only for a stand-in's, as in the recorded demonstration ([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md)) |
| `GET /api/executions/:execution_id/evaluation` | What Seorak measured about the execution's session (estimated cost, outcome, verification runs), read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability, 404 for an unknown execution. Each answer spends three of Seorak's 60 requests a minute, so fetch it on demand, for example when a workstream is opened, and never poll. `source.synthetic` as for understanding |
| `GET /api/usage-limits` | `UsageLimitsResponse`: the provider usage limits Seorak last observed, account wide, read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability. `available` carries at least one reading, and `complete`, false when Seorak read only some limits (it stopped at a result limit): the readings it returned are exact, and a window missing from them is unknown, never zero. Each reading has: the agent (Seorak's open id, and a label to show), the window (`rolling-5h` or `weekly`), the used percentage the provider reported, when it resets and when it was observed, Seorak's freshness flag, and an account that is always `unidentified`. A reading past its reset is never served; with none left the answer is `unavailable` (`no_current_reading`), never 0%. `unauthorized` covers a missing credential, one without `limits:read` (`insufficient_scope`) and a project- or date-restricted one (`outside_credential_restriction`); `unavailable` covers `not_captured`, a stopped or rate-limited Seorak, and `not_configured` where no source serves limits, as in the recorded demonstration. One request of Seorak's 60 a minute: read it when a person opens Usage left, never on a timer. Not Workstream status, and not part of starting work |
| `POST /api/commands` | Submits a `CommandEnvelope` (JSON only) |
| `POST /api/transcriptions` | Turns one clip of speech into a draft on the Mac ([ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md)); the only route that takes anything but JSON. The body is `audio/wav`, 16-bit mono PCM at 16 kHz, 0.5 to 30 s, at most 960,044 bytes (`TRANSCRIPTION_MAX_BYTES`). Answers `TranscriptionResponse`: `heard` with the text, its language (`en`) and the engine's name and version, or `nothing_heard` when the engine found no speech. The text is untrusted, a draft the person confirms before anything is sent. Refusals: 503 `transcription_unavailable` (voice not set up), 415 for another content type, 400 `invalid_audio` or `audio_too_short`, 413 past the byte limit, 429 `transcription_busy` (this principal's clip is in progress) or `rate_limited` (six clips reach the engine in each fixed minute), 503 `transcription_busy_on_mac` (another clip is being transcribed), 502 `transcription_failed` (the engine failed or ran past 15 s, or its transcript is longer than a draft). Neither clip nor text is journaled, stored or logged |
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
  `{kind: 'new_folder', root, folder_name}`, where `root` is a root's `path` exactly as
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
  at most the three oldest pending questions; each other one shows once one before it is resolved,
  and meanwhile keeps the execution waiting and names itself in its attention reasons. A question
  whose text, keys and options together pass 16,000 characters is shown shortened, each cut
  marked, and cannot be answered.
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
  after `hello` is not fatal. Fatal errors close with code 1008. A command the control plane fails
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

