# Audit of the event-sourced core and the control plane

- **Question:** Does the core hold to its own rules (state only through the journal, accepted is not confirmed,
  provenance kept, no certainty fabricated at a restart), does it stay fast and bounded over a long session, and do
  its secrets and folders stay where they belong?
- **Date:** 2026-11-03.
- **Environment:** this Mac (Apple M5 Max, macOS 26.7), Node 24.15.0, .NET 10.0.401 for the client core, the pinned
  OpenCode and Codex where a test runs one; main from 466e5326 to 056bf71c.
- **Method:** read the core (`packages/{contracts,domain,runtime-core}`, `apps/control-plane/src/{core,journal,
  network,checkpoints,observer}`, the check and preview host, the client core's `ClientProjection` and
  `ActivityLog`), ranked twelve suspected defects, and took them most severe first; a thirteenth turned up while
  fixing one. Each defect found was proved by a test that failed before its fix; security fixes were read by AUDIT
  before they landed. Scale was measured, not guessed
  ([control-plane-scale-2026-11-03.md](control-plane-scale-2026-11-03.md)).
- **Status:** fixed as listed, each on main by the commit named; those named by their subject were at AUDIT or
  waiting for their batch when this was written. What remains is below, each with why it is not fixed.

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
| A turn a restart cut short counted as running for good, so every later check of its project read unknown, and its preview and Play were refused for good with words asking for a check that could not help | Verified (a test) | A check recorded after the restart starts current; one recorded before it stays unknown ("Stop a turn a restart cut short from holding every later check unknown", #13) |
| A start was admitted while the task's execution ran, so two agents worked in one folder | Verified (INTEGRATION's journey) | One execution at a time in a task (b263b05d, words 93de0913, ADR 0020 amendment) |
| An instruction to a resting execution was admitted while a newer one of its task ran, so a second Send during a switch set the old agent going too | Verified (AUDIT's switch verdict, then a test) | Refused the same way ("Refuse instructing a resting execution while another of its task is active or about to be", ADMIT-2) |
| The Mac kept every task's folder reading for good | Measured | Bounded across tasks (fa2a577f, ADR 0038 amendment) |
| The headset's client kept every task's activity, every command and every tool name for a whole connection | Measured | Bounded (e6458aaa); the snapshot and the client keep the same 50 finished commands (9702766c) |
| Every turn's start and end, and every file reading, visited all of its project's checks, none ever dropped, so a check after each changing round, as the automatic check makes, cost the square of the rounds, in every restart's fold too | Measured (a count test: 59,700 visits over 200 rounds) | Only checks whose files can still change are visited, 597 over the same rounds ("Visit only the checks whose files can still change", #8a) |
| Private project names in tracked files | Verified | Read from an ignored local file (04d658ae) |

Pinned by count tests: a task's share of the snapshot does not grow with its rounds, and what a task costs at the
longest instruction (cccd0a4b).

## Checked, no defect found

- **No contract-valid event stops the projection.** An apply that throws would leave its event journaled and
  unprojected, and every rebuild would meet it again. A seeded generator over the contract's own schemas makes
  events of all 48 types, each checked by `parseEventEnvelope`, and folds 600 of them in random orders and scopes for
  each of 30 seeds, the projects, tasks and executions they refer to made first: none throws, and the same events
  fold again to the same views (`packages/domain/src/projection-events.test.ts`). The test fails when the projection
  is made to throw for one event type. What the generator never makes stays untried: shapes past its small pools of
  ids and words.
- **The loopback guard and proof.** Only this server's `127.0.0.1`, `localhost` and `[::1]` with its port pass the
  Host check; sixteen other spellings are refused (no port, another port, a trailing dot, short, numeric and octal
  IPv4, IPv4 written as IPv6, `::1` written out, user information, two hosts). No spelling of a path reaches more than
  health without the token on the real server. The proof names the address a connection reached, differs for another
  address, port, token or challenge, and only a 32-byte lowercase hex challenge is answered
  (`http/security.test.ts`, `http/server.test.ts`).
- **Pairing under concurrent exchanges, and revocation.** An exchange already under way when the third failure locks
  the window gets no guess, the right code included, and is not counted; the right code pairs while the window is
  open, and the window then closes for the rest (`network/pairing.test.ts`). A slow read answered after its device is
  revoked gets the revocation's refusal, not the answer; with the check at the answer removed, the test fails
  (`network/network.test.ts`).
- **Accepted is not success.** Every runtime command (a start, an instruction, an approval's answer, a question's
  answer, a stop) stays accepted until its runtime confirms it, and one its runtime never confirms fails at the
  timeout with an unknown effect; with that effect made certain, the test fails (`core/control-plane.test.ts`).

## What remains, and why

Each was found by this audit and is not fixed by it.

- **A long history is folded at every restart.** Risk. A restart replays the whole journal before the control plane
  serves anyone: 7 to 19 µs an event here, so a million events would take 7 to 19 s and about 1.3 GB on disk
  (inference, extrapolated linearly). A checkpoint of the projections would change how the journal is read at a
  restart, an architecture decision, so it is an open question
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- **A long instruction rides with every event of its task.** Risk, kept by decision: about 34 KB an event at the
  32,000-character maximum, all parsed by the headset. A test holds the figure, so a change to it is deliberate.
- **Checks with no change between them are each visited at every round until the files next change.** Verified in
  the code. A changed check leaves the walk, so the automatic check after each changing round costs each round the
  same; checks asked for with nothing changed between them all stay in it until a change. Each such check is a whole
  build and browser run, so they come slowly.
- **Each event is serialized once per realtime connection.** Verified in the code; measured with up to 8
  subscribers, with no trend in the cost. Usually two clients are connected.
- **A summary of every tool result is kept per execution.** Verified in the code, not measured: the scale bench
  sends no tool results.
- **Not attacked in this audit:** `folder-safety.ts` against case and hard-link games, and error text and URLs in
  journal fields other than those above. No defect is suspected in them. `reads_confined` is recorded by each check
  and Play on the Mac refuses without it (tested); a Mac preview may serve an unconfined copy, its record saying so
  (ADR 0041); the headset's Quest Browser gate refuses one too, but no test pins that refusal.
- **Reads that follow links, outside the core's files:** the check's source scan and layer probe (`scan.ts`,
  `laptop-probe.ts`) read by path after a check of the path, so a file swapped for a link between the two is read
  through it; only line numbers, a hash and whether files match leave them. `catalog-match.ts` reads by path too.
- **Smaller:** the control plane's close does not wait for a checkpoint being written; the check's build profile
  lets the build write to `/dev/tty`; a tool call that never completes keeps its name in the headset's activity
  until the journal changes (about 200 bytes); the busy rule's refusals name raw statuses
  ("waiting_for_human").
