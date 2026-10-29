import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { chmodSync, existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { promisify } from 'node:util';

const run = promisify(execFile);
const DEVICE = new URL('./device.ts', import.meta.url).pathname;

/** What the stand-in headset answers, from `ADB_SCENARIO`. */
interface Headset {
  /** Whether the pulled copy has the bytes the headset's sha256sum reports. */
  readonly pullMatches?: boolean;
  /** The installed base.apk's sha256. */
  readonly installedSha256?: string;
  /** The capture service's log lines. */
  readonly syncLog?: string;
  /** No clip appears after a recording stops. */
  readonly noClip?: boolean;
  /** The capture service's log lines once a recording has stopped, where they differ. */
  readonly syncLogAfter?: string;
  /** ActivityTaskManager's lines after a launch. */
  readonly launchLog?: string;
  /** Each GET_PROPERTY reading of set_proximity_close in turn, 'true', 'false' or 'missing'; the last repeats. */
  readonly proximity?: readonly string[];
}

/**
 * A stand-in for adb, as `HALCYONIC_ADB` names one: it appends every command it is given to
 * `ADB_CALLS`, one per line, and answers the commands the device tool runs from the scenario.
 */
const STAND_IN = `
const fs = require('node:fs');
const crypto = require('node:crypto');
const scenario = JSON.parse(fs.readFileSync(process.env.ADB_SCENARIO, 'utf8'));
let args = process.argv.slice(2);
if (args[0] === '-L') args = args.slice(2);
if (args[0] === '-s') args = args.slice(2);
const said = args.join(' ');
fs.appendFileSync(process.env.ADB_CALLS, said + '\\n');
const out = (text) => process.stdout.write(text + '\\n');
const PULLED = 'the bytes the headset recorded';
const sha = (text) => crypto.createHash('sha256').update(text).digest('hex');
if (said === 'devices') out('List of devices attached\\nTEST-SERIAL\\tdevice');
else if (said === 'get-state') out('device');
else if (said.startsWith('shell ls /sdcard/Download/')) {
  process.stderr.write('ls: ' + args[2] + ': No such file or directory\\n');
  process.exit(1);
} else if (args[0] === 'shell' && args[1] === 'screenrecord') {}
else if (said.startsWith('shell sha256sum /sdcard/')) out(sha(PULLED) + '  ' + args[2]);
else if (args[0] === 'pull' && args[1].startsWith('/sdcard/')) fs.writeFileSync(args[2], scenario.pullMatches ? PULLED : 'other bytes');
else if (said === 'shell pm path com.halcyonic.xr') out('package:/data/app/test/base.apk');
else if (said === 'shell pm path com.halcyonic.xr.check') {
  if (fs.existsSync(process.env.ADB_CALLS + '.check-sha')) out('package:/data/app/check/base.apk');
} else if (said === 'shell sha256sum /data/app/check/base.apk') out(fs.readFileSync(process.env.ADB_CALLS + '.check-sha', 'utf8') + '  /data/app/check/base.apk');
else if (said === 'shell dumpsys package com.halcyonic.xr.check') out('      firstInstallTime=2026-01-01 00:00:00');
else if (args[0] === 'install' && args[1] === '-r') fs.writeFileSync(process.env.ADB_CALLS + '.check-sha', sha(fs.readFileSync(args[2], 'utf8')));
else if (said === 'uninstall com.halcyonic.xr.check' || said.startsWith('shell pm grant com.halcyonic.xr.check ')) {}
else if (said === 'shell sha256sum /data/app/test/base.apk') out(scenario.installedSha256 + '  /data/app/test/base.apk');
else if (said === 'shell dumpsys package com.halcyonic.xr') out('      firstInstallTime=2026-01-01 00:00:00');
else if (said === 'shell dumpsys activity activities') out('      topResumedActivity=ActivityRecord{a u0 com.oculus.vrshell/.HomeActivity t1}');
else if (args[0] === 'logcat' && said.includes('ActivityTaskManager')) out(scenario.launchLog ?? '');
else if (args[0] === 'logcat') {
  const stopped = fs.readFileSync(process.env.ADB_CALLS, 'utf8').includes('STOP_INTERNAL_CAPTURE_TO_DISK');
  out(stopped && scenario.syncLogAfter !== undefined ? scenario.syncLogAfter : (scenario.syncLog ?? ''));
} else if (said === 'shell ls /sdcard/Oculus/Screenshots /sdcard/Oculus/VideoShots') {
  const stopped = fs.readFileSync(process.env.ADB_CALLS, 'utf8').includes('STOP_INTERNAL_CAPTURE_TO_DISK');
  out('/sdcard/Oculus/Screenshots:\\n\\n/sdcard/Oculus/VideoShots:\\ncom.oculus.vrshell-20261016-020147-0.mp4' + (stopped && !scenario.noClip ? '\\ncom.oculus.vrshell-20261029-011231-0.mp4' : ''));
} else if (said.startsWith('shell am startservice -n com.oculus.metacam/.capture.CaptureService -a ')) {}
else if (said.startsWith('shell am broadcast') || said.startsWith('shell am force-stop') || said === 'shell input keyevent KEYCODE_WAKEUP') {}
else if (said.startsWith('shell content call')) {
  const seen = fs.readFileSync(process.env.ADB_CALLS, 'utf8').split('\\n').filter((c) => c.startsWith('shell content call')).length;
  const readings = scenario.proximity ?? ['false'];
  const reading = readings[Math.min(seen, readings.length) - 1];
  out(reading === 'missing' ? 'Error: no such method' : 'Result: Bundle[{disable_guardian=false, set_proximity_close=' + reading + '}]');
}
else if (args[0] === 'shell' && args[1] === 'am' && args[2] === 'start') out('Status: ok');
else if (args[0] === 'shell' && args[1] === 'rm') {}
else if (said === 'shell dumpsys power') out('  mWakefulness=Awake');
else if (said === 'shell dumpsys battery') out('  AC powered: true\\n  level: 100\\n  scale: 100\\n  temperature: 300');
else if (said === 'shell dumpsys thermalservice') out('Thermal Status: 0');
else if (said === 'shell pidof com.halcyonic.xr' || said === 'reverse --list' || said === 'forward --list') {}
else {
  process.stderr.write('stand-in adb: not modelled: ' + said + '\\n');
  process.exit(2);
}
`;

describe('pnpm quest:device, against a stand-in adb', () => {
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-quest-device-'));
  after(() => rmSync(directory, { recursive: true, force: true }));
  const adb = join(directory, 'adb');
  writeFileSync(adb, `#!${process.execPath}\n${STAND_IN}`);
  // A stand-in for aapt2: an APK here is a text file whose first line is `package <name>`.
  const aapt2 = join(directory, 'aapt2');
  writeFileSync(
    aapt2,
    `#!${process.execPath}\nconst name = /^package (\\S+)/.exec(require('node:fs').readFileSync(process.argv[process.argv.length - 1], 'utf8'))?.[1];\nif (!name) process.exit(1);\nconsole.log("package: name='" + name + "' versionCode='1'");\n`,
  );
  chmodSync(aapt2, 0o755);
  chmodSync(adb, 0o755);
  let runs = 0;

  /** Runs the device tool on `headset`, giving its exit code, what it printed and every adb command it ran. */
  async function device(headset: Headset, ...args: string[]) {
    const at = runs++;
    const scenario = join(directory, `scenario-${at}.json`);
    const calls = join(directory, `calls-${at}.txt`);
    writeFileSync(scenario, JSON.stringify(headset));
    writeFileSync(calls, '');
    const env = {
      ...process.env,
      HALCYONIC_ADB: adb,
      HALCYONIC_AAPT2: aapt2,
      HALCYONIC_QUEST_SERIAL: 'TEST-SERIAL',
      HALCYONIC_QUEST_STATE: join(directory, `state-${at}.json`),
      HALCYONIC_FFMPEG: join(directory, 'no-ffmpeg'),
      HALCYONIC_FFPROBE: join(directory, 'no-ffprobe'),
      HALCYONIC_QUEST_SETTLE_MS: '0',
      ADB_SCENARIO: scenario,
      ADB_CALLS: calls,
    };
    let code = 0;
    let said = '';
    try {
      const { stdout, stderr } = await run(process.execPath, [DEVICE, ...args], { env });
      said = stdout + stderr;
    } catch (error) {
      const failed = error as { code?: number; stdout?: string; stderr?: string };
      code = failed.code ?? -1;
      said = (failed.stdout ?? '') + (failed.stderr ?? '');
    }
    return { code, said, calls: readFileSync(calls, 'utf8').split('\n').filter(Boolean) };
  }

  test("a capture whose pulled copy differs from the headset's stays on the headset", async () => {
    const out = join(directory, 'out-mismatch');
    const { code, said, calls } = await device({ pullMatches: false }, 'shot', '--out', out);
    assert.equal(code, 1, said);
    assert.match(said, /does not match the headset's; it was left there/);
    assert.ok(!calls.some((call) => call.startsWith('shell rm')), calls.join('\n'));
  });

  test('a capture whose pulled copy matches is removed from the headset, by its name only', async () => {
    const out = join(directory, 'out-match');
    const { code, said, calls } = await device({ pullMatches: true }, 'shot', '--out', out);
    assert.equal(code, 0, said);
    const removed = calls.filter((call) => call.startsWith('shell rm'));
    assert.equal(removed.length, 1, calls.join('\n'));
    assert.match(
      removed[0] ?? '',
      /^shell rm \/sdcard\/Download\/halcyonic\.device-\d{8}-\d{6}\.mp4$/,
    );
  });

  test('install refuses a saved copy that is not the installed APK, and installs nothing', async () => {
    const apk = join(directory, 'new.apk');
    const saved = join(directory, 'saved.apk');
    writeFileSync(apk, 'package com.halcyonic.xr\nthe new build');
    writeFileSync(saved, 'package com.halcyonic.xr\nsome other build');
    const { code, said, calls } = await device(
      { installedSha256: 'a'.repeat(64) },
      'install',
      apk,
      '--saved',
      saved,
    );
    assert.equal(code, 1, said);
    assert.match(said, /is not the installed com\.halcyonic\.xr; nothing was installed/);
    assert.ok(!calls.some((call) => call.startsWith('install')), calls.join('\n'));
    assert.ok(!existsSync(join(directory, `state-${runs - 1}.json`)), 'no install recorded');
  });

  test('install without a saved copy refuses before touching the headset', async () => {
    const apk = join(directory, 'new.apk');
    const { code, said, calls } = await device({}, 'install', apk);
    assert.equal(code, 1, said);
    assert.match(said, /needs --saved/);
    assert.ok(
      !calls.some((call) => call.startsWith('install') || call.startsWith('shell')),
      calls.join('\n'),
    );
  });

  test('launch passes intent extras through, typed, after the same checks', async () => {
    const { code, said, calls } = await device(
      {},
      'launch',
      '--extra',
      'halcyonic.open=running-task',
      '--extra-bool',
      'halcyonic.kit=true',
    );
    assert.equal(code, 0, said);
    const start = calls.filter((call) => call.startsWith('shell am start'));
    assert.deepEqual(start, [
      'shell am start -W -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity --es halcyonic.open running-task --ez halcyonic.kit true',
    ]);
  });

  test('an extra the headset shell could read as more than words is refused before any launch', async () => {
    for (const extra of [
      ['--extra', 'halcyonic.open=task;reboot'],
      ['--extra', 'halcyonic open=task'],
      ['--extra-bool', 'halcyonic.kit=yes'],
      ['--extra', 'no-equals'],
    ]) {
      const { code, said, calls } = await device({}, 'launch', ...extra);
      assert.equal(code, 1, said);
      assert.ok(!calls.some((call) => call.startsWith('shell am start')), calls.join('\n'));
    }
  });

  test('give-back sends the release again when its second reading says the override is on', async () => {
    const { code, said, calls } = await device(
      { proximity: ['false', 'true', 'false', 'false'] },
      'give-back',
    );
    assert.equal(code, 0, said);
    assert.equal(
      calls.filter((call) => call.includes('automation_disable')).length,
      2,
      calls.join('\n'),
    );
    assert.match(said, /The proximity sensor is the headset's again/);
  });

  test('give-back never takes two unreadable readings for off', async () => {
    const { code, said, calls } = await device({ proximity: ['missing'] }, 'give-back');
    assert.equal(code, 1, said);
    assert.equal(
      calls.filter((call) => call.includes('automation_disable')).length,
      2,
      calls.join('\n'),
    );
    assert.match(said, /could not be read/);
  });

  test('awake says when the override could not be read', async () => {
    const { code, said } = await device({ proximity: ['missing'] }, 'awake');
    assert.equal(code, 1, said);
    assert.match(said, /could not be read/);
  });

  test('a launch a system dialog held is stopped again and fails', async () => {
    const launchLog =
      '10-28 15:11:02.100  1505  1662 I ActivityTaskManager: Launch is blocked because: a Guardian dialog is currently showing. Caching launch';
    const { code, said, calls } = await device({ launchLog }, 'launch');
    assert.equal(code, 1, said);
    assert.match(said, /The launch was held and com\.halcyonic\.xr was stopped/);
    assert.ok(calls.includes('shell am force-stop com.halcyonic.xr'), calls.join('\n'));
  });

  test('uninstall refuses every package but the check build, and names it exactly', async () => {
    for (const other of [[], ['--package', 'com.halcyonic.xr'], ['--package', 'com.boopy.quest']]) {
      const { code, said, calls } = await device({}, 'uninstall', ...other);
      assert.equal(code, 1, said);
      assert.ok(!calls.some((call) => call.startsWith('uninstall')), calls.join('\n'));
    }
    const { code, said, calls } = await device(
      {},
      'uninstall',
      '--package',
      'com.halcyonic.xr.check',
    );
    assert.equal(code, 0, said);
    assert.deepEqual(
      calls.filter((call) => call.startsWith('uninstall')),
      ['uninstall com.halcyonic.xr.check'],
    );
  });

  test('grant gives only the listed permissions, and only to the check build', async () => {
    for (const refused of [
      ['--package', 'com.halcyonic.xr', 'com.oculus.permission.USE_SCENE'],
      ['--package', 'com.halcyonic.xr.check', 'android.permission.READ_CONTACTS'],
      ['--package', 'com.halcyonic.xr.check'],
    ]) {
      const { code, said, calls } = await device({}, 'grant', ...refused);
      assert.equal(code, 1, said);
      assert.ok(!calls.some((call) => call.includes('pm grant')), calls.join('\n'));
    }
    const { code, said, calls } = await device(
      {},
      'grant',
      '--package',
      'com.halcyonic.xr.check',
      'com.oculus.permission.USE_SCENE',
    );
    assert.equal(code, 0, said);
    assert.deepEqual(
      calls.filter((call) => call.includes('pm grant')),
      ['shell pm grant com.halcyonic.xr.check com.oculus.permission.USE_SCENE'],
    );
  });

  test("the check build installs without a saved copy and is never taken for the owner's app", async () => {
    const apk = join(directory, 'check.apk');
    writeFileSync(apk, 'package com.halcyonic.xr.check\na check build');
    const { code, said, calls } = await device(
      {},
      'install',
      apk,
      '--package',
      'com.halcyonic.xr.check',
    );
    assert.equal(code, 0, said);
    assert.ok(calls.includes(`install -r ${apk}`), calls.join('\n'));
    assert.doesNotMatch(said, /The owner's/);
    const state = join(directory, `state-${runs - 1}.json`);
    const installs = existsSync(state) ? JSON.parse(readFileSync(state, 'utf8')).installs : {};
    assert.deepEqual(installs, {});
  });

  test("an APK of the owner's package given as the check build is refused before any install", async () => {
    const apk = join(directory, 'owner-as-check.apk');
    writeFileSync(apk, 'package com.halcyonic.xr\nthe owner package by mistake');
    const { code, said, calls } = await device(
      {},
      'install',
      apk,
      '--package',
      'com.halcyonic.xr.check',
    );
    assert.equal(code, 1, said);
    assert.match(said, /is com\.halcyonic\.xr, not com\.halcyonic\.xr\.check/);
    assert.ok(!calls.some((call) => call.startsWith('install')), calls.join('\n'));
  });

  test('an APK whose package is not the one named is refused on the saved-copy path too', async () => {
    const apk = join(directory, 'other.apk');
    const saved = join(directory, 'saved-other.apk');
    writeFileSync(apk, 'package com.example.other\nsome app');
    writeFileSync(saved, 'package com.halcyonic.xr\nsaved');
    const { code, said, calls } = await device({}, 'install', apk, '--saved', saved);
    assert.equal(code, 1, said);
    assert.match(said, /is com\.example\.other, not com\.halcyonic\.xr/);
    assert.ok(!calls.some((call) => call.startsWith('install')), calls.join('\n'));
  });

  test('record start, then record stop, pulls the clip with its sha256 beside it and removes it from the headset', async () => {
    const out = join(directory, 'footage');
    const off =
      '10-29 01:15:51.642 11156 11237 I SimpleMediaSyncUtils: media sync preference is enabled: false';
    const state = join(directory, 'state-record.json');
    const started = await device(
      { syncLog: off, pullMatches: true },
      'record',
      'start',
      '--sync-is-off',
      '--state',
      state,
    );
    assert.equal(started.code, 0, started.said);
    assert.match(started.said, /microphone/);
    const stopped = await device(
      { syncLog: off, pullMatches: true },
      'record',
      'stop',
      '--out',
      out,
      '--state',
      state,
    );
    assert.equal(stopped.code, 0, stopped.said);
    const clip = join(out, 'com.oculus.vrshell-20261029-011231-0.mp4');
    assert.ok(existsSync(clip), stopped.said);
    const sum = readFileSync(`${clip}.sha256`, 'utf8');
    assert.match(sum, /^[0-9a-f]{64} {2}com\.oculus\.vrshell-20261029-011231-0\.mp4\n$/);
    assert.deepEqual(
      stopped.calls.filter((call) => call.startsWith('shell rm')),
      ['shell rm /sdcard/Oculus/VideoShots/com.oculus.vrshell-20261029-011231-0.mp4'],
    );
  });

  test('status says a recording is in progress, and since when', async () => {
    const off =
      '10-29 01:15:51.642 11156 11237 I SimpleMediaSyncUtils: media sync preference is enabled: false';
    const state = join(directory, 'state-status.json');
    await device({ syncLog: off }, 'record', 'start', '--sync-is-off', '--state', state);
    const { code, said } = await device({ syncLog: off }, 'status', '--state', state);
    assert.equal(code, 0, said);
    assert.match(said, /A recording is in progress since \d{4}-\d{2}-\d{2}T/);
  });

  test('give-back stops and collects a recording a cut-off run left going', async () => {
    const off =
      '10-29 01:15:51.642 11156 11237 I SimpleMediaSyncUtils: media sync preference is enabled: false';
    const state = join(directory, 'state-giveback.json');
    const out = join(directory, 'footage-giveback');
    await device(
      { syncLog: off, pullMatches: true },
      'record',
      'start',
      '--sync-is-off',
      '--state',
      state,
    );
    const { code, said, calls } = await device(
      { syncLog: off, pullMatches: true, proximity: ['false'] },
      'give-back',
      '--state',
      state,
      '--out',
      out,
    );
    assert.equal(code, 0, said);
    assert.ok(
      calls.some((call) => call.includes('STOP_INTERNAL_CAPTURE_TO_DISK')),
      calls.join('\n'),
    );
    assert.ok(existsSync(join(out, 'com.oculus.vrshell-20261029-011231-0.mp4.sha256')), said);
  });

  test('give-back still stops the apps and gives the sensor back when the recording it stops yields no clip', async () => {
    const state = join(directory, 'state-noclip.json');
    writeFileSync(
      state,
      JSON.stringify({
        installs: {},
        started: ['com.halcyonic.xr'],
        recording: {
          before: ['com.oculus.vrshell-20261016-020147-0.mp4'],
          startedAt: '2026-10-29T01:00:00.000Z',
        },
      }),
    );
    const { code, said, calls } = await device(
      { noClip: true, proximity: ['false'] },
      'give-back',
      '--state',
      state,
    );
    assert.equal(code, 1, said);
    assert.ok(
      calls.some((call) => call.includes('STOP_INTERNAL_CAPTURE_TO_DISK')),
      calls.join('\n'),
    );
    assert.ok(calls.includes('shell am force-stop com.halcyonic.xr'), calls.join('\n'));
    assert.ok(
      calls.some((call) => call.includes('automation_disable')),
      calls.join('\n'),
    );
    assert.match(said, /no single new recording/);
  });

  test('record stop with no recording in progress stops nothing', async () => {
    const { code, said, calls } = await device(
      {},
      'record',
      'stop',
      '--state',
      join(directory, 'state-none.json'),
    );
    assert.equal(code, 1, said);
    assert.match(said, /No recording is in progress/);
    assert.ok(
      !calls.some((call) => call.includes('STOP_INTERNAL_CAPTURE_TO_DISK')),
      calls.join('\n'),
    );
  });

  test("record stop fails when the headset's latest sync line says the clip was uploaded", async () => {
    const off =
      '10-29 01:15:51.642 11156 11237 I SimpleMediaSyncUtils: media sync preference is enabled: false';
    const on = `${off}\n10-29 01:20:00.000 11156 11237 I SimpleMediaSyncUtils: media sync preference is enabled: true`;
    const state = join(directory, 'state-upload.json');
    const out = join(directory, 'footage-upload');
    await device(
      { syncLog: off, pullMatches: true },
      'record',
      'start',
      '--sync-is-off',
      '--state',
      state,
    );
    const { code, said } = await device(
      { syncLog: off, syncLogAfter: on, pullMatches: true },
      'record',
      'stop',
      '--out',
      out,
      '--state',
      state,
    );
    assert.equal(code, 1, said);
    assert.match(said, /uploaded/);
  });

  test("the capture service is not used while the headset's log says media sync is on", async () => {
    const out = join(directory, 'out-service');
    const syncLog =
      '10-28 15:21:32.620 11156 11221 I SimpleMediaSyncUtils: media sync preference is enabled: true';
    const { code, said, calls } = await device(
      { syncLog },
      'shot',
      '--service',
      '--sync-is-off',
      '--out',
      out,
    );
    assert.equal(code, 1, said);
    assert.match(said, /media sync is on/);
    assert.ok(!calls.some((call) => call.includes('startservice')), calls.join('\n'));
  });
});
