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

### Held character candidate adapted to the optional shelf, 2026-10-13

**Scope and method.** The preserved feature/blink work is selectively adapted to the current
one-agent shelf; the original held branch is unchanged. Existing grounding, independent label
lip attachment, full source marks, palette refresh, optionality and console placement remain.
The candidate adds only the six ADR 0013 identity features, declared identity blink intervals,
the ADR 0027 0.14-second blink token, paused-state freeze, shared geometry constants and the
held triangle-projection witness. Deterministic identity variety does not provide user
customization. No ring/rim, travel or inferred reference feature is introduced.

The shared hand/gaze target radius stays 0.1. Feature vertices are bounded to 1.32 shell units;
0.07 body scale times a conservative 1.08 squash/stretch reaches 0.099792, inside that target.
The most complex feature has at most 300 vertices/588 triangles, one material and one pass.
The current supported one-agent shelf therefore adds at most one feature pass, rather than
asserting that a six-agent group is a supported mode. This is a source bound, not device cost
measurement or a recognizability result.

**Regression entry points.** `StageRender.CharacterLifeCheck` runs at Standard and Larger text.
It creates the actual character-free Projects console and reuses the existing confirmed-shelf
search, whose whole envelope must clear that console and the measured field with existing
margins. It then exercises each of the 48 shape/feature combinations as exactly one grounded
agent. All existing activities in moving and held poses check shell/feature target containment,
actual shell contact, actual mesh/label containment in the placement envelope, label readability
and own-body clearance. Disabling only a feature must change pixels. Two fixed identities use
the actual production clock at 72 Hz; changing only `_EyeOpen` in the same blink frame must
change rendered eyes. Closed/crossed/flat eyes, immediate last-known freeze, Interrupted freeze
and live resumption retain separate assertions. `StageRender.AgentShelfCheck` now uses this
supported one-agent state consumer, retaining contact and adding state/readability/geometry
coverage. The historical `StageRender.Check` retains its old independent regressions.

**Stillness evidence remains unresolved.** The original held candidate's actual render passed
Larger but failed Standard: two pixels changed under Keep things still, by at most 13/255.
This candidate keeps the exact-zero assertion. Before any production stillness fix, both
current and historical fixtures now save before/after PNGs, exact transform/material snapshots
and parameter differences; a changing animation clock can be harmless when no rendered value
uses it. Asymptotic easing is a source hypothesis, not an established cause. Neither a tolerance
change nor a longer settling wait is proposed.

**Validation status.** The current candidate's filtered CharacterIdentityTests and GlazeTokenTests
passed 24/24 on .NET 10.0.401, with zero skipped. Actual Unity compilation and renders have not
run for this candidate. Required root renders are CharacterLifeCheck,
AgentShelfCheck and the historical StageRender.Check diagnostic, followed by the current
ShelfWorkspace/Director/Appearance gates. Isolated shader-ignores-EyeOpen and old-blink-period
mutations should each fail their specific witness and then be reverted. Stereo appearance,
feature recognizability, native/device behavior, hand interaction, cost and wearer acceptance
remain unverified.

**First actual candidate render.** `CharacterLifeCheck` on `bc75c72d` compiled and ran in Unity
6000.3.25f1. Standard passed; Larger failed exact stillness on Round/Ears, Blossom/Ears and
Squircle/Ears, each at two pixels with maximum differences of 18, 20 and 16/255. Other geometry,
feature, blink and paused assertions reported no failures. The historical `StageRender.Check`
also reproduced its Standard states-b failure, two pixels at at most 21/255; Larger passed.
This is not acceptance. Actual ear-edge pixels and saved parameter differences show residual
body rotation, Fog/EyeUneven and subnormal Look values; a causal explanation still needs isolation.
`CharacterStillConvergenceCheck` replays those three cases, saves the original frame, replaces
only those captured idle residuals with their exact rest targets in that same frame, renders,
then restores every changed value and requires identical restored pixels. Unchanged repeated
renders and corrected before/after moments must have exactly zero Color32/RGBA differences;
this diagnostic uses its own exact comparison, while the historical 2/255 RGB comparison is
unchanged. Larger
must reproduce the original drift, and the correction must account for every originally
changing pixel. It does not change production or relax the original stillness checks.

**Residual-only hypothesis rejected.** The first same-frame diagnostic on `20dc32ee` failed:
unchanged repeated and restored frames changed pixels, corrected moments still drifted, and
the original larger-text cross-moment drift did not reproduce in that capture sequence. This
contradicts a residual-only explanation; no production settling change is justified. The
follow-up exact Color32 comparison strengthens this diagnostic without changing historical
thresholds. `CharacterRenderDeterminismCheck` now captures at most eight unchanged frames per
controlled path: reused current target, disabled camera, disabled camera MSAA, freshly created
depth24/depth32 targets, isolated feature with the actual body shader and isolated feature with
built-in Unlit/Color. Every path retains identical animation values, logs exact RGBA coordinates,
camera/renderer matrices, shader globals and actual target format/sample/depth descriptors,
and restores camera targets, visibility and materials. These paths diagnose rendering; a zero
exit is only diagnostic execution success and is not stillness acceptance or a production fix.

**Unchanged rendering result and next composition isolation.** The actual `b84e2be0` run found
zero exact RGBA differences in isolated features with either the original body shader or
Unlit/Color, for all three identities at both text sizes. Composed scenes still changed a few
pixels across the camera/target paths. Both requested depth24 and depth32 were actually
D32_SFloat_S8_UInt with one sample on Metal; this did not test different depth formats.
Camera/renderer matrices, material values and shader globals stayed identical within each
sequence. This supports investigating composition rather than assuming standalone shader or
motion drift. `CharacterCompositeDeterminismCheck` therefore isolates shell+feature,
character parts, feature+non-character parts, the full scene minus its shell, and shell alone;
it also compares the unchanged full scene with explicit feature-before/after-shell sorting
orders, restoring all orders and visibility afterward. It logs actual queue/order/bounds and
exact pixels. No sorting tie, depth conflict or production fix is claimed before those results.

**Composition isolation and bounded candidate fix.** The actual `1294b209` run found exact
unchanged shell-only, full-minus-shell and feature/non-character renders. The shell/feature
pair drifted. Drawing the feature before the shell produced zero exact RGBA differences in
all six identity/text-size cases; feature-after-shell still drifted and restoring equal order
reproduced drift in five of six cases. This justifies validating one explicit shell-relative
sorting order, not a settling change. The production candidate sets only
`featureRenderer.sortingOrder = shellRenderer.sortingOrder - 1`; shader/depth/blending/queues
and all current motion paths stay unchanged. It does not claim a particular GPU batching or
internal sorting cause beyond this observed composition dependency.

`CharacterFeatureCompositionCheck` consumes all six feature kinds in every existing activity,
live and last-known (including Interrupted's paused state), beside a real confirmed one-agent
shelf and independent console. Eight unchanged captures must agree byte-for-byte in RGBA;
the actual consumed earlier sorting order and shared material are asserted. Disabling only
the feature must still change its pixels, and disabling the shell must change the composed
picture, preserving visible front/body content. Full current, without-shell and legacy-equal
order images retain inspection evidence; Ears, Antenna, Sprout and Horns also save actual-camera
junction close-ups in live and ghosted Idle. Stability alone is not appearance acceptance.
The composition diagnostic keeps the old equal-order condition explicit as its negative,
restoring the now-correct production order afterward. CharacterLife, AgentShelf, historical
Stage and current ShelfWorkspace/Director/Appearance gates remain required; no tolerance is
relaxed and no current candidate render pass is claimed before execution.
