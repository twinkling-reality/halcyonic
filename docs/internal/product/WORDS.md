# How Halora speaks

Canonical specification for Halora's on-screen copy on the headset and companion Mac pages: voice
principles, state badges, action titles, provenance rules, refusal patterns, and how outside text is
marked. Headset copy lives in `apps/xr/Packages/com.halcyonic.client` (`StateLanguage`, `EntryText`,
`WorkspaceText`, `ConnectionText`, `VoiceText`, `RuntimeFaces`, `SpendingLimit`, `EvidencePage`,
`FileChangeText`, `RoomBar`, `GalleryText` and their kin,
[ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)), and
command titles live in `packages/domain/src/policy.ts` (`COMMAND_SAYING`). Visual tokens and layout
live in [STYLE.md](STYLE.md).

## The voice

Calm and plain, like a capable colleague who states what is true now, how certain it is, and what
the person can do next.

1. **Controls, headers and stages are 1 to 3 words in Title Case.** Each names the exact system
   concept (`Needs You`, `Read Command`, `Activity`, `Send`), never a conversational clause.
2. **No facts strung on dots where a person reads rows, buttons or pages.** On Mac pages and in host
   strings (`tooling/no-dot-rows.test.ts`), on buttons (`RoomButton` splits any `" · "` onto
   separate lines), in `Nothing was sent:` notices (`DotJoinedWordsTests`), and on result and
   evidence cards, facts stand as separate labels, slots or plain sentences. The `" · "` cadence is
   used only on compact headset metadata flows (`KitFlow` on `KitMoment` and legacy `Glaze` lines),
   where line breaks occur between segments with the dot dropped so no line starts or ends with a
   dot.
3. **Only what is true now, and how sure.** `Sending`, `Sent` (`Waiting for the agent to start.` or
   `Waiting for the agent.`), `Confirmed` only once the runtime confirms, `Refused`, `Unconfirmed`,
   and `Not Sent` wherever a press sent nothing. A finished round says nothing about whether the
   software is right, so nothing celebrates it.
4. **Lead with the state or action, then the cause, then the next step.** Refusal and cancellation
   notices use plain sentences (`Nothing was sent: your answer changed. Read it again.`), blaming
   neither the person nor the agent.
5. **Buttons begin with a verb in Title Case.** Confirmations use `Confirm` beside `Cancel` under a
   question naming the exact action (`Approve Request?`, `Deny Request?`, `Send Instruction?`,
   `Send This?`, `Stop Task?`), or name the reviewed action directly on a single-press review card
   (`Add to Project`). An icon may stand before a button's words, never in their place.
6. **Familiar names for the work and its actor.** `Projects`, `Tasks`, `Activity`, `Changes`,
   `Checks`, `Reasoning`, `Outcome` and `Send` say what the person can do. `Agent` and `assistant`
   are ordinary nouns. `Halora` (`WorkspaceText.Product`) names the workspace; `your computer` or
   `This Computer` names the connected host.
7. **Outside text is never presented as Halora's own words.** Titles, paths, commands, agent prose,
   tool output, source reasons and the person's typed or transcribed words pass through `LabelText`
   (`Plain`, `Exact`, `ForTextMeshPro`): rich-text markup is disabled, backslashes are escaped, and
   control, format, default-ignorable, noncharacter and Private Use Area code points render
   literally as `‹U+XXXX›` (with `⇥` and `↵` in command/code wells,
   [ADR 0065](../decisions/0065-a-request-too-long-to-show-whole-is-denied-by-the-host-with-its-reason.md)).
   See [Words from outside](#words-from-outside).
8. **No exclamation marks, emoji, em dashes or all-caps emphasis in prose.** Use numerals for
   numbers. Eyebrow labels in the room's `Label` role render in spaced uppercase through
   `RoomType.Label`, while the underlying strings stay in Title Case.
9. **Source modes stay explicit beside the state.** `Practice`, `Demo`, `Recorded` and `Last Known`
   appear beside a state badge, never inside the state word itself.
10. **No vendor or sibling brand names on headset faces.** Sibling products (`Salidium`, `Seorak`)
    are never drawn in any headset build (`tooling/sibling-names.test.ts`; see
    [A provenance line](#a-provenance-line)). Agent apps and the host are named on faces by what
    they are (`RuntimeFaces`, `CaptureNames.Host`), while real runtime and model names stay one
    press deeper on pickers and details.

## Agent faces and host naming

A character in XR represents a Workstream, never a vendor session. Across headset faces (tags,
moment cards, project screens, `Activity` lifecycle entries, `New Project` recap/review summaries,
and `KitComposer` chips), Halora names an agent app by what it is (`RuntimeFaces.Head`) and names
the host `This Computer` (`KitMomentWords.ThisMac` / `CaptureNames.Host`):

| Runtime / host | Face label (`RuntimeFaces.Head`) | Picker subtitle (`RuntimeFaces.Line` / `SecondLine`) |
| --- | --- | --- |
| Synthetic (`mock`) | `Practice Run` | `Pretends to work; nothing real is done.` |
| `codex` on `ModelServed.ThisMac` | `Local Models` | `Runs only on models served on this computer.` |
| `claude-agent` | `Hosted Models` | `Runs on models its maker or your cloud provider serves, with your own key.` |
| `codex` on `ModelServed.Remote` | `Hosted Models` | `Runs on a model a provider serves online.` |
| `opencode` | `Your Model Setup` | `Runs on the models your own setup lists, on this computer or online.` |
| `antigravity`, `kiro`, `cursor` | `Signed-In App` | `Uses its own sign-in and its own models.` (`Always asks before it acts.` on Kiro; `Asks before it reads outside the project.` on Antigravity) |
| Unknown kind or mixed Codex | `Coding Agent` | `Runs on the models it lists, on this computer or online.` (Codex) |
| Host environment (`MayLeave` / `StaysInside`) | `Host: This Computer` / `Host: Sandbox` | Moment eyebrow: `Observed on This Computer` |

On the worker button at rest, on the working plate and on `New Project`'s recap card, the worker is
titled `Agent` (`Practice Run` when synthetic). The runtime's actual display name and model id
appear one press deeper where the person chooses or inspects them (`How It Runs`, `Use Model`, and
agent details via `RuntimeFaces.DetailsName`), unless development capture mode (`CaptureNames.Generic`)
substitutes `Coding Agent` and `AI Model` for footage capture.

## Starting a task and approval levels

- **Starting:** `New Task` opens an existing project's task draft without embedding outside project
  names into the button label. In `New Project`, the final step is `Start` / `Start Task`, confirmed
  by `Confirm` (or `Yes, start task` on legacy menus). Starting launches the selected runtime
  execution; it never claims a finished build or deployment.
- **Before It Acts (`PlanStart`, `AgentColumn`):** over the start-policy choices on the agent page
  and `New Project`'s change page stands `Before It Acts`, with three person-facing options:
  - `Ask Me First` (default supervised mode)
  - `Don't Ask Me` (`never_ask`, offered only where the runtime holds its bounds,
    [ADR 0043](../decisions/0043-a-person-may-let-a-tasks-agent-work-without-asking-where-its-runtime-keeps-it-in-bounds.md))
  - `Show Plan First` (`offers_plan`, where the agent plans first and rests at `Plan Ready` with
    `Carry Out Plan` as its main action)
- **Command titles (`COMMAND_SAYING` in `packages/domain/src/policy.ts`):** every command uses a
  1-to-3-word Title Case verb phrase with no brand name: `New Project`, `Change Folder`, `Project
  Rules`, `Make VR Copy`, `Restore`, `Check Project`, `Choose Check`, `Play`, `Stop Playing`,
  `Play on Computer`, `Show Folder`, `Save a Copy`, `Share`, `Stop Sharing`, `Add Secret`, `Remove
  Secret`, `Add Media`, `Send for Review`, `Publish`, `Take Down`, `Make Private`, `Link
  Repository`, `Unlink Repository`, `Remove From List`, `New Task`, `Add to Project`, `Fit to
  Project`, `Start`, `Send`, `Approve`, `Answer`, `Stop`, `Drop`, `Ask`, `Cancel` and `Use Model`.

## The word for each state

One word per state across badges, peeks, workspace headers and lists (`StateLanguage.WordOf`):

| State | Badge | In a line |
| --- | --- | --- |
| No run yet | `Not Started` | `Not Started` |
| Asked to start, not yet confirmed | `Starting` | `Starting · Unconfirmed` |
| The agent is at work | `Working` | `Working · 2 Tasks` |
| Agent-reported test run in progress | `Checking` | `Checking · Reported` |
| Approval or question pending | `Needs You` | `Needs You · 1 Task`; `Needs You · {title}`; `Needs You · 2` when multiple items wait |
| Round ended without failing checks | `Round Ended` | `Round Ended · Ready to Review` |
| Round ended with failing checks | `Checks Failed` | `Checks · 1 Failed · 23 Passed · Observed` (or `Reported`) |
| Execution failed | `Failed` | `Round Failed · Write Instruction`, or `Round Failed · Re-Add Task in Projects` when unsteerable |
| Stopped and runtime-confirmed | `Stopped` | `Stopped · Confirmed` |
| State cannot be verified | `Unknown` | `Status Unknown`; `Status Unknown · Agent App Unreachable`; `Status Unknown · Computer Restarted`; `Status Unknown · Start Unconfirmed` |

A finished round never says `Done` or `Complete`. `Running Tests` and `Running Command` remain tool
step descriptions while `Working` names the task state. In summary counts, `paused` covers tasks
that ran previously and now rest, while tasks with no execution read `not started`.

## What is happening, in steps

A task's panel lists its lifecycle steps from journal facts alone (`TaskSteps`), each marked done,
current or upcoming with at most one subtitle line:

| Step | When shown | Subtitle line |
| --- | --- | --- |
| `Your Request` | Task has an instruction | None (the instruction stands in the file, not repeated here) |
| `Planning` | Started with `Show Plan First`, until leaving plan mode | None; a finished plan rests before `Working` with `Carry Out Plan` |
| `Working` | Active agent turn (including agent-run tests) | Live step (`Editing index.html`, `Reading src/main.ts`, `Running Tests`, `Running Command`); once ended, `Changed 3 Files` from host observation |
| `Check` | Turn ended and no current host check covers the files | Starter app: `Builds the app and opens it.`; custom check: `Runs the project's own checks.`; none configured: `Choose a check for this project first.` |
| `Checking` | Host `Check Project` under way | None |
| `Ready` | Host check passed on current files | None |

Projects with no host check (such as `world` projects) omit `Check` and `Ready` so the list ends at
`Working`. A pending approval or question marks the active step with `Needs You: Run Command?` (for
commands) or `Needs You` (never a raw tool identifier). Tasks ending short of `Ready` end on `Checks
Failed`, `Partly Checked`, `Starter Only`, `Stopped`, `Failed`, or `Can't Tell` with its known
cause.

`Watch It Work` (`WorkspaceText.WatchCount`) reports host-observed file counts in matching words:
`No Files Changed Yet` or `Changed 3 Files So Far` (`Changed 1 File So Far`) during a turn, and
`Changed 3 Files` after a turn that changed files.

Under the steps at rest, `TaskUsage` shows `Tokens Used` in whole digits (`210,700`, with `At least
this much` and `Some parts not reported` or `2 of 3 turns reported` when partial, or `Not Reported
Yet` / `Not Reported by This App`, never `0`) and `Cost` only when the agent app supplied an
estimate (`By the agent's count`, preserving significant digits below one cent).

## Result, check, media, usage and code-colour pages

- **Changes and file reader (`RoomPanelCards`, `FileChangeText`):** `Changes` carries `What this
  task changed in the project.` under its title, listing each file with its kind (`Edited`, `New`,
  `Removed`) and line counts (`{n} Lines`, or `+{added}` / `−{removed}` from checkpoints). Opening a
  file shows its name and path, `Page {n} of {pages}`, `Reading File`, `Whole File` (`As it is now:`
  followed by path, or `As it is now.`), or truthful empty/refusal states: `Nothing to Compare`
  (`The computer kept no earlier version of this file` or `The earlier version was not found`),
  `Kept Private` (`Its name says it may hold secrets`), `Too Large to Show` (`Over 262 KB`, from
  `PROJECT_FILE_BYTES`), `Not Text`, `Empty File`, `The rest is too long to show here`, and `Not
  Read` (`The computer did not answer` with `Try Again`, `The computer's reply can't be shown`, or
  `Reason unknown`). Kept checkpoints are titled `Earlier Versions`.
- **Checks and automatic fixes (`CheckSteps`, `RoomResult`):** starter check rows are `Code Check`,
  `Builds`, `Starts and Shows` and `Has VR Mode` (`Has VR Mode` means the page offers WebXR, not
  that a headset session ran it); custom scripts read `Own Checks`. Steps after a failure read `Not
  Run`, never a blank box; a failure names the step (`The check stopped at Build.` or `The
  computer's check stopped on a step`). While automatic repair ([ADR 0044](../decisions/0044-a-failed-check-goes-back-to-its-agent-on-its-own-within-a-cap.md))
  runs, the card shows `Fixing` over `The agent tries again on its own.`; if the cap is exhausted,
  it shows `The agent tried twice and could not fix it.` Result cards show `All Passing`, `Checks
  Failed`, `Partly Checked` (`What was checked passed; one thing could not be checked.`, plus the
  three-sentence `No visible change to the page. ... Look and listen to judge it.` note when files
  changed without visual pixel change), or `Starter Only` (`Nothing new is drawn yet. Say what you
  want to see.`). An unconfined run shows `Not Kept to Folder` (never `confined`, `sandbox` or
  `seatbelt`).
- **Outcome and Reasoning (`EvidencePage`, `RunResults`, `RunUnderstanding`):**
  - `Outcome` reads Seorak's evaluation under eyebrow `Measured` (or `Simulated`) with sections
    `Activity` (`Time`, `Tool Calls`, `Instructions`), `Changes` (`Files Changed`, `Lines Added`,
    `Lines Removed`, `Commits`, `Errors`, `Lines Kept`), `Checks`, and `Cost` (`Estimated`),
    omitting nulls and unavailable fields rather than inventing zeros or grades. Unread or missing
    states use `Not Measured`, `Measurement Unavailable`, `Measurement Unreadable` or `Measurement
    Not Allowed` (`Nothing has been read for this task yet.` before the first read).
  - `Reasoning` reads Salidium's understanding under eyebrow `Observed`, `Reported` or `Simulated`,
    leading with the source's verdict headline and reason over `Runs` (`Passed`, `Failed`, `Partly
    Passed`, `Can't Tell`, `Out of Date`), `Changes` (`Files`, `Commits`), `Review` (`Open`,
    `Resolved`) and `Remaining`. Unread or missing states use `No Conclusions`, `Conclusions
    Unavailable`, `Conclusions Unreadable` or `Conclusions Not Allowed`.
- **Photos and videos (`TaskMedia`, [ADR 0066](../decisions/0066-a-persons-photo-or-video-goes-into-the-projects-folder-as-a-copy-without-its-place.md)):**
  refusals use plain sentences: `That photo or video is no longer kept on this computer. Add it
  again.`, `That photo or video is already in this project. Nothing was added.`, and `This project
  already holds as many photos and videos as it can keep. Add it to another project.`
- **Usage limits and stage banner:** provider gauges are `5-Hour Limit` and `Weekly Limit` with
  `{n}% Left` (in demo playback: `Practice agent · 5-Hour Window`, `Practice agent · Weekly`,
  `Recorded for Demo · No Account`, `Limits Part of Recording`, `Part of Recording`). Beside an
  external window, the stage banner reads `{n} more tasks not shown here` (`1 more task not shown
  here`) and `Still Open: {panel}` whole without truncation.
- **Code Colours (`AppearanceColumn`, `CodeColouring`):** `Appearance` offers `Code Colours` with
  `Halora` (`Soft colours for each kind of code.`), `Plain` (`All code in white.`) and `High
  Contrast` (`Brighter colours, set further apart.`), `Current` on the active theme, `Preview
  Colours` showing a `PREVIEW` specimen card (`// Top 3 tasks`, `let t: Task[];`, `log("Top", 3);`),
  `Apply Colours` and `Cancel`.

## The words a person sees

| Word | Means | Never say |
| --- | --- | --- |
| `project` | One body of work in one folder | |
| `task` | One durable Workstream, with or without a room character | workstream, work item |
| `work` | What agents do in general | execution |
| `agent` | The AI working on a task | runtime, bot |
| `round` | One stretch of agent work until it pauses or finishes | turn (on legacy surfaces) |
| `approval` | A specific permission decision requested by the agent | |
| `question` | A prompt the agent asks the person | prompt |
| `checks` / `tests` | `Tests` when test-run evidence exists; `Checks` for broader verification | |
| `your computer` / `This Computer` | The connected host running the control plane | control plane, server, Mac (on headset faces) |
| `folder` | Where a project's files live | location, directory, path |
| `agent app` | An underlying runtime when discussed as a category in setup/details | adapter |
| `sandbox` | Process confinement, named only on command approvals where [ADR 0033](../decisions/0033-approvals-declare-whether-commands-leave-the-sandbox.md) applies | seatbelt, permission profile |
| `practice run` | Simulated runtime execution (`Nothing is built`) | mock |
| `demo` / `recorded` | `Demo` for the interactive scripted walkthrough; `Recorded` for a replayed real run | |
| `assistant` | The AI helping shape an idea or answer `Ask` requests | |

A person never reads internal domain identifiers: `execution`, `journal`, `command`, `principal`,
`scenario`, `capability`, `projection`, `snapshot`, `workstream` or `access token` (`access code` is
used where a development build requires token entry).

## Words from outside

Outside text (`LineWords.Theirs`, `TheirName`) is never silently merged into Halora's voice:

- **Attribution chips:** reported lines carry `Agent · Reported`, `Subagent · Reported`, `Author
  Unknown`, `Explanation · Reported`, `Inferred`, `Planned` or `Agent's Question`. Observed host
  facts and Halora's own labels carry no chip.
- **Presentation:** in room faces (`KitType` with `room: true`), outside text stands upright,
  distinguished by its region, chip and monospace well (`KitWell`). On `Glaze` plane surfaces, mixed
  lines lean in italic on a subtle token ground (`Failing · {x}`, `To Do · {x}`, `In Progress ·
  {x}`) and whole-block rows carry `GlazeIcon.Quote`; on `KitWorkView`, whole-block attributed agent
  prose and the focused instruction input stand upright while mixed lines keep their lean and
  ground. Outside text is always shortened before Halora's words (`LineWords.CutToFit`).
- **Recorded and demo playback notices (`DemonstrationFallback`, [ADR 0035](../decisions/0035-a-recorded-real-run-lets-anyone-try-halcyonic-before-connecting-a-computer.md)):**
  - Banner: `Recorded: real agent work, played back here. Nothing here is live. It waits only where
    the agent waited. Nothing reaches an agent.`
  - End transitions: `Run ended. Next, a demo you can answer. Nothing reaches an agent.`, `Run
    ended. It starts again soon. Nothing reaches an agent.`, `Demo ended. Next, the recorded run.
    Nothing reaches an agent.`, `Demo ended. It starts again soon. Nothing reaches an agent.`
  - Composer / decision notices in recordings: `This is a recording, so nothing can be sent.`; `Not
    sent to any agent. This was approved when it ran, so the run goes on as it happened.` (or
    `denied`); `Nothing is sent to an agent. The recording goes on as if you had chosen its recorded
    answer.`

### A provenance line

No headset screen in any build draws a sibling product's name or version (`Salidium`, `Seorak`;
enforced by `tooling/sibling-names.test.ts` and `SourceWords`):

| Where | Provenance words |
| --- | --- |
| Understanding (`Salidium` or stand-in) | `Explanation, 2 min ago` (`Just Now`, `3 h ago`, `30 Oct 14:02`; `Recorded {time}` in a recording); stand-in: `Simulated Explanation, {when}`. On `Reasoning` (`EvidencePage`): `Observed`, `Reported` or `Simulated` |
| Evaluation (`Seorak` or stand-in) | `Measurement, Read 2 min ago`; stand-in: `Simulated Checks, {when}` or `Simulated Measurement, {when}`. On `Outcome` (`EvidencePage`): `Measured` or `Simulated` |

## Instructions, questions, approvals and spending limits

- **Instructions (`FileScreens.Instruction`, `RoomBar`, `VoiceText`, `WaitingText`):**
  - At rest on `RoomBar`, `Hold to Talk` and `Type` stand side by side (`Choose Instruction` with
    `Continue`, `Summarize` and `Run Checks Again` when no keyboard is available). While recording,
    `Hold to Talk` reads `Listening`; while transcribing, `Typing It Out`
    (`VoiceText.WritingDownWords`).
  - `Write Instruction` opens `Your Instruction` (placeholder `Instruction` with a keyboard, `Hold
    to Talk` without). The explicit send button is `Send` (`FileScreens.SendWords` and
    `COMMAND_SAYING['execution.send_instruction']`; `RoomBar` shows `Send Instruction` on typed
    drafts, and `KitComposer` shows `Start New Run` when switching runtimes).
  - Unread multi-part drafts lock the button as `Read Full Instruction` (`Next Part` / `Previous
    Part`). Spoken drafts require confirmation under `Send This?` (with `Talk Again` and `Cancel`);
    policy-reviewed typed drafts ask `Send Instruction?`.
  - Queued instructions on runtimes that cannot steer ([ADR 0056](../decisions/0056-an-instruction-sent-during-a-turn-waits-for-it-on-a-runtime-that-cannot-steer.md))
    show `Waiting` (`Each is sent when this turn ends.`) and `Waiting for Its Turn` (`It is sent
    when this turn ends`, with `Drop` and `Back`), or `Not Sent` with the cause (`The turn before it
    ended in an error`, `The turn was stopped before it ended`, `The agent's session could not be
    picked up again`, `The project's files were put back`, `You dropped it`) and `Send Again`.
- **Spending limits (`SpendingLimit`, [ADR 0062](../decisions/0062-a-tasks-spending-limit-is-the-apps-own-stop-handed-only-what-is-left.md)):**
  - At start (when `OffersSpendingCap` is true): `Spending Limit` offers `No Limit`, `$1`, `$5` and
    `$10`; the recap card shows `Stops Near $5.00` or `No Limit`, and the review shows `Limit $5.00`
    over `It stops near $5.00 by the app's own estimate; it can go over on its last step`.
  - On a task: an open cap shows `Limit $1.00`; a reached cap shows `At Spending Limit` with `Spent
    $0.30 of $1.00` (or `Limit $1.00` when spent is unknown); a post-restart cap with unknown prior
    spend shows `Spending Not Known` with `Limit $1.00`. Its composer asks `Spend Up To` over `$1
    More`, `$5 More` and `$10 More` with `Counts from now.`, locks `Send` with `Choose an Amount
    First` until an amount is pressed, and confirms with `Send Instruction, Spend Up To $5.00 More`.
- **Agent questions (`QuestionDraft`, `RoomBar`):**
  - Progress and selection: `Question 1 of 2 · Choose One` or `Choose Any` (`· Choices 1 of 2` when
    paged), paged by `More Choices` and `First Choices`, advancing via `Next Question`, `Next Part`,
    `See Answers`, `Review Answers` and `Send Answer` (confirmed under `Send Answers?`).
  - Locked reasons: `Read Full Question`, `Read Full Answer`, `Read All Answers`, `Read This Part`,
    `Write Answer First`. Custom answers use `Write Answer`, `Your Answer`, `Use Answer` and `Edit
    Answer`.
  - Duplicate choice labels (`WorkspaceText.AsShown`) cannot be selected: the unavailable row reads
    `Unavailable. Open it to read why.` -> `Duplicate Choices` (`These share a name, so none can be
    chosen: {words}`), or `Duplicate Answers · Not Answerable Here · Stop Task` when no valid option
    remains; typing a matching label warns `That matches answers that share a name: answer
    differently, or stop the task`.
- **Approvals (`WorkspaceSteering`, `KitMoment`):**
  - Heading / sentence: `Run Command?` / `Run Command`, `Use {tool}`, or `Approval Requested`. When
    declared by the adapter ([ADR 0033](../decisions/0033-approvals-declare-whether-commands-leave-the-sandbox.md)),
    sandbox scope reads `May Run Outside Sandbox · Your Access` or `Runs Inside Sandbox`.
  - Actions: `Approve` and `Deny` (or `Run`, `Decline` and `Reply` on moment cards / `KitReader`),
    followed by `Read Full Request` or `Approve Request?` / `Deny Request?` with `Confirm` and
    `Cancel`. Requests exceeding the 2,000-character approvable bound or marked incomplete offer
    only `Deny` / `Decline`.

## Connection, pairing, access code and unknown-effect patterns

- **Unknown-effect pattern (`WorkspaceText`):** when delivery or execution cannot be ruled out,
  Halora uses `{What} Unconfirmed · {What to Check}` (for example `Delivery Unconfirmed · Check
  Activity`), never claiming the command failed without effect.
- **Unsent / lapsed confirmations:** `Nothing was sent: the request changed. Read it again.`,
  `Nothing was sent: the question changed. Read it again.`, `Nothing was sent: your answer changed.
  Read it again.`, `Nothing was sent: the task changed.`, `Nothing was sent: the text size changed.
  Read it again.`, `Nothing was sent: the confirmation timed out. Press again.`, `Nothing was sent:
  a request is already under way.`, `Nothing was typed: the question changed. Read it again.`
- **Connection status (`ConnectionText`):** a computer that does not answer reads `Can't reach your
  computer; trying again` (or `Computer Unreachable · Retrying` in compact cadence), keeping raw
  socket/DNS/TLS exception text in `ConnectionStatus.ForLog` rather than on screen. Refused or ended
  connections speak by code (`Version Mismatch · Install Same Version`, `Message Unreadable ·
  Install Same Version`, `Too Many Connections · Restart App`, `Host Not Allowed · Retrying · Pair
  Again if Repeated`, `Computer Busy · Too Many Tries · Retrying`, or a certificate mismatch warning
  to forget and pair again).
- **Wi-Fi pairing failures (`PairingCards.Why`):** `Computer Not Found`, `Code Incomplete`, `Pairing
  Refused`, `Pairing Closed`, `Code Refused`, `Pairing Paused`, `Computer Busy`, `Timed Out`,
  `Connection Lost`, `Can't Pair`, `No Secure Connection`, `Not Verified`, or `Reason Unknown`.
- **Intelligence / catalog / folder read failures (`IntelligenceText`):** leads `No Conclusions`,
  `Conclusions Unavailable`, `Conclusions Unreadable`, `Conclusions Not Allowed`, `Not Measured`,
  `Measurement Unavailable`, `Measurement Unreadable` or `Measurement Not Allowed`, paired with
  code-specific reasons (`Not Running · Start It · Refresh`, `No Answer · Refresh Soon`, `Busy ·
  Refresh Soon`, `Too Many Requests · Refresh Later`, `Credential Missing · Set Up on Computer`,
  `Credential Wrong Kind · Not Sent · Set Up Another`, `Credential Refused · Set Up New One`,
  `Credential Lacks Access · Set Up Another`, `Credential Covers Other Work · Set Up Another`,
  `Task Not Seen Yet · Refresh Soon`, `Not Computed Yet · Refresh Soon`, `Task No Longer Kept`,
  `Agent App Not Followed`, `Task Not Found by Name`, `Result Limit Reached · Refresh`, `Answer
  Unreadable · Check Version on Computer`, `Request Turned Away · Check Setup on Computer`, `Wrong
  Source Answered · Check Setup on Computer`, `Setup Unreadable · Check on Computer`, `Session
  Unknown Yet · Refresh Soon`, `No Reason Given · Refresh`) without naming sibling products.

### The headset's access code

Where a development build uses the loopback access token over USB (`LoopbackProof`,
[xr-loopback-proof.md](../validation/xr-loopback-proof.md)), person-facing copy calls it the `access
code`:

| Condition | Exact message |
| --- | --- |
| Control plane answered `401` | `Your computer refused this headset's access code: it doesn't match your computer's. Put your computer's current access code on the headset, then restart the app.` |
| Endpoint failed loopback proof (token withheld) | `This headset's access code doesn't match your computer's, or something else is answering in its place, so the headset didn't send it. Put your computer's current access code on the headset, check that this app is running there, and restart the app.` |
| Non-loopback or disallowed scheme configured | `The access code goes only to ws:// or http:// at 127.0.0.1 or [::1], so it was not sent to {endpoint}. Name one of those instead.` |

## Workspace organization

`Move` / `Move Workspace`, `Compact` / `Minimize Workspace`, `Expand` / `Open Workspace`, `Reset
Position` and `Other Apps` (`Open Work Window`) are workspace controls separate from task decisions.
`Compact` / `Minimize Workspace` preserves the open view and unsent drafts; `Close` leaves or
discards the local context. Holding `Move` drags the workspace (`Done Moving` exits explicit move
mode); `Reset Position` restores default placement. Below an external window, placement details read
`The workspace stays below the window. Choose where the characters stand.` with `In front of you`,
`Room for a window` and `Either side of a window` applied on `Apply`. `Settings` groups `Appearance`
(`Glaze`, `Graphite`, `Neutral`, `Code Colours`, `Restore Default`), `Comfort` (`Motion`: `Keep
Still` / `Allow Motion`, text size), `Your Space` (`Surroundings`, `Room Layout`, `Agents`, `Agent
Shelf`, development `Boundary` with `Hide Boundary` / `Show Boundary` / `Stop Asking`, `Menu`) and
`Your Computer` (`Pairing`: `Pair Computer` / `Forget Computer`).

In the native Android continuation panel, restoration check wait text reads `Checking your computer
before restoring this view`, and the button back to Unity XR reads `Open Halora`.

## The Projects panel

The launch panel (`RoomBar.Projects`):

| Part | Words |
| --- | --- |
| Eyebrow over title | `Halora` (`WorkspaceText.Product`, rendered in spaced capitals by `RoomType.Label`) |
| Title | `Projects` |
| Top-right action | `Menu` (`Move Workspace`, `Reset Position`, `Minimize Workspace`, `Other Apps`) |
| Column headers | `Project` / `State` |
| Row state word | Single state word (`Needs You`, `Ready`, `Working`, `No Work`, `Paused`, `Not Started`) |
| Footer actions | `New Project`, `Next Page` (when paged), `Settings`, `Usage`, `Close` (where offered) |

## The room of projects

Copy for [ADR 0040](../decisions/0040-the-headset-is-a-room-of-projects.md)'s room surfaces
(`KitMomentWords`, `KitScreenModel`, `RoomBar`):

| Surface / moment | Words |
| --- | --- |
| Gallery header and controls (core `GalleryText`) | `Projects · {n} · {groups} Groups`; `Say Idea` (when speech is set up) or `Type Idea`; `Open Project`; `New Project`; `Needs You · {n}`; `+{n} More`; `Find Project` |
| Screen chip and screenshot stamp | `{project}`, then `{face} · {state}` (`RuntimeFaces.Head`, such as `Local Models · Ready`); `Last Check · Today {time} · Observed` (or `Last Check · {d MMM} {time} · Observed`) |
| Screen bar | Primary (`Make VR Copy` for a non-VR project, else `Request Change`), `Request Change` (when secondary), `Try Build` (only after a passed host check on current files with no active turn), `Activity`, `Reasoning`, `Outcome`, `Close` |
| Task `More` routes | `Place in Room`, `Ask About This Task`, `Character Colour`, `Show Project Screen`, `Back` |
| Request sent receipt | Eyebrow `Observed on This Computer`; title `Sent`; subtitle on its own line `Waiting for the agent to start.` or `Declined` (never `Started`) |
| Runtime-reported file edit | Eyebrow `The Agent Says`; title `Edit Reported`, `New File Reported` or `Removal Reported`; `{path} · Not Yet Observed` |
| Host-observed file change | Eyebrow `Observed on This Computer`; title `Change Observed`, `File Made` or `File Removed`; `{path} · {before} to {after} Lines · {time}` (or `+{added} −{removed} Lines` from checkpoint diff) |
| Host check moment | Eyebrow `Observed on This Computer`; `Computer Check` (`Builds`, `Starts and Shows`, `Checking`) while running; then `Build Passed`, `Build Failed` or `Check Ran` (`Can't Tell`) |
| Agent-reported check | Eyebrow `The Agent Says`; title `Check Reported`; command in well |
| Ready moment | Eyebrow `Observed on This Computer`; title `Build Ready`; project name, `Builds`, `Starts and Shows` (when opened) and `Took {duration}` on separate lines |
| Command approval moment | Eyebrow `Agent's Question`; title `Run Command?`; command in well; sandbox note (`May Run Outside Sandbox · Your Access` or `Runs Inside Sandbox`); actions `Run`, `Decline`, `Reply` (`Read to End` while unread; `{n} More Lines Not Shown` when clipped on card) |
| Agent note / skipped read / missing package | `The Agent Says` / `Agent Note`; `Observed on This Computer` / `Not Observed`; `Observed on This Computer` / `Not in Starter` (`Install Not Run`) |

## The projects gallery

Catalog state mapping (`GalleryText`, [ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)):
Omnorium states are always marked `Reported` so they never impersonate host-observed task states:

| Catalog wire state | Shelf group | Shown line |
| --- | --- | --- |
| `awaiting_input` | `Needs you` | `Agent Waiting` |
| `session_failed` | `Needs you` | `Agent Failed` |
| `checks_failing` | `Needs you` | `Checks Failing` |
| `review_requested` | `Needs you` | `Review Requested` |
| `changes_requested` | `Needs you` | `Changes Requested` |
| `ready_to_merge` | `Needs you` | `Ready to Merge` |
| Unknown catalog state | `Needs you` | `Needs a Look` |
| `agent_running` | `Working now` | `Agent Running` |
| `session_complete` | `Recent` | `Agent Finished` |
| `active` | `Recent` | `Changed Recently` |
| `inactive` | `Quiet` | `Quiet` |
| `archived` | `Quiet` | `Archived` |
| `access_lost` | `Quiet` | `Unreachable` |

Unlinked catalog entries read `Not Linked` (with `Possible Match · {folder}` marked `Inferred` only
from host matching). Gallery freshness reads `Checked · Today {time}`, `Checked · {d MMM} {time}`,
`Refresh Failed · Last · {when}`, `Refresh Failed` or `Not Checked`; sample and recorded modes read
`Sample Projects` and `Not in Recording`, and live catalog unavailability lines (`Omnorium · Not Set
Up · Set Up on Computer`, `Omnorium · Not Running`, `Omnorium · Gathering · Refresh`, `Omnorium ·
Access Refused · Set Up Again`, `Omnorium · Version Unsupported`, `Projects Unreadable · Refresh`)
appear only against a live control plane, never in demo or recorded builds.
