# Domain model

The vocabulary of Halcyonic and the rules over it. Types live in `packages/contracts`; rules live
in `packages/domain`.

## Concepts

- **Project**: a software project or product context. Created by `project.create`. It may have a
  **location**: one folder on the host, inside a project root the host allows, where every real
  execution of its work runs ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)).
  A client chooses it from the folders the host lists, or names a new folder the host makes; it
  never sends a path. `project.set_location` binds the project to another folder, for example after
  the folder was moved; executions already started keep the folder they started in.
  A new project may begin from a **starter** the host vets and keeps, such as a VR app for the
  headset's browser: its folder starts with the starter's files and a link to the starter's shared,
  read-only dependencies, and the project's view names the starter ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)).
  A project may be **linked** to one repository of the person's catalog, the list of every project
  they have across their accounts that Omnorium keeps
  ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)): GitHub's numeric repository id, its stable identity, beside its `owner/name` as the client sent it, taken from the catalog's answer; checked against the catalog this computer last read, never against GitHub. Only the person links (`project.link_catalog`,
  again to replace the link) and unlinks (`project.unlink_catalog`); the catalog itself is read
  through and never part of the model. The link is kept out of the project's view: clients are to
  see it through the catalog read. The two link events still reach the person's own clients in the realtime
  stream, `owner/name` included, as every event but a device's does.
- **Checkpoint**: every file of a project's folder as it was at one moment, kept by the host for
  Halcyonic's own undo when the host has set it up: before a task starts, after each turn the
  runtime reports ended, and before a restore. `project.restore` puts one back, and completes only
  once the host has read every file again and found the folder equal to it
  ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)).
  No runtime is asked to undo anything.
- **Workstream**: a meaningful unit of work, such as "Fix flaky checkout tests". It is the aggregate
  a character represents, and it keeps its identity across executions and runtimes.
- **Execution**: one attempt by one runtime toward a Workstream. It corresponds to the runtime's
  own session or thread (an OpenCode session, a Claude Code session, a Codex thread), whose id is
  kept as `native_id`. A workstream may have several executions, including concurrent ones.
- **Turn**: one burst of work inside an execution, ending as completed, failed or interrupted.
  All three verified runtimes have this concept. Turns are not stored as entities; they drive the
  execution's status.
- **Runtime**: a configured adapter instance (`runtime_id`) of some kind (`mock`, later `opencode`
  and others) with declared capabilities. A **synthetic** runtime fabricates activity for
  development, and every client must label its work as such. A runtime whose descriptor declares
  `uses_project_location` (Claude Code, OpenCode, Codex) runs its agents in the project's folder and
  nowhere else; the mock runtime uses none. An execution's `directory` is the folder its runtime was
  given, null for a runtime that uses none, and null, meaning not recorded, for executions
  journaled before folders were.
- **Command**: a request to change state. It is admitted or rejected, and if admitted it later
  completes or fails. Sending a command is never proof that it happened.
- **Event**: an immutable fact in the journal. See [EVENTS.md](EVENTS.md).
- **Question**: something an agent asks the person through its runtime's structured surface, one
  or more prompts with options and perhaps typed text, answered with `execution.answer_question`
  when the runtime has `answer_question` and the adapter can carry the answer back faithfully.
  A prompt asking for a secret is never answerable through Halcyonic. An execution shows at most
  three pending questions, those Halcyonic can answer first, then the others, each oldest first;
  only those shown can be answered or are named in its attention. An adapter reports a question
  longer than 16,000 characters in all shortened and unanswerable
  ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)).
- **Principal**: who sent a command, as the control plane authenticated it: `local`, whoever holds
  the access token on the control plane's machine, or a paired **device**, such as a headset on
  the same network. Devices belong to no project; the owner pairs them with a code and revokes
  them ([SECURITY.md](SECURITY.md)).

A **model provider** (Anthropic, OpenAI, Ollama and so on) sits below the runtime and is not a
Halcyonic concept; runtimes such as OpenCode already manage providers. A **model** is known only
through the runtime: a runtime whose `model_choice` is `listed` lists the models it can run, a
start on such a runtime must carry one of them by its opaque `model_ref`, and an execution's
`model_ref` is the model
the runtime last reported running on, null until it reports one
([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)).

## Execution status

Status is derived from facts, never stored. Precedence, highest first:

| Status | Derived when |
| --- | --- |
| `unknown` | Contact with the runtime was lost, or the control plane restarted, or a start may or may not have happened. Cleared by the next runtime observation, such as `runtime.connection.restored`. |
| `failed` | The runtime refused to start the execution. |
| `waiting_for_human` | At least one approval or question is pending. |
| `verifying` | A turn is running and a test run it reported is in progress. |
| `running` | A turn is running. |
| `completed` / `failed` / `interrupted` | No turn is running; this is how the last turn ended. |
| `starting` | Accepted, but the runtime has not started a turn. |

`completed` means the runtime says the turn ended normally. It says nothing about whether the work
is correct; that is what verification and evaluation are for. `interrupted` replaces the
specification's `cancelled`, because every verified runtime interrupts a turn rather than
terminating the whole session.

Candidates from the specification that are deliberately absent, because nothing produces them:
`queued` (no scheduler exists), `paused` (no verified runtime can pause a turn), `reviewing` (no
reviewer concept yet). A status is added together with the fact that produces it.

A workstream's status is `created` until it has an execution, then the status of its most recent
execution.

## Attention

Whether a workstream needs its human, and why. Every signal names the facts behind it.

| Level | Reasons |
| --- | --- |
| `action_required` | `approval_pending`: an execution is waiting for an approval; `question_pending`: an agent is waiting for an answer, whether or not it can be answered through Halcyonic. |
| `notice` | `execution_failed` or `execution_state_unknown` for the current execution; `verification_failed`: the current execution completed while its last test run did not pass. |
| `none` | Nothing needs the human. |

Completion alone is not an attention signal; status already conveys it. The server sends status
and attention only; clients decide presentation (animation, material, sound, placement).

## Runtime kinds

| Kind | Runtime | Synthetic |
| --- | --- | --- |
| `mock` | Scripted scenarios for development and for the recorded demonstration, whose runtimes are named for it | yes |
| `claude-agent` | Claude Code through the Claude Agent SDK, displayed as "Claude Agent" | no |
| `opencode` | OpenCode's v2 server API, pinned version | no |
| `codex` | Codex's app-server, stable surface, pinned version | no |

A kind names the adapter type; the runtime id names a configured instance of it. Salidium and
Seorak observe `claude-agent` sessions (as their provider `claude-code`) and `codex` threads (as
`codex`, by thread id); they do not observe `mock` or `opencode`.

## Commands

| Command | Policy | Admitted when |
| --- | --- | --- |
| `project.create` | low consequence | always, and when it names a location, the host can bind it (below) |
| `project.set_location` | low consequence | the project exists and the host can bind the location |
| `project.make_vr_version` | low consequence | the original exists and has a folder (`location_required` otherwise), the iwsdk-vr starter is installed (`capability_unsupported` otherwise), and the host can make the new folder; a new project "<name> VR" from the starter, with `project.made_from` naming the original and the instruction the host wrote; it starts no work (M1.8c-1) |
| `project.link_catalog` | low consequence | the project exists, a catalog of the person's projects is set up on the host (`capability_unsupported` otherwise), and the catalog it last read holds the repository by its id under the same `owner/name` and is the person's own, not a sample (`invalid_state` otherwise) |
| `project.unlink_catalog` | low consequence | the project exists and is linked (`invalid_state` otherwise) |
| `project.restore` | review required | undo is set up on the host (`capability_unsupported` otherwise), the checkpoint is the project's (`checkpoint_not_found` otherwise), no task in the project is `starting`, `running`, `verifying` or `waiting_for_human` or `unknown` other than after a restart, and no command in the project waits for its outcome (`invalid_state` otherwise) |
| `project.check` | low consequence | checks are set up on the host (`capability_unsupported` otherwise), the project was made from a starter and has its folder, and no task in it is running (`invalid_state` otherwise). With `workstream_id`, a check of that task's own copy ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)): the task must have one (`invalid_state` otherwise; one without works in the project's folder) and be at rest with nothing of it pending, and no check, restore or preview start of the project may await its outcome; other tasks at work in their own copies do not hold it. A check records the folder it checked, and only work in that folder, or one inside it or around it, makes it unknown or changed; an add makes a check of the project's folder changed, and a fit a check of that copy |
| `project.preview.start` | low consequence | the project's latest check is still current (`files === 'current'`), passed, and no preview of the project runs (`invalid_state` otherwise) |
| `project.preview.stop` | low consequence | a preview of the project runs (`invalid_state` otherwise) |
| `project.open_on_mac` | low consequence | a browser to play in is set up on the host (`capability_unsupported` otherwise), the project's latest check is still current (`files === 'current'`), passed, saw the game work with a mouse and keyboard and found every agent run confined, the running preview serves that check and is still current, and no play of the project runs (`invalid_state` otherwise); the game plays in the host's contained browser, never the person's Chrome (P-A, T2) |
| `project.show_in_finder` | low consequence | the project exists and has a folder (`location_required`). The host shows its own folder in Finder on the Mac, selected; never a task's copy, never Halcyonic's data. Completes once Finder was asked (keeping what was made, F3) |
| `project.save_copy` | review required | the project exists and has a folder (`location_required`), no restore of it or add to it awaits its outcome, and no task in it is at work or may be (`invalid_state`, as for a restore: a copy taken mid-write would hold half of it). The host writes a zip to the account's Downloads folder, `<the folder's own name> <date> <time>.zip`, never over a file that is there, and shows it in Finder; result `project_saved` with the file's name, its files and bytes. The zip is the checkpoint store's tree of the folder, so never `.git` nor what the project ignores, and the store refuses a folder holding Halcyonic's data; it also leaves out, wherever they sit and whatever the case of their names, every file the one secret-file list names (`secretShapedPath` in runtime-core, the list a sandbox keeps agents from: `.env*`, private keys and key stores, SSH keys, package and network credentials, files named `credentials` or `secrets`), and every other project's folder bound inside this one. The tree is filtered whole before git's archive reads it (F3) |
| `project.share.start` | review required | carries `way`, `link` or `wifi`. Sharing is set up on the host (`unavailable` otherwise: no previews on this Mac), no share of the project is live by either way, its latest check passed, is still current and found every agent run confined (`reads_confined`), and the running preview serves that check and is still current (`invalid_state` otherwise, in the product's words). By `link` the host gives the preview a temporary public `https://` address, through the pinned tunnel program (with none set up it fails `unavailable`); by `wifi` it listens on the Mac's private IPv4 address on its local network, at a port of 47970 to 47979, and passes only `GET` and `HEAD` to the preview, under the preview's own Host and with no cookie or body (off any network it fails `no_network`, "This Mac isn't on a Wi-Fi network."). It completes, `share_started` with the address and the way, once the address answered 200, a link's over verified TLS; with no answer in 90 s it fails `share_failed`, effect none, and records nothing ([ADR 0054](../decisions/0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md)) |
| `project.share.stop` | low consequence | a share of the project is live (`invalid_state` otherwise). The address stops answering at once |
| `project.send_to_github` | review required | carries `title` and `review`, the digest of `POST /api/projects/:id/github-review` (ADR 0051). The project exists and has a folder (`location_required`), and no restore of it or add to it awaits its outcome and no task in it is at work (`invalid_state`, as for Save a Copy). On the host, with the person's own git and gh only [ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md): the folder must be a git repository of its own, with an origin on GitHub, on a branch with a commit, and differ from that commit; the review is checked again first (`review_stale`, nothing sent). One commit of the folder's files, without secret-shaped files or other projects, on top of the branch, pushed as a new branch, never forced; then a pull request where `gh` is set up. Result `sent_to_github` with the new branch, the commit and the pull request's address or null; refusals `not_a_repository`, `no_github_origin`, `detached_head`, `no_commit`, `nothing_to_send`, `no_author`, `uses_filters` (a `.gitattributes` names a filter, as Git LFS's do), `unreadable_excludes` (a rule keeping files out that Halcyonic cannot read), `already_sent`, `push_refused`, effect none. What is sent is the folder as the person's git sees it: the scratch index starts from the branch's last commit, the folder's `.git/info/exclude` and the person's excludes file apply, secret-shaped paths and other projects stay as that commit has them, and none of the person's git settings apply while files are staged |
| `project.publish` | review required | carries `review`, the digest of `POST /api/projects/:id/publish-review`. The project has a folder (`location_required`), and its latest check passed, is still current and found every agent run confined (`invalid_state` otherwise, in words). On the host [ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md): the folder's origin is on GitHub, `gh` is set up and signed in, and the check's built copy is on this Mac (`no_github_origin`, `no_gh`, `gh_signed_out`, `no_built_copy`). That built copy alone, never the source, as one commit on `halcyonic-pages` on top of the last publish, never forced (`push_refused`), then Pages turned on; result `published` with the commit and the address, null where GitHub did not say it |
| `project.unpublish` | review required | the project's game is published (`invalid_state` otherwise). The host deletes the `halcyonic-pages` branch with the person's own git |
| `workstream.create` | low consequence | the project exists. With `from_checkpoint`, the task starts from that version of the project ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)): it must be one of this project's own (`checkpoint_not_found` otherwise), and the host must keep task copies and undo (`capability_unsupported` otherwise). At its first start its copy is the project's folder cloned and then made into that version, which becomes its base; an agent this computer cannot keep to one folder is refused at that start (`capability_unsupported`), never sent to the project's folder |
| `workstream.add_to_project` | review required | carries `review`, optional: the digest of the copy's latest reading (`copy.to_add_review`), which a paired device must send (`review_required`) and which, from anyone, must still be the latest (`review_stale`), nothing changed ([ADR 0051](../decisions/0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md)). The task has a copy of its own (`invalid_state` otherwise: it has none yet, or it works in the project's folder), none of its executions is active and no command for it awaits its outcome, no restore of the project is in progress, the project has a folder (`location_required`), no add, restore, check, folder change or VR version of the project awaits its outcome, and no other task works in the project's folder (FOLDER) ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)). On the host it adds only what the person was shown: a copy whose tree is not its latest reading's is read again and the add fails `changed_since_review`, nothing written. A start of a task with a copy of its own is refused on the host where copies are off, or where its runtime is not held to that folder |
| `workstream.fit_to_project` | low consequence | the task has a copy of its own whose latest add met the project (`invalid_state` otherwise), none of its executions is active, no command for it awaits its outcome, no restore of the project is in progress, the project has a folder, and no add, restore, check, folder change or VR version of the project awaits its outcome, since a fit reads the project's folder whole |
| `execution.start` | low consequence | no restore of the project is in progress, the workstream exists, none of its executions is `starting`, `running`, `verifying` or `waiting_for_human` and no `execution.*` command for it awaits its outcome (`invalid_state` otherwise: a task runs one execution at a time, so two agents never work in its folder; stop it first, or start another task; an `unknown` one does not hold it, since refusing would only send the person to a new task in the same folder, while the project's busy rule, stricter, still counts it for restores and checks), the runtime is registered with `start_execution`, a `model_ref` is given exactly when the runtime's `model_choice` is `listed` (`model_required` when it is missing), for a runtime that `uses_project_location` the project has a folder the host's policy still allows at the same real path, no add or fit for the task awaits its outcome, and no execution of another task, of any project, whose folder is the task's own (its copy when it has one, ADR 0045, else the project's) or one inside it or around it is `starting`, `running`, `verifying` or `waiting_for_human`, and no start or instruction for another task in such a folder awaits its outcome, where a pending start counts only for a task the host refused a copy, since a task that has not been refused one is being given its own and the host asks this rule again (`folderInUse`) before a start whose copy was refused works in the project's folder, failing it with `folder_in_use` otherwise (`invalid_state` otherwise, "Another task is running in this folder": one task at a time in a folder, FOLDER; an execution `unknown` for any reason but a restart holds it too, "may still be running", as the project's busy rule counts it, since a start that timed out or a lost connection may leave its agent at work there, while one cut short by a restart does not, since no runtime session survives one), and the adapter accepts the options and the model |; with `plan`, the runtime offers a plan (`capability_unsupported` otherwise) and the level is not `never_ask` (`invalid_runtime_options` otherwise, PLAN)
| `execution.send_instruction` | low consequence | no restore of the project is in progress, the runtime has started a session, and: at rest (`completed`, `failed`, `interrupted`) with `instruct_at_rest`, no other execution of its workstream `starting`, `running`, `verifying` or `waiting_for_human` and no `execution.*` command for another of them or a start awaiting its outcome (`invalid_state` otherwise: an instruction sets the execution going, and two agents would work in one folder, ADMIT-2), and, where its execution was given a folder, the folder rule of `execution.start` for another task's work there (FOLDER), or running (`running`, `verifying`, `waiting_for_human`) with `instruct_while_running` |; with `leave_plan`, also the runtime offers a plan (`capability_unsupported` otherwise) and the execution is a plan now, never one or already left (`invalid_state` otherwise, PLAN)
| `execution.respond_to_approval` | review required | the approval is pending, the execution is `waiting_for_human`, and the runtime has `respond_to_approval` |
| `execution.answer_question` | low consequence | the question is pending (`question_not_found` otherwise), the execution is `waiting_for_human`, the runtime has `answer_question`, the question is answerable and asks for no secret (`capability_unsupported` otherwise), and the answers fit its prompts (`invalid_answer` otherwise) |
| `execution.interrupt` | review required | a turn is running and the runtime has `interrupt` |

A location is refused with `location_not_allowed` when it is not one of the host's project roots
or a folder directly inside one (a symbolic link, a hidden name, `..`, or no roots at all),
`location_missing` when the folder or its root is not there or is not a folder, and
`location_exists` when a new folder's name is taken. A start in a project without a folder is
refused with `location_required`; one whose folder has gone, or whose path now leads elsewhere
through a symbolic link, with `location_missing`. A new folder the file system will not make fails
the command with `location_not_created` and effect `none`, and no project exists; a folder made but
then found unusable fails it with effect `unknown`, and the message says where the folder is. The runtime asks
the host's policy again before it starts anything, so a folder removed after admission fails the
start with effect `none` and the execution reads `failed`.

Policy categories are recorded with every accepted command and sent to realtime clients in
`welcome`. Clients must require a deliberate, explicit action for `review_required` commands,
and send an answer to a question only when the person presses the control that sends it. Authorization is currently a single local
principal, so categories do not yet restrict who may act ([SECURITY.md](SECURITY.md)).

## Command lifecycle

```text
submitted ──> rejected                  (journaled with a reason; nothing else happens)
          └─> accepted ──> completed    (runtime confirmed, or local action done)
                       └─> failed       (effect: none | unknown)
```

- Command ids come from the client. Resubmitting the same command returns its current state;
  reusing an id for a different command is a conflict and is not journaled. A refused answer is
  journaled with its keys only, so the control plane tells it apart from another answer by a
  keyed digest it holds in memory, with a key drawn per process. It compares one only for the
  principal that sent the answer, at most six times a minute; any other principal, and the sender
  past that, gets a conflict whatever it sends, so resending guesses cannot recover a short answer.
  After a restart, sending it again is a conflict.
- A runtime action that is not confirmed within the timeout fails with `code: timeout` and
  `effect: unknown`, because the runtime may still act.
- A start that fails with an unknown effect makes the execution `unknown`, not `failed`.
- A restore is done by the host, not a runtime, so no timeout applies; it completes only after its
  own check, and fails with `effect: unknown` once a file was written.
- Effects are observed separately: an approved request completes as a command, and the runtime's
  `runtime.approval.resolved` shows that the runtime applied it. An answer likewise counts once
  `runtime.question.resolved` says the runtime took it. When the turn ends first, as Codex 0.157.0
  ends an interrupted turn before it settles the request, the turn's end withdraws the question,
  no resolution is reported, and an answer the runtime had not yet confirmed fails with an unknown
  effect.

## Invariants

- An event naming an execution also names its workstream and project.
- A runtime event with a source sequence not after the last applied one does not change state; the
  projection reports it.
- Ending a turn clears everything in flight: pending approvals and questions, active tools and
  the active test run. There is no command to dismiss a question; stopping the turn withdraws it.
- Agent text is `reported`; it never becomes `observed`.
- The same journal always produces the same state.
