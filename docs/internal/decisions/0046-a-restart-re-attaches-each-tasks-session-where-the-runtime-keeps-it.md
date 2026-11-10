# ADR 0046: A restart re-attaches each task's session where the runtime keeps it

- Status: Accepted; the control plane's side built with the mock and a stub runtime, no real
  adapter declares it yet
- Date: 2026-11-10

## Context

Before this decision a restart of the control plane recorded every execution in flight
`unknown` (`control_plane_restarted`) and re-attached nothing. Executions at rest were hit too,
without a record: an instruction to a task that had finished before the restart was admitted and
then refused by the adapter (`execution_unknown_to_runtime`), so no task from before a restart could
be told anything again.

At start, before anything else, the control plane stops every runtime process the last one left
(`stopStaleRuntimeServers`), so a turn that was running when it ended never goes on. What can go on
is the session. Verified on 2026-11-10 on the pinned binaries with fake providers
([record](../validation/restart-reattach.md)):

- Codex 0.157.0 keeps a thread after its server ends: `thread/resume` by thread id on a new server
  over the same `CODEX_HOME` continues it with its history, and the killed turn reads
  `interrupted` (already used for a server that dies in-process; end to end test).
- OpenCode 2.0.18 keeps a session in its store after its server is killed mid-turn: a new server
  over the same data folder reads it back, lists nothing running, and takes a new prompt whose
  model request carries every earlier prompt. The session's `outcome` still reads the turn before
  the cut one.
- Claude Code (CLI 2.1.283, agent SDK 0.3.283) keeps a session's transcript after its process is
  killed mid-turn: a new query with `resume` and the session id continues it under the same id with
  its history, the CLI having written a placeholder answer for the cut turn.

## Decision

- A runtime's descriptor declares `resumes_after_restart` once a runtime test shows its sessions
  outlive their processes; absent means a restart loses them. An adapter that declares it
  implements `reattach`, and one that implements it declares it (`capabilityProblems`).
- `reattach` takes the execution, its native id and the bounds it started with, from the journal:
  the start's options and chosen model, the approval level the runtime confirmed, whether the
  runtime reported it confined, its folder, and the turn in flight, if any. The adapter re-attaches
  only with exactly those bounds, checking what the runtime reports as it checks a start and the
  folder against the directory policy again; anything else it refuses. Once re-attached it emits
  `runtime.connection.restored` and, for the cut turn, `runtime.turn.interrupted` with the turn's
  id, marked inferred, which also ends what that turn left open.
- At start, the control plane records each execution in flight unknown as before, then asks its
  runtime to re-attach it, one at a time per runtime. An execution at rest is re-attached when the
  next action on it comes, before the action runs; an execution this process started needs none.
  The projection keeps each execution's start command (`execution.created`'s causation), from which
  the bounds are read.
- An execution whose runtime does not declare it, that never reported a native id, whose runtime
  refuses or does not answer within the command timeout, is recorded `execution.state_unknown` with
  the code `session_lost`, and the action that found it fails with `session_lost`, effect `none`.
  Like `control_plane_restarted`, it holds its project busy no longer: nothing of it runs. A later
  restart leaves it lost. The words a person reads for it are the headset's, never the contract's.
- Fixture journals are historical: nothing in them is re-attached or lost.

## Alternatives considered

- Keeping runtime processes alive across a restart, so the running turn goes on. Codex's server
  ends with its standard input, and an OpenCode server or Claude Code process nobody supervises
  keeps running commands and asking for approvals nobody can answer; the stale stop exists for that.
- Re-attaching every execution at start. It would launch every runtime and load every session ever
  started for work nobody may touch again; at-rest work loses nothing by waiting for its next action.
- A new event type for a re-attach. `runtime.connection.restored` already says the runtime answers
  for the session again, and the projection already clears an unknown state on it.

## Consequences

- A task from before a restart is directed again from the headset where its runtime keeps it; one
  that cannot be is said to be lost instead of left unknown.
- Each adapter must rebuild its in-memory record of a session from the request alone, and refuse a
  session that comes back with other bounds. Its end to end test kills a host mid-turn and
  re-attaches from a second adapter.
- A runtime upgrade re-runs the probes: the stale OpenCode outcome and the Claude Code placeholder
  answer are quirks of the pinned versions.
- Revisit if a runtime offers to carry on a turn across its own restart.
