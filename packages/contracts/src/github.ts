import Type, { type Static } from 'typebox';
import { RepositoryFullName } from './catalog.ts';
import { GitObjectId, ReviewDigest } from './checkpoints.ts';
import { ChangedFiles } from './copies.ts';
import { ErrorInfo, ProjectId } from './primitives.ts';

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

/**
 * What Send to GitHub would do, for the person to read before the press (ADR 0039, amended): the
 * repository by owner and name only (never its address), the branch the project's folder is on and
 * its last commit, the new branch it would push, the files that differ from that commit, and whether
 * a pull request can be opened on this Mac. `review` is the digest the command carries back (ADR
 * 0051).
 */
export const GitHubSendReview = Type.Object(
  {
    project_id: ProjectId,
    repository: RepositoryFullName,
    branch: GitBranchName,
    base_commit: GitObjectId,
    new_branch: GitBranchName,
    files: ChangedFiles,
    pull_request: Type.Boolean(),
    review: ReviewDigest,
  },
  strict,
);
export type GitHubSendReview = Static<typeof GitHubSendReview>;

/** `POST /api/projects/:project_id/github-review`: the review, or why there is nothing to send. */
export const GitHubSendReviewResponse = Type.Union([
  Type.Object({ availability: Type.Literal('available'), send: GitHubSendReview }, strict),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
]);
export type GitHubSendReviewResponse = Static<typeof GitHubSendReviewResponse>;
