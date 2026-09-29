# Character rendering and placement

- **Question:** How do the characters of [ADR 0013](../decisions/0013-characters-are-bots-with-a-living-surface.md)
  render and place themselves in the Unity client, which Unity and OpenXR behavior do they rely on,
  and what do they cost on a Meta Quest 3?
- **Date:** 2026-09-29.
- **Environment:** Unity 6000.3.25f1 on an Apple M5 Max with macOS 26.7, the editor in batch mode
  on Metal; the pinned OpenXR 1.18.0 and Meta XR Core SDK 207.0.0 packages; the project's committed
  settings. No headset.
- **Method:** the packages' documentation and source in the project's package cache; TextMesh
  measured in batch mode; images of every state and of six characters in the arc, rendered in batch
  mode by a temporary editor script that is not kept; a development APK build; shader cost counted
  by operation.
- **Status:** Verified in the editor: the shaders compile, every state renders with its cues, and
  labels wrap to their plates. The development APK builds. Not verified on the headset: the frame
  rate, rendering in both eyes under multiview, placement after a recenter or a boundary change,
  and legibility at 2.4 m.

## Findings

### Placement

- OpenXR 1.18.0's documentation (`Documentation~/input.md`, "Reference Space Changes and Tracking
  Origin Updated") says Unity forwards OpenXR's reference space change notifications to
  `XRInputSubsystem.trackingOriginUpdated`, that a recenter is the most common trigger, and that
  one recenter can raise several events over several frames, so a handler should tolerate repeats
  or wait for a short stabilization period. The stage waits until requests have stopped for
  0.3 s and the head is tracked.
- The same page maps the floor tracking origin to a local floor space when recentering is allowed,
  and to the stage space otherwise. On the first headset run the stage came back into view after a
  recenter, so the rig's floor origin follows recenters.
- Meta's SDK also reports recenters: `OVRManager.display.RecenteredPose`, raised when OVRPlugin's
  recenter count changes (`OVRDisplay.cs`), and `OVRManager.TrackingOriginChangePending`, raised for
  reference space changes (`OVRManager.cs`). The stage does not use them, so the Unity layer stays
  on core Unity APIs; they are the fallback if Unity's event proves unreliable on the headset.

### Text

- TextMesh draws one font pixel as 0.1 units at a character size of 1: ten "M" at font size 48 and
  character size 0.1 measured 4.0 units, and the built-in font advances "M" by 40 pixels.
- Lines advance by 1.15 font sizes, measured at 48 and 96. The built-in font reports a line height
  of 14 at its size of 13, which underestimates that by 7%.

### Rendering

- The Android build target's default quality level (Medium) has no MSAA, so silhouettes, eyes, the
  halftone, the ring and the label plates are anti-aliased in the shaders.
- OpenXR renders single-pass instanced, which is multiview on Android. The shaders use Unity's
  stereo instancing macros and compute the per-eye view direction in the vertex stage.
- Render order: halos (queue 2980), bodies (2990, transparent but writing depth, so the ring's far
  side is hidden), label plates (2995), then rings and text (3000).

### Cost, estimated

- The body shader is one pass without keywords: about 160 arithmetic operations per pixel at rest
  and 185 while working, with at most two texture reads in any state, from a 128 by 128 noise
  texture (64 KB) baked once at startup. The lookbook's satin flow evaluated 3D value noise 16 times
  per pixel, about 120 operations each: some 2,000 operations, ten times the whole body shader.
- A body about 0.42 m wide at 2.4 m spans about 10 degrees. At about 20 pixels per degree in the eye
  buffer that is about 31,000 pixels per eye, and about 380,000 pixels a frame for six characters
  in both eyes.
- A halo is 4.4 body radii across, so six halos cover about six times the bodies' area, at a dozen
  operations a pixel with blending; only needs you, failed, finished, running tests and unknown
  have one.
- A body mesh has 2,594 vertices. Per character and frame the CPU writes a few transforms and three
  property blocks, and allocates nothing; text is wrapped again only when it changes.

## Consequences

- Characters ship their shaders through materials in `Assets/Halcyonic/Characters/Resources`
  ([XR_CLIENT.md](../architecture/XR_CLIENT.md)).
- If the stage does not follow a recenter on the headset, subscribe to Meta's recenter events too.

## To check on the headset

1. The characters render in both eyes, none magenta, with their labels readable at 2.4 m.
2. With six characters animating, `adb logcat -s VrApi` reports 72 fps with no stale frames, and
   the app's GPU time stays well under a frame.
3. After a recenter and after a boundary change, the stage stands in front of the person again, and
   `adb logcat -s Unity` shows `placed the stage in front of the person because ...`.
