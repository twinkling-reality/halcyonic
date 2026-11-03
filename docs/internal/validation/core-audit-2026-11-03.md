# Audit of the event-sourced core and the control plane

- **Question:** Does the core hold to its own rules (state only through the journal, accepted is not confirmed,
  provenance kept, no certainty fabricated at a restart), does it stay fast and bounded over a long session, and do
  its secrets and folders stay where they belong?
- **Date:** 2026-11-03.
- **Environment:** this Mac (Apple M5 Max, macOS 26.7), Node 24.15.0, .NET 10.0.401 for the client core, the pinned
  OpenCode and Codex where a test runs one; main from 466e5326 to 056bf71c.
- **Method:** read the core (`packages/{contracts,domain,runtime-core}`, `apps/control-plane/src/{core,journal,
  network,checkpoints,observer}`, the check and preview host, the client core's `ClientProjection` and
  `ActivityLog`), ranked twelve suspected defects, and took them most severe first. Each defect found was proved by
  a test that failed before its fix; security fixes were read by AUDIT before they landed. Scale was measured, not
  guessed ([control-plane-scale-2026-11-03.md](control-plane-scale-2026-11-03.md)).
- **Status:** fixed as listed, each on main by the commit named; the two named by their subject were at AUDIT or
  waiting for their batch on 2026-11-03. What remains is below, each with why it is not fixed.

## Found and fixed

| Finding | Kind | Fix, on main |
| --- | --- | --- |
| Undo's restore could be steered outside the project by a process left running in it, swapping a folder for a link between the host's check and its write | Verified (a race test) | Undo's git and removals run inside Seatbelt profiles that keep them in the project (4a0062ab, ADR 0036 amendment) |
| The check's build read outside the agent's reach and could inline what it read into the copy the check serves | Verified | The build's reads closed to its scope, Node and the system's own paths (dcdd5927, ADR 0041 amendment) |
| The check's browser kept a terminal, could signal other processes and read other folders | Verified | Closed in its profile, and it leads its own process group (146b850a) |
| Omnorium's credential and the network listener's key were not held, so runtime text could carry them to devices | Verified | Both held, the key whole and line by line (d6a1889f) |
| An agent's own messages kept exact copies of held secrets | Verified | Taken out, named (b15f7d5b) |
| A checked page's exception texts reached the journal unredacted | Verified | Treated as error text ("Take what Halcyonic holds and what looks like a key out of a checked page's exception texts") |
| Changes made between turns, by an undo or by the person, were credited to the next turn | Verified (a real runtime too) | The folder is read before a turn starts from rest (1505ac02, ADR 0038 amendment) |
| The observer and undo's compare could read through a folder swapped for a link | Verified | Opened with `O_NOFOLLOW_ANY`, which refuses a link anywhere in the path (de207b0c) |
| A task unknown before a restart kept its project busy for good | Verified | Marked unknown because of the restart, so its project frees (ee108021) |
| A start was admitted while the task's execution ran, so two agents worked in one folder | Verified (INTEGRATION's journey) | One execution at a time in a task (b263b05d, words 93de0913, ADR 0020 amendment) |
| The Mac kept every task's folder reading for good | Measured | Bounded across tasks (fa2a577f, ADR 0038 amendment) |
| The headset's client kept every task's activity, every command and every tool name for a whole connection | Measured | Bounded (e6458aaa); the snapshot and the client keep the same 50 finished commands ("Keep in a snapshot, and in the headset's commands, the 50 that finished last") |
| Private project names in tracked files | Verified | Read from an ignored local file (04d658ae) |

Pinned by count tests: a task's share of the snapshot does not grow with its rounds, and what a task costs at the
longest instruction (cccd0a4b).

## What remains, and why

Each was found by this audit and is not fixed by it.

- **A long history is folded at every restart.** Risk. A restart replays the whole journal before the control plane
  serves anyone: 7 to 19 µs an event here, so a million events would take 7 to 19 s and about 1.3 GB on disk
  (inference, extrapolated linearly). A checkpoint of the projections would change how the journal is read at a
  restart, an architecture decision, so it is an open question
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- **A long instruction rides with every event of its task.** Risk, kept by decision: about 34 KB an event at the
  32,000-character maximum, all parsed by the headset. A test holds the figure, so a change to it is deliberate.
- **Checks are walked at every round.** Verified in the code, not measured: every round's start and end walks all of
  its project's checks, and none is dropped, so a long-lived project with a check after each changing round costs
  the square of its rounds. The scale bench ran no checks, so the cost at today's sizes is not known.
- **Each event is serialized once per realtime connection.** Verified in the code; measured with up to 8
  subscribers, with no trend in the cost. Usually two clients are connected.
- **A summary of every tool result is kept per execution.** Verified in the code, not measured: the scale bench
  sends no tool results.
- **An event the projection cannot apply would stop every later start.** Verified in the code: an apply that throws
  leaves its event journaled and unprojected, and every rebuild meets it again. No such event is known; no test yet
  applies every shape of contract-valid event.
- **Not attacked in this audit:** the loopback proof, pairing under concurrent attempts and revocation against long
  requests, `reads_confined` before a preview or a play opens, accepted-is-not-success across every command type,
  `folder-safety.ts` against case and hard-link games, and error text and URLs in journal fields other than those
  above. No defect is suspected in them.
- **Reads that follow links, outside the core's files:** the check's source scan and layer probe (`scan.ts`,
  `laptop-probe.ts`) read by path after a check of the path, so a file swapped for a link between the two is read
  through it; only line numbers, a hash and whether files match leave them. `catalog-match.ts` reads by path too.
- **Smaller:** the control plane's close does not wait for a checkpoint being written; the check's build profile
  lets the build write to `/dev/tty`; a tool call that never completes keeps its name in the headset's activity
  until the journal changes (about 200 bytes); the busy rule's refusals name raw statuses
  ("waiting_for_human").
