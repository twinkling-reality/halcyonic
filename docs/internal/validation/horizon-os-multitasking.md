# Horizon OS multitasking

- **Question:** Can Halcyonic stay present while the person does something else in the headset:
  codes on their Mac through Meta's Virtual Display, or uses another immersive app?
- **Date:** 2026-09-29.
- **Method:** Meta's developer documentation, help pages and release notes, and press coverage,
  read on the date above. Nothing was run on the headset. Sources are linked with the date each
  page showed.
- **Status:** Documentation only. The headset checks at the end are not done yet.

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

## Consequences

- Halcyonic stays the immersive app, and the characters stay alive while it lacks input focus
  (`ControlPlaneConnection` pauses only on an application pause, never on focus loss).
- Place the characters a little beyond 2 m, so system windows such as Virtual Display's screens do
  not intersect them.
- A 2D companion window for use inside other immersive apps is possible in the same APK, but its
  lifetime when hidden, its cost (a second client in Kotlin or Java) and store review are
  unverified; it is not built.
- Notifications are not a channel for the competition build.

## To check on the headset

1. With Halcyonic live, open Virtual Display and connect the Mac: are the screens and the
   characters both visible, and does the client stay connected?
2. Run `pnpm demo` while the owner types on the Mac: is "Needs you" noticed at the side?
3. Launch another immersive app: what happens to Halcyonic's process and to open windows?
