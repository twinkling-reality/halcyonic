# Plan-only modes per runtime

## 2026-11-11: what each pinned runtime offers that changes no file

The question: can a person talk a change through with an agent before any file changes, and how would each
runtime Halcyonic ships do it? Read from each pinned runtime's own surface on this Mac; no model was run.

- **Claude Code** (Agent SDK 0.3.283). The SDK's `PermissionMode` has `plan`, and Halcyonic's adapter already
  accepts it as a start option (`permission_mode`, `packages/integrations/claude-code/src/claude-agent-runtime.ts`,
  `PERMISSION_MODES`), with `default`, `acceptEdits` and `dontAsk`. The headset sends no start option today, so a
  person cannot choose it. Not run here: what Claude Code does in plan mode on this Mac.
- **Codex** (0.157.0, `codex app-server generate-json-schema` on the pinned binary). A collaboration mode exists,
  `ModeKind` = `plan` or `default`, but the stable v2 surface Halcyonic uses (ADR 0011) only reports it
  (`ThreadResumeResponse`, `ThreadSettingsUpdatedNotification`); `TurnStartParams` does not take it. What
  `turn/start` does take, "for this turn and subsequent turns", is `sandboxPolicy`, whose variants are
  `dangerFullAccess`, `readOnly`, `externalSandbox` and `workspaceWrite`, and `approvalPolicy`. So the stable way
  to a turn that writes nothing is a `readOnly` sandbox policy. On a starter project Halcyonic runs Codex under
  `externalSandbox` inside its own Seatbelt profile (ADR 0041); a read-only turn there would replace that policy
  for its turns, which is not checked. Not run here.
- **OpenCode** (2.0.18, read from the pinned binary). It ships a built-in primary agent `Plan`: "Read-only agent
  for exploring the codebase and planning work before implementation". Its rules deny `edit` everywhere except its
  own plan folder (`.opencode/plan` in OpenCode's home, where edits and that external directory are allowed), and
  allow questions. It does not deny shell commands, which Halcyonic's sessions already ask the person about
  (ADR 0028). Halcyonic's adapter does not choose an agent today, so its sessions use OpenCode's default agent.
  Not run here.

None of the three is offered to the person yet. Halcyonic's own read-only path is the assistant's whole-project
and all-work request (ADR 0030), which can read and answer but sends no instruction.

## 2026-11-11: run on the pinned binaries (VRIFY, PLAN)

No model: each runtime's scripted or fake provider asks for one tool call, and every ask is answered No unless said.

### OpenCode 2.0.18

The session API takes `agent` at creation (`agent: "plan"`) and reports it back. With Halcyonic's own session rules
(`sessionPermissions`):

| agent | level | `write` | `edit` | shell `echo > file` |
| --- | --- | --- | --- | --- |
| `plan` | ask | refused, no ask, nothing written | refused, no ask, unchanged | asks |
| `plan` | never_ask | refused, no ask | refused, no ask | **runs, the file is written** |
| default | ask | written, no ask | changed, no ask | asks |
| default | never_ask | written | changed | runs, written |

- So the plan agent refuses the edit tools, not shell commands. A plan changes no file only while every shell command
  asks: Halcyonic offers it (`offers_plan`), starts it on that agent, and never with `never_ask`.
- In the adapter's own e2e test, inside the sandbox: a plan's write is refused unasked, its shell command asks, and
  its start records `plan: true` (from the agent OpenCode reports back).
- **A later instruction in the same session:**
  - A prompt naming no agent stays on `plan`; so does one naming `agent: "build"` in its body.
  - OpenCode's `POST /api/session/:id/agent` (`session.switchAgent`, "the agent used by subsequent provider turns")
    answers 204. After switching to `build` the next prompt's write is written; switching back to `plan` refuses
    writes again.
  - So the work can go ahead from the plan in the same session, by switching its agent. Halcyonic does not do that
    yet: an instruction carries no plan choice today.

### Claude Code 2.1.283 (the bundled CLI, the SDK's streaming input, a fake Messages API, unconfined)

- The `system/init` message reports the mode a start asked for: `plan`, `acceptEdits` and `default` each read back as
  given. (Corrected 2026-11-11 14:14Z: this record first said init read `default` whatever the option. That probe kept
  the last init it saw, and after `setPermissionMode('default')` Claude Code sends a second `system/init`, with
  `default`, on the next turn, which overwrote the first.)
- With `permissionMode: 'plan'`, a Write is not refused. It goes to `canUseTool`, as in the default mode, and runs
  once allowed. So on Claude Code a plan changes no file only while the person answers No, as every Claude Code start
  already asks; what plan adds is Claude Code's own planning, which a fake model cannot show.
- `setPermissionMode('plan')` and `setPermissionMode('default')` take effect, reported as `system/status` messages
  with the mode. After `default`, the next instruction goes ahead in the same session.
- **Not declared yet:** its plan bounds a write no differently from a default start's, every write asking. Its start's
  mode is observable in `system/init`, so a later unit may record a plan from it and declare `offers_plan`; until
  then Claude Code offers no plan.

### Codex 0.157.0

`codex app-server generate-json-schema`: the stable `TurnStartParams` has no plan mode; `--experimental` adds
`collaborationMode: { mode: "plan" | "default", settings }`. Halcyonic uses the stable surface (ADR 0011), and a plan
made of read-only turns would be Halcyonic's mechanism, not the runtime's, so Codex does not offer a plan. A start
asking for one is refused in admission.

## 2026-11-11: carrying a plan out, in the same session (VRIFY, PLAN 4a)

- **The contract:**
  - `execution.send_instruction` takes `leave_plan` (optional; only `true` means anything).
  - The adapter reports `runtime.plan.left` once the runtime confirms the switch, before the instruction is sent.
  - `ExecutionView` carries `plan`, `plan_left` and `unconfined_reason`.
- **Admission:** `leave_plan` is refused on a runtime that offers no plan (`capability_unsupported`), and on an
  execution that is not a plan now, never one or already left (`invalid_state`, "This task is not a plan now, so there
  is no plan to carry out. Send the instruction without it.").
- **OpenCode 2.0.18, the adapter's e2e test inside the sandbox:**
  - A plan's write is refused unasked.
  - Then an instruction with `leave_plan` switches the session to `build` (`POST /api/session/:id/agent`) and reads it
    back (`GET /api/session/:id` reports `agent: "build"`).
  - It records `runtime.plan.left` before the turn starts, and the write goes ahead in the same session.
  - A read-back naming `plan` refuses the instruction (`runtime_refused`), with nothing sent and nothing recorded: the
    task is still a plan.
  - A read-back that fails or names another agent, after OpenCode took the switch, also refuses it with nothing sent,
    but with effect `unknown` and words that say the task may have left its plan (AUDIT3 4a S1;
    `planSwitchOutcome`, unit-tested). Both paths are read, not run: the pinned binary always confirmed.
- **Only at rest** (completed, failed, interrupted): while a plan's turn runs or waits for the person, `leave_plan` is
  refused ("Carry the plan out once it stops."). A steer switched mid-turn could still be answered on the plan
  (AUDIT3 4a S2).
- **The level** is the start's throughout: leaving a plan changes the agent, never the session's rules.
- **A re-attach after leaving** expects the session on `build`: the bounds' `plan` is false once it was left.

## 2026-11-11: Claude Code offers a plan (VRIFY, PLAN 4b)

The bundled CLI 2.1.283, a fake Messages API, `plan.e2e.test.ts` (opt-in with `CLAUDE_CODE_BIN`).

- **A plan start:** it runs with `permissionMode: 'plan'`. Its start records `plan: true` from the mode the first
  `system/init` reports (`plan`); a start that also names another `permission_mode` is refused.
  - In plan mode a Write asks, as every Claude Code write does, and refused it writes nothing. So Claude Code's plan is
    bounded by the person's answer to each write, plus Claude Code's own planning, which a fake model cannot show.
  - `offers_plan` is declared.
- **Carrying it out** (`leave_plan`): the adapter asks the running session for `setPermissionMode('default')`, waits
  up to 10 s for a `system/status` message reporting `default`, records `runtime.plan.left`, then sends the
  instruction. In the run the next Write asked, was approved, and was written, in the same session.
  - No confirmation, or a failed switch: the instruction is refused (`runtime_refused`), nothing is sent and nothing
    is recorded. That path is read, not run.
  - A re-attached session that has not resumed since the restart has no process to confirm the switch, so carrying
    its plan out is refused in words until an instruction resumes it.
- **Re-attach:** a session whose start recorded a plan resumes in plan mode; one whose plan was left resumes in
  default mode.

