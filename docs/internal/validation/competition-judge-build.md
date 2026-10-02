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
| approval + 3.4 s | After Approve: the migration, "Checks failed" (1 failed, 23 passed), the round ends; two recorded instructions are offered for 60 s |
| next round | Either instruction plays a second round whose checks pass; then the end holds 20 s and the demonstration starts again |

The first decision comes about 7 seconds after the stage appears, and the whole story, played
without pauses, takes under a minute; it waits for the judge at the question and at the request. Within 10 minutes a
seated judge with hands only has a decision, a confirmed-in-words answer, failing checks, a
correction and passing checks. That is the satisfying moment, on paper; whether a first-time
wearer finds it without coaching is the headset session's question.

## Gaps against today's product

| Area | Today in the judge build | Evidence | Who closes it |
| --- | --- | --- | --- |
| Welcome and onboarding | Never shown: the welcome opens only on a live first visit (`EntryPanel`, `Live` is false while the demonstration plays). A judge gets the line above the stage and the pinch hint only | `EntryPanel.cs`, lines 229 and 301 | Final recording: a demonstration welcome (script below) |
| Agent questions (ADR 0022) | Closed on this branch: the story's first decision is a question with two options, each with its own recorded continuation; Understand still reads Working at the question, since the simulated explanation treats only approvals as waiting | `sign_in_rate_limit.json`, `DemonstrationAnswerKind.Answer` | Lane W for the explanation at a question |
| Create a project | "The demo can't start new work. Connect your Mac to start real work." | `EntryText.DemoCannotStart` | Lane C's companion, then a recorded Create path |
| Understand and Checked | Shown with the recorded simulated answers, but Checked reads as data ("available · 1 of 1 session, complete · fresh, data to 05:00:08", "unavailable: not yet computed …") | `WorkspaceRenders/desk-evaluation-panel.png` | Lane W |
| Usage left | Closed 2026-10-02 (coordinator): the rail offers no Usage left while the demonstration plays; before, the chip opened an empty panel | `UsageLeftGlance` | None |
| The line above the stage | Closed: the owner's words of 2026-10-02 | `DemonstrationFallback.Describe` | None |
| Agent app names | Closed: "Practice agent" and "Practice agent, watch only" | `demonstration.ts` | None |
| Sound on answering | Answering a question has no cue of its own (no `WorkspaceAct` for it), live or in the demo | `SoundCues.cs` | Lane U or sound |
| The pinch hint | Still on the old workspace visuals, not the ADR 0023 components | `OnboardingHint.cs` | Lane U |
| Three arrangements | In Settings as live; nothing in the story shows why to use them | Settings words in `JudgeWordsTests` | The video, not the build |
| Sound cues | Play as live work does (`StageSound`) | `StageSound.cs` | Headset check |
| Icons | On badges, marks and actions, as live | Stage and workspace renders | None |
| Starting again | Closed 2026-10-02: after a pause it goes on where it stood; only after its end does it start again | `DemonstrationTransport.Resume` | None |

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

Before an upload, still the owner's: a release key, a version code above every earlier upload
(now set by `HALCYONIC_VERSION_CODE` at build time, YYMMDDNN, approved), and
the upload itself ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md), "Before an upload").

## Consequences

- The judge path works end to end off the device and its words name no third-party product; three
  names wait for the owner's words.
- The demonstration's story predates agent questions, Create and the welcome; its final recording
  waits for lane C (Create with a companion) and lane W (Understand and Checked).
- The layout does not yet adapt to the device's field; the fix is agreed with lane U.
- Re-read the rules before the submission; they can change.
