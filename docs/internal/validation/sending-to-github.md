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

## Any host (2026-11-12, `sending/github.test.ts`, `pages.test.ts`)

- An origin `https://someone:secret@gitlab.example/group/sub/garden.git` (the test's git settings send that host to
  a local bare repository): the review names `{host: gitlab.example, path: group/sub/garden}`, never the user part,
  and `review_request` false; the press pushes the branch there and asks gh for nothing. Its ssh form reads the same.
- An origin with a port, a climbing path, or `file://`: refused `no_origin`, nothing sent.
- Publish with an origin on gitlab.example: refused `no_pages_host`, in words naming the host.

## The first real run against github.com (2026-11-12, 07:17Z to 07:21Z)

On the owner's yes of 03:40Z (one throwaway repository in their account, one branch and one pull request, a test
page for a few minutes, the repository deleted after), read by AUDIT3 (PLAN INSIDE SCOPE). The code: `git archive
8810cb41c` (the link rule, the general Send, AUDIT3's S1 to S3, Add's link rule), on a scratch control plane on
127.0.0.1:47910 with its own data and tasks folders, the game's folder outside every repository. The person's own
git and gh did the work with the sign-in already on this Mac; no token was read, printed or copied. The commits
were signed by a scratch identity set in Halcyonic's own repository for the project, so the owner's git name and
email reached nothing. The repository is `<owner>/halcyonic-proof-20261112-0717`.
- 07:17:45 a game from the VR starter, a box of its own; its check passed, confined.
- 07:19:28 Send's review, nothing given: a new private repository `halcyonic-proof-20261112-0717`, title "Proof
  Game", 26 files, no link left out. The scan of what could leave: 0 hits.
- 07:19:35 **the first Send**: gh made the repository, private; `main` was pushed; the folder became its clone
  (`## main...origin/main`). The commit's author and committer: the scratch identity, as checked.
- 07:20:29 **the second Send** (the box blue, checked again, title "Proof <stamp>: a blue box"): one changed file,
  the branch `halcyonic/halcyonic-proof-20261112-0717-060624d6`, and **pull request 1** opened by gh.
- 07:20:36 **Publish, as it was**: GitHub kept no page for the private repository (GitHub Free), so the product
  refused it `pages_refused` and the Pages branch it had pushed came down again (effect none), as built.
- 07:20:41 **Publish with Make Public: refused too, and that is the finding.** Its review said the repository's
  visibility was `unknown` and offered no Make Public, though Halcyonic had made it private three minutes before:
  gh printed the owner with capitals, and the folder's origin is read lower-cased, so the two names did not compare
  equal. GitHub's names are case-insensitive. Fixed after the run: every comparison of repository names ignores
  case (`sameRepository`, and the projection's Make Public record), with a test of a capitalised owner.
- So no page went live and Take Down did not run; the open question (does a page stop when its branch is deleted
  with pages still on) is still open.
- 07:20:42 the teardown: pages off (none were on), `gh repo delete` succeeded, and `gh repo view` answers not found.
  The repository is gone.
- A slip in the record itself, also fixed: the run's own log masked the login case-sensitively, so its lower-case
  spelling stayed in the run's private log and in the lane's chat once; the logs are scrubbed and the mask ignores
  case now. Nothing tracked holds it.

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

## The second real run against github.com (2026-11-12, 14:01Z to 14:04Z)

One run, inside the owner's yes ("Yes, run it once"), on main 08dbce62a, read by AUDIT3 before and after.
- Proven:
  - the first Send made a private repository and pushed `main`, signed by the scratch identity;
  - the second Send pushed a branch and opened pull request 1;
  - Publish as it was was refused (`pages_refused`, GitHub Free and a private repository), and its branch came
    down again;
  - Make Public was offered for the repository the first Send made, spelled two ways (the case fix), and GitHub
    made it public (`project.repository_made_public`).
- Then the push of `halcyonic-pages` to the now-public repository was refused, about three seconds after the
  visibility change. The same files had been pushed to the same branch name while it was private, seconds before.
  - The product kept no git words, so why is not known. Since then every `push_refused` carries git's own line,
    masked.
  - The refusal also said effect none while the repository was public. It now says effect `unknown`, says the
    repository is public, and offers Make Private.
- The repository was deleted at the end; `gh repo view` answers not found. It was public for about four seconds.
- Not proven, still: a live page, its real `html_url`, Take Down of a live page, and whether a page stops when its
  branch is deleted.

## What GitHub documents after a visibility change (read 2026-11-12, working rule 2)

Read by a fetch of the official pages only, with nothing inferred:
- "Setting repository visibility"
  (https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility):
  - It lists what making a repository public does: push rulesets are disabled; anyone can fork it; stars and
    watchers are erased; it gains GitHub Advanced Security features; Actions history becomes visible; the change
    is published as activity.
  - It says nothing of the change taking time, or of pushes being refused or delayed around it.
- REST "Update a repository" (https://docs.github.com/en/rest/repos/repos#update-a-repository), which
  `gh repo edit --visibility` calls:
  - It documents 200, 307, 403, 404 and 422, with no 202 and no status to poll.
  - It says nothing about what a client should do after the change.
- REST "Create a GitHub Pages site" (https://docs.github.com/en/rest/pages/pages#create-a-github-pages-site):
  - It documents 201, 409 and 422 and gives no timing.
  - `html_url` is typed only as a URI.
- "About push protection" (https://docs.github.com/en/code-security/secret-scanning/introduction/about-push-protection):
  - Push protection for users is on by default and stops pushes of secrets to public repositories.
  - At the repository level it is off by default.
  - A blocked push gets "a detailed message"; the page shows no example.
  - This is the one documented refusal tied to a repository being public. Whether it was run 2's cause is not known.
    The built copy held nothing of the well-known key shapes, but GitHub's detectors are more than those.
- "Rate limits for the REST API" (https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api):
  retry guidance for REST requests (`retry-after`, `x-ratelimit-reset`), nothing for a git push.
- gh's manual for `gh repo edit` (https://cli.github.com/manual/gh_repo_edit): `--accept-visibility-change-consequences`
  is required with `--visibility`; nothing on timing.

So the documentation supports neither waiting after a visibility change nor retrying a refused push, and the
product does neither:
- the refusal stays as git said it;
- the repository's new visibility is said, and recorded;
- the next step is offered: Publish again (offered once the check is current, and now with no Make Public, since
  the repository is public), or Make Private.
Proven on the stand-ins (`pages.test.ts`): a refusal that clears (the person's next Publish publishes) and one
that never clears (each press is refused in git's words, effect none, since nothing more changed).

## A repository's own git settings, and the history a push carries (2026-11-13, `sending/github.test.ts`)

Same stand-ins, same git (`2.50.1 (Apple Git-155)`). Prompted by AUDIT3's 12:44Z ALERT (an agent can write a
project's `.git/config`) and the lead's 12:52Z and 13:04Z answers.

1. **A repository's own settings run nothing in a send.** The origin is `git@standin.test:owner/garden.git`, reached
   over "ssh" through the person's own global `core.sshCommand`, a script that serves the stand-in repository. The
   project repository sets `core.sshCommand` and a `pushInsteadOf` to a decoy repository, eight more
   program-running settings (`core.fsmonitor`, `core.pager`, `core.askPass`, `core.alternateRefsCommand`,
   `uploadpack.packObjectsHook`, `credential.helper`, `gpg.program` with `commit.gpgSign`, `diff.external`) and
   `core.hooksPath` with five hooks, each writing its own mark. A review and a press with a secret held leave no mark;
   the new branch reaches the stand-in, the decoy is empty. Controls: the folder's own `git push` goes to the decoy
   and runs `pre-push`; its own `git ls-remote` runs the repository's ssh command. Send's authentication is
   unchanged. Not exercised: `credential.helper` and `core.askPass`, which only an https remote would use.
2. **A held secret in a commit the remote lacks refuses the press.** A commit adding `key.txt` with a held (made-up)
   value, then one removing it, neither pushed: on main the send went and the stand-in's `git log --all -p` held the
   value. Now the press is refused naming `key.txt` as an earlier version, with nothing pushed, even with the folder's
   `refs/remotes/origin/main` moved onto the commit. Also refused: a value in a commit message, in a file's name now or
   in an earlier version (the name never shown), in the title (at the review, before a first send makes a
   repository), more history than the bound (`history_too_large`) and a remote that cannot be asked
   (`not_reached`). A name with a line break before the value is found in the folder that holds it, and a remote on protocol v0 is read
   by its listed tips and still refuses an unsent value. A value already on the remote is not read again, also when the
   remote is ahead of the branch (protocol v2 only: through the v0 fallback, a remote ahead is read from the first commit);
   one the push's own address lacks (a global `pushInsteadOf` to a second repository) is refused though the address
   it is read from holds it. The first two failed on main; the remote ahead and the push address failed on the first
   version of the fix (`ls-remote` tips), which the critic found.

## Not proven

- A real push to github.com over https with the macOS keychain's credential helper, or over ssh with an
  agent's key.
- A real `gh pr create` or its output on success.
- The history check against a real remote: GitHub's answer to `fetch --negotiate-only` and a push with
  `push.negotiate`, a host that cannot negotiate (read by its listed tips), and the time to read a long unsent history
  (512 MB at most) are not measured. An ssh key that asks for a touch is asked three times per send (the check, the
  push and the push's own negotiation; four on a host that cannot negotiate), and `push.negotiate` adds a connection
  to every push, held secret or not, inside the push's 120 s. Not tested: the push's own negotiation failing where the
  check's did not, and the history check refusing after a first send made its repository. Publish's refusal is tested
  (`pages.test.ts`: an earlier publish the remote no longer has, holding a value held since, refuses before Make Public).
- Send One Change against a real remote: GitHub's `ls-remote --symref` answer for its default branch, the fetch of
  one branch at the push address, and how the review request reads with a base older than the branch's tip. Proven
  on the local stand-in only (`github.test.ts`, "Send One Change"): the offer, the base a branch reaches and never
  one only kept in the store, the remote moved back between review and press, the remote's own branch over the
  folder's tracking settings, a `.env` kept back, and no offer with today's files holding it or no history in
  common.
- A real Pages site: turning Pages on through GitHub's API, its `html_url`, the build GitHub runs, a
  private repository's plan, or a game opened from `github.io` in a headset.
- A repository whose objects are SHA-256 (read from its config): the bare repository is made to match,
  but no test uses one.
- Git LFS: refused, never sent. A repository-level `core.excludesFile` in the person's `.git/config` is not read
  (that file is an agent's to write); only `.git/info/exclude`, `.gitignore` files and the global excludes file are.
