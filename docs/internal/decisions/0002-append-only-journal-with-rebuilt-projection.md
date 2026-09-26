# ADR 0002: Append-only journal with a projection rebuilt at startup

- Status: Accepted
- Date: 2026-09-26

## Context

Halcyonic needs history, replay, debugging, audit and synchronization, and it must survive
restarts without losing or inventing state. The control plane runs locally for one person, with
event volumes in the thousands to hundreds of thousands, not millions.

## Decision

- Every fact is an event in an append-only SQLite journal, validated on write and on read.
  Events are deduplicated by event id and by native source record.
- Current state is an in-memory projection built by pure domain code from the journal. It is
  rebuilt from the whole journal at startup. No projection snapshots are stored.
- One synchronous write path (`Recorder`) appends, projects and publishes each event without
  yielding.
- After a restart, anything that was in flight is recorded as unknown, because no runtime session
  survives a restart.
- WAL mode with `synchronous = FULL`; files are owner-only.

## Alternatives considered

- **Mutable state tables only.** Loses history and replay, and makes audit a separate system.
- **Full event sourcing with stored projections and snapshots.** More moving parts than a local
  journal of this size needs. Snapshots can be added without changing the journal.
- **An asynchronous store.** Would require locks or queues to keep journal and projection
  consistent, with nothing gained locally.

## Consequences

- The same journal always produces the same state; tests rely on that.
- Startup time grows with the journal. Add persisted snapshots when that becomes noticeable.
- The journal API is synchronous. A future asynchronous store (for example PostgreSQL for a hosted
  deployment) needs a serialized write path and a subscribe-then-read resume, not just a new
  implementation.
