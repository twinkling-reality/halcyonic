# Send to GitHub: what was proven, on stand-ins only (2026-11-11)

For [ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md), amended. Run on
this Mac with `/usr/bin/git` (Xcode's `git version 2.50.1 (Apple Git-155)`), in scratch folders only.

## Nothing here has met github.com

No test, probe or lane pushed to GitHub, opened a pull request, or used anyone's GitHub sign-in. In every
test:
- `origin` is `https://github.com/owner/garden.git`, which the test's own git settings (a scratch `HOME`
  with an `insteadOf` rule) send to a local bare repository;
- `gh` is a stand-in script that records each call and whether a token reached it, and answers with a
  pull request's address.

The first real send to GitHub is the owner's own, with their press.

## Proven on the stand-ins (`sending/github.test.ts`, `http/github-route.test.ts`)

1. **The review** names the repository as `owner/garden` (from `.git/config` read as a file, git not run),
   the branch from `.git/HEAD` read as a file, the branch's last commit, a new branch
   `halcyonic/garden-<8 of the tree>`, and the files with how each changed.
   - A `.env` and a folder bound to another project are not among the files.
   - An ignored folder (`node_modules`) is not either.
   - The same folder gives the same digest; a change gives another.
2. **A send** pushes exactly one commit, on top of the branch's last commit, as the new branch, with the
   title typed and the author from git's own settings.
   - The secret-shaped file is not in it, and `main` on the stand-in GitHub is untouched.
   - The person's folder has the same HEAD, the same `git status` and the same refs after it.
   - The pull request is asked of `gh` as `pr create --repo owner/garden --head <new branch> --base main
     --title <title>`.
3. **A stale review sends nothing**: the folder changed after the review, so no branch appears.
4. **Nothing written in the project's repository runs.** The test plants hooks (`pre-push`,
   `post-checkout`, `pre-commit`, `reference-transaction`), `core.fsmonitor`, a `credential.helper` and
   `core.sshCommand` in it, each touching a marker. After a review and a send there is no marker.
   - The test fails when the push takes the folder's hooks (checked by changing the code, then restored).
5. **No token crosses**: with `GH_TOKEN` and `GITHUB_TOKEN` in Halcyonic's own environment, the stand-in
   `gh` sees neither.
6. **Without gh**, the review says no pull request can be opened, and the send pushes only.
7. **Refusals:** a folder that is no repository, an origin that is not on GitHub, a detached HEAD, nothing
   to send, and a push the stand-in refuses (its repository removed). Each is in words, and none asks gh
   for a pull request.
8. **Through the control plane:**
   - the review route answers `nothing_to_send`, then `available`;
   - the command completes `sent_to_github`, and `project.sent_to_github` records the repository, the
     branches and the commits;
   - the journal holds no address and nothing of the person's git identity;
   - a computer without the sender answers `unavailable`.

9. **The person's own repository as their git sees it** (AUDIT3's probes G1 to G4 and S4, kept as tests):
   - a file tracked but ignored (`vendor/lib.js` under `vendor/` in `.gitignore`) and a tracked
     `.env.production` stay in the sent commit when the person changed only `app.txt`; the review lists only
     `app.txt`; a change to `.env.production` on this Mac is not sent, the commit keeps the base's;
   - a file the folder's `.git/info/exclude` names, and one the person's global `core.excludesFile` names,
     are neither listed nor sent;
   - a `.gitattributes` naming a filter (the repository's own `filter.evil`, the person's global
     `filter.mine`, or Git LFS's) is refused `uses_filters`, and neither filter ran; `uploadpack.packObjectsHook`
     in the repository never ran; attributes naming no filter send as files;
   - a review runs no gh;
   - the same work sent twice is refused `already_sent`, the first branch kept;
   - an origin in its `git@github.com:` or `ssh://git@github.com/` form reaches the same stand-in, and any other
     github.com address leads to a path that is not there.
   Each of G1, G2, S1 and S2/S3's tests fails on c3d3fe5d's sender and passes on its fix.

## Not proven

- A real push to github.com over https with the macOS keychain's credential helper, or over ssh with an
  agent's key.
- A real `gh pr create` or its output on success.
- A repository whose objects are SHA-256 (read from its config): the bare repository is made to match,
  but no test uses one.
- Git LFS: refused, never sent. A repository-level `core.excludesFile` in the person's `.git/config` is not read
  (that file is an agent's to write); only `.git/info/exclude`, `.gitignore` files and the global excludes file are.
