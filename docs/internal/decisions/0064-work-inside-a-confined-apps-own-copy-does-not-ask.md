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
   Nothing changes for a connected folder, a plain folder, an unconfined start or any other host.
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
