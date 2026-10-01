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
