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
 * The laptop step (M1.8b): the check's own mouse presses and keys, sent to the page. `mouse` and `key`
 * are what the page's input layer reports (`reported`); `screen_changed` is observed in the host from
 * the pictures, against a pair taken with no input: null when the scene moves on its own.
 * `response_seen` (M1.8g) is observed against a twin: the page replayed under a clock the host steps,
 * with and without a press, compared at the same frame; null when the twins did not match, absent
 * when not tried. `exceptions_during` counts uncaught exceptions while the step acted. `ready` needs
 * the starter's own layer and either the mouse reported reached with the screen observed changed, or
 * a response seen against the twin.
 */
export const CheckLaptop = Type.Object(
  {
    layer: Type.Enum(['starter', 'changed', 'missing']),
    targets: Type.Integer({ minimum: 0, maximum: 32 }),
    mouse: Type.Enum(['reached', 'not_reached', 'no_target', 'unknown']),
    key: Type.Enum(['reached', 'not_reached', 'unknown']),
    screen_changed: Nullable(Type.Boolean()),
    response_seen: Type.Optional(Nullable(Type.Boolean())),
    exceptions_during: Type.Optional(Type.Integer({ minimum: 0 })),
    ready: Type.Boolean(),
  },
  strict,
);
export type CheckLaptop = Static<typeof CheckLaptop>;

/**
 * The VR-entry step (M1.8b): whether the page called `offerSession` or `requestSession` for
 * `immersive-vr` on a stand-in WebXR on the computer. Never that it enters VR on a headset.
 */
export const CheckVr = Type.Object(
  {
    offered: Type.Enum(['offered', 'not_offered', 'unknown']),
    how: Nullable(Type.Enum(['offer_session', 'request_session'])),
    exceptions: Type.Integer({ minimum: 0 }),
    ready: Type.Boolean(),
  },
  strict,
);
export type CheckVr = Static<typeof CheckVr>;

/** What the scan of the project's source noticed about input read around the starter's layer. */
export const CheckNoteKind = Type.Enum([
  'window_listener',
  'document_listener',
  'canvas_listener',
  'xr_input_read',
  'gamepad_read',
]);
export type CheckNoteKind = Static<typeof CheckNoteKind>;

/**
 * The scan's notes, never a pass or a fail by themselves. `file` is the path in the project only
 * when it is made of plain characters; nothing of a file's contents is kept.
 */
export const CheckNotes = Type.Object(
  {
    scanned: Type.Enum(['full', 'partial']),
    items: Type.Array(
      Type.Object(
        {
          kind: CheckNoteKind,
          file: Nullable(Type.String({ pattern: '^[A-Za-z0-9._/-]{1,120}$' })),
          line: Type.Integer({ minimum: 1 }),
        },
        strict,
      ),
      { maxItems: 5 },
    ),
  },
  strict,
);
export type CheckNotes = Static<typeof CheckNotes>;

/**
 * A finished check, as the host observed it. `passed` is every finding at once: type check 0, build
 * 0, the page opened, no uncaught exception, something drawn; never that the game works.
 * `reads_confined` says whether every execution in the project ran on a runtime that confines a
 * starter project's agent to its folder (ADR 0041). `laptop`, `vr`, `notes` and `ready` are absent
 * on checks recorded before M1.8b, which reads as not checked. `ready` is `passed` and both steps
 * ready; never that the game plays well, or plays on a headset.
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
    laptop: Type.Optional(Nullable(CheckLaptop)),
    vr: Type.Optional(Nullable(CheckVr)),
    notes: Type.Optional(CheckNotes),
    ready: Type.Optional(Type.Boolean()),
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
