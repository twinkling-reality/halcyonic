# ADR 0041: A new app's agent is confined to its project

- Status: Accepted
- Date: 2026-10-29
- Complements ADR 0028 (OpenCode's sandbox), ADR 0033 and ADR 0037

## Context

A new app (ADR 0037) is made by an agent from one sentence and then opened in the headset's browser. Codex 0.157.0's
own sandbox, `workspace-write`, limits writes to the project but reads the whole disk: an agent asked to build a game
could read the person's keys, Halcyonic's journal or another project and write them into the page. The headset side
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
