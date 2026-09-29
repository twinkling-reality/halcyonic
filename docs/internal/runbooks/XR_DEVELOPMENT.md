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
and Interaction SDKs 207.0.0 (from Meta's registry at `npm.developer.oculus.com`, under Meta's SDK
license), XR Hands 1.9.0, Newtonsoft.Json 3.2.2, the Test Framework, the generated contracts from
`packages/contracts/csharp`, and the embedded client core. The manifest also lists the AI,
AssetBundle and Physics 2D engine modules, which Meta's SDKs use without declaring; without them
Meta's code does not compile.

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
- the Hand Tracking building block;
- the Halcyonic stage object, with the connection, the characters and the focus guard.

Both hands are assigned to `FocusGuard`, so they hide when the app loses input focus.
`HalcyonicBootstrap` still adds a stage to any other scene that lacks one, but without hand visuals
to hide.

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
  `ClientWebSocket` and would leave the permission out.

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
- `QuestBuild` filters the assemblies of the Immersive Debugger, its dev agent and the agent bridge
  out of every non-development build; nothing else in the build references them.
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
`INTERNET`, `com.oculus.permission.HAND_TRACKING` and AndroidX's own receiver permission, the
`com.oculus.intent.category.VR` launcher category and `com.oculus.vr.focusaware`, and nothing from
`com.meta.agenticxr`.

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

The first launch creates the app's data directory and reports that it has no access token. Copy
the token there and start the app again:

```bash
adb push ~/.halcyonic/access-token /sdcard/Android/data/com.halcyonic.xr/files/access-token
adb shell am force-stop com.halcyonic.xr
adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
```

The token survives reinstalls. The control plane logs `realtime client connected` for
`halcyonic-xr`.

- **Run `adb reverse` again after any Unity run for Android.** Every Unity run with the Android
  target kills the adb server as it exits, an import as well as a build, which drops the rule; the
  app cannot reach the control plane until it is back, and then reconnects by itself.
- **Stage out of view:** the stage is placed from the world origin. After a boundary change,
  recenter: look at a palm, then pinch and hold the Meta icon.
- **Logs:** `adb logcat -s Unity` is the app's log, and `adb logcat -s VrApi` reports the frame
  rate every second.

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

Results on a Quest 3, including the milestone 2 checks: [quest-3-device.md](../validation/quest-3-device.md).
