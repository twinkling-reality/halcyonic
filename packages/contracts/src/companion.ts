import Type, { type Static } from 'typebox';
import { ErrorInfo, Text } from './primitives.ts';

const strict = { additionalProperties: false } as const;

/** The most messages one request carries: the person's and the companion's, together (ADR 0025). */
export const COMPANION_MAX_MESSAGES = 20;

/** The most characters of text one request carries, counted over every message. */
export const COMPANION_MAX_CHARACTERS = 24_000;

/** The longest message of the person's. */
export const COMPANION_PERSON_MAX = 2_000;

/** The most questions the companion asks in one exchange; after them it only proposes. */
export const COMPANION_MAX_QUESTIONS = 4;

/** The most choices a question offers; the person can always answer in their own words. */
export const COMPANION_MAX_CHOICES = 4;

/** The companion's line to the person: at most two short sentences. */
export const COMPANION_LINE_MAX = 300;
/**
 * The companion's question: what New project's quote holds in its two rows, so the question is never
 * cut nor takes a third row (ADR 0025, amendment 4). Measured at 18 dp in a file's 36 degrees with
 * "The companion says" around it: 105 characters of ordinary English, 66 of the widest letters.
 */
export const COMPANION_QUESTION_MAX = 100;
export const COMPANION_CHOICE_MAX = 48;
export const COMPANION_NAME_MAX = 60;
export const COMPANION_TASK_MAX = 1_000;

/**
 * The model that answers as the companion, as the host names it: an opaque string, never parsed
 * for meaning. The companion only ever runs on the host, so where it runs is not a choice.
 */
export const CompanionModel = Type.Object(
  { name: Text(200), served: Type.Literal('this_mac') },
  strict,
);
export type CompanionModel = Static<typeof CompanionModel>;

/**
 * The companion's view of the idea, an opinion it reports, never a check: `clear` when it could
 * propose now, `unclear` when something that changes the first step is missing, `not_buildable`
 * when it thinks the idea cannot be built as software. It never stops the person.
 */
export const CompanionView = Type.Union([
  Type.Literal('clear'),
  Type.Literal('unclear'),
  Type.Literal('not_buildable'),
]);
export type CompanionView = Static<typeof CompanionView>;

/** One question, with at most four short choices. */
export const CompanionQuestion = Type.Object(
  {
    text: Text(COMPANION_QUESTION_MAX),
    choices: Type.Array(Text(COMPANION_CHOICE_MAX), { maxItems: COMPANION_MAX_CHOICES }),
  },
  strict,
);
export type CompanionQuestion = Static<typeof CompanionQuestion>;

/**
 * What the companion suggests for the recap: the project's name and its first task, which become
 * the ordinary commands only once the person has read and confirmed them.
 */
export const CompanionProposal = Type.Object(
  { project_name: Text(COMPANION_NAME_MAX), first_task: Text(COMPANION_TASK_MAX) },
  strict,
);
export type CompanionProposal = Static<typeof CompanionProposal>;

/**
 * One reply of the companion's: a line to the person and its view of the idea, then either one
 * question (`ask`) or a proposal for the recap (`propose`). All of it is model text: untrusted,
 * reported, and shown only as the companion's.
 */
export const CompanionReply = Type.Union([
  Type.Object(
    {
      next: Type.Literal('ask'),
      line: Text(COMPANION_LINE_MAX),
      view: CompanionView,
      question: CompanionQuestion,
    },
    strict,
  ),
  Type.Object(
    {
      next: Type.Literal('propose'),
      line: Text(COMPANION_LINE_MAX),
      view: CompanionView,
      proposal: CompanionProposal,
    },
    strict,
  ),
]);
export type CompanionReply = Static<typeof CompanionReply>;

/**
 * One message of the exchange so far, as the headset kept it: the person's words (typed, spoken
 * and confirmed, or a choice they pressed), or a reply the companion gave earlier.
 */
export const CompanionExchangeTurn = Type.Union([
  Type.Object({ from: Type.Literal('person'), text: Text(COMPANION_PERSON_MAX) }, strict),
  Type.Object({ from: Type.Literal('companion'), reply: CompanionReply }, strict),
]);
export type CompanionExchangeTurn = Static<typeof CompanionExchangeTurn>;

/** How Create began: from the person's own idea, or from Help me figure it out. */
export const CompanionStart = Type.Union([Type.Literal('idea'), Type.Literal('help')]);
export type CompanionStart = Static<typeof CompanionStart>;

/** What the person asks for: whatever the companion judges next, or a proposal for the recap now. */
export const CompanionWant = Type.Union([Type.Literal('next'), Type.Literal('proposal')]);
export type CompanionWant = Static<typeof CompanionWant>;

/**
 * Asks the companion for one reply (`POST /api/companion/replies`, ADR 0025). The host keeps no
 * session: the whole exchange so far travels with each request, and nothing of it is journaled,
 * stored or logged. `start` says how Create began: `idea`, from the person's own idea, which is
 * the first message; `help`, from Help me figure it out, where the exchange may be empty and the
 * companion asks first. `want`: `next` for whatever the companion judges next, `proposal` when the
 * person asked for the recap.
 */
export const CompanionRepliesRequest = Type.Object(
  {
    start: CompanionStart,
    want: CompanionWant,
    messages: Type.Array(CompanionExchangeTurn, { maxItems: COMPANION_MAX_MESSAGES }),
  },
  strict,
);
export type CompanionRepliesRequest = Static<typeof CompanionRepliesRequest>;

/**
 * The companion's reply. `provenance` is always `reported`: the reply is the model's claim, never
 * an observed fact, and `companion` names the model that gave it.
 */
export const CompanionReplyResponse = Type.Object(
  {
    reply: CompanionReply,
    provenance: Type.Literal('reported'),
    companion: CompanionModel,
  },
  strict,
);
export type CompanionReplyResponse = Static<typeof CompanionReplyResponse>;

/**
 * Whether the companion can be asked now (`GET /api/companion`), read from the host's model list
 * without asking the model. `unavailable` carries why, by code: `companion_not_set_up`,
 * `companion_not_running`, `companion_model_missing` or `companion_model_not_local`.
 */
export const CompanionStatus = Type.Union([
  Type.Object(
    {
      availability: Type.Literal('available'),
      companion: CompanionModel,
      max_questions: Type.Integer({ minimum: 0, maximum: COMPANION_MAX_QUESTIONS }),
    },
    strict,
  ),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
]);
export type CompanionStatus = Static<typeof CompanionStatus>;
