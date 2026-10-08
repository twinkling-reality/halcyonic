# ADR 0027: The headset moves only to answer the person or to show a wait

- Status: Accepted on 2026-10-07 by the owner; amended by the owner's picks on 2026-10-08.
- Date: 2026-10-04, amended 2026-10-07 (Hold to talk drawn from the voice in every column; a wait's words in the secondary tone) and 2026-10-08 (a file assembling out of its character, the characters' own movement, seven more tokens, and Keep things still with its still highlight; then the characters' looks and working poses)

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

On 2026-10-08 the owner picked lane V's proposal "One kit, four forms" (outside the repository). Under
this ADR's rule it fills in how a file opens out of its character, gives the characters motion of
their own ([ADR 0013](0013-characters-are-bots-with-a-living-surface.md)), and settles the
reduced-motion setting's name and what a wait shows under it. Its references, for qualities only
([headset-redesign-research.md](../validation/headset-redesign-research.md)): Iron Man 3's helmet
display, light with real depth; Star Citizen's card interface, which builds up around the player; and
a reel of characters that fly with soft trails. The order of an opening, its timings, the travel
speed and the turn on confirmation are lane V's own.

## Decision

1. **One set of motion tokens, in `Glaze` (client core).** Durations: press, release, state, appear,
   leave and slide. Two waits: the shimmer's sweep (`ShimmerSeconds`) and the listening pulse
   (`ListeningPulseSeconds`). One easing per kind, as functions every view uses:
   - `EaseOut` for what arrives;
   - `EaseIn` for what leaves;
   - `EaseInOut` for what moves between two places;
   - loops follow a sine, which never jerks.

   Seven more, for files and characters (2026-10-08), each added with the first view that uses it:

   - `StaggerSeconds`, 0.03: between a column's parts as it assembles;
   - `DrawSeconds`, 0.18: a light line or a ring drawing on;
   - `TravelMetersPerSecond`, 0.5 at most: a character going to a place its state names;
   - `SpinSeconds`, 0.6: one turn;
   - `BlinkSeconds`, 0.14: a blink, every 3 to 7 seconds, each character's interval drawn from its
     identity so renders repeat;
   - `FloatSeconds`, 3.2: a working character's float, at most 6 mm above or below its height;
   - `TrailSeconds`, 0.3: the light a travelling character leaves, fading out.

   An opening runs on them, with two shares beside them: `PartsAfterDraw`, 0.55, and
   `UndrawShare`, 0.9.

   - A file opened out of its character draws its light line over Draw, easing out. Its part i (the
     subject, the sections, then the page with its footer) starts PartsAfterDraw × Draw + i ×
     Stagger after the press and fades in over Appear, easing out: 0.359 s for a file's three parts.
   - A column with no character to come from, as the menu from the bar or New project, has no line
     to wait for, so its part i starts at i × Stagger: 0.26 s for three parts.
   - Another task's file, or New project, taking the open file's place assembles again, from its
     own character where it has one.
   - Closing fades everything together over Leave, easing in, and the line draws back over
     UndrawShare × Leave.
   - The menu coming back from stepping aside fades in over Appear as it slides.
   - A place, a section or a page changing, or a side panel showing another row, does not animate;
     its buttons keep their settle before they take a press.

   Nothing overshoots or bounces.
2. **What moves.** Only what answers the person, shows a wait or says a state:
   - presses and holds, at once;
   - the menu, a file and a side panel opening, closing and stepping aside;
   - a file opening out of its character: its light line draws from the character, then its parts
     fade in from top to bottom, a stagger apart, by opacity alone;
   - a state changing;
   - the characters, as [ADR 0013](0013-characters-are-bots-with-a-living-surface.md) says:
     travelling to a place their state names, one turn when the agent confirms what the person sent,
     and their life in place, a breath, a blink and a slow float while working;
   - a new task's character arriving from the bar, and an old one leaving into it;
   - a character looking back when the person looks at it, which answers the person; project-mates
     looking once at one that needs the person or has just joined, and two that changed one file
     turning to each other, each of which says a state; and a working character's two poses, as its
     agent's tool calls start and end (ADR 0013, 2026-10-08);
   - Hold to talk's three states;
   - every wait the person sees, with one shimmer. A wait is a line, or a prompt's words, marked as one.
   The shimmer changes the brightness of the words, never their place.
3. **What never moves.**
   - The plane while the person reads, and its words. A shimmer brightens; it never shifts a letter.
   - Anything that would carry content across the edge of the field.
   - Anything the person did not cause, beyond a wait's shimmer, Waiting for you's breath and the
     characters' state movement (ADR 0013).
   - A character across an open file, or nearer the eyes than the front of the desk.
   - A look at the person that the person didn't ask for, from any character but one that waits for
     them.
   - Anything that suggests work or progress the agent has not reported: nothing floats faster for
     more work, and nothing guesses how long.
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
6. **Reduced motion.** "Keep things still", renamed from Keep badges still (the owner, 2026-10-08), is
   the one reduced-motion setting. It stops every loop (the breath, the turning, the pulse, the
   shimmer, a character's float and blinks), turns travel into a fade out and in, and drops turns and
   trails, the looks between characters and the two working poses; a character looking back keeps its
   eyes and drops its turn, since the person caused it. A wait keeps a still highlight in place of its
   shimmer: its words in the active tone, as
   Apple's reduced-motion criteria allow a colour shift for motion that carries status. Every state
   still shows by place, eyes, ring, colour, icon and words. Presses, slides and a file assembling
   keep their feedback, which the person caused.
7. **Cost.** Every motion runs without allocating per frame, changes colours or a size on meshes
   already built, and adds no draw call: the shimmer recolours the words' own vertices, and the pulse
   scales the icon's own mesh. A character's travel and turn move transforms only, and its blinks
   and lids change a size or a colour on its eyes. A trail is the one exception: one ribbon a
   travelling character, at most two at once, and a third traveller goes without. A ring is one quad
   and replaces the halo's larger one. Looks and poses move transforms and redraw eyes already built; a
   shared file's light line is one more draw call while it shows.

## Verification

`GlazeRender` draws two strips and fails on any of these:

- `gallery-motion.png`: Sent… and Hold to talk writing down a third, a half and four fifths into the
  shimmer's sweep, then under Keep things still. The words change between two points of the sweep,
  the band lifts them by at least 30 of 255, and they return to their own colours once the prompt no
  longer waits. Under Keep things still every letter of the waiting words stands in the active tone
  at every point of the sweep, and returns to its own colour once the prompt no longer waits.
- `gallery-listening.png`: Hold to talk listening at three points of its pulse, then under Keep things
  still. It reads "Listening" in the active tone, its microphone grows by the pulse's depth at the top
  of the pulse, stands at its own size under Keep things still and once the voice is idle.
- Sixty frames of the shimmer, and of the pulse, allocate nothing, and a waiting footer draws as many
  renderers as the same prompts not waiting.

The workspace render drives the director's voice on a file's question: held, let go, the words come,
then held and dropped; Hold to talk shows each stage, keeps its width and place, and the page never
moves.

Added with the 2026-10-08 amendment, each with the view that brings it:

- An opening strip: a file at points through its opening, its line drawn first and its parts arriving
  top to bottom by opacity alone, none moving; a column with no character, the same without a line.
- A stage strip: a character travelling at a quarter, half and the end of its path, and under Keep
  things still no position change, only a fade; a turn only after the render's journal records the
  agent's confirmation, and none on a command only sent.
- Sixty frames of a stage with every character moving allocate nothing.
- A look strip: a character before, during and after the person's look; the states that don't answer
  unchanged; and over a scripted burst of tool calls, each working pose held at least 1.5 s.

## Alternatives considered

- **Per-component timings, as before.** Each view kept its own numbers, and the owner saw too little
  motion, and none of it shared. A central set keeps one rhythm and one off switch.
- **A spinner or a progress bar for waits.** A spinner draws the eye to the periphery and adds a draw
  call; a bar claims a length of time nothing here can know. The shimmer stays on the words of the wait
  itself.
- **Animating the plane on open (scale or drift).** Moving large content in view is what Meta's
  comfort guidance warns against; the parts already slide a short way into place.
- **A file fading in whole** (2026-10-08). Quicker, at 0.2 s, but with no sense of where it came
  from; assembling out of its character takes 0.36 s and shows which task it is.
- **Under reduced motion, a wait in its own colour** (2026-10-08). The words alone say it waits, and
  nothing shows that something is under way.
- **Less motion, as a name** (2026-10-08). Closer to the system's own words, but the setting stops
  loops entirely rather than lessening them.

## Consequences

- New motion must use these tokens and say which of the moving kinds it is; lane V refines the values
  against the headset.
- Columns mark their waits (`PageLine.Waits`) rather than animating them, so a new wait needs no view
  code.
- "Keep badges still" stopped more than badges, so on 2026-10-08 it was renamed Keep things still, and
  Decision 6 answers the open question on a still highlight.
- As amended on 2026-10-08, the opening, the characters' movement and the still highlight are being
  built by lane U, one step at a time; none has been on a headset.
- To revisit if, on the headset, the shimmer reads as noise beside the agent's own words, the pulse
  draws the eye away from the page, opening in 0.36 s feels slow, travel at 0.5 m/s toward a seated
  person startles, or blinking in the corner of the eye distracts.
