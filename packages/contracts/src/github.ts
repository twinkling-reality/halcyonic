import Type, { type Static } from 'typebox';
import { RepositoryFullName } from './catalog.ts';
import { GitObjectId, PathList, ReviewDigest } from './checkpoints.ts';
import { ChangedFiles } from './copies.ts';
import { CheckId, ShareCode } from './previews.ts';
import { ErrorInfo, Nullable, ProjectId, Text, Timestamp } from './primitives.ts';

const strict = { additionalProperties: false } as const;

/**
 * A branch name as git takes one: no whitespace, none of git's special characters, at most 255
 * characters (ADR 0039, amended: Send to GitHub).
 */
export const GitBranchName = Type.String({
  minLength: 1,
  maxLength: 255,
  pattern: '^(?!/)(?!.*//)(?!.*\\.\\.)(?!.*\\.lock$)(?!.*/$)[^\\s~^:?*\\[\\\\\\x00-\\x1f\\x7f]+$',
});
export type GitBranchName = Static<typeof GitBranchName>;

/** A pull request's own address on github.com, as the person's `gh` printed it. */
export const PullRequestUrl = Type.String({
  maxLength: 300,
  pattern:
    '^https://github\\.com/[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9._-]{1,100}/pull/[0-9]{1,10}$',
});
export type PullRequestUrl = Static<typeof PullRequestUrl>;

/** A repository's name as GitHub takes one: letters, digits, dots, hyphens, underscores; never . or .. */
export const RepositoryName = Type.String({ pattern: '^(?!\\.\\.?$)[A-Za-z0-9._-]{1,100}$' });
export type RepositoryName = Static<typeof RepositoryName>;

/** Who can see a repository on GitHub. */
export const RepositoryVisibility = Type.Union([Type.Literal('private'), Type.Literal('public')]);
export type RepositoryVisibility = Static<typeof RepositoryVisibility>;

/**
 * What Send to GitHub would do, for the person to read before the press (ADR 0039, amended): the
 * repository by owner and name only (never its address), the branch the project's folder is on and
 * its last commit, the new branch it would push, the title exactly as it goes, the files that differ
 * from that commit, the links in the folder that are not sent because they point outside it, and
 * whether a pull request can be opened on this Mac. For a folder that is no repository yet,
 * `creates` names the repository the send makes in the person's own account and who can see it;
 * its owner is the account `gh` is signed in to, which a review never asks (`repository` null),
 * there is no commit to build on (`base_commit` null) and `main` is pushed. `review` is the
 * digest the command carries back, over every value the press sends (ADR 0051).
 */
export const GitHubSendReview = Type.Object(
  {
    project_id: ProjectId,
    repository: Nullable(RepositoryFullName),
    creates: Nullable(
      Type.Object(
        {
          name: RepositoryName,
          visibility: RepositoryVisibility,
        },
        strict,
      ),
    ),
    branch: GitBranchName,
    base_commit: Nullable(GitObjectId),
    new_branch: GitBranchName,
    /** The commit's message and the pull request's title, as sent: the person's, or the host's own. */
    title: Text(200),
    files: ChangedFiles,
    /** Links the folder holds that point outside it: never sent (a starter's `node_modules` is one). */
    links_left_out: PathList,
    pull_request: Type.Boolean(),
    review: ReviewDigest,
  },
  strict,
);
export type GitHubSendReview = Static<typeof GitHubSendReview>;

/**
 * The body of `POST /api/projects/:project_id/github-review`: the values the press will send, so the
 * review shows the title exactly as it goes and covers both (an empty body takes the host's own).
 */
export const GitHubSendReviewRequest = Type.Object(
  {
    title: Type.Optional(Text(200)),
    visibility: Type.Optional(RepositoryVisibility),
    repository_name: Type.Optional(RepositoryName),
  },
  strict,
);
export type GitHubSendReviewRequest = Static<typeof GitHubSendReviewRequest>;

/** `POST /api/projects/:project_id/github-review`: the review, or why there is nothing to send. */
export const GitHubSendReviewResponse = Type.Union([
  Type.Object({ availability: Type.Literal('available'), send: GitHubSendReview }, strict),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
]);
export type GitHubSendReviewResponse = Static<typeof GitHubSendReviewResponse>;

/**
 * A published game's lasting address on the person's own GitHub Pages (ADR 0039, amended: Publish):
 * `https://<owner>.github.io/<repository>/`, or `https://<owner>.github.io/` for the repository named
 * so, as GitHub gave it.
 */
export const PagesAddress = Type.String({
  maxLength: 200,
  pattern: '^https://[A-Za-z0-9-]{1,39}\\.github\\.io/(?:[A-Za-z0-9._-]{1,100}/)?$',
});
export type PagesAddress = Static<typeof PagesAddress>;

/** The one branch Publish writes on the person's repository. */
export const PAGES_BRANCH = 'halcyonic-pages';

/**
 * What Publish would do, for the person to read before the press: the repository by owner and name,
 * the check whose built copy goes (never the source), how many files it holds, the links in it that
 * are not published because they point outside it, the commit of the last publish it builds on (null
 * for a first), the address it is expected at, who can see the repository as far as this Mac knows
 * (`private` for one Halcyonic made private and has not made public, else `unknown`: a review asks
 * GitHub nothing), whether the press first makes it public, and the digest the command carries back
 * (ADR 0051). GitHub Free serves pages only from a public repository.
 */
export const GitHubPublishReview = Type.Object(
  {
    project_id: ProjectId,
    repository: RepositoryFullName,
    check_id: CheckId,
    files: Type.Integer({ minimum: 1 }),
    links_left_out: PathList,
    visibility: Type.Union([
      Type.Literal('private'),
      Type.Literal('public'),
      Type.Literal('unknown'),
    ]),
    make_public: Type.Boolean(),
    replaces: Nullable(GitObjectId),
    address: PagesAddress,
    review: ReviewDigest,
  },
  strict,
);
export type GitHubPublishReview = Static<typeof GitHubPublishReview>;

/** The body of `POST /api/projects/:project_id/publish-review`: whether the press makes it public first. */
export const GitHubPublishReviewRequest = Type.Object(
  { make_public: Type.Optional(Type.Boolean()) },
  strict,
);
export type GitHubPublishReviewRequest = Static<typeof GitHubPublishReviewRequest>;

/** `POST /api/projects/:project_id/publish-review`: the review, or why nothing can be published. */
export const GitHubPublishReviewResponse = Type.Union([
  Type.Object({ availability: Type.Literal('available'), publish: GitHubPublishReview }, strict),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
]);
export type GitHubPublishReviewResponse = Static<typeof GitHubPublishReviewResponse>;

/**
 * A project's game as last published: the check it came from, the commit on the Pages branch, and
 * its address, null where GitHub did not say it.
 */
export const ProjectPublishedView = Type.Object(
  {
    /** The repository it was published to, owner/name: Take Down acts on this one only (AUDIT3 U1). */
    repository: RepositoryFullName,
    check_id: CheckId,
    commit: GitObjectId,
    address: Nullable(PagesAddress),
    published_at: Timestamp,
  },
  strict,
);
export type ProjectPublishedView = Static<typeof ProjectPublishedView>;

/** `GET /api/projects/:project_id/published/code`: the published address as a code to scan. */
export const PublishedCodeResponse = Type.Object(
  { check_id: CheckId, address: PagesAddress, code: ShareCode },
  strict,
);
export type PublishedCodeResponse = Static<typeof PublishedCodeResponse>;
