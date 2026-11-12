import { execFile } from 'node:child_process';
import { lstat, mkdir, open, readdir, realpath, rename, rm, writeFile } from 'node:fs/promises';
import { basename, dirname, join, relative, sep } from 'node:path';
import {
  type ChangedFiles,
  type CheckId,
  PAGES_BRANCH,
  type ProjectId,
  type PublishReview,
  type RepositoryVisibility,
  type SendRemote,
  type SendReview,
} from '@halcyonic/contracts';
import { secretShapedPath } from '@halcyonic/runtime-core';
import { readOriginUrl } from '../catalog-match.ts';
import { changedFileList, pathList } from '../checkpoints/checkpointer.ts';
import { linkLeavesFolder } from '../checkpoints/links.ts';
import { type HeldHit, heldInTree, heldMessage } from '../core/held-in-tree.ts';
import type { HeldSecret } from '../core/redaction.ts';
import { reviewDigest } from '../core/review.ts';

/**
 * Send to GitHub (ADR 0039, amended): the project's work as one commit on top of the branch its
 * folder is on, pushed as a new branch of its origin on github.com with the person's own git, and a
 * pull request opened with the person's own `gh`.
 *
 * The person's repository is only read: its HEAD's branch, its origin and, by a fetch, its commits.
 * Nothing runs inside it with its own settings or hooks, which an agent working there could have
 * written. The commit is built in a bare repository of Halcyonic's own in its data folder, from the
 * folder's files as git sees them, without any file whose name says it may hold secrets, without
 * other projects' folders inside this one and without a link that points outside the folder; it is pushed
 * from there with no hooks. Git and gh run in
 * the person's own environment, so their own credential helper and sign-in are used: Halcyonic
 * passes no token, reads none, and keeps from what they print only an exit code, the commit and the
 * pull request's address.
 */

export interface GitHubSenderDeps {
  /** The person's git, by absolute path (`/usr/bin/git` on a Mac). */
  readonly git: string;
  /** The person's gh, by absolute path, or null: then a send pushes and opens no pull request. */
  readonly gh: string | null;
  /** Halcyonic's data folder: its own sending repositories live in `sending/` there. */
  readonly dataDir: string;
  /** The environment git and gh run with; only the names in `PASSED` are taken from it. */
  readonly env: NodeJS.ProcessEnv;
  /**
   * Every secret the host holds, as redaction reads them: a tree that holds one, in any form
   * `heldForms` states, is never sent or published (the lead, 2026-11-12). Absent, none is held.
   */
  readonly held?: () => Iterable<HeldSecret>;
}

/** A refusal in the product's words; `effect` says when something on GitHub may have changed anyway. */
export type SendFailure = {
  readonly ok: false;
  readonly code: string;
  readonly message: string;
  readonly effect?: 'unknown';
  /** For `holds_secret`: the file and which secret, never its value. */
  readonly hit?: HeldHit;
};

/** What this Mac's journal says of a repository a send of Halcyonic's made, which Publish reads. */
export interface MadeRepository {
  readonly repository: string;
  readonly visibility: RepositoryVisibility;
}

/**
 * A send's choices and what the journal knows: who can see a repository it makes (private unless
 * public), its name (the folder's unless given), the repository an earlier first send made for this
 * project, and `onMade`, called the moment GitHub has made one, so the journal holds it even when
 * what follows fails.
 */
export interface SendOptions {
  readonly visibility?: RepositoryVisibility;
  readonly name?: string | undefined;
  readonly made?: MadeRepository | undefined;
  readonly onMade?: (made: MadeRepository) => void;
}

/**
 * Publish's choices: the repository the journal knows was made, whether to make it public first, and
 * `onMadePublic`, called the moment GitHub has made it public, whatever follows.
 */
export interface PublishOptions {
  readonly made?: MadeRepository | undefined;
  readonly makePublic?: boolean;
  readonly onMadePublic?: (repository: string) => void;
}

interface Prepared {
  readonly send: SendReview;
  readonly ssh: boolean;
  readonly tree: string;
  readonly repository: string;
  /** What the folder holds that the send kept out (a first send's): its clone's git keeps them out too. */
  readonly withheld?: readonly string[];
}

/** The person's environment as git and gh need it, and nothing that could carry one of our tokens. */
const PASSED = ['HOME', 'USER', 'LOGNAME', 'LANG', 'LC_ALL', 'TMPDIR', 'SSH_AUTH_SOCK'] as const;

/** The most of a program's output read, which is never logged or kept but for its last line. */
const OUTPUT_MAX = 1024 * 1024;

/** The most of a tree's files read at once to look for held secrets: one batch, or one larger file. */
const TREE_READ_MAX = 512 * 1024 * 1024;

const PULL_REQUEST =
  /https:\/\/github\.com\/[A-Za-z0-9][A-Za-z0-9-]{0,38}\/[A-Za-z0-9._-]{1,100}\/pull\/[0-9]{1,10}/;

export class GitHubSender {
  readonly #deps: GitHubSenderDeps;

  constructor(deps: GitHubSenderDeps) {
    this.#deps = deps;
  }

  /** What a send would do now, with its digest; nothing leaves this Mac, and gh is not run. */
  async review(
    projectId: ProjectId,
    folder: string,
    otherFolders: readonly string[],
    title: string,
    options: SendOptions = {},
  ): Promise<{ readonly ok: true; readonly send: SendReview } | SendFailure> {
    const prepared = await this.#prepare(projectId, folder, otherFolders, title, options);
    return prepared.ok ? { ok: true, send: prepared.prepared.send } : prepared;
  }

  /**
   * Sends what `review` described, checked again first: a folder changed since sends nothing. The
   * push never forces and never names a branch that is there. A folder that is no repository yet is
   * sent once as a new repository of the person's own, and becomes its clone.
   */
  async send(
    projectId: ProjectId,
    folder: string,
    otherFolders: readonly string[],
    title: string,
    review: string,
    options: SendOptions = {},
  ): Promise<
    | {
        readonly ok: true;
        readonly send: SendReview & { readonly remote: SendRemote };
        readonly commit: string;
        readonly reviewRequestUrl: string | null;
        /** False when the work is on GitHub but the folder could not be made its clone. */
        readonly cloned: boolean;
      }
    | SendFailure
  > {
    const prepared = await this.#prepare(projectId, folder, otherFolders, title, options);
    if (!prepared.ok) return prepared;
    const { send, repository, tree, ssh } = prepared.prepared;
    if (send.review !== review)
      return refuse(
        'review_stale',
        'The project changed since its files were shown, so nothing was sent. Look at them again, then send.',
      );
    const name = await this.#run(this.#deps.git, ['--git-dir', repository, 'config', 'user.name']);
    const email = await this.#run(this.#deps.git, [
      '--git-dir',
      repository,
      'config',
      'user.email',
    ]);
    if (name.code !== 0 || email.code !== 0)
      return refuse(
        'no_author',
        'Git on this computer has no name and email to sign the commit with. Set them in git, then send again.',
      );
    if (send.creates !== null || send.remote === null || send.base_commit === null)
      return this.#sendNew(folder, prepared.prepared, title, options);
    const committed = await this.#run(this.#deps.git, [
      '--git-dir',
      repository,
      'commit-tree',
      tree,
      '-p',
      send.base_commit,
      '-m',
      title,
    ]);
    const commit = committed.stdout.trim();
    if (committed.code !== 0 || !/^(?:[0-9a-f]{40}|[0-9a-f]{64})$/.test(commit))
      return refuse('send_failed', 'The commit could not be made, so nothing was sent.');
    const remote = remoteOf(send.remote, ssh);
    const sentTo = { ...send, remote: send.remote };
    const pushed = await this.#run(
      this.#deps.git,
      [
        '--git-dir',
        repository,
        '-c',
        'core.hooksPath=/dev/null',
        'push',
        '--quiet',
        remote,
        `${commit}:refs/heads/${send.new_branch}`,
      ],
      120_000,
    );
    if (pushed.code !== 0) {
      // A branch of that name already there is why, when this very work was sent before (AUDIT3 S3).
      const there = await this.#run(
        this.#deps.git,
        ['--git-dir', repository, 'ls-remote', '--heads', remote, `refs/heads/${send.new_branch}`],
        60_000,
      );
      if (there.code === 0 && there.stdout.trim() !== '')
        return refuse(
          'already_sent',
          `This work was already sent as ${send.new_branch}, so nothing new was sent. Change something, then send again.`,
        );
      return refuse(
        'push_refused',
        `${send.remote.host} didn't take the push, so nothing was sent. Check that this computer can push to the repository, then send again.`,
      );
    }
    let reviewRequestUrl: string | null = null;
    // A review request only where the host's tool is set up here (the review said so).
    if (this.#deps.gh !== null && send.review_request) {
      const opened = await this.#run(
        this.#deps.gh,
        [
          'pr',
          'create',
          '--repo',
          send.remote.path,
          '--head',
          send.new_branch,
          '--base',
          send.branch,
          '--title',
          title,
          '--body',
          'Sent from Halcyonic: the project as it is on the computer, on top of this branch.',
        ],
        60_000,
      );
      if (opened.code === 0) reviewRequestUrl = PULL_REQUEST.exec(opened.stdout)?.[0] ?? null;
    }
    return { ok: true, send: sentTo, commit, reviewRequestUrl, cloned: true };
  }

  /**
   * The first send of a folder that is no repository: asked of gh only now, whether it is signed in;
   * then a new repository of the person's own, made with their gh as the review named it and recorded
   * at once (`onMade`), or the one an earlier first send made for this project; the work pushed as
   * its `main`, never forced; then the folder made its clone, so the person's git, every later send,
   * Publish and Take Down see an ordinary repository. A send that stopped after GitHub made the
   * repository is sent again into it: an empty `main` takes the push, and a `main` holding
   * Halcyonic's own earlier send (a clone that did not take) is built on, or only cloned where the
   * work is the same. Anything else in it is refused.
   */
  async #sendNew(
    folder: string,
    prepared: Prepared,
    title: string,
    options: SendOptions,
  ): Promise<
    | {
        readonly ok: true;
        readonly send: SendReview & { readonly remote: SendRemote };
        readonly commit: string;
        readonly reviewRequestUrl: null;
        readonly cloned: boolean;
      }
    | SendFailure
  > {
    const { send, repository, tree } = prepared;
    const gh = this.#deps.gh;
    if (gh === null)
      return refuse(
        'no_gh',
        "Making a repository on GitHub needs GitHub's own command line tool on this computer, signed in.",
      );
    if ((await this.#run(gh, ['auth', 'status'], 15_000)).code !== 0)
      return refuse(
        'gh_signed_out',
        "GitHub's command line tool on this computer isn't signed in, so nothing was sent.",
      );
    let target: SendRemote;
    if (send.creates !== null) {
      const { name, visibility } = send.creates;
      const made = await this.#run(gh, ['repo', 'create', `--${visibility}`, '--', name], 60_000);
      const named = REPOSITORY_ADDRESS.exec(made.stdout);
      if (made.code !== 0 || named === null || named[2] !== name)
        return {
          ...refuse(
            'repository_not_made',
            `GitHub didn't make a repository named ${name}, or didn't say so, so nothing was pushed. Your account may hold one of that name already: send it with another name.`,
          ),
          effect: 'unknown',
        };
      target = { host: MAKES_REPOSITORIES, path: `${named[1]}/${named[2]}` };
      options.onMade?.({ repository: target.path, visibility });
    } else if (send.remote !== null) target = send.remote;
    else return refuse('send_failed', 'Halcyonic could not prepare the send.');
    // The person's own choice of how their git reaches GitHub, as gh keeps it.
    const protocol = await this.#run(
      gh,
      ['config', 'get', 'git_protocol', '-h', target.host],
      15_000,
    );
    const fullName = target.path;
    const remote = remoteOf(target, protocol.code === 0 && protocol.stdout.trim() === 'ssh');
    const there = await this.#run(
      this.#deps.git,
      ['--git-dir', repository, 'ls-remote', '--heads', remote, 'refs/heads/main'],
      60_000,
    );
    if (there.code !== 0)
      return {
        ...refuse(
          'push_refused',
          `GitHub has the repository ${fullName}, but this computer couldn't reach it, so nothing was pushed. Check that it can push there, then send again.`,
        ),
        effect: 'unknown',
      };
    const held = there.stdout.trim().split(/\s/)[0] ?? '';
    // What `main` holds, if anything: a commit Halcyonic itself sent is known by its own repository
    // holding it (a first send whose clone did not take); anything else is someone else's work.
    let parent: string | null = null;
    if (held !== '') {
      const ours = await this.#run(this.#deps.git, [
        '--git-dir',
        repository,
        'cat-file',
        '-e',
        `${held}^{commit}`,
      ]);
      if (ours.code !== 0)
        return refuse(
          'repository_not_empty',
          `${fullName} already has commits Halcyonic didn't send, so nothing was pushed. Send it with git yourself.`,
        );
      parent = held;
    }
    const sameTree =
      parent !== null &&
      (
        await this.#run(this.#deps.git, [
          '--git-dir',
          repository,
          'rev-parse',
          '--verify',
          '--quiet',
          `${parent}^{tree}`,
        ])
      ).stdout.trim() === tree;
    let commit: string;
    if (parent !== null && sameTree) commit = parent;
    else {
      const committed = await this.#run(this.#deps.git, [
        '--git-dir',
        repository,
        'commit-tree',
        tree,
        ...(parent === null ? [] : ['-p', parent]),
        '-m',
        title,
      ]);
      commit = committed.stdout.trim();
      if (committed.code !== 0 || !/^(?:[0-9a-f]{40}|[0-9a-f]{64})$/.test(commit))
        return refuse('send_failed', 'The commit could not be made, so nothing was pushed.');
      // On top of Halcyonic's own earlier commit, or into an empty `main`: never a force.
      const pushed = await this.#run(
        this.#deps.git,
        [
          '--git-dir',
          repository,
          '-c',
          'core.hooksPath=/dev/null',
          'push',
          '--quiet',
          remote,
          `${commit}:refs/heads/main`,
        ],
        120_000,
      );
      if (pushed.code !== 0)
        return {
          ...refuse(
            'push_refused',
            `GitHub has the repository ${fullName}, but didn't take the push. Check that this computer can push to it, then send again.`,
          ),
          effect: 'unknown',
        };
    }
    const cloned = await this.#cloneInto(
      folder,
      repository,
      commit,
      remote,
      prepared.withheld ?? [],
    );
    return {
      ok: true,
      send: { ...send, remote: target },
      commit,
      reviewRequestUrl: null,
      cloned,
    };
  }

  /**
   * Makes the folder the repository's clone, with none of anyone's git settings, templates or hooks,
   * whole or not at all: its git folder is built beside, in the folder, as a bare repository holding
   * the sent commit as `main` and `origin/main` (pushed in from Halcyonic's own repository, so no
   * fetch record names this Mac's paths), `origin` the repository and the index as that commit; then
   * it is made the folder's `.git` in one rename, which fails where a `.git` appeared meanwhile.
   * The files themselves are never touched. False when it did not take; nothing is left behind.
   */
  async #cloneInto(
    folder: string,
    repository: string,
    commit: string,
    remote: string,
    withheld: readonly string[],
  ): Promise<boolean> {
    for (const entry of await readdir(folder).catch(() => [] as string[]))
      if (entry.startsWith(CLONE_ASIDE))
        await rm(join(folder, entry), { recursive: true, force: true });
    const aside = join(folder, `${CLONE_ASIDE}${process.pid}-${Date.now()}`);
    const none = { GIT_CONFIG_GLOBAL: '/dev/null', GIT_CONFIG_NOSYSTEM: '1' };
    const steps: string[][] = [
      ['init', '--bare', '--quiet', '--template=', '-b', 'main', aside],
      [
        '--git-dir',
        repository,
        '-c',
        'core.hooksPath=/dev/null',
        'push',
        '--quiet',
        aside,
        `${commit}:refs/remotes/origin/main`,
      ],
      ['--git-dir', aside, 'update-ref', 'refs/heads/main', commit],
      ['--git-dir', aside, 'config', 'remote.origin.url', remote],
      ['--git-dir', aside, 'config', 'remote.origin.fetch', '+refs/heads/*:refs/remotes/origin/*'],
      ['--git-dir', aside, 'config', 'branch.main.remote', 'origin'],
      ['--git-dir', aside, 'config', 'branch.main.merge', 'refs/heads/main'],
      ['--git-dir', aside, 'read-tree', 'refs/heads/main'],
      ['--git-dir', aside, 'config', 'core.bare', 'false'],
      ['--git-dir', aside, 'config', 'core.logAllRefUpdates', 'true'],
    ];
    try {
      for (const step of steps)
        if ((await this.#run(this.#deps.git, step, 60_000, none)).code !== 0) return false;
      // What the send kept out stays out of the person's own git too: each path, as it is, anchored (AUDIT3 S2).
      await mkdir(join(aside, 'info'), { recursive: true, mode: 0o700 });
      await writeFile(
        join(aside, 'info', 'exclude'),
        withheld.map((path) => `/${path.replace(/[\\*?[\]!# ]/g, (c) => `\\${c}`)}\n`).join(''),
        { mode: 0o600 },
      );
      if ((await lstat(join(folder, '.git')).catch(() => null)) !== null) return false;
      await rename(aside, join(folder, '.git'));
      return true;
    } catch {
      return false;
    } finally {
      await rm(aside, { recursive: true, force: true });
    }
  }

  /** Halcyonic's own bare repository for the project, made on first use in the person's object format. */
  async #repository(projectId: ProjectId, format: 'sha1' | 'sha256'): Promise<string | null> {
    const repository = join(this.#deps.dataDir, 'sending', `${projectId}.git`);
    if ((await lstat(repository).catch(() => null)) !== null) return repository;
    await mkdir(dirname(repository), { recursive: true, mode: 0o700 });
    const made = await this.#run(this.#deps.git, [
      'init',
      '--bare',
      '--quiet',
      `--object-format=${format}`,
      repository,
    ]);
    return made.code === 0 ? repository : null;
  }

  /**
   * What Publish would do now (ADR 0039, amended): `built`, the check's built copy, as one commit on
   * the Pages branch on top of `replaces`, the last publish's commit, at the address GitHub Pages
   * gives the repository. Nothing leaves this Mac, and gh is not run.
   */
  async publishReview(
    projectId: ProjectId,
    folder: string,
    checkId: CheckId,
    built: string | null,
    replaces: string | null,
    options: PublishOptions = {},
  ): Promise<{ readonly ok: true; readonly publish: PublishReview } | SendFailure> {
    const prepared = await this.#preparePublish(
      projectId,
      folder,
      checkId,
      built,
      replaces,
      options,
    );
    return prepared.ok ? { ok: true, publish: prepared.publish } : prepared;
  }

  /** Publishes what `publishReview` described, checked again first; never a force. */
  async publish(
    projectId: ProjectId,
    folder: string,
    checkId: CheckId,
    built: string | null,
    replaces: string | null,
    review: string,
    options: PublishOptions = {},
  ): Promise<
    | {
        readonly ok: true;
        readonly publish: PublishReview;
        readonly commit: string;
        readonly address: string | null;
      }
    | SendFailure
  > {
    const prepared = await this.#preparePublish(
      projectId,
      folder,
      checkId,
      built,
      replaces,
      options,
    );
    if (!prepared.ok) return prepared;
    const { publish, repository, tree, ssh, host } = prepared;
    if (publish.review !== review)
      return refuse(
        'review_stale',
        'The game changed since it was shown, so nothing was published. Look at it again, then publish.',
      );
    // Asked of gh only at the press: a review reaches nothing (AUDIT3, Send's S2).
    if ((await this.#run(this.#deps.gh as string, ['auth', 'status'], 15_000)).code !== 0)
      return refuse(
        'gh_signed_out',
        "GitHub's command line tool on this computer isn't signed in, so nothing was published.",
      );
    const gh = this.#deps.gh as string;
    // Made public first, as the review said: GitHub Free serves pages only from a public repository.
    if (publish.make_public) {
      const opened = await this.#run(
        gh,
        [
          'repo',
          'edit',
          publish.repository,
          '--visibility',
          'public',
          '--accept-visibility-change-consequences',
        ],
        60_000,
      );
      if (opened.code !== 0)
        return {
          ...refuse(
            'not_made_public',
            `GitHub didn't make ${publish.repository} public, or didn't say so, so nothing was published.`,
          ),
          effect: 'unknown',
        };
      options.onMadePublic?.(publish.repository);
    }
    // Once GitHub made it public, that happened, whatever fails after: every refusal from here says so,
    // with the way back, and its effect is never "none" (run 2, 2026-11-12; AUDIT3).
    const opened = publish.make_public;
    const after = (failure: SendFailure): SendFailure =>
      opened
        ? {
            ...failure,
            message: `${failure.message} ${publish.repository} is public now: Make Private puts it back.`,
            effect: 'unknown',
          }
        : failure;
    const committed = await this.#run(this.#deps.git, [
      '--git-dir',
      repository,
      'commit-tree',
      tree,
      ...(replaces === null ? [] : ['-p', replaces]),
      '-m',
      `Publish the checked game (${checkId})`,
    ]);
    const commit = committed.stdout.trim();
    if (committed.code !== 0 || !/^(?:[0-9a-f]{40}|[0-9a-f]{64})$/.test(commit))
      return after(refuse('publish_failed', 'The game could not be made ready to publish.'));
    const pushed = await this.#run(
      this.#deps.git,
      [
        '--git-dir',
        repository,
        '-c',
        'core.hooksPath=/dev/null',
        'push',
        '--quiet',
        remoteOf({ host, path: publish.repository }, ssh),
        `${commit}:refs/heads/${PAGES_BRANCH}`,
      ],
      120_000,
    );
    if (pushed.code !== 0)
      return after(
        refuse(
          'push_refused',
          `GitHub didn't take the publish, so no page changed. If the repository has a ${PAGES_BRANCH} branch Halcyonic did not write, remove it first.`,
        ),
      );
    // Pages turned on for the branch (GitHub answers that it is on already, after the first time),
    // then its own address, as GitHub says it. A site GitHub does not keep is no publish: the branch
    // just pushed comes down again, only while it still holds that commit.
    await this.#run(
      gh,
      [
        'api',
        '--method',
        'POST',
        `repos/${publish.repository}/pages`,
        '-f',
        `source[branch]=${PAGES_BRANCH}`,
        '-f',
        'source[path]=/',
      ],
      60_000,
    );
    const asked = await this.#run(
      gh,
      ['api', `repos/${publish.repository}/pages`, '--jq', '.html_url'],
      60_000,
    );
    const said = asked.stdout.trim();
    if (asked.code !== 0) {
      const removed = await this.#run(
        this.#deps.git,
        [
          '--git-dir',
          repository,
          '-c',
          'core.hooksPath=/dev/null',
          'push',
          '--quiet',
          `--force-with-lease=refs/heads/${PAGES_BRANCH}:${commit}`,
          remoteOf({ host, path: publish.repository }, ssh),
          `:refs/heads/${PAGES_BRANCH}`,
        ],
        120_000,
      );
      const failure = after(
        refuse(
          'pages_refused',
          publish.visibility === 'private' && !publish.make_public
            ? "GitHub doesn't serve pages from a private repository on a free plan, so nothing was published. Publish it again with Make Public."
            : opened
              ? `GitHub didn't turn on pages for ${publish.repository}, so nothing was published.`
              : `GitHub didn't turn on pages for ${publish.repository}, so nothing was published. A private repository needs a paid plan for them.`,
        ),
      );
      // The branch just pushed came down again, so nothing beyond what was recorded is left.
      return removed.code === 0 ? failure : { ...failure, effect: 'unknown' };
    }
    const address = PAGES_ADDRESS.test(said) ? (said.endsWith('/') ? said : `${said}/`) : null;
    return { ok: true, publish, commit, address };
  }

  /**
   * Makes `repository`, the one this project's first Send made and a publish made public, private again
   * with the person's own gh (ADR 0039, amended): the way back after a publish that failed once the
   * repository was public. Asked of gh only at the press.
   */
  async makePrivate(repository: string): Promise<{ readonly ok: true } | SendFailure> {
    const gh = this.#deps.gh;
    if (gh === null)
      return refuse(
        'no_gh',
        "Making a repository private needs GitHub's own command line tool on this computer, signed in.",
      );
    if (!/^[A-Za-z0-9][A-Za-z0-9-]{0,38}\/[A-Za-z0-9._-]{1,100}$/.test(repository))
      return refuse(
        'make_private_failed',
        'Halcyonic could not name the repository to make private.',
      );
    if ((await this.#run(gh, ['auth', 'status'], 15_000)).code !== 0)
      return refuse(
        'gh_signed_out',
        "GitHub's command line tool on this computer isn't signed in, so nothing changed.",
      );
    const closed = await this.#run(
      gh,
      [
        'repo',
        'edit',
        repository,
        '--visibility',
        'private',
        '--accept-visibility-change-consequences',
      ],
      60_000,
    );
    if (closed.code !== 0)
      return {
        ...refuse(
          'not_made_private',
          `GitHub didn't make ${repository} private, or didn't say so. Look at it on GitHub.`,
        ),
        effect: 'unknown',
      };
    return { ok: true };
  }

  /**
   * Takes the published game down (ADR 0039, amended): the Pages branch of the repository it was
   * published to, never whatever the folder's origin names now (AUDIT3 U1), deleted with the person's
   * own git only while it still holds the commit Halcyonic published, so no one's commit on it is
   * lost (U2).
   */
  async unpublish(
    projectId: ProjectId,
    folder: string,
    published: { readonly repository: string; readonly commit: string },
  ): Promise<{ readonly ok: true; readonly repository: string } | SendFailure> {
    const origin = await readOrigin(folder);
    if (
      origin === null ||
      HOST_TOOLS[origin.remote.host]?.pages !== true ||
      !sameRepository(origin.remote.path, published.repository)
    )
      return refuse(
        'origin_changed',
        `The game was published to ${published.repository}, and this folder's origin is no longer it, so nothing was taken down. Point the origin back at ${published.repository}, then take it down.`,
      );
    const repository = await this.#repository(projectId, await objectFormat(folder));
    if (repository === null)
      return refuse('publish_failed', 'Halcyonic could not prepare the change.');
    const remote = remoteOf(origin.remote, origin.ssh);
    const removed = await this.#run(
      this.#deps.git,
      [
        '--git-dir',
        repository,
        '-c',
        'core.hooksPath=/dev/null',
        'push',
        '--quiet',
        `--force-with-lease=refs/heads/${PAGES_BRANCH}:${published.commit}`,
        remote,
        `:refs/heads/${PAGES_BRANCH}`,
      ],
      120_000,
    );
    if (removed.code === 0) return { ok: true, repository: published.repository };
    const there = await this.#run(
      this.#deps.git,
      ['--git-dir', repository, 'ls-remote', '--heads', remote, `refs/heads/${PAGES_BRANCH}`],
      60_000,
    );
    const now = there.code === 0 ? (there.stdout.trim().split(/\s/)[0] ?? '') : '';
    if (now !== '' && now !== published.commit)
      return refuse(
        'pages_changed',
        `The ${PAGES_BRANCH} branch has commits Halcyonic didn't publish, so nothing was taken down. Remove the branch on GitHub yourself if you mean to.`,
      );
    return refuse(
      'push_refused',
      "GitHub didn't take the change, so the game is still published. Try again.",
    );
  }

  async #preparePublish(
    projectId: ProjectId,
    folder: string,
    checkId: CheckId,
    built: string | null,
    replaces: string | null,
    options: PublishOptions,
  ): Promise<
    | {
        readonly ok: true;
        readonly publish: PublishReview;
        readonly repository: string;
        readonly tree: string;
        readonly ssh: boolean;
        readonly host: string;
      }
    | SendFailure
  > {
    const read = await readOrigin(folder);
    if (read === null) return refuse('no_origin', NO_ORIGIN);
    // A public page only on a host whose tool keeps one here (github.com, with gh).
    if (HOST_TOOLS[read.remote.host]?.pages !== true || read.remote.path.split('/').length !== 2)
      return refuse(
        'no_pages_host',
        `A public page is set up here only for a repository on a host whose tool keeps one; this project's is on ${read.remote.host}.`,
      );
    const origin = { fullName: read.remote.path, ssh: read.ssh, host: read.remote.host };
    if (this.#deps.gh === null)
      return refuse(
        'no_gh',
        "Publishing needs GitHub's own command line tool on this computer, signed in.",
      );
    if (built === null)
      return refuse(
        'no_built_copy',
        "The checked game's built files aren't on this computer. Check the game again.",
      );
    const repository = await this.#repository(projectId, await objectFormat(folder));
    if (repository === null)
      return refuse('publish_failed', 'Halcyonic could not prepare the publish.');
    // A built copy has no commit to start from and no excludes of the person's.
    const tree = await this.#tree(repository, built, [], null, null);
    if (!tree.ok && tree.code === 'holds_secret' && tree.hit !== undefined)
      return refuse('holds_secret', heldMessage(tree.hit, 'published'));
    if (!tree.ok)
      return tree.code === 'uses_filters'
        ? refuse(
            'uses_filters',
            "The built game holds a .gitattributes that names a filter, which Halcyonic doesn't run, so it isn't published.",
          )
        : refuse('no_built_copy', "The checked game's built files could not be read.");
    if (tree.files === 0)
      return refuse('no_built_copy', "The checked game's built files could not be read.");
    const [owner = '', name = ''] = origin.fullName.split('/');
    const address =
      name === `${owner}.github.io`
        ? `https://${owner}.github.io/`
        : `https://${owner}.github.io/${name}/`;
    // Who can see it as far as the journal says, for a repository a send of Halcyonic's made; else
    // unknown, since a review asks GitHub nothing.
    const visibility =
      options.made !== undefined && sameRepository(options.made.repository, origin.fullName)
        ? options.made.visibility
        : 'unknown';
    // Only a repository this project's own first Send made private is ever made public by a press: never
    // one the person had before, whose code and history would all go public with it (AUDIT3 S1).
    const makePublic = options.makePublic === true && visibility === 'private';
    const facts = {
      repository: origin.fullName,
      check_id: checkId,
      tree: tree.tree,
      links_left_out: tree.outside,
      visibility,
      make_public: makePublic,
      replaces,
      branch: PAGES_BRANCH,
    };
    const publish: PublishReview = {
      project_id: projectId,
      repository: origin.fullName,
      check_id: checkId,
      files: tree.files,
      links_left_out: pathList(tree.outside),
      visibility,
      make_public: makePublic,
      replaces,
      address,
      review: reviewDigest('project.publish', { project_id: projectId }, facts),
    };
    return { ok: true, publish, repository, tree: tree.tree, ssh: origin.ssh, host: origin.host };
  }

  async #prepare(
    projectId: ProjectId,
    folder: string,
    otherFolders: readonly string[],
    title: string,
    options: SendOptions,
  ): Promise<{ readonly ok: true; readonly prepared: Prepared } | SendFailure> {
    const git = await lstat(join(folder, '.git')).catch(() => null);
    if (git === null) return this.#prepareNew(projectId, folder, otherFolders, title, options);
    if (!git.isDirectory())
      return refuse('not_a_repository', "The project's folder isn't a git repository of its own.");
    const origin = await readOrigin(folder);
    if (origin === null) return refuse('no_origin', NO_ORIGIN);
    const branch = await headBranch(folder);
    if (branch === null) return refuse('detached_head', "The project's folder isn't on a branch.");
    const format = await objectFormat(folder);
    const repository = await this.#repository(projectId, format);
    if (repository === null) return refuse('send_failed', 'Halcyonic could not prepare the send.');
    // The person's commits, by a fetch: nothing of theirs runs in our repository, nothing of ours in theirs.
    const fetched = await this.#run(this.#deps.git, [
      '--git-dir',
      repository,
      '-c',
      'core.hooksPath=/dev/null',
      'fetch',
      '--quiet',
      '--no-tags',
      '--force',
      folder,
      `refs/heads/${branch}:refs/halcyonic/base`,
    ]);
    if (fetched.code !== 0)
      return refuse('no_commit', "The project's branch has no commit to send on top of.");
    const base = (
      await this.#run(this.#deps.git, ['--git-dir', repository, 'rev-parse', 'refs/halcyonic/base'])
    ).stdout.trim();
    const built = await this.#tree(repository, folder, otherFolders, 'refs/halcyonic/base');
    if (!built.ok) return built;
    const tree = built.tree;
    const diff = await this.#run(this.#deps.git, [
      '--git-dir',
      repository,
      'diff-tree',
      '-r',
      '-z',
      '--name-status',
      '--no-renames',
      base,
      tree,
    ]);
    if (diff.code !== 0) return refuse('send_failed', "The project's files could not be read.");
    const files = changedFiles(diff.stdout);
    if (files.total === 0)
      return refuse(
        'nothing_to_send',
        built.outside.length > 0
          ? "Only links that point outside the project's folder differ from its branch's last commit, and those are never sent."
          : "Nothing in the project differs from its branch's last commit.",
      );
    const newBranch = `halcyonic/${slug(basename(folder))}-${tree.slice(0, 8)}`;
    // A review request where the host's tool is set up on this Mac, never asked of the host: a review
    // sends nothing anywhere (AUDIT3 S2). Whether the person is signed in shows only at the press.
    const reviewRequest = this.#deps.gh !== null && HOST_TOOLS[origin.remote.host] !== undefined;
    const facts = {
      remote: origin.remote,
      branch,
      base_commit: base,
      tree,
      new_branch: newBranch,
      title,
      files,
      links_left_out: built.outside,
    };
    const send: SendReview = {
      project_id: projectId,
      remote: origin.remote,
      creates: null,
      branch,
      base_commit: base,
      new_branch: newBranch,
      title,
      files,
      links_left_out: pathList(built.outside),
      review_request: reviewRequest,
      review: reviewDigest('project.send', { project_id: projectId }, facts),
    };
    return { ok: true, prepared: { send, ssh: origin.ssh, tree, repository } };
  }

  /**
   * A send of a folder that is no git repository: every file the folder holds as git would add it, and
   * `main`, into the repository an earlier first send made for this project, as the journal says, or
   * into a new one it would make (named for the folder unless the person names it, in the account gh
   * is signed in to, which a review never asks). A folder inside another repository is refused; the
   * person's home folder kept as a repository of its own settings files does not count.
   */
  async #prepareNew(
    projectId: ProjectId,
    folder: string,
    otherFolders: readonly string[],
    title: string,
    options: SendOptions,
  ): Promise<{ readonly ok: true; readonly prepared: Prepared } | SendFailure> {
    if (await insideRepository(folder, this.#deps.env.HOME ?? null))
      return refuse(
        'inside_repository',
        "The project's folder is inside another git repository, so it isn't sent as one of its own. Send that repository with git yourself.",
      );
    if (this.#deps.gh === null)
      return refuse(
        'no_gh',
        "Making a repository on GitHub needs GitHub's own command line tool on this computer, signed in.",
      );
    const name = options.name ?? slug(basename(folder));
    if (!REPOSITORY_NAME.test(name))
      return refuse(
        'invalid_name',
        'A repository name starts with a letter or digit and holds only letters, digits, dots, hyphens and underscores, at most 100.',
      );
    const repository = await this.#repository(projectId, 'sha1');
    if (repository === null) return refuse('send_failed', 'Halcyonic could not prepare the send.');
    const built = await this.#tree(repository, folder, otherFolders, null, null);
    if (!built.ok) return built;
    if (built.files === 0)
      return refuse(
        'nothing_to_send',
        built.outside.length > 0
          ? "The project's folder holds only links that point outside it, and those are never sent."
          : "The project's folder holds nothing to send.",
      );
    const listed = await this.#run(this.#deps.git, [
      '--git-dir',
      repository,
      'ls-tree',
      '-r',
      '-z',
      '--name-only',
      built.tree,
    ]);
    if (listed.code !== 0) return refuse('send_failed', "The project's files could not be read.");
    const files = changedFileList(
      listed.stdout
        .split('\0')
        .filter((path) => path !== '')
        .map((path) => ({ path, change: 'added' as const })),
    );
    const made = options.made;
    const creates =
      made === undefined ? { name, visibility: options.visibility ?? 'private' } : null;
    const facts = {
      creates,
      into: made?.repository ?? null,
      tree: built.tree,
      title,
      files,
      links_left_out: built.outside,
    };
    const send: SendReview = {
      project_id: projectId,
      remote: made === undefined ? null : { host: MAKES_REPOSITORIES, path: made.repository },
      creates,
      branch: 'main',
      base_commit: null,
      new_branch: 'main',
      title,
      files,
      links_left_out: pathList(built.outside),
      review_request: false,
      review: reviewDigest('project.send', { project_id: projectId }, facts),
    };
    return {
      ok: true,
      prepared: { send, ssh: false, tree: built.tree, repository, withheld: built.withheld },
    };
  }

  /**
   * The folder's files as the person's git sees them, built in our repository's own scratch index
   * seeded from `base`, so what git tracks stays tracked, ignore rules or not (AUDIT3 G1), with the
   * folder's own `info/exclude` (G2) and the person's own excludes file, and none of their git
   * settings, so no filter of theirs runs (S1). A path the secret-file list names, or inside another
   * project's folder, stays exactly as `base` has it: never added, changed or removed. A repository
   * whose attributes name a filter, as Git LFS does, is refused: its files are not what it commits.
   * `base` is null for a tree with no parent (Publish's).
   */
  async #tree(
    repository: string,
    folder: string,
    otherFolders: readonly string[],
    base: string | null,
    excludes: string | null = join(folder, '.git', 'info', 'exclude'),
  ): Promise<
    | {
        readonly ok: true;
        readonly tree: string;
        readonly files: number;
        /** Links the folder holds, pointing outside it, that the tree does not carry as the folder has them. */
        readonly outside: readonly string[];
        /** Everything the folder holds, as staged, that the tree does not carry as the folder has it. */
        readonly withheld: readonly string[];
      }
    | SendFailure
  > {
    const unreadable = refuse('send_failed', "The project's files could not be read.");
    const real = await realpath(folder);
    const inside: string[] = [];
    for (const other of otherFolders) {
      const path = await realpath(other).catch(() => other);
      if (path !== real && path.startsWith(real + sep))
        inside.push(relative(real, path).split(sep).join('/'));
    }
    const held = (path: string) =>
      secretShapedPath(path) ||
      path.split('/')[0]?.startsWith(CLONE_ASIDE) === true ||
      inside.some((other) => path === other || path.startsWith(`${other}/`));
    // What tells git which files the person keeps out, all of it read, or nothing is sent (AUDIT3
    // reread): the folder's `info/exclude` as a small regular file, never through a link, since an
    // agent can write it, copied into ours; and the person's own excludes file, the one their global
    // settings name or git's default, by its real path. One there but not read refuses the send.
    const cannotRead = refuse(
      'unreadable_excludes',
      "Halcyonic can't read a rule this project's git uses to keep files out, so it sends nothing. Send it with git yourself.",
    );
    let folderExcludes = '';
    if (excludes !== null && (await lstat(excludes).catch(() => null)) !== null) {
      const text = await readSmall(excludes);
      if (text === null) return cannotRead;
      folderExcludes = text;
    }
    await mkdir(join(repository, 'info'), { recursive: true, mode: 0o700 });
    await writeFile(join(repository, 'info', 'exclude'), folderExcludes, { mode: 0o600 });
    const named = await this.#run(this.#deps.git, [
      'config',
      '--global',
      '--path',
      'core.excludesFile',
    ]);
    const home = this.#deps.env.HOME ?? '';
    const personal =
      named.code === 0 && named.stdout.trim() !== ''
        ? named.stdout.trim()
        : home === ''
          ? null
          : join(home, '.config', 'git', 'ignore');
    let globalExcludes = '';
    if (personal !== null && (await lstat(personal).catch(() => null)) !== null) {
      // The person's own file, often a link from their dotfiles: followed, then read whole.
      const real = await realpath(personal).catch(() => null);
      if (real === null || (await readable(real)) === null) return cannotRead;
      globalExcludes = real;
    }
    const index = join(repository, `send-${process.pid}-${Date.now()}.index`);
    const indexed = { GIT_INDEX_FILE: index };
    // No settings of the person's, global or system, while their files are staged (AUDIT3 S1).
    const bare = { ...indexed, GIT_CONFIG_GLOBAL: '/dev/null', GIT_CONFIG_NOSYSTEM: '1' };
    const entries = async (tree: string): Promise<Map<string, string> | null> => {
      const listed = await this.#run(this.#deps.git, [
        '--git-dir',
        repository,
        'ls-tree',
        '-r',
        '-z',
        '--full-tree',
        tree,
      ]);
      if (listed.code !== 0) return null;
      const map = new Map<string, string>();
      for (const entry of listed.stdout.split('\0')) {
        const tab = entry.indexOf('\t');
        if (tab > 0) map.set(entry.slice(tab + 1), entry);
      }
      return map;
    };
    try {
      const seeded = await this.#run(
        this.#deps.git,
        ['--git-dir', repository, 'read-tree', ...(base === null ? ['--empty'] : [base])],
        30_000,
        bare,
      );
      if (seeded.code !== 0) return unreadable;
      const added = await this.#run(
        this.#deps.git,
        [
          '--git-dir',
          repository,
          '--work-tree',
          real,
          '-c',
          'core.hooksPath=/dev/null',
          ...(globalExcludes === '' ? [] : ['-c', `core.excludesFile=${globalExcludes}`]),
          'add',
          '-A',
          '--',
          '.',
        ],
        120_000,
        bare,
      );
      if (added.code !== 0) return unreadable;
      const whole = await this.#run(
        this.#deps.git,
        ['--git-dir', repository, 'write-tree'],
        30_000,
        bare,
      );
      if (whole.code !== 0) return unreadable;
      const now = await entries(whole.stdout.trim());
      const before = base === null ? new Map<string, string>() : await entries(base);
      if (now === null || before === null) return unreadable;
      if (await this.#filtered(repository, now))
        return refuse(
          'uses_filters',
          "This project's git runs a filter on its files, as Git LFS does, which Halcyonic doesn't run. Send it with git yourself.",
        );
      // A link that points outside the folder is never sent, as a secret-shaped path is not: its target is a
      // place on this computer, never the person's work (a starter's node_modules is a link to the shared
      // dependencies by their absolute path, which holds the computer's user name).
      const outside = await this.#outsideLinks(repository, now);
      if (outside === null) return unreadable;
      const keep = (path: string) => held(path) || outside.has(path);
      const kept: string[] = [];
      for (const [path, entry] of now) if (!keep(path)) kept.push(entry);
      for (const [path, entry] of before) if (keep(path)) kept.push(entry);
      await rm(index, { force: true });
      if (
        (
          await this.#run(
            this.#deps.git,
            ['--git-dir', repository, 'read-tree', '--empty'],
            30_000,
            bare,
          )
        ).code !== 0
      )
        return unreadable;
      if (
        kept.length > 0 &&
        (
          await this.#run(
            this.#deps.git,
            ['--git-dir', repository, 'update-index', '-z', '--index-info'],
            30_000,
            bare,
            `${kept.join('\0')}\0`,
          )
        ).code !== 0
      )
        return unreadable;
      const written = await this.#run(
        this.#deps.git,
        ['--git-dir', repository, 'write-tree'],
        30_000,
        bare,
      );
      const tree = written.stdout.trim();
      if (written.code !== 0 || !/^(?:[0-9a-f]{40}|[0-9a-f]{64})$/.test(tree)) return unreadable;
      // Nothing that holds a secret this Mac keeps leaves it: the whole tree, as it would go, is read.
      let hit: HeldHit | null;
      try {
        hit = await heldInTree(
          (args, input) => this.#bytes(['--git-dir', repository, ...args], input),
          tree,
          this.#deps.held?.() ?? [],
        );
      } catch {
        return unreadable;
      }
      if (hit !== null) return { ...refuse('holds_secret', heldMessage(hit, 'sent')), hit };
      return {
        ok: true,
        tree,
        files: kept.length,
        outside: [...outside].filter((path) => before.get(path) !== now.get(path)),
        withheld: [...now.keys()].filter(
          (path) => keep(path) && before.get(path) !== now.get(path),
        ),
      };
    } finally {
      await rm(index, { force: true });
    }
  }

  /**
   * The staged links whose target leaves the folder: an absolute target, or a relative one that climbs out of
   * it. A link within the folder is the person's own and is sent as git keeps it. Null when one can't be read.
   */
  async #outsideLinks(
    repository: string,
    entries: ReadonlyMap<string, string>,
  ): Promise<ReadonlySet<string> | null> {
    const outside = new Set<string>();
    for (const [path, entry] of entries) {
      const object = /^120000 blob ([0-9a-f]+)\t/.exec(entry)?.[1];
      if (object === undefined) continue;
      const target = await this.#run(this.#deps.git, [
        '--git-dir',
        repository,
        'cat-file',
        'blob',
        object,
      ]);
      if (target.code !== 0) return null;
      if (linkLeavesFolder(path, target.stdout)) outside.add(path);
    }
    return outside;
  }

  /** Whether any `.gitattributes` in the staged files names a filter, as Git LFS's do. */
  async #filtered(repository: string, entries: ReadonlyMap<string, string>): Promise<boolean> {
    for (const [path, entry] of entries) {
      if (path !== '.gitattributes' && !path.endsWith('/.gitattributes')) continue;
      const object = /^\d+ blob ([0-9a-f]+)\t/.exec(entry)?.[1];
      if (object === undefined) continue;
      const text = await this.#run(this.#deps.git, [
        '--git-dir',
        repository,
        'cat-file',
        'blob',
        object,
      ]);
      if (text.code !== 0 || /(^|\s)filter=/m.test(text.stdout)) return true;
    }
    return false;
  }

  /**
   * Git with no shell and none of the person's settings, its output as bytes (a file's content is read
   * whole, never as text), the person's environment as `PASSED` names it.
   */
  #bytes(
    args: readonly string[],
    input?: Buffer,
  ): Promise<{ readonly code: number; readonly stdout: Buffer }> {
    const env: Record<string, string> = {
      PATH: '/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin:/usr/local/bin',
      GIT_TERMINAL_PROMPT: '0',
      GIT_CONFIG_GLOBAL: '/dev/null',
      GIT_CONFIG_NOSYSTEM: '1',
    };
    for (const name of PASSED) {
      const value = this.#deps.env[name];
      if (value !== undefined) env[name] = value;
    }
    return new Promise((resolve) => {
      const child = execFile(
        this.#deps.git,
        [...args],
        {
          env,
          timeout: 120_000,
          maxBuffer: TREE_READ_MAX,
          encoding: 'buffer',
          cwd: this.#deps.dataDir,
        },
        (error, stdout) => {
          const code = error === null ? 0 : typeof error.code === 'number' ? error.code : 1;
          resolve({ code, stdout: Buffer.isBuffer(stdout) ? stdout : Buffer.from(stdout) });
        },
      );
      child.stdin?.end(input);
    });
  }

  /** Runs a program with no shell, the person's environment as `PASSED` names it, and no prompt. */
  #run(
    file: string,
    args: readonly string[],
    timeout = 30_000,
    extra: Readonly<Record<string, string>> = {},
    input?: string,
  ): Promise<{ readonly code: number; readonly stdout: string }> {
    const env: Record<string, string> = {
      PATH: '/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin:/usr/local/bin',
      GIT_TERMINAL_PROMPT: '0',
      GH_PROMPT_DISABLED: '1',
      GH_NO_UPDATE_NOTIFIER: '1',
      ...extra,
    };
    for (const name of PASSED) {
      const value = this.#deps.env[name];
      if (value !== undefined) env[name] = value;
    }
    return new Promise((resolve) => {
      const child = execFile(
        file,
        [...args],
        { env, timeout, maxBuffer: OUTPUT_MAX, cwd: this.#deps.dataDir },
        (error, stdout) => {
          const code = error === null ? 0 : typeof error.code === 'number' ? error.code : 1;
          resolve({ code, stdout: String(stdout) });
        },
      );
      if (input !== undefined) child.stdin?.end(input);
      else child.stdin?.end();
    });
  }
}

/**
 * Each host whose tool is set up on this Mac, and what it does there: open a review request (a pull
 * request), keep a public page, make a repository. Every other host is pushed to with the person's own
 * git, and no more, as the review says (ADR 0039, amended): a new host is a line here and its tool.
 */
const HOST_TOOLS: Readonly<Record<string, { readonly tool: 'gh'; readonly pages: boolean }>> = {
  'github.com': { tool: 'gh', pages: true },
};

/**
 * Whether two repository paths name the same repository: GitHub's owner and name are case-insensitive
 * (gh prints them as the account spells them, a folder's origin may spell them otherwise), found by
 * the first real run (2026-11-12, a login with capitals).
 */
export function sameRepository(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}

/** The host whose tool makes a first send's repository in the person's own account. */
const MAKES_REPOSITORIES = 'github.com';

const NO_ORIGIN =
  "The project's folder has no origin Halcyonic can send to: an https or ssh address of a host and a repository.";

/**
 * The folder's `origin` as host and path, from its `.git/config` read as a file (git is not run in
 * it), in any of git's forms: `https://host/path`, `ssh://user@host/path`, `user@host:path`, with or
 * without `.git`. A user part is dropped, never kept; a port, an odd host or a path that climbs is
 * refused (null).
 */
async function readOrigin(
  folder: string,
): Promise<{ readonly remote: SendRemote; readonly ssh: boolean } | null> {
  const url = await readOriginUrl(folder);
  if (url === null) return null;
  const forms: readonly [RegExp, boolean][] = [
    [/^https:\/\/(?:[^@/]+@)?([^/:]+)\/(.+?)(?:\.git)?\/?$/i, false],
    [/^ssh:\/\/(?:[^@/]+@)?([^/:]+)\/(.+?)(?:\.git)?\/?$/i, true],
    [/^(?:[A-Za-z0-9._-]+@)?([^/:@]+):(?!\/)(.+?)(?:\.git)?\/?$/, true],
  ];
  for (const [form, ssh] of forms) {
    const found = form.exec(url.trim());
    if (found === null) continue;
    const host = (found[1] ?? '').toLowerCase();
    const path = found[2] ?? '';
    if (!HOST_NAME.test(host) || !REMOTE_PATH.test(path)) return null;
    return { remote: { host, path }, ssh };
  }
  return null;
}

const HOST_NAME =
  /^(?=.{1,253}$)[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?)+$/;
const REMOTE_PATH =
  /^(?=.{1,240}$)(?!.*(?:^|\/)\.\.?(?:\/|$))[A-Za-z0-9._-]+(?:\/[A-Za-z0-9._-]+)*$/;

/** The remote's address as the person's git reaches it, built from its host and path, never read out. */
function remoteOf(remote: SendRemote, ssh: boolean): string {
  return ssh
    ? `git@${remote.host}:${remote.path}.git`
    : `https://${remote.host}/${remote.path}.git`;
}

/**
 * The title a send goes with, as its review shows it: the person's own, else the project's latest
 * task's title, else the project's name. One line, control characters and runs of spaces made one
 * space, at most 200 characters, as a typed one is held (Text(200)).
 */
export function sendTitle(
  given: string | undefined,
  project: { readonly name: string } | undefined,
  tasks: readonly { readonly title: string; readonly updated_at: string }[],
): string {
  const latest = [...tasks].sort((a, b) => b.updated_at.localeCompare(a.updated_at))[0]?.title;
  for (const candidate of [given, latest, project?.name]) {
    const flat = [...(candidate ?? '')]
      .map((character) => {
        const code = character.codePointAt(0) ?? 0;
        return code < 0x20 || code === 0x7f || /\s/u.test(character) ? ' ' : character;
      })
      .join('')
      .replace(/ +/g, ' ')
      .trim();
    const clean = [...flat].slice(0, 200).join('').trim();
    if (clean !== '') return clean;
  }
  return 'Changes';
}

/** The start of the name of a clone's git folder while it is built beside, in the project's folder. */
const CLONE_ASIDE = '.git-halcyonic-';

/** A repository's name as GitHub takes one: letters, digits, dots, hyphens, underscores; never . or .. */
const REPOSITORY_NAME = /^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$/;

/** A repository's address as `gh repo create` prints it: owner and name. */
const REPOSITORY_ADDRESS =
  /https:\/\/github\.com\/([A-Za-z0-9][A-Za-z0-9-]{0,38})\/([A-Za-z0-9._-]{1,100})(?![A-Za-z0-9._/-])/;

/**
 * Whether a folder above this one, up to the file system's root, holds a `.git`: then the folder is
 * part of that repository. The person's home folder is passed over, since a home kept as a
 * repository of its settings files is no project.
 */
async function insideRepository(folder: string, home: string | null): Promise<boolean> {
  const real = await realpath(folder);
  const homeReal = home === null ? null : await realpath(home).catch(() => null);
  let parent = dirname(real);
  for (;;) {
    if (parent !== homeReal && (await lstat(join(parent, '.git')).catch(() => null)) !== null)
      return true;
    const up = dirname(parent);
    if (up === parent) return false;
    parent = up;
  }
}

const PAGES_ADDRESS = /^https:\/\/[A-Za-z0-9-]{1,39}\.github\.io\/(?:[A-Za-z0-9._-]{1,100}\/?)?$/;

function refuse(code: string, message: string): SendFailure {
  return { ok: false, code, message };
}

/** The branch HEAD names, from `.git/HEAD` read as a file (git is not run in the folder), or null. */
async function headBranch(folder: string): Promise<string | null> {
  const text = await readSmall(join(folder, '.git', 'HEAD'));
  const ref = /^ref: refs\/heads\/([^\s]+)\s*$/.exec(text ?? '');
  const name = ref?.[1];
  return name !== undefined && /^[^\s~^:?*[\\]+$/.test(name) && !name.includes('..') ? name : null;
}

/** The repository's object format, `sha1` or `sha256`, from its `config` read as a file. */
async function objectFormat(folder: string): Promise<'sha1' | 'sha256'> {
  const text = (await readSmall(join(folder, '.git', 'config'))) ?? '';
  return /^\s*objectformat\s*=\s*sha256\s*$/im.test(text) ? 'sha256' : 'sha1';
}

/** A regular file's text, at most 1 MiB, followed to its real path, or null. */
async function readable(path: string): Promise<string | null> {
  const named = await lstat(path).catch(() => null);
  if (named === null || !named.isFile() || named.size > 1024 * 1024) return null;
  const file = await open(path, 'r').catch(() => null);
  if (file === null) return null;
  try {
    return (await file.readFile()).toString('utf8');
  } catch {
    return null;
  } finally {
    await file.close();
  }
}

/** A small regular file's text, never through a link, or null. */
async function readSmall(path: string): Promise<string | null> {
  const named = await lstat(path).catch(() => null);
  if (named === null || !named.isFile() || named.size > 64 * 1024) return null;
  const file = await open(path, 'r').catch(() => null);
  if (file === null) return null;
  try {
    return (await file.readFile()).toString('utf8');
  } finally {
    await file.close();
  }
}

/** `git diff-tree -z --name-status` as files with how each changed. */
function changedFiles(output: string): ChangedFiles {
  const parts = output.split('\0').filter((part) => part !== '');
  const files: { path: string; change: 'added' | 'changed' | 'removed' }[] = [];
  for (let i = 0; i + 1 < parts.length; i += 2) {
    const status = parts[i] as string;
    files.push({
      path: parts[i + 1] as string,
      change: status === 'A' ? 'added' : status === 'D' ? 'removed' : 'changed',
    });
  }
  return changedFileList(files);
}

/** A folder's name as a branch's part: lower case letters, digits and hyphens, at most 40. */
function slug(name: string): string {
  const cut = name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 40)
    .replace(/-+$/, '');
  return cut === '' ? 'project' : cut;
}
