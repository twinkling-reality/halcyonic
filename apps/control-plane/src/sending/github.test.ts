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
    `[user]\n\tname = Test Person\n\temail = person@example.test\n[url "file://${origin}"]\n\tinsteadOf = ${REMOTE}\n[init]\n\tdefaultBranch = main\n`,
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
