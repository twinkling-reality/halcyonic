# ADR 0036: Halcyonic undoes a task's changes itself, from checkpoints it keeps outside the project

- Status: Accepted
- Date: 2026-10-28
- Complements ADR 0002, ADR 0020 and ADR 0028

## Context

A person who lets an agent change a project needs a way back: every file as it was before the task,
or before one turn of it, with proof that it really is as it was. Agent-built changes are often
wrong the first time, so the way back has to be ordinary, not an emergency.

No runtime Halcyonic supports declares an undo. The byte-exact restores done so far were the work
of a supervising script around a runtime: the agent was told to run reviewed commands against a
baseline kept in a separate Git directory, and the script compared every file afterwards. That
depends on the model running exactly the right commands, needs approvals, and still needs something
other than the agent to check the result. Codex's own sandbox also refuses index writes in a linked
worktree ([codex-capabilities.md](../validation/codex-capabilities.md), 2026-10-16).

Probes with the system's git (2.50.1 on this Mac, 2026-10-28, scratch folders only) showed four
traps that decide how a host-side undo must work:

- a failed `git add -A` (for example, a repository nested inside the folder) leaves an empty index,
  and restoring that tree removes every file;
- ignore rules read from the folder as it is now are untrusted: deleting `.gitignore` made a clean
  remove what it had ignored, and a new folder whose own `.gitignore` says `*` hid itself and its
  files from every check that used the folder's rules;
- the project's `.gitattributes` can ask git to change line endings or run filters on the way in
  and out;
- the person's global git settings (excludes, hooks, a file system monitor) would apply too.

## Decision

Undo is Halcyonic's own host action, never a runtime capability and never an instruction to an
agent. It is off unless the host names a git by absolute path in `HALCYONIC_GIT_BIN`; with it off,
no checkpoint is taken and `project.restore` is refused as unsupported.

**Checkpoints.** The host keeps, per project, a Git repository of its own in
`<data dir>/checkpoints/<project_id>.git`, with SHA-256 objects and the project's folder as its work
tree. It never writes to the project's own `.git`, and sits outside every sandbox's writable
folders, so no agent can change a checkpoint. A checkpoint is taken:

- `before_task`: once `execution.start` is admitted for a runtime that works in the project's
  folder, before the runtime is asked, outside the command's timeout. If it cannot be taken, the
  start goes on and `project.checkpoint.failed` says so: the task has no undo from before it;
- `after_turn`: whenever a runtime reports a turn completed, failed or interrupted in that folder;
- `before_restore`: just before a restore writes anything, holding also the files the restore is
  about to remove, so a restore can itself be undone.

A checkpoint holds every file of the folder except the project's own `.git`, what the project's
ignore rules ignore at that moment, and nothing at all when another repository is nested inside
(`nested_repository`), more than 200,000 files or 2 GB would be kept (`too_large`), or a file cannot
be read. What was ignored is recorded with the checkpoint. File modes and symbolic links are kept as
git keeps them, the link itself and never what it points to.

**Restore.** `project.restore` names a checkpoint of the project. It is `review_required`, admitted
only when no task in the project is starting, running, verifying or waiting, none is in an unknown
state other than after a restart, and no command in the project waits for its outcome. While it
runs, starts and instructions in the project are refused. The host then:

1. takes the `before_restore` checkpoint; if it cannot, nothing is changed and the restore fails
   with effect `none`;
2. removes anything standing where the checkpoint needs a folder or a file (a link, a folder), as
   itself, never following it;
3. writes every file of the checkpoint with git;
4. removes every untracked path that the checkpoint did not record as ignored, judged against the
   recorded list and never against the folder's ignore files now;
5. reads every file again itself and compares it with the checkpoint's blob id, type and mode, and
   lists the folder for anything left over.

Only when step 5 finds the folder equal does it journal `project.restored` (the checkpoint, its
tree hash, the files compared, the paths changed back, removed and returned) and then complete the
command. Otherwise it journals `project.restore_incomplete` with what still differs and fails the
command, with effect `unknown` once anything was written. A restore in flight when the control plane
stops is failed with effect `unknown` at the next start, like any command awaiting its outcome.

**Isolation.** Git runs with no system or global configuration, a home of the store's own, no
hooks, no file system monitor, literal path specs, and store attributes that turn off line-ending
conversion, filters and encodings for every path. It is never looked up on PATH.

**What the journal holds.** Hashes, counts, reasons and at most 100 paths per list, each relative to
the project's folder; never file contents and never another path. The folder a checkpoint was taken
of is kept in the store, and a restore into another folder is refused.

## Alternatives considered

- **Ask the runtime to restore.** It needs no new mechanism, but it is only as reliable as the model
  and its approvals, it takes a turn, and something other than the agent must verify it anyway.
  It would also present an instruction as if it were a capability.
- **The project's own Git repository** (stash, checkout, reset). Not every project has one, an agent
  can rewrite it, and it would change the person's history and branches.
- **A copy of the folder, or content-addressed copies of our own.** Git already stores content once,
  lists untracked files and writes files without following links; a copy would duplicate a large
  project at every turn.
- **Trusting git's own status for the check.** Git compares cached stat data and applies the
  folder's ignore rules; the check reads every byte itself and uses the recorded ignore list.

## Consequences

- A person can put a project back to before a task or any turn of it, from any client, whichever
  runtime did the work, and see that every file was checked.
- What the project ignored is outside undo, and the headset must say so: build outputs and caches
  come back only if a build makes them again. Files ignored by rules the checkpoint did not have
  are removed, unless they sit in a folder that was ignored as a whole.
- A restore also undoes changes anyone made by hand since the checkpoint; the host cannot tell who
  changed a file.
- A process an agent left running (a development server, a sleeping script) can still write while a
  restore runs; Halcyonic refuses a restore only while it knows of running work. The final check
  catches a file it changed in the folder, and each removal first checks that no folder above it has
  become a link, but such a process can still race the restore.
- A project folder that holds Halcyonic's data directory or the store is refused outright
  (`holds_host_data`). Commits the agent made stay in the project's own history; the journal says only
  whether the project's commit moved.
- The first checkpoint of a large project reads every file once; later ones store only what changed.
  Their size and time on real projects still need measuring.
- Revisit if a runtime gains a verified undo of its own, if projects need more than the bounds, or
  if two tasks must run in one folder at once (not verified).
