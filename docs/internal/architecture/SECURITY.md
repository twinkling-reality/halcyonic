# Security

## Reads of a project's files

The headset may read a project's files (REALTIME.md, "Project reads"): what changed between two of
its checkpoints and one file's diff, one file of its folder as it is now, and a check step's output.
Owner-journal principals only (the local owner or a still-paired device, of the current journal), as
the tool result reads; a paired device so reads what an agent of the project could, never more. A
file is opened once without following a link anywhere in its path (`O_NOFOLLOW_ANY` on macOS), judged
and read through that handle, only inside the project's folder and never inside Halcyonic's own data;
a file whose name says it may hold secrets (version control's own, `.env*`, private keys and stores,
SSH keys, package and network credentials, files named credentials or secrets, by the rules agents
are kept from them, one list in runtime-core's `secret-files.ts` that the sandbox, these reads and a
project's export all use) is listed but never read or diffed. The list is by name only: a file such
as `.kube/config`, `.docker/config.json` or a `*.tfstate` is read like any other. Every text loses
exact copies of the secrets Halcyonic holds; secrets Halcyonic does not hold, written into ordinary
files, are not found. A hard link inside the folder to a file elsewhere on the volume is read as an
ordinary file: refusing files with more than one link would refuse pnpm's hard-linked packages, and
only something able to write the folder, which could read that file itself, can plant one. Halcyonic's
own data is compared by real path and without case.

A project's history (REALTIME.md) gives counts, ids, reasons and failure messages, never paths or
contents. Comparing a version with its folder as it is now reads that folder as taking a checkpoint
does, with the same checks of where it is (inside a project root, or a task's own copy, never
Halcyonic's data) and the same file and byte bounds, and writes its files' objects into Halcyonic's
own checkpoint store, never into the folder. One read per version is shared while it runs and kept
five seconds while nothing new is journaled, so asking often does not hash the folder often. The
objects are named nowhere, so the store's prune (ADR 0036, pruning) removes them once they are an
hour old; within that hour a reader asking every few seconds while the folder changes grows the
store, as each turn's checkpoint does. A pruned checkpoint's files are gone from the store too, once
nothing else names them, so it can no longer be put back.

## Recorded tool-result work content

Observed final command output and per-file patch fields follow the established tool-title owner
work-content policy ([ADR 0031](../decisions/0031-record-bounded-native-tool-results-in-the-owner-journal.md)).
An over-64-KiB source field is omitted whole; an admitted field loses exact known held values
before bounded storage. Actual replacements, omission and truncation are explicit. Unknown
credentials can remain, including in patches: neither regex shapes nor field completeness promise
secret-free content. These fields enter only the durable owner-only journal, never operational
logs, raw-field hashes or a source spool. They are not collected by parsing instruction, model,
agent-message or terminal text.

Authenticated result reads bind the current journal/project/task/execution and an accepted
observed result index, then cross-check the immutable journal event. A still-active paired device
shares the existing owner's journal visibility; this adds no per-project multi-user tenancy. The
coordinator is constrained to its original principal/request scope, and native/main views retain
their original selection and recheck it after awaits. Read-only evidence grants no approval,
instruction, filesystem, launch or new runtime authority. Lexical path classification deliberately
does not resolve symlinks. The RPC transport's earlier unbounded native frame and overall journal/
index memory are not bounded by the stored-field limits.

## Scope today

The control plane runs on the developer's own machine and serves loopback by default. When the
owner turns it on, a second listener serves devices paired with it over the local network, TLS
only ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md), "Devices on the
network" below). The threats it defends against now are:

- web pages in a browser on the same machine, or on another machine on the network (cross-site
  requests, DNS rebinding, cross-site WebSocket hijacking);
- other local user accounts;
- other devices on the same network, including one that intercepts traffic;
- a paired device that is lost or stolen, once the owner revokes it;
- malformed or malicious input on every interface;
- leaking secrets or work content into logs.

It is not a sandbox against other processes running as the same user; those can read the token,
the network listener's key and the journal, as with Salidium.

The person's key for a declared provider that something here reads ([ADR 0061](../decisions/0061-a-hosted-provider-is-one-declaration-file-the-host-reads.md)) is typed or pasted on the setup page, sent once
over the page's session and origin checks, and written to its declared file in the data directory, mode 0600,
through a temporary file renamed into place. It is never in the page's state, a log, the journal or an event; the
page shows only Key Stored and when, and removes it on a second press. A stored key turns on a reader that says it
does (Claude Agent) at the next start; the settings file still cannot ([ADR 0053](../decisions/0053-the-macs-setup-runs-from-a-page-beside-pairing.md),
amendment of 2026-11-12).

When the Claude Agent runtime is enabled, by `HALCYONIC_CLAUDE_AGENT=1` or a stored key, the Anthropic API key
(from the environment, or else from the key file its provider's declaration names, today `<data dir>/anthropic-api-key`, refused when other users can read it or when it is a link; with no provider declared for it, from the environment only) or cloud provider
credentials reach each launched Claude Code process through an explicitly built environment: an allowlist of variables, plus the names listed in
`HALCYONIC_AGENT_ENV`. Nothing else in the control plane's environment reaches an agent, and a
claude.ai login is never used. Launched Claude Code sessions carry `SEORAK_LAUNCHER=halcyonic`, so
Seorak attributes them to Halcyonic. `ANTHROPIC_BASE_URL` is not inherited, because it decides where the
API key is sent and a tool that launches the control plane may set it for its own endpoint; a
gateway must be passed on purpose.

The assistant runs in the control plane itself, never in an agent's process. When a hosted provider the product
declares has its key stored (ADR 0030, amendment of 2026-11-12), the control plane reads that key at each turn from
its private file only (refused when other users can read it; never from the environment), and sends it only to the
provider's declared https address, or to loopback for a proof, refusing redirects. The SDK is given that key with
no environment token, profile or base URL and its logging off; it still applies `ANTHROPIC_CUSTOM_HEADERS` from the
plane's own environment, which only the Mac's owner sets. Text inside the record a turn is sent as cannot open or
close the record's marks, so agent or project text cannot pose as the model's own earlier calls. A key never reaches an event, the journal, a log or a reply: a
provider's error text is never relayed, only the host's own words and a code. On a hosted provider, what a request
reads leaves the Mac for that provider: the host's instructions and lists, project and task names, the Mac's evidence
in words, the agent's quoted text and the person's words within the request's bound. The declared providers are
Meta's (key in `meta-api-key`, sent as a Bearer token; Standard-mode models only) and Anthropic's (key in
`anthropic-api-key`, sent as `x-api-key`). Each hosted turn writes one line to the plane's log, at info ("hosted
assistant turn"). That covers an answered turn and a failed one whose reply gave counts, since both are billed. The
line holds:
- the provider's id and the declared model asked for, never the reply's own model string;
- the outcome's code, the host's own;
- the provider's own token counts: read, written, and cached reads where it says.

It holds no word of the request or the reply, and never a key. tooling/log-calls.test.ts lists the call as reviewed.

On a folder the person connected, Claude Code runs outside the closed profile but holds no key (since 2026-11-13): it
reaches its API through the key gate with a run credential of its own, and a light seatbelt profile keeps it and its
commands out of the host's own folders (the data folder with the keys, the task copies, every `.halcyonic*` home)
([claude-code-connected-folders.md](../validation/claude-code-connected-folders.md)).

Claude Code loads no settings file (`settingSources: []`, since 2026-11-12), for each task and for listing models:
a settings `env` could add a credential that outranks the stored key. Outside the closed profile the person's own
`CLAUDE.md` and Salidium's and Seorak's hooks are passed explicitly, so they observe the session, and a managed
setting naming another credential refuses the run ([claude-code-settings-isolation.md](../validation/claude-code-settings-isolation.md)). Nothing of a project's own loads by itself: not its `CLAUDE.md`, `CLAUDE.local.md` or
`AGENTS.md`, its `.claude` rules or skills, the hooks or permissions of its `.claude/settings.json`
and `.claude/settings.local.json`, or the servers its `.mcp.json` names. Until then all of these
loaded, and a repository's hooks and servers each ran a command for every Claude Code task before
the agent did anything, with no approval asked. Proven on the fake host only; it never met the
hosted service ([claude-code-project-settings.md](../validation/claude-code-project-settings.md)).
Text of the project still reaches the model when the agent reads a file, as for every runtime.

A project's rules file reaches an agent only where the person chose it (ADR 0059). The host shows it
whole first, takes the choice only with the review of the text as it reads now, and puts exactly that
text before each task's first instruction, marked as the project's own words. A file changed since it
was chosen is not passed on, and the choice turns off. The journal keeps the file's digest, never its
text. The file is read without following a link, at most 32 KB.

When the Codex runtime is enabled, its app-server also gets an explicitly built environment: an
allowlist (`PATH`, `HOME`, `USER`, `LOGNAME`, `SHELL`, the locale variables, `TMPDIR`, `TZ` and
the XDG directories), plus the names listed in `HALCYONIC_AGENT_ENV`, without the ones Codex signs
in with (`OPENAI_API_KEY`, `CODEX_API_KEY`, `CODEX_ACCESS_TOKEN`, `OPENAI_IDENTITY_TOKEN_FILE`,
`CODEX_CONNECTORS_TOKEN`, `CODEX_GITHUB_PERSONAL_ACCESS_TOKEN`), which the adapter also refuses.
Configuration may not set `CODEX_HOME`, `SALIDIUM_INTERNAL`, `CODEX_INTERNAL_ORIGINATOR_OVERRIDE`,
which would replace Halcyonic's identity on its threads, or
`CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED`, which the adapter always sets so the server
never enables remote control, a second control channel through chatgpt.com.

Codex runs on models served on this Mac and, since 2026-11-13, in a new app's confined project only, on a declared
hosted provider through a key gate that holds the person's key (the server holds a run credential, its commands
none; [agent-apps-hosted-providers.md](../validation/agent-apps-hosted-providers.md)); every other remote provider stays
refused. It runs in a home of its own, never the person's `~/.codex`
([ADR 0011](../decisions/0011-codex-app-server-stable-surface.md), note of 2026-10-07). The control
plane chooses the home, `<data dir>/codex-home` (`~/.halcyonic/codex-home`); it is not a setting.
Before every launch the adapter makes it with mode 700 when it is missing, and refuses to launch
when it is a link or not a folder, belongs to another user, can be opened by others, or holds a
sign-in (`auth.json`). It holds `config.toml`, mode 600, written by `pnpm mac-setup local-model`
(the `ollama` provider, the model, its context and compaction threshold), and what Codex writes
itself: its SQLite databases (state, logs, goals, memories, queue), its installation id, a
`skills` folder, temporary files, and each thread's rollout under `sessions`, which holds the
conversation, commands and their output, and the project trust entries Codex adds (below). Every
launch passes, as `-c` overrides, `features.plugins=false` (on 0.157.0 the only setting that stops
the plugin sync connecting to GitHub at startup; it is undocumented), the other features 0.157.0
has on by default that reach the network or another app (`apps`, `remote_plugin`,
`plugin_sharing`, `in_app_updates`, `image_generation`, `browser_use`, `browser_use_external`,
`computer_use`, `skill_mcp_dependency_install`, `tool_suggest`, `daemon_auto_start`,
`system_proxy_fallback`) set to false, `check_for_update_on_startup=false`,
`analytics.enabled=false` (the analytics events client, and the metrics sent to ab.chatgpt.com),
`web_search="disabled"`, `skills.include_instructions=false` (below), and `cli_auth_credentials_store="file"` and
`mcp_oauth_credentials_store="file"`, so no credential is read from the keychain. Codex ranks
its configuration layers by precedence (`codex-rs/config/src/config_layer_source.rs` at
rust-v0.157.0): a device profile's settings 0, `/etc/codex/config.toml` 10, enterprise-managed
settings 15, the home's `config.toml` 20, a project's `.codex/config.toml` 25, the launch's `-c`
overrides 30, and above them `/etc/codex/managed_config.toml` 40 and its device-profile form 50.
Managed requirements (`/etc/codex/requirements.toml` or a device profile) are not a layer: they can
pin a feature on over the whole configuration (`codex-rs/core/src/config/managed_features.rs`)
while `config/read` still reports it off, since only some of their fields are projected into it
(`apply_exact_to_config`, `codex-rs/config/src/config_requirements.rs`). So right after every
launch, before anything is listed, and again at every start, the adapter checks that `config/read`
reports each of these settings, which catches the managed files, and that `configRequirements/read`
reports no requirements at all (null, "no requirements are configured",
`codex-rs/app-server-protocol/src/protocol/v2/config.rs`); otherwise it stops the server and
refuses. The same check refuses when `config/read`'s layers include one that applies whatever
Halcyonic sets: a device profile's (`mdm`), an enterprise's, a managed file's, or the system
layer when it holds anything (Codex reports that layer whether or not `/etc/codex/config.toml`
exists). Before any launch, too, the adapter refuses when `/etc/codex` exists at all, where Codex
reads `config.toml` (below the overrides, but able to add MCP servers or providers),
`managed_config.toml`, `requirements.toml`, managed hooks (`hooks.json`,
`codex-rs/hooks/src/engine/discovery.rs`; hooks are on by default) and skills (`skills`,
`codex-rs/ext/skills/src/host_roots.rs`), or a `com.openai.codex` managed-preferences file under
`/Library/Managed Preferences`, machine-wide or for this user, where a device profile's forced
`config_toml_base64` and `requirements_toml_base64` live (`codex-rs/config/src/loader/macos.rs`).
Only whether each exists is read, and one that can't be checked counts as present; that refusal
lifts once the path is gone. A refusal by the check after a launch, or at a start, which also
stops a running server and reports its threads lost, is remembered until the control plane
restarts, so no list or start launches Codex again; any other failure, such as a `config.toml`
Codex can't read yet, is tried again by the next request. The check runs at launches and starts
only: managed configuration that appears while Codex is idle or running leaves it running until
the next start. `/etc/codex` does not exist on this Mac,
and `configRequirements/read` answers null here.
The proxy variables never reach Codex from the control plane, and the adapter sets `NO_PROXY` to
`localhost,127.0.0.1,::1` after any additions, so a request to Ollama, the prompts and code with
it, never goes through a proxy. No `CODEX_` or `OPENAI_` variable can be configured for Codex: the
control plane leaves the `OPENAI_` ones it passes to other runtimes out, and the adapter refuses
any, among them `CODEX_EXEC_SERVER_*` (commands on a remote exec server), `CODEX_OSS_BASE_URL`
and `CODEX_OSS_PORT` (the local providers' address), `CODEX_SQLITE_HOME` (the databases outside
the home) and `CODEX_ROLLOUT_TRACE_ROOT` (a trace of prompts, responses and terminal output
wherever it points).

"Served on this Mac" means sent to a loopback address. A thread's model provider must be one
Codex sends to `localhost`, `127.0.0.1` or `::1`, the names `NO_PROXY` covers, Ollama on port 11434 in practice, judged as Codex 0.157.0 judges it:
`openai` by `openai_base_url` alone and `ollama` and `lmstudio` by their built-in address, since
Codex never applies a configured entry under a built-in provider's id (except Amazon
Bedrock's): it refuses the configuration when any merged layer defines one
(`validate_reserved_model_provider_ids`, `codex-rs/config/src/config_toml.rs`;
[local-models.md](../validation/local-models.md)); the
provider is named on the thread explicitly. Amazon Bedrock is always remote: Codex signs in to it
with AWS credentials, which its SSO and STS clients may fetch from AWS whatever address it is
given. A model Ollama runs on its own remote service
(`:cloud`, `-cloud`, in any case) is refused, and the model list leaves out every model not served
on this Mac. The home is checked again at every start, so a sign-in put there after the launch
refuses the next start, and its mode is never changed: one others could open is refused, to be
moved away. Halcyonic sets what keeps the person in control too: the working directory,
the sandbox mode, an approval policy that asks (`on-request` or `untrusted`), and approvals routed
to the person rather than to a reviewer agent; it refuses a thread for which Codex reports other
settings, or another model or provider than asked. The end to end suite's network probe watches
the server's process tree through startup, idle and a full run on a local model, and is re-run on
every Codex upgrade ([local-models.md](../validation/local-models.md)).

A project's own Codex settings never load. Left alone, on `thread/start` for a folder whose trust
the configuration does not record, and which is not projectless (it has a project root marker,
`.git` by default, a checkout root, or a `.codex` configuration of its own;
`codex-rs/config/src/loader/mod.rs`), Codex 0.157.0 writes `[projects."<folder, or its git root>"]
trust_level = "trusted"` into the home's `config.toml` whenever the thread's sandbox can write the
folder, as `workspace-write` can
(`codex-rs/app-server/src/request_processors/thread_processor.rs` at rust-v0.157.0), and the
repository's `.codex/config.toml` then applies: MCP servers, the sandbox's network access, hooks
and command rules. So every thread, started or resumed, marks its folder and every folder above
it `untrusted` in its own overrides, as a nested `projects` object since paths hold dots. A
thread's overrides join the launch's `-c` overrides at precedence 30, above the home, and Codex
reads them before it decides trust (`codex-rs/app-server/src/config_manager.rs`,
`load_with_cli_overrides`); it looks a folder up by itself, then its project root, then its repo
root, and judges each `.codex` folder between them the same way
(`codex-rs/config/src/config_toml.rs`, `get_active_project`, and `loader/mod.rs`), all of them
the folder or above it, and writes trust only when none is known. `config/read` cannot see a
thread's overrides, so before each start the adapter also reads it from the folder
(`{cwd, includeLayers: true}`) and refuses when any project layer would load without a disabled
reason, as when the home records the project as trusted. The end to end suite checks both with a
project whose own settings start an MCP server that leaves a mark, from the repository's root and
from a folder below it.

Codex lists no skill to the model; a skill's instructions reach it only when an instruction names
it with `$name`. Codex 0.157.0 discovers skills from an untrusted project too
(`.codex/skills`, `.agents/skills`), from `~/.agents/skills` in the person's home and from its own
bundled ones (`codex-rs/config/src/state.rs` and `codex-rs/ext/skills/src/host_roots.rs`), and has
no setting that turns discovery off (`skills` takes only `bundled`, `include_instructions`,
`max_context_tokens` and per-skill rules, `codex-rs/config/src/skills_config.rs`). By default it
lists every one of them to the model, the bundled skill installer among them. So every launch
passes `skills.include_instructions=false` and `skills.bundled.enabled=false`, checked in
`config/read` with the other local-only settings; with them, no skill, and no mention of skills,
reaches what a thread sends the provider, and the bundled skills are out of the catalog (the end
to end suite's E5). The limit: Codex still picks out a skill an instruction names, as `$name` or
by path, and reads its `SKILL.md` into the turn whatever these settings say
(`codex-rs/ext/skills/src/selection.rs`), from an untrusted project too. A name is any text of up
to 64 characters (`codex-rs/skills/src/parser.rs`), and a mention is `$` followed by letters,
digits, `_`, `-` or `:`, all but a few common environment variable names
(`codex-rs/skills/src/mentions.rs`), so a repository can plant a skill named `5` that "$5" in an
instruction brings in (E5b pins this).

Through Halcyonic, a repository's text reaches the model in two ways only: through files the
model reads, which show as commands under the thread's sandbox and approvals, and through a skill
an instruction names with `$`. The second is the one way without a visible command, and the
defence is the approval policy for whatever the skill then asks. A project's `AGENTS.md` (and
`AGENTS.override.md`) is not loaded: Codex returns before reading any when the active project is
untrusted (`load_project_instructions`, `codex-rs/core/src/agents_md.rs` at rust-v0.157.0), and
the adapter marks every project untrusted. A fake-provider read confirmed it: a git project's
`AGENTS.md` text was absent from what a thread sent, and present in the control without the
untrusted marking ([local-models.md](../validation/local-models.md)).
The coordinator decided on 2026-10-07, under the owner's delegation, to leave the `$name` limit as
documented and register Codex on the owner's Mac, where it runs on the owner's own repositories;
what to do before Codex is offered on any other Mac is open
([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

Every request Codex sends to the model provider, the Ollama on this Mac, carries the originator
`halcyonic`, a user agent with the Codex version and the operating system, and turn metadata with
the installation id, the thread and session ids, the sandbox mode, whether analytics is on and,
for a workspace that is a git repository, its path, latest commit hash and whether it has
uncommitted changes; the working directory also reaches the provider in the conversation's
environment context. Codex writes each thread's rollout to Halcyonic's Codex home, tagged
`halcyonic`, where neither Salidium nor Seorak reads it unless the person tells it to: a Salidium
after 0.8.3 reads the Codex homes and OpenCode stores named in `SALIDIUM_EXTRA_CODEX_HOMES` and
`SALIDIUM_EXTRA_OPENCODE_STORES`, and a Seorak after e3aa039e tails the Codex sessions folders named
in `SEORAK_CODEX_EXTRA_DIRS`, each beside its defaults, never instead (both unreleased on
2026-11-11; the person sets them, Halcyonic changes nothing of theirs). For a Codex execution whose
rollout it finds there (it checks only that a file of that name exists in the date folders around
the execution's start, follows no link, and looks only for an id shaped as a thread id), the control
plane reads Salidium's version from its discovery file without a credential and asks it only past
0.8.3; Seorak names no version, so it is asked with its credential. A miss there reads as not set up
yet (`own_sources_not_set_up`), never as a fault. The
adapter writes no logs. The Codex and OpenCode adapters drain their server's error output without
keeping it, as the Claude Code adapter does: it may hold secrets, such as a key that verbose
logging or a configuration error prints, and a start failure is journaled and shown on every
device. A start failure says the exit status and how to see the output: run the binary in a
terminal on the Mac.

When the OpenCode runtime is enabled, its server gets the same kind of allowlist, plus the names in
`HALCYONIC_AGENT_ENV`, and uses the developer's own OpenCode configuration and providers. What
OpenCode 2.0.18 itself sends off the Mac, whatever model runs ([local-models.md](../validation/local-models.md)):
its model catalog, fetched from `models.opencode.ai` at launch and every five minutes unless
`OPENCODE_DISABLE_MODELS_FETCH=true` reaches it through `HALCYONIC_AGENT_ENV`; and ripgrep,
downloaded from GitHub the first time an agent searches files when no `rg` is on the PATH it
inherits. Its configuration decides the rest, and its defaults do not keep work local: without a
configured model it uses a free hosted model of its own service (OpenCode Zen) even when local
models are listed, and it runs every tool without asking, `webfetch` and `websearch` included,
unless its permissions say otherwise. Every session Halcyonic creates carries rules of its own,
which outrank every configuration file and any saved "always": `execute` (Code Mode, whose
JavaScript `fetch` no permission covers), `webfetch`, `websearch` and `subagent` (whose `model`
input can send a child session to any listed model, hosted ones among them) are denied, so none is
offered to the model, and so are edits to every hidden path, in any case of its letters (the
folders OpenCode reads its configuration, plugins and MCP servers from, and a repository's `.git`,
whose configuration and hooks name programs git runs), and to `opencode.json` in any case
([opencode-permissions.md](../validation/opencode-permissions.md)). Shell commands ask the
person (the owner's decision of 2026-10-08), whatever any settings file says, and the approval
shows where the command runs and the command the model gave, whole, or offers only Deny when that
is not known; other edits need no press, except one outside the task's folder, which asks by its
own path after the folder's own ask. A repository's own OpenCode configuration never loads:
the adapter launches the server with OpenCode's switch that skips the project's folder and every
folder above it, so its `opencode.json`, `.opencode` plugins and MCP servers, `.claude` and
`.agents` take no effect (runtime verified). A plugin in the person's own OpenCode settings, used
when Halcyonic's own are not set up, runs as code in the server and can change a command after it
was shown or turn the ask off. Approving a command
such as `npm test` also runs whatever the project's files now hold, which edits change without a
press. The ask
depends on OpenCode's parse of the command: OpenCode raises it for the commands its parse finds,
and some shell constructs run without an ask. So Halcyonic treats the ask as the person's say over
what runs, not a boundary. The boundary, on macOS, is a sandbox of Halcyonic's own around the
OpenCode server and everything it starts ([ADR 0028](../decisions/0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md)):
outbound network only to the server's own port and a gate of Halcyonic's own in front of Ollama,
so no connection beyond the Mac or to another program listening on it, Ollama's own port included; data, state, cache and temporary folders of its own
under the data directory, never the person's own OpenCode folders; writes only there and in the
one folder it serves (since BOX-3 each execution's folder has a server of its own, and a server
that lists models writes no project). Since BOX-3b the profile is closed by default, the one Codex's
confined servers use ([ADR 0048](../decisions/0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md)): it
reads that folder, its own folders, Halcyonic's own OpenCode settings, the pinned binary, the tool folders and what the
folder names (a starter's dependencies, a VR version's original without its secret-shaped files), and nothing else of
the disk: never the person's home, their own OpenCode settings included, another project or Halcyonic's data
directory. The server and its commands get a home of their own, and the server listens on its own port alone. The adapter refuses to launch OpenCode on
macOS without it, and the control plane warns where it runs unsandboxed. What stays reachable from
inside: its folder; the server's own data folder, one per folder since BOX-3, where a command can
read that folder's other OpenCode sessions and change their saved rules without the API, but never
another folder's; a model
on this Mac, through the gate, which passes on only the model list, a model's details and chat with
a model that runs here, refusing pull, push and every other request, so nothing leaves the Mac
through Ollama; and the server's own API, whose password is readable from inside (below).
A command an agent leaves running in the background (a dev server, a watcher) can outlive its shell, and each agent
app differs in what its own stop reaches, as seen on the pinned versions. OpenCode runs each shell command in a process
group of its own, so a child started with `&` is re-parented to launchd and the server's stop never reaches it. Codex
0.157.0 ends a command's whole process group when the command ends, so `&`, `nohup` and a double fork end with it; only
a process that starts a session of its own (`setsid`, as tmux and screen do) outlives the turn and the server. Claude
Code leaves even a plain `&` running after its turn and after its own end. What an agent started in the background in
a sandboxed run ends with its session, in any folder: in the headset a person can neither see nor stop a process an
agent left. A run outside the sandbox cannot be told apart from the person's own processes, so what it leaves keeps
running (an approved Codex command outside its own sandbox, say). Since 2026-11-14 each adapter ends such leftovers
itself once the server or session has stopped, by any path (for OpenCode a rest too), with one more sweep a few
seconds later (`packages/seatbelt/src/leftovers.ts`, AUDIT3's method): OpenCode for each confined folder's server,
Codex for each folder its server ran a thread in, Claude Code for each session once it has ended for good, never
between the processes of a relaunch. A process is ended only when all of these hold, re-read before TERM and again
before KILL: it is this user's, its working folder is the run's folder or inside it, it started after the run's server
or session launched, its parent is launchd, and its environment cannot be read, which macOS does for a sandboxed
process (Halcyonic's profile, a connected folder's lighter one, and Codex's own sandbox); a terminal, a dev server and
most editors a person runs are not sandboxed, and the other four conditions must hold too. Halcyonic's own confined
preview and play servers are the control plane's children and are never ended by it. Not guaranteed: a command that
left the folder, or one not yet orphaned at the second sweep, keeps running inside the profile until the Mac restarts;
what a run left before Halcyonic itself last stopped is not counted, since the sweep counts only what started after the
current launch; and a person's process started with an empty environment (`env -i`) also reads as sandboxed, so one
that is orphaned in the folder after the launch would be ended with the leftovers.
An approved command that needs the network, or a local port it opens itself, fails; the person runs
those ([opencode-sandbox.md](../validation/opencode-sandbox.md)). Outside the sandbox, as on other
platforms, a saved "always" outruns the ask, below. The server's password, passed in
`OPENCODE_PASSWORD`, is not secret from commands the agent runs: each session's own environment
leaves it out, but any process of the same user can read the server's starting environment
(`ps -wwE`). So the adapter stops a task whose rules change, or whose request is approved, without
it, and stops the whole server when a session it did not open appears there; it reacts after the
change rather than preventing it. Halcyonic's OpenCode server
uses the person's own OpenCode data folder, so a saved "always", a Console login or a provider
connection from their own OpenCode applies to its sessions, and Salidium reads those sessions from
it: an "always" the person gave for a command in the same project in their own OpenCode lets a
Halcyonic task run commands it covers without asking (runtime verified). Halcyonic itself only ever
answers "once". When `HALCYONIC_OPENCODE_CONFIG_HOME`
is set, as `pnpm mac-setup local-model` sets it, OpenCode alone gets that directory as its
`XDG_CONFIG_HOME`: Halcyonic's own OpenCode settings, which ask before shell commands, refuse
`webfetch` and `websearch`, and name a model the Mac serves through Ollama as the default and as the
small model. The control plane refuses to start unless they are held to the settings file's
standard: the directory and its `opencode` folder are real folders owned by the user and closed to
others (mode 0700), and the `opencode` folder holds nothing but `opencode.json`, a regular file of mode 0600 that
holds only `model`, `small_model`, `permissions` and Ollama's context limits, with both models
served on this Mac. Other folders may sit beside `opencode`, since tools an agent runs inherit the
same configuration home. Their permissions are the person's to change and apply when something
other than Halcyonic runs OpenCode with them; each session Halcyonic opens carries the rules above,
which outrank them, and its server never loads a project's own `opencode.json` (runtime verified).

When Create's companion is set up (`HALCYONIC_COMPANION_MODEL`, [ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)), the control plane asks
Ollama on loopback for one reply at a time. Ollama's API has no authentication, so any process of
the same user can ask the same model, as the agents' runtimes do, and while Ollama is stopped
another local account could listen on its port and receive the exchange, as with Seorak's port
below. The address must be `http://` on a loopback address with a port, and the control plane
refuses to start the companion while Node's environment proxy would send that address through a
proxy (`NODE_USE_ENV_PROXY` or `--use-env-proxy` with `HTTP_PROXY`, unless `NO_PROXY` names the
address). Before every turn it reads Ollama's model list and refuses a model that is not listed,
that Ollama would pass to another host (`remote_host`, `remote_model`), or whose name has a
`cloud` tag; the `remote_host` check has not been tried against a real remote model. It never pulls,
creates or deletes a model. The model gets no tools and no image, sees only the person's own words
and the companion's earlier replies under a fixed prompt (never a folder, a project, a runtime or
other work), and its reply is checked against the contract, refused if a field holds a control,
bidirectional control or mark, zero-width space, line or paragraph separator, byte order mark, tag
character or half a surrogate pair, and shown as the companion's reported words. Nothing it says
reaches a command: its proposal fills the headset's recap, and the ordinary commands are sent only
after the person reads the whole first task in the review and confirms it. An idea or spoken words
can carry instructions to the model: every angle bracket in what the client sends is written as an
entity, so the person's words cannot close their tags or spell a chat template's turn markers,
Halcyonic's own notes go only as system messages, with the rule repeated after the exchange, and no
model tried kept injected text out of everything it said: the review is the boundary
([companion-model.md](../validation/companion-model.md)). So the review counts a line as read only
once the headset has drawn it, holds Yes until every line of the layout showing has been, and reads
again any item drawn only in part when the text is laid out anew; a quick second press never
passes a part. The exchange's order and its four
questions are checked, but the client sends the companion's earlier replies back, so a client can
forge them; that shapes only its own reply. An exchange whose UTF-8 size could pass the model's
context (two bytes a token, so the engine never cuts its start) is refused before the model is
asked. One principal can keep the companion answering it back to back within its twelve turns a
minute, so another principal meanwhile hears it is busy; one person at a time is the companion's
case. The exchange is kept only on the headset, in the app's
private files, with the rest of a Create draft, for 7 days without a change; the control plane keeps
no session and journals, stores and logs none of it. Ollama keeps the prompt in memory while the
model is loaded and, at its default log level, logs counts and times but no text; with
`OLLAMA_DEBUG` set it may log more (not verified).

A runtime's list of models (`GET /api/runtimes/:runtime_id/models`,
[ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)) is read from
the runtime at each request and never cached or journaled; a failure is logged with the runtime id
and its code only. Adapters read each model field by field, so a provider's settings, keys and
headers never reach a client: OpenCode's `GET /api/model` carries each provider's settings, API
key included, of which only the model's own fields are kept, and `/api/provider` is never read.
Where a model is served is decided from the address the runtime sends its requests to, never from
the model's name, because a local model may carry a hosted model's name (this Mac's Ollama serves
`llama3.2:1b` as `gpt-4o:latest`), and each name says what serves the model. Listing launches the
OpenCode or Codex server when none runs. Listing Claude Code's models starts a short-lived Claude
Code process with an execution's environment, key included, in a temporary directory, so it may
reach Anthropic; the tests never list against the real CLI. A chosen model is checked against a
fresh list before anything runs, so a start never falls back to another model.

The control plane also holds one credential for each product whose conclusions it reads through.
Each is read from its file on every request, refused when other users can read the file, and never
logged or passed to launched agents:

- Salidium's consumer credential, in `<data dir>/salidium-credential`, reads Salidium's reports
  and nothing else. It is sent only after Salidium's discovery file and endpoint prove the same
  instance on loopback. That proof is the instance id alone, which Salidium's discovery endpoint
  gives any local process, so an account that read it once could pose as Salidium on its port
  after an unclean stop left the file behind, and receive the credential.
- Omnorium's consumer credential, in `<data dir>/omnorium-credential`, reads the
  person's catalog and nothing else, read from a regular file of at most 1 KiB opened without
  following a link or waiting on a pipe ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)). It is never journaled or sent to a client.
  The discovery file (`consumer.json` in `OMNORIUM_HOME`, else `PHAT_PORCUPINE_HOME`, else
  `~/.omnorium` and then `~/.phat-porcupine`, moving to the second only when the first has no
  discovery file and never past an unsafe, unreadable or invalid one) is read only when it is a
  regular file, not a link, owned by this user and writable by nobody else. The instance must then
  answer a fresh 32-byte challenge with its HMAC-SHA256 under a secret only that file holds,
  compared in constant time, before the credential is sent, so a listener that took the port, even
  one that read an earlier discovery answer, gets nothing. The catalog request goes only on the
  connection the proof came back on, kept open between them: the client opens one connection per
  read and refuses a second, so if the server closes that connection after the proof, the credential
  is sent on no other and the gallery hears that the connection closed. It is sent only in
  Omnorium's `omc_` or legacy `ppc_` form, only to 127.0.0.1 on the port the file names,
  over `node:http` with no proxy and no redirect followed, and an answer past 8 MiB is not read.
- Seorak's integration credential, in `<data dir>/seorak-credential`, reads Seorak's content-free
  measurements with the `sessions:read` and `replay:read` scopes, and the provider usage limits
  Seorak last observed, account wide, with `limits:read`, for the audience
  `http://127.0.0.1:4317/api/v1`, until it expires or the owner revokes it. It is sent only if it
  has Seorak's `srkx_` form, only to 127.0.0.1 on Seorak's port, and never along a redirect.
  Seorak publishes no way to prove that the process on that port is Seorak (the request is open
  with Seorak), so while Seorak is stopped another local account could listen there and receive
  the credential. With it, that account could read the same measurements through Seorak's loopback
  plane until the credential expires or is revoked. Seorak's unauthenticated `GET /data-plane`
  proves nothing, since any listener could answer it, and is not read.

The access token meets the same risk on loopback: while the control plane is stopped, another local
account could listen on its port and receive the token from a client that sends it, and the token
never expires. `pnpm devices`, `pnpm pair`, `pnpm demo` and `pnpm mac-setup --with-token` therefore
send it only after the server proves, again before every request that carries it, that it holds
it, follow no redirect (which would carry the token on without a proof), and stop at the first
request that can't be proved (`fetchWithProof`), so a control plane
restarted while `pnpm pair` polls, and whatever took its port, gets no token; what remains is the
moment between a proof and the request it precedes. For each proof the client sends a fresh
32-byte challenge to the public `GET /api/health` in `x-halcyonic-challenge`, and only the loopback listener answers, in
`x-halcyonic-proof`, with an HMAC-SHA256 under the token of a fixed label, the local address and
port the connection reached, and the challenge, which reveals nothing about the token. The clients
dial a literal address, 127.0.0.1 or [::1] (`localhost` is tried as each), and check the proof
against the address and port they dialled, so a listener on another port or address that relays the
challenge to the real control plane gets a proof for the control plane's address, not its own, and
no token. The headset over USB (`adb reverse`) and the editor ask for the same proof, dial only
`ws://` or `http://` at 127.0.0.1 or [::1] (never `localhost` or another scheme, which ends the
session), through no proxy, and follow no
redirect (`LoopbackProof` and `LoopbackProofHandler` in the client core). Each REST request opens
a connection of its own, never from a pool, asks the proof on it and sends the token on that same
connection, so only what just proved itself receives it, and a connection another program kept
open while the control plane was stopped is never used again. The realtime upgrade does the same:
it asks the proof on a connection of its own and upgrades that connection
(`LoopbackWebSocketTransport`). Each reads its answers whole and refuses one framed more than one
way. What answers without the proof ends the session, which says the headset
didn't send its access code; nothing answering is tried again. The proof stops an app on the
headset that listens on 127.0.0.1:47800 there while `adb reverse` isn't in place, and another
account on the Mac that listens on 47800 while the control plane is stopped; the limits below hold
all the same.

The glance (a spike in development builds only, [XR_CLIENT.md](XR_CLIENT.md)) is a Java client on the
headset that asks for the proof on one plain socket and sends the token only on that same connection,
once the answer proves the listener holds it, so no retry or pooled connection can carry it anywhere
else (`GlancePoll`, run whole against a real control plane and misbehaving listeners by
`tooling/glance`). It goes through no proxy, follows no redirect, reads bounded heads and bodies
within a 10 second deadline, and refuses a token file that is a link, not its own, or readable or
writable by anyone else.

These limits hold for the access token on the headset, the app's (`files/access-token`) and the
glance's (`files/glance-access-token`) alike:

- **The access token lives on the headset.** It is the owner's token, which never expires and is
  not a revocable device credential; anyone with adb on the unlocked headset can read it with
  `run-as`, since development builds are debuggable. It is for the owner's own headset only, and is
  removed with this, whole: `.tmp` is what a write left before its move, `.off` a token a session
  set aside, and `.new` where the app leaves one if it is stopped mid-move.
  ```bash
  adb shell run-as com.halcyonic.xr rm -f files/access-token files/access-token.off files/access-token.tmp files/access-token.new files/glance-access-token files/glance-access-token.tmp
  ```
- **The Mac's adb server answers every local account.** While the headset is attached, the adb
  server on the Mac's 127.0.0.1:5037 takes commands from any process on the Mac without
  authenticating it, so another local account, the threat the proof exists for, can read the token
  with `run-as` or add a mapping of its own. After a session, remove the token and stop the server
  (`adb kill-server`).
- **The proof's address binding does not reach across `adb reverse`.** The control plane names the
  address and port its own socket reached, which on the Mac is always its listener whatever port the
  headset dialled. So anything that routes to the Mac's 47800 lets whatever listens on the headset's
  127.0.0.1:47800 relay a challenge and receive the token: a second reverse mapping, and equally an
  `ssh -L`, `socat` or a proxy on the Mac. Keep exactly one mapping involving Halcyonic's port
  (`adb reverse --list`), 47800 to 47800, and nothing else forwarding to 47800. Independent routes
  for other applications do not relay this proof and must remain intact. The session check rejects
  duplicate Halcyonic mappings, alternate endpoints forwarding to 47800 and a 47800 endpoint
  forwarding elsewhere. The port comparison includes ADB-accepted decimal/hex aliases and
  host-qualified TCP destinations, so `tcp:047800` or `tcp:localhost:47800` cannot hide a relay.
  Ambiguous or unparsed nonempty list lines fail closed, including whitespace port spellings; it never recommends removing every route.
- **A stale token and an impostor read the same.** The proof can't tell a control plane holding
  another token from another program, so the app's line names both: the code doesn't match, or
  something else is answering in its place. Either way the token was not sent.

## Controls

| Control | Implementation |
| --- | --- |
| Loopback only | `HALCYONIC_HOST` must be `127.0.0.1`, `::1` or `localhost`; anything else is refused at startup |
| DNS rebinding | Every request's `Host` must name this server's loopback address and port, otherwise 403; on the network listener, an IP address or a `.local` name with the listener's port |
| Browser requests | Any `Origin` header or `Sec-Fetch-Site: cross-site` is refused with 403, which also blocks browser WebSocket upgrades, on both listeners |
| Authentication | Loopback: a bearer token on every request and WebSocket upgrade except `/api/health`; compared in constant time. Network listener: a paired device's credential, checked by its SHA-256 against the device registry, on everything except `/api/health` and `/pair`; the access token is never accepted there |
| Token storage | 32 random bytes in `<data dir>/access-token`, mode 0600; created once; never logged; `authorization` headers are redacted from logs. On a headset (a development build reaching the computer over USB) it lives in the app's private storage (`Context.getFilesDir()`), written there with `run-as` straight from the computer's file, whole and made mode 0600 before it takes the old one's place, never on shared storage. A development build that finds a token earlier builds kept in `/sdcard/Android/data/com.halcyonic.xr/files` moves it in at startup, before it reads a pairing; a release build removes it unread. Only a regular file with one name, of at most 64 bytes, holding a token in its own form (43 characters of base64url) is taken: it is looked at with `lstat`, opened with `O_NOFOLLOW` and `O_NONBLOCK` and checked again with `fstat` before a byte is read, so no link is followed in place of the file and no pipe holds the app, and nothing in a folder that is a link is read or removed. That holds for the file, not its folder: the folder is checked once, with `lstat`, before the file is opened, so a folder swapped for a link in that moment would be followed. A system call that fails other than with `ENOENT` or `ENOTDIR` (nothing there), `ELOOP` from the open (a link) or `EACCES`, `EPERM` or `EROFS` from the removal (refused) stops the move and is logged with the call and its errno, so a failure is never taken for nothing there, not a token or removed; a `chmod` that fails is logged as a mode that could not be set, and the token still moves. The new file is made mode 0600 where it can be while still empty, before the token is written (private storage alone already keeps other apps out). A copy the app can't remove, as one `adb push` left owned by `shell`, is logged with the command that removes it, and the owner replaces the token once after every headset moved it, since it sat readable over `adb` and USB file access and never expires (XR_DEVELOPMENT.md); none of this is yet checked on a headset |
| Logs and error text | The `authorization`, `cookie`, `x-halcyonic-proof` and `x-halcyonic-challenge` headers are redacted wherever a logged object carries headers; an error is logged as its type, code and stack frames, never its message, which can quote what it read, unless it is one of Halcyonic's own errors in fixed words (`OwnWordsError`: a setting's problem, a contract's issue paths, the speech engine's state), nor its other fields, such as the head of a malformed request with its Authorization header in Node's `rawPacket`. A runtime's or a provider's error text, which every device sees once journaled or read through (a turn's failure, a refused action, a lost connection, Seorak's reason text and labels, a model listing's failure, and since 2026-11-03 a checked page's exception texts, which can quote what its build pulled in), loses every exact copy of a secret Halcyonic holds or passes to a runtime, in its place which one it was, as "[redacted: Anthropic key]" (a provider's declared name and "key"): the access token, every key a declared provider names (the host's one `HostProviders`, from the key's variable and its file, any regular file whatever its mode, never a link), OpenCode's server password, Salidium's, Seorak's and Omnorium's credentials, the network listener's private key (whole, with its body's lines joined, and each line of it, since a copy may be joined or escaped as JSON escapes it), and the `HALCYONIC_AGENT_ENV` values whose names have a secret's word in them (KEY, TOKEN, SECRET, PASSWORD, PASS, PASSPHRASE, PAT, AUTH, CREDENTIAL, COOKIE, SESSION, HEADER and the like, matched as whole words) or that read as a credential by themselves, each part of a value that does, as a header's value in `Name: value`, the password and `user:password` of any URL in one, up to the last `@` before the host, any value given to a secret's key in one (password, pwd, passwd, passphrase, secret, token, api_key, access_key, credentials, an Azure SAS's `sig`), as in `password=…;`, `?password=…` or `"api_key": "…"`, escaped quotes and commas in it included, and any value given to a secret's command-line option, as in `--password …`, however they look, all under their variable's name; a part of a value that is an absolute path is not held for looking random alone. It then loses every credential-shaped run as "[redacted]" (a scheme's credential, never a path, though one with `+` or `=` padding is base64's, never a path's; a URL's user and password, up to the last `@` before its host; well-known key shapes; a long random run, never a name built of words) (`core/redaction.ts`). The rest is kept as given, but the shapes can also take what only looks like a credential: a URL's user name, a word with a digit or `+`, `/` or `=` after Bearer, Basic or Token that is not a path, or a run of 32 or more characters mixing capitals, small letters and digits with few words in it, such as a random id. Only the first 4096 characters are read, and the result is cut to its field's limit. What a person reads to decide, a tool's title, what an approval asks for, a question's prompt, a test run's label and summary, and the agent's own messages (since 2026-11-03: an agent can echo a key it was given), loses only the exact copies, named, so the command a person approves is never guessed at; a credential Halcyonic doesn't hold stays in it, and a question's options stay as given, since an answer names them. The adapters report a title, a summary, a label, a question's texts and error text whole, never cut, since a held secret cut in two is no longer an exact copy; the control plane takes credentials out, then cuts each to its limit, counted in code points as the contract counts, with "[truncated]", a question with `fitQuestion` (each text to its field's limit, then a question past 16,000 characters shortened, prompts to 1000), once. A question with anything cut can no longer be answered, and a fitted one stays within the 16,000 whatever the markers add. An agent can type the marker itself (open, [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)). What redaction knowingly misses is listed below the table. `tooling/log-calls.test.ts` fails on a log call that names something that can be private unless it was reviewed ([logging-audit.md](../validation/logging-audit.md)) |
| Network listener | Off unless `HALCYONIC_NETWORK_HOST` names an IP address; TLS only (1.2 or later), with a self-signed ECDSA P-256 certificate generated once into `<data dir>/network-key.pem` and `network-certificate.pem`, mode 0600; the key is never logged; 5 seconds for the TLS handshake, 10 for a whole request, 60 for a connection on which nothing moves, 5 between requests; 32 connections at once, 8 from one address, and 4 realtime connections per device; a WebSocket it closes waits a second for the client's answer |
| Pairing | Opened only from loopback with the access token (`pnpm pair`), one window at a time, for five minutes, closed by the first device that pairs, by three failed proofs, or by the owner; an eight-digit code, never logged or journaled; SRP-6a (RFC 5054, 3072-bit group, SHA-256) with the TLS certificate bound into both proofs; the salt and verifier derived once per window, so no exchange's work depends on the code; `/pair` refused outside a window before any cryptography; four exchanges at once, one per address, six connections per address a minute, 30 seconds each; what it turns away or cuts short without checking a code is counted by address and shown by `pnpm pair` |
| Pairing page | [ADR 0047](../decisions/0047-pairing-starts-from-a-page-on-the-mac.md). A server of its own on 127.0.0.1, port 47802 to 47809 (the control plane's port plus 2 to plus 9; a free port for a plane on port 0), in the control plane's process, holding no access token and no device credential. A link from `POST /api/pairing/page` (loopback, access token) carries a 32-byte one-time code good for 60 seconds, traded for an HttpOnly, SameSite=Strict cookie for its port alone (8 browsers at most), every cookie of that name read so a planted one keeps nobody out; the link is handed to the browser in a mode-600 file in `<data dir>/open-links`, opened by its path and removed after a minute, never in a process's arguments; every request must name `127.0.0.1:<port>` as Host, a GET come from no page or its own origin (`Sec-Fetch-Site` none or same-origin) except the trade of a code, a top-level navigation from any site, and a POST carry its own origin and come from it; a strict CSP, no caching, no referrer. A GET with no `Sec-Fetch-Site` at all is refused with words that say why (an older browser). It can turn the network listener on and off, open a pairing window and show that window's code, only beside an address a headset can reach (never a loopback one), and forget a device; it serves its fonts itself (`font-src 'self'`); run-out hand-over files are swept by the control plane each minute and at each new link; nothing at load or start, only on a press. It reads no project, command or journal entry, and the code it shows is never logged |
| Preview relay | [ADR 0052](../decisions/0052-a-preview-reaches-a-paired-headset-through-a-loopback-relay.md). On the network listener, to a paired device only: `GET`/`HEAD /api/projects/:id/preview/...` asks 127.0.0.1 at that project's preview port and nowhere else, per request only while the preview serves the latest check, passed, with `reads_confined`, as the host's own record of its server says; passes on only `Accept`, `Accept-Language`, `Range`, `If-None-Match`, `If-Modified-Since` and back only the file's own description, with `no-store` and `nosniff`, never a cookie or a CORS header; refuses a service worker script; 32 MiB and 15 s a file. Since 2026-11-12 the headset's `PreviewRelay` uses the preview stream (next row); this route remains for builds before it. The relay is closed as soon as the browser leaves the preview |
| Preview stream | [ADR 0052](../decisions/0052-a-preview-reaches-a-paired-headset-through-a-loopback-relay.md), amended 2026-11-12. On the network listener, to a paired device only, a WebSocket upgrade `GET /api/projects/:id/preview-stream`, admitted by the same gate as the file route (the preview serves the latest check, passed, `reads_confined`, by the host's own record); the Mac then dials only 127.0.0.1 at the port the gate names, never one the request names, and pipes bytes both ways. Each open stream re-asks the gate and whether its device is still paired every second and ends at once when either says no; it ends after 60 s with no byte either way, and when the listener closes (pairing turned off). A device holds at most 6 at once (the listener's 8 connections per address bound them too). On the headset `PreviewRelay` reads each browser connection's first request head, requires `Host: 127.0.0.1:<its port>` and no `Service-Worker`, forwards it with `Connection: close` (a WebSocket upgrade keeps its connection), and carries the connection in binary frames; the device credential is on the upgrade only, never in the browser's bytes. Development builds only (`RelayInDevelopmentBuilds`) |
| Device credentials | 32 random bytes per device (`hlcd_` and base64url), sent once, encrypted under the SRP session key inside TLS; the journal keeps only their SHA-256; revoked from loopback (`pnpm devices revoke`) or by the device itself, at once and for what is already open: its realtime connections handle nothing more and are cut off, its commands are rejected where they would act (`device_revoked`), and any answer to a request it opened before is replaced by 401 |
| Failed credentials | 30 refused credentials from one address in a minute, and it gets 429 for the rest of the minute |
| Content types | JSON only; `text/plain` and form bodies are refused with 415. The one exception, `POST /api/transcriptions`, takes `audio/wav` only, through a parser registered in a scope of its own so no other route gains it, and refuses JSON |
| Input validation | Every command, client message and query is validated against the contracts |
| Size limits | 1 MiB request bodies; 256 KiB for a companion request, whose exchange holds at most 20 messages and 24,000 characters; 960,044 bytes for a clip of speech, refused with 413 from its declared length before the body is read, or as soon as a chunked body passes it; 256 KiB WebSocket messages; slow WebSocket clients are disconnected |
| Data at rest | Data directory mode 0700; journal, WAL and SHM files mode 0600 |
| Host settings | `<data dir>/settings.json`, written by `pnpm mac-setup`, fills in the `HALCYONIC_` variables the environment leaves unset ([ADR 0024](../decisions/0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md)). Startup is refused unless it is a regular file opened without following a link, owned by the user running the control plane, mode 0600, at most 64 KiB, in a data directory that user owns and that is closed to others (mode 0700), with `"format": 1` and only known settings; anything but a regular file, such as a named pipe, is refused before it is opened. It may hold the project roots, the pinned OpenCode and Codex binaries, Halcyonic's own OpenCode settings, the voice files, the companion's model and its loopback Ollama, and the network listener's address, never `HALCYONIC_CLAUDE_AGENT`, `HALCYONIC_CLAUDE_EXECUTABLE`, `HALCYONIC_AGENT_ENV` or a model that can run on a remote service (a companion model with a `cloud` tag, or Ollama anywhere but loopback, stops startup), so a hand edit cannot start paid model use or pass a variable to agents. Its values are checked like the environment's, and the ready log names the settings taken from it and each OpenCode and Codex binary's path with whether its SHA-256 is the pinned one (a warning when it is not) |
| Project roots | Every root, from the environment or the settings file, must be an existing folder that may hold projects, or the control plane does not start (`folder-safety.ts`): never `/`, `/Users`, `/Volumes` or a whole drive in it, `/private`, `/var`, `/tmp` or `/opt`; nothing in `/System`, `/Library`, `/Applications`, `/usr`, `/bin`, `/sbin`, `/etc` (`/private/etc`), `/dev`, `/opt/homebrew` or `/Users/Shared`; nothing in `/private/var` but a folder made inside a user's own temporary folder (`/private/var/folders/xx/yyy/T`); not another person's home, the home folder (the account's own, not `$HOME`, which a launcher may change) or a folder holding it, Halcyonic's data or a folder holding it or in it, a hidden folder or Library in the home folder, a folder another user owns, or a folder any user can change |
| Logging | Log context carries identifiers only, never tokens, pairing codes, device credentials or their hashes, keys, instructions, agent text, clips of speech or their transcripts, or a companion's exchange or reply (only its outcome, view, times and token counts) |
| Agent working directories | Every real execution runs in its project's folder, and a client never sends a path ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)). A project is bound to a folder a client chooses by naming one of the host's project roots (`HALCYONIC_PROJECT_ROOTS`) and a folder directly inside it: the host composes the path, refuses a symbolic link, a hidden name or `..`, and records the real path the file system itself gives (`realpath(3)`), so one folder has one recorded spelling whatever case or Unicode form the client used. Before each start the control plane, then the adapter before it launches anything, and the adapter again right before it hands the folder to the runtime (OpenCode's before each model read that sends the folder, and Codex's before it resumes a thread after a relaunch) ask the directory policy again: only real paths under a root, so `..` and symbolic links cannot escape it, and a path that now resolves elsewhere is refused. With no roots configured, no real runtime can start. Since ADR 0045, with copies on (the default; `HALCYONIC_TASK_COPIES=0` turns them off), a task whose runtime is held to the folder it is given (each adapter says so itself, `RuntimeAdapter.holdsFolder`: today Codex, confined in every execution's folder on macOS since BOX-2; OpenCode, whose server for each execution's folder writes that folder alone on macOS since BOX-3; Claude Code inside the closed profile, a new app's project and its copies on macOS since unit 4c, and not elsewhere, where it has no sandbox) works in its own copy of the project's folder, a folder of Halcyonic's own beside the data folder; any other task works in the project's folder, one at a time, and is refused a copy. A copy's own git settings lose the user part of every remote's address. Even so, a sandboxed agent reads what its sandbox lets it, and Claude Code outside the profile, working in the project's folder, can read and write another task's copy: an add brings only the tree the person was last shown and refuses a copy changed since. Only where copies are on the runtimes' policy admits folders inside `HALCYONIC_TASKS_DIR` too (never the folder itself), resolved the same way, while the host's locations, and so every client's choice, keep the roots alone. A copy is made only by the host, from the project's folder, and refused where the folder's `.git` is a file (a copy would still act on the person's repository), where it holds Halcyonic's data or the copies, or where a checkpoint could not keep it whole |
| Project folders | The host makes a new folder only directly inside a root, with a one-segment name of letters, digits, `.`, `_` and `-` that does not start with `.`, and a non-recursive `mkdir` that fails rather than follow or reuse anything already at the path. Right before it binds or makes anything, it checks that the root is still the folder it found at startup (same real path, no symbolic link along it, same device and inode); a root replaced since, by a link or another folder, is refused until the control plane restarts. A folder it made that then fails the policy is left in place and reported, with effect `unknown` and the folder's real path. One race remains: a root swapped for a link between that check and the `mkdir`, which takes a path, gets the new, empty folder made where the link leads; the failure then names it. It deletes no folder |
| Instruction pictures | An instruction may carry up to four pictures of the project's folder, named by a project-relative path, never bytes or a path from the client, and only for a runtime that declares `takes_pictures` ([ADR 0058](../decisions/0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md)). The host reads each once, as the project's file read does: inside the folder, never through a link anywhere in its path (`O_NOFOLLOW_ANY` on macOS), never a secret-shaped name or Halcyonic's own folders, judged and read through the one handle, at most 5 MB, PNG, JPEG or WebP by its first bytes and whole by its structure. The runtime is handed those bytes (a `data:` URL or a base64 block), never a path, so no runtime reads the file again and what it was given is what `execution.instruction.pictures_read` names by SHA-256; the journal never holds a picture's bytes. A picture still shows the model whatever the folder holds, as any file an agent reads does; the person chose it. The list read (`GET /api/projects/:project_id/pictures`) walks at most 8 folders deep and 20,000 entries, and skips hidden names, `node_modules`, links and Halcyonic's own folders |
| Given pictures | `POST /api/pictures` (local or a paired device, behind the same principal check as every route) keeps one whole PNG, JPEG or WebP of at most 5 MiB by its SHA-256 in `<data dir>/pictures` (0700, files 0600), 64 at most for 24 hours; never journaled, never logged. An attachment names one by its id; the host reads it again, checks its digest and that it is whole, and hands the runtime the bytes (ADR 0058, amended) |
| Agent permissions | Runtime permission modes that take decisions away from the supervising person (`bypassPermissions`, `auto`) are refused as start options, and so are Codex's approval policy `never`, its granular policies and `danger-full-access` with `on-request`, under which Codex runs every command it does not flag as dangerous without asking. One exception, never a start option: on a project made from a starter and confined by Halcyonic's own sandbox, checked at launch, Codex's commands inside the project run without asking (on macOS every other folder is confined too since BOX-2, and asks, under `untrusted`, when its start asks) ([ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md), with its conditions). A second, chosen per project on the headset, where it is the default only where the screen that sends the start names the level, until the person chooses Ask First (ADR 0043's amendment of 2026-11-10; the control plane's own default stays ask): the approval level `never_ask`, offered only by a runtime that declares `offers_never_ask` once a runtime test on its pinned version shows nothing asks and the agent stays in a sandbox bounded to the task's own project; today Codex alone, as the policy `never` with `workspace-write`, every thread pinned to no network and no writable folder but the project above the home's `config.toml`, refused while that home names an MCP server; OpenCode inside Halcyonic's sandbox, where since BOX-3 each folder's server writes that folder alone, with session rules that allow shell commands and refuse whatever would ask; Claude Code refuses it, having no sandbox of Halcyonic's; for either runtime, every other bound project's folder inside a run's own is denied to its reads and writes, and where a sandbox cannot do that (Codex off macOS) a start that never asks is refused for that folder (ADR 0043, note of 2026-11-11) ([ADR 0043](../decisions/0043-a-person-may-let-a-tasks-agent-work-without-asking-where-its-runtime-keeps-it-in-bounds.md)). A request whose summary is not complete (see "Untrusted text in the client") can be denied, never approved: admission answers an approve with `approval_not_whole`. Approving a Codex command can let it run outside Codex's sandbox with the person's access (an escalation) or keep it inside (a command Codex judges dangerous), and its request does not say which, so each runtime declares where an approved command runs (`approval_may_leave_sandbox`) and the headset says it under the request in Halcyonic's own words ([ADR 0033](../decisions/0033-approvals-declare-whether-commands-leave-the-sandbox.md)) |
| Agent processes | Stopped on close and when the control plane exits, including on a second signal during shutdown. Every Claude Code process and the OpenCode and Codex servers are recorded before they receive work and watched by a small process that stops them if the control plane dies, even by SIGKILL; the next start stops anything recorded that survived. Identity is checked before any signal. Codex starts each command in a session of its own, beyond the reach of a signal to its server's process group: ending the server's input makes Codex stop them, and a server that has to be killed is killed with all its descendants. A Codex server killed by anything else leaves its running commands behind |
| OpenCode server | Launched from the configured binary only, never from PATH; bound to 127.0.0.1 on a free port with a password generated per launch and kept in memory; refused unless it reports version 2.0.18 and the process id Halcyonic started; recorded (without the password, mode 0600) so the next start stops it after a crash, and watched by a small process that stops it if the control plane dies; on macOS, only inside Halcyonic's sandbox ([ADR 0028](../decisions/0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md)), its profile written beside the record at each launch (mode 0600) |
| Turn file readings | The control plane reads every file of a task's folder (not the top-level `.git` or `node_modules`; each file opened without following a link and read only if what was opened is a file, so never through a link, a FIFO or a device, and on macOS without following a link anywhere in its path (`O_NOFOLLOW_ANY`), so a folder above a file swapped for a link after the listing leads nowhere (U4); each subfolder listed only while it is a real folder at its real path, checked before and after the listing, which narrows the time a folder swapped for a link could be walked without closing it; a file reused from the last reading only while its size, modification and change times, inode and device are all unchanged; a file past the byte bound refused before it is read; at most 20,000 files and 256 MB; never a folder that holds Halcyonic's own data, which each turn reports as `holds_host_data`; a start waits at most 10 seconds for it) at the start, before a turn an instruction starts from rest, and after each turn, keeping only paths, sizes, times, SHA-256s and line counts in memory, at most 100,000 files' worth across every task's latest reading (the least recently read dropped first, its next turn then reading again from rest or saying `no_baseline`), and journaling paths relative to the folder with sizes and line counts ([ADR 0038](../decisions/0038-the-mac-observes-each-turns-file-changes-itself.md)); never contents. It reads what the agent wrote, so it hashes and counts only, and never runs or parses a file |
| World description readings | For a project whose starter declares the file that describes it (`"describes"` in its `starter.json`: the `world` starter's `world.json`), and no other, the control plane reads that one file an agent may have written, at the top of the project's own folder as the host resolved it, and parses it ([ADR 0049](../decisions/0049-a-persons-world-is-a-project-whose-description-the-mac-reads.md)): opened without following a link (on macOS anywhere in its path), never waited on, read only if what was opened is a regular file of at most 16 KiB (one byte more is read, so a file that grew is refused), decoded as UTF-8, parsed as JSON and validated against `WorldDescription`, which admits only ranged numbers, six-digit hexadecimal colours, two capped lists, names of lower case letters, digits and underscores that must be its scene's own (its manifest in `starters/world/scenes/`, each light once, each place at most once) and a project's name of at most 200 characters, with no member that could hold a path, an address or code. What is journaled is rebuilt member by member in the contract's order; a refusal carries a code, where the first rule broke by the contract's own member names and list positions, and the Mac's own sentence. Nothing in the file is run, opened or followed, and no word of it enters a message or a log line. A place's project name is only compared with project names, never drawn or used as a path. A client must validate again before drawing, since the generated bindings do not enforce ranges or patterns |
| Bringing a world forward | The one place the Mac writes into a world project's folder ([ADR 0049](../decisions/0049-a-persons-world-is-a-project-whose-description-the-mac-reads.md)): when what it read is a format 1 description, it first keeps that file whole beside it under a name made of its own and the first twelve hexadecimal digits of its SHA-256 (never written over: a copy already there is left, and a different file under that name stops the step), then writes the format 2 `world.json` it derived and, only when the folder's `AGENTS.md` is byte for byte the format 1 guide the starter wrote (by its SHA-256), the starter's current guide. Each file is written whole under a new random name beside it, opened exclusively and through no link, synced, then renamed over the old name, so a reader sees the old file or the new one; nothing else in the folder is touched, nothing is deleted, and a guide anyone changed is kept. The event names only the format, the kept copy's name, the scene and whether the guide was replaced |
| Starters | A starter is tracked in the repository and reviewed like code; its dependencies are installed on the Mac from the public registry only, with no install scripts and the lockfile's integrity hashes, into `<data dir>/starters`, and a starter counts as installed only while its record names the tracked lockfile's SHA-256 ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)). A project gets a copy of the template, refused if the template holds anything but files and folders, and `node_modules` as a link to the shared set, which is outside every folder an agent's sandbox may write: the sandbox judges the path a write resolves to, so a write through the link is refused (tested with the write rule of ADR 0028's profile; Codex's own sandbox not tested). The host's Vite config for a starter loads no config file, environment file or PostCSS config of the project's (PostCSS is set inline, so an agent's `postcss.config.*` never runs on the Mac), and its dev server serves only the project and the host's shared dependencies, by the path the host passes in (`HALCYONIC_PREVIEW_DEPS`), never where the project's `node_modules` link leads, since the agent can repoint that link. For the build and the server alike, a load hook refuses any file whose real path is outside the project, the shared dependencies and Vite's cache, so an import such as `../../.ssh/id_rsa?raw` cannot put a file from elsewhere into the page. The hook sees module loads only: an asset Vite reads itself (`new URL(…, import.meta.url)`, a CSS `url()`, an HTML asset) is kept to the same folders by the build's Seatbelt profile below, which reads nothing else (AUDIT P1). A check's `reads_confined` is true only when every execution in the project recorded, at its start, that it ran inside this confinement (S2): the Codex adapter observes which server it started the execution on, so an execution from before the record existed, or one on the open server, makes it false. A VR version of a project (`project.make_vr_version`, M1.8c-1) is a new starter project whose confined agent also reads the original's folder and writes nothing of it; the original's `.git` and secret-shaped files (`.env*`, keys, certificates' private parts, SSH keys, `.npmrc`, `.netrc`, `.pypirc`, `.git-credentials`, credentials and secrets files) are refused by its profile, and Halcyonic's data folder is refused outright in every confined agent's profile, the agent's own Codex home and the starters in it allowed again ([ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md)). The original must be one game's own folder: a project root, the home folder or one above it, a folder holding or inside Halcyonic's data, or one holding another project's folder is refused, when the command is admitted and again each time the agent's confinement is built, which also reads no original when two starter projects share one folder. A secret under another name is readable, as is anything an earlier, unconfined agent of the original copied or linked into it. A starter's own files fetch nothing at run time. No service worker can register on a preview's origin: the preview ports are shared by every project, so a worker one game left could answer a later project's preview with bytes the check never saw; the host's preview and dev servers answer 404, with no body, to any request carrying the `Service-Worker` header, which a browser puts on every worker script fetch (AUDIT M16A-C1). Every response of a preview's server is sent with `Cache-Control: no-store`, so no browser's HTTP cache, the headset's or the Mac's, can show an earlier check's bytes on a reused port, and every top-level page load (`Sec-Fetch-Dest: document`) with `Clear-Site-Data: "storage", "cache"`, which clears what an earlier game left on that origin (local storage, IndexedDB, the Cache API, any service worker) before the page runs, in every browser that loads it (AUDIT M16A-C2; Chrome 156 on this Mac honours it over http://127.0.0.1, the Quest Browser not yet tried). The cost: a game's own saves do not survive a reload of its preview. Cookies are not cleared: on 127.0.0.1 they span every port, so clearing them would reset every other preview's, and a game's cookies stay visible to later projects on any port, a stated limit. The check is not affected either way, since its browser starts from an empty profile. The preview and dev servers answer no cross-origin request: Vite answers CORS for any localhost origin by default, so another preview's page, on another port of 127.0.0.1, could read this one's built game; with `cors: false` no origin is sent `Access-Control-Allow-Origin`, a preflight included (AUDIT H1). `@iwsdk/xr-input` names `cdn.jsdelivr.net` for controller and hand models; the host config rewrites that one constant (refusing the module if it changed) to a path of the preview's own origin, where the host serves only the models `starters/iwsdk-vr/host/input-profiles.json` lists, from `<data dir>/starters/iwsdk-vr/input-profiles`, each opened without following a link and only while its size and SHA-256 equal the listing; a build carries the same verified files and their licence notice. `@iwsdk/core`'s glTF loader still names `unpkg.com` for Draco and KTX2 decoders; the preview's Content-Security-Policy (`connect-src 'self' data:`, `data:` for the panels' inlined yoga-layout WASM, which reaches no network) is meant to block it, so such a model may not load (not yet seen in a browser or on a headset; the decoders already ship in `super-three`). Nothing containing `@drawcall/uikitml`, which states no licence, is published until the owner decides. On macOS, Codex works on a project made from a starter (and, since BOX-2, on every execution's folder, which then reads only itself, the tools and its git data) only from a server of its own, with a Codex home of its own beside the open one, inside a Seatbelt profile of Halcyonic's that is closed by default (Codex's own base policy): reads only of the project, its own Codex home, the pinned binary's folder, the starter's shared dependencies, the Node installation's package folders (never a package manager's `var` or `etc`, but OpenSSL's configuration file) and the system's own paths; writes only in the project and its own Codex home; network only to Ollama's port; no bind, no inbound connection, no LaunchServices, pasteboard or Apple events. Its turns use Codex's `externalSandbox` policy, under which Codex does not ask before a command, even one asking to leave its sandbox; the profile still holds, and is checked at every launch with one canary per way out (Halcyonic's data, the open Codex home, `/private/tmp`, the pasteboard, LaunchServices, a bind on every address, a package manager's `var` and `etc`), each of which must be refused ([ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md)) The check of such a project (`apps/control-plane/src/preview/check.ts`) runs its type check, build and preview server inside a Seatbelt profile with no outbound connection that writes only in the check's cache and reads, closed by default, only the system's own paths a confined agent may read, Node's installation outside the home folder, the project, the starter, the check's cache and the controller models, so the check reads nothing the agent whose files it checks may not (AUDIT P1: before, it read everything outside the home folder, and the type check's first lines, which reach the agent's next instruction (FX1), and an asset the build inlined, which the preview serves, could carry it back), and opens the built copy in a copy of chrome-headless-shell (S1: Google's headless-only build of Chrome, checked at every start against the repository's pin, the digest of a manifest of the whole folder (every entry's kind, mode and path, every file's SHA-256), with any link refused (`pins.ts`, `preview/browser-manifest.txt`, AUDIT M1); the person's Chrome is no longer used) inside a profile of its own: outbound only to the preview's port, no URL handed to another app (`lsopen`; a page's `mailto:` once opened Mail), inbound only on loopback and its own sockets, every Mach service and IOKit driver refused but four (Chrome's own rendezvous between its processes, the GPU's two user clients and Metal's compiler: never LaunchServices, the privacy service, the window server or the pasteboard, found by refusing everything and allowing one at a time, then removing each not needed), driven over a pipe rather than a DevTools port, and stopped by its own pid. Chrome's own sandbox cannot start inside the profile, so a renderer running the agent's page is native code under it after an exploit; the profile therefore also refuses every terminal (`/dev/tty*`, read, written or controlled), every signal to a process outside it, and reads of every person's home folder, drives and network shares, every app's temporary and cache folders and the package managers' data and configuration, and the browser starts in a session of its own, launcher or not, so it has no terminal to type into (AUDIT P2: without the launcher it had kept the control plane's terminal, which `TIOCSTI` could type commands into). It starts through Halcyonic's launcher (`native/disclaim.c`, built on the Mac by `pnpm mac-setup launcher` into `<data dir>/bin`, runnable only by its owner, its SHA-256 recorded then and checked before every launch), which starts it as its own responsible process, so macOS gives it none of the privacy permissions of the app that started Halcyonic (Files and Folders, the camera and the rest); a launcher missing or changed is not used, the check then runs as before and records `own_permissions: false`. The launcher uses `responsibility_spawnattrs_setdisclaim`, a system call Apple does not document; on macOS 26.7 a program started through it is shown to be responsible for itself, while one started plainly is the app's; what macOS then refuses it in a protected folder was not exercised, to avoid a privacy prompt ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)). The page is the agent's code, so nothing the check concludes is computed in it: the pictures' edges are counted in the host from the PNGs (the page could replace `getImageData`), canvases are counted through the DevTools DOM domain, and a message from Chrome over 16 MB (a page can log a string that large) ends the run as unknown, never a pass. The laptop step reads the starter's check hook, which a page turns on only for `?check` and can imitate, so what it reports is `reported`; the host's own finding is the screen change between pictures it decodes. The VR step's stand-in WebXR reports through a DevTools binding with a random name, taken into the stand-in's closure and deleted from the global object before the page runs, so the page has no function that names it; the host keeps only the two literal calls it can send. The source scan reads only the project's `src/`, following no link, at most 500 files of 1 MB, and keeps a file's path only when it is plain characters; it and the comparison of the input layer with the starter's open each file once refusing a link anywhere in its path (on macOS), and read it through that handle only while it is the file they looked at, so a file or a folder above it swapped for a link in between leads nowhere (AUDIT S3). The twin replay (M1.8g) runs the page under an injected clock that seeds `Math.random` and `crypto`'s random values and moves time only to frames the host steps to; the host steps it with a trusted F24 key that the clock takes first, on the window in the capture phase, and stops, so the page neither receives nor can make it, and the clock reports frames through a binding hidden as the VR stand-in's is. A page that brings its own time or chance (a worker, event timestamps) makes the twins differ and reads unknown. A page can tell it is being checked (headless Chrome, a fixed window size, `?check`, a clock that only moves when stepped) and could behave differently then; the check shows what the page did under it, not what it does for a person. |
| A project's own check | [ADR 0055](../decisions/0055-a-projects-own-check-is-chosen-by-the-person-and-confined-as-an-agent.md). A project not made from a starter is checked only by the scripts of its own `package.json` named `test`, `check`, `typecheck`, `lint` or `build`, read at the folder's top without following a link, at most 256 KiB; the person reads each command before choosing (`project.own_check.choose`, review required), and a command changed since, by anyone, is not run (its SHA-256 as written is compared before every run). A command is journaled and shown with any secret Halcyonic holds taken out. Each script runs as `<manager> run -s <name>`, the manager the lockfile names and only one on the host's own PATH, inside the agents' closed profile (`confinedProfile`): reads of the folder and the tools' folders, writes only in the folder and a home and temporary folder of the check's own under the data folder, Halcyonic's data folder hidden, every other bound project inside refused, no network, no port; standard input closed, no variable of the host's but PATH; its output read over pipes, kept beside the check and never journaled or logged; its whole process group stopped after 10 minutes, at its own exit and when the control plane closes, a crash's left run stopped at the next start by its pid record. Only the chosen script runs: settings in its environment outrank a project's `.npmrc` (no pre or post script, the system shell, an empty user and global config), and its home and temporary folder are emptied for every run. The choice carries each command's SHA-256 as the person read it. No turn runs in the folder while its check does. A run whose failed script's last lines name a refusal of the sandbox reads `refused` and is never sent to an agent; a run whose pipes outlived it, or whose folder changed just after it, reads `left_running`. What a check writes in the folder is recorded as its own. A child a script starts in a new session escapes the group stop and can write in the folder later, inside the profile. A script can still do inside the folder anything the agent could, and it runs whatever the person chose: the choice is theirs. |
| Playing on the Mac | Off unless the owner runs `pnpm mac-setup browser`, which sets `HALCYONIC_BROWSER` and builds the launcher. A checked game plays only in the copy of chrome-headless-shell the host makes in `<data dir>/browser` (S1), the same the check runs, never in the person's Chrome and never from the folder the owner named: the digest of the whole folder's manifest (every entry's kind, mode and path, every file's SHA-256, nothing that is a link) must be the repository's pin, checked before and after the copy and at every start, a refusal naming the first entry that differs, is extra or is missing, and a copy that fails is removed and replaced only from a source that passes. A game is played only through the launcher (below), so the browser has none of the app's macOS permissions. `project.open_on_mac` is admitted only for the latest check when it passed, saw the game work with a mouse and keyboard, and found every agent run confined, and only while the preview serves that check. The browser runs inside the check's own Seatbelt profile (outbound only to the preview's port, nothing of any home folder, drive, app's temporary or cache folder or package manager's data, no terminal, no signal outside it, no URL handed to another app, its own copy read, every system service and driver refused but the check's few) in a session of its own, plus the three audio services sound out needs, with the microphone and camera denied in the profile, and fifteen permissions a game could ask for (notifications, location, camera, microphone, clipboard, USB, HID, serial, Bluetooth, MIDI system messages, window placement, local fonts, idle detection, keyboard lock, file writing) blocked in its own settings, written before it starts; printing is off and downloads stay in its play folder. It is driven over a pipe, never a DevTools port; WebGPU, the media router and the browser's own DNS are off. It opens only the preview's page. Sound reaches the Mac's speakers. The microphone is never granted: Seatbelt refuses the device, the browser sees only stand-in media devices (`--use-fake-device-for-media-stream`), answers every permission prompt no (`--deny-permission-prompts`), and is told before the page opens that the preview's origin is refused the microphone, the camera and twelve other permissions (`Browser.setPermission`); a game's `getUserMedia` is refused at once (`NotSupportedError` on chrome-headless-shell). The person sees the game in a viewer, a server of its own per play on 127.0.0.1 at a port of 47940 to 47949, apart from the control plane, whose rule against every browser origin stays untouched: the viewer holds no access token and reaches nothing but its play's frames and input. The host opens its link in the Mac's default browser as it opens the pairing page's, through a hand-over file only its owner can read (`openLinkPrivately`), so the code is never in a process's arguments where another process could read and spend it first (CARBON A1-1, 2026-11-14); the link carries a code used once and only within 60 s, which becomes a session cookie for that port alone (`HttpOnly`, `SameSite=Strict`, named for the port). Cookies on 127.0.0.1 span every port, so a game's page in another tab would carry it: the viewer answers only a Host of `127.0.0.1:<its port>`, refuses any request naming another origin, takes a request with no origin only when the browser says no page started it (`Sec-Fetch-Site: none`, or `same-origin` for its own redirect) or, for the trade of the code alone, as a top-level navigation from the hand-over file (`Sec-Fetch-Mode: navigate`, `Sec-Fetch-Dest: document`), as the pairing page does, and opens its WebSocket only for its exact origin with its cookie (tested from another 127.0.0.1 port, refused). It serves only Halcyonic's own page, script and styles (`preview/viewer/`), each only to its session, with `Cache-Control: no-store` and a Content-Security-Policy that loads only its own script and styles and connects only to itself. A stream that drops may come back within 5 s, as on a reload, with the same port's cookie and from the same origin; past that the play stops. The game's sound is read from CoreAudio's own record of each client process (S1): a second small program of Halcyonic's, `native/audio-output.c`, built and hash-checked like the launcher, asks whether the play browser or a process it started runs audio with an active output stream (`kAudioProcessPropertyIsRunningOutput`), never anything the page reports; it reads no sound and changes nothing. An active output stream may be quiet, so Sound On means the game sends sound out, not that it is loud. When the helper is missing or changed, or CoreAudio cannot be read, the viewer says nothing about sound rather than "No Sound". A viewer's message reaches the page only as `Input.dispatchMouseEvent` (move, press, release; left, right or middle button; inside 1280 by 720) or `Input.dispatchKeyEvent` (down or up; a key and code of at most 24 characters), checked field by field; anything else sends nothing. A play stops with its preview, on a newer check, when its last viewer leaves or none comes within 60 s, and with the host; one a crash left is stopped at the next start by the pid its record names, only while that pid still runs a command naming its play folder. A headed browser was not used: under the check's closed profile it aborts without LaunchServices and about twenty more system services ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)). |
| Sharing a checked game | [ADR 0054](../decisions/0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md). Only by the person's press, only a passed, current check whose agent runs were all confined, and only the one built copy the running preview serves, read only. A link: the pinned `cloudflared` (its bytes checked at every start) under Seatbelt, writing only its own home, reading nothing of the data folder and no system configuration of its own; recorded only once its address answers over verified TLS. On this Wi-Fi: a listener on the Mac's private address, ports 47970 to 47979, passing only `GET` and `HEAD`, no body, no cookie, at most 32 connections. Either stops with the person's press, the preview, its own end and the control plane; none survives a restart. The headset is given a link's address as a code to scan, never as text |
| Send to GitHub | [ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md). Only the person's reviewed command, never an agent's, the automatic check's or auto-fix's; the digest is checked again just before the push. Halcyonic holds no GitHub credential and passes no token: only `HOME`, `USER`, `LOGNAME`, `LANG`, `LC_ALL`, `TMPDIR` and `SSH_AUTH_SOCK` reach the person's git and gh, whose own credential helper and sign-in act. Nothing of the project's repository runs (its config and hooks are an agent's to write): it is read as files and fetched from, the commit is built and pushed from Halcyonic's own bare repository with no hooks, without secret-shaped files, other projects or a new or changed link whose target leaves the folder, which the review lists (a starter project's `node_modules` is a link to the shared dependencies by an absolute path that holds the Mac's user name). A first send of a folder with no repository makes one with the person's gh, private unless they say public, as the review named it, then writes the folder's `.git` with none of anyone's git settings, templates or hooks. One new branch, never forced; nothing they print is journaled or logged; the review shows owner/name only, never an address |
| Publish to GitHub Pages | [ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md). The person's reviewed command only, for a passed, current check whose agent runs were all confined. What leaves is that check's built copy, the files its preview serves, never the source and never a secret-shaped file, as one commit on `halcyonic-pages`, never forced, pushed as Send to GitHub pushes; Pages is turned on with the person's own `gh`. The published game is public for a public repository: anyone can open it for as long as the branch is there. Unpublish deletes the branch |
| Undo (checkpoints) | Off unless the owner sets `HALCYONIC_GIT_BIN` to an absolute path of an existing file; git is never looked up on PATH ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)). The store, one Git repository per project under `<data dir>/checkpoints` (folder mode 0700), is outside every sandbox's writable folders, so an agent cannot change a checkpoint, and a project folder that holds the store or Halcyonic's data directory is refused before anything is read or written (`holds_host_data`), so the journal and access token are never kept or written back; it never writes to the project's own `.git`. Git runs with no system or global configuration, a home of the store's own, no hooks, no file system monitor and literal path specs, and the store's own attributes turn off line-ending conversion, filters and encodings, so nothing in the project's `.gitattributes` or the person's settings runs a program or changes bytes. The project's own commit is read from its files, never by running git there. A restore removes what stands in a checkpoint path's way as itself, never following a link, removes only paths inside the folder, and checks immediately before each removal that every folder above it is still a real folder inside the project, judges leftovers against the ignore list recorded with the checkpoint (never the folder's own ignore files, which an agent can write), and completes only after reading every file again, each read refusing, on macOS, a link anywhere in its path, so a folder swapped for one never makes a file outside read as the checkpoint's own (U4). Those checks only narrow the time in which a process an agent left running could swap a checked folder for a link, and before 2026-11-03 such a swap sent a removal outside the project (shown with a deterministic swap, U2). On macOS the kernel now holds the line: every git of the store runs as the binary Apple's `/usr/bin/git` shim would (`xcrun --find git`, or the configured git's own real path) inside a Seatbelt profile that reads only the system's own paths, git's own folders, the project's repository, the store's home for git and the project, and writes only the repository and the project; each removal runs as `/bin/rm` where it may write only under the project's real path, and a path still there afterwards stops the restore, recorded incomplete. A git whose own folders hold the project is not run. Elsewhere the checks narrow the race and do not close it. Restoring into another folder than the checkpoint's is refused. The journal holds hashes, counts and at most 100 paths per list relative to the folder; never contents. A restore also undoes changes anyone made by hand since the checkpoint, which the headset must say before the person confirms |
| Speech engine | Off unless the owner sets `HALCYONIC_WHISPER_BIN`, `HALCYONIC_WHISPER_MODEL` and `HALCYONIC_WHISPER_VAD_MODEL`, all absolute paths to existing files ([ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md)). whisper.cpp's `whisper-cli` is launched from that binary only, never from PATH, once per clip, with no environment and nothing from the client in its arguments, and startup is refused unless it reports version 1.9.4. That it opens no port and reaches no network is `whisper-cli`'s own behaviour, observed with `lsof` (voice-transcription.md); nothing sandboxes it, and it runs with the owner's file and network access. A clip is read by Halcyonic's own WAV parser and its samples alone are written into a new WAV, mode 0600, in a fresh temporary directory of mode 0700. That directory is removed when the engine exits, whatever the outcome, or as the control plane exits if it does first; one left by a control plane that was killed is removed at the next start, once it is three minutes old. The engine runs in a process group of its own, which is killed whole after 15 s (two minutes for the warm-up at startup) or after 64 KiB of output, so nothing it started holds the Mac. One clip at a time per principal, six reaching the engine in each fixed minute, and one transcription at a time on the Mac; a transcript longer than a draft (4,096 characters) is refused. While one principal's clip is transcribed, another's is refused as `transcription_busy_on_mac`, which tells a paired device that someone else is speaking at that moment. Voice activity detection means a clip with no speech yields no text rather than invented words |
| Companion | Off unless the owner names its model (`HALCYONIC_COMPANION_MODEL`); Ollama only at a loopback `http://` address with a port and nothing after it, never through Node's environment proxy, and a model name with a `cloud` tag refused at startup. Before every turn the model must be in Ollama's list without `remote_host` or `remote_model`. `POST /api/chat` only, streamed, thinking off, no tools, `format` the reply's schema where the engine keeps to one, `num_ctx` 16,384, `num_predict` 512; no pull, create, delete or `keep_alive`. 30 s to the first token and 45 s for a turn, one retry within them (and one more request without the schema when the engine cannot keep to one, so at most three requests a turn); past either the request is closed, which stops the model. At most 4,096 characters of reply read. One turn at a time per principal and one on the computer, 12 a minute per principal. A request carries at most 20 messages and 24,000 characters (2,000 for a message of the person's) and must fit the context at two bytes a token; its order is checked for consistency, since a client can forge the companion's earlier turns; every angle bracket the client sends is written as an entity, and written back in the reply. Whether it can be asked is read at most once in 2 s, however often it is asked. The reply is model text: checked against the contract, never cut to fit, shown only as the companion's reported words, and never a command |
| Codex server | Launched from the configured native binary only, never from PATH, in its own process group, speaking JSON-RPC over its stdin and stdout, so it listens on no port; refused unless both `codex --version` and its answer to `initialize` report 0.157.0 and `ps` shows the launched binary; remote control switched off; a thread Codex reports working in another folder than the project's is refused; only methods on the stable API surface, never the experimental opt-in, with one under-development feature switched on per thread so the agent can ask the person questions (`default_mode_request_user_input`, guarded by an end to end test and withdrawn by the adapter option `answerQuestions: false`, [ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)); a question marked secret is shown but never answerable through Halcyonic; requests Halcyonic does not show the person (permission grants, MCP elicitations) are refused, which Codex takes as a denial or an empty answer; recorded (mode 0600, no secrets) so the next start stops it after a crash, and watched by a small process that stops it if the control plane dies. Off macOS, a project runs on this open server, whose Codex `workspace-write` writes the project and also `/tmp` (seen 2026-11-04: an agent wrote `/private/tmp/new_index.html`), and its executions record `confined: false`. On macOS, since BOX-2, every execution's folder runs on a confined server of its own instead, which keeps writes to that folder, and the open server only lists models ([ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md), amendments of 2026-11-04 and 2026-11-10). The host still checks only starter projects |
| Project secrets | [ADR 0057](../decisions/0057-a-projects-secrets-are-kept-on-the-mac-and-given-to-a-tasks-commands-at-its-start.md). Each value is a file of its own, `<data dir>/secrets/<project>/<name>`, mode 0600 in a folder of 0700, sent only in its own write-only request (the Mac's setup page, or `POST /api/projects/:id/secrets` with the access token or a paired device's credential over its pinned TLS): no command, event, view, response, log or screen holds one, only its name. Either stages the value under the id of the name-only command it submits as its caller; a set with nothing staged for it fails and keeps nothing. Every value kept or staged is a held secret for redaction, read as `[redacted: NAME]`; one under 8 characters is refused. Given only to a start whose runtime declares `secret_environment`, under the adapter's own variables; never to a check, a preview, a share, a copy, a saved copy, a send or a publish, which read the project's folder or a check's build and never the data folder. A name the Mac or an agent runs by (`PATH`, `HOME`, `DYLD_*`, `NODE_*`, `GIT_*` and the rest) is refused, and so is one a runtime here reads for itself, as each adapter names its own (`RuntimeAdapter.ownsEnvironmentName`: `CLAUDE_*`, `ANTHROPIC_*`, `BUN_*`, `OPENCODE_*`, `CODEX_*`, `OPENAI_*`); a start is never given one. An agent given a key can use, print and send it wherever its commands reach, and what it prints is in its runtime's own records and what it sends its model; the page says so. A value replaced or removed stays a held secret in memory for the control plane's life, since a task started before still holds it. Names that change where a program connects or what it trusts (`*_PROXY`, `SSL_CERT_*`) are refused too. An agent outside Halcyonic's sandbox can read every project's secrets, as it can the rest of the data folder; each runtime says whether its starts can run so (`RuntimeAdapter.runsOutsideSandbox`: Claude Code on a connected folder, OpenCode without its sandbox, Codex without Halcyonic's confinement; one that does not say counts as can), and the page names each while one is set up |

### A held secret never leaves in a tree

Every key file is read one way (`readKeyFile`, `workspace-agent/providers/keys.ts`): a regular file of its own, never a link, that only the person can read. A link is refused for use, so no key is used that the held secrets, which read regular files only, do not hold.

Send, Publish and Save a Copy read the whole tree that would leave the Mac, or be set aside in Downloads, before
it goes (Send at its press; its review reads what comes from the folder, below), and Share reads every file of the built copy it would serve (`heldInFolder`: a link that leads out of the copy is refused by its path, `link_out`, whatever is held (a broken one too, when its own target is absolute or climbs with `..`), since the preview's server would serve what it leads to; a link within it is followed, each real folder once; at most 20,000 files and 512 MB, past which nothing is shared), and each refuses it (`holds_secret`, effect none) when any file holds a secret the host keeps: the very list
redaction reads (the access token, each runtime's and service's key, the person's project secrets), whoever
declared it, so no vendor or name is listed here. The forms searched (`apps/control-plane/src/core/held-in-tree.ts`):
the value exactly (as a file, an env file or a quoted string holds it), JSON-escaped, URL-encoded, and its base64,
standard and URL-safe, at each of the three byte alignments it can start at inside a longer base64 text (the groups
of four holding its bytes alone, eight characters or more). The refusal names the file and which secret, never the
value. A tree git cannot read is refused too, so nothing leaves unread. Send's review reads what comes from the folder now;
a path Send keeps exactly as the commit it goes on has it (a secret-shaped file such as `.env`, another project's
folder, a link out) is that commit's, so it is read at the press: first by the history check below, which refuses a
held value in a commit the remote lacks in the history's words, with Send One Change offered where it can be; then the
whole tree as it goes is read again, whatever the remote says it holds, and a held value in a kept file is refused
with the true steps (a new value, the old one turned off where it was issued), since taking it out of the folder's
file cannot help (TASKS, 2026-11-14; a critic's SHOULD on c993a4eac). Not searched: a value shorter than eight
characters, one split across files or lines, compressed, encrypted, or encoded in any other way (hex, a different
character set), and a value the host does not hold; nor what an agent's own network access sends while it runs.

A push also carries every commit of the branch the remote does not hold, not only the tree read for the review.
Just before each push of Send, a first send and Publish (`heldInHistory`), the remote itself is asked what it holds,
never the folder's own record of it, which its agent can write: at the address the push goes to (`pushInsteadOf`
applied, `remote get-url --push`), the commits it has in common with the one to push (`fetch --negotiate-only`),
and the push negotiates the same way (`push.negotiate`). The address is read as it is, never rewritten again. A
remote that cannot negotiate is read by the tips it lists that this Mac has, so a remote ahead of the branch is read
from the branch's first commit. Not guaranteed: when the push's own negotiation fails where the check's did not, git
sends by the tips the remote lists, which can resend commits the remote said it holds, though no branch of its holds
them (one scrubbed from its history, say). And "holds" means the remote has the object in its store: a commit a host keeps after it was scrubbed from every branch counts as held, so it is not read, and a new branch can make it reachable again (nothing that never left this Mac leaves by it). Publish is read before Make Public; a first send after GitHub made its
repository, so a refusal there says its effect is unknown. Every commit reachable from the one to push and from none of those is read:
its own text (message, people, signature), every file it brings and every folder's own bytes, in the same forms, so
every name, one with a line break or a submodule's included. Less known about the remote means more is read, never less. A hit refuses the press (`holds_secret`): an
earlier version of a file, a change's saved description, or a name (shown with the value as the secret's name in brackets), said
to be one the repository does not have (what the check knows, never "not yet sent": it may have gone elsewhere), with
the two ways forward that are true, said as steps with no claim about what a service has done (a new value made and
the old one turned off where it was issued, since making a new one does not turn the old one off at most services,
then the new one stored on this Mac, the old one treated as exposed until it is off; or a developer taking the old
version out). On Publish the earlier version is Halcyonic's own earlier publish, which left this Mac with it, so its
words put turning the old one off first and offer no other way (AUDIT2, 16:49Z and 18:07Z; the lead, 18:08Z). Where
Send One Change is offered, Send's refusal names it as one more way (AUDIT2 W4). A title that holds one is refused at
the review, before a first send makes a repository. More than 512 MB to read is refused (`history_too_large`), and so
is a remote that cannot be asked (`not_reached`). The tree check reads names too.

Send One Change ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md), amended
2026-11-13) is the one offer on Send's refusal for an earlier version (`holds_secret_in_history`, made only where a
true base exists). It sends the files as they are now as one commit whose one parent is the newest commit of the
person's branch that the remote's own branch reaches.
- **The fetch.** That branch is fetched at that moment, at the push's own address, into Halcyonic's own repository.
- **The branch it goes on** is the remote's: the one with the person's branch's name where listed, else the
  remote's default. It is never read from the folder's tracking settings or its record of the remote.
- **What can never be the base:** a commit the remote only keeps in its store. So nothing scrubbed is made
  reachable again.
- **Staged as the person's own branch tracks the files,** with a secret-shaped file kept as the base has it, so
  one the unsent versions added stays out, and a file the person stopped tracking is not sent again.
- **The press fetches again.** A base the branch no longer reaches sends nothing (`review_stale`). Each search, with its record, runs one at a time per project, so presses at once never empty each other's refs.
- **The one reader.** The leak fix's history check reads the new commit before the push. A hit there is the
  fix's own refusal, never the offer again.
- **The record** of what a refusal found (base, branch, the secret's name, never its value) is kept in Halcyonic's
  own repository in the data folder, which no agent run can write.
- **Not guaranteed:** that the person never pushes the earlier versions themselves; the card says so.

No git command of Send's or Publish's reads the project repository's own configuration but one: the push and the
question to the remote run in Halcyonic's own bare repository in the data folder, which no agent writes, with the
person's own global and system settings, which hold how their git authenticates (a credential helper such as the
Command Line Tools' `osxkeychain` in the system settings, an ssh command, an address rewrite); staging runs with none
at all; the folder's origin, branch and object format are read as files. The one is `upload-pack`, which the fetch
of the person's branch runs in their repository: it reads that repository's settings, runs no hook, and refuses a
repository's `uploadpack.packObjectsHook`. Proved on the stand-in (2026-11-13), over ssh through the person's own
ssh command: a repository's `core.sshCommand` and `pushInsteadOf`, eight other program-running settings and its hooks
ran nothing at a review or a press, while the folder's own git used that push address, ran its hooks and ran its ssh
command. The settings only an https remote would use (`credential.helper`, `core.askPass`) were not exercised; they are
not read for the same reason.

### What redaction misses

Known, and accepted when the redaction work closed (2026-10-03). Leaks, where a credential stays:
- A credential Halcyonic doesn't hold, in error text, in a shape no pattern knows: shorter than 32
  characters, after no scheme word and without a known prefix, as a 20-character password a
  provider's 401 echoes; `Basic user:pass` that isn't base64; or a random token that reads as a
  name, 1.3 percent of random 32-character ones. In what a person reads to decide, any credential
  Halcyonic doesn't hold stays, by design, so the command they approve is never guessed at.
- A held value shorter than 8 characters, too likely an ordinary word to replace wherever it
  appears, and a held value that reaches the text in another form, split across lines or encoded
  otherwise than as written or percent-encoded.
- An agent value with no secret's word in its name that holds a credential in no form read here:
  not random-looking, as a password made of words, and not in a URL, a `key=value` pair under a
  secret's key, a header or a long command-line option; a short option such as `-p value` is not
  read. A part of a value that is an absolute path is not held for looking random alone.
- Error text past its first 4096 characters, which is never read and never journaled.
- A credential Halcyonic doesn't hold in the agent's own messages, which lose only exact copies of
  what it holds (since 2026-11-03), a question's options or a question's header, reported as given,
  and Salidium's understanding text, the agent's statements and steps, which devices read as
  Salidium gives it.
- Events journaled before 2026-10-02, when redaction began, keep what they held: the journal is
  append-only and replayed as written. So do checks journaled before their page exception texts
  were redacted (P1b, 2026-11-04), which keep those texts as the page threw them, cut to 300
  characters.
- The marker, which an agent can type itself, and a question or approval the sink or the recorder
  drops, which leaves the agent waiting while the person sees nothing (both open,
  [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

Over-redaction, where something that is no credential is taken out, never a leak:
- A value given to a secret's key or option that is no secret: `"credentials": "same-origin"`
  holds `same-origin`, and `mysql --no-password --insecure` holds `--insecure`.
- A path held by its key or name: `PWD=/Users/me/project` in an agent value holds the project's
  folder, and `SSH_AUTH_SOCK` holds its socket's path, since a value held by its name, key, option
  or URL is held even when it starts with `/`, as a base64 secret does about once in 64.
- A URL glued to an email address by a comma without a space loses its host with its password.
- What only looks like a credential, in error text: a URL's user name, a word with a digit after
  Bearer, Basic or Token, a long run that reads as random.

## Project folders and clients

What a client sees and can do about folders on the host
([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)):

- **Paths reach every authenticated client**, paired devices included: the listing of roots and
  the folders directly in them (`GET /api/locations`), each project's `location` and each
  execution's `directory`. Folder names are the file system's, so clients treat them as untrusted
  text, as they do agent text. A device could already start agents that read everything under the
  roots, so the names give it nothing it could not reach; they do show the Mac's folder layout and
  user name to anyone holding a device credential.
- **A device can make empty folders** directly inside a root, one per `project.create` or
  `project.set_location`, and bind projects to any folder directly inside a root. It cannot make
  one elsewhere, name a deeper folder, follow a link, or delete anything. Nothing limits how many
  it makes: there is no per-principal or per-device limit on folder creation, so a paired device
  can fill a root with empty folders until the owner revokes it.
- **Listing reads a bounded part of each root**: at most 10,000 entries, in the order the file
  system returns them, then sorts the folders among them and keeps 200; a root with more entries is
  marked truncated, and its folders past the first 10,000 entries are not offered.
- **A root's label** (`label`, beside its folder's own `name`) is that name, or, where another root
  reads the same, the name with folders above that tell them apart, nearest first and written in
  path order ("Projects (Work)" for `/Volumes/Work/Projects` beside "Projects (person)", or
  "Projects (Personal, Work)" where one folder above is not enough), or a number where nothing does
  (`rootLabels`). No two labels read alike once case and compatibility forms are folded; only labels
  still alike reach further up, a root named by its own folder alone keeps that name, and none holds a
  path. Startup refuses `/`, `/Users`, `/Volumes` and a whole drive as roots (`assessFolder`). A label
  can change when the roots change, so it is shown, never kept as a choice's identity: a choice sends
  the root's path. `ProjectFolder.Current` reads a kept choice's label again from the latest listing,
  saying a place is gone rather than showing an old label; New project calls it once lane C's wiring
  lands, and until then a restored draft can show a stale label beside the right path. `label` is
  optional in the contract, so a headset reads an older host that sends none, showing `name`. Labels
  come from folder names, so they may hold control, bidi or private-use characters; the host leaves
  them as they are and the headset shows them by `LabelText`'s rule, as it does every name. Projects
  are named, and look-alikes compared, by `name`.
- **What the listing tells about a folder** comes from the folder's own entry and from one name
  inside it, never from a file's contents (the catalog's folder match, below, is the one read of a
  file inside a folder, and only when a client opens the person's projects): whether a `.git` folder or file sits directly inside it
  (`repository`), the newer modification time of the folder and of that `.git` entry
  (`changed_at`), and the projects bound to it (`used_by`): a project counts while its recorded
  path is still its own real path, the rule every start applies, matched to the folder by device
  and inode. Each folder costs at most three `lstat` calls, each leaving a link at the end of its
  path unfollowed; each bound path directly inside a root is read once, and no other is read. Facts
  are kept only when the folder is the same directory after the reads as before, and a root found
  replaced after its folders were read lists as missing with none. A folder swapped for a link and
  back between two reads can still have one fact read elsewhere, which only someone who can already
  read the file system can arrange. So a paired device
  learns which folders are repositories and roughly when each last changed at its top level, which
  a device that can start an agent there could learn anyway; project ids it already sees in the
  snapshot. Connect a folder in the client core (`FolderConnect`, not yet drawn on the headset)
  offers only folders whose `used_by` is empty, and connecting one is an ordinary `project.create`
  with an `existing_folder` choice, checked like any other; the host does
  not refuse a folder another project already uses (ADR 0020 lets projects share one), so two
  devices, or a listing gone stale, can still bind one folder twice. Facts are a moment's: a
  folder replaced by another between the listing and Connect is bound with no word of the change.
  Cost: one `lstat` per distinct bound path inside the roots, so a device that makes many projects
  in one folder adds one read, not one per project.
- **The catalog's folder match** ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md), `catalog-match.ts`) reads one file in a listed
  folder, its `.git/config`, to suggest which catalog repository the folder is a clone of. It runs
  only when a client reads `GET /api/catalog` with a catalog that is the person's own, never a
  sample, reading at most 400 folders and stopping after 2 seconds. It reads only a folder whose
  `.git` is a directory, never a link or a `.git` file naming another place, and a folder holding
  Halcyonic's own data not at all. It opens `config` without waiting on a pipe and without following
  a link, on macOS anywhere in its path (`O_NOFOLLOW_ANY`), so the folder or its `.git` swapped for a
  link after it was checked leads nowhere; it checks through the handle that it is the same regular
  file of at most 64 KiB, and never runs git.
  It takes only the one `url` of `[remote "origin"]` (several, or none, is no match) and applies no
  `include`, `includeIf` or `insteadOf`. It drops any user information first, where a credential in
  a remote's URL would sit, accepts only https, ssh and scp-like forms on exactly github.com with
  GitHub's own characters for the owner and name, and keeps only `owner/name` folded to lower case.
  The URL is never kept, returned, logged or journaled. A match is `inferred` and grants nothing: an
  agent can write a folder's `.git/config`, so the person's link, checked against the catalog this
  computer last read, is the only thing that ties a project to a catalog repository.
- **Folder names are someone else's words.** Anything that can write in a root (an agent working
  there, for one) can name a folder to look like another or to read as Halcyonic's sentence. Connect
  a folder's words (`ConnectText`) show names by `LabelText`'s rule, quote them inside Halcyonic's
  sentences, and mark a free folder whose shown name looks like another free folder's, a folder in
  use or a project's (spacing, case, compatibility forms and invisible characters aside) with "Look-alike
  name", keeping its place and change time on the row to tell them apart;
  Projects marks a project whose name looks like another project's or a free folder's the same way, so
  a decoy folder named after a project, and the project it imitates, both carry the mark. A name that
  shows as nothing (only white space, or characters `LabelText` drops) shows each character's code
  point instead (`LabelText.Name`), so it can neither vanish nor stop the menu from building. Letters
  of other scripts that look alike are not caught, and the newest folders come first, so a decoy made
  a moment ago stands above the folder it imitates with only its facts and that mark to tell them
  apart. A row's key holds the root's absolute path and the folder's raw name: it is never shown and
  kept out of every log. Every press in Projects goes through `ProjectsScreens.Allows`, which acts
  only on a prompt or row the frame built from the same state shows as available, so a stale press,
  a second Connect, or Add a task in the demonstration does nothing.
- **A folder that changes after it was bound** is checked again at every start: gone or no longer
  a folder is `location_missing`, now leading elsewhere through a symbolic link is
  `location_missing` too, and outside the roots is `location_not_allowed`. Work already running
  keeps its folder.
- **Two executions can share a folder**: parallel workstreams of one project, or two projects bound
  to one folder. Nothing isolates them from each other's edits
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

## Setting up the Mac

`pnpm mac-setup` checks the Mac's setup and writes the settings file
([ADR 0024](../decisions/0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md),
[mac-host-setup.md](../validation/mac-host-setup.md)). What it touches:

- **Credentials.** It never opens the Seorak, Salidium or Anthropic credential files: it checks only
  that each exists and that other users cannot read it. A credential still moves only as a file
  with mode 600, never through the clipboard, a prompt or an AI. Whether Seorak accepts its
  credential it learns from the running control plane (`GET /api/usage-limits`).
- **The access token.** By default it reads none, and asks the running control plane only its
  public health check. With `--with-token` it reads the token and sends it only after the server
  proves it holds it (above), then asks on loopback which folders, agent apps and paired devices it
  has, as `pnpm devices` does.
- **The person's own files.** It never reads the person's own Codex or OpenCode settings, which
  may hold provider keys. Of Halcyonic's own Codex settings, `<data dir>/codex-home/config.toml`,
  it reads only the top-level `model_provider` and `model` lines, and prints a model name only when
  it matches the name pattern the Codex adapter takes. `local-model` writes that file and refuses a
  Codex home that is a link, another user's, or holds a sign-in.
- **Binaries and models.** It records the pinned OpenCode and Codex binaries and the voice models only
  when their SHA-256 matches the pins Halcyonic was checked with on Apple silicon; elsewhere it says
  it cannot check them. It downloads nothing: what needs a download it shows as a command. The
  setup page (below) runs those same commands, on the person's second press only.
- **Folders.** Allowing a folder says what it allows and asks first. It refuses what the control
  plane refuses as a root (Controls, "Project roots"), and says a personal folder such as Documents
  holds much more than projects before it asks.
- **Pairing.** It turns the network listener on only on `pnpm mac-setup pairing on`, after saying
  what it opens (an encrypted listener for every device on the network, the macOS firewall prompt,
  that anyone on the network can try to pair while `pnpm pair` runs) and asking. Besides it, only the
  pairing page turns it on, on the person's press (below).
- **What it writes.** The settings file, and Halcyonic's own OpenCode settings in
  `<data dir>/opencode-config`, each mode 0600 in directories of mode 0700, through a new file
  renamed into place.

### The setup page

The same check and actions on a page at `/setup` of the pairing page's server
([ADR 0053](../decisions/0053-the-macs-setup-runs-from-a-page-beside-pairing.md)). What it adds:

- **Reach.** Only what the pairing page reaches: loopback, its one-time link and cookie, its Host,
  Origin and `Sec-Fetch-Site` checks and its CSP. A view mounted on the page may add routes only
  under `/setup`, each behind the session.
- **Presses.** A fixed list of setup actions, each acting only on a second press of the same action
  and argument; anything else is refused in words. None sets the paid runtime's switches.
- **Downloads.** Only the pinned commands setup prints (npm with `--ignore-scripts`, the starter's
  `npm ci` with no user or global npm settings and a fresh cache, `ollama pull` of the checked
  models), with no shell, in a process group of their own, with `PATH`, `HOME`, `TMPDIR` and `LANG`
  only: none of the control plane's own environment, its keys among it, reaches a download. One at
  a time; Stop ends the whole group. Its lines are shown cleaned of terminal sequences and unseen
  characters, with the home folder as `~`. What a download installs is recorded only through
  setup's own action, which checks the pins; OpenCode's and Codex's binaries are checked again by
  the page, and one that does not match reads Not Set Up and is not recorded. Their `npm install`
  uses the person's npm settings, as the printed commands do. A model is pulled by name and tag
  with no pinned digest, and its button says so.
- **Sessions.** A pending press belongs to the session that made it (a hash of the session reaches
  the view, never the cookie), so another session's `confirmed` press is a first press there.
- **Project secrets.** [ADR 0057](../decisions/0057-a-projects-secrets-are-kept-on-the-mac-and-given-to-a-tasks-commands-at-its-start.md). A name and a hidden field per project; the value is cleared as it is
  sent and no answer of the page holds it. Its route alone takes up to 8 KiB of value plus JSON's
  escaping; every other route keeps the page's 1 KiB. A refusal is said in that project's card, the typed
  name kept and the value cleared. Remove asks first and deletes only on a second press of the same secret
  (LINK's critic, decided by the lead 2026-11-11).
- **Restart.** Restart to Apply closes the control plane as a signal does and exits with code 75,
  which only `Start Halcyonic.command` starts again; it says first which tasks will pause.

## Authorization

There are two kinds of principal: `local`, whoever holds the access token, which only loopback
accepts, and `device`, a paired device, which only the network listener accepts. Every command
records the principal that sent it, as the control plane authenticated it, beside the client's
self-declared identity, which is recorded for audit and never trusted. What differs between them
today is device management: opening a pairing window, listing devices and revoking one are served
on loopback only, and a device can revoke no credential but its own. Commands are otherwise admitted
alike for both, with one difference since 2026-11-11 ([ADR 0051](../decisions/0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md)):
a paired device changes a project's files or folder only with the digest of the review it read. Its
`project.set_location` that rebinds a project that has a folder, its `project.restore`, and its
`workstream.add_to_project`, must carry `review`, the digest the read that showed the move, the
restore or the add answered (the location review, the restore preview, the copy's latest reading in
the task's view), and that digest must still describe what is true when the command acts: a move is
checked at admission, as is an add, a restore in its own turn before it writes. Without one: `review_required`;
changed since: `review_stale`. A digest is checked whoever sends it; the local principal may send none,
as before. The digest proves that what was read still holds, not that a person read it: a device can
read those facts for itself. A restore's digest covers every path it would change, not only the ones
the preview lists. A device can still start work in any project. Every accepted command records its policy
category (`low_consequence`, `review_required`, `high_consequence`); beyond those three commands,
categories do not restrict anyone. No
`high_consequence` command exists; merge, deploy, delete and destructive commands must not be added
until explicit human confirmation and a policy that tells principals apart exist.

`project.restore` is the one command that removes files (ADR 0036). It is admitted because nothing
is written until a checkpoint of everything it would change or remove has been kept, so a restore
can itself be put back; when that checkpoint cannot be kept, no file changes. A paired device's restore
must carry the preview's digest (above), so it acts only on what the client showed; whether the person
read it, the control plane still cannot tell, and the headset offers no restore.

A conversational model must never turn vague speech into permission for an irreversible action.

## Audit

The journal is the audit log. For each command it records the full command, when it was received,
through which transport and from which principal, its policy category, the admission decision,
and the runtime confirmed outcome or failure (including whether the effect is unknown). Commands
journaled before principals were recorded read with a null principal. Pairing and revoking
devices are journaled too (`device.paired`, `device.revoked`, [EVENTS.md](EVENTS.md)); pairing
windows and failed attempts are logged, without the code. Instructions and answers to an agent's
questions are work content and are journaled locally; they are never logged. No question asking
for a secret can be answered through Halcyonic: admission refuses an answer to any prompt marked
secret, whatever the adapter said, and a refused answer is journaled with its keys only, so what a
client sent with it never reaches the journal or the other clients. Nor can another client learn it
by resending guesses under its command id: only the principal that sent it is told whether a
resend matches, at most six times a minute (DOMAIN_MODEL.md). A question that asks for a
secret in plain words, without the runtime marking it secret, cannot be told apart and is
answerable like any other.

## Devices on the network

What each party can do with the network listener on
([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md),
[network-pairing.md](../validation/network-pairing.md)):

- **A hostile device on the same network** sees that a TLS service listens on the port, and its
  self-signed certificate. Without a credential it gets `/api/health` and nothing else. It cannot
  pair outside a window the owner opened; inside one it gets three guesses at an eight-digit code
  in all, so one chance in 33 million, and SRP gives it nothing to test offline, even when it sits
  in the middle and relays every message: the device binds the certificate it saw into its proof,
  and the relay's is not the control plane's. It cannot read or change a paired device's traffic,
  because the device refuses any certificate but the one it pinned before sending anything. It can
  deny service: flood the port, use up a window's three attempts (the owner sees the window close
  after three failures), hold the four exchange slots open without guessing (`pnpm pair` shows
  each connection turned away or cut short, with its address), hold the 8 connections one
  address may, each for at most 5 seconds without finishing TLS and 10 without a request, fill all
  32 from several addresses, or block traffic. Its timing tells it nothing about the code: an
  exchange's work depends only on a secret it draws itself. While a window is open, and only then,
  it also hears the computer announce itself (ADR 0017, amendment of 2026-11-11): the Mac's Bonjour
  name, the port and the first 16 hex of the certificate's SHA-256, all public already. It can
  announce a computer of its own under any name, which a headset may offer beside the real one; the
  person's code and the certificate bound into the proofs still decide, so a false offer cannot
  pair without the code and cannot pass for the Mac.
- **A stolen or lost headset** holds its credential in app-internal storage and can do whatever
  its owner could over that network, until the owner revokes it with `pnpm devices revoke`, which
  refuses the credential and ends what it has open at once: a realtime connection handles nothing
  more, even if the headset ignores the close frame, a command in a request opened earlier is
  rejected and journaled as such, and no answer reaches it. What the headset started before the
  revocation keeps running: revoking stops the device, not work it already set going, which the
  owner can interrupt from loopback. Paired devices cannot see one another: no device reads device
  events. It reaches the control plane only
  where the listener is reachable, the owner's network. It learns where else the computer can be
  reached (`GET /api/device/listener`: the Mac's `.local` name and interface addresses), which it
  could see on the network anyway, and dials only IP addresses and `.local` names, under its pin. A development build is debuggable, so
  anyone with `adb` access to the unlocked headset can read the credential, or an access token put
  there for USB, with `run-as`; an access token, unlike a device credential, works until the owner
  replaces it on the computer; the
  headset's own lock is the first defense. It holds no provider key, repository secret or SSH
  key.
- **Meta XR Operator in a development build.** Meta's build step puts the Operator's Android library
  into every development build. While such an app runs, the library serves agents over MCP on port
  8720 on every address of the headset, with no authentication, and answers any web origin
  (`access-control-allow-origin: *`): any app or browser page on the headset, any device on its
  network, and, while an `adb forward` to it exists, any browser tab on the Mac can read the scene
  (its canvases, controls and their text) and capture what the wearer sees
  ([quest-headset-in-the-loop.md](../validation/quest-headset-in-the-loop.md)). Halcyonic leaves
  the library out of every build (`OperatorInDevelopmentBuilds`), and `BuildDevelopmentApk` deletes
  an APK that carries its layer, unless a journey build sets `HALCYONIC_OPERATOR=1`. A journey build
  runs only against a scratch control plane, never over the owner's own app or with its token, and
  its forwards are removed when the journey ends. Release builds never carry it.
- **A web page**, on this machine or another, cannot drive either listener: both refuse any
  `Origin` and cross-site fetches, the network listener refuses `Host` names other than addresses
  and `.local` names, and browsers refuse its self-signed certificate.
- **Another account on this machine** can reach the network listener as a device on the network
  can. It cannot read the data directory (mode 0700), so neither the access token nor the key.
- **Someone who sees the code** during the five minutes it is valid, and is on the network, could
  pair first. `pnpm pair` names the device that paired; revoke it and pair again.

Remaining risks: a device that pairs again gets a second credential, and the first stays valid
until revoked; credentials do not expire; the device's label is self-declared; a paired device can
hold the 8 connections its address may with slow requests, and devices on several addresses can
fill the listener's 32, a connection that sends no request for at most 10 seconds at a time.

## Untrusted text in the client

The XR client shows text Halcyonic did not write: workstream titles and objectives, which come
from commands; anything an agent or a tool wrote, such as messages, activity, approval requests
naming a shell command or a file path, and questions with their options; refusals and failures; setup problems carrying exception
text; what Salidium and Seorak say; names from runtimes; and, while pairing, a refusal in the words
of whatever answers at the typed address. TextMeshPro interprets text even with
rich text off: a backslash with u and four hex digits becomes that character whatever its
settings, a backslash with n, r, t or v a control character while escape parsing is on, the end of
text character U+0003, typed or escaped, ends a label there without an ellipsis, a carriage return
draws what follows over the start of the line, and zero width and bidirectional control characters
draw nothing or a mark over their neighbors ([workspace-interaction.md](../validation/workspace-interaction.md)).
An agent could hide the end of its own approval request that way, or make two commands read alike.
So ([XR_CLIENT.md](XR_CLIENT.md), "Words"):

- Every label that can show such text gets it through one rule in the client core (`LabelText`):
  no markup, in TextMeshPro and in Unity's `TextMesh` alike; every backslash doubled for
  TextMeshPro's escape parsing, so a backslash sequence shows as written; line breaks and tabs as
  spaces; and every control, format or default ignorable character, noncharacter, and half a
  surrogate pair, shown as its code point, as ‹U+202E›, so nothing is hidden or reordered. Every Private Use Area
  character shows as its code point too, as ‹U+E769›: Halcyonic's icons are drawn from those code
  points in their own font, so text from outside cannot draw a state's icon among its words.
- Nothing is cut short silently: a label cut short ends in an ellipsis, and no label uses
  TextMeshPro's italics or bold, which lose it. An approval is confirmed only once the whole
  request it answers has shown, in parts when it is long.
- A builder cannot skip the rule (AUDIT's X1, 2026-11-03): words handed to a line whole, Halcyonic's
  own or outside text a builder has already shown by a rule of its own, are refused, line by line,
  where `LabelText.Plain` would change them, and the names the review before Start building spells
  itself are held as unspelled runs, which a label refuses until spelled (`LineWords`). A builder that
  forgot the rule fails its tests instead of putting raw text on a label.
- The editor's render check puts hostile text on every workspace label and on a character's, and
  fails if one interprets any of it or cuts it short without an ellipsis. The line above the stage
  and the pairing line go through the same code but are not rendered by it.

Not covered: characters that only look alike, such as a Cyrillic letter for a Latin one or a
no-break space for a space, show as they look. The client shows an approval summary as the control
plane recorded it, which the contract limits to 2000 characters. The Codex, OpenCode and Claude
Code adapters report a summary whole, and the control plane cuts it to that limit with "[truncated]" after taking credentials out of it. Since 2026-10-08 each request also says whether it is
complete (`runtime.approval.requested`'s `complete`; [EVENTS.md](EVENTS.md)): the adapter says
so for what it reports, and the control plane's cut makes it false. A request that is not
complete shows as `approvable: false`, the headset offers only Deny for it, and the control plane
refuses an approve for it from any client with `approval_not_whole`. A change's content is not
part of a request: Codex and Claude Code edits are approved on the paths they name
([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

## Not yet built

- **Remote relay**, where the developer's machine connects outward and nothing is exposed
  unauthenticated. Pairing is for the local network only.
- **Policy per principal**, for example a `high_consequence` command that only the local principal,
  or a confirmation on the Mac, may send.
- **Rotation**: a device credential rotates by revoking it and pairing again, and the listener's
  certificate by deleting its key and certificate, after which every device pairs again; the
  access token still rotates only by deleting its file. Nothing expires.
- **Keystore protection** of the credential on the headset, and discovery without typing the
  address (mDNS).
- **The headset's card does not yet draw a file write's change** (AUDIT3, 2026-11-13). The request carries the
  change whole (`ApprovalSubject.changes`, ADR 0065 point 5), but no headset card draws it yet: in an unconfined run,
  approving a write on the headset still approves content the person was not shown. A confined run is not affected
  (the closed profile holds its writes and the host answers them, ADR 0064). This line leaves when the card draws it.

Device pairing, encrypted transport for devices on the network, and per-device identity exist
([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)); serving anything to the
internet still needs the relay and more.

Provider credentials (Anthropic, OpenAI and others) must stay with the runtime on the machine that
needs them. XR clients must never receive provider keys, repository secrets or SSH keys.
