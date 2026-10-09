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

`(allow default)`, then: outbound network denied but to the Ollama gate's port on loopback (below)
and the server's own; writes denied but in the project roots,
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
socket did not, since a local socket is outbound network too. So the profile allowed only the model's
port and the server's own (since replaced by the gate's, below): by hand, Ollama answered and the debugger and a server the sandboxed
command started itself did not; IPv6 loopback to Ollama's port did not either, which costs nothing
while Ollama listens on IPv4.

## Ollama, through a gate

An independent review of the gate (2026-10-08, reading and small probes against the gate and a
stand-in, never Ollama) found that a chat naming the local model as `model` and a cloud model as
`Model` passed the check, Ollama's decoder reading the second; and that the gate's memory and its
waiting chats were unbounded. Its re-check of the fixes confirmed the case folding against Go's
own decoder (the Kelvin sign and long s are the only letters outside ASCII that fold into it) and
found that a request leaving while it waited to be read kept its read place, so two such requests
left every later one hanging, and that idle connections could fill the gate; its third look found
that a request sent on a connection while that connection's reply was still open went untimed.
Fixed before this record: the checks and limits in ADR 0028, decision 5, with a unit test for
each.

Before the gate, Ollama's port was reachable from inside, and Ollama runs unsandboxed: it can be
asked to pull a model from, or push one to, any registry, and a pull's name can itself carry data to
a host the command chooses (source reading of Ollama's API, not run). Since 2026-10-08 the sandbox
reaches Ollama only through a gate in the control plane's process
([ADR 0028](../decisions/0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md), decision 5).

From the source, OpenCode 2.0.18 asks Ollama for the model list (`GET /api/tags`, at start and
every 30 s), a model's details (`POST /api/show`, when its digest changes) and chat
(`POST /v1/chat/completions`, through its OpenAI-compatible provider), at the address its settings
name, `providers.ollama.settings.baseURL`, `/v1` swapped for `/api/...` for the first two
(`core/src/plugin/provider/ollama.ts`, `ai/src/protocols/openai-compatible-chat.ts`). Its settings
documents load in the order wellknown, global, explicit and direct files, the project's, then
`OPENCODE_CONFIG_CONTENT` last, and a later document's provider settings win key by key
(`core/src/config.ts`, `load`; `provider.ts`, `mergeOverlay`). Four runtime tests,
2026-10-08, with Ollama 0.34.4 and the pinned binary:

1. **What OpenCode asks.** OpenCode pointed straight at a stand-in Ollama that records every
   request: a session's first turn, with a shell command approved in it, made two chats, never two
   at once; no title request, since Halcyonic names each session itself (OpenCode asks for a title
   beside the turn for a session without one, `core/src/session/runner/llm.ts`), and no child
   session, since Halcyonic denies the subagent tool. Two sessions side by side,
   each in a slow reply, held two at once. Across all of it the stand-in saw only the three requests
   (tracked e2e test). So the gate allows one chat at a time for each turn running or starting on
   the server, and at least one; a tracked test shows two tasks side by side both get replies.
2. **What the gate refuses.** Through the adapter, an approved command's requests to the gate: pull,
   push, create, delete, copy, generate, `/api/chat`, embed, ps, version, `/v1/models` and the list
   with a query, 404; chats naming a cloud model, a remote one and one not listed, and details of the
   remote one, 403; a chat naming the local model and then, as `Model`, a cloud one, 400; a chat with
   the model on this Mac, 200; Ollama's own port, no connection. The gate's list showed the local
   model alone, and the stand-in received nothing refused (tracked e2e test). The gate's own unit
   tests add a body over 8 MiB, one not JSON, one nested too deep, keys differing in case anywhere
   (the Kelvin sign and long s too), fields OpenCode does not send left behind, a request's own
   headers left behind, a closed chat closing Ollama's reply, the cap, a waiting chat that leaves,
   too many waiting and a wait too long, a request leaving while it waits to be read, a connection
   sending nothing or sending its request too slowly while a reply streams on, a body dripping in a
   byte at a time, a request sent behind an open reply on the same connection, and requests still
   waiting when the gate closes; and a tracked test shows the gate closed once its server is.
3. **Settings cannot move it.** With OpenCode's global settings naming another Ollama address
   (port 9, where nothing answers), the turn still reached the stand-in, through the gate (tracked
   e2e test). The project's own file named it too, but Halcyonic never loads a project's settings,
   so its rank below `OPENCODE_CONFIG_CONTENT` is read in source only.
4. **The real Ollama** (0.34.4, nothing loaded, its sockets sampled every 100 ms by `lsof`): through
   the gate it listed its 10 models, none remote or cloud, and gave a model's details; a chat naming
   a missing model was refused at the gate, and asked directly Ollama answered 404 "not found" and
   pulled nothing. No Ollama socket beyond loopback in 19 samples, and nothing loaded after
   (private probe).

## ripgrep

OpenCode 2.0.18 searches files with the first `rg` on its PATH, then one in its cache's `bin`, and
otherwise downloads ripgrep 15.1.0 from GitHub into that cache (`core/src/ripgrep/binary.ts`).
Inside the sandbox the download is refused, and the server's own cache starts empty, so without
`rg` on the PATH OpenCode cannot search files. The control plane warns at startup when that is so,
and mac-setup says it (source reading; on this Mac `rg` is on the PATH, so the runs above did not
reach the download; found by review, 2026-10-08).

## Not verified

- Whether OpenCode's server ever connects to itself on its own port; the port is allowed in case.
- A command that outlives its server keeps a profile naming the gate's port after the gate has
  closed, so a program that later listens on that port is reachable from it, as with the server's
  own port.
- What Ollama does with every field of a chat with a local model, such as an image inside a
  message: known from reading only, not run, so "nothing leaves the Mac through Ollama" rests on
  that reading for those fields.
- A real model's turns through the gate; the stand-in streams as Ollama's OpenAI-compatible
  endpoint does, and the real Ollama was asked only for its list and a model's details.
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
