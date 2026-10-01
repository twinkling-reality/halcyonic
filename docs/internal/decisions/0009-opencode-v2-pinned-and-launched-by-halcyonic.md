# ADR 0009: Target OpenCode's v2 API, pinned, on a server Halcyonic launches

- Status: Accepted
- Date: 2026-09-26

## Context

OpenCode ships two server APIs side by side: v1 (`opencode-ai` 1.18, the default install) and v2
(`@opencode/cli` 2.0, labeled experimental 0.0.1, released at a fast cadence). The vendor's
migration guide says integrations that call the v1 server API must migrate to v2. Both were run
headless on 2026-09-26 against a fake provider
([opencode-capabilities.md](../validation/opencode-capabilities.md)).

Observed: v2 has execution lifecycle events with reasons, a per-session sequence number, `steer`
and `queue` delivery, interrupt that reports whether it interrupted and removes pending
permissions, a session outcome, and authentication that cannot be turned off; it made no network
requests. v1 has no authentication by default and downloaded a plugin from npm on first use. Both
lose events across a reconnect, have no pause, and did not recover a run after a restart. v2
2.0.18 drops the message attached to a rejection, and its OpenAPI document does not describe
event payloads.

Both versions install a command named `opencode` and share configuration locations, so the
`opencode` on a user's PATH may be either.

## Decision

- The OpenCode adapter targets the v2 server API, pinned to one exact `@opencode/cli` version
  (2.0.18) and its binary checksum. Upgrading means repeating the smoke test first.
- Halcyonic launches its own OpenCode server from a configured binary path, never the `opencode`
  on PATH: bound to 127.0.0.1, with an explicit `--port` and a generated password, in an
  explicitly built environment that keeps the user's OpenCode configuration directories (their
  providers and keys live there).
- Event payload types are defined from recorded captures of the pinned binary, and unknown event
  types are ignored.
- After a reconnect the adapter reconciles from session state; when the server dies mid-turn the
  adapter reports `runtime.connection.lost`.
- Driving a user's own running OpenCode server, v1 or v2, is out of scope; it would need its own
  decision.

## Alternatives considered

- **v1.** The default install today, with the more complete OpenAPI document, but declared
  superseded by the vendor, unauthenticated by default, and without lifecycle reasons, sequence
  numbers or delivery modes.
- **Both, behind one adapter.** Doubles the surface to verify on every release for no user need
  yet.
- **Attach to whatever server the user runs.** Its version, authentication and configuration are
  unknown, and a v1 server would need a different adapter.

## Consequences

- One pinned binary to verify, with a repeatable smoke test and recorded fixtures.
- Halcyonic must obtain and configure that binary; how it is installed for users is not decided.
- Rejection reasons do not reach the model on 2.0.18; re-check on every upgrade.
- Revisit when v2 declares a stable API, or if replay or restart recovery start to work.

## Note, 2026-10-01

In the fifth headset session the adapter reported `runtime.connection.lost` for a server that still
ran, and the execution stayed `unknown` overnight. The Mac had slept with its lid closed; on waking
the event stream reconnected, but the first read of the session back timed out, and the adapter
took that as final ([opencode-capabilities.md](../validation/opencode-capabilities.md)). The adapter
now reads a session again after a failed read, and when the reads still fail while the server runs,
reports the loss as before but keeps trying, with growing delays, for as long as the server runs.
When the session can be read again it records a new event, `runtime.connection.restored`, then
what changed meanwhile. A server that exits still loses its sessions for good. The decision is
otherwise unchanged.
