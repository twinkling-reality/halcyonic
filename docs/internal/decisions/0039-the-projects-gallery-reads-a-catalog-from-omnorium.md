# ADR 0039: The projects gallery reads the person's catalog from Omnorium, and Halcyonic never signs in to GitHub

- Status: Accepted; contract, link, gallery core, route and client built; live read not wired. Amended
  2026-11-11: Send to GitHub and Publish (below), built and proven against stand-ins only
- Date: 2026-10-29
- Complements ADR 0010 and ADR 0020

## Context

The owner, 2026-10-29: Halcyonic should be agentic development made enjoyable and easy for anyone with a
headset, children and people with no technical experience included, with everything organised. A person
with many projects across several GitHub accounts and organisations should see all of them in one place,
the ones being worked on and the ones that need them first; a newcomer with no GitHub starts from what
they make.

Halcyonic knows only the Projects created in it, each bound to one folder the host approves
([ADR 0020](0020-a-project-works-in-one-host-approved-folder.md)). It holds no GitHub credential and
makes no request to GitHub.

Omnorium is the owner's separate GitHub portfolio app. From its source, read on 2026-10-29 without
running it: an instance registers its own GitHub App asking for read-only Metadata, Pull requests, Checks
and Commit statuses, never Contents, installable on every account and organisation; it syncs every
installation's repositories; a project holds several repositories with one primary, and projects sit in
named groups; one pure function derives each project's state (for example awaiting input, checks failing,
review requested, agent running, inactive), listing the signals it could not read rather than reading
them as zero; a fictional sample dataset is served from its own database and labelled. Its Node server
listens on 127.0.0.1 with a Host header guard. It has no API for a program other than its own web app.

## Decision

- **The gallery is the home.** It shows every project the person has, grouped as products, those that need
  the person first and those being worked on with their character. This states intent; the headset's
  composition is decided separately ([ADR 0026](0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md)
  and its amendments).
- **Halcyonic's own Projects are its floor.** The gallery always opens and always holds them, from the
  journal's projection. Nothing about it requires GitHub or Omnorium, so a newcomer's gallery is
  what they made in Halcyonic.
- **Omnorium widens it**, through a versioned, read-only consumer contract that Omnorium
  publishes for programs on the same computer: a credential the person creates for Halcyonic and can
  revoke, scoped to one Omnorium user; loopback only; a discovery file in the person's home whose
  instance id must match the served one before the credential is sent, as with Salidium's consumer
  contract; GET only. Halcyonic proposed the contract to Omnorium; Omnorium owns it.
- **The catalog is read through and never journaled**, as [ADR 0010](0010-external-intelligence-is-read-through.md)
  decides for Salidium and Seorak. Everything in it is `reported` by Omnorium, never `observed` by
  the Mac, keeps Omnorium's own wording of unknown, and never feeds a Workstream's status or
  attention. Every answer says whether Omnorium served its sample data, and a sample is labelled
  wherever it appears and never mixed with real projects.
- **The person links a catalog project to a Halcyonic Project**, and that link is journaled, because it is
  the person's own fact. The link names GitHub's numeric repository id, its stable identity, beside the
  name Omnorium reported at that moment. The host may suggest a folder for a catalog project by a
  named rule; a suggestion is `inferred` and is never applied without the person.
- **A character stands only for a Halcyonic Workstream.** Agent sessions Omnorium reports are shown as
  its report and never animate a character, so one run is not drawn twice.
- **The headset reads the catalog only from the control plane**, never from Omnorium, and treats
  every name, description and title in it as untrusted text.
- **One way.** Halcyonic reads Omnorium and never writes to it.
- When Omnorium is absent, stopped, refuses the credential or speaks an incompatible version, the
  gallery shows Halcyonic's own projects and one line saying which.

## Amendment, 2026-11-11: Send to GitHub

The owner's bar ("to its fullest potential like bolt/cursor/lovable etc but for VR") asks for what a
person gets from those: their work on their own GitHub, as a branch and a pull request. The lead
decided it (lead 11-11b, from TASKS's ASK). This amends only what is said above about `gh`: that
alternative was turned down for a read-only gallery, whose need is far below the person's write
access. Here the person uses that access on purpose, each time, after reading what will go.

- **A read, then the person's command.** `POST /api/projects/:id/github-review` says what would go: the
  repository by owner and name only (never its address, which can carry a user part), the branch the
  folder is on and its last commit, the new branch, the files with how each changed, the links left
  out (a link whose target leaves the folder never leaves the Mac: a starter project's `node_modules`
  is a link to the shared dependencies by an absolute path), and whether a pull request can be opened. It answers a review digest (ADR 0051). `project.send_to_github
  {project_id, title, review}` is review required and checked against the digest again just before
  anything leaves: a folder changed since sends nothing (`review_stale`).
- **The person's own git and gh, never Halcyonic's sign-in.** Halcyonic holds no GitHub credential,
  passes no token (only `HOME`, `USER`, `LOGNAME`, `LANG`, `LC_ALL`, `TMPDIR` and `SSH_AUTH_SOCK`
  reach them, never `GH_TOKEN` or `GITHUB_TOKEN`), reads none, and keeps from what they print only an
  exit code, the commit and the pull request's address. Nothing they print is journaled or logged.
  `gh` is the one `HALCYONIC_GH_BIN` names, never looked up on PATH; without it a send pushes only. A
  review runs no gh, so reading one reaches nothing; whether the person is signed in shows at the press.
- **Nothing of the project's repository runs.** Its `.git/config` and hooks are an agent's to write.
  So the person's repository is only read: `.git/HEAD` and `origin` as files, and its commits by a
  fetch. The commit is built in a bare repository of Halcyonic's own, `<data dir>/sending/<project
  id>.git`, from the folder's files as the person's git sees them, and pushed from there with no
  hooks. The person's folder, index, branches and refs are not changed. "As their git sees them"
  (AUDIT3, 2026-11-11):
  - the scratch index starts from the branch's last commit, so a file git tracks stays tracked
    whatever the ignore rules say, and only what the person changed differs;
  - the folder's `.git/info/exclude` is read as a small regular file, never through a link, and the
    person's own excludes file (the one their global settings name, or git's default
    `~/.config/git/ignore`) by its real path. When Halcyonic cannot read what tells it which files the
    person keeps out (a linked or oversized `info/exclude`, an excludes file that is not a readable
    file), it sends nothing and says so (`unreadable_excludes`);
  - a path the secret-file list names, or inside another project's folder, stays exactly as that
    commit has it, never added, changed or removed;
  - none of the person's git settings, global or system, apply while files are staged, so no filter
    of theirs or the repository's runs. A repository whose `.gitattributes` names a filter, as Git
    LFS does, is refused (`uses_filters`), since its files are not what it commits.
- **One new branch, never a force.** The commit is on top of the folder's branch's last commit, pushed
  as `halcyonic/<folder>-<tree>` (the same work sent again is refused as `already_sent`), and a pull request from it into that branch is opened with the
  person's `gh`, titled as they typed. `project.sent_to_github` records the repository, the branches,
  the commits and the pull request's address.
- **Only the person's.** Never from an agent, the automatic check or auto-fix: it is a command a
  client sends after its review. Refusals in the product's words: no git repository of its own, no
  origin on GitHub, no branch, no commit, nothing changed, a stale review, no name and email in git,
  filters it would need, an exclude rule it cannot read, work already sent, a push GitHub refused.
- **What has met github.com: nothing.** Every test uses a local bare repository as `origin` and a
  stand-in `gh` ([record](../validation/sending-to-github.md)). The first real send is the owner's own.

## Amendment, 2026-11-11: Publish to GitHub Pages

A lasting address for a checked game, which stays up when the Mac is off (Share a Link, ADR 0054, lives
only while the Mac runs). It is built on Send to GitHub's pieces, and the lead decided it with Send.

- **Only a checked build, never the source.** `project.publish {project_id, review}`, review required,
  is admitted only for the project's latest check that passed, is current and found every agent run
  confined (as a share). What goes is that check's built copy, the preview's own files
  (`<data dir>/checks/<project>/<check>/dist`), without secret-shaped files. The starter builds with
  relative paths (`base: './'`), so it works under the repository's subpath.
- **A read first.** `POST /api/projects/:id/publish-review` gives the repository (owner/name), the check,
  how many files go, the links left out as for a send, the commit of the last publish it builds on,
  the expected address, and a review
  digest checked again just before the push.
- **One branch of the person's repository, never forced.** The files are one commit on
  `halcyonic-pages`, on top of the last publish (recorded in the journal), pushed from Halcyonic's own
  bare repository with the person's git and no hooks. A branch of that name Halcyonic did not write
  refuses the push. Then the person's `gh` turns Pages on for that branch (`POST
  repos/<owner>/<name>/pages`), and its address is read back from GitHub (`html_url`); where GitHub
  does not say it, the address is recorded as unknown, never guessed.
- **Taking it down.** `project.unpublish {project_id, commit}`, review required. `commit` is the
  published commit the person was shown (`published.commit`), sent by every client as its review, and a
  publish since makes it `review_stale`. The branch is deleted with the person's git (AUDIT3, 2026-11-11):
  - only in the repository the game was published to (`published.repository`), never whatever the
    folder's origin names now; an origin that names another is refused (`origin_changed`);
  - only while the branch still holds that commit (`--force-with-lease`). A branch someone else moved
    since is refused (`pages_changed`), and their commits are kept.
- **Recorded:** `project.published` (repository, check, branch, commit, address) and
  `project.unpublished`. The project's view gives `published` (the repository, the check, the commit,
  the address, and when). The headset gets the address only as a code to scan, `GET
  /api/projects/:id/published/code`, never drawn as text.
- **Needs** `HALCYONIC_GH_BIN` signed in (`no_gh` at the review, `gh_signed_out` at the press; a review
  runs no gh, so reading one reaches nothing). The built files are staged as Send's are, with none of the
  person's git settings, and a `.gitattributes` among them that names a filter is refused (`uses_filters`). A private repository
  on a plan without Pages fails at GitHub's end, and nothing is recorded as published.

## Alternatives considered

- **Halcyonic signs in to GitHub itself**, with an OAuth device flow or a GitHub App of its own. It
  duplicates what Omnorium already does (app registration, sealed and rotating tokens, sync across
  installations, product grouping, derived states), puts a GitHub credential in the control plane, which
  today holds only credentials for products on the same Mac, and makes the control plane call the
  internet.
- **Halcyonic runs the `gh` command line tool** with the person's existing sign-in. That sign-in usually
  carries the full `repo` scope, read and write access to code, far beyond a gallery's need, and gives no
  grouping or state.
- **Halcyonic reads Omnorium's database.** Integrations never read another product's database
  ([INTEGRATIONS.md](../architecture/INTEGRATIONS.md)); there would be no consent, revocation or version.
- **Journal the catalog.** It would keep a second, stale copy of another product's facts and replay them
  as if they were Halcyonic's.

## Consequences

- The full catalog exists only while Omnorium runs on the same Mac and the person has given
  Halcyonic a credential. Nothing of it is kept on disk by Halcyonic beyond the links the person made.
- Halcyonic gains a contract, a client package and a control plane read for the catalog, specified
  before Omnorium publishes; until it does, they are tested against a stand-in only, and the
  live check is recorded in `docs/internal/validation/` when it runs.
- Suggesting a folder from a repository's git remote would be a new read of one file inside each listed
  folder; the listing reads no file's contents today ([SECURITY.md](../architecture/SECURITY.md)). It
  needs its own security review before it is built.
- The competition video and the judge build show a labelled, invented catalog stand-in, never a real
  person's project names unless the owner picks them, and never the names GitHub or Omnorium.
- Getting a project that is not on the Mac onto it (a clone) is not part of this decision: it uses the
  network and the person's own git credentials and needs a decision of its own.
- Revisit if Omnorium's real collection moves to a hosted instance: reading it would make the
  control plane call the internet with a per-person credential, which this decision does not cover.
