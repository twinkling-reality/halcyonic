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

## Amendment, 2026-11-03: the kernel keeps undo inside the project (U2)

The consequence above, that a process an agent left running "can still race the restore", was worse than a wrong
file inside the folder. The store checked a folder, then acted on its path, and the kernel resolves the path again
at the act. With a folder swapped for a link to a folder outside right after the last check (made certain in a test
by stepping in at that check), `removeInside` removed the outside folder, and a whole restore did the same. An agent
confined by ADR 0041 can leave such a process: its sandbox lets it make links in its own folder, and the store, which
runs as the person, followed them.

So on macOS the store's git and its removals now run inside Seatbelt profiles of Halcyonic's: git reads only the
system's own paths, git's own folders, the project's repository, the store's home for git and the project, and writes
only the repository and the project; a removal (`/bin/rm`) writes only under the project's real path, and a path still
there after it stops the restore, recorded incomplete. The kernel judges the path each read and write resolves to, so
a swap at any moment leads nowhere outside. Apple's `/usr/bin/git` is a shim that reads Xcode's own files and writes a
cache in the user's temporary folder through xcrun, so the store runs the binary the shim would (`xcrun --find git`);
any other configured git runs by its own real path. A git whose own folders hold the project is not run. The checks
stay, as the only defence elsewhere. Tested on this Mac with the system's git, the swap made certain for a removal and
for a whole restore, and each profile refusing a read and a write outside (`checkpoints/store.test.ts`). Inferred, not
shown: since git reads nothing outside under its profile, a swap during `git add` can no longer take a file from outside
into a checkpoint, which a later restore would have written into the project; a race inside git cannot be made certain
in a test.

## Amendment, 2026-11-11: a project's history, and a version against the folder now

The headset's History (the parity map's gap 2) needs what current state did not keep. Decided:

- **The projection keeps each project's history.** That is its versions, the checkpoints not kept, and its restores,
  complete or not, in journal order. A restore had been a journal fact only, so a headset that connected after it
  could not show it. A version records whose folder it holds, the project's or a task's own copy (ADR 0045), and
  the version before it of that same folder. The history is part of the projection's state, so a start restored
  from a checkpoint keeps it (ADR 0050, state format 5).
- **An after-turn checkpoint carries its turn.** `project.checkpoint.recorded` and `CheckpointView` gain an
  optional `turn_id`, the runtime's id of the turn the checkpoint follows. Older events and turns without an id
  have none.
- **`GET /api/projects/:id/history`** reads the history newest first, in pages of at most 50, with an entry id as
  its cursor. A version's `changed_files` is counted against its previous version by `git diff-tree --name-only`
  on the two trees. The count is kept by tree pair, since a tree's id names its contents.
- **The change reads compare a version with its folder as it is now** when `to` is left out. The folder is read
  into a tree in the store as a checkpoint's is, with the same checks of where it is. The read waits behind the
  project's checkpoints and restores, and records nothing.

Not decided here: the history of a task's own copy beyond marking its versions, and pruning the store (decided
below). Tested
against the system's git in temporary folders (`checkpoints/checkpointer.test.ts`) and on synthetic events
(`domain/history.test.ts`).

## Amendment, 2026-11-11: pruning the store

Until now the store kept every checkpoint forever, and a read of a folder as it is now added objects nothing
names. Decided: the host stops keeping a project's old checkpoints by a rule, and says so in the journal.

- **When.** After a checkpoint before a task, after a turn, or before an add, never during a restore: once the
  project keeps more than 220 checkpoints (`CHECKPOINTS_KEPT` 200 plus `CHECKPOINTS_PRUNE_SLACK` 20), so a prune
  runs once in every 20 checkpoints. It runs in the project's queue, so no checkpoint, restore or read of the
  folder now runs beside it.
- **What stays, always:**
  - the newest 200;
  - each folder's newest version, the project's and each task copy's;
  - every version of a task still at work (starting, running, verifying or waiting), where its undo starts;
  - the `before_restore` version of the newest restore, complete or not, which undoes it;
  - every `before_add` version, which undoes that add;
  - every version of a task's copy while the copy is still the task's own;
  - any checkpoint that an accepted restore, still waiting to run, names.
- **What goes:** every other checkpoint. Its two names go from the store in one `update-ref --stdin`. Then
  `git prune --expire=1.hour.ago` removes objects nothing names that were written over an hour ago. The hour
  keeps a tree another step has just written and not yet named, such as a task copy's or a read of a folder
  now. A task copy's own trees are named under `refs/halcyonic/copies` and never pruned.
- **Recorded.** `project.checkpoints.pruned` names the checkpoints (at most 1,000 an event) and how many are kept.
  It is recorded only after the store no longer names them. The projection drops them from the checkpoints, so
  a restore or a comparison of one is refused, and marks their history versions with `pruned_at`. A prune that
  fails is logged and runs after a later checkpoint; it never fails the checkpoint.
- **Not decided:**
  - a bound by bytes or by age;
  - the person's own way to keep a version (a pin);
  - pruning a project's store when the project is removed.

Tested on synthetic events (`domain/history.test.ts`) and against the system's git with small bounds
(`checkpoints/checkpointer.test.ts`).

