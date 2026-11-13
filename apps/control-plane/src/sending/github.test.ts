import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, test } from 'node:test';
import type { ProjectId } from '@halcyonic/contracts';
import { GitHubSender, gitSaid, sendTitle } from './github.ts';

// Send to GitHub (ADR 0039, amended), proven only against stand-ins: github.com is a local bare
// repository the test's own git settings point https://github.com/owner/garden.git at, and `gh` is a
// script that records what it was asked. No test reaches github.com or uses anyone's sign-in.

const GIT = '/usr/bin/git';
const PROJECT = '01a19000-0000-7000-8000-00000000ab01' as ProjectId;
const REMOTE = 'https://github.com/owner/garden.git';

let root = '';
let home = '';
let folder = '';
let origin = '';
let ghLog = '';
let marker = '';
let ghPath = '';

function git(cwd: string, ...args: string[]): string {
  return execFileSync(GIT, args, {
    cwd,
    env: { PATH: '/usr/bin:/bin', HOME: home, GIT_CONFIG_NOSYSTEM: '1' },
  })
    .toString()
    .trim();
}

function sender(
  options: {
    gh?: string | null;
    env?: NodeJS.ProcessEnv;
    held?: readonly { what: string; value: string }[];
    historyMost?: number;
  } = {},
) {
  return new GitHubSender({
    git: GIT,
    gh: options.gh === undefined ? ghPath : options.gh,
    dataDir: join(root, 'data'),
    env: options.env ?? { HOME: home },
    ...(options.held === undefined ? {} : { held: () => options.held ?? [] }),
    ...(options.historyMost === undefined ? {} : { historyMost: options.historyMost }),
  });
}

/** A made-up secret the host holds in these tests: never a real key. */
const HELD = [{ what: 'MY_SERVICE_KEY', value: 'svc-held-value-0123456789abcdef' }];

beforeEach(() => {
  root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-send-')));
  home = join(root, 'home');
  folder = join(root, 'projects', 'garden');
  origin = join(root, 'origin.git');
  ghLog = join(root, 'gh.log');
  marker = join(root, 'ran');
  mkdirSync(home, { recursive: true });
  mkdirSync(folder, { recursive: true });
  mkdirSync(join(root, 'data'), { recursive: true });
  // The test's own git settings: an author, and github.com for this repository is the local bare one.
  writeFileSync(
    join(home, '.gitconfig'),
    [
      '[user]\n\tname = Test Person\n\temail = person@example.test',
      // Every form of this repository's address leads to the local bare one (AUDIT3 S4), and any
      // other github.com address to a path that is not there.
      `[url "file://${origin}"]`,
      `\tinsteadOf = ${REMOTE}`,
      '\tinsteadOf = git@github.com:owner/garden.git',
      '\tinsteadOf = ssh://git@github.com/owner/garden.git',
      `[url "file://${join(root, 'no-github')}/"]`,
      '\tinsteadOf = https://github.com/',
      '\tinsteadOf = git@github.com:',
      '\tinsteadOf = ssh://git@github.com/',
      '[init]\n\tdefaultBranch = main\n',
    ].join('\n'),
  );
  git(root, 'init', '--bare', '-q', origin);
  git(folder, 'init', '-q');
  writeFileSync(join(folder, 'app.txt'), 'person\n');
  writeFileSync(join(folder, '.gitignore'), 'node_modules\n');
  git(folder, 'add', '-A');
  git(folder, 'commit', '-q', '-m', 'first');
  git(folder, 'remote', 'add', 'origin', REMOTE);
  git(folder, 'push', '-q', 'origin', 'main');
  // A stand-in gh: says it is signed in, records each call and whether a token reached it.
  ghPath = join(root, 'bin', 'gh');
  mkdirSync(join(root, 'bin'));
  writeFileSync(
    ghPath,
    `#!/bin/sh\necho "$* token:\${GH_TOKEN:-none}:\${GITHUB_TOKEN:-none}" >> '${ghLog}'\nif [ "$1" = pr ]; then echo 'https://github.com/owner/garden/pull/7'; fi\n` +
      `if [ "$1" = auth ] && [ -e '${join(root, 'signed-out')}' ]; then exit 1; fi\n` +
      `if [ "$1" = config ] && [ -e '${join(root, 'ssh')}' ]; then echo ssh; fi\n` +
      // A repository made: a bare one where the test's git settings send owner/<name>, unless it is there.
      `if [ "$1" = repo ] && [ "$2" = create ]; then [ -e '${join(root, 'no-github', 'owner')}'/"$5".git ] && exit 1; /usr/bin/git init --bare -q '${join(root, 'no-github', 'owner')}'/"$5".git; [ -e '${join(root, 'refuse-push')}' ] && chmod 555 '${join(root, 'no-github', 'owner')}'/"$5".git/objects; echo "https://github.com/owner/$5"; fi\nexit 0\n`,
  );
  chmodSync(ghPath, 0o755);
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe('Send to GitHub (ADR 0039, amended)', { skip: process.platform !== 'darwin' }, () => {
  test('a review says what would go: the repository by owner and name, the branch, the files with their kinds, never a secret or another project', async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    writeFileSync(join(folder, 'new.ts'), 'export {};\n');
    writeFileSync(join(folder, '.env'), 'KEY=secret\n');
    mkdirSync(join(folder, 'node_modules'));
    writeFileSync(join(folder, 'node_modules', 'x.js'), 'ignored\n');
    mkdirSync(join(folder, 'inner'));
    writeFileSync(join(folder, 'inner', 'other.txt'), 'another project\n');
    const reviewed = await sender().review(PROJECT, folder, [join(folder, 'inner')], 'Lanterns');
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    const send = reviewed.send;
    assert.deepEqual(send.remote, { host: 'github.com', path: 'owner/garden' });
    assert.equal(send.branch, 'main');
    assert.equal(send.base_commit, git(folder, 'rev-parse', 'HEAD'));
    assert.match(send.new_branch, /^halcyonic\/garden-[0-9a-f]{8}$/);
    assert.deepEqual(send.files, {
      files: [
        { path: 'app.txt', change: 'changed' },
        { path: 'new.ts', change: 'added' },
      ],
      total: 2,
    });
    assert.equal(send.review_request, true);
    assert.match(send.review, /^[0-9a-f]{64}$/);
    assert.equal(JSON.stringify(send).includes('example.test'), false, 'no address, no author');
    // The same folder reviews the same; a change makes another review.
    const again = await sender().review(PROJECT, folder, [join(folder, 'inner')], 'Lanterns');
    assert.equal(again.ok && again.send.review, send.review);
    writeFileSync(join(folder, 'new.ts'), 'export const a = 1;\n');
    const changed = await sender().review(PROJECT, folder, [join(folder, 'inner')], 'Lanterns');
    assert.notEqual(changed.ok && changed.send.review, send.review);
  });

  test("a send pushes one new branch on top of the folder's branch and opens a pull request, leaving the person's folder, branches and index as they were", async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    writeFileSync(join(folder, '.env'), 'KEY=secret\n');
    const head = git(folder, 'rev-parse', 'HEAD');
    const status = git(folder, 'status', '--porcelain');
    const branches = git(folder, 'for-each-ref', '--format=%(refname)');
    const reviewed = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok);
    const stale = await sender().send(PROJECT, folder, [], 'Lanterns', 'f'.repeat(64));
    assert.equal(stale.ok ? null : stale.code, 'review_stale');
    assert.equal(existsSync(ghLog) && readFileSync(ghLog, 'utf8').includes('pr create'), false);
    const sent = await sender().send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.ok(sent.ok, sent.ok ? '' : sent.message);
    assert.equal(sent.reviewRequestUrl, 'https://github.com/owner/garden/pull/7');
    // On the stand-in GitHub: the new branch, one commit on top of main, with the file and no secret.
    const pushed = git(origin, 'rev-parse', `refs/heads/${reviewed.send.new_branch}`);
    assert.equal(pushed, sent.commit);
    assert.equal(git(origin, 'rev-parse', `${sent.commit}^`), head);
    assert.equal(git(origin, 'show', `${sent.commit}:app.txt`), 'lanterns');
    assert.equal(git(origin, 'log', '-1', '--format=%s %an', sent.commit), 'Lanterns Test Person');
    assert.throws(() => git(origin, 'show', `${sent.commit}:.env`), 'the secret never left');
    assert.equal(git(origin, 'rev-parse', 'refs/heads/main'), head, 'main is not touched');
    // The person's folder: the same HEAD, the same files waiting, the same branches.
    assert.equal(git(folder, 'rev-parse', 'HEAD'), head);
    assert.equal(git(folder, 'status', '--porcelain'), status);
    assert.equal(git(folder, 'for-each-ref', '--format=%(refname)'), branches);
    // The pull request, asked of the person's own gh, with no token of ours.
    const asked = readFileSync(ghLog, 'utf8');
    assert.match(
      asked,
      new RegExp(
        `pr create --repo owner/garden --head ${reviewed.send.new_branch.replace('/', '\\/')} --base main --title Lanterns`,
      ),
    );
    assert.ok(
      asked.split('\n').every((line) => line === '' || line.endsWith('token:none:none')),
      'no token crossed',
    );
  });

  test("nothing an agent could write in the project's repository runs: its hooks, fsmonitor and credential helper", async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    const touch = `#!/bin/sh\ntouch '${marker}'\n`;
    for (const hook of ['pre-push', 'post-checkout', 'pre-commit', 'reference-transaction']) {
      writeFileSync(join(folder, '.git', 'hooks', hook), touch);
      chmodSync(join(folder, '.git', 'hooks', hook), 0o755);
    }
    writeFileSync(join(root, 'evil.sh'), touch);
    chmodSync(join(root, 'evil.sh'), 0o755);
    git(folder, 'config', 'core.fsmonitor', join(root, 'evil.sh'));
    git(folder, 'config', 'credential.helper', `!${join(root, 'evil.sh')}`);
    git(folder, 'config', 'core.sshCommand', join(root, 'evil.sh'));
    const reviewed = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok);
    const sent = await sender().send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.ok(sent.ok, sent.ok ? '' : sent.message);
    assert.equal(existsSync(marker), false, 'nothing of the repository ran');
  });

  test('a token in our own environment never reaches git or gh', async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    const env = {
      HOME: home,
      GH_TOKEN: 'ghp_ours',
      GITHUB_TOKEN: 'ghp_ours',
      HALCYONIC_ACCESS: 'x',
    };
    const reviewed = await sender({ env }).review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok);
    const sent = await sender({ env }).send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.ok(sent.ok);
    assert.equal(readFileSync(ghLog, 'utf8').includes('ghp_ours'), false);
  });

  test('without gh it pushes and opens no pull request, and says so before the press', async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    const reviewed = await sender({ gh: null }).review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok);
    assert.equal(reviewed.send.review_request, false);
    const sent = await sender({ gh: null }).send(
      PROJECT,
      folder,
      [],
      'Lanterns',
      reviewed.send.review,
    );
    assert.ok(sent.ok);
    assert.equal(sent.reviewRequestUrl, null);
    assert.equal(git(origin, 'rev-parse', `refs/heads/${reviewed.send.new_branch}`), sent.commit);
  });

  test('a file git tracks stays tracked, ignored or secret-shaped, and only what the person changed is sent (AUDIT3 G1)', async () => {
    mkdirSync(join(folder, 'vendor'));
    writeFileSync(join(folder, 'vendor', 'lib.js'), 'kept\n');
    writeFileSync(join(folder, '.gitignore'), 'node_modules\nvendor/\n');
    writeFileSync(join(folder, '.env.production'), 'API=tracked-by-the-person\n');
    git(folder, 'add', '-f', 'vendor/lib.js', '.gitignore', '.env.production');
    git(folder, 'commit', '-q', '-m', 'vendor and env');
    git(folder, 'push', '-q', 'origin', 'main');
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    // A secret-shaped file the person changes is still never sent: the base's stays.
    writeFileSync(join(folder, '.env.production'), 'API=changed-on-this-mac\n');
    const reviewed = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    assert.deepEqual(
      reviewed.send.files.files.map((file) => [file.path, file.change]),
      [['app.txt', 'changed']],
    );
    const sent = await sender().send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.ok(sent.ok, sent.ok ? '' : sent.message);
    assert.equal(
      git(origin, 'ls-tree', '-r', '--name-only', sent.commit),
      '.env.production\n.gitignore\napp.txt\nvendor/lib.js',
    );
    assert.equal(
      git(origin, 'show', `${sent.commit}:.env.production`),
      'API=tracked-by-the-person',
    );
  });

  test('a link pointing outside the folder is never sent, as a starter project holds node_modules; a link inside it is', async () => {
    // A starter's .gitignore says node_modules/, which a link does not match.
    writeFileSync(join(folder, '.gitignore'), 'node_modules/\n');
    git(folder, 'add', '.gitignore');
    git(folder, 'commit', '-q', '-m', 'ignore as a starter does');
    git(folder, 'push', '-q', 'origin', 'main');
    mkdirSync(join(root, 'shared', 'node_modules'), { recursive: true });
    symlinkSync(join(root, 'shared', 'node_modules'), join(folder, 'node_modules'), 'dir');
    mkdirSync(join(folder, 'docs'));
    symlinkSync('../app.txt', join(folder, 'docs', 'readme'));
    symlinkSync('docs/../../elsewhere', join(folder, 'up'));
    const reviewed = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    assert.deepEqual(
      reviewed.send.files.files.map((file) => [file.path, file.change]),
      [['docs/readme', 'added']],
    );
    assert.deepEqual(reviewed.send.links_left_out, { paths: ['node_modules', 'up'], total: 2 });
    const sent = await sender().send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.ok(sent.ok, sent.ok ? '' : sent.message);
    assert.equal(
      git(origin, 'ls-tree', '-r', '--name-only', sent.commit),
      '.gitignore\napp.txt\ndocs/readme',
    );
    assert.equal(git(origin, 'cat-file', 'blob', `${sent.commit}:docs/readme`), '../app.txt');
    assert.match(git(origin, 'ls-tree', sent.commit, 'docs/readme'), /^120000 blob /);
    // Where only such a link differs, the refusal says so.
    git(folder, 'add', '-A');
    git(folder, 'reset', '-q', 'node_modules', 'up');
    git(folder, 'commit', '-q', '-m', 'the inside link');
    const only = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.equal(only.ok ? null : only.code, 'nothing_to_send');
    assert.match(only.ok ? '' : only.message, /Only links that point outside/);
  });

  test("a file the person's own git excludes is never sent: the folder's info/exclude and their excludes file (AUDIT3 G2)", async () => {
    writeFileSync(join(folder, '.git', 'info', 'exclude'), 'private-notes.txt\n');
    writeFileSync(join(folder, 'private-notes.txt'), 'only mine\n');
    writeFileSync(join(home, 'global-ignore'), 'scratch.log\n');
    git(folder, 'config', '--global', 'core.excludesFile', join(home, 'global-ignore'));
    writeFileSync(join(folder, 'scratch.log'), 'mine too\n');
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    assert.equal(git(folder, 'status', '--porcelain'), 'M app.txt');
    const reviewed = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok);
    assert.deepEqual(
      reviewed.send.files.files.map((file) => file.path),
      ['app.txt'],
    );
    const sent = await sender().send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.ok(sent.ok);
    assert.equal(git(origin, 'ls-tree', '-r', '--name-only', sent.commit), '.gitignore\napp.txt');
  });

  test("an exclude rule Halcyonic can't read stops the send: a linked or oversized info/exclude, an unreadable excludes file (AUDIT3 reread G5)", async () => {
    writeFileSync(join(folder, 'private-notes.txt'), 'only mine\n');
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    const rules = join(root, 'rules');
    writeFileSync(rules, 'private-notes.txt\n');
    const exclude = join(folder, '.git', 'info', 'exclude');
    rmSync(exclude, { force: true });
    symlinkSync(rules, exclude);
    assert.equal(git(folder, 'status', '--porcelain'), 'M app.txt', "the person's git hides it");
    const linked = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.equal(linked.ok ? null : linked.code, 'unreadable_excludes');
    rmSync(exclude);
    writeFileSync(exclude, `${'# padding\n'.repeat(7000)}private-notes.txt\n`);
    const large = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.equal(large.ok ? null : large.code, 'unreadable_excludes');
    assert.match(large.ok ? '' : large.message, /sends nothing/);
    rmSync(exclude);
    // The person's own excludes file, named but not a file Halcyonic can read.
    mkdirSync(join(home, 'not-a-file'));
    git(folder, 'config', '--global', 'core.excludesFile', join(home, 'not-a-file'));
    const unreadable = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.equal(unreadable.ok ? null : unreadable.code, 'unreadable_excludes');
  });

  test("the person's excludes file is read where git reads it: its default place, and through a link from their dotfiles (AUDIT3 reread S1)", async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    writeFileSync(join(folder, 'scratch.log'), 'mine\n');
    mkdirSync(join(home, '.config', 'git'), { recursive: true });
    writeFileSync(join(home, '.config', 'git', 'ignore'), 'scratch.log\n');
    assert.equal(git(folder, 'status', '--porcelain'), 'M app.txt');
    const byDefault = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(byDefault.ok, byDefault.ok ? '' : byDefault.message);
    assert.deepEqual(
      byDefault.send.files.files.map((file) => file.path),
      ['app.txt'],
    );
    rmSync(join(home, '.config', 'git', 'ignore'));
    writeFileSync(join(root, 'dotfiles-ignore'), 'scratch.log\n');
    symlinkSync(join(root, 'dotfiles-ignore'), join(home, 'linked-ignore'));
    git(folder, 'config', '--global', 'core.excludesFile', join(home, 'linked-ignore'));
    const linked = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(linked.ok, linked.ok ? '' : linked.message);
    assert.deepEqual(
      linked.send.files.files.map((file) => file.path),
      ['app.txt'],
    );
  });

  test("no filter runs, the repository's or the person's own, and a repository that names one is refused (AUDIT3 S1)", async () => {
    const theirs = join(root, 'personal-ran');
    writeFileSync(join(root, 'evil.sh'), `#!/bin/sh\ntouch '${marker}'\ncat\n`);
    writeFileSync(join(root, 'personal.sh'), `#!/bin/sh\ntouch '${theirs}'\ncat\n`);
    chmodSync(join(root, 'evil.sh'), 0o755);
    chmodSync(join(root, 'personal.sh'), 0o755);
    git(folder, 'config', 'uploadpack.packObjectsHook', join(root, 'evil.sh'));
    git(folder, 'config', 'filter.evil.clean', join(root, 'evil.sh'));
    git(folder, 'config', '--global', 'filter.mine.clean', join(root, 'personal.sh'));
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    for (const attributes of [
      '* filter=evil\n',
      '* filter=mine\n',
      '*.psd filter=lfs diff=lfs merge=lfs -text\n',
    ]) {
      writeFileSync(join(folder, '.gitattributes'), attributes);
      const reviewed = await sender().review(PROJECT, folder, [], 'Lanterns');
      assert.equal(reviewed.ok ? null : reviewed.code, 'uses_filters', attributes);
    }
    assert.equal(existsSync(marker), false, "the repository's filter never ran");
    assert.equal(existsSync(theirs), false, "the person's own filter never ran");
    // Attributes that name no filter are sent as files.
    writeFileSync(join(folder, '.gitattributes'), '*.txt text eol=lf\n');
    const plain = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(plain.ok, plain.ok ? '' : plain.message);
  });

  test('a review asks nothing of gh; the same work sent twice says it was already sent (AUDIT3 S2, S3)', async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    const first = await sender().review(PROJECT, folder, [], 'One');
    assert.ok(first.ok);
    assert.equal(existsSync(ghLog), false, 'a review runs no gh');
    const sent = await sender().send(PROJECT, folder, [], 'One', first.send.review);
    assert.ok(sent.ok);
    const again = await sender().review(PROJECT, folder, [], 'Two');
    assert.ok(again.ok);
    const twice = await sender().send(PROJECT, folder, [], 'Two', again.send.review);
    assert.equal(twice.ok ? null : twice.code, 'already_sent');
    assert.match(twice.ok ? '' : twice.message, /already sent as halcyonic\/garden-/);
    assert.equal(git(origin, 'rev-parse', `refs/heads/${first.send.new_branch}`), sent.commit);
  });

  test('an origin in its git@ or ssh:// form sends to the same stand-in, and no other github.com address is reached (AUDIT3 S4)', async () => {
    for (const form of [
      'git@github.com:owner/garden.git',
      'ssh://git@github.com/owner/garden.git',
    ]) {
      git(folder, 'remote', 'set-url', 'origin', form);
      writeFileSync(join(folder, 'app.txt'), `${form}\n`);
      const reviewed = await sender().review(PROJECT, folder, [], 'Lanterns');
      assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
      const sent = await sender().send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
      assert.ok(sent.ok, `${form}: ${sent.ok ? '' : sent.message}`);
      assert.equal(git(origin, 'rev-parse', `refs/heads/${reviewed.send.new_branch}`), sent.commit);
    }
    // Another repository on github.com leads to a path that is not there: the push is refused.
    git(folder, 'remote', 'set-url', 'origin', 'https://github.com/someone/else.git');
    writeFileSync(join(folder, 'app.txt'), 'elsewhere\n');
    const reviewed = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok);
    const refused = await sender().send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.equal(refused.ok ? null : refused.code, 'push_refused');
  });

  test('a folder that is no repository is sent once as a new private repository, which it then becomes the clone of', async () => {
    const plain = join(root, 'projects', 'lantern-game');
    mkdirSync(join(plain, 'src'), { recursive: true });
    writeFileSync(join(plain, 'index.html'), '<title>Lanterns</title>\n');
    writeFileSync(join(plain, 'src', 'game.ts'), 'export const lanterns = 3;\n');
    writeFileSync(join(plain, '.gitignore'), 'dist\n');
    mkdirSync(join(plain, 'dist'));
    writeFileSync(join(plain, 'dist', 'built.js'), 'built\n');
    writeFileSync(join(plain, '.env'), 'KEY=never\n');
    const reviewed = await sender().review(PROJECT, plain, [], 'Lanterns');
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    assert.equal(reviewed.send.remote, null);
    assert.deepEqual(reviewed.send.creates, { name: 'lantern-game', visibility: 'private' });
    assert.equal(reviewed.send.base_commit, null);
    assert.equal(reviewed.send.new_branch, 'main');
    assert.equal(reviewed.send.title, 'Lanterns');
    assert.equal(reviewed.send.review_request, false);
    assert.deepEqual(
      reviewed.send.files.files.map((file) => [file.path, file.change]),
      [
        ['.gitignore', 'added'],
        ['index.html', 'added'],
        ['src/game.ts', 'added'],
      ],
    );
    // Who can see it is in the digest: a review of it public is another.
    const shown = await sender().review(PROJECT, plain, [], 'Lanterns', { visibility: 'public' });
    assert.ok(shown.ok);
    assert.notEqual(shown.send.review, reviewed.send.review);
    const crossed = await sender().send(PROJECT, plain, [], 'Lanterns', reviewed.send.review, {
      visibility: 'public',
    });
    assert.equal(crossed.ok ? null : crossed.code, 'review_stale');
    const recorded: { repository: string; visibility: string }[] = [];
    const sent = await sender().send(PROJECT, plain, [], 'Lanterns', reviewed.send.review, {
      onMade: (made) => recorded.push({ ...made }),
    });
    assert.ok(sent.ok, sent.ok ? '' : sent.message);
    assert.deepEqual(sent.send.remote, { host: 'github.com', path: 'owner/lantern-game' });
    assert.deepEqual(recorded, [{ repository: 'owner/lantern-game', visibility: 'private' }]);
    assert.equal(sent.cloned, true);
    assert.equal(sent.reviewRequestUrl, null);
    const asked = readFileSync(ghLog, 'utf8');
    assert.match(asked, /^repo create --private -- lantern-game token:none:none$/m);
    assert.ok(
      asked.indexOf('auth status') < asked.indexOf('repo create'),
      'signed in, asked first',
    );
    // Nothing of the clone's making names this Mac's paths in the folder's git.
    assert.equal(existsSync(join(plain, '.git', 'FETCH_HEAD')), false);
    assert.equal(
      readdirSync(plain).filter((entry) => entry.startsWith('.git-halcyonic-')).length,
      0,
    );
    const made = join(root, 'no-github', 'owner', 'lantern-game.git');
    assert.equal(git(made, 'rev-parse', 'refs/heads/main'), sent.commit);
    assert.equal(
      git(made, 'ls-tree', '-r', '--name-only', sent.commit),
      '.gitignore\nindex.html\nsrc/game.ts',
    );
    assert.equal(git(made, 'log', '-1', '--format=%s%n%an', sent.commit), 'Lanterns\nTest Person');
    // The folder is now that repository's clone: its files as committed, untouched.
    assert.equal(git(plain, 'rev-parse', 'HEAD'), sent.commit);
    assert.equal(git(plain, 'rev-parse', '--abbrev-ref', 'HEAD'), 'main');
    assert.equal(
      git(plain, 'config', 'remote.origin.url'),
      'https://github.com/owner/lantern-game.git',
    );
    // What the send kept out stays out of the person's own git too (AUDIT3 S2).
    assert.equal(git(plain, 'status', '--porcelain'), '');
    assert.equal(readFileSync(join(plain, '.git', 'info', 'exclude'), 'utf8'), '/.env\n');
    assert.equal(readFileSync(join(plain, '.env'), 'utf8'), 'KEY=never\n');
    assert.equal(existsSync(join(plain, '.git', 'hooks')), false);
    // The next send is the ordinary one: a branch and a pull request on the new repository.
    writeFileSync(join(plain, 'src', 'game.ts'), 'export const lanterns = 4;\n');
    const next = await sender().review(PROJECT, plain, [], 'More lanterns');
    assert.ok(next.ok, next.ok ? '' : next.message);
    assert.equal(next.send.creates, null);
    assert.deepEqual(next.send.remote, { host: 'github.com', path: 'owner/lantern-game' });
    assert.equal(next.send.base_commit, sent.commit);
    assert.deepEqual(
      next.send.files.files.map((file) => file.path),
      ['src/game.ts'],
    );
    // The name is taken now: a first send of another folder of that name is refused, nothing pushed.
    const twin = join(root, 'elsewhere', 'lantern-game');
    mkdirSync(twin, { recursive: true });
    writeFileSync(join(twin, 'a.txt'), 'a\n');
    const twinReview = await sender().review(PROJECT, twin, [], 'Lanterns');
    assert.ok(twinReview.ok);
    const refused = await sender().send(PROJECT, twin, [], 'Lanterns', twinReview.send.review);
    assert.equal(refused.ok ? null : refused.code, 'repository_not_made');
    assert.equal(existsSync(join(twin, '.git')), false);
  });

  test('a first send stopped after GitHub made the repository goes into it again; one already holding other work is refused', async () => {
    const plain = join(root, 'projects', 'stopped');
    mkdirSync(plain, { recursive: true });
    writeFileSync(join(plain, 'a.txt'), 'a\n');
    const recorded: { repository: string; visibility: 'private' | 'public' }[] = [];
    const first = await sender().review(PROJECT, plain, [], 'Lanterns', { name: 'stopped-game' });
    assert.ok(first.ok);
    assert.deepEqual(first.send.creates, { name: 'stopped-game', visibility: 'private' });
    // GitHub makes it, then refuses the push: here the made repository cannot take objects.
    const made = join(root, 'no-github', 'owner', 'stopped-game.git');
    writeFileSync(join(root, 'refuse-push'), '');
    const once = await sender().send(PROJECT, plain, [], 'Lanterns', first.send.review, {
      name: 'stopped-game',
      onMade: (made) => recorded.push({ ...made }),
    });
    rmSync(join(root, 'refuse-push'));
    if (once.ok) assert.fail('the push should have been refused');
    assert.equal(once.code, 'push_refused');
    assert.equal(once.effect, 'unknown');
    assert.deepEqual(recorded, [{ repository: 'owner/stopped-game', visibility: 'private' }]);
    assert.equal(existsSync(join(plain, '.git')), false);
    // Again, as the journal says it was made: into it, no second repository asked for.
    chmodSync(join(made, 'objects'), 0o755);
    const again = await sender().review(PROJECT, plain, [], 'Lanterns', { made: recorded[0] });
    assert.ok(again.ok, again.ok ? '' : again.message);
    assert.equal(again.send.creates, null);
    assert.deepEqual(again.send.remote, { host: 'github.com', path: 'owner/stopped-game' });
    const sent = await sender().send(PROJECT, plain, [], 'Lanterns', again.send.review, {
      made: recorded[0],
      onMade: () => assert.fail('no second repository'),
    });
    assert.ok(sent.ok, sent.ok ? '' : sent.message);
    assert.equal(git(made, 'rev-parse', 'refs/heads/main'), sent.commit);
    assert.equal(git(plain, 'rev-parse', 'HEAD'), sent.commit);
    assert.equal(readFileSync(ghLog, 'utf8').match(/repo create/g)?.length, 1);
    // A made repository whose main holds work Halcyonic did not send is refused, nothing pushed.
    const other = join(root, 'projects', 'other');
    mkdirSync(other, { recursive: true });
    writeFileSync(join(other, 'b.txt'), 'b\n');
    const otherMade = { repository: 'owner/stopped-game', visibility: 'private' as const };
    // Another project's own repository of Halcyonic's holds none of that work.
    const OTHER = '01a19000-0000-7000-8000-00000000ab02' as ProjectId;
    const theirs = await sender().review(OTHER, other, [], 'Lanterns', { made: otherMade });
    assert.ok(theirs.ok);
    const refused = await sender().send(OTHER, other, [], 'Lanterns', theirs.send.review, {
      made: otherMade,
    });
    assert.equal(refused.ok ? null : refused.code, 'repository_not_empty');
    assert.equal(git(made, 'rev-parse', 'refs/heads/main'), sent.commit, 'their main untouched');
  });

  test("a first send whose clone did not take is built on by the next send, never refused as someone else's", async () => {
    const plain = join(root, 'projects', 'unclonable');
    mkdirSync(plain, { recursive: true });
    writeFileSync(join(plain, 'a.txt'), 'a\n');
    const recorded: { repository: string; visibility: 'private' | 'public' }[] = [];
    const first = await sender().review(PROJECT, plain, [], 'Lanterns');
    assert.ok(first.ok);
    // The folder cannot take a .git: the work goes to GitHub, the clone does not take.
    chmodSync(plain, 0o555);
    const sent = await sender().send(PROJECT, plain, [], 'Lanterns', first.send.review, {
      onMade: (made) => recorded.push({ ...made }),
    });
    chmodSync(plain, 0o755);
    assert.ok(sent.ok, sent.ok ? '' : sent.message);
    assert.equal(sent.cloned, false);
    assert.equal(existsSync(join(plain, '.git')), false);
    assert.equal(
      readdirSync(plain).filter((entry) => entry.startsWith('.git-halcyonic-')).length,
      0,
    );
    const made = join(root, 'no-github', 'owner', 'unclonable.git');
    // The person changes the game, and sends again: on top of what the first send pushed.
    writeFileSync(join(plain, 'a.txt'), 'b\n');
    const next = await sender().review(PROJECT, plain, [], 'More', { made: recorded[0] });
    assert.ok(next.ok, next.ok ? '' : next.message);
    const again = await sender().send(PROJECT, plain, [], 'More', next.send.review, {
      made: recorded[0],
    });
    assert.ok(again.ok, again.ok ? '' : again.message);
    assert.equal(again.cloned, true);
    assert.equal(git(made, 'rev-parse', 'refs/heads/main^'), sent.commit, 'built on the first');
    assert.equal(git(made, 'rev-parse', 'refs/heads/main'), again.commit);
    assert.equal(git(plain, 'rev-parse', 'HEAD'), again.commit);
    assert.equal(git(plain, 'status', '--porcelain'), '');
  });

  test('a first send asks gh whether it is signed in before anything is made, and reaches GitHub as gh says (ssh)', async () => {
    const plain = join(root, 'projects', 'signed');
    mkdirSync(plain, { recursive: true });
    writeFileSync(join(plain, 'a.txt'), 'a\n');
    writeFileSync(join(root, 'signed-out'), '');
    const reviewed = await sender().review(PROJECT, plain, [], 'Lanterns');
    assert.ok(reviewed.ok, 'a review asks gh nothing');
    const out = await sender().send(PROJECT, plain, [], 'Lanterns', reviewed.send.review);
    assert.equal(out.ok ? null : out.code, 'gh_signed_out');
    assert.equal(readFileSync(ghLog, 'utf8').includes('repo create'), false);
    rmSync(join(root, 'signed-out'));
    writeFileSync(join(root, 'ssh'), '');
    const sent = await sender().send(PROJECT, plain, [], 'Lanterns', reviewed.send.review);
    assert.ok(sent.ok, sent.ok ? '' : sent.message);
    assert.equal(git(plain, 'config', 'remote.origin.url'), 'git@github.com:owner/signed.git');
    const named = await sender().review(PROJECT, join(root, 'projects', 'signed'), [], 'Lanterns', {
      name: '..',
    });
    // The folder is its clone now: an ordinary send, which asks no name (here nothing new to send).
    assert.equal(named.ok ? null : named.code, 'nothing_to_send');
    // A name GitHub would not take is refused before anything is made.
    const fresh = join(root, 'projects', 'fresh');
    mkdirSync(fresh, { recursive: true });
    writeFileSync(join(fresh, 'a.txt'), 'a\n');
    const bad = await sender().review(PROJECT, fresh, [], 'Lanterns', { name: '..' });
    assert.equal(bad.ok ? null : bad.code, 'invalid_name');
    // Never one gh would read as a flag (AUDIT3 S3).
    const flag = await sender().review(PROJECT, fresh, [], 'Lanterns', { name: '--public' });
    assert.equal(flag.ok ? null : flag.code, 'invalid_name');
  });

  test('a folder inside another repository is never sent as a new one; without gh none is made', async () => {
    const inner = join(folder, 'inner');
    mkdirSync(inner);
    writeFileSync(join(inner, 'a.txt'), 'a\n');
    const inside = await sender().review(PROJECT, inner, [], 'Lanterns');
    assert.equal(inside.ok ? null : inside.code, 'inside_repository');
    const plain = join(root, 'projects', 'plain');
    mkdirSync(plain);
    writeFileSync(join(plain, 'a.txt'), 'a\n');
    const noGh = await sender({ gh: null }).review(PROJECT, plain, [], 'Lanterns');
    assert.equal(noGh.ok ? null : noGh.code, 'no_gh');
    // The home folder kept as a repository of its settings files does not count.
    git(home, 'init', '-q');
    const underHome = join(home, 'projects', 'mine');
    mkdirSync(underHome, { recursive: true });
    writeFileSync(join(underHome, 'a.txt'), 'a\n');
    const mine = await sender().review(PROJECT, underHome, [], 'Lanterns');
    assert.ok(mine.ok, mine.ok ? '' : mine.message);
  });

  test("the title is the person's, else the project's latest task's, else its name: one line, at most 200 characters", () => {
    const project = { name: 'Garden' };
    const tasks = [
      { title: 'Older task', updated_at: '2026-11-12T01:00:00.000Z' },
      { title: 'Add\nthree\tlanterns ', updated_at: '2026-11-12T02:00:00.000Z' },
    ];
    assert.equal(sendTitle('  Mine\u0007 ', project, tasks), 'Mine');
    assert.equal(sendTitle(undefined, project, tasks), 'Add three lanterns');
    assert.equal(sendTitle('', project, []), 'Garden');
    assert.equal(sendTitle(undefined, undefined, []), 'Changes');
    assert.equal(sendTitle('x'.repeat(300), project, []).length, 200);
  });

  test("a folder whose origin is on another host is sent there with the person's git, with no review request, and is never published", async () => {
    const other = join(root, 'gitlab', 'group', 'sub', 'garden.git');
    git(root, 'init', '--bare', '-q', other);
    git(
      folder,
      'config',
      '--global',
      `url.file://${join(root, 'gitlab')}/.insteadOf`,
      'https://gitlab.example/',
    );
    git(
      folder,
      'remote',
      'set-url',
      'origin',
      'https://someone:secret@gitlab.example/group/sub/garden.git',
    );
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    const reviewed = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    // The host and path only: the user part is never kept or shown.
    assert.deepEqual(reviewed.send.remote, { host: 'gitlab.example', path: 'group/sub/garden' });
    assert.equal(reviewed.send.review_request, false, 'no tool is set up here for that host');
    const sent = await sender().send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.ok(sent.ok, sent.ok ? '' : sent.message);
    assert.equal(sent.reviewRequestUrl, null);
    assert.equal(git(other, 'rev-parse', `refs/heads/${reviewed.send.new_branch}`), sent.commit);
    assert.equal(existsSync(ghLog) && readFileSync(ghLog, 'utf8').includes('pr create'), false);
    // An ssh form of another host reads the same way.
    git(folder, 'remote', 'set-url', 'origin', 'git@gitlab.example:group/sub/garden.git');
    const viaSsh = await sender().review(PROJECT, folder, [], 'Other');
    assert.ok(viaSsh.ok || viaSsh.code === 'nothing_to_send');
  });

  test('every refusal is said in words, and nothing is sent', async () => {
    const plain = join(root, 'projects', 'plain');
    mkdirSync(plain);
    writeFileSync(join(plain, 'a.txt'), 'a\n');
    writeFileSync(join(plain, '.git'), 'gitdir: /elsewhere\n');
    const notRepo = await sender().review(PROJECT, plain, [], 'Lanterns');
    assert.equal(notRepo.ok ? null : notRepo.code, 'not_a_repository');
    const nothing = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.equal(nothing.ok ? null : nothing.code, 'nothing_to_send');
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    // An origin Halcyonic can't send to: a port, a user and password, a path that climbs.
    for (const odd of [
      'ssh://git@example.test:2222/owner/garden.git',
      'https://example.test/../garden.git',
      'file:///tmp/garden.git',
    ]) {
      git(folder, 'remote', 'set-url', 'origin', odd);
      const elsewhere = await sender().review(PROJECT, folder, [], 'Lanterns');
      assert.equal(elsewhere.ok ? null : elsewhere.code, 'no_origin', odd);
    }
    git(folder, 'remote', 'set-url', 'origin', REMOTE);
    git(folder, 'checkout', '-q', '--detach');
    const detached = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.equal(detached.ok ? null : detached.code, 'detached_head');
    git(folder, 'checkout', '-q', 'main');
    // A push GitHub refuses: here the stand-in repository is gone.
    const reviewed = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok);
    rmSync(origin, { recursive: true, force: true });
    const refused = await sender().send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.equal(refused.ok ? null : refused.code, 'push_refused');
    assert.equal(existsSync(ghLog) && readFileSync(ghLog, 'utf8').includes('pr create'), false);
  });
});

describe("the project repository's own git settings never run in a send (AUDIT3, 2026-11-13)", {
  skip: process.platform !== 'darwin',
}, () => {
  test("over ssh, its ssh command, its push address, its other settings and its hooks run nothing at the review or the press, though the folder's own git runs them", async () => {
    const marks = join(root, 'marks');
    mkdirSync(marks);
    const mark = (name: string) => `touch ${join(marks, name)}`;
    // The person's own ssh command stands in for ssh to a host of its own, serving the stand-in repository.
    const ssh = join(root, 'bin', 'person-ssh');
    writeFileSync(
      ssh,
      `#!/bin/sh\nfor a; do last=$a; done\nexec sh -c "$(printf %s "$last" | sed "s#'owner/garden.git'#'${origin}'#")"\n`,
    );
    chmodSync(ssh, 0o755);
    git(home, 'config', '--global', 'core.sshCommand', ssh);
    const address = 'git@standin.test:owner/garden.git';
    git(folder, 'remote', 'set-url', 'origin', address);
    // Where the repository's own settings would push the person's git instead.
    const decoy = join(root, 'decoy.git');
    git(root, 'init', '--bare', '-q', decoy);
    const settings: [string, string][] = [
      ['core.sshCommand', mark('sshCommand')],
      [`url.file://${decoy}.pushInsteadOf`, address],
      ['core.fsmonitor', mark('fsmonitor')],
      ['core.pager', mark('pager')],
      ['core.askPass', mark('askPass')],
      ['core.alternateRefsCommand', mark('alternateRefsCommand')],
      ['uploadpack.packObjectsHook', mark('packObjectsHook')],
      ['credential.helper', `!${mark('credential')}`],
      ['gpg.program', mark('gpg')],
      ['commit.gpgSign', 'true'],
      ['diff.external', mark('diffExternal')],
      ['core.hooksPath', join(folder, '.git', 'hooks')],
    ];
    for (const [key, value] of settings) git(folder, 'config', key, value);
    for (const hook of [
      'pre-push',
      'reference-transaction',
      'pre-commit',
      'post-commit',
      'post-checkout',
    ]) {
      writeFileSync(join(folder, '.git', 'hooks', hook), `#!/bin/sh\n${mark(`hook-${hook}`)}\n`);
      chmodSync(join(folder, '.git', 'hooks', hook), 0o755);
    }
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    // With a secret held, so the press asks the remote what it holds before it pushes.
    const reviewed = await sender({ held: HELD }).review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    const sent = await sender({ held: HELD }).send(
      PROJECT,
      folder,
      [],
      'Lanterns',
      reviewed.send.review,
    );
    assert.ok(sent.ok, sent.ok ? '' : sent.message);
    assert.equal(
      git(origin, 'for-each-ref', '--format=%(refname)', 'refs/heads/halcyonic'),
      `refs/heads/${reviewed.send.new_branch}`,
      "it reached the stand-in through the person's own ssh command",
    );
    assert.equal(git(decoy, 'for-each-ref'), '', 'not the address the repository would push to');
    assert.deepEqual(readdirSync(marks), [], 'nothing of the repository ran');
    // The controls: the folder's own git, as the person runs it, uses the repository's push address and hooks,
    // and its ssh command.
    git(folder, 'push', '-q', 'origin', 'HEAD:refs/heads/control');
    assert.notEqual(git(decoy, 'for-each-ref'), '');
    assert.ok(readdirSync(marks).includes('hook-pre-push'));
    try {
      git(folder, 'ls-remote', 'origin');
    } catch {
      // The repository's ssh command only touched its mark, so there was no remote to list.
    }
    assert.ok(readdirSync(marks).includes('sshCommand'));
  });
});

describe('a commit holding a secret the host keeps is never sent (TASKS, 2026-11-13)', {
  skip: process.platform !== 'darwin',
}, () => {
  const VALUE = HELD[0]?.value ?? '';
  const commitWithKey = (message = 'with key') => {
    writeFileSync(join(folder, 'key.txt'), `KEY=${VALUE}\n`);
    git(folder, 'add', '-A');
    git(folder, 'commit', '-q', '-m', message);
    rmSync(join(folder, 'key.txt'));
    git(folder, 'add', '-A');
    git(folder, 'commit', '-q', '-m', 'without key');
  };
  const press = async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    const reviewed = await sender({ held: HELD }).review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    return sender({ held: HELD }).send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
  };
  const onRemote = () => git(origin, 'log', '--all', '-p').includes(VALUE);

  test('one the remote lacks, in a file a later commit removed, refuses the press, naming the file; nothing is pushed', async () => {
    commitWithKey();
    // The folder's own record of the remote, which its agent can write, is not believed.
    git(folder, 'update-ref', 'refs/remotes/origin/main', 'HEAD');
    const before = git(origin, 'for-each-ref', '--format=%(refname)');
    const pressed = await press();
    assert.equal(pressed.ok ? null : pressed.code, 'holds_secret');
    assert.equal(
      pressed.ok ? '' : pressed.message,
      'An earlier version of key.txt, which owner/garden does not have, holds a secret this Mac keeps (MY_SERVICE_KEY), and sending now would send it too, so nothing was sent. To send, make a new MY_SERVICE_KEY with the service that gave it to you, store it on this Mac, then send again: the old one goes with that version, but no longer works for anyone. Or ask a developer to take that old version out.',
    );
    assert.equal(git(origin, 'for-each-ref', '--format=%(refname)'), before);
    assert.equal(onRemote(), false);
  });

  test("one in a commit's own message is found too", async () => {
    git(folder, 'commit', '-q', '--allow-empty', '-m', `token ${VALUE}`);
    const pressed = await press();
    assert.equal(pressed.ok ? null : pressed.code, 'holds_secret');
    assert.match(
      pressed.ok ? '' : pressed.message,
      /^The description saved with an earlier change, which owner\/garden does not have, holds a secret this Mac keeps \(MY_SERVICE_KEY\), and sending now would send it too, so nothing was sent\..* Or ask a developer to change that description\.$/,
    );
    assert.equal(onRemote(), false);
  });

  test('a title holding one is refused at the review, on a first send before any repository is made', async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    const titled = `Lanterns ${VALUE}`;
    const words =
      'The title you gave this send holds a secret this Mac keeps (MY_SERVICE_KEY), so nothing was sent. Change the title, then try again.';
    const reviewed = await sender({ held: HELD }).review(PROJECT, folder, [], titled);
    assert.deepEqual(reviewed.ok ? null : [reviewed.code, reviewed.message], [
      'holds_secret',
      words,
    ]);
    const plain = join(root, 'projects', 'titled');
    mkdirSync(plain, { recursive: true });
    writeFileSync(join(plain, 'a.txt'), 'a\n');
    const first = await sender({ held: HELD }).review(PROJECT, plain, [], titled);
    assert.deepEqual(first.ok ? null : [first.code, first.message], ['holds_secret', words]);
    const pressed = await sender({ held: HELD }).send(PROJECT, plain, [], titled, 'a'.repeat(64));
    assert.equal(pressed.ok ? null : pressed.code, 'holds_secret');
    assert.equal(existsSync(ghLog) && readFileSync(ghLog, 'utf8').includes('repo create'), false);
    assert.equal(onRemote(), false);
  });

  test('one already on the remote is not read again when the remote is ahead of the branch, so the send goes', async () => {
    commitWithKey();
    git(folder, 'push', '-q', 'origin', 'main');
    // Someone else's work lands on main after the folder last fetched.
    const other = join(root, 'teammate');
    git(root, 'clone', '-q', origin, other);
    writeFileSync(join(other, 'theirs.txt'), 'theirs\n');
    git(other, 'add', '-A');
    git(other, 'commit', '-q', '-m', 'theirs');
    git(other, 'push', '-q', 'origin', 'main');
    const pressed = await press();
    assert.ok(pressed.ok, pressed.ok ? '' : pressed.message);
  });

  test("one the push's own address lacks is refused, though the address it is read from holds it", async () => {
    commitWithKey();
    git(folder, 'push', '-q', 'origin', 'main');
    // The person pushes to a second repository of their own that lacks it (their global push address).
    const pushes = join(root, 'pushes.git');
    git(root, 'init', '--bare', '-q', pushes);
    git(home, 'config', '--global', `url.file://${pushes}.pushInsteadOf`, REMOTE);
    const pressed = await press();
    assert.equal(pressed.ok ? null : pressed.code, 'holds_secret');
    assert.equal(git(pushes, 'for-each-ref'), '', 'nothing reached it');
  });

  test('a remote that cannot be asked what it holds, and more history than is read, are refused in words', async () => {
    commitWithKey();
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    const reviewed = await sender({ held: HELD }).review(PROJECT, folder, [], 'Lanterns');
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    const most = await sender({ held: HELD, historyMost: 10 }).send(
      PROJECT,
      folder,
      [],
      'Lanterns',
      reviewed.send.review,
    );
    assert.deepEqual(most.ok ? null : [most.code, most.message], [
      'history_too_large',
      'This would send over 512 MB of earlier work, more than can be checked for secrets, so nothing was sent. Ask a developer to send it.',
    ]);
    rmSync(origin, { recursive: true, force: true });
    const unreached = await sender({ held: HELD }).send(
      PROJECT,
      folder,
      [],
      'Lanterns',
      reviewed.send.review,
    );
    assert.deepEqual(unreached.ok ? null : [unreached.code, unreached.message], [
      'not_reached',
      "The repository couldn't be reached, so nothing was sent. Check this computer's connection, then try again.",
    ]);
  });

  test('a name holding one, in an earlier version or in what is here now, is refused without the name, so the value is never said', async () => {
    writeFileSync(join(folder, `note-${VALUE}.txt`), 'x\n');
    git(folder, 'add', '-A');
    git(folder, 'commit', '-q', '-m', 'named');
    git(folder, 'rm', '-q', `note-${VALUE}.txt`);
    git(folder, 'commit', '-q', '-m', 'unnamed');
    const pressed = await press();
    assert.equal(pressed.ok ? null : pressed.code, 'holds_secret');
    assert.match(
      pressed.ok ? '' : pressed.message,
      /^A file's or folder's name in an earlier version that owner\/garden does not have \(note-\[MY_SERVICE_KEY\]\.txt, with the secret's name in place of its value\) holds a secret this Mac keeps \(MY_SERVICE_KEY\)/,
    );
    assert.equal((pressed.ok ? '' : pressed.message).includes(VALUE), false);
    writeFileSync(join(folder, `now-${VALUE}.txt`), 'x\n');
    const shown = await sender({ held: HELD }).review(PROJECT, folder, [], 'Lanterns');
    assert.deepEqual(shown.ok ? null : [shown.code, shown.message], [
      'holds_secret',
      "A file's or folder's name holds a secret this Mac keeps (MY_SERVICE_KEY), so nothing was sent. It is now-[MY_SERVICE_KEY].txt here, with the secret's name in place of its value. Rename it in the project's folder, then try again.",
    ]);
  });

  test('a name with a line break before the value, which no listing shows whole, is found in the folder that holds it', async () => {
    writeFileSync(join(folder, `odd\n${VALUE}.txt`), 'x\n');
    git(folder, 'add', '-A');
    git(folder, 'commit', '-q', '-m', 'odd name');
    git(folder, 'rm', '-q', `odd\n${VALUE}.txt`);
    git(folder, 'commit', '-q', '-m', 'gone');
    const pressed = await press();
    assert.equal(pressed.ok ? null : pressed.code, 'holds_secret');
    assert.match(
      pressed.ok ? '' : pressed.message,
      /^A name in the top folder of an earlier version, which owner\/garden does not have, holds a secret this Mac keeps \(MY_SERVICE_KEY\)/,
    );
    assert.equal((pressed.ok ? '' : pressed.message).includes(VALUE), false);
    assert.equal(onRemote(), false);
  });

  test('a remote that cannot negotiate is read by the tips it lists, and one the remote lacks is still refused', async () => {
    git(home, 'config', '--global', 'protocol.version', '0');
    commitWithKey();
    const pressed = await press();
    assert.equal(pressed.ok ? null : pressed.code, 'holds_secret');
    assert.equal(onRemote(), false);
  });

  test('one the remote already holds leaves nothing new, so the send goes', async () => {
    commitWithKey();
    git(folder, 'push', '-q', 'origin', 'main');
    const pressed = await press();
    assert.ok(pressed.ok, pressed.ok ? '' : pressed.message);
  });
});

describe('a tree holding a secret the host keeps is never sent (the lead, 2026-11-12)', {
  skip: process.platform !== 'darwin',
}, () => {
  test('Send refuses, at the review and at the press, naming the file and the secret, never the value; nothing is pushed', async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    mkdirSync(join(folder, 'src'));
    writeFileSync(join(folder, 'src', 'config.ts'), `export const key = "${HELD[0]?.value}";\n`);
    const shown = await sender().review(PROJECT, folder, [], 'Lanterns');
    assert.ok(shown.ok, 'with nothing held, the review is as before');
    const reviewed = await sender({ held: HELD }).review(PROJECT, folder, [], 'Lanterns');
    assert.equal(reviewed.ok ? null : reviewed.code, 'holds_secret');
    assert.equal(
      reviewed.ok ? '' : reviewed.message,
      'src/config.ts holds a secret this Mac keeps (MY_SERVICE_KEY), so nothing was sent. Take it out of the file, then try again.',
    );
    const before = git(origin, 'for-each-ref', '--format=%(refname)');
    const pressed = await sender({ held: HELD }).send(
      PROJECT,
      folder,
      [],
      'Lanterns',
      shown.ok ? shown.send.review : '',
    );
    assert.equal(pressed.ok ? null : pressed.code, 'holds_secret');
    assert.equal(
      git(origin, 'for-each-ref', '--format=%(refname)'),
      before,
      'nothing reached the remote',
    );
    assert.equal(existsSync(ghLog) && readFileSync(ghLog, 'utf8').includes('pr create'), false);
    // Base64 of it in a file a tool wrote is found too.
    writeFileSync(join(folder, 'src', 'config.ts'), 'export {};\n');
    writeFileSync(join(folder, 'blob.txt'), Buffer.from(`x${HELD[0]?.value}y`).toString('base64'));
    const encoded = await sender({ held: HELD }).review(PROJECT, folder, [], 'Lanterns');
    assert.equal(encoded.ok ? null : encoded.code, 'holds_secret');
    assert.match(encoded.ok ? '' : encoded.message, /^blob\.txt holds/);
  });
});

test("a refused push keeps git's own most telling line, with the owner, every address and every path on this Mac masked (run 2)", () => {
  const rejected = [
    'To https://github.com/Owner/garden.git',
    ' ! [remote rejected] abc -> halcyonic-pages (push declined due to repository rule violations)',
    "error: failed to push some refs to 'https://github.com/Owner/garden.git'",
  ].join('\n');
  assert.equal(
    gitSaid(rejected, 'owner/garden'),
    ' Git said: "! [remote rejected] abc -> halcyonic-pages (push declined due to repository rule violations)"',
  );
  // GitHub's own refusal code is told before git's summary; the login in any case is masked.
  assert.equal(
    gitSaid(
      'remote: error: GH013: Repository rule violations found for refs/heads/x by OWNER.\nerror: failed to push',
      'owner/garden',
    ),
    ' Git said: "remote: error: GH013: Repository rule violations found for refs/heads/x by <owner>."',
  );
  assert.equal(
    gitSaid(
      "fatal: unable to access 'https://github.com/owner/garden.git/': Could not resolve host",
      'owner/garden',
    ),
    ' Git said: "fatal: unable to access \'<address>\': Could not resolve host"',
  );
  assert.equal(
    gitSaid(
      'fatal: could not read from git@github.com:owner/garden.git and /Users/someone/dev/garden',
      'owner/garden',
    ),
    ' Git said: "fatal: could not read from <address> and <path>"',
  );
  assert.equal(gitSaid('', 'owner/garden'), '', 'git said nothing: nothing is made up');
  assert.equal(gitSaid('To x\n', 'owner/garden'), '');
  const long = gitSaid(`error: ${'a'.repeat(400)}`, 'owner/garden');
  assert.ok(long.length < 220 && long.endsWith('…"'));
  const controls = gitSaid('error: bad\u0007\u001b[31m thing', 'owner/garden');
  assert.ok(
    Array.from(controls).every((ch) => ch.charCodeAt(0) >= 0x20 && ch.charCodeAt(0) !== 0x7f),
    'no control character is kept',
  );
});
