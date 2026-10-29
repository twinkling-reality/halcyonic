import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { promisify } from 'node:util';
import {
  confinedProfile,
  confinementKey,
  SANDBOX_EXEC,
  TOOL_PREFIXES,
  toolFoldersFor,
} from './sandbox-profile.ts';

const run = promisify(execFile);

/** A scratch data directory with a project, a confined server's home, the open server's home, and a secret. */
function scene() {
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-confine-test-')));
  const folder = (name: string) => {
    const path = join(root, name);
    mkdirSync(path, { recursive: true });
    return path;
  };
  const scope = {
    project: folder('dev/garden'),
    readable: [folder('data/starters/shared')],
    codexHome: folder('data/codex-confined/k'),
    temp: folder('data/codex-confined/k/tmp'),
    runtimeFolder: folder('runtimes/codex'),
    toolFolders: toolFoldersFor(process.execPath),
    loopbackPorts: [11434],
  };
  const openHome = folder('data/codex-home');
  writeFileSync(join(scope.project, 'inside.txt'), 'inside');
  writeFileSync(join(scope.readable[0] as string, 'shared.txt'), 'shared');
  writeFileSync(join(folder('data'), 'secret.txt'), 'secret');
  return { root, scope, openHome, cleanup: () => rmSync(root, { recursive: true, force: true }) };
}

describe("a confined Codex server's Seatbelt profile (ADR 0041)", () => {
  test('is closed by default: no default allowance, no bind or inbound, no LaunchServices or pasteboard', () => {
    const { scope, cleanup } = scene();
    try {
      const profile = confinedProfile(scope);
      assert.match(profile, /^\(version 1\)/);
      assert.match(profile, /\n\(deny default\)\n/);
      assert.doesNotMatch(profile, /\(allow default\)/);
      assert.doesNotMatch(profile, /network-bind|network-inbound/);
      const services = profile.match(/\(global-name "[^"]+"\)/g) ?? [];
      assert.ok(services.length > 0);
      for (const service of services) {
        assert.doesNotMatch(
          service,
          /launchservices|pasteboard|appleevents|coreservices|lsd\./i,
          service,
        );
      }
      // Said again at the end, after every allowance, so none can reopen them.
      const tail = profile.slice(profile.indexOf("; Halcyonic's confinement"));
      assert.ok(tail.includes('\n(deny lsopen)\n'));
      assert.ok(
        tail.includes('\n(deny mach-lookup (global-name-regex #"^com\\.apple\\.pasteboard\\."))\n'),
      );
      assert.ok(tail.includes('\n(deny appleevent-send)\n'));
      // No preferences IPC: it can expose data outside the read roots.
      assert.doesNotMatch(profile, /cfprefs|user-preference-read/);
    } finally {
      cleanup();
    }
  });

  test("reads only the scope's folders, writes only the project, its own Codex home and temporary folder", () => {
    const { scope, openHome, cleanup } = scene();
    try {
      const profile = confinedProfile(scope);
      const ours = profile.slice(profile.indexOf("; Halcyonic's confinement"));
      const line = (start: string) => ours.split('\n').find((l) => l.startsWith(start)) ?? '';
      const reads = line('(allow file-read* file-test-existence');
      for (const path of [
        scope.project,
        scope.codexHome,
        scope.temp,
        scope.runtimeFolder,
        ...scope.readable,
      ]) {
        assert.ok(reads.includes(`(subpath "${path}")`), path);
      }
      const writes = line('(allow file-write*');
      for (const path of [scope.project, scope.codexHome, scope.temp])
        assert.ok(writes.includes(path));
      for (const path of [scope.runtimeFolder, ...scope.readable, openHome])
        assert.ok(!writes.includes(path));
      assert.ok(!ours.includes(openHome), "the open server's home is not named at all");
      assert.match(ours, /\(allow network-outbound \(remote ip "localhost:11434"\)\)/);
    } finally {
      cleanup();
    }
  });

  test('names a project by a short stable key', () => {
    assert.equal(confinementKey('/a/b'), confinementKey('/a/b'));
    assert.notEqual(confinementKey('/a/b'), confinementKey('/a/c'));
    assert.match(confinementKey('/a/b'), /^[0-9a-f]{16}$/);
  });

  test("gives a tool's package folders under Homebrew or /usr/local, never var or etc, else the folder above its bin", () => {
    const parts = ['Cellar', 'opt', 'lib', 'bin', 'libexec', 'share'];
    // And the one file of etc a Homebrew Node needs to start.
    const under = (prefix: string) => [
      ...parts.map((part) => `${prefix}/${part}`),
      `${prefix}/etc/openssl@3/openssl.cnf`,
    ];
    assert.deepEqual(
      toolFoldersFor('/opt/homebrew/Cellar/node@24/24.15.0/bin/node'),
      under('/opt/homebrew'),
    );
    assert.deepEqual(toolFoldersFor('/usr/local/bin/node'), under('/usr/local'));
    assert.deepEqual(toolFoldersFor('/nowhere/node-v24/bin/node'), ['/nowhere/node-v24']);
  });

  test('on this Mac, a process inside it works in the project and reaches nothing else', {
    skip: process.platform !== 'darwin',
  }, async () => {
    const { root, scope, openHome, cleanup } = scene();
    try {
      const file = join(scope.temp, 'profile.sb');
      writeFileSync(file, confinedProfile(scope));
      const inside = (command: string[]) =>
        run(SANDBOX_EXEC, ['-f', file, ...command]).then(
          ({ stdout }) => ({ code: 0, out: stdout }),
          (error: { code?: number; stdout?: string; stderr?: string }) => ({
            code: typeof error.code === 'number' ? error.code : 1,
            out: `${error.stdout ?? ''}${error.stderr ?? ''}`,
          }),
        );
      const refused = /Operation not permitted/;
      assert.equal((await inside(['/bin/cat', join(scope.project, 'inside.txt')])).out, 'inside');
      assert.equal(
        (await inside(['/bin/cat', join(scope.readable[0] as string, 'shared.txt')])).out,
        'shared',
      );
      assert.equal((await inside(['/usr/bin/touch', join(scope.project, 'made')])).code, 0);
      assert.match((await inside([process.execPath, '--version'])).out, /^v\d+/);
      assert.match((await inside(['/bin/cat', join(root, 'data/secret.txt')])).out, refused);
      assert.match((await inside(['/usr/bin/touch', join(openHome, 'planted.toml')])).out, refused);
      assert.match(
        (await inside(['/usr/bin/touch', join(scope.readable[0] as string, 'made')])).out,
        refused,
      );
      assert.match((await inside(['/bin/ls', '/private/tmp'])).out, refused);
      assert.match((await inside(['/bin/ls', '/Users/Shared'])).out, refused);
      // Package managers' data and configuration (databases live in var): listings go nowhere.
      for (const prefix of TOOL_PREFIXES) {
        for (const part of ['var', 'etc']) {
          if (!existsSync(join(prefix, part))) continue;
          const listed = await inside(['/bin/sh', '-c', `/bin/ls '${prefix}/${part}' > /dev/null`]);
          assert.match(listed.out, refused, `${prefix}/${part}`);
        }
      }
      // Escape targets never act on the owner's apps or data: the pasteboard's contents go nowhere.
      assert.notEqual((await inside(['/bin/sh', '-c', '/usr/bin/pbpaste > /dev/null'])).code, 0);
      assert.match((await inside(['/usr/bin/lsappinfo', 'front'])).out, /\[ NULL \]/);
      assert.notEqual((await inside(['/usr/bin/osascript', '-e', 'return 1'])).code, 0);
      assert.match(
        (
          await inside([
            '/usr/bin/perl',
            '-e',
            'use Socket; socket(S, PF_INET, SOCK_STREAM, 0) or die "socket: $!"; bind(S, sockaddr_in(0, INADDR_ANY)) or die "bind: $!"; print "bound"',
          ])
        ).out,
        /bind: Operation not permitted/,
      );
    } finally {
      cleanup();
    }
  });
});
