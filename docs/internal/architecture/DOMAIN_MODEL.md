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
- **Principal**: who sent a command, as the control plane authenticated it: `local`, whoever holds
  the access token on the control plane's machine, or a paired **device**, such as a headset on
  the same network. Devices belong to no project; the owner pairs them with a code and revokes
  them ([SECURITY.md](SECURITY.md)).

A **model provider** (Anthropic, OpenAI, Ollama and so on) sits below the runtime and is not a
Halcyonic concept; runtimes such as OpenCode already manage providers. A **model** is known only
through the runtime: a runtime whose `model_choice` is `listed` lists the models it can run, a
start may carry one of them by its opaque `model_ref`, and an execution's `model_ref` is the model
the runtime last reported running on, null until it reports one
([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)).

## Execution status

Status is derived from facts, never stored. Precedence, highest first:

| Status | Derived when |
| --- | --- |
| `unknown` | Contact with the runtime was lost, or the control plane restarted, or a start may or may not have happened. Cleared by the next runtime observation. |
| `failed` | The runtime refused to start the execution. |
| `waiting_for_human` | At least one approval is pending. |
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
| `action_required` | `approval_pending`: an execution is waiting for an approval. |
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
| `workstream.create` | low consequence | the project exists |
| `execution.start` | low consequence | the workstream exists, the runtime is registered with `start_execution`, a `model_ref` is null or the runtime's `model_choice` is `listed`, for a runtime that `uses_project_location` the project has a folder the host's policy still allows at the same real path, and the adapter accepts the options and the model |
| `execution.send_instruction` | low consequence | the runtime has started a session, and: at rest (`completed`, `failed`, `interrupted`) with `instruct_at_rest`, or running (`running`, `verifying`, `waiting_for_human`) with `instruct_while_running` |
| `execution.respond_to_approval` | review required | the approval is pending, the execution is `waiting_for_human`, and the runtime has `respond_to_approval` |
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
`welcome`. Clients must require a deliberate, explicit action for `review_required` commands. Authorization is currently a single local
principal, so categories do not yet restrict who may act ([SECURITY.md](SECURITY.md)).

## Command lifecycle

```text
submitted ──> rejected                  (journaled with a reason; nothing else happens)
          └─> accepted ──> completed    (runtime confirmed, or local action done)
                       └─> failed       (effect: none | unknown)
```

- Command ids come from the client. Resubmitting the same command returns its current state;
  reusing an id for a different command is a conflict and is not journaled.
- A runtime action that is not confirmed within the timeout fails with `code: timeout` and
  `effect: unknown`, because the runtime may still act.
- A start that fails with an unknown effect makes the execution `unknown`, not `failed`.
- Effects are observed separately: an approved request completes as a command, and the runtime's
  `runtime.approval.resolved` shows that the runtime applied it.

## Invariants

- An event naming an execution also names its workstream and project.
- A runtime event with a source sequence not after the last applied one does not change state; the
  projection reports it.
- Ending a turn clears everything in flight: pending approvals, active tools and the active test
  run.
- Agent text is `reported`; it never becomes `observed`.
- The same journal always produces the same state.
