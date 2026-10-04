/**
 * Reads what a headset session checks without a person (HEADSET_SESSION.md): the `adb reverse`
 * mappings, the access token files' mode and size, whether a copy is on shared storage, and the
 * lines Halcyonic and its glance write to the headset's log. A verdict says pass or fail in plain
 * words; it never repeats a token, a file's contents or a raw log line, only the fixed words below,
 * modes, sizes, glance codes and errno names.
 */

import { epochMillis } from './readings.ts';

export interface Verdict {
  readonly pass: boolean;
  readonly line: string;
}

const pass = (line: string): Verdict => ({ pass: true, line });
const fail = (line: string): Verdict => ({ pass: false, line });

/** The one mapping a session keeps: the headset's 47800 to the Mac's 47800 (SECURITY.md). */
export const PORT_MAPPING = 'tcp:47800';

/** `adb reverse --list`: one `<serial or (reverse)> <headset side> <Mac side>` line per mapping. */
export function checkReverse(text: string): Verdict {
  const mappings = text
    .split(/\r?\n/)
    .map((line) => line.trim().split(/\s+/))
    .filter((fields) => fields.length >= 2 && fields.slice(-2).every((spec) => spec.includes(':')))
    .map((fields) => `${fields[fields.length - 2]} to ${fields[fields.length - 1]}`);
  const only = `${PORT_MAPPING} to ${PORT_MAPPING}`;
  if (mappings.length === 1 && mappings[0] === only)
    return pass(`adb reverse: one mapping, ${only}`);
  if (mappings.length === 0) {
    return fail(`adb reverse: no mapping; run adb reverse ${PORT_MAPPING} ${PORT_MAPPING}`);
  }
  return fail(
    `adb reverse: ${mappings.length === 1 ? 'the mapping is' : `${mappings.length} mappings,`} ${mappings.join(', ')}; keep only ${only} (adb reverse --remove-all, then adb reverse ${PORT_MAPPING} ${PORT_MAPPING})`,
  );
}

/** What `stat -c '%a %s %F'` says of a file, through `run-as` or not, without its contents. */
export type FileState =
  | { readonly kind: 'file'; readonly mode: string; readonly bytes: number; readonly type: string }
  | { readonly kind: 'absent' }
  | { readonly kind: 'not-debuggable' }
  | { readonly kind: 'not-installed' }
  | { readonly kind: 'unreadable' };

export function readFileState(output: string): FileState {
  const text = output.trim();
  // toybox words a type as `regular file`, `symbolic link` or `FIFO (named pipe)`.
  const stat = /^([0-7]{3,4}) (\d+) ([A-Za-z][A-Za-z ()-]*)$/.exec(
    text.split(/\r?\n/)[0]?.trim() ?? '',
  );
  if (stat?.[1] !== undefined && stat[2] !== undefined && stat[3] !== undefined) {
    return { kind: 'file', mode: stat[1], bytes: Number(stat[2]), type: stat[3] };
  }
  if (/No such file or directory/i.test(text)) return { kind: 'absent' };
  if (/not debuggable/i.test(text)) return { kind: 'not-debuggable' };
  if (/unknown package/i.test(text)) return { kind: 'not-installed' };
  return { kind: 'unreadable' };
}

/** A token as the control plane makes it, 43 characters, and the newline the runbook writes. */
export const TOKEN_FILE_BYTES = 44;

/** A token file in the app's private storage, as written with `run-as`: mode 600, 44 bytes, a regular file. */
export function checkTokenWritten(name: string, state: FileState, optional = false): Verdict {
  switch (state.kind) {
    case 'file': {
      const problems = [
        state.type.startsWith('regular') ? null : `is a ${state.type}`,
        state.mode === '600' ? null : `has mode ${state.mode}, not 600`,
        state.bytes === TOKEN_FILE_BYTES
          ? null
          : `is ${state.bytes} bytes, not ${TOKEN_FILE_BYTES}`,
      ].filter((problem) => problem !== null);
      return problems.length === 0
        ? pass(`${name}: mode 600, ${TOKEN_FILE_BYTES} bytes`)
        : fail(
            `${name} ${problems.join(' and ')}; write it again with run-as (HEADSET_SESSION.md)`,
          );
    }
    case 'absent':
      return optional
        ? pass(`${name}: not written, so its checks are skipped`)
        : fail(`${name}: not there; write it with run-as`);
    case 'not-debuggable':
      return fail(`${name}: run-as refused, so this is not a development build`);
    case 'not-installed':
      return fail(`${name}: Halcyonic is not installed`);
    default:
      return fail(`${name}: could not be looked at`);
  }
}

/**
 * The `run-as` write's temporary files, which its move step renames into place: one left over means
 * the move never ran, so the app reads no new token (HEADSET_SESSION.md).
 */
export function checkWritesFinished(files: readonly (readonly [string, FileState])[]): Verdict {
  const left = files.filter(([, state]) => state.kind === 'file').map(([name]) => name);
  if (left.length > 0) {
    return fail(
      `${left.join(' and ')} left over: the run-as write stopped before its move, so the app has not got it; run the move step again (HEADSET_SESSION.md)`,
    );
  }
  const unseen = files.filter(([, state]) => state.kind === 'unreadable').map(([name]) => name);
  return unseen.length > 0
    ? fail(`${unseen.join(' and ')}: could not be looked at`)
    : pass('run-as writes: no temporary file left over');
}

/** On the release build, `run-as` must be refused: it is not debuggable. */
export function checkTokenOnRelease(name: string, state: FileState): Verdict {
  return state.kind === 'not-debuggable'
    ? pass(`${name}: run-as refused, as on a release build`)
    : fail(`${name}: run-as reached it, so the release build is not the one installed`);
}

/** A token file removed at the end of a session. */
export function checkTokenRemoved(name: string, state: FileState): Verdict {
  switch (state.kind) {
    case 'absent':
      return pass(`${name}: removed`);
    case 'file':
      return fail(`${name}: still there; remove it (HEADSET_SESSION.md, "Close")`);
    case 'not-debuggable':
      return fail(
        `${name}: run-as refused, so the release build is installed; put the development build back over it (adb install -r apps/xr/Builds/Halcyonic.apk), then remove the tokens`,
      );
    case 'not-installed':
      return pass(`${name}: Halcyonic is not installed, so nothing of it is left`);
    default:
      return fail(`${name}: could not be looked at`);
  }
}

/** The old place on shared storage, which must hold nothing. */
export function checkSharedCopy(state: FileState): Verdict {
  if (state.kind === 'absent')
    return pass("shared storage: nothing at the access token's old place");
  if (state.kind === 'file') {
    return fail(
      "shared storage: something is at the access token's old place; adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token",
    );
  }
  return fail('shared storage: the old place could not be looked at');
}

/** What the headset's log says, read from one process's lines: counts and fixed facts only. */
export interface LogReading {
  /** Whether the app's first-frame line is there, so the log reaches back to its start. */
  readonly started: boolean;
  readonly moved: number;
  readonly modeNotSet: number;
  readonly keptPrivate: number;
  readonly notAToken: number;
  readonly releaseDiscarded: number;
  readonly linkedFolder: number;
  readonly copyRemains: number;
  /** The call and errno of each failed move, such as `open failed with EACCES`, or `not shown`. */
  readonly couldNotDeal: readonly string[];
  /** Every connection line's outcome, in order. */
  readonly connections: readonly ConnectionOutcome[];
  /** The code of each glance poll, in order: `ok`, `unproved`, `refused_401`, or `not shown`. */
  readonly glancePolls: readonly string[];
  readonly glanceNotificationFailed: number;
  readonly glancePollFailed: number;
  readonly glancePollerEnded: number;
  readonly cleartext: number;
  readonly strictMode: number;
}

export type ConnectionOutcome =
  | 'live'
  | 'unproved'
  | 'refused'
  | 'not-loopback'
  | 'unreachable'
  | 'connecting'
  | 'other';

/** What a step may expect of the connection: an outcome, or no connection at all. */
export type ExpectedConnection = Exclude<ConnectionOutcome, 'connecting' | 'other'> | 'none';

export const EXPECTED_CONNECTIONS: readonly ExpectedConnection[] = [
  'live',
  'unproved',
  'refused',
  'not-loopback',
  'unreachable',
  'none',
];

/** The lines the app writes about the token's move (ControlPlaneSettings.MigrateAccessToken). */
export const MOVE_LINES = {
  moved: 'Halcyonic: moved the access token from shared storage into app-private storage.',
  modeNotSet: "Halcyonic: could not set the moved access token's mode to 600",
  keptPrivate:
    'Halcyonic: an access token is in app-private storage, so the one on shared storage is not used.',
  notAToken:
    "Halcyonic: what was at the access token's old place on shared storage was not a token in its own form",
  releaseDiscarded:
    'Halcyonic: a release build found an access token on shared storage and did not read it.',
  linkedFolder: 'Halcyonic: the folder on shared storage where the access token was kept is a link',
  copyRemains:
    'Halcyonic: a copy of the access token is still on shared storage, and this app could not remove it.',
} as const;

export const COULD_NOT_DEAL =
  "Halcyonic: could not deal with the access token's old place on shared storage, so a copy may still be there (";

/**
 * The app's first-frame line (DeviceMeasures), written once at every start where DeviceMeasures
 * runs; on a headset it has not run so far (quest-3-device.md, sixth session), so the log's reach
 * is read from times as well (`readReach`).
 */
export const STARTED = 'Halcyonic: device first frame ';

/** `ps -o ETIME -p <pid>`: how long the process has run, `[[dd-]hh:]mm:ss`, in whole seconds, or null. */
export function readElapsed(text: string): number | null {
  const lines = text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== '');
  const match = /^(?:(\d+)-)?(?:(\d+):)?(\d+):(\d{2})$/.exec(lines[lines.length - 1] ?? '');
  if (match === null) return null;
  const [, days, hours, minutes, seconds] = match;
  return (
    ((Number(days ?? 0) * 24 + Number(hours ?? 0)) * 60 + Number(minutes)) * 60 + Number(seconds)
  );
}

/**
 * Whether the headset's main log still holds the app's start, from times alone: its oldest line,
 * as `logcat -v epoch` stamps it, against the headset's clock less how long the app has run, a
 * second earlier for the elapsed time's rounding. Null where any of them could not be read.
 */
export function readReach(
  mainLog: string,
  deviceSeconds: string,
  elapsed: number | null,
): boolean | null {
  const now = Number(deviceSeconds.trim());
  if (elapsed === null || !Number.isFinite(now) || now <= 0) return null;
  let oldest: number | null = null;
  for (const line of mainLog.split(/\r?\n/)) {
    oldest = epochMillis(line);
    if (oldest !== null) break;
  }
  if (oldest === null) return null;
  return oldest <= (now - elapsed - 1) * 1000;
}

/** How each connection detail begins (ConnectionText, LoopbackProof). */
export const CONNECTION_DETAILS = {
  unproved: "This headset's access code doesn't match ",
  refused: "Your computer refused this headset's access code:",
  notLoopback: 'The access code goes only to ws:// or http:// at 127.0.0.1 or [::1]',
} as const;

/** The glance's poll codes (GlancePoll.java, GlanceClient.java): only these are ever repeated. */
export const GLANCE_CODES = [
  'ok',
  'unproved',
  'unreadable',
  'too_large',
  'too_slow',
  'unreachable',
  'token_not_private',
  'token_malformed',
  'no_token',
  'token_unreadable',
] as const;

const isGlanceCode = (code: string) =>
  (GLANCE_CODES as readonly string[]).includes(code) || /^refused_\d{3}$/.test(code);

/** Only a call and an errno name, or an exception's type name, is ever repeated from a reason. */
const REASON =
  /^(?:[a-z]+ failed with (?:E[A-Z0-9]+|an error that names no errno)|[A-Z][A-Za-z]*Exception)$/;

export function readLog(text: string): LogReading {
  const counts = {
    moved: 0,
    modeNotSet: 0,
    keptPrivate: 0,
    notAToken: 0,
    releaseDiscarded: 0,
    linkedFolder: 0,
    copyRemains: 0,
  };
  const couldNotDeal: string[] = [];
  const glancePolls: string[] = [];
  const connections: ConnectionOutcome[] = [];
  let started = false;
  let glanceNotificationFailed = 0;
  let glancePollFailed = 0;
  let glancePollerEnded = 0;
  let cleartext = 0;
  let strictMode = 0;
  for (const line of text.split(/\r?\n/)) {
    if (line.includes(STARTED)) started = true;
    for (const [key, words] of Object.entries(MOVE_LINES) as [keyof typeof MOVE_LINES, string][]) {
      if (line.includes(words)) counts[key]++;
    }
    const failedMove = line.indexOf(COULD_NOT_DEAL);
    if (failedMove >= 0) {
      const rest = line.slice(failedMove + COULD_NOT_DEAL.length);
      const reason = rest.slice(0, rest.indexOf(')'));
      couldNotDeal.push(REASON.test(reason) ? reason : 'not shown');
    }
    const status = /Halcyonic: connection (\w+)(?:: (.*))?$/.exec(line);
    if (status?.[1] !== undefined) connections.push(outcomeOf(status[1], status[2] ?? ''));
    const poll = /\bglance polled (\S+)/.exec(line);
    if (poll?.[1] !== undefined) glancePolls.push(isGlanceCode(poll[1]) ? poll[1] : 'not shown');
    if (line.includes('glance notification failed')) glanceNotificationFailed++;
    if (line.includes('glance poll failed')) glancePollFailed++;
    if (line.includes('glance poller ended')) glancePollerEnded++;
    if (/cleartext/i.test(line)) cleartext++;
    if (/strictmode/i.test(line)) strictMode++;
  }
  return {
    started,
    ...counts,
    couldNotDeal,
    connections,
    glancePolls,
    glanceNotificationFailed,
    glancePollFailed,
    glancePollerEnded,
    cleartext,
    strictMode,
  };
}

function outcomeOf(phase: string, detail: string): ConnectionOutcome {
  if (phase === 'Live') return 'live';
  if (phase === 'Connecting' || phase === 'Synchronizing') return 'connecting';
  if (phase === 'WaitingToRetry') return 'unreachable';
  if (phase === 'Refused') {
    if (detail.startsWith(CONNECTION_DETAILS.unproved)) return 'unproved';
    if (detail.startsWith(CONNECTION_DETAILS.refused)) return 'refused';
    if (detail.startsWith(CONNECTION_DETAILS.notLoopback)) return 'not-loopback';
  }
  return 'other';
}

const SAID: Record<ConnectionOutcome, string> = {
  live: 'live: the control plane proved it holds the token, and the upgrade followed on that connection',
  unproved:
    "refused: something answered without proving it holds the token, so the headset didn't send it",
  refused: 'refused: the control plane proved itself and then answered 401 to the token',
  'not-loopback': 'refused: the endpoint is not ws:// at 127.0.0.1 or [::1]',
  unreachable: "trying again: nothing answered in time, so the token wasn't sent",
  connecting: 'still connecting; run the check again',
  other: 'refused or stopped for another reason; read the line above the stage',
};

const NOT_FROM_START = "the log no longer reaches the app's start; restart the app and check again";

/** Where the log can't be shown to hold the app's start, why. */
const REACH_UNKNOWN: Record<'false' | 'null', string> = {
  false: "the headset's log no longer holds the app's start",
  null: "the headset's log could not be shown to hold the app's start",
};

/** The connection, against what this step of the session expects (live, unless the step says otherwise). */
export function checkConnection(
  reading: LogReading,
  expected: ExpectedConnection = 'live',
  reach: boolean | null = null,
): Verdict {
  const last = reading.connections[reading.connections.length - 1];
  if (last === undefined) {
    if (expected === 'none') return pass('connection: none, as this step expects');
    return fail(
      reading.started || reach === true
        ? 'connection: no connection line yet; is a token written, and is Halcyonic in front?'
        : `connection: ${NOT_FROM_START}`,
    );
  }
  const said = `connection ${SAID[last]}`;
  // Each try starts with Connecting, so a session trying again may show it after having found no one.
  const met =
    last === expected ||
    (expected === 'unreachable' &&
      last === 'connecting' &&
      reading.connections.includes('unreachable'));
  return met ? pass(said) : fail(`${said} (this step expects ${expected})`);
}

/** What the move from shared storage did, when nothing failed. */
export type MoveOutcome = 'moved' | 'kept' | 'released' | 'not-a-token' | 'nothing';

export const MOVE_OUTCOMES: readonly MoveOutcome[] = [
  'moved',
  'kept',
  'released',
  'not-a-token',
  'nothing',
];

const MOVE_SAID: Record<MoveOutcome, string> = {
  moved: 'moved the token into private storage, with mode 600',
  kept: 'a private token was there, and the shared one was removed unread',
  released: 'a release build removed the shared one unread',
  'not-a-token': 'what was on shared storage was not a token, and was removed unused',
  nothing: 'nothing was on shared storage (read as nothing there)',
};

/**
 * The move from shared storage: a failure fails; otherwise what happened, against what the step
 * expects, if it says. With nothing on shared storage the app writes no line, so nothing there is
 * read only from a log that holds the app's start (`reach`, or its first-frame line). Where it
 * can't be shown to, the move can't be judged from this log: said plainly, and a failure only where
 * the step expects an outcome.
 */
export function checkMove(
  reading: LogReading,
  expected?: MoveOutcome,
  reach: boolean | null = null,
): Verdict {
  if (reading.couldNotDeal.length > 0) {
    const reasons = reading.couldNotDeal.join(', ');
    const advice = reading.couldNotDeal.includes('open failed with EACCES')
      ? 'the app may not read a file adb made: remove it from the Mac'
      : 'look into it';
    return fail(`move: could not deal with the old place (${reasons}); ${advice}`);
  }
  if (reading.linkedFolder > 0)
    return fail('move: the folder on shared storage is a link; nothing in it was touched');
  if (reading.modeNotSet > 0) return fail('move: moved, but the mode could not be set to 600');
  if (reading.copyRemains > 0) {
    return fail(
      reading.moved > 0
        ? 'move: moved, and a copy is still on shared storage; remove it from the Mac'
        : 'move: a copy is still on shared storage; remove it from the Mac',
    );
  }
  const outcome: MoveOutcome | null =
    reading.moved > 0
      ? 'moved'
      : reading.keptPrivate > 0
        ? 'kept'
        : reading.releaseDiscarded > 0
          ? 'released'
          : reading.notAToken > 0
            ? 'not-a-token'
            : reading.started || reach === true
              ? 'nothing'
              : null;
  if (outcome === null) {
    const why = REACH_UNKNOWN[reach === false ? 'false' : 'null'];
    return expected === undefined
      ? pass(
          `move: can't be judged from this log, since ${why}; to judge it, restart the app and run the check at once`,
        )
      : fail(
          `move: can't be judged from this log, since ${why}; restart the app and run the check at once (this step expects ${expected})`,
        );
  }
  const said = `move: ${MOVE_SAID[outcome]}`;
  return expected === undefined || outcome === expected
    ? pass(said)
    : fail(`${said} (this step expects ${expected})`);
}

/** The glance's last poll: ok passes; no poll is a skip, as when the glance is not open. */
export function checkGlance(reading: LogReading): Verdict {
  const failures = [
    reading.glanceNotificationFailed > 0
      ? `a notification failed ${reading.glanceNotificationFailed} time(s)`
      : null,
    reading.glancePollFailed > 0 ? `a poll failed ${reading.glancePollFailed} time(s)` : null,
    reading.glancePollerEnded > 0 ? 'its poller ended' : null,
  ].filter((failure) => failure !== null);
  if (failures.length > 0) return fail(`glance: ${failures.join(', ')}`);
  const last = reading.glancePolls[reading.glancePolls.length - 1];
  if (last === undefined) return pass('glance: no poll in this run, so its checks are skipped');
  return last === 'ok'
    ? pass(`glance: polled ok (${reading.glancePolls.length} poll(s))`)
    : fail(`glance: last poll ${last}`);
}

/** Neither cleartext nor StrictMode is ever mentioned in the app's own lines. */
export function checkQuiet(reading: LogReading): Verdict {
  if (reading.cleartext === 0 && reading.strictMode === 0)
    return pass('no cleartext or StrictMode lines from the app');
  return fail(
    `${reading.cleartext} cleartext and ${reading.strictMode} StrictMode line(s) from the app; read them with adb logcat --pid`,
  );
}
