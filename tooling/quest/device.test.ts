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
else if (said === 'shell sha256sum /data/app/test/base.apk') out(scenario.installedSha256 + '  /data/app/test/base.apk');
else if (said === 'shell dumpsys package com.halcyonic.xr') out('      firstInstallTime=2026-01-01 00:00:00');
else if (said === 'shell dumpsys activity activities') out('      topResumedActivity=ActivityRecord{a u0 com.oculus.vrshell/.HomeActivity t1}');
else if (args[0] === 'logcat') out(scenario.syncLog ?? '');
else if (args[0] === 'shell' && args[1] === 'rm') {}
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
      HALCYONIC_QUEST_SERIAL: 'TEST-SERIAL',
      HALCYONIC_QUEST_STATE: join(directory, `state-${at}.json`),
      HALCYONIC_FFMPEG: join(directory, 'no-ffmpeg'),
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
    writeFileSync(apk, 'the new build');
    writeFileSync(saved, 'some other build');
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
