# Horizon OS multitasking

- **Question:** Can Halcyonic stay present while the person does something else in the headset:
  codes on their Mac through Meta's Virtual Display, or uses another immersive app?
- **Date:** 2026-09-29.
- **Method:** Meta's developer documentation, help pages and release notes, and press coverage,
  read on the date above; for focus with windows open, also the v207 SDK sources, Unity's OpenXR
  package documentation and the OpenXR specification, and the second headset session's findings.
  Sources are linked with the date each page showed.
- **Status:** Documentation, and what the second headset session showed with Virtual Display open.
  Most of the headset checks at the end are not done yet.

## Findings

- **2D windows over an immersive app.** Since v69, rolled out globally in v74 (February 2025),
  Horizon OS keeps "the universal menu and up to three windows open" during immersive experiences
  ([release notes](https://www.meta.com/help/quest/172903867975450/), 2026-09-28). The immersive
  app keeps running and rendering; only one app has input
  ([app lifecycle](https://developers.meta.com/horizon/documentation/unity/unity-lifecycle/),
  2026-07-23). A person opens a window from the Meta button, Navigator and Library; no developer
  opt-in is documented. A 2D app sets its window size in its manifest's `<layout>`
  ([Android app features](https://developers.meta.com/horizon/documentation/android-apps/features-overview),
  2026-09-08).
- **Virtual Display.** Meta renamed Remote Desktop to Virtual Display in 2.7, with up to three
  screens ([help](https://www.meta.com/help/quest/1370025034331518/)). Its screens are windows that
  stay open inside VR and mixed reality apps
  ([UploadVR](https://uploadvr.com/quest-3-windows-11-remote-desktop-ultrawide-mode), 2025-10-13,
  press). Inference: a person can see their Mac and Halcyonic's characters at once while Halcyonic
  is the immersive app.
- **System windows sit close.** System overlays render within about 2 m of the person
  ([focus awareness](https://developers.meta.com/horizon/documentation/unity/unity-focus-awareness/),
  2024-12-20), so content placed beyond that is not intersected by them.
- **One immersive app at a time.** Launching another immersive app suspends Halcyonic's; passthrough
  changes the background, not this rule.
- **A 2D window from the same APK.** Meta's hybrid app guide puts a 2D activity beside the immersive
  one in one APK; with the `OVERLAY_LAUNCHER` category, the 2D activity is what opens when the app
  is launched from inside another immersive app, and started with `NEW_TASK` from the immersive
  activity it appears as a window over it. The guide targets OS v69 or later
  ([hybrid apps](https://developers.meta.com/horizon/documentation/spatial-sdk/hybrid-apps-overview),
  2026-09-22). Such an activity, written in Kotlin or Java, cannot reuse the C# client core, which
  runs in the Unity player (inference).
- **Background work and notifications.** The only background work Meta documents is media playback
  through a foreground `MediaSessionService`; `SYSTEM_ALERT_WINDOW` and
  `START_FOREGROUND_SERVICES_FROM_BACKGROUND` are prohibited
  ([prohibited permissions](https://developers.meta.com/horizon/resources/permissions-prohibited/),
  2025-04-30). `POST_NOTIFICATIONS` requires review, and Meta points apps to dashboard
  notifications, which do not appear in VR
  ([review-required permissions](https://developers.meta.com/horizon/resources/permissions-review-required/),
  2025-11-26).
- **The competition.** An entry is one APK in a release channel named Competition, and it must be a
  VR or MR application ([rules](https://start-developer-competition-26.devpost.com/rules)); a
  second app would not be judged.

## Focus with system windows open (after the second headset session, 2026-09-29)

What the session showed ([quest-3-device.md](quest-3-device.md)): with Virtual Display's windows
open inside Halcyonic, input focus went to whichever the hand ray reached first, and the windows
hung in front of the characters; the session flapped between focused and visible dozens of times,
each flap with a reference space change (OVRPlugin's recenter count reached 99) that moved nothing;
the stage re-placed itself about every two seconds; after the owner minimized the windows, Virtual
Display stayed the top resumed activity and Halcyonic stayed unfocused.

- **How a person returns focus.** Meta's requirement for focus-aware apps says to "Return focus to
  the immersive app, by pressing the [Meta button] on the controller again or by selecting the
  backgrounded app to restore focus to it"
  ([VRC.Quest.Input.4](https://developers.meta.com/horizon/resources/vrc-quest-input-4/), updated
  2025-09-26). With hands, selecting the app is pointing at its content, away from every window,
  and pinching; the Meta button is the palm gesture: look at a palm and pinch the Meta icon to open
  the universal menu, and again, or Resume, to close it. Build v2.9 PTC adds "Double press the Meta
  button on your Touch controller" to show or hide windows in immersive experiences
  ([release notes](https://www.meta.com/help/quest/172903867975450/), 2026-09-28); no hand gesture
  for it is documented. Whether a pinch at Halcyonic's content works while a minimized window is
  still the top resumed activity is not verified.
- **Nothing lets an app take focus.** The v207 Core SDK only reports it: `OVRManager.InputFocusLost`
  and `InputFocusAcquired`, `OVRManager.hasInputFocus`, and Unity's `OnApplicationFocus` (the
  [lifecycle page](https://developers.meta.com/horizon/documentation/unity/unity-lifecycle/),
  2026-07-23); neither it nor OpenXR has a call to ask for it.
- **Windows are drawn over the app.** The system renders overlay UI within about two meters, and
  asks apps to "Hide any input affordances or objects displayed closer than this distance, such as
  in-app menus, to prevent visual disparities"
  ([focus awareness](https://developers.meta.com/horizon/documentation/unity/unity-focus-awareness/),
  2024-12-20). In the session, where a window covered a character, the ray met the window first
  and the window took focus.
- **Reference space changes.** Unity's OpenXR package forwards the runtime's reference space changes
  to `XRInputSubsystem.trackingOriginUpdated`; "a single recenter may produce multiple reference
  space change events" (the package's input documentation, 1.18.0). OpenXR announces a change
  before its pose takes effect (`XrEventDataReferenceSpaceChangePending`). The Core SDK exposes the
  new origin relative to the old as `OVRManager.TrackingOriginChangePending`, and counts recenters
  in `OVRPlugin.GetLocalTrackingSpaceRecenterCount`, which `OVRDisplay.RecenteredPose` follows.
- **What Halcyonic does.** The stage in front of the person no longer re-places itself on
  reference space changes that move nothing: a change counts only when the head jumps within one
  frame, farther and faster than a head moves, which only a moved tracking space explains. Then the
  stage moves with the space at once, so it stays where it was around the person, and only a move
  with no focus change around it is taken for the person recentering, which places the stage in
  front of them. The log says which (`kept the stage where it stands: the reference space changed
  n times during focus changes and nothing moved`). The desk placement ignores these events too
  ([ADR 0015](../decisions/0015-the-stage-stands-on-the-persons-desk.md)).
- **Where windows are unlikely to cover the stage.** Halcyonic cannot read where windows are, and
  the person places them. In the session the screens hung in front of the arc 2.4 m away, which
  sits 10 degrees below eye level, in the middle of the view; on a desk the characters sit 30 to 40
  degrees down, below the middle. The part that is Halcyonic's: the desk placement, which the real
  room makes the default; the stage staying put while focus flaps; and look and pinch, which opens
  a character with a relaxed hand, whose headset ray (from the shoulder) then points down, below
  a window in front (an inference to check on the headset). Moving a window above the characters,
  by its bar, is the person's.

## Consequences

- Halcyonic stays the immersive app, and the characters stay alive while it lacks input focus
  (`ControlPlaneConnection` pauses only on an application pause, never on focus loss).
- Place the characters a little beyond 2 m, so system windows such as Virtual Display's screens do
  not intersect them.
- A 2D companion window for use inside other immersive apps is possible in the same APK, but its
  lifetime when hidden, its cost (a second client in Kotlin or Java) and store review are
  unverified; it is not built.
- Notifications are not a channel for the competition build.
- The stage ignores reference space changes that move nothing, and moves with a tracking space
  that does, so focus flapping under system windows no longer moves the characters.
- Returning focus with hands is the person's gesture, documented in the milestone 3 checks of
  [XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md); the app can only show its state.

## To check on the headset

1. With Halcyonic live, open Virtual Display and connect the Mac: are the screens and the
   characters both visible, and does the client stay connected? (Second session: both visible,
   the screens in front of the arc.)
2. Run `pnpm demo` while the owner types on the Mac: is "Needs you" noticed at the side?
3. Launch another immersive app: what happens to Halcyonic's process and to open windows?
4. With the screens open, point between them and the characters: does the stage stay put, and does
   the log show the reference space changes as kept?
5. Unfocused, and again after minimizing the screens: does pointing at Halcyonic's content and
   pinching return focus, or only the palm gesture and the universal menu?
6. Does a recenter with the palm gesture come with a focus change?
