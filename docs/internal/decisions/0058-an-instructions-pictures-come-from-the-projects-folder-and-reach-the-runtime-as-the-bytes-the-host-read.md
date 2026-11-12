# ADR 0058: An instruction's pictures come from the project's folder, and reach the runtime as the bytes the host read

- Status: Accepted
- Date: 2026-11-11

## Context

A person directing a task often has something to show, not only to say: a drawing, a screenshot, a picture of
how a thing should look. Before this decision Halcyonic carried text only, in its contracts, its adapter interface
and all three adapters.

Each runtime takes pictures (parity A4). This was verified on 2026-11-11 against each runtime's end to end fake
provider, with the pinned binaries ([picture-and-file-input.md](../validation/picture-and-file-input.md)):

- **Claude Code** sends a base64 image block to the Messages API byte for byte.
- **Codex 0.157.0** takes `{type: "image", url}` items in `turn/start` and `turn/steer`. A `data:` URL reaches the
  provider as an `input_image` of the same bytes, for a model outside its own catalog too.
  - A picture it cannot decode is replaced with "image content omitted because it could not be processed", and
    the words are sent alone.
- **OpenCode 2.0.18** takes `files: [{uri}]` on a prompt, `data:` included, at a start and steered.
  - A picture it cannot decode fails the prompt with a 400.
  - For a model it lists as taking text only, it sends no picture, putting an error line in its place. Its
    Ollama models are listed so.

So a picture can be lost silently in two ways: one a runtime cannot decode, and one a model does not take.
Either way the person believes the agent saw what it never saw.

## Decision

- **What may go with an instruction:** `execution.start` and `execution.send_instruction` take
  `attachments`.
  - At most four, each `{path, kind: "image"}`.
  - The path is relative to the project's folder. Never a path outside it, never bytes from the client.
- **The runtime declares it.** `RuntimeDescriptor.takes_pictures` is declared only once a runtime test shows the
  real send hands the bytes on. Today Claude Code, Codex and OpenCode declare it; the mock does so only when
  told to.
  - Admission refuses pictures for any other runtime (`capability_unsupported`), and for a project without a
    folder (`location_required`).
  - The words are never sent without the pictures the person chose.
- **The host reads each picture once,** when the instruction is sent: at the start, at the send, or when a
  waiting instruction is delivered.
  - It reads it as the project's own file read does: inside the folder, never through a link anywhere in its path,
    never a withheld name or a folder of the host's own, judged and read through one handle.
  - At most 5 MB.
  - What it is (PNG, JPEG or WebP) comes from its first bytes, never its name.
  - It must be whole by its structure: a PNG's chunks with their CRCs and image data of the size its header
    names; a JPEG's segments through to its scan and end marker; a WebP's RIFF size and first chunk.
- **A picture that cannot be read fails the command** with effect `none`, in words that name the picture:
  `picture_not_found`, `picture_withheld`, `picture_too_large` or `picture_not_a_picture`. Nothing is sent and
  no execution is created.
- **The runtime is handed the bytes the host read, never a path:**
  - Claude Code: base64 image blocks before the words;
  - Codex: `image` items with a `data:` URL after the words;
  - OpenCode: `data:` files.
  - So no runtime reads the file again, and what it was given is what the journal names.
- **OpenCode checks the model.** Before a prompt with pictures, the OpenCode adapter reads the session's model and
  OpenCode's list. It refuses, with no effect, a model that is not listed as taking images.
  - `RuntimeModel.picture_input` says what each runtime's list says: `declared`, `not_declared`, or absent for
    unknown.
- **The journal keeps what was read, never the bytes.** `execution.instruction.pictures_read` holds each picture's
  path, kind, size and SHA-256, and is recorded before the runtime is given them.
- **The headset picks from what the folder holds.** `GET /api/projects/:project_id/pictures` lists the pictures an
  instruction may carry: sorted, at most 200, never hidden, withheld, linked or in `node_modules`.

## Alternatives considered

- **Bytes uploaded by the client.** Lost for now: an upload needs a store, limits and a review of its own. A
  picture in the project's folder is already the person's, and is what an agent can also open.
- **A path handed to the runtime** (Codex's `localImage`, OpenCode's `file://`). Lost: the runtime would read
  the file again after the host's check. In a confined server it might not be able to, and nothing would say
  whether the bytes it sent were the ones recorded.
- **Trusting the first bytes alone.** Lost to the two silent losses above.
- **Leaving the per-model question to OpenCode.** Lost: it answers by sending the words alone.

## Consequences

- **A person can show an agent a picture of the project.** The headset's side (pick, see it attached) is
  SCREENS's to build on the list read and the attachment shape.
- **Not yet covered:**
  - A picture of what the person sees now (a running preview, or the headset's own view) needs a capture step;
    see the validation record.
  - Codex's model list does not say whether an Ollama model takes pictures. Its `picture_input` stays absent,
    and Codex sends a picture to any model.
  - Whether a local model answers about a picture is not shown yet: the one local generation has not run.
- **Revisit** when a runtime is added or upgraded (each needs its own runtime test before it declares
  `takes_pictures`), or if the person needs pictures from outside the project.

## Amended 2026-11-12: a picture the person gives

A picture need not be in the project's folder. What the person sees in the headset, or a frame of the running game,
goes with an instruction as a desk session pastes a screenshot (CORE's first report of 2026-11-12, item (a)3):
- `POST /api/pictures` (this Mac or a paired headset) takes one PNG, JPEG or WebP, whole, at most `PICTURE_BYTES`,
  refused past it before more is read; the second route that takes anything but JSON, in a scope of its own. The host
  keeps it once by its SHA-256 in `<data dir>/pictures` (0700; each file 0600), at most 64 for 24 hours, never
  journaled, never logged, and answers `{picture: {id, media_type, size_bytes}}`.
- An instruction's attachment may then be `{kind: 'given', picture: <id>}` beside `{kind: 'image', path}`. The host
  reads it again, checks its digest and that it is a whole picture, and hands the runtime those bytes, as for the
  folder's. A given picture needs no project folder. `PictureRead.path` is null for one; its `sha256` names it.

