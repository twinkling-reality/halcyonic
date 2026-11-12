import assert from 'node:assert/strict';
import {
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
import type { CheckId, ProjectId } from '@halcyonic/contracts';
import { standInGitHub } from '../testing/github-stand-in.ts';
import { GitHubSender } from './github.ts';

// Publish to the person's own GitHub Pages (ADR 0039, amended), proven only against stand-ins: a
// local bare repository as origin and a stand-in gh. Nothing reaches github.com.

const PROJECT = '01a19000-0000-7000-8000-00000000ab11' as ProjectId;
const CHECK = '01a19000-0000-7000-8000-00000000cc11' as CheckId;
const BRANCH = 'refs/heads/halcyonic-pages';

let root = '';
let stand: ReturnType<typeof standInGitHub>;
let built = '';

function sender(gh: string | null = stand.ghPath) {
  return new GitHubSender({
    git: '/usr/bin/git',
    gh,
    dataDir: join(root, 'data'),
    env: { HOME: stand.home },
  });
}

beforeEach(() => {
  root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-pages-')));
  stand = standInGitHub(root);
  // A check's built copy, as the preview serves it.
  built = join(root, 'checks', 'dist');
  mkdirSync(join(built, 'assets'), { recursive: true });
  writeFileSync(join(built, 'index.html'), '<script src="./assets/app.js"></script>\n');
  writeFileSync(join(built, 'assets', 'app.js'), 'console.log(1);\n');
  writeFileSync(join(built, '.env'), 'KEY=secret\n');
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe('Publish to GitHub Pages (ADR 0039, amended)', {
  skip: process.platform !== 'darwin',
}, () => {
  test('the review names the repository, the check, its built files and the address; never a secret', async () => {
    const reviewed = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    assert.equal(reviewed.publish.repository, 'owner/garden');
    assert.equal(reviewed.publish.check_id, CHECK);
    assert.equal(reviewed.publish.files, 2, 'index.html and app.js, never .env');
    assert.equal(reviewed.publish.replaces, null);
    assert.equal(reviewed.publish.address, 'https://owner.github.io/garden/');
    const noGh = await sender(null).publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.equal(noGh.ok ? null : noGh.code, 'no_gh');
    const noBuild = await sender().publishReview(PROJECT, stand.folder, CHECK, null, null);
    assert.equal(noBuild.ok ? null : noBuild.code, 'no_built_copy');
  });

  test('a link in the built copy that points outside it is never published, and the review lists it', async () => {
    symlinkSync('/etc', join(built, 'assets', 'escape'));
    const reviewed = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    assert.equal(reviewed.publish.files, 2);
    assert.deepEqual(reviewed.publish.links_left_out, { paths: ['assets/escape'], total: 1 });
  });

  test('a publish pushes the built copy alone to the Pages branch, turns Pages on, and a second builds on the first, never forced', async () => {
    const head = stand.git(stand.folder, 'rev-parse', 'HEAD');
    const status = stand.git(stand.folder, 'status', '--porcelain');
    const reviewed = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(reviewed.ok);
    const stale = await sender().publish(PROJECT, stand.folder, CHECK, built, null, 'f'.repeat(64));
    assert.equal(stale.ok ? null : stale.code, 'review_stale');
    const first = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      reviewed.publish.review,
    );
    assert.ok(first.ok, first.ok ? '' : first.message);
    assert.equal(first.address, 'https://owner.github.io/garden/');
    assert.equal(stand.git(stand.origin, 'rev-parse', BRANCH), first.commit);
    assert.equal(
      stand.git(stand.origin, 'ls-tree', '-r', '--name-only', first.commit),
      'assets/app.js\nindex.html',
      'the built copy only: no source, no secret',
    );
    const asked = readFileSync(stand.ghLog, 'utf8');
    assert.match(
      asked,
      /api --method POST repos\/owner\/garden\/pages -f source\[branch\]=halcyonic-pages/,
    );
    assert.match(asked, /api repos\/owner\/garden\/pages --jq .html_url/);
    // The person's own folder and branch are untouched.
    assert.equal(stand.git(stand.folder, 'rev-parse', 'HEAD'), head);
    assert.equal(stand.git(stand.folder, 'status', '--porcelain'), status);
    // A new check's build, on top of the last publish.
    writeFileSync(join(built, 'assets', 'app.js'), 'console.log(2);\n');
    const next = await sender().publishReview(PROJECT, stand.folder, CHECK, built, first.commit);
    assert.ok(next.ok);
    const second = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      first.commit,
      next.publish.review,
    );
    assert.ok(second.ok);
    assert.equal(stand.git(stand.origin, 'rev-parse', `${second.commit}^`), first.commit);
    // A publish that does not build on what the branch holds is refused, never forced.
    writeFileSync(join(built, 'assets', 'app.js'), 'console.log(3);\n');
    const blind = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(blind.ok);
    const refused = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      blind.publish.review,
    );
    assert.equal(refused.ok ? null : refused.code, 'push_refused');
    assert.equal(stand.git(stand.origin, 'rev-parse', BRANCH), second.commit);
  });

  test('unpublishing deletes the Pages branch with the person’s own git', async () => {
    const reviewed = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(reviewed.ok);
    const published = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      reviewed.publish.review,
    );
    assert.ok(published.ok);
    const down = await sender().unpublish(PROJECT, stand.folder, {
      repository: 'owner/garden',
      commit: published.commit,
    });
    assert.ok(down.ok, down.ok ? '' : down.message);
    assert.throws(() => stand.git(stand.origin, 'rev-parse', '--verify', BRANCH));
  });

  test('a review asks nothing of gh; only the press does (AUDIT3)', async () => {
    const reviewed = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(reviewed.ok);
    assert.equal(existsSync(stand.ghLog), false, 'a review runs no gh');
  });

  test("Take Down acts on the repository the game was published to, never the folder's origin now (AUDIT3 U1)", async () => {
    const reviewed = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(reviewed.ok);
    const first = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      reviewed.publish.review,
    );
    assert.ok(first.ok, first.ok ? '' : first.message);
    // Another repository of the person's, with a Pages branch of its own, by the test's own insteadOf.
    const other = join(root, 'other.git');
    stand.git(root, 'init', '--bare', '-q', other);
    stand.git(stand.folder, 'push', '-q', other, 'main:refs/heads/halcyonic-pages');
    writeFileSync(
      join(stand.home, '.gitconfig'),
      `${readFileSync(join(stand.home, '.gitconfig'), 'utf8')}[url "file://${other}"]\n\tinsteadOf = https://github.com/owner/other.git\n`,
    );
    stand.git(stand.folder, 'remote', 'set-url', 'origin', 'https://github.com/owner/other.git');
    const down = await sender().unpublish(PROJECT, stand.folder, {
      repository: 'owner/garden',
      commit: first.commit,
    });
    assert.equal(down.ok ? null : down.code, 'origin_changed');
    assert.match(down.ok ? '' : down.message, /published to owner\/garden/);
    assert.equal(stand.git(stand.origin, 'rev-parse', BRANCH), first.commit, 'still published');
    assert.ok(stand.git(other, 'rev-parse', BRANCH), "the other repository's branch is untouched");
    // The origin back: it comes down.
    stand.git(stand.folder, 'remote', 'set-url', 'origin', 'https://github.com/owner/garden.git');
    const back = await sender().unpublish(PROJECT, stand.folder, {
      repository: 'owner/garden',
      commit: first.commit,
    });
    assert.ok(back.ok, back.ok ? '' : back.message);
    assert.throws(() => stand.git(stand.origin, 'rev-parse', '--verify', BRANCH));
  });

  test("Take Down never deletes a commit Halcyonic didn't publish (AUDIT3 U2)", async () => {
    const reviewed = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(reviewed.ok);
    const first = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      reviewed.publish.review,
    );
    assert.ok(first.ok);
    // The person's own commit on top, pushed from another clone.
    const clone = join(root, 'clone');
    stand.git(root, 'clone', '-q', '--branch', 'halcyonic-pages', stand.origin, clone);
    writeFileSync(join(clone, 'CNAME'), 'game.example.test\n');
    stand.git(clone, 'add', 'CNAME');
    stand.git(clone, 'commit', '-q', '-m', 'mine');
    stand.git(clone, 'push', '-q', 'origin', 'halcyonic-pages');
    const theirs = stand.git(clone, 'rev-parse', 'HEAD');
    const down = await sender().unpublish(PROJECT, stand.folder, {
      repository: 'owner/garden',
      commit: first.commit,
    });
    assert.equal(down.ok ? null : down.code, 'pages_changed');
    assert.equal(stand.git(stand.origin, 'rev-parse', BRANCH), theirs, 'their commit is kept');
  });
});
