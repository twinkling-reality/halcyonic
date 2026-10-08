# OpenCode permission rules and network reach

- **Question:** Should Halcyonic give each OpenCode session permission rules of its own, so that a
  person is asked before shell commands and the agent cannot reach the network with a local model,
  as Codex now runs ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md))? How are such rules set,
  what do they reach, how do they combine with the person's configuration, does Halcyonic's
  approval path already carry them, what else reaches the network, and what would it cost?
- **Date:** 2026-10-07; the ask before shell commands, 2026-10-08.
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
  no permission check covers. Runtime: with the session's rule asking before shell commands, an
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
    file; `*` matches `/` too (`core/src/util/wildcard.ts`). Since 2026-10-08 every hidden path
    instead of the hidden names, below. A shell command can still write those paths, once the
    person approves it, or through what the ask misses (below).
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
    adapter's wait for a listed model can pass too early. Fixed since: the adapter waits for the
    model to have a package ([local-models.md](local-models.md), "Listed is not yet runnable").
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

## Shell commands ask (2026-10-08)

The owner decided on 2026-10-08: every OpenCode session Halcyonic opens asks before shell
commands; edits do not ask; subagents stay denied.

- **Built:** the session's rules add `{action: "shell", resource: "*", effect: "ask"}`. A session's
  rule outranks every configuration file, so no settings file, the person's or a repository's,
  can turn the ask off: runtime, with both allowing `shell` for every resource, a Halcyonic task
  asked, and a session without Halcyonic's rules on a server of the same settings ran the same
  command unasked. Plugins can, below. The session's ask also outranks a person's own deny, of
  shell or of one command (`git push *`), which becomes an ask. Edits keep OpenCode's own rules,
  which allow them, so they need no press, except on hidden paths, below.
- **Plugins can change what runs or turn the ask off** (source): a `shell` `create.before` hook
  runs before the parse and can change the command (`core/src/shell.ts:274`); a `tool`
  `execute.before` hook can change a tool's input after `session.tool.called` was published
  (`core/src/tool.ts`); a `permission` `evaluate` hook can turn an ask into an allow
  (`core/src/permission.ts`); a session hook can rename the shell tool, so the adapter never
  learns its command and offers only Deny. Halcyonic configures none, and since 2026-10-08 a
  repository's own plugins never load (below); plugins in the person's own OpenCode settings, used
  when Halcyonic's own settings are not set up, load as code in the server whatever the rules.
- **The approval shows the command the model gave, whole.** OpenCode's ask lists `resources`: the
  commands its parse of the command line found, each as the text of its node. Those can leave out
  what the command does: `echo start && printf ran > ran.txt` was listed as `echo start` and
  `printf ran`, without where it writes; a command substitution inside an assignment was listed as
  the inner command alone. So the adapter keeps each shell tool call's input from
  `session.tool.called`, which OpenCode publishes before the tool runs
  (`core/src/session/runner/step.ts:101`), by the call's message id and its own (a call id comes
  from the provider and may repeat), and an ask whose `source` names that call (`{type: "tool",
  messageID, id}`) shows it as its summary: first a bracket of where and how it runs, "[in the
  task's folder]" or "[in "<workdir as given>"]", quoted so no folder's name reads as the default,
  with ", in the background" inside it when it applies, always first, so a command that starts with
  a bracket of its own follows the real one;
  then the command exactly as the model wrote it. After a reconnect it is read from the session's
  messages (the tool part's `state.input`, newest message first). When the input does not say it
  plainly, the summary is the resources after "[whole command not known]", and the headset offers
  only Deny for it: a call never seen, input that is not an object (OpenCode repairs a stringified
  one only after the ask, `core/src/plugin/tool-input-repair.ts`), a `workdir` that is not a
  string or holds a bracket, a `background` that is not a boolean. The cost: a model that sends
  its arguments in such a shape cannot get a command approved. The headset shows every line break
  and other white space but the space by its code point (`LabelText.Exact`), pages the request
  with Approve only after its last part, and offers only Deny for a request the control plane cut
  to fit its 2,000 characters. The control plane itself does not yet refuse an approval of such a
  request from another client ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- **The ask depends on OpenCode's parse.** The shell tool asks only when its parse of the command
  line finds a command (`core/src/tool/plugin/shell.ts:135`, `if (parsed.commands.length > 0)`),
  with a bash grammar whatever shell runs it (the person's `SHELL`, `/bin/zsh` by default on macOS,
  or a configuration's `shell`). Runtime, 2026-10-08: some shell constructs, in zsh and in bash,
  ran a command or wrote a file without any ask, inside the project and outside it. So the ask is
  a safeguard against an ordinary model, not a boundary: a model working to get around it can run
  commands, write files and reach the network unasked. The details stay out of this repository
  until the owner decides on reporting them to OpenCode
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- **Every hidden path is denied to edits.** A repository's `.git` configuration names programs git
  runs (filters, `core.fsmonitor`, `core.hooksPath`, hooks), so an edit there would run code the
  next time the person approves a git command that looks harmless. Runtime: a clean filter written
  into `.git/config` by an edit, with a `.gitattributes` naming it, did not run from OpenCode's own
  `git status` (it passes `core.fsmonitor=false` and uses `--no-ext-diff`, `plugin/vcs/git.ts`), but
  an approved `git status` would run it. OpenCode matches a resource by case except on Windows
  (`core/src/util/wildcard.ts`), and the resource is the path as the model wrote it
  (`core/src/file-access.ts`), while macOS's disk ignores case, so a rule for `.git` missed
  `.GIT/config`, which landed in `.git` (the review, 2026-10-08); the earlier rules for
  `.opencode`, `.claude` and `.agents` had the same gap. The session's rules now deny `edit` on
  `.*` and `*/.*`: every path one of whose parts starts with a dot, in any case, inside the project
  and outside it, the configuration folders and `.git` among them. OpenCode resolves the path the
  model gave before it makes the resource (`path.resolve`, then relative to the task's folder,
  `core/src/file-access.ts:73-108`, for write, edit and patch alike), so `./notes.txt` is
  `notes.txt` and is written, and `src/../.git/config` is `.git/config` and is refused (runtime,
  2026-10-08). The cost: the edit tool cannot
  change `.gitignore`, `.github` or any other dotfile, nor, in a task whose folder is inside a
  repository, a file above it (`../x`); a shell command can, once approved. `opencode.json` and
  `opencode.jsonc` are not hidden, and OpenCode loads them by an exact path the disk resolves in
  any case (`core/src/config.ts`), so an `OpenCode.json` would load an MCP server or plugin with no
  approval at all (the second review): every spelling of `opencode` by case is denied with any
  extension of four letters or more, 512 rules. The global folder and an `OPENCODE_CONFIG` file
  are still denied as written, outside the project, where an outside-folder ask comes first.
- **Approving a command can run files the agent edited without asking.** `npm test`, `make`, a
  test runner or `git commit` with hooks a repository set up in a folder of its own run files of
  the project, which edits change without a press. The person approves the command, not what those
  files now hold; the hidden-path deny closes this only for `.git` itself.
- **Halcyonic answers "once", never "always".** Its approval contract has only approve and deny,
  and the adapter sends `once` for approve. Runtime: after a Yes, nothing was saved
  (`GET /api/permission/saved` empty) and the same command asked again.
- **What a saved "always" in the person's own OpenCode still does.** Runtime: with the person's
  own OpenCode (a server on the same data folder) answering "always" to `echo first && printf
  first > first.txt` in the project, OpenCode saved `echo *` and `printf *` for the project, and a
  later Halcyonic task in the same project ran `echo start && printf ran > ran.txt` without any
  ask. Saved rules are added after the session's unless one of those denies
  (`core/src/permission.ts`, `evaluateInput`), so a saved "always" outruns the ask but not a deny.
  They are kept per project: for a git repository, a hash of its normalised remote URL, else the
  id cached in `.git/opencode`, else its root commit; for a folder outside version control, a hash
  of its path (`core/src/project.ts`). So an "always" given in one clone of a repository applies to
  every clone of the same remote. Halcyonic does not read or remove them (they are the person's;
  `GET /api/permission/saved`, `DELETE /api/permission/saved/:id`); a data folder of Halcyonic's
  own would end it ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- **The cost, as decided:** every shell command OpenCode asks about is a press on the headset, the
  same command again next time; reading, searching and listing files, and edits, are not. A person
  who denied shell in their own settings is asked instead, since the session's ask outranks it.
- **The end to end suite checks** (`opencode-runtime.e2e.test.ts`): the ask carries the whole
  command and nothing runs before Yes; Yes runs it once and saves nothing; No runs nothing; the
  person's and the project's settings allowing shell do not skip the ask, while a session without
  Halcyonic's rules runs it unasked; a saved "always" does skip it; writes to hidden paths and to
  `opencode.json`, in any case, `.gitignore` and `OpenCode.json` among them, are refused while an
  ordinary file is written, and none of them asks. The
  sandbox's own settings no longer ask before shell, so every approval test in the suite rests on
  the session's rule.

## A repository's own configuration never loads (2026-10-08)

- **Built:** the adapter launches every OpenCode server with `OPENCODE_DISABLE_PROJECT_CONFIG` and
  its other name `OPENCODE_CONFIG_PROJECT_DISABLE` set to `true` (`OPENCODE_CONFIG_PROJECT_DISABLE`
  wins when both are set, `cli/src/server-process.ts:109-113`); configuration may set neither, nor
  `OPENCODE_CONFIG_CONTENT`, whose relative plugin paths would resolve against the task's folder
  (`core/src/config/plugin/source.ts`). The flag reaches every location the server serves, whatever
  folder a request names (`server/src/routes.ts:124-131`), and a reload discovers with the same
  flag (`core/src/config.ts`). OpenCode then skips its
  search of the task's folder and every folder above it (`core/src/config/discovery.ts:33-36`):
  no `opencode.json` or `opencode.jsonc`, `.opencode` (plugins, agents, commands, settings),
  `.claude` or `.agents` there contributes anything, in any case of its letters. Still loaded: the
  global configuration folder (Halcyonic's own when `HALCYONIC_OPENCODE_CONFIG_HOME` is set, which
  holds only `opencode.json`; else the person's own, plugins included, which `pnpm mac-setup`'s
  check says), the skills in `~/.claude` and `~/.agents`, which are text the model reads
  (`core/src/config/plugin/compatibility.ts`), and configuration from an origin the person signed
  in to with their own OpenCode, kept in the shared data folder (`core/src/wellknown.ts`), which
  can name MCP servers and plugins. A relative `skills.paths` entry in the person's global settings
  resolves against the task's folder (`core/src/config/plugin/skill.ts`), so it would load a
  project's skill text.
- **Runtime, 2026-10-08:** a project holding an `opencode.json` with a rule denying `glob` and a
  local MCP server that leaves a marker, and a `.opencode/plugins` file that leaves a marker when
  loaded: in a Halcyonic task neither marker appeared and `glob` was offered; on a server launched
  from the same folder without the switch, both markers appeared and `glob` was not offered. The
  server's own lists for the folder (`GET /api/plugin`, `GET /api/mcp`) named neither the plugin
  nor the MCP server with the switch, and both without it.
- **Cost:** a repository's OpenCode setup does not apply to Halcyonic's tasks: its models,
  agents, commands, instructions and rules, and the skills in its own `.claude` and `.agents`.
  The project's AGENTS.md is not loaded when the server starts either
  (`core/src/config/plugin/instruction.ts`). When the agent reads a file in a subfolder, OpenCode
  still adds that subfolder's AGENTS.md to what the model sees, without asking
  (`core/src/tool/plugin/read.ts:82-104`; it skips the task's folder's own), so a repository's text
  can still reach the model that way, as any file it reads does. Codex in an untrusted project
  loads none ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- The edit denies on hidden paths and `opencode.json` stay, so a task still cannot write those
  files for a later server, or for the person's own OpenCode, to load.

## Not verified

- Tools called from within Code Mode, and whether they ask: with `execute` denied they cannot run.
- MCP tools, skills and the `browser` plugin's tools at runtime; the plugins a person configured.
- A real local model's behaviour when shell commands ask: how many presses an ordinary task needs.
- Which plugins a person might configure that change a shell command before its ask.
- Whether a child session's ask can be answered through the parent: the adapter does not see it.
