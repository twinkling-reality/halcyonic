# ADR 0020: A project works in one folder the host approves, and clients never name a path

- Status: Proposed
- Date: 2026-09-30

## Context

Real runtimes (Claude Code, OpenCode, Codex) run agents in a directory on the Mac. Until now each
start named it in a runtime-specific option (`cwd` for Claude Code and Codex, `directory` for
OpenCode), an absolute path the client typed, which each adapter checked through the host's
`DirectoryPolicy`: only real paths under `HALCYONIC_PROJECT_ROOTS`, so `..` and symbolic links
cannot escape. That kept agents inside the roots, but left three problems:

- The headset cannot type or know a path on the Mac. Its new work panel sends no runtime options,
  so a start on Codex or OpenCode from it is refused after the workstream is created
  ([XR_CLIENT.md](../architecture/XR_CLIENT.md)).
- Where an execution ran was recorded only inside opaque, vendor-specific options, not as a fact
  the projection or an auditor can read.
- Nothing tied a project to its files: two starts in one project could name different folders.

Verified on 2026-09-30 through the control plane on this Mac
([project-location.md](../validation/project-location.md)): OpenCode 2.0.18 and Codex 0.157.0,
each on `qwen3.6:35b-a3b-nvfp4` served by Ollama, started and finished a turn in a new, empty
folder that is not a git repository, which the host had just made. Codex needed neither a git
repository nor a trust entry through its app-server with the adapter's explicit sandbox and
approval policy. Claude Code was not run (it spends Anthropic credit); its adapter is covered by
unit tests only.

## Decision

- **A project has at most one location**, a folder on the host. `ProjectView.location` is
  `{path, name, created}` or null: `path` the real path the file system gave the host
  (`realpath(3)`, one spelling per folder whatever case or Unicode form was asked) when it bound
  the project, `name` the folder's own name for display, `created` whether the host made it. It is set by
  `project.create` (`location`, nullable) and changed by a new command, `project.set_location`
  (low consequence), and journaled as `project.created`'s `location` and a new event,
  `project.location_set`.
- **A client chooses from what the host lists, never a path.** `GET /api/locations` lists each
  project root (as its real path) and the folders directly inside it, leaving out hidden folders
  and symbolic links, at most 200 per root, read from the file system on request and never
  journaled. A choice names a root the host listed and a folder in it:
  `{kind: 'existing_folder', root, folder_name}` (null for the root itself) or
  `{kind: 'new_folder', root, folder_name}`. The host composes the path, refuses a symbolic link
  or anything but a folder directly inside one of its roots, and applies `DirectoryPolicy`. A new
  folder's name is one plain segment (`^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`); an existing one's is
  any single visible segment. The host makes a new folder with a non-recursive `mkdir`, which
  fails rather than follow or reuse anything already there.
- **Every real execution runs in its project's folder, and nowhere else.**
  `RuntimeDescriptor.uses_project_location` declares whether a runtime works in it (true for
  Claude Code, OpenCode and Codex, false for the mock runtime). Their `cwd` and `directory` start
  options are removed. Admission refuses a start for such a runtime in a project without a folder
  (`location_required`) or whose folder the policy now refuses (`location_missing`,
  `location_not_allowed`); a path that now resolves elsewhere, because a symbolic link replaced
  part of it, counts as missing. The control plane records the folder it gives the runtime as
  `execution.created`'s `directory`, and passes it in `StartExecutionRequest.directory`; each
  adapter asks the policy again (`confirmProjectLocation`) before it launches anything, so a
  refusal at dispatch is a failure with effect `none`.
- **Refusals say what to do.** New rejection codes: `location_required`, `location_missing`,
  `location_not_allowed`, `location_exists`. A folder the file system will not make fails the
  command with `location_not_created`, effect `none`, and no project; a folder made and then found
  unusable, for example because its root changed at that moment, is left in place and reported
  with effect `unknown`. A root replaced after the control plane started, by a symbolic link or by
  another folder at the same path, is refused until it restarts, so nothing is made through it.
- **Stored events are migrated without a new version.** Journal migration 4 gives stored
  `project.created` events and `project.create` commands `location: null`, and stored
  `execution.created` events `directory: null`, which there means "not recorded", not "none".

## Alternatives considered

- **Keep a path per start.** The headset cannot supply one, every start could name a different
  folder, and where work ran stays buried in vendor options.
- **A path in the command, checked by the policy.** Safe as far as the policy goes, but a client
  would compose paths on a machine it cannot see, and every client would need to know the Mac's
  file layout. Naming a listed root and one folder keeps composition on the host.
- **Any folder below a root, not only directly inside one.** Deeper folders are reachable by
  making the deeper folder a root. Listing and choosing one level keeps the choice small enough for
  a person in a headset; the root itself is choosable, so a root can be a single repository
  without exposing its siblings.
- **Discover repositories by scanning.** Reading every repository's contents or git state to
  suggest projects is more than choosing a folder needs, and slow on large roots. Git status could
  be added to the listing later if people need it.
- **One folder per workstream.** It would make parallel workstreams independent, but needs git
  worktrees or copies, their creation and cleanup, and merging back; a design of its own
  (below).
- **Create the project first, then bind the folder.** Two commands leave a project without a folder
  when the second fails; `project.create` with its location either makes both or neither.

## Consequences

- The headset can create a project in a new folder, or connect an existing one, and start real
  work in it by choosing from a list; the headset's Create flow renders the list and the refusals.
- Where each execution ran is in the journal, and a moved or removed folder refuses the next start
  in words until the project is bound again. Executions already running keep their folder.
- Paths and folder names reach every authenticated client, paired devices included: in the
  listing, in `ProjectView.location` and `ExecutionView.directory`. A device could already start
  agents that read everything under the roots, so the names add no reach; they are untrusted text
  for display. A device can now also make empty folders directly inside a root.
- **A crash between making a new folder and journaling its project** leaves the folder without a
  project. The command is then failed with an unknown effect when the control plane restarts, and a
  retry of the same choice is refused with `location_exists`, whose message says to choose it as an
  existing folder, which binds it (tested). Nothing deletes the folder.
- **Parallel workstreams in one project share its folder.** Two executions, of one workstream or of
  two, can run at once in the same tree: agents may overwrite each other's edits, and one agent's
  checks can see the other's half-finished work. Nothing prevents it today. The options are one
  active execution per folder (admission refuses a second start while one runs), a git worktree per
  workstream made by the host, or leaving it to the person; this stays an open question
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)). Several projects bound to one folder share
  it the same way.
- The contracts, generated schema and C# bindings, recorded traces and the demonstration change
  together; the journal gains migration 4. Scripts that passed `cwd` or `directory` must bind the
  project instead.
- Revisit when parallel workstreams need isolation, when a person needs folders deeper than one
  level or on another machine, or when packaging the host (consumer setup) chooses its own roots.
