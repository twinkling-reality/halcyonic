/**
 * How the welcome card comes and goes (VRIFY, 2026-11-16): by game time only, never a timer or a press, so a checked
 * page draws the same pixels at the same moments in every load. It fades in over its first FADE_IN seconds, holds,
 * and fades out over FADE_OUT seconds ending at its end; never a cut. The first card waits CARD_FROM seconds, so it
 * never stands on the black a place opens from, and every card waits for its words' font.
 */

/** Seconds of game time H's card stays on a laptop before it has gone. */
export const CARD_SECONDS = 6;

/**
 * Game time by which the first card has gone (AUDIT, 2026-11-16): before the check's twin loads take their first
 * frame (TWIN_FRAMES[0], 5 s), so no twin frame sees the card, its fade or its absence, whenever its font arrives.
 */
export const FIRST_CARD_END = 4.9;
export const FADE_IN = 0.4;
export const FADE_OUT = 0.8;

/**
 * Seconds of game time the first card waits before it fades in: the kit's places open from black, a veil lifting over
 * their first second (0.1 to 0.9 s), and the card comes in only once the place is in view (AUDIT, 2026-11-16).
 */
export const CARD_FROM = 1;

/**
 * The card's opacity at game time `time` when it was shown at `shownAt`, waits `from` seconds and has gone `end`
 * seconds after `shownAt`: 0 before, 1 while held, 0 from `end` on, so a wait shortens the hold and never moves the end.
 */
export function cardOpacity(time: number, shownAt: number, from = 0, end = CARD_SECONDS): number {
  const since = time - shownAt - from;
  const length = end - from;
  if (!Number.isFinite(since) || since < 0) return 0;
  // Too little time left to fade in, be read and fade out: no card at all, never a faint flash.
  if (length < FADE_IN + MIN_HOLD + FADE_OUT) return 0;
  if (since < FADE_IN) return since / FADE_IN;
  if (since < length - FADE_OUT) return 1;
  if (since < length) return (length - since) / FADE_OUT;
  return 0;
}

/**
 * The shortest hold at full opacity a card is shown for, in seconds of game time: a card whose words are ready too
 * late for it does not show at all (H brings it back), never a blink; its end never moves (AUDIT, 2026-11-16).
 */
export const MIN_HOLD = 1.5;

/**
 * How long the card waits after `shownAt`: at least `from`, and until its words are ready (`readyAt`, the game time
 * every text in it first had its font; null while any is still loading), so it never fades in as an empty box
 * (AUDIT2, 2026-11-16). While the words are not ready it waits for ever: no card.
 */
export function cardWait(shownAt: number, from: number, readyAt: number | null): number {
  return readyAt === null ? Number.POSITIVE_INFINITY : Math.max(from, readyAt - shownAt);
}
