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
- IL2CPP on ARM64, minimum API level 32;
- Internet Access set to Require, because Unity's automatic detection does not see
  `ClientWebSocket` and would leave the permission out.

Switch the editor's platform to Android before building. `adb` comes with Unity's Android module,
in `PlaybackEngines/AndroidPlayer/SDK/platform-tools`.

The control plane serves only loopback. Over USB, with developer mode enabled on the headset:

```bash
adb reverse tcp:47800 tcp:47800
```

Then copy the token into the app's persistent data directory:

```bash
adb push ~/.halcyonic/access-token /sdcard/Android/data/com.halcyonic.xr/files/access-token
```

Whether `ClientWebSocket` works under IL2CPP on Quest is unverified; test it first.
