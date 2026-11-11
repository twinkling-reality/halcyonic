import { execFile } from 'node:child_process';
import { lstat, mkdir, open, realpath, rm, writeFile } from 'node:fs/promises';
import { basename, dirname, join, relative, sep } from 'node:path';
import type { ChangedFiles, GitHubSendReview, ProjectId } from '@halcyonic/contracts';
import { secretShapedPath } from '@halcyonic/runtime-core';
import { readGitHubOrigin } from '../catalog-match.ts';
import { changedFileList } from '../checkpoints/checkpointer.ts';
import { reviewDigest } from '../core/review.ts';

/**
 * Send to GitHub (ADR 0039, amended): the project's work as one commit on top of the branch its
 * folder is on, pushed as a new branch of its origin on github.com with the person's own git, and a
 * pull request opened with the person's own `gh`.
 *
 * The person's repository is only read: its HEAD's branch, its origin and, by a fetch, its commits.
 * Nothing runs inside it with its own settings or hooks, which an agent working there could have
 * written. The commit is built in a bare repository of Halcyonic's own in its data folder, from the
 * folder's files as git sees them, without any file whose name says it may hold secrets and without
 * other projects' folders inside this one; it is pushed from there with no hooks. Git and gh run in
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
}

export type SendFailure = { readonly ok: false; readonly code: string; readonly message: string };

interface Prepared {
  readonly send: GitHubSendReview;
  readonly ssh: boolean;
  readonly tree: string;
  readonly repository: string;
}

/** The person's environment as git and gh need it, and nothing that could carry one of our tokens. */
const PASSED = ['HOME', 'USER', 'LOGNAME', 'LANG', 'LC_ALL', 'TMPDIR', 'SSH_AUTH_SOCK'] as const;

/** The most of a program's output read, which is never logged or kept but for its last line. */
const OUTPUT_MAX = 1024 * 1024;

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
  ): Promise<{ readonly ok: true; readonly send: GitHubSendReview } | SendFailure> {
    const prepared = await this.#prepare(projectId, folder, otherFolders);
    return prepared.ok ? { ok: true, send: prepared.prepared.send } : prepared;
  }

  /**
   * Sends what `review` described, checked again first: a folder changed since sends nothing. The
   * push never forces and never names a branch that is there.
   */
  async send(
    projectId: ProjectId,
    folder: string,
    otherFolders: readonly string[],
    title: string,
    review: string,
  ): Promise<
    | {
        readonly ok: true;
        readonly send: GitHubSendReview;
        readonly commit: string;
        readonly pullRequestUrl: string | null;
      }
    | SendFailure
  > {
    const prepared = await this.#prepare(projectId, folder, otherFolders);
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
    const remote = ssh
      ? `git@github.com:${send.repository}.git`
      : `https://github.com/${send.repository}.git`;
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
        "GitHub didn't take the push, so nothing was sent. Check that this computer can push to the repository, then send again.",
      );
    }
    let pullRequestUrl: string | null = null;
    if (this.#deps.gh !== null) {
      const opened = await this.#run(
        this.#deps.gh,
        [
          'pr',
          'create',
          '--repo',
          send.repository,
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
      if (opened.code === 0) pullRequestUrl = PULL_REQUEST.exec(opened.stdout)?.[0] ?? null;
    }
    return { ok: true, send, commit, pullRequestUrl };
  }

  async #prepare(
    projectId: ProjectId,
    folder: string,
    otherFolders: readonly string[],
  ): Promise<{ readonly ok: true; readonly prepared: Prepared } | SendFailure> {
    const git = await lstat(join(folder, '.git')).catch(() => null);
    if (git === null || !git.isDirectory())
      return refuse('not_a_repository', "The project's folder isn't a git repository of its own.");
    const origin = await readGitHubOrigin(folder);
    if (origin === null)
      return refuse('no_github_origin', "The project's folder has no origin on GitHub.");
    const branch = await headBranch(folder);
    if (branch === null) return refuse('detached_head', "The project's folder isn't on a branch.");
    const format = await objectFormat(folder);
    const repository = join(this.#deps.dataDir, 'sending', `${projectId}.git`);
    if ((await lstat(repository).catch(() => null)) === null) {
      await mkdir(dirname(repository), { recursive: true, mode: 0o700 });
      const made = await this.#run(this.#deps.git, [
        'init',
        '--bare',
        '--quiet',
        `--object-format=${format}`,
        repository,
      ]);
      if (made.code !== 0) return refuse('send_failed', 'Halcyonic could not prepare the send.');
    }
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
        "Nothing in the project differs from its branch's last commit.",
      );
    const newBranch = `halcyonic/${slug(basename(folder))}-${tree.slice(0, 8)}`;
    // Whether gh is set up on this Mac, never asked of GitHub: a review sends nothing anywhere (AUDIT3
    // S2). Whether the person is signed in shows only at the press.
    const pullRequest = this.#deps.gh !== null;
    const facts = {
      repository: origin.fullName,
      branch,
      base_commit: base,
      tree,
      new_branch: newBranch,
      files,
    };
    const send: GitHubSendReview = {
      project_id: projectId,
      repository: origin.fullName,
      branch,
      base_commit: base,
      new_branch: newBranch,
      files,
      pull_request: pullRequest,
      review: reviewDigest('project.send_to_github', { project_id: projectId }, facts),
    };
    return { ok: true, prepared: { send, ssh: origin.ssh, tree, repository } };
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
  ): Promise<{ readonly ok: true; readonly tree: string } | SendFailure> {
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
      const kept: string[] = [];
      for (const [path, entry] of now) if (!held(path)) kept.push(entry);
      for (const [path, entry] of before) if (held(path)) kept.push(entry);
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
      return written.code === 0 && /^(?:[0-9a-f]{40}|[0-9a-f]{64})$/.test(tree)
        ? { ok: true, tree }
        : unreadable;
    } finally {
      await rm(index, { force: true });
    }
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
