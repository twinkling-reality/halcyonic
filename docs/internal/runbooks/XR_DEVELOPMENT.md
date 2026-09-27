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
`packages/contracts/csharp`, and the embedded client core. It then writes the project settings,
`Packages/packages-lock.json` and a `.meta` file for every asset.

1. On the editor's first launch, Unity shows its Editor Software Terms, possibly on another desktop
   Space. Nothing loads, and the editor log stops after licensing, until you accept them.
2. Check the Console for compile errors. The Unity layer (`apps/xr/Assets/Halcyonic`) has not been
   compiled by Unity yet; fix anything it reports.
3. In **Project Settings > XR Plug-in Management**, enable **OpenXR** for Android and for the
   desktop platform (the Simulator runs there), and enable the **Meta Quest** feature group.
4. Run Meta's **Project Setup Tool** and apply the required fixes.
5. Commit the generated `ProjectSettings/`, `Packages/packages-lock.json` and `.meta` files. The
   `.meta` files fix asset identities, so they must be committed once and kept.

## Scene

Create a scene with Meta's Building Blocks for a camera rig and hand tracking. No Halcyonic object
is needed in the scene: `HalcyonicBootstrap` adds the stage (connection, characters and focus
guard) to any scene that does not have one. Assign the rig's hand visuals to the `FocusGuard`
component, so they hide when the app loses input focus.

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

## On a Quest (later)

The control plane serves only loopback. Over USB, with developer mode enabled on the headset:

```bash
adb reverse tcp:47800 tcp:47800
```

Then copy the token into the app's persistent data directory, replacing `<application id>` with
the id set in Player Settings:

```bash
adb push ~/.halcyonic/access-token /sdcard/Android/data/<application id>/files/access-token
```

Whether `ClientWebSocket` works under IL2CPP on Quest is unverified; test it first.
