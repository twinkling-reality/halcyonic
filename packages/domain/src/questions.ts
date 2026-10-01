import type { QuestionPrompt } from '@halcyonic/contracts';

/**
 * How many of an execution's pending questions its view shows, oldest first. The rest wait their
 * turn: each one still holds the execution waiting and names itself in the attention reasons, and
 * shows once one before it is resolved. Every snapshot and change carries the view whole, so it
 * must stay small whatever an agent asks (ADR 0022).
 */
export const SHOWN_QUESTIONS = 3;

/** The most text, in characters, one question carries to clients: keys, headers, texts and options. */
export const QUESTION_TEXT_LIMIT = 16_000;

/** Limits a question longer than {@link QUESTION_TEXT_LIMIT} is shortened to, which fit it. */
const SHORTENED = { prompts: 4, options: 8, header: 100, text: 1000, label: 100, description: 200 };

const TRUNCATED = ' [truncated]';

/**
 * A question as its view keeps it. One within {@link QUESTION_TEXT_LIMIT} is kept whole; a longer
 * one is shortened, each cut marked, and cannot be answered, since the person would not see whole
 * what they answer.
 */
export function fitQuestion(
  prompts: readonly QuestionPrompt[],
  answerable: boolean,
): { readonly prompts: readonly QuestionPrompt[]; readonly answerable: boolean } {
  if (questionTextLength(prompts) <= QUESTION_TEXT_LIMIT) return { prompts, answerable };
  return {
    prompts: prompts.slice(0, SHORTENED.prompts).map((prompt) => ({
      ...prompt,
      header: prompt.header === null ? null : shorten(prompt.header, SHORTENED.header),
      text: shorten(prompt.text, SHORTENED.text),
      options: prompt.options.slice(0, SHORTENED.options).map((option) => ({
        label: shorten(option.label, SHORTENED.label),
        description:
          option.description === null ? null : shorten(option.description, SHORTENED.description),
      })),
    })),
    answerable: false,
  };
}

/** The characters a question's text takes, counted in code points as the contract counts them. */
export function questionTextLength(prompts: readonly QuestionPrompt[]): number {
  let length = 0;
  for (const prompt of prompts) {
    length += characters(prompt.key) + characters(prompt.header ?? '') + characters(prompt.text);
    for (const option of prompt.options) {
      length += characters(option.label) + characters(option.description ?? '');
    }
  }
  return length;
}

function characters(text: string): number {
  let count = 0;
  for (const _ of text) count += 1;
  return count;
}

function shorten(text: string, max: number): string {
  const all = Array.from(text);
  return all.length <= max ? text : all.slice(0, max - TRUNCATED.length).join('') + TRUNCATED;
}
