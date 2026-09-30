# OpenCode capabilities

- **Question:** What can a Halcyonic adapter control and observe through OpenCode's official
  server API, and which API version should it target?
- **Date:** 2026-09-26.
- **Versions:** `opencode-ai` 1.18.32 (v1, published 2026-09-21, the default install) and
  `@opencode/cli` 2.0.18 (v2, first released 2026-09-11). Repository `anomalyco/opencode`
  (formerly `sst/opencode`), MIT.
- **Method:** Official documentation, the OpenAPI documents committed at tags `v1.18.32` and
  `v2.0.18`, server source at those tags, and the npm registry; then a runtime smoke test of both
  versions (below).
- **Status:** Documentation, source and runtime verified for the scenarios listed. Real providers,
  long or concurrent runs, and the `--stdio` and `--service` modes are not tested.

## Findings from documentation and source

- **Two major versions ship side by side.** The official migration guide
  (https://opencode.ai/v2/docs/migrate-v1/) says: "Integrations that call the V1 server API must
  migrate to the V2 API." v2's OpenAPI document labels itself "Experimental" with version 0.0.1,
  and v2 shipped 19 releases in 15 days.
- **One server, many projects,** selected per request by directory.
- **Sessions and prompting (v1).** `POST /session`, `POST /session/{id}/prompt_async` (returns
  204), `POST /session/{id}/abort`, `GET /session/{id}/diff`, `GET /session/status`.
- **Permissions (v1).** A `permission.asked` event, answered by
  `POST /permission/{requestID}/reply` with `once`, `always` or `reject` and an optional message.
  The source says rejecting cascades to the session's other pending requests (not runtime tested).
- **v2 additions.** `session.execution.started/succeeded/failed/interrupted` events, instructions
  with `delivery: "steer"` or `"queue"`, interrupt with resume. The v2 documentation also describes
  recovery of interrupted runs after a restart and a replayable session log; neither was observed
  at runtime (below).
- **Providers and local models.** 75+ providers through AI SDK and Models.dev. Documented local
  paths: Ollama, LM Studio and llama.cpp (OpenAI-compatible). vLLM is documented in the v2 docs;
  in v1 it is reachable only through the generic OpenAI-compatible provider.
- **SDK.** `@opencode-ai/sdk` 1.18.32; only its `/v2` subpath is generated from the OpenAPI
  document. v2 has `@opencode/client`.

## Runtime smoke test (2026-09-26)

Method: both platform binaries (darwin arm64; v1 sha256 `a3c45d4e…4395e`, v2 sha256
`6759c7f8…6bf`) run headless under `env -i` with temporary HOME, XDG and TMPDIR directories,
bound to loopback, against a local fake OpenAI-compatible provider, with an allowlisting proxy and
a socket monitor. No real provider, no credentials. Eight scenarios per version: startup and
authentication, the OpenAPI document, a simple turn, an approval, interrupt, a prompt while busy,
reconnect, several directories; plus a server restart mid-turn. The raw evidence is kept outside
the repository.

Observed in both versions:

- Server-sent events carry only `data:` lines: no `event:`, `id:` or `retry:` fields, though every
  payload has its own `evt_…` id. Events during a disconnect are lost, and `Last-Event-ID` is
  ignored.
- There is no pause and no "waiting for approval" status: a session waiting on a permission shows
  as busy (v1) or running (v2); the pending permission is the only signal.
- A prompt sent while a turn runs is delivered at the next step boundary and never cuts a running
  model stream.
- After the server was stopped (SIGTERM or SIGKILL) mid-turn and restarted, the run was not
  resumed and no final event was emitted.

v1 (`opencode-ai` 1.18.32):

- No authentication unless `OPENCODE_SERVER_PASSWORD` is set; a heartbeat data event every 10 s.
- A rejection message reaches the model. Abort leaves the pending permission listed, and a late
  reply returns 200 but runs nothing.
- It downloaded `@opencode-ai/plugin` from npm on first opening a project, even with default
  plugins disabled.

v2 (`@opencode/cli` 2.0.18):

- Authentication is always on; a password is generated and printed unless `OPENCODE_PASSWORD` is
  set. `/` and unknown paths serve the web UI without authentication. With the default port busy
  it moves to 4097.
- The served `/openapi.json` has 115 paths (2 more than the committed document) and describes
  event payloads only as an opaque string.
- One global `/api/event` stream for all directories, with a `location` field per event, a
  heartbeat comment every 15 s, and a per-session `durable.seq` on durable events.
- Execution lifecycle events carry reasons, and the session records an `outcome`.
- `steer` and `queue` both wait for the running step; inside a tool loop, `steer` is delivered
  right after the tool result and `queue` after the model answers it. `interrupt?resume=true`
  interrupts and delivers the steered instruction in one call.
- Interrupt removes pending permissions without any event; a late reply gets 404.
- Defects: a rejection message is accepted (204) but reaches neither the model nor any event; tool
  output can only be polled (`GET /api/shell/{shellID}/output`), not streamed; the experimental
  session log returned no events; a nonexistent directory gives 500 on `/api/location` yet a
  session can still be created there.
- It made no network requests beyond loopback.

## Capability matrix

Runtime observed unless stated.

| Capability | v1 1.18.32 | v2 2.0.18 |
| --- | --- | --- |
| start_execution | Supported | Supported |
| instruct_at_rest | Supported | Supported |
| instruct_while_running | Partial: next step boundary, no delivery choice | Supported: `steer` or `queue`, next step boundary |
| respond_to_approval | Supported | Supported; the rejection message is dropped |
| interrupt | Supported; the pending permission stays listed | Supported; pending permissions are removed silently |
| pause | Not supported | Not supported |
| discover existing work | Supported | Supported |
| diff | Supported, needs a message id | Supported |
| terminal output | Pushed in events; PTY over WebSocket | Polled only; PTY over WebSocket |
| replay after reconnect | Not supported | Not supported; the experimental log returned nothing |
| recovery after restart | Not supported | Documented; not observed |

## Consequences for Halcyonic

- Target v2, pinned to an exact version and binary checksum, and have Halcyonic launch its own
  copy with an explicit port and password
  ([ADR 0009](../decisions/0009-opencode-v2-pinned-and-launched-by-halcyonic.md)).
- Event streams do not resume: after a reconnect the adapter re-reads each hosted session's state
  and pending permissions and reconciles; the journal's `(source, source_native_id)` uniqueness
  deduplicates by `evt_` id.
- The adapter emits `runtime.approval.requested` from `permission.asked`; the normalized status
  then becomes `waiting_for_human`. A turn ended by interrupt clears pending approvals, which
  matches v2 removing them silently.
- A server that dies mid-turn loses its runs: the adapter reports `runtime.connection.lost` rather
  than waiting for a recovery that was not observed.
- Denials cannot carry a reason to the model on 2.0.18; the adapter still sends it, and the defect
  must be re-checked on every upgrade.

## Not yet tested

Several pending approvals at once and the cascade on reject; `always` replies; the question tool;
subagents; compaction; `--stdio` and `--service`; Linux and Windows; real providers; long or
concurrent runs.

## Adapter build (2026-09-26)

Found while building and testing `packages/integrations/opencode` against the 2.0.18 binary with a
fake provider (15 end to end scenarios, five consecutive green runs):

- `/api/info` answers 503 with `Retry-After: 1` for 30 to 50 ms after the port opens; readiness
  retries on it. A busy explicit `--port` makes the server exit with code 1; there is no fallback.
- The first prompt on a fresh project blocks OpenCode's event loop for 1.0 to 1.6 s, so a very
  short silence timeout would drop the event stream during it.
- `session.execution.failed` carries `{sessionID, error: {type, message, status?}}`. Creating a
  session does not check the model: an unknown one is accepted and the execution then fails with
  `provider.no-route`. `model: {providerID, id}` selects the model.
- `POST /api/session` accepts a caller-chosen id starting with `ses`, and a duplicate silently
  returns the existing session with 200, so the adapter lets OpenCode choose ids.
- A message given with an approval, not only with a rejection, is accepted and dropped. Answering
  an already answered request returns 404 `PermissionNotFoundError`.
- The session inbox holds enqueued prompts not yet delivered; a prompt's inbox item id equals the
  prompt response id and leaves the inbox on delivery. The adapter uses this to settle a reconnect
  that lands between a prompt and its first event.
- During a later turn the session still shows the previous turn's `outcome`; only
  `/api/session/active` says it is running.
- A directory reached through a symbolic link gives an odd relative subpath, so the adapter passes
  the real path returned by the host's directory policy. Non-git directories work.
- OpenCode ignores the end of its standard input and survives its parent being killed with
  SIGKILL, so the adapter records the server and runs a watchdog process to stop it.

The adapter refuses any server that does not report version 2.0.18. The configured binary's
checksum is not verified yet.

## Local models and steering (2026-09-29)

Found while running the pinned binary with open models that Ollama serves on the Mac, through
the control plane; the runs, the network and the measurements are in
[local-models.md](local-models.md).

- **Steering.** A prompt with `delivery: "steer"` sent while a turn runs is accepted into the
  session's inbox and reaches the model when the running step ends, in the same turn. One still in
  the inbox when the turn is interrupted is not delivered then; it reaches the model with the next
  prompt, and no turn starts for it on its own. The adapter now declares `instruct_while_running`.
- **Rejecting cascades.** With two permission requests pending, a rejection without a message
  resolved both as rejected and interrupted the turn.
- **The model list settles late.** `GET /api/model` and `GET /api/model/default` take a `location`
  deep object (`?location%5Bdirectory%5D=...`), and each location settles separately. Right after
  launch they list nothing, then the models of the built-in catalog, then those discovered from
  local servers such as Ollama; a session prompted before its model is listed fails with
  `provider.no-route`. The adapter waits for a named model to be listed. Each listed model carries
  its provider's settings, API key included, its headers and its request body, so the adapter reads
  only the fields it needs.
- **Defaults.** The default agent allows every action except reading `.env` files and working
  outside the project directory. Sessions accept their own `permissions` ruleset at creation.
- **Network.** The model catalog fetch from `models.opencode.ai` and the download of ripgrep from
  GitHub are described in the local models record; `OPENCODE_DISABLE_MODELS_FETCH=true` stops the
  first, and a ripgrep on the PATH prevents the second.
