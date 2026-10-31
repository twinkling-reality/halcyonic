import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { promisify } from 'node:util';
import { buildProfile, chromeProfile, SANDBOX_EXEC } from './profiles.ts';

const run = promisify(execFile);

/** A scratch folder standing for the home folder, with a project, a starter, a cache and a secret. */
function scene() {
  const home = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-preview-profiles-')));
  const folder = (name: string) => {
    const path = join(home, name);
    mkdirSync(path, { recursive: true });
    return path;
  };
  const scope = {
    home,
    project: folder('dev/garden'),
    starter: folder('data/starters/iwsdk-vr'),
    cache: folder('data/checks/garden'),
    profiles: folder('data/starters/iwsdk-vr/input-profiles'),
  };
  writeFileSync(join(scope.project, 'index.html'), 'page');
  writeFileSync(join(scope.starter, 'shared.txt'), 'shared');
  writeFileSync(join(folder('data'), 'journal.db'), 'secret');
  return { scope, cleanup: () => rmSync(home, { recursive: true, force: true }) };
}

describe("the preview's Seatbelt profiles (ADR 0037)", () => {
  test('the build profile: no outbound connection, writes only in the cache, reads of the home folder only as named', () => {
    const { scope, cleanup } = scene();
    try {
      const profile = buildProfile(scope);
      assert.match(profile, /\(deny network-outbound\)\n/);
      assert.doesNotMatch(profile, /allow network-outbound/);
      const writes =
        profile.split('\n').find((line) => line.startsWith('(allow file-write*')) ?? '';
      assert.ok(writes.includes(scope.cache));
      for (const path of [scope.project, scope.starter])
        assert.ok(!writes.includes(`"${path}"`), path);
      assert.match(profile, new RegExp(`\\(deny file-read\\* \\(subpath "${scope.home}"\\)\\)`));
      for (const rule of [
        '(deny lsopen)',
        '(deny mach-lookup (global-name-regex #"^com\\.apple\\.pasteboard\\."))',
        '(deny appleevent-send)',
      ]) {
        assert.ok(profile.includes(`\n${rule}\n`), rule);
      }
      // Later rules win in Seatbelt: the exceptions come after the deny.
      assert.ok(profile.indexOf('(deny file-read*') < profile.indexOf('(allow file-read*'));
    } finally {
      cleanup();
    }
  });

  test("Chrome's profile: out only to the preview's port, no LaunchServices, inbound only on loopback and its own sockets", () => {
    const { scope, cleanup } = scene();
    try {
      const folder = join(scope.cache, 'chrome');
      const browser = join(scope.home, 'browser');
      const profile = chromeProfile({ home: scope.home, folder, port: 47882, browser });
      // Its own browser is read; every system service and driver is refused but those it draws with.
      assert.match(profile, new RegExp(`\\(allow file-read\\* \\(subpath "${browser}"\\)\\)`));
      assert.match(profile, /\n\(deny mach-lookup\)\n\(deny iokit-open\)\n/);
      const allowed = profile
        .split('\n')
        .filter((line) => /^\(allow (mach-lookup|iokit-open)/.test(line));
      assert.deepEqual(allowed, [
        '(allow mach-lookup (global-name-regex #"^(org\\.chromium\\.Chromium|com\\.google\\.(Chrome|chrome\\.for\\.testing))\\.MachPortRendezvousServer\\.[0-9]+$"))',
        '(allow mach-lookup (global-name "com.apple.MTLCompilerService"))',
        '(allow iokit-open (iokit-user-client-class "AGXDeviceUserClient"))',
        '(allow iokit-open (iokit-user-client-class "IOSurfaceRootUserClient"))',
      ]);
      const withSound = chromeProfile({
        home: scope.home,
        folder,
        port: 47882,
        browser,
        sound: true,
      });
      assert.deepEqual(
        withSound
          .split('\n')
          .filter((line) => /^\(allow mach-lookup \(global-name "com\.apple\.audio/.test(line)),
        [
          '(allow mach-lookup (global-name "com.apple.audio.audiohald"))',
          '(allow mach-lookup (global-name "com.apple.audio.AudioComponentRegistrar"))',
          '(allow mach-lookup (global-name "com.apple.audio.AudioSession"))',
        ],
      );
      for (const text of [profile, withSound])
        assert.doesNotMatch(
          text,
          /launchservicesd|tccd|windowserver|pasteboard|CARenderServer|coreservicesd/,
        );
      assert.match(
        profile,
        /\(deny network-outbound\)\n\(allow network-outbound \(remote ip "localhost:47882"\)\)\n/,
      );
      assert.match(profile, /\n\(deny lsopen\)\n/);
      assert.match(
        profile,
        /\n\(deny network-inbound\)\n\(allow network-inbound \(local ip "localhost:\*"\) \(local unix-socket \(subpath "[^"]+\/chrome"\)\)\)\n/,
      );
      assert.match(profile, /\(deny process-exec \(regex #"\/GoogleUpdater\\\.app\/"\)\)/);
      assert.throws(
        () => chromeProfile({ home: scope.home, folder, port: 0, browser }),
        /not a port/,
      );
    } finally {
      cleanup();
    }
  });

  test('on this Mac, the build profile lets a process read the project and starter and write the cache, and nothing else', {
    skip: process.platform !== 'darwin',
  }, async () => {
    const { scope, cleanup } = scene();
    try {
      const file = join(scope.cache, 'build.sb');
      writeFileSync(file, buildProfile(scope));
      // Escape-shaped tries only touch the scratch folder of this test (LANE-RULES 11b).
      const inside = (command: string[]) =>
        run(SANDBOX_EXEC, ['-f', file, ...command], { cwd: scope.cache }).then(
          ({ stdout }) => stdout,
          (error: { stderr?: string }) => error.stderr ?? 'failed',
        );
      assert.equal(await inside(['/bin/cat', join(scope.project, 'index.html')]), 'page');
      assert.equal(await inside(['/bin/cat', join(scope.starter, 'shared.txt')]), 'shared');
      assert.equal(await inside(['/usr/bin/touch', join(scope.cache, 'made')]), '');
      assert.match(
        await inside(['/bin/cat', join(scope.home, 'data', 'journal.db')]),
        /Operation not permitted/,
      );
      assert.match(
        await inside(['/usr/bin/touch', join(scope.project, 'made')]),
        /Operation not permitted/,
      );
    } finally {
      cleanup();
    }
  });
});
