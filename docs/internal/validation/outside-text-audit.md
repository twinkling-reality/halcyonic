# Text from outside on the headset: every line that can carry it

- **Question:** Where does text Halcyonic did not write reach a line a person reads on the headset,
  and on each path: does it go through `LabelText`'s rule and a length limit, or is the line chosen by
  code; can the reader tell it is someone else's words; and could it pass for Halcyonic's own words,
  as a task titled "Your computer refused this headset" or an answer labelled like a button?
  Outside means an agent's or runtime's text (titles, questions, options, approval requests, tool and
  test names, messages), the control plane's, Salidium's and Seorak's data, file system names, model
  and runtime names, the companion's words, what speech heard, and the recorded demonstration.
  Failure reasons and refusals were audited before, in lane W's batches, and are said by code
  (`WorkspaceText.WhyFailed`, `WhyRefused`, `EntryText`, `ConnectionText`, `PairingClient`,
  `IntelligenceText.Why` and `WhyUnread`); they are left out here unless a message still shows.
- **Date:** 2026-10-07.
- **Versions:** Halcyonic main 3cde3ac4: the client core (`apps/xr/Packages/com.halcyonic.client/Runtime`),
  the Unity layer (`apps/xr/Assets/Halcyonic`), the glance's Java (`apps/xr/Android/glance`) and the
  contracts (`packages/contracts`).
- **Method:** Three read-only sweeps, one an area: agent text; the control plane's and runtimes' text,
  names and the demonstration; Salidium's and Seorak's data. Each traced every place an outside string
  is put into a visible string, to the label that draws it. The paths flagged below were then read
  again at their lines; the crashes were confirmed against the contract (a field that allows "" or
  only white space) and `PageLine`'s check. Nothing was changed and nothing ran on a headset.
- **Status:** Verified by reading the code. The gaps are proposals until the coordinator settles
  their fixes; each fix comes with a test that fails without it.

## How a line reaches the glass

- **One rule for every label.** Every TextMeshPro label is set through `GlazeText.SetLiteral`, or the
  menu's own wrap, which applies the same `LabelText.ForTextMeshPro` (`MenuFrameView.cs`, `Wrap`):
  one line, no markup, backslashes doubled, every control, format, default ignorable, noncharacter,
  braille blank and Private Use character shown by its code point (‹U+202E›). Halcyonic's own words
  go through it too. No Unity `TextMesh` is used. The only text that bypasses it is the system
  keyboard's prompt and prefill (below) and the icon glyphs. The glance draws through `GlanceText`,
  held to `LabelText`'s ranges by `tooling/glance/glance.test.ts`.
- **Length.** A line has rows; past them it ends in an ellipsis (`GlazeText.Lay`), or it shows in
  parts, a row to the next. A side panel shows a cut answer whole.
- **The data flags show nothing.** `wordsAreData`, `subjectIsData`, `factIsData`, `sourceIsData` and
  `TitleIsData` only let the render checks allow an ellipsis on that line (`MenuFrameView.MayCut`,
  `PanelFrame`); a fact marked as data also goes through `LabelText.Name` and keeps to 40% of its row.
  A reader tells someone else's words only by what is drawn: curly quotes added by the words' builder,
  a chip ("Agent says", "Model explains", "Inferred"), leaning letters (`Claim`, which also adds quotes
  in the menu, `MenuFrameView.Quoted`), a provenance line ("From Salidium 0.6.0, 2 minutes ago"), or
  a lead in words ("It says:", "The companion says:").

## Agent and runtime text

| Path | What it shows | Rule and limit | Can the reader tell? | Flag |
| --- | --- | --- | --- | --- |
| The task's title as the file's subject (`FileScreens.cs`), the Tasks row (`TasksColumn.cs`), the stage plate (`CharacterLabelView.cs`), the glance row | The title alone | Plain; 2 rows (subject), 1 (row), 2 (plate); the glance cuts at 80 | No quotes; it stands where Halcyonic's subjects stand ("Nothing is waiting for you.") | A title can read as Halcyonic's sentence (gap 9) |
| The banner's "Still open: {title}" (`AmbientText.cs`) | Halcyonic's lead, the title after it | Cut to 32 characters before `Plain` runs, so code points can lengthen it; 2 lines | No quotes | Gap 9 |
| The system keyboard's prompt, "What to tell it: {title}" (`FileColumn.cs`) | Raw title | None: the system keyboard draws it, not `LabelText` | No quotes | Gap 9 |
| The agent's question (`FileScreens.Waiting.cs`) | “{text}” over its answers, or in parts | Plain; 2 rows, or parts | Quoted; no lean, no chip | Unlike an agent message in Activity (gap 4) |
| An answer row: label · description | The label, then its description | Plain; at most 2 rows, the rest in a side panel | No quotes, no chip, the same choice row as Halcyonic's "Type my answer", "More answers, 2 of 3", "Next question, 2 of 2", "Your answers" | A label can look like Halcyonic's own row (gap 3) |
| A question's header as the side panel's subject, on Your answers ("{header}: {labels}") and in the confirmation | The header, the labels after it | Plain; parts or 2 rows | No quotes | A header "Not answered" or "Your answers" copies Halcyonic's words (gap 3) |
| An approval: "It wants to use {tool}:" in amber, the request under it (`WorkspaceText.cs`, `FileScreens.Waiting.cs`) | Halcyonic's lead with the tool's name in it; the request on its own lines | Plain; the lead 1 row, the request up to 3, the confirmation in parts | The tool's name sits unquoted in Halcyonic's sentence; the request is unquoted; the whole lead line is marked as data, so Halcyonic's words may be cut | Gap 6, gap 12 |
| The peek: "It wants to run: {summary}", "It wants to use {tool}: {summary}", "Asks you: {header}: {text}", "Checks: {summary}"; a failed check's detail is the summary alone (`CharacterPresentation.cs`, `CharacterLabel.cs`) | Halcyonic's lead, then the outside words, then "(+N more)" | Plain; 2 lines | No quotes, no chip; Halcyonic's "(+N more)" comes last and the ellipsis can cut it | Gap 6, gap 12 |
| Activity: an agent message (`ActivityLog.cs`) | “{text}”, "Agent says", leaning, its time | Plain; 1 row | Quoted twice: ““…”” | Gap 4 |
| Activity: a tool ("{tool}: {title}", "{tool} succeeded"), a test run ("Tests started: {label}", "Tests passed: {summary}"), an approval ("Approval requested to use {tool}: {summary}"), a start ("Asked {app} to start", "Started on {app}") | Halcyonic's words with the runtime's in them | Plain; 1 row; also "Latest: …" (2 rows) and the peek | No quotes, no chip: shown as observed fact; a tool named "Approved" makes a line like Halcyonic's own entry | Gap 6 |
| What was sent last (`WorkspacePresentation.cs`), when its refusal's code is `demonstration` | The refusal's message, whole | Plain; 1 row | No quotes | A live control plane's message would show; the contract says only the demonstration answers with that code, and nothing checks it (gap 5) |
| Instructions and answers the person gave, as sent or heard | “{words}” in parts; "Your answer: “…”"; "Your computer heard: “…”" | Plain; parts, 2 rows | Quoted | None |
| The glance's notification | Counts only, never titles | Halcyonic's words | n/a | None |

## The control plane's text, names and the demonstration

| Path | What it shows | Rule and limit | Can the reader tell? | Flag |
| --- | --- | --- | --- | --- |
| A runtime's display name in New project's How it runs and the recap, "{name} (simulated)" in the demonstration | The name | Plain; 1 row; a blank name throws | Not quoted | The ellipsis can cut "(simulated)" (gap 12); a blank name crashes (gap 1) |
| A model's display name, and "Model: {name}" on the review | The name | Plain; 1 row; a blank name throws | Not quoted; a model named like a fallback ("chosen by the agent app", "none") reads as one | Gap 1, gap 13 |
| The companion's reply and question (`CompanionText.Says`) | "The companion says: “{line}”" | Plain; 3 rows, the contract keeps it to 300 characters | Quoted and tagged, then the menu quotes the whole line again: “The companion says: “…”” | Gap 4 |
| The companion's choices (`NewProjectScreens.cs`) | Answer rows beside Halcyonic's "Go on without it" and "Type my answer" | Plain; 2 rows, 48 characters | No quotes, no chip; only the page's source line says they are the companion's | Gap 3 |
| The companion's suggested name and first task | Rows with the fact "Suggested" | Plain; 1 to 8 rows, the contract's limits | Tagged by the fact | None, but the system keyboard prefills them raw (gap 9) |
| What speech heard for the idea, as the frame's subject | The words | Plain; 2 rows | Not quoted; a separate line says it was heard | Low (gap 9) |
| Project names (Projects, the subject "New task in {name}") | The name | `LabelText.Name`; 1 to 2 rows | Not quoted in the subject | Gap 9 |
| Connect's sentences: "The folder “{name}” itself. Connecting makes a project called “{project}” … Nothing in it changes until you add a task." (`ConnectText.cs`) | Quoted names inside Halcyonic's sentence | Plain; 3 rows; names not cut; the line not marked as data | Quoted | A long name pushes Halcyonic's safety clause past the ellipsis (gap 10) |
| New project's folder list: "{folder}", "Directly in {root}", "New folder in {root}", facts "In {root}", "Files go straight into {root}"; the recap's "{folder} in {root} from now on" (`ProjectFolder.cs`, `EntryText.cs`) | Names inside Halcyonic's clauses | Plain, not `Name`; 1 row; the fact not marked as data | Not quoted | A name of only white space crashes (gap 1); a folder "New folder in Projects" or a root "a place your computer no longer lists" passes for Halcyonic's option (gap 11); "from now on" can be cut (gap 12) |
| Usage left: "{label}, 5-hour window" (`UsageLeftPresentation.cs`) | Seorak's label | Plain, then cut at 32, which can split a ‹U+…› | Source line "From Seorak, as the provider reported" | The cut (gap 13); the agent the label belongs to is not shown |
| The paired address, Settings and the banner | The address the person typed | Plain; 2 to 3 rows | Not quoted | None: the person's own words, checked when typed |
| The recorded demonstration's presets, answers and companion | As the live paths show them | As the live paths | Source lines say recorded; a recorded understanding or evaluation loads only if marked synthetic | Only gap 5 |

## Salidium's and Seorak's data

| Path | What it shows | Rule and limit | Can the reader tell? | Flag |
| --- | --- | --- | --- | --- |
| Provenance: "From Salidium {version}, 2 minutes ago", "From Seorak, read 1 minute ago", "Simulated explanation · …" | The source and its version | Version cut at 24 after `Plain`, which can split a ‹U+…› | It is the provenance line | The cut (gap 13); the Checks side panel shows Salidium's line over Seorak's measurement (gap 8) |
| Statements and reasons ("Agent says: “…”", lead + “…” + chip, `WorkAnswers.QuoteLine`) | The agent's or subagent's words | Plain; 2 rows | The chip comes from the statement's epistemic, not its author: an agent's words tagged observed show as a bare quote with no "Agent says" | Agent text shown as observed (gap 2); quoted twice in the file column (gap 4) |
| Remaining work: "Still to do, the agent says: “…”", "Failing: …", "In progress: …", "To do: …" | The item's words | Plain; 1 row | Only the reported status is attributed; an agent's item with another status is unquoted | Gap 2 |
| `changes.summary` when no file changed; `verification.summary` when no run did; Seorak's `empty_reason` | The source's sentence, alone | Plain; 1 row; blank text throws | No quotes, no chip, and often the page's only line: "All checks passed" reads as Halcyonic's verdict | Gap 7; a blank one crashes (gap 1) |
| A check run: "Tests passed at 14:02: {label}" in the good tone; Seorak's "{label}: 23 passed of 24 runs (96%)" | Halcyonic's outcome words, the source's label after | Plain; 1 row | The label is unquoted; a reported run gets "Agent says" on a green, unquoted line; scope and exit observation are dropped | Gap 7 |
| The review line "{summary}: {label}; {label}" | Salidium's summary and two group labels | Plain; 2 rows | Classed by the first item of the first group only; with no items it counts as Halcyonic's own words, not marked as data | Gap 7 |
| The explanation's summaries and steps ("Now: ", "Because: ", "1. ") | The model's words with Halcyonic's leads | Plain; 1 to 3 rows | "Model explains", leaning; the menu's quotes wrap Halcyonic's leads too | Gap 4; a blank summary crashes (gap 1) |
| Files and revisions: "Edited: {path} (+38 −9)", "From commit {sha} to {sha} on {branch}" | Paths, branches, short hashes | Plain; 1 row; the whole repository path (up to 4096) comes before the counts; branches not cut; the short hash cut after `Plain` | Observed facts | The file name and counts get cut (gap 13) |
| The measurement's parts in the file column (Checks, Cost, Outcome) | Their lines | Plain; 1 row | The part's name is only a tag the old panel draws; the file column shows none | Low (gap 14) |
| Seorak's measurement when Salidium's brief has no lines or hasn't answered | Nothing | n/a | n/a | It can't be reached from the file column (gap 8) |

## Gaps, most serious first

1. **Blank outside text crashes a page.** `PageLine` throws on blank words, and these reach it blank:
   Salidium's `changes.summary`, `verification.summary`, explanation summaries and lane titles, Seorak's
   `empty_reason` (the contracts' text fields have a maximum and no minimum), a folder or root name of
   only white space in New project (`DisplayName` is 1 to 255 characters, and `Plain` makes spaces
   nothing), and a blank runtime or model name. The file or New project then can't be drawn.
2. **Agent words can be shown as observed.** `WorkAnswers.QuoteLine` chooses the chip by epistemic;
   an agent's or subagent's statement tagged observed loses "Agent says", and an agent's remaining item
   loses its quotes. AGENTS.md: agent text is reported, never observed.
3. **Outside labels look like Halcyonic's own rows.** Agent answers and the companion's choices are
   unquoted choice rows beside Halcyonic's "Type my answer", "Go on without it", "More answers",
   "Next question", "Your answers"; a question's header stands as a subject and as a name before the
   labels.
4. **Claim lines are quoted twice, and Halcyonic's words end up inside the quotes.** The menu adds
   quotes to every claim line, and their builders already quote them: Activity's agent messages show
   ““…””, the companion's lines “The companion says: “…””, and the explanation's leads and "Still to
   do:" read as the model's or the agent's words. The agent's question, by contrast, is quoted but
   neither leans nor carries a chip.
5. **A refusal's message shows whenever its code is `demonstration`,** live session or not.
6. **Runtime and tool text sits unquoted in Halcyonic's sentences:** Activity's tool, test, approval
   and start lines, "Latest: …", the peek, and Waiting's "It wants to use {tool}:". A failed check's
   peek shows the runtime's summary alone as if it were Halcyonic's reason.
7. **A source's summaries and labels read as Halcyonic's verdict:** `verification.summary`,
   `changes.summary` and `empty_reason` alone on a page; a run's label after Halcyonic's "Tests passed";
   the review line counted as Halcyonic's own words.
8. **The Checks side panel carries Salidium's provenance over Seorak's measurement,** and Seorak's
   measurement can't be reached when Salidium's brief has nothing to open.
9. **A title or name stands where Halcyonic's words stand:** the task's subject, "Still open: {title}"
   (cut before `Plain`), the system keyboard's "What to tell it: {title}" and its prefills (not through
   `LabelText`), "New task in {name}", and the heard idea as a subject.
10. **Long names push Halcyonic's words past the ellipsis** in Connect's sentences.
11. **A folder or root name can pose as New project's own option** ("New folder in Projects", "a place
    your computer no longer lists"); the list has no look-alike mark, and its facts aren't marked as data.
12. **Halcyonic's words on a data line can be cut:** "(+N more)", "(simulated)", "from now on", and
    lines of Halcyonic's own marked as data (What was sent last, Waiting's lead, Activity's notes).
13. **Cuts that hide what matters or split a code point:** a repository path before its counts, a
    branch name, and `Truncate` or `Substring` after `Plain` (short hash, version, usage label).
14. **Low:** the measurement's part names in the file column; the usage reading's agent; a ” inside the
    companion's line; "Simulated explanation" over What changed and Why; one file's `by` applied to all;
    a group's heading dropped on Changes; the review's plain values; the editor-only `WorkspaceScreens`.

Each gap's fix lands in a batch for independent review, with its words settled by the coordinator and
a test that fails without it; this record says which have landed.

**Landed (batch A, 2026-10-08):**
- **Gap 1:** a source's blank sentence is left out (`SectionPresentation`); a blank folder, place or agent app name shows by code point (`LabelText.Name`).
- **Gap 2:** an agent's or subagent's statement and remaining item are always reported, quoted and attributed.
- **Gap 4:** a claim is quoted once, by its builder, around the outside words alone (`PageLine.Drawn`); the agent's question leans.
- **Gap 5:** a refusal coded `demonstration` is said in its own words only while the demonstration shows (`CommandSubmissions.Demonstration`).
- **Gap 8:** each page of an answer carries its own source's provenance, and the measurement opens even when the understanding source had no answer.

**Waiting:** batch B (gaps 3, 6, 7, 9, 11) waits for lane V's treatment of outside words beside Halcyonic's own. Batch C (gaps 10, 12, 13, 14) follows A.

## Not verified

- Nothing ran on a headset. How TextMeshPro's font draws a glyph it lacks (one box for many) is
  assumed, and is named in OPEN_QUESTIONS.md's look-alike row.
- The crashes were confirmed by reading `PageLine` and the contracts, not by a failing test; each fix
  adds one.
