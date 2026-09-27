# Integrations

## The runtime adapter contract

A runtime integration implements `RuntimeAdapter` (`packages/runtime-core/src/adapter.ts`):

- `descriptor`: runtime id, kind, display name, whether it is `synthetic`, and its capabilities.
- `validateStartOptions(options)`: checks runtime-specific start options before anything is
  recorded. Options are opaque to the rest of the system, so no vendor option enters the core.
- `startExecution`, `sendInstruction`, `respondToApproval`, `interrupt`: each is present exactly
  when its capability is declared; registration refuses an inconsistent adapter. Each resolves
  when the runtime has confirmed the action and rejects with `RuntimeActionError` otherwise,
  stating whether the action may have taken effect anyway.
- Observations: the adapter reports what the runtime does as normalized `RuntimeObservation`s
  (the `runtime.*` events), with the native record id for deduplication, the native sequence when
  one exists, and honest provenance. It never throws into the sink and never invents state.
- `close()`.

## Capabilities

| Capability | Meaning |
| --- | --- |
| `start_execution` | Create an execution and run its first turn from an instruction |
| `instruct_at_rest` | Start a new turn on an execution whose last turn ended |
| `instruct_while_running` | Deliver an instruction while a turn runs |
| `respond_to_approval` | Answer an approval the runtime raised |
| `interrupt` | Stop the running turn; the execution remains |

Clients show only actions whose capability is true. Admission rejects the rest with
`capability_unsupported`; nothing is emulated.

**Why these and not more.** Each capability maps onto at least one verified runtime surface.
There is no `pause`: no verified runtime can suspend a turn and resume it later (Codex goals can be
paused between turns, which is not the same). There is no `cancel` distinct from `interrupt`:
every runtime stops the turn and keeps the session. Discovery of existing work, diffs, terminal
output and review requests are verified on some runtimes but will be added with the first adapter
that needs them, not before.

## Evidence snapshot

The contract was shaped by the surfaces verified on 2026-09-26. Vendor facts change: the dated
details and sources are in the validation records, and they must be re-verified before an adapter
is built.

| Capability | OpenCode server | Claude Code Agent SDK | Codex app-server (experimental) | Codex exec / TS SDK (stable) |
| --- | --- | --- | --- | --- |
| start_execution | yes | yes | yes | yes |
| instruct_at_rest | yes | yes (resume) | yes | yes (resume) |
| instruct_while_running | v2 steer or queue, at the next step | queued (streaming input) | yes (`turn/steer`) | no |
| respond_to_approval | yes | yes (`canUseTool`) | yes (server requests) | no, approvals are rejected |
| interrupt | yes (abort) | yes (streaming input) | yes (`turn/interrupt`) | process signal only |
| pause | no | no | no | no |

Records: [OpenCode](../validation/opencode-capabilities.md),
[Claude Code](../validation/claude-code-capabilities.md), [Codex](../validation/codex-capabilities.md).

## Integration status

| Integration | Status | Blocking question |
| --- | --- | --- |
| Mock runtime | Built. Synthetic, labeled everywhere. | None |
| OpenCode | Built: `packages/integrations/opencode`, the v2 API pinned to `@opencode/cli` 2.0.18 on a server Halcyonic launches and supervises ([ADR 0009](../decisions/0009-opencode-v2-pinned-and-launched-by-halcyonic.md)); end to end tests against the real binary and a fake provider. Registered when `HALCYONIC_OPENCODE_BIN` is set. Proves provider and local model independence. | How the pinned binary is installed for users |
| Claude Code | Built: `packages/integrations/claude-code`, runtime kind `claude-agent` ("Claude Agent"), on the Agent SDK 0.3.283 with streaming input and a session id chosen at launch; API key or cloud provider authentication only. Registered when `HALCYONIC_CLAUDE_AGENT=1`. Verified against the real CLI, without a model and then with one: approval round trip, second turn, interrupt ([record](../validation/claude-code-capabilities.md)). | Attaching to terminal sessions |
| Codex | Not started. Target decided: app-server's stable surface, pinned to 0.157.0, on a server Halcyonic launches ([ADR 0011](../decisions/0011-codex-app-server-stable-surface.md)). | Building the adapter |
| Salidium | Built: a client for Salidium's versioned, read-only consumer contract v1, which `salidium@0.6.0` serves; exercised against the release candidate and the released daemon ([record](../validation/salidium-consumer-contract.md)); served per execution at `GET /api/executions/:execution_id/understanding`. | None |
| Seorak | Built: a client for Seorak's versioned, read-only integration API v1 on its local plane, which `seorak` 0.3.0 serves, written from the published `@seorak/types` 0.2.0. It resolves the runtime's own session id to Seorak's session, which answers the correlation question, then reads the estimated cost, outcome and verification lens; verified against the running plane ([record](../validation/seorak-integration-api.md)); served per execution at `GET /api/executions/:execution_id/evaluation`. An evaluation costs three of the credential's 60 requests a minute, so clients fetch it on demand and never poll. | None. The verification lens rows, 403 and 429 are not verified yet ([open questions](../product/OPEN_QUESTIONS.md)) |

## Salidium and Seorak boundary

- Halcyonic owns Projects, Workstreams, Executions, Commands and runtime control
  ([ADR 0007](../decisions/0007-agent-control-belongs-to-halcyonic.md)). Salidium and Seorak own
  understanding and performance and stay observational; Halcyonic displays their conclusions with
  their provenance and never restates them as its own facts.
- The dependency runs one way: Halcyonic depends on their published, versioned, read-only
  contracts, and neither gains Halcyonic-specific features. Sessions are correlated through the
  neutral key every orchestrator already has: the runtime kind and the runtime's own session id.
- Halcyonic never reads their databases, never imports their private packages, and never calls
  their write endpoints.
- They are optional. When one is absent, paused or incompatible, the relevant surface says
  unavailable.
- Their conclusions are read through on request and never journaled; they keep the provider's own
  epistemic classes and never feed status or attention
  ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)).
- Both already observe Claude Code and Codex sessions on the machine, including ones Halcyonic
  starts. Link rather than merge: keep each execution's `native_id` so their records can be
  correlated.

## Rules for a new adapter

1. Verify the runtime's current official surface and write a validation record, including a
   runtime smoke test, before writing the adapter.
2. Use official structured surfaces. Never scrape a terminal UI.
3. Never install global hooks or change the user's runtime configuration to gain control;
   control executions through the runtime's own API.
4. Declare only verified capabilities. Map native states to normalized events only where the
   mapping is defensible; otherwise emit nothing and let the status be `unknown`.
5. Keep native ids as opaque references; never expose vendor objects through Halcyonic APIs.
   Prefer choosing the native id at launch where the runtime allows it (Claude Code's
   `--session-id`), so the execution can be correlated before the runtime reports anything.
6. Launch agents with an explicitly built environment. Leave the runtime's home and
   configuration directories at the developer's defaults so Salidium and Seorak can observe the
   sessions, and never pass `SALIDIUM_INTERNAL` to a launched agent: it makes Salidium drop the
   session's hooks.
7. Record fixtures from real runs, sanitized, for contract tests.
8. Accept a working directory only through the host's `DirectoryPolicy`
   (`packages/runtime-core/src/adapter.ts`) and use the real path it returns. The control plane
   allows only directories under `HALCYONIC_PROJECT_ROOTS`, so a client cannot point an agent
   anywhere else on the machine.
9. Make sure an agent process cannot outlive the control plane unsupervised: stop it on close and on
   the control plane's exit, and say plainly in the validation record what a hard kill leaves
   running.
