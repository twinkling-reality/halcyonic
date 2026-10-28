import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  captureName,
  describeFront,
  isLaunchHeld,
  isMediaSyncOn,
  newCaptures,
  readBasePaths,
  readFirstInstall,
  readListedFiles,
  readSha256,
  readTestingProperties,
  readTopActivity,
  readWakefulness,
} from './headset.ts';

// Shapes recorded from a Quest 3 on Horizon OS v207 (validation/quest-headset-in-the-loop.md), with made-up record ids.

describe('headset state', () => {
  test('wakefulness comes from dumpsys power', () => {
    assert.equal(
      readWakefulness('Power Manager State:\n  mDirty=0x0\n  mWakefulness=Asleep\n'),
      'asleep',
    );
    assert.equal(readWakefulness('  mWakefulness=Awake\n  mWakefulnessChanging=false\n'), 'awake');
    assert.equal(readWakefulness('nothing here'), null);
  });

  test('only the first topResumedActivity says what is in front', () => {
    const text = [
      '  ResumedActivity: ActivityRecord{aaa u0 com.oculus.vrshell/.HomeActivity t1}',
      '      topResumedActivity=ActivityRecord{bbb u0 com.oculus.guardian/com.oculus.vrguardianservice.guardiandialog.GuardianDialogActivity t2}',
      '      topResumedActivity=ActivityRecord{ccc u0 com.oculus.vrshell/.HomeActivity t1}',
      '  mFocusedApp=ActivityRecord{aaa u0 com.oculus.vrshell/.HomeActivity t1}',
    ].join('\n');
    assert.deepEqual(readTopActivity(text), {
      package: 'com.oculus.guardian',
      activity: 'com.oculus.vrguardianservice.guardiandialog.GuardianDialogActivity',
    });
    assert.equal(readTopActivity('  mFocusedApp=null\n'), null);
  });

  test('what is in front reads as plain words', () => {
    const guardian = {
      package: 'com.oculus.guardian',
      activity: 'com.oculus.vrguardianservice.guardiandialog.GuardianDialogActivity',
    };
    assert.match(
      describeFront(guardian, 'com.halcyonic.xr'),
      /can't find its position in the room/,
    );
    assert.equal(
      describeFront(
        { package: 'com.halcyonic.xr', activity: 'com.unity3d.player.UnityPlayerGameActivity' },
        'com.halcyonic.xr',
      ),
      'com.halcyonic.xr is in front.',
    );
    assert.equal(
      describeFront(
        { package: 'com.oculus.vrshell', activity: '.HomeActivity' },
        'com.halcyonic.xr',
      ),
      'Another app is in front: com.oculus.vrshell.',
    );
    assert.equal(
      describeFront(null, 'com.halcyonic.xr'),
      'Nothing is in front (the headset may be asleep).',
    );
  });

  test("the testing service's properties read as booleans", () => {
    const text =
      'Result: Bundle[{disable_guardian=false, set_proximity_close=true, disable_dialogs=false, disable_autosleep=false}]';
    assert.deepEqual(readTestingProperties(text), {
      disable_guardian: false,
      set_proximity_close: true,
      disable_dialogs: false,
      disable_autosleep: false,
    });
    assert.deepEqual(readTestingProperties('Error: no such method'), {});
  });

  test('a launch held by a system dialog is found in the log', () => {
    assert.ok(
      isLaunchHeld(
        '10-28 15:11:02.100  1505  1662 I ActivityTaskManager: Launch is blocked because: a Guardian dialog is currently showing. Caching launch',
      ),
    );
    assert.ok(!isLaunchHeld('10-28 15:22:28.340 12037 12087 D Unity   : [EGL] Attaching window'));
  });

  test('media sync being on is read from the capture service line', () => {
    assert.ok(
      isMediaSyncOn(
        '10-28 15:21:32.620 11156 11221 I SimpleMediaSyncUtils: media sync preference is enabled: true',
      ),
    );
    assert.ok(
      !isMediaSyncOn(
        '10-28 15:21:32.620 11156 11221 I SimpleMediaSyncUtils: media sync preference is enabled: false',
      ),
    );
    assert.ok(!isMediaSyncOn(''));
  });
});

describe('packages and bytes', () => {
  test('pm path must name exactly one base.apk', () => {
    assert.deepEqual(readBasePaths('package:/data/app/~~A==/com.example.app-B==/base.apk\n'), [
      '/data/app/~~A==/com.example.app-B==/base.apk',
    ]);
    assert.deepEqual(
      readBasePaths(
        'package:/data/app/x/base.apk\npackage:/data/app/x/split_config.arm64_v8a.apk\n',
      ),
      ['/data/app/x/base.apk', '/data/app/x/split_config.arm64_v8a.apk'],
    );
    assert.deepEqual(readBasePaths(''), []);
  });

  test('a sha256 line gives its 64 hex digits, nothing else', () => {
    const sum = 'a'.repeat(64);
    assert.equal(readSha256(`${sum}  /data/app/x/base.apk\n`), sum);
    assert.equal(readSha256('sha256sum: /data/app/x/base.apk: No such file or directory'), null);
  });

  test('the first install time is read from dumpsys package', () => {
    assert.equal(
      readFirstInstall(
        '    lastUpdateTime=2026-10-28 15:36:01\n      firstInstallTime=2026-10-11 20:35:37\n',
      ),
      '2026-10-11 20:35:37',
    );
    assert.equal(readFirstInstall('Unable to find package: com.example.none'), null);
  });
});

describe('captures', () => {
  test("only the tool's own new files are new", () => {
    const before = [
      'com.example.other-20261022-170844.jpg',
      'com.oculus.vrshell-20261016-020147-0.mp4',
    ];
    const after = [...before, 'com.halcyonic.xr-20261028-152240.jpg', 'notes.txt'];
    assert.deepEqual(newCaptures(before, after), ['com.halcyonic.xr-20261028-152240.jpg']);
    assert.deepEqual(newCaptures(before, before), []);
  });

  test('a capture name has the foreground package, a date and a time', () => {
    assert.ok(captureName.test('com.halcyonic.xr-20261028-152240.jpg'));
    assert.ok(captureName.test('com.oculus.vrshell-20261028-152126-0.mp4'));
    assert.ok(!captureName.test('../com.halcyonic.xr-20261028-152240.jpg'));
    assert.ok(!captureName.test('com.halcyonic.xr-20261028-152240.jpg; rm -rf /'));
  });

  test('a folder listing reads as one name per line, folder headers dropped', () => {
    const text =
      '/sdcard/Oculus/Screenshots:\r\ncom.example.other-20261022-170844.jpg\r\n\r\n/sdcard/Oculus/VideoShots:\r\n';
    assert.deepEqual(readListedFiles(text), ['com.example.other-20261022-170844.jpg']);
  });
});
