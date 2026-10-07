# ADR 0027: The headset moves only to answer the person or to show a wait

- Status: Proposed
- Date: 2026-10-04, amended 2026-10-07 (Hold to talk drawn from the voice in every column; a wait's words in the secondary tone)

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

References, checked against the current pages on 2026-10-07
([headset-motion-guidance.md](../validation/headset-motion-guidance.md)):

- Meta's Horizon OS design guidance: content anchored in space rather than following the person,
  little motion while the person is immersed, large objects revealed with a fade, and on every press a
  visual change (a highlight or a compressing movement) with a sound. Meta gives no timings.
- Apple's Human Interface Guidelines on motion: motion only with a purpose, never the only way to say
  something, feedback brief and precise, none at the edges of the field of view, nothing large carried
  across it. Under Reduce Motion, ongoing motion stops, and motion that carries status becomes a
  dissolve, a highlight fade or a colour shift. Apple says nothing about easing or bouncing.
- Destiny 2, from ADR 0026's research (an observation, not a document): a held prompt fills as it is
  held, so the person sees the hold take before it acts; waits show a quiet, steady sign of progress,
  never a blank.

What this ADR adds of its own, beyond those pages: nothing overshoots or bounces, a press answers at
once, and the token values.

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
   The director passes it to the plane, which draws the held prompt of the column that held voiced
   (`Footer.Voiced`, `Prompt.Voiced`), so every column's Hold to talk looks the same, New project's
   included, and no column draws a state of its own:
   - pressed: the lit treatment every held prompt has from its press;
   - listening: "Listening", its cap filled and its words drawn in the active tone, its microphone
     growing and shrinking along a sine (`ListeningPulseSeconds`, `ListeningPulseDepth`);
   - writing down: the transcribe icon (speech becoming text) and "Writing down", its words shimmering.

   It is laid at the widest of its three words, so it never changes width under the hand, and the
   voice's own lines ("Listening. Let go when you're done.") add no line to any page.
5. **A wait's words in the secondary tone.** The shimmer lifts the words toward white, which would not
   show on near-white words such as Hold to talk's. A prompt that waits draws its words in the
   secondary tone, so the lift reads the same on "Writing down" as on "Sent…" (decided by the
   coordinator, 2026-10-05).
6. **Reduced motion.** "Keep badges still" becomes the one reduced-motion setting. It stops every loop
   (the breath, the turning, the pulse and the shimmer), and the state each shows stays by colour,
   icon and words alone. Presses and slides keep their feedback, which the person caused.
7. **Cost.** Every motion runs without allocating per frame, changes colours or a size on meshes
   already built, and adds no draw call: the shimmer recolours the words' own vertices, and the pulse
   scales the icon's own mesh.

## Verification

`GlazeRender` draws two strips and fails on any of these:

- `gallery-motion.png`: Sent… and Hold to talk writing down a third, a half and four fifths into the
  shimmer's sweep, then under Keep badges still. The words change between two points of the sweep,
  the band lifts them by at least 30 of 255, they return to their own colours once the prompt no
  longer waits, and every letter keeps its own colour under Keep badges still.
- `gallery-listening.png`: Hold to talk listening at three points of its pulse, then under Keep badges
  still. It reads "Listening" in the active tone, its microphone grows by the pulse's depth at the top
  of the pulse, stands at its own size under Keep badges still and once the voice is idle.
- Sixty frames of the shimmer, and of the pulse, allocate nothing, and a waiting footer draws as many
  renderers as the same prompts not waiting.

The workspace render drives the director's voice on a file's question: held, let go, the words come,
then held and dropped; Hold to talk shows each stage, keeps its width and place, and the page never
moves.

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
