# Scale of the control plane and the headset's client

- **Question:** What grows with a long history, on the Mac and in the headset's client, at 10,000
  and 50,000 events, with dozens of projects, many tasks streaming at once and a large folder? What
  is bounded, what must be, and what does a task cost on the wire?
- **Date:** 2026-11-03.
- **Environment:** Apple M5 Max with 64 GiB and macOS 26.7; Node 24.15.0 for the control plane;
  .NET 10.0.401 (Debug) for the client core. No headset. The machine's load average was 9 to 24
  while it ran.
- **Method:** A bench outside the repository drove the real command path (`createTestControlPlane`
  over a real SQLite journal) with a stub runtime that plays one round for each start and each
  instruction: round started, three tool calls started and completed, two reported agent messages of
  474 characters, round ended. Instructions were 616 characters. Each round instructed every task
  at once, so up to 200 tasks streamed interleaved. Each journal was then exported as the realtime
  stream a headset receives from position 0 (an empty snapshot, then every event with its changes)
  and replayed through the client's own parser, `ClientProjection` and `ActivityLog`, as
  `WorkspaceDirector` feeds them. `readFolder` was measured on folders of small files. Times are the
  least of several tries and only indications; counts and sizes are properties of the data, and
  those are what the tests below hold.
- **Status:** Measured off the device. Bounded on 2026-11-03: the Mac's folder readings (U6a) and
  the client's commands, tool names and activity lines (U6b). Held by tests: a task's share of the
  snapshot does not grow with its rounds, and what a task costs at the longest instruction. Not
  measured: tool result events (the stub sends none) and anything on a Quest.

## The journals

| | Projects | Tasks | Rounds a task | Events |
| --- | --- | --- | --- | --- |
| 10k | 10 | 50 | 15 | 9,280 |
| 50k | 40 | 200 | 19 | 46,720 |
| deep | 2 | 4 | 1,000 | 48,026 |

## On the Mac

| | 10k | 50k | deep |
| --- | --- | --- | --- |
| Snapshot message | 103 KiB | 352 KiB | 27 KiB |
| Of it, each execution | 1,256 B | 1,257 B | 1,257 B |
| Snapshot built and serialized | 0.2 ms | 0.7 ms | 0.2 ms |
| `projectBusy` | 0.016 ms | 0.048 ms | 0.026 ms |
| `recorder.record` alone | 0.09 to 0.27 ms | 0.11 to 0.13 ms | |
| A command submitted | 0.12 ms | 0.18 ms | |
| Journal on disk | 14,920 KiB | 62,860 KiB | 61,260 KiB |
| Rebuild at a restart | 174 ms | 544 ms | 351 ms |
| Projection kept, after collection | 2.4 MiB | 6.3 MiB | 4.9 MiB |

- **History does not slow the hot paths at this scale.** Recording an event takes 0.1 to 0.3 ms at
  either size. Admitting a command and the busy rule scan every command, which grows with history,
  but at 50,000 events they take 0.18 ms and 0.05 ms. A restart folds the journal at 7 to 19 µs an
  event, and the projection keeps about 100 to 180 bytes an event. A round applied live, published
  to 0, 1, 4 and 8 subscribers that each serialize every event as a realtime connection does, cost
  0.1 to 2.5 ms an event with no trend in the number of subscribers; those figures were noisy (a
  virtual clock and a loaded machine).
- **The snapshot follows tasks, not events.** Four tasks of 1,000 rounds each make a 27 KiB
  snapshot: an execution's view has no list that grows with rounds. At a 616-character instruction
  a task adds 1,934 bytes (its workstream's and execution's views), so about 8,700 tasks fill the
  16 MiB the headset's client takes in one message; the existing test holds 30 projects and 200
  tasks under an eighth of that.
- **Folder readings were kept without a bound (fixed, U6a).** The Mac keeps each task's latest
  reading of its folder between turns: 6.6 MiB for 20,000 small files (346 bytes a file; 313 at
  2,000 files), whose first reading took 4.1 s. Every task's reading was kept for good. Now at most
  100,000 files' worth are kept across tasks, about 33 MiB at that size, the least recently read
  dropped first; its next turn reads again from rest or says `no_baseline`
  ([ADR 0038](../decisions/0038-the-mac-observes-each-turns-file-changes-itself.md)).

## In the headset's client

Each journal's stream, replayed on .NET before and after U6b:

| | deep, before | deep, after | 50k, before | 50k, after |
| --- | --- | --- | --- | --- |
| Messages | 48,219 | 48,219 | 51,621 | 51,621 |
| Each message, of it parsing | 34.5 µs, 30 µs | 42.0 µs, 36 µs | 34.3 µs, 30 µs | 40.2 µs, 35 µs |
| Kept, after collection | 14.0 MiB | 4.5 MiB | 66.8 MiB | 6.4 MiB |
| Commands | 4,022 | 50 | 4,740 | 550 |
| Activity lines | 800 | 800 | 40,000 | 632 |
| Tool names held | 12,048 | 0 | 12,000 | 0 |

- **Before, three things grew for as long as the headset stayed on one journal.** Activity lines, 200
  for every task ever streamed; every command since the last snapshot; and the name of every tool
  call ever started. A reconnect keeps the activity, so only a change of journal cleared them.
- **Now (U6b):** the client keeps pending commands and the last 50 to finish, as many finished as a
  snapshot carries; a tool's name until its completion line; and every line of the 32 tasks most
  recently recorded or read, and of the rest the last 3 and the last one that is not a round's. The
  open file and a peek read their task's lines, which keeps those whole; a peek shows the last line
  that is not a round's; opening a file reads its history again. `ClientProjectionTests` and
  `ActivityLogBoundTests` hold each bound, the open file and the peek.
- The 50k stream's 550 commands are 500 still pending at its end, and 50 finished. It ends with five
  rounds of one line each for 100 tasks, so each fell out of the 32 kept whole between its own
  lines: 200 tasks keep 3 lines each, and the last 32 one more.
- Parsing took longer in the second run too (35 against 30 µs a message), so the machine was
  busier; the bounds themselves cost a microsecond or two a message.

## Risks

- **A long instruction rides with every event of its task.** Every event that changes an execution
  carries its whole view, instruction included. At 616 characters an event message is about 2.6 KB.
  At the 32,000-character maximum each round, tool, test and message event is about 34 KB, 13 times
  as much, all of it parsed by the headset, and the execution's creation 66 KB. A task with that
  instruction adds 33,318 bytes to a snapshot, so about 500 such tasks fill the 16 MiB the headset
  takes in one message. Kept for now, since the headset shows the whole instruction on request;
  `snapshot-scale.test.ts` holds both figures, so a change is deliberate. A preview in the view, with
  the whole text in `execution.created` and history, would be a contract change with an ADR.
- **A long history is folded at every restart.** The journal takes about 1.4 KB an event on disk and
  a restart folds all of it before the control plane serves anyone. Extrapolated linearly, not
  measured: a million events are about 1.3 GB and 7 to 19 s. A checkpoint of the projections would
  be an ADR-level change.
- **A command that waited while 50 others finished.** The Mac's snapshot keeps the last 50 finished
  commands in the order they were first journaled; the client keeps the 50 that finished last, so a
  command that has just finished is never dropped before what watches it reads it. After a
  reconnect, such a command can be missing from the snapshot.

## What only the headset tells

- How long parsing takes under IL2CPP on a Quest 3: 30 to 36 µs a message here, several times that
  on the device, and proportionally more for the 34 KB messages of a task with a long instruction.
- How much the client keeps on the device's heap; the figures above are .NET's on the Mac.
