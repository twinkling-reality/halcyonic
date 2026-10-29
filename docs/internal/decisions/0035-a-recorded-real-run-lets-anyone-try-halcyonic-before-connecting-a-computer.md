# ADR 0035: A recorded real run lets anyone try Halcyonic before connecting a computer

- Status: Accepted by the owner on 2026-10-29 ("Yes, real work"), for a recording of real agent work
  on Halcyonic's own repository; no recorded run is bundled yet
- Date: 2026-10-29
- Amends [ADR 0012](0012-judges-run-a-labeled-demonstration-on-the-headset.md): its demonstration
  stays, and a recorded real run now plays before it

## Context

Halcyonic does nothing useful until a headset reaches a computer that runs agents. Before that, a
new person, above all one who has never written code, has no way to see what directing an agent
looks like. ADR 0012 gave the headset an on-device demonstration for that moment, recorded from the
mock runtime: it shows the interface and lets a person answer, but none of its work happened, and
every task in it says so.

The first person in that position is a competition judge, who runs the release APK on their own
headset with no computer behind it. The owner's plan for the entry is a build that replays real
recorded work, clearly marked as recorded, and on 2026-10-29 the owner agreed that it may be real
agent work on Halcyonic's own repository.

What was true when this was decided:

- A real run's journal is private as it stands. The one complete run kept from earlier rehearsals
  names products and home folders inside its instructions, approval requests and agent messages,
  and rewriting those words would misreport what the person and the agent said.
- The competition's rules forbid brand names with no exception for an entrant's own products, and
  ask for an honest entry that works without another device
  ([competition-judge-build.md](../validation/competition-judge-build.md)).
- The client refused any recording whose runtime is not synthetic.
- Release builds offer no pairing (an open question in
  [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)). Installed fresh, as on a judge's headset, a
  release build has no pairing and no token, so it always plays the on-device recordings; one
  installed over a development build without clearing its data keeps both.

## Decision

A recorded real run is a product capability, not a judges-only mode: the way a person tries
Halcyonic before connecting a computer, by watching real work they know is recorded. The judge
build is its first user.

1. A real run is recorded to be shown. Its instruction is written for a reader, and its journal is
   kept, privately, outside the repository.
2. `pnpm judge:record` prepares the run to be shown and records it for the headset
   ([EVENTS.md](../architecture/EVENTS.md), "Recorded runs"). It changes only where files live, the
   runtime's and model's names, the client's label and the start command's options, and leaves
   command output out. The project folder's path is rewritten wherever it appears, in what the
   person and the agent said too; nothing else in their words is edited. Its rules refuse what they
   find: if a string names a product, a home or private folder, an address, a credential shape or a
   word from the private deny file, it writes nothing, and the run is recorded again. The rules find
   only what they list, so a person (AUDIT, then the owner) reads the whole recording before it
   ships or is shared.
3. The headset plays it as format version 3, kind `recorded_run`
   ([XR_CLIENT.md](../architecture/XR_CLIENT.md)), labeled Recorded, never Practice or Demo. It
   holds where the run waited for someone and offers only the answer given then, saying so in words.
   Simulated work never enters a recorded run, and real work never enters the demonstration.
4. When a build has no control plane to reach and bundles a recorded run, it plays the recorded run
   first and, once its end has held, the demonstration of ADR 0012, where a person answers a
   question, denies and sees a correction round.
5. Which run is bundled is a choice for each build, not part of this decision. Each one passes the
   recorder's rules and a reading of the whole recording, by AUDIT and then the owner, before it
   ships.

## Alternatives considered

- **The demonstration only.** Nothing a person sees happened.
- **Edit a run's words until it can be shown.** It would misreport what was said.
- **Mix real and simulated work in one recording.** The labels would stop saying what a task is.
- **Recorded branches for a real run, so a person can answer it.** A real agent cannot be asked the
  same thing twice from the same state; a second branch would be another run.
- **A recorded run only in the judge build.** The same moment, a person with nothing connected yet,
  comes for everyone who installs Halcyonic, and a mode built for one audience would drift.

## Consequences

- A person meets real work first, then acts in the demonstration.
- A recorded run cannot follow a deny or a stop: it says that it is recorded as it happened.
- The same recorder and reading are the starting point for sharing a real run later (a bug report,
  documentation, a store listing): the rules refuse what they find, and a person reads the whole
  recording before it leaves the computer. How that would work, and who reads it, is an open
  question in [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md); nothing shares one today.
- Upkeep: a change to the event format, the contracts or the pipeline means recording the run
  again from its kept journal with `pnpm judge:record`, as the demonstration is recorded again with
  `pnpm demonstration:record`. `pnpm judge:record ... --check` says whether the bundled file is
  still that run's recording, but only on a computer that holds the journal: unlike the
  demonstration's check, `pnpm check` cannot run it, because the journal never enters the
  repository. Losing the journal means recording a new run.
- Revisit if a run that asked the person a question is wanted (the recorder refuses one today), or
  if release builds gain pairing.
