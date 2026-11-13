# ADR 0065: A request too long to show whole is denied by the host, with its reason

- Status: Accepted
- Date: 2026-11-13

## Context

The control plane cut an approval's summary at 2000 characters and marked a cut request not approvable, so that nobody
approves what nobody could read whole. Run (b3) on the proving plane (Meta's model through OpenCode) met the cost: the
agent asked to run an inline script of more than 2000 characters, the card could only be denied, nothing told the
agent why, and the run stood still until a person denied it by hand. The lead ruled on 2026-11-13 (17:07Z) that a
person is never left with a card whose only button is Deny, and AUDIT3 passed the shape (18:17Z) with conditions.

## Decision

1. **Two bounds.** The request's summary is kept whole for the record up to 32000 characters
   (`ApprovalSubject.summary`, the bound an agent's message has; cut beyond it). It is approvable only up to the
   longest request the headset's card shows whole and holds Approve to its last part: `APPROVABLE_LIMIT`, 2000 today.
   The headset already pages a request and holds Yes to its last part (XR, 19:36Z), but that is not yet tried at
   length, so raising the approvable bound is one constant once a client test pages a long request whole and the
   lead says how long a request a person should be asked to read.
2. **Beyond the approvable bound the host denies the step at once where it would reach a person**, confined or not
   (`HostDenials`, `apps/control-plane/src/core/host-denials.ts`): the request carries `too_long: { length }`, the
   host records `execution.approval.denied_by_host` with the reason and length, then sends the same denial a press
   sends, with words that say why and name the bound, and suggest nothing more: while a file write's approval shows
   only its path, "write it as a file" would be a way round the rule (AUDIT2, AUDIT3; the lead, 2026-11-13 19:46Z).
   The advice comes back with the second half of this decision, ordered the same evening: an approval shows whole
   what it approves, a write's content and an edit's old and new text too.
   **A confined app's own work is not denied for length** (AUDIT3 M1, the lead 19:53Z): the bound is a person's
   reading bound, and in a confined copy no person approves (ADR 0064). One decision (`requestOutcome`) says, for
   each request, whether the host answers it (the no-ask rule holds and the agent app reported it complete:
   `too_long.reported_complete`), denies it for length, or leaves it to the person; the host's answer approves it
   whatever its length, keeps it whole in the record up to 32000 and, beyond that, records the cut and the whole
   length (`cut`), the whole being the project's own copy.
3. **Whether the agent was told is declared, never assumed.** `RuntimeDescriptor.delivers_denial_message`: Claude
   Agent declares it (the SDK's deny carries `message` as the tool result the model reads). OpenCode 2.0.18 delivers a
   denial's message nowhere (its e2e) and Codex 0.157.0's approval reply has no field for one (its own schema,
   [approval-levels.md](../validation/approval-levels.md)): the event records `delivered: false` there.
4. **What is shown is what runs** (AUDIT3 L1). Characters a card does not draw (C0 and C1 controls but tab and
   newline, carriage return, the bidirectional overrides and isolates, the zero-width characters and the byte order
   mark) are written in the summary as their escape (`\u{202E}`), for every request, before it is fitted.

## Alternatives considered

- **Raise the approvable bound to 32000 now.** The card's gate exists but no test has paged 30000 characters (about
  a hundred parts); approval must rest on what the card shows to the end (AUDIT3).
- **Keep the Deny-only card and add its reason.** It still leaves the run waiting on a person for a step nobody may
  approve.
- **Deny without words.** The agent would retry the same command.

## Consequences

- A long one-line command no longer stalls a run: the agent is told why, where its app can carry it.
- An agent app that carries no denial's words (OpenCode, Codex) still sees only a failed tool; the person reads why.
- Raising `APPROVABLE_LIMIT` waits on the headset's paged card being tried at length.
