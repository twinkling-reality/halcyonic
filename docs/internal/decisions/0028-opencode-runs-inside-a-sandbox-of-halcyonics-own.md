# ADR 0028: OpenCode runs inside a macOS sandbox of Halcyonic's own

- Status: Proposed
- Date: 2026-10-08

## Context

- Every OpenCode session Halcyonic opens asks before shell commands (the owner's decision of
  2026-10-08), but OpenCode 2.0.18 raises the ask only for the commands its parse of the command
  line finds: some shell constructs, in zsh and in bash, run a command or write a file with no ask,
  inside the project and outside it ([opencode-permissions.md](../validation/opencode-permissions.md)).
  OpenCode's own security policy calls its permission system a feature for awareness, not
  isolation, and puts sandbox escapes out of its scope, so upstream will not make the ask a
  boundary.
- Before this decision a command that got past the ask, or that a person approved without seeing
  what it would do, could do anything the person can: reach any host, write their shell profiles,
  other repositories or `~/.halcyonic`, read their keys and Halcyonic's access token, change their
  own OpenCode (its packages, saved rules and credentials), and steer any program listening on the
  Mac.
- macOS's Seatbelt sandbox, through `/usr/bin/sandbox-exec`, confines a process and everything it
  starts. On this Mac (macOS 26.7, 2026-10-08) the pinned OpenCode server runs under a profile of
  Halcyonic's own, and the four runtime tests below pass
  ([opencode-sandbox.md](../validation/opencode-sandbox.md)). Codex's own sandbox is the same
  mechanism. `sandbox-exec` is marked deprecated, but ships and works.

## Decision

1. On macOS the OpenCode adapter launches `opencode serve` only under `sandbox-exec` with a
   profile it writes at each launch beside the server's record (mode 600, written whole under a new
   name and moved into place), and refuses to launch without one (`runtime_unavailable`, effect
   `none`). Elsewhere the control plane warns that OpenCode runs unsandboxed.
2. Inside, the server has data, state, cache and temporary folders of its own,
   `<data dir>/opencode-sandbox`, never the person's own OpenCode folders, which their own OpenCode
   runs packages and binaries from and keeps its saved rules and credentials in, nor the temporary
   folder every app shares. It reads Halcyonic's own OpenCode settings, or the person's when those
   are not set up.
3. The profile allows everything except:
   - **network:** outbound only to Ollama's port on loopback (11434, the only one OpenCode 2.0.18
     asks) and the server's own; no connection beyond the Mac, to another program listening on it
     (a debugger, a database, an app's own port), or to a local socket;
   - **writes:** only in the host's project roots, the server's own folders and the shell's
     devices; never `~/.halcyonic` otherwise, OpenCode's configuration folders, shell profiles or
     other repositories;
   - **reads:** nothing of Halcyonic's data directory (its journal and every credential in it) but
     the pinned binaries, the server's own folders and Halcyonic's own OpenCode settings, nor these
     credentials of the person's: `~/.ssh`, `~/.aws`, gcloud's, Azure's, Kubernetes', Docker's,
     npm's, PyPI's, `.netrc`, GnuPG, `.git-credentials`, the GitHub command line's, `~/.codex`,
     Cargo's and the login keychains.
4. The shell ask stays, as the person's say over what runs; the sandbox bounds what anything run
   can reach.

## Alternatives considered

- **A Halcyonic OpenCode plugin asking for every shell call by its whole command** (the `tool`
  `execute.before` hook, which may reject a call, and `POST /api/session/:id/permission`): it would
  make the ask complete, but bounds nothing a command does once approved; whether a hook can wait
  for the person's answer is unverified; and Halcyonic's own OpenCode settings would have to hold a
  plugin file, which the control plane refuses today. An open question.
- **Loopback open to every port:** a project's own tests and dev servers keep working, but a
  command can reach any local listener; at test time that included a Node.js debugger port, through
  which code runs in another, unsandboxed process. Refused: the agent must not steer other programs
  on the Mac.
- **The person's own OpenCode folders, shared:** kept Salidium's view of OpenCode tasks, and a saved
  "always" or a Console login of theirs applying; but a command could write packages and binaries
  their own OpenCode runs outside the sandbox, or change its saved rules and credentials. Refused.
- **Named hosts for package managers:** refused for now.
- **Deny shell:** loses builds, tests and git, which the owner decided against.
- **A container or virtual machine:** stronger, but heavy on a Mac, and the project folders and
  Ollama would have to be shared into it.

## Consequences

- A command, however it got to run (a bypassed ask, a repository file it executes, a plugin), can
  no longer connect beyond the Mac or to another local program itself, write outside the project
  roots and the server's own folders, change the person's own OpenCode, or read Halcyonic's or
  those credentials of the person's.
- **Residual, stated plainly:**
  - **Ollama:** its port is reachable, and Ollama runs unsandboxed: it can be asked to pull a model
    from, or push one to, any registry, and a pull's name can itself carry data to a host the
    command chooses. So data can still leave the Mac through Ollama. Ollama's own `OLLAMA_NO_CLOUD`
    keeps its cloud models off; a proxy in front of Ollama is an open question.
  - **The project roots** are writable, so an approved command can change any project there.
  - **The server's own port** is reachable, and OpenCode's server password stays readable from
    inside: macOS gives a process the starting environment of another of the same user's
    third-party processes, and no Seatbelt rule tried (`process-info*` for other processes,
    `sysctl-read` by name or whole) withholds it. A command can therefore use OpenCode's API to
    change its session's rules or answer its own asks; the adapter's tamper detection stops the task
    when it does, after the fact, and the sandbox still bounds what any rule then allows.
  - Credentials the person keeps elsewhere than the paths above stay readable.
- **Costs:** approved commands that need the network fail (`npm install`, `git fetch`,
  `pip install`), and so do a project's own tests or dev servers that open a local port and connect
  to it: the person runs those. Hosted models OpenCode lists cannot be reached from inside, so a
  start on one fails. A saved "always" or a Console login from the person's own OpenCode no longer
  applies to Halcyonic's tasks; and Salidium's OpenCode source (off by default, and Salidium itself
  off now) no longer sees Halcyonic's OpenCode tasks, whose sessions are in Halcyonic's own data
  folder. Each OpenCode upgrade needs the profile re-verified and the runtime tests re-run.
- **Required:** every OpenCode end to end test that goes through the adapter runs under the
  profile (a few controls launch a bare server on purpose); a tracked test shows an approved command
  refused its write outside, a credential, a connection beyond the Mac and to another program's
  port, a nested looser sandbox and its own profile; a private test shows each recorded
  parse-bypass construct contained. Linux and Windows have no Seatbelt; the adapter runs OpenCode
  unsandboxed there, and says so.
- **Worth revisiting:** if Apple removes `sandbox-exec`; if OpenCode ships a sandbox of its own; if
  a person-chosen list of extra local ports or a proxy in front of Ollama is wanted; or to give
  Codex's and Claude Code's runs the same bounds.
