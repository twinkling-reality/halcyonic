import type { QuestionView } from '@halcyonic/contracts';

/**
 * How many of an execution's pending questions its view shows. The rest wait their turn: each
 * still holds the execution waiting, and shows once one before it is resolved. Every snapshot and
 * change carries the view whole, so it must stay small whatever an agent asks (ADR 0022).
 */
export const SHOWN_QUESTIONS = 3;

/**
 * The pending questions shown, and the only ones a person can answer or is told about: those
 * Halcyonic can answer first, then the others, each oldest first, so questions that cannot be
 * answered never crowd out one that can.
 */
export function shownQuestions(pending: ReadonlyMap<string, QuestionView>): QuestionView[] {
  const all = [...pending.values()];
  return [
    ...all.filter((question) => question.answerable),
    ...all.filter((question) => !question.answerable),
  ].slice(0, SHOWN_QUESTIONS);
}
