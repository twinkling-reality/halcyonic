/**
 * The checks of a headset session that need no person (HEADSET_SESSION.md), each a plain pass or
 * fail: one `adb reverse` mapping, 47800 to 47800; the access token files in the app's private
 * storage at mode 600 and 44 bytes; nothing on shared storage; and, from the running app's own log
 * lines since it started, the token's move, the connection's proof and the glance's polls, with no
 * cleartext or StrictMode line. It reads modes and sizes only, never a token or a file's contents,
 * and repeats no log line. Exits 1 when any check fails.
 *
 *   pnpm quest:check                          during the session: the connection should be live
 *   pnpm quest:check -- --expect unproved     a step expecting another outcome: unproved, refused,
 *                                             not-loopback, unreachable or none
 *   pnpm quest:check -- --move moved          a step expecting the move to have done one thing:
 *                                             moved, kept, released, not-a-token or nothing
 *   pnpm quest:check -- --release             the release build: run-as refused, no connection
 *   pnpm quest:check -- --closed              at the end: the tokens are gone
 */
import { adbSaid, PACKAGE, requireHeadset, textOption } from './adb.ts';
import {
  checkConnection,
  checkGlance,
  checkMove,
  checkQuiet,
  checkReverse,
  checkSharedCopy,
  checkTokenOnRelease,
  checkTokenRemoved,
  checkTokenWritten,
  EXPECTED_CONNECTIONS,
  type ExpectedConnection,
  MOVE_OUTCOMES,
  type MoveOutcome,
  readFileState,
  readLog,
  type Verdict,
} from './checks.ts';

const SHARED = `/sdcard/Android/data/${PACKAGE}/files/access-token`;

function choice<T extends string>(
  name: string,
  allowed: readonly T[],
  fallback: T | undefined,
): T | undefined {
  const value = textOption(name, fallback ?? '');
  if (value === '') return undefined;
  if (!(allowed as readonly string[]).includes(value)) {
    console.error(`--${name} takes one of ${allowed.join(', ')}`);
    process.exit(2);
  }
  return value as T;
}

const stat = (path: string, privately: boolean) =>
  privately
    ? adbSaid('shell', 'run-as', PACKAGE, 'stat', '-c', "'%a %s %F'", path)
    : adbSaid('shell', 'stat', '-c', "'%a %s %F'", path);

async function check(): Promise<Verdict[]> {
  const closed = process.argv.includes('--closed');
  const release = process.argv.includes('--release');
  const expected = choice<ExpectedConnection>(
    'expect',
    EXPECTED_CONNECTIONS,
    release ? 'none' : 'live',
  );
  const move = choice<MoveOutcome>('move', MOVE_OUTCOMES, release ? 'released' : undefined);
  const verdicts: Verdict[] = [];
  const appToken = readFileState(await stat('files/access-token', true));
  const glanceToken = readFileState(await stat('files/glance-access-token', true));
  const shared = readFileState(await stat(SHARED, false));
  if (closed) {
    verdicts.push(checkTokenRemoved('files/access-token', appToken));
    verdicts.push(
      checkTokenRemoved(
        'files/access-token.off',
        readFileState(await stat('files/access-token.off', true)),
      ),
    );
    verdicts.push(checkTokenRemoved('files/glance-access-token', glanceToken));
    verdicts.push(checkSharedCopy(shared));
    return verdicts;
  }
  verdicts.push(checkReverse(await adbSaid('reverse', '--list')));
  if (release) {
    verdicts.push(checkTokenOnRelease('files/access-token', appToken));
  } else {
    verdicts.push(checkTokenWritten('files/access-token', appToken, expected === 'none'));
    verdicts.push(checkTokenWritten('files/glance-access-token', glanceToken, true));
  }
  verdicts.push(checkSharedCopy(shared));
  const pid = (await adbSaid('shell', 'pidof', PACKAGE)).trim().split(/\s+/)[0] ?? '';
  if (!/^\d+$/.test(pid)) {
    verdicts.push({
      pass: false,
      line: 'log: Halcyonic is not running; start it, then run the check again',
    });
    return verdicts;
  }
  // Only the running app's own lines, since it started: its Unity side and its glance share one process.
  const log = readLog(await adbSaid('logcat', '-d', '-v', 'brief', `--pid=${pid}`));
  verdicts.push(checkMove(log, move));
  verdicts.push(checkConnection(log, expected));
  if (!release) verdicts.push(checkGlance(log));
  verdicts.push(checkQuiet(log));
  return verdicts;
}

await requireHeadset();
let verdicts: Verdict[];
try {
  verdicts = await check();
} catch (error) {
  // Never the error itself, which could carry what adb printed.
  verdicts = [
    { pass: false, line: `the check could not run (${(error as Error).constructor.name})` },
  ];
}
for (const verdict of verdicts) console.log(`${verdict.pass ? 'pass' : 'FAIL'}  ${verdict.line}`);
process.exit(verdicts.every((verdict) => verdict.pass) ? 0 : 1);
