/**
 * How the welcome card comes and goes (VRIFY, 2026-11-16): by game time only, never a timer or a press, so a checked
 * page draws the same pixels at the same moments in every load. It fades in over its first FADE_IN seconds, holds,
 * and fades out over FADE_OUT seconds ending CARD_SECONDS after it was shown; never a cut.
 */

/** Seconds of game time the card's words stay before they have gone, so the game is in view. */
export const CARD_SECONDS = 6;
export const FADE_IN = 0.4;
export const FADE_OUT = 0.8;

/** The card's opacity at game time `time` when it was shown at `shownAt`: 0 before, 1 while held, 0 from CARD_SECONDS on. */
export function cardOpacity(time: number, shownAt: number): number {
  const since = time - shownAt;
  if (!Number.isFinite(since) || since < 0) return 0;
  if (since < FADE_IN) return since / FADE_IN;
  if (since < CARD_SECONDS - FADE_OUT) return 1;
  if (since < CARD_SECONDS) return (CARD_SECONDS - since) / FADE_OUT;
  return 0;
}
