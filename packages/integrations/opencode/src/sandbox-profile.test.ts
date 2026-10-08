import assert from 'node:assert/strict';
import { mkdtempSync, realpathSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { openCodeFolders, personalSecrets, sandboxProfile } from './sandbox-profile.ts';

describe("Halcyonic's sandbox profile for OpenCode (ADR 0028)", () => {
  test('denies the network but loopback, every write but the scope, and every read it names', () => {
    const folder = mkdtempSync(join(tmpdir(), 'halcyonic-profile-'));
    try {
      const profile = sandboxProfile({
        loopbackPorts: [11434, 47800],
        writable: [folder, join(folder, 'later/deeper'), '/not/there/yet'],
        unreadable: ['/Users/someone/.ssh'],
      });
      const lines = profile.trim().split('\n');
      // Only the given ports on loopback: no other program's listener on this Mac.
      assert.deepEqual(lines.slice(0, 6), [
        '(version 1)',
        '(allow default)',
        '(deny network-outbound)',
        '(allow network-outbound (remote ip "localhost:11434"))',
        '(allow network-outbound (remote ip "localhost:47800"))',
        '(deny file-write*)',
      ]);
      // Seatbelt matches resolved paths (/var is /private/var), so a folder is given as resolved,
      // through its nearest existing folder when it is not made yet, and one with none as written.
      assert.ok(lines[6]?.includes(`(subpath ${JSON.stringify(realpathSync(folder))})`));
      assert.ok(
        lines[6]?.includes(
          `(subpath ${JSON.stringify(join(realpathSync(folder), 'later/deeper'))})`,
        ),
      );
      assert.ok(lines[6]?.includes('(subpath "/not/there/yet")'));
      assert.ok(lines[6]?.includes('(literal "/dev/null")'));
      assert.equal(
        lines[7],
        '(deny file-read* (subpath "/Users/someone/.ssh") (literal "/Users/someone/.ssh"))',
      );
    } finally {
      rmSync(folder, { recursive: true, force: true });
    }
  });

  test('reads inside an unreadable folder stay allowed where named, after the deny', () => {
    const lines = sandboxProfile({
      loopbackPorts: [],
      writable: [],
      unreadable: ['/d'],
      readable: ['/d/runtimes'],
    })
      .trim()
      .split('\n');
    const deny = lines.findIndex((line) => line.startsWith('(deny file-read*'));
    const allow = lines.findIndex((line) => line.startsWith('(allow file-read*'));
    assert.ok(deny > 0 && allow > deny, 'the allow comes after the deny, so it wins');
    assert.equal(lines[allow], '(allow file-read* (subpath "/d/runtimes"))');
  });

  test("OpenCode's own folders follow the XDG variables, else their defaults, and the temporary folder", () => {
    assert.deepEqual(openCodeFolders({ HOME: '/h', TMPDIR: '/t/' }), [
      '/h/.local/share/opencode',
      '/h/.local/state/opencode',
      '/h/.cache/opencode',
      '/t/',
    ]);
    assert.deepEqual(
      openCodeFolders({
        HOME: '/h',
        XDG_DATA_HOME: '/d',
        XDG_STATE_HOME: '/s',
        XDG_CACHE_HOME: '/c',
      }),
      ['/d/opencode', '/s/opencode', '/c/opencode'],
    );
  });

  test("the person's credentials are under their home", () => {
    const secrets = personalSecrets('/h');
    for (const path of ['/h/.ssh', '/h/.aws', '/h/Library/Keychains', '/h/.npmrc']) {
      assert.ok(secrets.includes(path), path);
    }
  });
});
