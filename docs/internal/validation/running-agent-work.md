# Connecting agent work started outside Halcyonic

- **Question:** Could Halcyonic truthfully connect agent work a person started outside it (their own
  OpenCode, Codex or Claude Code session, from a terminal or another app), using only each runtime's
  structured surfaces and never its terminal UI? What could it do with that work (watch, answer
  approvals and questions, stop), what would two controllers do to each other, and at what cost?
- **Date:** 2026-10-02.
- **Versions:** OpenCode 2.0.18 and Codex 0.157.0, the pinned binaries
  ([local-models.md](local-models.md)); Codex source read at tag `rust-v0.157.0`; the Claude Agent
  SDK 0.3.283 and the Claude Code CLI 2.1.283 it bundles.
- **Method:** `--help` of the pinned binaries; OpenCode's served `/openapi.json`; Codex's
  `app-server generate-json-schema`; the SDK's `sdk.d.ts`; the official pages below, read on
  2026-10-02. Probes ran on 127.0.0.1 in scratch homes with no provider reachable and no sign-in, and
  no model turn ran anywhere. Two read-only listings touched the owner's real data and printed
  counts only: Codex `thread/list` on the owner's `CODEX_HOME` (an app-server writes to its home
  whenever it runs), and `claude agents --json`.
- **Status:** Research only. Nothing is attached and no capability is declared; the decision is in
  [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md). Labels below: **fact** (seen in a source or a
  probe), **inference**, **open**, **risk**.

## The short answer

| | OpenCode 2.0.18 | Codex 0.157.0 | Claude Code (SDK 0.3.283) |
| --- | --- | --- | --- |
| Where a person's work runs | One shared background service per user, which the TUI and CLI use by default | An app-server inside the TUI unless a shared daemon runs; the desktop app runs its own private one over stdio | One CLI process per session; background sessions under a supervisor |
| List sessions started elsewhere, with their folder | Yes, from any server on the same data home (`location.directory`) | Yes, `thread/list` from any app-server on the same `CODEX_HOME` (`cwd`) | Yes, `listSessions` from disk (`cwd`); `claude agents --json` for live ones |
| Live events | Only from the server that runs the session | Only from the process that runs it, after `thread/resume` joins it | None; a status to poll |
| Answer an approval or question | Yes, through the shared service; the first answer wins | Yes, through the shared daemon (source only); the first answer wins | No |
| Stop | Yes, through the shared service | Yes, through the shared daemon | No (`claude stop` ends a background session whole) |
| A second process taking over | Reads the session, cannot see or answer its pending requests | Refused: the thread "already has an active writer" | Allowed and harmful: both write into one transcript |
| What could be declared | Watch, answer, stop and instruct, for sessions on the shared service reached by pairing; list only otherwise | List and history now; watch, answer, stop on a version-matched shared daemon later | Watch only, as reported status |

## OpenCode 2.0.18

**Facts**

- The shared service is the default: the pinned binary describes `--standalone` as running "with a
  private server instead of the background service", and
  [the CLI docs](https://opencode.ai/v2/docs/cli) say every local client connects to one shared
  background server per user account, which owns sessions, permissions and tool execution.
- Clients find it through `$XDG_STATE_HOME/opencode/service.json` (mode 0600) holding its id,
  version, loopback URL, process id and password; `serve --service` wrote it in a scratch home.
- Pairing is the documented way for another app to connect. `opencode pair` prints one-use links
  that expire in five minutes; redeeming one (`GET /auth/connect/{code}`) returned a session token
  that authorized `GET /api/session`; a second redemption and a request without it got 401. The docs
  say pairing requires the shared service.
- `GET /api/session` lists sessions with `location.directory`, title, times, outcome, model, cost and
  tokens, filterable by directory. A session made on the scratch service was listed by a second
  `serve` on the same data home: the database is shared.
- Live state is not. A pending permission (made with `POST /api/session/{id}/permission`, no model)
  appeared on the service and its event stream, and on nothing of the second server; replying there
  got 404, as did a pending form, and `interrupt` there answered `{interrupted: false}` (it acts on
  "active execution owned by this OpenCode process").
- Two clients of one server do not conflict: the first permission reply got 204 and the second 404;
  the first form reply 204 and the second 409 `FormAlreadySettledError`.
- Halcyonic's own OpenCode server already shares the person's data home: the adapter keeps HOME and
  the XDG directories (`packages/integrations/opencode/src/server.ts`), so each side can already
  list the other's sessions.

**Inferences:** connected to the person's service by pairing, the existing adapter calls (events,
permission and form replies, prompts, interrupt) would work unchanged on their sessions. A session
on `--standalone` or on a `serve` the person started is reachable only with its URL and password;
otherwise it can only be listed. Acting on a session through any server other than the one running
it could start a second execution in another process (untested: it needs a model turn).

**Open:** how long a pairing token lives and whether the person can revoke it; whether the TUI
clears its prompt when another client answers; whether the service updates itself past 2.0.18.

**Risks:** the adapter refuses anything but 2.0.18 and v2 is still experimental; the global event
stream carries every session's messages, not only connected ones; reading `service.json` without
the person's act would take control without consent, so only pairing is acceptable.

**Cost:** a few days: a mode that connects to an existing server instead of launching one, a pairing
step, filtering events to connected sessions, no watchdog, tests and a record. No model quota for
watching or answering; instructions spend the person's own provider setup.

## Codex 0.157.0

**Facts**

- A TUI uses a local daemon when its control socket answers
  (`CODEX_HOME/app-server-control/app-server-control.sock`, mode 0600 in a 0700 directory) and an
  embedded app-server otherwise (`codex-rs/tui/src/lib.rs`, `app_server_target_for_launch`).
- The daemon is experimental: its README says its lifecycle contract may change, managed daemons
  check for updates after five minutes and then hourly, and a restart for an update may interrupt
  active or queued work. [The app-server docs](https://learn.chatgpt.com/docs/app-server) call the
  app-server and its WebSocket transport experimental and not for production. No daemon ran on the
  owner's Mac on 2026-10-02.
- `thread/list` filters by `cwd`, source kinds and search terms, with `useStateDbOnly` to avoid
  metadata repair writes; a `Thread` carries `cwd`, `source`, `cliVersion`, `status`, `name` and
  `preview` (usually the first user message). On the owner's home every listed thread had a `cwd`
  and a `preview`, and every one read `notLoaded`, including one updated seconds earlier.
- In a scratch probe with no turn, a thread one client started over `ws://127.0.0.1` reached a second
  client of the same process, which joined it with `thread/resume`. A second process on the same
  `CODEX_HOME` saw no loaded thread, read it as `notLoaded`, was refused `thread/resume` ("already has
  an active writer") and got "thread not found" for `turn/interrupt`.
- Joining keeps the thread's own approval policy and sandbox and ignores the joiner's
  (`thread_processor.rs`); pending requests are replayed to a client that joins and the first answer
  takes each (`outgoing_message.rs`).

**Inferences:** today Halcyonic can list threads and read their history after the fact; `notLoaded`
says only that Halcyonic's process has not loaded the thread, not whether it runs. With the person's
daemon running, Halcyonic could connect to its socket, join a thread, and watch, answer, steer and
interrupt it.

**Open:** whether the desktop app ever uses the shared daemon; how to pin a version against a daemon
that updates itself; whether any stable field tells a running thread apart in the listing (none
found).

**Risks:** the owner's threads already span several CLI versions; previews and history are the
person's text; the control socket is guarded by file mode alone and turns run on the person's sign-in
(whether a third party may drive Codex with it is already open); every app-server writes to
`CODEX_HOME`.

**Cost:** listing and history are cheap, since Halcyonic already runs an app-server on the person's
`CODEX_HOME`. Joining the daemon is moderate to high: a client over the Unix socket, version
handling, and executions whose policy Halcyonic did not choose (the adapter refuses those today).
`answer_question` would apply only to threads started with the question feature on, which Halcyonic
cannot set for someone else's thread ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)).

## Claude Code (Agent SDK 0.3.283)

**Facts**

- `listSessions`, `getSessionInfo` and `getSessionMessages` (`sdk.d.ts`) read sessions from disk
  with `cwd`, summary, first prompt, branch and times, and no running state. The transcript format
  is internal and changes between versions ([sessions](https://code.claude.com/docs/en/sessions)).
- [Agent view](https://code.claude.com/docs/en/agent-view) names `claude agents --json` as the
  supported way to read session state from outside: folder, kind, start time, process id and status
  (busy, waiting or idle) while alive, what it waits for, and the session id.
- Resuming one session in two processes is not guarded: the docs say messages from both interleave
  into one transcript. The SDK's `resume`, `continue` and `forkSession` start a new process on the
  stored transcript.
- Nothing documented answers a prompt or sends a message to an interactive session from outside.
  `claude --resume <id> "prompt"` reaches a running background session only from a terminal; piped,
  it sends nothing and exits 1. The SDK's multi-client options apply only to remote sessions through
  claude.ai. Hooks are out: Halcyonic installs no global hooks ([INTEGRATIONS.md](../architecture/INTEGRATIONS.md)).

**Inference:** Halcyonic could show a person's Claude Code sessions with their folder, a polled
status that the CLI reports, and history after the fact. It cannot show what the agent asks or
answer it, and a resume of a live session would add a second agent writing into the person's
transcript, on Halcyonic's key.

**Open:** agent view says interactive sessions in other terminals appear only once backgrounded,
yet the bundled 2.1.283 listed interactive ones; whether `agents --json` reaches the network or
starts the supervisor.

**Cost:** low, and no quota. Status would be `reported`, never `observed`.

## What it means for Halcyonic

- Every execution today is one Halcyonic started. Connecting outside work needs an execution that
  was found, not started, with only the capabilities of the process running it, and a status that
  stays `unknown` where discovery alone cannot tell (Codex `notLoaded`, OpenCode sessions on another
  server, Claude Code between polls). That is a domain and contract change, so it needs an ADR first.
- Listing alone would put the person's own text (titles, first prompts, previews) on every paired
  device, so even list only needs a consent design.
- The cheapest truthful first step is OpenCode through pairing with the shared service, which gives
  the most (watch, answer, stop, instruct) with the least emulation. Codex waits for a daemon that
  can be pinned; Claude Code stays watch only.

## Sources

- https://opencode.ai/v2/docs/cli and https://opencode.ai/v2/docs/cli/commands, read 2026-10-02.
- https://learn.chatgpt.com/docs/app-server, read 2026-10-02; Codex source at `rust-v0.157.0`
  (`codex-rs/tui/src/lib.rs`, `transport/mod.rs`, `unix_socket.rs`, `thread_processor.rs`,
  `outgoing_message.rs`, the daemon README).
- https://code.claude.com/docs/en/cli-reference, /sessions and /agent-view, read 2026-10-02;
  `sdk.d.ts` of `@anthropic-ai/claude-agent-sdk` 0.3.283.
