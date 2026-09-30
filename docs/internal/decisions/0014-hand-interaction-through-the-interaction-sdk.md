# ADR 0014: Open work in place with hands, through Meta's Interaction SDK

- Status: Accepted
- Date: 2026-09-29

## Context

Milestone 3 is the defining interaction ([PRODUCT.md](../product/PRODUCT.md)): open a character
into its workspace, understand and act, collapse it back, with hands only. After trying the stage
on a Quest 3, the owner set three levels of detail, all in place: ambient characters; a one-line
peek while a hand ray points at one, without a pinch; and a pinch that opens the workspace beside
that character, never in a separate scene. The competition then asked for gaze interactions with
the Interaction SDK v207, a layout that suits a narrower field of view, hands first throughout, a
first five minutes without a wall of text, and everything within about two feet of a seated
person.

The pinned manifest already has the Meta Interaction SDK 207 (`com.meta.xr.sdk.interaction` and
`.ovr`), and Stage.unity had Meta's camera rig and hand tracking building blocks but no
interactors. What the SDK, TextMeshPro and the system keyboard provide, and how they behave in a
batch build, was verified on 2026-09-29 ([workspace-interaction.md](../validation/workspace-interaction.md)).
The Meta XR Simulator renders nothing on the development Mac, so the only runtime test is the
headset.

## Decision

- **Input.** Hands point with the Interaction SDK's hand ray and select with a pinch, or poke when
  near. The rig is Meta's comprehensive interaction rig, added to Stage.unity the way the SDK's
  "Interactions Rig" building block adds it, by an idempotent editor step (`StageSetup`). The
  rig's locomotion and its locomotor are deactivated, because the stage is stationary, and
  `FocusGuard` deactivates its interactors and hides its hands when the app loses focus.
- **Targets.** The workspace builds its interactables in code at runtime with the SDK's inject
  methods: a ray and gaze sphere and a person-facing poke plane on each character the stage
  creates, and ray and poke planes for each button. Characters are not changed; `CharacterStage`
  only announces them and looks them up.
- **Gaze.** The SDK's gaze (its eye gaze with camera pose emulation, which is head gaze on a
  Quest 3, and a gaze conecaster) drives a gaze interactor that only hovers: the gaze peeks,
  selection stays with the hand ray's pinch and the poke.
- **Levels of detail.** Ambient, peek and open, all in place, as the owner set them. The open
  workspace is a world-space panel next to its character in view, grown out of it and linked to it
  while open: 0.6 m from the eyes, within a seated person's reach, at about 34 by 27 degrees, near
  the middle of the view.
- **First time.** A pinch cue with three words above the character that needs the person, until the
  first open.
- **Words and decisions in the client core.** Everything shown is written by `WorkspaceText`;
  presses become commands in `WorkspaceSteering`; `CommandSubmissions` describes a command this
  client sent until the control plane's record of it takes over. A command the policy marks for
  review needs a second press on a separate button that names what will be sent, and that
  confirmation lapses after 15 seconds or when the state it asked about changes.
- **Text.** TextMeshPro with Liberation Sans SDF, never parsing markup. Its essential resources are
  committed, without the EmojiOne sprites and the HDRP and URP shader graphs.
- **Instructions.** Typed on the Quest system keyboard through `TouchScreenKeyboard`, with Require
  System Keyboard on. Three preset instructions stand in only where no keyboard is supported.

## Alternatives considered

- **Unity's XR Interaction Toolkit.** Not in the manifest, a new download, and Meta's hand data and
  hand visuals come with the Interaction SDK already present.
- **A minimal hand-built rig** (hand data, ray and poke prefabs, wired by hand). Fewer objects, but
  its wiring is what Meta's wizard and building block exist to get right, and the comprehensive
  rig's best-hover group already keeps a poke and a ray from acting together.
- **Meta's comprehensive rig with locomotion left on.** Hand microgestures and controller sticks
  would turn or move the person away from the stage, and the locomotor's tunneling darkens the view
  when the head meets a collider, such as a character's.
- **A uGUI world-space canvas with the SDK's pointable canvas module.** An event system and input
  module on top, with this project's Input System only setting; plain interactables on planes are
  fewer parts for a handful of buttons.
- **Detecting pinches directly from hand tracking.** It would re-implement rays, pokes and their
  arbitration, which the SDK already provides and Meta tests.
- **Unity's legacy TextMesh.** Bitmap glyphs that blur at a distance, and no wrapping.
- **Opening the workspace in a separate view or scene.** Ruled out by the owner: the same work must
  read as the same object at a different level of detail.
- **Opening the workspace at the character's distance, beside it.** Out of reach for a poke at 1.6
  to 2.4 m, and at the edge of the view for a character at the end of the arc.
- **Look and pinch to open (the SDK's hand gaze interactor).** A second way to select that would
  compete with the hand ray's pinch for the same gesture; left for a headset trial.
- **Preset instructions only.** Always usable, but cannot say anything specific.

## Consequences

- Stage.unity depends on Meta's rig prefab by GUID. A newer SDK that renames what `StageSetup`
  adjusts makes it stop with the rig's hierarchy in the log, instead of saving a half-set-up scene.
- The development APK grows from about 65 MB to 81.5 MB.
- The repository carries TextMeshPro's essential resources: Unity's shaders and settings, and
  Liberation Sans under the SIL Open Font License, whose text is included.
- Runtime behavior is verified only on the headset: rays, pinches, pokes and gaze on these
  targets, the system keyboard under OpenXR and its focus change, `HttpClient` under IL2CPP for
  history, and legibility. Revisit the input choice if the rig costs too much frame time, and the keyboard
  choice if the system keyboard is unusable with hands.
