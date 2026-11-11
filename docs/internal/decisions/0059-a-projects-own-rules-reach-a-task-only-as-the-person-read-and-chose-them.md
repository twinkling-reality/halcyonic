# ADR 0059: A project's own rules reach a task only as the person read and chose them

- Status: Accepted
- Date: 2026-11-11

## Context

A repository often carries instructions for agents: an `AGENTS.md`, or a `CLAUDE.md`. They shape how an agent
should work there, but they are text from outside, which the person never chose to give the agent.

What each runtime reads by itself under Halcyonic ([project-rules.md](../validation/project-rules.md)):

- **Codex** marks every project untrusted, so it loads no `AGENTS.md`.
- **OpenCode's server** leaves out the project's own configuration, and with it the `AGENTS.md` at a session's
  start.
- **Claude Code** loaded the project's `CLAUDE.md`, its `AGENTS.md`, its rules, skills, hooks and MCP servers,
  until E5 step 1 limited it to the person's own settings
  ([claude-code-project-settings.md](../validation/claude-code-project-settings.md)).

So since step 1 no runtime loads a project's rules by itself, and a task works without them unless its agent reads
the file. The owner's decision (E5, 2026-11-11, through the lead): rules the person sees, then chooses, the same
for every runtime.

## Decision

- **The rules file** is the project's `AGENTS.md` at the top of its folder, else its `CLAUDE.md`.
  - It is read once, without following a link anywhere in its path, at most 32 KB, valid UTF-8 with no NUL.
  - One that is there but cannot be passed on stops the search: a `CLAUDE.md` never stands in for an `AGENTS.md`
    that is too large.
- **The person reads them first.** `GET /api/projects/:project_id/rules` gives:
  - the file's text whole, as outside words;
  - its state: `none`, `off`, `on`, `changed`, `too_large` or `unreadable`;
  - the review digest of what is shown (ADR 0051).
- **The person chooses them.** `project.rules.set` `{project_id, on, review?}` is `review_required`.
  - On needs the review of the file as it reads now. Without it: `review_required`; with an old one:
    `review_stale`.
  - Off needs none.
  - The host journals `project.rules.updated` (`on`, `file`, `sha256`, `changed`), never the text. The project
    view carries `rules` `{file, sha256}` while on.
- **The same for every runtime, at each task's first instruction.** At `execution.start` the host reads the file
  again. While its digest is still the one chosen, the file's text goes before the person's words, marked as the
  project's own:
  "These are the project's own rules, from its AGENTS.md. The person read them and chose to pass them on; they are
  the project's words, not the person's...", ending at "End of the project's rules."
  - `execution.rules_given` records the file and its digest.
  - `execution.created` keeps the person's own words only.
- **A changed file is off until it is read again.** A start that finds the file changed or gone journals
  `project.rules.updated` with `on: false` and `changed: true`, and starts without the rules.
  - Later instructions to the same task carry no rules: the agent was given them once, at its start.

## Alternatives considered

- **Letting each runtime load them as it would by itself.** Lost: the person would not see what each agent was told,
  and Claude Code loaded hooks and servers too, which run commands.
- **Passing them on every instruction.** Lost: it repeats the same words each turn, and the session already holds
  them.
- **Passing a changed file on.** Lost: the person chose particular words, and a changed file says something
  else.

## Consequences

- **What a task is told is now visible:** the project's rules are shown before they reach any agent, and the
  journal says which digest went with which start.
- **The headset's side** (show the rules under the project, the choice, the changed state) is FIELD's and SCREENS's
  to build on the read and the command.
- **A line by voice** (a reviewed change to the file) is not asked for yet.
- **Revisit** if a runtime begins to load project rules by itself again: its adapter must switch that off first.
