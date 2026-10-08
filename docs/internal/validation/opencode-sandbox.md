# OpenCode inside Halcyonic's macOS sandbox

- **Question:** Can the pinned OpenCode server, and everything it starts, run inside a Seatbelt
  profile of Halcyonic's own that bounds what any command can reach, whatever OpenCode's ask lets
  through ([ADR 0028](../decisions/0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md))?
- **Date:** 2026-10-08.
- **Versions:** macOS 26.7 on Apple silicon, `/usr/bin/sandbox-exec`; `@opencode/cli` 2.0.18, the
  pinned binary.
- **Method:** the adapter writes a profile (`sandbox-profile.ts`) and launches the server through
  `sandbox-exec -f <profile> <binary> serve …`, which runs the binary in its own place, so the
  process id and the `ps` identity check are unchanged. Runtime tests: the OpenCode end to end suite
  in its sandbox (private HOME, XDG and TMPDIR, a scripted provider and a proxy trap on loopback),
  one tracked escape test, one private test of the recorded parse-bypass constructs, and probes by
  hand. No model was loaded; no hosted call was made.
- **Status:** runtime verified, as below.

## The profile

`(allow default)`, then: outbound network denied but to the model's port on loopback and the
server's own; writes denied but in the project roots, OpenCode's data, state and cache folders, the
server's `TMPDIR` and `/dev/null`, `/dev/zero`, `/dev/tty*` and `/dev/fd/*`; reads denied of
Halcyonic's data directory and the person's own credentials, then allowed again of the pinned
binaries and Halcyonic's own OpenCode settings. Seatbelt matches a path as the file system resolves
it (`/var` is `/private/var`), so the adapter writes each path resolved, through its nearest
existing folder when it is not made yet.

## The four runtime tests

1. **The pinned server works under the profile, end to end.** It starts, serves, lists models and
   runs turns; its data folder is made inside the profile. OpenCode tries to make its configuration
   folder at startup and is refused; Halcyonic's own settings folder always exists, so the start
   goes on, and the folder stays unwritable, so nothing can plant a plugin there for the next start.
   Checked also as the control plane builds the adapter from this Mac's own settings: the server
   started inside the profile and listed 17 models, 10 of them Ollama's.
2. **The whole OpenCode end to end suite under the profile:** 44 of 44 pass, the network probe with
   its controls among them. One test changed meaning: an approved write outside the project, asked
   for twice (the folder, then the file), is now refused by the sandbox.
3. **The recorded parse-bypass constructs** (details private), each run with every ask approved,
   under zsh and under bash: none wrote outside the project, and a direct connection beyond the Mac
   failed (`curl` exit 7). Pass in both shells.
4. **The profile cannot be loosened from inside** (tracked e2e test): one approved command was
   refused a write outside the project, reading a stand-in for Halcyonic's access token, a
   connection beyond the Mac, a connection to another program listening on loopback, a nested
   `sandbox-exec` with an open profile (exit 71), and rewriting its own profile; its write inside the
   project went through, and the stand-in token never reached the model.

Two things the tests caught and the build fixed: granting the system's whole temporary folder let a
write escape (the test sandbox lives there), so only the server's own `TMPDIR` is granted; and a
folder not yet made was written unresolved, so OpenCode could not make its data folder.

## The server's password

Not withheld. A process may read the starting environment of another of the same user's processes
(`KERN_PROCARGS2`); macOS hides it for Apple's own platform binaries, not for third-party ones such
as OpenCode or Node.js. A stand-in secret in a Node.js process's environment was read from inside
profiles adding `(deny process-info* (target others))`, `(deny sysctl-read (sysctl-name …))` and a
blanket `(deny sysctl-read)`. `/bin/ps` itself cannot run inside, since `sandbox-exec` refuses a
setuid program, but the system call works without it. The adapter's answer stays as recorded in
[opencode-permissions.md](opencode-permissions.md): each session's environment leaves the password
out, and tamper detection stops a task whose rules or approvals something else changes.

## Loopback

Listening on this Mac at test time (`lsof -iTCP -sTCP:LISTEN`, names and ports only): Ollama 11434
and a helper port; Seorak (node) 4317; postgres 19490, 49448, 54733 and 54735; mongod 27017; a
Docker stack 54321 to 54324 and 54327; Discord's local RPC 6463; Python, Google and Spline helper
ports; workerd and dotnet test servers; other Node.js servers, among them a debugger on 9229;
AirPlay 5000 and 7000; rapportd. From inside a profile open to all of loopback, the Node.js debugger
answered, and through a debugger port code runs in that other, unsandboxed process; Docker's local
socket did not, since a local socket is outbound network too. So the profile allows only the model's
port and the server's own: by hand, Ollama answered and the debugger and a server the sandboxed
command started itself did not; IPv6 loopback to Ollama's port did not either, which costs nothing
while Ollama listens on IPv4.

## Not verified

- Whether OpenCode's server ever connects to itself on its own port; the port is allowed in case.
- A real local model's turns under the profile, with real builds and tests: what else an ordinary
  task trips on.
- Plugins and MCP servers a person configures in their own OpenCode settings, under the profile.
