import Type, { type Static } from 'typebox';
import { Nullable, Text } from './primitives.ts';

const strict = { additionalProperties: false } as const;

/** One answer an agent offers to a question. Agent text: untrusted. */
export const QuestionOption = Type.Object(
  { label: Text(200), description: Nullable(Text(1000)) },
  strict,
);
export type QuestionOption = Static<typeof QuestionOption>;

/**
 * One question of a request an agent made to the person (ADR 0022), as the runtime's own question
 * surface asked it. Every text in it is the agent's, so untrusted. `key` is the adapter's opaque
 * name for the question, sent back with its answer. `multiple`: several options may be chosen.
 * `free_text`: a typed answer is accepted besides the options. `secret`: the runtime says the
 * answer is secret; Halcyonic journals answers, so it never carries one.
 */
export const QuestionPrompt = Type.Object(
  {
    key: Text(256),
    header: Nullable(Text(200)),
    text: Text(4000),
    options: Type.Array(QuestionOption, { maxItems: 20 }),
    multiple: Type.Boolean(),
    free_text: Type.Boolean(),
    secret: Type.Boolean(),
  },
  strict,
);
export type QuestionPrompt = Static<typeof QuestionPrompt>;

/** The questions of one request, at least one. */
export const QuestionPrompts = Type.Array(QuestionPrompt, { minItems: 1, maxItems: 10 });

/**
 * The person's answer to one question: the labels of the options chosen, and typed text where the
 * question takes it. Every question of a request is answered at once.
 */
export const QuestionAnswer = Type.Object(
  {
    key: Text(256),
    selected: Type.Array(Text(200), { maxItems: 20 }),
    text: Nullable(Text(4000)),
  },
  strict,
);
export type QuestionAnswer = Static<typeof QuestionAnswer>;

/**
 * The most text, in characters, one question carries in all: keys, headers, texts and options.
 * Adapters fit every question to it before reporting it, so the journal and every client hold
 * questions of bounded size, whatever an agent asks.
 */
export const QUESTION_TEXT_LIMIT = 16_000;

/** The most each of a question's texts holds, as {@link QuestionPrompt} and {@link QuestionOption} say. */
const FIELD = { key: 256, header: 200, text: 4000, label: 200, description: 1000 };

/** Limits a question longer than {@link QUESTION_TEXT_LIMIT} is shortened to, which fit it. */
const SHORTENED = { prompts: 4, options: 8, header: 100, text: 1000, label: 100, description: 200 };

const TRUNCATED = ' [truncated]';

/**
 * A question fitted to the contract: each text cut to its field's limit, then, when the whole is
 * longer than {@link QUESTION_TEXT_LIMIT}, shortened further, each cut marked. A question with
 * anything cut cannot be answered, since the person would not see whole what they answer. One
 * within every limit comes back as it was. Adapters report a question whole; the control plane
 * fits it once, after taking the secrets it holds out of it, since a secret cut in two is no
 * longer found.
 */
export function fitQuestion(
  prompts: readonly QuestionPrompt[],
  answerable: boolean,
): { readonly prompts: readonly QuestionPrompt[]; readonly answerable: boolean } {
  let cut = false;
  const fit = (text: string, max: number): string => {
    const fitted = shorten(text, max);
    if (fitted !== text) cut = true;
    return fitted;
  };
  const fitted = prompts.map((prompt) => ({
    ...prompt,
    key: fit(prompt.key, FIELD.key),
    header: prompt.header === null ? null : fit(prompt.header, FIELD.header),
    text: fit(prompt.text, FIELD.text),
    options: prompt.options.map((option) => ({
      label: fit(option.label, FIELD.label),
      description: option.description === null ? null : fit(option.description, FIELD.description),
    })),
  }));
  const each = cut ? fitted : prompts;
  if (questionTextLength(each) <= QUESTION_TEXT_LIMIT)
    return { prompts: each, answerable: answerable && !cut };
  return {
    prompts: each.slice(0, SHORTENED.prompts).map((prompt) => ({
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
