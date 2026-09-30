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
| `GET /api/runtimes/:runtime_id/models` | `RuntimeModelsResponse`: the models the runtime lists now, from its own list, read through and never journaled ([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)); always 200 with an availability (`unavailable` carries the reason in words: `timeout` after 30 s, `invalid_models` for a list outside the contract, or the adapter's own code), 404 `models_not_listed` for a runtime whose `model_choice` is `none`, 404 `runtime_not_found` for an unknown one. Listing may start the runtime, so it can take seconds: fetch it when a person opens the choice, and never poll |
| `GET /api/events?after=&limit=&workstream_id=` | Journal history after a position (limit 1 to 1000, default 200); a paired device's reads leave device events out |
| `GET /api/executions/:execution_id/understanding` | What Salidium says about the execution's session, read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability, 404 for an unknown execution. An available answer's `source.synthetic` is true only for a stand-in's, as in the recorded demonstration ([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md)) |
| `GET /api/executions/:execution_id/evaluation` | What Seorak measured about the execution's session (estimated cost, outcome, verification runs), read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability, 404 for an unknown execution. Each answer spends three of Seorak's 60 requests a minute, so fetch it on demand, for example when a workstream is opened, and never poll. `source.synthetic` as for understanding |
| `POST /api/commands` | Submits a `CommandEnvelope` (JSON only) |
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
- **Choosing a model.** `execution.start` carries `model_ref`: null, or a model's reference exactly
  as `GET /api/runtimes/:runtime_id/models` gave it. A choice for a runtime whose `model_choice` is
  `none` is rejected with `capability_unsupported`, and one the runtime does not list with
  `invalid_runtime_options`; a model that leaves the runtime's list before the start fails it with
  `model_unavailable`, before anything runs. The field was added as an explicit null without a new
  protocol or schema version, because no client in the field sends `execution.start`; commands
  stored before it read as `model_ref: null`
  ([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)).
- **Errors.** `error {error: {code, message, issues}, fatal}`. Invalid JSON or an invalid message
  after `hello` is not fatal. Fatal errors close with code 1008.
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

