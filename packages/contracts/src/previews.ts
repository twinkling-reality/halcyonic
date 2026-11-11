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
 * The page's own picture against the starter's (DRAWN): its frame at the end of the twin's frames with
 * no input, against the pristine starter's frame taken the same way, as the share of the pixels
 * outside the welcome card's corner that differ (`share`, null when either picture could not be
 * taken). `reference` says whether the two were compared, the project began from another template than
 * the tracked one (`other_template`: then `drawn` reads the edges alone), or the starter's own picture
 * could not be made (`unavailable`: then `drawn` is false).
 */
export const CheckOwn = Type.Object(
  {
    share: Nullable(Type.Number({ minimum: 0, maximum: 1 })),
    reference: Type.Enum(['compared', 'other_template', 'unavailable']),
  },
  strict,
);
export type CheckOwn = Static<typeof CheckOwn>;

/**
 * What the page did in the check's headless browser in 10 seconds. `drawn` means something is drawn
 * in view of the laptop camera (sharp edges in a picture at 3 s or 10 s) and, since DRAWN, something
 * of the project's own (`own`), never that the game works. `own` is absent on checks recorded before
 * it, which reads as not compared.
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
    own: Type.Optional(CheckOwn),
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
 * when not tried. `response_unknown` (M1.8g-b) says why `response_seen` is null: the two no-input
 * loads differed (`controls_differ`), a press run differed from the twin before any input
 * (`press_differs_before`), a seen press loaded again ended otherwise (`repeat_differs`), the step ran
 * out of its time or the page stopped reaching frames (`out_of_time`), or a load could not be made or
 * compared (`failed`); absent when not unknown. `exceptions_during` counts uncaught exceptions while
 * the step acted. `ready` needs
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
    response_unknown: Type.Optional(
      Nullable(
        Type.Enum([
          'controls_differ',
          'press_differs_before',
          'repeat_differs',
          'out_of_time',
          'failed',
        ]),
      ),
    ),
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

/**
 * What the scan of the project's source noticed: input read around the starter's layer, or a game
 * still set in the starter's default world (`default_world`, W1), which every new game shares.
 */
export const CheckNoteKind = Type.Enum([
  'window_listener',
  'document_listener',
  'canvas_listener',
  'xr_input_read',
  'gamepad_read',
  'default_world',
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
 * starter project's agent to its folder (ADR 0041) and, for a VR version (M1.8c-1), lets it read its
 * original's folder only read-only, with the original's version control and secret-shaped files and
 * Halcyonic's data refused. `laptop`, `vr`, `notes` and `ready` are absent
 * on checks recorded before M1.8b, which reads as not checked. `ready` is `passed` and both steps
 * ready; never that the game plays well, or plays on a headset. `own_permissions` is true when the
 * check's browser ran as its own process for macOS privacy permissions, so it had none of the app
 * that started Halcyonic, and false when it ran without that separation (the host's launcher for it
 * missing or changed); absent on checks recorded before it, which reads as not known.
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
    own_permissions: Type.Optional(Type.Boolean()),
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

/**
 * How a checked game is shared (ADR 0054): `link` at a temporary public `https://` address, through
 * a tunnel, which another headset can enter VR from; `wifi` at a plain `http://` address of the Mac
 * on its local network, which is not a secure page, so a game opened from it plays in the browser
 * but cannot enter VR.
 */
export const ShareWay = Type.Union([Type.Literal('link'), Type.Literal('wifi')]);
export type ShareWay = Static<typeof ShareWay>;

/**
 * Why a share stopped: `asked` the person stopped it; `preview_stopped` the preview it reached
 * stopped (a new check stops the preview too); `ended` the tunnel or the Wi-Fi listener ended on its
 * own; `host_stopped` the control plane closed, or a restart found it.
 */
export const ShareStopReason = Type.Union([
  Type.Literal('asked'),
  Type.Literal('preview_stopped'),
  Type.Literal('ended'),
  Type.Literal('host_stopped'),
]);
export type ShareStopReason = Static<typeof ShareStopReason>;

/**
 * A share's address, no path, no query, no user part. A link's: `https://` and the host name the
 * tunnel gave. On the Wi-Fi: `http://`, the Mac's IPv4 address on its local network and a port.
 * Anyone who has it, and for the Wi-Fi anyone on that network, can open the checked game while it is
 * shared.
 */
export const ShareAddress = Type.String({
  pattern:
    '^(?:https://[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+|http://(?:[0-9]{1,3}\\.){3}[0-9]{1,3}:[0-9]{1,5})$',
  maxLength: 253,
});

/** The most tries a failed check is sent back to its agent on its own, per chain (ADR 0044). */
export const AUTO_FIX_CAP_MAX = 10;

/**
 * Why sending a failed check back to its agent on its own ended, or never began (ADR 0044):
 * `passed` the next check passed; `cap_reached` the check still failed after the last try;
 * `person_stopped` the person asked to stop the task; `person_instructed` the person (or their
 * request) gave the project's agents their own words; `waiting_on_person` an approval or a question
 * waits; `project_busy` another task or command in the project is under way; `runtime_cannot` the
 * runtime does not declare instructions at rest; `turn_failed` the agent's turn did not complete;
 * `no_change` the try's turn changed no file, so no check followed; `nothing_to_send` the check gave
 * nothing an agent could act on; `check_unavailable` the check itself did not finish; `send_failed`
 * the try was refused or not delivered; `several_tasks` more than one task changed the project
 * before the check, so no one of them is sent it; `host_restarted` the host restarted meanwhile.
 */
export const AutoFixEndReason = Type.Union([
  Type.Literal('passed'),
  Type.Literal('cap_reached'),
  Type.Literal('person_stopped'),
  Type.Literal('person_instructed'),
  Type.Literal('waiting_on_person'),
  Type.Literal('project_busy'),
  Type.Literal('runtime_cannot'),
  Type.Literal('turn_failed'),
  Type.Literal('no_change'),
  Type.Literal('nothing_to_send'),
  Type.Literal('check_unavailable'),
  Type.Literal('send_failed'),
  Type.Literal('several_tasks'),
  Type.Literal('host_restarted'),
]);
export type AutoFixEndReason = Static<typeof AutoFixEndReason>;

/**
 * A task's failed check sent back to its agent on its own (ADR 0044): `fixing` while a try is under
 * way, `attempt` of `cap`; `ended`, with `attempt` the tries made (0 when none was sent) and why.
 * States and numbers only: clients choose the words.
 */
export const AutoFixView = Type.Object(
  {
    state: Type.Union([Type.Literal('fixing'), Type.Literal('ended')]),
    check_id: CheckId,
    attempt: Type.Integer({ minimum: 0, maximum: AUTO_FIX_CAP_MAX }),
    cap: Type.Integer({ minimum: 1, maximum: AUTO_FIX_CAP_MAX }),
    reason: Nullable(AutoFixEndReason),
    /**
     * The steps that failed in the check the chain answered: while a try runs, what it was sent to
     * fix; once the chain ended `passed`, what got fixed; once it ended on a failed check, that check's.
     * Absent where the check is not known to the projection. Steps only: clients choose the words.
     */
    failed_steps: Type.Optional(
      Type.Array(
        Type.Union([Type.Literal('typecheck'), Type.Literal('build'), Type.Literal('page')]),
        {
          maxItems: 3,
        },
      ),
    ),
  },
  strict,
);
export type AutoFixView = Static<typeof AutoFixView>;
