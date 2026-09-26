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
| `GET /api/events?after=&limit=&workstream_id=` | Journal history after a position (limit 1 to 1000, default 200) |
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
  │ <── welcome {journal, head, resumed}   │
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
