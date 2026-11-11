# ADR 0045: Each task works in a copy of its own, and its work reaches the project only when the person adds it

- Status: Accepted 2026-11-10; contracts, views, admission, the copies made at a task's first start, adding, its conflicts and the fit built, run with Codex and OpenCode at once on 2026-11-10; on by default since the headset offers Add to Project (`HALCYONIC_TASK_COPIES=0` turns it off); a check of a task's own copy and the automatic check after a turn in it, which a failed check's fix follows, built
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
   runtime is held to the folder it is given on this computer, so that in a copy it writes only that
   copy. Runtime by runtime, as built on 2026-11-11:
   - **Codex** is held where its confinement covers the folder: every execution's folder on macOS
     since BOX-2 (ADR 0041), and so its copy; elsewhere it is not confined, and gets no copy.
   - **OpenCode** is held on macOS since BOX-3: every execution's folder has a sandboxed server of
     its own that writes that folder alone, so in a copy it writes only that copy. Elsewhere it has
     no sandbox, and gets no copy.
   - **Claude Code** is not: it runs with no sandbox. It gets no copy.
   A runtime not held works in the project's folder, one task at a time (FOLDER), its task refused
   a copy in words (`copy_refused` `runtime_unconfined`), and a task with a copy is never started
   with one. VRIFY's sandbox for every agent (ADR 0048) is what will hold Claude Code. So the claim
   "each agent sees and changes only its copy" holds for the writes of a sandboxed Codex or OpenCode
   agent; each still reads what its sandbox lets it, and a Claude Code agent, working in the
   project's folder, can read and write anywhere, other tasks' copies included, which is why an add
   brings only what the person was shown (4). The copy is an APFS clone made just before the task's first start, ignored
   files included so dependencies and builds work, in
   `<HALCYONIC_TASKS_DIR, default ~/.halcyonic-tasks>/<project id>/<task id>/<the folder's own name>`:
   a folder of Halcyonic's own beside its data folder, never inside it and never inside the project.
   Every later start of the task, with any agent held to it, works in the same copy.
2. The copy's base is the tree of the copy as it was made, kept in the project's checkpoint store
   (`workstream.copy_made`). Halcyonic writes nothing to the person's `.git`: no branch, no ref. The
   copy keeps whatever branch was checked out, and an agent's commits stay in the copy's own `.git`.
   The copy's own `.git/config` keeps no token of the person's: every address of a key ending in
   `.url`, `.pushurl` or `.proxy` (remotes, submodules, LFS, a proxy) loses its user part
   (`scheme://user[:password]@host`); every `http.extraheader` and `http.<url>.extraheader`, every
   `credential` and `credential.<url>` key, every `include` and `includeIf` key, and every
   `url.<base>` rewrite whose base or value carries a user part are removed; `user@host:path` names
   an ssh login and stays. What a copy can still hold that carries one, as the project's folder does:
   its working files, ignored ones included (a `.env`, a key file), the scripts in `.git/hooks`, any
   other file under `.git` a tool of the person's wrote, a key of the person's own tool that is not
   an address (a token stored under another name), and a program `core.sshCommand` or
   `core.askPass` names.
3. A task that cannot have a copy works in the project's folder under the one-task rule, and says
   why (`workstream.copy_refused`): its `.git` is a file, another repository sits inside it, it is
   larger than a checkpoint keeps, its volume cannot clone, it is missing, or it holds Halcyonic's
   data, or its runtime is not held to the folder (1).
   - **A world's tasks get no copy** (amended 2026-11-11, AUDIT2's finding on copies by default; the
     lead's choice). A world is the one starter whose tasks change the project in place: the person
     speaks to it, and the world the headset shows follows each turn (ADR 0049), which reads the
     project's own folder. In a copy, no turn would change the world shown, and only an add would.
     So a world project's tasks work in its folder, one at a time, with nothing recorded as refused:
     their view has no copy, and the headset offers no Add to Project for them.
4. The project's folder changes only when the person adds a task's work (`workstream.add_to_project`,
   review required, admitted only while the task is at rest and nothing else writes the project's
   folder) or restores a checkpoint. Nothing adds by itself, even for the only task at work.
   The add brings only what the person was shown: the copy's tree must equal the tree of its latest
   reading (`workstream.copy_changes_read`, whose list the headset shows); a copy changed since, by
   its agent or anything else, is read again and shown, and the add fails `changed_since_review`
   with nothing written. The host snapshots the copy and the folder, merges them over the base, keeps a `before_add`
   checkpoint of the folder, applies the merge and reads the folder again; only then is
   `workstream.added` recorded, and the base becomes the task's tree. The changes arrive as plain
   edits in the folder. Files the project ignores stay in the copy.
5. When both changed the same places the folder is not touched: `workstream.add_conflicted` names
   the files and how they met (`text`, `binary`, `deleted`, `other`). On the person's
   `workstream.fit_to_project` the host writes the merged files, both sides marked, into the copy,
   the base becomes the project's tree (`workstream.fitted`), and the task's agent is asked to fit
   its work; a file that cannot be marked gets the project's version and is named.
6. The task's view carries its copy: its folder, base, what differs from the base as last read
   (`workstream.copy_changes_read`; `to_add`, and `to_add_files` with how each file changed: added,
   changed or removed), its latest add and any conflict, as states, counts and paths;
   the headset's words are its own. Absent on every task from before, so journals from before,
   the demonstration and the recorded run play as they did.
7. Checkpoints follow the folder they were taken of: a task's turns are kept from its copy and a
   restore puts a checkpoint back where it was taken, so undoing a turn restores the copy and
   undoing an add restores the project. The computer's check, previews and Play run per task, on
   its copy, so the person can try a task's change before adding it. The automatic check after a
   turn checks the folder the turn changed, the task's copy or the project's, one check at a time
   per project, and a failed check goes back only to the agents that worked in the folder checked
   (ADR 0044). Make VR Version reads the
   project's own folder, without work not yet added.
8. **A second line of work from an older version** (amended 2026-11-11, the lead's yes to TASKS's
   ASK). `workstream.create` takes an optional `from_checkpoint`, one of the project's own versions
   (its checkpoints). It is recorded on `workstream.created` and shown on the task's view.
   - At the task's first start its copy is made as any other, a clone of the project's folder with
     its `.git` and ignored files, and then made into that version's tracked files
     (`store.apply`). The version's tree is the copy's base.
   - The agent's first instruction says, apart from the person's words, which version its folder
     holds. The project's own folder is not touched.
   - The add is unchanged: its merge base is the version's tree, so it brings only what the task
     changed since that version, beside what the project gained since, and a file both changed
     meets as a conflict.
   - A version of another task's own copy (History lists them) is refused while that task's work
     has not been added to the project since: a task started on it would carry work the project
     never took, which its own add would then not bring either (AUDIT3 S2). Once that work is
     added, the version is one of the project's like any other.
   - The line telling the agent which version it works on goes with the task's first run that has
     a turn, so a start that failed first does not lose it (AUDIT3 S3).
   - Refused, in words: another project's version or a missing one (`checkpoint_not_found`); a version
     of a world, which changes in place with no copy to make into it (`invalid_state`);
     creating one with copies or undo off, or starting it where no copy can be made, as for an
     agent this computer cannot keep to one folder (`capability_unsupported`). It never falls back
     to the project's folder, which holds another version.
   - A new project from a version is not part of it.

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
- The runtimes' directory policy must admit the copies' folder (each sandbox then holds its run to
  the copy it is given), and every lookup of a project by its folder must also know a task's copy.
- Disk: a clone shares blocks until changed, but a project with very many files takes longer to
  copy per new task. A copy is kept while its task exists (Halcyonic has no command that removes a
  task). At every start, before anything can start a task, the host removes each copy no task holds:
  one a start left when it stopped before recording it, or one a journal that no longer names it left
  behind. Only a folder `<project id>/<task id>` of real folders named by ids is ever removed, never
  one that holds a recorded copy, and the removal runs confined to the copies' folder; anything else
  there is left and counted.
- Revisit if a runtime asks anything new for a folder it has not seen, or if Salidium and Seorak
  cannot follow an execution whose folder is a copy.
