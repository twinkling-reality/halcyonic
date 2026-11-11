# ADR 0056: An instruction sent during a turn waits for it, on a runtime that cannot steer

- Status: Accepted
- Date: 2026-11-11
- Complements ADR 0044 (the fix loop) and ADR 0046 (re-attach after a restart)

## Context

A person may say more while an agent works: a correction, a next step. What each runtime does with an
instruction sent while a turn runs, as verified:

- **Codex** steers the running turn with `turn/steer`. The instruction reaches the model at its next request
  ([codex-capabilities.md](../validation/codex-capabilities.md)).
- **OpenCode** steers too, with `delivery: "steer"`. The instruction reaches the model when the running step
  ends. One still waiting when the turn is interrupted is not delivered then, but with the next prompt
  ([opencode-capabilities.md](../validation/opencode-capabilities.md), 2026-09-29).
- **Claude Code** runs in the Agent SDK's streaming input mode, which documents a queue processed after the
  current work. How the CLI treats a message queued while a turn runs is unverified, so the adapter declares
  `instruct_while_running: false` ([claude-code-capabilities.md](../validation/claude-code-capabilities.md)).
  Such a queue would live in the CLI process, which a restart ends.

Until now admission refused an instruction to a running execution whose runtime does not declare
`instruct_while_running`. A person on Claude Code had to wait for the turn to end and send again.

## Decision

1. **The host holds the instruction.** For a running, verifying or waiting execution whose runtime does not steer
   but takes instructions at rest, `execution.send_instruction` is admitted as queued:
   - at most `QUEUED_INSTRUCTIONS_MAX` (3) per execution, and a fourth is refused in words;
   - no runtime is asked to queue anything;
   - a runtime that steers is unchanged;
   - only where the host's queue runs, on the person's own journal. On a recording's journal, as the fixture
     traces and the headset's demonstration are recorded, no queue runs, so such an instruction is refused as the
     runtime declares, never left waiting with nothing to send or fail it.
2. **It lives in the journal.** Its command's `command.accepted` already holds its words.
   `execution.instruction.queued` (`command_id`, `position`) records that it waits. The projection keeps the queue
   on the execution, and `ExecutionView.queued_instructions` shows it, oldest first, without the words. The
   command stays accepted, and a pending command of the project, until it is sent and confirmed or fails.
3. **Sent only after a turn that completed.** When a turn completes, the first waiting instruction is sent,
   exactly as an instruction at rest is: the same reads of the folder before the turn, and completion once the
   runtime confirms. The next one waits for that turn.
   - `execution.instruction.dequeued` (`command_id`) is recorded when its send begins, so from then on it is
     not waiting: it cannot be dropped or failed unsent, and it ends as any sent instruction does (AUDIT2,
     2026-11-11).
   - A restart fails what is still waiting as `queued_stopped`, effect `none`, before the restart's own pass
     fails other pending commands with effect `unknown`. A send already begun still fails with `unknown`: it may
     have reached the runtime.
4. **Never sent blindly, never lost silently.** Every instruction still waiting fails unsent, effect `none`,
   with a code of its own:
   - a turn that failed: `queued_turn_failed`;
   - a turn interrupted, by Stop or cut by a restart: `queued_stopped`;
   - a Stop accepted: `queued_stopped`;
   - a session that cannot be kept (`execution.state_unknown`): `queued_session_lost`;
   - a restore of the project accepted: `queued_files_restored`.

   The person sees it was not sent, and may send it again.
5. **The person may drop one before it is sent.** `execution.drop_queued_instruction` (`execution_id`,
   `command_id`), low consequence, fails that one with `queued_dropped`. Only a waiting one can be dropped.
6. **The person's words come before the product's own.** A waiting instruction is a pending command of its
   project, so `projectBusy` holds back the automatic check, and through it the fix loop (ADR 0044), until the
   turn that took the last queued instruction has ended. No rule in auto-fix changed. Admission already refuses a
   second instruction at rest to a task with a pending one, so nothing passes the queue.
7. **A restart keeps the queue** in the journal. The cut turn is reported interrupted (ADR 0046), so what was
   waiting fails as `queued_stopped`, never sent after an end the person did not see.

## Alternatives considered

- **Verify the CLI's queue and declare `instruct_while_running` for Claude Code.** It is cheaper, but the queue
  would live in the CLI process: a restart loses it with no trace in the journal, it would serve one runtime
  only, and the person could neither see nor drop what waits.
- **Send what waits after any turn's end.** An instruction sent after an error would go to an agent in a state
  the person has not seen. The lead's rule is to send after a completed turn only.
- **No bound.** One person's words should not pile up unseen behind a long turn. Three is enough for "and also",
  and few enough to read.

## Consequences

- Claude Code and the mock runtime take instructions during a turn. Codex and OpenCode keep steering.
- **OpenCode's held steer is still open.** A steered instruction still waiting when its turn is stopped reaches
  the model only with the next prompt, and its command already completed when OpenCode accepted it
  (OPEN_QUESTIONS.md).
- The headset shows what waits and offers Drop (FIELD's words; a call note is in FIELD's folder).
- **Revisit when** a runtime documents and verifies its own queue with an outcome the host can observe.
