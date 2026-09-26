# Events and the journal

## The journal

Every fact Halcyonic knows is an event in an append-only journal (SQLite, one file per control
plane). Events are never changed or deleted. Current state is the projection of the journal, so
history, replay, debugging and audit all come from the same record.

- **Position.** The journal assigns each event an increasing position. Positions may have gaps;
  clients track the last position they applied.
- **Idempotency.** An event is journaled once per `event_id`, and a runtime record is journaled
  once per `(source, source_native_id)`, so a runtime record delivered twice is not duplicated.
- **Identity.** Each journal has a `journal_id` and an `origin`: `live` for a running control plane,
  `fixture` for one loaded from a trace. Clients show fixture data as such.
- **Durability.** WAL mode with `synchronous = FULL`; files are readable by their owner only.
- **Validation.** Events are validated when written and again when read back.

## Envelope

| Field | Meaning |
| --- | --- |
| `schema_version` | Envelope version, currently `1` |
| `event_id` | UUIDv7, assigned by the control plane |
| `event_type` | See the catalog below |
| `project_id`, `workstream_id`, `execution_id` | Scope. Null where not applicable, never invented. |
| `source` | `{kind: 'control_plane'}` or `{kind: 'runtime', runtime_id}` |
| `source_native_id` | Id of the native record, for deduplication and traceability |
| `sequence` | Source-supplied order within an execution, when the source has one |
| `occurred_at` | When it happened, by the observing source's clock (UTC, `...sss Z`) |
| `ingested_at` | When the control plane journaled it |
| `correlation_id` | The command that started this chain of work |
| `causation_id` | The command or event that directly caused this one |
| `provenance` | How the fact is known; see below |
| `payload` | Type-specific data |

Ordering is by journal position. `occurred_at` is informative only, because clocks differ and
runtimes may deliver late; the projection ignores runtime events whose `sequence` is not after the
last one it applied for that execution.

## Provenance

Every event says how its fact is known, using the same classes as Salidium:

- `observed`: read from structured runtime or control plane state.
- `reported`: a claim in agent or user text. Agent messages are always `reported`; the contract
  rejects anything else. A claim never becomes an observation.
- `inferred`: derived by a named deterministic rule, which the event records.

Assessments from other systems (Salidium, Seorak) will get their own event types that carry their
evidence references; none exist yet.

## Catalog

| Event | Scope | Source | Meaning |
| --- | --- | --- | --- |
| `project.created` | project | control plane | A project exists |
| `workstream.created` | workstream | control plane | A workstream exists |
| `execution.created` | execution | control plane | A start was accepted; the runtime has not confirmed yet |
| `execution.start_failed` | execution | control plane | The runtime refused to start |
| `execution.state_unknown` | execution | control plane | The execution can no longer be observed (`control_plane_restarted`, `start_outcome_unknown`) |
| `command.accepted` | as resolved | control plane | Admitted; carries the full command, its policy and how it arrived |
| `command.rejected` | as resolved | control plane | Refused; carries the full command and the reason |
| `command.completed` | as resolved | control plane | Done, confirmed by the runtime where one was involved |
| `command.failed` | as resolved | control plane | Not done; `effect` says whether it may have happened anyway |
| `runtime.execution.started` | execution | runtime | The native session or thread exists |
| `runtime.turn.started` | execution | runtime | A turn began |
| `runtime.turn.completed` | execution | runtime | The turn ended normally |
| `runtime.turn.failed` | execution | runtime | The turn ended with an error |
| `runtime.turn.interrupted` | execution | runtime | The turn was stopped on request |
| `runtime.approval.requested` | execution | runtime | The runtime is blocked on a human decision |
| `runtime.approval.resolved` | execution | runtime | The runtime applied a decision |
| `runtime.tool.started` / `.completed` | execution | runtime | Tool activity |
| `runtime.agent_message` | execution | runtime | Agent text (`reported`) |
| `runtime.test_run.started` / `.completed` | execution | runtime | A test run and its outcome |
| `runtime.connection.lost` | execution | runtime | The adapter lost contact with the runtime |

The command events carry the full command, which is the audit record: who asked (as the client
declared itself), through which transport, under which policy, and with what outcome.

## Contracts: one source of truth

The schemas are TypeBox definitions in `packages/contracts`. TypeBox schemas are JSON Schema, so
the TypeScript types are inferred from them and `pnpm contracts:emit` serializes them, with shared
definitions referenced by name, into `packages/contracts/schema/halcyonic-contracts.schema.json`
for other languages. A test fails if the committed document is stale or if it validates anything
differently from the TypeScript validators. See [ADR 0005](../decisions/0005-typebox-contracts-as-single-source.md).

To change a contract:

1. Edit the schema in `packages/contracts`.
2. Update the projection, adapters and clients that use it.
3. Run `pnpm contracts:emit` and `pnpm fixtures:record` and review both diffs.
4. For an incompatible change, bump the relevant version in `versions.ts` and add a journal
   migration if stored events are affected.
5. Update this document.

## Versioning and migration

- Envelope, command and realtime protocol versions are independent integers.
- The journal schema is versioned by `PRAGMA user_version` with forward-only migrations, each in
  its own transaction. A journal written by a newer build is refused rather than misread.
- Until there are external users, breaking changes are acceptable when coordinated: migrate
  fixtures, the journal schema, generated bindings and documentation together.

## Traces

A trace is a journal exported as JSON Lines: one envelope per line, in position order.
`fixtures/traces/multiple_workstreams.jsonl` is recorded by `pnpm fixtures:record`, which drives
the real control plane and the mock runtime under virtual time with seeded identifiers. The same
code therefore always produces the same file, and a test fails when the committed trace no longer
matches. Replaying a trace goes through the normal write path, so replayed events are validated,
projected and streamed exactly like live ones, and replaying twice changes nothing.

Traces are development fixtures. They must never contain credentials or private repository
content.
