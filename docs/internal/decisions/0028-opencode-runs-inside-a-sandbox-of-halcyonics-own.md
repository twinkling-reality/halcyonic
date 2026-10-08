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
  other repositories or `~/.halcyonic`, read their keys and Halcyonic's access token, and steer any
  program listening on the Mac.
- macOS's Seatbelt sandbox, through `/usr/bin/sandbox-exec`, confines a process and everything it
  starts. On this Mac (macOS 26.7, 2026-10-08) the pinned OpenCode server runs under a profile of
  Halcyonic's own, and the four runtime tests below pass
  ([opencode-sandbox.md](../validation/opencode-sandbox.md)). Codex's own sandbox is the same
  mechanism. `sandbox-exec` is marked deprecated, but ships and works.

## Decision

1. On macOS the OpenCode adapter launches `opencode serve` only under `sandbox-exec` with a
   profile it writes at each launch beside the server's record (mode 600), and refuses to launch
   without one (`runtime_unavailable`, effect `none`). Elsewhere the control plane warns that
   OpenCode runs unsandboxed.
2. The profile allows everything except:
   - **network:** outbound only to the model's port on loopback (Ollama's, from `OLLAMA_HOST` or
     11434) and the server's own; no other program listening on the Mac (a debugger, a database,
     an app's own port) and no local socket is reached, and nothing beyond the Mac;
   - **writes:** only in the host's project roots, OpenCode's own data, state and cache folders,
     the server's own temporary folder (`TMPDIR`) and the shell's devices; never `~/.halcyonic`,
     OpenCode's configuration folders, shell profiles or other repositories;
   - **reads:** nothing of Halcyonic's data directory (its journal and every credential in it) but
     the pinned binaries and Halcyonic's own OpenCode settings, nor the person's own credentials
     (`~/.ssh`, cloud command lines', container and package registries', `.netrc`, GnuPG, the
     login keychains).
3. The shell ask stays, as the person's say over what runs; the sandbox bounds what anything run
   can reach. With it, OpenCode on a local model is local-only, with the residual risk stated
   below.

## Alternatives considered

- **A Halcyonic OpenCode plugin asking for every shell call by its whole command** (the `tool`
  `execute.before` hook, which may reject a call, and `POST /api/session/:id/permission`): it would
  make the ask complete, but bounds nothing a command does once approved; whether a hook can wait
  for the person's answer is unverified; and Halcyonic's own OpenCode settings would have to hold a
  plugin file, which the control plane refuses today. An open question.
- **Loopback open to every port:** a project's own tests and dev servers keep working, but a
  command can reach any local listener; at test time that included a Node.js debugger port, through
  which code runs in another, unsandboxed process. Refused: local-only must mean the agent cannot
  steer other programs on the Mac.
- **Named hosts for package managers:** refused for now; local-only should mean local-only.
- **Deny shell:** loses builds, tests and git, which the owner decided against.
- **A container or virtual machine:** stronger, but heavy on a Mac, and the project folders and
  Ollama would have to be shared into it.

## Consequences

- A command, however it got to run (a bypassed ask, a repository file it executes, a plugin), can
  no longer reach the network or another local program, write outside the project roots, or read
  Halcyonic's or the person's credentials.
- **Residual:** the project roots are writable, so an approved command can still change any project
  there; the model's port is reachable, so a command can ask Ollama for anything; the server's own
  port is reachable, and OpenCode's server password stays readable from inside: macOS gives a
  process the environment of another of the same user's third-party processes, and no Seatbelt rule
  tried (`process-info*` for other processes, `sysctl-read` by name or whole) withholds it. A
  command can therefore use OpenCode's API to change its session's rules or answer its own asks;
  the adapter's tamper detection stops the task when it does, after the fact, and the sandbox still
  bounds what any rule then allows.
- **Costs:** approved commands that need the network fail (`npm install`, `git fetch`,
  `pip install`), and so do a project's own tests or dev servers that open a local port and connect
  to it: the person runs those. Writes outside the roots fail. Each OpenCode upgrade needs the
  profile re-verified (its folders, its startup), and the runtime tests below re-run.
- **Required:** the end to end suite runs every OpenCode test under the profile; a tracked test
  shows an approved command refused its write outside, a credential, the network, another program's
  port, a nested looser sandbox and its own profile; a private test shows each recorded parse-bypass
  construct contained. Linux and Windows have no Seatbelt; the adapter runs OpenCode unsandboxed
  there, and says so.
- **Worth revisiting:** if Apple removes `sandbox-exec`; if OpenCode ships a sandbox of its own; if
  a person-chosen list of extra local ports is wanted; or to give Codex's and Claude Code's runs the
  same bounds.
