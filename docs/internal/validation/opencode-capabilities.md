# OpenCode capabilities

- **Question:** What can a Halcyonic adapter control and observe through OpenCode's official
  server API, and is it a sound first real runtime?
- **Date:** 2026-09-26.
- **Versions:** `opencode-ai` 1.18.32 (v1, published 2026-09-21, the default install) and
  `@opencode/cli` 2.0.18 (v2, first released 2026-09-11). Repository `anomalyco/opencode`
  (formerly `sst/opencode`), MIT.
- **Method:** Official documentation, the OpenAPI documents committed at tags `v1.18.32` and
  `v2.0.18`, server source at those tags, and the npm registry. OpenCode was **not** installed or
  run. The migration statement below was re-checked directly against the official page.
- **Status:** Documentation and source verified. Runtime smoke test not yet performed.

## Findings

- **Two major versions ship side by side.** The official migration guide
  (https://opencode.ai/v2/docs/migrate-v1/) says: "Integrations that call the V1 server API must
  migrate to the V2 API." v2's OpenAPI document labels itself "Experimental" with version 0.0.1,
  and v2 shipped 19 releases in 15 days.
- **Headless server.** `opencode serve`, loopback by default. v1 tries port 4096, then a random
  port, and has **no authentication unless `OPENCODE_SERVER_PASSWORD` is set** (HTTP Basic). v2
  always requires a password and generates one if none is set.
- **OpenAPI 3.1.** v1 serves it at `/doc` (162 paths); v2 at `/openapi.json` (113 paths).
- **One server, many projects,** selected per request by directory (`x-opencode-directory`).
- **Sessions and prompting (v1).** `POST /session`, `POST /session/{id}/prompt_async` (returns
  204), `POST /session/{id}/abort`, `GET /session/{id}/diff`, `GET /session/status`.
- **Permissions (v1).** A `permission.asked` event, answered by
  `POST /permission/{requestID}/reply` with `once`, `always` or `reject` and an optional message.
  Rejecting cascades to the session's other pending requests.
- **Status model.** `idle | busy | retry`. A session waiting for permission stays `busy`; the
  pending request is the only signal.
- **Events (v1).** Server-sent events at `/event` (per directory) and `/global/event`, with a
  heartbeat every 10 seconds. No resume: SSE ids are not set, so a reconnecting client cannot ask
  for what it missed (experimental catch-up endpoints exist).
- **v2 additions.** `session.execution.started/succeeded/failed/interrupted` events, instructions
  with `delivery: "steer"` or `"queue"`, interrupt with resume, recovery of interrupted runs after
  a restart, and a replayable session log (experimental).
- **Providers and local models.** 75+ providers through AI SDK and Models.dev. Documented local
  paths: Ollama, LM Studio and llama.cpp (OpenAI-compatible). vLLM is documented in the v2 docs;
  in v1 it is reachable only through the generic OpenAI-compatible provider.
- **SDK.** `@opencode-ai/sdk` 1.18.32; only its `/v2` subpath is generated from the OpenAPI
  document. v2 has `@opencode/client`.

## Capability matrix

| Capability | v1 | v2 |
| --- | --- | --- |
| start_execution | Supported: create session, `prompt_async` | Supported |
| instruct_at_rest | Supported: new prompt to the session | Supported |
| instruct_while_running | Partial: joins the running loop; timing untested | Supported: steer or queue |
| respond_to_approval | Supported: permission reply | Supported |
| interrupt | Supported: abort | Supported: interrupt |
| pause | Not supported | Not supported (queue parking is not a pause) |
| discover existing work | Supported: project and session listing | Supported |
| diff | Supported | Supported |
| terminal output | Partial: tool output in message parts; PTY over WebSocket | Supported |

## Consequences for Halcyonic

- OpenCode remains the right first real runtime (provider and local-model independence), but the
  adapter must target one API version deliberately, pin the exact OpenCode version, and generate
  its client types from the OpenAPI document served by that binary.
- Because v1 has no authentication by default, the adapter must start or require OpenCode with a
  password and never expose its port beyond loopback.
- Event streams do not resume, so after a reconnect the adapter must re-read session state and
  deduplicate by native ids; the journal's `(source, source_native_id)` uniqueness supports this.
- A pending permission is the only signal that a session is waiting. The adapter must emit
  `runtime.approval.requested` from it; the normalized status then becomes `waiting_for_human`.

## Needs a runtime smoke test

Exact SSE framing; whether prompts sent while busy are picked up mid-turn; whether abort rejects
pending permissions; `/doc` content type and authentication; v2 password and port behavior; v2
restart recovery.
