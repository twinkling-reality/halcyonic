import assert from 'node:assert/strict';
import { execFile, spawn } from 'node:child_process';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { type AddressInfo, createServer } from 'node:net';
import { homedir, tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { describe, test } from 'node:test';
import { promisify } from 'node:util';
import {
  confinedProfile,
  confinementKey,
  gitDataOutside,
  SANDBOX_EXEC,
  secretsUnder,
  TOOL_PREFIXES,
  toolFoldersFor,
} from './profile.ts';

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
  'tools/.git-credentials',
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
    home: folder('data/codex-confined/k'),
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
        scope.home,
        scope.temp,
        scope.runtimeFolder,
        ...scope.readable,
      ]) {
        assert.ok(reads.includes(`(subpath "${path}")`), path);
      }
      const writes = line('(allow file-write*');
      for (const path of [scope.project, scope.home, scope.temp]) assert.ok(writes.includes(path));
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
      writeFileSync(join(scope.home, 'own.txt'), 'own');
      const inside = (command: string[]) =>
        run(SANDBOX_EXEC, ['-f', file, ...command]).then(
          ({ stdout }) => stdout,
          (error: { stdout?: string; stderr?: string }) =>
            `${error.stdout ?? ''}${error.stderr ?? ''}`,
        );
      const refused = /Operation not permitted/;
      assert.match(await inside(['/bin/cat', join(root, 'data/secret.txt')]), refused);
      assert.match(await inside(['/bin/ls', join(root, 'data')]), refused);
      assert.equal(await inside(['/bin/cat', join(scope.home, 'own.txt')]), 'own');
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
      assert.equal(await inside(['/bin/cat', join(scope.home, 'own.txt')]), 'own');
      assert.equal(await inside(['/usr/bin/touch', join(scope.home, 'made')]), '');
      assert.equal(
        await inside(['/bin/cat', join(scope.readable[0] as string, 'shared.txt')]),
        'shared',
      );
    } finally {
      cleanup();
    }
  });

  test('lets the folders between a hidden one and its own be found, never listed or read (CX1)', () => {
    const { root, scope, cleanup } = scene();
    try {
      const profile = confinedProfile(scope);
      const ours = profile.slice(profile.indexOf("; Halcyonic's confinement"));
      const found = ours
        .split('\n')
        .find((l) => l.startsWith('(allow file-read-metadata file-test-existence'));
      assert.equal(
        found,
        `(allow file-read-metadata file-test-existence ${[
          'data',
          'data/codex-confined',
          'data/starters',
        ]
          .map((name) => `(literal "${join(root, name)}")`)
          .join(' ')})`,
      );
      const hidden = ours.indexOf('(deny file-read* file-test-existence (subpath');
      assert.ok(ours.indexOf(found) > hidden, 'found after the hidden folder is refused');
      // Nothing of the data folder is opened wider: no subpath of it or of codex-confined, no reading of data.
      for (const allowed of ours.split('\n').filter((l) => l.startsWith('(allow'))) {
        for (const path of [join(root, 'data'), join(root, 'data/codex-confined')])
          assert.ok(!allowed.includes(`(subpath "${path}")`), allowed);
      }
      assert.doesNotMatch(found, /file-read-data|file-read\*|subpath/);
    } finally {
      cleanup();
    }
  });

  test('on this Mac, its own folders resolve inside it, and the data folder is still never listed or read (CX1)', {
    skip: process.platform !== 'darwin',
  }, async () => {
    const { root, scope, openHome, cleanup } = scene();
    try {
      const file = join(scope.temp, 'profile.sb');
      writeFileSync(file, confinedProfile(scope));
      writeFileSync(join(openHome, 'config.toml'), 'open');
      const inside = (command: string[]) =>
        run(SANDBOX_EXEC, ['-f', file, ...command]).then(
          ({ stdout }) => ({ code: 0, out: stdout }),
          (error: { code?: number; stdout?: string; stderr?: string }) => ({
            code: typeof error.code === 'number' ? error.code : 1,
            out: `${error.stdout ?? ''}${error.stderr ?? ''}`,
          }),
        );
      // What Codex does first with its home: resolve it through every folder above it (canonicalize).
      assert.equal((await inside(['/bin/realpath', scope.home])).out.trim(), scope.home);
      assert.equal((await inside(['/bin/realpath', scope.temp])).out.trim(), scope.temp);
      assert.equal(
        (await inside(['/bin/realpath', scope.readable[0] as string])).out.trim(),
        scope.readable[0],
      );
      // Tried with the denial in place, on markers of our own: nothing in the data folder is listed, read or found.
      const refused = /Operation not permitted/;
      assert.match((await inside(['/bin/ls', join(root, 'data')])).out, refused);
      assert.match((await inside(['/bin/ls', join(root, 'data/codex-confined')])).out, refused);
      assert.match((await inside(['/bin/cat', join(root, 'data/secret.txt')])).out, refused);
      assert.match((await inside(['/usr/bin/stat', join(root, 'data/secret.txt')])).out, refused);
      assert.notEqual((await inside(['/bin/test', '-e', join(root, 'data/secret.txt')])).code, 0);
      assert.notEqual((await inside(['/bin/test', '-e', openHome])).code, 0);
      assert.match((await inside(['/bin/cat', join(openHome, 'config.toml')])).out, refused);
      // Nor the person's home folder or their own Codex settings.
      assert.match((await inside(['/bin/ls', homedir()])).out, refused);
      if (existsSync(join(homedir(), '.codex')))
        assert.match((await inside(['/bin/ls', join(homedir(), '.codex')])).out, refused);
    } finally {
      cleanup();
    }
  });

  test('on this Mac, the pinned Codex app-server starts inside it with its home in the data folder (CX1)', {
    skip:
      process.platform !== 'darwin' || process.env.HALCYONIC_TEST_CODEX_BIN === undefined
        ? 'set HALCYONIC_TEST_CODEX_BIN to the pinned Codex binary'
        : false,
  }, async () => {
    const binary = process.env.HALCYONIC_TEST_CODEX_BIN as string;
    const { scope, cleanup } = scene();
    try {
      const confined = { ...scope, runtimeFolder: dirname(realpathSync(binary)) };
      const file = join(scope.temp, 'profile.sb');
      writeFileSync(file, confinedProfile(confined));
      // Closed input: the server starts, reads its home and settings, then ends as its client went away.
      const ended = await new Promise<{ code: number | null; err: string }>((resolve) => {
        const child = spawn(SANDBOX_EXEC, ['-f', file, binary, 'app-server'], {
          cwd: scope.project,
          env: {
            HOME: homedir(),
            PATH: '/usr/bin:/bin',
            CODEX_HOME: scope.home,
            TMPDIR: scope.temp,
          },
          stdio: ['ignore', 'ignore', 'pipe'],
        });
        let err = '';
        child.stderr.on('data', (chunk: Buffer) => {
          err += chunk.toString();
        });
        const timer = setTimeout(() => child.kill('SIGTERM'), 15_000);
        child.on('close', (code) => {
          clearTimeout(timer);
          resolve({ code, err });
        });
      });
      assert.doesNotMatch(ended.err, /canonicalize/, ended.err);
      assert.equal(ended.code, 0, ended.err);
    } finally {
      cleanup();
    }
  });

  test('refuses a scope whose own folder is or holds a hidden one, so the re-allow can never reopen it (AUDIT M18C1-A-S1)', () => {
    const { root, scope, cleanup } = scene();
    try {
      const data = join(root, 'data');
      // The project, or a tool folder, at or above the data folder: refused, whatever the original.
      for (const wrong of [
        { ...scope, hidden: [data], project: data },
        { ...scope, hidden: [data], toolFolders: [root] },
        { ...scope, hidden: [scope.home] },
        { ...scope, originals: [], hidden: [scope.readable[0] as string] },
      ]) {
        assert.throws(() => confinedProfile(wrong), /hidden/);
      }
      // Its own folders inside the hidden one are allowed again, as they must be.
      assert.doesNotThrow(() => confinedProfile({ ...scope, hidden: [data] }));
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
      // macOS's own OpenSSL configuration, which a Node build may read to start: that file alone,
      // never the folder's private keys or certificates beside it (TASKS's run, 2026-11-10).
      assert.equal((await inside(['/bin/cat', '/System/Library/OpenSSL/openssl.cnf'])).code, 0);
      for (const beside of ['/System/Library/OpenSSL/private', '/System/Library/OpenSSL/certs'])
        assert.match((await inside(['/bin/ls', beside])).out, refused, beside);
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

describe('a server of its own port, listening on it alone (BOX-3b)', () => {
  test('binds and is reached only on the ports the scope names, on loopback', () => {
    const { scope, cleanup } = scene();
    try {
      const profile = confinedProfile({ ...scope, listenPorts: [47123] });
      assert.ok(
        profile.includes('\n(allow network-bind network-inbound (local ip "localhost:47123"))\n'),
      );
      assert.equal(profile.match(/network-bind|network-inbound/g)?.length, 2);
    } finally {
      cleanup();
    }
  });

  test('on this Mac, a bind on its own port works and on any other is refused', {
    skip: process.platform !== 'darwin',
  }, async () => {
    const { scope, cleanup } = scene();
    try {
      const free = () =>
        new Promise<number>((resolve, reject) => {
          const server = createServer();
          server.once('error', reject);
          server.listen(0, '127.0.0.1', () => {
            const { port } = server.address() as AddressInfo;
            server.close(() => resolve(port));
          });
        });
      const own = await free();
      const other = await free();
      const file = join(scope.temp, 'profile.sb');
      writeFileSync(file, confinedProfile({ ...scope, listenPorts: [own] }));
      const bind = (port: number) =>
        run(SANDBOX_EXEC, [
          '-f',
          file,
          '/usr/bin/perl',
          '-e',
          `use Socket; socket(S, PF_INET, SOCK_STREAM, 0) or die "socket: $!"; bind(S, sockaddr_in(${port}, INADDR_LOOPBACK)) or die "bind: $!"; listen(S, 1) or die "listen: $!"; print "bound"`,
        ]).then(
          ({ stdout }) => stdout,
          (error: { stdout?: string; stderr?: string }) =>
            `${error.stdout ?? ''}${error.stderr ?? ''}`,
        );
      assert.equal(await bind(own), 'bound');
      assert.match(await bind(other), /bind: Operation not permitted/);
    } finally {
      cleanup();
    }
  });
});

describe("a folder's git data outside it, read only (BOX)", () => {
  const temporary = () => realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-gitdata-test-')));

  test("a worktree's and a submodule's own git data and their repository's .git; nothing for any other shape", () => {
    const root = temporary();
    try {
      const made = (path: string) => {
        mkdirSync(join(root, path), { recursive: true });
        return join(root, path);
      };
      const common = made('repo/.git');
      const worktree = made('wt');
      writeFileSync(join(worktree, '.git'), `gitdir: ${join(common, 'worktrees', 'wt')}\n`);
      made('repo/.git/worktrees/wt');
      // Not named back by the repository yet: nothing (AUDIT BOX-1 F1).
      assert.deepEqual(gitDataOutside(worktree), []);
      // git names the worktree back, as it does when it makes one.
      writeFileSync(join(common, 'worktrees', 'wt', 'gitdir'), `${join(worktree, '.git')}\n`);
      assert.deepEqual(gitDataOutside(worktree), [common, join(common, 'worktrees', 'wt')]);
      // A submodule names its data relatively, and its config names it back.
      const submodule = made('repo/lib');
      made('repo/.git/modules/lib');
      writeFileSync(join(submodule, '.git'), 'gitdir: ../.git/modules/lib\n');
      assert.deepEqual(gitDataOutside(submodule), []);
      writeFileSync(
        join(common, 'modules', 'lib', 'config'),
        '[core]\n\tworktree = ../../../lib\n',
      );
      assert.deepEqual(gitDataOutside(submodule), [common, join(common, 'modules', 'lib')]);
      // Another folder naming the same worktree is not named back.
      const thief = made('thief');
      writeFileSync(join(thief, '.git'), `gitdir: ${join(common, 'worktrees', 'wt')}\n`);
      assert.deepEqual(gitDataOutside(thief), []);
      // Its own repository, no .git at all, or a .git file naming any other folder: nothing.
      assert.deepEqual(gitDataOutside(join(root, 'repo')), []);
      assert.deepEqual(gitDataOutside(made('plain')), []);
      for (const named of [
        '/',
        homedir(),
        join(root, 'repo'),
        join(common, 'worktrees'),
        join(root, 'elsewhere'),
      ]) {
        const odd = made(`odd-${confinementKey(named)}`);
        writeFileSync(join(odd, '.git'), `gitdir: ${named}\n`);
        assert.deepEqual(gitDataOutside(odd), [], named);
      }
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  test("on this Mac, a .git file the agent writes opens no other repository's data (AUDIT BOX-1 F1)", {
    skip: process.platform !== 'darwin',
  }, async () => {
    const root = temporary();
    try {
      // A repository of the test's own beside the confined project, its config holding a marker.
      const other = join(root, 'other-private-repo');
      mkdirSync(other);
      await run('/usr/bin/git', ['init', '-q', '-b', 'main'], { cwd: other });
      await run('/usr/bin/git', ['config', 'halcyonic.marker', 'marker-7b3f'], { cwd: other });
      // A real worktree of it elsewhere, so a named folder that exists is tried too.
      await run(
        '/usr/bin/git',
        [
          '-c',
          'user.email=box@example.invalid',
          '-c',
          'user.name=box',
          'commit',
          '-q',
          '--allow-empty',
          '-m',
          'box',
        ],
        { cwd: other },
      );
      await run('/usr/bin/git', ['worktree', 'add', '-q', join(root, 'real-wt'), '-b', 'real'], {
        cwd: other,
      });
      const project = join(root, 'project');
      const home = join(root, 'home');
      const temp = join(root, 'temp');
      for (const folder of [project, home, temp]) mkdirSync(folder);
      for (const pointer of [
        null,
        `${join(other, '.git', 'worktrees', 'never-made')}`,
        `${join(other, '.git', 'modules', 'x')}`,
        '../other-private-repo/.git/worktrees/z',
        `${join(other, '.git', 'worktrees', 'real-wt')}`,
      ]) {
        rmSync(join(project, '.git'), { force: true });
        if (pointer !== null) writeFileSync(join(project, '.git'), `gitdir: ${pointer}\n`);
        assert.deepEqual(gitDataOutside(project), [], String(pointer));
        const file = join(temp, 'profile.sb');
        writeFileSync(
          file,
          confinedProfile({
            project,
            readable: [],
            home: home,
            temp,
            runtimeFolder: home,
            toolFolders: toolFoldersFor(process.execPath),
            loopbackPorts: [11434],
          }),
        );
        const read = await run(SANDBOX_EXEC, [
          '-f',
          file,
          '/bin/cat',
          join(other, '.git', 'config'),
        ]).then(
          ({ stdout }) => stdout,
          (error: { stderr?: string }) => error.stderr ?? '',
        );
        assert.match(read, /Operation not permitted/, String(pointer));
        assert.doesNotMatch(read, /marker-7b3f/, String(pointer));
      }
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  test('on this Mac, git reads a worktree inside the profile, and its shared data is never written', {
    skip: process.platform !== 'darwin',
  }, async () => {
    const root = temporary();
    try {
      const repo = join(root, 'repo');
      mkdirSync(repo);
      const git = (cwd: string, ...args: string[]) =>
        run(
          '/usr/bin/git',
          ['-c', 'user.email=box@example.invalid', '-c', 'user.name=box', ...args],
          { cwd },
        );
      await git(repo, 'init', '-q', '-b', 'main');
      writeFileSync(join(repo, 'README.md'), 'box\n');
      await git(repo, 'add', '.');
      await git(repo, 'commit', '-q', '-m', 'box');
      const worktree = join(root, 'wt');
      await git(repo, 'worktree', 'add', '-q', worktree, '-b', 'wt');
      const home = join(root, 'home');
      const temp = join(root, 'temp');
      mkdirSync(home);
      mkdirSync(temp);
      const file = join(temp, 'profile.sb');
      writeFileSync(
        file,
        confinedProfile({
          project: worktree,
          readable: [],
          home: home,
          temp,
          runtimeFolder: home,
          toolFolders: toolFoldersFor(process.execPath),
          loopbackPorts: [11434],
        }),
      );
      // The developer folder's own git, not Apple's /usr/bin shim, which writes a cache in the
      // person's temporary folder; HOME and system configuration as the confined server gives them.
      const developer = (await run('/usr/bin/xcode-select', ['-p'])).stdout.trim();
      const inside = (...args: string[]) =>
        run(SANDBOX_EXEC, ['-f', file, join(developer, 'usr', 'bin', 'git'), ...args], {
          cwd: worktree,
          env: { PATH: '/usr/bin:/bin', HOME: temp, GIT_CONFIG_NOSYSTEM: '1' },
        }).then(
          ({ stdout }) => ({ code: 0, out: stdout }),
          (error: { code?: number; stdout?: string; stderr?: string }) => ({
            code: typeof error.code === 'number' ? error.code : 1,
            out: `${error.stdout ?? ''}${error.stderr ?? ''}`,
          }),
        );
      const log = await inside('log', '--oneline', '-1');
      assert.equal(log.code, 0, log.out);
      assert.match(log.out, / box\n$/);
      const status = await inside('status', '--short', '--branch');
      assert.equal(status.code, 0, status.out);
      assert.match(status.out, /^## wt/);
      // A commit writes the shared objects and refs: refused, in git's own words, and nothing changed.
      writeFileSync(join(worktree, 'new.txt'), 'new\n');
      const before = (await git(repo, 'rev-parse', 'refs/heads/wt')).stdout;
      const added = await inside('add', 'new.txt');
      const committed = await inside(
        '-c',
        'user.email=box@example.invalid',
        '-c',
        'user.name=box',
        'commit',
        '-q',
        '-m',
        'unasked',
      );
      assert.notEqual(
        added.code === 0 && committed.code === 0,
        true,
        `${added.out}${committed.out}`,
      );
      assert.match(
        `${added.out}${committed.out}`,
        /Operation not permitted|Unable to create|unable to/i,
      );
      assert.equal((await git(repo, 'rev-parse', 'refs/heads/wt')).stdout, before);
      // The repository's own working files stay unreadable: only its .git is opened.
      const readme = await run(SANDBOX_EXEC, [
        '-f',
        file,
        '/bin/cat',
        join(repo, 'README.md'),
      ]).then(
        () => 'read',
        (error: { stderr?: string }) => error.stderr ?? '',
      );
      assert.match(readme, /Operation not permitted/);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
});

describe('another bound project inside a confined folder (AUDIT BOX-3 F1)', () => {
  test('on this Mac, its folder is refused to reads, writes and links, while the rest of the folder works', {
    skip: process.platform !== 'darwin',
  }, async () => {
    const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-inner-test-')));
    try {
      const project = join(root, 'outer');
      const inner = join(project, 'inner-project');
      mkdirSync(inner, { recursive: true });
      writeFileSync(join(inner, 'kept.txt'), 'kept-3e1a\n');
      writeFileSync(join(project, 'own.txt'), 'own\n');
      const home = join(root, 'home');
      const temp = join(root, 'temp');
      mkdirSync(home);
      mkdirSync(temp);
      const file = join(temp, 'profile.sb');
      writeFileSync(
        file,
        confinedProfile({
          project,
          readable: [],
          inner: [inner],
          home: home,
          temp,
          runtimeFolder: home,
          toolFolders: toolFoldersFor(process.execPath),
          loopbackPorts: [],
        }),
      );
      const inside = (command: string[]) =>
        run(SANDBOX_EXEC, ['-f', file, ...command]).then(
          ({ stdout }) => ({ code: 0, out: stdout }),
          (error: { code?: number; stdout?: string; stderr?: string }) => ({
            code: typeof error.code === 'number' ? error.code : 1,
            out: `${error.stdout ?? ''}${error.stderr ?? ''}`,
          }),
        );
      const refused = /Operation not permitted/;
      // The control: the folder's own files read and write.
      assert.equal((await inside(['/bin/cat', join(project, 'own.txt')])).out, 'own\n');
      assert.equal((await inside(['/usr/bin/touch', join(project, 'made')])).code, 0);
      // The other project: not read, not listed, not written, not linked into the folder.
      assert.match((await inside(['/bin/cat', join(inner, 'kept.txt')])).out, refused);
      assert.match((await inside(['/bin/ls', inner])).out, refused);
      assert.match((await inside(['/usr/bin/touch', join(inner, 'marker.txt')])).out, refused);
      assert.match(
        (await inside(['/bin/sh', '-c', `echo removed > '${join(inner, 'kept.txt')}'`])).out,
        refused,
      );
      assert.match(
        (await inside(['/bin/ln', join(inner, 'kept.txt'), join(project, 'linked')])).out,
        refused,
      );
      assert.match((await inside(['/bin/mv', inner, join(project, 'moved')])).out, refused);
      assert.equal(readFileSync(join(inner, 'kept.txt'), 'utf8'), 'kept-3e1a\n');
      assert.equal(existsSync(join(inner, 'marker.txt')), false);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
});
