# ADR 0060: The computer says what can be done here, and buttons, saying it and one review read that one list

- Status: Accepted (read by CORE, SCREENS and AUDIT2). Built and tested with a synthetic model: `propose_command`, `read_choices`, `go_to`, the waiting proposal and the rule of origin, the person's press through `act` (secrets refused), the confirm through resume, read from the host's own list, preparation and review (section 6). No model run yet; headset pending
- Date: 2026-11-12

## Context

On main 60a0d8830 each feature is written three times: a route, a headset column, and an assistant tool. The
assistant has thirteen tools (`WORKSPACE_TOOL_ARGUMENTS`, `packages/contracts/src/workspace-tools.ts`), and one of
them acts: `continue_task`, which sends `execution.send_instruction`. The host has about thirty commands, each with a
policy in `COMMAND_POLICY` (`packages/domain/src/policy.ts`). Landed commands such as Send to GitHub, Publish, Take
Down and choosing a project's own check can be reached by no client, and none can be said. ADR 0051 already makes a
review a digest that the command carries back, for restore, move and add. The lead's decision of 2026-11-12 (one way
to reach everything, rules R1 to R6) asks for one mechanism.

Two facts shape it. First, admission (`admitCommand`, `packages/domain/src/admission.ts`) judges a whole envelope, so
"could this be done here" needs its checks split so that one needs no payload (CORE). Second, the model reads
untrusted text in the same context as the person's words (runtime reports, titles, tool output), so its choice of a
command is never the person's press (AUDIT2).

## Decision

### 1. What can be done here: one list, held by the host

For a scope (a task with its current execution, a project, or all work: the three scopes of a request),
`GET /api/actions?project_id=&workstream_id=&execution_id=` (local and paired device, like the other reads) answers
`{ scope, position, actions }`. A client reads it again once the journal passes `position`.

The list is computed from the same checks admission runs, split so that a check needs no payload:
`offered(command_type, scope, …)` answers offered, or the code admission would give. `admitCommand` calls it first,
so the list and admission cannot disagree. With needs still empty, the scope's checks alone decide the list (N3); a
filled proposal still runs full admission. Each entry, in the host's order (the first is the scope's main action):

- `command_type`.
- `title`: the host's button words (WORDS.md: 1 to 3 words, Title Case, no brand names).
- `description`: one plain line, read by the model and by the card.
- `confirm`: `none`, `undo` or `review`, declared once beside `COMMAND_POLICY`.
  - `review` exactly where the host builds a review digest.
  - `undo` only once the host has an Undo for that command (none exists today, so none is declared).
  - `none` elsewhere, which includes an approval and Stop, whose press is the decision (R1).
  - The policy category stays for audit. A test holds that every `review` command is `review_required`.
- `sayable` (section 4).
- `needs`: declared as data beside the policy, never guessed from the payload schema. Each need has a `name` (the
  payload field), a `kind`, a `label`, `required`, and, for a choice, the `list` its candidates come from.
  - `kind` is one of `text`, `choice`, `choices` (several indices, with a maximum), `yes_no`, `count` or `secret`.
  - A test holds that every payload field is filled by the scope, filled by the host (an id, `options: {}`), carried
    by the review, or declared as a need.
- `opens` (addition B): instead of a command, an entry may open a list the host holds, such as a project's tasks,
  its versions, a result's changed files or its checks.

**Lists (addition B).** Every list the host holds has one shape: rows `{ index, kind, label, detail, theirs }`, with
`kind` from one enum the host declares (R3) and `theirs` marking a person's or another host's name. Each row carries
its own entries, the commands that take that row as their choice (a version's Restore, a file's change). The headset
draws every list on one list card. The model reads every list with `read_choices`. Nobody writes a column for
History or a reader for Tasks.

### 2. One preparation, two proposers (addition A)

A command reached through the list is never built by its proposer.
- **The proposers:** the person (a button, through the list card or any screen) and the model (`propose_command`).
  Each gives only `{ scope, command_type, values }`. `values` maps each need's name to a string, a boolean, an
  integer, or an index (or indices) into the need's list as last read.
- **The host prepares** the payload by one code path:
  - the binding's ids are filled in;
  - each index becomes the id of the row it named, from a fresh list whose digest must equal the one read
    (`choices_changed` otherwise);
  - the whole command must pass its own validator and full admission.
- **`text` values are cleaned like a typed one (addition D).** A `text` value that leaves the Mac (a title, a pull
  request's words, a commit line) is clipped and cleaned by the host, exactly as a typed one is, including one that
  defaulted from `reported` text (a task's summary). The review shows it exactly as it will be sent.
- **The person's press is a request with no model.** It is `workspace.request.submit` carrying `act: { command_type,
  values }` in place of words. The host records the prepared command as the request's one step and goes by
  section 3. The same journal trail, the same review and the same Confirm serve buttons and saying it alike, and a
  screen never builds a payload by knowing the command.
- **A secret never rides in `act` (AUDIT2's C3).** `workspace.request.submit` is journaled whole, so an entry with a
  `secret` need is never reached through `act`, and the host refuses an `act` that names one. Such an entry is drawn
  as its own field and keeps TASKS's write-only request: the value is staged, and the command carries the name only.
- **An instruction is the person's own words (AUDIT2's N4).** Where a proposed `execution.start` or
  `execution.send_instruction` carries an instruction, the host fills it with the person's own words, whole, as
  `continue_task` does. It is never a `text` value the model wrote, so the card shows the heard words, not the
  model's version of them.

### 3. Who may act at once (AUDIT2's change 1; R1 and R6)

- **The person's own press** goes by the command's `confirm`:
  - `none`: done at once.
  - `undo`: done at once, with its Undo.
  - `review`: the one review card, then one Confirm.
- **A model's proposal** is never more direct than a press, and never acts outside the bound task without one.
  - It acts at once only as `continue_task` does today: the person's own words, sent whole to the request's own
    bound task.
  - It also acts at once for an `undo` command in the same scope, once undo exists.
  - Every other proposal waits as a step. The step holds the prepared command and a digest of the whole prepared
    payload (N2), so the press sends exactly what was shown. For a `none` command the card is the light one: what
    will happen, any `text` values shown whole as the assistant's proposal, and one Send. For a `review` command it
    is the host's review.
  - So starting a task by voice is: say it, then one Send that shows what will start (R1). An agent's words can
    never start another agent.
- **The confirm:** `workspace.request.resume { request_id, text: null, confirm: { step_id, review } }`. A request
  waits for the person, and the person resumes it. This saves a new command type, which would have changed every
  welcome's policy list and the recorded demonstrations (the lead and AUDIT2, 2026-11-12).
  - It is admitted only from the request's own owner: the command service's owner check for resume (N1).
  - The host re-checks the digest (`review_stale` otherwise), then submits the prepared command, with that digest,
    under that principal.
  - Cancel, or a new request on the same scope, rejects a waiting proposal.
- **The assistant can never confirm.** No `workspace.request.*` command is ever a prepared command or a list entry,
  and no tool sends one. No tool answers a runtime's approval or question. A proposal is data
  until the press, and the reply stays `reported`.

### 4. What may be said (R6)

Every entry may be proposed in any of the three scopes. This amends ADR 0030: a project request and an all-work
request may propose. They are never sayable:
- pairing and a device's rights;
- turning pairing or a share on;
- deleting a project (taking one off the list is not deleting: it deletes nothing, adding its folder again undoes it,
  and it is sayable, ADR 0063, the lead's decision of 2026-11-13);
- `execution.respond_to_approval` and `execution.answer_question` (AUDIT2's change 2);
- every `workspace.request.*` command;
- any entry with a `secret` need, whose value is typed in a field, never spoken or passed through a model.

The list enforces this with `sayable` false. The request loop drops the same entries again, whatever a list says.

### 5. The model's tools, and how its choice stays small

- **`propose_command { command_type, values }`.** `command_type` is an enum of this scope's sayable entries, built
  per turn from the list (typically 3 to 8 of them, never thirty). The scope's list sits in the host context of the
  first turn, so a change can be proposed on the first turn without a read.
- **`read_choices { list }`.** It answers up to 32 rows of a list that an entry opens or a need names, in the host's
  words, with outside names clipped and marked as theirs. The step records the list's digest host-side.
- **`go_to { index }`** (addition C). Places such as "show my world", "open settings" or "back to my projects" are
  the client's, not the host's. The client names the places it can go in `workspace.request.submit` (`places`, at
  most 16 labels). The model picks one by index, and the request's view carries `go_to` for the client. No place
  name is the model's. A place is not a command: the client goes there at once, with no press.
- The model never supplies an id (ADR 0030; `workspace-tools.ts`).

### 6. The list is the host's own

The request loop reads what can be done here from the host itself, CORE's `CommandService.here(scope)`, at every
turn, and its rows from `CommandService.choices(scope, list)`. A proposal and a press are prepared by
`CommandService.prepare`, and a reviewed command's review comes from `buildReview`, so the assistant, the buttons
and the one card read one list and one review. On the assistant's side:
- The model is offered only entries the host marks sayable. The request loop drops the never-sayable ones again
  whatever a list says.
- In a task's request, the bound task's own instruction is `continue_task`'s (section 7), so the entry
  `execution.send_instruction` is not offered to the model there.
- The list reaches the model compactly, one line of needs per entry, and the person's words take the room the rest
  of the first turn leaves, so a request with the list beside it never makes a body the transport refuses.
- Not done: AUDIT2's N4, an instruction filled with the person's own words by the host. The host's declarations
  mark an instruction as `text`, so the model writes it. The one card shows those words whole and as the
  assistant's (`said`), before the press. A declaration that a field takes the person's own words would let the host
  fill it; that is CORE's.

### 7. What stays as it is until the general thing replaces it (R5)

- `continue_task` keeps sending the person's words whole. In the list it is the task scope's
  `execution.send_instruction` entry; it is retired into `propose_command` once a window shows the local model
  sends a change through the list as reliably.
- Every landed button keeps working until its screen reads the list, and the digests that buttons send today stay
  valid: the generic review uses `reviewDigest(command_type, args, facts)` exactly as ADR 0051 does.

## The review's shape (CORE builds it; SCREENS draws it)

The host builds one review for any command: `{ command_type, digest, confirm_words, what_happens, facts, items,
items_total, warnings }`, with one reader per command type for its facts.
- `facts`: at most four, each `{ kind, value, theirs }`.
- `items`: each `{ kind, name, note, theirs }`.
- `warnings`: each `{ code, line }`.
- Every word is the host's, and kinds come from one enum. The card reads no `command_type` and no words of its own.
- **A waiting proposal carries the review's words too** (2026-11-13, XR's Usage row): `confirm_words`, `facts` and
  `warnings`. That holds for a reviewed one, and for the host's own one-press offer where the command has a review.
  So the client draws the host's card from the step alone. The fields are optional: a proposal recorded before them
  has only `what_happens` and `said`.

## A start is one review (2026-11-13, the recap is the review)

New Project, and New Task in a project, start an agent working with one press on one host review, never a card the
client builds (R6). The press is `workspace.request.submit` with `act` `{ command_type: execution.start, values,
lists }` where no task is bound. CORE's `prepareStart` (`apps/control-plane/src/core/start.ts`) prepares the whole
start before anything is made:
- **Values by the commands' own fields:** `name` and `location` for a new project, `instruction`, `runtime_id`,
  `model_ref`, `approval_level`, `plan` and `spending_cap_usd` for the run. No id is ever a value. The task's title
  and goal are the host's, made from the instruction. A new project may also take `starter`, a row of the `starters`
  list (each starter set up on this computer, no default): it goes into the new folder project.create makes
  (`new_from_starter`), is refused for an existing folder, and is said in what happens and the folder fact.
- **An absent value is the list's `default`** (the choices route), so the press can carry the idea alone. A list with
  no default, where the start needs one, refuses it and names the list in the step's `needs_choice`.
- **One step holds the commands:** `project.create` (a new project only), `workstream.create` and
  `execution.start`, each through the one preparation (`CommandService.prepareInStart`, bound to the scope it will
  stand in, its lists read where the person read them). The run's start is the step's `proposed_command`; the
  others ride in `proposed_before`. An id a later command needs from an earlier one's result is the host's
  placeholder (`START_PROJECT`, `START_TASK`), filled at the press from what was made.
- **What the runtime alone refuses is refused now** (`startOptionsRefusal`, the adapter's options, a folder the app
  needs), so the press does not stop part way on what could be known before it.
- **The review:** `confirm_words` "Start Building"; `what_happens` saying it makes the project and its new folder
  where it does; four facts, kinds `agent` (the app with its model, one fact), `place` (this computer, or the
  provider by its declared name), `folder` and `level` (what the start requests; a confirmed level differs only on
  the result); warnings by the route the run takes: free on this computer, paid on the person's own provider account
  through a key they added, or any cost on the app's own account, the price unknown, and what leaves the computer;
  reaching beyond its folder and a spending limit where they hold. Each is one short line with no product name, read
  by a fresh reader at the larger text size. A run that never asks says so first among the warnings too, in words of
  its own, since the level fact alone was misread. `said` holds the name
  and the instruction whole. The digest covers every command held and all of it.
- **The press:** the review is made again from what is held and must give the digest shown (`review_stale` before,
  "nothing was made" after). Then the commands run in order, in one turn, under the person's principal, each through
  the whole admission; the step's `made` names what each made. A stop part way says what was made and what was not,
  never "nothing changed". One press runs at a time; a press on a step that no longer waits is refused, so a project
  is never made twice.
- **A project taken off the list (ADR 0063):** a start in its scope is refused before anything is prepared, and a
  New Project on its folder says "Brings back <name>", by the project's own name, never that it makes one; the press
  brings that project back and starts the task in it.
- **The level says what the run will really ask (with the no-ask unit):** "Asks Only Outside Task Folder" where the
  host answers a confined app's own work (a kind it answers for, kept inside the sandbox, in a starter's project:
  `answersOwnWork` and `ControlPlane.answersOwnWorkIn`, the same facts the host's answers read); "Asks First" for any
  other run that asks; "Works Without Asking" where it never asks. Whether it asks at all is the adapter's to say where
  its runtime can run at another level than the one asked for (`RuntimeAdapter.levelAt`, the same confinement choice
  and policy its start makes): Codex on a starter's confined project asks nothing (ADR 0041), so its card says "Works
  Without Asking" whatever was asked for.

## Alternatives considered

- **One assistant tool per command:** a contract change per feature, a third copy of each feature, and a model
  choosing among thirty tools in an 8K context. Rejected by R5.
- **A model's `none` proposal done at once:** fewer presses, but untrusted text in the model's context could start
  work with the person's authority. Rejected (AUDIT2). At a desk the person types each new task.
- **The client resends the command at the Confirm:** the client would have to know every command, and what runs
  might differ from what was reviewed. Rejected: the host submits what it holds.
- **The model names a whole payload:** ids and folders would be the model's to supply. Rejected (ADR 0030).

## Consequences

- **A new command** is its schema, its policy line, a title, a description, `confirm`, `sayable` and `needs`. On the
  day it lands it is reachable by button, by saying it and by the one card, with no client code and no new tool.
- **Contract changes, made once, each in the rule 27 queue:**
  - LOOP: `workspace-tools.ts` gains `propose_command`, `read_choices` and `go_to`. `WorkspaceRequestStep` gains
    `proposal` and `place`. `step_recorded` gains `proposed_command`. `workspace.request.submit` gains `act` and
    `places`. The view gains `places`. `workspace.request.resume` gains `confirm`. The projection keeps a proposal's
    command in the same map as an instruction, so its saved state keeps its shape and format.
  - CORE: the list's view and route, the declarations, and the review.
  - CORE (2026-11-13, a start is one review): `ReviewKind` gains `agent`, `place` and `level`;
    `WorkspaceRequestStep` gains `made` and `needs_choice`; `step_recorded` gains `proposed_before`;
    `ChoicesResponse` gains `default`; `ChoicesQuery` gains `runtime` and `runtimes`. The projection keeps a start's
    earlier commands beside its run's (state format 23).
- **After a restart**, a waiting proposal is still in the journal. Its owner's Confirm is admitted, since it is their
  own press re-checked by its digest. Any further model step needs Resume, as before.
- **Revisit** if the local model picks the wrong entry often (measured in the first window after this lands), or if a
  scope's list grows past what one prompt holds.
