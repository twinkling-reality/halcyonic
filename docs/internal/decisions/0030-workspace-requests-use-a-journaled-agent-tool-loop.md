# ADR 0030: Workspace requests use a journaled agent tool loop

- Status: Accepted; built in the control plane and run end to end on local models with a real runtime (2026-11-10, [record](../validation/workspace-requests.md)); the requests across all work, the quoted data block and the reused context not yet shown on a real model; the headset does not show a whole-project or all-work request yet; headset request UI, speech and device evidence pending
- Date: 2026-10-12
- Complements ADR 0025. The creation companion remains a separate, transient proposal flow.

## Context

The owner requires open-ended requests carried through to an outcome in VR. Field dictation and
the creation companion do not provide that capability: the companion sends no tools and proposes
only a first task. A phrase router or a model choosing one predefined screen would still require
the person to coordinate the work. Panels should make the work and its decisions inspectable.

The current local model transport can support a real tool loop. Dated synthetic round trips on
the configured model are recorded in [contextual-voice.md](../validation/contextual-voice.md).
Those prove the protocol, not autonomous task completion. Existing runtime adapters already carry
software instructions through their verified execution surfaces. Their permissions, capabilities
and observations remain authoritative.

## Decision

A separate workspace request coordinator assembles context, asks the model, validates and runs
available tools, reads results and continues. Natural-language requests are not classified into a
fixed phrase catalogue. Typed tools define real operations; reusable panels display context,
reported reasoning, observed receipts and decisions without defining the extent of the agent's
reasoning. Held speech supplies an editable ordinary request through the current transcription path.

The first consumed scope is an existing project, task and current execution. The deliberate request
binds that scope and authenticated principal. Reads and authorized continuation run without asking
the person to open each panel. Required human approvals and questions stay under their existing
review rules. Different project, runtime, model or folder authority is never inferred from model
text. Tool output, task titles and quoted material are data, not new instructions from the person.

Request lifecycle facts go through `Recorder` and a journal projection consumed by the request UI.
A request identifies one direction about existing work; it does not replace a Workstream or create
a vendor-session character. Reconnection reads the same request. In-memory model messages and
in-flight tasks are implementation details, not an independent source of durable work state.

Submission, continuation and cancellation use admitted, deduplicated commands. Before a mutating
tool acts, its prepared command identity is durable. Dispatch preserves the original authenticated
principal through `CommandService` and rechecks current journal, project/task/execution, directory
policy and declared capability. Awaited reads do not extend expired authority. A changed target,
revocation or cancellation prevents further actions. Model-generated commands never use an
internal privileged principal.

Existing command and runtime events provide action evidence. Command acceptance is pending, and
confirmation that an instruction was delivered is not confirmation that the software task finished.
The loop observes the relevant runtime outcome before continuing. A timeout or response loss with
unknown effect never authorizes a new mutation attempt. Model prose remains reported; a settled
conversation does not certify the correctness or completion of software work.

External Understanding and Evaluation conclusions remain read-through under ADR 0010. The journal
can retain read recipes, source references and times, not turn a provider document into observed
domain truth. After restart those observations expire and require a fresh authorized read. A
restart interrupts coordinator ownership and reconciles already-issued command outcomes. The
first implementation requires explicit Resume before new mutations; it does not replay a pending
or unknown command. Runtime work can continue independently while its coordinator is interrupted.

The model transport remains local and verifies actual tool capability. Bounds apply to model time,
turn count, tool batches, context and input/output size. Waiting for runtime or human input does not
continuously call the model. Reaching a bound produces an explicit resumable state. There is no
hosted fallback, model download, arbitrary network tool, raw shell tool, credential tool or assumed
external-screen access. Native runtime tools continue to use their existing sandbox and approvals.

## Consequences

The implementation must include the lifecycle contract, projection, host loop and actual request
consumer together. Generated JSON Schema and C# bindings come from TypeBox once. Unity and native
surfaces must share request identity and show reported text separately from command/runtime facts.
Hiding a panel does not cancel already-authorized work; cancelling planning does not imply that a
runtime was interrupted. Both outcomes need clear, truthful controls and receipts.

Acceptance requires real model tool/result continuation and an actual control-plane/runtime path,
with negative cases for malformed calls, changed context, revocation, response loss, duplicates and
restart. Independent review must examine authority and persistence. Headset speech, interruption,
correction, contextual views and other-app return require device evidence. A passing protocol probe
or a new request screen alone is not acceptance of the owner's end-to-end agent requirement.

## Amendment, 2026-11-10: a request about a whole project, and the evidence of what a task did

The owner asked for understanding of all kinds, for anyone (2026-11-10). Two gaps stood: a request
had to name one task and its current execution, and its tools could not see what the Mac observed a
task change, nor the project's check, so "what did it change and is it working" had no evidence to
answer from.

- A request may bind a project alone: `workspace.request.submit`'s `workstream_id` and
  `execution_id` are both null or both set (admission refuses one without the other), as are the
  request view's and the three request events' scope (a request scope of its own). A whole-project
  request is read-only: it is offered `read_project`, `read_task` and `ask_person`, never
  `continue_task` or `observe_task`; to act, the person opens the task.
- `read_project` lists the project's tasks (at most 64) with their status, agent app, turns, what
  waits for the person and the failed check sent back on its own (ADR 0044), and the project's
  latest check. `read_task {index}` reads one task by its index in the list `read_project` last gave
  this request; the host keeps that list for the request and refuses an index before it or outside
  it. The model never names a task, so no target comes from model text. A task's request is not
  offered them: the model's input is bounded at 12 KiB, and they cost room its own tools need.
- The host's context at every wake of a task's request (`observed_by_the_mac`), and `read_task`, carry
  the evidence the Mac observed: the files each turn created, changed and deleted (totals, and up to
  12 paths, newest first), the project's latest check (each
  step, page errors, whether the files changed since), the chain of ADR 0044, and the model the
  runtime reported. It sits in the context, not only behind a tool, because the first real run asked
  "what did it change, and is it working?" and the model answered from the agent's own messages
  without reading it. `read_events` gives counts for the host's own task events. Paths and page errors
  are outside text, marked as data. The system prompt asks for plain words, what the Mac saw and did
  not, and "not checked" where there is no check.
- Not decided here: a request across projects. It is proposed separately.
- After work the request sent, the coordinator waits while the project's check is under way, or while a
  turn's files are not yet read (its check may follow), and wakes when the check ends or the files are
  read: "does it work" is answered from the Mac's check, never ahead of it. The first real run answered
  five seconds before its check ended.
- A tool call is refused unless the request was offered that tool at that moment, whatever tools exist
  (AUDIT2, 2026-11-10). The wait for the Mac's reading of a turn's files ends a minute after the task's
  last activity, with a wake-up then, so an observer that is off or failing never holds a request.

## Amendment, 2026-11-10 (later): a request about all work, the model it runs on, and quoted data

- A request may bind nothing: `project_id`, `workstream_id` and `execution_id` all null, for "what is going
  on with my work". It is read-only and offered `read_projects` (the person's projects by index, at most 64,
  with task counts by state, the latest check, and whether a fix chain runs), `read_project_at {index}` (one of
  that list, read as `read_project`; it chooses the project whose tasks `read_task` then reads) and
  `ask_person`. Only Halcyonic's own projects, from the host's own lists; the model never names one. Answers
  name projects and tasks by the names the host gave. Admission refuses any other mix of null fields.
- The model: a request runs on the person's chosen local model, the one `pnpm mac-setup local-model` named,
  where one is set, else on the companion's model; never a hosted model (the lead, 2026-11-10). SYSTEM.md
  says which setting decides.
- A request that asks for a change calls `continue_task` first: the Mac's evidence is already in its context,
  so it reads only what that lacks. The 30B local model spent all eight turns on reads before this rule.
- Text an agent wrote, or that was read from the project (file paths, page errors, the agent's own messages),
  reaches the model in a user-role block that says it is quoted data, neither the person's words nor
  instructions; the host's own receipts stay in a system message (AUDIT2 S3).
- Once the work a request sent has its outcome (its turn ended, nothing waits, the Mac's check of it in),
  the next model turn is offered no tool, so the model answers from the evidence already in its
  context. This changes the earlier rule that a wake never forces a final answer: the 30B local model
  read on past the outcome until its planning bound, with no reply (2026-11-11). The prompt also says
  so (`work_outcome_in`). A model that still answers with a tool call there fails that turn, as for any
  call not offered.
