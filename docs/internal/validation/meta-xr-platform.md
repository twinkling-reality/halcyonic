# Meta XR platform

- **Question:** Which Unity, OpenXR and Meta XR versions and platform behaviors constrain the XR
  client, and what can only be settled on hardware?
- **Date:** 2026-09-26.
- **Method:** Official Meta Horizon developer documentation and release notes, the Meta and Unity
  package registries, and Unity release pages. Nothing was installed.
- **Status:** Documentation verified, the project's first import in the Unity editor, and a
  Simulator run that rendered nothing (below). The first run on a device is in
  [quest-3-device.md](quest-3-device.md).

## Findings

- **Unity.** Unity 6.3 LTS is current: `6000.3.25f1` (2026-09-24), supported until December 2027.
  Unity 6.0 LTS support ends in October 2026, during the competition. Meta XR SDK v207 requires
  Unity `6000.0.66f2` or later. Meta has said it is "standardizing on Unity 6.3+". Unity states that
  Meta VR Glasses support requires Unity 6.6 or later.
- **OpenXR.** The Oculus XR Plugin is deprecated ("Use the Unity OpenXR Plugin instead").
  `com.unity.xr.openxr` 1.18.0 is the current release (1.19.0-pre.1 is a prerelease). The "1.17 or
  later" requirement belongs to Meta XR Operator, not to all v207 tooling.
- **Meta XR SDK.** Core, Interaction SDK and MR Utility Kit 207.0.0 (2026-09-22 to 2026-09-24) are
  current. v207 Interaction SDK adds gaze interactions and changes ray-grab defaults.
- **Meta XR Simulator v207** is now a standalone application with device profiles including Meta
  Quest 3 and **Meta VR Glasses** (the official name). **Look and Pinch is an input mode**, not an
  interaction profile, available with the Glasses profile: gaze targets and a pinch selects. The
  Simulator is an OpenXR runtime; it is not a device or Horizon OS emulator.
- **Meta XR Operator** is an experimental OpenXR API layer in the Core SDK that lets MCP-capable
  agents inspect and drive a running app (hands, gaze and pinch, scene graph, captures).
  Meta: "Avoid depending on it in production apps." Useful for automated development testing
  only.
- **Focus (VRC.Quest.Input.4, required).** "Apps must be focus-aware. They must continue rendering
  when they lose focus, hide any user hands or controllers, and ignore all hand or controller
  input." Audio input may continue.
- **Eye tracking.** Meta VR Glasses and Quest Pro have it; "other Meta VR devices don't support eye
  tracking", which includes Quest 3. The Interaction SDK falls back to rays when eye data is not
  valid. Meta VR Glasses hardware is expected in spring 2027.
- **WebSocket on Quest.** No official statement was found on
  `System.Net.WebSockets.ClientWebSocket` under Android IL2CPP. **Unverified** here; verified on a
  Quest 3 on 2026-09-29 ([quest-3-device.md](quest-3-device.md)).

## Consequences for the XR client

- Pin Unity `6000.3.25f1`, `com.unity.xr.openxr` 1.18.0 and every `com.meta.xr.*` package at
  207.0.0. Do not upgrade during the competition without reading release notes and re-validating
  in the Simulator and on a device.
- Build one focus service that stops rendering hands and ignores their input on focus loss, and
  keeps rendering the scene.
- Design input around ray and pinch with gaze as an enhancement; never require gaze on Quest 3.
- Put the WebSocket behind `IRealtimeTransport`, set Internet Access to Require, test `wss://` on
  a Quest in the first week, and keep a native Android WebSocket as a fallback.
- Keep Meta XR Operator out of release builds. Meta's build step does, for every non-development
  build ([horizon-store-release.md](horizon-store-release.md)).

## First import (2026-09-27)

Unity `6000.3.25f1` on macOS (Apple silicon) opened `apps/xr` for the first time, with the
packages pinned above.

- **Undeclared engine modules.** Meta's Core and Interaction SDKs use `AssetBundle`,
  `UnityEngine.AI` (`NavMeshQueryFilter`) and `Physics2D` without declaring the built-in modules
  that provide them. Without `com.unity.modules.assetbundle`, `ai` and `physics2d` in the manifest,
  `Oculus.VR` and `Oculus.Interaction` fail with CS1069 and the editor offers Safe Mode.
- **Resolved versions.** The Test Framework is built into this editor at 1.6.0. Resolution pulls in
  Input System 1.20.0, XR Core Utilities 2.6.0 and XR Plug-in Management 4.6.1. The Package Manager
  warns that Meta's packages carry no signature.
- **Restart and crash.** After the first import the editor set Active Input Handling to Both and
  relaunched itself. On this editor version that relaunch crashed during shutdown (an Objective-C
  exception while unloading scripted objects), so the project had to be reopened by hand. The
  settings written before the crash were intact.
- **Meta's first-run prompts.** Meta asks to enable its OpenXR feature set on Standalone and
  Android, and asks whether to share additional usage data with Meta. The data choice applies to
  every Meta developer tool on the machine.
- **Project Setup Tool.** On the fresh project its required tasks were the Oculus Touch interaction
  profile (Standalone) and a single GameActivity entry point (Android). Hand tracking is a
  recommended task, off by default.
- **Android build checks.** The Android build refuses Active Input Handling set to "Both" (Unity:
  "not supported on Android"), so the project uses the Input System package only. A failed build can
  leave the XR settings in the preloaded assets, the Performance Testing package's run files in
  `Assets/Resources` and `Assets/StreamingAssets/RuntimeActionBindings.json` behind; remove them
  rather than commit them.
- **Per-machine file.** Meta's Immersive Debugger writes `Assets/Resources/DevAgentSettings.asset`
  with the machine's LAN address and a generated access token.
- **Halcyonic's code.** The contracts, the client core and the Unity layer compiled with no warnings.

## Simulator (2026-09-27)

The stage scene ran in Play mode against Meta XR Simulator 207.0, the active OpenXR runtime.
Machine: an Apple M5 Max on macOS 26.7, with Metal. Nothing has rendered in the Simulator yet, so
the characters have not been seen there.

- **Client connection.** OpenXR loaded the Simulator, and the Halcyonic client connected to the
  control plane.
- **Pause bug.** Unity reported a resume without a pause when the XR session started. That exposed
  a pause bug in the client, fixed in 6df47ac.
- **Frames only while the editor is active.** Hidden windows: after one frame the app submitted no
  more, so the session never left READY, and the client's heartbeat to the control plane stalled.
  Visible but inactive windows: the editor still submitted no frames. Frames flowed only once the
  editor was the frontmost application; Run In Background made no difference.
- **Every frame is refused.** With frames flowing, each session failed at its first `xrEndFrame`
  with `XR_ERROR_RUNTIME_FAILURE`:
  - The Simulator marked every `xrBeginFrame` as discarded.
  - Before each session it warned "Cannot find MoltenVK entry points".
  - Unity then restarted the session, which failed the same way.
- **What did not help.** None of these changed the failure:
  - activating the Simulator through Unity's Window > Meta > Meta XR Simulator menu, which sets the
    runtime for the editor process;
  - a freshly launched Simulator;
  - multi-pass rendering;
  - turning off Meta's foveation feature on Standalone;
  - a scene holding only Meta's Camera Rig (Halcyonic's bootstrap still added its stage, which draws
    only text and spheres).
- **Libraries.** The editor process loaded the Simulator runtime (`SIMULATOR.so`), but no Vulkan or
  MoltenVK library appeared among its open files, although the Simulator ships both.
- **Inference.** The Simulator's Metal path fails on this machine. Unresolved: whether the cause is
  the Simulator version, the GPU and OS, or a missing setup step. The first session also logged a
  null passthrough just before failing, although passthrough is off in the scene and the project.
- **Warnings.** The Simulator rejects the Oculus Touch proximity binding
  (`XR_ERROR_PATH_UNSUPPORTED`); that did not stop a session.

## Only settled on hardware

Sustained frame rate with hand tracking and network traffic; focus changes under the real
Universal Menu; WebSocket survival across sleep, headset removal and focus loss; pinch reliability
when seated or reclined; text legibility; battery and thermals; LAN discovery; long-session
comfort.
