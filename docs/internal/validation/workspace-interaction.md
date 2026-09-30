# Workspace interaction: Interaction SDK, TextMeshPro and the system keyboard

- **Question:** What do the Meta Interaction SDK 207, TextMeshPro in Unity 6000.3 and the Quest
  system keyboard provide for the expanded workspace (milestone 3), and how do they behave when
  set up and built in batch mode?
- **Date:** 2026-09-29.
- **Environment:** Unity 6000.3.25f1 in batch mode on an Apple M5 Max with macOS 26.7; the Meta XR
  Core and Interaction SDKs 207.0.0 and `com.unity.ugui` 2.0.0 as resolved by the pinned manifest;
  the control plane from this repository on a private port and data directory.
- **Method:** Meta's documentation for the system keyboard; the SDK and package sources in the
  project's package cache; batch runs that set up the scene, compile and build the APK; the built
  APK's manifest read with `aapt2`; a private control plane driven by `pnpm demo`.
- **Status:** Verified in the editor and in the build. Not verified on a headset: every behavior
  below that happens at runtime (rays, pokes, gaze, the keyboard, focus, REST history).

## Findings

### Interaction SDK

- **The rig.** The SDK's "Interactions Rig" building block runs Meta's comprehensive rig wizard
  without an editable copy: it instantiates `OVRComprehensiveInteractionRig.prefab` under the
  camera rig, lets the SDK's auto-wiring link it (for example `OVRCameraRigRef._ovrCameraRig` to the
  camera rig), and disables the renderers of the camera rig's existing `OVRHand` objects, since the
  rig draws its own hands. Its public `QuickActionsAPI` adds interactors to an existing rig but has
  no call for the rig itself.
- **What the rig contains.** Hand and controller data sources; hand and controller visuals; per
  hand, poke, ray, grab, distance grab and touch grab interactors under a best-hover group, so a
  poke and a ray do not both act; hand locomotion as `MicroGesturesLocomotionHandInteractorGroup`
  (in 207 there is no `LocomotionHandInteractorGroup`), controller locomotion as
  `LocomotionControllerInteractorGroup` (stick slide and turn active by default); and a `Locomotor`
  whose `FirstPersonLocomotor` auto-wires to move the camera rig, with tunneling effects that
  raycast against any collider. Without a floor collider the locomotor disables its velocity on
  start and logs a warning.
- **Runtime interactables.** Every SDK component has `Inject…` methods for objects built in code,
  and checks its dependencies in `Start`, which `BeginStart` and `EndStart` defer by disabling and
  re-enabling the component. Injecting right after `AddComponent`, in the same frame, is enough.
- **Surfaces.** `PlaneSurface` faces backward by default: its normal is the transform's -Z, the
  side text faces, and a one-sided plane ignores rays from behind. `ClippedPlaneSurface` bounds it
  with `BoundsClipper`s. `ColliderSurface` raycasts one collider only. A `PokeInteractable` needs a
  surface patch, so a sphere cannot be poked; a plane in front of it can. Poke defaults: hover
  within 0.15 m of the surface, select on crossing it.
- **Ray targeting.** The ray takes the closest hit among all ray interactables within 5 m; hits
  within 1 mm of each other go to the higher tiebreaker score. A background interactable therefore
  blocks what is behind it, and a button 4 mm in front of it wins.

### Gaze

- The v207 gaze classes (`EyeGaze`, `GazeInteractor`, `GazeInteractable`, `GazeConecaster`) are
  marked experimental in the source.
- `EyeGaze` with **Emulate Gaze With Camera Pose** replaces the eye pose with the camera pose and
  marks it tracked. `FromOVREyeGazeDataSource` records the center eye camera's pose on every update
  and adds eye data only where `OVRPlugin.eyeGazeInteractionsSupported`, so with the emulation the
  gaze is the head's direction on a device without eye tracking, such as a Quest 3.
- `GazeConecaster` tests the surface of every registered `GazeInteractable` against a cone of 2
  degrees and 10 m from the gaze pose, and settles on a candidate after a 0.2 s dwell.
  `GazeInteractable` takes any `ISurface`, so a character's ray sphere serves it too.
  `GazeInteractor` needs a selector, a pointer transform and that candidate provider, all
  injectable at runtime.
- The SDK's gaze quick action builds the gaze as Meta's eye gaze prefab beside the rig's HMD with a
  `GazeConecaster` child. Built that way in batch mode, the auto-wiring linked its data source to
  the rig's `OVRCameraRigRef` and to the center eye camera.
- The SDK's gaze and ray fallback filters hand rays away from gaze-tagged objects while the gaze is
  active. With head gaze always active, that would switch the rays off for those objects, so it is
  not used: the workspace's gaze interactor only hovers.

### TextMeshPro

- `com.unity.textmeshpro` 5.0.0 is a shim; TextMeshPro lives in `com.unity.ugui` 2.0.0, which ships
  `TMP Essential Resources.unitypackage`. `TMP_PackageResourceImporter.ImportResources` queues the
  import, which completes on a later editor update: a batch run with `-quit` exits first, so the
  import needs the editor alive until `AssetDatabase.importPackageCompleted`.
- Liberation Sans SDF is a static atlas of 250 characters, including "·", "“", "”" and "…"; a
  dynamic fallback asset covers the rest from `LiberationSans.ttf`. The SDF shader reaches a build
  through the font asset's material in `Resources`.
- For the 3D `TextMeshPro` component, one point is a tenth of a unit: font size 0.25 is a 25 mm em.
- `enableWordWrapping` is obsolete in this version; `textWrappingMode` replaces it.

### System keyboard

- Meta's documentation (Unity keyboard overlay, read 2026-09-29): enable **Require System
  Keyboard** (focus awareness must be on), open it with `TouchScreenKeyboard.Open("",
  TouchScreenKeyboardType.Default)`, the only supported type, and read `TouchScreenKeyboard.text`.
  While the keyboard is shown the app loses input focus (`OVRManager.InputFocusLost`) and gets it
  back when it closes (`InputFocusAcquired`).
- `requiresSystemKeyboard` in `OculusProjectConfig` is applied at build time: Meta's Gradle step
  patches the generated manifest. The built APK declares `oculus.software.overlay_keyboard` with
  `required="false"`, while `Assets/Plugins/Android/AndroidManifest.xml` stays unchanged.

### Editor and build behavior

- Every batch run with Android as the build target logs "Killing ADB server" when it exits, a
  build or not, which drops `adb reverse` rules on a connected headset.
- Meta's `OVREngineConfigurationUpdater` sets the Android orientation to landscape left and
  `vSyncCount` to 0 on the editor's first update with Android active; the editor then saves
  `ProjectSettings.asset` and `QualitySettings.asset`. Batch runs with `-quit` end before it runs.
- The development APK with the rig and TextMeshPro is 81.5 MB (main's is about 63 MB), built in
  3 min 18 s after an asset reimport. Most of the difference is the rig's hand and controller
  assets and the Interaction SDK's code; by the build report, the controller models and textures
  alone are 40 MB before compression. After incremental rebuilds the file grew to 107 MB while its entries still totaled
  81.4 MB, which suggests space left by the packager updating the APK in place.

### An approval that waits

`pnpm demo` answers its approval 3 seconds after the request. Reading its output with
`sed '/approval requested/q'` ends the demo with a broken pipe as soon as it asks; 5 seconds later
the approval workstream was still `waiting_for_human` with one pending approval.

## After the second headset session (2026-09-29, evening)

The owner opened, read and approved on a Quest 3 ([quest-3-device.md](quest-3-device.md)), and
found four problems: the gaze peeked every character the head swept past, the hand ray appeared
only at one raised hand angle, the panel let labels behind it show through, and it covered the
rest of the arc. The hands looked like grey outlines.

- **Method:** the v207 sources and prefabs in the package cache (`BaseInteractors.prefab`,
  `HandRayInteractor.prefab`, `HandGazeInteractor.prefab`, `HandPointerPose`,
  `FromOVRHandDataSource`, `OVRHand`, `GazeConecaster`, `GazeInteractor`, `IndexPinchSelector`,
  `PokeInteractable`, `OculusHand.mat`); Meta's design and gaze documentation and the OpenXR
  specification, read on 2026-09-29; editor renders with `WorkspaceRender`.
- **Status:** verified in the sources and in the editor. What happens on the headset is not.

### The hand ray's active state in the comprehensive rig

- Each hand's `HandRayInteractor` comes unchanged from `HandRayInteractor.prefab` inside
  `BaseInteractors.prefab`. Its `RayInteractor` is on while an `ActiveStateGroup` (OR) is:
  `HandPointerPose`, whose `Active` is `IHand.IsPointerPoseValid`, or an `InteractorActiveState`
  for a selected interactable, so a pinch that began on a target keeps its ray. An
  `ActiveStateTracker` turns the interactor off while the hand is not connected.
- `IsPointerPoseValid` comes from `FromOVRHandDataSource`, which copies
  `OVRHand.IsPointerPoseValid`, the runtime's `HandStatus.InputStateValid`: under OpenXR, the valid
  bit of `XR_FB_hand_tracking_aim`, which the specification describes only as "Aiming data is
  valid". The ray's origin and direction are the runtime's pointer pose. Meta's guidance stabilizes
  a hand ray from "a secondary body position, like a shoulder or hip point of origin"
  ([raycasting best practices](https://developers.meta.com/horizon/design/raycasting_bp/), updated
  2026-09-14), and the SDK's own `ShoulderEstimatePosition` puts the shoulder 0.25 m below the eyes.
- The ray's visual shows only while the ray hovers a target (`_hideWhenNoInteractable`), and the
  rig's `BestHoverInteractorGroup` lets one interactor of a hand hover at a time, the poke first.
- From a shoulder 0.25 m below the eyes, a character on the arc 2.4 m away, 0.45 m below the eyes,
  is about 5 degrees down and its target about 11 degrees across, so a shoulder-based ray reaches it
  only from a hand held within a few centimeters of shoulder height: the "one raised hand angle" of
  the session. This is inferred from the geometry; how the runtime computes its pose, and when it
  calls it valid, is not documented.

### Gaze

- `GazeInteractor` selects its candidate when its `ISelector` fires, and the SDK's
  `HandGazeInteractor.prefab` pairs it with the hand's `IndexPinchSelector`: look and pinch. Meta's
  [gaze interaction](https://developers.meta.com/horizon/documentation/unity/unity-isdk-gaze-interaction)
  page (updated 2026-09-09) describes the same four parts, with a pinch or a controller button to
  confirm, and the camera pose as the gaze where eyes are not tracked.
- `GazeConecaster` settles on the target hovered longest over 0.2 s, within 2 degrees. Sweeping the
  head at 60 degrees a second keeps each character (about 11 degrees across, plus the cone) under
  the gaze for about 0.25 s, longer than that: this is why every character swept past peeked.

### Pokes, the panel and the hands

- `PokeInteractable` hovers from 0.15 m along its surface's normal, and stops at 0.2 m. On a desk,
  0.55 m from the eyes (ADR 0015), a character is within that of hands typing in front of it.
- The panel's plate was `Sprites/Default` at 95 percent opacity. Rendered in the editor, with the
  project in linear color space, the stage behind it changed up to 54 of 255 levels in 1,671 of
  its pixels with the characters 2.4 m away, and in 553 with them on a desk: labels read through
  it. Opaque, no pixel changes. The workspace already drew after everything at the characters'
  distance (sorting order 10 to 12 against 0 to 2), so the opacity was the fault, not the order.
- Most batch runs on 2026-09-29 (a compile, the stage setup twice, a render and the APK build)
  ended with exit status 134 after `Exiting batchmode successfully now!`: macOS's crash reports
  put the abort in the Interaction SDK's `ISDKEngineTelemetry.dylib`, a telemetry thread locking a
  mutex during shutdown. The work was done and saved each time; the log, not the exit status, says
  whether it succeeded.
- The rig's `OVRHandVisualLeft` and `OVRHandVisualRight` use the SDK's `OculusHand.mat`: a dark
  grey fill (0.12 to 0.20) at 79 percent opacity with a mid-grey outline (0.54) 1.4 mm wide. In a
  dark space the fill all but disappears and the outline stays: grey outlines. The Hand Tracking
  building block's own hands are disabled (`StageSetup`), and `FocusGuard` hides the rig's while
  Halcyonic lacks input focus, so the grey outlines seen while Halcyonic had focus were Halcyonic's
  rig hands, the Interaction SDK's default look, not the system's. Nothing was changed.

### What changed

- **Seated rays.** `StageSetup` gives each hand ray a `SeatedHandRay` in the pointer pose's place in
  its active state group and disables the SDK's `HandPointerPose`, which moved the same transform,
  the ray's origin. `SeatedPointing` (client core) runs the ray through the index knuckle, which a
  pinch barely moves, from a pivot 0.40 m below the eyes, 0.10 m behind them and 0.13 m to the
  hand's side, about where a relaxed forearm rests: a hand about 0.40 m below the eyes points
  straight ahead, at the characters on the arc and, moved in front of one, on a desk. It is on
  while the hand is tracked with high confidence, at least 0.1 m in front of the eyes, with its palm
  more than 35 degrees from facing the floor and more than 60 degrees from facing the eyes.
- **The trade-off.** A pivot that low makes a hand at a desk's height point near the stage, so the
  pivot alone would put rays on characters while the person types. The palm decides instead: a
  hand resting or typing faces the floor and has no ray; to point, the palm turns sideways or away,
  half way is enough, as it does for the headset's own ray. Pointing palm down with an extended
  finger shows no ray, and a hand resting on its side on a desk has one, pointing just below the
  lineup. The runtime's validity is no longer required, since what it demands is not documented;
  the palm test excludes the system gesture.
- **Look and pinch.** `GazeHover` is its gaze interactor's selector, fed by an `IndexPinchSelector`
  on each of the hands the seated rays use. A pinch selects only while `PeekChoice` says a gaze peek is
  showing, no hand ray or finger is on any target, and the pinching palm does not face the eyes;
  the director then opens the character only if it is the one the pinch was for.
- **A calm peek.** `PeekChoice` (client core) waits for the gaze to rest half a second on one
  character within 7 degrees of where the head faces, restarts while the head turns faster than 20
  degrees a second, fades peeks in (0.25 s) and out (0.15 s) one at a time, keeps a peek 0.3 s after
  a glance aside, never peeks the open character, and lets only hands peek while a workspace is
  open, so reading it never brings up a peek or opens another character.
- **Pokes on a desk.** A character's poke hovers within 0.06 of its scale, at most the SDK's 0.15 m:
  3.3 cm on a desk, 0.14 m on the arc 2.4 m away.
- **The workspace.** `WorkspacePlacement` (client core) opens it clear of every character's body,
  below the ones it passes or above them, with its center between 30 degrees below and 2 above eye
  level: below the arc 2.4 m away (its center at 30 degrees down, over the labels of the characters
  it passes), above the lineup on a desk (10 to 15 degrees down), never lower than 5 cm above the
  desk. The plate is opaque. `WorkspaceRender` renders both and fails if a pixel of the workspace
  changes with the stage behind it or a body is behind it.

## Consequences

- Stage.unity uses the comprehensive rig as the building block installs it, with locomotion and the
  locomotor deactivated, and the SDK's gaze with camera pose emulation, set up by `StageSetup`
  ([ADR 0014](../decisions/0014-hand-interaction-through-the-interaction-sdk.md)).
- The workspace builds its targets in code with the SDK's inject methods, planes for pokes, and a
  gaze interactor that selects only on a look and pinch the client core allows.
- The rig's hand rays are the seated rays, set up by `StageSetup` like the rest of the rig.
- Still to verify on a Quest 3: the calm peek's timing, the seated rays from a relaxed posture and
  none while typing, look and pinch, pokes on a desk, the workspace clear of the arc in both places;
  the system keyboard under OpenXR, and whether Unity reports its focus change to `FocusGuard`;
  legibility at the chosen sizes. `HttpClient` under IL2CPP works
  ([quest-3-device.md](quest-3-device.md)).
- The controller visuals are most of the APK's growth. A hands-only build could drop them from the
  rig; nothing needs a controller.
