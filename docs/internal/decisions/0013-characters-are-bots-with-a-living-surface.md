# ADR 0013: Characters are bots whose eyes, motion and light carry state

- Status: Accepted (by the owner, 2026-09-29)
- Date: 2026-09-29

## Context

A workstream's compressed form is a small character that shows at a glance that work exists,
whether it is active, whether it needs its person, and whether it finished or failed
([PRODUCT.md](../product/PRODUCT.md)). State is never conveyed by color alone. The first headset
run used placeholder spheres whose color and bobbing carried state
([quest-3-device.md](../validation/quest-3-device.md)).

On 2026-09-29 the owner compared two directions in a lookbook, a single page that drew each
direction in all eight states and six at once in the headset's arc. It stays outside the
repository. Both directions were drawn from scratch; public bot avatars and an agent orb study
inspired them, and nothing was copied.

- **Bots:** a glossy, rounded body with two eyes and no mouth. Shape and hue are the character's
  own; the eyes, the motion and the light carry the state. While it works, its surface flows like
  the orb's.
- **Orbs:** one faceless sphere for every workstream. Color, flow and texture carry the state, the
  way a voice assistant's orb does.

The characters render on a Meta Quest 3 at 72 Hz, six at once, beside system windows that open
within about 2 m of the person ([horizon-os-multitasking.md](../validation/horizon-os-multitasking.md)).

## Decision

The owner chose bots.

- A character is a glossy, rounded body with two eyes and no mouth.
- Identity is shape and hue, derived only from the workstream id (`CharacterIdentity`), so a
  workstream looks the same in every session: one of eight outlines, and one of eight hues in a
  deep or a light tone. The hues run from teal to pink and stay at least 25 degrees from the state
  colors: amber for needs you, red for failed, green for a finished turn.
- State lives in the eyes, motion and light, and the written status stays under every character.
  `CharacterCues` in the client core maps a `CharacterPresentation` to these cues; the Unity layer
  renders them and derives nothing.

| State | Eyes | Body | Light and surface |
| --- | --- | --- | --- |
| Working | Open, down on the task | Hops | The orb's satin flow moves across it |
| Running tests | Scan from side to side | Hovers | A ring sweeps around it |
| Needs you | Wide, on the person | Turns to the person, rises toward their eye level | Warm amber halo |
| Turn finished | Closed | Settles, no celebration | Soft green halo, red when its tests failed |
| Failed | Crossed out | Slumps | Cracks, red halo |
| State unknown | Half open, unfocused | Drifts | Fogged over, grey halo |
| Stopped | Flat | Frozen mid-motion | Dulled |
| Last known (not live) | As they were | Frozen | Ghosted into a halftone of dots |
| Not started, starting | Open; down on the task | Breathes; a quick bob | None |

- A finished turn gets no celebration, because it proves nothing about correctness.
- Everything is code: meshes generated in C#, two shaders, and a noise texture baked at startup. No
  rigging, and no purchased or generated assets, so everything stays under the project's license.

## Alternatives considered

- **Orbs.** Every character has the same shape, so identity would rest on color alone, and state
  on color, flow and texture, against the rule that state is never conveyed by color alone. An orb
  has no gaze, and eyes that turn to the person are the clearest attention cue in a headset. The
  orb's flowing surface survives as the bots' cue for work in progress.
- **Keeping the placeholder spheres.** Color and bobbing speed told the states apart, and nothing
  told six workstreams apart except their titles.
- **Rigged or purchased character art.** Costs rigging work and licensing, and puts assets outside
  the project's license, for cues that procedural shapes and shaders already carry.

## Consequences

- The cues are testable without Unity: no two activities differ only in color, a stale state keeps
  its cues and stops moving, a finished turn never celebrates.
- Identities can repeat. There are 128; among six time-ordered ids, two share a shape and a hue in
  about a quarter of stages, and their titles still tell them apart.
- Characters move all the time while work runs. Whether that stays calm over a long session, in
  the corner of the person's eye while they code, is open
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- Custom shaders must ship through materials that the build includes
  ([XR_CLIENT.md](../architecture/XR_CLIENT.md)).
- Revisit if hardware sessions show that the characters are decoration, a kill condition, or that
  identities do not help people tell workstreams apart.
