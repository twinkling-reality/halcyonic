# ADR 0051: A paired device changes a project's files or folder only with the digest of the review it read

- Status: Accepted; amended 2026-11-11 for `workstream.add_to_project` (TASKS, AUDIT's F2 on XR's A13)
- Date: 2026-11-11

## Context

Every authenticated principal was admitted alike for every command (SECURITY.md, Authorization): a
paired device could put a project's files back from a checkpoint (`project.restore`) or bind a project
to another folder (`project.set_location`), and `review_required` was a client's duty the control
plane could not see. A headset lost, borrowed or running a faulty build could change files or where
later work runs with nothing showing that a person read what would happen. The owner's direction
(2026-11-10): each device can do only what it should.

Facts read in the code on 2026-11-10: `submit()` admits a command synchronously; a restore's preview
is read from the files (`CheckpointStore.preview`, asynchronous), and the restore computes it again in
its own serialized turn before it writes anything (`Checkpointer.#restore`); a project's current folder
is in the projection.

## Decision

- **A review is a digest, carried back.** A read that shows the person what a command will do answers
  `review`: the SHA-256 of the canonical JSON of the command type, its arguments and the facts shown
  (`core/review.ts`). The restore preview answers it beside the preview; a new read,
  `POST /api/projects/:project_id/location-review` (`LocationReviewRequest`, `LocationReviewResponse`),
  answers it for a move with the folder the project works in now. The command carries it back in an
  optional `review` field of `project.restore` and `project.set_location`. No receipts are kept: no
  table, no expiry, nothing lost at a restart; the digest is journaled with the accepted command.
- **A paired device must carry it.** A device's `project.restore` without one is rejected
  `review_required`; so is a device's `project.set_location` that rebinds a project that has a folder.
  A first folder for a project needs none: nothing was there.
- **It must still be true.** The control plane computes the digest of what is true when the command
  would act. A move is checked at admission against the project's folder now (`review_stale`, from any
  principal that sends a digest). A restore is checked in its own turn, against what it would change
  then, since that is read from the files: a stale one changes nothing and is journaled as
  `project.restore_incomplete` with the failure `review_stale`, effect none. A replayed command is stale
  once the first one changed the facts.
- **The owner on the Mac** (the access token) is admitted as before when it sends no digest.
- `project.set_location` becomes `review_required` in policy, the honest statement to every client of
  what the host now enforces (the check above does not read the category). The judges' recorded
  demonstration, whose welcome lists every category, is recorded again for it: one line of its
  welcome changes (the lead's decision with AUDIT2, 2026-11-11).
- The headset: restore stays unoffered there. `ControlPlaneApi.ReviewLocationAsync` reads a move's
  review, and `BuildSequence.MoveReview` carries its digest into the move it sends.
- **Amended: adding a task's work** (`workstream.add_to_project`, ADR 0045) writes the task's files
  into the project, and the headset is its first sender, so it takes a review the same way:
  - The review is the latest reading of the task's copy. When the host reads a copy it records the
    digest with the reading (`workstream.copy_changes_read`'s `review`): the task, the copy's base,
    the copy's tree as read (which fixes every file the add would write) and the files listed with
    how each changed (`addReview`, `core/review.ts`).
  - The task's view gives it as `copy.to_add_review`, beside `to_add_files`, null where there is no
    reading or nothing left to add.
  - The add carries it back in an optional `review`. A paired device's add without one is rejected
    `review_required`. One that is not the latest reading's, from any principal, is rejected
    `review_stale`, checked at admission against the projection, so it changes nothing.
  - In the add's own turn the host still compares the copy's tree with the reviewed one (ADR 0045): a
    copy changed since, with no reading yet, adds nothing and is read again.
  - The owner on the Mac adds as before without one.

## Alternatives considered

- **Receipts kept in memory** (an id, an expiry, used once): what a digest gives without state, and lost
  at every restart.
- **Refusing both commands from devices:** takes moving a project away from the headset, which Start
  building offers.
- **Checking a restore's digest at admission:** needs the files read inside the synchronous admission;
  the restore's own turn reads them anyway, before it writes.

## Consequences

- A device can no longer change a project's files or folder without having read the review, and never
  once what it read has changed. **What the digest proves:** that what was read still holds when the
  command acts. It does not prove a person read it: the facts are ones a device can read for itself, so
  a device holding a credential can read and act on its own. That is as much as a host can know; that a
  person looked stays the client's to show (AUDIT2 S1).
- A restore's digest covers every path it would change back, remove or return, each set whole, though
  the preview lists at most the first of each (AUDIT2 S2).
- The preview names the files a restore changes, not their contents: a file changed again in the same
  way since the read does not make the review stale. That is what the person read.
- Every client that moves a project from a device reads the review first; XR's entry panel does it.
- A task's work added to its project carries a digest the same way (amended above). Revisit for any
  other command that changes a project's files.
- A reading recorded before the amendment has no review: a device cannot add from it, and its card
  waits for the copy's next reading, after the agent's next turn.
