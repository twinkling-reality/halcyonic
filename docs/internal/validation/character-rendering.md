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
- A body about 0.34 m wide at 2.4 m spans about 8 degrees. At about 20 pixels per degree in the eye
  buffer that is about 20,000 pixels per eye, and about 240,000 pixels a frame for six characters
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

### Optional shelf render evidence, 2026-10-12

Unity 6000.3.25f1 ran `WorkspaceRender.ShelfWorkspaceCheck` against revision `302d9ca0`
with the Android target. Both Standard and Larger text passed the production one-agent shelf
witness: all nine activity states and four source treatments, actual label bounds inside the
placement envelope, the existing 14 dp type minimum, field and console clearance, and badge
top attachment within 0.1 mm of the physical shelf front lip. The coordinator inspected the
Waiting for you/live and Working/demo renders. The body remains grounded; growing the label
does not move the confirmed shelf or independent console. Task titles can still ellipsize after
two lines; full identity is available in the task view. These scene checks do not establish
headset comfort, hand interaction, performance or wearer acceptance.

### Device checks

1. The characters render in both eyes, none magenta, with their labels readable at 2.4 m, and the
   outermost labels end within about 36 degrees of straight ahead.
2. With six characters animating, `adb logcat -s VrApi` reports 72 fps with no stale frames, and
   the app's GPU time stays well under a frame.
3. After a recenter and after a boundary change, the stage stands in front of the person again, and
   `adb logcat -s Unity` shows `placed the stage in front of the person because ...`.

### Character features on the optional shelf, 2026-10-13

**Supported design.** [ADR 0029](../decisions/0029-workspace-is-complete-without-characters-and-continues-in-a-native-panel.md)
supersedes mandatory character entry and automatic stage placement. The feature/blink work is
adapted to the current zero/one-agent shelf, with existing grounded shell contact, independent
label attachment to the shelf lip, source marks, palette refresh and independent console
placement preserved. The original held branch remains unchanged. The six ADR 0013 feature
kinds are None, Antenna, Ears, Sprout, Crest and Horns, selected deterministically from workstream
identity. This provides identity variety; it does not provide user customization. No ring/rim,
travel, automatic placement or inferred reference feature is added.

The hand/gaze target radius remains 0.1. Feature vertices stay within 1.32 shell units;
0.07 body scale times a conservative 1.08 stretch reaches 0.099792, inside that target.
The largest feature has 300 vertices/588 triangles, one material and one pass. The supported
one-agent shelf adds at most one feature pass. These are source bounds, not device performance
or recognizability measurements. Blink intervals are declared by identity in the existing
3–7-second range; the blink duration uses ADR 0027's 0.14-second token. Last-known and
Interrupted presentations snap to their paused appearance and freeze pose, material values
and clock; confirmed live state resumes the existing motion path. Keep things still retains
the existing easing and clock semantics.

**Rendering decision.** The shell keeps depth writing through the body shader's default
`_DepthWrite = 1`. A single generated cached feature material uses the same shader with depth
writing disabled and draws before its shell (`sortingOrder = shell.sortingOrder - 1`). No
per-character material is generated. Subsystem reset disposes only this owned clone, preserving
the Resources-owned body material. Shader math, depth testing, blending, render queues,
animation and project settings are unchanged. Unity's
[ZWrite reference](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-ZWrite.html) documents
depth-buffer writes and warns that disabling them can require CPU geometry sorting. The
installed pinned Meta Interaction RoundedPanelUI shader uses the same numeric material-property
syntax. Actual Unity compilation and rendering verify the candidate syntax on this stack.

**Causal evidence.** Unity 6000.3.25f1 ran the candidate on Metal with the Android target at
Standard and Larger text. Original candidate `bc75c72d` reproduced tiny Ear stillness failures
and the historical Standard stage failure. Captured motion tails alone were not causal:
`20dc32ee` changed pixels in repeated unchanged and restored frames, and residual correction
still drifted. No finite settling change or tolerance relaxation was made.

Controlled captures on `b84e2be0`, `1294b209` and `9aeb4531` kept shell-only, feature-only with
the original shader, feature/non-character and Unlit captures identical, while shell/feature
pairs and full scenes drifted at fixed poses. Camera/renderer matrices, MPBs and globals stayed
fixed. Requested target depths 24/32 both resolved to D32_SFloat_S8_UInt with one sample, so
this was not a depth-format comparison. Explicit earlier feature order removed the first
failures but `e3d13d8c` still failed ghost Ears/Crest and Standard Clover/Horns. Configured Android
static/dynamic batching were both 1; actual draw participation remains unknown. No batching or
GPU-internal cause is claimed.

The private pair-state run on `a5d32bb3` tested the same five failed histories at both text
sizes. All ten feature-ZWrite-Off series had zero exact RGBA differences. Material-copy,
name-only shader-copy, distinct queue, depth-bias and no-batching controls each failed in at
least one case; negative baselines were intermittent. This isolates a useful depth-write
repair, without claiming a particular GPU implementation fault. Same-camera junction
inspection found localized changes and no gross feature loss. Detailed captures and logs
remain private evidence; stability alone does not establish correct appearance or occlusion.

**Consumed regressions and current result.**

| Entry point | Coverage | Actual result |
| --- | --- | --- |
| `CharacterFeatureCompositionCheck` | All six Round features × all nine activities × live/last-known; eight unchanged renders compared byte-for-byte in Color32 RGBA, shader/depth/order ownership, feature/shell visibility and representative actual-camera junction images | `f67307ab` and augmented `53241906` passed Standard/Larger |
| Same composition entry point | Destroy/recreate views around the actual cache reset; one regenerated shared feature surface, original Resources body identity/depth; live/last-known opaque foreground/background; real Checking ring relationship | `53241906` passed Standard/Larger |
| `CharacterLifeCheck` | All 48 shape/feature identities: idle temporal stillness, all-state moving/held contact, target/envelope/field/console/label geometry; feature-visible differential; actual-clock blink and paused/live resumption | `f67307ab` passed Standard/Larger |
| `AgentShelfCheck` | One confirmed supported agent, every activity's temporal held pixels, contact, source/readability and geometry | `f67307ab` passed Standard/Larger |
| Historical `StageRender.Check` | Original independent stage, geometry, palette and held-motion negative regressions; this does not make the old six-agent arc a supported mode | `53241906` passed Standard/Larger |
| `WorkspaceRender.Check` | Migrated raw panel plus current dedicated workspace, requests, shelf, MenuPlane/Director, answers, Judge and entry/project journeys | `a33f14c5` passed Standard/Larger |
| Full `AppearanceCheck` / Workspace PalettePass | Current zero/one-agent journeys and migrated raw component galleries in both palettes/sizes | `a33f14c5` passed both presets and text sizes; the preserved `53241906` failure was the legacy six-arc Clear assertion |

The unchanged temporal stillness comparisons in CharacterLife, AgentShelf and historical Stage
require zero changed pixels under the existing 2/255 RGB comparison. Their motion negatives
remain in place. They are distinct from the stricter composition repeat check, which compares
exact RGBA bytes. All 48 identities get temporal idle and all-state geometry coverage; this
is not a claim of all 48 × all states exact RGBA pixels. The filtered CharacterIdentity and
GlazeToken tests passed 24/24 on .NET 10.0.401 with zero skipped. Actual root-isolated shader-ignores-EyeOpen and fixed-4-second-period mutations each
exited 1 on the original `53241906` fixture: the shader produced four specific eye-pixel errors
and the period produced four declared-interval errors (two identities × both text sizes), with
additional blink/duration errors for the period. Both original files were restored exactly. No existing threshold, text margin, envelope or stale-state assertion is weakened.

**Occlusion limits.** The augmented composition check exercises the cache-reset editor branch;
it does not claim a no-domain-reload player lifecycle test. Opaque foreground cubes hide the
isolated features exactly, and opaque backgrounds leave them visible. Actual shell/feature/
ring-only frames plus triangle-ray depths check the existing Checking ring, respecting body
Cull Back and ring Cull Off. Front/back, missed-ray and behind-ray negatives exercise the
classifier. Rear-shell samples match ring-hidden pixels and each case retains visible foreground ring
samples; all current cases have positive counts. Rear-feature overlaps would be compared against
an actual later-feature painter-order reference, but every current case had zero such samples.
This proves no generic transparent occlusion support. A future transparent object behind a
protruding non-depth-writing feature can paint over it unless deliberately ordered; that
composition needs its own geometry and appearance validation. Temporary materials, visibility
and images are restored and destroyed.

**Raw component fixture migration.** Full Appearance exposed eight repeated Clear failures in
the legacy six-agent far gallery across sizes/palettes. Actual pixel body/label and opacity
assertions passed; the conservative legacy label/body bounds needed more clearance than the
unchanged comfort floor allowed. Those bounds are preserved. The raw panel's positive component
journeys now start from the actual character-free Projects MenuPlane direction, then confirm
one optional grounded shelf whose complete envelope clears both the production console and
the measured raw frame by the unchanged 1.5 degrees. Only this caller considers the additional
1.05/1.2 m drops; all existing shelf consumers retain their original candidates. Actual agent/
label points still pass the complete measured field margin, and all raw opacity/paging/target/
hostile-text assertions remain. The original six-arc default remains as a no-fit negative:
its direction must be false and separately measured actual frame/label and frame/body gaps at
the allowed lower/upper alternatives must both fall short of their published clearances. This
migration passed `WorkspaceRender.Check` on `a33f14c5` at both text sizes. Actual lower label gaps
were 0.1/-0.4 degrees for Standard far/3S and 1.1/0.6 for Larger, each below the required 1.2;
upper body gaps were -17/-18.8 degrees, below the required 1.5. This independently confirms
the legacy clearance shortfall, while the positive one-agent/component assertions all pass.
It is not permission to restore six-character placement authority. The raw gallery named
"desk" now uses the same independent gaze-centered positive scene and no longer proves the old
six-arc desk geometry. Separate current MenuPlane/Director desk journeys retain their current
composition/routing/drag coverage; none of these editor scenes establishes device desk comfort.

**Remaining validation.** Independent source review and the final candidate gates passed before
replay into integration `00572379`. That replay combines other changes; full integrated suite,
build and device validation is separate from these candidate renders. Stereo appearance, headset/device behavior,
feature recognizability, hand interaction, no-domain-reload player behavior, cost and wearer
acceptance remain unverified. Device follow-up uses the current optional one-agent shelf;
the six-character cost/recenter record above is historical evidence, not current supported
placement authority.
