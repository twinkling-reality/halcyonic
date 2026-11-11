# ADR 0044: A failed check goes back to its agent on its own, within a cap

- Status: Accepted; built in the control plane and tested through the mock; headset view pending
- Date: 2026-11-10

## Context

The host checks a starter project on its own after a turn that changed its files (A1, ADR 0037),
and a failed check's first lines go to the agent with the person's next instruction (FX1, the
amendment of 2026-10-31 to ADR 0037). Until a person speaks again, nothing happens: a beginner
who does not read a type error sees a broken game and does not know what to say. The owner asked
for a harness that helps by itself and explains itself to anyone (2026-11-10).

What the code had: the automatic check (`control-plane.ts`, `#attachAutoCheck`), the check's
lines for an agent (`preview.ts`, `failedCheckOutput`; `check-output.ts`), their delivery with any
instruction, once per check (`command-service.ts`, `#checkOutputFor`), and `received_via:
'internal'` with a null principal for the host's own commands. Every runtime Halcyonic ships
(Codex, OpenCode, Claude Agent, the mock) declares `instruct_at_rest`; the recorded run and the
demonstration do not.

## Decision

When an automatic check of a starter project fails after a turn that changed its files, the host
sends the failure back to the agent whose turn it was, as that agent's next instruction, up to a
cap of two tries per chain. `apps/control-plane/src/core/auto-fix.ts` does it.

- A try is sent only when all of these hold at that moment: exactly one execution changed the
  project's files since its last check began; that execution's last turn completed; the person has
  not asked to stop it since their own last instruction to it, whether that Stop was accepted or
  refused; no approval or question waits; no other task or command in the project is under way;
  its runtime declares `instruct_at_rest`; the chain has tries left; the check gave something an
  agent can act on: its type check or build lines, the page's errors, or a page that showed nothing
  at all. A page that shows only the starter's own scene (`drawn` false with the starter's edges) is
  a game not made yet, not a fault, and is not sent: no one asked this agent to make it. The first
  real run (2026-11-10, Codex 0.157.0 on a local 30B model) found this case: the first try fixed a
  type error, and the check still failed on the starter's own look.
- The try is `execution.send_instruction`, submitted `internal` with a null principal through
  `CommandService`, so admission checks it like any command. Its text is the host's own words,
  marked as sent by the computer and not by the person, then the page's errors, which are already
  journaled with the check; FX1 adds the check's lines after them, which are never journaled.
- `execution.auto_fix.sent` is recorded before the command is submitted, naming the check, the
  try, the cap and the command. Delivery stays that command's own outcome.
- `execution.auto_fix.ended` records why a chain ended, or why none began (0 tries): `passed`,
  `cap_reached`, `person_stopped`, `person_instructed`, `waiting_on_person`, `project_busy`,
  `runtime_cannot`, `turn_failed`, `no_change`, `nothing_to_send`, `check_unavailable`,
  `send_failed`, `several_tasks`, `host_restarted`.
- Stop ends a chain at once. A Stop accepted during a try ends the chain on its acceptance. A Stop
  refused because the task was at rest still counts: no try follows it, and when a try already sent
  has not begun its turn, the host interrupts that turn as soon as it runs.
- The person's own start or instruction to any task in the project ends that project's chains.
  Their turn may then begin a new chain. A turn the host set going whose chain has ended begins none.
- The person's words to a task, refused or not, count like their Stop for that task: refused (a try
  runs on a runtime that takes no instruction while running, or the try's own command has no outcome
  yet), they end its chain `person_instructed` and no try follows until the person's own instruction
  is accepted (AUDIT2's finding, 2026-11-10).
- A try refused at once never reached the agent and is not counted in the end's `attempts`. A refused
  automatic check ends the chains it followed `check_unavailable`. A chain is ended `host_restarted`
  only on the person's own journal, never a fixture's.
- A chain under way when the host stops ends `host_restarted` at the next start; nothing is
  replayed, as ADR 0030 does for the request coordinator.
- When two or more tasks changed the project before its check, none is sent the failure: whose
  change failed it is not known.
- The execution view's optional `auto_fix` carries the latest chain's state and numbers (`fixing`
  or `ended`, the check, the try, the cap, the reason), never words: clients choose those. Since
  2026-11-11 it also carries `failed_steps` (`typecheck`, `build`, `page`): while a try runs, what it
  was sent to fix; after `passed`, what got fixed; after a failed check, that check's. So the headset
  can say what failed without matching the project's latest check, which a newer check replaces.
- On wherever checks are; `ControlPlaneOptions.autoFix: false` turns it off.

## Alternatives considered

- Leave the failure for the person's next words (FX1 alone): a beginner may never find them.
- A planning loop with a model choosing whether to retry: a model call per check, and a decision
  that is not the person's. A fixed cap and fixed words are predictable and cost nothing.
- A new command to turn it off per project: the person's Stop already ends it, and their own words
  take over; a toggle can follow if the headset needs one.
- Send it to the latest of several tasks that changed the project: a guess about whose change
  failed the check (the lead's amendment, 2026-11-10).

## Consequences

- Real instructions reach an agent with no person present. Every one is journaled and marked as
  the host's own, admission checks each, and the cap bounds them. An auditor reads each change here.
- Only starter projects are checked, so only they fix themselves. Other projects need a check of
  their own first.
- An older generated C# client fails on the two new event types; the headset must be rebuilt with
  the regenerated bindings before a host that sends them (REALTIME.md's upgrade order).
- The headset should show a try under way and how a chain ended. Draft words, for the FIELD lane to
  settle in WORDS.md: Fixing · Try 1 of 2 while a try runs, with Stop; Fixed · Check Passed;
  Still Failing · Stopped After 2 Tries · See Failures.
- Revisit when checks become per task (TASKS lane): the failed check will name its task, which
  replaces "the execution whose turn changed the files".
