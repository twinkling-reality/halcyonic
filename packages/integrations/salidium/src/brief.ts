import {
  compileValidator,
  Nullable,
  Timestamp,
  type UnderstandingBrief,
  type UnderstandingBriefSegment,
  type Validated,
  type ValidationIssue,
} from '@halcyonic/contracts';
import Type, { type Static } from 'typebox';
import type { Discovered } from './connection.ts';
import { WireEpistemic } from './wire.ts';

/**
 * Salidium's execution brief, consumer contract 1.2, as this client reads it.
 *
 * Written from Salidium's own source while 1.2 was not yet committed (its working tree on
 * 2026-11-18: `packages/consumer-contract/src/schemas.ts` and `packages/daemon/src/consumer/
 * routes.ts`), so it may still change; an answer that does not match reads as incompatible. A
 * tolerant reader like the report's: it requires only what Halcyonic maps, leaves every object
 * open, and checks the bounds Salidium's schema states.
 */

/** The brief Salidium offers from this minor version of consumer contract 1. */
export const BRIEF_MINOR = 2;

const Count = Type.Integer({ minimum: 0 });
const Id = Type.String({ minLength: 1, maxLength: 1100 });
const Text = (maxLength: number) => Type.String({ maxLength });
/** Salidium's schema bounds no path; Halcyonic refuses one longer than this. */
const Path = Text(4096);
/** A word of a vocabulary Salidium may grow within its major version, read as unknown when new. */
const Word = Text(64);

const WireSegment = Type.Object({
  kind: Word,
  text: Type.Optional(Text(400)),
  tokenKind: Type.Optional(Word),
  label: Type.Optional(Text(160)),
  detail: Type.Optional(Text(80)),
  epistemic: Type.Optional(WireEpistemic),
  tone: Type.Optional(Word),
  focusPath: Type.Optional(Text(400)),
  focusPaths: Type.Optional(Type.Array(Text(400), { maxItems: 16 })),
  additions: Type.Optional(Count),
  deletions: Type.Optional(Count),
  checkOutcomes: Type.Optional(Type.Array(Word, { maxItems: 12 })),
});
type WireSegment = Static<typeof WireSegment>;

const WireMove = Type.Object({
  kind: Word,
  epistemic: WireEpistemic,
  segments: Type.Array(WireSegment, { minItems: 1, maxItems: 24 }),
  plainText: Text(600),
});
type WireMove = Static<typeof WireMove>;

const WireFootprint = Type.Object({
  editedFiles: Type.Array(
    Type.Object({
      path: Path,
      status: Word,
      linesAdded: Type.Optional(Count),
      linesRemoved: Type.Optional(Count),
      module: Type.Optional(Path),
    }),
  ),
  modules: Type.Optional(
    Type.Array(Type.Object({ id: Path, name: Type.Optional(Path), files: Type.Array(Path) })),
  ),
  blastRadius: Type.Object({
    unverifiedDependents: Count,
    untouchedCallers: Type.Optional(Type.Array(Path)),
  }),
});

/**
 * `ExecutionBriefDocument`. Salidium's schema names its format `salidium.execution-brief` while its
 * daemon sends `salidium.session-brief` (routes.ts:549); both name this document, so either is
 * read. The brief carries no evidence sequence.
 */
export const WireBrief = Type.Object({
  format: Type.Optional(
    Type.Union([Type.Literal('salidium.execution-brief'), Type.Literal('salidium.session-brief')]),
  ),
  generatedAt: Type.Optional(Timestamp),
  execution: Type.Object({ provider: Id, sessionId: Id }),
  brief: Type.Object({
    archetype: Word,
    moves: Type.Array(WireMove, { minItems: 2, maxItems: 4 }),
    move2Footprint: Type.Optional(Nullable(WireFootprint)),
  }),
});
export type WireBrief = Static<typeof WireBrief>;

const validateShape = compileValidator(WireBrief);

/** The brief's shape, then what the shape cannot say: a token names what it is and how it is known. */
export function validateBrief(value: unknown): Validated<WireBrief> {
  const shape = validateShape(value);
  if (!shape.ok) return shape;
  const issues: ValidationIssue[] = [];
  shape.value.brief.moves.forEach((move, m) => {
    move.segments.forEach((segment, s) => {
      const path = `/brief/moves/${m}/segments/${s}`;
      if (segment.kind === 'text' && segment.text === undefined)
        issues.push({ path, message: 'is a text segment without text' });
      if (
        segment.kind === 'token' &&
        (segment.tokenKind === undefined ||
          segment.label === undefined ||
          segment.epistemic === undefined)
      )
        issues.push({ path, message: 'is a token without its kind, label or epistemic class' });
    });
  });
  return issues.length > 0 ? { ok: false, issues } : shape;
}

/** A value of one of `values`, or `fallback` for one Salidium added later. */
function known<T extends string, F>(values: readonly T[], value: string, fallback: F): T | F {
  return values.find((candidate) => candidate === value) ?? fallback;
}

const ARCHETYPES = [
  'verified-edit',
  'blast-radius-edit',
  'failing-or-unverified-edit',
  'read-only-exploration',
  'active-mid-flight',
] as const;
const MOVE_KINDS = [
  'action-scope',
  'mechanism-blast',
  'epistemic-verification',
  'next-focus',
] as const;
const TOKEN_KINDS = [
  'file',
  'file-rollup',
  'caller',
  'caller-rollup',
  'check',
  'turn',
  'subagent',
  'command',
  'review',
  'metric',
] as const;
const TONES = ['good', 'bad', 'warn', 'accent', 'neutral'] as const;
const CHECK_OUTCOMES = ['pass', 'fail', 'partial'] as const;
const FILE_STATUSES = ['verified', 'unverified', 'failed'] as const;

/** The label every command token carries: Salidium sends the command's own words unredacted. */
export const COMMAND_LABEL = 'Command';

const NO_TOKEN = { detail: null, tone: null, lines_added: null, lines_removed: null } as const;

function textSegment(text: string): UnderstandingBriefSegment {
  return {
    kind: 'text',
    text,
    token: null,
    label: null,
    epistemic: null,
    paths: [],
    check_outcomes: [],
    ...NO_TOKEN,
  };
}

/** One segment in Halcyonic's shape, or null for a kind of segment Salidium added later. */
function toSegment(segment: WireSegment): UnderstandingBriefSegment | null {
  if (segment.kind === 'text') return textSegment(segment.text ?? '');
  if (segment.kind !== 'token') return null;
  const token = known(TOKEN_KINDS, segment.tokenKind ?? '', 'unknown' as const);
  const common = {
    kind: 'token',
    text: null,
    token,
    epistemic: segment.epistemic ?? null,
    tone: segment.tone === undefined ? null : known(TONES, segment.tone, null),
  } as const;
  // A command's words are the session's own and may hold anything: only its place is kept.
  if (token === 'command')
    return {
      ...common,
      ...NO_TOKEN,
      tone: common.tone,
      label: COMMAND_LABEL,
      paths: [],
      check_outcomes: [],
    };
  const paths = [
    ...new Set([
      ...(segment.focusPath === undefined ? [] : [segment.focusPath]),
      ...(segment.focusPaths ?? []),
    ]),
  ];
  return {
    ...common,
    label: segment.label ?? '',
    detail: segment.detail ?? null,
    paths,
    lines_added: segment.additions ?? null,
    lines_removed: segment.deletions ?? null,
    check_outcomes: (segment.checkOutcomes ?? []).map((outcome) =>
      known(CHECK_OUTCOMES, outcome, 'unknown' as const),
    ),
  };
}

/**
 * A move's sentence in Halcyonic's shape. Where a command token's words were left out, the plain
 * text is composed again from the kept segments by Salidium's own rule (composeCanvasSpec.ts:516),
 * so the command's words do not cross in it either.
 */
function toMove(move: WireMove): UnderstandingBrief['moves'][number] {
  const kept = move.segments.map(toSegment).filter((segment) => segment !== null);
  // Every segment of a kind added later is left out; the sentence then stands as its plain text.
  const segments = kept.length > 0 ? kept : [textSegment(move.plainText.slice(0, 400))];
  const hidesCommand = segments.some((segment) => segment.token === 'command');
  const plain = hidesCommand
    ? segments
        .map((segment) =>
          segment.kind === 'text'
            ? (segment.text ?? '')
            : segment.detail
              ? `${segment.label} (${segment.detail})`
              : (segment.label ?? ''),
        )
        .join('')
        .replace(/\s+/g, ' ')
        .trim()
        .slice(0, 600)
    : move.plainText;
  return {
    kind: known(MOVE_KINDS, move.kind, 'unknown' as const),
    epistemic: move.epistemic,
    plain_text: plain,
    segments,
  };
}

/**
 * Maps a Salidium brief onto Halcyonic's, field by field. Every epistemic class is copied as it
 * is; the footprint, which carries none, is copied without one. Properties this function does not
 * name never reach the result: the brief's delta, view match, steering, overlaps with other
 * sessions, claims, verification move and every evidence id stay out.
 */
export function toBrief(brief: WireBrief, { discovery, contract }: Discovered): UnderstandingBrief {
  const footprint = brief.brief.move2Footprint ?? null;
  return {
    source: {
      system: 'salidium',
      // Read from Salidium. The recorded demonstration marks its stand-in's answers synthetic.
      synthetic: false,
      version: discovery.salidium.version,
      contract: { name: contract.name, major: contract.major, minor: contract.minor },
      instance_id: discovery.instanceId,
      generated_at: brief.generatedAt ?? null,
    },
    archetype: known(ARCHETYPES, brief.brief.archetype, 'unknown' as const),
    moves: brief.brief.moves.map(toMove),
    footprint: footprint && {
      edited_files: footprint.editedFiles.map((file) => ({
        path: file.path,
        status: known(FILE_STATUSES, file.status, 'unknown' as const),
        lines_added: file.linesAdded ?? null,
        lines_removed: file.linesRemoved ?? null,
        module: file.module ?? null,
      })),
      modules: (footprint.modules ?? []).map((module) => ({
        id: module.id,
        name: module.name ?? null,
        files: module.files,
      })),
      untouched_callers: footprint.blastRadius.untouchedCallers ?? null,
      unverified_dependents: footprint.blastRadius.unverifiedDependents,
    },
  };
}
