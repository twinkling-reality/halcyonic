# How Halcyonic speaks

Halcyonic's own words on the headset: how they sound, the word for each state of a task, and what
a person never reads. Every word lives in the client core (`apps/xr/Packages/com.halcyonic.client`),
in `StateLanguage`, `EntryText`, `WorkspaceText`, `ConnectionText`, `VoiceText`, `HostText` and their kin, so
tests hold them to these rules and the Unity layer only lays them out
([ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)).
Text from outside (titles, agent messages, tool output, server errors) is not ours: it shows as
written, by `LabelText`'s rule, and only where a person needs it.

## The voice

Calm and plain, like a capable colleague who says what is true now, how sure it is, and what you
can do next.

1. **Sentences a person would say.** "1 task is waiting for you", "2 tasks running", "1 task
   finished, ready to look at", "Nothing is waiting for you." Never shorthand such as "1 needs
   you" or "2 active" in a sentence.
2. **Short words only where space is fixed.** A state badge says the state's word ("Waiting for
   you"); a chip with a fixed width says the short count ("1 task waiting", "2 tasks running",
   "Hidden · 1 waiting"). Wherever there is room, the same state is said in full.
3. **Only what is true now, and how sure.** Sending, Sent, Waiting for the agent, Confirmed only
   when the agent confirmed, Couldn't do that, Not sure it happened. A finished round says nothing
   about whether the work is right, so nothing celebrates it.
4. **Lead with the state or the action**, in at most two short sentences.
5. **Buttons** begin with a verb, in sentence case, in one to three words. A confirmation is
   "Yes, {verb}" beside Cancel. An icon may stand before a button's words, never in their place.
6. **"You" for the person, "it" for the agent, "your computer" for the host**, never a product
   name such as Mac, which the competition rules forbid in what judges see. The word lives in one
   place, `HostText`.
7. **No blame, and a next step.** Every refusal ends with what to do: "Couldn't start: this project
   has no folder yet. Choose where its files live."
8. **The agent's words are its own**, quoted, tagged ("It says: “…”" in the workspace's log, "Agent
   says" in a section) and leaning, never restated as fact.
9. **No exclamation marks, emoji, em dashes or capitals for emphasis.** Numerals for numbers.
10. **Practice, Demo, Recorded and Last known** are said wherever they apply, beside a state and
    never inside its word.
11. **No brands in our words.** App and model names arrive as data; Salidium and Seorak appear
    only in a provenance line.

## The word for each state

One word per state, the same on the badge, in the peek, in the workspace and in every list
(`StateLanguage.WordOf`). A sentence says the same state in full.

| State | Badge | In a sentence |
| --- | --- | --- |
| No run yet | Not started | Not started yet. |
| Asked to start, not yet confirmed | Starting | Starting. Waiting for the agent to confirm. |
| Running | Working | 2 tasks running. |
| A test run in progress | Checking its work | Checking its work. |
| An approval or a question waits | Waiting for you | 1 task is waiting for you. “Fix the login” is waiting for you. |
| The round ended | Finished this round | 1 task finished, ready to look at. |
| The round ended and its checks did not pass | Checks failed | Checks: 1 failed, 23 passed. |
| The run failed | Couldn't finish | Couldn't finish this round. Tell it to try again, or what to do instead. (the way on, never the agent app's error) |
| Stopped, as the agent confirmed | Stopped | Stopped. |
| Halcyonic cannot see the work | Can't tell yet | Can't tell what it's doing right now. |

The state badge also counts what waits when more than one thing does ("Waiting for you · 2"). In
counts, "paused" covers tasks at rest that are neither running nor waiting for you.

## The words a person sees

| Word | Means | Instead of |
| --- | --- | --- |
| project | One body of work, in one folder | |
| task | One workstream: one character on the stage | workstream, work item |
| work | What the agents do, in general | execution |
| agent | The AI working on a task; the character is the task, not the agent | runtime, bot |
| round | One stretch of the agent's work, until it stops to wait | turn |
| request | Something the agent wants your approval for | approval as a noun, permission |
| question | Something the agent asks you | prompt |
| checks | Tests and other verification | tests, lint |
| your computer | The host and its control plane | control plane, server, Mac |
| folder | Where a project's files live | location, directory, path |
| agent app | The runtime, such as OpenCode or Codex, named as data | runtime, adapter |
| practice run | Work on the simulated runtime: nothing is built | mock, simulated |
| demo | The recorded demonstration | interactive example |
| companion | The AI on your computer that helps shape an idea in Create; it builds nothing and is never a task | assistant, AI, chatbot |

A person never reads: execution, journal, command, principal, scenario, capability, projection,
snapshot, workstream, access token (an access code, only where the person must act on one).

### The chip beside an answer line

Each line of an answer says how it is known with a chip; an observed fact, a measurement and
Halcyonic's own words go without one. The words below were settled by the coordinator on
2026-10-02; the old words are kept beside them so no surface goes back to them.

| The line is | Chip | Instead of |
| --- | --- | --- |
| The agent's own words | Agent says | |
| A subagent's own words | Subagent says | |
| Words whose author the source does not know | Author unknown | Quoted |
| A model's account of the work, after it | Model explains | Explanation |
| The source's own reading of what it observed, not a fact | Inferred | |
| What the agent planned to do | Planned | |

### Usage in the demonstration

While the recorded demonstration plays, Usage shows its recorded limits for one practice agent.
These words were settled by the coordinator on 2026-10-02.

| Where | Words |
| --- | --- |
| Each limit | Practice agent, 5-hour window · Practice agent, weekly |
| Where they come from, in place of a source's name | Recorded for the demo, not from any account |
| Under the limits, in place of the account note | These limits are part of the recording. |
| A limit's Account fact, in place of who it belongs to | Part of the recording |

### The agent's question in a file

How a file's Waiting page pages the agent's question (ADR 0026), as settled by the coordinator on
2026-10-02, the old words beside the new.

| Where | Words | Instead of |
| --- | --- | --- |
| The row at the end of a prompt's page of answers | More answers, 2 of 2; from the last page, First answers, 1 of 2 | Next, the old panel's pager |
| The row after a prompt, in a question of several | Next question, 2 of 2; after the last, Your answers | |
| The row ending a long question's last part, before its answers | On to the answers; where the answers page, On to the answers, 1 of 2 | |
| Send answer's reason on a prompt's page, in a question of several | Answer each question, then send from Your answers. | |
| Send answer's reason while a long question isn't read to its end | Read the whole question first. | Open the question to read the rest. |
| Send answer's reason while a chosen cut answer or a long typed one isn't read whole | Read the whole answer you chose first. | |
| Send answer's reason while a page of Your answers isn't drawn | Read all your answers first. | |
| The row paging Your answers | Your answers, 2 of 2; from the last page, Your answers, 1 of 2 | |
| The row for the person's own answer | Type my answer; once typed, Your answer: "…", chosen | Type an answer; Typed: … |

### Steering from a file

Settled by the coordinator on 2026-10-02.

| Where | Words |
| --- | --- |
| Tell it's reason while the instructions offered show and none is chosen | Choose what to tell it first. |
| A confirmation dropped because the request it asks about now reads differently | Nothing was sent: the request changed. Read it again. |
| Answers awaiting their Yes, dropped because the question is no longer the one asked | Nothing was sent: the question changed. Read it again. (instead of Check it again) |

### The headset's access code

Where a person must act on the development build's access token, it is the access code. These
lines were settled by the coordinator on 2026-10-02; the old words are kept beside them so no
surface goes back to them.

| When | Words | Instead of |
| --- | --- | --- |
| Your computer answered 401 to the code | Your computer refused this headset's access code: it doesn't match your computer's. Put your computer's current access code on the headset, then restart the app. | Your computer refused this headset's access token: it doesn't match your computer's. Put your computer's current access token on the headset, then restart the app. |
| What answers can't prove it holds the code, so the headset didn't send it | This headset's access code doesn't match your computer's, or something else is answering in its place, so the headset didn't send it. Put your computer's current access code on the headset, check that Halcyonic is running there, and restart the app. | |
| The endpoint is one the code never goes to, a name or another scheme (an editor setting) | The access code goes only to ws:// or http:// at 127.0.0.1 or [::1], so it was not sent to {endpoint}. Name one of those instead. | The access code goes only to 127.0.0.1 or [::1], so it was not sent to localhost:47800. Name one of those instead. |

## Sentence patterns

| Kind | Pattern | Example |
| --- | --- | --- |
| A peek | {badge}, then the reason without the state's words | Waiting for you · It wants to run: make migrate |
| Nothing waiting | Nothing is waiting for you. Latest: {activity}. | Nothing is waiting for you. Latest: edit: src/auth/rate-limit.ts |
| Sent | Sent. Waiting for {who}… | Sent. Waiting for the agent… |
| Still on its way | Sent…, unavailable, where the action that sent it stood | Sent… |
| Confirmed | Confirmed: {what}. | Confirmed: it has your answer. |
| Refusal | Couldn't {verb}: {cause}. {Next step}. | Couldn't start: this project has no folder yet. Choose where its files live. |
| Unknown effect | Not sure it happened. {What to check first}. | Not sure it happened. Check the tasks on the stage before you try again. |
| Connection | {What is true}: {plain cause}. {What happens}. | Last known: can't reach your computer. Trying again… |
| Provenance | From {source} · {when} | From Seorak · 2 min ago |
| The work's own state | As the agent reported it, the source line of a file's Waiting and Activity, no app's name in it (settled by the coordinator on 2026-10-02) | As the agent reported it |
| Confirmation | Yes, {verb}, where no control stood a moment before; Cancel where the first press was | Yes, start building · Change; Yes, clear · Cancel |
| Asking about what shows | {Verb} the {thing} above? | Approve the request above? |
| Locked final press | Read to part {n} first | Read to part 3 first |
| Kept while away | Still open: {panel} | Still open: Create a project |
| The companion's words | The companion says: “{line}”, quoted and leaning, never in our voice; its view as its opinion | The companion thinks this is unclear. |

Some of Halcyonic's words still predate this guide; each surface takes these words as it moves to
the interface of ADR 0023, one surface at a time. The character labels, the rail and Settings, the
entry panel, the workspace and Usage left have moved; the ambient lines have not yet.

## Words changed by the menu

The menu of [ADR 0026](../decisions/0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md)
replaces the entry panel's screens. Routine wording, settled by the coordinator, old beside new:

| Where | Old | New | Settled |
| --- | --- | --- | --- |
| A file's instruction armed to send, heard through Hold to talk or typed for a policy's review | "Your computer heard: “…” Send it?" or "Tell it this? “…”", the words inside the question, cut at three rows | The words quoted above, in parts as a request is, each but the last ending in "Next part, 2 of 3", then "Read to part 3 first" until the last part has shown, then "Your computer heard the words above. Send them?" or "Tell it the words above?"; a Yes refused before then, as a backstop, says "Nothing was sent: read to the last part first." | 2026-10-04, by the coordinator |
| A file's question offering two answers by one label, as "Yes" to keep the data and "Yes" to delete it | None: both rows showed, and either sent "Yes" twice, which your computer refused, so it could never be answered | "Two of its answers read the same, so your choice can't be sent from here. Press Stop to go on.", then "It's waiting for an answer." and Stop, as a question asking for a secret shows; no answer rows and no Hold to talk | 2026-10-04, by the coordinator |
| Why it can't tell what a task is doing, on the character, its peek and the file's Activity | "Can't tell what it's doing: {the reason's message}", an agent app's diagnostic, as "After the OpenCode event stream reconnected, the session could not be read (GET /api/session/…)"; in the log "Lost contact with the runtime: {message}" and "State unknown: {message}" | By the reason's code: "Can't tell what it's doing: your computer lost touch with the agent app.", "…: your computer restarted and lost touch with the agent app.", "…: not sure it started."; any other code "Can't tell what it's doing right now."; the log's line for a lost connection "Your computer lost touch with the agent app." | 2026-10-04, by the coordinator |
| Why a task couldn't start or finish, on the character, its peek and the file's Activity | "Couldn't finish: {the reason's message}", an agent app's own error | "Couldn't finish this round. Tell it to try again, or what to do instead." where Tell it is offered, else "Couldn't finish this round. Add the task again in Projects to try again."; a start refused over its folder, "Couldn't start: {the folder's problem, by its code}"; any other start, "Couldn't start. Add the task again in Projects to try again." | 2026-10-04, by the coordinator |
| A peek's next step under Couldn't finish | "Open it to see why." | "Open it to see what it did.", since the file no longer shows the agent app's error | 2026-10-04, by the coordinator |
| The activity log's lines for a round and for what failed | "Turn started", "Turn finished", "Turn stopped", "Turn failed: {message}", "Could not start: {message}", "A request failed: {message}", "Asked to stop the turn" | "Round started", "Round finished", "Round stopped", "Couldn't finish this round", "Couldn't start" or "Couldn't start: {the folder's problem}", "Couldn't {verb}" (as "Couldn't send an instruction") where it had no effect, and where it may have happened anyway what may have, then what to check, never after "Couldn't", which says nothing happened: "Not sure the instruction reached it.", "Not sure it stopped.", "Not sure it has your decision.", "Not sure it started.", each then "Check its activity before you try again."; an answer as before; anything else "Not sure it happened. Check its activity before you try again." (the same on a task's line of what was sent last; settled by the coordinator, 2026-10-04); and "Asked to stop it" | 2026-10-04, by the coordinator (the unknown effect corrected after review) |
| A command your computer refused, in the activity log, the file's line of what was sent last, and Start building's and Connect's steps | "Refused to {verb}: {message}", "Couldn't do that: {message}", "Couldn't connect: {message}", the control plane's words for developers, as "Runtime mock does not support instruct_while_running." | "Couldn't {verb}: {why}." by the refusal's code: "this project isn't on your computer any more. Choose another in Projects.", "this task isn't on your computer any more. Choose another in Tasks.", "this work isn't on your computer any more. Add the task again in Projects to try again.", "its agent app isn't on your computer now. Set it up there, then try again.", "it no longer waits for that decision. See what it's doing now.", "it's no longer waiting for this answer. See what it's doing now.", "it couldn't take that answer. Read the question again, then answer it.", "its agent app needs a model. Choose one, then try again.", "its agent app can't do that. See what it's doing, then try something it offers.", "it can't take that right now. See what it's doing, then try again.", "its agent app didn't accept those settings. Choose them again.", a folder's problem as before, the pairing line, "your computer no longer accepts this headset's pairing. Forget the computer on the headset and pair again.", one sentence for a revoked pairing wherever it is said (the last three ways on settled by the coordinator, 2026-10-04); a failure with no effect whose code every agent app shares, in words already used for it: approval_not_pending and question_not_pending as their refusals, no_running_turn as invalid_state, execution_unknown_to_runtime as execution_not_found, model_unavailable as model_required, capability_unimplemented as capability_unsupported, and runtime_unreachable or runtime_closed "your computer lost touch with the agent app. See what it's doing, then try again." where a task runs, else (Start building's and Connect's steps, a start that never ran) "…lost touch with the agent app. Check that the agent app is running on your computer, then try again."; any other, "Couldn't do that: nothing changed. Try again." on Start building's and Connect's steps, and on a task's line of what was sent last "Couldn't do that: nothing changed. See what it's doing, then try again." | 2026-10-04, by the coordinator |
| Why the headset isn't connected, after "Can't reach your computer; trying again." or "Your computer refused this app." | "The control plane did not answer within 10 s.", "The control plane closed the connection (…)", "The control plane speaks realtime protocol 2.", "The control plane refused the connection: {message}", "The control plane sent a message this app cannot read (…)", "The control plane sent nothing for 30 s.", "The client fell behind; resynchronizing." | "Your computer didn't answer within 10 seconds.", "Your computer closed the connection.", "Your computer runs another version of this app. Install the same version on both.", by the code your computer turned it away with: the same for another version, the pairing line for a revoked pairing, "This headset already has too many connections open to your computer. Close the app, then open it again.", "Your computer couldn't read what this app sent. Install the same version on both.", else "Your computer ended the connection."; "Your computer sent something this app can't read. Install the same version on both.", "Your computer sent nothing for 30 seconds.", "This headset fell behind, so it's catching up." | 2026-10-04, by the coordinator (no product name in our words) |
| Why the headset isn't connected, when your computer refuses the connection's upgrade for another reason than its credential | "The control plane refused the connection (403): The Host header must name this loopback server.", "(429): Too many failed credentials; wait a minute." | By its code: host_not_allowed, which only the Wi-Fi pairing's listener answers, "Your computer refused the name this headset uses for it. Pair it again in Settings."; too_many_requests "Your computer is turning this headset away for a minute after too many tries. It tries again by itself."; device_revoked the pairing line; any other "Your computer refused the connection. Restart the app, and pair it again in Settings if it happens again." | 2026-10-04, by the coordinator |
| A file's Send answer, armed, when words heard or typed change the answer | None: Yes sent the answer as it stood, the new words unread | "Nothing was sent: your answer changed. Read it again.", the confirmation cancelled | 2026-10-04, by the coordinator |
| A file's Hold to talk, its words heard after the question it was held for gave way to another | None: the words typed into whatever question showed, "This is what your computer heard. Check it, then press Send answer." | "Nothing was typed: the question changed while you spoke. Read it again.", the words dropped | 2026-10-04, by the coordinator |
| A file's Send answer, armed, when the text size changes | "Nothing was sent: the question changed. Read it again.", on the Yes pressed after | "Nothing was sent: the text size changed. Read it again.", as the size changes, the confirmation cancelled | 2026-10-04, by the coordinator |
| Tasks with no tasks | None: the entry panel listed no tasks of its own | "No tasks yet.", a quiet line on the page under the subject, which reads "Nothing is waiting for you." | 2026-10-02, by the coordinator |
| Projects' subject | "Welcome", then "Show projects from your computer, or make a new one. Work already running keeps going." on a first visit; "Connect projects" otherwise | "What would you like to work on?" on every visit, the place's purpose under the lit place's word; it keeps to one line (21.9 of 29.2 degrees), so the fallback "What do you want to work on?" was not needed | 2026-10-02, by the coordinator, every visit by lane V; the stage shows that running work goes on |
| A project's small fact on its row | "Hidden · 1 waiting", "1 task waiting" | "Hidden · 1 task waiting", "1 task waiting", "2 tasks running": a count keeps its noun, and a long name shortens first | 2026-10-02, by the coordinator |
| Projects' folders | Connect a folder, its own screen | "Folders on your computer", a heading in Projects; a folder's row says "Repository · changed 3 days ago" | 2026-10-02, by lane V's brief |
| Projects' prompts | Done, Show all, Add a task beside each project | "New project" with no row chosen; for a chosen project "Add a task" and "Hide from stage" or "Show on stage" (first "Hide from the stage", which needs 31.4 of the column's 30.3 degrees; "Hide from stage" needs 29.3, and says where the tasks go, since Tasks still lists them); for a chosen folder "Connect"; a list that pages "Next page", which on its last page reads "First page" and starts again; Close does what Done did | 2026-10-02, by the coordinator, as lane L proposed |
| A side panel's facts in Projects | (none) | A project: "Its work", "On the stage" ("Shown", "Hidden"). A folder: "Place", "Repository" ("Yes", "No"), "Changed", "Its name" for a look-alike, then "Connecting", or "What happened" once sent; "Your computer can't look inside it" where it couldn't | 2026-10-02, by the coordinator, as lane L proposed |
| Projects, when no folder can be listed | "Couldn't read your computer's folders: {reason} Press Try again." and "Your computer doesn't allow any folder yet. Allow one on your computer, then press Try again.", each a line that pressed read the folders again | A row that opens its side panel: "Couldn't read your computer's folders", its panel's "What happened" over the reason; or "Your computer doesn't allow any folder yet", its panel saying "Allow a folder on your computer, then try again."; "Try again" is the footer's main action while either is chosen, since a row never acts | 2026-10-02, by the coordinator |
| Projects, after the security review | A project's "1 task finished" for failed or unknown work; "Look for it in Connect projects"; "Not sure it happened. Look for “x” in Connect projects before you try again."; "Still waiting to hear whether “x” was connected" after an unknown outcome; "Another folder here has a name that looks the same. Check this is the one you mean by when it changed and where it is."; the mark "Looks like another folder's name" | "1 task to look at" (row) and "1 task waiting for you, 1 to look at, 2 running" (side panel); "Not sure whether “x” was connected. Look for it in Projects."; while it is on its way, "Still waiting to hear whether “x” was connected. Connecting another folder waits until that's known."; once its outcome can't be known, "Not sure whether “x” was connected. Look for it in Projects. Connecting another folder waits until that's known, or until you restart."; "Another folder or project has a name that looks the same. Check where it is and when it changed to be sure it's the one you mean."; a project's "Its name": "Another project or folder has a name that looks the same. Check its work to be sure it's the one you mean."; the mark "Look-alike name", before a look-alike row's own facts | 2026-10-02, by the coordinator |
| New project's Questions: the main prompt while Go on without it is chosen, making the recap from the person's own words without the companion | Make the recap | Make the recap from my words; Recap from my words where the footer measures too narrow beside Close and Hold to talk | The coordinator, 2026-10-02 |
| New project's Your idea: the row for the fixed questions | Help me figure it out, with A few fixed questions under it | Answer a few questions | The coordinator, 2026-10-02 |
| New project's Your idea: the main prompt while Talk it through with the companion is chosen | None: pressing the row opened the companion | Talk it through | The coordinator, 2026-10-02 |
| New project's Your idea: the main prompt while Answer a few questions is chosen | None: pressing the row opened them | Start the questions | The coordinator, 2026-10-02 |
| New project's Questions: why Make the recap can't be pressed before the person has said anything | Waiting for the companion… (nothing was waiting) | Answer a question first, or go on without it. | The coordinator, 2026-10-02 |
| New project's Recap: the answer that types a first task in place of the companion's suggestion and the person's own words | Use my words, a button beside the task | Type my own | The coordinator, 2026-10-02 |
| New project's Recap: the small fact beside a fact the companion suggested | Suggested by the companion, under the value | Suggested, at the row's right, and nothing once the value is the person's own; Suggested by the companion names the suggestion in its side panel | The coordinator, 2026-10-02 |
| New project's Recap: the name over the person's own words in a suggested first task's side panel | None | Your own words | The coordinator, 2026-10-02 |
| New project's Start building: the review's way to its next part | Read to part {n} first, on a locked Yes, start building | Next part, 2 of 3, a row at the end of each part; Yes, start building appears only once the last part has shown | The coordinator, 2026-10-02 |
| New project's fixed questions: the main action, which gives the chosen answer | None: pressing an answer gave it and moved on | Next question; Make the recap on the last question | The coordinator, 2026-10-02 |
| New project's fixed questions: why Next question can't be pressed yet | None | Choose or type an answer first. (a file's question's own words, reused) | The coordinator, 2026-10-02 |
| New project's fixed questions: the name question's skip, its last answer row | Name it later, a button | Name it later, kept over Go on without it, which under "What should it be called?" reads as going on without a name for good | The coordinator, 2026-10-02 |
| New project's Start building, when the project a task was being added to has gone | This project isn't on your computer any more. Close this, then choose a project in Connect projects. | This project isn't on your computer any more. Close this, then choose another in Projects. | The coordinator, 2026-10-03 |
| Settings' Your space and Your computer | The Settings sheet's sections, Your room and Your computer, each a line over its buttons | Rows: "Around you" ("Your room", "Virtual space"), "Your room's layout" ("No access", "Not set up", "Ready"), "The characters" ("In front of you", "Room for a window", "Either side of a window"), "The menu" ("Where it stands", its change "Reset position"), and in development builds "Pairing" ("Not paired", "Pairing…", "Paired", "Forgetting…"); each change keeps the old button's words, and Forget asks with "Yes, forget this computer" | The coordinator, 2026-10-03, from lane V's design; "Where it stands" until hold-to-drag records a move, then "Where you moved it" while one stands |
| Settings, while the room's controls aren't there, before they start or once they have gone | None: Your space was left out | "Not known" on Around you, Your room's layout and The characters, each saying "Your room can't be read now."; The menu and its Reset position stay | Proposed by lane W, 2026-10-03 |
| Settings' Pairing, in a development build, while the pairing isn't there yet or has gone | None: the row was left out | "Not ready", saying "Pairing isn't ready yet." | Proposed by lane W, 2026-10-03 |
| New project's last step, in its row of shapes | Start building | Build, since at 36 degrees the old words filled the step's shape edge to edge; the recap's main action stays Start building, and the confirmation Yes, start building | The coordinator, 2026-10-02 |
| New project's Starting, where it needs more than the stage gives and its secondary place holds its change: the row at its end | None: the page ran past the field's low edge | Next page, 2 of 3; First page on the last, as the menu's pager and the review's row say; New project's lists page by the footer's Next page and its questions' answers by More answers rows, the menu's own words | The coordinator, 2026-10-03 |
| New project's unknown start, read in parts where it needs more than the stage gives: Clear's reason until its last part is drawn | None: Clear was always pressable | Read to part 3 first, as the review's locked Yes says; each part but the last ends in Next part, 2 of 3 | The coordinator, 2026-10-03 |
| New project's unknown start, when its words change while Yes, clear waits, as when the computer's record arrives | None: the Yes stayed, and cleared what the person hadn't read | Nothing was cleared: this changed while you were clearing it. Read it again. As "Nothing was sent: the request changed. Read it again." says; the Yes lapses, and the page shows the first part with anything unread | The coordinator, 2026-10-04 |
| New project's Recap: Start over | Start over, beside Close in the footer while no fact was chosen | Start over, the recap's last row, on its last page, chosen as a fact is; chosen, Start over stands in the footer's middle and asks, over Yes, start over | The coordinator, 2026-10-03 (never four prompts) |
| New project's Recap: what Start over clears, in its side panel and its confirmation alike | "This clears your idea and every choice here.", on the confirmation only, though the agent app and model stay | For a new project: "This clears your idea, its answers, the name, first task and folder. How it runs stays." For a task added to a project, which keeps its name and folder: "This clears your idea, its answers, the first task and any change of folder. The project and how it runs stay." | The coordinator, 2026-10-03 |
| New project's Recap: the companion's note on a side panel showing its suggested name or first task | The companion is an AI on your computer. It can be wrong, and you can change everything before you start. (cut short beside the page at standard text, a note having two rows of a 26 degree column) | An AI on your computer suggested this. It can be wrong. The page keeps the whole note, and the side panel's Change stands beside it | The coordinator, 2026-10-03 |
| New project's How it runs, its models: another agent app | Change agent app, in the footer's middle | Change agent app, an action row before the models, as Type my answer is, so the footer's middle is free for Next page | The coordinator, 2026-10-03 (never four prompts) |
| Start building while a build is on its way, its reason as the page's last line | None: Start building did nothing and said nothing | Already starting. Wait to hear how it went. | The coordinator, 2026-10-02 |
| A place, where two share a name | "Projects" and "Projects", told apart by nothing | "Projects (person)" and "Projects (Work)": the place's own name, then the nearest folder above that tells them apart; where one is not enough, more, in path order, "Projects (Personal, Work)"; where nothing above tells them apart, a number, "Projects (old) 2"; never a path. The host names them (`label`), so Projects, New project and every listing say the same | 2026-10-02, by the coordinator |
| A kept choice whose place is gone | The old place name, kept with the draft | "{folder}, in a place your computer no longer lists" | 2026-10-02, by the coordinator |
