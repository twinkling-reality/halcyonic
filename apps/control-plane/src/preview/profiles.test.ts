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
      const profile = chromeProfile({ home: scope.home, folder, port: 47882 });
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
      assert.throws(() => chromeProfile({ home: scope.home, folder, port: 0 }), /not a port/);
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
