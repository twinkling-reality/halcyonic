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
and again on a desk half a meter away, saves `far.png`, `desk.png` and each part alone in
`apps/xr/Builds/WorkspaceRenders`, and logs where the workspace opened. It fails if a pixel of the
workspace changes when the stage behind it is drawn, if a character's body is behind it, or if its
center leaves the comfortable band. It then shows the Understanding and Evaluation sections with
the bundled demonstration's answers, at the story's approval and after approving, and saves
`far-understanding.png`, `desk-evaluation-after-approving.png` and so on, each with a
`-closeup.png` at a Quest 3's 25 pixels per degree, for judging legibility (`far-closeup.png` is
the activity). It fails if a section lets the stage show through, a line does not fit, or a part's
availability, coverage and freshness is cut short, and it checks on real labels that source text
shows exactly as written and that a quote cut short ends in an ellipsis. Then it confirms an
approval of a very long shell command and saves `far-approval.png`, `far-approval-closeup.png` (its
first part) and `far-approval-last-closeup.png`, and the demonstration's short one as
`far-approval-short-closeup.png`, and the same for the desk; it fails unless the parts together are
the whole request and "Yes, approve" shows on the last part only. It shows an agent's question
(the mock's scripted one, as `far-question.png` and its close-up): with an answer chosen, its
second prompt with several, its typed answer beside Hold to talk, three questions shown, a question
longer than two lines in parts, twenty answers offered and a secret question Halcyonic cannot
answer; it fails if any of it lets the stage show through, cuts a word short, or runs below the
workspace, if Next does not reach the second prompt, or if a long question counts as read before
its last part shows. Last, it puts hostile text on
every label that shows text from outside (saved as `far-untrusted-closeup.png` and
`far-untrusted-activity-closeup.png`) and fails if a label interprets markup or an escape sequence,
shows text that did not go through the one rule, or cuts a line short without an ellipsis; the log
says how many labels each pass checked. It works in a new, unsaved
scene, and leaves the committed TextMeshPro font assets as they were, which drawing text in the
editor would otherwise upgrade and save; characters the static atlas lacks, such as the minus sign
in a change summary, are drawn from it as look-alikes for the render only, and the log names them.

The project rail and the entry panel render the same way, **Halcyonic > Render the Entry Panel
Over the Stage**, or in batch mode:

```bash
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Workspace.Editor.EntryRender.Check -logFile ~/Library/Logs/Unity/halcyonic-xr-entry-render.log
```

It saves the rail alone and every screen of the entry panel (welcome, Connect projects, More work,
Create a project, the guided questions, the recap with and without Needs you, More options, the
review's first and last pages, a refused start and a request that may have run) over both stages in
`apps/xr/Builds/EntryRenders`, each with a close-up, and logs where the rail and the panel stand
(`Halcyonic: entry render ...`). The checks are in [XR_CLIENT.md](../architecture/XR_CLIENT.md),
"Scene".

The stage beside a window, and large panels folded and restored, render the same way, **Halcyonic >
Render the Stage Beside a Window**, or in batch mode:

```bash
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Workspace.Editor.AmbientRender.Check -logFile ~/Library/Logs/Unity/halcyonic-xr-ambient-render.log
```

It saves the lineup in front of and turned aside from a window-sized plate (1.4 by 0.79 m at 1.6 m,
straight ahead at eye level) and the entry and Usage left panels open, folded and restored, in
`apps/xr/Builds/AmbientRenders`, and logs how many characters' bodies the plate covers. It fails if
turning the lineup aside uncovers no one, if a folded panel still shows, or if a restored panel
differs by a pixel from before it folded. The plate is not a real window: only the headset shows
what a real one covers.

The Usage left glance renders the same way, **Halcyonic > Render Usage Left Over the Stage**, or in
batch mode:

```bash
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Workspace.Editor.UsageLeftRender.Check -logFile ~/Library/Logs/Unity/halcyonic-xr-usage-left-render.log
```

It saves the chip on the rail, then the panel with two readings, not set up, no reading yet, while
reading, a partial answer and with an agent name from outside, over both stages in `apps/xr/Builds/UsageLeftRenders`,
each with a close-up, and logs how far below eye level the panel spans (`Halcyonic: usage left
render ...`). It fails if the chip leaves the rail's free room or comes near a rail button, if the
panel covers a character's body or label plate or reaches beyond the space the workspace may take,
if one of its own words is cut short, or if outside text does not show as written. It reads no
control plane.

Batch runs can end with exit status 134 after `Exiting batchmode successfully now!`: the
Interaction SDK's telemetry library (`ISDKEngineTelemetry.dylib`) aborts on a mutex during
shutdown, as macOS's crash reports show. It happens after the work is done and saved; read the
verdict from the log (`Halcyonic: workspace render: ...`, `Halcyonic: stage interaction set up.`,
`Build Finished, Result: Success.`) rather than from the exit status.

Text in the workspace is TextMeshPro. Its essential resources are committed in
`Assets/TextMesh Pro`, imported from the builtin `com.unity.ugui` package, without the EmojiOne
sprites and the HDRP and URP shader graphs, which the stage does not use.

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
connects to `ws://127.0.0.1:47800/realtime` (override with `HALCYONIC_ENDPOINT`). The line above
the characters says whether the state is live.

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
- The approval workstream shows "Needs you" with the approval explained, and rises toward the eye
  line; after the demo approves it, it returns to working and then "Turn finished".
- The replayed traces show the same states, labeled as recorded, including the failure trace's
  failed, unknown and interrupted characters, each with its reason written out.
- Stopping the control plane shows "Disconnected, showing the last known state"; restarting it
  reconnects without restarting Play mode.
- Everything works with hands only; no controller is needed.
- Opening the system menu hides the hands and keeps the scene rendering (VRC.Quest.Input.4).
  Whether the Simulator reproduces the real focus change is itself something to confirm.

## On a Quest

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

`ClientWebSocket` works under IL2CPP on a Quest 3 ([quest-3-device.md](../validation/quest-3-device.md)).

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
account and secrets, and are not automated:

1. Create a release keystore outside the repository, for example with `keytool -genkeypair` from
   Unity's OpenJDK (`PlaybackEngines/AndroidPlayer/OpenJDK/bin`). Back it up with its passwords:
   every update to the app must be signed with the same key. Never commit it, and never put its
   passwords on a command line.
2. Raise **Bundle Version Code** (`AndroidBundleVersionCode` in `ProjectSettings.asset`) above
   that of every build already uploaded for the app.
3. Build the release APK, then sign it with the release key. `apksigner` replaces the debug
   signature and asks for the passwords:
   ```bash
   $TOOLS/apksigner sign --ks /path/outside/the/repository/release.keystore apps/xr/Builds/Halcyonic-release.apk
   $TOOLS/apksigner verify --verbose --print-certs apps/xr/Builds/Halcyonic-release.apk
   ```
4. Upload it to the release channel with Meta Quest Developer Hub or `ovr-platform-util`.

Once the release APK carries the owner's key, the headset needs an uninstall before installing it
over a debug-signed build, and the uninstall deletes the pushed access token.

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
Copy the token there and start the app again:

```bash
adb push ~/.halcyonic/access-token /sdcard/Android/data/com.halcyonic.xr/files/access-token
adb shell am force-stop com.halcyonic.xr
adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
```

The token survives reinstalls. The control plane logs `realtime client connected` for
`halcyonic-xr`.

If the line above the stage says "Your Mac refused this headset's access token", the token on the
headset is from an earlier data directory or was replaced on the Mac: the control plane answered
401, and the app stopped trying. Push the current token and start the app again, as above. "Can't
reach your Mac" instead means nothing answered: check the control plane is running and
`adb reverse tcp:47800 tcp:47800` is in place.

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

In the headset, with hands only:

1. Low to your left, under the stage, pinch or poke **Pair with a Mac**.
2. The system keyboard opens: type the address `pnpm pair` printed, such as `192.168.1.23:47801`,
   and press Enter. The next time, the last address is already there.
3. The number pad opens: type the eight digits and press Enter.
4. The line above the button reads "Pairing with ...", then "Paired with the Mac at ... Connecting
   over Wi-Fi.", and the stage connects to the control plane. `pnpm pair` names the headset and
   ends.

The pairing is kept in the app's internal storage and survives restarts and `adb install -r`; it
takes the place of a pushed access token: with both, the app uses the pairing.

- **Forget:** pinch **Forget this Mac**, then **Yes, forget this Mac** within six seconds. The Mac
  stops accepting this headset (`pnpm devices` shows it revoked), and the app returns to the pushed
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
  hands; the line is readable; the panel and the room controls do not cover the stage or each
  other. The Mac lists the headset with a readable label (`pnpm devices`).
- **Live over Wi-Fi:** with the USB cable unplugged, the line above the stage reads live, and
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
front of the app, so the visual checks need a wearer.

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
  "Understanding unavailable: Salidium does not observe sessions of the mock runtime." and
  "Evaluation unavailable: Seorak does not observe sessions of the mock runtime." Stop `pnpm dev`
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
- Halcyonic installed fresh, or its data cleared, so the welcome shows. Usage left left as it is
  until Seorak's limits build runs.

In the headset:

1. **Welcome.** Once connected: "Welcome", one line, Connect projects and Create a project, and Not
   now. Running work stays on the stage behind it. Once any of them is chosen it does not come back.
2. **Connect projects.** It says it lists the projects on your Mac that Halcyonic knows. Hide the
   project whose work waits for approval: its characters leave the stage, its rail chip reads
   "Hidden · 1 needs you", and More work lists that work first and reads "1 needs you". Press it in
   More work: it stands on the stage and opens; collapse it, show the project again.
3. **Create from a typed idea.** Create a project, Type my idea, type a sentence on the system
   keyboard: the recap names the project from its first words. More options: choose the runtime,
   then the local model; it reads "on your Mac". The recap asks where its files live.
4. **Choose a new folder.** Choose: the places your Mac allows, each with New folder, the place
   itself and its folders. New folder, accept the offered name: the recap shows "new folder ... in
   ...".
5. **The review.** Start building: the whole request in whole words across the panel, the folder
   among it; Next part to the end; Yes, start building only on the last part, where Start building
   was not.
6. **Start building.** Each step reads "Sent, waiting for the result", then "Confirmed". A
   character appears reading Starting, and Working only once the runtime confirms; note how long
   that took. The new folder exists on the Mac, and the work runs there.
7. **Create from the fixed questions.** Create a project, Help me figure it out: four questions,
   said to be fixed questions and not an AI. Answer with choices, type one answer, skip the name:
   the recap reads "Make ... First, ...". Choose a new folder with the name used in step 4 and
   start building: it is refused because the folder exists, offering Use that folder; it returns
   through the review and starts there.
8. **Open now from creating.** With a recap showing, start another approval
   (`pnpm demo | sed '/approval requested/q'`): the line under the title names the work that needs
   you, with Open now and Keep creating, and nothing switches by itself. Open now: that work opens
   on What do you need from me?; collapse it: the recap returns exactly as it was.
9. **The four questions.** In the opened workspace, What is it doing?, Help me understand and What
   was checked? read whole on their tabs, and What do you need from me? shows only while the approval
   waits. Approve, read the whole request, confirm: the answer counts once the runtime confirms it.
10. **Usage left.** At the right end of the rail's lower row, Usage left: pressed, it says "Usage
    left isn't set up on your Mac." It steps aside when the entry panel or a workspace opens.
11. **Reset position.** Move steps the panel right, left and back. Turn in the chair and press
    Reset position: the panel and the rail come in front of you.

Throughout, note whether the rail sits over a character, its label or a system window, whether
anything needs leaning in to read, and whether any button pressed did nothing. Afterwards, the cases
the journey does not reach: Add work to a project in another folder (the recap says all later work
runs there, and the review shows the folder now and from now on), and a control plane without
roots (the choice says your Mac doesn't allow any folder yet).

### Hold to talk on a Quest

In a development build, with voice set up on the Mac
([LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md#turn-on-voice)) and the headset connected to that
control plane. The control plane's log names the engine and its warm-up at startup. Seated, in
the virtual space:

1. **The permission.** Create a project, then hold Hold to talk, under Type my idea: the first hold
   asks for the microphone and records nothing, and beside the button it says to allow it and hold
   again. Allow it.
2. **An idea.** Hold, say "A website for my bakery that shows the menu and the opening hours", let
   go: beside the button, "Listening", then "Your Mac is turning that into text.", then the recap,
   which says "Heard on your Mac. Check it before you go on." with the sentence as the first task
   and a name from its first words. Nothing has been sent; note how long from letting go to the
   recap.
3. **A tap and silence.** A tap says it was too short to hear. Hold a few seconds without speaking:
   "Nothing was heard". Press the Meta button while holding: it says it stopped listening, and
   nothing was sent. Hold, then move the ray off the button while still pinching, and poke and
   pull the finger away: note whether each stops listening, which the editor cannot check.
4. **An instruction.** Open running work: Hold to talk is at the end of the action row. Say an
   instruction: the workspace asks "Heard on your Mac. Send this instruction?" with the words, and
   only Send sends it. Let the 15 seconds lapse once: nothing is sent.
5. **Without voice.** Stop the control plane, start it without the three `HALCYONIC_WHISPER_`
   variables, and hold again: "Voice isn't set up on your Mac. Type instead."

Note anything misheard as the person said it, word for word, for the next measurement with real
voices; never record the clip itself.

### The demonstration judges see

A headset with neither a pairing nor an access token plays the recorded demonstration and follows
your answers ([XR_CLIENT.md](../architecture/XR_CLIENT.md)); a release build never pairs. To see it
on a development build that has a token, move the token aside and start the app again, and on one
that is paired, stop the Mac's control plane first; move the token back afterwards:

```bash
adb shell mv /sdcard/Android/data/com.halcyonic.xr/files/access-token /sdcard/Android/data/com.halcyonic.xr/files/access-token.off
adb shell am force-stop com.halcyonic.xr
adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
# afterwards
adb shell mv /sdcard/Android/data/com.halcyonic.xr/files/access-token.off /sdcard/Android/data/com.halcyonic.xr/files/access-token
```

Then, with hands only:

- **The line.** Above the stage: "Demonstration: recorded, simulated work played on this device, not
  live." and "It follows your answers, and nothing reaches an agent."
- **Beside the story.** Of three characters, "Paginate the order history endpoint" and "Send an
  order confirmation email" work for about five seconds and finish. Opened, each shows "On Simulated
  agent (demonstration, watch only)" and no buttons.
- **The story.** "Add rate limiting to the sign-in endpoint" then works, and about nine seconds in
  it needs you, with "Approval needed to use shell: Run make migrate …". It waits as long as you
  like. Opened: "On Simulated agent (demonstration), simulated work", and Approve, Deny and Stop the
  turn.
- **Approve.** After the confirmation, Requests reads "Not sent to any agent; the recording
  continues as recorded for approving.", and the recording continues: "demonstration recorder asked
  to approve", "Approved", the migration, then "Tests failed: 1 failed, 23 passed" and "Turn
  finished".
- **Instruct.** Instruct offers two buttons instead of the keyboard, "Count per account too" and
  "Change the test instead". Either one: "Not sent to any agent; the recording continues as recorded
  for “…”.", a new turn, and "24 passed".
- **Deny** instead: the agent says it did not run the migration; Instruct then offers "Keep them in
  memory", which also ends with the tests passing. **Stop the turn**, at any moment while it works
  or waits: "Stopped".
- **Understanding.** At the approval, pinch Understanding: the first line reads "Simulated, not from
  Salidium · recorded at" and a time, then "Waiting for you. Run make migrate …" tagged observed,
  "Agent says: “…”" tagged reported, the two files changed, and "2 files not checked after the last
  change" tagged inferred. Approve: the details return to Activity. Once the turn has ended, pinch
  Understanding again: "1 test failing", "Tests failed: 1 failed, 23 passed …", and an explanation
  tagged explained. Left open while the work goes on, it follows by itself within about two seconds
  of each change. The change summary shows a minus sign, "(+71 −0)", not an empty box: that glyph
  comes from the dynamic fallback font.
- **Evaluation.** Pinch Evaluation: "Simulated, not from Seorak · recorded at …", the cost as "About
  $0.20. Estimated from token counts at list prices. Not a bill.", and under each of the cost, the
  outcome and the checks its own availability, coverage and freshness; before the first turn ends,
  the outcome reads "Nothing measured yet." and "unavailable: not yet computed …". It changes only
  when you pinch Refresh. Opened for the two finished characters beside the story, both sections show
  their own simulated answers.
- **The end.** Once the story has ended, the line adds "This recording has ended and starts again
  shortly.", and the workspace says nothing can be sent. About 20 seconds later the characters go
  back to "Not started" and it plays again, with no "Disconnected" and no "last known" on the
  way; an open workspace shows none of the earlier activity or answers. Unanswered instructions
  also give way to a new start after a minute.
- **Sleep.** Take the headset off until it sleeps and put it back on: the demonstration plays from
  its beginning.
- **Log.** `adb logcat -s Unity` shows `Halcyonic: demonstration plays from its beginning (n)` at
  each start and `demonstration reached an end` at each end, and never what was answered.

### Room placement checks on a Quest

The room placement ([XR_CLIENT.md](../architecture/XR_CLIENT.md), under "The room") in rooms that
are set up and rooms that are not. Sit at a desk, hands only. Follow the placement in the log:

```bash
adb logcat -s Unity | grep --line-buffered "Halcyonic: room"
```

To see the first launch again, clear the app's data, which also deletes the pushed access token,
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
  way on the desk and in the virtual space, with its line above the stage.
- **Focus.** With the system menu open, the room controls do not respond to a poke or a pinch.
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

- **Ready.** A few seconds after launch: `Halcyonic: sound ready: 79 clips rendered at 48000 Hz in
  ... ms on a worker thread, 17.7 MiB of samples, ...`. Record the milliseconds and the rate: the
  Quest 3's render time is not measured yet. `adb logcat -s VrApi` stays at 72 fps while it renders.
- **Silence while work goes well.** With the demonstration, nothing sounds while characters work,
  run tests or wait, except a soft double tap when work starts and four muted taps when a test run
  starts. Nothing loops, and a character that keeps working stays silent.
- **Needs you.** Two strikes rising, the second ringing on, from the character that rises and turns
  to you. It is the loudest cue, yet not alarming.
- **Finished.** The pair falling onto the character's own note, with nothing celebratory about it,
  also when its tests failed.
- **Your actions.** Open a character (pinch, or look and pinch): a chord unfolding in front of you.
  Approve, and confirm: two notes struck together in front of you, then later, from the character,
  the soft double tap as it works again. Collapse: the chord folding back. Instruct and Stop the
  turn sound too; Stop's result is the character's own caught strike once the recording or the
  runtime confirms it. An instruction typed on the system keyboard sounds its three light taps as
  the keyboard closes and the app has focus again.
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

1. **Place.** Characters on the desk, or in front; then open a browser video window (Meta's
   browser, any video) and place it in front of you. Note how many characters it covers. Press Make
   room for a window in the room controls (low right) and note the count again; Characters in front
   turns them back. Record both counts and which side the window was on.
2. **Watch.** Select the video. The log says focus went to another window; three seconds later,
   large panels are folded. Open a workspace or the entry panel first to see it fold; nothing else
   on the stage moves. Characters keep animating.
3. **Needs you while watching.** Make a workstream need you. The character rises and turns, and the
   line above the stage says "1 needs you" in amber. Can you see either past the window?
4. **Return by hand.** Pinch on a character, the rail or empty space. The first pinch only returns
   focus: nothing opens or presses, and panels come back as they were. Then open the character and
   read the request.
5. **Half done.** Arm Yes, approve, then select the video before confirming, and come back. The
   workspace says "You went to another window, so nothing was sent. Press it again to confirm.";
   the request still waits. Confirm it afresh; the character shows the runtime's result.
6. **Return by the Meta menu.** Press the Meta button, then Resume. Same as step 4.
7. **Back to the video.** Select it again; the character you approved stays nearby, panels fold.
8. **Flapping.** Move a hand quickly between the video and the stage several times. Nothing on the
   stage rearranges; the log shows losses but no folding unless focus stays away three seconds.
9. **Keyboard.** Start a new project and type its idea. The keyboard takes focus; the entry panel
   stays put and the draft is kept. Close the keyboard with Done and the text arrives.
10. **Hold to talk**, if the build has it: hold, then select the video mid-sentence. Recording
    stops; nothing is transcribed and the field keeps its text.
11. **Sleep and resume.** Take the headset off until it sleeps, put it back on. Does the stage say
    Last known, then Live, and is no action offered while it says Last known?
12. **Sound options.** First silent (the default): does a Needs you while watching go unnoticed?
    Then turn on the option and repeat step 3:

    ```bash
    adb shell touch /sdcard/Android/data/com.halcyonic.xr/files/needs-you-sound-while-away
    ```

    One quieter Needs you, never repeated. Was it helpful or an interruption? Remove the file to
    turn it off again (`adb shell rm` the same path).

Record what each step showed in [quest-3-device.md](../validation/quest-3-device.md), including what
did not happen as written. A step not tried stays unverified.

Results on a Quest 3, including the milestone 2 checks: [quest-3-device.md](../validation/quest-3-device.md).
