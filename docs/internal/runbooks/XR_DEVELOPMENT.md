# XR development

Running the Unity client against a local control plane. The architecture is in
[XR_CLIENT.md](../architecture/XR_CLIENT.md); versions and platform facts are in
[meta-xr-platform.md](../validation/meta-xr-platform.md).

## One-time setup

These steps need the owner's accounts and are not automated.

1. Install Unity Hub and sign in with a Unity account.
2. In Unity Hub, install **Unity 6000.3.25f1** with **Android Build Support**, including OpenJDK
   and the Android SDK and NDK tools. The project is pinned to this version
   (`apps/xr/ProjectSettings/ProjectVersion.txt`); do not upgrade it during the competition.
3. Install the **Meta XR Simulator v207**, a standalone application and OpenXR runtime, following
   Meta's Meta XR Simulator documentation.
4. In Unity Hub, choose **Add project from disk** and select `apps/xr`.

## First open

Unity resolves the packages in `apps/xr/Packages/manifest.json`: OpenXR 1.18.0, the Meta XR Core
and Interaction SDKs and the MR Utility Kit 207.0.0 (from Meta's registry at
`npm.developer.oculus.com`, under Meta's SDK license), XR Hands 1.9.0, Newtonsoft.Json 3.2.2, the
Test Framework, the generated contracts from `packages/contracts/csharp`, and the embedded client
core. The MR Utility Kit brings AI Navigation, which resolves to 2.0.14, the version this editor
bundles. The manifest also lists the AI, AssetBundle and Physics 2D engine modules, which Meta's
SDKs use without declaring; without them Meta's code does not compile.

The project settings are committed, so a fresh clone opens configured:

- OpenXR is the XR loader for Android and for the desktop platform (the Simulator runs there), and
  starts with the app.
- Meta's OpenXR feature set is enabled on both, with the Oculus Touch controller profiles.
- Active Input Handling is the Input System package only: the Android build refuses "Both" with the
  GameActivity entry point, which Meta's setup tool requires.
- Hand tracking support is "Controllers and Hands", and the Android manifest declares it.

Meta's Project Setup Tool reports no required task. Its remaining recommendations (Vulkan,
single-pass instancing, ASTC textures, target API 34, MSAA and others) are left for the Quest build.

On first open:

1. On the editor's first launch, Unity shows its Editor Software Terms, possibly on another desktop
   Space. Nothing loads, and the editor log stops after licensing, until you accept them.
2. Meta asks whether to share additional usage data with Meta. Halcyonic's choice is **Only share
   essential data**. It applies to every Meta developer tool on the machine.
3. Meta writes `Assets/Resources/DevAgentSettings.asset` with the machine's LAN address and a
   generated token. It is git-ignored; never commit it.

Unity writes a `.meta` file for every asset, including in `packages/contracts/csharp`. The `.meta`
files fix asset identities, so commit new ones and keep them.

## Scene

`Assets/Halcyonic/Scenes/Stage.unity`, the only scene in the build, holds:
- Meta's Camera Rig building block, with a floor-level tracking origin;
- the Hand Tracking building block, which keeps tracking but no longer draws the hands;
- Meta's comprehensive interaction rig under the camera rig: hand data, the hands that are drawn,
  and the hand ray and poke interactors, with its locomotion deactivated and its hand rays seated
  (`SeatedHandRay` in place of the SDK's `HandPointerPose`);
- Meta's eye gaze with a gaze conecaster beside the rig's HMD, emulating gaze with the head's
  direction;
- the Halcyonic stage object, with the connection, the characters, the focus guard and the
  workspace director.

`FocusGuard` hides the rig's hands and controllers and deactivates its interactors when the app
loses input focus. `HalcyonicBootstrap` still adds a stage to any other scene that lacks one, but
without hand visuals to hide and without hand interaction.

`StageSetup` made the interaction part of the scene and can make it again: in the editor,
**Halcyonic > Set Up Stage Interaction**; with the editor closed, in batch mode:

```bash
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Workspace.Editor.StageSetup.Apply -logFile ~/Library/Logs/Unity/halcyonic-xr-setup.log
```

It adds the rig the way the Interaction SDK's "Interactions Rig" building block does, and the gaze
the way the SDK's gaze quick action does, seats the rig's two hand rays, and changes nothing when
run again. If a newer SDK renames the objects it adjusts, it stops and logs the rig's hierarchy
instead of saving the scene.

The Meta XR Simulator renders nothing on this Mac, so the workspace's layout and opacity are
checked by rendering them in the editor: **Halcyonic > Render the Workspace Over the Stage**, or in
batch mode, without `-quit`, since it exits by itself, with status 1 when a check fails:

```bash
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Workspace.Editor.WorkspaceRender.Check -logFile ~/Library/Logs/Unity/halcyonic-xr-render.log
```

It opens the workspace for the character that needs the person, with six characters 2.4 m away
and again on a desk half a meter away, every screen drawn from the client core's models on the frame
at touch distance, saves `far.png`, `desk.png` and each part alone in
`apps/xr/Builds/WorkspaceRenders`, and logs where the workspace opened. Each screen is saved with a
`-closeup.png` at a Quest 3's 25 pixels per degree, for judging legibility, and a `-panel.png` of
the whole panel, whose edges the close-up crops: `far-need` (an approval under Waiting for you),
`far-doing` and `far-doing-stop`, `far-understanding`, `far-evaluation-after-approving` and so on,
the questions (`far-question`, `-typed`, `-three`, `-several`, `-long`, `-options`, `-long-label`,
`-secret`, `-sent`), the whole request (`far-approval`, its first part, `far-approval-last`,
`far-approval-short`, `far-deny`), the other confirmations (`far-stop`, `far-stop-beside-hold`,
`far-heard`, `far-answers`), `far-hold-to-talk` and the hostile text (`far-untrusted`,
`far-untrusted-activity`), and the same for the desk, then the far and desk stages again with a
Quest 3S's field of view set (`far-3s`, `desk-3s`: 96 by 90 degrees, split evenly until a device
measures it). It fails if a pixel of the workspace changes
when the stage behind it is drawn, a character's body or label is behind it, or its center leaves
the comfortable band, or, with the 3S's field, a corner of it leaves the field less 1.5 degrees with
the head level; if a target is too small or two closer than 12 mm, a word too small, any of
our words cut short, the list pages or a line runs below the body; if the tabs don't show whole
beside Close; if a section's line does not fit under its heading; if the log loses its newest line
or the agent's words don't lean; if a question's text is cut, a long one counts as read before its
last part shows, or anything but the unavailable Sent… stands at the bar's right end while an answer
may still take effect; if the whole request's parts are not the whole request, its pager is not at
the top, or "Yes, approve" unlocks before the last part; and if any confirmation's Yes stands within
12 mm of a control shown before its confirm step or since. It checks on real labels that source text
shows exactly as written and that a quote cut short ends in an ellipsis, and it puts hostile text on
every label that shows text from outside and fails if a label interprets markup or an escape
sequence, shows text that did not go through the one rule, or cuts a line short without an ellipsis;
the log says how many labels each pass checked, how many log lines gave way, and how many places
each Yes stands clear of. It works in a new, unsaved scene, and leaves the committed TextMeshPro
font assets as they were, which drawing text in the editor would otherwise upgrade and save;
characters the static atlas lacks, such as the minus sign in a change summary, are drawn from it as
look-alikes for the render only, and the log names them.

The same run then walks a judge's path through the menu (ADR 0026) on the recorded demonstration,
from the eyes on the far stage with the 3S's field. It saves `far-3s-judge-1-bar` to
`far-3s-judge-14-closed` at each text size: the closed bar, Tasks, the file at the agent's question,
an answer chosen, the approval, its request's first part, Yes, Checks, Tell it's recorded rows,
Usage with a limit's side panel, Settings with a setting chosen, New project's recorded questions
and its recap, and the bar again. The recording
stands where each step is reached, and nothing is sent. It fails on the plane's checks; if the
waiting task is not Tasks' first row; if Yes shows on the request's first part or not after the
last; if Checks does not show the recorded simulated checks; if Tell it offers other than the
recorded instructions; if a recorded limit's Account does not say it is part of the recording; or
if New project's companion is not quoted as its own, or its recap offers a start or lacks the note
that the companion is an AI.

Then the demonstration's first visit, on the far and desk stages with the 3S's field:
`far-3s-demo-welcome-1-projects` and `-2-a-task-waits`, and the same for `desk-3s`, at each text
size. The menu stands open on Projects as the recording begins, then once its directed task waits,
with the stage's banner where the stage stands it then, raised above the characters with the
demonstration's lines alone. It fails if the demonstration's lines would be missing with the menu
open, or a live session's banner would show or rise; if the lines come within a degree of a
character's highest reach, risen and moving, of a label or of the plane; if they leave the field
while the menu is read; if they reach more than 20 degrees above eye level; or if Tasks does not take
the amber dot when the task waits. The log says from how high to how high the lines stand.

The stage beside a window renders the same way, **Halcyonic > Render the Stage Beside a Window**,
or in batch mode:

```bash
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Workspace.Editor.AmbientRender.Check -logFile ~/Library/Logs/Unity/halcyonic-xr-ambient-render.log
```

It saves the characters in each arrangement, in front, turned aside and beside a window, behind a
window-sized plate (1.4 by 0.79 m at 1.6 m, straight ahead at eye level) (`window-front.png`,
`window-aside.png`, `window-beside.png`), in `apps/xr/Builds/AmbientRenders`. It logs, for each arrangement, how many characters'
bodies and labels the plate covers and how far out the outermost label reaches, and how far below
eye level the banner hangs beside a window. It fails if turning the lineup aside uncovers no one;
if beside a window the plate covers any body or label, a body or label comes within a degree of
the window's lane or of another character's, a whole title shows or a short one is missing, takes
more than one line or is wider than 10.5 degrees, or the banner reaches into the lane,
sits under the window or a label, cuts its words short or takes a press. Beside a window it names in the log any two short titles cut to the same
words, as the render's own two do. The plate is not a real window: only the headset shows what a
real one covers.

Every state of a task on the stage renders the same way, **Halcyonic > Render Every State on the
Stage**, or in batch mode:

```bash
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Workspace.Editor.StageRender.Check -logFile ~/Library/Logs/Unity/halcyonic-xr-stage-render.log
```

It saves every state at the stage's default distance and height, with the banner and with a peek,
the demonstration's marked stage, and a desk, each with close-ups at a Quest 3's pixels per degree,
in `apps/xr/Builds/StageRenders`, and logs how far down the labels end, each badge's width and
whether its icon shows, and the smallest text (`Halcyonic: stage render ...`). The interface's
rules it checks are in [XR_CLIENT.md](../architecture/XR_CLIENT.md).

Every component of the interface renders in every state, **Halcyonic > Render Every Component**, or
in batch mode:

```bash
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.UI.Editor.GlazeRender.Check -logFile ~/Library/Logs/Unity/halcyonic-xr-component-render.log
```

It saves the gallery in `apps/xr/Builds/GlazeRenders` and logs each button label's contrast on its
fill (`Halcyonic: component render ...`). It fails if a word is too small or cut short, a target too
small, a label short of 4.5:1 on its fill, a badge's word wrong, or a meter filled other than to its
share, or at all while waiting; and if the icon atlas lacks an icon the client core names, an icon
shows on no badge, mark or button (`gallery-actions.png` shows every action's), an icon is under a
degree or has no words beside it, or a label of words draws from the icon atlas. It also draws ADR 0027's
motion as two strips, `gallery-motion.png` (Sent… and Hold to talk writing down at three points of the
shimmer's sweep, then under Keep badges still) and `gallery-listening.png` (Hold to talk listening at
three points of its pulse, then still) and `gallery-state.png` (a badge changing from Working to Waiting
for you at none, a quarter, half and all of its cross-fade, then half under Keep badges still), and fails if a wait's words do not move, are lifted too little
to see, keep moving once the wait ends or under Keep badges still; if Hold to talk listening is not in the
active tone, its microphone does not pulse, or pulses still once idle or under Keep badges still; if its
words lean; if a changing badge's pill is not where easing in and out puts it, its word is in neither
state's colour or drops under 3:1 on the pill at any twentieth of the change either way, it still changes
once done, or in the editor it cross-fades without a render stepping it; or if a wait adds a renderer, or any of sixty frames of either motion allocates in each of
three tries (a badge's change as well). Unity's count of allocations takes in the editor's other threads, and this editor's Mono
counts nothing for one thread alone, so only an allocation that comes again at the same frame is the
motion's; the log gives the frames and the quietest try's bytes.

The headset redesign's frames (ADR 0026) render the same way, **Halcyonic > Render the Redesign
Directions**, or in batch mode:

```bash
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Workspace.Editor.DirectionsRender.Check -logFile ~/Library/Logs/Unity/halcyonic-xr-directions-render.log
```

It saves every round's frames in `apps/xr/Builds/DirectionRenders`: the first three directions (`a`),
the game menu and the case file (`d`, `e`), D styled (`s`), D refined on one plane (`r`) and the
heads-up concept (`h`); `HALCYONIC_DIRECTIONS_ONLY=r` renders only the shots whose names start so.
The refined shots, up to Usage and Settings (`r15` to `r18`), are held to ADR 0026's rules: one
plane facing the eyes at its centre, never rolled, its parts a degree apart and its columns aligned;
one selection treatment; type that only steps down, a state pill reading with its subject; no text
under 14 dp as the eyes see it; a Quest 3S's field as `FieldChecks` sees it; a light line that
crosses no label or character; and no row in a page's glow. Shots kept to show what a rule catches
name what they must fail, the upright plane (`r1`, `r2`), a plane that stays put while a file
slides out (`r6`) and the menu and a file together beside a video window (`r21`), and the run fails
if they stop failing. Beside a window (`r19` to `r21`), nothing may come within a degree of it. `r12` also logs a quote measure: how many
characters of a companion's question fit its 2 rows at a file's width. `HALCYONIC_PROTO_ICONS`, the path of a font of the file-type
glyphs cut from Material Symbols, which is not committed, draws those icons in the styled and refined
shots; without it they are left out.

What the interface costs a Quest 3, measured off the device, **Halcyonic > Measure the Interface**,
or in batch mode:

```bash
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Workspace.Editor.MeasureRender.Check -logFile ~/Library/Logs/Unity/halcyonic-xr-measure.log
```

It builds the stage, the stage beside a window while another window has focus, and a workspace,
and logs for each the draw calls at most, the text
labels and their characters, the vertices and triangles; for each panel, what showing the same
screen again costs (text meshes built again, the editor's milliseconds, bytes); and what each
per-frame method allocates a frame (`Halcyonic: interface measure ...`). It fails if a panel could
draw more than 60 times or everything showing more than 220 (ADR 0023), if showing an unchanged
screen again builds any text mesh, if any frame allocates, or if a line whose words stay the same
does not lean, or stand upright, as soon as it is drawn after becoming the agent's words or
Halcyonic's. Its times are a Mac's; what only the
headset tells is in [quest-3-performance.md](../validation/quest-3-performance.md).

Every render, and the measure, runs twice: at the standard text size, then with reading text a step
larger, as a person may choose in Settings' Comfort (`Comfort.LargerTextScale`), and fails
if either pass does. The second pass logs `Halcyonic: the same again with reading text a step
larger.` before it starts, its failures begin "at the larger text size", and its pictures go to a
`Larger` folder inside the render's own, such as `apps/xr/Builds/FileRenders/Larger`. A run takes
about twice as long as before.

Every render also measures each label's text as the eyes see it, slant included
(`GlazeChecks.TextAsSeen`): text on a surface the eyes meet at a slant, such as an upright one below
them, reads smaller than its size. The stage render holds it, its labels leaning back on a desk;
today's panels only list what reads under 14 dp that way (`Halcyonic: text as seen, listed, not
failed ...`), as the owner chose on 2026-10-02, until the redesign replaces them. The component
render proves the check fails on an upright plate under the eyes and passes the same plate facing
them, and proves the redesign's other checks catch what they must: parts off one plane facing the
eyes (`OnePlane`), type that rises down a column (`TypeStepsDown`), and a second selection treatment
or an accent bar (`OneSelectionTreatment`).

Batch runs can end with exit status 134 after `Exiting batchmode successfully now!`: the
Interaction SDK's telemetry library (`ISDKEngineTelemetry.dylib`) aborts on a mutex during
shutdown, as macOS's crash reports show. It happens after the work is done and saved; read the
verdict from the log (`Halcyonic: workspace render: ...`, `Halcyonic: stage interaction set up.`,
`Build Finished, Result: Success.`) rather than from the exit status.

Text in the workspace is TextMeshPro. Its essential resources are committed in
`Assets/TextMesh Pro`, imported from the builtin `com.unity.ugui` package, without the EmojiOne
sprites and the HDRP and URP shader graphs, which the stage does not use.

### The icons

The icons are Material Symbols Rounded, cut to the glyphs the headset uses
([material-symbols.md](../validation/material-symbols.md)). To add or change one, or to take a new
release:

1. Change the client core's `GlazeIcon` (in `StateLanguage.cs`) and the `ICONS` list in
   `apps/xr/tools/glaze_icons.py` together; the script stops while they differ.
2. Make the icon font and its code point table (`GlazeIconGlyphs`) from Google's variable font,
   `variablefont/MaterialSymbolsRounded[FILL,GRAD,opsz,wght].ttf` in google/material-design-icons,
   kept outside the repository and never committed, with fontTools installed:

   ```bash
   python3 apps/xr/tools/glaze_icons.py "/path/to/MaterialSymbolsRounded[FILL,GRAD,opsz,wght].ttf"
   ```

   It prints the source's version and SHA-256 and the font it wrote. Record a new source in
   material-symbols.md.
3. Build the atlas from that font: in the editor, **Halcyonic > Build the Icon Atlas**, or in batch
   mode, without `-quit`, since it exits by itself, with status 1 when a glyph is missing:

   ```bash
   /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.UI.Editor.GlazeIconAtlas.Check -logFile ~/Library/Logs/Unity/halcyonic-xr-icon-atlas.log
   ```

   It writes `apps/xr/Assets/Halcyonic/UI/Resources/HalcyonicUI/GlazeIcons.asset` in place, keeping its meta file, and logs
   its size (`Halcyonic: icon atlas: ...`). Every build gives the texture and material inside it
   new identifiers, so an atlas built again from an unchanged font is not worth committing.
4. Run the component render, which fails if the atlas lacks an icon the client core names, then the
   other renders.

## Run against the control plane

In the repository:

```bash
pnpm dev
```

In another terminal, once it is ready:

```bash
pnpm demo
```

Enter Play mode with the Meta XR Simulator active. The client reads the token from
`~/.halcyonic/access-token` (override with `HALCYONIC_TOKEN_FILE` or `HALCYONIC_DATA_DIR`) and
connects to `ws://127.0.0.1:47800/realtime` (override with `HALCYONIC_ENDPOINT`). The banner under
the characters' labels says whether the state is live.

- **Keep the editor frontmost.** The editor submitted XR frames only while it was the active
  application; hidden or merely visible, the session stayed at READY and nothing rendered. Run In
  Background is on, but it did not change this.
- **Known failure on this Mac.** On an M5 Max with macOS 26.7, every session then failed at its
  first frame (`XR_ERROR_RUNTIME_FAILURE`), with the Simulator warning that it cannot find MoltenVK
  entry points. See [meta-xr-platform.md](../validation/meta-xr-platform.md) for what was tried.
- **First launch.** The Simulator starts with a welcome screen.
- **Controllers by default.** The Simulator starts with controllers as its input; the hands-only
  check needs its inputs switched to hands.

To see recorded data instead, stop `pnpm dev` and run
`pnpm replay fixtures/traces/multiple_workstreams.jsonl`; the stage labels it as recorded.
`fixtures/traces/failure_modes.jsonl` shows a failed, an unknown, an interrupted and a finished
workstream.

## Milestone 2 checks in the Simulator

- Three characters appear for the demo's three workstreams, each with its title and a written
  status.
- The approval workstream shows "Waiting for you", with the approval explained in its peek, and
  rises toward the eye line; after the demo approves it, it returns to working and then "Finished
  this round".
- The replayed traces show the same states, labeled as recorded, including the failure trace's
  failed, unknown and interrupted characters, each with its reason written out.
- Stopping the control plane shows "Last known: can't reach your computer. Trying again…"; restarting it
  reconnects without restarting Play mode.
- Everything works with hands only; no controller is needed.
- Opening the system menu hides the hands and keeps the scene rendering (VRC.Quest.Input.4).
  Whether the Simulator reproduces the real focus change is itself something to confirm.

## On a Quest

A session on the headset follows [HEADSET_SESSION.md](HEADSET_SESSION.md): one order through the
token, proof, glance, comfort and judge's path checks below, with pointers to the rest of them to
fit in, and `pnpm quest:check` for what needs no person.

The Android player settings are committed:
- application id `com.halcyonic.xr`, product name Halcyonic;
- IL2CPP on ARM64, minimum API level 32, target API level 34, which the Horizon Store requires
  ([horizon-store-release.md](../validation/horizon-store-release.md));
- Internet Access set to Require, because Unity's automatic detection does not see
  `ClientWebSocket` and would leave the permission out;
- in `OculusProjectConfig`, Passthrough Support and Scene Support set to Supported and Anchor
  Support enabled, and in the committed manifest the matching entries: passthrough as a feature
  the app supports but does not require, `com.oculus.permission.USE_SCENE` for the room's layout,
  which the person grants at runtime, and `com.oculus.permission.USE_ANCHOR_API` for the stage's
  anchor ([mixed-reality-room.md](../validation/mixed-reality-room.md)).

`ClientWebSocket` works under IL2CPP on a Quest 3 ([quest-3-device.md](../validation/quest-3-device.md));
the app no longer uses it: over USB it performs the WebSocket upgrade itself, on the connection the
control plane proved itself on, which "Token storage on a Quest" below checks.

### Build

`QuestBuild` (`Assets/Halcyonic/Editor`) builds the scenes in the build settings into
`apps/xr/Builds/`, which git ignores:

| APK | Menu item | Batch method | For |
| --- | --- | --- | --- |
| `Halcyonic.apk` | **Halcyonic > Build Quest APK** | `BuildDevelopmentApk` | The owner's headset only |
| `Halcyonic-release.apk` | **Halcyonic > Build Quest Release APK** | `BuildReleaseApk` | A Horizon Store release channel, once signed |

In the editor, switch the platform to Android first, or the build switches it and reimports. With
the editor closed, build in batch mode from the repository root, naming the method:

```bash
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Editor.QuestBuild.BuildDevelopmentApk -logFile ~/Library/Logs/Unity/halcyonic-xr-build.log
```

Close the editor first: only one Unity instance can have the project open. In batch mode a failed
build exits with status 1, and the log says why.

A build changes the project in ways that are expected:
- Meta's build step adds `OculusRuntimeSettings` to the preloaded assets and never removes it; that
  entry is committed.
- The Android Gradle plugin's native build files (`.utmp`), Meta's `OVRBuildConfig`, an empty
  `StreamingAssets` folder and, after a failed build, the Performance Testing package's run files
  are git-ignored.
- A failed build can leave the XR settings in the preloaded assets. Restore `ProjectSettings.asset`
  rather than commit them.
- Meta's `OVREngineConfigurationUpdater` sets the Android orientation to landscape left and
  `vSyncCount` to 0 on the editor's first update with Android active, and the editor saves them.
  Batch runs with `-quit` end before that update; a batch run that keeps the editor alive, or the
  editor window, changes `ProjectSettings.asset` and `QualitySettings.asset`. Restore them unless
  the change is intended.

**Never share the development APK.** It is debuggable and carries Meta's development tools:
Meta XR Operator, which serves agents from inside the app and brings a screen capture activity and
service with the `FOREGROUND_SERVICE_MEDIA_PROJECTION` permission; the Immersive Debugger and its
dev agent; and `DevAgentSettings.asset`, into which Meta's build step writes this Mac's LAN address
and the token of the editor's remote agent server.

The release APK is built without the development option, so it is not debuggable and has no
profiler connection, and it leaves those tools out:
- Meta's own build step leaves Meta XR Operator's Android library out of every non-development
  build, and with it the media projection activity, service and permission. The project's manifest
  removes nothing, so the development APK keeps them.
- `QuestBuild` filters the assemblies of the Immersive Debugger's dev agent and the agent bridge
  out of every non-development build; nothing else in the build references them. The debugger's
  runtime assembly stays, disabled by the committed settings, because the MR Utility Kit reads
  its settings and a release build without it fails to link
  ([horizon-store-release.md](../validation/horizon-store-release.md)).
- `BuildReleaseApk` moves `DevAgentSettings.asset` out of `Resources` for the build and back
  afterwards; Meta never recreates it while a player builds. Any other non-development build fails
  while the asset is in `Resources`. An interrupted release build can leave it at
  `Assets/DevAgentSettings.asset`, which git ignores; move it back to `Assets/Resources`.
- After building, `BuildReleaseApk` checks the APK for all of these, and deletes it if any remain.

To inspect an APK, with the build tools in Unity's Android SDK:

```bash
TOOLS=/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer/SDK/build-tools/36.0.0
$TOOLS/aapt2 dump badging apps/xr/Builds/Halcyonic-release.apk
$TOOLS/aapt2 dump xmltree --file AndroidManifest.xml apps/xr/Builds/Halcyonic-release.apk
$TOOLS/apksigner verify --verbose apps/xr/Builds/Halcyonic-release.apk
```

A release APK shows `targetSdkVersion:'34'`, no `application-debuggable`, the permissions
`INTERNET`, `com.oculus.permission.HAND_TRACKING`, `com.oculus.permission.USE_ANCHOR_API`,
`com.oculus.permission.USE_SCENE` and AndroidX's own receiver permission, the features
`com.oculus.feature.PASSTHROUGH` and `oculus.software.overlay_keyboard` as not required, the
`com.oculus.intent.category.VR` launcher category and `com.oculus.vr.focusaware`, and nothing from
`com.meta.agenticxr`. It never asks for `android.permission.RECORD_AUDIO`: hold to talk is in
development builds only, and `BuildReleaseApk` deletes and fails an APK that asks for it. A
development APK asks for it, because hold to talk uses the microphone.

### Before an upload

The release APK is signed with Unity's debug key, and Meta requires the developer's own
([horizon-store-release.md](../validation/horizon-store-release.md)). These steps need the owner's
account and secrets, and are not automated; never paste a password, the keystore or the app secret
into a chat or a command line that a log keeps.

1. Once: create a release keystore outside the repository with `keytool` from Unity's OpenJDK
   (`PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool -genkeypair -v -keystore <path> -alias
   halcyonic -keyalg RSA -keysize 4096 -validity 10000`, which asks for its passwords). Back the file
   and its passwords up together: every update to the app must be signed with the same key, and a
   lost key means a new app.
2. Once: in the Developer Dashboard, create the release channel named exactly `Competition`, and
   note the app's id. Meta says every upload to any channel must meet the release packaging
   requirements.
3. Choose the version code, YYMMDDNN: the date and that day's build number, for example
   `26111701` for the first build on 2026-11-17. It must be above every code uploaded before for the
   app, on any channel; developers report the store refuses a repeated one. Record each uploaded code
   in the owner's notes.
4. Build the release APK with it. `BuildReleaseApk` sets it for the build only and puts the
   project's code back, so `ProjectSettings.asset` does not change; without the variable the log says
   which code it kept:
   ```bash
   HALCYONIC_VERSION_CODE=26111701 /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Editor.QuestBuild.BuildReleaseApk -logFile ~/Library/Logs/Unity/halcyonic-xr-release.log
   ```
   Check the log's `Halcyonic: the release APK carries version code …` and
   `Halcyonic: built …`, and `aapt2 dump badging` for `versionCode`.
5. Sign it with the release key. `apksigner` replaces the debug signature and asks for the
   passwords:
   ```bash
   $TOOLS/apksigner sign --ks /path/outside/the/repository/release.keystore apps/xr/Builds/Halcyonic-release.apk
   $TOOLS/apksigner verify --verbose --print-certs apps/xr/Builds/Halcyonic-release.apk
   ```
   The certificate printed must be the release key's, not `CN=Android Debug`.
6. Install the signed APK on the headset (uninstall first, see below) and walk the judge's path in
   "The demonstration judges see", with no token and no pairing.
7. Upload it to the `Competition` channel with Meta Quest Developer Hub, or with Meta's platform
   utility, which takes the app's id, its secret or a token, the APK, the channel and the age group the
   owner chose (`ovr-platform-util upload-quest-build --age-group <group> --app-id <id> --app-secret
   <secret> --apk <apk> --channel Competition`, per Meta's page of 2026-07-20). Then invite the
   judges' accounts or share the channel's invite link as the competition asks.

Once the release APK carries the owner's key, the headset needs an uninstall before installing it
over a debug-signed build, and the uninstall deletes the access token written to the headset.

### One-time headset setup

These steps need the owner's accounts.

1. Set up the headset with the owner's Meta account, paired with the Meta Horizon app.
2. At developers.meta.com, with the same account, create or join a developer organization.
3. In the Meta Horizon app, open the headset's settings and turn on **Developer Mode**.
4. Connect the headset with a USB-C data cable. In the headset, accept **Allow USB debugging**
   with **Always allow from this computer**. `adb devices` must then list the headset as
   `device`, not `unauthorized`.

`adb` comes with Unity's Android module, in `PlaybackEngines/AndroidPlayer/SDK/platform-tools`.

### Install and connect

Run the control plane from the same commit of main the APK was built from. A headset reads what the
control plane sends strictly where a field is required, so an APK newer than the control plane can
fail to read it: since `repository`, `changed_at` and `used_by` joined `GET /api/locations`, a new
APK cannot read an older control plane's folders, and Create's folder step fails with it. An older
APK ignores fields it does not know, so the other way round works.

The control plane serves only loopback; over USB, `adb reverse` makes the headset's loopback reach
it. With `pnpm dev` running, from the repository root:

```bash
adb install -r apps/xr/Builds/Halcyonic.apk
adb reverse tcp:47800 tcp:47800
adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
```

The first launch creates the app's data directory, logs that it has neither a pairing nor an access
token, and shows the recorded demonstration, labeled as such, which is what a headset without a
control plane shows.
Write the token into the app's private storage and start the app again:

```bash
adb shell run-as com.halcyonic.xr rm -f files/access-token.tmp
adb exec-in "run-as com.halcyonic.xr sh -c 'umask 077; mkdir -p files && rm -f files/access-token.tmp && cat > files/access-token.tmp'" < ~/.halcyonic/access-token
adb shell "run-as com.halcyonic.xr sh -c 'for i in 1 2 3 4 5 6 7 8 9 10; do test \"\$(stat -c %s files/access-token.tmp 2>/dev/null)\" = 44 && break; sleep 1; done; if test \"\$(stat -c %s files/access-token.tmp 2>/dev/null)\" = 44 && chmod 600 files/access-token.tmp && mv -f files/access-token.tmp files/access-token; then echo written; else rm -f files/access-token.tmp; echo not written: the token file is not the 44 bytes the control plane makes, or it never arrived, so write it again; fi'"
adb shell run-as com.halcyonic.xr ls -l files/access-token
adb shell am force-stop com.halcyonic.xr
adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
```

The token goes from the Mac's own file into the app's private storage with no copy anywhere in
between: it is written whole beside the old one, made mode 600 and only then put in its place, so a
cut-off write never replaces a token and an old file's mode never carries over. No other app can
read it there, and `run-as` reaches it only on a development build, which is debuggable.

The first command removes what an earlier write left half done, and returns only once it has. The
second sends the token through `adb exec-in` into a new `.tmp` file. On a Quest, the remote `cat` of
the old one-step write never saw the end of its input, so nothing after it in the same command ran
(2026-10-04), and `adb exec-in` returns before its remote command has started or ended: the third
command therefore waits up to 10 seconds for the whole 44 bytes, then makes the file mode 600 and
moves it into place, printing `written`. Otherwise it removes the `.tmp` and prints `not written`,
so a file the second command never wrote, as when the Mac's token file is missing and the shell
refuses the redirect, is never taken. `adb shell` joins the words it is given with spaces and does
not quote them again, so its remote `sh -c` is given as one quoted string, for the headset's shell
to read the inner quotes itself; `adb exec-in` quotes each word after the first. Nothing here shows
what is in the token, and `adb exec-in` says nothing when `run-as` fails, as on a release build, so
the fourth line checks: it should list `files/access-token` with `-rw-------` and 44 bytes. A `cat`
still waiting for its input's end holds the file open until `adb kill-server`, which a session's
close runs. `pnpm quest:check` fails while a `.tmp` file is left over. It
should survive `adb install -r`. The control plane logs `realtime client connected` for
`halcyonic-xr`.

**A token left on shared storage.** Development builds before this kept the token on shared
storage, in `/sdcard/Android/data/com.halcyonic.xr/files`, where `adb push` left it readable by
anything with `adb` or file access over USB. A development build that finds a token there moves it
into private storage at its next start, before it reads a pairing, and removes it; a release build
removes it without reading it. Only a regular file with one name holding a token in its own form is
taken, and nothing there is followed through a link or waited on as a pipe. A token made by hand in
another form works on the computer, which takes any of at least 32 characters, but the move refuses
it and removes it unused. The write above takes only the control plane's own token, 44 bytes in
its file, and says `not written` for any other; only that form can be written. The app may not be
allowed to remove a file `adb push` made, since `shell` owns it; it then logs "a copy of the access
token is still on shared storage". If it may not even read it, it logs that it could not deal with
the old place (`open failed with EACCES`). Either way, remove it from the Mac, which can, and check
that it is gone:

```bash
adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token
adb shell ls /sdcard/Android/data/com.halcyonic.xr/files/
```

Once every headset has moved its token, replace the token once, whatever happened, since it sat
readable on shared storage and never expires: stop the control plane, delete
`~/.halcyonic/access-token`, start it again, which makes a new one, and write it to each headset
as above. Paired headsets are not affected; they use their own credential. None of this has been
run on a headset yet ([headset-token-storage.md](../validation/headset-token-storage.md));
"Token storage on a Quest" below lists what to check.

Before the headset sends its token, it asks the control plane to prove it holds the same token,
for the address and port the headset dialled, as `pnpm devices` does
([SECURITY.md](../architecture/SECURITY.md)). The proof names the address the control plane was
reached at on the Mac, so keep the same port on both sides of `adb reverse` and the control plane
on 127.0.0.1 (the default); a headset endpoint must be `ws://` at a literal address, `127.0.0.1` or
`[::1]`, never `localhost`. If the line above the stage says "This headset's access code doesn't match your
computer's, or something else is answering in its place", the token on the headset is from an
earlier data directory or was replaced on the Mac, or another program holds port 47800 while the
control plane is stopped: the headset sent nothing and stopped trying. Start the control plane if
it is stopped, write the current token again and start the app again, as above. "Your computer
refused this headset's access code" means the control plane proved itself and then answered 401,
as when the token is replaced in between: write it again the same way. "Can't reach your computer"
instead means nothing answered: check the control plane is running and `adb reverse tcp:47800
tcp:47800` is in place.

- **Run the control plane without `--watch` for a headset session:**
  `node apps/control-plane/src/main.ts`, not `pnpm dev`. `pnpm dev` restarts it whenever code in the
  checkout changes, as a merge does, and that leaves every running execution in an unknown state.
- **Run `adb reverse` again after any Unity run for Android.** Every Unity run with the Android
  target kills the adb server as it exits, an import as well as a build, which drops the rule. Until
  the rule is back, an app that has not yet been live plays the recorded demonstration; it keeps
  trying, and switches to the control plane by itself once it connects.
- **Stage placement:** in the real room the stage stands on the surface the room placement found
  (`Halcyonic: room placed the stage on the surface, because ...`) and stays there through
  recenters. Otherwise it places itself in front of the person when the session starts, after a
  recenter, after a pause, and after a jump of the tracking space no head makes, and the app's log
  says why (`placed the stage in front of the person because ...`). Reference space changes that
  move nothing, which come in bursts while system windows take and give back focus, leave it where
  it is (`kept the stage where it stands: ...`), and a tracking space that moves takes the stage
  with it (`moved the stage with the tracking space ...`). If it is still out of view, recenter:
  look at a palm, then pinch and hold the Meta icon.
- **Logs:** `adb logcat -s Unity` is the app's log: its `Halcyonic:` lines say whether the control
  plane or the demonstration is shown, and each change of connection status, and its
  `Halcyonic: room` lines what the room placement did and why. `adb logcat -s VrApi` reports the
  frame rate every second.

### Pair over Wi-Fi

A development build can pair with the control plane over the local network instead, with no cable
([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)); release builds cannot.
The Mac and the headset must be on the same network, and not a guest network that keeps devices
apart. On the Mac, start the control plane with its network listener and open pairing
([LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md)):

```bash
HALCYONIC_NETWORK_HOST=0.0.0.0 pnpm dev
pnpm pair        # in another terminal: prints the Mac's address and an eight-digit code
```

Or keep the listener on in the Mac's settings with `pnpm mac-setup pairing on`, which says what it
opens and asks first, and start the control plane as usual.

In the headset, with hands only:

1. Open the menu, then Settings; under Your computer, choose **Pairing** and press **Pair with a computer**.
2. The system keyboard opens: type the address `pnpm pair` printed, such as `192.168.1.23:47801`,
   and press Enter. The next time, the last address is already there.
3. The number pad opens: type the eight digits and press Enter.
4. The stage's banner says "Pairing with ...", then "Paired with your computer at ... Connecting
   over Wi-Fi.", and the stage connects to the control plane. `pnpm pair` names the headset and
   ends.

The pairing is kept in the app's internal storage and survives restarts and `adb install -r`; it
takes the place of an access token written to the headset: with both, the app uses the pairing.

- **Forget:** in Settings, Pairing, press **Forget this computer**, then **Yes, forget this computer**,
  which stands in the middle of the footer, within six seconds. The Mac
  stops accepting this headset (`pnpm devices` shows it revoked), and the app returns to the access
  token, or to the demonstration.
- **A new address:** if the Mac's address changes, forget it and pair again.
- **Logs:** `adb logcat -s Unity | grep --line-buffered "Halcyonic: \(pairing\|paired\|forgot\|connecting over\|connection\)"`
  shows what pairing did and each change of the connection, never the code or the credential.
  To inspect the stored pairing on a debuggable build:
  `adb shell run-as com.halcyonic.xr cat files/halcyonic-pairing.json` (it holds the credential;
  keep it off screen recordings). `adb shell pm clear com.halcyonic.xr` deletes it with the rest
  of the app's data.

### Pairing checks on a Quest

What the Mac cannot check ([network-pairing.md](../validation/network-pairing.md)):

- **Pairing:** the steps above, seated, hands only. Both keyboards appear and can be used with
  hands; the notices on the banner are readable; Settings does not cover the stage. The Mac lists the headset with a readable label (`pnpm devices`).
- **Live over Wi-Fi:** with the USB cable unplugged, the banner under the stage reads "Connected to your computer", and
  `pnpm demo` on the Mac moves the characters. Open a workspace: its activity includes what
  happened before it opened, so REST works over the pinned connection. Approve something: the
  control plane's log shows `realtime client connected` with the device id, and `pnpm devices`
  shows the headset connected.
- **A wrong code:** pair with a wrong code: "The code was not accepted ... 2 attempts left.", and
  `pnpm pair` says a code was refused.
- **Sleep and restart:** take the headset off until it sleeps and put it on again; it reconnects by
  itself. Stop the app and start it: it reconnects over Wi-Fi without pairing again.
- **Revoked on the Mac:** `pnpm devices revoke <id>`: the stage shows the last known state, and the
  connection log says the device was revoked; then forget the Mac and pair again.
- **Another identity:** stop the control plane, move `network-key.pem` and
  `network-certificate.pem` aside in its data directory, start it again: the headset refuses to
  connect and the connection log says the certificate is not the one it paired with. Put the files
  back.
- **Frame rate:** `adb logcat -s VrApi` stays at 72 fps while pairing, since the exchange runs in the
  background.

### Token storage on a Quest

What only a headset can tell about the access token ([headset-token-storage.md](../validation/headset-token-storage.md)):

- **The write.** After the `run-as` write above, `ls -l files/access-token` shows `-rw-------` and 44
  bytes, and the app connects. It still connects after `adb install -r` of a new development build.
- **The move.** Push a token the old way (`adb push ~/.halcyonic/access-token
  /sdcard/Android/data/com.halcyonic.xr/files/access-token`) with no private token, start the app:
  the log says it moved the token, the private file exists with `-rw-------`, and the shared copy is
  gone, or the log says a copy remains, which shows whether the app may remove a file `shell` made.
  Repeat on a paired headset: the move happens there too.
- **The release build.** Install a release build over a development build's data, with a token left
  on shared storage: the log says it was not read, and it is gone or reported.
- **Links and pipes.** Whether `adb shell` or file transfer over USB can make a link or a named pipe
  in `/sdcard/Android/data/com.halcyonic.xr/files` at all (`ln -s`, `mkfifo`); if so, that the app
  starts at once and logs that what was there was not a token.
- **The calls.** That `android.system.Os` `lstat`, `open` with `O_NOFOLLOW | O_NONBLOCK`, `fstat`,
  `read`, `chmod` and `remove` work through JNI under IL2CPP: with no private token, the move above
  logs that it moved the token, with no warning that it could not set the mode to 600, and then
  possibly that a copy is still on shared storage, when the app may not remove a file `shell` made.
  The copy line alone, without the moved line, means a private token was already there, so the
  file was never opened or read. "Could not deal with the access token's old place" names the call
  and its errno: `open failed with EACCES` means the app may not read a file `shell` made, so remove
  it from the Mac as above; any other is a failure to look into. With nothing on shared storage, a
  start logs none of these lines, which shows `ENOENT` is read as nothing there. And what the app's
  umask makes a new file's mode before `chmod`.
- **Backups.** Whether a Meta or Horizon backup ever copied the shared file; the app itself sets
  `android:allowBackup="false"`.
- **The proof.** That the app connects over `adb reverse tcp:47800 tcp:47800` and a workstream's
  history and the folders load, which shows the control plane sees the headset's connections at
  127.0.0.1:47800, the proof holds over USB, and the app's own WebSocket upgrade and HTTP and the
  HMAC work under IL2CPP ([xr-loopback-proof.md](../validation/xr-loopback-proof.md)). Then, with the control plane
  stopped, run a listener on the Mac's 47800 that answers every request without a proof:

  ```bash
  while true; do printf 'HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n' | nc -l 127.0.0.1 47800; done
  ```

  Start the app: it says the access code doesn't match or something else is answering, and stops,
  and the listener prints only `GET /api/health` with an `x-halcyonic-challenge`, never an
  `Authorization` header. A listener that never answers instead reads "Can't reach your computer"
  and is tried again. Last, pause the control plane (`kill -STOP <pid>`) and start the app: it
  says it can't reach your computer within a few seconds and keeps trying, which shows that
  closing a connection ends a read that is never answered under Mono; then `kill -CONT <pid>`, and
  it connects.

### Captures and an unattended headset

`adb exec-out screencap` does not work on a Quest. Meta's capture service saves a JPEG of the
wearer's view to `/sdcard/Oculus/Screenshots`:

```bash
adb shell am startservice -n com.oculus.metacam/.capture.CaptureService -a TAKE_SCREENSHOT
```

A capture shows the room when passthrough is on; keep captures out of the repository.

A headset that is not worn sleeps, and the app pauses. The first command keeps it awake until a
reboot or the second command:

```bash
adb shell am broadcast -a com.oculus.vrpowermanager.prox_close
adb shell am broadcast -a com.oculus.vrpowermanager.automation_disable
```

Even awake, a headset left on a desk can lose its boundary, and the system then puts a dialog in
front of the app, so the visual checks need a wearer. Sit, or set a roomscale boundary: leaning out
of a stationary boundary made everything vanish in the sixth session, with no focus change logged.
That the boundary showed passthrough in the app's place is likely, not verified
([quest-3-device.md](../validation/quest-3-device.md)).

### Milestone 3 checks on a Quest

The workspace, with hands only. `pnpm demo` answers its approval by itself after 3 seconds; for an
approval that waits for you, stop reading the demo's output as soon as it asks, which ends the demo
with a broken pipe error before it answers:

```bash
pnpm demo | sed '/approval requested/q'
```

Run it again for each approval you need, or leave one waiting with
`pnpm demo --scenario approval_required`, which never answers it; `pnpm demo --scenario
question_asked` leaves the agent's question waiting for What do you need from me?
([LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md#drive-it)). Check seated at a desk, with the characters in the
virtual space (2.4 m away) and on the desk (the real room), and never pick up a controller. Then,
in the headset:

- **First time.** On a fresh install, above the character that needs you: a thumb and finger
  closing into a pinch, and "Look, then pinch". It goes after the first open, whichever way, and
  does not come back.
- **Calm peek.** Turn your head slowly, then quickly, across the stage, left and right: no line
  appears. Rest your gaze on one character: after about half a second one line fades in beside it,
  what it needs from you ("Approval needed to use bash: …") or what it did last, agent text as
  "Agent says: “…”", and the character turns to look at you. Glance aside briefly: it stays. Look
  away: it fades out. Only one line ever shows, and a character off to the side of where your head
  points does not peek.
- **Look and pinch.** With a character's line showing and your hands relaxed, in your lap, on the
  armrest or on the desk, pinch with either hand: the workspace opens. With a hand ray on a button
  or another character, a pinch does what the ray points at instead. A pinch while looking at your
  palm, the headset's menu gesture, opens nothing.
- **Relaxed rays.** Seated, forearm resting, hand a little above the desk, palm turned sideways or
  away: the ray reaches the characters, 2.4 m away or on the desk, without raising your hand to
  your shoulder, and pointing at one brings up its line at once. Rest your hands palm down on the
  desk or the keyboard, and type: no ray and no line. Note whether pointing felt natural, and
  whether a ray ever appeared while typing.
- **Open.** Pinch while pointing, or look and pinch: the workspace grows out of the character to
  about two feet in front of you, a ring marks the character and a line joins them, and the
  character turns to look at you. With the characters 2.4 m away it opens below them, over their
  labels but none of their bodies; on the desk it opens above them, clear of the desk. Every other
  character stays in view, and no label or character shows through the workspace. It shows the
  title, the status in words, the execution and its runtime, the objective, the approval
  explained, the buttons offered, and the recent activity, including what happened before the app
  started (otherwise the activity caption says why the history is unavailable). Looking at other
  characters while it is open peeks nothing; pointing at them still does.
- **Approve.** Pinch Approve: the row asks "Approve the request below?", with Cancel and "Yes,
  approve" at the far right, and in place of the tabs and the activity, "The whole request" over
  "bash: Run `pnpm db:migrate` against the local development database". Pinch "Yes, approve":
  Requests reads "Sending to the control plane…", then "Answering the approval…", then "Approval
  answered" once the runtime confirmed; the activity and the status follow the turn to "Turn
  finished". Deny works the same way.
- **Lapse.** Arm a confirmation and wait 15 seconds: the question goes, and "The confirmation timed
  out, so nothing was sent." shows.
- **Stop.** On a waiting approval, pinch "Stop the turn", then "Yes, stop it": "Stopping the
  turn…", then "Turn stopped"; the status reads "Stopped".
- **Instruct.** On a finished character, pinch Instruct: the system keyboard opens and the app's
  hands pause. Type, press Enter: "Sending the instruction…", then "Instruction delivered", and the
  activity shows the mock runtime's new turn. Note whether the keyboard appeared and could be used
  with hands.
- **Collapse.** Pinch Collapse, top right, or point at the character and pinch again: the
  workspace shrinks back into the character.
- **Understanding and Evaluation.** Under the actions, three tabs: Activity, chosen, with a bar
  under it, then Understanding and Evaluation. Pinch Understanding, then poke Evaluation: each shows
  in place of the activity, with Refresh at the right. With the mock runtime they say, in words,
  "From Salidium · Understanding unavailable: it doesn't follow tasks this agent app runs." and
  "From Seorak · Evaluation unavailable: it doesn't follow tasks this agent app runs." Stop `pnpm dev`
  and pinch Refresh: the line says the answer could not be read again, and why. Pinch Approve from a
  section: the whole request shows in its place; pinch Cancel, or confirm, and the details return to
  Activity, where the request shows. Every line readable, 2.4 m away (the workspace below the
  characters) and on the desk (above them), without leaning in.
- **Poke.** Seated, poke the workspace's buttons without leaning. On the desk, push a fingertip into
  the front of a character's body: it opens; typing in front of the characters brings up no line.
- **Focus.** With the workspace open, open the system menu: hands, rays and the peek go, nothing
  can be pressed; close it and they return.
- **Disconnected.** Stop `pnpm dev`: the status adds "last known", and the buttons give way to
  "Nothing can be sent until the connection is live again."; restart it and they return.
- **Legible.** Every line readable where the workspace opens, and the peek at the character's
  distance, without leaning in.
- **Hands.** Halcyonic's hands are the Interaction SDK's: a dark, translucent fill with a grey
  outline, which in a dark space shows mostly as grey outlines. They vanish while another app or
  the system menu has input focus.

Text from outside and a request too long to show at once need an approval and agent text of your
own. Copy `fixtures/scenarios` to a folder outside the repository. In the copy's
`approval_required.json`, make the approval's `summary` a shell command of about 1,500 characters,
many steps joined with `&&`, with a line break (`\n` in the JSON) and a tab in it, that ends in
`&& echo THE-END`; and make the first agent message's `text` hold markup such as
`<alpha=#00>hidden</alpha>`, a backslash sequence (in the JSON, two backslashes then `u0041`), and
the end of text character (in the JSON, a backslash, `u0003`) followed by more words. Stop
`pnpm dev`, start it again with `HALCYONIC_MOCK_SCENARIOS_DIR` set to the copy, and run
`pnpm demo | sed '/approval requested/q'`. Then, with hands only, at both distances:

- **The whole request.** Open the character. What needs you shows two rows of the command, ending
  in "…". Pinch Approve: the row reads "Read the whole request below before approving it." and
  Cancel, with nothing at its right; the tab row reads "The whole request, part 1 of 3" (or 2, or
  4) and "Next part"; the details show the start of the command, its line break as a space. Pinch
  Next part to the last part, which ends in "&& echo THE-END": only then does "Yes, approve" appear,
  at the far right, where nothing was; Cancel never moves. Previous part goes back and keeps "Yes,
  approve". Rest 15 seconds on one part: the question goes, and "The confirmation timed out, so
  nothing was sent." shows; turning a part every few seconds keeps it. Pinch Deny instead: the
  request shows the same way, and "Yes, deny" shows at once. Every part readable without leaning
  in, and note whether reading a long request this way felt reasonable.
- **Text from outside.** In the activity, the agent's message shows its markup as written, the
  backslash sequence as typed, and ‹U+0003› followed by the rest of the message: nothing hidden,
  nothing colored or resized by it. Its line leans as a claim, and where it is cut short it ends in
  "…". The character's notes under it, and the peek, show the command with its line break as a
  space.

With Virtual Display showing the Mac, in the virtual space (the characters 2.4 m away), following
the stage in the log:

```bash
adb logcat -s Unity | grep --line-buffered "Halcyonic: .*stage"
```

- **Windows and the stage.** Open Virtual Display, then point back and forth between its screen and
  the characters for a minute: the characters never move. The log shows lines like `kept the stage
  where it stands: the reference space changed 12 times during focus changes and nothing moved`,
  and no `placed the stage in front of the person` after the first.
- **Returning focus.** With Halcyonic unfocused after using a screen, point at the characters or the
  space around them, away from every window, and pinch: Halcyonic's hands and rays return. If they
  do not, look at a palm, pinch the Meta icon, then close the menu. Note which worked, and do the
  same after minimizing the screens.
- **Look and pinch past a window.** With a screen in front of a character, rest your gaze on the
  character until its line shows, and pinch with a hand resting on the desk: note whether the
  character opens or the screen takes focus. With a workspace open where a screen overlaps it,
  note whether the screen hides the workspace.
- **Recenter.** Turn 45 degrees in the chair and recenter (look at a palm, pinch and hold the Meta
  icon): the characters come in front of you, and the log says `moved the stage with the tracking
  space ...` and then `placed the stage in front of the person because the person recentered ...`.
  If it says `the tracking space moved during a focus change` instead, the recenter came with a
  focus change and the stage stayed where it was: note it.

### The first-time journey on a Quest

The entry as a person new to Halcyonic meets it, in this order, seated, once in the virtual space
and once at a desk. Give the wearer only the goal ("Look at the work you have, then start a small
website") and no coaching; the observer notes where they hesitate, what they press that does
nothing, and any word they read aloud as unclear.

Before the session, on the Mac:

- A control plane with `HALCYONIC_PROJECT_ROOTS` set and OpenCode (`HALCYONIC_OPENCODE_BIN`) or
  Codex (`HALCYONIC_CODEX_BIN`) on a local model served by Ollama, as in
  [LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md), so no hosted model is called.
- At least three projects and more than six workstreams in its journal (for example `pnpm demo`
  more than once), and one approval waiting in one of the projects
  (`pnpm demo | sed '/approval requested/q'`).
- Halcyonic installed fresh, or its data cleared, so the first visit opens the menu by itself.
  Usage left left as it is until Seorak's limits build runs.

In the headset:

1. **First visit.** Once connected, the menu opens by itself on Projects: "What would you like to
   work on?", each project's row saying what waits in it, and New project as the main prompt.
   Running work stays on the stage. It opens by itself only on this first visit.
2. **Hide a project.** Choose the project whose work waits for approval, then Hide from stage: its
   characters leave the stage, and its row reads "Hidden · 1 task waiting". Open Tasks: that work is
   still listed. Open it: it stands on the stage and opens; collapse it, then Show on stage.
3. **Create from a typed idea.** Open the menu, Projects, New project. Type my idea, type a
   sentence on the system keyboard, Make the recap: the recap names the project from its first
   words. Choose how it runs, More options: choose the agent app, then the local model; Done; the
   recap reads "On your computer". Start building stays unavailable, saying to choose where its
   files live.
4. **Choose a new folder.** Choose where its files live, Choose another folder: the places your
   computer allows, each with New folder, Directly in the place and its folders. New folder, accept
   the offered name, Done: the recap shows "A new folder, ..., in ...".
5. **The review.** Start building: the whole request in parts, the folder among it. Yes, start
   building stays locked, saying which part to read to, until the last part; Next part to the end,
   and it unlocks only there.
6. **Start building.** Each step reads "Sent. Waiting for your computer…" (the start, "Waiting for the
   agent…"), then "Confirmed". A character appears reading Starting, and Working only once the
   runtime confirms; note how long that took. The new folder exists on the Mac, and the work runs
   there.
7. **Create from the fixed questions.** New project again, Answer a few questions: four questions,
   said to be fixed questions and not an AI. Choose answers, type one, skip the name, each given by
   Next question: the recap reads "Make ... First, ...". Choose a new folder with the name used in
   step 4 and start building: it is refused because the folder exists, offering Use that folder;
   it returns through the review and starts there.
8. **Work that waits while creating.** With a recap showing, start another approval
   (`pnpm demo | sed '/approval requested/q'`): the menu's bar says something waits, and nothing
   switches by itself. Open that work from Tasks, collapse it, and open New project again: the
   recap is as it was.
9. **The four questions.** In the opened workspace, What is it doing?, Help me understand and What
   was checked? read whole on their tabs, and What do you need from me? shows only while the approval
   waits. Approve, read the whole request, confirm: the answer counts once the runtime confirms it.
10. **Usage left.** Open the menu, Usage: it says "Usage left isn't set up on your computer yet. Set
    it up there to see it here.", in white, not red.
11. **Reset position.** Turn in the chair, then Settings, Your space, The menu, Reset position: the
    menu comes in front of you.

Throughout, note whether the menu sits over a character, its label or a system window, whether
anything needs leaning in to read, and whether any button pressed did nothing. Afterwards, the cases
the journey does not reach: Add a task (Projects, the project, Add a task) to a project in another folder (the recap says every later
task uses the new folder, and the review shows the folder now and from now on), and a control plane
without roots (the choice says your computer doesn't allow any folder yet).

### Hold to talk on a Quest

In a development build, with voice set up on the Mac
([LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md#turn-on-voice)) and the headset connected to that
control plane. The control plane's log names the engine and its warm-up at startup. Seated, in
the virtual space:

1. **The permission.** Open New project from the menu's Projects, then hold Hold to talk in its
   footer: the first hold asks for the microphone and records nothing, and a line on the page says
   to allow it and hold again. Allow it.
2. **An idea.** Hold, say "A website for my bakery that shows the menu and the opening hours", let
   go: Hold to talk itself reads "Listening" in the active tone, its microphone pulsing, then shows
   the transcribe icon and "Writing down", shimmering, and no line is added to the page; then the sentence
   stands in Type my idea's row, chosen, with "This is what your computer heard. Check it before
   you go on." under it. Make the recap: the sentence is the first task, with a name from its first
   words. Nothing has been sent; note how long from letting go to the words.
3. **A tap and silence.** A tap says it was too quick. Hold a few seconds without speaking:
   "I didn't catch anything". Press the Meta button while holding: it says it stopped listening, and
   nothing was sent. Hold, then move the ray off the button while still pinching, and poke and
   pull the finger away: note whether each stops listening, which the editor cannot check.
4. **An instruction.** Open running work: Hold to talk is at the end of the action row. Say an
   instruction: the workspace asks "Your computer heard: ... Send it?" with the words, and
   only Send sends it. Let the 15 seconds lapse once: nothing is sent.
5. **Without voice.** Stop the control plane, start it without the three `HALCYONIC_WHISPER_`
   variables, and hold again: "Voice isn't set up on your computer. Type instead."

Note anything misheard as the person said it, word for word, for the next measurement with real
voices; never record the clip itself.

### The demonstration judges see

A headset with neither a pairing nor an access token plays the recorded demonstration and follows
your answers ([XR_CLIENT.md](../architecture/XR_CLIENT.md)); a release build never pairs. To see it
on a development build that has a token, move the token aside and start the app again, and on one
that is paired, stop the Mac's control plane first; move the token back afterwards:

```bash
adb shell run-as com.halcyonic.xr mv files/access-token files/access-token.off
adb shell am force-stop com.halcyonic.xr
adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
# afterwards
adb shell run-as com.halcyonic.xr mv files/access-token.off files/access-token
```

Then, with hands only, through the menu (ADR 0026). This is intended behaviour, from the recording,
`JudgeMenuWalkTests` and the judge walk render (`far-3s-judge-1-bar` to `far-3s-judge-12-closed` in
`apps/xr/Builds/WorkspaceRenders`); it has not yet been walked on a headset.

- **The line.** Above the stage: "Demo: recorded work played on this headset. Nothing here is
  live." and "It follows your answers. Nothing reaches an agent." Every character carries the Demo
  mark. While the menu is open the line rises over the characters, a degree above the highest a
  risen one reaches, and the peek still hides it.
- **The welcome.** On the demonstration's first visit on this headset the menu opens by itself on
  Projects, under "What would you like to work on?", with the recording's projects and New project
  (`demo-welcome-1-projects`). It opens once: to see it again, clear the app's data. A visit to
  your computer has its own first visit, so pairing afterwards opens Projects once more. Close it
  to follow the walk below from the closed bar; left open, Projects stays while the task below
  comes to wait, and Tasks takes the amber dot (`demo-welcome-2-a-task-waits`).
- **Beside the story.** Of three characters, "Paginate the order history endpoint" and "Send an
  order confirmation email" work for about five seconds and then finish.
- **The bar.** "Add rate limiting to the sign-in endpoint" starts at about 6 seconds and, at about
  7, is Waiting for you. The closed bar under the stage then reads "1 task is waiting for you",
  with Tasks' amber dot (`judge-1-bar`).
- **Tasks.** Open the menu: it opens on Tasks, under "1 task is waiting for you", with the waiting
  task first (`judge-2-tasks`). Its row opens its file beside the menu on Waiting, under its
  "Waiting for you" pill, joined to its character by a light line.
- **The question.** The agent asks: "How long should an address stay locked after five failed
  sign-ins?", with the answers 15 minutes and 1 hour (`judge-3-question`). With larger text the
  question reads first in parts, then its answers a page at a time; turning a page clears a choice
  made on it. Choose 15 minutes and press Send answer (`judge-4-answer-chosen`): "Nothing is sent
  to an agent. The recording goes on as if you answered “15 minutes”." The agent goes on.
- **The request.** About 3 seconds later it waits again (`judge-5-approval`): it wants to run
  `make migrate`. Approve shows the whole request again, a part at a time, with no Yes before the
  last part (`judge-6-request-part-1`). Then Yes, approve (`judge-7-yes`): "Not sent to any agent;
  the recording continues as recorded for approving." Then the migration runs, the tests fail, and
  the round ends. Deny instead shows "Yes, deny" from the first part; the agent says it did not run
  the migration. Cancel sends nothing. Stop is on Activity.
- **Checks.** "Tests failed …: 1 failed, 23 passed", under "Simulated checks · recorded at …"
  (`judge-8-checks`). Changes shows the recorded simulated explanations the same way.
- **Tell it.** On Activity, Tell it offers the recorded instructions as rows, "Count per account
  too" and "Change the test instead" (`judge-9-tell-it`). Choose one and press Tell it: a second
  round plays, and its checks pass.
- **Usage.** Two limits for one practice agent, "At most 62% left" and "At most 79% left", under
  "Recorded for the demo, not from any account", with no Refresh. A limit's side panel says when it
  was seen and resets, and Account "Part of the recording" (`judge-10-usage`).
- **Projects.** The demonstration's one project, "Storefront API", with no folders.
- **New project.** Projects' New project opens it beside the menu. Nobody types: press Talk it
  through, and the companion's recording brings its idea. Its question is quoted as its own ("The
  companion says: “…”"), under "Recorded replies. Nothing here asks the companion."
  (`judge-12-new-project-questions`). Only the recorded answer can be pressed; choose it and press
  Send answer. The recap follows, its suggestions marked Suggested, under "The companion is an AI
  on your computer. It can be wrong, and you can change everything before you start." Start
  building waits: "The demo can't start new work. Real work runs on your computer."
  (`judge-13-new-project-recap`); where the facts need more than a page, the footer's Next page
  turns them. Nothing is sent.
- **Settings.** A page a group: Your space first (Around you, Your room's layout, The characters, The
  menu), then Comfort (Text size, Moving badges, Sounds), each changing on this headset only
  (`judge-11-settings`). The release build offers no Your computer, since it never pairs.
- **Closed.** Close the file and the menu: the bar alone (`judge-14-closed`), reading "Nothing is
  waiting for you." once nothing waits.
- **The end.** Once the story has ended the line adds "This recording has ended and starts again
  shortly.", and about 20 seconds later the characters go back to Not started and it plays again,
  never Disconnected or Last known on the way. Unanswered instructions give way after a minute.
- **Sleep.** Take the headset off until it sleeps and put it back on, or open the system menu and
  come back: the demonstration goes on where it stood, with no Disconnected and no rewind; only a
  recording that had reached its end starts again.
- **Log.** `adb logcat -s Unity` shows `Halcyonic: demonstration plays from its beginning (n)` at
  each start, `demonstration reached an end` at each end, and the `Halcyonic: device` lines
  ("Device measures on a Quest"), never what was answered.

### Room placement checks on a Quest

The room placement ([XR_CLIENT.md](../architecture/XR_CLIENT.md), under "The room") in rooms that
are set up and rooms that are not. Sit at a desk, hands only. Follow the placement in the log:

```bash
adb logcat -s Unity | grep --line-buffered "Halcyonic: room"
```

To see the first launch again, clear the app's data, which also deletes the access token written to the headset,
the app's record of a declined room access and every remembered placement (anchors it saved stay
on the headset, unused), and revoke the spatial data permission, which clearing the data may leave
granted:

```bash
adb shell pm clear com.halcyonic.xr
adb shell pm revoke com.halcyonic.xr com.oculus.permission.USE_SCENE
```

In a room set up with its desk (the headset's Space Setup, with the desk captured as a table):

- **First launch.** The room shows through passthrough and the characters appear in front of you.
  Within a few seconds a line comes up ahead of you, "To stand your agents on your desk, allow
  access to this room's layout.", then the headset explains its spatial data permission and asks.
  Allow it: the characters move onto the near half of the desk, facing you, within reach and
  without turning your head, with their label plates on the desktop, and the line reads "Your
  agents are on your desk." The log says `asking for access`, `room access allowed`,
  `read the room: Read, ...`, `chose a desk ... m away, ...`, and `kept the placement with a
  spatial anchor saved for this room`.
- **Second launch.** Stop the app and start it again from the same seat: no prompt, the
  characters return to the same place on the desk, "Your agents are back on your desk.", and the
  log says `restored the placement saved for this room`.
- **Another seat.** Start it from a chair a meter back or across the room: the log says the saved
  placement `no longer suits where the person sits`, and a new place is chosen, or the characters
  stand in front of you with the reason.
- **Recenter.** Recenter (look at a palm, pinch and hold the Meta icon): the characters stay on the
  desk. At most one line, `its anchor moved ... m with the room, as after a recenter`.
- **System windows.** Open the universal menu, then Virtual Display with the Mac, move its windows
  and close them, several times: the characters stay on the desk, and no room line appears while
  the windows come and go, in particular no `stands in front of the person`, so the anchor stayed
  localized through the focus changes. Note whether the windows cover the characters on the desk.
- **Virtual space.** Point at and pinch, or poke, "Show a virtual space" on the small controls low
  to your right: passthrough goes, the characters stand in front of you in the virtual space, and
  the switch reads "Show my room". Restart the app: still virtual. Switch back: the room and the
  characters on the desk return, with no prompt.
- **Demonstration.** The demonstration judges see (above) plays and follows your answers the same
  way on the desk and in the virtual space, with its line on the banner.
- **Focus.** With the system menu open, Settings' room controls do not respond to a poke or a pinch.
- **Frame rate.** `adb logcat -s VrApi` reports 72 fps with passthrough on, the characters on the
  desk and hands tracked.

Where things are missing:

- **Declined.** Clear the app's data and start it; decline the permission: the characters stand
  in front of you, "Without room access, your agents stand in front of you.", with "Allow room
  access". Start the app again: it does not ask by itself. Press "Allow room access": the headset
  asks again; declined twice, the line says the headset's settings can allow it.
- **A room that is not set up.** In a room with no Space Setup, the characters stand in front of
  you and, once the anchor saved elsewhere has failed to localize (up to six seconds), the line
  reads "You are outside your set-up rooms, so your agents stand in front of you." (or "This room
  is not set up, ..." on a headset with no rooms at all), with the offer "Set up this room".
  Nothing starts by itself. Press it: the headset's space setup opens; cancel it, or capture the
  room with a table. Back in the app, the room is read again, and with a table in reach the
  characters move onto it.
- **No desk in reach.** Sit where no table is within about a meter, for example on a couch facing
  away from the desk: "No free desk or table in reach, so your agents stand in front of you.",
  with "Set up this room".
- **Losing the desk.** If the headset stops tracking the desk's anchor for five seconds while the
  app has focus (covering its cameras may do it; how to cause it reliably is not known), "Lost
  track of your desk, so your agents stand in front of you."; once the anchor is tracked again,
  the characters return to the desk.

### Sound checks on a Quest

The Glaze cues ([XR_CLIENT.md](../architecture/XR_CLIENT.md), under "Sound"), through the headset's
own speakers, seated at the desk, hands only. Set the headset's volume where you would keep it for
hours. Follow the cues in the log, which names each cue, its place and its note, never a workstream:

```bash
adb logcat -s Unity | grep --line-buffered "Halcyonic: sound"
```

- **Ready.** A few seconds after launch: `Halcyonic: sound ready: 87 clips rendered at 48000 Hz in
  ... ms on a worker thread, 18.1 MiB of samples, ...`. Record the milliseconds and the rate: the
  Quest 3's render time is not measured yet. `adb logcat -s VrApi` stays at 72 fps while it renders.
- **Silence while work goes well.** With the demonstration, nothing sounds while characters work,
  run tests or wait, except a soft double tap when work starts and four muted taps when a test run
  starts. Nothing loops, and a character that keeps working stays silent.
- **Waiting for you.** Two strikes rising, the second ringing on, from the character that rises and
  turns to you. It is the loudest cue, yet not alarming.
- **Finished.** The pair falling onto the character's own note, with nothing celebratory about it,
  also when its tests failed.
- **Your actions.** Open a character (pinch, or look and pinch): a chord unfolding in front of you.
  Approve, and confirm: two notes struck together in front of you, then later, from the character,
  the soft double tap as it works again. Close: the chord folding back. Tell it and Stop sound too;
  Stop's result is the character's own caught strike once the recording or the runtime confirms it.
  An instruction typed on the system keyboard sounds its three light taps as the keyboard closes and
  the app has focus again.
- **Presses.** Press a tab, the pager or a rail filter: one soft tap from the button you pressed,
  at once, and nothing more. Press Yes on an approval: only Approve, no tap with it. Press a button
  that is unavailable, such as Refresh while Usage left reads or a locked Yes: Not now, a damped step
  down, quieter than Deny. Press anything just after coming back from another window: nothing,
  since that pinch only returns focus. Follow them in the log as `sound Touch from the control
  pressed` and `sound NotNow from the control pressed`.
- **Where each sounds from.** Turn your head: each character's cues come from where it stands, on
  the desk and after "Show a virtual space" 2.4 m away, at about the same level in both. Characters
  on the left sound from the left, and each keeps its own note while it is shown, the lowest on the
  left when they first appear.
- **Together.** At the demonstration's beginning two characters start at once: their cues come one
  after the other, never on top of each other.
- **Last known.** With the control plane live (`pnpm dev` and `adb reverse`), stop `pnpm dev`: once,
  all six notes as if through a wall, from the whole stage; nothing more while it retries.
  Restart it: nothing sounds for coming back. Changes the resumed session delivers sound, such as
  work the restart left in an unknown state; a session that starts from a snapshot instead is
  silent.
- **Starting again and sleep.** When the demonstration starts again, and after the headset slept
  and woke, nothing sounds for the jump back: no Last known, no burst of cues.
- **Focus.** Open the system menu while a character is about to change: no cue plays while the menu
  has focus, and none plays late after you close it. Open Virtual Display and work on the Mac: the
  characters are silent while its windows have focus. Note whether that silence is what you want
  while you work beside them ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- **Over time.** After an hour beside the characters: were the cues noticed, never annoying, and
  distinct enough to tell which character spoke by its place and note?

### Beside a window on a Quest

A session of 20 minutes or less, seated at the desk, hands only, in passthrough, with a live
control plane that has three to six workstreams and a way to make one need you
(`pnpm demo --scenario approval_required` or `--scenario question_asked`, or a real runtime that
asks). Follow focus and folding in the log:

```bash
adb logcat -s Unity | grep --line-buffered -E "Halcyonic: (focus|large panels|sound|placed the stage|kept the stage)"
```

1. **Place.** Characters in front of you, not on the desk; then open a browser video window (Meta's
   browser, any video) and place it straight ahead. Note how many characters it covers. In
   Settings, Your space, The characters, press Make room for a window and note the count again;
   then Either side of a window, and note it once more; Characters in front turns them back.
   Record the three counts, where the window stood, and whether you had to turn your head to see
   the outermost characters beside it (their labels reach about 37 degrees out). Beside a window,
   with only badges showing, can you tell the tasks apart without peeking?
2. **Watch.** Select the video. The log says focus went to another window; three seconds later,
   large panels are folded. Open a task's file first, and note whether the menu's plane folds away
   and whether the banner says what is still open; nothing else on the stage moves. Characters keep
   animating. Beside a window, the banner says how many more tasks are not shown. On a device that
   has never shown it, coming back the first time with the characters in front, the banner says
   once "Window in the way? Settings can move the characters."
3. **Waiting for you while watching.** Make a workstream need you. The character rises and turns,
   and the banner under the stage says "1 task is waiting for you" in amber. Can you see either past
   the window?
4. **Return by hand.** Pinch on a character, the menu or empty space. The first pinch only returns
   focus: nothing opens or presses, and panels come back as they were. Then open the character and
   read the request.
5. **Half done.** Arm Yes, approve, then select the video before confirming, and come back. The
   workspace says "You went to another window, so nothing was sent. Press it again to confirm.";
   the request still waits. Confirm it afresh; the character shows the runtime's result.
6. **Return by the Meta menu.** Press the Meta button, then Resume. Same as step 4.
7. **Back to the video.** Select it again; the character you approved stays nearby, panels fold.
8. **Flapping.** Move a hand quickly between the video and the stage several times. Nothing on the
   stage rearranges; the log shows losses but no folding unless focus stays away three seconds.
9. **Keyboard.** Start a new project and type its idea. The keyboard takes focus; the menu's plane
   stays put and the draft is kept. Close the keyboard with Done and the text arrives.
10. **Hold to talk**, if the build has it: hold, then select the video mid-sentence. Recording
    stops; nothing is transcribed and the field keeps its text.
11. **Sleep and resume.** Take the headset off until it sleeps, put it back on. Does the stage say
    Last known, then Live, and is no action offered while it says Last known?
12. **Sound options.** First silent (the default): does a Waiting for you while watching go
    unnoticed? Then turn on the option and repeat step 3:

    ```bash
    adb shell touch /sdcard/Android/data/com.halcyonic.xr/files/waiting-for-you-sound-while-away
    ```

    One quieter Waiting for you, never repeated. Was it helpful or an interruption? Remove the file
    to turn it off again (`adb shell rm` the same path). A headset set up with the option's earlier
    name, `needs-you-sound-while-away`, keeps it until that file is removed too.

Record what each step showed in [quest-3-device.md](../validation/quest-3-device.md), including what
did not happen as written. A step not tried stays unverified.

### The interface on a Quest

The whole redesign of [ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)
in one session of 22 minutes, the checks that matter most first, so a short session still answers
them. Seated at the desk, hands only, in passthrough, with a development build (Settings shows Your
Mac and Hold to talk works), `pnpm demo` running against the control plane so something waits for
you, and a browser video to hand. Follow the log while you go:

```bash
adb logcat -s Unity VrApi | grep --line-buffered -E "Halcyonic: (sound|focus|large panels|placed the stage)|FPS="
```

For each step write down what you saw and how it felt, in
[quest-3-device.md](../validation/quest-3-device.md), including what did not happen as written.

1. **Reading at touch distance (3 minutes).** Open the character that waits for you: the workspace
   opens 0.46 m away, 44 by 26 degrees, beside it. Read the whole request, part by part, and poke
   Next, Approve and Cancel. On Doing, press Show details: can you tell what runs the work and
   where? Then open the menu on Projects and read its first page. Can you read for
   a few minutes without strain, and poke every button without leaning or stretching? If reading
   strains, say so: the fallback is the same panel at 1 m, ray only.
2. **The stage's height and the labels (2 minutes).** The characters stand 2.4 m away, their centres
   about 4 degrees below your eyes. Without lifting your chin, can you see every body and read every
   badge and title? Do neighbours' labels ever touch? Do the badge words (Waiting for you, Working,
   Checking its work, Finished this round, Couldn't finish, Can't tell yet, Stopped) say what each
   is doing without the colours? Can you make out each badge's icon, and do Starting's and Working's
   turning icons stay calm at the edge of your view? Checking its work, Finished this round and
   Waiting for you with a count have no room for their icon on the stage: do you miss it? On the
   panels, does each button's icon help you find it, or only crowd its words?
3. **A window in front, three ways (5 minutes).** Put the browser video straight ahead. Count the
   characters it covers. In Settings, Your space, The characters, press Make room for a window:
   the lineup turns 32 degrees right, the outermost label about 67 degrees from straight ahead. Is
   turning your head that far comfortable? Count again. Then press Either side of a window: four
   characters, two each side of the window at 32 degrees, one just above eye level and one below,
   badges with one short line of title. Count again. Can you tell the tasks apart before peeking? Did the window
   really sit where Halcyonic assumed, straight ahead? Then Characters in front. Record the three
   counts and which arrangement you would keep.
4. **Watching beside the characters (2 minutes).** With the workspace open, select the video. Three
   seconds later the panel folds, and the banner under the characters, or under the window beside
   it, says how many tasks wait for you, that the workspace is still open ("Still open: ..."), and
   beside a window how many more tasks are not shown. Can you see it past the video? Pinch to come
   back: the first pinch only returns focus, and the panel comes back as it was. On a device that
   has never shown it, the banner says once "Window in the way? Settings can move the characters."
5. **Sounds (3 minutes).** Press a tab, the pager and a row of the menu: one soft tap from the button
   each time. Press a locked Yes, or Refresh while Usage left reads: Not now, a damped step down.
   Approve a request and confirm: the confirming press sounds Approve's two notes, with no tap.
   Answer a question and send it: Send answer's light tap falling onto a warmer note, with no tap
   from the button.
   Make a task wait for you:
   two strikes rising from its character. Press anything just after coming back from the video:
   nothing. Are the taps welcome or too much over an hour?
6. **Pressing twice in one place (2 minutes).** Approve, then press again where Approve was: it is
   Cancel or nothing, never Yes. On the review, press where Start building was: nothing starts.
   Does a double press ever do what you didn't mean?
7. **Smoothness (throughout).** With a workspace open for a minute, the log's `FPS=` stays at 72.
   Does anything stutter, for example every half second while a panel is open?
8. **Usage left (1 minute).** Open it from the menu, Usage. Each window's meter ends in dots: does it read
   as "at most"? Press Refresh: the meters empty to their tracks and Refresh waits until the read
   is back.
9. **The same places (1 minute).** Through Projects, Tasks, a question and the review: Close stays at the top right, Back at the bottom left, the button the screen leads to at
   the bottom right, the pager above it at the right. Does your hand learn where to go?
10. **Comfort (2 minutes).** In Settings, Comfort, Text size, press Make text larger: the menu grows
    where it stands, and the characters' titles, the peek and the banner grow a step. Open the
    character that waits for you: the workspace is a step larger and opens lower, to stay under the
    titles. Can you read its bottom row, and press its bar, without bending your neck? Is tipping
    your head to read the larger panels comfortable, on a Quest 3S above all? Press Keep
    badges still: Starting's and Working's icons stop turning and Waiting for you stops breathing.
    Press Make sounds quieter, then Turn sounds off, and make a task wait: half as loud, then
    nothing. Quit Halcyonic and start it again: the settings stay. Put them back as they were.

If time remains: the stage's banner when the control plane stops (Last known, every badge grey and
dotted, still readable); the peek, which never covers another label; a desk, its labels resting on
it, read looking down; lists four to a page in two columns.

Results on a Quest 3, including the milestone 2 checks: [quest-3-device.md](../validation/quest-3-device.md).

### Device measures on a Quest

For milestone 6 and the competition's guidelines (at least 60 frames a second, a fast cold start, a
clean pause and resume, a field of view that suits a Quest 3S). Every build logs, under the tag
`Unity`, lines of numbers only (`DeviceMeasures`):

- `Halcyonic: device first frame N ms after start`: the app's first frame, by its own clock.
- `Halcyonic: device view field left eye left L right R up U down D, right eye …, both A across T
  tall`: each eye's field in degrees, once the headset renders in stereo. Record both eyes' four
  numbers in the device record: the layout's field-of-view rules use them.
- `Halcyonic: device frames N in S s, F a second at H Hz, slowest X ms, B below 60, M missed`, each
  minute: frames below 60 a second should be 0 or near it; missed counts frames slower than one
  refresh. "ended by a pause" marks a stretch cut short by sleep or the system menu.
- `Halcyonic: device paused` and `Halcyonic: device resumed after N ms`.
- `Halcyonic: demonstration read K KiB, loaded in L ms on the main thread, parsed in P ms on
  another`: what the recorded demonstration costs a cold start (P delays its first play; L holds up
  a frame).

From the Mac, with the headset on USB (both tools only read the headset, and start or stop
Halcyonic; `HALCYONIC_ADB` names another adb):

```bash
pnpm quest:cold-start -- --runs 5
```

stops Halcyonic, starts it, and prints for each run, on the headset's clock from the start command,
`launch_ms` (Android's own measure), `first_frame_ms`, `stage_ready_ms` (the demonstration's
first play, or a live control plane) and the demonstration's `demo_loaded_ms` and `demo_parsed_ms`,
then their medians. The headset must be worn or its
proximity sensor overridden, or nothing renders.

```bash
pnpm quest:session -- --minutes 60 --every 30
```

prints a row every 30 seconds and writes it to `.private/m6/session-<time>.csv`: battery percent and
degrees, whether it charges, the thermal status (Android's 0 none to 6 shutdown), the hottest CPU,
GPU and skin sensors, the app's frames a second, frames below 60 and slowest frame from its last
minute, and the compositor's `FPS=` where the headset logs a VrApi line. It ends with a summary:
battery used and per hour, the hottest reading, the lowest minute's frame rate, and frames below 60.
A USB cable charges the headset, so `plugged` reads 1 and the battery barely moves; for the hour's
battery figures, use the headset's Wireless debugging (`adb pair`, then `adb connect`), which is
encrypted, and unplug the cable. Never `adb tcpip`: its traffic is not encrypted, and through `adb
reverse` it would carry the access token and every task title across the network.

### The glance on a Quest (spike)

The glance is a small 2D window of Halcyonic's, opened from the Library or the Navigator while a game
or another immersive app runs, to learn whether agents can reach the person there
([horizon-os-multitasking.md](../validation/horizon-os-multitasking.md), "Reaching the person inside
another app"). It shows what waits and what is working, offers only Open Halcyonic, and raises a
notification without the task's title when a task starts waiting. It is in development builds only
(`GlanceInDevelopmentBuilds`); `BuildReleaseApk` deletes and fails an APK that carries it. Its Java
lives in `apps/xr/Android/glance`; `tooling/glance` and `GlanceParityTests` hold its loopback proof
and text rule to the TypeScript and C# ones.

Setup, after installing a development APK and `adb reverse tcp:47800 tcp:47800`, over USB or the
headset's encrypted Wireless debugging, never `adb tcpip`. Keep that one reverse mapping and no other
(`adb reverse --list`): the proof the glance checks names the Mac's own address and port, which is
the same through any mapping, so another mapping, or anything else on the Mac forwarding to 47800
(`ssh -L`, `socat`, a proxy), would let whatever listens on the headset's 127.0.0.1:47800 relay the
challenge (SECURITY.md). Put the access token in the app's
private storage, readable only by the app, with no copy on the headset's shared storage (`run-as`
works on debuggable builds only):

```bash
adb shell run-as com.halcyonic.xr rm -f files/glance-access-token.tmp
adb exec-in "run-as com.halcyonic.xr sh -c 'umask 077; mkdir -p files && rm -f files/glance-access-token.tmp && cat > files/glance-access-token.tmp'" < ~/.halcyonic/access-token
adb shell "run-as com.halcyonic.xr sh -c 'for i in 1 2 3 4 5 6 7 8 9 10; do test \"\$(stat -c %s files/glance-access-token.tmp 2>/dev/null)\" = 44 && break; sleep 1; done; if test \"\$(stat -c %s files/glance-access-token.tmp 2>/dev/null)\" = 44 && chmod 600 files/glance-access-token.tmp && mv -f files/glance-access-token.tmp files/glance-access-token; then echo written; else rm -f files/glance-access-token.tmp; echo not written: the token file is not the 44 bytes the control plane makes, or it never arrived, so write it again; fi'"
adb shell run-as com.halcyonic.xr ls -l files/glance-access-token
```

As for the app's own token ("Install and connect"), the file is whole and private before it takes
the old one's place, the third line prints `written`, and the fourth should list it with
`-rw-------` and 44 bytes.

It refuses a token file that is a link, not the app's own, or readable or writable by anyone else,
and sends the token only on the connection on which the control plane has just proved it holds it.
Its own socket is not bound by Meta's network security configuration, which refuses cleartext to
Android's HTTP stacks only (AOSP's `NetworkSecurityPolicy`), so the build adds no exception, and a
development build checks that the release build's glance check finds the glance in it. The Mac's adb
server answers any local account while the headset is attached, so after each session remove the
token and stop the server:

```bash
adb shell run-as com.halcyonic.xr rm -f files/access-token files/access-token.off files/access-token.tmp files/access-token.new files/glance-access-token files/glance-access-token.tmp
```

```bash
adb kill-server
```

Its log lines, tag `Halcyonic`, codes and numbers only (`adb logcat -s Halcyonic`):
`glance started`, `glance visible`, `glance hidden`, `glance polled ok in 42 ms, 1 waiting, 2
working, visible 1` (other codes: `no_token`, `token_not_private`, `token_malformed`,
`token_unreadable`, `unreachable`, `unproved`, `refused_401`, `unreadable`, `too_large`,
`too_slow`; `unreachable` and `too_slow` name the exception's class after them, as in
`glance polled unreachable (ConnectException)`, and a connection closed before any answer, as
adbd closes one when nothing listens behind its mapping, is `unreachable (EOFException)`), `glance poll failed: <exception class>`, `glance notified, 1 newly waiting`,
`glance notification not allowed`, `glance notification failed: <exception class>`,
`glance poller ended: <exception class>`, `glance stopped`. It polls every 10 seconds while visible and every 30 while hidden.

The checks, in order, about 20 minutes:

1. Glance alone, no game: open Halcyonic's 2D window from the Library (or start it with
   `adb shell am start -n com.halcyonic.xr/com.halcyonic.glance.GlanceActivity`). Its list matches the
   stage's tasks; allow notifications when asked.
2. Over a game: start any immersive game, then open Halcyonic from the Library or the Navigator. Does
   the glance open as a window over the game, and does the game keep running?
3. Live while visible: `pnpm demo --scenario approval_required` on the Mac. Does the row turn to
   Waiting for you within one poll, does "A task is waiting for you" show as a toast over the game,
   and is it heard?
4. Minimised: minimise the window and repeat 3. Do `glance polled` lines continue, and does the toast
   still show?
5. How long it lives: leave it minimised over the game with `pnpm quest:session -- --minutes 60`
   running; note when the poll lines stop. For the battery per hour against the game alone, connect
   over Wireless debugging rather than USB (see "Device measures on a Quest"), never `adb tcpip`.
6. Open Halcyonic from the toast's action and from the window's button: does the game end, and does
   Halcyonic open?
7. With Do Not Disturb on, is the toast silenced?

Then what only a Quest can show of its safety, about 15 minutes more. First, that its plain socket
reaches the control plane with no cleartext exception: `adb reverse --list` shows only tcp:47800,
the stage is connected, and `adb logcat -s Halcyonic` shows `glance polled ok`; `unreachable`
while the stage is connected means the system refused the socket. `adb logcat | grep -i -e
cleartext -e StrictMode` shows nothing for the app.

8. Notify over a game: with a task newly waiting while a game runs, does posting the notification
   ever fail (`glance notification failed`), and does Halcyonic keep running if it does?
9. The deadline on the headset: with a listener on the Mac that answers one byte a second
   (`adb reverse` to it in place of the control plane), does the poll end as `too_slow` near 10
   seconds? Then put the control plane's mapping back.
10. adbd: does `adb shell ss -ltn` (or `netstat -ltn`) show the reverse mapping's 127.0.0.1:47800
    on loopback only?
11. The token file: after the setup above, is `adb shell run-as com.halcyonic.xr ls -l files` mode
    `-rw-------`? With the file replaced by a link (`ln -sf /dev/null files/glance-access-token` under
    `run-as`), does the poll log `token_not_private`?
12. Recents and capture: does the glance's window show in the recent apps, and does a screenshot or
    cast capture its titles?
13. Backups: with `adb backup` refused for the app (allowBackup is false), is the token absent from
    any backup the headset offers?
14. Minimised: do `glance polled` lines continue at the hidden pace with the window minimised over a
    game, and do they stop when the window is closed?

Record what the Quest shows in [horizon-os-multitasking.md](../validation/horizon-os-multitasking.md).
