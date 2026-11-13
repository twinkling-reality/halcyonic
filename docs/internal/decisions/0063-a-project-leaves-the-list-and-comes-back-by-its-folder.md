# ADR 0063: A project leaves the list and comes back by its folder

- Status: Accepted
- Date: 2026-11-13

## Context

A person could not take a project off their list: no command did it (VRIFY, 2026-11-13 08:40Z, taking four folders
off the proving plane). A list that only grows is not one a person keeps tidy. ADR 0060's R6 keeps "deleting a
project" unsayable and without a command; this is not that. The shape went to AUDIT2 before code (TASKS's ASK of
09:11Z, version 2 after a fresh critic found that hiding the project from the host's own reads would delete its
secret values at the next start, open its folder to other projects' agents and cut a VR version from its original),
and AUDIT2 passed it at 12:37Z with five MUSTs. The lead made it sayable at 12:35Z.

## Decision

1. **`project.remove_from_list { project_id, review }`** records `project.removed_from_list` through the Recorder.
   Nothing is deleted and no file is touched: not the folder, the task copies, the checkpoint store or the secret
   store. A folder that is gone can be removed.
2. **The projection keeps the project whole**, marked `removedAt`. Every read of the host's own is unchanged
   (`project`, `projects`, `workstreams`, `executions`), so these keep it: the secret store's start, which keeps only
   the values of names the projection lists; the held secrets for redaction and every check of what leaves; the
   folders denied to other projects' agents; a VR version's original; the copies' count and the checkpoint keep rule.
3. **Only what a person or a client reads leaves it out**, each through `projection.listed`:
   - the snapshot (its projects, tasks, executions and commands, but the removal's own);
   - the deltas, which carry the project's view once with `removed_at` and none of its tasks;
   - the realtime event stream and `/api/events`, which send nothing of it but the removal and its command, and
     the person's own requests to the assistant and their commands, so a request about it is seen to end;
   - `/api/projects`, `/api/workstreams`, `/api/locations` (its folder reads as free) and the catalog's project ids;
   - every route by its project's, a task's or an execution's id (one guard, `project_not_found`), and the lists read
     by scope (`/api/actions`, `/api/actions/choices`), whose scope no longer holds, so its folder is not read for one;
   - the assistant's reads of all work, a project chosen by index from them, and the names given to the model; a
     request about it ends at its next step (interrupted), the one that pressed Remove included, so the open request
     is no reason to refuse and its own commands do not count as work;
   - the spoken names;
   - the setup page's shared and secrets lists;
   - the list of what can be done here.
   The world reader stops reading its folder, and reads it again on return.
4. **Admission** refuses every command naming it, a task of it or an execution of it, before any command's own checks,
   with "This project was taken off the list. Add its folder again to bring it back." Ending a request stays open.
   While a removal waits for its outcome, every other command of the project is refused.
5. **The removal is refused, in words, with nothing stopped**, while:
   - a task is at work, or any command of the project waits (its own excluded);
   - the host is still re-attaching its tasks after a restart;
   - it is shared, playing on the Mac or previewing;
   - it has no folder and holds secrets, since it could not come back to remove them.
   The census of every other read, kept whole on purpose: the copies' and checkpoints' own reads of a project's tasks
   (`checkpointer.ts`, `control-plane.ts` noting copies), a restart's clean-up of previews, Mac play and shares
   (`preview.ts`, `share.ts`), the instruction queue, Ollama's unload, auto-fix, the setup page's check that a project
   exists, and the nested-folder exclusions of Send, Save a Copy, their reviews and the own check. None of them shows
   anything to a person; each acts only where the project's own state says to, and a removed project has nothing at
   work.
   Tested filtered reads: the snapshot, the deltas, realtime and `/api/events`, `/api/projects`, `/api/workstreams`,
   `/api/locations`, the route guard, the actions and choices lists, the spoken names, the assistant's reads and the
   world reader. Filtered but not tested on their own: the catalog route's project ids and the setup page's shared and
   secrets lists, which read `listedProjects` directly.
6. **The review card is read from the projection alone.** It names the project by its name and its folder, so a
   misheard name is caught before the press (the lead's condition). It lists the tasks, the tasks with changes not
   added (or never read), and the secret names kept. It warns of a published game, which stays up, and of a project
   with no folder, which cannot come back. Its digest covers every fact on it. `review_required`, confirm `review`,
   sayable.
7. **Adding the folder again brings back the same project.** A `project.create` of `existing_folder` that binds to the
   real path a removed project holds records `project.returned { location }` for that project (the one removed last,
   when several hold the path) and completes with `project_created` and `returned: true`. Its tasks, versions, rules
   choice, own check, secret names, catalog link, starter, VR link and published record come back. Its checks are
   marked changed, as a new location's are. A choice made under a review comes back without one, which is safe
   because each is checked again at use: rules by ADR 0059's digest, an own check by its command's digest when it runs.

## Alternatives considered

- **A new project when the folder is added again.** Rejected: the old history would be out of reach for good, which
  is deleting by another name.
- **Hiding the project inside the projection's own reads.** Rejected after the critic: it deletes secret values at the
  next start, weakens the sandbox for nested folders and cuts a VR version from its original. A forgotten filter now
  shows too much; it never destroys anything.
- **Stopping a share, a preview or a game playing inside the removal.** Rejected: it needs a partial effect that
  `CommandFailure` does not have, and new stop reasons in three enums. The Stop is on the same screen.
- **Refusing while a published site is up.** Rejected for one press fewer. The review says the site stays, and Take
  Down needs the project back.

## Consequences

- PROJECTION_STATE_FORMAT 23.
- Every new place a person or a client reads projects must ask `listed`; every new host read must not.
- **Open** (OPEN_QUESTIONS): a folder moved or renamed while its project is off comes back as a new project. The old
  one stays off for good, its secrets held unseen. A small list of what was taken off, with Bring Back, would answer
  that, the no-folder case and the headset's way back in one read; not now.
- A return keeps the project's own name, not the one typed when adding the folder; the `returned` flag is the client's
  cue to say so. The headset side (an open project going back to the list in words, the `returned` flag said) is
  FIELD's and XR's,
  from TASKS's note once the Mac side passes.
