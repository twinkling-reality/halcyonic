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

`(allow default)`, then: outbound network denied but to Ollama's port on loopback (11434: OpenCode
2.0.18 asks Ollama only there, `core/src/plugin/provider/ollama.ts`, and Halcyonic's own OpenCode
settings may not name another address; without them the person's own settings are read, and an
Ollama address there on another port is refused) and the server's own; writes denied but in the project roots,
the server's own data, state, cache and temporary folders under `<data dir>/opencode-sandbox`, and
`/dev/null`, `/dev/zero`, `/dev/tty*` and `/dev/fd/*`; reads denied of Halcyonic's data directory and
a listed set of the person's credentials, then allowed again of the pinned binaries, the server's own
folders and Halcyonic's own OpenCode settings. Seatbelt matches a path as the file system resolves it
(`/var` is `/private/var`), so the adapter writes each path as the native call resolves it, letter
case and Unicode form included, through its nearest existing folder when it is not made yet.

The server's folders are its own, never the person's: OpenCode 2.0.18 installs provider and plugin
packages under its cache and imports them, and looks up `rg` and shells there
(`util/src/npm.ts`, `ripgrep/binary.ts`, `shell/select.ts`); its data folder holds its saved rules
and credentials. Writable from inside, the person's own folders would let a command plant code their
own OpenCode runs outside the sandbox, or change its rules. Its temporary folder is its own too, never
the system's `/var/folders/…/T`, which every app shares (found by review, 2026-10-08).

## The four runtime tests

1. **The pinned server works under the profile, end to end.** It starts, serves, lists models and
   runs turns; its data folder is made inside the profile. OpenCode tries to make its configuration
   folder at startup and is refused; Halcyonic's own settings folder always exists, so the start
   goes on, and the folder stays unwritable, so nothing can plant a plugin there for the next start.
   Checked also as the control plane builds the adapter from this Mac's own settings: the server
   started inside the profile, with its own folders under `~/.halcyonic/opencode-sandbox`, and
   listed 17 models, 10 of them Ollama's.
2. **The whole OpenCode end to end suite**, every test that goes through the adapter under the
   profile (a few controls launch a bare server on purpose): 44 of 44 pass, the network probe with
   its controls among them, also with the server's own folders. Two tests changed meaning: an
   approved write outside the project, asked for twice (the folder, then the file), is now refused
   by the sandbox; and a saved "always" from the person's own OpenCode no longer reaches a
   Halcyonic task, whose server keeps a data folder of its own, so the command asks.
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

## Ollama, the residual

Ollama's port is reachable from inside, and Ollama runs unsandboxed: it can be asked to pull a model
from, or push one to, any registry, and a pull's name can itself carry data to a host the command
chooses. So data can still leave the Mac through Ollama (source reading of Ollama's API, not run).
`OLLAMA_NO_CLOUD=1` keeps its cloud models off; a proxy in front of Ollama is an open question.

## ripgrep

OpenCode 2.0.18 searches files with the first `rg` on its PATH, then one in its cache's `bin`, and
otherwise downloads ripgrep 15.1.0 from GitHub into that cache (`core/src/ripgrep/binary.ts`).
Inside the sandbox the download is refused, and the server's own cache starts empty, so without
`rg` on the PATH OpenCode cannot search files. The control plane warns at startup when that is so,
and mac-setup says it (source reading; on this Mac `rg` is on the PATH, so the runs above did not
reach the download; found by review, 2026-10-08).

## Not verified

- Whether OpenCode's server ever connects to itself on its own port; the port is allowed in case.
- A real local model's turns under the profile, with real builds and tests: what else an ordinary
  task trips on.
- Plugins and MCP servers a person configures in their own OpenCode settings, under the profile.
- A credential file that is a symbolic link, inside one of the denied folders, to a file outside
  them: Seatbelt checks the path a link resolves to, so the file it points to would stay readable
  unless it is denied too (inference, not run).
- Whether tamper detection notices saved session rules changed straight in the server's own
  database, not through OpenCode's API.
- Hosted models: OpenCode lists them (17 models, 10 of them Ollama's, on this Mac); from inside the
  sandbox none can be reached, so a start on one fails when its turn calls the provider.
