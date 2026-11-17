# ADR 0064: Work inside a confined app's own copy does not ask

- Status: Accepted
- Date: 2026-11-13

## Context

A new app's agent runs inside Halcyonic's closed profile ([ADR 0048](0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md)).
A run whose start recorded `confined: true` can read only its own folder, the starter's shared files and the original
it was made from, can write only its own folder, its home and its temporary folder, and reaches only the loopback
ports the host gives it. Since 1111-b134 it can write nothing in its folder that runs later outside the sandbox
(git data and the files an editor runs when a folder opens).

Yet each step asked. The hosted Make VR Version runs on the proving plane took 5, 21 and 13 approvals with Claude
Agent, every one inside that boundary; a person at a desk tool gives none. With OpenCode on Muse Wonder, run (b) was
stopped by its answering script at OpenCode's first request: OpenCode asks `external_directory` for every folder of
the original it reads, again each time, since its replies are once only. In the product that is a card naming a
folder and no file, several in a few seconds, before any work.

The lead ruled on 2026-11-13 (answers of 12:43Z and 13:45Z) that in a confined app work inside its own copy and
reads of a declared original do not ask, that a shell command is answered under the closed profile, never by
reading its words, that every step the host answers stays readable whole, and that an adapter that cannot give a
request's places as structure does not get the host's answer. AUDIT3 read the shape (12:42Z, conditions C1 to C4).

## Decision

1. **Where.** Only for an execution whose start recorded `confined: true`, in a folder that is a starter's project.
   Nothing changes for a connected folder, a plain folder, an unconfined start or any other host. (Amended
   2026-11-17: also a task's own copy of a connected folder under the closed profile; see the end.)
2. **The host answers, Claude Agent.** The adapter gives a file tool's places as structure on the request
   (`ApprovalSubject.paths`, by the SDK's own tool schemas, [claude-code-sandbox.md](../validation/claude-code-sandbox.md)):
   Write, Edit and NotebookEdit write, Read, Glob and Grep read. A Glob pattern or Grep glob that is absolute, starts
   at a home or holds `..` gives none. The control plane (`HostAnswers`, `apps/control-plane/src/core/host-answers.ts`)
   approves a complete request when every place, resolved through links, is inside the task's folder (a write never
   in git data or an open-time file), or is read in a declared original or the starter's shared files; or when it is
   the shell (`Bash`), which the profile holds. It records `execution.approval.answered_by_host` with why and the
   places as it resolved them, then sends the same `execution.respond_to_approval` a press sends. A write's test of
   git data and open-time files folds case, as the Mac's disk does. If that command is not accepted, the host
   records `execution.approval.host_answer_withdrawn` and the request is a card after all.
3. **OpenCode, at its own layer.** A confined folder's session rules allow `external_directory` for each declared
   original (its real path) and every folder inside it, and refuse the original's `.git`; an original whose path holds
   `*` or `?` gets no such rule and asks. Its edits inside its folder
   already ask nothing at its own layer. What it still asks there, a shell command, is answered by the host as
   above, whole.
4. **Codex** already asks nothing on a starter's confined project ([ADR 0041](0041-a-new-apps-agent-is-confined-to-its-project.md));
   unchanged.
5. **What still asks.** Every question, a request that is not complete, a file tool with no places, any place
   outside, a write into an original, a read of an original's `.git`, a link out, MCP, web and network tools and every unknown tool.
6. **What is shown.** A request the host answered is never a card. The execution counts what went ahead and what
   asked (`ExecutionView.approvals`).

## Alternatives considered

- **Read the shell command and decide.** Rejected by the lead: the profile, not the words of a command, is what
  holds it.
- **A setting the person picks** (ADR 0043's `never_ask`). It stays for other runs; here the profile already makes
  the card protect nothing, so it is the confined app's rule, not a level.
- **OpenCode's folder requests answered by the host.** Its own session rules are the layer that asks; answering a
  card that names a folder and no file would still record a step that tells the person nothing.

## Consequences

- A confined Make VR Version runs without the person pressing anything for work inside its own copy.
- The boundary is the profile alone for these steps: a door it leaves open is now walked without a card. AUDIT3
  reads every change to the profile with that in mind.
- A Claude Agent tool added in a later SDK asks until it is named here with its places verified.
- Not yet drawn on the headset: the count and each step as "went ahead" are in the projection for the clients.

## Amended 2026-11-16: reads in the run's own temporary folder

A confined Claude Agent run that pulls a frame from a video with a command writes it into its TMPDIR, the folder the
host made for runs in that folder (`claude-sandbox-<key>/tmp` under the data folder), then reads it to look at it.
That read was left for a person, so a task with media stalled on a VR app's copy (LOOP, 2026-11-16). The host now
answers a complete request whose every place is a read strictly inside that one folder, by the host's own record of it
(`confinedTempFolder`, never a path from the agent), resolved through links, with the reason `run_temp_read`. A write
there, a link out of it, the folder itself, a sibling under the run's sandbox, another folder's temporary folder, or a
request naming it with any other place still reaches the person (AUDIT3 T1 to T6). It rides protocol 3.

## Amended 2026-11-17: a copy of a folder the person connected, under the closed profile

The line is the sandbox, not where the project came from (AUDIT3's shape H1 to H5, the lead's 15:17Z ruling). The host
answers, as above, in a task's own copy of a folder the person connected too, where:
- the execution's start recorded `confined: true` and its runtime kind runs such copies under the closed profile
  (`connectedCopies`: OpenCode, and Codex, which holds every folder there); Claude Code's copies run under the copy
  profile, whose network is open, so every request there keeps asking (`OPEN_RUN_ASKS` holds its unasked git reads);
- its folder is that task's own copy, as `workstream.copy_made` recorded it, never the project's folder: a task that
  works in the folder itself is never answered (`ConfinedReads.connectedCopy`, checked against the workstream's copy).
Same kinds and refusals as a starter's project: OpenCode's shell, whole; Codex's command only from its parts
(`ApprovalSubject.command`): one that asks for the network, has no folder, or runs in a folder outside the copy (links
resolved) asks. Every answer is journaled with its reason. Add to Project still shows its own review and asks.
A start's level says "Asks Only Outside Task Folder" there for OpenCode and for Codex, whose patches inside its writable
folder apply without asking at its own layer (codex-capabilities.md) and whose commands there the host answers; Claude
Code's copies say "Asks First", since nothing there is answered.

The temporary folder counts only while it is a real folder, never a link: a run may remove its own TMPDIR and put a
link in its place (seen by LOOP's probe under the profile), so the host takes the folder only when it is a real
directory and resolves its parent alone. Residual, as for every answered read: between the host's answer and the
runtime's read the run could swap the folder for a link; the read then goes where the link points, still bounded by
the profile fixed at launch, while the journal says `run_temp_read`.
