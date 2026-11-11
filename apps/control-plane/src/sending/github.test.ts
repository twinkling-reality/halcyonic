import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
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
import { GitHubSender } from './github.ts';

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

function sender(options: { gh?: string | null; env?: NodeJS.ProcessEnv } = {}) {
  return new GitHubSender({
    git: GIT,
    gh: options.gh === undefined ? ghPath : options.gh,
    dataDir: join(root, 'data'),
    env: options.env ?? { HOME: home },
  });
}

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
    `#!/bin/sh\necho "$* token:\${GH_TOKEN:-none}:\${GITHUB_TOKEN:-none}" >> '${ghLog}'\nif [ "$1" = pr ]; then echo 'https://github.com/owner/garden/pull/7'; fi\nexit 0\n`,
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
    const reviewed = await sender().review(PROJECT, folder, [join(folder, 'inner')]);
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    const send = reviewed.send;
    assert.equal(send.repository, 'owner/garden');
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
    assert.equal(send.pull_request, true);
    assert.match(send.review, /^[0-9a-f]{64}$/);
    assert.equal(JSON.stringify(send).includes('example.test'), false, 'no address, no author');
    // The same folder reviews the same; a change makes another review.
    const again = await sender().review(PROJECT, folder, [join(folder, 'inner')]);
    assert.equal(again.ok && again.send.review, send.review);
    writeFileSync(join(folder, 'new.ts'), 'export const a = 1;\n');
    const changed = await sender().review(PROJECT, folder, [join(folder, 'inner')]);
    assert.notEqual(changed.ok && changed.send.review, send.review);
  });

  test("a send pushes one new branch on top of the folder's branch and opens a pull request, leaving the person's folder, branches and index as they were", async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    writeFileSync(join(folder, '.env'), 'KEY=secret\n');
    const head = git(folder, 'rev-parse', 'HEAD');
    const status = git(folder, 'status', '--porcelain');
    const branches = git(folder, 'for-each-ref', '--format=%(refname)');
    const reviewed = await sender().review(PROJECT, folder, []);
    assert.ok(reviewed.ok);
    const stale = await sender().send(PROJECT, folder, [], 'Lanterns', 'f'.repeat(64));
    assert.equal(stale.ok ? null : stale.code, 'review_stale');
    assert.equal(existsSync(ghLog) && readFileSync(ghLog, 'utf8').includes('pr create'), false);
    const sent = await sender().send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.ok(sent.ok, sent.ok ? '' : sent.message);
    assert.equal(sent.pullRequestUrl, 'https://github.com/owner/garden/pull/7');
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
    const reviewed = await sender().review(PROJECT, folder, []);
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
    const reviewed = await sender({ env }).review(PROJECT, folder, []);
    assert.ok(reviewed.ok);
    const sent = await sender({ env }).send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.ok(sent.ok);
    assert.equal(readFileSync(ghLog, 'utf8').includes('ghp_ours'), false);
  });

  test('without gh it pushes and opens no pull request, and says so before the press', async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    const reviewed = await sender({ gh: null }).review(PROJECT, folder, []);
    assert.ok(reviewed.ok);
    assert.equal(reviewed.send.pull_request, false);
    const sent = await sender({ gh: null }).send(
      PROJECT,
      folder,
      [],
      'Lanterns',
      reviewed.send.review,
    );
    assert.ok(sent.ok);
    assert.equal(sent.pullRequestUrl, null);
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
    const reviewed = await sender().review(PROJECT, folder, []);
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

  test("a file the person's own git excludes is never sent: the folder's info/exclude and their excludes file (AUDIT3 G2)", async () => {
    writeFileSync(join(folder, '.git', 'info', 'exclude'), 'private-notes.txt\n');
    writeFileSync(join(folder, 'private-notes.txt'), 'only mine\n');
    writeFileSync(join(home, 'global-ignore'), 'scratch.log\n');
    git(folder, 'config', '--global', 'core.excludesFile', join(home, 'global-ignore'));
    writeFileSync(join(folder, 'scratch.log'), 'mine too\n');
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    assert.equal(git(folder, 'status', '--porcelain'), 'M app.txt');
    const reviewed = await sender().review(PROJECT, folder, []);
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
    const linked = await sender().review(PROJECT, folder, []);
    assert.equal(linked.ok ? null : linked.code, 'unreadable_excludes');
    rmSync(exclude);
    writeFileSync(exclude, `${'# padding\n'.repeat(7000)}private-notes.txt\n`);
    const large = await sender().review(PROJECT, folder, []);
    assert.equal(large.ok ? null : large.code, 'unreadable_excludes');
    assert.match(large.ok ? '' : large.message, /sends nothing/);
    rmSync(exclude);
    // The person's own excludes file, named but not a file Halcyonic can read.
    mkdirSync(join(home, 'not-a-file'));
    git(folder, 'config', '--global', 'core.excludesFile', join(home, 'not-a-file'));
    const unreadable = await sender().review(PROJECT, folder, []);
    assert.equal(unreadable.ok ? null : unreadable.code, 'unreadable_excludes');
  });

  test("the person's excludes file is read where git reads it: its default place, and through a link from their dotfiles (AUDIT3 reread S1)", async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    writeFileSync(join(folder, 'scratch.log'), 'mine\n');
    mkdirSync(join(home, '.config', 'git'), { recursive: true });
    writeFileSync(join(home, '.config', 'git', 'ignore'), 'scratch.log\n');
    assert.equal(git(folder, 'status', '--porcelain'), 'M app.txt');
    const byDefault = await sender().review(PROJECT, folder, []);
    assert.ok(byDefault.ok, byDefault.ok ? '' : byDefault.message);
    assert.deepEqual(
      byDefault.send.files.files.map((file) => file.path),
      ['app.txt'],
    );
    rmSync(join(home, '.config', 'git', 'ignore'));
    writeFileSync(join(root, 'dotfiles-ignore'), 'scratch.log\n');
    symlinkSync(join(root, 'dotfiles-ignore'), join(home, 'linked-ignore'));
    git(folder, 'config', '--global', 'core.excludesFile', join(home, 'linked-ignore'));
    const linked = await sender().review(PROJECT, folder, []);
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
      const reviewed = await sender().review(PROJECT, folder, []);
      assert.equal(reviewed.ok ? null : reviewed.code, 'uses_filters', attributes);
    }
    assert.equal(existsSync(marker), false, "the repository's filter never ran");
    assert.equal(existsSync(theirs), false, "the person's own filter never ran");
    // Attributes that name no filter are sent as files.
    writeFileSync(join(folder, '.gitattributes'), '*.txt text eol=lf\n');
    const plain = await sender().review(PROJECT, folder, []);
    assert.ok(plain.ok, plain.ok ? '' : plain.message);
  });

  test('a review asks nothing of gh; the same work sent twice says it was already sent (AUDIT3 S2, S3)', async () => {
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    const first = await sender().review(PROJECT, folder, []);
    assert.ok(first.ok);
    assert.equal(existsSync(ghLog), false, 'a review runs no gh');
    const sent = await sender().send(PROJECT, folder, [], 'One', first.send.review);
    assert.ok(sent.ok);
    const again = await sender().review(PROJECT, folder, []);
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
      const reviewed = await sender().review(PROJECT, folder, []);
      assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
      const sent = await sender().send(PROJECT, folder, [], 'Form', reviewed.send.review);
      assert.ok(sent.ok, `${form}: ${sent.ok ? '' : sent.message}`);
      assert.equal(git(origin, 'rev-parse', `refs/heads/${reviewed.send.new_branch}`), sent.commit);
    }
    // Another repository on github.com leads to a path that is not there: the push is refused.
    git(folder, 'remote', 'set-url', 'origin', 'https://github.com/someone/else.git');
    writeFileSync(join(folder, 'app.txt'), 'elsewhere\n');
    const reviewed = await sender().review(PROJECT, folder, []);
    assert.ok(reviewed.ok);
    const refused = await sender().send(PROJECT, folder, [], 'Else', reviewed.send.review);
    assert.equal(refused.ok ? null : refused.code, 'push_refused');
  });

  test('every refusal is said in words, and nothing is sent', async () => {
    const plain = join(root, 'projects', 'plain');
    mkdirSync(plain);
    writeFileSync(join(plain, 'a.txt'), 'a\n');
    const notRepo = await sender().review(PROJECT, plain, []);
    assert.equal(notRepo.ok ? null : notRepo.code, 'not_a_repository');
    const nothing = await sender().review(PROJECT, folder, []);
    assert.equal(nothing.ok ? null : nothing.code, 'nothing_to_send');
    writeFileSync(join(folder, 'app.txt'), 'lanterns\n');
    git(folder, 'remote', 'set-url', 'origin', 'https://gitlab.example/owner/garden.git');
    const elsewhere = await sender().review(PROJECT, folder, []);
    assert.equal(elsewhere.ok ? null : elsewhere.code, 'no_github_origin');
    git(folder, 'remote', 'set-url', 'origin', REMOTE);
    git(folder, 'checkout', '-q', '--detach');
    const detached = await sender().review(PROJECT, folder, []);
    assert.equal(detached.ok ? null : detached.code, 'detached_head');
    git(folder, 'checkout', '-q', 'main');
    // A push GitHub refuses: here the stand-in repository is gone.
    const reviewed = await sender().review(PROJECT, folder, []);
    assert.ok(reviewed.ok);
    rmSync(origin, { recursive: true, force: true });
    const refused = await sender().send(PROJECT, folder, [], 'Lanterns', reviewed.send.review);
    assert.equal(refused.ok ? null : refused.code, 'push_refused');
    assert.equal(existsSync(ghLog) && readFileSync(ghLog, 'utf8').includes('pr create'), false);
  });
});
