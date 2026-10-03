# Horizon OS multitasking

- **Question:** Can Halcyonic stay present while the person does something else in the headset:
  codes on their Mac through Meta's Virtual Display, or uses another immersive app?
- **Date:** 2026-09-29; corrected and extended on 2026-10-02 (below).
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
- **One immersive app at a time.** Only one immersive app runs in the foreground. That launching
  another one suspends Halcyonic's, rather than ending it, has no Meta source we have read
  (unverified, 2026-10-02); passthrough changes the background, not this rule.
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
  2025-04-30). `POST_NOTIFICATIONS` requires review, and Meta points apps to the dashboard's User
  Notifications instead
  ([review-required permissions](https://developers.meta.com/horizon/resources/permissions-review-required/),
  2025-11-26). Corrected on 2026-10-02: dashboard notifications do reach the headset, in its
  notification feed (below); the earlier "do not appear in VR" was wrong.
- **The competition.** An entry is one APK in a release channel named Competition, and it must be a
  VR or MR application ([rules](https://start-developer-competition-26.devpost.com/rules)); a
  second app would not be judged.

## Reaching the person inside another app (2026-10-02)

Read on 2026-10-02 by the coordinator's research and checked again in this lane; each page's own date
is given where it shows one.

- **Dashboard notifications reach the headset's feed, not a toast.** Single-send and event-based
  notifications go to the "VR notification feed"; only mobile ones also push. They are made and
  submitted in the Developer Dashboard, are reviewed (Approved, Pending, Rejected and so on), follow
  wording rules, and follower notifications are limited to "1 notification sent per day from your
  organization" ([user notifications](https://developers.meta.com/horizon/documentation/native/ps-user-notifications),
  2025-11-04). Sending one from an app's own events goes through Meta's servers with the app's
  credentials (the coordinator's research; the page read here does not show the endpoints).
- **A device notification from the app itself.** The Kotlin VR Platform SDK's
  `Notifications.deviceNotification()` "Triggers a device notification to show the notification
  toast and feed the notification to notification feed"
  ([reference v0.2.2](https://developers.meta.com/horizon/reference/horizon-platform-sdk-android-kotlin/v0.2.2/horizon_platform_notifications_notifications),
  no date shown). Its `DeviceNotificationConfig` takes a title and a message, and optionally
  `isToastOnly`, an image (`mediaAttachmentUri`), an app icon by package name, a delivery id, and
  an action with a title, an icon and a display type that opens an app by id or package, or an
  intent with data and extras
  ([config reference](https://developers.meta.com/horizon/reference/horizon-platform-sdk-android-kotlin/v0.2.2/horizon_platform_notifications_configs_devicenotificationconfig)).
  Meta's sample (meta-quest/horizon-platform-sdk-samples at dde65b4, 2026-09-16, notifications,
  SDK 0.2.0) declares only `INTERNET`, sets `minSdk` 34, and first calls
  `HorizonServiceConnection.connect(APPLICATION_ID, …)` with the app's Application ID from the
  Developer Dashboard. The guide page for Kotlin apps is not yet published, and the Unity Platform
  SDK v207 has no such call. Not verified: whether the toast shows over another immersive app,
  whether it makes a sound, whether it is reviewed, and its rate limits. Do Not Disturb "silences
  notifications while you're in 3-dimensional games and apps"
  ([v49 announcement](https://www.meta.com/blog/meta-quest-v49-do-not-disturb-family-center-abstract-home/)),
  which suggests that without it toasts do show over them (inference).
- **Windows over a game keep their state.** Up to three windows stay open over an immersive app
  (above), and "Minimized apps will maintain their state" and reopen from the Navigator
  ([moving and adjusting windows](https://www.meta.com/help/quest/542427545314119/)). Nothing read
  says whether a window, visible or minimized, keeps its network connections or what it may cost in
  battery; a hidden window's process is likely ended under memory pressure, as on Android
  (inference).
- **From inside the game.** A hybrid app's 2D activity with `OVERLAY_LAUNCHER` is what opens when
  the app is launched from inside another immersive app (above). Going to Home ends the immersive
  app that was running (the coordinator's research; unverified here).
- **Prohibited**, rechecked: `SYSTEM_ALERT_WINDOW`, `STATUS_BAR`, `BIND_APPWIDGET`,
  `BIND_NOTIFICATION_LISTENER_SERVICE` and `START_FOREGROUND_SERVICES_FROM_BACKGROUND`
  ([prohibited permissions](https://developers.meta.com/horizon/resources/permissions-prohibited/),
  2025-04-30). `FOREGROUND_SERVICE` itself is not on that list.

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
  lifetime when hidden and store review are unverified. A spike builds it, with a device
  notification, in development builds only (the glance, 2026-10-02; XR_DEVELOPMENT.md, "The glance
  on a Quest (spike)"); nothing of it has run on a Quest yet.
- The glance reads the control plane over a plain socket, which Meta's network security
  configuration does not bind: Android honors the cleartext flag "on a best effort basis", and
  AOSP's `NetworkSecurityPolicy.isCleartextTrafficPermitted` documentation says there is no
  expectation that the Socket API honors it (read 2026-10-02). Its development builds therefore add
  no cleartext exception.
- Notifications are not a channel for the competition build. Dashboard notifications need Meta's
  servers and the app's credentials, so they stay out unless the owner approves a hosted service.
- The stage ignores reference space changes that move nothing, and moves with a tracking space
  that does, so focus flapping under system windows no longer moves the characters.
- Returning focus with hands is the person's gesture, documented in the milestone 3 checks of
  [XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md); the app can only show its state.

## To check on the headset

1. With Halcyonic live, open Virtual Display and connect the Mac: are the screens and the
   characters both visible, and does the client stay connected? (Second session: both visible,
   the screens in front of the arc.)
2. Run `pnpm demo` while the owner types on the Mac: is "Needs you" noticed at the side?
3. Launch another immersive app: what happens to Halcyonic's process and to open windows? Is
   Halcyonic suspended or ended?
4. With the screens open, point between them and the characters: does the stage stay put, and does
   the log show the reference space changes as kept?
5. Unfocused, and again after minimizing the screens: does pointing at Halcyonic's content and
   pinching return focus, or only the palm gesture and the universal menu?
6. Does a recenter with the palm gesture come with a focus change?
