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
  development option.
- The stage could place itself in front of the user after a recenter or boundary change rather
  than at the world origin.
- The system menu check remains for the next session with the wearer.
