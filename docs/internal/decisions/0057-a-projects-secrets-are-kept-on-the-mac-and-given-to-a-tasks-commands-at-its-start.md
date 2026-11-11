# ADR 0057: A project's secrets are kept on the Mac and given to a task's commands at its start

- Status: Accepted
- Date: 2026-11-11

## Context

A game or app an agent builds often needs a key: a payment provider's test key, a database's URL with its
password, a map service's token. Until now the only way to give one to an agent was to write it into the
project's folder, where it lands in every copy, saved copy, check, share and push, or to set it in
Halcyonic's own `HALCYONIC_AGENT_ENV`, which every agent of every project gets.

What was true on 2026-11-11, before this decision:

- A start (`StartExecutionRequest`) carried no environment. Each adapter's environment was fixed when the
  adapter was made.
- **Claude Code** launches one process per session with the SDK's `env`. Its tool commands inherit that
  process's environment ([claude-code-capabilities.md](../validation/claude-code-capabilities.md)).
- **OpenCode** runs one server per folder (BOX-3), and the adapter already gives each session its own
  command environment with `PUT /api/session/:id/environment`, which replaces what that session's shell
  commands get, in memory only ([opencode-permissions.md](../validation/opencode-permissions.md)).
  No server restart is needed for a session to get variables of its own.
- **Codex** 0.157.0: `thread/start` and `thread/resume` take `config`, and
  `shell_environment_policy.set` there reaches that thread's commands alone, on a shared server, and is not
  kept in Codex's home. `turn/start` takes no `config`. A runtime test by CORE on the pinned binary and
  the fake provider ([project-secrets.md](../validation/project-secrets.md)).
- `command.accepted` journals the whole command envelope, and the journal is replayed to every paired
  device. A value carried in a command would be in the journal for good.

## Decision

1. **A value lives in one place.** Each secret is a file of its own, `<data dir>/secrets/<project>/<name>`,
   mode 600, in a folder of mode 700. It is never journaled, logged, returned by a read, drawn on any
   screen, written into a project's folder, or given to a copy, a saved copy, a check, a preview, a share,
   a send to GitHub or a publish, all of which read the project's folder or a check's build, never the data
   folder.
2. **Names only cross the contracts.** `project.secret.set` and `project.secret.remove` carry the project
   and the name; `project.secret.stored` (with whether it replaced one) and `project.secret.removed` record
   them; `ProjectView.secrets` lists the names, sorted. A name is capitals, digits and `_`, at most 64
   characters, and never one the Mac or an agent runs by (`PATH`, `HOME`, `TMPDIR`, the shell's own, and any
   starting `HALCYONIC_`, `OPENCODE_`, `CODEX_`, `CLAUDE_`, `ANTHROPIC_`, `OLLAMA_`, `XDG_`, `LC_`, `DYLD_`,
   `LD_`, `GIT_`, `NODE_`, `NPM_CONFIG_`, `PYTHON`, `SANDBOX_`, `BUN_`), nor one that changes where a program
   connects or what it trusts (`HTTP_PROXY`, `HTTPS_PROXY`, `ALL_PROXY`, `NO_PROXY`, `SSL_CERT_FILE`,
   `SSL_CERT_DIR`, `REQUESTS_CA_BUNDLE`, `CURL_CA_BUNDLE`), since Claude Code gets the secrets in its whole
   process environment (AUDIT3). A project keeps at most 32. Removing one is low consequence, from any
   client, a paired device too: it gives later tasks nothing and leaks nothing, so it needs no review.
3. **A value travels only in its own request, write-only.** Two ways, one path inside: the Mac's setup page
   (ADR 0053), as the person's local press, and `POST /api/projects/:id/secrets` (`ProjectSecretSetRequest`:
   the command's id, its client, the name and the value), from a client on this Mac with the access token or
   a paired device over its pinned TLS (amended 2026-11-11, the headset's write-only route). Either stages
   the value in the store under the command's id, then submits the name-only command as its caller, the
   device for a device. The command's dispatch puts the staged value in place and records the event; a set
   with no value staged for exactly that command, as a bare command over the realtime protocol, fails ("A
   secret is set with its value in its own request, so this command alone sets nothing."), and a set
   refused or failed leaves nothing kept. A retried command id is the same command: the first value stays.
   No route gives a value back, and a refusal never repeats it. The page's field is cleared as it is sent;
   its route takes up to 8 KiB of value, while its other routes keep the page's 1 KiB.
4. **Redaction knows a value from the moment it is staged.** Every value kept or staged is among the held
   secrets each runtime's text is cleaned of, shown as `[redacted: NAME]`. So is every value replaced or
   removed while the control plane runs, in memory only, never on disk: a task started before still holds
   it and can print it (AUDIT3). After a restart only the values kept are held; a re-attached task is given
   those, though an old one may remain in its runtime's own history. A value shorter than 8
   characters is refused, since redaction leaves values that short alone.
5. **Given at a start, declared per runtime, never emulated.** `RuntimeDescriptor.secret_environment`, true
   only where a runtime test shows a command reading one: for OpenCode and Codex, the end to end tests on the
   pinned binaries; for Claude Code, its process holding it through the real SDK, its commands inheriting it
   by Claude Code's own behaviour, read and not run. A start on such a runtime gets the project's
   secrets as they are then (`StartExecutionRequest.environment`), and `execution.created` records their
   names (`secret_names`). A re-attach after a restart gets them again, as they are then. A runtime that
   does not declare it gets none. An adapter never lets a secret replace a variable of its own.
   - Claude Code: the session's process environment.
   - OpenCode: the session's own command environment, set before its first prompt and again at a
     re-attach. No server restarts for secrets, and no running turn is touched.
   - Codex: `shell_environment_policy.set.<NAME>` in the thread's `config`, at its start and every resume. A
     name holding `KEY`, `SECRET` or `TOKEN`, which Codex's default excludes drop from what a command inherits,
     still reaches it when set this way (shown with `PROJECT_TEST_KEY`).
6. **When it takes effect.** A task gets the project's secrets when it starts; one already running keeps
   what it started with, until a restart re-attaches it. The page says so, and says plainly that an agent
   given a key can use it, print it and send it wherever its commands reach. It names the agents that start
   without the secrets, and in a row with the alert mark the ones that run outside the sandbox on a folder
   the person connected. A refusal is said in that project's card, and Remove takes a second press (LINK's
   critic, decided by the lead on 2026-11-11).
7. **The store follows the journal.** At a start, after the journal is folded, a value whose name the
   journal does not list, and anything a press left staged, is deleted.

## Alternatives considered

- **The value in the command, kept out of the journal by a special case.** Every command is journaled
  whole and replayed to devices; a special case there is one mistake from a key in every headset's copy.
- **Secrets in the project's folder (`.env`).** Copies, checks, shares and pushes would all have to filter
  it, and an agent could commit it. The folder's secret-shaped files are already kept out of those.
- **Restarting OpenCode's folder server with the secrets in its environment.** Verified unnecessary: each
  session already has an environment of its own. A restart would end every session at rest there.
- **Setting a changed secret at the next turn of a running task.** Codex takes `config` only per thread,
  and a turn takes none, so the three runtimes would differ. One rule, at a start, is what the page says.
- **Land Claude Code alone first.** Refused by the lead (2026-11-11): the agents people run here are
  OpenCode and Codex on a local model.

## Consequences

- A person gives a task a key without writing it into the project, and every way work leaves the Mac
  stays free of it.
- A value an agent prints is in its runtime's own records (Codex's rollout, OpenCode's session) as all
  command output is, and in what it sends to its model. Redaction takes it out of what Halcyonic journals
  and shows; the runtime's own copies are not Halcyonic's to clean. The page tells the person to give
  agents test keys.
- **An agent outside Halcyonic's sandbox can read every project's secrets.** Claude Code on a folder the
  person connected (not one made from a starter, ADR 0048) runs with the person's own access, so its
  commands can read `<data dir>/secrets` of any project, as they could already read the rest of the data
  folder. Halcyonic hands each task only its own project's secrets; it cannot stop such a run reading the
  rest. The page says so whenever such an agent is set up here (`unconfined_agent`). Keeping values where an
  unconfined run cannot simply read them (the Keychain) is the way to close it, and is not built.
- **After a restart, an old value is no longer known.** A task re-attached then is given the current values,
  but its runtime's own history (OpenCode's session, Codex's rollout) may hold an old one it printed before,
  and a repeat of that one would be journaled whole (AUDIT3's reread S2). Only the values kept are held
  after a restart, since retired ones are never written to disk.
- Neither OpenCode's nor Codex's own files hold a value given this way after the end to end runs (the
  runtimes' sandbox folders searched, with the variable's name as the control).
- Admission and the store agree only through the journal. A crash between the store's rename and the
  event's record leaves a value the next start deletes; the person sets it again.
- Revisit when a runtime can set variables per turn, or when the headset gets its write-only route.
