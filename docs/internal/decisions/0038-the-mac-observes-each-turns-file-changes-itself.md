# ADR 0038: The Mac observes each turn's file changes itself

- Status: Accepted
- Date: 2026-10-29
- Complements ADR 0031 and ADR 0036

## Context

A person watching an agent build something, a child making a game included, should see which files
appear, change and disappear as the work goes on, and trust it. What an agent says it did is its own
claim (`reported`). Codex reports its own file-change items (ADR 0031); OpenCode reports none in a
form Halcyonic records; neither is the folder as it is.

Undo's checkpoints (ADR 0036) would give exact differences, but undo is off unless the host turns it
on, and its store keeps a copy of every file. The changes must be visible with undo off, and without
keeping contents.

## Decision

The control plane reads a task's folder itself and journals what each turn changed, for every
execution it starts in a folder:

- It reads the folder when the start is admitted, before the runtime is asked, and again whenever the
  runtime reports a turn completed, failed or interrupted. Each reading keeps, per file, the path
  relative to the folder, its size, its modification time, its SHA-256 and its line count; never its
  contents. A file whose size and time are unchanged is not read again.
- It skips `.git` and `node_modules` at any depth and never follows a link. It reads at most 20,000
  files and 256 MB; a larger folder, a folder that has gone, or a file it cannot read gives a turn's
  record with `skipped` and the reason instead of lists. A start waits at most 10 seconds for the
  first reading; past that it goes ahead, the late reading is not used, and the first turn says
  `no_baseline`.
- The control plane's own start turns it on; it is off by default for recorded traces and tests.
- After each turn it journals `execution.files.observed`: the turn, the files created (lines, bytes),
  changed (lines before and after, bytes) and deleted (lines before), at most 100 of each with
  totals, provenance `observed`. A line count is null for a binary file or one over 2 MB.
- A turn without an earlier reading in this process, as after a restart, says `no_baseline`. Turn
  events of executions this process did not start, such as a recorded run played back, are history
  and are not observed.

The record says what changed in the folder while the task ran; the Mac cannot tell whether the agent
or a person changed a file, and clients say so.

## Alternatives considered

- **Undo's checkpoints.** Exact lines added and removed, but off by default and they keep contents;
  when undo is on, they can refine this record later.
- **Watching the folder live (file system events).** Cards as files change during a turn, but bursty
  and lossy under load; Codex's own items already give live detail, and a per-turn reading stays the
  check.
- **The agent's own report.** It is a claim, not an observation.

## Consequences

- The live build view can show created, changed and deleted files with line counts as fact, beside
  the agent's own words.
- Large folders, such as a Unity project with its library, are skipped and say so; ignore files are
  not applied without git.
- Two tasks in one folder at once would see each other's changes; that is open with undo's.
