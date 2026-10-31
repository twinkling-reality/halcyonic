# How Halcyonic speaks

Halcyonic's own words on the headset: how they sound, the word for each state of a task, and what
a person never reads. Every word lives in the client core (`apps/xr/Packages/com.halcyonic.client`),
in `StateLanguage`, `EntryText`, `WorkspaceText`, `ConnectionText`, `VoiceText`, `HostText` and their kin, so
tests hold them to these rules and the Unity layer only lays them out
([ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)).
Text from outside (titles, agent messages, tool output, server errors) is not ours: it shows as
written, by `LabelText`'s rule, and only where a person needs it, and it leans, on a ground only
Halcyonic draws where it meets our words (the owner, 2026-10-08; ADR 0026). Amended 2026-10-16 for
the independent workspace candidate (ADR 0026): agent prose attributed whole, by its chip and
Halcyonic's quote mark, and the person's instruction in its input stand upright; a line mixing our
words with theirs keeps the lean and the ground.

## Starting a task

The current action is **Start task**, confirmed by **Yes, start task**. It starts the selected
runtime execution; it does not promise a software build, APK, deployment or working output.
The wizard’s final visible step is **Start**, including its review and waiting states. Its internal
Build enum/navigation key remains an implementation detail.
Older wording examples below that call this action Start building are superseded by this name.
The selected-project Tasks view offers **New task** as a literal action, keeping the project's
outside name out of the fixed button label. Opening it only opens that existing project's draft;
no project or execution is created until the full existing review and explicit confirmation.

## The voice

Calm and plain, like a capable colleague who says what is true now, how sure it is, and what you
can do next.

1. **Controls, headers and stages are 1 to 3 words in Title Case.** Each names the exact system
   concept ("Needs You", "Read Command", "Activity"), never a conversational clause (the owner's
   voice standard, 2026-10-30).
2. **Metadata reads in the " · " cadence.** Segments are joined by " · " without chatty
   prepositions ("Codex · 0.9 s · Observed"). Words from outside (names, paths, commands, the
   agent's text) stay exactly as written. Stages are parallel, and a sentence that only explains
   the control beside it is deleted; a sentence that carries a truth stays, in the cadence.
3. **Only what is true now, and how sure.** Sending, Sent · Awaiting Agent, Confirmed only when
   the agent confirmed, Refused, Unconfirmed, Not Sent wherever a press sent nothing. A finished round says nothing
   about whether the work is right, so nothing celebrates it.
4. **Lead with the state or the action**, then the cause, then the way on (Not Sent · Answer Changed · Reread).
5. **Buttons** begin with a verb, in Title Case, in one to three words. A confirmation is
   "Confirm" beside Cancel, under the question that names the action ("Approve Request?"). An icon may
   stand before a button's words, never in their place.
6. **Use familiar names for the work and its actor.** Projects, Tasks, Approvals, Activity, Changes,
   Tests and Send Instruction say what the person can do. Agent and assistant are ordinary nouns;
   avoid forced pronouns and invented circumlocutions. Your computer identifies the connected host.
   Halcyonic identifies this workspace. Brands supplied as data remain outside text.
7. **No blame, and a next step.** Every refusal ends with what to do: "Start Failed · No Folder ·
   Choose Folder".
8. **The agent's words are its own**, marked as theirs and never restated as fact: tagged by their
   claim's chip ("Agent · Reported" in a section) and leaning. Since 2026-10-08 (the owner, ADR 0026)
   Halcyonic adds no quotes or leads such as "It says:" around them: they lean on a ground only
   Halcyonic draws wherever they meet our words, and they are cut first. The same holds for all text
   Halcyonic didn't write, a title, a name, a label, a source's sentence and the person's own typed or
   heard words included (lane V's ruling, 2026-10-08): it leans, never quoted, and where we can word it
   so it comes after our words. Applied by lane W's batch B (the table "Words from outside" below).
   Amended 2026-10-16 for the independent workspace candidate (ADR 0026): an agent's words attributed
   whole, every run theirs beside the chip and Halcyonic's quote mark, stand upright, and so does the
   person's ordinary instruction in its input, whose label says whose words they are. A line where
   Halcyonic's words lead into theirs, as "Failing: {x}", keeps its lean and ground beside the chip,
   so the chip is never the only mark.
9. **No exclamation marks, emoji, em dashes or capitals for emphasis.** Numerals for numbers.
10. **Practice, Demo, Recorded and Last known** are said wherever they apply, beside a state and
    never inside its word.
11. **No brands in our words.** App and model names arrive as data; Salidium and Seorak appear
    only in a provenance line.

## The word for each state

One word per state, the same on the badge, in the peek, in the workspace and in every list
(`StateLanguage.WordOf`). A sentence says the same state in full.

| State | Badge | In a line |
| --- | --- | --- |
| No run yet | Not Started | Not Started |
| Asked to start, not yet confirmed | Starting | Starting · Unconfirmed |
| Running | Running | Running · 2 Tasks |
| A test run in progress | Checking | Checking · Reported |
| An approval or a question waits | Needs You | Needs You · 1 Task; Needs You · {title} |
| The round ended | Round Ended | Round Ended · Ready to Review |
| The round ended and its checks did not pass | Checks Failed | Checks · 1 Failed · 23 Passed · Observed or Reported |
| The run failed | Failed | Round Failed · Write Instruction; where nothing can be told to it, Round Failed · Re-Add Task in Projects (the way on, never the agent app's error) |
| Stopped, as the agent confirmed | Stopped | Stopped · Confirmed |
| Halcyonic cannot see the work | Unknown | Status Unknown; with a known cause, Status Unknown · Agent App Unreachable |

The badges and lines follow the owner's voice standard (2026-10-30). The file and the workspace say
Round Failed, Status Unknown and, in Latest, Checking · Reported so; the menu's counts, lists and
sentences still read "2 tasks running" and the like until the menu's words move. A finished round never says Done or Complete. Checking is the agent's
report: wherever its line stands it says Reported, so it never reads as your computer's own check.

The state badge also counts what waits when more than one thing does ("Needs You · 2"). In
counts, "paused" covers tasks that ran and rest, neither running nor waiting for you; tasks never
started are "not started", the badge's own word ("3 tasks not started", "1 task paused, 2 not
started").

## The words a person sees

| Word | Means | Instead of |
| --- | --- | --- |
| project | One body of work, in one folder | |
| task | One durable workstream, with or without a spatial character | workstream, work item |
| work | What the agents do, in general | execution |
| agent | The AI working on a task; the character is the task, not the agent | runtime, bot |
| round | One stretch of the agent's work, until it stops to wait | turn |
| approval | A specific decision the agent requests | |
| question | Something the agent asks you | prompt |
| tests / checks | Tests when actual test evidence is available; checks for broader verification | |
| your computer | The host and its control plane | control plane, server, Mac |
| folder | Where a project's files live | location, directory, path |
| agent app | The runtime, such as OpenCode or Codex, named as data | runtime, adapter |
| sandbox | What keeps an agent app's commands to what it allows, said only where an approval can change that | seatbelt, permission profile |
| practice run | Work on the simulated runtime: nothing is built | mock, simulated |
| demo | The recorded demonstration | interactive example |
| assistant | The AI helping shape an idea; it builds nothing until a task starts | |

A person never reads: execution, journal, command, principal, scenario, capability, projection,
snapshot, workstream, access token (an access code, only where the person must act on one).

### The chip beside an answer line

Each line of an answer says how it is known with a chip; an observed fact, a measurement and
Halcyonic's own words go without one. The words below were settled by the coordinator on
2026-10-02 and put in the owner's voice standard on 2026-10-30; the old words are kept beside them
so no surface goes back to them. The kit's report chip is the agent's own (Agent · Reported).

| The line is | Chip | Instead of |
| --- | --- | --- |
| The agent's own words, whatever the source tagged them, and what it says is left: beside the chip, "To Do · {x}", "Failing · {x}", "In Progress · {x}", its words leaning, never quoted (2026-10-08; these mixed lines keep the lean on the independent candidate too, 2026-10-16); where no chip shows, "Failing · Agent · Reported · {x}" and the like | Agent · Reported | Agent says |
| A subagent's own words | Subagent · Reported | Subagent says |
| Words whose author the source does not know | Author Unknown | Author unknown; Quoted |
| A model's account of the work, after it | Explanation · Reported | Model explains; Explanation |
| The source's own reading of what it observed, not a fact | Inferred | |
| What the agent planned to do | Planned | |
| A question the agent asks, on a character's glance and as a lone question's heading, its words leaning on their ground (2026-10-08; in the voice standard's cadence, 2026-10-30) | Agent · Question | Agent asks; It asks: |

### Usage in the demonstration

While the recorded demonstration plays, Usage shows its recorded limits for one practice agent.
These words were settled by the coordinator on 2026-10-02 and put in the owner's voice standard on 2026-10-30.

| Where | Words |
| --- | --- |
| Each limit | Practice agent · 5-Hour Window, Practice agent · Weekly |
| Where they come from, in place of a source's name | Recorded for Demo · No Account |
| Under the limits, in place of the account note | Limits Part of Recording |
| A limit's Account fact, in place of who it belongs to | Part of Recording |

### The agent's question in a file

How a file's Waiting page pages the agent's question (ADR 0026), as settled by the coordinator on
2026-10-11 for the focused answer sequence and put in the owner's voice standard on 2026-10-30.
A locked control's reason names what to read or do, in 1 to 3 words where it can.

| Where | Words | Instead of |
| --- | --- | --- |
| The row at the end of a prompt's page of answers | More Choices; from the last page, First Choices | Next, the old panel's pager |
| The primary action after a prompt | Next Question; after the last, Review Answers | Your answers as a plain navigation row |
| The primary action on a long question | Next Part · 2 of 3; after the last, See Answers | On to the answers as a plain row |
| The question's progress and selection rule | Question 1 of 2 · Choose One; Question 1 of 2 · Choose Any; with pages, · Choices 1 of 2 after them | A disabled Send answer throughout selection |
| Choice-page position and navigation | Choices 1 of 2; More Choices; First Choices | Paging without a visible current position |
| Clipped long-question context | Read Question | A hidden target or no way to reread the question |
| An unavailable choice with a duplicated displayed name | Unavailable · Read Why; explanation title Duplicate Choices; Same Name · Not Selectable · {full conflicting words}; Back; Stop Task when offered | A selectable radio/checkbox on an ambiguous label or a Stop cue where no Stop control is offered |
| Empty custom-answer field | Answer with a keyboard; Hold to Talk with voice only | Promising an unavailable keyboard |
| Send Answer's reason while a long question isn't read to its end | Read Full Question | Open the question to read the rest. |
| Send Answer's reason while a chosen cut answer or a long typed one isn't read whole | Read Full Answer | |
| Send Answer's reason while a page of Your Answers isn't drawn | Read All Answers | |
| Review and editor part navigation | Next Part · 2 of 3; Previous Part; a part not yet read, Read This Part | Your answers as a plain paging row |
| Custom-answer navigation | Write Answer; once saved, selected Edit Answer with Selected | Type my answer styled as an offered choice |
| The custom-answer editor | Your Answer; Use Answer; Cancel | Saving or sending on keyboard Done or transcription |
| The saved custom answer | Selected Edit Answer control; Your Answer · {their words}, leaning, in editor and mandatory review | A duplicate preview row among the choices |
| A free-text-only question | Write Answer; Write Answer First | Choose one with no offered choices |

### Steering from a file

Activity shows recorded events and actual pending decisions or problems. The task's state already
appears above it: do not invent a latest event from that state or fill the log with "Nothing is
waiting for you." An empty log says "No Activity". A loading or unavailable-history
message takes its place when present. Agent messages retain their individual "Agent says" chip
and outside-text treatment; observed events are not labeled as agent claims. The mixed log has no
blanket agent source line. Waiting and request pages use "Reported" for their source.

**Write Instruction** opens the draft. **Review Instruction** opens its complete review, and
**Confirm** sends only after that review. A transcribed draft asks "Send Heard Instruction?";
a typed draft asks "Send Instruction?". These labels do not change capability, complete-reading or
current-request checks.

| Where | Words |
| --- | --- |
| Review Instruction while offered instructions show and none is chosen | Choose Instruction (locked) |
| A confirmation dropped because the request it asks about now reads differently | Not Sent · Request Changed · Reread |
| Answers awaiting their Confirm, dropped because the question is no longer the one asked | Not Sent · Question Changed · Reread (instead of Check it again) |

### The headset's access code

Where a person must act on the development build's access token, it is the access code. These
lines were settled by the coordinator on 2026-10-02; the old words are kept beside them so no
surface goes back to them.

| When | Words | Instead of |
| --- | --- | --- |
| Your computer answered 401 to the code | Your computer refused this headset's access code: it doesn't match your computer's. Put your computer's current access code on the headset, then restart the app. | Your computer refused this headset's access token: it doesn't match your computer's. Put your computer's current access token on the headset, then restart the app. |
| What answers can't prove it holds the code, so the headset didn't send it | This headset's access code doesn't match your computer's, or something else is answering in its place, so the headset didn't send it. Put your computer's current access code on the headset, check that this app is running there, and restart the app. | "check that Halcyonic is running there", since our words never name the product (the coordinator, 2026-10-07) |
| The endpoint is one the code never goes to, a name or another scheme (an editor setting) | The access code goes only to ws:// or http:// at 127.0.0.1 or [::1], so it was not sent to {endpoint}. Name one of those instead. | The access code goes only to 127.0.0.1 or [::1], so it was not sent to localhost:47800. Name one of those instead. |

## Sentence patterns

Halcyonic's own lines read in the " · " cadence (the owner's voice standard, 2026-10-30): the state
or the action first, then the cause, then the way on, each segment in Title Case; words from outside
stay exactly as written and lean.

| Kind | Pattern | Example |
| --- | --- | --- |
| A peek | {State} · {reason}, the reason without the state's words | Needs You · make migrate |
| Nothing waiting | Nothing Waiting, then Latest · {activity}, a name or the agent's message in it leaning, the agent's beside its chip | Nothing Waiting · Latest · Used edit: src/auth/rate-limit.ts |
| Sent | Sent · Awaiting {Who} | Sent · Awaiting Agent |
| Still on its way | Sending, unavailable, where the action that sent it stood | Sending |
| Confirmed | Confirmed · {What} | Confirmed · Answer Received |
| Refusal | {Action} Refused or Failed · {Cause} · {Next Step}, the next step a control's name where one is offered | Start Failed · No Folder · Choose Folder |
| Unknown effect | {What} Unconfirmed · {What to Check} | Delivery Unconfirmed · Check Activity |
| Not sent | Not Sent · {Why} · {Next Step}, wherever a press sent nothing | Not Sent · Answer Changed · Reread |
| Connection | {Truth} · {Cause} · {What Happens} | Computer Unreachable · Retrying |
| Provenance | {Source} · {When} | Seorak · 2 min ago |
| The work's reported request or state | Reported, on Waiting and request pages; Activity identifies each reported message separately | Reported |
| Confirmation | Confirm, where no control stood a moment before, under the question that names the action; Cancel where the first press was | Approve Request? · Confirm · Cancel |
| Asking about what shows | {Verb} {Thing}? | Approve Request? |
| Where an approved command runs | May Run Outside Sandbox · Your Access, or Runs Inside Sandbox, only for a command that can be approved and only as its agent app declares it, before what Approve does ([ADR 0033](../decisions/0033-approvals-declare-whether-commands-leave-the-sandbox.md)) | May Run Outside Sandbox · Your Access |
| Locked final press | Read to Part {n} (locked) | Read to Part 3 |
| Kept while away | Open · {Panel} | Open · New Project |
| The companion's words | {line}, leaning on its ground under Halcyonic's quote mark, never in our voice; the page's source line names the companion; its view as its opinion, in our words | The companion thinks this is unclear. |

The file, its question and its steering, the workspace's answers and refusals, voice, and since
2026-10-30 the menu's own lines (New project, Projects, Connect, the companion, the activity log,
Settings, Usage, results, workspace requests and recovery, and the reasons Help me understand and
What was checked? give) follow these patterns.

Some of Halcyonic's words still predate this guide; each surface takes these words as it moves to
the interface of ADR 0023, one surface at a time. The ambient lines and the answers' own body lines
(as "And 2 more reasons it gave") have not moved yet.

## Words changed by the menu

The menu of [ADR 0026](../decisions/0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md)
replaces the entry panel's screens. Routine wording, settled by the coordinator, old beside new:

| Where | Old | New | Settled |
| --- | --- | --- | --- |
| A file's instruction armed to send, heard through Hold to talk or typed for a policy's review | The words above (since 2026-10-08 leaning, never quoted), in parts as a request is, each but the last ending in "Next part, 2 of 3", then "Read to part 3 first" until the last part has shown, then "Your computer heard the words above. Send them?" or "Tell it the words above?"; a Yes refused before then, as a backstop, says "Nothing was sent: read to the last part first." | The words above, leaning, in parts as a request is, each but the last ending in Next Part · 2 of 3, Read Full Instruction until the last part is read, then Send Heard Instruction? or Send Instruction?; a press before then, Not Sent · Read to Last Part | The owner's voice standard, 2026-10-30 (ART's form) |
| A file's question offering two answers by one label, as "Yes" to keep the data and "Yes" to delete it | "Two of its answers read the same, so your choice can't be sent from here. Press Stop to go on.", then "It's waiting for an answer." and Stop, as a question asking for a secret shows; no answer rows and no Hold to talk | Duplicate Answers · Not Answerable Here · Stop Task, then Awaiting Answer and Stop, as a question asking for a secret shows | The owner's voice standard, 2026-10-30 (ART's form) |
| A file's question where some answers read the same, as "Yes" to keep the data and "Yes " to delete it, beside other ways to answer | Those rows show and can't be chosen; under them "Answers that read the same can't be chosen here. Press Stop if you meant one of them.", with Stop on the page (without Stop to offer, the first sentence alone); words typed or heard that read the same as one of them, a notice: "Your answer reads the same as answers that can't be chosen here. Answer another way, or press Stop if you meant one of them." ("Answer another way." where Stop isn't on the page); a prompt left with no way to answer keeps the row above | Those rows show and can't be chosen; under them Duplicate Answers · Not Selectable · Stop Task where Stop is offered; words typed or heard that read the same, Matches Duplicate Answers · Answer Differently or Stop Task | The owner's voice standard, 2026-10-30 (ART's form) |
| Why it can't tell what a task is doing, on the character, its peek and the file's Activity | By the reason's code: "Can't tell what it's doing: your computer lost touch with the agent app.", "…: your computer restarted and lost touch with the agent app.", "…: not sure it started."; any other code "Can't tell what it's doing right now."; the log's line for a lost connection "Your computer lost touch with the agent app." | By the reason's code: Status Unknown · Agent App Unreachable, Status Unknown · Computer Restarted, Status Unknown · Start Unconfirmed; any other code, Status Unknown | The owner's voice standard, 2026-10-30 (ART's form) |
| Why a task couldn't start or finish, on the character, its peek and the file's Activity | "Couldn't finish this round. Tell it to try again, or what to do instead." where Tell it is offered, else "Couldn't finish this round. Add the task again in Projects to try again."; a start refused over its folder, "Couldn't start: {the folder's problem, by its code}"; a start that failed with a code every agent app shares, "Couldn't start: {why}" as the next rows say it (a lost agent app, one not available, a version not supported, one that can't do that); any other start, "Couldn't start. Add the task again in Projects to try again." | Round Failed · Write Instruction where Write Instruction is offered, else Round Failed · Re-Add Task in Projects; a start refused, Start Failed · {why by its code}, as Start Failed · Agent App Unavailable · Check Setup · Re-Add Task; stopped because its permissions changed, Round Failed · Task Stopped · Permissions Changed · See Activity | The owner's voice standard, 2026-10-30 (ART's form) |
| A peek's next step under Couldn't finish | "Open it to see what it did.", since the file no longer shows the agent app's error | Open Task (Failed); Open to Answer (Needs You); See Failures (Checks Failed); See Changes (Round Ended) | The owner's voice standard, 2026-10-30 |
| The activity log's lines for a round and for what failed | "Round started", "Round finished", "Round stopped", "Couldn't finish this round", "Couldn't start", "Couldn't start: {the folder's problem}" or "Couldn't start work: {why}" by a shared code, "Couldn't {verb}" (as "Couldn't send an instruction") where it had no effect, and where it may have happened anyway what may have, then what to check, never after "Couldn't", which says nothing happened: "Not sure the instruction reached it.", "Not sure it stopped.", "Not sure it has your decision.", "Not sure it started.", each then "Check its activity before you try again."; an answer as before; anything else "Not sure it happened. Check its activity before you try again." (the same on a task's line of what was sent last; settled by the coordinator, 2026-10-04); and "Asked to stop it" | Start Requested · {app}; Started · {app}; Round Started; Round Ended; Round Stopped; Round Failed; Start Failed; Start Failed · {why}; {Action} Sent; {Action} Refused · {why}; {Action} Failed · {why} | The owner's voice standard, 2026-10-30 |
| A command your computer refused, in the activity log, the file's line of what was sent last, and Start building's and Connect's steps | "Couldn't {verb}: {why}." by the refusal's code: "this project isn't on your computer any more. Choose another in Projects.", "this task isn't on your computer any more. Choose another in Tasks.", "this work isn't on your computer any more. Add the task again in Projects to try again.", "its agent app isn't on your computer now. Set it up there, then try again.", "it no longer waits for that decision. See what it's doing now.", "it's no longer waiting for this answer. See what it's doing now.", "it couldn't take that answer. Read the question again, then answer it.", "its agent app needs a model. Choose one, then try again.", "its agent app can't do that. See what it's doing, then try something it offers.", "it can't take that right now. See what it's doing, then try again.", "its agent app didn't accept those settings. Choose them again.", a folder's problem as before, the pairing line, "your computer no longer accepts this headset's pairing. Forget the computer on the headset and pair again.", one sentence for a revoked pairing wherever it is said (the last three ways on settled by the coordinator, 2026-10-04); a failure with no effect whose code every agent app shares, in words already used for it: approval_not_pending and question_not_pending as their refusals, no_running_turn as invalid_state, execution_unknown_to_runtime as execution_not_found, model_unavailable as model_required, capability_unimplemented as capability_unsupported where a task runs, and runtime_unreachable or runtime_closed "your computer lost touch with the agent app. See what it's doing; if it can't go on, add the task again in Projects." where a task runs, since one code covers a blip and an ended process; where nothing runs yet (a start, in the log and on the line of what was sent last alike) "…lost touch with the agent app. Check that the agent app is running on your computer, then try again.", and a start that would fail the same way again by its way on: runtime_unavailable "the agent app isn't available on your computer. Check its setup there, then add the task again in Projects.", runtime_version_unsupported "this agent app's version on your computer isn't supported yet. Add the task again in Projects with another agent app.", capability_unimplemented "its agent app can't do that. Add the task again in Projects with another agent app." (2026-10-07); Start building's and Connect's own steps by the ways on New project offers, never adding the task again, each within its two rows: runtime_unreachable or runtime_closed "your computer lost touch with the agent app. Check it's running there, then try again.", runtime_unavailable "the agent app isn't available on your computer. Check its setup, then try again.", runtime_version_unsupported "this agent app's version isn't supported yet. Choose another in How it runs.", capability_unimplemented "its agent app doesn't support it. Choose another agent app in How it runs, then try again." (2026-10-07, after review); any other, "Couldn't do that: nothing changed. Try again." on Start building's and Connect's steps and for a start, and on a task's line of what was sent last "Couldn't do that: nothing changed. See what it's doing, then try again." | {Action} Refused · {Cause} · {Next Step}, by the refusal's code, as Refused · Project Missing · Choose Another; Connect Refused · Task Missing · Choose Another | The owner's voice standard, 2026-10-30 |
| Why the headset isn't connected, after "Can't reach your computer; trying again." or "Your computer refused this app." | "Your computer didn't answer within 10 seconds.", "Your computer closed the connection. The headset tries again by itself.", "Your computer runs another version of this app. Install the same version on both.", by the code your computer turned it away with: the same for another version, the pairing line for a revoked pairing, "This headset already has too many connections open to your computer. Close the app, then open it again.", "Your computer couldn't read what this app sent. Install the same version on both.", else "Your computer ended the connection. The headset tries again by itself."; "Your computer sent something this app can't read. Install the same version on both.", "Your computer sent nothing for 30 seconds. The headset tries again by itself.", "This headset fell behind, so it's catching up." | Computer Unreachable · No Answer in 10 s; Connection Closed · Retrying; Connection Ended · Retrying; Version Mismatch · Install Same Version; Message Unreadable · Install Same Version; Too Many Connections · Restart App | The owner's voice standard, 2026-10-30 |
| Why the headset isn't connected, when your computer refuses the connection's upgrade for another reason than its credential | By its code, on its own and never after "Can't reach your computer", since your computer answered: host_not_allowed, which only the network listener answers and a stored pairing passed once, "Your computer turned away the address this headset uses for it, which may have changed there. The headset tries again by itself; if this keeps happening, pair it again in Settings."; too_many_requests "Your computer is turning this headset away for a minute after too many tries. The headset tries again by itself."; any other, worded for how this headset reaches your computer: paired, "Your computer turned the connection away. The headset tries again by itself; if this keeps happening, pair it again in Settings."; over USB, "...; if this keeps happening, restart this app on your computer." (a revoked pairing comes as 401, a refused credential, never here) | By its code: Host Not Allowed · Retrying · Pair Again if Repeated; Computer Busy · Too Many Tries · Retrying; otherwise Connection Refused · Retrying | The owner's voice standard, 2026-10-30 |
| A file's Send answer, armed, when words heard or typed change the answer | "Nothing was sent: your answer changed. Read it again.", the confirmation cancelled | Not Sent · Answer Changed · Reread, the confirmation cancelled | The owner's voice standard, 2026-10-30 (ART's form) |
| A file's Hold to talk while the voice records, then while the computer writes down what was said | "Listening", then "Writing down", on Hold to talk itself, laid at the widest of its words; nothing is added to the page; since 2026-10-07 in every column, New project's too (ADR 0027) | Listening, then Transcribing, on Hold to Talk itself, laid at the widest of its words; nothing is added to the page | The owner's voice standard, 2026-10-30 (ART's form) |
| A file's Hold to talk, its words heard after the question it was held for gave way to another | "Nothing was typed: the question changed while you spoke. Read it again.", the words dropped | Not Typed · Question Changed · Reread, the words dropped | The owner's voice standard, 2026-10-30 (ART's form) |
| A file's Send answer, armed, when the text size changes | "Nothing was sent: the text size changed. Read it again.", as the size changes, the confirmation cancelled | Not Sent · Text Size Changed · Reread, as the size changes, the confirmation cancelled | The owner's voice standard, 2026-10-30 (ART's form) |
| The stage's banner while the menu is folded away | "Still open: {task title}" while a task's file is open, "Still open: New project" while New project is, else the menu's place, as "Still open: Tasks"; nothing with only the closed bar | Open · {task title} while a task's file is open, Open · New project while New project is, else the menu's place, as Open · Tasks; nothing with only the closed bar | The owner's voice standard, 2026-10-30 (ART's form) |
| The menu's row in Settings after the person dragged it | "Where you moved it", as its value and what it is now, until Reset position puts it back, then "Where it stands" again | Moved, its now Position · Moved, until Reset Position puts it back, then Default (Position · Default); the change Back in Front · Within Reach | The owner's voice standard, 2026-10-30 |
| Tasks with no tasks | "No tasks yet.", a quiet line on the page under the subject, which reads "Nothing is waiting for you." | No Tasks; subject Nothing Waiting, or Needs You · 1 Task | The owner's voice standard, 2026-10-30 |
| Projects' subject | "Welcome", then "Show projects from your computer, or make a new one. Work already running keeps going." on a first visit; "Connect projects" otherwise | "What would you like to work on?" on every visit, the place's purpose under the lit place's word; it keeps to one line (21.9 of 29.2 degrees), so the fallback "What do you want to work on?" was not needed | 2026-10-02, by the coordinator, every visit by lane V; the stage shows that running work goes on |
| A project's small fact on its row | "Hidden · 1 task waiting", "1 task waiting", "2 tasks running": a count keeps its noun, and a long name shortens first | Needs You · 1 Task; Ready · 1 Task; Running · 2 Tasks; Paused · 1 Task; Not Started · 3 Tasks; Hidden · {fact}; Similar Name · {fact} | The owner's voice standard, 2026-10-30 |
| A project's small fact on its row, and its work in its side panel, where tasks were never started | "3 tasks not started"; tasks that ran and rest first, as "1 task paused" on the row and "1 task paused, 2 not started" in the side panel, since a task never started has paused nothing and its label says Not started | Not Started · 3 Tasks on the row; in the side panel, Paused · 1 · Not Started · 2 | The owner's voice standard, 2026-10-30 |
| A project's work in its side panel, "Its work" | Every task, in order: waiting for you, to look at, running, paused, not started, the noun on the first count only: "2 tasks running, 1 not started", "1 task waiting for you, 2 paused", at most "1 task waiting for you, 1 to look at, 1 running, 1 paused, 1 not started"; the row keeps one fact by priority | Needs You · 1 · Ready · 1 · Running · 2 · Paused · 1 · Not Started · 1; No Work | The owner's voice standard, 2026-10-30 |
| Projects' folders | "Folders on your computer", a heading in Projects; a folder's row says "Repository · changed 3 days ago" | Folders; Repository · Changed 3 Days Ago; Changed Just Now; Contents Unreadable | The owner's voice standard, 2026-10-30 |
| Projects' prompts | "New project" with no row chosen; for a chosen project "Add a task" and "Hide from stage" or "Show on stage" (first "Hide from the stage", which needs 31.4 of the column's 30.3 degrees; "Hide from stage" needs 29.3, and says where the tasks go, since Tasks still lists them); for a chosen folder "Connect"; a list that pages "Next page", which on its last page reads "First page" and starts again; Close does what Done did | New Project; Add Task; Hide from Stage / Show on Stage; Connect Folder / Back to Projects | The owner's voice standard, 2026-10-30 |
| A side panel's facts in Projects | A project: "Its work", "On the stage" ("Shown", "Hidden"). A folder: "Place", "Repository" ("Yes", "No"), "Changed", "Its name" for a look-alike, then "Connecting", or "What happened" once sent; "Your computer can't look inside it" where it couldn't | Work; On Stage (Shown, Hidden); Place; Repository (Yes, No); Changed; Name; Connecting | The owner's voice standard, 2026-10-30 |
| Projects, when no folder can be listed | A row that opens its side panel: "Couldn't read your computer's folders", its panel's "What happened" over the reason; or "Your computer doesn't allow any folder yet", its panel saying "Allow a folder on your computer, then try again."; "Try again" is the footer's main action while either is chosen, since a row never acts | Folders Unreadable; No Folders Allowed · Allow on Computer · Try Again | The owner's voice standard, 2026-10-30 |
| Projects, after the security review | "1 task to look at" (row) and "1 task waiting for you, 1 to look at, 2 running" (side panel); "Not sure whether “x” was connected. Look for it in Projects."; while it is on its way, "Still waiting to hear whether “x” was connected. Connecting another folder waits until that's known."; once its outcome can't be known, "Not sure whether “x” was connected. Look for it in Projects. Connecting another folder waits until that's known, or until you restart."; "Another folder or project has a name that looks the same. Check where it is and when it changed to be sure it's the one you mean."; a project's "Its name": "Another project or folder has a name that looks the same. Check its work to be sure it's the one you mean."; the mark "Look-alike name", before a look-alike row's own facts | Connected · {name} · Add Task; Connect Unconfirmed · {name} · Check Projects | The owner's voice standard, 2026-10-30 |
| New project's Questions: the main prompt while Go on without it is chosen, making the recap from the person's own words without the companion | Make the recap from my words; Recap from my words where the footer measures too narrow beside Close and Hold to talk | Recap My Words | The owner's voice standard, 2026-10-30 |
| New project's Your idea: the row for the fixed questions | Answer a few questions | Answer Questions | The owner's voice standard, 2026-10-30 |
| New project's Your idea: the main prompt while Talk it through with the companion is chosen | Talk it through | Talk It Through | The owner's voice standard, 2026-10-30 |
| New project's Your idea: the main prompt while Answer a few questions is chosen | Start the questions | Start Questions | The owner's voice standard, 2026-10-30 |
| New project's Questions: why Make the recap can't be pressed before the person has said anything | Answer a question first, or go on without it. | Answer One First (locked) | The owner's voice standard, 2026-10-30 |
| New project's Recap: the answer that types a first task in place of the companion's suggestion and the person's own words | Type my own | Type My Own | The owner's voice standard, 2026-10-30 |
| New project's Recap: the small fact beside a fact the companion suggested | Suggested, at the row's right, and nothing once the value is the person's own; Suggested by the companion names the suggestion in its side panel | Suggested · Companion | The owner's voice standard, 2026-10-30 |
| New project's Recap: the name over the person's own words in a suggested first task's side panel | Your own words | Your Words | The owner's voice standard, 2026-10-30 |
| New project's Start building: the review's way to its next part | Next part, 2 of 3, a row at the end of each part; Yes, start building appears only once the last part has shown | Next Part · 2 of 3; Confirm after the last part | The owner's voice standard, 2026-10-30 |
| New project's fixed questions: the main action, which gives the chosen answer | Next question; Make the recap on the last question | Next Question; Make Recap | The owner's voice standard, 2026-10-30 |
| New project's fixed questions: why Next question can't be pressed yet | Choose or type an answer first. (a file's question's own words, reused) | Choose or Type First (locked) | The owner's voice standard, 2026-10-30 |
| New project's fixed questions: the name question's skip, its last answer row | Name it later, kept over Go on without it, which under "What should it be called?" reads as going on without a name for good | Name Later | The owner's voice standard, 2026-10-30 |
| New project's Start building, when the project a task was being added to has gone | This project isn't on your computer any more. Close this, then choose another in Projects. | Project Missing · Choose Another | The owner's voice standard, 2026-10-30 |
| Settings' Your space and Your computer | Rows: "Around you" ("Your room", "Virtual space"), "Your room's layout" ("No access", "Not set up", "Ready"), "The characters" ("In front of you", "Room for a window", "Either side of a window"), "The menu" ("Where it stands", its change "Reset position"), and in development builds "Pairing" ("Not paired", "Pairing…", "Paired", "Forgetting…"); each change keeps the old button's words, and Forget asks with "Yes, forget this computer" | Sections Your Space and Your Computer. Rows: Surroundings (Your Room, Virtual Space), Room Layout (No Access, Not Set Up, Ready, Not Read), Agents (On, Off), Agent Shelf (Placed, Not Placed; Move Shelf or Place Shelf), Menu (Default, Moved; Reset Position), and in development builds Pairing (Not Paired, Pairing, Paired with Paired · {address}, Forgetting; Pair Computer, Forget Computer); Forget asks with Confirm | The owner's voice standard, 2026-10-30 |
| Settings' category index | "Appearance", "Comfort", "Your space" and build-supported "Your computer", each with its current value; "Back" returns to categories | Appearance, Comfort, Your Space and build-supported Your Computer, each with its current value; Back returns to categories | The owner's voice standard, 2026-10-30 |
| Appearance choices and preview | "How should it look?", "Glaze", "Graphite", "Current", "Preview theme", then "Preview: {theme}", "Apply theme" and "Cancel"; "Next examples, {part} of 5" or "First examples" pages the labelled specimen | Appearance, its presets Glaze and Graphite, Current on the applied one, Preview Theme, then Preview · {theme}, Apply Theme and Cancel; Next Examples · {part} of 5 or First Examples pages the labelled specimen | The owner's voice standard, 2026-10-30 |
| Restoring appearance | "Restore default appearance", then "Restore default appearance?", "Use Glaze for the interface.", "Your comfort and space stay as you set them.", "Restore default" and "Cancel" | Restore Default, then Restore Default?, Glaze Interface, Comfort Kept · Space Kept, Restore Default and Cancel | The owner's voice standard, 2026-10-30 |
| Settings, while the room's controls aren't there, before they start or once they have gone | "Not known" on Around you, Your room's layout and The characters, each saying "Your room can't be read now."; The menu and its Reset position stay | Unknown on Surroundings, Room Layout and the characters' row, each saying Room Unreadable; Menu and its Reset Position stay | The owner's voice standard, 2026-10-30 |
| Settings' Pairing, in a development build, while the pairing isn't there yet or has gone | "Not ready", saying "Pairing isn't ready yet." | Not Ready, its reason Not Ready | The owner's voice standard, 2026-10-30 |
| Settings' Boundary, in a development build, Your space's row before The menu | "Shown" ("The headset draws its boundary as you set it up"; change "Hidden while your room shows", "The headset stops drawing its boundary while your room shows. It comes back when your room stops showing, when Halcyonic pauses or loses focus, or when you choose Show boundary"; "Hide boundary"; while the room isn't showing, "Only while your room shows."), "Asked to hide" ("The headset hasn't said it hid its boundary yet"), "Hidden" ("Your boundary is hidden while your room shows"), each with change "Shown", "The headset draws its boundary again", "Show boundary"; "Kept on" ("The headset kept its boundary on. Halcyonic asks again while your room shows"; "Halcyonic stops asking", "Stop asking"); once a session, the first time the headset reports it hidden, the workspace says "Your boundary is hidden while your room shows. Settings, Your space brings it back." | Shown (Drawn as Set Up; change Hidden While Room Shows, Returns When Room Stops or App Pauses, Hide Boundary; while the room isn't showing, Only While Room Shows), Hide Requested (Hide Unconfirmed), Hidden (Hidden While Room Shows), each with change Shown, Boundary Returns, Show Boundary; Kept On (Headset Kept It On · Asks Again; Stops Asking, Stop Asking); hidden is said only once the headset reports it | The owner's voice standard, 2026-10-30; hidden is said only once the headset reports it |
| New project's last step, in its row of shapes | Build, since at 36 degrees the old words filled the step's shape edge to edge; the recap's main action stays Start building, and the confirmation Yes, start building | Start Task | The owner's voice standard, 2026-10-30 |
| New project's Starting, where it needs more than the stage gives and its secondary place holds its change: the row at its end | Next page, 2 of 3; First page on the last, as the menu's pager and the review's row say; New project's lists page by the footer's Next page and its questions' answers by More answers rows, the menu's own words | Next Page · 2 of 3; First Page | The owner's voice standard, 2026-10-30 |
| New project's unknown start, read in parts where it needs more than the stage gives: Clear's reason until its last part is drawn | Read to part 3 first, as the review's locked Yes says; each part but the last ends in Next part, 2 of 3 | Read to Part 3 (locked) | The owner's voice standard, 2026-10-30 |
| New project's unknown start, when its words change while Yes, clear waits, as when the computer's record arrives | Nothing was cleared: this changed while you were clearing it. Read it again. As "Nothing was sent: the request changed. Read it again." says; the Yes lapses, and the page shows the first part with anything unread | Not Cleared · Changed · Reread | The owner's voice standard, 2026-10-30 |
| New project's Recap: Start over | Start over, the recap's last row, on its last page, chosen as a fact is; chosen, Start over stands in the footer's middle and asks, over Yes, start over | Start Over; Confirm | The owner's voice standard, 2026-10-30 |
| New project's Recap: what Start over clears, in its side panel and its confirmation alike | For a new project: "This clears your idea, its answers, the name, first task and folder. How it runs stays." For a task added to a project, which keeps its name and folder: "This clears your idea, its answers, the first task and any change of folder. The project and how it runs stay." | Clears · Idea · Answers · Name · First Task · Folder · Keeps · Run Settings; a task added to a project: Clears · Idea · Answers · First Task · Folder Change · Keeps · Project · Run Settings | The owner's voice standard, 2026-10-30 |
| New project's Recap: the companion's note on a side panel showing its suggested name or first task | An AI on your computer suggested this. It can be wrong. The page keeps the whole note, and the side panel's Change stands beside it | Suggested · Local AI · May Be Wrong | The owner's voice standard, 2026-10-30 |
| New project's How it runs, its models: another agent app | Change agent app, an action row before the models, as Type my answer is, so the footer's middle is free for Next page | Change Agent App | The owner's voice standard, 2026-10-30 |
| New project's How it runs, its models, when they couldn't be read | By the reason's code: a version not supported, "Couldn't read its models: this agent app's version on your computer isn't supported yet. Choose another."; an agent app that didn't start or answer (or timed out, or exited), "Couldn't read its models: the agent app didn't answer. Check its setup on your computer, then choose it again."; any other code or a request that failed, "Couldn't read its models. Choose the agent app again to retry."; not connected, "Couldn't read its models: your computer isn't connected. Choose the agent app again when it is." | By its code: Models Unreadable · Version Unsupported · Choose Another; Models Unreadable · Agent App Silent · Check Setup; Models Unreadable · No Answer · Choose Again to Retry; Models Unreadable · Not Connected · Retry When Connected | The owner's voice standard, 2026-10-30 |
| Why your computer's folders couldn't be read, on New project's Where its files live and in Projects | The same in both: "Couldn't read your computer's folders." (Projects' row and side panel heading), then why on its own line (Projects' What happened): "It didn't answer. Check that this app is running there, then press Try again." for a request with no reply; the pairing line for a revoked pairing; for a refused credential, the sentence the connection says for how this headset reaches your computer (the access code's or the pairing's); "Your computer is turning this headset away for a minute after too many tries. Press Try again after a minute."; not connected, "Your computer isn't connected. Press Try again when it is."; anything else, "Press Try again." (in Projects a line, not What happened) | Folders Unreadable; No Answer · Check App · Try Again; Host Unverified · Code Not Sent · Check App · Try Again | The owner's voice standard, 2026-10-30 |
| Why the headset isn't connected, while it tries again | "Can't reach your computer; trying again. Check that this app is running there and this headset can reach it.", as "Your computer runs another version of this app" names the computer's side | Computer Unreachable · Retrying · Check App and Network; Last Known · Computer Unreachable · Retrying | The owner's voice standard, 2026-10-30 |
| Why the headset isn't connected, when nothing that is your computer answered | The sentence alone, the message in the device log only; for a certificate other than the paired one, after it, "What answered isn't the computer this headset paired with, so the headset sent nothing. If you set up your computer again, forget it on the headset and pair again." | Unknown Host · Not Your Computer · Nothing Sent | The owner's voice standard, 2026-10-30 |
| Pairing that didn't complete, on the stage's banner | By its code, never what answered: too_many_requests "Your computer is turning this headset away for a minute after too many tries. Try again after a minute."; busy "Your computer is pairing another device right now. Try again in a moment."; timeout "Pairing took too long; try again."; any other refusal "Your computer refused to pair this headset. Open pairing there again, then try again."; a broken exchange "What answered at that address doesn't pair the way your computer does. Check the address, then try again."; something at the address that answered but set up no secure connection (not_secure) "Something at {address} answered but didn't set up a secure connection. Check the address and port; if they're right, try again in a minute.", where a name that doesn't resolve or a port nothing takes keeps "Nothing answered at {address}. …"; a connection that closed or failed partway (connection_lost) "The connection closed before pairing finished. Check the address and that pairing is open on your computer, then try again."; server_not_proven "What answered couldn't prove it knew the code, so nothing was paired. Check the address, then try again."; anything else "Couldn't pair this headset. Check the address, then try again." | By its code: Pairing Paused · Too Many Tries · Retry in 1 min; Computer Busy · Retry; Pairing Closed · Run pnpm pair · Try Again | The owner's voice standard, 2026-10-30 |
| A folder's problem, wherever it is said (the character, the log, Start building's and Connect's steps) | "Your computer can't use that folder now. Choose it again, or fix it on your computer."; "This project has no folder yet. Choose where its files live, then try again."; a folder that couldn't be made, with no effect, "Your computer couldn't make that folder, so nothing changed. Choose another name or place."; the pairing line alone; every composed step line within New project's 110 characters | No Folder · Choose Folder; Folder Unusable · Choose Again or Fix on Computer; Folder Not Allowed · Choose Listed Folder; Folder Exists · Use It or Rename; Folder Not Made · Nothing Changed · Choose Another | The owner's voice standard, 2026-10-30 |
| "Didn't answer" only where nothing answered: the folders' why and the models step | Folders by the inner error: a certificate other than the paired one, the connection's "What answered isn't the computer this headset paired with..."; an access code not sent for lack of proof, "What answered couldn't prove it holds the access code, so the headset sent nothing. Check that this app is running there, then press Try again."; an endpoint the code never goes to, an answer that isn't HTTP or is too large, and a connection something took without setting up a secure one, "Press Try again."; F1 only for a socket error, a proof over USB that nothing answered, or the headset's own timeout. Models, the headset's 40 s: "Couldn't read its models: your computer didn't answer in time. Choose the agent app again to retry." | No Answer only where nothing answered; otherwise the code's own reason | The owner's voice standard, 2026-10-30 |
| A task its agent app's adapter stopped because something other than Halcyonic changed what it may do (runtime_tampered): a start, a round, the activity line | "Something on your computer changed what this task was allowed to do, so it was stopped. See what it ran in Activity.", after "Couldn't start:" or "Couldn't finish:"; never on New project's steps, which no start command fails with | Task Stopped · Permissions Changed · See Activity | The owner's voice standard, 2026-10-30 |
| Halcyonic's words beside a name or a reason from outside, which an ellipsis could cut | "{name} (simulated)", "{current} now, {new place} from now on", "{reason} (+1 more)" | "Simulated: {name}", "From now on, {new place}; {current} now", "+1 more · {reason}": Halcyonic's words first | 2026-10-08, by the coordinator |
| Where no check was seen or nothing was measured and the source's own sentence shows as nothing | "Nothing measured." | Not Measured | The owner's voice standard, 2026-10-30 |
| Why Help me understand or What was checked? has nothing: a source that answered without conclusions, and this headset's own read that failed | The lead, then why by the reason's code, the source named only in the provenance line: not_running "it isn't running on your computer. Start it there, then press Refresh."; unreachable, timed_out "it didn't answer on your computer. Press Refresh in a moment."; temporarily_unavailable, server_error "it couldn't answer just now. Press Refresh in a moment."; rate_limited "it asked for a pause after too many requests. Press Refresh a little later."; credential_missing "your computer has no credential set up for it. Set one up there."; credential_malformed "the credential set up for it on your computer isn't the right kind, so it wasn't sent. Set up another there."; credential_rejected "it turned down the credential set up on your computer. Set up a new one there."; credential_forbidden, insufficient_scope "the credential set up on your computer can't read this. Set up one that can."; outside_credential_restriction "the credential set up on your computer covers other projects or dates. Set up one that covers this task."; not_captured, not_observed "it hasn't seen this task yet. Press Refresh in a moment."; not_yet_computed "it hasn't worked this task out yet. Press Refresh in a moment."; not_retained "it no longer keeps this task, so there's nothing more to read."; runtime_not_observed "it doesn't follow tasks this agent app runs."; not_resolvable, not_observable "it can't look up this task by the name its agent app gave it."; result_limit "it stopped at a limit before it found this task. Press Refresh to try again."; a reader's or the control plane's incompatible codes "its answer isn't one this app can read. Check its version on your computer."; host_not_allowed, origin_not_allowed "it turned the request away on your computer. Check its setup there."; instance_mismatch "what answered isn't it. Check its setup on your computer."; discovery_unreadable "its setup on your computer can't be read. Check it there."; native_id_unknown, naming no source, "the agent app hasn't said which session this is yet. Press Refresh in a moment."; any other "it didn't say why. Press Refresh to try again." This headset's read: "Couldn't read the evaluation: ", "Couldn't read the understanding: " or " · couldn't read it again: ", then "your computer didn't answer in time. Press Refresh to try again.", "your computer didn't answer. Press Refresh to try again.", "your computer isn't connected. Press Refresh when it is.", "your computer is turning this headset away for a minute after too many tries. Press Refresh after a minute.", the pairing line, the refused credential's line for how this headset reaches the computer, the whole certificate line, "what answered couldn't prove it holds the access code, so the headset sent nothing. Check that this app is running there, then press Refresh.", the unreadable line, else "something went wrong. Press Refresh to try again."; a sentence reused after a colon with its first letter lowered | The lead in the cadence, then why by the reason's code, the source named only in the provenance line (Salidium · or Seorak · before the lead). Leads: No Conclusions, Conclusions Unavailable, Conclusions Unreadable, Conclusions Not Allowed; for a measurement Not Measured, Measurement Unavailable, Measurement Unreadable, Measurement Not Allowed. Why: not_running Not Running · Start It · Refresh; unreachable, timed_out No Answer · Refresh Soon; temporarily_unavailable, server_error Busy · Refresh Soon; rate_limited Too Many Requests · Refresh Later; credential_missing Credential Missing · Set Up on Computer; credential_malformed Credential Wrong Kind · Not Sent · Set Up Another; credential_rejected Credential Refused · Set Up New One; credential_forbidden, insufficient_scope Credential Lacks Access · Set Up Another; outside_credential_restriction Credential Covers Other Work · Set Up Another; not_captured, not_observed Task Not Seen Yet · Refresh Soon; not_yet_computed Not Computed Yet · Refresh Soon; not_retained Task No Longer Kept; runtime_not_observed Agent App Not Followed; not_resolvable, not_observable Task Not Found by Name; result_limit Result Limit Reached · Refresh; the incompatible codes Answer Unreadable · Check Version on Computer; host_not_allowed, origin_not_allowed Request Turned Away · Check Setup on Computer; instance_mismatch Wrong Source Answered · Check Setup on Computer; discovery_unreadable Setup Unreadable · Check on Computer; native_id_unknown, naming no source, Session Unknown Yet · Refresh Soon; any other No Reason Given · Refresh. This headset's read: Measurement Unreadable · , Understanding Unreadable · or · Reread Failed · , then Computer Timed Out · Refresh, No Answer · Refresh, Not Connected · Refresh Once Connected, Pairing Paused · Too Many Tries · Refresh in 1 min, the pairing line, the refused credential's line, the certificate line, Host Unverified · Code Not Sent · Check App · Refresh, the unreadable line, else Read Failed · Refresh; every line keeps its own case, so none is lowered after a lead. While reading: Reading Understanding, Reading Measurement, then · Reading Again; before any read, Not Read; before work, No Work Yet | The owner's voice standard, 2026-10-30 |
| The first visit, on a computer with no task (ADR 0026) | One plate: "What would you like to work on?", its rows "Something new" (chosen as it opens) and "A project on your computer", its main action "Start a project" or "Show my projects", and Settings alone in the row of places | New Project; Connect Projects; Start from Idea; Not Now | The owner's voice standard, 2026-10-30 |
| The closed bar while there is no task at all | "Nothing is running yet" | Nothing Running | The owner's voice standard, 2026-10-30 |
| The first question's idea, heard as New project opens on a later step of a draft kept from before | "Nothing was typed: this project has moved on from Your idea. Choose Your idea above, then hold to talk again.", the step's line; a page with no line for Hold to talk drops the words | Not Typed · Step Changed · Choose Your Idea | The owner's voice standard, 2026-10-30 |
| The reduced-motion setting, in Settings' Comfort | "Motion" ("On", "Kept still"), now "Things move" or "Things keep still", the change "Keep things still" or "Let things move"; turning it on: "Nothing keeps moving on its own: a wait shows a still highlight, and characters and badges stand still.", which once characters travel becomes "Nothing keeps moving on its own: a wait shows a still highlight, characters and badges stand still, and characters fade from place to place."; turning it off: "Waits shimmer, a task waiting for you breathes, and characters move." | Motion (On, Still); turning it on: Still Highlights · Characters and Badges Still; turning it off: Waits Shimmer · Waiting Tasks Breathe · Characters Move; Comfort's change Keep Still or Allow Motion | The owner's voice standard, 2026-10-30 |
| Where tasks wait, asked once (ADR 0026) | Settings' "The characters" alone | "Where should your tasks wait?", its rows "Below my screen", "On my right", "On my left", its main action "Keep them here", "Move them right" or "Move them left" | 2026-10-08, by the coordinator |
| Work that left the stage, in Tasks (ADR 0026) | None: every task stayed on the stage | The heading "Earlier"; its rows' small facts "Ended 2 days ago" and "Last heard 3 days ago"; on a file opened from it, "Keep on stage" | 2026-10-08, by the coordinator |
| A Can't tell yet a day or more old | "Can't tell what it's doing right now." | The badge keeps "Can't tell yet", with "Last heard 3 days ago" beside it on a brought-back character's label; the file says "Can't tell what it's doing: nothing heard from it for 3 days." | 2026-10-08, by the coordinator |
| Quiet tasks crowding the stage as a session starts | None | A plate asked once: "4 tasks haven't been heard from since Monday." ("for over a week" past 6 days), then "Move them to Earlier? Tasks keeps them, and you can bring any back.", its main action "Move to Earlier" | 2026-10-08, by the coordinator |
| The first-time hint on the stage | A prompt at the foot of a character's glance, the pinch icon in its cap and "Open", until the first file opened from a character | Open | The owner's voice standard, 2026-10-30 |
| What is still open while things are folded into the bar | The closed bar's subject says "Still open: {task title}" while nothing waits; what waits wins | Open · {task title} | The owner's voice standard, 2026-10-30 |
| Two tasks of one project that changed the same file, on either character's glance (ADR 0013) | None | "Both changed {file}", the file on a token | 2026-10-08, by the coordinator |
| Start building while a build is on its way, its reason as the page's last line | Already starting. Wait to hear how it went. | Starting · Awaiting Result (locked) | The owner's voice standard, 2026-10-30 |
| A place, where two share a name | "Projects (person)" and "Projects (Work)": the place's own name, then the nearest folder above that tells them apart; where one is not enough, more, in path order, "Projects (Personal, Work)"; where nothing above tells them apart, a number, "Projects (old) 2"; never a path. The host names them (`label`), so Projects, New project and every listing say the same | {folder} · {place}; In {place} where several share a name | The owner's voice standard, 2026-10-30 |
| A kept choice whose place is gone | "{folder}, in a place your computer no longer lists" | {folder} · Place Unlisted | The owner's voice standard, 2026-10-30 |

## Words from outside

Lane V's treatment, picked by the owner on 2026-10-08 ([ADR 0026](../decisions/0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md)),
applied by lane W's batch B. {x} is text Halcyonic didn't write: it leans, on a ground where it meets
our words, under our quote mark where a row or a block is all of it, and is cut before our words.
Settled by the coordinator on 2026-10-08, old beside new.

| Where | Old | New |
| --- | --- | --- |
| Activity's agent message | “{message}” with "Agent says" | {message} with "Agent says", under our quote mark |
| Activity: a start asked for | Asked {app} to start | Asked to start on {app} |
| Activity: a tool starting | {tool}: {title}, or {tool} | Used {tool}: {title}, or Used {tool} |
| Activity: a tool done | {tool} succeeded, {tool} failed | Succeeded: {tool}, Failed: {tool}; with no name, A tool succeeded, A tool failed |
| A file's Doing, the latest line | Latest: It says: “{message}” | Latest: {message}, with "Agent says" |
| An agent's question, its answers, a header | “{question}”; answers as plain rows | {question} as a block under our quote mark; each answer a row of theirs under it |
| The companion's question and reply | The companion says: “{line}” | {line} under our quote mark; the source line names the companion |
| Seorak's measured checks | 23 passed of 24 runs (96%): {label} | 23 of 24 Passed · 96% · {label}; a count not given, Passes Unknown · 24 Runs or 23 Passed · Runs Unknown; no rate, No Pass Rate; kinds apart by "; "; our counts first, the label leaning last (the owner's voice standard, 2026-10-30) |
| The measurement's lines in a file | Checks: …, Cost: About $1.20. {note}, Outcome: … | Checks · …, Cost · About $1.20 · {note} (Under $0.01 below a cent; Unknown · Unpriced or Not Measured), Outcome · … (as No Commits Landed · 2 Tool Errors · First at 17:44 · End Unknown, then Uncommitted · 3 Files · +41 −7 · Kept After 3 Days · 90% · 36 of 40 Lines); each part's status Available, Partly Available · {reason} or Unavailable · {reason}, then 3 of 4 Sessions · Complete or Incomplete · {gaps}, then Fresh, Stale or Recomputing · Data to {time} (No Data Yet), or Stale Since {time} (the owner's voice standard, 2026-10-30) |
| A statement or an item beside its chip | Failing: “{x}”, “{x}” | Failing: {x}, {x} |
| The system keyboard's prompt for an instruction | What to tell it: {title} | What to tell it; what it holds, shown by LabelText's plain rule |
| The usage reading's side panel | Agent app: {agent}, then Seen, Resets, Account | Agent App · {agent}, then Seen, Resets, Account (the owner's voice standard, 2026-10-30) |
| Your answer and the person's instructions | Your answer: “{words}”; “{instruction}” above the question | Your answer: {words}; {instruction} above the question, leaning |
| Connect's sentences and Projects' facts | “{name}” | {name}, cut at 40 |
| Connect's reason while another folder's connection is unsettled | Still waiting to hear whether “{project}” was connected. … | Still waiting to hear whether the other folder was connected. Connecting another folder waits until that's known. Once it can't be known: Not sure whether the other folder was connected. Look for it in Projects. Connecting another folder waits until that's known, or until you restart the app. (a prompt's reason holds our words alone) |
| A recorded run's word on the answer it was given when it ran (2026-10-28) | (none) | Not sent to any agent. This was approved when it ran, so the run goes on as it happened.; …This was denied when it ran…; Not sent to any agent. The run goes on as it happened. |
| A recorded run's word on anything it holds nothing for (2026-10-28) | (none) | Not sent to any agent. This run is recorded as it happened, so nothing changes. |
| The line above the stage while a recorded run plays (2026-10-28; reworded 2026-10-29, since a run may never wait for anyone and the banner keeps to three rows with a task waiting) | Recorded: real agent work, played back on this headset. Nothing here is live. It waits for you where the agent waited. Nothing reaches an agent. | Recorded: real agent work, played back here. Nothing here is live. It waits only where the agent waited. Nothing reaches an agent. |
| A recorded run's end, with a demo to follow; the end line takes the second sentence's place (2026-10-29) | This run has ended. A demo you can answer comes next. (added after the second sentence) | Run ended. Next, a demo you can answer. Nothing reaches an agent. |
| A recorded run's end with no demo to follow (2026-10-29) | This recording has ended and starts again shortly. (added after the second sentence) | Run ended. It starts again soon. Nothing reaches an agent. |
| The demo's end, with the recorded run to follow (2026-10-29) | This demo has ended. The recorded run comes next. (added after the second sentence) | Demo ended. Next, the recorded run. Nothing reaches an agent. |
| The demo's end, playing alone (2026-10-29) | This recording has ended and starts again shortly. (added after the second sentence) | Demo ended. It starts again soon. Nothing reaches an agent. |
| The demonstration's word on an answer it continues from its recording | Nothing is sent to an agent. The recording goes on as if you answered “{label}”.; Not sent to any agent; the recording continues as recorded for “{label}”.; …so it continues as recorded for “{label}”. | Nothing is sent to an agent. The recording goes on as if you had chosen its recorded answer.; Not sent to any agent; the recording continues as recorded for this instruction.; Not sent to any agent; the recording holds only its own instructions, so it continues with the one it recorded. (Halcyonic's message names no outside text) |
| A provenance line | From Salidium {version}, 2 minutes ago, {version} leaning on its ground: a source names its own version | Salidium {version} · 2 min ago (Just Now, 3 h ago, 30 Oct 14:02; Recorded {time} in a recording), {version} leaning on its ground; Seorak · Read 2 min ago; a stand-in's Simulated Explanation · , Simulated Checks · or Simulated Measurement · {when} (the owner's voice standard, 2026-10-30) |
| Unchanged, now marked | Started on {app}; Tests started: {label}; Tests passed: {summary}; Approval requested to use {tool}: {summary}; It wants to use {tool}:; Tests passed at 14:02: {label}; Still open: {title}; New task in {name}; New folder in {root}; Directly in {root}; In {root}; Files go straight into {root}; From now on, … | The same words, {x} leaning on its ground |

## Workspace organization

Move, Compact and Expand are persistent named workspace controls outside the decision footer.
Compact keeps the exact current page and valid local draft. Expand resumes that composition;
Close retains its existing leave/discard meaning. Move's short press opens placement details;
Hold Move to drag the whole workspace explains its hold. Below a window the detail says,
"The workspace stays below the window. Choose where the characters stand."
The actual choices are In front of you, Room for a window and Either side of a window. Selecting
one shows Apply; it changes only on that explicit press. Reset position brings the workspace back
within reach (Reset Position). Read title shows the retained task's complete outside title in readable parts.

## Instructions

Write Instruction opens Your Instruction. An empty keyboard field says Instruction; without a
keyboard it says Hold to Talk. Keyboard Done and completed transcription save only local
words. The explicit action is Send Instruction. Recorded or generic instruction choices lead to
Review Instruction, which copies a local draft; choosing or reviewing never sends. Cancel discards
that draft locally. A spoken draft retains its additional reviewed Confirm after Send Instruction,
including after a keyboard edit. Read Full Instruction explains an unread draft; Answer Approval
First explains approval preemption. A changed execution says Not Sent · Task Changed.

Next Choices names the preset chooser's body pager. Back to Choices acknowledges the full notice
and returns to the preserved selection. Task unavailable is the compact fallback only when the
retained task is missing and no verified last title is available.


## Dedicated workspace navigation

The workspace opens with normal Projects and Tasks navigation for fresh, demonstration,
disconnected and returning sessions. Halcyonic, connection mode and Project > Task context are
visible on the composed frame. Minimize workspace is global; Close task is local. Open Workspace
restores the retained selection and valid local drafts after fresh drawn authority. Other Apps
opens the actual native panel when supported and explains unavailability otherwise. Move and
Reset Position describe real workspace movement, with no promises about another app's window.

Navigation uses familiar capability names. Run ended does not mean the task is accepted, its
result is correct, or tests passed. Synthetic and recorded work remain explicitly marked beside
the task even inside a live journal containing real work. Existing wording elsewhere in this document is historical
where it mandates character-first discovery or forbids these familiar labels; implementation
updates must preserve source provenance and current-action truth.


### Project and task navigation

Projects and Tasks use those concrete view names. Selecting a project opens its Tasks, with the
project name in the persistent workspace header. All Projects returns to the full task list;
Project Details preserves the project's existing Add Task and agent visibility actions. Saved
Drafts remains reachable in either task list. Every task row carries its actual state and applicable
Practice, Demo, Recorded or Last known marks, even within a live journal containing mixed sources.
The selected task's state appears once in the attached header rather than again above its title.
The Projects header control remains reachable while an instruction is open and retains that draft.
An empty folder list says No Folders Allowed, with the concrete next step Allow on Computer · Try
Again. Earlier copy tables above are historical
for these replaced subjects and navigation routes.


Ask workspace opens a general request for the selected task and run. Request, Reply and Actions
are views of the same request, not a catalogue of accepted phrases. Start request and Continue
request are explicit actions; heard words first offer Yes, send request with Cancel. The visible
scope says Read this task and send instructions to this run. Assistant reply is marked reported;
recorded action receipts show recorded outcomes separately. Cancel request stops the planner;
the task may keep running. Back to task retains both local drafts. Unavailable assistant or
connection state keeps the words and gives a concrete Refresh or reconnect route.


The native panel says Checking your computer before restoring this view only for its restoration
check. During the actual in-flight read its words shimmer, or stand highlighted under Keep things
still or disabled system animations. Connection failure replaces that wait with the existing
recovery message; the animation never changes a checking state into a success claim.

Workspace opens local organization and navigation. Move workspace enters placement; its header
target says Move and its return says Done moving. Minimize workspace preserves work and drafts.
Open work window opens the existing native work panel and retains its platform availability rule;
it does not open the system's app launcher. Show task portraits and Hide
task portraits name a device-wide local preference, separate from per-task colour. The Projects
root omits Close; a task retains Close because that action removes its context and does not
always navigate to a task list. Blue marks the main action, while amber still means waiting for
you. These words describe the isolated candidate; wearer acceptance remains open.

## The room of projects

The words of [ADR 0040](../decisions/0040-the-headset-is-a-room-of-projects.md)'s room, decided with its storyboard on
2026-10-29 and its truth read, and put in the owner's voice standard on 2026-10-30 (ART's room section, DESIGN's moment
card anatomy). {runtime} is the agent app's name, said only as provenance; anything the runtime wrote (a path, a command,
its sentence) is theirs and drawn as theirs. A moment card reads top to bottom: its eyebrow (who and how known), a 1 to 3
word Title, one metadata line in the " · " cadence, a well for a command, a risk line, its answers. The moment words live
in `KitMomentWords`; the gallery's title, newcomer line, shelves and finder are not built yet.

| Kind | Words | Example |
| --- | --- | --- |
| The gallery's title | Projects · {n} · {groups} Groups | Projects · 214 · 6 Groups |
| A newcomer's line | Say Idea when speech is set up on the Mac, otherwise Type Idea; Open Project | Say Idea |
| Making something | New Project, on the first tile and at the top of the gallery | New Project |
| What needs the person | Needs You · {n} | Needs You · 3 |
| The rest of a shelf | +{n} More | +42 More |
| Finding | Find Project | Find Project |
| A catalog state | Halcyonic's words mapped from the catalog's states (the gallery's table below), marked Reported beside the catalog's name; an attention kind Halcyonic does not know reads Needs a Look | Review Requested · Reported · Sample Projects |
| A catalog project linked to no Project here | Not Linked. Never "Not on this computer yet": Halcyonic cannot know what is on the computer. Possible Match · {folder}, marked Inferred, only from the host's own suggestion | Not Linked |
| A linked project with both states | Halcyonic's state with its dot, Observed, then the catalog's, Reported; neither hides the other | Running · Observed / Review Requested · Reported |
| The screen's picture | Last Check · Today {time} · Observed, or Last Check · {d MMM} {time} · Observed where it isn't today, on the host's check screenshot; no picture and no label until the check takes one | Last Check · Today 10:38 · Observed |
| The one clear action | Make VR Version, for a project that is not VR yet. It only asks the agent. | Make VR Version |
| The screen's title | {project}, then {runtime} · {state}, on the screen's own chip | Tower Toss · Codex · Ready |
| The screen's bar | Its primary action first (Make VR Version for a project that is not VR yet, else Request Change), then Request Change where it is not the primary, then Try Build only after a passed check of the computer's own, with a route to try it, while no turn runs and with no change seen since that check; then Activity, Reasoning, Outcome | Request Change |
| A request sent | Eyebrow Your Computer · Observed (your computer sent it); Title Sent; {runtime} · Awaiting Start. Never "Started" on acceptance. | Sent · Codex · Awaiting Start |
| The runtime's report of an edit | Eyebrow {runtime} · Reported; Title Edit Reported (New File Reported, Removal Reported); the path; Not Yet Observed | Edit Reported · src/towers.ts · Not Yet Observed |
| The Mac's reading at the turn's end | Eyebrow Your Computer · Observed; Title Change Observed (File Made, File Removed); the path · {before} to {after} Lines · {time}; never the lines themselves | Change Observed · src/towers.ts · 41 to 54 Lines · 10:42 |
| Lines added and removed | +{added} −{removed} Lines, only from a checkpoint diff when undo is on | +14 −3 Lines |
| Earlier moments | Earlier · {n} More, counting only the moments before the shown one | Earlier · 4 More |
| Moments after a waiting question | {n} Newer, counting the moments after a question that stays shown | 1 Newer |
| A check the Mac ran | Eyebrow Your Computer · Observed; Computer Check · Builds · Under Way while no exit is seen, never the agent's own "Checking"; then Build Passed or Build Failed · Builds (· Opens only where the check opened the build) | Build Passed · Builds · Opens |
| A check the agent ran | Eyebrow {runtime} · Reported; Title Check Reported; the command in the well; its outcome as theirs | Check Reported · npm run build · exit 1 |
| A check that ended with no outcome | Check Ran · Result Unknown | Check Ran · Result Unknown |
| A command to approve | Eyebrow Agent · Question · Reported; Title Run Command?; the command in the well; ADR 0033's line when the runtime declares it (May Run Outside Sandbox · Your Access, or Runs Inside Sandbox). "Nothing runs until you answer." is deleted: the question and its locked answers carry it. | Run Command? · npm install matter-js |
| The answers | Run · Decline · Reply; a cut command offers no Run | Run |
| The agent's own note | Eyebrow {runtime} · Reported; Title Agent Note; their words beneath as prose | Agent Note |
| Ready | Eyebrow Your Computer · Observed; Title Build Ready; the project; Builds · {duration} (Builds · Opens · {duration} where the check opened it). Only after a check of the computer's own passed; Try Build only where a route to try it exists; for a VR version, only after the check opened the VR version. | Build Ready · Tower Toss · Builds · Opens · 6 min 40 s |
| A skipped reading | Eyebrow Your Computer · Observed; Title Not Observed; the reason. Never "no changes". | Not Observed · too many files |
| A package the starter doesn't hold | Eyebrow Your Computer · Observed; Title Not in Starter; the package; Install Not Run | Not in Starter · matter-js · Install Not Run |
| Reading | Read File, Read Output, Fold, Whole File (unavailable until the Mac has read the file); a cut command in the well says Cut · {n} More Lines | Read File |
| The report beside a task, in the kit | Agent · Reported, as the agent's chip everywhere; Nothing Reported; Earlier Activity · {n} More | Agent · Reported |
| The composer, in the kit | Instruction (empty field) · Your Answer · Not Sent · Send Instruction · Hold to Talk | Send Instruction |

## The projects gallery

The words of the gallery's core (`GalleryText`, [ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-phat-porcupine.md)),
agreed with DESIGN on 2026-10-29 and put in the owner's voice standard on 2026-10-30. How the room lays them out is in
"The room of projects". A project's state in the person's catalog is reported by Phat Porcupine, so its line never
repeats a task's own words ("Needs You", "Checks Failed") and is marked Reported with the catalog's name: a reported
state is never read as one Halcyonic observed. Names, owners, groups and pull request titles from the catalog lean, as
words from outside do; these lines are Halcyonic's own.

| The catalog's state | Group | Line |
| --- | --- | --- |
| `awaiting_input` | Needs you | Agent Waiting |
| `session_failed` | Needs you | Agent Failed |
| `checks_failing` | Needs you | Checks Failing |
| `review_requested` | Needs you | Review Requested |
| `changes_requested` | Needs you | Changes Requested |
| `ready_to_merge` | Needs you | Ready to Merge |
| any state Halcyonic doesn't know yet | Needs you | Needs a Look |
| `agent_running` | Working now | Agent Running (a line, never a character) |
| `session_complete` | Recent | Agent Finished |
| `active` | Recent | Changed Recently |
| `inactive` | Quiet | Quiet |
| `archived` | Quiet | Archived |
| `access_lost` | Quiet | Unreachable |

A Halcyonic project keeps its tasks' words, or "No work yet", always, as this computer observed them. Linked to a
catalog project, it shows the catalog's line beside them, marked as the catalog's, and the more urgent of the two places
it in a group.

| Where | Words |
| --- | --- |
| Where a Halcyonic project is | On Your Computer · No Folder Yet |
| Where a catalog project is, as far as Halcyonic knows | Not Linked · Possible Match · {a folder the computer matched by a rule, leaning} (Inferred, never a link) |
| How old the catalog is, on the gallery's header line | Checked · Today 14:50; Checked · 27 Oct 09:05; Refresh Failed · Last · Today 14:50; Refresh Failed; Not Checked (a refresh that could not run, never a failed result) |
| Over a sample or simulated catalog, kept apart from real projects | Sample Projects |
| No catalog: not set up | Phat Porcupine · Not Set Up · Set Up on Computer |
| No catalog: not running | Phat Porcupine · Not Running |
| No catalog: still gathering | Phat Porcupine · Gathering · Refresh |
| No catalog: credential refused | Phat Porcupine · Access Refused · Set Up Again |
| No catalog: a version Halcyonic can't read | Phat Porcupine · Version Unsupported |
| No catalog: any other reason | Projects Unreadable · Refresh |
| This headset's own read failed | Projects Unreadable · {why, as Help me understand says it; that sentence moves with the menu's words} |
| No catalog in a recording (the demonstration, the judge build, a recorded run) | Not in Recording |

The lines without a catalog that name Phat Porcupine appear only where a real control plane
answered, never over sample projects or in a recording, so neither the judge build nor the
competition video shows "Phat Porcupine" or "GitHub". "Phat Porcupine" is one
constant (`GalleryText.Source`) until the owner settles the name.
