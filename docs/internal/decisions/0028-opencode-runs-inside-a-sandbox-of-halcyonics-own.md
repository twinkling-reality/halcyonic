# ADR 0028: OpenCode runs inside a macOS sandbox of Halcyonic's own

- Status: Accepted (delegated by the owner, 2026-10-08); amended 2026-10-08: Ollama only through
  a gate of Halcyonic's own (decision 5)
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
   - **network:** outbound only to the Ollama gate's port on loopback (decision 5) and the
     server's own; no connection beyond the Mac, to Ollama's own port, to another program listening
     on it (a debugger, a database, an app's own port), or to a local socket;
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
5. **Ollama only through a gate** (amended 2026-10-08, the coordinator's decision): a loopback
   server in the control plane's own process, outside the sandbox, which the adapter opens on a free
   port before it launches the server and closes once the server has exited
   (`packages/integrations/opencode/src/ollama-gate.ts`). It passes on exactly the three requests
   OpenCode 2.0.18 makes of Ollama, `GET /api/tags`, `POST /api/show` and
   `POST /v1/chat/completions`, and refuses everything else before it reaches Ollama. The model list
   it answers holds only models that run on this Mac (no `remote_host` or `remote_model`, no `cloud`
   tag). A show or chat body is a JSON object of at most 8 MiB, nested at most 64 deep, that must
   name one of them; a body in which any object has two keys differing only in letter case is
   refused, since Ollama's Go decoder reads them as one and keeps the last. What reaches Ollama is
   built anew: a show by the model alone, a chat from the fields OpenCode sends (`bodyFields`,
   `ai/src/protocols/openai-chat.ts`). Chats stream back, and closing one closes Ollama's reply. At
   most one chat is open for each turn running or starting on the server, and at least one; at
   most four more wait, first come first served, each for at most 60 s, then are refused. At most
   256 connections are open and two bodies read at once. A connection is closed when it takes more
   than 15 s to send a request's headers, from when it opened or its last reply ended, when its
   request takes more than 5 s to send its body once read, and when it sends a request while its
   last reply is still open, which OpenCode was not seen doing in any end to end test through the
   gate; a request that waits more than 15 s for a place to be read is refused. The gate always connects to the control
   plane's Ollama address, `127.0.0.1:11434`, never one a request names. The adapter points
   OpenCode at it with `OPENCODE_CONFIG_CONTENT`, which OpenCode loads after every other settings
   source, so its `baseURL` wins, and which nothing else may set.

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
  - **Ollama, through the gate:** a command can ask a model on this Mac for replies, as OpenCode
    does, holding it while it runs, and can take the chat slots, keep the gate's connections full
    or keep its read places busy with slow requests, so that OpenCode's own next request waits or
    fails, visibly, as a failed turn; nothing it
    sends that way leaves the Mac. Before the gate
    (2026-10-08, decision 5), Ollama's own port was reachable, and Ollama, unsandboxed, could be
    asked to pull from or push to any registry, a pull's name carrying data to a host the command
    chose, or to run a cloud model.
  - **The project roots** are writable, so an approved command can change any project there.
  - **The server's own data folder** is one for every project: a command run in one project can
    read another's OpenCode sessions there, and change saved session rules in its database
    directly, not only through OpenCode's API. Whether tamper detection notices a change made
    there is not verified.
  - **The server's own port** is reachable, and OpenCode's server password stays readable from
    inside: macOS gives a process the starting environment of another of the same user's
    third-party processes, and no Seatbelt rule tried (`process-info*` for other processes,
    `sysctl-read` by name or whole) withholds it. A command can therefore use OpenCode's API to
    change its session's rules or answer its own asks; the adapter's tamper detection stops the task
    when it does, after the fact, and the sandbox still bounds what any rule then allows.
  - Credentials the person keeps elsewhere than the paths above stay readable.
- **Costs:** one more loopback listener in the control plane for each OpenCode server, and a hop on
  every streamed token. If an OpenCode upgrade asks Ollama for something new, the gate refuses it
  and that feature fails until the gate allows it. Approved commands that need the network fail (`npm install`, `git fetch`,
  `pip install`), and so do a project's own tests or dev servers that open a local port and connect
  to it: the person runs those. OpenCode cannot search files unless `rg` is on the PATH, since the
  sandbox refuses its download of ripgrep; the control plane warns at startup. Hosted models
  OpenCode lists cannot be reached from inside, so a start on one fails. A saved "always" or a Console login from the person's own OpenCode no longer
  applies to Halcyonic's tasks; and Salidium's OpenCode source (off by default, and Salidium itself
  off now) no longer sees Halcyonic's OpenCode tasks, whose sessions are in Halcyonic's own data
  folder. Each OpenCode upgrade needs the profile re-verified and the runtime tests re-run.
- **Required:** every OpenCode end to end test that goes through the adapter runs under the
  profile (a few controls launch a bare server on purpose); a tracked test shows an approved command
  refused its write outside, a credential, a connection beyond the Mac and to another program's
  port, a nested looser sandbox and its own profile; a private test shows each recorded
  parse-bypass construct contained; tracked tests show OpenCode asks Ollama for nothing but the
  three requests, one chat at a time for each session, that it reaches Ollama through the gate
  whatever its settings name, and that the gate refuses an approved command everything else and
  Ollama's own port. Linux and Windows have no Seatbelt; the adapter runs OpenCode
  unsandboxed there, and says so.
- **Worth revisiting:** if Apple removes `sandbox-exec`; if OpenCode ships a sandbox of its own; if
  a person-chosen list of extra local ports is wanted; or to give
  Codex's and Claude Code's runs the same bounds.
