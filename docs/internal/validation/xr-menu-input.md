# The input that opens the app's own menu on Quest

Verified on 2026-11-12 against Meta's own developer pages, for the lead's decision that the menu opens from anywhere.

## Verified fact
- **Touch controllers.** The Meta (Oculus) button is reserved for the system and is not available to an app; the Menu
  button (the three lines, on the left controller) is the one an app uses to bring up or dismiss its own pause or in-app
  menu. Source: [Touch button mapping best practices](https://developers.meta.com/horizon/blog/tech-note-touch-button-mapping-best-practices/)
  (written in 2017 for Rift Touch; Meta has published nothing that changes it for Quest that this read found).
- **Hands.** The system gesture is the dominant hand held palm toward the face with an index pinch; it opens the system's
  universal menu and is reserved. The same gesture on the non-dominant hand triggers the `Button.Start` event, which an app
  may poll for its own actions. Source: [Interactions setup, hand tracking](https://developers.meta.com/vr/documentation/unity/unity-handtracking-interactions/).
- **Requirement.** VRC.Quest.Input.8 (required for immersive apps): for apps that support hand tracking, the system
  gesture is reserved and must not trigger any other action in the app; no gesture event is to be processed while it is
  in progress. Source: [VRC.Quest.Input.8](https://developers.meta.com/vr/resources/vrc-quest-input-8/).

## What this checkout uses
The project runs Meta's XR Core SDK (`com.meta.xr.sdk.core` 207) over Unity's OpenXR loader with the Meta XR feature
enabled, and already uses `OVRManager` and `OVRCameraRig`, so `OVRInput` is available. The menu opens on
`OVRInput.GetDown(OVRInput.Button.Start)`.

## Not verified
- The OVRInput page (https://developers.meta.com/horizon/documentation/unity/unity-ovrinput/) states the button mapping
  only in diagrams; that `Button.Start` is the left controller's Menu button on Quest Touch is taken from those diagrams
  and Meta's long-standing mapping, not from text. It is proven only on the headset.
- That the left-hand gesture reaches `Button.Start` over the OpenXR backend in this build: not checked; the headset
  decides it.
- Whether the app's own pinch handling (`GazeHover`) already ignores pinches while the system gesture is in progress
  (VRC.Quest.Input.8): not checked in this change.
