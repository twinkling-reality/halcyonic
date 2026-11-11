# ADR 0043: A person may let a task's agent work without asking, where its runtime keeps it in bounds

- Status: Accepted
- Date: 2026-11-10
- Complements ADR 0016, ADR 0033 and ADR 0041; amends SECURITY.md's agent permissions rule

## Context

On 2026-11-10 the owner asked for a way to let an agent work without asking, as desktop agent tools offer: in a
headset, pressing Approve again and again tires a person, and many people who direct agents run them that way.

Until now every start asked as its runtime asks by default, and SECURITY.md refused every mode that takes decisions
away from the supervising person, with one exception that is never a start option (ADR 0041: a confined starter
project's Codex commands run without asking). What the pinned runtimes offer, read on 2026-11-10 from each pinned
binary's own schema or help and the pinned SDK's types ([approval-levels.md](../validation/approval-levels.md)):

- Codex 0.157.0: approval policies `untrusted`, `on-request`, `never` and granular ones; the sandbox is chosen
  apart from the policy. `never`: "Never ask for user approval. Execution failures are immediately returned to the
  model".
- OpenCode 2.0.18: each permission rule's effect is `allow`, `ask` or `deny`; Halcyonic's session rules outrank every
  configuration file.
- Claude Agent SDK 0.3.283: `dontAsk` denies whatever is not pre-approved; `bypassPermissions` checks nothing.
  Halcyonic gives Claude Code no sandbox of its own.

What a person approves is what a command may reach. Where a sandbox already bounds the agent, a press adds little
but fatigue; where nothing bounds it, a press is the only check.

## Decision

1. **Two levels, in the core.** `ApprovalLevel` is `ask` (each runtime as it asks today, the default) or
   `never_ask` (the agent's actions run without a request to the person). `never_ask` widens asking only, never what
   the agent may touch: each runtime keeps the same sandbox, folder and network as under `ask`.
2. **Declared, never assumed.** A runtime offers `never_ask` only through `RuntimeDescriptor.offers_never_ask`,
   declared once a runtime test on its pinned version shows that nothing asks and the agent stays inside the bounds
   of decision 1. Admission refuses `never_ask` for any other runtime (`capability_unsupported`); the adapter refuses
   it again at the start.
3. **Chosen per run.** `execution.start` carries an optional `approval_level`; absent means `ask`, as every start
   before it meant, so no stored command changes. Changing it is a new run, as changing the model is. No command and
   no journaled setting holds a project's choice; the headset remembers the person's last choice for a project itself.
4. **Recorded as observed.** The adapter records the level the runtime confirmed in `runtime.execution.started`, and
   the execution's view carries it. A client shows the recorded level, never the choice. Absence means two different
   things, and nothing infers one from the other:
   - absent on `execution.start` means `ask`, which is what every start before this decision meant;
   - absent on `runtime.execution.started` and on the view means not recorded (an older run, or a runtime that does
     not record it), never `ask`.
5. **Per runtime:**
   - Codex: `never_ask` is the approval policy `never` with the `workspace-write` sandbox, reviewer `user`; a start
     that also names `approval_policy` or `sandbox` is refused, and the option `approval_policy: "never"` stays
     refused. Every thread, at both levels, carries its own `sandbox_workspace_write.network_access = false` and
     `writable_roots = []`, above the home's `config.toml`, and a thread Codex reports with network access or a
     writable folder other than the project is refused. A never-asking thread is refused while the home names an MCP
     server, whose tools run outside the command sandbox. The level recorded is the policy Codex reports for the
     thread. On a confined starter project (ADR 0041) its turns use `externalSandbox`, under which nothing asks, so
     its start records `never_ask` whatever was chosen.
     Offered: the end to end test on 0.157.0 shows an escalation under `never` is refused by Codex itself, a
     dangerous command stays in the sandbox, nothing asks, and a home that turns on network access or adds a writable
     folder does not reach the thread.
   - OpenCode: not offered yet, and refused in words at the check and at the start. Its one server's sandbox (ADR
     0028) writes every project under the host's project roots, so a command run without asking could change another
     project (AUDIT, 2026-11-10). The session rules that would make it ask nothing were verified on 2.0.18
     ([approval-levels.md](../validation/approval-levels.md)) and wait for a sandbox that writes only the task's
     project.
   - Claude Code: not offered, and refused in words at the check and again at the start, never ignored. Halcyonic
     gives it no sandbox, so never asking would give the agent the person's own access
     ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
   - The mock offers it only when made to, synthetic like the rest of it; the recorded demonstration's runtimes offer
     none, so the judges' build shows no choice.
6. **What each runtime may write, under either level.**
   - Codex on an open project: the project folder (the thread's folder), its `$TMPDIR` and `/tmp` (Codex 0.157.0
     reports `excludeTmpdirEnvVar` and `excludeSlashTmp` false), nothing else, and no network for commands. Reads
     reach the whole disk, as under `ask`. `$TMPDIR` and `/tmp` are shared with the person's other programs: a
     command run without asking can change what another program keeps there.
   - Codex on a confined project: the project, its own Codex home and its own temporary folder (ADR 0041).
   - OpenCode: every project under the host's project roots and its own folders (ADR 0028), which is why it does not
     offer `never_ask`.
7. **On the headset, no confirmation of its own.** Choosing never-ask is a control like the model's, and it takes a
   settled press like any other. Two things show instead: the level the next start will use, before that start goes,
   and the run's recorded level (decision 4), after. No sentence of warning is added. The words are the headset's
   design's to settle, not this decision's.
8. **Not gated on undo.** The level is offered wherever a sandbox bounds the agent to the task's own project (and,
   for Codex, the temporary folders of decision 6). What
   undo covers is the product's existing rule ([ADR 0036](0036-halcyonic-undoes-a-tasks-changes-from-checkpoints-it-keeps.md))
   and stays as it is. What never-ask changes differs by runtime:
   - Under `ask`, Codex already runs most commands inside its sandbox without a press. It asks for a command that
     would leave the sandbox and for one it judges dangerous (`rm -rf`, ADR 0033). Under `never_ask` the first is
     refused by Codex itself, and the second runs inside the sandbox with no press, so it can delete uncommitted work
     in the project.
   - Under `ask`, OpenCode waits for every shell command, so once it offers `never_ask` a command could delete
     uncommitted work in the project with no press.
   Either is the person's explicit choice, bounded to the task's own project. When tasks get a space of their own,
   a never-ask run's changes stay in that space until the person brings them back, which is the undo. The argument
   against, for AUDIT: without undo and without that space, nothing brings a deleted file back.

## Alternatives considered

- **Keep asking always.** Safest, but it fails the owner's request and the headset's use: a person tires of pressing.
- **A middle level** (edits without asking, commands asking): today's `ask` already is one for Codex and OpenCode,
  whose edits inside the project need no press.
- **A per-project setting in the journal.** One more command and event for a choice the headset can remember itself.
- **The runtimes' own bypass modes** (`danger-full-access`, `bypassPermissions`, OpenCode's `--auto`): they widen
  what the agent may touch, not only who decides.

## Consequences

- A person can let an agent work in a bounded project without pressing Approve, and sees the level the runtime
  confirmed.
- A runtime upgrade, or a change to how Halcyonic starts it, needs its declaration verified again; the end to end
  suites pin it.
- Claude Code and OpenCode need a sandbox Halcyonic controls, bounded to one task's project, before they can offer
  the level; that is a decision of its own ([ADR 0048](0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md)).
- Revisit when a runtime marks escalations in its requests, or when a third level is wanted.

## Amendment 2026-11-10: the headset's default

On the headset a task's next start is never ask by default wherever the runtime that start uses offers it AND the
level is shown before the start goes, and ask anywhere else: the owner asked for no constant pressing of Approve in VR, and inside the sandbox an asking Codex
run asks before every command (37 of 37 in VRIFY's survey, `ls` and `cat` among them). The person chooses Ask First
for a project with one press, and goes back with the same press.

- The choice is the person's, per project: the agent app, the model, and the level where they chose one. The
  headset keeps it across restarts of the app, where it keeps its settings.
- The default and a kept choice change nothing of decision 2: a start carries `never_ask` only where the runtime's
  descriptor offers it at that start; anywhere else it leaves the level out, which means ask.
- Decision 7 holds: the level shown before a start is the level sent. An instruction armed for its confirmation goes
  only on the agent, model and level it was armed with; a new project's review is made at its level, and a build
  confirms only that level.
- Shown before the start means on the screen that sends it (AUDIT, 2026-11-11): the redesign's row names the agent and
  its level at rest, so a task's next start there takes the default; the older kit names no level, so it asks unless
  the person chose never ask; new work takes the default only where its review names the level; Make VR Version's
  copy takes only the level the person chose for its source project.
- The control plane's default is unchanged: an `execution.start` without a level asks. The default is the headset's.

## Note, 2026-11-10: every Codex folder is confined on macOS (BOX-2)

Decision 6's "Codex on an open project" now holds only off macOS. On macOS every Codex execution's folder runs on a
confined server of its own ([ADR 0041](0041-a-new-apps-agent-is-confined-to-its-project.md), amendment of 2026-11-10).
That server writes only the folder, its own home and its own temporary folder; never `/tmp` or the person's `$TMPDIR`.
Inside it, a start that asks gets `untrusted`, which asks before every command Codex does not know is safe, and
`never_ask` gets `never`. A starter's project still asks nothing. The decision is otherwise unchanged.

## Note, 2026-11-11: OpenCode offers never-ask again (BOX-3)

OpenCode now runs one server per execution folder, and each server writes that folder alone ([ADR
0028](0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md), amendment of 2026-11-10). So decision 5's reason for
not offering it is gone. Inside Halcyonic's sandbox it declares `offers_never_ask` again, with the session rules of
[approval-levels.md](../validation/approval-levels.md). A start that never asks is refused without the sandbox, at the
check and again at the start.

The end to end test now holds AUDIT's case, another project under the same root as the task's, which a never-asking
shell call could neither overwrite nor add to. It fails when the server's writable folder is widened to that root.
Decision 6's line for OpenCode now reads: its folder and its own folders. Reads stay open until BOX-3b.

## Note, 2026-11-11: a folder that holds another project (AUDIT BOX-3 F1)

The control plane binds a root as a project and each folder inside it as another, so a task's folder can hold
another bound project's folder. A sandbox that writes its folder would write that project too: AUDIT showed a
never-asking OpenCode run rewriting it, and a confined Codex run had the same exposure.

- **Codex and OpenCode alike:** a run's profile now denies reads and writes of every other bound project's folder
  inside its own, last, so no allowance reopens them. The list is read from the journal at each start
  (`foldersInside` in `apps/control-plane/src/runtimes.ts`).
- **OpenCode:** a project bound inside a folder after that folder's server started replaces the server at the next
  start, once no turn runs there. Its tasks at rest are reported lost, in words, since OpenCode resumes no session
  on another server. While a turn runs, the start is refused in words.
- **Where a sandbox cannot do it** (Codex on the open server, off macOS): a start that never asks is refused for a
  folder that holds another project.
- **A task already at rest** (AUDIT BOX-3 fix F1): its server keeps the profile it was made with, so each instruction
  looks again. Where a project was bound inside the folder since the server started, the instruction is refused in
  words, on both runtimes ("Start a new task here to go on"). The next start then gets the bounded profile.
- **Tests:** on this Mac and on both pinned binaries, with a project inside the task's folder and one beside it,
  neither was read or written by a never-asking run. Each test fails with the deny taken out.
