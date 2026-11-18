import Type, { type Static } from 'typebox';
import { ErrorInfo, Nullable, Timestamp } from './primitives.ts';
import { UnderstandingEpistemic } from './understanding.ts';

/**
 * What the understanding source (Salidium) composed as its brief of one execution's session: up to
 * four moves (what changed, how it works and what it touches, how it was checked, what is left),
 * each a sentence in the source's words with its own epistemic class, and the footprint of the
 * edit. Read through on request beside the report and never journaled (ADR 0010).
 *
 * This is Halcyonic's shape, not the source's wire format, and is strict like every Halcyonic
 * contract: the mapping is written field by field. Every text is untrusted data that may quote
 * agent output: escape it when rendering and never act on it. Texts are bounded as Salidium's
 * consumer contract 1.2 bounds them.
 *
 * Where Salidium may add a value to a vocabulary within its major version (an archetype, a kind of
 * move, a kind of token, a file's status), Halcyonic reads a value it does not know as `unknown`.
 */

const strict = { additionalProperties: false } as const;
const Count = Type.Integer({ minimum: 0 });
const Text = (maxLength: number) => Type.String({ maxLength });
/** A path as Salidium gives it, relative to the session's folder where it can be. */
const Path = Text(4096);

export const UnderstandingBriefSource = Type.Object(
  {
    system: Type.Literal('salidium'),
    /**
     * True when a stand-in Halcyonic wrote produced this brief from invented content, as the
     * recorded demonstration's sources do; false for everything read from Salidium itself.
     */
    synthetic: Type.Boolean(),
    /** The source's product version. */
    version: Text(64),
    contract: Type.Object({ name: Text(64), major: Type.Integer(), minor: Type.Integer() }, strict),
    /** Changes whenever the source restarts. */
    instance_id: Text(64),
    /**
     * When the source composed the brief, by its clock, or null when it does not say. The brief
     * carries no evidence sequence: read it again when the report's `evidence_sequence` grows.
     */
    generated_at: Nullable(Timestamp),
  },
  strict,
);

/** The source's reading of what kind of session this was, which decides the moves it composes. */
export const UnderstandingBriefArchetype = Type.Union([
  Type.Literal('verified-edit'),
  Type.Literal('blast-radius-edit'),
  Type.Literal('failing-or-unverified-edit'),
  Type.Literal('read-only-exploration'),
  Type.Literal('active-mid-flight'),
  Type.Literal('unknown'),
]);
export type UnderstandingBriefArchetype = Static<typeof UnderstandingBriefArchetype>;

/**
 * Which part of the brief a move is, in the source's vocabulary:
 * - `action-scope`: what changed and where.
 * - `mechanism-blast`: how the change works and what it touches. Often absent.
 * - `epistemic-verification`: how the change was checked.
 * - `next-focus`: what is left. Present only when the brief has room for it.
 */
export const UnderstandingBriefMoveKind = Type.Union([
  Type.Literal('action-scope'),
  Type.Literal('mechanism-blast'),
  Type.Literal('epistemic-verification'),
  Type.Literal('next-focus'),
  Type.Literal('unknown'),
]);
export type UnderstandingBriefMoveKind = Static<typeof UnderstandingBriefMoveKind>;

/** What a token in a move's sentence stands for. */
export const UnderstandingBriefTokenKind = Type.Union([
  Type.Literal('file'),
  Type.Literal('file-rollup'),
  Type.Literal('caller'),
  Type.Literal('caller-rollup'),
  Type.Literal('check'),
  Type.Literal('turn'),
  Type.Literal('subagent'),
  /** A command the session ran. Its text is never carried: the source does not redact it. */
  Type.Literal('command'),
  Type.Literal('review'),
  Type.Literal('metric'),
  Type.Literal('unknown'),
]);
export type UnderstandingBriefTokenKind = Static<typeof UnderstandingBriefTokenKind>;

export const UnderstandingBriefCheckOutcome = Type.Union([
  Type.Literal('pass'),
  Type.Literal('fail'),
  Type.Literal('partial'),
  Type.Literal('unknown'),
]);
export type UnderstandingBriefCheckOutcome = Static<typeof UnderstandingBriefCheckOutcome>;

/** The tone the source gives a token. Null when it gives none or one Halcyonic does not know. */
export const UnderstandingBriefTone = Type.Union([
  Type.Literal('good'),
  Type.Literal('bad'),
  Type.Literal('warn'),
  Type.Literal('accent'),
  Type.Literal('neutral'),
]);
export type UnderstandingBriefTone = Static<typeof UnderstandingBriefTone>;

/**
 * One piece of a move's sentence, in order: plain `text`, or a `token` that stands for something
 * the source can point at. A text segment has only `text`; a token has no `text`, and its other
 * fields are null or empty where the source gives nothing.
 */
export const UnderstandingBriefSegment = Type.Object(
  {
    kind: Type.Union([Type.Literal('text'), Type.Literal('token')]),
    /** The words of a text segment; null for a token. */
    text: Nullable(Text(400)),
    /** What a token stands for; null for a text segment. */
    token: Nullable(UnderstandingBriefTokenKind),
    /** A token's words; null for a text segment. A command token reads "Command". */
    label: Nullable(Text(160)),
    detail: Nullable(Text(80)),
    /** How the source knows a token's claim; null for a text segment, which takes its move's. */
    epistemic: Nullable(UnderstandingEpistemic),
    tone: Nullable(UnderstandingBriefTone),
    /** The files a token points at, as the source gives them. Never its evidence ids. */
    paths: Type.Array(Text(400), { maxItems: 17 }),
    lines_added: Nullable(Count),
    lines_removed: Nullable(Count),
    /** The outcomes of the checks a token stands for, in the source's order. */
    check_outcomes: Type.Array(UnderstandingBriefCheckOutcome, { maxItems: 12 }),
  },
  strict,
);
export type UnderstandingBriefSegment = Static<typeof UnderstandingBriefSegment>;

export const UnderstandingBriefMove = Type.Object(
  {
    kind: UnderstandingBriefMoveKind,
    /** How the source knows the move as a whole. */
    epistemic: UnderstandingEpistemic,
    /** The move's sentence as plain words. */
    plain_text: Text(600),
    /** The same sentence in pieces, in order. */
    segments: Type.Array(UnderstandingBriefSegment, { minItems: 1, maxItems: 24 }),
  },
  strict,
);
export type UnderstandingBriefMove = Static<typeof UnderstandingBriefMove>;

export const UnderstandingBriefFileStatus = Type.Union([
  Type.Literal('verified'),
  Type.Literal('unverified'),
  Type.Literal('failed'),
  Type.Literal('unknown'),
]);
export type UnderstandingBriefFileStatus = Static<typeof UnderstandingBriefFileStatus>;

/** One file the session edited, with the source's reading of whether a check covered it. */
export const UnderstandingBriefEditedFile = Type.Object(
  {
    path: Path,
    status: UnderstandingBriefFileStatus,
    lines_added: Nullable(Count),
    lines_removed: Nullable(Count),
    /** The id of the module the source placed the file in, or null when it placed it in none. */
    module: Nullable(Text(4096)),
  },
  strict,
);
export type UnderstandingBriefEditedFile = Static<typeof UnderstandingBriefEditedFile>;

/** A group of edited files the source drew by a rule of its own over their paths. */
export const UnderstandingBriefModule = Type.Object(
  {
    id: Text(4096),
    /** Null when the source names the module by its id only. */
    name: Nullable(Text(4096)),
    files: Type.Array(Path),
  },
  strict,
);
export type UnderstandingBriefModule = Static<typeof UnderstandingBriefModule>;

/**
 * What the edit touches, as the source derived it. The source gives no epistemic class for any of
 * it: it is the source's own derivation, never an observation. No caller is tied to the edited file
 * it calls: the source gives only the callers' paths and their count.
 */
export const UnderstandingBriefFootprint = Type.Object(
  {
    edited_files: Type.Array(UnderstandingBriefEditedFile),
    modules: Type.Array(UnderstandingBriefModule),
    /** Files that call the edited code and were not changed, or null when the source lists none. */
    untouched_callers: Nullable(Type.Array(Path)),
    /** How many dependents of the edit no check covered. */
    unverified_dependents: Count,
  },
  strict,
);
export type UnderstandingBriefFootprint = Static<typeof UnderstandingBriefFootprint>;

export const UnderstandingBrief = Type.Object(
  {
    source: UnderstandingBriefSource,
    archetype: UnderstandingBriefArchetype,
    /** Two to four moves. Find each by its kind, never by its position. */
    moves: Type.Array(UnderstandingBriefMove, { minItems: 2, maxItems: 4 }),
    /** Null when the source composed no footprint for this session. */
    footprint: Nullable(UnderstandingBriefFootprint),
  },
  strict,
);
export type UnderstandingBrief = Static<typeof UnderstandingBrief>;

/**
 * The answer to "what brief did the understanding source compose of this execution". Only
 * `available` carries a brief; the other states are the report's (`UnderstandingResult`), and
 * `incompatible` also when the source's contract does not offer the brief (before 1.2).
 */
export const UnderstandingBriefResult = Type.Union([
  Type.Object({ availability: Type.Literal('available'), brief: UnderstandingBrief }, strict),
  Type.Object({ availability: Type.Literal('not_found'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('incompatible'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('unauthorized'), reason: ErrorInfo }, strict),
]);
export type UnderstandingBriefResult = Static<typeof UnderstandingBriefResult>;
