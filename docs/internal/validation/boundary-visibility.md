# Hiding the boundary while the room shows

- **Question:** May a development build hide the headset's boundary while passthrough shows the
  room, as a candidate fix for reading that turned faint as the wearer leaned toward a Stationary
  boundary's edge, and what keeps the boundary coming back?
- **Date:** 2026-10-16.
- **Method:** Meta's and Khronos' pages below, read on the date above for the change's safety
  review, with each page's own date; the Meta XR Core SDK in the project's package cache
  (com.meta.xr.sdk.core, the version the project pins); `RoomPassthroughTests` and the development
  APK's manifest. No headset.
- **Status:** Documentation, source and unit tests. **Unverified on a headset.** Development builds
  only, off by default.

## Findings

### What the headset does with the request

- Hiding the boundary is a request the runtime may refuse. `xrRequestBoundaryVisibilityMETA` returns a
  success code meaning "not allowed" under runtime-specific conditions, and the runtime may change
  visibility itself and reports each change with `XrEventDataBoundaryVisibilityChangedMETA`
  ([request](https://registry.khronos.org/OpenXR/specs/1.1/man/html/xrRequestBoundaryVisibilityMETA.html),
  [event](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrEventDataBoundaryVisibilityChangedMETA.html),
  OpenXR 1.1.63).
- Meta honours it only while a passthrough layer renders, for 6DOF apps, not over Link, and not
  together with full boundaryless mode
  ([boundaryless mode in Unity](https://developers.meta.com/horizon/documentation/unity/unity-boundaryless/),
  2026-02-06; [Core SDK 66.0 notes](https://developers.meta.com/horizon/downloads/package/meta-xr-core-sdk/66.0/),
  2024-06-25). Full boundaryless (`com.oculus.feature.BOUNDARYLESS_APP`) is for apps with no immersive
  part; Halcyonic has a virtual space and never declares it.
- No page found promises that the boundary returns by itself when passthrough stops, the app pauses
  or loses focus, or it quits. Meta's best practices tell apps to turn the boundary back on before
  leaving mixed reality
  ([boundaryless best practices](https://developers.meta.com/horizon/resources/boundaryless-best-practices/),
  2026-09-04). System views such as Change Boundary and Enter Passthrough pause the app
  ([VRC.Quest.Functional.2](https://developers.meta.com/horizon/resources/vrc-quest-functional-2/),
  2025-01-17, revised 2025-09-05).
- With the boundary off, the room's stage space is unreliable, so content is held by world locking
  or an anchor ([boundaryless mode in Unity](https://developers.meta.com/horizon/documentation/unity/unity-boundaryless/)).
  Halcyonic tracks from the floor level (`Stage.unity`, `_trackingOriginType: 1`) and keeps surfaces
  with spatial anchors; whether that holds with the boundary hidden is unverified.

### What the SDK does (source)

- `OVRManager.UpdateBoundary` sends a request only while passthrough is starting or running and
  `isInsightPassthroughEnabled` is true, retrying each frame while the wanted and actual states
  differ. It stops sending once passthrough is disabled, so a flag cleared in the same step that turns
  passthrough off never asks for the boundary back. If the flag stays false while the boundary is
  hidden, it asks to show it on its next frame.
- Boundary Visibility Support changes only the manifest in a build: it adds
  `com.oculus.permission.BOUNDARY_VISIBILITY`. The OpenXR extension `XR_META_boundary_visibility` is
  requested regardless. With the project's own manifest, Meta's build step leaves an existing tag
  alone when the support is None.

## Consequences

- `OculusProjectConfig` keeps Boundary Visibility Support at None. `BoundaryInDevelopmentBuilds`
  adds the permission only while `QuestBuild` builds a development APK; the development build fails
  without it, and the release build deletes an APK that asks for it or for the boundaryless feature.
  The requests compile only into development builds and the editor.
- `RoomPassthrough` asks for the boundary hidden only while the person chose it this session,
  passthrough runs, its layer shows, and the app has focus and is not paused. On every way out (Stop,
  for the virtual space or the scene's teardown; every unavailable path; pause; focus loss; quit;
  Show boundary) it clears OVRManager's flag and asks for the boundary directly, and only then turns
  passthrough off. Stopping the room clears the choice. `RoomPassthroughTests` prove that order for
  each path with a recording device.
- Settings' Your space shows a Boundary row in development builds. It says Hidden only after the
  headset reported it, and the first such report in a session says so once in the workspace.
- Every request, its answer and every report go to the log as "Halcyonic: room boundary ...".

## To check on the headset

1. The boundary is hidden only while the room shows, and the launch notice the system may show
   appears.
2. It returns on Show boundary, on the virtual space, when passthrough fails, on Enter Passthrough
   and Change Boundary, on the system menu (focus loss), when the headset comes off, and on quit, each
   with the log's answer to the request.
3. Seated, leaning toward the edge of a Stationary boundary, the faint reading seen on 2026-10-15
   does not appear while the boundary is hidden, and does with it shown.
4. The workspace and a placed shelf stay where they were with the boundary hidden.
5. Store policy for contextual boundaryless is rechecked before any store submission.
