# ADR 0041: A new app's agent is confined to its project

- Status: Accepted
- Date: 2026-10-29
- Complements ADR 0028 (OpenCode's sandbox), ADR 0033 and ADR 0037

## Context

A new app (ADR 0037) is made by an agent from one sentence and then opened in the headset's browser. Codex 0.157.0's
own sandbox, `workspace-write`, limits writes to the project but reads the whole disk: an agent asked to build a game
could read the person's keys, Halcyonic's journal or another project and write them into the page. (Amended
2026-11-04: `workspace-write` also writes `/tmp`. See the amendment of that date.) The headset side
cannot close the page's way out (RUNTIME's M1.6 plan), so the reading has to be stopped on the Mac.

Seatbelt profiles do not nest: Codex's own sandbox cannot start inside another one (`sandbox_apply: Operation not
permitted`, exit 71, validation/codex-capabilities.md). Codex offers a sandbox policy for exactly this case,
`externalSandbox`, under which it runs commands without its own sandbox because something outside it confines them.
`thread/start` does not accept it as a mode; `turn/start` takes it as a policy for that turn and the ones after it.

## Decision

**A project made from a starter gets a Codex server of its own, with a Codex home of its own, inside a Seatbelt
profile of Halcyonic's that is closed by default.** The profile starts from Codex 0.157.0's own base policy and its
minimal macOS platform defaults (`(deny default)`, Apache-2.0, as the pinned binary composes them, without the
preferences rules Codex drops when reads are restricted), then allows exactly this:
- **Read:** the project; the server's own Codex home (`<data dir>/codex-confined/<key>`, beside the open server's
  `codex-home`, never inside it) and its temporary folder inside it; the pinned binary's platform folder (Codex's
  bundled `rg` and `zsh`); the starter's shared dependencies (`<data dir>/starters/<id>`); the Node installation's
  folders: when Node lives under Homebrew's (`/opt/homebrew`) or `/usr/local`'s prefix, that prefix's `Cellar`,
  `opt`, `lib`, `bin`, `libexec` and `share`, and the one file `etc/openssl@3/openssl.cnf` Node reads to start, but
  never the prefix's `var` (databases such as PostgreSQL's and MongoDB's live there) or the rest of its `etc`;
  otherwise the folder two levels above the Node binary; `/System/Library/Perl`; and, from Codex's base, the
  system's own paths (`/System` frameworks and `CoreServices` version files, `/usr/lib`, `/usr/share`, `/bin`,
  `/sbin`, `/usr/bin`, `/usr/sbin`, `/usr/libexec`, `/etc`, `/Library/Apple`, `/Library/Preferences`, `/var/db`,
  `/Applications`, `/opt/homebrew/lib`, `/usr/local/lib`, the standard devices), plus the metadata of the folders
  above each allowed path. Codex's base, not Halcyonic, makes `/var/db` and `/Library/Preferences` readable: they are
  system-wide, and the person's own preferences under their home folder are not. Nothing else: not the rest of the person's home folder, `/private/tmp`, the
  person's temporary folder, `/Users/Shared`, `/Volumes`, other projects or project roots, Halcyonic's data.
- **Write:** the project, the server's own Codex home and its temporary folder, and the standard devices.
- **Network:** connections to Ollama's port on loopback only; no bind and no inbound connection on any address.
- **Mach services:** only Codex's base list (directory information, logging, trust, notifications, power, audio).
  No LaunchServices (so no `open` and no other app started), no pasteboard, no Apple events.
The confined server's Codex home gets a copy of the open home's `config.toml` at each launch. The open server's home
is outside the profile, so nothing a confined server does can plant settings, hooks or skills where an unconfined
server loads them. Everything the server starts, every command the model runs, inherits the profile.

**The profile is checked at every launch**, before anything is listed or started on that server, with one canary
for each way out, each run by the server: a read of a file in Halcyonic's data directory, a write to the open
server's Codex home, a listing of `/private/tmp`, the pasteboard (`pbpaste`), LaunchServices (`lsappinfo front`), a
listen on every address (Perl), and a listing of `/opt/homebrew` and `/usr/local`'s `var` and `etc` where they exist.
Every canary's output goes nowhere: only whether it was refused is looked at. Each must be refused; otherwise the server is stopped, nothing starts, and the
refusal names the way out that was open.

**Each of its turns uses Codex's `externalSandbox` policy**, given with every `turn/start`, so a thread resumed on a
relaunched server keeps it too. The thread itself starts as `workspace-write`, as every other thread does. The policy is
never a start option a person can choose: the adapter sets it only on a server it launched inside the profile.

**Other projects are unchanged:** one open server, Codex's own sandbox.

**On a confined project, commands inside the project run without asking the person.** Under `externalSandbox`,
Codex 0.157.0 runs every command, even one asking to leave its sandbox, without an approval request (observed with a
scripted model, validation/codex-capabilities.md). This amends, for this case only, SECURITY.md's rule that refuses
modes taking decisions away from the supervising person. Why: what the person approves is what a command may reach,
and here the sandbox bounds that to the project's folder, with nothing of theirs readable and no network but the
local model; a child making a game should not be stopped by a request at every step (the pattern of app builders such
as Lovable); and undo covers the project. Conditions, all required:
1. Only for a project that starts confined: made from a starter, its server inside the profile and the profile
   checked at launch. Never a start option a person can choose, never for any other project.
2. The headset says it in plain words before the first turn of such a project: that the agent will make and change
   files in this project without asking, and cannot reach anything else. This is a requirement on the headset's units
   (M1.7, M1.8e), not yet built.
3. A command the sandbox refused is shown as refused, from the command's own result as Codex reports it (its exit
   code and "Operation not permitted"), never as a success. The adapter passes these results today; the headset's
   wording is the headset units' to build.
4. Undo: with undo on (`HALCYONIC_GIT_BIN`, ADR 0036), a starter project's files are checkpoint 0 (ADR 0037) and each
   turn is checkpointed. With undo off there is no undo for a confined project; the headset must then say so ("No undo
   for this task", M1.7) before the first turn, next to the sentence of condition 2.
5. The runtime still declares `approval_may_leave_sandbox: true`: the declaration is the runtime's, for every
   project, and a confined project asks nothing it would apply to.

## Alternatives considered

- **Codex's `workspace-write` with read rules.** 0.157.0 has none that hold for every command.
- **Codex's `danger-full-access` inside the profile.** The same reach, but the name says the opposite of what holds,
  and the adapter refuses it as a start option (SECURITY.md, Agent permissions).
- **One profile for every project.** Each project would read every other project; a server per project keeps each to
  its own folder.

## Consequences

- An agent making a new app cannot read the person's keys, Halcyonic's data or another project, nor write outside the
  project, whatever the model asks (end to end tests with the real binary, `codex-runtime.e2e.test.ts`).
- Each confined project starts a Codex server of its own, recorded beside the open one (`codex-server.json-<key>`)
  with its profile (`.sb`); a server left by a crash is stopped at its next launch.
- The headset's approval requests do not appear for a new app's commands. If that proves wrong for the person,
  Codex's `untrusted` approval policy is the next thing to try.
- A server whose launch check is not refused on every way out is never used: the project's start is refused
  instead, naming the way out.
- A command that needs more than the profile allows (a network fetch, the clipboard, a file elsewhere) fails inside
  the project; the agent sees the refusal as the command's own output.
- macOS only. Elsewhere, and for OpenCode (whose profile, ADR 0028, still lets it read outside the project roots), a
  new app's agent is not confined, and the preview's record says so.
- Revisit with each Codex upgrade: `externalSandbox`, its behaviour and the nesting failure are 0.157.0's.

## Amendment, 2026-10-30: a VR version reads its original (M1.8c-1)

`project.make_vr_version` makes a new project from the iwsdk-vr starter beside an existing one (the original) and
records `project.made_from`. The new project's agent is confined as above, with one more read-only root: the
original's folder, as the journal names it when an execution starts. It can read the original's files but write
none of them. Under that folder its profile refuses, after the allow, every `.git`, `.env*`, `*.pem`, `*.key`,
`*.p12`, `*.pfx`, `*.keystore`, `*.jks`, `id_rsa`, `id_dsa`, `id_ecdsa` and `id_ed25519*`, `.npmrc`, `.netrc`,
`.pypirc` and file named credentials or secrets, in any case. A folder name the profile's rules cannot quote is
refused rather than left open. A secret kept under another name is readable; the rule is for the common shapes,
not a guarantee that the original holds nothing private, so the person chooses which game to make a VR version of.
The original must be one game's own folder (AUDIT M18C1-M1): never a project root or a folder holding one, the home folder or a folder
above it, a folder holding or inside Halcyonic's data, or a folder holding another project's folder. The host refuses
such an original when `project.make_vr_version` is admitted, and again each time it builds the agent's confinement,
which then reads no original; it also reads none when two starter projects share one folder (M18C1-S2). Every confined
agent's profile refuses Halcyonic's data folder outright, through an original or not, then allows the
confinement's own folders in it (its Codex home, the starters) again. A confined agent cannot hard-link a file it may not read into its own folder (tried, refused); but the
original's own files are readable whatever they hold, including anything an earlier agent of the original, which may
not have been confined, copied or linked into it. Tested on this Mac with the real profile on marker files
(`sandbox-profile.test.ts`).

## Amendment, 2026-10-30: confinement recorded per execution (S2)

Whether an execution ran confined is recorded when it starts, as observed by the adapter that chose its server: the
Codex adapter sets `confined` on `runtime.execution.started`, true only when it started the execution on the project's
confined server. A check's `reads_confined` is true only when every execution in the project has such a record saying
true. It is no longer inferred from the host's settings and the runtime's kind, so an execution recorded before this
change, or one on the open server, makes it false; a project made before it reads not confined.


## Amendment, 2026-10-31: the folders above the confined home can be found (CX1)

Refusing the data folder outright (M1.8c-1) also refused finding the folders between it and the confinement's own
folders in it, and Codex resolves its home through every folder above it when it starts: the confined server exited
before answering initialize ("failed to canonicalize CODEX_HOME ... Operation not permitted"), so no starter project's
agent could start. The kernel's sandbox log named the refused operation: `file-test-existence` on the data folder.
The profile now allows, after the hidden folders are refused and the own folders allowed again, only
`file-read-metadata` and `file-test-existence` on each folder from a hidden folder down to an own folder inside it,
as literal paths: the data folder, `codex-confined` and `starters`. A confined agent can now learn that those three
folders exist and read their metadata (size, mode, owner and times); it still cannot list them, read anything in
them, or find any other file or folder in the data folder, and nothing else is widened. Tested on this Mac with the
real profile and the pinned Codex 0.157.0 ([codex-capabilities.md](../validation/codex-capabilities.md)).

## Amendment, 2026-11-03: the check reads no more than the agent (AUDIT P1)

The check of a starter project (ADR 0037) runs the agent's project through the starter's TypeScript and Vite, and
its profile read everything outside the home folder, so it reached what the confined agent may not: `/private/tmp`,
every app's per-user temporary and cache folders, drives, `/Users/Shared`, Homebrew's `etc` and `var`. The agent
names what the check reads, in its `tsconfig.json` and its sources, and the result came back to it: a type check's
first lines go after the person's next words (FX1), quoting the names in a file it read, and Vite inlines or copies
an asset a source names into the built copy the preview serves. Both were run on this Mac with the pinned starter
(TypeScript 5.9.3, Vite 7.3.7): the outside file's names were in `tsc.log`, and its bytes inlined in the built copy.
The check's profile is now closed for reads by default: the system's own paths this profile's base allows a confined
agent, Node's installation outside the home folder (`toolFoldersFor`), and the project, the starter, the check's
cache and the controller models, nothing else. The same tests now find neither (`preview/check.test.ts`, opt-in with
an installed starter; `preview/profiles.test.ts`).

## Amendment, 2026-11-03: a temporary folder for every command

A rehearsal of a new app on a local model found that a confined server's commands could not write a here-document:
zsh writes every here-document and here-string to a file under `TMPPREFIX`, `/tmp/zsh` unless it is set, and the
profile refuses `/private/tmp` (`zsh:1: can't create temp file for here document: operation not permitted`). The
server's temporary folder stays `tmp` in its own Codex home, and its environment now names it twice: `TMPDIR`, and
`TMPPREFIX` as `<that folder>/zsh`; every command inherits both. Each project has a home of its own, so no two projects
share the folder. Codex keeps helper links of its own in it too (`tmp/arg0`), made when it starts.

The folder is made anew, empty and closed to others (mode 700), at every launch, and removed when the adapter closes.
A command of the server's may write anything in its home, so it may leave a link in the folder, or move the home into
its project and put a link in its place (tried on this Mac, on folders of our own). So only macOS's own `chmod`, `rm`
and `mkdir` make and remove the folder, inside a second Seatbelt profile that refuses the network and every write but
in the folder, with the folder named through `codex-confined`, which is Halcyonic's, never through the home: a path
through such a link leads outside the folder and is refused, so making or removing it reaches nothing the server could
not reach itself. Folders a command made unwritable, as Go's module cache is, are made writable first. A folder that
cannot be emptied, such as one holding a file its owner made immutable, stops the launch, and the start is refused,
naming the folder to remove. A server stopped by its watchdog rather than by close leaves its folder until its
project's next launch.

Writes to `/tmp` stay refused, since that folder is shared with the person's other programs. macOS's own `mktemp`,
given no template, with `-t` or with `-d`, makes its file in the person's own temporary folder under `/var/folders`
whatever `TMPDIR` says (macOS 26.7), and the profile refuses that folder too; `mktemp -p "$TMPDIR"`, a template under
`$TMPDIR`, and tools that read `TMPDIR`, such as Node's `os.tmpdir()`, use the server's own folder. Whether confined
commands should get a `mktemp` that reads `TMPDIR` is open ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
Tested with the pinned binary and the fake provider (`codex-runtime.e2e.test.ts`), the fake binary
(`codex-runtime.test.ts`) and the real profile on folders of our own (`confined-temp.test.ts`)
([codex-capabilities.md](../validation/codex-capabilities.md)).

## Amendment, 2026-11-04: the open server writes `/tmp` too (AUDIT DEMO-TMP-WRITE)

The Context's "limits writes to the project" holds only for this decision's confined server. A project not made from a
starter, such as an existing 2D game, has its agent on the open server. That server runs Codex's own `workspace-write`,
which writes the project and also `/tmp`.

- Seen on 2026-11-04 in a rehearsal on a scratch control plane: an agent on the open server wrote
  `/private/tmp/new_index.html`, outside its project, with no request.
- Codex's configuration documents also name `$TMPDIR` as writable under `workspace-write`
  (`sandbox_workspace_write.exclude_slash_tmp`, `exclude_tmpdir_env_var`). That is from memory and not re-read for
  0.157.0.
- Such a project's executions record `confined: false` on `runtime.execution.started` (S2). The host checks only
  projects made from a starter (`preview.ts`), so such a project has no check. A starter project with any execution on
  the open server reads `reads_confined: false`. So nothing shown of such a project, a film included, may say its
  agent was kept to its project.
- Closing `/tmp` on the open server is not decided here. It would need Codex's two exclude settings and a `TMPDIR` and
  `TMPPREFIX` of the server's own, as the confined server has; without them, here-documents and a bare `mktemp` fail
  ([codex-capabilities.md](../validation/codex-capabilities.md)). That is a runtime change for later.

## Amendment, 2026-11-10: a home of its own for commands, and a worktree's git data read only (BOX-1)

The first step of a sandbox of Halcyonic's for every agent (ADR 0048, to be written). Seen on this Mac with the real
profile and the pinned Codex 0.157.0, no model:

- **git stopped inside the profile.** A confined server's commands inherited the person's own `HOME`, and git stops
  at a configuration file it may not read (`~/.gitconfig`, and Homebrew's `etc/gitconfig`), where a missing one is
  fine. So `git status` in a confined project exited 128 for anyone with a `~/.gitconfig`.
- **The fix:** a confined server now gives its commands `HOME` inside its own temporary folder, the XDG folders under
  it, and `GIT_CONFIG_NOSYSTEM=1`. The person's home stays closed. git works in a confined project that is its own
  repository (`codex-runtime.e2e.test.ts`, with a `.gitconfig` planted in the test's home: 128 before, 0 after).
- **A worktree or submodule has its git data outside its folder:** its `.git` is a file naming
  `<repository>/.git/worktrees/<name>` or `<repository>/.git/modules/...`. The agent can write that file, so it is
  never enough (AUDIT BOX-1 F1: a crafted one opened another repository's data). The profile reads, never writes,
  that folder and the repository's `.git` it lives in only when the repository names this folder back, in a file
  only its own git writes, and the named folder exists:
  - a worktree: `<repository>/.git/worktrees/<name>/gitdir` resolves to this folder's `.git`;
  - a submodule: its `config`'s `core.worktree` resolves to this folder.

  That is enough for `git status` and `git log`. A commit, which writes
  the shared objects and refs, is refused, in git's own words, and changes nothing (`sandbox-profile.test.ts`).
  - The repository's own working files stay unreadable.
  - A `.git` file naming any other shape opens nothing.
  - The repository's `.git/config` becomes readable to the agent, and a remote's address with a token in it would be
    too.
  - In a task's own copy (ADR 0045) the `.git` is the copy's own, so none of this applies there, and the copy's git
    settings lose the user part of every remote's address when the copy is made: no token of the person's is readable
    in a copy. A folder whose `.git` is a file never gets a copy.
- **Apple's `/usr/bin/git`** is a shim that writes a cache into the person's own temporary folder. Inside the profile
  that cache write is refused and the shim falls back to `xcodebuild`. It works, about three seconds a call, with
  warnings in the command's output. The developer folder's own git (`xcode-select -p`) and Homebrew's run directly.

## Amendment, 2026-11-10: every execution's folder is confined, and asking means asking (BOX-2)

On macOS, every Codex execution now runs on a server of its own inside this profile, for whatever folder it was
given, not only a project made from a starter (`codexConfinement` in `apps/control-plane/src/runtimes.ts`).
- **What a folder reads:**
  - A starter's project also reads the starter's shared dependencies, and a VR version also reads its original, as
    above.
  - Any other folder reads only itself, the tool folders and the git data of the amendment above.
- **Asking, which the lead decided on 2026-11-10:** a confined thread's turns run under `externalSandbox`, where
  Codex's `on-request` policy asks nothing (seen on 0.157.0: a `touch` ran unasked).
  - A starter's project keeps this decision: it asks nothing, whatever the start chose, and records `never_ask`.
  - Any other confined folder asks when its start asks. With no policy of its own, it gets `untrusted`, which asks
    before every command Codex does not know is safe (seen: the same `touch` raised an approval request). A start
    that never asks gets `never` (ADR 0043).
  - The record follows the policy.
  - A start that names `on-request` itself gets `untrusted` there too (AUDIT BOX-2 F1). Under `externalSandbox`
    that policy asks nothing, so keeping it would let a start that asks never ask.
  - A thread re-attached after a restart (ADR 0046) resumes with the same policy and level.
- **The tools a command gets:**
  - The product's own Node comes first on its `PATH`, the one the profile's tool folders are for. On this Mac the
    inherited `PATH` found a Node 20 from another installer, which the profile half reads (TASKS's run: npm warned of
    v20.10.0).
  - The profile reads one more system file, `/System/Library/OpenSSL/openssl.cnf`, which a Node build may read to
    start. It reads that file alone, never the folder's `private` or `certs`.
- **The trade-off, plainly:** asking, confined, asks before every command. The open server asked only for an
  escalation or a command it judged dangerous. A person who wants fewer presses chooses never-ask, which the sandbox
  bounds to the folder.
- **Which commands `untrusted` runs unasked: none tried.** Seen on 0.157.0 with the scripted provider through
  Codex's `exec_command` tool, which is how Halcyonic's threads run commands. Inside a confined folder it asked
  before each of 37 commands, the read-only ones included: `ls`, `pwd`, `cat`, `grep`, `rg`, `git status`,
  `git log`, `git diff`, `node -v`, `date`, `echo`. On the open server it asked before `ls`, `cat` and `git status`
  too. Codex's documentation calls `untrusted` asking for commands not known to be safe; on this path no command was
  known to be safe.
- **Elsewhere than macOS:** nothing changes. There is no profile, and the open server's `workspace-write` applies.
- **Still not confined:** listing models uses the open server, which runs no command.

## Amendment, 2026-11-11: another project inside a confined folder (AUDIT BOX-3 F1)

A confined folder's profile denies reads, writes and links of every other bound project's folder inside it, after
every allowance (`Confinement.inner`). See [ADR 0043](0043-a-person-may-let-a-tasks-agent-work-without-asking-where-its-runtime-keeps-it-in-bounds.md),
note of 2026-11-11.
