import Type, { type Static } from 'typebox';
import { ReviewDigest } from './checkpoints.ts';
import { CommandType } from './commands.ts';
import { ErrorInfo, ExecutionId, Nullable, ProjectId, Text, WorkstreamId } from './primitives.ts';

const strict = { additionalProperties: false } as const;

/**
 * What can be done where the person stands (ADR 0060): one list the host holds, read by every screen's
 * buttons, by the assistant's one tool and by the one review card. Nothing here names a screen.
 */

/**
 * How a command is confirmed (R1), declared once beside its policy: done at once; done at once with
 * the command that undoes it offered; or the host's review, then one press that carries its digest.
 */
export const ConfirmKind = Type.Union([
  Type.Literal('none'),
  Type.Literal('undo'),
  Type.Literal('review'),
]);
export type ConfirmKind = Static<typeof ConfirmKind>;

/**
 * What the person still gives for a command: words; one row of a host list; several of one; a yes or
 * no; a whole number; an amount of US dollars to the cent; the answers to a runtime's question, as
 * its prompts ask; or a secret, typed in a field, never said and never passed through a model.
 */
export const NeedKind = Type.Union([
  Type.Literal('text'),
  Type.Literal('choice'),
  Type.Literal('choices'),
  Type.Literal('yes_no'),
  Type.Literal('count'),
  Type.Literal('amount'),
  Type.Literal('answers'),
  Type.Literal('secret'),
]);
export type NeedKind = Static<typeof NeedKind>;

/** The lists the host holds, that a choice is made from and that an entry may open. */
export const ChoiceList = Type.Union([
  Type.Literal('tasks'),
  Type.Literal('versions'),
  Type.Literal('runtimes'),
  Type.Literal('models'),
  Type.Literal('approval_levels'),
  Type.Literal('folders'),
  Type.Literal('pictures'),
  Type.Literal('catalog'),
  Type.Literal('share_ways'),
  Type.Literal('own_check_scripts'),
  Type.Literal('secrets'),
  Type.Literal('queued_instructions'),
  Type.Literal('approvals'),
  Type.Literal('decisions'),
  Type.Literal('questions'),
  /** Where the assistant may run: this computer's model, then each declared hosted model with its key stored. */
  Type.Literal('assistant_models'),
]);
export type ChoiceList = Static<typeof ChoiceList>;

/** One thing a command still needs, by its payload field's name. */
export const HereNeed = Type.Object(
  {
    name: Type.String({ minLength: 1, maxLength: 64, pattern: '^[a-z][a-z_]*$' }),
    kind: NeedKind,
    /** The host's words for it, such as "Instruction" or "Version". */
    label: Text(60),
    required: Type.Boolean(),
    /** For a choice: the list its rows come from. */
    list: Nullable(ChoiceList),
    /** For several choices: how many at most. */
    max: Nullable(Type.Integer({ minimum: 1, maximum: 32 })),
  },
  strict,
);
export type HereNeed = Static<typeof HereNeed>;

/**
 * One entry of what can be done here, in the host's order (the first is the scope's main action): a
 * command, with its words, how it is confirmed, whether the assistant may propose it and what it
 * still needs; or a list the host holds, to open.
 */
export const HereAction = Type.Union([
  Type.Object(
    {
      kind: Type.Literal('command'),
      command_type: CommandType,
      /** The button's words: 1 to 3 words, Title Case. */
      title: Text(40),
      /** One sentence of what it does, in the host's words, read by the assistant and by the card. */
      description: Text(160),
      confirm: ConfirmKind,
      /** The command that undoes it, for a command confirmed with an Undo. */
      undo: Nullable(CommandType),
      sayable: Type.Boolean(),
      needs: Type.Array(HereNeed, { maxItems: 12 }),
    },
    strict,
  ),
  Type.Object(
    { kind: Type.Literal('list'), list: ChoiceList, title: Text(40), description: Text(160) },
    strict,
  ),
]);
export type HereAction = Static<typeof HereAction>;

/** Where the person stands: a task with its current execution, a project, or all of their work. */
export const HereScope = Type.Object(
  {
    project_id: Nullable(ProjectId),
    workstream_id: Nullable(WorkstreamId),
    execution_id: Nullable(ExecutionId),
  },
  strict,
);
export type HereScope = Static<typeof HereScope>;

/** `GET /api/actions`: a scope's ids as query strings, all absent for all work. */
export const HereQuery = Type.Object(
  {
    project_id: Type.Optional(ProjectId),
    workstream_id: Type.Optional(WorkstreamId),
    execution_id: Type.Optional(ExecutionId),
  },
  strict,
);
export type HereQuery = Static<typeof HereQuery>;

/**
 * What can be done here now, as of the journal's `position`: a client reads it again once the
 * journal passes that position. Only what the host would admit here is listed.
 */
export const HereResponse = Type.Object(
  {
    scope: HereScope,
    position: Type.Integer({ minimum: 0 }),
    actions: Type.Array(HereAction, { maxItems: 64 }),
  },
  strict,
);
export type HereResponse = Static<typeof HereResponse>;

/** What kind of thing a row of a list is, from one enum (R3): a reader matches on it, never on words. */
export const ChoiceRowKind = Type.Union([
  Type.Literal('task'),
  Type.Literal('version'),
  Type.Literal('runtime'),
  Type.Literal('model'),
  Type.Literal('approval_level'),
  Type.Literal('folder'),
  Type.Literal('picture'),
  Type.Literal('repository'),
  Type.Literal('share_way'),
  Type.Literal('script'),
  Type.Literal('secret'),
  Type.Literal('instruction'),
  Type.Literal('approval'),
  Type.Literal('decision'),
  Type.Literal('question'),
]);
export type ChoiceRowKind = Static<typeof ChoiceRowKind>;

/**
 * One row of a list the host holds, in its words, by its index: a choice is given back by `index`,
 * never by an id. `theirs` marks a person's or another program's name, said as theirs. `actions`
 * are the commands that take this row as their choice, such as a version's Restore.
 */
export const ChoiceRow = Type.Object(
  {
    index: Type.Integer({ minimum: 0, maximum: 255 }),
    kind: ChoiceRowKind,
    label: Text(200),
    detail: Nullable(Text(400)),
    theirs: Type.Boolean(),
    actions: Type.Array(CommandType, { maxItems: 8 }),
  },
  strict,
);
export type ChoiceRow = Static<typeof ChoiceRow>;

/** `GET /api/actions/choices`: the scope's ids, as `HereQuery`, and the list. */
export const ChoicesQuery = Type.Object(
  {
    project_id: Type.Optional(ProjectId),
    workstream_id: Type.Optional(WorkstreamId),
    execution_id: Type.Optional(ExecutionId),
    list: ChoiceList,
  },
  strict,
);
export type ChoicesQuery = Static<typeof ChoicesQuery>;

/**
 * A list's rows as of `position`, at most 32 of `total`. `digest` names exactly these rows in this
 * order: a choice given back by index carries it, and one whose list has changed since is refused.
 */
export const ChoicesResponse = Type.Object(
  {
    scope: HereScope,
    list: ChoiceList,
    position: Type.Integer({ minimum: 0 }),
    digest: ReviewDigest,
    rows: Type.Array(ChoiceRow, { maxItems: 32 }),
    total: Type.Integer({ minimum: 0 }),
  },
  strict,
);
export type ChoicesResponse = Static<typeof ChoicesResponse>;

/** What kind of thing a review's fact or item is, from one enum (R3). */
export const ReviewKind = Type.Union([
  Type.Literal('file_added'),
  Type.Literal('file_changed'),
  Type.Literal('file_removed'),
  Type.Literal('folder'),
  Type.Literal('repository'),
  Type.Literal('branch'),
  Type.Literal('address'),
  Type.Literal('task'),
  Type.Literal('version'),
  Type.Literal('script'),
  Type.Literal('words'),
  Type.Literal('unknown'),
]);
export type ReviewKind = Static<typeof ReviewKind>;

/** One fact of a review: a repository, a branch, a folder, an address. `theirs` marks a name not the product's. */
export const ActionReviewFact = Type.Object(
  { kind: ReviewKind, value: Text(400), theirs: Type.Boolean() },
  strict,
);
export type ActionReviewFact = Static<typeof ActionReviewFact>;

/** One thing a reviewed command touches, in the order to read: a file and how it changes, a task. */
export const ActionReviewItem = Type.Object(
  { kind: ReviewKind, name: Text(400), note: Nullable(Text(200)), theirs: Type.Boolean() },
  strict,
);
export type ActionReviewItem = Static<typeof ActionReviewItem>;

/** Words given for one field that go out with the command, by the host's label for the field. */
export const ActionReviewSaid = Type.Object({ label: Text(60), words: Text(32000) }, strict);
export type ActionReviewSaid = Static<typeof ActionReviewSaid>;

/** A line the person should read before the press, by its code, in the host's words. */
export const ActionReviewWarning = Type.Object(
  { code: Type.String({ pattern: '^[a-z][a-z_]*$', maxLength: 64 }), line: Text(200) },
  strict,
);
export type ActionReviewWarning = Static<typeof ActionReviewWarning>;

/**
 * The host's review of one command before its one press (ADR 0051 made general): what will happen in
 * the host's words, at most four facts, what it touches (clipped, `items_total` in all) and its
 * warnings. `digest` is what the press carries back; the host computes it again from what is true
 * when the command would act, and a difference means what was shown has changed.
 */
export const ActionReview = Type.Object(
  {
    command_type: CommandType,
    digest: ReviewDigest,
    /** The one button's words, the host's, such as "Add 3 Files". */
    confirm_words: Text(40),
    /** One plain line of what will happen, the host's words. */
    what_happens: Text(200),
    facts: Type.Array(ActionReviewFact, { maxItems: 4 }),
    items: Type.Array(ActionReviewItem, { maxItems: 200 }),
    items_total: Type.Integer({ minimum: 0 }),
    warnings: Type.Array(ActionReviewWarning, { maxItems: 8 }),
    /**
     * The words the person or the assistant gave that go out with the command, each by the host's
     * label for its field and exactly as it will be sent, in the payload's order: shown whole, as
     * theirs. Ids and the host's own fills are never here.
     */
    said: Type.Array(ActionReviewSaid, { maxItems: 12 }),
  },
  strict,
);
export type ActionReview = Static<typeof ActionReview>;

/**
 * Taking a project off the list's review (ADR 0063), read before its one press: the card, or why it cannot
 * be taken off now, in words, such as a task still at work.
 */
export const RemovalReviewResponse = Type.Union([
  Type.Object({ availability: Type.Literal('available'), review: ActionReview }, strict),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
]);
export type RemovalReviewResponse = Static<typeof RemovalReviewResponse>;
