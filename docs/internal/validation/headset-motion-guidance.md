# Headset motion guidance

- **Question:** Do Meta's and Apple's current pages say what ADR 0027 (the headset moves only to
  answer the person or to show a wait) cites them for: nothing the person did not cause moves large
  content in view; every press answers at once; motion is brief, eases rather than bounces, explains a
  change of state and gives way to the system's reduced-motion setting?
- **Date:** 2026-10-07.
- **Method:** Meta's design pages (developers.meta.com/horizon/design), Apple's Human Interface
  Guidelines page on motion (read through its published JSON, since the page itself is drawn by
  script) and App Store Connect's Reduced Motion evaluation criteria, read on the date above. No
  headset. Destiny 2, the ADR's third reference, is a game, not a document: what it does is ADR
  0026's own observation and is not checked here.
- **Status:** Documentation only. Nothing here is verified on a headset.

## Findings

### Meta

- Content anchored in space rather than following the person, or following loosely "using smoothing
  animation"; head-locked content kept to a minimum; "Avoid displaying motion or forcing the user to
  physically move around too much while fully immersed"; a large object revealed gradually with a
  fade ([MR design guidelines](https://developers.meta.com/horizon/design/mr-design-guideline/)).
  Nowhere does Meta say, in these words, that nothing the person did not cause may move; the ADR's
  rule is our reading of these lines (inference).
- Feedback on press: "Provide audio/visual feedback such as compressing movement, or highlighting on
  press accompanied by audio feedback" (MR design guidelines); clear hover, pressed and disabled
  states so people always understand the current state
  ([buttons: best practices](https://developers.meta.com/horizon/design/buttons_bp/), updated
  2026-02-27). Neither page gives a time, so "at once" is the ADR's decision, not Meta's number.
- No general animation timings, as recorded in [headset-ui-guidance.md](headset-ui-guidance.md).

### Apple

- [Motion](https://developer.apple.com/design/human-interface-guidelines/motion) (alert dated
  2025-09-09): add motion purposefully, never for its own sake; make motion optional and never the only
  way to say something; "Aim for brevity and precision in feedback animations"; let people cancel
  motion; in apps, generally avoid adding motion to interactions that happen often. In visionOS, avoid
  motion at the edges of the field of view, help people stay comfortable while large objects move
  (more translucency, less contrast), prefer fades to carrying an object across, never rotate the
  world, and give people a stationary frame of reference.
- The page says nothing about easing, bouncing or overshoot. "Eases rather than bounces" is the
  ADR's own decision, not Apple's guidance.
- [Reduced Motion evaluation criteria](https://developer.apple.com/help/app-store-connect/manage-app-accessibility/reduced-motion-evaluation-criteria/):
  the motion to stop or change under Reduce Motion is depth simulation, multi-axis or multi-speed
  motion, spinning and vortex effects, and ongoing motion. Where motion conveys a status change, Apple
  advises replacing it rather than removing it, with "a dissolve, highlight fade, or color shift".

## What it means for ADR 0027

- **Supported:** motion only with a purpose; brief feedback on presses; nothing large carried across
  the view; a reduced-motion setting that stops ongoing loops (the turning, the breath, the shimmer, the
  pulse), with each state still told by colour, icon and words, as Apple asks of motion that carries
  status.
- **Ours, not theirs:** nothing overshoots or bounces; the exact press and release times; the claim
  that a press answers "at once".
- **To weigh, not yet a change:** Apple names a colour shift as an acceptable reduced form of a status
  animation. The shimmer is a colour change that moves no letter, so Keep badges still could keep a
  still highlight in its place; today it shows the words in their own (secondary) colour. Apple also
  advises against motion on interactions that happen often: the pulse runs only while Hold to talk
  records and the shimmer only while something waits, neither on a press. Both are in
  [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md) for lane V on the headset.
- The footer's prompts sit at the bottom of the plane, inside the field of view; whether the pulse and
  the shimmer read as peripheral motion on a Quest 3 is a headset question.
