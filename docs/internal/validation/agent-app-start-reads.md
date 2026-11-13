# What an agent app reads beyond its folder (VRIFY, 2026-11-13, documentation only)

The open question in [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md) asks what a run on a folder the person
connected may read beyond that folder. A real agent's first command on a plain folder searched the whole disk for
instruction files (`find / -name AGENTS.md`), and on a hosted agent app what a run reads goes to the provider. Its
first half is what each agent app reads on its own when it starts, and what its tools may read by default. This file
holds that half from each app's own current documentation and, for the open-source ones, its source. No app was run
for it. The other half, what Halcyonic's light profile allows (`hostFoldersDeniedProfile` in
`packages/seatbelt/src/profile.ts`: everything but the host's own folders), is in
[claude-code-connected-folders.md](claude-code-connected-folders.md).

## Verified from the documentation

### Claude Code (the CLI and its Agent SDK)

- Instruction files: `CLAUDE.md` and `CLAUDE.local.md` in the working directory and in every directory above it, read
  "from the filesystem root down to your working directory", so the walk does not stop at a git root; `AGENTS.md`
  only where no `CLAUDE.md` is; `~/.claude/CLAUDE.md`, `~/.claude/rules/` and a managed file (on macOS
  `/Library/Application Support/ClaudeCode/CLAUDE.md`). An `@path` import can reach outside the folder; a project's
  outside import needs a one-time approval (https://code.claude.com/docs/en/memory).
- Settings: `~/.claude/settings.json`, the project's `.claude/settings.json` and `.claude/settings.local.json`,
  managed settings and `~/.claude.json`; `CLAUDE_CONFIG_DIR` moves them (https://code.claude.com/docs/en/settings).
  `.mcp.json` at the project root (https://code.claude.com/docs/en/claude-directory). The Agent SDK loads the user,
  project and local settings when `settingSources` is left out, and auto memory under `~/.claude/projects/` regardless
  (https://code.claude.com/docs/en/agent-sdk/claude-code-features).
- Credentials: the macOS Keychain, or `~/.claude/.credentials.json`; `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN` and
  an `apiKeyHelper` (https://code.claude.com/docs/en/authentication). State it writes: transcripts, history, file
  history and shell snapshots under `~/.claude`, and a scratch folder in the system's temporary folder.
- What its tools may read: Read and Grep need no approval inside the working directory and the added directories
  (https://code.claude.com/docs/en/permissions); `permissions.blockReadsOutsideWorkingDirectories` is a choice, off by
  default. Its own sandbox is off by default and, when on, covers Bash only, with "read access to the entire computer,
  except certain denied directories" (https://code.claude.com/docs/en/sandboxing).

### Codex CLI

- Instruction files: the global `AGENTS.md` (or `AGENTS.override.md`) in `CODEX_HOME` (`~/.codex`), then one per
  directory from the project root (the git root, by `project_root_markers`) down to the working directory, at most 32
  KiB in all; with no project root, only the working directory's
  (https://learn.chatgpt.com/docs/agent-configuration/agents-md).
- Config: `~/.codex/config.toml` and its profiles, and each `.codex/config.toml` from the root to the working
  directory in a trusted project; history, logs and caches under `CODEX_HOME`; credentials in `auth.json` or the system
  keyring (https://learn.chatgpt.com/docs/config-file/config-advanced).
- What its tools may read: read-only and workspace-write both read the whole disk ("can read the entire disk", in its
  source, `codex-rs/protocol/src/protocol.rs`); the docs do not say how far reads reach. A permissions profile's
  `"deny"` rules narrow reads (https://learn.chatgpt.com/docs/config-file/config-reference).

### OpenCode (v1.19.5)

- Instruction files: `AGENTS.md`, then `CLAUDE.md`, searched upward from the working directory
  (https://opencode.ai/docs/rules/); in its source the walk stops at the worktree, and a folder without version control
  has `/` as its worktree, so the walk reaches the filesystem root (`packages/opencode/src/session/instruction.ts`,
  `packages/opencode/src/project/project.ts`). Globally `~/.config/opencode/AGENTS.md`, falling back to
  `~/.claude/CLAUDE.md`, and `~/.claude/skills`.
- Config: `opencode.json` and `.opencode/` from the working directory to the filesystem root, `~/.config/opencode/` and
  managed files (https://opencode.ai/docs/config/); credentials in `~/.local/share/opencode/auth.json` (source).
- What its tools may read: the docs say everything is allowed by default, with reads of `*.env` files asking, and
  outside directories allowed (https://opencode.ai/docs/permissions/); the v1.19.5 source asks for any path outside the
  working directory and git root (`external_directory: {"*": "ask"}`, `packages/opencode/src/agent/agent.ts`).

## Seen: Claude Code as Halcyonic starts it (2026-11-13, a stand-in API on loopback)

Halcyonic starts Claude Code with no settings source (`settingSources: []` in `claude-agent-runtime.ts`), so the walk
above does not happen at start: no `CLAUDE.md` in the folder or above it loads, and outside the closed profile only the
person's own `CLAUDE.md` in Claude Code's configuration folder is passed in. `open-launch.e2e.test.ts` (a connected
folder) and `confined.e2e.test.ts` (a confined app) each put a marker `CLAUDE.md` in the project's parent folder and
start the bundled Claude Code 2.1.283 once in the project against a stand-in Messages API: neither run's requests carry
the marker. Fail first: with `settingSources: ['project']` the connected-folder run's requests carry it, so the CLI's
own walk is real and only Halcyonic's launch keeps it out. In a confined app the closed profile refuses reading that
folder besides.

Codex 0.157.0 and OpenCode 2.0.18, installed by npm at those versions for the test (their binaries' SHA-256 are the
pins'), each started once in a task's folder against its suite's stand-in provider, with a marker `AGENTS.md` in the
folder and in the folder above (`codex-runtime.e2e.test.ts`, `opencode-runtime.e2e.test.ts`): neither marker reaches
the model from either. Codex: every folder from the task's up is marked untrusted (`untrustedFrom`), and Codex loads
no project instructions for an untrusted folder, inside a git repository too (where its own default walks from the
repository's root down); fail first, without those marks the folder's own `AGENTS.md` arrives. OpenCode: its launch
turns off its search for project configuration (which also stops the project `AGENTS.md`), gives it a home of its own,
and its sandbox reads no folder above; without that switch OpenCode does not start here, so no fail first was shown,
and the same captured requests hold its instruction, so the capture sees what it sends. So for all three agent apps
as Halcyonic starts them, no instruction file in or above the folder reaches the model at start.

## What follows (inference, not a decision)

- None of the three keeps its tools' reads inside the working directory by default. Claude Code asks before a Read or
  Grep outside it but runs a shell command that reads anywhere; Codex reads the whole disk; OpenCode's release asks.
- Two of them read files above the folder on their own at start by default: Claude Code walks to the filesystem root
  for `CLAUDE.md` (not as Halcyonic starts it, seen above), and OpenCode walks to the root for `AGENTS.md` and its
  config when the folder has no git. Codex stops at the git root. Where an app does walk, a connected folder inside a
  person's home gives it the instruction files of every folder above it, and those words reach the model.
- What stays open on a connected folder is what the agent's own commands read once it runs (a `find /` lists the
  whole disk), which the light profile does not bound beyond the host's own folders.
- A light profile that keeps a person's tools working must leave each app its own config, credentials and state
  folders above, and the toolchains and caches it runs (AUDIT3's read lists them); denying the person's secret shapes
  and their other project roots is the shape to test.

## Not verified

- What each app opens at start beyond what reaches the model (a file-access trace), and whether each still starts and
  works with secret shapes and other project roots denied.
- Claude Code's other reads at start (its configuration folder, the Keychain) were not traced; only what reaches the
  model was.
- Claude Code: whether a git status or `.gitignore` is read at start; whether Manual mode asks for every outside read.
- Codex: system config paths such as `/etc/codex`; `auth.json`'s exact place.
- OpenCode: whether the walk up to the home directory on its development branch has shipped.
