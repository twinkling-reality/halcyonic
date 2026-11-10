# ADR 0050: A start restores the projection from a checkpoint and folds only the journal after it

- Status: Accepted; built, measured at 50,000 and about a million events
- Date: 2026-11-10
- Amends: [ADR 0002](0002-append-only-journal-with-rebuilt-projection.md) (the journal stays the only
  source of truth; a start no longer always folds all of it)

## Context

Every start folded the whole journal before the control plane served anyone. Measured on this Mac on
2026-11-10 ([record](../validation/control-plane-scale-2026-11-10.md)): about 0.4 s at 50,000
events and 3.5 to 6.5 s at 937,021 (a 1.2 GB journal), growing with every week of use. About 90%
of it is reading, parsing and validating old envelopes, not folding them, so faster folding cannot
bound it; skipping what was already folded can. The projection's whole state at a million events is
69 MiB and reads back in about 0.3 s.

## Decision

- The projection exports its whole state (`Projection.exportState`, every field its events build,
  and the device registry's) and restores one into a projection that has applied nothing
  (`restoreState`), refusing a state of another shape before taking anything. The domain stays free
  of IO; the control plane serializes the state with `v8.serialize`, which keeps maps, sets and the
  objects the projection's fields share.
- A live journal keeps one checkpoint row beside its events (`projection_checkpoint`, journal
  migration 6): the position, the id of the event there, the journal's id, the identity of the code
  that wrote it, the state's SHA-256 and the state. The events are never touched, cut or compacted;
  the checkpoint is only a cache of them.
- A start takes the checkpoint only when every check passes, and otherwise folds the whole journal
  as it would with none, saying why in the log:
  - written for this journal (its journal id);
  - written by this code: a hash of every source file of the domain and contracts packages, the
    journal's schema version, the state's format and the V8 version, taken at the start, so any
    change of code, an upgrade included, means one full fold;
  - at a position the journal still holds, with the same event there (a replaced, rewound or
    rewritten journal fails it);
  - its bytes unchanged (the SHA-256), readable, and a state of the projection's shape at that
    position.
  Opening the journal drops the checkpoint whenever a migration runs, since a migration may change
  events. A start then folds only the events after the checkpoint's position, validated as always.
- Only the event at the checkpoint's position is compared, not the events before it. A journal
  whose earlier events were changed in place, without a migration, would restore a stale fold.
  That is accepted: Halcyonic never changes an event once appended (ADR 0002), every migration
  drops the checkpoint, and a journal edited by hand is outside what the product supports. A running
  hash of every envelope would catch it, at the cost of reading them all, which is what a
  checkpoint saves.
- The control plane writes a checkpoint at a clean close and after every 50,000 events appended
  since the last one, between appends. Writing never fails an append: a failed write is logged.
- Fixture journals (recorded traces, the judges' build) are never checkpointed and always folded
  whole.

## Alternatives considered

- Skipping validation of old envelopes: still reads and parses every event, and the journal stays
  data, not trusted code.
- Compacting or cutting the journal: loses the history replays, understanding and evaluation read.
- A snapshot per entity: far more code for the same bound, and more places to forget a field.
- Writing the checkpoint only at close: a crash would fold everything since the last clean close.

## Consequences

- A start takes about the checkpoint's read plus at most 50,000 events: measured 11 to 14 ms at
  50,000 events and 0.3 s at a million from a fresh checkpoint, and at worst about 0.6 s at a
  million with 50,000 events after it.
- Each periodic write pauses the control plane while the state is serialized and written: about
  20 ms at 50,000 events and 0.27 to 0.31 s at a million, measured as the longest gap the event
  loop saw. Far inside the realtime liveness of a ping every 15 s; an event in that moment reaches a
  headset that much later. Revisit the interval, or write when idle, if that is felt.
- Every field the projection adds must be in its exported state; a test compares the class's
  declared fields with the state's, and another restores states at many positions of recorded
  traces and a run of every mock scenario and requires the result equal to a whole fold.
- The checkpoint holds what the journal holds, in the same file with the same permissions.
