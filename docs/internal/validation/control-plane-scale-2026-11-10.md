# A start at any journal size, and the busy rule on a long journal

- **Question:** How long does a start of the control plane take at 50,000 and about a million events,
  where does the time go, and what does a checkpoint of the projection change? What do the busy rule
  and check recording cost on a long journal?
- **Date:** 2026-11-10.
- **Environment:** Apple M5 Max with 64 GiB, macOS 26, Node 24.15.0. Load average 7 to 20 from other
  work while it ran: times are indications, the least of three tries where repeated.
- **Method:** The bench of [2026-11-03](control-plane-scale-2026-11-03.md) (outside the repository)
  built the journals through the real command path with its stub runtime: 50k (40 projects, 200
  tasks, 19 rounds, 51,620 events) and 1m (40 projects, 4,000 tasks, 19 rounds, 937,021 events,
  generated in 413 s, 2,258 events a second). Probes outside the repository timed each stage of a
  start, the projection's state under `v8.serialize`, `createTestControlPlane` opening each journal
  copy twice (a whole fold, then from the checkpoint its close wrote), and folding the last 50,000
  events alone.
- **Decision:** [ADR 0050](../decisions/0050-a-start-restores-the-projection-from-a-checkpoint-and-folds-only-the-journal-after-it.md)

## Where a whole fold goes

| Journal | Events | Envelopes | SQL read | JSON parse | Validation | Fold | Total |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 50k | 51,620 | 37.0 MiB | 214 ms | 79 ms | 131 ms | 44 ms | 469 ms |
| 1m | 937,021 | 660 MiB | 655 ms | 1,346 ms | 1,356 ms | 628 ms | 3,986 ms |

The 1m journal's file is 1.2 GB. The 2026-11-03 extrapolation for a million events (7 to 19 s) was
pessimistic here: 3.5 to 6.5 s, the latter with the journal's new migration in the same open.

## The projection's state

| Journal | State | gzip | Serialize | Deserialize |
| --- | --- | --- | --- | --- |
| 50k | 3.9 MiB | 0.4 MiB | 5 to 8 ms | 8 to 13 ms |
| 1m | 69.2 MiB | 6.8 MiB | 122 ms | 211 ms |

It grows with tasks and commands, all of which the projection keeps: about 17 KiB a task here.

## A start, through the control plane

| Journal | Whole fold | Close, writing the checkpoint | Start from it |
| --- | --- | --- | --- |
| 50k | 411 ms | 17 ms | 11 to 14 ms |
| 1m | 6,531 ms (with the migration) | 503 ms | 289 to 375 ms |

The last 50,000 events of the 1m journal read, validated and folded alone: 195 to 345 ms. So after
a crash, with a checkpoint written every 50,000 events, a start at a million events takes at most
about 0.6 s here.

## The pause a checkpoint write makes

The write is synchronous: it serializes the state and writes the row in one transaction under
`synchronous = FULL`, so nothing else runs meanwhile. One write, timed by the same function the
periodic write calls, with the longest gap between 1 ms timer ticks around it (three writes each,
load average about 12):

| Journal | State | Write, and the longest gap the loop saw |
| --- | --- | --- |
| 50k | 3.9 MiB | 19 to 21 ms |
| 1m | 69.2 MiB | 270 to 308 ms |

Once every 50,000 events, and at close. The realtime server pings every 15 s and drops a client only
when a pong is missed (REALTIME.md), so a 0.3 s pause is far inside it; an event in that moment
reaches the headset that much later. (The 503 ms at close in the table above is the whole close.)

## The busy rule and check recording

The busy rule and the restore check, for each of the 50k journal's 40 projects: 0.691 ms in all
before indexing each project's executions and the pending commands, 0.090 ms after (17.3 to 2.2 µs a
project). Before, each read every execution and command the projection keeps, so its cost grew with
the journal; now it grows with the project's own tasks.

## Not measured

A real long-lived journal (the owner's) and anything on a Quest. The periodic write's pause while
work streams to a headset at a million events.
