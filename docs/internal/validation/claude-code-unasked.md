# Which Bash commands Claude Code runs without asking

Verified 2026-11-17 on the Claude Code CLI the agent SDK 0.3.283 bundles (`claude --version`: 2.1.283), as Halora launches
it outside the closed profile: default permission mode, `settingSources: []`, the host's `canUseTool`. A fake
Messages API on loopback asked for one Bash call per command; the callback recorded whether it was called and denied.
HOME was a scratch folder, and gh, security, curl, git, ssh, wget and nc were shims on PATH that did nothing, so no
command read a real credential or reached the network. The test that holds it is
`packages/integrations/claude-code/src/unasked-bash.e2e.test.ts` (needs CLAUDE_CODE_BIN).

## Claude Code's own default (no ask rules)

Ran unasked, inside the session's folder: `ls`, `cat`, `head`, `grep`, `sed -n`, `sleep`, a pipe of those
(`ls | head -1`), `git status`, `git log`, `git show`, `git branch -a`, `git describe`, `git grep`, `git blame`,
`git shortlog`, `git rev-parse`, and also `git remote -v`, `git config --get <key>`, `cat .git/config` and a bare
`git ls-remote`.

Asked: a redirect (`cat x > y`), `rm`, `env`, `printenv`, `curl`, `gh auth status`, `gh auth token`,
`security find-generic-password`, `git push`, `git fetch`, `git pull`, `git clone`, `git submodule update`,
`git archive --remote`, `git ls-remote <url>`, `git config --list`, `git config --get-regexp`, `git credential fill`,
`git var -l`, and any `cat`, `head`, `sed`, `ls`, `find`, `grep -r` or `git -C` that names a path outside the folder.

## With Halora's ask rules

Every run outside the closed profile passes `permissions.ask` in its own flag settings (`OPEN_RUN_ASKS`:
`git remote`, `git config`, `git ls-remote`, `git fetch`, `git pull`, `git push`, `git clone`, `git submodule`,
`git credential`, `git var`). Then `git remote -v`, `git config --get` and a bare `git ls-remote` ask too; the
read-only rest is unchanged. The rules hold the forms that would hide a git command from a prefix: `git -C .`,
`git --no-pager`, `git -c core.pager=cat`, `GIT_DIR=.git git`, `cd . && git`, `ls && git`, `git ... | cat`,
`command git`, `sh -c 'git ...'` and `bash -c 'git ...'` all ask (four of them, `cd . &&`, `ls &&`, `| cat` and
`command git`, ran unasked before the rules).

`cat .git/config` still runs unasked: in a task's copy that file holds no remote and no credential (ADR 0045); outside
a copy it is the folder's own and can show its remote addresses, including any credential written into one, unasked.
So a task that would work in the folder itself (copies off, `HALCYONIC_TASK_COPIES=0`, or an agent app that cannot be
held to a copy) does not start where that file holds a password or token inside an http, https or ftp address: the
host refuses it in words that never show the address (`folderHoldsAddressCredential`, `ADDRESS_CREDENTIAL_REFUSED`).

## Other tools

Read of a file inside the folder runs unasked; Read of one outside it asks. WebFetch (of a loopback address, none of
Claude Code's own preapproved hosts) and WebSearch ask. This CLI offers no Grep or Glob tool: a call to either is
answered "No such tool available", so a search runs only through Bash, which the rules above hold. The test fails if a
new CLI offers them again.

## What it does not show

The set is Claude Code's, by command text, and changes with its version; the test fails when it does. A command the
probe did not name is Claude Code's to classify. Read was probed for one file inside and one outside; its rules for
other paths are Claude Code's. A credential kept in the person's global git settings (`~/.gitconfig`) is read only by
commands that ask (`git config --global`, `git config --list`, a read outside the folder).
