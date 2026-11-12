# Default arms on the headset side

Reviewed 2026-11-12, on the client and the Unity layer as they stood after the headset batch of that night.

## What is guarded

A C# switch expression's discard arm (`_ =>`, `_ when ... =>`) answers for every value its other arms do not
list. Where that answer is a named thing (words, an enum member, another case's name), a value nobody listed is
shown as something nobody observed. The night's first finding was of that kind: the fixing line called a project's
own check "Page Drawn", the name of another step, because the arm that took it named the page step. AGENTS.md asks
for `unknown` where nothing was observed, so an answer for values nobody named says so in plain words or says nothing.

`tooling/log-calls.test.ts` guards log calls in the same spirit; `tooling/default-arms.test.ts` guards these arms.
It reads `apps/xr/Packages/com.halcyonic.client/Runtime` and `apps/xr/Assets/Halcyonic` outside Editor folders, with
comments and string contents blanked so braces in them are not counted. Each discard arm:

- passes when it names nothing: null, a throw, false, an empty value, or a word that says unknown or none;
- passes when the switch is over one enum and at most one member is left for it. The test finds that member from
  the enum's own declaration (the client's, the Unity layer's or the generated contracts'), so a new member makes
  the arm fail on the day it is added;
- otherwise must be listed in `tooling/default-arms.reviewed.json` with why it is safe. A new or changed arm fails
  until a person reviews it, and an entry whose arm is gone fails too, so the list never holds a stale pass.

It reads code, not behaviour: it narrows what a review must look at and proves nothing about an arm it passes.

## What the review found

Of 248 discard arms, 101 name nothing and 75 stand for exactly one remaining member. The remaining 72 were read one
by one, with the values that can reach each:

- 21 choose only a look (a colour, a size, a glyph, a sound);
- 24 are true for every value that reaches them;
- 13 are a deliberately generic failure, true though vague;
- 7 choose a screen, a section, a command or a control;
- 7 tell a person something false for a value that reaches them.

One of the seven was fixed the same night, when the fixing line named every step and fell back to its plain line
for a step it cannot name. The other six stay listed, marked `KNOWN WRONG`, each saying what is owed, and each fix
removes its entry:

- Pending words for commands no agent takes ("Sent · Awaiting Agent" for creating a project, a check, a share or a
  restore).
- A reason the computer gave, `own_sources_not_set_up`, shown as "No Reason Given".
- Pairing failures that were never refusals (`unreachable`, a malformed `invalid_code` that was never sent, any
  other `error`) shown as "Pairing Refused" with advice to reopen pairing.
- `companion_failed`, sent after the model was asked, shown as "Companion Not Asked".
- A status the preview relay passes through unnamed, shown as the preview not answering. This is true for a 502
  and not for an app's own 409.
- A failed share with no listed code, shown as the way not answering.

Each of these is a host code with no words of its own on the headset. One table that turns the host's codes into
words, with unknown said as unknown, is what removes them.

Later the same night five of the six got words of their own, each unknown code said as unknown: the pending words,
the reason a source gave, the companion that was asked and failed, the pairing refusals and the preview relay. Their
entries left the list; the failed share with no listed code is the one still owed.
