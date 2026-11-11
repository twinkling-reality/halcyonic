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
