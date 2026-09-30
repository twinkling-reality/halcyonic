# Mixed reality in the person's room

- **Question:** What do passthrough, the scene model through the MR Utility Kit (MRUK), spatial
  anchors and their permissions need from the XR client, so that the characters stand on the
  person's real desk and are there again next session, and what happens in rooms nobody tested?
- **Date:** 2026-09-29.
- **Environment:** Unity 6000.3.25f1 on macOS 26.7 (Apple M5 Max); OpenXR 1.18.0; the Meta XR
  Core and Interaction SDKs 207.0.0; MRUK (`com.meta.xr.mrutilitykit`) 207.0.0, added with the
  owner's approval of 2026-09-29.
- **Method:** Meta's developer documentation, read on the date above, with each page's own
  last-updated date; the SDKs' source in the package cache; batch development and release builds
  with `QuestBuild`, inspected with `aapt2`; and NUnit tests of the engine-free decisions on
  .NET 10. Nothing in this record ran on a headset.
- **Status:** Requirements and SDK behavior verified in documentation and source; the room
  placement compiles, builds and passes its .NET tests. Not verified: anything at runtime on a
  headset (listed at the end).

## Findings

### The package

- MRUK 207.0.0 declares `com.meta.xr.sdk.core` 207.0.0, `com.unity.ai.navigation` 1.1.4,
  `com.unity.textmeshpro` 3.0.6 and `com.unity.nuget.newtonsoft-json` 3.0.2. The project resolved
  AI Navigation to 2.0.14, the version this editor bundles in its own package folder, TextMeshPro
  to the built-in 5.0.0 and Newtonsoft.Json to the pinned 3.2.2. The Package Manager now reports
  four Meta packages installed without signatures.
- MRUK's runtime assembly references `Oculus.VR`, `Oculus.Interaction`, `Unity.AI.Navigation`,
  `Unity.TextMeshPro`, `Meta.XR.EnvironmentDepth` and `Meta.XR.ImmersiveDebugger`. `MRUK.Awake`
  reads `Meta.XR.ImmersiveDebugger.RuntimeSettings.Instance.ImmersiveDebuggerEnabled` without a
  guard, so a release build that leaves that assembly out fails in the linker
  (`error IL1005 ... MRUK.Awake()`), even with MRUK unused. The release build now ships that
  assembly; why that is acceptable is in [horizon-store-release.md](horizon-store-release.md).
- Every build that contains MRUK starts its native library before the splash screen, whether or
  not a scene uses MRUK (`MRUK.Shared.cs`): it creates its context, attaches to the OpenXR
  session when one exists, and registers callbacks, one of which can set the camera rig's
  tracking space pose. Only `MRUK.Update` applies a world lock offset, and only while
  `EnableWorldLock` is true.
- MRUK sends a telemetry event on each scene load (`TelemetryConstants.SendMRUKEvent`: the event,
  the room count, the device OS, the platform and the OpenXR runtime's name), marked
  non-essential, through the Core SDK's native telemetry (`OVRPlugin.SendUnifiedEvent`), the path
  the Core SDK already uses for its own events. What the native side does with non-essential
  events under the headset owner's consent was not verified.

### Passthrough

- `OculusProjectConfig`'s **Passthrough Support** (`_insightPassthroughSupport`: None, Supported,
  Required) is what Meta's build step turns into `<uses-feature android:name="com.oculus.feature.PASSTHROUGH">`
  with `required` false or true. The older `insightPassthroughEnabled` field is obsolete and does
  nothing (`OVRProjectConfig`). The project declares Supported, since the app runs without
  passthrough.
- At runtime, `OVRManager.isInsightPassthroughEnabled` may be set at any time: `OVRManager.Update`
  initializes passthrough when it turns on and shuts it down when it turns off. Initialization can
  be pending; `IsInsightPassthroughInitialized`, `HasInsightPassthroughInitFailed` and
  `IsInsightPassthroughSupported` report its state, and a failure is not retried.
- `OVRPassthroughLayer` always composes as an underlay at the greatest composition depth, and
  disables its layer while passthrough is not initialized. For the room to show, the camera must
  clear to a solid color with zero alpha, and the passthrough layer reports when it is visible
  ([Passthrough getting started][pt], 2026-04-09).
  `OVRManager.IsPassthroughRecommended()` reports whether the person uses passthrough in Home; the
  app does not use it, because the real room is the product's default.
- Under OpenXR, the Meta XR feature (enabled for Android in the committed settings) requests
  `XR_FB_passthrough`, `XR_FB_scene`, `XR_FB_scene_capture`, the `XR_FB_spatial_entity` family
  and `XR_META_spatial_entity_persistence` itself; no other OpenXR feature is needed.

### The scene model and its permission

- Reading the scene model needs `com.oculus.permission.USE_SCENE`
  (`OVRPermissionsRequester.ScenePermission`), a runtime permission. On a request the system first
  explains the permission once, then asks for consent. Without it, MRUK and the OVRAnchor API
  return no scene data and raise no error; MRUK's `LoadSceneFromDevice` returns
  `NoScenePermission`. Meta's guidance: request the permission when the feature is needed, provide
  a fallback if it is denied, do not combine request methods, and do not rely on
  `PermissionDeniedAndDontAskAgain` ([Spatial data permission][perm], 2024-08-15). A request made
  while the dialog is already open is dropped without a callback (a comment in `MRUK.cs`), so the
  app also polls the permission.
- Both OVRManager's "Permission Requests On Startup > Scene" and MRUK's `LoadSceneOnStartup`
  (true by default) ask at launch, before anything is shown. The app turns both off and asks
  after a line of explanation instead.
- MRUK's scene settings default to loading on startup, `LoadSceneFromDevice`'s
  `requestSceneCaptureIfNoDataFound` defaults to true, which starts space setup whenever no room
  is found, and `EnableWorldLock` defaults to true, which makes MRUK set the camera rig's
  TrackingSpace every frame and warn when anything else moves it
  ([Manage scene data][scene], 2026-07-06; `MRUK.cs`). The app creates MRUK itself with loading
  on startup off, world lock off, and space setup only on the person's request.
- `LoadSceneFromDevice` reports `Success`, `NoScenePermission`, `NoRoomsFound` (space setup was
  never done), `DiscoveryOngoing`, and failures including `NotInitialized` (no headset runtime),
  insufficient view, rate limiting, too dark and too bright. `GetCurrentRoom` returns
  the room the headset is in, or the last one it was in, or the first room, so the app checks
  `IsPositionInRoom` against the eyes before using a room.
- Anchor geometry (`MRUKAnchor`, `MRUKRoom`): a plane's normal is the anchor's +Z, and its
  `PlaneRect` and `PlaneBoundary2D` lie in the anchor's local XY plane; a volume (a table, a couch,
  a screen) reaches from its bottom at local minimum Z to its top at maximum Z, which is why MRUK
  aligns prefabs to a volume's bottom at its minimum Z; MRUK's own surface search counts a plane
  as facing up when its +Z is within 45 degrees of up. Desks are labeled `TABLE`; there is no
  desk label.
- `OVRScene.RequestSpaceSetup()` pauses the app while the system's space setup runs and resumes
  it when the person finishes or cancels; a cancel still completes the task successfully
  (`OVRScene.cs`). Space setup cannot run over Link.
- Meta's build step adds `USE_SCENE` when Scene Support is not None, and `USE_ANCHOR_API` when
  Anchor Support is enabled or Scene Support is not None (`OVRManifestPreprocessor`). With a
  custom manifest, as this project has, it adds missing entries and never changes or removes
  existing ones.

### Spatial anchors

- An `OVRSpatialAnchor` added to a GameObject creates an anchor at that transform in its `Start`,
  converting the pose to tracking space through `Camera.main`; `Created`, `Localized` and
  `IsTracked` report its state, and every `Update` sets the transform to the anchor's located pose.
  `SaveAnchorAsync` saves it on the headset; `LoadUnboundAnchorsAsync` (up to 50 UUIDs) finds
  saved ones; `UnboundAnchor.LocalizeAsync(timeout)` localizes one, and `BindTo` binds it to a
  component added in the same frame; `EraseAnchorsAsync` erases by UUID without loading. Destroying
  the component does not erase the saved anchor ([Persist content][persist], 2025-12-04).
- The anchor API needs `com.oculus.permission.USE_ANCHOR_API` and Anchor Support, and not the
  spatial data permission. Inference: a saved placement can be restored when the person declined
  room access.
- Localization can fail where the headset has not perceived or mapped the anchor's surroundings
  well ([Persist content][persist]), and Meta suggests objects within about three meters of an
  anchor may share it ([Spatial anchors][anchors], 2025-12-22). An anchor saved in another room,
  or one the headset no longer holds, is not found or does not localize; the app treats both as a
  lost placement and chooses again.

### Store requirements

- Neither `USE_SCENE` nor `USE_ANCHOR_API`, nor passthrough, is on Meta's prohibited
  ([list][prohibited], 2025-04-30) or review-required ([list][review], 2025-11-26) permissions.
  Security.2 requires every declared permission to be used: `USE_SCENE` reads the room,
  `USE_ANCHOR_API` keeps the stage's place.

## Consequences

- The room placement lives in its own assembly, `Halcyonic.XR.Room`, the only one that
  references MRUK; its choices and words are in the client core with tests
  ([XR_CLIENT.md](../architecture/XR_CLIENT.md), [ADR 0015](../decisions/0015-the-stage-stands-on-the-persons-desk.md)).
- MRUK is configured in code: no loading on startup, no world lock, no automatic space setup.
- The release build ships the Immersive Debugger's runtime, disabled.
- Every fallback leaves the characters in front of the person, and says why in one line.

## To check on the headset

The steps are in [XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md), under "Room placement
checks on a Quest". What only the headset can answer:

1. Passthrough starts, the room shows behind the characters, and the frame rate stays at 72 fps
   with passthrough, hand tracking and the scene model loaded.
2. The explainer and consent dialogs appear after the app's line, and the answer arrives.
3. MRUK reads a room set up with a desk, `IsPositionInRoom` holds for a seated person, and the
   chosen spot lies on the real desk's near half, in reach and in view.
4. The anchor is created, localized and saved within seconds, restores on the next launch at the
   same place, and does not localize in another room.
5. A recenter moves the anchored stage with the desk, once, after it settles.
6. Opening and closing system windows, Virtual Display's among them, over the app does not move
   the stage or clear its placement, and the anchor stays localized through the focus changes.
7. Space setup started from the app pauses it and resumes it, and the room is read again.
8. The release APK starts with the Immersive Debugger's runtime included and disabled, with no
   debugger interface.

[pt]: https://developers.meta.com/horizon/documentation/unity/unity-passthrough-gs
[perm]: https://developers.meta.com/horizon/documentation/unity/unity-spatial-data-perm/
[scene]: https://developers.meta.com/horizon/documentation/unity/unity-mr-utility-kit-manage-scene-data/
[persist]: https://developers.meta.com/horizon/documentation/unity/unity-spatial-anchors-persist-content
[anchors]: https://developers.meta.com/horizon/documentation/unity/unity-spatial-anchors-overview/
[prohibited]: https://developers.meta.com/horizon/resources/permissions-prohibited
[review]: https://developers.meta.com/horizon/resources/permissions-review-required
