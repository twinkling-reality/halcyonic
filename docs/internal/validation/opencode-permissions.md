# OpenCode permission rules and network reach

- **Question:** Should Halcyonic give each OpenCode session permission rules of its own, so that a
  person is asked before shell commands and the agent cannot reach the network with a local model,
  as Codex now runs ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md))? How are such rules set,
  what do they reach, how do they combine with the person's configuration, does Halcyonic's
  approval path already carry them, what else reaches the network, and what would it cost?
- **Date:** 2026-10-07.
- **Versions:** `@opencode/cli` 2.0.18, darwin arm64, the pinned binary; source at
  `anomalyco/opencode` tag `v2.0.18` (commit `cd9a14a6b688`, whose `packages/cli/package.json` is
  2.0.18). Paths below are under `packages/` at that tag.
- **Method:** The source at the tag, read for the permission system and for every network path,
  with the v2 documentation at opencode.ai/v2/docs where it helps. Then runtime tests of the pinned
  binary (`opencode serve`) in the end to end suite's sandbox (private HOME, XDG and TMPDIR, a
  recording proxy trap, `OPENCODE_DISABLE_MODELS_FETCH` and `OPENCODE_DISABLE_AUTOUPDATE` as the
  adapter sets them), against a scripted OpenAI-compatible provider on loopback that calls whatever
  tool a message names, driven over OpenCode's v2 HTTP API. No model, no credentials, no hosted
  call. The probe script stays outside the repository.
- **Status:** Runtime verified for scenarios S0 to S6 below. Marked "source" where only read in
  source; marked "inferred" where neither.

## How a session's rules are set, and what they reach

- **Set at creation, or replaced later.** `POST /api/session` takes `permissions`, a list of
  `{action, resource, effect}` with `effect` one of `allow`, `ask`, `deny`
  (`protocol/src/groups/session.ts:220-229`, `schema/src/permission.ts:55-66`). `PATCH
  /api/session/:id` replaces the whole list (`core/src/session/projector.ts:584-589`). In a
  resource, `*` matches anything; the last matching rule wins, and no match asks
  (`core/src/permission.ts:87-97`). Source and runtime (S1).
- **The tools and their actions** (source): `shell` (each parsed command), `edit` (edit, write and
  patch), `read`, `glob`, `grep`, `webfetch` (the URL), `websearch` (the query), `subagent`, `skill`,
  `question`, each MCP tool as `<server>_<tool>`, and `external_directory` for any path outside
  the project. No todo, LSP or codesearch tool exists in 2.0.18.
- **A tool every rule denies is not offered at all.** With the session denying `webfetch`,
  `websearch` and `execute`, the model was offered `edit, glob, grep, question, read, shell,
  skill, subagent, write`; without rules, also `webfetch, websearch, execute`. A webfetch call then
  failed with "No tool named "webfetch" is currently available", and nothing reached the proxy trap
  (S3).
- **Code Mode's `execute` tool reaches the network unasked.** It is offered by default and runs
  JavaScript with a `fetch` (`core/src/codemode/web.ts:13-19`, `core/src/codemode/tool.ts:64`) that
  no permission check covers. Runtime: with the session asking before every shell command, an
  `execute` call whose code POSTs to a loopback listener reached it, and no ask was raised (S6). The
  v2 documentation says Code Mode has no `fetch`; the binary has one. Denying `execute` for every
  resource removes the tool (`core/src/tool.ts:238`; S3). Halcyonic's own OpenCode settings, which
  `pnpm mac-setup local-model` writes, ask before shell and deny `webfetch` and `websearch`, but
  do not deny `execute`, so today a local model working through them can send anything anywhere
  with Code Mode.
- **Other ungated tools** (source): `opencode.session_move` moves a session to any directory
  without asking (`core/src/tool/plugin/opencode.ts:110-131`), after which that directory counts as
  inside the project (inferred); `opencode.session_rename` and `opencode.models`; tools a plugin
  adds are gated only if the plugin asks. These run within Code Mode's `opencode` namespace, which
  denying `execute` also hides (source).
- **Subagents copy the rules but ask from their own session.** The subagent tool creates a child
  session that copies the parent's rules (`core/src/tool/plugin/subagent.ts:186-192`,
  `core/src/session.ts:273-276`). Runtime: a subagent asked to run a shell command raised its ask
  on the child session; the parent listed none (S5). Halcyonic's adapter routes events by the
  session it created and drops the rest (`opencode-runtime.ts`, `#dispatch`), so a child's ask
  never reaches the headset and the subagent waits until the person stops the task. This is so
  today, with Halcyonic's own settings asking before shell.

## How session rules combine with the person's configuration

The rules are combined in this order, the last match winning (source,
`core/src/permission.ts:148-188` and `config/plugin/agent.ts:84-123`): every agent's base rules
(`*` allowed, outside directories and `.env` files asked); the built-in agent's own; the top-level
`permissions` of every configuration file, global first, then the project's; each agent's own
`permissions`; **the session's rules**; then, unless a rule so far denies, every saved "always" as
an allow; then plugin policy hooks (an organisation's `experimental.policies` deny is final).

- **The session's rules outrank the person's configuration.** Runtime: with the configuration
  denying `shell` and the session asking, a shell call asked (S2). So a person who denied
  something in their own settings would be asked instead under a session rule that asks; one that
  denies stays denied whatever the configuration says.
- **A saved "always" outruns a session's ask, not its deny.** Answering "always" stores an allow
  for the project in OpenCode's database (`core/src/permission/saved.ts:63-78`): for a shell
  command, its first word and `*` (`echo *` for `echo s4a`), and for `edit`, `read`, `webfetch`,
  `glob` or `grep`, every resource. Runtime: after one "always" in one session, a later session in
  the same project with a rule asking before shell ran `echo s4b` without asking; a session
  denying `shell` was not offered the tool (S4). Halcyonic only ever answers "once", but its
  OpenCode server inherits the person's `XDG_DATA_HOME`, so it shares their OpenCode database: an
  "always" the person gave in their own OpenCode app or terminal, in the same project, also skips
  Halcyonic's ask. The saved rules are listed at `GET /api/permission/saved` and removed with
  `DELETE /api/permission/saved/:id` (source).
- **Clients that approve by themselves** (source): the CLI's `--auto` and the hidden `--yolo` and
  `--dangerously-skip-permissions`, the terminal UI's "autoaccept", and the web and desktop app's
  auto-approve, which answers every session on the server it is connected to. Halcyonic runs a
  server of its own on a port and password of its own, so those clients do not reach it (inferred
  from how it is launched).

## An "ask" on the wire, and Halcyonic's path for it

- `permission.asked` on `GET /api/event` carries `{id: "per_…", sessionID, action, resources,
  save, metadata, source}` (`schema/src/permission.ts:25-44`); it is answered with `POST
  /api/session/:sessionID/permission/:requestID/reply` and `{decision: "once" | "always" |
  "reject", message?}`, which answers 204, then `permission.replied` (source; S1: the ask for `echo
  s1-ran` listed `resources: ["echo s1-ran"]`, `save: ["echo *"]`, and "once" ran it).
- A reject also rejects every other pending request of the session; without a message the turn is
  interrupted, with one the model carries on (source; recorded before in
  [opencode-capabilities.md](opencode-capabilities.md)).
- **Halcyonic already carries it end to end** for the parent session: the adapter turns
  `permission.asked` into `runtime.approval.requested` with the action as the tool and the
  resources as the summary, answers the headset's Yes with "once" and No with "reject", and settles
  on `permission.replied`; the OpenCode end to end suite runs this with an asking rule from the
  configuration file. A rule set on the session raises the same event (S1), so nothing more is
  needed there; the gap is the child sessions above.

## What else reaches the network with a local model

From the source, every path in `opencode serve` 2.0.18 with Ollama on loopback, and its switch:

| Path | When | Where | How it stays off |
| --- | --- | --- | --- |
| Model catalog | At launch and every 5 minutes | models.opencode.ai | `OPENCODE_DISABLE_MODELS_FETCH=true`, set by the adapter |
| ripgrep download | No `rg` on the PATH | github.com | `rg` on the PATH, as mac-setup checks |
| Update check | The terminal UI's default command only; `serve` never runs it | opencode.ai, npm, Homebrew | Not reached; `OPENCODE_DISABLE_AUTOUPDATE` also set by the adapter |
| `webfetch` | The model asks for a URL | Any host | Session rule `webfetch` deny |
| `websearch` | The model searches | Exa and four other search services, no key needed | Session rule `websearch` deny, or `websearch: false` |
| Code Mode `fetch` | The model runs code | Any host | Session rule `execute` deny (S6) |
| `shell` | A command that reaches the network | Any host | Asked before each command; the person decides |
| OpenCode Zen | Offered without a key; the default model when none is chosen | opencode.ai/zen | Halcyonic always names the model; listed as remote ([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)) |
| Telemetry | Only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set | That endpoint | Not passed; the adapter's environment is an allowlist |
| OpenCode Console | Only with a stored login or `OPENCODE_API_KEY`; then it fetches configuration, policies, MCP servers and search every minute | opencode.ai | The key is not inherited; a login the person stored in their own OpenCode is shared, since Halcyonic's server uses their data folder (inferred) |
| Formatters, package plugins, MCP servers, git references, remote skills | Only when configured | npm, configured hosts | Not configured by Halcyonic; the person's own configuration may |
| Other hosted providers | A stored connection or a vendor key in the environment | Their APIs | Keys are not inherited unless passed on purpose; stored connections are shared through the data folder, as above (inferred), and their models are listed as remote |

Session sharing, LSP downloads and codesearch do not exist in 2.0.18; local model discovery polls
only loopback ports (source).

## The cost to a person

- **Shell commands are asked one by one.** Reading, searching and listing need no press: OpenCode
  has its own `read`, `glob` and `grep` tools. Builds, tests, git and package managers each wait
  for a press on the headset. "Once" is the only answer Halcyonic gives, so the same command asks
  again next time.
- **Edits:** asking before them too would make every file change a press; allowing them keeps
  OpenCode's default and leaves them visible in the task's tool activity.
- **Lost:** web fetches and searches; Code Mode, and the `opencode` namespace tools it carries
  (moving or renaming a session, listing models); subagents, if denied until the adapter follows
  child sessions.
- **For a person with their own OpenCode rules:** a session rule that asks turns their deny into an
  ask, and one that denies overrides their allow. A person who allowed shell commands would be
  asked; one who denied webfetch loses nothing.
- Nothing becomes slower otherwise: rules are evaluated in the server, and a denied tool is simply
  not offered.

## What Halcyonic does, and what moving the data folder would cost (2026-10-07)

- **Built, 2026-10-07, after the security review:** every session the adapter creates carries
  rules that deny `execute`, `webfetch`, `websearch` and `subagent`, and deny `edit` on every path
  OpenCode reads its configuration from (`sessionPermissions`, sent on `POST /api/session`; never
  replaced, so no `PATCH`).
  - **`subagent`:** its `model` input ("providerID/modelID") is resolved against every model
    OpenCode lists (`core/src/tool/plugin/subagent.ts:36-39`, `:75-87`, `:168`, `:183`), keyless
    OpenCode Zen and any provider stored in the data folder among them, so a repository file could
    have the model delegate its code and files to a hosted model. Denying it also ends the child
    sessions whose asks the adapter never sees.
  - **Configuration paths:** in the session's folder and every folder above it, OpenCode reads
    `.opencode` (whose plugins load as server code), `.claude`, `.agents`, `opencode.json` and
    `opencode.jsonc`, and `~/.claude`, `~/.agents` and its global folder
    (`core/src/config/discovery.ts:23-84`; `OPENCODE_CONFIG_DIR`, else
    `$XDG_CONFIG_HOME/opencode`, else `~/.config/opencode`, `util/src/global-roots.ts`), watching
    them even before they exist and reloading MCP servers on a change (`config/watch.ts:8-39`,
    `config/plugin/mcp.ts:17-57`). Edits are not asked, so the model could write a plugin. The
    rules deny `edit` on `.opencode/*`, `*/.opencode/*`, `.claude/*`, `*/.claude/*`, `.agents/*`,
    `*/.agents/*`, `opencode.json*`, `*/opencode.json*`, the global folder and an `OPENCODE_CONFIG`
    file; `*` matches `/` too (`core/src/util/wildcard.ts`). **A shell command can still write
    those paths** unless shell commands ask, which is the owner's question.
  - **The end to end suite checks:** that none of the denied tools is offered to the model and
    that the session, read back, holds the rules; that writes to `.opencode/plugin/planted.ts`,
    `opencode.json`, `sub/.opencode/plugins/p.ts` and `.agents/skills/x/SKILL.md` are refused while
    `notes.txt` is written; that Code Mode's `fetch` reaches a loopback listener from a session
    made without the rules on the same server and never from Halcyonic's; and the network probe:
    nothing beyond loopback from the OpenCode server's tree, or any process of the binary, through
    startup, a minute idle and a full run, with the connection to the scripted provider seen as
    the positive control, and, as the negative control, a server launched without the catalog
    switch seen reaching beyond loopback when a folder is loaded (skipped when
    `models.opencode.ai` does not resolve).
- **The server's password is not secret from what the agent runs** (runtime, 2026-10-07, a shell
  call printing only a length and a count): the adapter passes it in `OPENCODE_PASSWORD`, and
  OpenCode deletes it only in stdio mode (`cli/src/server-process.ts:72-77`), so a command saw a
  length of 43; `ps -wwE` on the server's process showed it too, since any process of the same
  user can read another's starting environment. With it, a command could use the loopback API to
  replace its session's rules, answer its own asks, or open a session without rules. So:
  - every session gets an environment of its own without the password (`PUT
    /api/session/:id/environment`, which replaces what shell commands get,
    `protocol/src/groups/session.ts:871-880`, `core/src/shell.ts:268-272`; it lives in memory, and
    sessions do not outlive a server restart). A command then saw a length of 0, but `ps -wwE`
    still showed it: defence in depth only;
  - the adapter stops a task when something else changes what it may do: a `session.permissions`
    event on its session (it never sets rules after creation), or an approval answered "once" or
    "always" that it did not send (a reject, which also settles the session's other requests,
    never lets anything run). A running turn fails with `runtime_tampered`; a task at rest is
    reported lost. A `session.created` on its server that no creation of the adapter's claims
    stops every task there and the server. The end to end suite makes each change through the API
    and checks the task stops. The adapter reacts after a change; it does not prevent it.
  - **Nothing internal trips these checks in Halcyonic's setup** (source): a session is created
    only by the subagent tool (`core/src/tool/plugin/subagent.ts:188`, denied), a configured
    command whose agent is a subagent (`core/src/config/plugin/command.ts:101`), which runs only
    through `POST /api/session/:id/command`, never called by the adapter, a plugin through the
    plugin host (`core/src/plugin/host.ts:521`), which no built-in plugin does, and the API's own
    create, fork and import. Compaction (`core/src/session/compaction.ts`) and title generation
    create none; an agent switch emits `session.agent.selected`. `session.permissions` comes only
    from `PATCH` (`server/src/handlers/session.ts:275`) and the plugin host
    (`core/src/plugin/host.ts:541`). So only an API call with the password, or a plugin the
    person configured, stops a task. Runtime, 2026-10-08: a task through the adapter, its tamper
    checks live, on `qwen3:4b-instruct` with OpenCode told the model has 6,000 tokens of context,
    read six files of about 1,300 tokens each, then had an explicit `POST /api/session/:id/compact`
    and a second turn: both turns completed with tool calls, the session's messages held ten
    compaction marks (one explicit compact alone left three in an earlier run), and nothing was
    stopped as tampered. The model was unloaded afterwards.
  - **A race seen along the way:** a start right after launch failed its first turn with
    `provider_no_route` ("Unsupported package for ollama/qwen3:4b-instruct") when the
    configuration named the model with its limits, as Halcyonic's own OpenCode settings do: the
    model is listed from the configuration before Ollama's discovery gives it a package, so the
    adapter's wait for a listed model can pass too early. Not fixed here
    ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
  - Stdio mode would keep the password out of the server's environment altogether; it would move
    the adapter off HTTP ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- **What the shared data folder still lets through:** Halcyonic's OpenCode uses the person's data
  folder, so a saved "always" from the person's own OpenCode for the project (for example `curl *`
  for a shell command) skips a session's ask, a stored OpenCode Console login makes the server
  fetch configuration, policies, MCP servers and search from opencode.ai every minute, and stored
  providers are listed (as remote). The session's denies still hold over a saved "always".
- **Moving OpenCode's data folder** (not done): Salidium reads OpenCode's sessions straight from
  `$XDG_DATA_HOME/opencode/opencode.db`, `XDG_DATA_HOME` if absolute, else
  `~/.local/share` (Salidium `abb7a93`, `packages/adapters/opencode/src/storeSource.ts:58-66`),
  read only, through `node:sqlite` with only three tables allowed (`storeAccess.ts:18-40`,
  `119-126`), every session with no filter (`records.ts:111-114`), one database at a time
  (`storeSource.ts:339-343`); it never connects to an OpenCode server or installs a plugin
  (`openCodeAdapter.ts:10-12`), and its OpenCode source is off by default (`daemonConfig.ts:65`).
  Seorak does not observe OpenCode at all (`e4f33e92`: only a label in
  `packages/types/src/identity.ts:129`). So a data folder of Halcyonic's own would cost Understand
  for OpenCode tasks, as Codex's home does, for a person who turned Salidium's OpenCode source on,
  and would gain: no saved "always", Console login or provider connection from the person's own
  OpenCode applying to Halcyonic's sessions.

## Not verified

- Tools called from within Code Mode, and whether they ask: with `execute` denied they cannot run.
- MCP tools, skills and the `browser` plugin's tools at runtime; the plugins a person configured.
- A real local model's behaviour when shell asks every time: how many presses an ordinary task
  needs.
- Whether a child session's ask can be answered through the parent: the adapter does not see it.
