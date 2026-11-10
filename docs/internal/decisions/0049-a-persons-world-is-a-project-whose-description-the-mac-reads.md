# ADR 0049: A person's world is a project whose description the Mac reads and the headset draws

- Status: Proposed. Built and tested on this Mac: the description's contract, the Mac's own reading of it after
  a turn, the event and the project's view that carry it, and the world starter. Not built: the headset's
  drawing of it. Not run with a model. Not seen on a headset
- Date: 2026-11-10
- Builds on [ADR 0037](0037-new-vr-apps-start-from-a-host-vetted-web-starter.md) (starters; agent-written code
  runs only in the headset's browser), [ADR 0038](0038-the-mac-observes-each-turns-file-changes-itself.md) (the
  Mac observes a turn's files itself) and [ADR 0041](0041-a-new-apps-agent-is-confined-to-its-project.md) (a
  starter project's agent is confined to its folder)

## Context

The headset's Virtual Space is the engine's stock sky: nothing of Halcyonic's is drawn there. The worlds
Halcyonic makes today are web apps for the headset's browser, a different app from the client where a person
directs agents. The wish is a place of the person's own inside that client, which they can change by speaking,
and which ties to their projects.

Read from the code and from current public documentation on 2026-11-10:

- Nothing carried a file's content to the headset. `execution.files.observed` carries paths, sizes and line
  counts, never contents.
- ADR 0037's rule stands: agent-written code is never run by the native client.
- A change a person speaks already has a whole path: speech becomes a draft on the Mac, the draft becomes an
  instruction to a task's agent, the agent edits files in its project's folder, and the Mac observes the files.
- A world generated as imagery or as a captured scene is made elsewhere, in minutes, and cannot be edited by a
  sentence; it can only be swapped. A world described by values can change one value at a time.

## Decision

1. **A world is a project.** It begins from the starter `world`, tracked under `starters/world/`: one
   description file, `world.json`, and a guide for its agent. Its agent is a task like any other. A project is a
   world only by that starter (`WORLD_STARTER_ID`), never by what its folder happens to hold.
2. **The description is data, never code.** `WorldDescription` in `packages/contracts` names everything a world
   can say: a sky, a sun or moon, stars, clouds, mist, water, the ground the person is on, wind, and at most 8
   places, each a project's name with a bearing and a distance. Every number has a range, every colour is six
   hexadecimal digits, nothing else may be present, and no member can hold a path, an address, a file name or a
   picture. A place's project name is the one free text: a client compares it with the names of the projects it
   knows and never draws it, opens it or uses it as a path.
3. **The Mac reads the file itself.** When the starter's files are in a project's folder, and after each turn
   the Mac observed there, the control plane opens `world.json` at the top of the project's own folder without
   following a link, reads at most 16 KiB, decodes it as UTF-8, parses it as JSON and validates it against the
   contract. It records `project.world.observed` through the `Recorder` only when the reading differs from the
   last one recorded: the description, rebuilt member by member in the contract's order, or why the file is not
   one (`missing`, `not_a_file`, `too_large`, `not_text`, `not_json`, `not_a_description`) with where the first
   rule broke, named by the description's own member names and list positions only. No word of the file enters
   a message, a log line or the record. The agent's own account of its edit is never the source.
4. **The project's view carries it.** `ProjectView.world` holds the newest valid description, why the file as
   last read is not one (or null), and when the Mac last read it. A refused reading keeps the last valid
   description, so a bad edit never empties the world.
5. **A world has no build.** The check that runs by itself after a turn skips a world, and `project.check` on a
   world is refused. Validating the description is the Mac's whole check of it.
6. **A starter may have no packages.** The world starter says `"packages": "none"` in its `starter.json` and its
   lockfile pins none; only with both does a starter count as installed with nothing installed, and its
   projects get the template's files and no dependency link. Every other starter installs as before.
7. **A client validates again and draws only with parts of its own.** The generated bindings do not enforce
   ranges or patterns, so a client checks every value before it draws, eases from the old values to the new, and
   draws nothing the description does not name. This part is not built yet.

## Alternatives considered

- **A project with a `world.json` in any folder is a world.** No starter change, but its agent would not be
  confined to its folder (ADR 0041 keys on the starter), the headset would offer it Make VR Version, and any
  project could become a world by a file an agent writes. Lost to the starter.
- **Let the headset fetch the file from the Mac.** The headset would then parse what an agent wrote without the
  Mac's validation, the journal would not hold what was shown, and a client connecting later would see nothing
  until it fetched. Lost to an event and a view.
- **Carry the description on `execution.files.observed`.** That event says what the Mac saw change, never
  contents (ADR 0038). Mixing one file's content into it would weaken a rule the security notes rest on.
- **A world as code the client runs.** ADR 0037 forbids it, and no vocabulary the client can check would exist.
- **Generated worlds (captured scenes, pictures).** Hosted, paid or research-licensed, minutes per world, far
  heavier than a standalone headset draws at its frame rate, and not editable by a sentence. They may come later
  as places to step into; they are not this decision.

## Consequences

- A person's spoken change reaches the headset as observed data: the instruction is sent as every instruction
  is, the Mac records what the file says once the turn ends, and the view updates.
- The vocabulary is the limit. What it cannot say, the agent can only say it cannot do. It grows by a contract
  change, a new format number and new drawing parts, never by speaking.
- The control plane now parses one file an agent wrote, as JSON, bounded at 16 KiB and validated before
  anything is kept. Every other file it still only hashes and counts.
- An older client refuses a description with members it does not know and keeps the last one it could draw.
- A person's edit of `world.json` between turns shows after the next turn the Mac observes, not before.
- Worth revisiting if a second kind of world needs members this one's vocabulary cannot hold, or if places
  should be bound to projects by id rather than by name.
