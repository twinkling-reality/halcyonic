# ADR 0045: Each task works in a copy of its own, and its work reaches the project only when the person adds it

- Status: Accepted 2026-11-10; contracts, views and admission built; the host's copies, adding and checks per task not yet
- Date: 2026-11-10
- Amends ADR 0020 (a project works in one folder) and ADR 0036 (checkpoints); complements ADR 0037 and ADR 0041

## Context

Several characters at work on one project is the point of the room, but every execution of a
project ran in its one folder (ADR 0020), so two agents at once wrote the same files and each one's
checks saw the other's unfinished work. Since 2026-11-10 one task at a time works in a folder
(FOLDER, [DOMAIN_MODEL.md](../architecture/DOMAIN_MODEL.md)): safe, but the second task waits.

Facts that decide the shape ([task-copies.md](../validation/task-copies.md), 2026-11-10):

- The projects beginners make are plain folders, not git repositories: a starter's folder is
  copied from its template with `node_modules` an absolute link to the shared dependencies (ADR
  0037). A space per task only for git projects would leave them out.
- Halcyonic already keeps a git store of its own per project for checkpoints (ADR 0036): the folder
  as the work tree, bytes exact, nothing written to the project's `.git`, on any folder.
- With the store's git (2.50.1), a three-way merge of trees (`merge-tree --write-tree
  --merge-base`) combines two sides that changed different places, deletions, new executable files
  and binary files included, and names the files when both changed the same places; applying the
  merged tree with `read-tree -m -u` on an index built from the folder's own snapshot leaves the
  folder exactly at the merged tree, and refuses when the person edited a file after the snapshot.
- An APFS clone (`cp -c -R`) copies a folder, links as they are, in about 2 s for 13,600 files.
- A git worktree writes into the person's repository (a branch, `.git/worktrees`), its `.git` is a
  file, which Codex's sandbox refuses index writes through ([codex-capabilities.md](../validation/codex-capabilities.md)),
  and it lacks the ignored files a build needs. A folder whose own `.git` is a file copies as a
  file still pointing at the person's repository.
- Halcyonic's data folder holds the journal and the access token, and is hidden from Codex and
  unreadable to OpenCode on purpose ([SECURITY.md](../architecture/SECURITY.md)).

## Decision

1. Every task of a project with a folder, git or not, works in its own copy of that folder when its
   runtime uses a folder. The copy is an APFS clone made just before the task's first start, ignored
   files included so dependencies and builds work, in
   `<HALCYONIC_TASKS_DIR, default ~/.halcyonic-tasks>/<project id>/<task id>/<the folder's own name>`:
   a folder of Halcyonic's own beside its data folder, never inside it and never inside the project.
   Every later start of the task, with any agent, works in the same copy.
2. The copy's base is the tree of the copy as it was made, kept in the project's checkpoint store
   (`workstream.copy_made`). Halcyonic writes nothing to the person's `.git`: no branch, no ref. The
   copy keeps whatever branch was checked out, and an agent's commits stay in the copy's own `.git`.
3. A task that cannot have a copy works in the project's folder under the one-task rule, and says
   why (`workstream.copy_refused`): its `.git` is a file, another repository sits inside it, it is
   larger than a checkpoint keeps, its volume cannot clone, it is missing, or it holds Halcyonic's
   data.
4. The project's folder changes only when the person adds a task's work (`workstream.add_to_project`,
   review required, admitted only while the task is at rest and nothing else writes the project's
   folder) or restores a checkpoint. Nothing adds by itself, even for the only task at work.
   The host snapshots the copy and the folder, merges them over the base, keeps a `before_add`
   checkpoint of the folder, applies the merge and reads the folder again; only then is
   `workstream.added` recorded, and the base becomes the task's tree. The changes arrive as plain
   edits in the folder. Files the project ignores stay in the copy.
5. When both changed the same places the folder is not touched: `workstream.add_conflicted` names
   the files and how they met (`text`, `binary`, `deleted`, `other`). On the person's
   `workstream.fit_to_project` the host writes the merged files, both sides marked, into the copy,
   the base becomes the project's tree (`workstream.fitted`), and the task's agent is asked to fit
   its work; a file that cannot be marked gets the project's version and is named.
6. The task's view carries its copy: its folder, base, what differs from the base as last read
   (`workstream.copy_changes_read`), its latest add and any conflict, as states, counts and paths;
   the headset's words are its own. Absent on every task from before, so journals from before,
   the demonstration and the recorded run play as they did.
7. Checkpoints follow the folder they were taken of: a task's turns are kept from its copy and a
   restore puts a checkpoint back where it was taken, so undoing a turn restores the copy and
   undoing an add restores the project. The computer's check, previews and Play run per task, on
   its copy, so the person can try a task's change before adding it. Make VR Version reads the
   project's own folder, without work not yet added.

## Alternatives considered

- **A git worktree per task**: writes branches and worktree records into the person's repository,
  needs a repository, so beginners' projects get nothing, lacks ignored files, and Codex refuses
  index writes in one. Lost on all four.
- **Copies inside the data folder**: would open part of the folder that holds the journal and the
  token to every agent's sandbox. Lost on confinement.
- **The first task in the folder, only later ones in copies**: two kinds of task, one needing an
  add and one not, which a beginner cannot tell apart. Lost on clarity.
- **Adding automatically when a task ends**: the person loses the moment to try the change and
  decide; a conflict would arrive unasked. Lost on the person's control.

## Consequences

- Two tasks of one project run at once, each agent seeing and changing only its copy; the person
  tries each and adds it in one step; a conflict is told plainly and the agent can fit its work.
- New: commands `workstream.add_to_project` and `workstream.fit_to_project`, six `workstream.*`
  events, the `before_add` checkpoint reason, `WorkstreamView.copy`. The welcome's command policies
  list the two commands, so the committed demonstration's welcome changes by those two entries.
- The directory policy, OpenCode's sandbox and Codex's confinement must admit the copies' folder,
  and every lookup of a project by its folder must also know a task's copy.
- Disk: a clone shares blocks until changed, but a project with very many files takes longer to
  copy per new task. Copies are kept while their task exists; removing them is a later decision.
- Revisit if a runtime asks anything new for a folder it has not seen, or if Salidium and Seorak
  cannot follow an execution whose folder is a copy.
