# The competition's judge build

- **Question:** Does the build judges run, the release APK playing the recorded demonstration
  ([ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md)), meet the
  competition's rules and show today's product: the interface of
  [ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md),
  agent questions ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)), Waiting for
  you, the three arrangements, Usage left and the sound cues, with a satisfying moment well within
  10 minutes, seated and with hands only, from a cold start?
- **Date:** 2026-10-02.
- **Environment:** `main` at a214dce on an Apple M5 Max with macOS 26.7; Unity 6000.3.25f1 in batch
  mode with the Android target; the client core on .NET 10. No headset.
- **Method:** The competition's rules page and Meta's device comparison, read on 2026-10-02. The
  bundled recording (`HalcyonicDemonstration.json`) read event by event. `JudgeWordsTests` walks
  every point of every path of the recording and gathers every word the client core gives the
  headset there (badges, marks, peeks, every workspace tab, the recorded sections, the line above the
  stage, Connect projects, More tasks, Create, Usage left and Settings), then looks for names of
  products, companies and platforms. The release APK built with `BuildReleaseApk` and read with
  `aapt2` and `apksigner`. The stage, workspace, entry and Usage left renders and the interface
  measure (`MeasureRender`), all passing. The layout's angles read from the code.
- **Status:** Off the device only. Every device claim below (frame rate, the field's real split,
  pause and resume, cold start) waits for the headset session; the tools for it are in
  [XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md), "Device measures on a Quest".

## The rules that bind the judge build

From [the rules](https://start-developer-competition-26.devpost.com/rules), read on 2026-10-02 (the
page gives no update date):

- Content: no "commercial or corporate advertising (including, without limitation, corporate
  logos, brand names, and slogans)", and no implied association with the sponsor. The clause has no
  exception for an entrant's own products.
- Hands first: the whole experience without ever pairing a controller.
- Guidelines: seated, a fast cold start, a clean pause and resume, a satisfying moment within 10
  minutes; essential UI and interactions "within a comfortable, narrower FoV and adapt across
  devices"; at least 60 frames a second.
- The APK goes into a release channel named "Competition" and cannot change after the deadline.

## What a judge meets

The release APK has no access token and offers no pairing (`PairingBootstrap` runs in development
builds only), so it always plays the demonstration. From the recording:

| At | What happens |
| --- | --- |
| 0 s | Three characters, Not started; the line above the stage: "Demo: recorded work played on this headset. Nothing here is live." and "It follows your answers. Nothing reaches an agent." |
| 0.3 and 0.6 s | The two watch-only tasks start working |
| 4.8 s | Their checks finish; at 5.3 s both read Finished this round |
| 5.7 s | The directed task, "Add rate limiting to the sign-in endpoint", starts |
| 6.6 s | It is Waiting for you with the agent's question, how long a locked address waits (15 minutes or 1 hour); the hint "Look, then pinch" stands over it until a workspace has been opened once; the recording holds until the judge answers |
| answer + 2.7 s | Waiting for you again, for a request to run `make migrate`; it holds again |
| approval + 3.4 s | After Approve: the migration, "Checks failed" (1 failed, 23 passed), the round ends; two recorded instructions are offered for 60 s, counted from when the file closes if it is open |
| next round | Either instruction plays a second round whose checks pass; then the end holds 20 s, counted the same way, and the demonstration starts again |

The first decision comes about 7 seconds after the stage appears, and the whole story, played
without pauses, takes under a minute; it waits for the judge at the question and at the request. Within 10 minutes a
seated judge with hands only has a decision, a confirmed-in-words answer, failing checks, a
correction and passing checks. That is the satisfying moment, on paper; whether a first-time
wearer finds it without coaching is the headset session's question.

### The file on the recording (ADR 0026), 2026-10-02

`JudgeFileWalkTests` plays the recording as the headset does and walks the directed task's file
(`FileScreens`) at a page of four rows and of three for larger text. The bar and Tasks wait for
lane U's screens, so the walk starts at the file a task opens. It found, off the device:

- At the question the file opens on Waiting under its pill. At three rows the question reads first
  in three parts, then its two answers one a page; turning the page clears what was chosen on it, so
  Send answer sends only what is in view. The question has one prompt, so there is no Your answers
  page, and the demonstration's policy sends an answer without a Yes.
- At the request, Approve's Yes shows only once every part of the request has been drawn, and
  sends once; Deny's Yes shows from the first part; Cancel sends nothing.
- After the approved turn, Checks reads "Tests failed … 1 failed, 23 passed" with the source line
  "Simulated checks · recorded at …", and Tell it offers the two recorded instructions as rows, sent
  in exactly the words shown.
- At each stop the file offers exactly what the recording answers there. A recording whose
  question asked for a secret offers no answer, no Send answer and no Tell it, only Stop.
- `JudgeWordsTests` reads every section, side panel and confirmation of every file on every path,
  and finds no brand name.

### The whole menu on the recording (ADR 0026), 2026-10-03

`JudgeMenuWalkTests` walks the live menu (`MenuNavigator` with the real Tasks, file, Usage, Projects
and Settings columns) on the recording at both text sizes, drawing as `MenuDirector` does. In order:
the closed bar says "1 task is waiting for you"; the menu opens on Tasks, the waiting task first;
its row opens the file on Waiting under its pill; the question's answers lead to Send answer; then
Deny's Yes and Cancel; then Approve's parts, with Yes only after the last; then Checks, Tell it's
recorded rows, Usage's recorded limits ("Part of the recording"), Projects and each setting; New
project's recorded companion, quoted as its own, to a recap marked as the AI's suggestions whose
Start building waits ("The demo can't start new work."), nothing sent; and finally the closed bar,
"Nothing is waiting for you." It scans every frame and side panel it drew
and finds no brand name. It found that a press on a request's confirmation was never taken: the
file rebuilt on every draw, so the frame drawn never stood. Lane W fixed that in 1df3bb9.
`WorkspaceRender`'s judge walk (`far-3s judge 1 bar` to `12 closed`) draws the same steps from the
eyes on the far Quest 3S stage. Each step is held to the plane's checks at both text sizes.

## Gaps against today's product

| Area | Today in the judge build | Evidence | Who closes it |
| --- | --- | --- | --- |
| Welcome and onboarding | Closed 2026-10-07 (lane C, e4e06d7d): the demonstration's first visit opens the menu on Projects, and the stage's demonstration lines stand above the stage and the open menu. Lane V's calmer first view (picked 2026-10-08) will replace it | `WorkspaceDirector.OpenOnFirstVisit`, `JudgeMenuWalkTests`, `far-3s-demo-welcome-1` and `-2` | Lane C, with the calmer first view |
| Agent questions (ADR 0022) | Closed: the story's first decision is a question with two options, each with its own recorded continuation, asked in the file under Waiting; the simulated explanation now treats a waiting question as waiting | `sign_in_rate_limit.json`, `JudgeFileWalkTests`, `demonstration-sources.ts` | None |
| New project | Closed 2026-10-03 (lane C): Projects' New project plays the companion's recording, its question quoted as its own, to a recap marked as its suggestions under the note that it is an AI; Start building waits with "The demo can't start new work. Real work runs on your computer." | `JudgeMenuWalkTests`, `far-3s-judge-12` and `-13` | None |
| Changes and Checks | Closed: the file's Changes and Checks show the recorded simulated answers in words, as "Tests failed …: 1 failed, 23 passed" under "Simulated checks · recorded at …" | `JudgeMenuWalkTests`, `far-3s-judge-8-checks` | None |
| Usage | Closed 2026-10-02, on the menu since 10-03: the menu's Usage shows recorded limits for one practice agent, under "Recorded for the demo, not from any account", with no Refresh; a limit's Account is "Part of the recording"; `JudgeWordsTests` scans them for brand names | `UsageColumn`, `DemonstrationRecording.UsageLimitsAt`, `far-3s-judge-10-usage` | None |
| The line above the stage | Closed: the owner's words of 2026-10-02 | `DemonstrationFallback.Describe` | None |
| Agent app names | Closed: "Practice agent" and "Practice agent, watch only" | `demonstration.ts` | None |
| Sound on answering | Closed: sending an answer plays its own cue, live and in the demo | `WorkspaceAct.Answer`, `SoundCue.SendAnswer` in `SoundCues.cs` | None |
| The pinch hint | Still on the old workspace visuals, not the ADR 0023 components; it shows until a task's file has been opened once | `OnboardingHint.cs` | Lane U |
| Three arrangements | In Settings under Your space, "The characters", as live; nothing in the story shows why to use them | `SpaceSettings`, `JudgeWordsTests` | The video, not the build |
| Sound cues | Play as live work does (`StageSound`) | `StageSound.cs` | Headset check |
| Icons | On badges, marks and actions, as live | Stage and workspace renders | None |
| Starting again | Closed 2026-10-02: after a pause it goes on where it stood; only after its end does it start again | `DemonstrationTransport.Resume` | None |
| The holds | Closed 2026-10-08 (the coordinator's call): the 60 s instruction hold and the 20 s end hold do not run while a task's file is open and run whole from its close, so a judge reading Changes or Checks is never cut off | `DemonstrationPlayer.Reading`, `JudgeHoldsTests` | None |

## Names a judge reads

On 2026-10-02 `JudgeWordsTests` found, along every path, no agent app, model, company or platform
name but these three, all since replaced; it now allows no brand at all:

| Name | Where | Approved by the owner on 2026-10-02 |
| --- | --- | --- |
| Salidium | "Simulated, not from Salidium · recorded at 09:00:00" in Understand | "Simulated explanation · recorded at 09:00:00" |
| Seorak | "Simulated, not from Seorak · recorded at 09:00:00" in Checked | "Simulated measurement · recorded at 09:00:00" |
| Mac | "The demo can't start new work. Connect your Mac to start real work." (Create) | "The demo can't start new work. Real work runs on your computer." ("your computer" everywhere, lane G) |

Salidium and Seorak are the owner's own products, but the clause names brand names without an
exception, they predate the entry period, and the judge build does not use them, so naming them
there invites both questions. "Mac" names another company's product. Live provenance keeps the
products' names. Also approved: the agent apps "Practice agent" and "Practice agent, watch only",
and the line above the stage (above). Lane W applied the provenance words ("Simulated
explanation", "Simulated checks", "Simulated measurement") and lane G "your computer", and with
both on main the test fails on any brand a judge could read.

No word names the sponsor or its products. The app's label is Halcyonic; the APK declares Meta's
platform features, which judges do not read.

## Field of view

Meta's [device comparison](https://developers.meta.com/horizon/essentials/compare-devices/)
(2026-09-30): Quest 3, 110 by 96 degrees and 25 pixels per degree; Quest 3S, 96 by 90 and 20. Meta
gives no split about the view's forward, which only the device tells.

| Element | Where, from the eyes with the head level | Fits a Quest 3S? |
| --- | --- | --- |
| Lineup | ±30 degrees across, labels down to about 14 degrees | Yes |
| Line above the stage | At most 24 degrees wide, under the labels | Yes |
| A panel (44 by 26 degrees at 0.46 m) | Centre within 15 degrees of where the person looks, elevation −31 to +2: edges to ±37 across, lower edge down to about −44 | Across, yes; its lower edge at the lowest centre reaches the bottom of an evenly split 90 degrees |
| Rail | ±24 across, rows 40 to 49 degrees down | Its lower row lies past an evenly split 3S field until the person looks down |
| Beside a window | Outermost label 37.1 degrees | Yes |
| Turned aside | Outermost label 67.3 degrees | No, by design: the stage is turned away for a window |

Until this change nothing in the app read the device's field: the layout was the same on both
headsets. A seated person looks down at the rail as at a keyboard, so this was not a failure on a
Quest 3, but the criterion asks for adapting across devices. Now `DeviceMeasures` reads each eye's
field once the headset renders in stereo and sets `ViewField.Current`; with it, the rail rises until
its corners are 1.5 degrees inside the field with the head level (never above 29 degrees down,
so a degree under the line above the stage at its tallest, beside a window; AmbientRender checks it), and a panel prefers the side that keeps it inside, never pushed
into a label (agreed with lane U). The margin is a design decision: Meta gives no number, and on an
evenly split Quest 3S a workspace under the far lineup's labels has its lower corners 1.65 degrees
inside the field, so a larger margin would collide with the labels. The entry and workspace renders
run a Quest 3S pass (96 by 90, split evenly): the rail stands 39.0 degrees down instead of 44.5, the
panels where they were, and every corner of the rail and the panels is inside. Over a desk the
rail stays about 53 degrees down, inside the field when the head looks down at the lineup. The real
split comes from the device's `Halcyonic: device view field` line.

Larger text (Settings, lane U, 2026-10-02) makes a panel taller than designed, which on an evenly
split Quest 3S does not fit under the far lineup's labels with the head level. For such a panel the
rule takes the head to be tipped down by 1.5 times the extra height, at most 8 degrees
(`WorkspacePlacement.ReadingPitch`; about 5.6 for the next text size), so its fit depends on the
person looking down a little. Judges at the standard size are unaffected: a designed panel keeps
the head-level rule. The 4.4 and 5.6 degree figures assume the even split and are to be worked out
again from the headset's logged field (OPEN_QUESTIONS.md).

## Frame rate

`MeasureRender` at this commit: at most 75 draw calls a scene and 35 a panel (budget 220 and 60),
about 32,400 triangles, nothing allocated per frame
([quest-3-performance.md](quest-3-performance.md)). The Quest 3 ran at 72 frames a second in the
first sessions ([quest-3-device.md](quest-3-device.md)). At least 60 is the rule; the headset
session confirms it with `Halcyonic: device frames` each minute, which counts frames slower than a
60th of a second, and `pnpm quest:session` over an hour.

## Pause and resume

On a pause the demonstration's session stops; on resume it goes on where it stood (decided
2026-10-02; tested in `PausingStopsTheDemonstrationAndResumingGoesOnWhereItStood`), the stage keeps
its place and `DeviceMeasures` logs `paused` and `resumed after N ms`. Not yet seen on a
headset: a clean resume after the system menu, after sleep, and after another app.

## The release APK

Built at a214dce with `BuildReleaseApk`: 71,454,487 bytes, `com.halcyonic.xr`, version code 1,
version 1.0, target and compile API 34, not debuggable; permissions `HAND_TRACKING`,
`USE_ANCHOR_API`, `USE_SCENE`, `INTERNET` and AndroidX's receiver permission; no `RECORD_AUDIO` and
nothing of Meta's development tools (its own check passed, so it was kept); supported devices
`quest2|questpro|quest3|quest3s|stanley`; signed with the Android debug key, scheme v2 only. A
first attempt was ended by a signal during Gradle and left `DevAgentSettings.asset` beside
Resources and two preloaded assets in `ProjectSettings.asset`, as the runbook warns; both were put
back by hand before the second, successful build.

Built again on this branch (after the device measures and the question) with
`HALCYONIC_VERSION_CODE=26100201`: 71,498,397 bytes, `versionCode='26100201'`, the same permissions,
its own check passed, and `ProjectSettings.asset` still says version code 1 afterwards, with
`DevAgentSettings.asset` back in Resources.

Built a third time after Meta VR Glasses were dropped from the project's target devices
(`HALCYONIC_VERSION_CODE=26100202`): `com.oculus.supportedDevices` reads
`quest2|questpro|quest3|quest3s`, and the build's own check now refuses `stanley`. Unity wrote the
APK and said "Exiting batchmode successfully now!", then aborted in its own shutdown (exit 134,
"terminate_handler unexpectedly returned"); the APK and the project were intact. A batch build's
exit code alone is therefore not proof of failure; read the log's `Halcyonic: built` line.

Built again on 2026-10-08 at b9346b83 (the demonstration's first visit on Projects, lane W's
outside-text words, lane U's motion), without `HALCYONIC_VERSION_CODE`:

| APK | Bytes | SHA-256 |
| --- | --- | --- |
| Release (`BuildReleaseApk`) | 72,141,285 | `e3467353f280fc1b28f013623d8681ef38a9f539a530428e6c339f1bfff6f1b9` |
| Development (`BuildDevelopmentApk`) | 113,890,823 | `71aa3e352d9589ee200ba5d7cfe2214c1cfa0667121b18b34927531c37dbe1cf` |

The release build's own checks passed, and `aapt2` and `apksigner` agree: `com.halcyonic.xr`,
version code 1, target API 34, not debuggable; the same five permissions as above, no
`RECORD_AUDIO`; `quest2|questpro|quest3|quest3s`; no glance activity, permission or class; none of
the dev agent, the agent bridge, `METAX_operator` or `DevAgentSettings`; signed with the Android
debug key, scheme v2 only. `Meta.XR.ImmersiveDebugger` and its interface are in it by design
([horizon-store-release.md](horizon-store-release.md)). The development build carries the glance
(`GlanceActivity`, `POST_NOTIFICATIONS`), and its positive control found every marker the release
check looks for. `WorkspaceRender` passed at both text sizes, the judge walk and the demonstration's
first visit among its renders, and the client core's 64 judge and demonstration tests passed
(`JudgeWordsTests`, `JudgeFileWalkTests`, `JudgeMenuWalkTests`, the demonstration's own). `ProjectSettings.asset` was unchanged after both
builds.

The rules were read again on 2026-10-08 with no change that binds the judge build. The video may show
the project on a Quest or "via XR Simulator or another equivalent emulator", and the language clause
names testing instructions among the entry materials.

Before an upload, still the owner's: a release key, a version code above every earlier upload
(now set by `HALCYONIC_VERSION_CODE` at build time, YYMMDDNN, approved), and
the upload itself ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md), "Before an upload").

## Testing instructions for judges (draft for the owner, 2026-10-08)

For the submission form, written for the ambient first visit and the kit's first-open prompt that
the owner approved on 2026-10-08; to be checked against the judge build at the freeze. No brand
names.

> Halcyonic lets you see and direct AI agents building software, from where you sit. This build
> plays a recorded demo on the headset: it needs no computer, account or network, and nothing you
> do reaches an agent.
>
> 1. Sit down and use your hands; no controllers are needed.
> 2. Three characters stand in front of you, each a task an agent is working on. The line above them
>    says the demo is recorded.
> 3. After a few seconds one of them reads "Waiting for you". Look at it and pinch to open its file.
> 4. The agent asks a question: pinch an answer, then Send answer.
> 5. It then asks to run a command: read the request to its end, then approve it.
> 6. A check fails. Look through Changes and Checks, then use Tell it to send one of the offered
>    instructions. The agent goes again and the checks pass.
> 7. Close puts the file away. From the menu you can also try Projects (New project talks an idea
>    through with an AI companion), Usage and Settings.
>
> The demo waits for you at every decision and while a file is open, and starts again by itself
> after it ends. Taking the headset off and on picks up where you were.

## Consequences

- The judge path works end to end off the device, through the menu of ADR 0026, and its words name
  no brand: the three names above were replaced with the owner's words, and the scan allows none
  (updated 2026-10-03).
- The demonstration's story has its agent question, New project's recorded companion and a first
  visit that opens Projects; lane V's calmer first view will change that first visit before the
  final recording.
- The layout adapts to the device's field: every menu state the judge walk draws is checked inside
  a Quest 3S's field at both text sizes. The real split waits for the headset session.
- Re-read the rules before the submission; they can change.
