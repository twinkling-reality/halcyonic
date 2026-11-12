# A project's own rules, as each runtime reads them under Halcyonic (parity E5)

- **Question:** which rules files (AGENTS.md, CLAUDE.md, a runtime's own settings) does each runtime read by itself
  as Halcyonic launches it, and what can a person see or change of them from the headset?
- **Date:** 2026-11-11.
- **Versions:** `codex-cli` 0.157.0, `@opencode/cli` 2.0.18, `@anthropic-ai/claude-agent-sdk` 0.3.283 with its
  bundled CLI 2.1.283.
- **Method:** Halcyonic's adapters, the earlier records cited below, the SDK's type definitions, and the bundled
  CLI's own strings. For Claude Code, then, a runtime test on the fake host
  ([claude-code-project-settings.md](claude-code-project-settings.md)). The pinned Codex and OpenCode binaries were
  not run for this record.
- **Status:** each fact below is read from source or recorded earlier, as marked. What is not verified is listed.

## Codex
- **Halcyonic's own home.** Codex runs from a home of Halcyonic's own (`<data dir>/codex-home`, or a confined
  server's own), never `~/.codex`. Halcyonic writes no instruction setting into it.
- **Every project is untrusted.** Every thread marks its folder and each folder above it untrusted
  (`codex-runtime.ts`, `untrustedFrom`), and refuses a start whose trust slipped past that.
- **The project's AGENTS.md is not loaded.**
  - An untrusted project's `AGENTS.md` and `AGENTS.override.md` are not loaded
    ([SECURITY.md](../architecture/SECURITY.md), from Codex's source at rust-v0.157.0).
  - A fake-provider runtime test on 2026-10-07 found a marker from them absent from what the thread sent, and
    present in the control run ([local-models.md](local-models.md)). That test is not in the e2e suite.
- **Remaining ways project text reaches Codex:** files the agent reads, and a skill an instruction names with `$`.
- **Not verified:** whether Codex reads a global `AGENTS.md` in its own home. Halcyonic writes none.

## OpenCode
- **Project configuration is switched off.** Every launch sets `OPENCODE_CONFIG_PROJECT_DISABLE` and
  `OPENCODE_DISABLE_PROJECT_CONFIG` (`server.ts`).
- From OpenCode's source, this skips the search of the project for its configuration, and the project's `AGENTS.md`
  is not loaded when a session starts ([opencode-permissions.md](opencode-permissions.md)). A runtime test confirmed
  only the configuration and plugin markers.
- **On macOS** its home is the sandbox's own folder, so the person's own rules files there are not found.
- **A subfolder's AGENTS.md still gets in.** When the agent reads a file in a subfolder, OpenCode adds that
  subfolder's `AGENTS.md` to the conversation (from source), though not the task folder's own.
- **Not verified:** a runtime test of what reaches the provider with markers in the project's `AGENTS.md`, its
  `CLAUDE.md`, the configuration home and the sandbox's home.

## Claude Code
- **Settings sources, until 2026-11-11.** The adapter left `settingSources` unset. The SDK's types say "When
  omitted, all sources are loaded (matches CLI defaults)", and that `'project'` must be among them to load
  CLAUDE.md files.
- **What that loaded,** on the fake host: the project's `CLAUDE.md`, `CLAUDE.local.md`, `.claude/rules` and skill,
  its `AGENTS.md` where it had no `CLAUDE.md`, the hooks in both of its `.claude` settings files, and a server its
  `.mcp.json` named, whose command ran before the agent did anything
  ([claude-code-project-settings.md](claude-code-project-settings.md)).
- **From 2026-11-11** the adapter passed `settingSources: ['user']`; **since 2026-11-12** `[]`, with the person's own
  `CLAUDE.md` and Salidium's and Seorak's hooks passed explicitly
  ([claude-code-settings-isolation.md](claude-code-settings-isolation.md)), so they still observe each session. None of the project's files above load. Proven on the fake host
  only; it never met the hosted service.

## The starters
- **Each starter carries one rules file:** `AGENTS.md` (`starters/iwsdk-vr/template/AGENTS.md`,
  `starters/world/template/AGENTS.md`), copied into every new project.
- **Who reads it:**
  - Codex: not loaded (untrusted project).
  - OpenCode: not loaded when a session starts.
  - Claude Code: loaded until 2026-11-11, as the project has no `CLAUDE.md`; not loaded since.
- The VR version's instruction points its agent at it by name.

## The headset
- **Nothing shows or changes a project's rules.** The control plane's read of one file
  (`GET /api/projects/:id/file`) can read `AGENTS.md` as text, but the headset does not call it for that.
- **Changing them:** the only way is to ask an agent to edit the file.

## Where the documents disagreed
[OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md) asked whether a task should be handed its project's standing
instructions, and said that for now nothing passed them. That held for Codex and OpenCode. It did not hold for
Claude Code, which loaded the project's own `CLAUDE.md` or `AGENTS.md`, and its `.claude` settings, as Halcyonic ran
it until 2026-11-11. Since then it holds for all three, and the row says so.
