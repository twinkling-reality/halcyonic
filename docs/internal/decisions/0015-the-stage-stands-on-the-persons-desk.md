# ADR 0015: The stage stands on the person's desk, found with MRUK and kept with a spatial anchor

- Status: Accepted on 2026-09-29, a decision the owner delegated; the headset checks in
  [mixed-reality-room.md](../validation/mixed-reality-room.md) are still to run.
- Date: 2026-09-29

## Context

Halcyonic's idea for mixed reality is that a person's agents live in their real workspace: the
characters stand on the desk in front of the seated person and are still there the next time. The
competition's judging asks for passthrough that changes the experience, for correct use of scene
understanding through the MR Utility Kit (MRUK), for content anchored to real surfaces, for
spatial anchors, and for entries that hold up in rooms their developer never tested; it asks too
for a seated experience within about two feet, hands first, at 60 fps or more.

What was verified on 2026-09-29, in Meta's documentation and the v207 SDKs' source
([mixed-reality-room.md](../validation/mixed-reality-room.md)):

- The owner approved adding MRUK 207.0.0 on 2026-09-29. Its runtime reads the Immersive
  Debugger's settings in `MRUK.Awake`, so a release build that leaves the debugger's runtime out
  fails to link ([horizon-store-release.md](../validation/horizon-store-release.md)).
- Reading the room needs the spatial data permission, which the person grants at runtime after the
  system explains it; without it the room reads as empty, with no error. Spatial anchors need only
  an install-time permission.
- MRUK loads the room on startup, starts space setup when no room is found, and moves the camera
  rig's tracking space to lock the world, unless told otherwise.
- A saved spatial anchor localizes only where the headset recognizes its surroundings, so one saved
  in another room is simply not found there.
- On a Quest 3, with Virtual Display's windows open over the app, the session's focus flapped many
  times a second and the runtime reported a reference space change each time, although nothing
  moved (headset finding reported by the orchestrator on 2026-09-29).
- The stage already reads a placement through `IStagePlacementSource`: the point on a surface
  where the middle of the lineup stands; it draws a 60 degree arc around the person's side of that
  point at their distance, scales the characters with it, and moves only when the source says so.

## Decision

- **The real room is the default** wherever passthrough works. The person can switch to a virtual
  space, and the choice is kept on the device. Where passthrough is unavailable, the virtual space
  shows by itself.
- **MRUK reads the room**, created in code with loading on startup off, world lock off, and space
  setup started only when the person asks. The room is the one around the person's eyes.
- **The client core chooses the spot** (`StageSurfaces`): a desk or table top first, then other
  furniture tops, 0.4 to 0.9 m from the eyes, 0.15 to 1.0 m below them, within 45 degrees of where
  the person faces, with the whole lineup on the surface, clear of objects standing on it, and as
  near as it fits to 0.55 m straight ahead without making the person look steeply down.
- **A spatial anchor keeps the spot**, saved on the headset, one per room for the eight rooms used
  last. At launch the room's anchor is restored if it localizes and still suits where the person
  sits; otherwise the spot is chosen again and the new anchor replaces the old one, which is
  erased. Without room access, the most recent anchor is tried.
- **Only the anchor's tracked pose moves the stage**: after it moves more than 2 cm or 2 degrees
  and holds still for half a second with input focus, as after a recenter. Reference space events
  never move it. Untracked for five seconds with focus, the stage returns in front of the person
  until the anchor is tracked again.
- **Asking for room access**: once, after a one-line explanation, when the person first reaches the
  real room; a person who declines is not asked again unless they choose to.
- **Every failure leaves the stage in front of the person** (no passthrough, no access, no room set
  up, the person outside their rooms, no surface that fits, no anchor), and one line says why.
  Space setup is offered where it would help, never started unasked.
- **Two small controls**, the workspace's buttons, so they are pointed at and pinched or poked
  like every other: the switch between the spaces, and the offer to allow access or set up the
  room. Nothing needs them to start.
- **Release builds ship the Immersive Debugger's runtime**, disabled by the committed settings;
  its dev agent and the agent bridge stay out.

## Alternatives considered

- **MRUK's world lock instead of an anchor of our own.** It moves the camera rig's tracking space
  every frame, under everything else in the scene, and keeps nothing between sessions.
- **The table's own scene anchor.** Loading it needs room access, and whether it keeps its identity
  through a new space setup was not verified; a spatial anchor of the app's own restores without
  room access, as the brief asked.
- **The Core SDK's scene API without MRUK.** No new package and no debugger in release builds, but
  it rebuilds the room queries MRUK provides, and the judging names MRUK.
- **Asking for access at startup**, through OVRManager or MRUK's loading on startup: the system's
  dialog would come before anything is shown and without the app's reason.
- **Starting space setup whenever no room is found**, MRUK's default: it forces a long system flow
  on anyone who has not set up their room, judges included.
- **Any horizontal plane, the floor included.** The floor is too far below a seated person's eyes,
  and a couch's box top is its backrest, not a seat.
- **Passthrough only where the system recommends it** (the person uses passthrough in Home): the
  product makes the real room the default.
- **Following the reference space events for a placement without an anchor.** They fire in bursts
  during focus changes that move nothing; such a placement stays where it was put.
- **Custom fingertip detection for the controls.** A second interaction model beside the
  Interaction SDK's; the controls use the workspace's buttons instead.

## Consequences

- The characters stand within about a meter of the person, inside the two meters within which
  system windows such as Virtual Display's render ([horizon-os-multitasking.md](../validation/horizon-os-multitasking.md)),
  which the stage's default placement stays beyond. Whether those windows cover the characters on
  the desk is open ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- The app declares `com.oculus.permission.USE_SCENE`, `com.oculus.permission.USE_ANCHOR_API` and
  passthrough as a feature it supports but does not require.
- Release builds carry the disabled Immersive Debugger runtime, whose startup hooks keep up to 200
  recent log lines in memory; `QuestBuild` no longer leaves it out.
- The anchors live on the headset; the app erases the ones it no longer uses.
- The engine-free choices are tested on .NET; everything at runtime is verified only on a headset.
- Revisit if MRUK stops needing the debugger, if judges' rooms often end in front of the person,
  if system windows cover the desk, or if passthrough costs the frame rate.
