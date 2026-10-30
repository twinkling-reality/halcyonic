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
  secretsUnder,
  TOOL_PREFIXES,
  toolFoldersFor,
} from './sandbox-profile.ts';

const run = promisify(execFile);

/** Files an original holds that its VR version's agent must not read, each a marker of our own. */
const ORIGINAL_SECRETS = [
  '.git/config',
  'src/.git/HEAD',
  '.env',
  '.env.local',
  'src/.env.production',
  'server.pem',
  'keys/private.key',
  'cert.p12',
  'store.pfx',
  'release.keystore',
  'signing.jks',
  'id_rsa',
  'deploy/id_ed25519.pub',
  '.npmrc',
  '.netrc',
  '.pypirc',
  'credentials.json',
  'config/secrets.yml',
  'Release/Upload.PEM',
];

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
    // The original a VR version is made from (M1.8c-1): read only, without its version control or secrets.
    originals: [folder('dev/web game.v2')],
    // Halcyonic's data folder, refused outright; the confined server's own home and the starters in it stay.
    hidden: [folder('data')],
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
  const original = scope.originals[0] as string;
  writeFileSync(join(original, 'game.js'), 'game');
  writeFileSync(join(folder('dev/web game.v2/src'), 'level.json'), 'level');
  for (const name of ORIGINAL_SECRETS) {
    const path = join(original, name);
    mkdirSync(join(path, '..'), { recursive: true });
    writeFileSync(path, 'secret');
  }
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
      // The original is read only, and its version control and secret-shaped files are refused after the allow.
      const original = scope.originals[0] as string;
      assert.ok(reads.includes(`(subpath "${original}")`));
      assert.ok(!writes.includes(original));
      const denied = line('(deny file-read* file-test-existence');
      assert.ok(ours.indexOf(denied) > ours.indexOf(reads), 'the denial comes after the allow');
      assert.ok(denied.includes('web game\\.v2'), denied);
      assert.match(ours, /\(allow network-outbound \(remote ip "localhost:11434"\)\)/);
    } finally {
      cleanup();
    }
  });

  test('an original that holds the data folder still never shows it, while the scope keeps its own folders (AUDIT M18C1-M1)', {
    skip: process.platform !== 'darwin',
  }, async () => {
    const { root, scope, cleanup } = scene();
    try {
      // The host refuses such an original; the profile holds even if one got through.
      const wide = { ...scope, originals: [root], hidden: [join(root, 'data')] };
      const file = join(scope.temp, 'profile.sb');
      writeFileSync(file, confinedProfile(wide));
      writeFileSync(join(scope.codexHome, 'own.txt'), 'own');
      const inside = (command: string[]) =>
        run(SANDBOX_EXEC, ['-f', file, ...command]).then(
          ({ stdout }) => stdout,
          (error: { stdout?: string; stderr?: string }) =>
            `${error.stdout ?? ''}${error.stderr ?? ''}`,
        );
      const refused = /Operation not permitted/;
      assert.match(await inside(['/bin/cat', join(root, 'data/secret.txt')]), refused);
      assert.match(await inside(['/bin/ls', join(root, 'data')]), refused);
      assert.equal(await inside(['/bin/cat', join(scope.codexHome, 'own.txt')]), 'own');
      assert.equal(
        await inside(['/bin/cat', join(scope.readable[0] as string, 'shared.txt')]),
        'shared',
      );
      assert.equal(await inside(['/bin/cat', join(scope.project, 'inside.txt')]), 'inside');
      assert.equal(
        await inside(['/bin/cat', join(scope.originals[0] as string, 'game.js')]),
        'game',
      );
      // With no original, the data folder is refused all the same and the server's own folders in it still work.
      const { originals: _none, ...plain } = scope;
      writeFileSync(file, confinedProfile({ ...plain, hidden: [join(root, 'data')] }));
      assert.match(await inside(['/bin/cat', join(root, 'data/secret.txt')]), refused);
      assert.equal(await inside(['/bin/cat', join(scope.codexHome, 'own.txt')]), 'own');
      assert.equal(await inside(['/usr/bin/touch', join(scope.codexHome, 'made')]), '');
      assert.equal(
        await inside(['/bin/cat', join(scope.readable[0] as string, 'shared.txt')]),
        'shared',
      );
    } finally {
      cleanup();
    }
  });

  test("refuses an original's folder whose name its rules cannot quote, rather than leave it open", () => {
    assert.throws(() => secretsUnder('/Users/someone/dev/say "hi"'), /cannot name/);
    assert.throws(() => secretsUnder('/Users/someone/dev/two\nlines'), /cannot name/);
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
      // The original (M1.8c-1): its game is readable, nothing of it is writable, and its version control and
      // secret-shaped files are refused, each tried with the denial in place, on markers of our own.
      const original = scope.originals[0] as string;
      assert.equal((await inside(['/bin/cat', join(original, 'game.js')])).out, 'game');
      assert.equal((await inside(['/bin/cat', join(original, 'src/level.json')])).out, 'level');
      assert.match((await inside(['/usr/bin/touch', join(original, 'made')])).out, refused);
      for (const name of ORIGINAL_SECRETS)
        assert.match((await inside(['/bin/cat', join(original, name)])).out, refused, name);
      assert.match((await inside(['/bin/ls', join(original, '.git')])).out, refused);
      // Nothing it may not read can be hard-linked into its own folder to be read there (AUDIT M18C1-S1).
      for (const source of [join(root, 'data/secret.txt'), join(original, '.env')]) {
        const link = join(scope.project, 'linked');
        assert.match((await inside(['/bin/ln', source, link])).out, refused, source);
        assert.equal(existsSync(link), false);
      }
      // A name that only looks like one is still readable: the rule is for the file, not the word.
      writeFileSync(join(original, 'environment.js'), 'env');
      assert.equal((await inside(['/bin/cat', join(original, 'environment.js')])).out, 'env');
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
