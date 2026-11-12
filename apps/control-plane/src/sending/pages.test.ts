import assert from 'node:assert/strict';
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

  test('a game is published only where the host keeps a public page for it: another host is refused in words', async () => {
    stand.git(
      stand.folder,
      'remote',
      'set-url',
      'origin',
      'https://gitlab.example/group/garden.git',
    );
    const reviewed = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.equal(reviewed.ok ? null : reviewed.code, 'no_pages_host');
    assert.match(reviewed.ok ? '' : reviewed.message, /this project's is on gitlab\.example/);
  });

  test('a link in the built copy that points outside it is never published, and the review lists it', async () => {
    symlinkSync('/etc', join(built, 'assets', 'escape'));
    const reviewed = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    assert.equal(reviewed.publish.files, 2);
    assert.deepEqual(reviewed.publish.links_left_out, { paths: ['assets/escape'], total: 1 });
  });

  test('a repository Halcyonic made private is said so, Make Public makes it public first, and the choice is in the digest', async () => {
    const made = { repository: 'owner/garden', visibility: 'private' as const };
    const plain = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(plain.ok);
    assert.equal(plain.publish.visibility, 'unknown', 'a review asks GitHub nothing');
    const shown = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null, { made });
    assert.ok(shown.ok);
    assert.equal(shown.publish.visibility, 'private');
    assert.equal(shown.publish.make_public, false);
    const opening = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null, {
      made,
      makePublic: true,
    });
    assert.ok(opening.ok);
    assert.equal(opening.publish.make_public, true);
    assert.notEqual(opening.publish.review, shown.publish.review);
    const crossed = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      shown.publish.review,
      {
        made,
        makePublic: true,
      },
    );
    assert.equal(crossed.ok ? null : crossed.code, 'review_stale');
    const opened: string[] = [];
    const done = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      opening.publish.review,
      {
        made,
        makePublic: true,
        onMadePublic: (repository) => opened.push(repository),
      },
    );
    assert.ok(done.ok, done.ok ? '' : done.message);
    assert.deepEqual(opened, ['owner/garden'], 'recorded the moment it was made public');
    const log = readFileSync(stand.ghLog, 'utf8');
    assert.ok(
      log.indexOf(
        'repo edit owner/garden --visibility public --accept-visibility-change-consequences',
      ) < log.indexOf('api --method POST repos/owner/garden/pages'),
      'made public before pages are asked for',
    );
    // A repository the journal does not say this project's first Send made private is never made public by
    // a press: one the person had before keeps who can see it (AUDIT3 S1).
    const theirs = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null, {
      makePublic: true,
    });
    assert.ok(theirs.ok);
    assert.equal(theirs.publish.visibility, 'unknown');
    assert.equal(theirs.publish.make_public, false);
    // Already public: there is nothing to make public.
    const open = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null, {
      made: { ...made, visibility: 'public' },
      makePublic: true,
    });
    assert.ok(open.ok);
    assert.equal(open.publish.make_public, false);
  });

  test('a site GitHub does not keep is no publish: the branch comes down again and the refusal says why', async () => {
    writeFileSync(join(root, 'pages-refused'), '');
    const made = { repository: 'owner/garden', visibility: 'private' as const };
    const reviewed = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null, {
      made,
    });
    assert.ok(reviewed.ok);
    const refused = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      reviewed.publish.review,
      {
        made,
      },
    );
    assert.equal(refused.ok ? null : refused.code, 'pages_refused');
    assert.match(
      refused.ok ? '' : refused.message,
      /private repository on a free plan.*Make Public/,
    );
    assert.equal(refused.ok ? null : (refused.effect ?? 'none'), 'none');
    assert.throws(
      () => stand.git(stand.origin, 'rev-parse', '--verify', BRANCH),
      'no Pages branch is left',
    );
    // Made public first and then refused: that it is public now is recorded and said.
    const opened: string[] = [];
    const opening = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null, {
      made,
      makePublic: true,
    });
    assert.ok(opening.ok);
    const after = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      opening.publish.review,
      {
        made,
        makePublic: true,
        onMadePublic: (repository) => opened.push(repository),
      },
    );
    assert.equal(after.ok ? null : after.code, 'pages_refused');
    assert.match(
      after.ok ? '' : after.message,
      /owner\/garden is public now: Make Private puts it back\./,
    );
    assert.doesNotMatch(
      after.ok ? '' : after.message,
      /paid plan/,
      'it is public: a plan is not why',
    );
    assert.equal(
      after.ok ? null : after.effect,
      'unknown',
      'the repository changed: never effect none (run 2, AUDIT3)',
    );
    assert.deepEqual(opened, ['owner/garden']);
    assert.throws(() => stand.git(stand.origin, 'rev-parse', '--verify', BRANCH));
  });

  test('a push GitHub refuses after Make Public says truly that the repository is public now, with the way back (run 2)', async () => {
    const made = { repository: 'owner/garden', visibility: 'private' as const };
    // The branch's lock taken in the origin: git refuses the push with its own words.
    writeFileSync(join(stand.origin, 'refs', 'heads', 'halcyonic-pages.lock'), '');
    const opening = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null, {
      made,
      makePublic: true,
    });
    assert.ok(opening.ok);
    const opened: string[] = [];
    const refused = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      opening.publish.review,
      { made, makePublic: true, onMadePublic: (repository) => opened.push(repository) },
    );
    assert.equal(refused.ok ? null : refused.code, 'push_refused');
    assert.equal(refused.ok ? null : refused.effect, 'unknown');
    assert.match(
      refused.ok ? '' : refused.message,
      /no page changed.*owner\/garden is public now: Make Private puts it back\./,
    );
    assert.deepEqual(opened, ['owner/garden'], 'recorded the moment it was made public');
    // Without Make Public, the same refusal changed nothing: effect none, and nothing said of public.
    const plain = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(plain.ok);
    const unchanged = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      plain.publish.review,
    );
    assert.equal(unchanged.ok ? null : unchanged.code, 'push_refused');
    assert.equal(unchanged.ok ? null : (unchanged.effect ?? 'none'), 'none');
    assert.doesNotMatch(unchanged.ok ? '' : unchanged.message, /public now/);
  });

  test("Make Private makes the repository private with the person's own gh, and says so when GitHub does not (run 2)", async () => {
    const done = await sender().makePrivate('owner/garden');
    assert.ok(done.ok, done.ok ? '' : done.message);
    assert.match(
      readFileSync(stand.ghLog, 'utf8'),
      /repo edit owner\/garden --visibility private --accept-visibility-change-consequences/,
    );
    const odd = await sender().makePrivate('owner/../garden');
    assert.equal(odd.ok ? null : odd.code, 'make_private_failed');
    const none = await sender(null).makePrivate('owner/garden');
    assert.equal(none.ok ? null : none.code, 'no_gh');
    // A gh that refuses: GitHub may or may not have changed it, so the effect is unknown.
    const failing = join(root, 'bin', 'gh-failing');
    writeFileSync(failing, '#!/bin/sh\n[ "$1" = auth ] && exit 0\nexit 1\n');
    chmodSync(failing, 0o755);
    const refused = await sender(failing).makePrivate('owner/garden');
    assert.equal(refused.ok ? null : refused.code, 'not_made_private');
    assert.equal(refused.ok ? null : refused.effect, 'unknown');
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

  test("a repository's owner and name are the same in any case: as gh spells them, and as a folder's origin does (the first real run)", async () => {
    // gh printed the account with capitals; the folder's origin spells it in lower case.
    const made = { repository: 'Owner/Garden', visibility: 'private' as const };
    const reviewed = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null, {
      made,
      makePublic: true,
    });
    assert.ok(reviewed.ok, reviewed.ok ? '' : reviewed.message);
    assert.equal(reviewed.publish.visibility, 'private');
    assert.equal(
      reviewed.publish.make_public,
      true,
      'Make Public is offered for the repository Halcyonic made',
    );
    const done = await sender().publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      reviewed.publish.review,
      { made, makePublic: true },
    );
    assert.ok(done.ok, done.ok ? '' : done.message);
    // Taken down from the repository it went to, whatever case the record spells it in.
    const down = await sender().unpublish(PROJECT, stand.folder, {
      repository: 'OWNER/garden',
      commit: done.commit,
    });
    assert.ok(down.ok, down.ok ? '' : down.message);
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

describe('a built copy holding a secret the host keeps is never published (the lead, 2026-11-12)', {
  skip: process.platform !== 'darwin',
}, () => {
  test('Publish refuses at the review and the press, naming the file and the secret, never the value', async () => {
    const held = [{ what: 'MY_SERVICE_KEY', value: 'svc-held-value-0123456789abcdef' }];
    const holding = new GitHubSender({
      git: '/usr/bin/git',
      gh: stand.ghPath,
      dataDir: join(root, 'data'),
      env: { HOME: stand.home },
      held: () => held,
    });
    writeFileSync(
      join(built, 'assets', 'app.js'),
      `fetch("/x", { headers: { k: ${JSON.stringify(held[0]?.value)} } });\n`,
    );
    const reviewed = await holding.publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.equal(reviewed.ok ? null : reviewed.code, 'holds_secret');
    assert.equal(
      reviewed.ok ? '' : reviewed.message,
      'assets/app.js holds a secret this Mac keeps (MY_SERVICE_KEY), so nothing was published. Take it out of the file, then try again.',
    );
    const plain = await sender().publishReview(PROJECT, stand.folder, CHECK, built, null);
    assert.ok(plain.ok);
    const pressed = await holding.publish(
      PROJECT,
      stand.folder,
      CHECK,
      built,
      null,
      plain.publish.review,
    );
    assert.equal(pressed.ok ? null : pressed.code, 'holds_secret');
    assert.throws(
      () => stand.git(stand.origin, 'rev-parse', '--verify', BRANCH),
      'nothing was pushed',
    );
  });
});
