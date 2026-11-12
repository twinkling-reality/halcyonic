# Send to GitHub and Publish: what was proven, on stand-ins only (2026-11-11)

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
   - a `.git/info/exclude` that is a link, or over 64 KiB, and an excludes file that is not a readable file,
     refuse the review `unreadable_excludes` (AUDIT3's reread G5; this test fails on 38b0e689's sender);
   - git's default `~/.config/git/ignore`, and an excludes file reached through a link from dotfiles, are
     honoured while the person's other settings are not (this pins behaviour git already had: it passes on
     38b0e689 too);
   - a review runs no gh;
   - the same work sent twice is refused `already_sent`, the first branch kept;
   - an origin in its `git@github.com:` or `ssh://git@github.com/` form reaches the same stand-in, and any other
     github.com address leads to a path that is not there.
   Each of G1, G2, S1 and S2/S3's tests fails on c3d3fe5d's sender and passes on its fix.
10. **A link that leaves the folder is never sent** (found on 2026-11-12 in a rehearsal on a scratch control plane
    with a game made from the VR starter: its `.gitignore` says `node_modules/`, which a link does not match, and
    its `node_modules` is a link to the shared dependencies by an absolute path holding the Mac's user name):
    with that `.gitignore`, a `node_modules` link to an absolute folder and a link `up` to `docs/../../elsewhere`
    are neither listed as files nor sent, the review lists them as `links_left_out`, and `docs/readme`, a link to
    `../app.txt` inside the folder, is sent as a link (mode 120000). Where only such links differ, the refusal
    says so. A link in Publish's built copy to `/etc` is not published and is listed the same way. The send test
    fails without the rule (all three listed as added) and passes with it. The starter's own `.gitignore` now
    names `node_modules` without the slash, so a person's own `git add -A` keeps the link out too.
    Seen on a scratch control plane (127.0.0.1:47910), a game made from the VR starter with its folder's
    exclude emptied: Send's review on main 60a0d8830 listed `index.html`, `node_modules`, `src/index.ts`; on the
    fix, `index.html` and `src/index.ts`.

## Publish, on the same stand-ins (`sending/pages.test.ts`)

1. **The review** counts the built copy's files (an `index.html` and an `app.js`, never its `.env`), names
   `owner/garden` and the expected address `https://owner.github.io/garden/`. It refuses without gh
   (`no_gh`) and without a built copy (`no_built_copy`).
2. **A publish** pushes one commit holding exactly the built files to `halcyonic-pages`, with no parent
   the first time. It asks the stand-in gh to turn Pages on for that branch, then for `html_url`, and
   returns that address. The person's folder keeps its HEAD and status. A stale review publishes nothing.
3. **A second publish** builds on the first: its parent is the first's commit. One that does not build
   on what the branch holds is refused (`push_refused`), and the branch keeps its commit: never a force.
4. **Unpublishing** deletes the branch on the stand-in GitHub.
5. **Take Down acts on what was published, and only on it** (AUDIT3's probes U1 and U2, kept as tests):
   - with the folder's origin pointed at another repository that has a Pages branch of its own, Take Down
     is refused `origin_changed`, and both branches stay; with the origin back, it comes down;
   - after the person pushed a commit of their own (a `CNAME`) onto the branch from another clone, Take
     Down is refused `pages_changed`, and their commit stays;
   - each test fails when the origin check and the lease are taken out (checked by changing the code, then
     restored);
   - a Take Down carrying another commit than the published one is refused `review_stale` (admission);
   - a publish review runs no gh.

## The first Send, the title and Make Public, on the same stand-ins (2026-11-12)

- **A folder that is no repository** (`lantern-game`, with a `.gitignore` naming `dist`, a built `dist/` and a
  `.env`): the review names no repository, `creates {name: lantern-game, visibility: private}`, `main`, no base,
  the title as given, and the three files git would add (`.gitignore`, `index.html`, `src/game.ts`; never
  `dist/` or `.env`). A review of it public has another digest, and a press with the other visibility is
  `review_stale`. The press asks the stand-in gh `repo create lantern-game --private` (no token reaches it),
  pushes one commit with no parent as `main` (the title as its message, the git author from git's own
  settings), and the folder is then that repository's clone: HEAD on `main` at the sent commit, `origin` its
  address, `git status` clean with `/.env` in its `.git/info/exclude` (what the send kept out stays out of the
  person's own git), the `.env` unchanged, no `hooks` folder. Its next
  send is the ordinary one: `repository owner/lantern-game`, the base the first send's commit, one changed file.
- **Refused:** a second folder of the same name (`repository_not_made`, its folder left with no `.git`); a
  folder inside another repository (`inside_repository`); no gh (`no_gh`); gh signed out at the press
  (`gh_signed_out`, no `repo create` asked); a name GitHub would not take, `..`, or one gh would read as a flag,
  `--public` (`invalid_name`); gh is asked `repo create --private -- <name>`. A folder under a
  home folder kept as a repository is sent as its own. Gh is asked `auth status` before `repo create`; the made
  repository is handed to the journal at once (`onMade`); the clone leaves no `FETCH_HEAD` and nothing beside.
- **A first send stopped after GitHub made the repository** (the stand-in's new repository refuses objects):
  `push_refused`, effect unknown, the folder left with no `.git`, the made repository handed to the journal
  once. The next review, with the journal's made repository, names it and makes none (`creates` null); its
  press pushes `main` there and clones the folder, and `repo create` was asked once in all. Another folder
  sent into that repository, whose `main` holds work Halcyonic did not send, is refused
  `repository_not_empty` and its `main` is untouched.
- **Ssh:** where gh's `git_protocol` is `ssh`, the clone's `origin` is `git@github.com:owner/<name>.git`.
- **A clone that did not take** (the folder cannot take a `.git`): the send completes with the work on GitHub,
  `cloned` false, no `.git` and nothing beside left in the folder. After a change, the next send, into the
  journal's made repository, commits on top of that first commit (`main^` is it), pushes `main` without a
  force, and clones the folder, `git status` clean. A git folder a killed send left beside, named
  `.git-halcyonic-*`, is never sent and is removed by the next clone.
- **The title** (`sendTitle`): the person's own made one line; else the latest task's title; else the
  project's name; never over 200 characters. Through the control plane, a review with an empty body shows
  the project's name (`Garden`), a send with no title goes with it as the commit's message, and a body with
  an unknown field is refused `invalid_request`.
- **Publish:** a repository the journal says a first send made private is reviewed `private`; any other is
  `unknown` (a review asks GitHub nothing). Make Public is in the digest (a press without the choice it was
  reviewed with is `review_stale`), and its press asks gh `repo edit owner/garden --visibility public
  --accept-visibility-change-consequences` before pages are turned on. A repository already public has nothing
  to make public, and neither has one the journal does not say this project's first Send made (`unknown`).
  Where GitHub answers no pages address, the publish is refused `pages_refused` with Make
  Public named for a private repository, and the Pages branch it pushed is gone again. Make Public is handed to
  the journal the moment it is done (`onMadePublic`).
- **The journal:** `project.repository_made` keeps who can see a repository a first send made;
  `project.repository_made_public` turns it public, for that repository only; both survive a checkpoint. A
  publish that made it public and was then refused records it, says it is public now, and leaves no Pages branch.

## GitHub's own rule for pages (read 2026-11-12, working rule 2)

- GitHub's plans (https://docs.github.com/en/get-started/learning-about-github/githubs-plans): GitHub Free for
  personal accounts lists pages in public repositories only; Pro and Team list pages among the tools for
  private repositories.
- "Creating a GitHub Pages site"
  (https://docs.github.com/en/pages/getting-started-with-github-pages/creating-a-github-pages-site): on GitHub
  Free or GitHub Free for organizations, the repository must be public.
- The REST API's "Create a GitHub Pages site" (https://docs.github.com/en/rest/pages/pages#create-a-github-pages-site)
  documents 201, 409 and 422 and does not say what 409 means. Neither "Delete a GitHub Pages site" nor
  "Unpublishing a GitHub Pages site" says whether a site stops being served when its source branch is deleted:
  Take Down deletes the branch, and whether the address then stops answering is for the real run to show.

## Not proven

- A real push to github.com over https with the macOS keychain's credential helper, or over ssh with an
  agent's key.
- A real `gh pr create` or its output on success.
- A real Pages site: turning Pages on through GitHub's API, its `html_url`, the build GitHub runs, a
  private repository's plan, or a game opened from `github.io` in a headset.
- A repository whose objects are SHA-256 (read from its config): the bare repository is made to match,
  but no test uses one.
- Git LFS: refused, never sent. A repository-level `core.excludesFile` in the person's `.git/config` is not read
  (that file is an agent's to write); only `.git/info/exclude`, `.gitignore` files and the global excludes file are.
