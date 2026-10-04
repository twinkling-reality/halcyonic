# Meta Quest 3 device

- **Question:** Does the XR client build, install and run on a Meta Quest 3, reach the control
  plane over USB, and pass the milestone 2 checks? First of all: does
  `System.Net.WebSockets.ClientWebSocket` work under IL2CPP on the headset?
- **Date:** 2026-09-29.
- **Environment:** a Meta Quest 3 (`eureka`) on Horizon OS 207 (Android 14, API level 34, build
  `UP1A.231005.007.A1`), connected by a USB-C data cable to an Apple M5 Max on macOS 26.7; Unity
  6000.3.25f1 with its own Android SDK, NDK, OpenJDK and `adb` 36.0.0; OpenXR 1.18.0; the Meta XR
  Core and Interaction SDKs 207.0.0; Newtonsoft.Json 3.2.2; the control plane from `main` with the
  mock runtime and the owner's data directory.
- **Method:** a development APK built in batch mode with `QuestBuild`, installed with
  `adb install -r`, the control plane reached through `adb reverse tcp:47800 tcp:47800`, and the
  access token pushed into the app's persistent data directory. Evidence from the control plane
  log, `adb logcat` (Unity, OpenXR and VrApi) and captures of the wearer's view, which stay out of
  the repository because they can show the room.
- **Status:** Verified on the headset: the build, the install, the XR session, `ClientWebSocket`
  under IL2CPP (connect, snapshot, events, reconnect and resume), rendering at 72 fps, and the
  milestone 2 checks except two: the system menu check was not done yet, and "Needs you" was seen
  only in a recorded trace, because a mock runtime bug stalled the live demo (below). Not verified:
  `wss://`, REST through `HttpClient`, the socket across sleep and focus loss, legibility over a
  long session, battery and thermals.

## Findings

### Headset setup

- Developer mode needs the Meta account to belong to a developer organization. The owner's account
  already did, and the Meta Horizon app then offered Developer Mode in the headset's settings.
- Over USB-C, `adb devices` listed the headset as `unauthorized` until the owner accepted Allow USB
  debugging, with Always allow from this computer, inside the headset. It then listed it as
  `device` (`product:eureka model:Quest_3`).

### Build

- `QuestBuild` in batch mode built the development APK on the first attempt. The player build took
  2 min 17 s (IL2CPP, ARM64) after the scripts compiled, and 25 s after a change to one script. The
  APK was 63 to 67 MB.
- The merged manifest has the `com.oculus.intent.category.VR` launcher category, `INTERNET`, hand
  tracking and `minSdkVersion` 32. `targetSdkVersion` is 36, the highest installed SDK, because the
  project leaves it automatic. Meta's build step rewrote `com.oculus.supportedDevices` to
  `quest2|questpro|quest3|quest3s|stanley`.
- The development build carries Meta components that do not belong in a release:
  `com.meta.agenticxr.AgenticMediaProjectionActivity` and `AgenticMediaProjectionService`, with the
  `FOREGROUND_SERVICE_MEDIA_PROJECTION` permission (screen capture for Meta's agent tooling), and
  the Immersive Debugger's assemblies. `Assets/Resources/DevAgentSettings.asset`, with the Mac's
  LAN address and a token, is packaged because it sits in Resources. The player also announces the
  headset's LAN address for Unity's profiler connection, as development builds do.
- **Unity's Android build restarts the adb server, which drops `adb reverse` rules.** After a
  rebuild and reinstall the app could not reach the control plane. Once `adb reverse` was run
  again, it connected by itself within about four seconds.

### Install and token

- `adb install -r` of the 63 MB APK took about 6 s.
- The first launch created `/sdcard/Android/data/com.halcyonic.xr/files` (owned by the app, group
  `ext_data_rw`) and logged the missing token, as designed. The token pushed there with `adb push`
  (owned by `shell`, mode 0644) was read at the next launch, and it survived `adb install -r`.

### XR session and rendering

- The OpenXR session went from idle to focused in about 0.1 s. VrApi reported 72 fps with no stale
  frames and about 1 ms of GPU time per frame for the app.
- The characters rendered magenta. A primitive's default material uses the Standard shader, which
  the build left out: the build log shows `Legacy Shaders/Diffuse`, an always-included shader,
  compiled, and no Standard. Characters now use that shader (0637707), verified on the headset.
- The stage is placed from the world origin. After the boundary was recreated, the stage was out of
  view until the owner recentered.

### WebSocket and REST

- **`ClientWebSocket` works under IL2CPP on the Quest 3.** Through `adb reverse`, the client
  connected to `ws://127.0.0.1:47800/realtime`, with the bearer token on the upgrade request, about
  four seconds after launch, applied the snapshot and followed events. It reconnected by itself:
  with a snapshot after the control plane stopped and restarted, with a snapshot after a switch to
  a replayed fixture journal and back, and resuming without a snapshot after a `node --watch`
  restart. It stayed live throughout, which it does not after a message fails to parse, so
  Newtonsoft.Json read the messages under IL2CPP with the default managed stripping.
- Not exercised: `wss://`, and REST through `HttpClient` (`ControlPlaneApi`), which the stage does
  not call yet.

### Milestone 2 checks on the headset

- **Characters with titles and written statuses:** verified with the live journal's workstreams,
  three and then six.
- **"Needs you":** verified with the recorded trace (`multiple_workstreams.jsonl`). The character
  read "Needs you", with the approval written out, rose toward the eye line, and read "Turn
  finished" after the approval. The live demo could not show it (below).
- **Replays labeled as recorded:** verified: "Live (recorded data)" above the stage and "recorded"
  on each character. In `failure_modes.jsonl`, the failed character gives its reason ("The model
  provider rejected the request: rate limit exceeded."), and so does the unknown one ("The runtime
  process exited unexpectedly while a command was running."). The interrupted one reads "Stopped"
  with no reason line, because a person asked for the stop and it needs no attention.
- **Stopping the control plane:** every character at once read "last known" and lost saturation.
  The line above the stage was out of view in every capture. Restarting reconnected without
  restarting the app, and the three executions that were starting became "State unknown", with
  the reason, as designed.
- **Hands only:** tracked hands rendered in the app with no controller. Milestone 2 has no
  interaction to perform.
- **System menu hiding the hands (VRC.Quest.Input.4):** not done yet.

### Mock runtime bug found here

`pnpm demo` against a journal that already held mock runtime events stalled: its start commands
completed, but no runtime event was journaled, so the executions stayed at `starting`. The mock
runtime numbers its sessions from 1 in every control plane process, so native event ids repeat
across restarts, and the journal ignores an event whose native id it already holds, logging that
only at debug level. The same demo on a fresh data directory completed.

### Captures and working without the wearer

- `adb exec-out screencap -p` fails on the headset (exit status 1, no data). Meta's capture service
  works: `adb shell am startservice -n com.oculus.metacam/.capture.CaptureService -a
  TAKE_SCREENSHOT` saves a 1440 by 1440 JPEG of one eye's view to `/sdcard/Oculus/Screenshots` in
  about a second, and shows a notification each time.
- A capture shows what the wearer sees, including the room when system passthrough is on.
- During frequent captures, a world-locked black rectangle appeared in them where the owner saw a
  panel. What it was is not established; a system notification layer, which captures show black,
  is the likeliest explanation.
- With the proximity sensor overridden (`adb shell am broadcast -a
  com.oculus.vrpowermanager.prox_close`), the headset stayed awake on the desk. But the boundary was
  lost, a system dialog asking for a new one took focus from the app, and captures were black until
  the owner wore the headset, created a stationary boundary and recentered. Unattended visual checks
  need the wearer, or the boundary turned off, which is a safety setting for the owner to decide.

## Consequences

- Keep `ClientWebSocket`; the native Android WebSocket fallback is not needed now, and
  `IRealtimeTransport` stays the seam. Still to verify on hardware: `wss://`, `HttpClient` for
  REST, and the socket across sleep, headset removal and focus loss (milestone 6).
- Run `adb reverse` again after every build.
- Objects created at runtime must use always-included shaders, or materials that assets in the
  build reference; editor-only availability hides the gap until a device build.
- Before a release or competition build: remove Meta's agentic media projection components and
  the Immersive Debugger, keep `DevAgentSettings.asset` out of the build, and build without the
  development option. The release build now does all four
  ([horizon-store-release.md](horizon-store-release.md)).
- The stage could place itself in front of the user after a recenter or boundary change rather
  than at the world origin.
- The system menu check remains for the next session with the wearer.

## Second session: characters and the workspace (2026-09-29, evening)

The build from `43cc7c3` (the characters of ADR 0013 and the workspace of ADR 0014), against a
control plane on a clean data directory, driven by `pnpm demo` held at its approval. The owner
wore the headset with Meta's Virtual Display showing their Mac.

- **Verified:** the bots rendered on the arc with their written statuses, the character that
  needed a decision rose in the middle with its approval explained, and the owner opened its
  workspace by pinching at it. The panel showed the objective, the approval and the recent
  activity, with agent text marked as such. Approving with the second press reached the control
  plane as `execution.respond_to_approval`, completed, and the panel then showed "Approval
  answered", the test run passing and "Turn finished" from the runtime's own events, offering only
  Instruct afterwards. Opening the workspace read its history over REST, so `HttpClient` works under
  IL2CPP on the Quest 3 too.
- **Virtual Display inside Halcyonic:** the Mac's screens appeared as windows inside Halcyonic's
  scene, and captures included them. With them open, input focus went to whichever the hand ray
  reached first, and the windows hung in front of the characters. The XR session flapped between
  focused and visible dozens of times, OVRPlugin reported reference space changes with its recenter
  count reaching 99, and the stage re-placed itself about every two seconds. After the owner
  minimized the windows, Virtual Display stayed the top resumed activity and Halcyonic stayed
  unfocused, showing no rays, until `am start` brought it to the front.
- **Interaction:** the gaze peek appeared over each character the head swept past; the hand ray
  appeared only at one raised hand angle, so pointing from a relaxed seated posture failed; the
  panel let the labels behind it show through and covered the rest of the arc.
- **Not checked:** instructing through the system keyboard, collapsing, and the system menu.
- The log line `Hands: failed to remap HAND_TRACKER ... missing runtime permission(s)` refers to
  `horizonos.permission.internal.ACCESS_HAND_TRACKING`, an internal permission third-party apps are
  not granted; hand tracking itself worked.

## Third session: the room, sound and the loop on a real desk (2026-09-30, morning)

Builds from main between `5388bbb` and `863f73b`, against a control plane on a clean data
directory. Mock executions were started through REST so that an approval waited for the wearer.
The owner wore the headset at their desk.

- **Room access and scan:** the owner allowed the spatial data prompt. The first read found no
  room ("NotSetUp, NoRoomsFound"). "Set up this room" started the system's space setup, and the next
  read found 7 surfaces and 8 objects.
- **No surface fit at first.** The per-surface explanation (logged since `40581cc`) showed why:
  - The desk was in reach, 0.30 to 1.21 m away and about 0.5 m below the eyes.
  - A monitor 0.59 m tall stood over most of its middle.
  - With the owner seated about 50 cm back, the free strip in front of the monitor was about 18 cm
    deep. A margin of half a label plate from every edge left no room on it.
- **On the desk after a second scan.** The owner cleared the desk and ran space setup again. The app
  then chose the desk 0.55 m away, 10 degrees left and 0.37 m below the eyes, and kept the spot with
  a spatial anchor.
- **Reads while not worn:** reads made while the headset was asleep, or lying on the desk, found no
  room, or no surface below the eyes. The room must be read with the person seated and the headset
  tracking.
- **Room fixes not yet tried on the device** (`e27aa01`): the client reads the room again on resume
  after a read that found none, and the margin is now a quarter of a label plate plus 1 cm.
- **Sound:** 79 clips rendered at 24,000 Hz in 0.64 to 0.71 s on a worker thread, holding 8.8 MiB of
  samples. The owner heard the cues. The log recorded:
  - Working, Verifying, Needs you and Turn finished from their characters;
  - Last known from the whole stage when the connection dropped;
  - Open, Collapse and Approve from the workspace.
- **Interaction:**
  - **Worked:**
    - The gaze peek stayed calm as the head swept.
    - Touching a character on the desk opened its workspace.
    - Everything was readable.
  - **Did not work, or worked badly:**
    - Look and pinch did not work for the owner. The gaze is the head's direction (the Quest 3 has no
      eye tracking), and a peek needs the head within 7 degrees of a character, with nothing showing
      where the head points.
    - The seated hand ray appeared only at some hand angles, because the palm's direction gates it.
    - A peek label could overlap, or go behind, a neighbouring character on the desk.
- **Approval on the desk:** the owner opened the character by touch and approved. The runtime
  resumed, its tests passed and the turn finished; then the owner collapsed the workspace. Each step
  played its cue.
- **Operations:**
  - **Control plane restarts:** `pnpm dev` runs `node --watch`, so merging code into the checkout
    restarted the session's control plane. An execution waiting at its approval became "unknown: the
    control plane restarted", and the workspace rightly offered no action. Headset sessions now run
    the control plane with `node apps/control-plane/src/main.ts`.
  - **adb restarts:** Unity Android batch runs, in any worktree or session on the Mac, restart the
    adb server, which drops `adb reverse`. A loop that re-applies it every 2 s restored the
    connection within about 3 s, and the client played Last known once.
- **Not checked:**
  - the judges' demonstration on the desk;
  - instructing through the keyboard;
  - the system menu;
  - Virtual Display alongside the stage;
  - paging through a long approval request;
  - pairing over Wi-Fi.

## Fourth session: new work and interaction checks (2026-09-30)

- **Environment:** Quest 3 over USB, development APK built from main `fddb736`, a live control plane
  with an isolated journal and only the mock runtime, and `adb reverse` on port 47800. The headset
  connected as `halcyonic-xr`. No paid model was available in this session.
- **Room:** the app reported `NotSetUp, NoRoomsFound` at launch and again on resume, and said the
  saved placement was no longer held by the headset. The owner chose the virtual space. The prior
  desk placement was therefore not reverified.
- **New work:** the owner could open and read the panel, choose the mock runtime and its simulated
  fast model, and type an objective through the system keyboard. Pressing Review and start while
  `Project: new project` had no name left the panel in place with a small note at the bottom asking
  for a project name. The owner initially took this as no response. Selecting the existing project
  opened the review pages. The owner confirmed on the last page. `workstream.create` completed, but
  `execution.start` was rejected with `invalid_runtime_options`: the panel sent no mock `scenario`.
  Further presses of Start repeated that rejection. The headset showed the created workstream as
  Not started, which the owner could mistake for the separate approval check character.
- **Panel placement:** the fixed New work panel blocked a character behind it. The owner expected
  to select and move the panel as in other VR interfaces, but this build offers no move control.
  Closing the panel exposed the character. This is direct wearer feedback, not a capture inference.
- **Interaction:** the device log recorded gaze and hand peeks, a look and pinch accepted for each
  character, workspace open and collapse sounds, and a hand ray reaching controls. The owner opened
  the separate simulated approval workstream and approved it. The control plane recorded
  `execution.respond_to_approval` as completed, and the owner saw Turn finished (simulated). These
  observations establish command completion and basic headset interaction, not comfort or precision
  of the reticle, seated ray or peek depth.
- **Still open:** system menu behavior, Virtual Display, a long approval request, real runtime
  start from the headset, and room re-read after a valid room scan.

## Fifth session: the first-time journey, partly (2026-09-30, evening)

- **Environment:** Quest 3 over USB, development APK built from main `a952b04` (the project rail,
  entry panel, folder step and Usage left), a live control plane on main with the owner's journal,
  one project root (a dedicated, empty folder), OpenCode 2.0.18 registered with a Halcyonic-only
  OpenCode configuration directory, Ollama serving local models, and simulated projects added so one
  approval waited. `adb reverse` on port 47800.
- **Connecting:** the new build first played the recorded demonstration and logged "Unable to connect
  to the remote server". The control plane had answered the headset's `/realtime` with 401: the
  access token pushed in an earlier session no longer matched the Mac's. Pushing the current token
  and restarting the app connected it live. The app's wording did not distinguish a refused token
  from an unreachable Mac.
- **Welcome and Create:** the owner found Create a project without help. The first project ran on
  the mock runtime, listed as "Mock runtime (development fixture)" beside OpenCode: it finished at
  once, built nothing and asked for no folder, because the mock runtime uses none. A newcomer took
  the simulated runtime for a real one.
- **A real start from the headset:** the second project chose OpenCode and the project root itself
  as its folder. The control plane recorded project, workstream and `execution.start` completed
  after `runtime.execution.started`, and the character appeared. The start carried
  `opencode/space-bunny-free`, a free hosted model the owner picked in More options: OpenCode's list
  puts its seven hosted models before every local one, and the local model the Halcyonic-only
  configuration names came twelfth. The model list's wording marks hosted models, but their order
  made a remote model the easy first choice. (Corrected 2026-10-01: an earlier version of this
  record said no model was chosen; the journal shows the choice.)
- **Approvals with a real agent:** OpenCode asked for approval; the owner approved in the headset,
  and `execution.respond_to_approval` completed with `runtime.approval.resolved`. Two approvals went
  through this way.
- **A question the headset could not show:** the agent then called OpenCode's `question` tool to ask
  the person something. Halcyonic recorded only a tool start, so the character kept reading Working
  while the agent waited for an answer it could not receive. The owner stopped the turn
  (`runtime.turn.interrupted`, command completed). No files were written.
- **The stage:** stale simulated work flagged for attention outranked the new work, which left the
  six-character stage; Connect projects hid those projects and brought it back.
- **Usage left:** read live through Seorak (one Codex weekly reading, account not identified); the
  headset's rendering of it was not confirmed with the owner.
- **Afterwards:** about two hours later the adapter recorded `runtime.connection.lost` for the
  OpenCode server while the server process still ran, and the execution stayed `unknown` overnight.
- **Owner feedback:** typing on the system keyboard is tiring; voice input was requested
  ([ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md), proposed).
- **Still open:** the rest of the journey (fixed questions, Open now while creating, Move and Reset
  position, the system menu), legibility and comfort, a local model through the headset, and
  recovery after the connection loss.

## Sixth session: the menu on a headset, partly (2026-10-04, afternoon)

- **Environment:** the same Quest 3 and OS build, over USB. A development APK built from main
  `ee1acdb9` (114,169,715 bytes): the menu, files and side panels on one plane
  ([ADR 0026](../decisions/0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md)).
  The control plane ran main's server code (unchanged since `f43d506c`) with the owner's journal
  (6 projects and 11 tasks, 4 of them `unknown` from earlier sessions), one project root, OpenCode
  2.0.18 (the pinned binary), voice and the companion. Ollama 0.34.4 had `qwen3.6:35b-a3b-nvfp4`
  loaded. Two practice tasks waited (`pnpm demo --scenario approval_required` and `question_asked`,
  the question in 2 prompts). The stage stood on the desk in passthrough. The computer was busy with
  other work (1-minute load 40 to 50, swap nearly full).
- **The access token before the start:** the old place on shared storage still held an
  `access-token` (44 bytes, readable by all), beside three crash dumps from 2026-09-30 23:17 (one of
  467,218 bytes, two empty).
- **Writing the token with `run-as`:** the runbook's one-step write left `files/access-token.tmp`
  (44 bytes, mode 600) and no `files/access-token`. The app started without a private token, logged
  "moved the access token from shared storage into app-private storage", and connected live.
  - `adb shell run-as com.halcyonic.xr sh -c 'echo A; echo B'`, given as separate words, ran only the
    first command inside `run-as`: adb joins the words, and the inner quotes are lost.
  - The same command as one quoted string, `adb shell "run-as com.halcyonic.xr sh -c '...'"`, ran
    whole.
  - With `adb exec-in`, a file written by the command's second part appeared only after later adb
    commands had run, so the command goes on after `adb exec-in` returns.
  - What worked: `adb exec-in` writing the content to `files/access-token.tmp`, then one quoted
    `adb shell "run-as ... sh -c 'test -s ... && chmod 600 ... && mv -f ...'"`, giving
    `files/access-token` at mode 600 and 44 bytes.
  - `pm clear com.halcyonic.xr`, used for a first visit, also removed the shared copy.
  - At the end the tokens were removed, and `pnpm quest:check -- --closed` passed.
- **`pnpm quest:check`:** every line passed, including "connection live: the control plane proved it
  holds the token, and the upgrade followed on that connection", over `adb reverse` under IL2CPP.
  The exception was the move line: "the log no longer reaches the app's start". The headset's log
  held no line from the app's first second, either in the check's own read or in a capture streamed
  from before the start; its first line was `connection Synchronizing`.
- **Frame rate:** VrApi's compositor reported 72 fps throughout, with no torn or stale frames, CPU
  level 4 and GPU level 2. Per second: 1108 at 73/72, 669 at 72/72, 268 at 71/72, 110 at 70/72 and 2
  at 69/72.
- **Device measures never ran:** the app logged no `device first frame`, `device view field` or
  `device frames` line in 25 minutes. `HalcyonicBootstrap` adds `DeviceMeasures` only to a scene
  without a `CharacterStage`, and `Stage.unity` carries one but not `DeviceMeasures`. `ViewField.Current`,
  which the placement rules keep the plane inside, is set only by `DeviceMeasures`, so on a headset
  it has never been set.
- **Stage labels:** every state badge and mark on a character's label drew its pill sized for its
  icon alone, the words running out of it with the icon over their first letters (Can't tell yet,
  Checks failed, Waiting for you, Practice). A file's own state pill drew correctly.
  - The cause, found in the code: characters that arrive before the stage is placed are built under
    the inactive arc, where TextMeshPro has not woken and measures 0.
  - The badge's and mark's caches never measured again.
  - The editor renders build every label under an active parent, so they never met it.
- **A file over the stage:** an open file covered the stage's line "Connected to your computer".
- **An old task's Activity** showed the adapter's diagnostic to the person, in red: "Can't tell what
  it's doing: After the OpenCode event stream reconnected, the session could not be read (GET
  /api/session/...)".
- **New project:**
  - The owner opened it and used Hold to talk: one clip, heard and written down in about 2 s.
  - The headset then logged two presses on prompts that weren't available.
  - Start building was never sent: no `project.create` reached the control plane.
  - An old `unknown` task, named like the new project, read as if the new project had failed.
- **Leaning back:** everything disappeared when the owner leaned back, and the app logged no focus
  change. Likely the stationary boundary, which shows passthrough in place of the app when the head
  leaves it. Not verified.
- **Owner feedback:**
  - Moving the panel: there is no way to move it out of the way to see the computer's monitor or the
    room (dragging is not in this build).
  - Multitasking: no clear way to multitask, or to reach the computer's screen in the headset with
    the menu open.
  - Too much at once: too much shows at once for a newcomer, and it is hard to control and
    understand.
  - Motion: nothing moves. Holding to talk changed no colour or icon, and loading showed no
    animation or shimmer.
  - Priorities for the product: easy to leave, easy to change, ambient by default, and other windows
    open beside it for multitasking.
- **Not done:** the comfort walk (HEADSET_SESSION.md, item 4), the judge's path on the release
  build, beside a window, the glance, the token and proof checks past the first connection, sound,
  and the agent's approval and question on the headset.
