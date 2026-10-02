# How Halcyonic speaks

Halcyonic's own words on the headset: how they sound, the word for each state of a task, and what
a person never reads. Every word lives in the client core (`apps/xr/Packages/com.halcyonic.client`),
in `StateLanguage`, `EntryText`, `WorkspaceText`, `ConnectionText`, `VoiceText` and their kin, so
tests hold them to these rules and the Unity layer only lays them out
([ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)).
Text from outside (titles, agent messages, tool output, server errors) is not ours: it shows as
written, by `LabelText`'s rule, and only where a person needs it.

## The voice

Calm and plain, like a capable colleague who says what is true now, how sure it is, and what you
can do next.

1. **Sentences a person would say.** "1 task is waiting for you", "2 tasks running", "1 task
   finished, ready to look at", "Nothing is waiting for you." Never shorthand such as "1 needs
   you" or "2 active" in a sentence.
2. **Short words only where space is fixed.** A state badge says the state's word ("Waiting for
   you"); a chip with a fixed width says the short count ("1 task waiting", "2 tasks running",
   "Hidden · 1 waiting"). Wherever there is room, the same state is said in full.
3. **Only what is true now, and how sure.** Sending, Sent, Waiting for the agent, Confirmed only
   when the agent confirmed, Couldn't do that, Not sure it happened. A finished round says nothing
   about whether the work is right, so nothing celebrates it.
4. **Lead with the state or the action**, in at most two short sentences.
5. **Buttons** begin with a verb, in sentence case, in one to three words. A confirmation is
   "Yes, {verb}" beside Cancel. An icon may stand before a button's words, never in their place.
6. **"You" for the person, "it" for the agent, "your Mac" for the host.**
7. **No blame, and a next step.** Every refusal ends with what to do: "Couldn't start: this project
   has no folder yet. Choose where its files live."
8. **The agent's words are its own**, quoted, tagged ("It says: “…”" in the workspace's log, "Agent
   says" in a section) and leaning, never restated as fact.
9. **No exclamation marks, emoji, em dashes or capitals for emphasis.** Numerals for numbers.
10. **Practice, Demo, Recorded and Last known** are said wherever they apply, beside a state and
    never inside its word.
11. **No brands in our words.** App and model names arrive as data; Salidium and Seorak appear
    only in a provenance line.

## The word for each state

One word per state, the same on the badge, in the peek, in the workspace and in every list
(`StateLanguage.WordOf`). A sentence says the same state in full.

| State | Badge | In a sentence |
| --- | --- | --- |
| No run yet | Not started | Not started yet. |
| Asked to start, not yet confirmed | Starting | Starting. Waiting for the agent to confirm. |
| Running | Working | 2 tasks running. |
| A test run in progress | Checking its work | Checking its work. |
| An approval or a question waits | Waiting for you | 1 task is waiting for you. “Fix the login” is waiting for you. |
| The round ended | Finished this round | 1 task finished, ready to look at. |
| The round ended and its checks did not pass | Checks failed | Checks: 1 failed, 23 passed. |
| The run failed | Couldn't finish | Couldn't finish: {reason}. |
| Stopped, as the agent confirmed | Stopped | Stopped. |
| Halcyonic cannot see the work | Can't tell yet | Can't tell what it's doing right now. |

The state badge also counts what waits when more than one thing does ("Waiting for you · 2"). In
counts, "paused" covers tasks at rest that are neither running nor waiting for you.

## The words a person sees

| Word | Means | Instead of |
| --- | --- | --- |
| project | One body of work, in one folder | |
| task | One workstream: one character on the stage | workstream, work item |
| work | What the agents do, in general | execution |
| agent | The AI working on a task; the character is the task, not the agent | runtime, bot |
| round | One stretch of the agent's work, until it stops to wait | turn |
| request | Something the agent wants your approval for | approval as a noun, permission |
| question | Something the agent asks you | prompt |
| checks | Tests and other verification | tests, lint |
| your Mac | The host and its control plane | control plane, server |
| folder | Where a project's files live | location, directory, path |
| agent app | The runtime, such as OpenCode or Codex, named as data | runtime, adapter |
| practice run | Work on the simulated runtime: nothing is built | mock, simulated |
| demo | The recorded demonstration | interactive example |

A person never reads: execution, journal, command, principal, scenario, capability, projection,
snapshot, workstream, access token (an access code, only where the person must act on one).

## Sentence patterns

| Kind | Pattern | Example |
| --- | --- | --- |
| A peek | {badge}, then the reason without the state's words | Waiting for you · It wants to run: make migrate |
| Nothing waiting | Nothing is waiting for you. Latest: {activity}. | Nothing is waiting for you. Latest: edit: src/auth/rate-limit.ts |
| Sent | Sent. Waiting for {who}… | Sent. Waiting for the agent… |
| Still on its way | Sent…, unavailable, where the action that sent it stood | Sent… |
| Confirmed | Confirmed: {what}. | Confirmed: it has your answer. |
| Refusal | Couldn't {verb}: {cause}. {Next step}. | Couldn't start: this project has no folder yet. Choose where its files live. |
| Unknown effect | Not sure it happened. {What to check first}. | Not sure it happened. Check the tasks on the stage before you try again. |
| Connection | {What is true}: {plain cause}. {What happens}. | Last known: can't reach your Mac. Trying again… |
| Provenance | From {source} · {when} | From Seorak · 2 min ago |
| Confirmation | Yes, {verb}, where no control stood a moment before; Cancel where the first press was | Yes, start building · Change; Yes, clear · Cancel |
| Asking about what shows | {Verb} the {thing} above? | Approve the request above? |
| Locked final press | Read to part {n} first | Read to part 3 first |
| Kept while away | Still open: {panel} | Still open: Create a project |

Some of Halcyonic's words still predate this guide; each surface takes these words as it moves to
the interface of ADR 0023, one surface at a time. The character labels, the rail and Settings, the
entry panel, the workspace and Usage left have moved; the ambient lines have not yet.
