# Meta XR platform

- **Question:** Which Unity, OpenXR and Meta XR versions and platform behaviors constrain the XR
  client, and what can only be settled on hardware?
- **Date:** 2026-09-26.
- **Method:** Official Meta Horizon developer documentation and release notes, the Meta and Unity
  package registries, and Unity release pages. Nothing was installed.
- **Status:** Documentation verified. No Unity project exists yet; nothing has run on a device.

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
  `System.Net.WebSockets.ClientWebSocket` under Android IL2CPP. **Unverified.**

## Consequences for the XR client

- Pin Unity `6000.3.25f1`, `com.unity.xr.openxr` 1.18.0 and every `com.meta.xr.*` package at
  207.0.0. Do not upgrade during the competition without reading release notes and re-validating
  in the Simulator and on a device.
- Build one focus service that stops rendering hands and ignores their input on focus loss, and
  keeps rendering the scene.
- Design input around ray and pinch with gaze as an enhancement; never require gaze on Quest 3.
- Put the WebSocket behind `IRealtimeTransport`, set Internet Access to Require, test `wss://` on
  a Quest in the first week, and keep a native Android WebSocket as a fallback.
- Keep Meta XR Operator out of release builds.

## Only settled on hardware

Sustained frame rate with hand tracking and network traffic; focus changes under the real
Universal Menu; WebSocket survival across sleep, headset removal and focus loss; pinch reliability
when seated or reclined; text legibility; battery and thermals; LAN discovery; long-session
comfort.
