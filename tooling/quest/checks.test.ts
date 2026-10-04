import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import {
  CONNECTION_DETAILS,
  COULD_NOT_DEAL,
  checkConnection,
  checkGlance,
  checkMove,
  checkQuiet,
  checkReverse,
  checkSharedCopy,
  checkTokenOnRelease,
  checkTokenRemoved,
  checkTokenWritten,
  checkWritesFinished,
  GLANCE_CODES,
  MOVE_LINES,
  readElapsed,
  readFileState,
  readLog,
  readReach,
  STARTED,
} from './checks.ts';

/** A line as `adb logcat -v brief` prints it. */
const unity = (message: string) => `I/Unity   (12345): Halcyonic: ${message}`;
const glance = (message: string) => `I/Halcyonic(12345): ${message}`;
/** The app's first-frame line, which says the log reaches back to its start. */
const start = unity('device first frame 2210 ms after start');
const log = (...lines: string[]) => readLog([start, ...lines].join('\n'));

const source = (path: string) => readFileSync(new URL(`../../${path}`, import.meta.url), 'utf8');

describe('headset session checks', () => {
  test('adb reverse passes only with the one mapping, 47800 to 47800', () => {
    assert.equal(checkReverse('UsbFfs tcp:47800 tcp:47800\n').pass, true);
    assert.equal(checkReverse('(reverse) tcp:47800 tcp:47800\r\n').pass, true);
    const none = checkReverse('');
    assert.equal(none.pass, false);
    assert.match(none.line, /no mapping; run adb reverse tcp:47800 tcp:47800/);
    const two = checkReverse('UsbFfs tcp:47800 tcp:47800\nUsbFfs tcp:47900 tcp:47800\n');
    assert.equal(two.pass, false);
    assert.match(two.line, /2 mappings, tcp:47800 to tcp:47800, tcp:47900 to tcp:47800; keep only/);
    assert.equal(
      checkReverse('UsbFfs tcp:47900 tcp:47800\n').pass,
      false,
      'another headset port relays a proof',
    );
    assert.equal(
      checkReverse('UsbFfs tcp:47800 tcp:47900\n').pass,
      false,
      'a listener in place of the control plane',
    );
    assert.doesNotMatch(two.line, /UsbFfs/, 'the device serial is not repeated');
  });

  test("a file's state is its mode, size and type, or why there is none", () => {
    assert.deepEqual(readFileState('600 44 regular file\n'), {
      kind: 'file',
      mode: '600',
      bytes: 44,
      type: 'regular file',
    });
    assert.deepEqual(readFileState("stat: 'files/access-token': No such file or directory"), {
      kind: 'absent',
    });
    assert.deepEqual(readFileState('run-as: package not debuggable: com.halcyonic.xr'), {
      kind: 'not-debuggable',
    });
    assert.deepEqual(readFileState('run-as: unknown package: com.halcyonic.xr'), {
      kind: 'not-installed',
    });
    assert.deepEqual(readFileState('something else'), { kind: 'unreadable' });
    assert.deepEqual(readFileState('777 9 symbolic link'), {
      kind: 'file',
      mode: '777',
      bytes: 9,
      type: 'symbolic link',
    });
    assert.deepEqual(readFileState('660 0 FIFO (named pipe)'), {
      kind: 'file',
      mode: '660',
      bytes: 0,
      type: 'FIFO (named pipe)',
    });
  });

  test('a written token file is mode 600, 44 bytes and regular; the glance one may be absent', () => {
    const name = 'files/access-token';
    assert.equal(checkTokenWritten(name, readFileState('600 44 regular file')).pass, true);
    const open = checkTokenWritten(name, readFileState('644 44 regular file'));
    assert.equal(open.pass, false);
    assert.match(open.line, /has mode 644, not 600/);
    assert.match(
      checkTokenWritten(name, readFileState('600 0 regular empty file')).line,
      /is 0 bytes, not 44/,
    );
    assert.match(
      checkTokenWritten(name, readFileState('777 9 symbolic link')).line,
      /is a symbolic link/,
    );
    assert.match(
      checkTokenWritten(name, readFileState('600 0 FIFO (named pipe)')).line,
      /is a FIFO \(named pipe\)/,
    );
    assert.equal(checkTokenWritten(name, { kind: 'absent' }).pass, false);
    assert.equal(
      checkTokenWritten('files/glance-access-token', { kind: 'absent' }, true).pass,
      true,
    );
    assert.match(
      checkTokenWritten(name, { kind: 'not-debuggable' }).line,
      /not a development build/,
    );
  });

  test('the release build passes only when run-as is refused', () => {
    assert.equal(checkTokenOnRelease('files/access-token', { kind: 'not-debuggable' }).pass, true);
    assert.equal(checkTokenOnRelease('files/access-token', { kind: 'absent' }).pass, false);
    assert.equal(
      checkTokenOnRelease('files/access-token', readFileState('600 44 regular file')).pass,
      false,
    );
  });

  test('at the close, the tokens are gone, and a release build is put back first', () => {
    assert.equal(checkTokenRemoved('files/access-token', { kind: 'absent' }).pass, true);
    assert.equal(
      checkTokenRemoved('files/access-token', readFileState('600 44 regular file')).pass,
      false,
    );
    assert.match(
      checkTokenRemoved('files/access-token', { kind: 'not-debuggable' }).line,
      /adb install -r apps\/xr\/Builds\/Halcyonic\.apk/,
    );
    assert.equal(checkTokenRemoved('files/access-token', { kind: 'not-installed' }).pass, true);
  });

  test('shared storage passes only with nothing at the old place', () => {
    assert.equal(checkSharedCopy({ kind: 'absent' }).pass, true);
    const copy = checkSharedCopy(readFileState('660 44 regular file'));
    assert.equal(copy.pass, false);
    assert.match(
      copy.line,
      /adb shell rm -f \/sdcard\/Android\/data\/com\.halcyonic\.xr\/files\/access-token/,
    );
  });

  test('the move reads as what happened, against what the step expects', () => {
    assert.equal(
      checkMove(log()).line,
      'move: nothing was on shared storage (read as nothing there)',
    );
    assert.equal(checkMove(log(), 'nothing').pass, true);
    assert.equal(checkMove(log(), 'moved').pass, false, 'expected a move that did not happen');
    const moved = log(
      unity('moved the access token from shared storage into app-private storage.'),
    );
    assert.deepEqual(
      [checkMove(moved, 'moved').pass, checkMove(moved).line],
      [true, 'move: moved the token into private storage, with mode 600'],
    );
    const kept = log(
      unity('an access token is in app-private storage, so the one on shared storage is not used.'),
    );
    assert.equal(checkMove(kept, 'kept').pass, true);
    assert.match(
      checkMove(kept, 'not-a-token').line,
      /removed unread \(this step expects not-a-token\)/,
    );
    assert.equal(
      checkMove(
        log(
          unity(
            "what was at the access token's old place on shared storage was not a token in its own form, so it was not used.",
          ),
        ),
        'not-a-token',
      ).pass,
      true,
    );
    assert.equal(
      checkMove(
        log(unity('a release build found an access token on shared storage and did not read it.')),
        'released',
      ).pass,
      true,
    );
  });

  test('a failed move fails, with its call and errno only', () => {
    const copy = `W/Unity   (12345): ${MOVE_LINES.copyRemains} From the computer: adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token`;
    const movedWithCopy = log(
      unity('moved the access token from shared storage into app-private storage.'),
      copy,
    );
    assert.equal(checkMove(movedWithCopy, 'moved').pass, false);
    assert.match(checkMove(movedWithCopy).line, /moved, and a copy is still on shared storage/);
    const keptWithCopy = log(
      unity('an access token is in app-private storage, so the one on shared storage is not used.'),
      copy,
    );
    assert.match(checkMove(keptWithCopy).line, /^move: a copy is still on shared storage/);
    const refused = log(
      `W/Unity   (12345): ${COULD_NOT_DEAL}open failed with EACCES). From the computer: adb shell ls x`,
    );
    assert.deepEqual(refused.couldNotDeal, ['open failed with EACCES']);
    assert.match(
      checkMove(refused).line,
      /\(open failed with EACCES\); the app may not read a file adb made: remove it from the Mac/,
    );
    assert.match(
      checkMove(
        log(
          unity(
            "could not set the moved access token's mode to 600; app-private storage still keeps other apps out.",
          ),
        ),
      ).line,
      /mode could not be set to 600/,
    );
    assert.equal(
      checkMove(
        log(
          unity(
            'the folder on shared storage where the access token was kept is a link, so nothing in it was read or removed.',
          ),
        ),
      ).pass,
      false,
    );
  });

  test("a log that can't be shown to hold the app's start is never read as nothing there", () => {
    const late = readLog(unity('connection Live'));
    assert.equal(late.started, false);
    for (const reach of [false, null]) {
      const said = checkMove(late, undefined, reach);
      assert.equal(said.pass, true, 'said plainly, not failed, where the step expects nothing');
      assert.match(said.line, /^move: can't be judged from this log, since the headset's log/);
      assert.doesNotMatch(said.line, /nothing was on shared storage/);
      const expected = checkMove(late, 'nothing', reach);
      assert.equal(expected.pass, false, 'a step that expects an outcome fails');
      assert.match(expected.line, /can't be judged .*\(this step expects nothing\)$/);
    }
    assert.match(checkMove(late, undefined, false).line, /no longer holds the app's start/);
    assert.match(checkMove(late, undefined, null).line, /could not be shown to hold/);
    assert.match(checkConnection(readLog('')).line, /no longer reaches the app's start/);
    assert.match(checkConnection(log()).line, /no connection line yet/);
  });

  test("a log that holds the app's start by its times reads nothing as nothing there", () => {
    // As on a headset, where the app writes no first-frame line and its first is a connection's.
    const quiet = readLog(unity('connection Synchronizing'));
    assert.equal(
      checkMove(quiet, 'nothing', true).line,
      'move: nothing was on shared storage (read as nothing there)',
    );
    assert.equal(checkMove(quiet, 'nothing', true).pass, true);
    assert.match(checkConnection(readLog(''), 'live', true).line, /no connection line yet/);
  });

  test("the log's reach is its oldest stamp against the app's start, a second early for rounding", () => {
    assert.equal(readElapsed('     ELAPSED\n       05:12\n'), 312);
    assert.equal(readElapsed('ELAPSED\n02:03:04'), 7384);
    assert.equal(readElapsed('ELAPSED\n1-02:03:04\r\n'), 93784);
    assert.equal(readElapsed('ELAPSED\n'), null);
    assert.equal(readElapsed('bad pid'), null);
    const main = [
      '--------- beginning of main',
      '1700000000.123  1234  1234 I Something: what it said',
      '1700000090.000  4321  4321 I Unity   : Halcyonic: connection Synchronizing',
    ].join('\n');
    // Started at 1700000100 - 50 = ...050, a second earlier at the most: the log began before that.
    assert.equal(readReach(main, '1700000100\n', 50), true);
    // Started at ...001 at the earliest, 99 s before ...100: the log began at ...000.123, after it.
    assert.equal(readReach(main, '1700000100', 99), false);
    assert.equal(readReach(main, '1700000100', 98), true);
    assert.equal(readReach(main, '1700000100', null), null, 'how long it has run, unread');
    assert.equal(readReach(main, 'date: bad', 50), null, "the headset's time, unread");
    assert.equal(readReach('--------- beginning of main\n', '1700000100', 50), null, 'no stamp');
    assert.equal(
      readReach('1700000000.123456  1234  1234 I Something: x', '1700000100', 50),
      true,
      'microseconds',
    );
  });

  test('a run-as write left half done fails, naming the file and the step to run again', () => {
    const none = checkWritesFinished([
      ['files/access-token.tmp', { kind: 'absent' }],
      ['files/glance-access-token.tmp', { kind: 'absent' }],
    ]);
    assert.deepEqual(none, { pass: true, line: 'run-as writes: no temporary file left over' });
    const left = checkWritesFinished([
      ['files/access-token.tmp', readFileState('600 44 regular file')],
      ['files/glance-access-token.tmp', { kind: 'absent' }],
    ]);
    assert.equal(left.pass, false);
    assert.match(
      left.line,
      /^files\/access-token\.tmp left over: the run-as write stopped before its move, .*run the move step again \(HEADSET_SESSION\.md\)$/,
    );
    const both = checkWritesFinished([
      ['files/access-token.tmp', readFileState('600 20 regular file')],
      ['files/glance-access-token.tmp', readFileState('600 44 regular file')],
    ]);
    assert.match(
      both.line,
      /^files\/access-token\.tmp and files\/glance-access-token\.tmp left over/,
    );
    assert.equal(
      checkWritesFinished([['files/access-token.tmp', { kind: 'unreadable' }]]).pass,
      false,
    );
  });

  test('the connection is its last line, against what the step expects', () => {
    const live = log(
      unity('connection Connecting'),
      unity('connection Synchronizing'),
      unity('connection Live'),
    );
    assert.equal(checkConnection(live).pass, true);
    const unproved = log(
      unity(
        "connection Refused: This headset's access code doesn't match your computer's, or something else is answering in its place, so the headset didn't send it. Put your computer's current access code on the headset, check that Halcyonic is running there, and restart the app.",
      ),
    );
    assert.equal(checkConnection(unproved).pass, false);
    assert.match(checkConnection(unproved).line, /this step expects live/);
    assert.equal(checkConnection(unproved, 'unproved').pass, true);
    const retrying = log(
      unity('connection Connecting'),
      unity(
        'connection WaitingToRetry: Nothing answered at 127.0.0.1:47800, so the access code was not sent.',
      ),
      unity('connection Connecting'),
    );
    assert.equal(
      checkConnection(retrying, 'unreachable').pass,
      true,
      'trying again after finding no one',
    );
    assert.equal(
      checkConnection(log(unity('connection Connecting')), 'unreachable').pass,
      false,
      'not yet found unreachable',
    );
    const refused = log(
      unity(
        "connection Refused: Your computer refused this headset's access code: it doesn't match your computer's. Put your computer's current access code on the headset, then restart the app.",
      ),
    );
    assert.equal(checkConnection(refused, 'refused').pass, true);
    const protocol = log(
      unity('connection Refused: The control plane speaks realtime protocol 2.'),
    );
    assert.equal(
      checkConnection(protocol, 'refused').pass,
      false,
      'another refusal is not the 401',
    );
    assert.match(checkConnection(protocol).line, /another reason/);
    const named = log(
      unity(
        'connection Refused: The access code goes only to ws:// or http:// at 127.0.0.1 or [::1], so it was not sent to ws://localhost:47800. Name one of those instead.',
      ),
    );
    assert.equal(checkConnection(named, 'not-loopback').pass, true);
    assert.equal(
      checkConnection(log(), 'none').pass,
      true,
      'the release build has no control plane',
    );
    assert.equal(checkConnection(live, 'none').pass, false);
  });

  test("the glance passes on its last poll's code, fails on any failure, and is skipped when it hasn't polled", () => {
    assert.match(checkGlance(log()).line, /skipped/);
    const ok = log(
      glance('glance started'),
      glance('glance polled ok in 42 ms, 1 waiting, 2 working, visible 1'),
    );
    assert.equal(checkGlance(ok).pass, true);
    const down = log(
      glance('glance polled ok in 42 ms, 1 waiting, 2 working, visible 1'),
      glance(
        'glance polled unreachable (SocketTimeoutException) in 3001 ms, 0 waiting, 0 working, visible 0',
      ),
    );
    assert.equal(checkGlance(down).line, 'glance: last poll unreachable');
    assert.equal(
      checkGlance(log(glance('glance polled refused_401 in 9 ms, 0 waiting, 0 working, visible 1')))
        .line,
      'glance: last poll refused_401',
    );
    const notified = log(
      glance('glance polled ok in 1 ms, 0 waiting, 0 working, visible 1'),
      glance('glance notification failed: SecurityException'),
    );
    assert.equal(checkGlance(notified).pass, false);
    assert.equal(
      checkGlance(log(glance('glance poll failed: IllegalStateException'))).pass,
      false,
      'every poll throwing is no skip',
    );
    assert.equal(
      checkGlance(log(glance('glance poller ended: RejectedExecutionException'))).pass,
      false,
    );
  });

  test('a cleartext or StrictMode line from the app fails', () => {
    assert.equal(checkQuiet(log(unity('connection Live'))).pass, true);
    assert.equal(
      checkQuiet(log('D/StrictMode(12345): StrictMode policy violation; ~duration=3 ms')).pass,
      false,
    );
    assert.equal(
      checkQuiet(
        log(
          'W/System.err(12345): java.net.UnknownServiceException: CLEARTEXT communication to 127.0.0.1 not permitted',
        ),
      ).pass,
      false,
    );
  });

  test('no verdict repeats a token, or any piece of one', () => {
    const token = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_-Z';
    const reading = log(
      `W/Unity   (12345): ${COULD_NOT_DEAL}${token}). From the computer: adb shell ls x`,
      unity(`connection WaitingToRetry: ${token}`),
      unity(`connection Refused: ${token}`),
      glance(`glance polled ${token} in 1 ms`),
      `I/Something(12345): ${token}`,
    );
    assert.deepEqual(reading.couldNotDeal, ['not shown']);
    assert.deepEqual(reading.glancePolls, ['not shown']);
    const lines = [
      checkMove(reading),
      checkConnection(reading),
      checkGlance(reading),
      checkQuiet(reading),
    ].map((verdict) => verdict.line);
    for (const line of lines) {
      for (let at = 0; at + 8 <= token.length; at++)
        assert.ok(!line.includes(token.slice(at, at + 8)), line);
    }
  });

  test('the lines matched are the ones the app and the glance write, word for word', () => {
    const settings = source('apps/xr/Assets/Halcyonic/Scripts/ControlPlaneSettings.cs');
    for (const words of Object.values(MOVE_LINES)) assert.ok(settings.includes(words), words);
    assert.ok(settings.includes(COULD_NOT_DEAL));
    assert.ok(
      source('apps/xr/Assets/Halcyonic/Scripts/DeviceMeasures.cs').includes('Log("first frame "'),
    );
    assert.ok(
      source('apps/xr/Assets/Halcyonic/Scripts/DeviceMeasures.cs').includes(
        '"Halcyonic: device {0}"',
      ),
    );
    assert.equal(STARTED, 'Halcyonic: device first frame ');
    const connection = source('apps/xr/Assets/Halcyonic/Scripts/ControlPlaneConnection.cs');
    assert.ok(connection.includes('LogStatus("connection"'));
    assert.ok(connection.includes('"Halcyonic: {0}"'));
    const text = source('apps/xr/Packages/com.halcyonic.client/Runtime/ConnectionText.cs');
    assert.ok(text.includes(`"${CONNECTION_DETAILS.unproved}"`));
    assert.ok(
      source('apps/xr/Packages/com.halcyonic.client/Runtime/HostText.cs').includes(
        'YourStart = "Your computer"',
      ),
    );
    assert.ok(
      text.includes(
        `" ${CONNECTION_DETAILS.refused.replace('Your computer ', '')} it doesn't match "`,
      ),
    );
    assert.ok(
      source('apps/xr/Packages/com.halcyonic.client/Runtime/LoopbackProof.cs').includes(
        CONNECTION_DETAILS.notLoopback,
      ),
    );
    const activity = source('apps/xr/Android/glance/src/com/halcyonic/glance/GlanceActivity.java');
    for (const words of [
      '"glance polled %s%s in ',
      '"glance notification failed: "',
      '"glance poll failed: "',
      '"glance poller ended: "',
    ]) {
      assert.ok(activity.includes(words), words);
    }
    const glanceSource = [
      source('apps/xr/Android/glance/src/com/halcyonic/glance/GlancePoll.java'),
      source('apps/xr/Android/glance/src/com/halcyonic/glance/GlanceClient.java'),
    ].join('\n');
    for (const code of GLANCE_CODES) assert.ok(glanceSource.includes(`"${code}"`), code);
    assert.ok(glanceSource.includes('"refused_%03d"'));
  });
});
