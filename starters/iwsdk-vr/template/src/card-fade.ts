/**
 * How the welcome card comes and goes (VRIFY, 2026-11-16): by game time only, never a timer or a press, so a checked
 * page draws the same pixels at the same moments in every load. It fades in over its first FADE_IN seconds, holds,
 * and fades out over FADE_OUT seconds ending CARD_SECONDS after it was shown; never a cut. The first card waits
 * CARD_FROM seconds, so it never stands on the black a place opens from.
 */

/** Seconds of game time the card's words stay before they have gone, so the game is in view. */
export const CARD_SECONDS = 6;
export const FADE_IN = 0.4;
export const FADE_OUT = 0.8;

/**
 * Seconds of game time the first card waits before it fades in: the kit's places open from black, a veil lifting over
 * their first second (0.1 to 0.9 s), and the card comes in only once the place is in view (AUDIT, 2026-11-16).
 */
export const CARD_FROM = 1;

/**
 * The card's opacity at game time `time` when it was shown at `shownAt` and waits `from` seconds: 0 before, 1 while
 * held, 0 from CARD_SECONDS after `shownAt` on, so a wait shortens the hold and never moves the end.
 */
export function cardOpacity(time: number, shownAt: number, from = 0): number {
  const since = time - shownAt - from;
  const length = CARD_SECONDS - from;
  if (!Number.isFinite(since) || since < 0) return 0;
  if (since < FADE_IN) return since / FADE_IN;
  if (since < length - FADE_OUT) return 1;
  if (since < length) return (length - since) / FADE_OUT;
  return 0;
}
