# REST and realtime

REST bootstraps, pages history and accepts commands. The WebSocket carries live state, events,
commands and acknowledgements. Why both, and why not SSE, polling, WebRTC or MQTT:
[ADR 0003](../decisions/0003-rest-bootstrap-and-websocket-realtime.md).

All payloads are defined in `packages/contracts` (`api.ts`, `realtime.ts`) and published in the
generated JSON Schema. Every request needs `Authorization: Bearer <token>` except `/api/health`;
see [SECURITY.md](SECURITY.md).

## REST

| Method and path | Returns |
| --- | --- |
| `GET /api/health` | `{status: 'ok'}`; no token needed, reveals nothing |
| `GET /api/snapshot` | `Snapshot`: journal identity, position, projects, workstreams, executions, recent commands, runtimes |
| `GET /api/projects` | Projects at the current position |
| `GET /api/workstreams?project_id=` | Workstreams, optionally for one project |
| `GET /api/runtimes` | Runtime descriptors with capabilities; clients show only supported actions |
| `GET /api/runtimes/:runtime_id/models` | `RuntimeModelsResponse`: the models the runtime lists now, from its own list, read through and never journaled ([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)); always 200 with an availability (`unavailable` carries the reason in words: `timeout` after 30 s, `invalid_models` for a list outside the contract, or the adapter's own code), 404 `models_not_listed` for a runtime whose `model_choice` is `none`, 404 `runtime_not_found` for an unknown one. Listing may start the runtime, so it can take seconds: fetch it when a person opens the choice, and never poll |
| `GET /api/events?after=&limit=&workstream_id=` | Journal history after a position (limit 1 to 1000, default 200) |
| `GET /api/executions/:execution_id/understanding` | What Salidium says about the execution's session, read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability, 404 for an unknown execution |
| `GET /api/executions/:execution_id/evaluation` | What Seorak measured about the execution's session (estimated cost, outcome, verification runs), read through and never journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)); always 200 with an availability, 404 for an unknown execution. Each answer spends three of Seorak's 60 requests a minute, so fetch it on demand, for example when a workstream is opened, and never poll |
| `POST /api/commands` | Submits a `CommandEnvelope` (JSON only) |

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
- **Events carry their effects.** Each `event` message includes `changes`: the current state of
  every project, workstream, execution and command the event changed. Clients replace their copies
  wholesale and never re-implement the projection.
- **Acknowledgements.** `command_ack` reports `accepted`, `rejected`, `duplicate` or `conflict`.
  Because events are recorded while the command is handled, a client may receive the command's
  events before its acknowledgement; correlate by `command_id`.
- **Rejection codes.** A rejected command's record carries a code and a message in words. The
  code `demonstration` never comes from a control plane: only the XR client's recorded
  demonstration, which stands in for one on a device without one, answers with it. It means the
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
