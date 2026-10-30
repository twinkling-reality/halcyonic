import Type, { type Static } from 'typebox';
import { Sha256Hex } from './devices.ts';
import { Nullable, Text } from './primitives.ts';

const strict = { additionalProperties: false } as const;

export type CheckId = string & { readonly __brand: 'CheckId' };

/** One check of a project made from a starter (ADR 0037); UUIDv7, as Halcyonic's other ids. */
export const CheckId = Type.Unsafe<CheckId>(
  Type.String({ pattern: '^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' }),
);

/** The 127.0.0.1 ports a check or a preview is served on, one each at a time; opened by IP, never by `localhost`. */
export const PREVIEW_PORT_FIRST = 47930;
export const PREVIEW_PORT_LAST = 47939;

/** A step of the check: its exit code, or null when it was stopped at its limit. */
export const CheckStep = Type.Object(
  { exit: Nullable(Type.Integer()), seconds: Type.Number({ minimum: 0 }) },
  strict,
);

/**
 * What the page did in the check's headless browser in 10 seconds. `drawn` means something is drawn
 * in view of the laptop camera (sharp edges in a picture at 3 s or 10 s), never that the game works.
 */
export const CheckPage = Type.Object(
  {
    /** Uncaught exceptions, distinct, each its first two lines, clipped; at most five. */
    exceptions: Type.Array(Text(300), { maxItems: 5 }),
    exception_count: Type.Integer({ minimum: 0 }),
    console_errors: Type.Integer({ minimum: 0 }),
    canvases: Type.Integer({ minimum: 0 }),
    edges_at_3s: Nullable(Type.Integer({ minimum: 0 })),
    edges_at_10s: Nullable(Type.Integer({ minimum: 0 })),
    drawn: Type.Boolean(),
    /** The pictures the host kept, by their SHA-256; the images stay on the computer. */
    pictures: Type.Array(
      Type.Object({ at_ms: Type.Integer({ minimum: 0 }), sha256: Sha256Hex }, strict),
      {
        maxItems: 2,
      },
    ),
  },
  strict,
);
export type CheckPage = Static<typeof CheckPage>;

/**
 * A finished check, as the host observed it. `passed` is every finding at once: type check 0, build
 * 0, the page opened, no uncaught exception, something drawn; never that the game works.
 * `reads_confined` says whether every execution in the project ran on a runtime that confines a
 * starter project's agent to its folder (ADR 0041).
 */
export const CheckFindings = Type.Object(
  {
    tsc: CheckStep,
    build: CheckStep,
    page: Nullable(CheckPage),
    /** What could not be found out, in a few words, or null. */
    unknown: Nullable(Text(300)),
    passed: Type.Boolean(),
    reads_confined: Type.Boolean(),
  },
  strict,
);
export type CheckFindings = Static<typeof CheckFindings>;

/** Why a preview stopped. */
export const PreviewStopReason = Type.Union([
  Type.Literal('asked'),
  Type.Literal('new_check'),
  Type.Literal('server_exited'),
  Type.Literal('host_restarted'),
]);
export type PreviewStopReason = Static<typeof PreviewStopReason>;
