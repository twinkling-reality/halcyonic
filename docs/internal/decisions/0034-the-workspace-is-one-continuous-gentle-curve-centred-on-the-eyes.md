# ADR 0034: The workspace is one continuous gentle curve centred on the eyes

- Status: Accepted as direction on Editor renders, 2026-10-17; implementation pending, through the D3 units;
  wearer review still required
- Date: 2026-10-17
- Superseded in part on 2026-10-29 by [ADR 0040](0040-the-headset-is-a-room-of-projects.md): the curve is no longer
  the workspace's main presentation
- Supersedes in part [ADR 0026](0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md): its
  rule 1, one plane facing the eyes, only

## Context

ADR 0026 put every composition on one flat plane, square to the line of sight at its centre, 0.46 m away. On
that plane a word shrinks as it stands farther from the centre: across, by the square root of one plus the
square of its offset, and at a slant. The owner rejected surfaces turned to face the eyes on their own, and
allowed one gentle common curve (ADR 0026, alternatives considered).

A spatial kit for the workspace (a context spine, a reading slate and an evidence rail as one instrument) was
drawn in the editor's render fixture both ways from the same code, on synthetic work, and measured where the
eyes see each point, at the standard and the larger text size. These are Editor renders, not headset captures:

- On one plane, Body text at a side region's far corner is seen at 0.88 degrees tall and 0.81 wide, at the
  14 dp floor; the side regions' far edges are met 31 degrees off square.
- On a curve centred on the eyes, every column faces them: the same corner reads 0.99 degrees tall and
  1.05 wide, the side regions are met square, and the instrument stands 0.46 to 0.50 m from the eyes.
- The curve spreads 68 degrees across at the standard text size against the plane's 61, and asks the head to
  tip 2.6 degrees at the larger size (the overview 6.2), inside the 8 degree limit.
- Squeezed to the plane's spread, the curve's narrower slate could not hold a long report whole at the larger
  text size, so it keeps its full widths.
- Gaps between the regions, on either form, read as separate cards, which the owner rejected.

The owner saw the curved render as a direction check and answered: "Yes, build every state on this curved
direction."

## Decision

- The workspace is one continuous instrument drawn on one gentle common curve: a vertical cylinder whose axis
  runs through the eyes where the workspace was placed, the forward region at 0.46 m. Moving the workspace
  places the curve again; it never follows the head.
- Its regions are joined, not spaced: the regions beside the forward one are recessed bands 10 mm farther
  out on the same curve, reaching under it and shaded where it stands over them. No region has an outline.
  One light runs along the forward region's top, in the state tone of the work it shows.
- Layout stays flat: the plane's layout is mapped onto the curve by one exact mapping (angle is arc length
  over radius, height unchanged, radius less the depth of a recessed band), so every check measures where
  the eyes see each point.
- The plane of ADR 0026 is the documented fallback, used where the curve cannot be drawn or a device check
  fails.
- Everything else in ADR 0026 stands.

## Alternatives considered

- **One plane (ADR 0026's rule).** Kept as the fallback: simplest, and holds every floor within 64 degrees,
  but only at the floor once the regions are spaced, and its sides are read at a slant.
- **Each region turned to face the eyes on its own.** Rejected by the owner (ADR 0026); measured again, it
  spread 70 degrees and its sides swung and slanted against the slate at rest.
- **The curve, or the plane, with gaps between regions.** Rejected: the regions read as separate cards.
- **The curve squeezed to the plane's spread.** Rejected: the slate could not hold a long report whole at the
  larger text size.

## Consequences

- Production needs curved drawing and curved hit-testing: surfaces bent in columns of a degree or less and
  rebuilt when they change, text bent after its lean in one ordered hook, and pointer targets on a cylinder
  surface with hits mapped back to the plane. None of this exists yet.
- Readability on the curve depends only on height, so the band model's worst case improves; field fit
  becomes stricter, since the spread is arc over radius.
- STYLE.md and WORDS.md change with each production unit that moves to the curve, describing what the code
  then does.
- Worth revisiting if a wearer finds the curve uncomfortable, if poke on a concave surface proves unreliable,
  or if frame time on the device cannot carry the bent meshes.

## Note, 2026-10-28: the angle of a recessed band

The mapping's angle is the laid-out x over the forward region's radius, for every band: a recessed band keeps
the angle and stands 10 mm farther out (`PlaneCurve`). An edge laid out at one x then stays on one line from
the eyes on the slate and on the bands beside it, and hits map back by the same inverse. The first renders bent
each band by its own radius instead; on the shared mapping the outer edges stand about 0.7 degrees farther out
(the focused work spreads 69.4 degrees at both text sizes, not 68.0).
