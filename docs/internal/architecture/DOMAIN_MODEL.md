# Domain model

The vocabulary of Halora (`halcyonic`) and the rules over it. Types live in `packages/contracts`;
rules live in `packages/domain`.

## Concepts

- **Project**: a software project or product context. Created by `project.create`. It may have a
  **location**: one folder on the host, inside a project root the host allows, where real
  executions of its work run or from which task copies are cloned
  ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)).
  A client chooses it from the folders the host lists, or names a new folder the host makes; it
  never sends a path. `project.set_location` binds the project to another folder, for example after
  the folder was moved; executions already started keep the folder they started in. A project can be
  taken off the visible list (`project.remove_from_list`) without touching its folder or journal,
  and adding that folder again restores the same project (`project.returned`,
  [ADR 0063](../decisions/0063-a-project-leaves-the-list-and-comes-back-by-its-folder.md)).
  A new project may begin from a **starter** the host vets and keeps, such as a VR app for the
  headset's browser: its folder starts with the starter's files and a link to the starter's shared,
  read-only dependencies, and `ProjectView.starter` names the starter (or is null;
  [ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)).
  `ProjectView` also carries optional `check`, `own_check`, `rules`, `preview`, `mac_play`,
  `share`, `published`, `secrets` (names only, never values), `media`, `usage`, `world` and, on
  the unlisted change delta, `removed_at`.
  A project may be **linked** to one repository of the person's catalog, the list of every project
  they have across their accounts that Omnorium keeps
  ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)): GitHub's
  numeric repository id, its stable identity, beside its `owner/name` as the client sent it, taken
  from the catalog's answer; checked against the catalog this computer last read, never against
  GitHub. Only the person links (`project.link_catalog`, again to replace the link) and unlinks
  (`project.unlink_catalog`); the catalog itself is read through and never part of the model. The
  link is kept out of `ProjectView`: clients read it through `GET /api/catalog`. The two link events
  still reach the person's own clients in the realtime stream, `owner/name` included, as every event
  except device pairing and revocation events does.
- **Checkpoint**: every file of a project's folder as it was at one moment, kept by the host for
  undo when the host has set it up: before a task starts, after each turn the runtime reports ended,
  and before a restore. `project.restore` puts one back, and completes only once the host has read
  every file again and found the folder equal to it
  ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)).
  No runtime is asked to undo anything.
- **Workstream**: a meaningful unit of work, such as "Fix flaky checkout tests". It is the aggregate
  a character represents, and it keeps its identity across executions and runtimes. When task copies
  are enabled ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)), each task works
  in an isolated clone of the project's folder (`WorkstreamView.copy`), can run its own check and
  preview (`WorkstreamView.preview`, projected from `taskPreview`), and carries summed token and
  cost figures across its runs (`WorkstreamView.usage`).
- **Execution**: one attempt by one runtime toward a Workstream. It corresponds to the runtime's
  own session or thread (an OpenCode session, a Claude Code session, a Codex thread, an ACP session),
  whose id is kept as `native_id`. A workstream may have several executions over time, one active at
  a time. `ExecutionView` carries derived `status`, `plan` and `plan_left`, `unconfined_reason`,
  `pending_approvals`, `approvals` (history), `pending_questions`, `queued_instructions`
  ([ADR 0056](../decisions/0056-an-instruction-sent-during-a-turn-waits-for-it-on-a-runtime-that-cannot-steer.md)),
  `active_tools`, `latest_test_run`, `latest_files`, `writing` (paths currently being written for
  Watch It Work), `media` ([ADR 0066](../decisions/0066-a-persons-photo-or-video-goes-into-the-projects-folder-as-a-copy-without-its-place.md)),
  `auto_fix`, `usage`, `spending_cap`
  ([ADR 0062](../decisions/0062-a-tasks-spending-limit-is-the-apps-own-stop-handed-only-what-is-left.md))
  and `closing_words` (`did`, `why`, `how`, `left`, always `reported`).
- **Turn**: one burst of work inside an execution, ending as completed, failed or interrupted.
  Every verified runtime has this concept. Turns are not stored as entities; they drive the
  execution's status.
- **Runtime**: a configured adapter instance (`runtime_id`) of some kind (`mock`, `claude-agent`,
  `opencode`, `codex`, `antigravity`, `kiro`, `cursor`) with declared capabilities. A **synthetic**
  runtime fabricates activity for development and the built-in demonstration, and every client must
  label its work as such. A runtime whose descriptor declares `uses_project_location` (Claude Code,
  OpenCode, Codex, Antigravity, Kiro, Cursor) runs its agents in the task's copy or the project's
  folder and nowhere else; the mock runtime uses none. An execution's `directory` is the folder its
  runtime was given, null for a runtime that uses none, and null, meaning not recorded, on legacy
  execution events written without a directory field.
- **Command**: a request to change state. It is admitted or rejected, and if admitted it later
  completes or fails. Sending a command is never proof that it happened.
- **Event**: an immutable fact in the journal. See [EVENTS.md](EVENTS.md).
- **Question**: something an agent asks the person through its runtime's structured surface, one
  or more prompts with options and perhaps typed text, answered with `execution.answer_question`
  when the runtime has `answer_question` and the adapter can carry the answer back faithfully.
  A prompt asking for a secret is never answerable through Halora. An execution shows at most
  three pending questions, those Halora can answer first, then the others, each oldest first;
  only those shown can be answered or are named in its attention. An adapter reports a question
  longer than 16,000 characters in all shortened and unanswerable
  ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)).
- **Principal**: who sent a command, as the control plane authenticated it: `local`, whoever holds
  the access token on the control plane's machine, or a paired **device**, such as a headset on
  the same network. Devices belong to no project; the person pairs them with a code and revokes
  them ([SECURITY.md](SECURITY.md)).

A **model provider** (Anthropic, OpenAI, Ollama and so on) sits below the runtime and is not a
domain entity; runtimes such as OpenCode manage their own providers, while hosted assistant and
runtime provider keys on the host are declared once in `providers/<id>/provider.json`
([ADR 0061](../decisions/0061-a-hosted-provider-is-one-declaration-file-the-host-reads.md)). A
**model** for an execution is known through the runtime: a runtime whose `model_choice` is `listed`
lists the models it can run, a start on such a runtime must carry one of them by its opaque
`model_ref`, and an execution's `model_ref` is the model the runtime last reported running on, null
until it reports one
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

Statuses from the specification that are deliberately absent, because nothing produces them:
`queued` (no scheduler exists), `paused` (no verified runtime can pause a turn), `reviewing` (no
reviewer concept exists). A status is added together with the fact that produces it.

A workstream's status is `created` until it has an execution, then the status of its most recent
execution.

## Attention

Whether a workstream needs its human, and why. Every signal names the facts behind it.

| Level | Reasons |
| --- | --- |
| `action_required` | `approval_pending`: an execution is waiting for an approval; `question_pending`: an agent is waiting for an answer, whether or not it can be answered through Halora. |
| `notice` | `execution_failed` or `execution_state_unknown` for the current execution; `verification_failed`: the current execution completed while its last test run did not pass. |
| `none` | Nothing needs the human. |

Completion alone is not an attention signal; status already conveys it. The server sends status
and attention only; clients decide presentation (animation, material, sound, placement).

## Runtime kinds

| Kind | Runtime | Synthetic |
| --- | --- | --- |
| `mock` | Scripted scenarios for development and for the recorded demonstration, whose runtimes are named for it | yes |
| `claude-agent` | Claude Code through the Claude Agent SDK, displayed in details as "Claude Agent" | no |
| `opencode` | OpenCode's v2 server API, pinned 2.0.18 | no |
| `codex` | Codex's app-server, stable surface, pinned 0.157.0 | no |
| `antigravity` | Antigravity over the Agent Client Protocol, pinned 1.5.10 ([ADR 0067](../decisions/0067-agent-apps-that-speak-acp-are-directed-through-one-adapter.md)) | no |
| `kiro` | Kiro CLI over the Agent Client Protocol, pinned 3.4.0 ([ADR 0067](../decisions/0067-agent-apps-that-speak-acp-are-directed-through-one-adapter.md)) | no |
| `cursor` | Cursor CLI over the Agent Client Protocol, pinned 2026.11.13 ([ADR 0067](../decisions/0067-agent-apps-that-speak-acp-are-directed-through-one-adapter.md)) | no |

A kind (`RuntimeKind`) names the adapter type; the runtime id (`RuntimeId`) names a configured
instance of it. Salidium observes `claude-agent` sessions (as provider `claude-code`), `codex`
threads (as `codex`, by thread id), and `opencode` sessions (as `salidium/opencode` when the
running Salidium daemon advertises that provider); Seorak observes `claude-agent` (`claude-code`)
and `codex` (`codex`). Neither observes `mock` or ACP agent apps.

## Commands

Policies and button titles (`COMMAND_POLICY` and `COMMAND_SAYING` in `packages/domain/src/policy.ts`):

| Command | Policy | Button (`COMMAND_SAYING`) | Admitted when |
| --- | --- | --- | --- |
| `project.create` | low consequence | `New Project` | always, and when it names a location, the host can bind it (below). Adding a folder that belonged to a project removed from the list brings back that project (`returned: true` on `project_created`, [ADR 0063](../decisions/0063-a-project-leaves-the-list-and-comes-back-by-its-folder.md)) |
| `project.set_location` | review required | `Change Folder` | carries `location` and optional `review` (required from a paired device when rebinding a project that already has a folder, [ADR 0051](../decisions/0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md)). The project exists and the host can bind the location |
| `project.rules.set` | review required | `Project Rules` | carries `on` and optional `review` (the digest from `GET /api/projects/:id/rules`, required when turning rules on, `review_required` / `review_stale`; [ADR 0051](../decisions/0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md), [ADR 0059](../decisions/0059-a-projects-own-rules-reach-a-task-only-as-the-person-read-and-chose-them.md)). The project exists and, when `on` is `true`, has a folder (`location_required`) with a readable `AGENTS.md` or `CLAUDE.md` |
| `project.own_check.choose` | review required | `Choose Check` | carries `scripts` (each `{ name, sha256 }` from `GET /api/projects/:id/own-check`) and optional `open_ports` (refused from the assistant). The project exists, was not made from a starter, has a folder, and is not busy (`invalid_state` otherwise, [ADR 0055](../decisions/0055-a-projects-own-check-is-chosen-by-the-person-and-confined-as-an-agent.md)) |
| `project.make_vr_version` | low consequence | `Make VR Copy` | the original exists and has a folder (`location_required`), a starter declaring `vr_version` (`iwsdk-vr`) is installed (`capability_unsupported`), and the host can make the new folder; creates `<name> VR` from that starter with `project.made_from` naming the original and the instruction the host wrote; starts no work |
| `project.link_catalog` | low consequence | `Link Repository` | the project exists, a catalog of the person's projects is set up on the host (`capability_unsupported` otherwise), and the catalog it last read holds the repository by its id under the same `owner/name` and is the person's own, not a sample (`invalid_state` otherwise) |
| `project.unlink_catalog` | low consequence | `Unlink Repository` | the project exists and is linked (`invalid_state` otherwise) |
| `project.remove_from_list` | review required | `Remove From List` | the project exists and is currently listed, no task in it is active, no check, preview, Mac play or share of it is running or live, and no command for the project awaits its outcome (`invalid_state` otherwise, [ADR 0063](../decisions/0063-a-project-leaves-the-list-and-comes-back-by-its-folder.md)). Leaves its folder and journal untouched |
| `project.restore` | review required | `Restore` | undo is set up on the host (`capability_unsupported` otherwise), the checkpoint is the project's (`checkpoint_not_found` otherwise), no task in the project is `starting`, `running`, `verifying`, `waiting_for_human` or `unknown` other than after a restart, and no command in the project waits for its outcome (`invalid_state` otherwise) |
| `project.check` | low consequence | `Check Project` | the project has a folder (`location_required`), either was made from a starter with starter checks set up on the host or is a non-starter project with a chosen `own_check` and own checks set up (`capability_unsupported` / `invalid_state`), and no task in that folder is running (`invalid_state`). With `workstream_id`, checks that task's own copy ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)): the task must have one and be at rest with nothing pending for it, while other tasks in their own copies do not hold it back |
| `project.preview.start` | low consequence | `Play` | previews are set up (`capability_unsupported`), the target folder's latest check is current (`files === 'current'`) and servable (`checkServable`: `passed` or `verdict === 'not_known'` with `drawn === null`), and no preview of that folder runs (`invalid_state`). Optional `workstream_id` starts a preview of that task's own copy (`WorkstreamView.preview`) |
| `project.preview.stop` | low consequence | `Stop Playing` | a preview of the project (or, with `workstream_id`, of that task's own copy) runs (`invalid_state` otherwise) |
| `project.open_on_mac` | low consequence | `Play on Computer` | a browser to play in is set up on the host (`capability_unsupported` otherwise), the project's latest check is current (`files === 'current'`), passed, saw the game work with a mouse and keyboard and found every agent run confined, the running preview serves that check and is current, and no play of the project runs (`invalid_state` otherwise); the game plays in the host's contained browser, never the person's Chrome |
| `project.show_in_finder` | low consequence | `Show Folder` | the project exists and has a folder (`location_required`). The host shows its own folder in Finder on the Mac, selected; never a task's copy, never Halora's data. Completes once Finder was asked |
| `project.save_copy` | low consequence | `Save a Copy` | the project exists and has a folder (`location_required`), no restore of it or add to it awaits its outcome, and no task in it is at work or may be (`invalid_state`). Writes `<folder name> <date> <time>.zip` to Downloads (or `<data dir>-downloads`), never overwriting an existing file, and shows it in Finder; result `project_saved` (`file`, `files`, `bytes`). Built from the checkpoint store's tree without `.git`, ignored paths, bound sub-projects, or paths matching `secretShapedPath` in `runtime-core`. Any tree holding a host-held secret in any `heldForms` encoding is refused `holds_secret`, effect `none`, naming the file and which secret, never its value |
| `project.share.start` | review required | `Share` | carries `way` (`link` or `wifi`). Sharing is set up on the host (`unavailable` otherwise), no share of the project is live by either way, its latest check passed, is current and found every agent run confined (`reads_confined`), and the running preview serves that check and is current (`invalid_state` otherwise). By `link` the host gives the preview a temporary public `https://` address through the pinned tunnel program; by `wifi` it listens on the Mac's private IPv4 address on its local network at a port of 48040 to 48059 (for the 47800 port family) and passes only `GET` and `HEAD` to the preview under the preview's own Host with no cookie or body (off any network it fails `no_network`). Completes as `share_started` (`check_id`, `url`, `way`) once the address answers 200 within 90 s ([ADR 0054](../decisions/0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md)). Refuses `holds_secret`, `link_out` or `unavailable` (over 20,000 files or 512 MB) before listening |
| `project.share.stop` | low consequence | `Stop Sharing` | a share of the project is live (`invalid_state` otherwise). The address stops answering at once |
| `project.send` | review required | `Send for Review` | carries `review`, the digest of `POST /api/projects/:id/send-review` over every value the press sends ([ADR 0051](../decisions/0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md)), and optionally `title`, `visibility`, `repository_name` and `one_change`. The project exists and has a folder (`location_required`), no restore or add awaits its outcome, and no task in it is at work (`invalid_state`). Uses the person's own `git` and `gh` ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)) to commit without secret-shaped files, sub-projects or outward links (`links_left_out`), push a new branch without force (creating the repository first on an initial send when needed), and open a pull request where `gh` is set up; result `sent` (`new_branch`, `commit`, `review_request_url`). Refuses `holds_secret` at review and press without pushing. One of `Send for Review`, `Publish`, `Take Down` and `Make Private` runs at a time per project (`invalid_state` otherwise) |
| `project.publish` | review required | `Publish` | carries `review`, the digest of `POST /api/projects/:id/publish-review`, and optionally `make_public`. The project has a folder (`location_required`), and its latest check passed, is current and found every agent run confined (`invalid_state` otherwise). Pushes only the check's built copy to `halcyonic-pages` without force and enables GitHub Pages ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md), [sending-to-github.md](../validation/sending-to-github.md)); result `published` (`commit`, `address`). Once `make_public` has made the repository public, any subsequent failure (`publish_failed`, `push_refused`, `pages_refused`) carries `effect: 'unknown'` and offers `Publish` again or `Make Private`. Refuses `holds_secret` at review and press. One of `Send for Review`, `Publish`, `Take Down` and `Make Private` runs at a time per project |
| `project.unpublish` | review required | `Take Down` | carries `commit`, the published commit the person was shown. The project's game is published (`invalid_state` otherwise) and still at that commit (`review_stale`). Deletes the `halcyonic-pages` branch of the repository it was published to, never the folder's current origin (`origin_changed` when different), with a lease on that commit (`pages_changed` if moved). One of `Send for Review`, `Publish`, `Take Down` and `Make Private` runs at a time per project |
| `project.make_private` | low consequence | `Make Private` | carries `project_id` only. The project exists and has a folder (`location_required`), this Mac's journal records `project.repository_made` and `project.repository_made_public`, and no game is published from it (`invalid_state` otherwise). Runs `gh repo edit <repository> --visibility private --accept-visibility-change-consequences` on the journaled repository, records `project.repository_made_private`, and completes ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)). One of `Send for Review`, `Publish`, `Take Down` and `Make Private` runs at a time per project |
| `project.secret.set` | review required | `Add Secret` | carries `name` only, never a value ([ADR 0057](../decisions/0057-a-projects-secrets-are-kept-on-the-mac-and-given-to-a-tasks-commands-at-its-start.md)). The project exists (`project_not_found`), the name is not reserved by the host or an agent (`invalid_state`), and the project keeps fewer than 32 secrets or already has that name (`invalid_state`). The value must be staged for this command via the Mac's setup page or `POST /api/projects/:id/secrets`; otherwise it fails `unavailable`. Records `project.secret.stored`; later tasks started on runtimes declaring `secret_environment` receive it |
| `project.secret.remove` | review required | `Remove Secret` | the project has a secret of that name (`invalid_state` otherwise). The value is deleted and `project.secret.removed` is recorded; later tasks no longer receive it |
| `project.media.add` | low consequence | `Add Media` | the project has a folder (`location_required`), nothing of it is at work or waiting for its outcome (`invalid_state`), and it holds fewer than 50 photos and videos and under 240 MB including derived files (`media_full`). Refuses `media_not_kept` or `media_present`; completes with `media_added` (`path`) once the tag-stripped copy and its stills or play copy are recorded ([ADR 0066](../decisions/0066-a-persons-photo-or-video-goes-into-the-projects-folder-as-a-copy-without-its-place.md)) |
| `workstream.create` | low consequence | `New Task` | the project exists. With `from_checkpoint`, starts from that version of the project ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)): it must belong to this project (`checkpoint_not_found` otherwise) and task copies and undo must be on (`capability_unsupported` otherwise) |
| `workstream.add_to_project` | review required | `Add to Project` | carries optional `review` (`copy.to_add_review`), required from a paired device (`review_required`) and checked against the latest reading (`review_stale`, [ADR 0051](../decisions/0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md)). The task has its own copy (`invalid_state` otherwise), none of its executions is active and no command for it awaits its outcome, the project has a folder (`location_required`), no restore, add, check, folder change or VR version awaits its outcome, and no other task works in the project's folder ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)); result `workstream_added` (`checkpoint_id`, `files`) |
| `workstream.fit_to_project` | low consequence | `Fit to Project` | the task has a copy whose latest add conflicted with the project (`invalid_state` otherwise), none of its executions is active, no command for it awaits its outcome, the project has a folder, and no restore, add, check, folder change or VR version awaits its outcome; result `workstream_fitted` (`files`) |
| `execution.start` | low consequence | `Start` | no restore of the project is in progress, the workstream exists, none of its executions is `starting`, `running`, `verifying` or `waiting_for_human` and no `execution.*` command for it awaits its outcome (`invalid_state`), the runtime is registered with `start_execution`, `model_ref` is given exactly when `model_choice` is `listed` (`model_required`), for `uses_project_location` the project has an allowed folder at the same real path, no add or fit for the task awaits its outcome, no other task's execution or pending uncopied start occupies an overlapping folder (`folder_in_use` / `invalid_state`), and the adapter accepts the options and model; with `approval_level: 'never_ask'`, the runtime declares `offers_never_ask` (`capability_unsupported`); with `plan`, the runtime declares `offers_plan` (`capability_unsupported`) and `approval_level` is not `never_ask` (`invalid_runtime_options`); with `spending_cap_usd` (never proposed by the assistant), the runtime declares `offers_spending_cap` (`capability_unsupported`, [ADR 0062](../decisions/0062-a-tasks-spending-limit-is-the-apps-own-stop-handed-only-what-is-left.md)) |
| `execution.send_instruction` | low consequence | `Send` | no restore of the project is in progress, the runtime has started a session, and either the execution is at rest (`completed`, `failed`, `interrupted`) with `instruct_at_rest`, no other execution of its workstream active or pending and no overlapping folder in use, or the execution is active (`running`, `verifying`, `waiting_for_human`) with `instruct_while_running` or instruction queueing ([ADR 0056](../decisions/0056-an-instruction-sent-during-a-turn-waits-for-it-on-a-runtime-that-cannot-steer.md)); with `leave_plan`, the runtime declares `offers_plan` and the execution is currently in plan mode (`invalid_state` otherwise); at a spending limit or waiting for one, `spending_cap_usd` is required (`invalid_state` otherwise, never proposed by the assistant; [ADR 0062](../decisions/0062-a-tasks-spending-limit-is-the-apps-own-stop-handed-only-what-is-left.md)) |
| `execution.drop_queued_instruction` | low consequence | `Drop` | carries `execution_id` and `command_id`. That instruction is currently waiting in `queued_instructions` on the execution (`invalid_state` otherwise, [ADR 0056](../decisions/0056-an-instruction-sent-during-a-turn-waits-for-it-on-a-runtime-that-cannot-steer.md)); dequeues it and fails the waiting command unsent (`queued_dropped`, `effect: 'none'`) |
| `execution.respond_to_approval` | review required | `Approve` | the approval is pending, the execution is `waiting_for_human`, and the runtime has `respond_to_approval` |
| `execution.answer_question` | low consequence | `Answer` | the question is pending (`question_not_found` otherwise), the execution is `waiting_for_human`, the runtime has `answer_question`, the question is answerable and asks for no secret (`capability_unsupported` otherwise), and the answers fit its prompts (`invalid_answer` otherwise) |
| `execution.interrupt` | review required | `Stop` | the execution is `running`, `verifying` or `waiting_for_human`, and the runtime has `interrupt` |
| `workspace.request.submit` | low consequence | `Ask` | workspace requests are configured on the host (`capability_unsupported` otherwise), optional `project_id`, `workstream_id` and `execution_id` exist and nest consistently (`*_not_found` / `invalid_state`), and fewer than 4 workspace requests are currently `running` or `awaiting_confirmation` (`invalid_state` otherwise, [ADR 0030](../decisions/0030-workspace-requests-use-a-journaled-agent-tool-loop.md)) |
| `workspace.request.resume` | low consequence | `Answer` | workspace requests are configured (`capability_unsupported`), and the request exists (`request_not_found`) and is `awaiting_confirmation` with `pending_confirmation` (`invalid_state` otherwise) |
| `workspace.request.cancel` | low consequence | `Cancel` | the request exists (`request_not_found`) and is `running` or `awaiting_confirmation` (`invalid_state` otherwise) |
| `assistant.model.choose` | review required | `Use Model` | carries `model` (`{ provider_id, model_id }` or `null` for this computer's local model). Admitted when `model` is `null` or matches an entry in the host's `assistant_models` list (`invalid_state` otherwise, [ADR 0030](../decisions/0030-workspace-requests-use-a-journaled-agent-tool-loop.md), [ADR 0061](../decisions/0061-a-hosted-provider-is-one-declaration-file-the-host-reads.md)) |

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
and send an answer to a question only when the person presses the control that sends it. Both
`local` and paired `device` principals may submit commands, with paired devices required to include
the review digest they read when acting on files ([ADR 0051](../decisions/0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md), [SECURITY.md](SECURITY.md)).

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
