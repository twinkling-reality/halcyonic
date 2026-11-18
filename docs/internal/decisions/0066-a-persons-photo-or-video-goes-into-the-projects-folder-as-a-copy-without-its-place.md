# ADR 0066: A person's photo or video goes into the project's folder as a copy without its place, with stills the host made

- Status: Accepted
- Date: 2026-11-16

## Context

A person wants a photo or video of their own, above all footage from camera glasses, to become part of what agents
build: a place to stand in that takes its colours from the clip, the clip itself on a window in it. Nothing on main
let them. [ADR 0058](0058-an-instructions-pictures-come-from-the-projects-folder-and-reach-the-runtime-as-the-bytes-the-host-read.md)
lets an instruction carry pictures already in the folder (PNG, JPEG or WebP, at most 5 MiB) or given to the host for
24 hours and never kept in the project; there was no video at all, and phone photos are HEIC, which no agent reads as
a picture. Phone and glasses media carry where they were taken (EXIF GPS, a QuickTime location atom, timed metadata
tracks) and the device's name, and a project's files reach checkpoints, task copies, GitHub and, through a built
piece, a public link.

Verified on 2026-11-16, metadata only: the sample clips are HEVC in MOV or MP4, 6.6 to 21 s and 12.5 to 69.4 MB.
Read in the code: a turn's observed changes read at most 256 MiB (`file-observer.ts`), Send refuses more than 512 MB
of history not yet on the remote (`github.ts`), the checkpoint store refuses a folder over 2 GiB (`store.ts`), and
GitHub refuses a file over 100 MiB. The shape went to review before code (five versions, the review's conditions
M1 to M9, C1 to C8, D1 to D4, E1 and E2, K1 and K2, and a fresh critic's read), and then the contract was written.

## Decision

1. **Two steps.** The Mac's Photos and Videos page (`/setup/media`, behind the page's session and its Host, Origin
   and Sec-Fetch-Site checks, which run before a byte of the body is read) streams the person's file into
   `<data dir>/media` (0700, files 0600) with its SHA-256 taken as it arrives: raw bytes only, one at a time, at most
   `MEDIA_VIDEO_BYTES` (80 MB), checked by structure (a picture as ADR 0058 reads one; HEIC, MOV and MP4 by their
   `ftyp` brand and a walk of their top-level boxes to the file's exact end, with `moov` for a video), kept at most
   4 files and 2 hours, never journaled, never logged. The page then submits `project.media.add {project_id, media}`
   as a local press. No other route takes media.
2. **What is placed is a copy, never the bytes as they came.** A video is remuxed keeping only its first video and
   audio streams with every container and stream tag dropped; a photo is re-encoded as a JPEG with no tags. The
   journal holds the placed copy's path, type, size and digest, never the person's file name, the upload's digest or
   anything read from the file's tags.
3. **The host chooses the name and writes only there.** `media/<photo|video>-<12 hex of the copy's digest>.<ext>`,
   created directly with `O_EXCL` and `O_NOFOLLOW_ANY` (no link anywhere in its path), every folder of `media/`
   made without recursion or found a real folder whose real path is where it should be, never in a folder of the
   host's own, never renamed, never over another file (`media_present`). `project.media.add` is one of the commands
   that write the project's folder, so it waits for, and holds off, a running task, a restore, a check or another
   folder command; past 50 media, or past `MEDIA_FOLDER_BYTES` (240 MB) once the copy's size is known, it is refused
   (`media_full`).
4. **Stills and a play copy, by one pinned tool.** The host makes one still of a photo and four of a video (at 10, 35,
   60 and 85% of its length, long side at most 1600 px) and, for a video, an H.264 copy any browser plays (long side
   at most 1280 px, 6 Mb/s), with ffmpeg named by `HALCYONIC_FFMPEG` and run only while its executable's real bytes
   have the SHA-256 the person recorded with `pnpm mac-setup ffmpeg` (`tool_changed` otherwise, never taken by
   itself). It runs with arguments as an array, no shell, a time bound, four environment variables, and a seatbelt
   profile that denies everything but reading its one input by literal path, its own and the system's libraries, and
   writing one fresh folder of the data folder; what it prints is never logged. The host checks each output
   (`wholePicture` and `PICTURE_BYTES` for a still, the box walk for a copy) and places it as in 3, each only while
   the folder stays within its bound (`too_large`, `no_room`). `project.media.prepared` records them or why not, and
   only then does the command complete, so no task copy is cloned from half-written files. No tool: nothing is added.
5. **A stop mid-write leaves nothing behind but the host's own.** Before each write the host notes the name, size
   and digest in its own data folder; at the next start, before anything is placed, it removes a noted file only if
   its path is one of its own names, `media/` is still the folder's own, the file opens without a link as a regular
   file no larger than noted, and its medium was never recorded added.
6. **What an agent gets.** A medium given to an instruction (`{kind: 'media', path}`) reaches a runtime that takes
   pictures as its stills (ADR 0058 pictures, counted in its four after expansion), and every runtime as one line
   after the person's words, marked as Halora's own and made only from the host's paths and the length it read.
   `execution.instruction.media_given` records which media and that the line was added, before the runtime is handed
   the instruction; the line itself is never journaled. ADR 0058 is amended for this line.

## Alternatives considered

- A file dropped into the folder by hand: never journaled, never stripped, no stills.
- One route that copies and journals at once: a state change outside a command.
- An upload route for paired devices: nothing would call it yet, and the network listener's request bound would cut
  it; a later version adds one with its own bound.
- macOS's own tools (AVFoundation through `osascript`, `sips`, `avconvert`): no install, but `sips` keeps EXIF and
  AVFoundation reaches system services, so neither is held to a closed profile as simply as one pinned tool.
- The person's file kept as given, with confirms saying it holds where it was taken: the place would reach every
  checkpoint, task copy and send of the project.
- A small photo as its own still: both are true to the contract; one made still for every photo gives every runtime a
  host-made, stripped JPEG by one path.

## Consequences

- A Homebrew upgrade of ffmpeg stops adding until the person records it again; SECURITY.md states that the pin covers
  the executable's own bytes, not the libraries it loads.
- A residual: a task copy cloned in the moment before the start's sweep settles could carry a cut-off file of the
  host's own name under `media/`; a new add waits for the sweep, which is a few file checks.
- An add stopped by a failure after the medium is in removes what it had made from it at once; the medium stays, and
  `project.media.prepared` says nothing was made.
- A paired device cannot upload in this version.
- A medium an agent copies into a built piece is public once the piece is shared.
- Removing a medium is the person's own; the view stays the journal's, and a read says when the file is gone.
- Revisit if a runtime reads video itself, or if a person needs media larger than 80 MB.

## Amendment, 2026-11-18: the person's media is what to make

After the host's line about each photo or video it gives a task, it adds one line, once: "From Halora: the photos and videos
above are the person's reference for what to make. Build what they show in the app itself, and show a photo or video as a picture only
where the person's words ask for that." (`MEDIA_IS_REFERENCE` in `command-service.ts`.) A VR version of a
camel ride had hung the video on a board instead of making the ride a place; the starter's own notes had asked for that
window and now no longer do (ADR 0037's amendment of the same day). Like the lines before it, it is never journaled.
