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
  `ask_person`. `read_projects` also gives `projects_total`, and `read_project` and `read_project_at` give
  `tasks_total`, the whole count from the host's lists, and the prompt says to give that number rather than count what
  was read (the 30B model said three of ten projects, 2026-11-11).
  Only Halcyonic's own projects, from the host's own lists; the model never names one. Answers
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
- On that turn the host also says, in a system message, to answer in plain words; and a reply that is a tool call
  written as text (a reply that starts with function-call or invoke markup, or bare JSON naming one of the assistant's
  own tools; an answer holding JSON or quoting markup stays an answer, AUDIT2 S1) is never kept as the reply: the request is
  interrupted with a step saying so, and Resume asks again (the 30B model wrote one, 2026-11-11).
- The planning budget's last two turns are offered no tool either, with a system message to answer from what was
  read and say what was not, so a request that reads on to its bound still ends in an answer rather than the budget
  interrupt (the 30B model spent all eight turns reading on an all-work request, 2026-11-11).
- A reply on a turn offered no tool that is a tool call written as text is asked again once a wake, still offered no
  tool, with a system message saying it was not an answer and was not kept; a second one interrupts the request as
  before. The step records each (the 30B model wrote one on the last turn of both its requests, 2026-11-11).
- A turn offered no tool is given a conversation of its own for answering: instructions that name no tool, the
  person's words and the host's context, and what was read as one quoted block, oldest dropped first to fit the input
  bound. It has no assistant message and no tool result, so the model has no tool call to copy (the 30B model wrote one
  as text on every toolless turn it was given the whole history, four runs on 2026-11-11).
- A task request's first turn, where it may send the person's words on, offers only `continue_task` and `ask_person`:
  the host's receipts and what the Mac saw are already in its context, and a question that needs no change is answered
  in words on that turn; every read comes from the second turn on. The 30B model read six times and never sent the
  change; offered `observe_task` as well, it chose to look and again never sent it (2026-11-11).
- A request about all work is not offered `ask_person`: it changes nothing, so it is answered with a summary from what
  the host lists, never a question back (the lead, 2026-11-11). The answering turn's instructions say so too: how many
  projects, which tasks run, which wait on the person, which checks failed, never a list of the fields read.
- A request never ends with nothing (the lead, 2026-11-11). When it ends (interrupted, failed or stopped) holding no reply at
  all, the change that ends it carries `observed`, what the computer itself saw, as facts: for a task, its run's
  state, its turns, the files the Mac saw its turns change, the check of this task's own folder (its own copy's, or the
  project folder's where it works there, never another task's; AUDIT2 2026-11-11) and whether the person's words
  reached the agent; for a project, its tasks by where they stand; for all work, the projects and the tasks across
  them. No model text, no path, no agent words; journaled as every control-plane fact is, observed. It stands on the
  view apart from `reply`, and any later change takes it away, so it is never beside a reply. The clients choose its
  words and draw them as the computer's own.
- The host's own lists and one task's view (`read_projects`, `read_project`, `read_project_at`, `read_task`) reach the
  model whole within 4 KiB, not as the short excerpt a large outside result gets. Past that, the last rows are left out
  and `more_projects` or `more_tasks` counts them, so the totals stay whole (the 30B model saw 13 projects as a partial
  excerpt and could not summarize them, 2026-11-11). The 4 KiB shrinks to the room the next turn's body leaves beside
  the instructions, the person's words, the host context, the current batch and every tool the request may be offered,
  measured as the transport measures it, so long words never make a body it refuses (AUDIT2, 2026-11-11).
- No request the contract accepts fails for its length (the lead, 2026-11-11; 9,600 characters of words made a first
  turn of 13,742 bytes, past the transport's 12,288, while the contract allows 32,000):
  - All the person's words share one budget, 3,584 bytes as the body escapes them, however many continuations a
    request takes (AUDIT2, 2026-11-12: a bound per message let five continuations of 900 characters pass the
    transport's bound). The request itself takes at most 2,560 and each continuation at most 1,024. The newest
    continuation is placed first, then each older one in the room left, always keeping room for the request. Each is
    whole where it fits; otherwise the first two thirds and the last third of its room, cut between characters,
    around a gap in which the computer says how many characters it left out and that the words go to the task whole.
    A continuation that cannot be given 320 bytes is left out with every older one, under one marked line that says
    how many and how many characters.
  - Words on their way to an agent are never shortened. `continue_task` sends the person's whole words, by the host,
    never retyped by the model. Where the host's framing and the model's follow-up would push them past the
    instruction limit, the person's words go alone and whole (a request alone is within that limit by its contract).
    Only continuations that together pass the limit still pause for the person, with nothing sent and nothing cut.
  - A reply written from a shortened view ends with the host's own sentence: how many of how many characters the
    answer read, the beginning and the end. It says that all of them went to the agent only when a `continue_task`
    step was confirmed after the person's last words, and only while every step of the request is still in its view;
    an instruction sent before a continuation never held it (AUDIT2, 2026-11-12). The host writes it, since the host
    knows what it cut; the reply stays `reported`.
  - Every answer is asked for in a few short sentences of the model's own, as to a person: never the computer's field
    names, ids, timestamps, headings or markup (the 30B window of 2026-11-12: both answers were right and read like a
    log).

## Amendment, 2026-11-12: proposing from what can be done here (ADR 0060)

- A request in any scope (a task, a project, all work) may propose one entry of the host's list of what can be done
  there, through `propose_command`, and read the host's lists through `read_choices`. A project request and an
  all-work request are no longer read-only for that one tool. They still change nothing on the model's word: a
  proposal waits for the person's one press, except an undo-kind command in the same scope; the person's words to the
  bound task stay `continue_task`'s.
- A request may carry the places the client can go (`places`), and the model picks one by index with `go_to`; a place
  is not a command, and the client goes at once.
## Amendment, 2026-11-12: the host's facts in sentences, and an answer that reads like records

- What the Mac observed of a task reaches the model in sentences, from one table of the host's codes to words
  (`workspace-agent/fact-words.ts`): its state, the files its turns changed, the computer's check and any retry it
  sent back. A project's latest check and a task's status read the same way. The file paths and page errors the
  agent wrote stay beside them as quoted data. So there is no field name or code for the model to repeat (the 30B
  window of 2026-11-12: both answers were right and read like a printout).
- An answer that still holds an identifier joined by underscores (not in the person's words or the names the request
  is about), a bracketed list or an ISO time is not kept: the model is asked once more, offered no tool, to say it in
  a few plain sentences. A path or a file name is never one.

