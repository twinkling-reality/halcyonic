# ADR 0027: The headset moves only to answer the person or to show a wait

- Status: Proposed
- Date: 2026-10-04

## Context

In the sixth headset session (2026-10-04, main ee1acdb9) the owner said "nothing moves". Holding Hold to
talk changed neither its colour nor its icon. A wait, such as the voice writing down what was said,
showed only its words, with no sign that anything was under way. The owner asked for "centralized sleek
motions/animations everywhere where possible", rich and seamless.

The interface already moves in a few places, each with its own timing:

- a press flashes its target (`Glaze.PressSeconds`, `Glaze.ReleaseSeconds`);
- the menu's parts slide to their places (`MenuPlane.SlideSeconds`);
- a badge cross-fades between states (`Glaze.StateSeconds`);
- Waiting for you breathes (`Glaze.AttentionBreathSeconds`);
- busy icons turn (`Glaze.BusyTurnSeconds`).

Settings' "Keep badges still" (`Comfort.Still`) stops the breath and the turning.

References, from public guidance as summarised in ADR 0026's research. They are to be checked against the
current pages before this is accepted.

- Meta's Horizon OS guidance on comfort: nothing the person did not cause should move large content in
  their view, since unexpected motion causes discomfort; feedback should come at once on every press.
- Apple's visionOS guidance on motion: motion is brief, eases rather than bounces, explains a change of
  state, and gives way to the system's Reduce Motion setting.
- Destiny 2, from ADR 0026's research: a held prompt fills as it is held, so the person sees the hold
  take before it acts; waits show a quiet, steady sign of progress, never a blank.

## Decision

1. **One set of motion tokens, in `Glaze` (client core).** Durations: press, release, state, appear,
   leave and slide. Two waits: the shimmer's sweep (`ShimmerSeconds`) and the listening pulse
   (`ListeningPulseSeconds`). One easing per kind, as functions every view uses:
   - `EaseOut` for what arrives;
   - `EaseIn` for what leaves;
   - `EaseInOut` for what moves between two places;
   - loops follow a sine, which never jerks.

   Nothing overshoots or bounces.
2. **What moves.** Only what answers the person or shows a wait:
   - presses and holds, at once;
   - the menu, a file and a side panel opening, closing and stepping aside;
   - a state changing;
   - Hold to talk's three states;
   - every wait the person sees, with one shimmer. A wait is a line, or a prompt's words, marked as one.
   The shimmer changes the brightness of the words, never their place.
3. **What never moves.**
   - The plane while the person reads, and its words. A shimmer brightens; it never shifts a letter.
   - Anything that would carry content across the edge of the field.
   - Anything the person did not cause, beyond a wait's shimmer and Waiting for you's breath.
4. **Hold to talk shows three states, centrally.** The menu's one voice (`MenuVoice.Stage`) says
   whether it is idle, listening (recording a hold) or writing down (waiting for the computer's words).
   The plane draws the held prompt from that, so every column's Hold to talk looks the same:
   - pressed: the lit treatment every held prompt has from its press;
   - listening: the active tone, its microphone pulsing, "Listening";
   - writing down: the pen icon, its words shimmering, "Writing down".
5. **Reduced motion.** "Keep badges still" becomes the one reduced-motion setting. It stops every loop
   (the breath, the turning, the pulse and the shimmer), and the state each shows stays by colour,
   icon and words alone. Presses and slides keep their feedback, which the person caused.
6. **Cost.** Every motion runs without allocating per frame, changes colours on meshes already built,
   and adds no draw call: the shimmer recolours the words' own vertices.

## Alternatives considered

- **Per-component timings, as before.** Each view kept its own numbers, and the owner saw too little
  motion, and none of it shared. A central set keeps one rhythm and one off switch.
- **A spinner or a progress bar for waits.** A spinner draws the eye to the periphery and adds a draw
  call; a bar claims a length of time nothing here can know. The shimmer stays on the words of the wait
  itself.
- **Animating the plane on open (scale or drift).** Moving large content in view is what Meta's
  comfort guidance warns against; the parts already slide a short way into place.

## Consequences

- New motion must use these tokens and say which of the moving kinds it is; lane V refines the values
  against the headset.
- Columns mark their waits (`PageLine.Waits`) rather than animating them, so a new wait needs no view
  code.
- "Keep badges still" now stops more than badges; its words may need to say so (an open question for
  lane V).
- To revisit if, on the headset, the shimmer reads as noise beside the agent's own words, or the pulse
  draws the eye away from the page.
