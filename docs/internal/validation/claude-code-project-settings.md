# A project's own files and Claude Code under Halcyonic (E5, step 1)

- **Question:** which of a project's own files does Claude Code load by itself, as Halcyonic launches it, and does
  the person's own configuration still load?
- **Date:** 2026-11-11.
- **Versions:** `@anthropic-ai/claude-agent-sdk` 0.3.283 with its bundled CLI 2.1.283, darwin-arm64.
- **Status:** proven on the fake host only. It never met the hosted service: no hosted model, no real key, no spend.

## Method
- **The test:** `packages/integrations/claude-code/src/project-settings.e2e.test.ts`.
  - It runs the real bundled CLI through Halcyonic's adapter, unconfined, with `CLAUDE_CODE_BIN` pointing at it.
    Without that variable the test is skipped, as the other end to end tests are.
  - A fake Messages API on loopback answers "Done." and keeps each request's body. The key is a dummy.
- **The person's own configuration** (the test's own `CLAUDE_CONFIG_DIR`):
  - a `CLAUDE.md` with a marker;
  - a `settings.json` whose `SessionStart` and `UserPromptSubmit` hooks each create a marker file.
- **Project one** has:
  - `CLAUDE.md`, `CLAUDE.local.md`, `.claude/rules/style.md` and a skill (`.claude/skills/house-style/SKILL.md`),
    each with a marker of its own;
  - `.claude/settings.json` and `.claude/settings.local.json`, each with the same two hooks, under names of their
    own, and `enableAllProjectMcpServers`;
  - `.mcp.json`, naming a server whose command creates a marker file.
- **Project two** has an `AGENTS.md` with a marker and no `CLAUDE.md`.
- **What is checked:** a text marker counts as loaded when it is in a request the fake API received. A hook or the
  server counts as run when its marker file exists. Every marker file is the test's own, in its own temporary
  folder. Nothing is printed but true or false.
- **The control:** the same run with the SDK's `settingSources` left unset, as the adapter did before this change.

## Results

| Loaded, or run | Before: every source | Now: `['user']` |
|---|---|---|
| The person's own `CLAUDE.md` | yes | yes |
| The person's own hooks (`SessionStart`, `UserPromptSubmit`) | yes | yes |
| The project's `CLAUDE.md` | yes | no |
| The project's `CLAUDE.local.md` | yes | no |
| The project's `.claude/rules` | yes | no |
| The project's skill | yes | no |
| The project's `AGENTS.md`, where it has no `CLAUDE.md` | yes | no |
| The hooks in the project's `.claude/settings.json` | yes | no |
| The hooks in the project's `.claude/settings.local.json` | yes | no |
| The server in the project's `.mcp.json` | yes, its command ran | no |

- **Before:** a repository's own files gave every Claude Code task instructions the person never saw. Its hooks
  and its MCP server each ran a command before the agent did anything, with no approval asked.
- **Now:** a start and a model listing pass `settingSources: ['user']` (the adapter's unit tests), which the SDK
  sends to the CLI as `--setting-sources=user` (`sdk-process.test.ts`). The person's own hooks still run, so
  Salidium and Seorak still observe each session.
- **Inside the sandbox** (a new app's project, BOX-4), no source loads at all (`settingSources: []`).
  - The 'user' source there would be the folder's own configuration, which the agent inside can write. A hook or an
    allow rule it wrote would load at the folder's next start or resume (AUDIT2's S1).
  - **The confined end to end test now plants both before the start:** a `settings.json` with an allow rule for
    every shell command, and a `UserPromptSubmit` hook marking the project.
    - With the fix, the command still asks and the hook does not run.
    - The control, with the 'user' source loaded there, failed: no request to approve came within the test's
      60 s, so the planted rule most likely let the command run unasked. The run then hung until stopped, so its
      summary was lost.
  - A project's own files do not load there either, as everywhere.

## Not verified
- Whether a hosted model's request differs from the fake host's in what it carries. It was not run against the
  hosted service.
- The project's `.claude/agents` and `.claude/commands`: they were not given markers here, so whether they load
  now is not known.
- A file the agent reads by itself still reaches the model, as any file does. A starter's instruction that names its
  `AGENTS.md` still brings it in that way.
