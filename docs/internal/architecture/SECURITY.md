# Security

## Reads of a project's files

The headset may read a project's files ([REALTIME.md](REALTIME.md), "Project reads"): what changed between two of
its checkpoints and one file's diff, one file of its folder as it is now, and a check step's output.
Owner-journal principals only (the local owner or a still-paired device, of the current journal), as
the tool result reads; a paired device so reads what an agent of the project could, never more. A
file is opened once without following a link anywhere in its path (`O_NOFOLLOW_ANY` on macOS), judged
and read through that handle, only inside the project's folder and never inside Halora's own data;
a file whose name says it may hold secrets (version control's own, `.env*`, private keys and stores,
SSH keys, package and network credentials, files named credentials or secrets, by the rules agents
are kept from them, one list in runtime-core's `secret-files.ts` that the sandbox, these reads and a
project's export all use) is listed but never read or diffed. The list is by name only: a file such
as `.kube/config`, `.docker/config.json` or a `*.tfstate` is read like any other. Every text loses
exact copies of the secrets Halora holds; secrets Halora does not hold, written into ordinary
files, are not found. A hard link inside the folder to a file elsewhere on the volume is read as an
ordinary file: refusing files with more than one link would refuse pnpm's hard-linked packages, and
only something able to write the folder, which could read that file itself, can plant one. Halora's
own data is compared by real path and without case.

A project's history ([REALTIME.md](REALTIME.md)) gives counts, ids, reasons and failure messages, never paths or
contents. Comparing a version with its folder as it is now reads that folder as taking a checkpoint
does, with the same checks of where it is (inside a project root, or a task's own copy, never
Halora's data) and the same file and byte bounds, and writes its files' objects into Halora's
own checkpoint store, never into the folder. One read per version is shared while it runs and kept
five seconds while nothing new is journaled, so asking often does not hash the folder often. The
objects are named nowhere, so the store's prune ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)) removes them once they are an
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

## Scope

The control plane runs on the developer's own machine and serves loopback by default. When the
owner turns it on, a second listener serves devices paired with it over the local network, TLS
only ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md), "Devices on the
network" below). The threats it defends against are:

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
page shows only `Key Stored` and when, and removes it on a second press. A stored key turns on a reader that declares
`turnsOn` (Claude Agent) at the next start; the settings file cannot ([ADR 0053](../decisions/0053-the-macs-setup-runs-from-a-page-beside-pairing.md)).

When the Claude Agent runtime is enabled, by `HALCYONIC_CLAUDE_AGENT=1` or a stored key, the Anthropic API key
(from the environment, or else from the key file its provider's declaration names, `<data dir>/anthropic-api-key`, refused when other users can read it or when it is a link; with no provider declared for it, from the environment only) or cloud provider
credentials reach each launched Claude Code process through an explicitly built environment: an allowlist of variables, plus the names listed in
`HALCYONIC_AGENT_ENV`. Nothing else in the control plane's environment reaches an agent, and a
claude.ai login is never used. Launched Claude Code sessions carry `SEORAK_LAUNCHER=halcyonic`, so
Seorak attributes them to Halora. `ANTHROPIC_BASE_URL` is not inherited, because it decides where the
API key is sent and a tool that launches the control plane may set it for its own endpoint; a
gateway must be passed on purpose.

The assistant runs in the control plane itself, never in an agent's process. When a hosted provider the product
declares has its key stored ([ADR 0030](../decisions/0030-workspace-requests-use-a-journaled-agent-tool-loop.md)), the control plane reads that key at each turn from
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
assistant turn"). That covers an answered turn and a failed one whose reply gave counts, because both are billed. The
line holds:
- the provider's id and the declared model asked for, never the reply's own model string;
- the outcome's code, the host's own;
- the provider's own token counts: read, written, and cached reads where it says.

It holds no word of the request or the reply, and never a key. `tooling/log-calls.test.ts` lists the call as reviewed.

On a connected project folder, Claude Code runs outside the closed Seatbelt profile ([claude-code-connected-folders.md](../validation/claude-code-connected-folders.md)):
- **Key gate and host isolation:** Claude Code holds no API key itself; it reaches its API through the adapter's loopback key gate with a per-run credential, while a light Seatbelt profile blocks access to host folders (the data directory with its keys, the task-copies directory, and every `.halcyonic*` folder in the home directory).
- **Copy profile on macOS ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)):** when task copies are on, each connected-folder task works in its own copy under the copy profile: that copy is writable, the person's folder (including `.git`, links resolved) is read-only, every sibling task copy is refused, and standard git push credentials are blocked (`~/.ssh`, `~/.config/gh`, `~/.git-credentials`, `~/.netrc` and `~/.config/git/credentials` neither read nor written, `git-credential-*` helpers blocked, launchd SSH agent socket unreachable and unforwarded). The copy carries no remote and runs none of the person's git hooks. The network, the rest of the home directory, tools and the macOS keychain (`/usr/bin/security`, where `gh` stores its default login) remain open, so tokens stored outside the blocked paths can still reach remotes; such runs record `confined: false`.
- **Unasked commands and git credential checks:** with `settingSources: []` and its default mode, Claude Code 2.1.283 runs read-only commands inside the folder unasked (`ls`, `cat`, `head`, `grep`, `sed -n`, `git status`, `git log`, `git show`) and asks before writes, outside reads, environment dumps and network tools (`env`, `curl`, `gh auth`, `security`, `git push`, `git fetch`, `WebFetch`, `WebSearch`; this CLI offers no `Grep` or `Glob` tool). Because it otherwise runs `git remote -v`, `git config --get` and bare `git ls-remote` unasked, every open run passes explicit ask rules (`OPEN_RUN_ASKS`) covering git commands that read remotes, git config or credentials or contact a remote (`git -C`, `sh -c`, `&&` and `|` forms included; [claude-code-unasked.md](../validation/claude-code-unasked.md)). Uncopied runs in a folder whose `.git/config` holds or may hold embedded credentials (URL userinfo, `extraheader`, cookie file, client key, `core.askPass`, `credential` or `include` sections) are refused without echoing the value.
- **Settings isolation (`settingSources: []`):** Claude Code loads no settings files when running tasks or listing models so no settings `env` can override the stored key. Outside the closed profile, the person's global `CLAUDE.md` and Salidium and Seorak hooks are passed explicitly, while managed settings naming another credential refuse the run ([claude-code-settings-isolation.md](../validation/claude-code-settings-isolation.md)). No project-level `CLAUDE.md`, `CLAUDE.local.md`, `AGENTS.md`, `.claude` rules, skills, settings, hooks or `.mcp.json` servers load automatically ([claude-code-project-settings.md](../validation/claude-code-project-settings.md)); project text enters context only when the agent reads a file or when reviewed project rules are enabled.

Host-answered approvals ([ADR 0064](../decisions/0064-work-inside-a-confined-apps-own-copy-does-not-ask.md)) apply to confined runs in starter projects and to confined task copies of connected folders running under the closed Seatbelt profile (OpenCode and Codex, whose profile allows only the copy, its explicit read list, its isolated home and loopback, blocking the person's folder, home, keychain and network; `Add to Project` still requires review). Uncopied runs and Claude Code's open-network copies are never host-answered.

The agent apps Halora directs over ACP (Antigravity, Kiro and Cursor; [ADR 0067](../decisions/0067-agent-apps-that-speak-acp-are-directed-through-one-adapter.md), [acp-agents.md](../validation/acp-agents.md)) authenticate to their vendor services with the person's own sign-in and are never given a Halora-held key:
- **Fresh per-launch settings homes:** each launch gets a fresh settings home removed on exit (`--gemini-dir` under `.gemini/halora`, `KIRO_HOME` under `.kiro/halora`, `CURSOR_CONFIG_DIR` under `.cursor/halora` pre-seeded with `cli-config.json` setting `approvalMode: "allowlist"`, empty `allow` and `NO_OPEN_BROWSER=1`), with every other launch's settings home blocked so neither the person's global settings (such as Antigravity's `"toolPermission": "always-proceed"`) nor mutations from an earlier session carry over.
- **Agent-app Seatbelt profile and task copies:** on macOS each app runs under the agent-app profile, blocking host folders, push credentials, cloud credentials (`~/.aws`, `~/.config/gcloud`, `~/.azure`, `~/.kube`, `~/.docker/config.json`) and project-level app settings (`.agents`, `.gemini`, `.kiro`, `.amazonq`, `.cursor`, at any depth and in any case). When task copies are on ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)), the copy alone is writable, the person's folder (including `.git`) is read-only, sibling task copies are blocked, and `git-credential-*` helpers and the SSH agent socket are refused. The rest of the home directory, network and keychain remain open, so ACP runs record `confined: false`.
- **Kiro privacy option:** Kiro's `contentCollection` ("Allow content to be used for service improvement", enabled by default in a fresh `KIRO_HOME`) is set to `disabled` and read back before every task runs; any session that re-enables it has its turn stopped.

A project's rules file reaches an agent only where the person chose it ([ADR 0059](../decisions/0059-a-projects-own-rules-reach-a-task-only-as-the-person-read-and-chose-them.md)). The host shows it
whole first, takes the choice only with the review of the text as it reads now, and puts exactly that
text before each task's first instruction, marked as the project's own words. A file changed after it
was chosen is not passed on, and the choice turns off. The journal keeps the file's digest, never its
text. The file is read without following a link, at most 32 KB.

When the Codex runtime is enabled, its app-server also gets an explicitly built environment: an
allowlist (`PATH`, `HOME`, `USER`, `LOGNAME`, `SHELL`, the locale variables, `TMPDIR`, `TZ` and
the XDG directories), plus the names listed in `HALCYONIC_AGENT_ENV`, without the ones Codex signs
in with (`OPENAI_API_KEY`, `CODEX_API_KEY`, `CODEX_ACCESS_TOKEN`, `OPENAI_IDENTITY_TOKEN_FILE`,
`CODEX_CONNECTORS_TOKEN`, `CODEX_GITHUB_PERSONAL_ACCESS_TOKEN`), which the adapter also refuses.
Configuration may not set `CODEX_HOME`, `SALIDIUM_INTERNAL`, `CODEX_INTERNAL_ORIGINATOR_OVERRIDE`,
which would replace Halora's identity on its threads, or
`CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED`, which the adapter always sets so the server
never enables remote control, a second control channel through chatgpt.com.

Codex runs on models served on this Mac and, in a starter project's confined run, on a declared
hosted provider through a key gate that holds the person's key (the server holds a run credential,
its commands none; [agent-apps-hosted-providers.md](../validation/agent-apps-hosted-providers.md));
every other remote provider stays refused. It runs in a home of its own, never the person's
`~/.codex` ([ADR 0011](../decisions/0011-codex-app-server-stable-surface.md)). The control
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
while `config/read` still reports it off, because only some of their fields are projected into it
(`apply_exact_to_config`, `codex-rs/config/src/config_requirements.rs`). Right after every
launch, before anything is listed, and again at every start, the adapter checks that `config/read`
reports each of these settings, which catches the managed files, and that `configRequirements/read`
reports no requirements at all (null, "no requirements are configured",
`codex-rs/app-server-protocol/src/protocol/v2/config.rs`); otherwise it stops the server and
refuses. The same check refuses when `config/read`'s layers include one that applies whatever
Halora sets: a device profile's (`mdm`), an enterprise's, a managed file's, or the system
layer when it holds anything (Codex reports that layer whether or not `/etc/codex/config.toml`
exists). Before any launch, too, the adapter refuses when `/etc/codex` exists at all, where Codex
reads `config.toml` (below the overrides, but able to add MCP servers or providers),
`managed_config.toml`, `requirements.toml`, managed hooks (`hooks.json`,
`codex-rs/hooks/src/engine/discovery.rs`; hooks are on by default) and skills (`skills`,
`codex-rs/ext/skills/src/host_roots.rs`), or a `com.openai.codex` managed-preferences file under
`/Library/Managed Preferences`, machine-wide or for this user, where a device profile's forced
`config_toml_base64` and `requirements_toml_base64` live (`codex-rs/config/src/loader/macos.rs`).
Only whether each exists is read, and one that cannot be checked counts as present; that refusal
lifts once the path is gone. A refusal by the check after a launch, or at a start, which also
stops a running server and reports its threads lost, is remembered until the control plane
restarts, so no list or start launches Codex again; any other failure, such as a `config.toml`
Codex cannot read yet, is tried again by the next request. The check runs at launches and starts
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
`openai` by `openai_base_url` alone and `ollama` and `lmstudio` by their built-in address, because
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
moved away. Halora sets what keeps the person in control too: the working directory,
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
it `untrusted` in its own overrides, as a nested `projects` object because paths hold dots. A
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
reaches what a thread sends the provider, and the bundled skills are out of the catalog. The limit:
Codex still picks out a skill an instruction names, as `$name` or by path, and reads its `SKILL.md`
into the turn whatever these settings say (`codex-rs/ext/skills/src/selection.rs`), from an
untrusted project too. A name is any text of up to 64 characters (`codex-rs/skills/src/parser.rs`),
and a mention is `$` followed by letters, digits, `_`, `-` or `:`, all but a few common environment
variable names (`codex-rs/skills/src/mentions.rs`), so a repository can plant a skill named `5`
that `$5` in an instruction brings in.

Through Halora, a repository's text reaches the model in two ways only: through files the
model reads, which show as commands under the thread's sandbox and approvals, and through a skill
an instruction names with `$`. The second is the one way without a visible command, and the
defence is the approval policy for whatever the skill then asks. A project's `AGENTS.md` (and
`AGENTS.override.md`) is not loaded: Codex returns before reading any when the active project is
untrusted (`load_project_instructions`, `codex-rs/core/src/agents_md.rs` at rust-v0.157.0), and
the adapter marks every project untrusted. A fake-provider read confirmed it: a git project's
`AGENTS.md` text was absent from what a thread sent, and present in the control without the
untrusted marking ([local-models.md](../validation/local-models.md)).
The `$name` skill-selection behavior is accepted for owner-controlled repositories on this Mac;
mitigating it before broader distribution remains an open question
([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

Every request Codex sends to the model provider, the Ollama on this Mac, carries the originator
`halcyonic`, a user agent with the Codex version and the operating system, and turn metadata with
the installation id, the thread and session ids, the sandbox mode, whether analytics is on and,
for a workspace that is a git repository, its path, latest commit hash and whether it has
uncommitted changes; the working directory also reaches the provider in the conversation's
environment context. Codex writes each thread's rollout to Halora's Codex home, tagged
`halcyonic`, where neither Salidium nor Seorak reads it unless configured to: Salidium versions
newer than 0.8.3 read the Codex homes and OpenCode stores named in `SALIDIUM_EXTRA_CODEX_HOMES` and
`SALIDIUM_EXTRA_OPENCODE_STORES`, and Seorak builds supporting `SEORAK_CODEX_EXTRA_DIRS` tail the
Codex sessions folders named there alongside their defaults (configured by the person; Halora
modifies neither tool's configuration). For a Codex execution whose rollout it finds there (checking
only that a matching thread-id file exists in the date folders around the execution's start,
following no link), the control plane reads Salidium's version from its discovery file without a
credential and queries it only past 0.8.3; Seorak names no version, so it is queried with its
credential. A miss there reads as `own_sources_not_set_up`, never as a fault. The
adapter writes no logs. The Codex and OpenCode adapters drain their server's error output without
keeping it, as the Claude Code adapter does: it may hold secrets, such as a key that verbose
logging or a configuration error prints, and a start failure is journaled and shown on every
device. A start failure says the exit status and how to see the output: run the binary in a
terminal on the Mac.

When the OpenCode runtime is enabled, its server gets an explicit environment allowlist plus
`HALCYONIC_AGENT_ENV`:
- **Network behavior and session rules:** OpenCode 2.0.18 fetches its model catalog from
  `models.opencode.ai` at launch and every five minutes unless `OPENCODE_DISABLE_MODELS_FETCH=true`
  is passed via `HALCYONIC_AGENT_ENV`, and downloads ripgrep from GitHub on first file search if
  `rg` is absent from `PATH` ([local-models.md](../validation/local-models.md)). Without Halora's
  overrides, OpenCode defaults to OpenCode Zen and runs tools without asking. Every session Halora
  creates carries explicit rules outranking configuration files and saved `"always"` grants:
  `execute` (Code Mode), `webfetch`, `websearch`, `subagent`, hidden paths in any letter case
  (including `.git`) and `opencode.json` are denied
  ([opencode-permissions.md](../validation/opencode-permissions.md)); shell commands require
  approval (showing working directory and full command, or offering `Deny` alone when unknown);
  edits inside the task folder proceed while external edits ask by path. Repository OpenCode config,
  `.opencode` plugins, MCP servers, `.claude` and `.agents` are skipped via OpenCode's directory
  flag. Because OpenCode's shell ask depends on its shell parser and user plugins (when unsandboxed)
  can alter commands, Halora treats the ask as human direction rather than a security boundary.
- **macOS Seatbelt sandbox ([ADR 0028](../decisions/0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md), [ADR 0048](../decisions/0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md), [opencode-sandbox.md](../validation/opencode-sandbox.md)):**
  each execution folder runs on its own OpenCode server (plus a no-write server for model listing)
  inside Halora's closed-by-default Seatbelt profile. Outbound network is restricted to the
  server's own loopback port and Halora's loopback Ollama gate (passing only model list, model
  details and local model chat; refusing `pull`, `push` and non-JSON or browser-origin requests).
  Reads are restricted to the execution folder, isolated server data/state/cache/temp folders under
  `<data dir>`, Halora's OpenCode config, the pinned binary, tool folders, and declared starter
  dependencies or VR-copy originals (minus secret-shaped files), blocking the person's home, other
  projects and `<data dir>`. The adapter refuses to launch OpenCode unsandboxed on macOS and logs a
  warning on other platforms.
- **Server password and isolated config home:** `OPENCODE_PASSWORD` is omitted from session command
  environments, though same-user processes can read the server's initial environment (`ps -wwE`);
  the adapter stops any task whose rules or approvals change externally and stops the server if a
  foreign session appears. Halora always answers `"once"`. When `HALCYONIC_OPENCODE_CONFIG_HOME` is
  set (`pnpm mac-setup local-model`), OpenCode receives it as `XDG_CONFIG_HOME`; startup verifies
  mode `0700` folders and a regular mode `0600` `opencode/opencode.json` naming only `model`,
  `small_model`, `permissions` and Ollama context limits for models served on this Mac.
- **Leftover background process sweeps (`packages/seatbelt/src/leftovers.ts`):** background commands
  can outlive a turn or server differently across runtimes (OpenCode re-parents `&` children to
  launchd; Codex 0.157.0 terminates process groups except `setsid` sessions; Claude Code leaves `&`
  children running). When a confined OpenCode server stops or rests, a Codex server stops, or a
  Claude Code session ends permanently (never mid-relaunch), each adapter sweeps leftover processes
  immediately and again a few seconds later. A process is signalled (`SIGTERM`, then `SIGKILL`,
  re-verified before each) only when all five conditions hold: owned by this user, `cwd` at or under
  the run's folder, started after the server or session launched, parented by launchd, and
  unreadable process environment (which macOS enforces on sandboxed processes under Halora's
  profiles and Codex's sandbox). Halora's own preview and play servers remain children of the
  control plane and are never swept. Processes that changed `cwd` outside the folder, orphaned after
  the second sweep, started under a prior control plane process, or launched by the user with
  `env -i` and orphaned inside the folder fall outside that distinction.

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
Halora's own notes go only as system messages, with the rule repeated after the exchange, and no
model tried kept injected text out of everything it said: the review is the boundary
([companion-model.md](../validation/companion-model.md)). So the review counts a line as read only
once the headset has drawn it, holds `Yes` until every line of the layout showing has been, and reads
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
  not a revocable device credential; anyone with adb on a screen-active headset can read it with
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
  `ssh -L`, `socat` or a proxy on the Mac. Keep exactly one mapping involving Halora's port
  (`adb reverse --list`), 47800 to 47800, and nothing else forwarding to 47800. Independent routes
  for other applications do not relay this proof and must remain intact. The session check rejects
  duplicate Halora mappings, alternate endpoints forwarding to 47800 and a 47800 endpoint
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
| Token storage | 32 random bytes in `<data dir>/access-token`, mode 0600; created once; never logged; `authorization` headers are redacted from logs. On a headset (a development build reaching the computer over USB) it lives in the app's private storage (`Context.getFilesDir()`), written there with `run-as` straight from the computer's file, whole and made mode 0600 before it takes the old one's place, never on shared storage. Legacy tokens found in `/sdcard/Android/data/com.halcyonic.xr/files` are migrated to private storage on development startup before reading a pairing and deleted unread on release startup: only a regular file of at most 64 bytes holding a 43-character base64url token is read (`lstat`, opened `O_NOFOLLOW | O_NONBLOCK`, verified with `fstat`, nothing inside a symlinked folder read or removed; the parent folder is checked once with `lstat` before open). Unexpected syscall errors stop the migration and log the call and errno; `chmod` failures log without blocking migration; mode `0600` is applied before writing into private storage; unremovable `shell`-owned files log the removal command so the owner can rotate the token ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md)); not yet exercised on a physical headset |
| Logs and error text | **Logged objects:** `authorization`, `cookie`, `x-halcyonic-proof` and `x-halcyonic-challenge` headers are redacted wherever headers are logged; errors log only type, code and stack frames (never `message` or fields such as Node's `rawPacket`), except fixed-word `OwnWordsError` instances (settings, contract issue paths, speech engine state). `tooling/log-calls.test.ts` fails on unreviewed private-data log calls ([logging-audit.md](../validation/logging-audit.md)). **Runtime and provider error text (`core/redaction.ts`):** turn failures, refused actions, lost connections, Seorak reason text and labels, model-listing failures and checked-page exceptions first replace exact copies of every secret Halora holds or passes to a runtime with named markers such as `[redacted: Anthropic key]` (covering `access-token`, all `HostProviders` keys from env or regular files, OpenCode's server password, Salidium/Seorak/Omnorium credentials, the joined and line-by-line network TLS key, and secret-named or credential-shaped `HALCYONIC_AGENT_ENV` values including header values, URL userinfo up to the last `@`, keyed parameters such as `password`, `pwd`, `passwd`, `passphrase`, `secret`, `token`, `api_key`, `access_key`, `credentials`, `sig`, and CLI flags such as `--password`; bare absolute paths are not treated as random secrets), then replace remaining credential-shaped runs with `[redacted]` within the first 4,096 characters before cutting to the contract bound. **Decision surfaces:** tool titles, approval summaries, question prompts, test labels/summaries and agent messages redact only exact held copies so approved commands are never guessed at, while question options stay verbatim. Adapters emit fields whole; the control plane redacts first, then cuts in code points with `[truncated]` (and fits questions via `fitQuestion` within 16,000 characters, marking any cut question unanswerable; [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)) |
| Network listener | Off unless `HALCYONIC_NETWORK_HOST` names an IP address; TLS only (1.2 or later), with a self-signed ECDSA P-256 certificate generated once into `<data dir>/network-key.pem` and `network-certificate.pem`, mode 0600; the key is never logged; 5 seconds for the TLS handshake, 10 for a whole request, 60 for a connection on which nothing moves, 5 between requests; 128 connections at once, 40 from one address (a headset's realtime connections, requests and up to three live project screens), and 4 realtime connections per device; a connection past a limit, within 8 more, finishes its handshake and is answered 503 `too_many_connections` in words, then closed, and one past that is refused before its handshake and logged; a WebSocket it closes waits a second for the client's answer |
| Loopback listeners | The control plane's own listener, the pairing and setup page and each play's viewer bound what any local process of any user can hold: 10 seconds for a request's headers, 10 for a whole request (two minutes on the page, which takes a photo or video in one request), 60 for a connection on which nothing moves, 5 between requests; 64 connections at once on the control plane, 8 on the page and on each viewer, none per address (every local client is 127.0.0.1). An upgraded WebSocket is outside the idle bound (ws turns its socket's timeout off): it counts toward the connections until it closes, and one that stops answering is ended, a realtime connection when it misses the 15-second heartbeat, a viewer's stream after two missed 15-second pings. A local client can still fill the connections and keep others waiting a while, as it could anyway as the same user; the bounds keep the control plane's descriptors for its journal and the agents it starts |
| Watch It Work | While a turn runs, the host watches the run's folder recursively for names only: nothing is opened or followed, `.git` and `node_modules` at the top are left out, a folder that holds the host's own data is not watched, and the watch ends with the turn, at a stop and when the control plane closes. It keeps the newest 20 names and a count (up to 10,000), never every name. A task's file is read during its turn only in exactly that task's own copy (the one exception to the copies' folder being refused; the data folder and every other copy still are), only for a path the host saw change in that run, at most 4 reads a second, through the one reader (no link anywhere in the path, secret-shaped names withheld, 256 KiB, redacted). Names are the agent's text and are drawn as text |
| Host approval answers and denials | In an execution recorded confined, a request inside the app's own copy, a read of its declared original, a read inside the run's own temporary folder (`run_temp_read`) and a shell command inside the closed profile are answered by the host without a card and recorded as `execution.approval.answered_by_host` ([ADR 0064](../decisions/0064-work-inside-a-confined-apps-own-copy-does-not-ask.md)). In any run working in a task's own copy, a complete structured request (`ApprovalSubject.paths`) whose resolved targets lie inside the task copies' folder (`another_task_copy`) or a host folder (`host_folder`) is denied automatically by the host with actionable guidance (`execution.approval.denied_by_host`), never shown as an unachievable choice. Across all runs, requests over 2,000 approvable characters (kept up to 32,000) or requests the journal could not record (`not_recorded`) are denied by the host with literal marks (`‹U+XXXX›`, `⇥`, `↵`, [ADR 0065](../decisions/0065-a-request-too-long-to-show-whole-is-denied-by-the-host-with-its-reason.md)) |
| What Halora adds to an instruction | After the person's words, marked as the Mac's own lines: a version's note ([ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)), the project's media note ([ADR 0066](../decisions/0066-a-persons-photo-or-video-goes-into-the-projects-folder-as-a-copy-without-its-place.md)), a failed check's first lines, the project's rules, and on every start and every instruction the closing request (`CLOSING_REQUEST` in `closing-words.ts`): it asks the agent to end its last message with up to four labelled lines (`Did:`, `Why:`, `How:`, `Left:`), one plain sentence each for someone who has never written code, and to leave out any line it has nothing true to say for rather than guess. A line that only echoes the request's own words reads as left out. It carries no data of the person's or the project's. What comes back is the agent's own text, redacted like every agent text, cut to 300 characters a place, journaled `reported` (`execution.closing_words`) and never counted as evidence |
| Pairing | Opened only from loopback with the access token (`pnpm pair`), one window at a time, for five minutes, closed by the first device that pairs, by three failed proofs, or by the owner; an eight-digit code, never logged or journaled; SRP-6a (RFC 5054, 3072-bit group, SHA-256) with the TLS certificate bound into both proofs; the salt and verifier derived once per window, so no exchange's work depends on the code; `/pair` refused outside a window before any cryptography; four exchanges at once, one per address, six connections per address a minute, 30 seconds each; what it turns away or cuts short without checking a code is counted by address and shown by `pnpm pair` |
| Pairing page | [ADR 0047](../decisions/0047-pairing-starts-from-a-page-on-the-mac.md). A server of its own on 127.0.0.1, port 47802 to 47809 (the control plane's port plus 2 to plus 9; a free port for a plane on port 0), in the control plane's process, holding no access token and no device credential. A link from `POST /api/pairing/page` (loopback, access token) carries a 32-byte one-time code good for 60 seconds, traded for an HttpOnly, SameSite=Strict cookie for its port alone (8 browsers at most), every cookie of that name read so a planted one keeps nobody out; the link is handed to the browser in a mode-600 file in `<data dir>/open-links`, opened by its path and removed after a minute, never in a process's arguments; every request must name `127.0.0.1:<port>` as Host, a GET come from no page or its own origin (`Sec-Fetch-Site` none or same-origin) except the trade of a code, a top-level navigation from any site, and a POST carry its own origin and come from it; a strict CSP, no caching, no referrer. A GET with no `Sec-Fetch-Site` at all is refused with words that say why (an older browser). It can turn the network listener on and off, open a pairing window and show that window's code, only beside an address a headset can reach (never a loopback one), and forget a device; it serves its fonts itself (`font-src 'self'`); run-out hand-over files are swept by the control plane each minute and at each new link; nothing at load or start, only on a press. It reads no project, command or journal entry, and the code it shows is never logged |
| Preview relay | [ADR 0052](../decisions/0052-a-preview-reaches-a-paired-headset-through-a-loopback-relay.md). On the network listener, to a paired device only: `GET`/`HEAD /api/projects/:id/preview/...` asks `127.0.0.1` at that project's preview port and nowhere else, per request only while the preview serves the latest check, passed, with `reads_confined`, as the host's own record of its server says; `GET`/`HEAD /api/projects/:id/tasks/:task/preview/...` does the same for that task copy's preview on a path prefix so page queries pass untouched; forwards only `Accept`, `Accept-Language`, `Range`, `If-None-Match`, `If-Modified-Since` and returns `no-store` and `nosniff`, never cookies or CORS headers; refuses service worker scripts; 32 MiB and 15 s per file. Headset `PreviewRelay` uses `GET /api/projects/:id/preview-stream` (next row), while the HTTP file routes remain available for single-request reads. The relay closes as soon as the browser leaves the preview |
| Preview stream | [ADR 0052](../decisions/0052-a-preview-reaches-a-paired-headset-through-a-loopback-relay.md). On the network listener, to a paired device only, a WebSocket upgrade `GET /api/projects/:id/preview-stream`, admitted by the same gate as the file route (the preview serves the latest check, passed, `reads_confined`, by the host's own record); an optional `task` query (a valid task id, else refused) checks that task copy's preview, latest check and server record instead; the Mac then dials only `127.0.0.1` at the port the gate names, never one the request names, and pipes bytes both ways. Each open stream re-checks the gate and device pairing every second and closes immediately if either fails, after 60 s idle, or when the listener closes. A device holds at most 18 streams at once (bounded also by the listener's 40 connections per address). On the headset `PreviewRelay` reads each browser connection's first request head, requires `Host: 127.0.0.1:<its port>` and no `Service-Worker`, forwards it with `Connection: close` (WebSocket upgrades keep their connection), and carries the connection in binary frames; the device credential is on the upgrade only, never in browser bytes. Development builds only (`RelayInDevelopmentBuilds`) |
| Device credentials | 32 random bytes per device (`hlcd_` and base64url), sent once, encrypted under the SRP session key inside TLS; the journal keeps only their SHA-256; revoked from loopback (`pnpm devices revoke`) or by the device itself, at once and for what is already open: its realtime connections handle nothing more and are cut off, its commands are rejected where they would act (`device_revoked`), and any answer to a request it opened before is replaced by 401 |
| Failed credentials | 30 refused credentials from one address in a minute, and it gets 429 for the rest of the minute |
| Content types | JSON only; `text/plain` and form bodies are refused with 415. The one exception, `POST /api/transcriptions`, takes `audio/wav` only, through a parser registered in a scope of its own so no other route gains it, and refuses JSON |
| Input validation | Every command, client message and query is validated against the contracts |
| Size limits | 1 MiB request bodies; 256 KiB for a companion request, whose exchange holds at most 20 messages and 24,000 characters; 960,044 bytes for a clip of speech, refused with 413 from its declared length before the body is read, or as soon as a chunked body passes it; 256 KiB WebSocket messages; slow WebSocket clients are disconnected |
| Data at rest | Data directory mode 0700; journal, WAL and SHM files mode 0600 |
| Host settings | `<data dir>/settings.json`, written by `pnpm mac-setup`, fills in the `HALCYONIC_` variables the environment leaves unset ([ADR 0024](../decisions/0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md)). Startup is refused unless it is a regular file opened without following a link, owned by the user running the control plane, mode 0600, at most 64 KiB, in a data directory that user owns and that is closed to others (mode 0700), with `"format": 1` and only known settings; anything but a regular file, such as a named pipe, is refused before it is opened. It may hold the project roots, the pinned OpenCode and Codex binaries, Halora's own OpenCode settings, the voice files, the companion's model and its loopback Ollama, and the network listener's address, never `HALCYONIC_CLAUDE_AGENT`, `HALCYONIC_CLAUDE_EXECUTABLE`, `HALCYONIC_AGENT_ENV` or a model that can run on a remote service (a companion model with a `cloud` tag, or Ollama anywhere but loopback, stops startup), so a hand edit cannot start paid model use or pass a variable to agents. Its values are checked like the environment's, and the ready log names the settings taken from it and each OpenCode and Codex binary's path with whether its SHA-256 is the pinned one (a warning when it is not) |
| Project roots | Every root, from the environment or the settings file, must be an existing folder that may hold projects, or the control plane does not start (`folder-safety.ts`): never `/`, `/Users`, `/Volumes` or a whole drive in it, `/private`, `/var`, `/tmp` or `/opt`; nothing in `/System`, `/Library`, `/Applications`, `/usr`, `/bin`, `/sbin`, `/etc` (`/private/etc`), `/dev`, `/opt/homebrew` or `/Users/Shared`; nothing in `/private/var` but a folder made inside a user's own temporary folder (`/private/var/folders/xx/yyy/T`); not another person's home, the home folder (the account's own, not `$HOME`, which a launcher may change) or a folder holding it, Halora's data or a folder holding it or in it, a hidden folder or Library in the home folder, a folder another user owns, or a folder any user can change |
| Logging | Log context carries identifiers only, never tokens, pairing codes, device credentials or their hashes, keys, instructions, agent text, clips of speech or their transcripts, or a companion's exchange or reply (only its outcome, view, times and token counts) |
| Agent working directories | Every real execution runs in its project's folder or task copy, and clients never send paths ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)). Projects bind to folders chosen from `HALCYONIC_PROJECT_ROOTS` via `realpath(3)`, refusing symlinks, hidden names and `..`; the directory policy is re-checked at admission, before process launch, and immediately before handing the folder to the runtime (including OpenCode directory model reads and Codex thread resumes). With no roots configured, no real runtime starts. When task copies are on (`HALCYONIC_TASK_COPIES=1`, [ADR 0045](../decisions/0045-each-task-works-in-a-copy-of-its-own.md)), any task whose runtime holds its given folder (`RuntimeAdapter.holdsFolder`: on macOS, Codex, OpenCode, Claude Code under the closed starter profile or connected-folder copy profile, and ACP agent apps under the agent-app copy profile) works in an isolated copy inside `HALCYONIC_TASKS_DIR` beside `<data dir>`, with URL userinfo stripped from remote addresses; runtimes that cannot hold a folder (such as unsandboxed runs off macOS) run in the project folder one at a time and are refused a copy. `Add to Project` merges only the reviewed tree digest and refuses copies modified afterwards. Copies are created only by the host and refused when `.git` is a file, when the folder holds host data or task copies, or when a checkpoint cannot capture the folder whole |
| Project folders | The host makes a new folder only directly inside a root, with a one-segment name of letters, digits, `.`, `_` and `-` that does not start with `.`, and a non-recursive `mkdir` that fails rather than follow or reuse anything already at the path. Right before it binds or makes anything, it checks that the root is still the folder it found at startup (same real path, no symbolic link along it, same device and inode); a root replaced afterwards, by a link or another folder, is refused until the control plane restarts. A folder it made that then fails the policy is left in place and reported, with effect `unknown` and the folder's real path. One race remains: a root swapped for a link between that check and the `mkdir`, which takes a path, gets the new, empty folder made where the link leads; the failure then names it. It deletes no folder |
| Instruction pictures | An instruction may carry up to four pictures of the project's folder, named by a project-relative path, never bytes or a path from the client, and only for a runtime that declares `takes_pictures` ([ADR 0058](../decisions/0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md)). The host reads each once, as the project's file read does: inside the folder, never through a link anywhere in its path (`O_NOFOLLOW_ANY` on macOS), never a secret-shaped name or Halora's own folders, judged and read through the one handle, at most 5 MB, PNG, JPEG or WebP by its first bytes and whole by its structure. The runtime is handed those bytes (a `data:` URL or a base64 block), never a path, so no runtime reads the file again and what it was given is what `execution.instruction.pictures_read` names by SHA-256; the journal never holds a picture's bytes. A picture still shows the model whatever the folder holds, as any file an agent reads does; the person chose it. The list read (`GET /api/projects/:project_id/pictures`) walks at most 8 folders deep and 20,000 entries, and skips hidden names, `node_modules`, links and Halora's own folders |
| Given pictures | `POST /api/pictures` (local or a paired device, behind the same principal check as every route) keeps one whole PNG, JPEG or WebP of at most 5 MiB by its SHA-256 in `<data dir>/pictures` (0700, files 0600), 64 at most for 24 hours; never journaled, never logged. An attachment names one by its id; the host reads it again, checks its digest and that it is whole, and hands the runtime the bytes ([ADR 0058](../decisions/0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md)) |
| Photos and videos ([ADR 0066](../decisions/0066-a-persons-photo-or-video-goes-into-the-projects-folder-as-a-copy-without-its-place.md)) | Taken only on the Mac's own page (`/setup/media`): the page's session, Host, Origin and Sec-Fetch-Site are checked before a byte of the body is read; raw bytes only, one upload at a time, at most 80 MB, streamed to `<data dir>/media` (0700, files 0600) with the digest taken as it arrives, checked by structure, kept at most 4 files for 2 hours, never journaled or logged. What is placed in the project is a copy with no tags (a video remuxed to its first video and audio streams, a photo re-encoded), so where and when it was taken and the device never reach checkpoints, task copies, GitHub or a share; the journal holds paths, types, sizes and digests only. The host writes only its own names under `media/`, with `O_EXCL` and `O_NOFOLLOW_ANY`, every folder of `media/` checked a real folder where it should be, never in a folder of its own, never over a file; only while no task, restore, check or other folder command of the project runs, and within 50 media and 240 MB. `ffmpeg` runs only at the absolute path and SHA-256 the person recorded (`pnpm mac-setup ffmpeg`), by arguments, no shell, a time bound, four environment variables, under a Seatbelt profile that denies all but reading its one input by literal path and the libraries and writing one fresh folder of the data folder; its output is checked before the host places it, and what it prints is never logged. The pin covers the executable's own bytes, not the Homebrew libraries it loads: a residual of a local tool run with no network. A stop mid-write is cleaned at the next start only where the noted file is still the host's own name, a regular file, no larger than noted, and its medium was never recorded. The page draws a chosen file's first frame in the browser before sending it (a photo decoded by `createImageBitmap`, a video from a `blob:` URL of the page's own making, revoked once read), so the setup server's CSP allows `media-src 'self' blob:` and nothing more; the frame never leaves the page. The headset reads a still only through `GET /api/projects/:project_id/picture`, the picture reader of [ADR 0058](../decisions/0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md) |
| Agent permissions | Runtime permission modes that bypass human review (`bypassPermissions`, `auto`) are refused as start options, as are Codex's `never`, granular policies and `danger-full-access` with `on-request`. Two bounded cases run commands without asking: (1) on a starter project confined inside Halora's closed Seatbelt profile, Codex runs commands inside the project under `externalSandbox` without asking, while other confined folders on macOS ask under `untrusted` when started at `ask` ([ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md)); (2) `approval_level: 'never_ask'` ([ADR 0043](../decisions/0043-a-person-may-let-a-tasks-agent-work-without-asking-where-its-runtime-keeps-it-in-bounds.md); control plane default remains `ask`, while headset start screens that display the level default to `never_ask` per project until the person chooses `Ask First`), admitted only when `RuntimeDescriptor.offers_never_ask` is true: Codex 0.157.0 (`never` with `workspace-write`, `user` reviewer, no network, writable folder pinned to the project, refused if its home names an MCP server) and OpenCode inside Halora's Seatbelt sandbox on macOS (per-folder server, shell allowed, other asks denied); Claude Code and ACP agent apps do not offer it. Nested bound sub-projects inside a run's folder are denied read and write access, and `never_ask` is refused where the platform lacks Seatbelt to enforce that exclusion (Codex off macOS). Incomplete requests (`approvable: false`) can only be denied (`approval_not_whole`), and `approval_may_leave_sandbox` states in host words whether an approved command can run outside any sandbox ([ADR 0033](../decisions/0033-approvals-declare-whether-commands-leave-the-sandbox.md)) |
| Agent processes | Stopped on close and when the control plane exits, including on a second signal during shutdown. Every Claude Code process and the OpenCode and Codex servers are recorded before they receive work and watched by a small process that stops them if the control plane dies, even by SIGKILL; the next start stops anything recorded that survived. Identity is checked before any signal. Codex starts each command in a session of its own, beyond the reach of a signal to its server's process group: ending the server's input makes Codex stop them, and a server that has to be killed is killed with all its descendants. A Codex server killed by anything else leaves its running commands behind |
| OpenCode server | Launched from the configured binary only, never from PATH; bound to 127.0.0.1 on a free port with a password generated per launch and kept in memory; refused unless it reports version 2.0.18 and the process id Halora started; recorded (without the password, mode 0600) so the next start stops it after a crash, and watched by a small process that stops it if the control plane dies; on macOS, only inside Halora's sandbox ([ADR 0028](../decisions/0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md)), its profile written beside the record at each launch (mode 0600) |
| Turn file readings | The control plane reads every file of a task's folder (excluding top-level `.git` and `node_modules`; each file opened without following a link and read only if regular, and on macOS with `O_NOFOLLOW_ANY` so a parent folder swapped for a link after listing leads nowhere; each subfolder verified as a real directory at its real path before and after listing; cached entries reused only while size, mtime, ctime, inode and device match; at most 20,000 files and 256 MB; folders holding Halora's own data refused as `holds_host_data`; 10 s start budget) at task start, before a turn started from rest, and after each turn, keeping at most 100,000 file entries in memory across active baselines and journaling relative paths, sizes and line counts ([ADR 0038](../decisions/0038-the-mac-observes-each-turns-file-changes-itself.md)), never contents. Files are hashed and line-counted only, never executed or parsed |
| World description readings | For a project whose starter declares the file that describes it (`"describes"` in its `starter.json`: the `world` starter's `world.json`), and no other, the control plane reads that one file an agent may have written, at the top of the project's own folder as the host resolved it, and parses it ([ADR 0049](../decisions/0049-a-persons-world-is-a-project-whose-description-the-mac-reads.md)): opened without following a link (on macOS anywhere in its path), never waited on, read only if what was opened is a regular file of at most 16 KiB (one byte more is read, so a file that grew is refused), decoded as UTF-8, parsed as JSON and validated against `WorldDescription`, which admits only ranged numbers, six-digit hexadecimal colours, two capped lists, names of lower case letters, digits and underscores that must be its scene's own (its manifest in `starters/world/scenes/`, each light once, each place at most once) and a project's name of at most 200 characters, with no member that could hold a path, an address or code. What is journaled is rebuilt member by member in the contract's order; a refusal carries a code, where the first rule broke by the contract's own member names and list positions, and the Mac's own sentence. Nothing in the file is run, opened or followed, and no word of it enters a message or a log line. A place's project name is only compared with project names, never drawn or used as a path. A client must validate again before drawing, because the generated bindings do not enforce ranges or patterns |
| Bringing a world forward | The one place the Mac writes into a world project's folder ([ADR 0049](../decisions/0049-a-persons-world-is-a-project-whose-description-the-mac-reads.md)): when what it read is a format 1 description, it first keeps that file whole beside it under a name made of its own and the first twelve hexadecimal digits of its SHA-256 (never written over: a copy already there is left, and a different file under that name stops the step), then writes the format 2 `world.json` it derived and, only when the folder's `AGENTS.md` is byte for byte the format 1 guide the starter wrote (by its SHA-256), the starter's current guide. Each file is written whole under a new random name beside it, opened exclusively and through no link, synced, then renamed over the old name, so a reader sees the old file or the new one; nothing else in the folder is touched, nothing is deleted, and a guide anyone changed is kept. The event names only the format, the kept copy's name, the scene and whether the guide was replaced |
| Starters | **Template and dependencies ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)):** starters are tracked in the repository, installed into `<data dir>/starters` from the public npm registry with `--ignore-scripts` and lockfile integrity hashes, and counted installed only while matching the tracked lockfile's SHA-256; projects copy files/folders only and symlink `node_modules` to the read-only shared install. **Vite build and origin isolation:** the host's Vite config ignores project config, env and PostCSS files (PostCSS is inline), serves dependencies only from `HALCYONIC_PREVIEW_DEPS` (never following a repointed `node_modules` link), and blocks module loads outside the project, shared dependencies and Vite cache while the build's closed Seatbelt profile restricts asset reads to the same paths. Preview and dev servers return 404 to `Service-Worker` fetches, send `Cache-Control: no-store` on every response and `Clear-Site-Data: "storage", "cache"` on top-level documents (cookies on `127.0.0.1` span ports and are not cleared), disable CORS (`cors: false`), rewrite `@iwsdk/xr-input` controller/hand model URLs from `cdn.jsdelivr.net` to verified local files listed in `starters/iwsdk-vr/host/input-profiles.json`, and enforce `connect-src 'self' data:` (blocking external decoder fetches such as `unpkg.com`). Nothing bundling `@drawcall/uikitml` (no stated licence) is published until the owner decides. **VR copies and `reads_confined` ([ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md)):** `reads_confined` is true only when every execution in the project recorded `confined: true` at start; `project.make_vr_version` allows read-only access to the single-project original folder while blocking its `.git`, secret-shaped files and Halora's data folder. **Confined Codex and check sandboxes (`apps/control-plane/src/preview/check.ts`):** on macOS, Codex runs per-folder confined servers under `externalSandbox` inside Halora's closed Seatbelt profile (verified at launch with seven escape canaries: `<data dir>`, open Codex home, `/private/tmp`, pasteboard, LaunchServices, wildcard bind, package-manager `var`/`etc`), and starter checks run type-check, build and preview under a networkless Seatbelt profile reading only paths a confined agent may read. **Headless browser, disclaimer launcher and stepped-clock twin:** checks open the built copy in a manifest-pinned copy of `chrome-headless-shell` (`pins.ts`, `preview/browser-manifest.txt`, never the person's Chrome) in its own session over a pipe under a dedicated Seatbelt profile (loopback preview port only, no `lsopen`, `/dev/tty*`, external signals, home folders, drives or app caches; only four Mach/IOKit GPU and rendezvous services) launched via `native/disclaim.c` (`responsibility_spawnattrs_setdisclaim`, recording `own_permissions: false` if absent). Findings are computed in the host from decoded PNGs and DevTools DOM/WebXR closures (messages over 16 MB mark the check unknown), `src/` scans use `O_NOFOLLOW_ANY` (up to 500 files of 1 MB), and twin replay steps an injected deterministic clock via captured `F24` events over hidden bindings |
| A project's own check | [ADR 0055](../decisions/0055-a-projects-own-check-is-chosen-by-the-person-and-confined-as-an-agent.md). A project not made from a starter is checked only by the scripts of its own `package.json` named `test`, `check`, `typecheck`, `lint` or `build`, read at the folder's top without following a link, at most 256 KiB; the person reads each command before choosing (`project.own_check.choose`, review required), and a command changed afterwards, by anyone, is not run (its SHA-256 as written is compared before every run). A command is journaled and shown with any secret Halora holds taken out. Each script runs as `<manager> run -s <name>`, the manager the lockfile names and only one on the host's own PATH, inside the agents' closed profile (`confinedProfile`): reads of the folder and the tools' folders, writes only in the folder and a home and temporary folder of the check's own under the data folder, Halora's data folder hidden, every other bound project inside refused, no network, no port unless the person chose otherwise (below); standard input closed, no variable of the host's but PATH; its output read over pipes, kept beside the check and never journaled or logged; its whole process group stopped after 10 minutes, at its own exit and when the control plane closes, a crash's left run stopped at the next start by its pid record. Only the chosen script runs: settings in its environment outrank a project's `.npmrc` (no pre or post script, the system shell, an empty user and global config), and its home and temporary folder are emptied for every run. The choice carries each command's SHA-256 as the person read it. No turn runs in the folder while its check does. A run whose failed script's last lines name a refusal of the sandbox reads `refused` and is never sent to an agent; a run whose pipes outlived it, or whose folder changed just after it, reads `left_running`. What a check writes in the folder is recorded as its own. A child a script starts in a new session escapes the group stop and can write in the folder later, inside the profile. A script can still do inside the folder anything the agent could, and it runs whatever the person chose: the choice is theirs. The person may also let the check open ports (`open_ports`, chosen with the scripts, never by default, never by an agent or the assistant, and never for the agent's own run; submitted only from the person's own press carrying the option's rebuilt digest): it may listen on loopback and connect to loopback over TCP alone (UDP and Unix sockets stay refused), and to none of the ports listening when that run starts (read fresh from `netstat -anp tcp` for every run) nor a fixed set (`adb` 5037 to 5039, 9222, 4317, 11434, Halora's own 47800 to 47999); it still reaches no outside network. Where the listeners cannot be read, that run opens no port. Two gaps remain and are stated before the person chooses: Seatbelt cannot restrict `bind`/`listen` to loopback, so a test server listening on `0.0.0.0` is reachable from the local network while the check runs; and a local service that starts listening after the check begins is reachable on loopback |
| Playing on the Mac | Off unless the owner runs `pnpm mac-setup browser`, which sets `HALCYONIC_BROWSER` and builds the launcher. A checked game plays only in the manifest-pinned copy of `chrome-headless-shell` in `<data dir>/browser`, never in the person's Chrome and never from the source folder: the whole folder manifest (kind, mode, path and SHA-256 of every entry, symlinks refused) is verified before and after copying and at every startup, replacing any mismatched copy only from a verified source. Played through the disclaimer launcher so the browser inherits none of the parent app's macOS permissions. `project.open_on_mac` requires a passed, current check with laptop readiness, `reads_confined`, and a running preview serving that check. Runs inside the check's Seatbelt profile in its own session over a pipe plus three CoreAudio output services, with camera/microphone denied in Seatbelt, `--use-fake-device-for-media-stream`, `--deny-permission-prompts`, fifteen web permissions blocked in browser settings and `Browser.setPermission`, and WebGPU, media router and built-in DNS disabled. Frames stream to a dedicated viewer server per play on `127.0.0.1` (ports 48020 to 48039) holding no access token; its link opens via a mode-`0600` hand-over file (`openLinkPrivately`, never CLI arguments) carrying a 60 s single-use code exchanged for a port-scoped `HttpOnly`, `SameSite=Strict` cookie, enforcing exact `Host`, `Origin` and `Sec-Fetch-Site` checks, `Cache-Control: no-store` and a self-only CSP (`preview/viewer/`), allowing a 5 s WebSocket reconnect window. Sound state is read from CoreAudio (`native/audio-output.c`, `kAudioProcessPropertyIsRunningOutput`), never from page claims, and viewer input forwards only validated mouse events (within 1280 by 720) and key events (up to 24 characters) ([ADR 0037](../decisions/0037-new-vr-apps-start-from-a-host-vetted-web-starter.md)) |
| Sharing a checked game | [ADR 0054](../decisions/0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md). Only by the person's press, only a passed, current check whose agent runs were all confined, and only the one built copy the running preview serves, read only. A link: the pinned `cloudflared` (its bytes checked at every start) under Seatbelt, writing only its own home, reading nothing of the data folder and no system configuration of its own; recorded only once its address answers over verified TLS. On this Wi-Fi: a listener on the Mac's private address, ports 48040 to 48059, passing only `GET` and `HEAD`, no body, no cookie, at most 32 connections. Either stops with the person's press, the preview, its own end and the control plane; none survives a restart. The headset is given a link's address as a code to scan, never as text |
| Send to GitHub | [ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md). Only the person's reviewed command, never an agent's, the automatic check's or auto-fix's; the digest is checked again just before the push. Halora holds no GitHub credential and passes no token: only `HOME`, `USER`, `LOGNAME`, `LANG`, `LC_ALL`, `TMPDIR` and `SSH_AUTH_SOCK` reach the person's `git` and `gh`, whose own credential helper and sign-in act. Nothing of the project's repository runs (its config and hooks are an agent's to write): it is read as files and fetched from, the commit is built and pushed from Halora's own bare repository with no hooks, without secret-shaped files, other projects or a new or changed link whose target leaves the folder, which the review lists (a starter project's `node_modules` is a link to the shared dependencies by an absolute path that holds the Mac's user name). A first send of a folder with no repository makes one with the person's `gh`, private unless they say public, as the review named it, then writes the folder's `.git` with none of anyone's git settings, templates or hooks. One new branch, never forced; nothing they print is journaled or logged; the review shows `owner/name` only, never an address |
| Publish to GitHub Pages | [ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md). The person's reviewed command only, for a passed, current check whose agent runs were all confined. What leaves is that check's built copy, the files its preview serves, never the source and never a secret-shaped file, as one commit on `halcyonic-pages`, never forced, pushed as Send to GitHub pushes; Pages is turned on with the person's own `gh`. The published game is public for a public repository: anyone can open it for as long as the branch is there. Unpublish deletes the branch |
| Undo (checkpoints) | Uses the `git` set in `HALCYONIC_GIT_BIN` (an existing absolute path, or `off` via `pnpm mac-setup undo off` to disable undo); when unset on macOS, `trusted-git.ts` selects Apple Command Line Tools' or Xcode's `git` once at startup only if the resolved binary and every parent directory up to `/` are `root`-owned and neither group- nor world-writable (refusing group-writable `/Applications/Xcode.app`, `/usr/bin/git`, Homebrew and `PATH`; [ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)). With no trusted `git`, undo is off and tasks requiring a copy are refused (`capability_unsupported`) rather than falling back to the project folder unless `HALCYONIC_TASK_COPIES=0`. Checkpoints live in per-project Git repositories under `<data dir>/checkpoints` (mode `0700`, outside agent sandboxes; folders holding host data refused as `holds_host_data`), running `git` with isolated config/home, no hooks or fsmonitor, literal pathspecs and disabled filters/textconv. Restores never follow symlinks, verify parent directories before each removal against the checkpoint's recorded ignore list, and complete only after re-reading every file (`O_NOFOLLOW_ANY` on macOS). On macOS, kernel Seatbelt profiles confine both store `git` and `/bin/rm` writes strictly under the checkpoint repository and the project's real path; on other platforms, user-space checks narrow the symlink swap window without closing it. Restores undo manual edits made after the checkpoint, which the client states before confirmation |
| Speech engine | Off unless the owner sets `HALCYONIC_WHISPER_BIN`, `HALCYONIC_WHISPER_MODEL` and `HALCYONIC_WHISPER_VAD_MODEL`, all absolute paths to existing files ([ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md)). whisper.cpp's `whisper-cli` is launched from that binary only, never from PATH, once per clip, with no environment and nothing from the client in its arguments, and startup is refused unless it reports version 1.9.4. That it opens no port and reaches no network is `whisper-cli`'s own behaviour, observed with `lsof` ([voice-transcription.md](../validation/voice-transcription.md)); nothing sandboxes it, and it runs with the owner's file and network access. A clip is read by Halora's own WAV parser and its samples alone are written into a new WAV, mode 0600, in a fresh temporary directory of mode 0700. That directory is removed when the engine exits, whatever the outcome, or as the control plane exits if it does first; one left by a control plane that was killed is removed at the next start, once it is three minutes old. The engine runs in a process group of its own, which is killed whole after 15 s (two minutes for the warm-up at startup) or after 64 KiB of output, so nothing it started holds the Mac. One clip at a time per principal, 30 reaching the engine in each fixed minute (`CLIPS_PER_MINUTE`), and one active transcription at a time on the Mac with up to 3 waiting clips (waiting at most 4 s before 503 `transcription_busy_on_mac` names how many are ahead); a transcript longer than a draft (4,096 characters) is refused. Voice activity detection means a clip with no speech yields no text rather than invented words |
| Companion | Off unless the owner names its model (`HALCYONIC_COMPANION_MODEL`); Ollama only at a loopback `http://` address with a port and nothing after it, never through Node's environment proxy, and a model name with a `cloud` tag refused at startup. Before every turn the model must be in Ollama's list without `remote_host` or `remote_model`. `POST /api/chat` only, streamed, thinking off, no tools, `format` the reply's schema where the engine keeps to one, `num_ctx` 16,384, `num_predict` 512; no pull, create, delete or `keep_alive`. 30 s to the first token and 45 s for a turn, one retry within them (and one more request without the schema when the engine cannot keep to one, so at most three requests a turn); past either the request is closed, which stops the model. At most 4,096 characters of reply read. One turn at a time per principal and one on the computer, 12 a minute per principal. A request carries at most 20 messages and 24,000 characters (2,000 for a message of the person's) and must fit the context at two bytes a token; its order is checked for consistency, because a client can forge the companion's earlier turns; every angle bracket the client sends is written as an entity, and written back in the reply. Whether it can be asked is read at most once in 2 s, however often it is asked. The reply is model text: checked against the contract, never cut to fit, shown only as the companion's reported words, and never a command |
| Codex server | Launched from the configured native binary only, never from PATH, in its own process group, speaking JSON-RPC over its stdin and stdout, so it listens on no port; refused unless both `codex --version` and its answer to `initialize` report 0.157.0 and `ps` shows the launched binary; remote control switched off; a thread Codex reports working in another folder than the project's is refused; only methods on the stable API surface, never the experimental opt-in, with one under-development feature switched on per thread so the agent can ask the person questions (`default_mode_request_user_input`, guarded by an end to end test and withdrawn by the adapter option `answerQuestions: false`, [ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)); a question marked secret is shown but never answerable through Halora; requests Halora does not show the person (permission grants, MCP elicitations) are refused, which Codex takes as a denial or an empty answer; recorded (mode 0600, no secrets) so the next start stops it after a crash, and watched by a small process that stops it if the control plane dies. On macOS, every execution's folder runs on a confined server of its own inside Halora's Seatbelt profile so writes stay within that folder, and the open server only lists models ([ADR 0041](../decisions/0041-a-new-apps-agent-is-confined-to-its-project.md)); off macOS, projects run on the open server (`workspace-write`, which also permits `/tmp`) and record `confined: false`. Automatic starter checks still run only on starter projects |
| Project secrets | [ADR 0057](../decisions/0057-a-projects-secrets-are-kept-on-the-mac-and-given-to-a-tasks-commands-at-its-start.md). Each value is a file of its own, `<data dir>/secrets/<project>/<name>`, mode 0600 in a folder of 0700, sent only in its own write-only request (the Mac's setup page, or `POST /api/projects/:id/secrets` with the access token or a paired device's credential over its pinned TLS): no command, event, view, response, log or screen holds one, only its name. Either stages the value under the id of the name-only command it submits as its caller; a set with nothing staged for it fails and keeps nothing. Every value kept or staged is a held secret for redaction, read as `[redacted: NAME]`; one under 8 characters is refused. Given only to a start whose runtime declares `secret_environment`, under the adapter's own variables; never to a check, a preview, a share, a copy, a saved copy, a send or a publish, which read the project's folder or a check's build and never the data folder. Names reserved by the host or runtime (`PATH`, `HOME`, `DYLD_*`, `NODE_*`, `GIT_*`, `*_PROXY`, `SSL_CERT_*`, plus `RuntimeAdapter.ownsEnvironmentName`: `CLAUDE_*`, `ANTHROPIC_*`, `BUN_*`, `OPENCODE_*`, `CODEX_*`, `OPENAI_*`, and ACP agent apps' own variables such as `GEMINI_*`, `GOOGLE_*`, `KIRO_*`, `CURSOR_*`) are refused. A replaced or removed value remains a held secret in memory for the life of the control plane process because tasks started earlier still hold it. Runtimes that can run outside Halora's closed sandbox (`RuntimeAdapter.runsOutsideSandbox`: Claude Code on connected folders, ACP agent apps, OpenCode without its sandbox, Codex off macOS; adapters omitting the method count as able to) are named on the setup page while configured |

### A held secret never leaves in a tree

Every key file is read one way (`readKeyFile`, `workspace-agent/providers/keys.ts`): a regular file of its own, never a link, that only the person can read. A link is refused for use, so no key is used that the held secrets, which read regular files only, do not hold.

`Send for Review`, `Publish` and `Save a Copy` read the whole tree that would leave the Mac, or be set aside in Downloads, before
it goes (`Send for Review` at its press; its review reads what comes from the folder, below), and `Share` reads every file of the built copy it would serve (`heldInFolder`: a link that leads out of the copy is refused by its path, `link_out`, whatever is held, broken links with absolute or `..` targets included, because the preview server would serve what it leads to; internal links are followed, each real folder once; at most 20,000 files and 512 MB, past which nothing is shared), and each refuses (`holds_secret`, effect `none`) when any file holds a secret the host keeps: the same list
redaction reads (the access token, each runtime's and service's key, the person's project secrets), whoever
declared it. The forms searched (`apps/control-plane/src/core/held-in-tree.ts`):
the value exactly (as a file, an env file or a quoted string holds it), JSON-escaped, URL-encoded, and its base64,
standard and URL-safe, at each of the three byte alignments it can start at inside a longer base64 text (the groups
of four holding its bytes alone, eight characters or more). The refusal names the file and which secret, never the
value. A tree `git` cannot read is refused too, so nothing leaves unread. `Send for Review`'s review reads what comes from the folder now;
a path kept exactly as the base commit has it (a secret-shaped file such as `.env`, another project's
folder, an outward link) is checked at the press: first by the history check below, which refuses a
held value in a commit the remote lacks and offers `Send One Change` where a clean remote base exists; then the
whole outgoing tree is read again regardless of what the remote holds, refusing any held value in a kept file
with key-rotation steps (issue a new key and revoke the old one where issued), because removing it from the working
folder alone leaves it in the kept base entry. Not searched: values shorter than eight
characters, values split across files or lines, compressed, encrypted, or encoded in other ways (hex, other
character sets), values the host does not hold, or traffic an agent's own network access sends while running.

A push also carries every commit of the branch the remote does not hold, not only the tree read for the review.
Just before each push of `Send for Review`, an initial send and `Publish` (`heldInHistory`), the remote itself is asked what it holds,
never the folder's own record of it, which its agent can write: at the address the push goes to (`pushInsteadOf`
applied, `remote get-url --push`), the commits it has in common with the one to push (`fetch --negotiate-only`),
and the push negotiates the same way (`push.negotiate`). The address is read as it is, never rewritten again. A
remote that cannot negotiate is read by the tips it lists that this Mac has, so a remote ahead of the branch is read
from the branch's first commit. Not guaranteed: when the push's own negotiation fails where the check's did not, `git`
sends by the tips the remote lists, which can resend commits the remote said it holds, though no branch of its holds
them (for example one scrubbed from its history). And "holds" means the remote has the object in its store: a commit a host keeps after it was scrubbed from every branch counts as held, so it is not read, and a new branch can make it reachable again (nothing that never left this Mac leaves by it). `Publish` is read before `Make Public`; an initial send after GitHub made its
repository, so a refusal there says its effect is unknown. Every commit reachable from the one to push and from none of those is read:
its own text (message, people, signature), every file it brings and every folder's own bytes, in the same forms, so
every name, one with a line break or a submodule's included. Less known about the remote means more is read, never less. A hit refuses the press (`holds_secret`): an
earlier version of a file, a change's saved description, or a name (shown with the value as the secret's name in brackets), reported
as one the repository does not have (never "not yet sent", because it may have gone elsewhere), with
the two remediation paths (creating a new key, revoking the old one at the issuer, and storing the new key on this
Mac while treating the old one as exposed; or rewriting the local history). On `Publish` the earlier version is
Halora's own prior publish commit, which already left this Mac, so the message puts revoking the old key first and
offers no history-rewrite option. Where `Send One Change` is available, `Send for Review`'s refusal names it as an
additional option. A title holding a secret is refused at review, before an initial send creates a repository. More
than 512 MB to read is refused (`history_too_large`), as is an unreachable remote (`not_reached`).

`Send One Change` ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md)) is offered
when `Send for Review` is refused with `holds_secret_in_history` and a clean remote base commit exists. It sends the
current files as a single commit whose sole parent is the newest commit of the person's branch reachable from the
remote's own branch:
- **The fetch:** that branch is fetched at the push's own address into Halora's bare repository.
- **The target branch:** the remote's branch matching the person's branch name when listed, else the remote's
  default, never read from the working folder's tracking settings or remote refs.
- **Base commit eligibility:** never a commit the remote keeps only as an unreachable object, so scrubbed commits
  are never made reachable again.
- **Staging:** staged as the person's branch tracks files, keeping secret-shaped files as the base commit holds
  them so secrets added in unsent commits stay out and untracked removals are respected.
- **Re-verification at press:** re-fetches and refuses (`review_stale`) if the remote branch moved, serialized one
  at a time per project, and runs `heldInHistory` over the new single commit before pushing.
- **Audit record:** records base, branch and secret name (never the value) in Halora's bare repository inside
  `<data dir>`, outside agent write access. The review card notes that manual `git push` by the person could still
  push earlier local commits.

No `git` command of `Send for Review` or `Publish` reads the project repository's own configuration except
`upload-pack` during the local fetch: pushes and remote queries run in Halora's bare repository inside `<data dir>`
using only global and system `git` settings (such as `osxkeychain`, SSH commands and URL rewrites), staging runs
with no config at all, and the working folder's origin, branch and object format are read directly as files; the
local `upload-pack` runs no hooks and explicitly refuses `uploadpack.packObjectsHook`, verified over SSH with a
stand-in remote so repository-level `core.sshCommand`, `pushInsteadOf`, hooks and eight other program-running
settings never execute during review or send (`credential.helper` and `core.askPass` for HTTPS remotes are skipped
by the same config isolation, though not separately exercised).

### What redaction misses

Known redaction boundaries where a credential can remain:
- A credential Halora does not hold, in error text, in a shape no pattern matches: shorter than 32
  characters, after no scheme word and without a known prefix (such as a 20-character password a
  provider's 401 echoes), `Basic user:pass` that is not base64, or a random token that reads as a
  word-like name (about 1.3 percent of random 32-character strings). In decision surfaces, any
  credential Halora does not hold stays by design so the command being approved is never guessed at.
- A held value shorter than 8 characters, or a held value split across lines or encoded outside the
  checked encodings.
- An agent environment value whose variable name contains no secret keyword and whose content matches
  none of the checked credential forms (for example a passphrase of ordinary words or a short CLI
  flag such as `-p value`). Bare absolute paths are not treated as random secrets.
- Error text beyond the first 4,096 characters, which is neither read nor journaled.
- Unheld credentials in agent messages (which strip only exact copies of held secrets), question
  options or headers (kept verbatim), and Salidium understanding statements and steps (served read
  through as Salidium provides them).
- Historical events in an append-only journal keep their stored fields on replay, including legacy
  check records whose page exception texts (up to 300 characters) were stored before page-exception
  redaction.
- Redaction markers typed literally by an agent, and questions or approvals dropped by the sink or
  recorder (both tracked in [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

Over-redaction, where non-secret text is replaced (never a leak):
- Non-secret values under secret-named keys or flags (`"credentials": "same-origin"` or
  `mysql --no-password --insecure`).
- Paths under secret-named variables (`PWD=/Users/me/project` or `SSH_AUTH_SOCK`), because values
  matched by variable name, key, flag or URL userinfo are held even when starting with `/` (as
  base64 secrets do roughly once in 64).
- A URL joined to an email address by a comma without whitespace loses its host along with its
  password.
- Non-secret error text matching credential patterns (URL usernames, alphanumeric tokens after
  `Bearer`, `Basic` or `Token`, or 32+ character high-entropy IDs).

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
  marking a place gone rather than showing an old label, and `New Project` (`NewProjectFlow`) refreshes
  active and saved drafts through `ProjectFolder.Current` whenever locations are read. `label` is
  optional in the contract, so a headset reads a host that sends none by showing `name`. Labels
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
  snapshot. `Connect Projects` (`FolderConnect`, `ProjectsScreens` and `ConnectScreens`)
  offers only folders whose `used_by` is empty, and connecting one is an ordinary `project.create`
  with an `existing_folder` choice, checked like any other; the host does
  not refuse a folder another project already uses ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md) lets projects share one), so two
  devices, or a listing gone stale, can still bind one folder twice. Facts are a moment's: a
  folder replaced by another between the listing and Connect is bound with no word of the change.
  Cost: one `lstat` per distinct bound path inside the roots, so a device that makes many projects
  in one folder adds one read, not one per project.
- **The catalog's folder match** ([ADR 0039](../decisions/0039-the-projects-gallery-reads-a-catalog-from-omnorium.md), `catalog-match.ts`) reads one file in a listed
  folder, its `.git/config`, to suggest which catalog repository the folder is a clone of. It runs
  only when a client reads `GET /api/catalog` with a catalog that is the person's own, never a
  sample, reading at most 400 folders and stopping after 2 seconds. It reads only a folder whose
  `.git` is a directory, never a link or a `.git` file naming another place, and a folder holding
  Halora's own data not at all. It opens `config` without waiting on a pipe and without following
  a link, on macOS anywhere in its path (`O_NOFOLLOW_ANY`), so the folder or its `.git` swapped for a
  link after it was checked leads nowhere; it checks through the handle that it is the same regular
  file of at most 64 KiB, and never runs `git`.
  It takes only the one `url` of `[remote "origin"]` (several, or none, is no match) and applies no
  `include`, `includeIf` or `insteadOf`. It drops any user information first, where a credential in
  a remote's URL would sit, accepts only https, ssh and scp-like forms on exactly github.com with
  GitHub's own characters for the owner and name, and keeps only `owner/name` folded to lower case.
  The URL is never kept, returned, logged or journaled. A match is `inferred` and grants nothing: an
  agent can write a folder's `.git/config`, so the person's link, checked against the catalog this
  computer last read, is the only thing that ties a project to a catalog repository.
- **Folder names are someone else's words.** Anything that can write in a root (an agent working
  there, for one) can name a folder to look like another or to read as Halora's sentence. Connect
  a folder's words (`ConnectText`) show names by `LabelText`'s rule, quote them inside Halora's
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
  a second Connect, or `Add Task` in the demonstration does nothing.
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
  may hold provider keys. Of Halora's own Codex settings, `<data dir>/codex-home/config.toml`,
  it reads only the top-level `model_provider` and `model` lines, and prints a model name only when
  it matches the name pattern the Codex adapter takes. `local-model` writes that file and refuses a
  Codex home that is a link, another user's, or holds a sign-in.
- **Binaries and models.** It records the pinned OpenCode and Codex binaries and the voice models only
  when their SHA-256 matches the pins Halora was checked with on Apple silicon; elsewhere it says
  it cannot check them. It downloads nothing: what needs a download it shows as a command. The
  setup page (below) runs those same commands, on the person's second press only.
- **Folders.** Allowing a folder says what it allows and asks first. It refuses what the control
  plane refuses as a root (Controls, "Project roots"), and says a personal folder such as Documents
  holds much more than projects before it asks.
- **Pairing.** It turns the network listener on only on `pnpm mac-setup pairing on`, after saying
  what it opens (an encrypted listener for every device on the network, the macOS firewall prompt,
  that anyone on the network can try to pair while `pnpm pair` runs) and asking. Besides it, only the
  pairing page turns it on, on the person's press (below).
- **What it writes.** The settings file, and Halora's own OpenCode settings in
  `<data dir>/opencode-config`, each mode 0600 in directories of mode 0700, through a new file
  renamed into place.

### The setup page

The same check and actions on a page at `/setup` of the pairing page's server
([ADR 0053](../decisions/0053-the-macs-setup-runs-from-a-page-beside-pairing.md)). What it adds:

- **Kiro, Antigravity and Cursor** ([ADR 0067](../decisions/0067-agent-apps-that-speak-acp-are-directed-through-one-adapter.md)): `Install` fetches each vendor's archive over HTTPS only, verifies its checksum before unpacking into `<data dir>/runtimes/`, and reports `Done` only when the unpacked binary (or Cursor's whole `dist-package` manifest) matches the pin. `Turn On` is the person's explicit action enabling an app to spend their plan: it writes `agent-apps-on.json` in the data folder only when the verified copy is installed and takes effect on the next start; neither `settings.json` nor installing alone turns any of them on.
- **Reach.** Only what the pairing page reaches: loopback, its one-time link and cookie, its Host,
  Origin and `Sec-Fetch-Site` checks and its CSP. A view mounted on the page may add routes only
  under `/setup`, each behind the session.
- **Presses.** A fixed list of setup actions, each acting only on a second press of the same action
  and argument; anything else is refused in words. None sets the paid runtime's switches.
- **Downloads.** Only the pinned commands setup prints (npm with `--ignore-scripts`, the starter's
  `npm ci` with no user or global npm settings and a fresh cache, `ollama pull` of the checked
  models), with no shell, in a process group of their own, with `PATH`, `HOME`, `TMPDIR` and `LANG`
  only: none of the control plane's own environment, its keys among it, reaches a download. One at
  a time; `Stop` ends the whole group. Its lines are shown cleaned of terminal sequences and unseen
  characters, with the home folder as `~`. What a download installs is recorded only through
  setup's own action, which checks the pins; OpenCode's and Codex's binaries are checked again by
  the page, and one that does not match reads `Not Set Up` and is not recorded. Their `npm install`
  uses the person's npm settings, as the printed commands do. A model is pulled by name and tag
  with no pinned digest, and its button says so.
- **Sessions.** A pending press belongs to the session that made it (a hash of the session reaches
  the view, never the cookie), so another session's `confirmed` press is a first press there.
- **Project secrets.** [ADR 0057](../decisions/0057-a-projects-secrets-are-kept-on-the-mac-and-given-to-a-tasks-commands-at-its-start.md). A name and a hidden field per project; the value is cleared as it is
  sent and no answer of the page holds it. Its route alone takes up to 8 KiB of value plus JSON's
  escaping; every other route keeps the page's 1 KiB. A refusal is said in that project's card, the typed
  name kept and the value cleared. `Remove` asks first and deletes only on a second press of the same secret.
- **Restart.** `Restart to Apply` closes the control plane as a signal does and exits with code 75,
  which only `Start Halora.command` starts again; it says first which tasks will pause.

## Authorization

There are two kinds of principal: `local`, whoever holds the access token, which only loopback
accepts, and `device`, a paired device, which only the network listener accepts. Every command
records the principal that sent it, as the control plane authenticated it, beside the client's
self-declared identity, which is recorded for audit and never trusted. Device management differs
between them: opening a pairing window, listing devices and revoking one are served on loopback
only, and a device can revoke no credential but its own. Commands are otherwise admitted alike for
both, except that a paired device changes a project's files or folder only with the digest of the
review it read ([ADR 0051](../decisions/0051-a-paired-device-acts-on-files-only-with-the-review-it-read.md)).
Its `project.set_location` that rebinds a project that has a folder, its `project.restore`, and its
`workstream.add_to_project` must carry `review`, the digest returned by the location review,
restore preview or task-copy reading, and that digest must still match when the command acts (checked
at admission for moves and adds, and immediately before writing for restores; `review_required` when
absent, `review_stale` when changed). A digest is checked whoever sends it, while the `local`
principal may omit it on those three commands. The digest proves that the underlying state has not
changed, not that a person read it, because a device can fetch those reads itself. A restore's digest
covers every path it would change, not only the first 100 listed in the preview. Every accepted
command records its policy category (`low_consequence`, `review_required`, `high_consequence`); no
`high_consequence` command exists, and merge, deploy, delete and destructive commands require
explicit human confirmation and per-principal policy before any is added.

`project.restore` is the one command that removes files ([ADR 0036](../decisions/0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md)). It is admitted because nothing
is written until a checkpoint of everything it would change or remove has been kept, so a restore
can itself be put back; when that checkpoint cannot be kept, no file changes. A paired device's restore
must carry the preview's digest (`HistoryColumn` on the headset reads the restore preview and submits
`project.restore` with that digest); whether the person read the preview remains a client UI guarantee.

A conversational model must never turn vague speech into permission for an irreversible action.

## Audit

The journal is the audit log. For each command it records the full command, when it was received,
through which transport and from which principal, its policy category, the admission decision,
and the runtime confirmed outcome or failure (including whether the effect is unknown). Legacy
commands migrated before principals were recorded carry `principal: null`. Pairing and revoking
devices are journaled too (`device.paired`, `device.revoked`, [EVENTS.md](EVENTS.md)); pairing
windows and failed attempts are logged, without the code. Instructions and answers to an agent's
questions are work content and are journaled locally; they are never logged. No question asking
for a secret can be answered through Halora: admission refuses an answer to any prompt marked
secret, whatever the adapter said, and a refused answer is journaled with its keys only, so what a
client sent with it never reaches the journal or the other clients. Nor can another client learn it
by resending guesses under its command id: only the principal that sent it is told whether a
resend matches, at most six times a minute ([DOMAIN_MODEL.md](DOMAIN_MODEL.md)). A question that asks for a
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
  each connection turned away or cut short, with its address), hold the 40 connections one
  address may, and the 8 more answered in words, each for at most 5 seconds without finishing TLS
  and 10 without a request, fill all 128 from several addresses, or block traffic. Its timing tells it nothing about the code: an
  exchange's work depends only on a secret it draws itself. While a window is open, and only then,
  it also hears the computer announce itself ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)): the Mac's Bonjour
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
  anyone with `adb` access to a screen-active headset can read the credential, or an access token put
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
  ([quest-headset-in-the-loop.md](../validation/quest-headset-in-the-loop.md)). Halora leaves
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
hold the 40 connections its address may with slow requests, and devices on several addresses can
fill the listener's 128, a connection that sends no request for at most 10 seconds at a time.

## Untrusted text in the client

The XR client shows text Halora did not write: workstream titles and objectives, which come
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
  character shows as its code point too, as ‹U+E769›: Halora's icons are drawn from those code
  points in their own font, so text from outside cannot draw a state's icon among its words.
- Nothing is cut short silently: a label cut short ends in an ellipsis, and no label uses
  TextMeshPro's italics or bold, which lose it. An approval is confirmed only once the whole
  request it answers has shown, in parts when it is long.
- Builders cannot bypass `LabelText`: lines handed whole text, whether Halora's own or outside text
  already formatted by a builder, are refused line by line where `LabelText.Plain` would alter them,
  and names that the review before `Start Building` spells character by character are held as unspelled
  runs refused by labels until spelled (`LineWords`). A builder that omits `LabelText` fails its tests
  rather than placing raw text on a label.
- The editor's render check puts hostile text on every workspace label and on a character's, and
  fails if one interprets any of it or cuts it short without an ellipsis. The line above the stage
  and the pairing line go through the same code but are not rendered by it.

Not covered: characters that only look alike, such as a Cyrillic letter for a Latin one or a
no-break space for a space, show as they look. The client shows an approval summary as the control
plane recorded it, bounded to 2,000 characters (`[truncated]` appended after held-secret redaction).
Each request also records `complete` (`runtime.approval.requested`, [EVENTS.md](EVENTS.md)), which is
copied to `approvable`: requests with `approvable: false` offer `Deny` alone on the headset, and the
control plane rejects any approve command for them with `approval_not_whole`. File-write requests
carry their diff or replacement whole in `ApprovalSubject.changes`
([ADR 0065](../decisions/0065-a-request-too-long-to-show-whole-is-denied-by-the-host-with-its-reason.md)),
counted toward the 2,000-character approvable bound, while the headset card renders
`Summary`, `Command` and `Inputs` (`WorkspaceText.Well`) rather than `ApprovalSubject.changes` (below).

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
- **Drawing file-write diffs on the headset approval card.** Approval requests carry file-write
  changes whole in `ApprovalSubject.changes`
  ([ADR 0065](../decisions/0065-a-request-too-long-to-show-whole-is-denied-by-the-host-with-its-reason.md)),
  but the headset card does not yet render `ApprovalSubject.changes`: in an unconfined run,
  approving a file write on the headset approves content whose paths and summary were shown without
  the diff body. Confined runs are unaffected because the closed profile holds their writes and the
  host answers them ([ADR 0064](../decisions/0064-work-inside-a-confined-apps-own-copy-does-not-ask.md)).

Device pairing, encrypted transport for devices on the network, and per-device identity exist
([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)); serving anything to the
internet still needs the relay and more.

Provider credentials (Anthropic, OpenAI and others) must stay with the runtime on the machine that
needs them. XR clients must never receive provider keys, repository secrets or SSH keys.
