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

## Consequences

- Stage.unity uses the comprehensive rig as the building block installs it, with locomotion and the
  locomotor deactivated, and the SDK's gaze with camera pose emulation, set up by `StageSetup`
  ([ADR 0014](../decisions/0014-hand-interaction-through-the-interaction-sdk.md)).
- The workspace builds its targets in code with the SDK's inject methods, planes for pokes, and a
  gaze interactor that only hovers.
- Still to verify on a Quest 3: rays, pinches, pokes and head gaze on these targets; the system
  keyboard under OpenXR, and whether Unity reports its focus change to `FocusGuard`; `HttpClient`
  under IL2CPP for the history; legibility at the chosen sizes.
- The controller visuals are most of the APK's growth. A hands-only build could drop them from the
  rig; nothing needs a controller.
