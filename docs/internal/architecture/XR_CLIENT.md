# XR client

How the Unity client is structured, what is verified, and what is not built yet. Decision and
alternatives: [ADR 0008](../decisions/0008-engine-independent-csharp-client-core.md).

## Layers

```text
Unity layer (apps/xr/Assets)          stage, characters, focus guard;                compiles and builds;
        │                             workspace: gaze and hand peek, panel,           the workspace, the
        │                             question tabs, project rail, entry panel,      rail, entry panel,
        │                             transition, first-time hint;                    room, the sound and
        │                             Meta's rig; room: passthrough, MRUK, stage      pairing are not
        │                             anchor; sound: the characters' voices;          verified on a
        │                             pairing: panel, development builds              headset
        ▼
Client core (com.halcyonic.client)    RealtimeSession, ClientProjection,             built, .NET tested
        │                             CharacterPresenter, CharacterCues,
        │                             CharacterIdentity, CharacterLineup,
        │                             WorkspacePresenter, WorkspaceText, LabelText,
        │                             WorkspaceSteering, CommandSubmissions,
        │                             NewWorkDraft, NewWorkReview,
        │                             NewWorkSubmission, BuildSequence,
        │                             StageVisibility, WorkOverview, ProjectIdea,
        │                             AttentionWatch, EntryText,
        │                             PeekChoice, WorkspacePlacement, SeatedPointing,
        │                             InFrontPlacement,
        │                             ActivityLog, EventHistory, CommandFactory,
        │                             IntelligenceFeed, UnderstandingPresenter,
        │                             EvaluationPresenter, IntelligenceText,
        │                             DemonstrationRecording, DemonstrationPlayer,
        │                             DemonstrationTransport, DemonstrationReads,
        │                             DemonstrationFallback,
        │                             StageSurfaces, PlacementMemory, RoomStatus,
        │                             GlazeSynthesizer, SoundCueSelector,
        │                             PairingClient, Srp6a, PairingCrypto,
        │                             PinnedConnection, PinnedWebSocketTransport,
        │                             PinnedHttpHandler, ControlPlaneTarget,
        │                             FilePairingStore
        ▼
Contracts (com.halcyonic.contracts)   C# bindings generated from packages/contracts    generated
        │
        ▼
Newtonsoft.Json (com.unity.nuget.newtonsoft-json 3.2.2, Newtonsoft.Json 13.0.2)
```

| Package | Location | Contents |
| --- | --- | --- |
| `com.halcyonic.contracts` | `packages/contracts/csharp` | `HalcyonicContracts.g.cs`, written by `pnpm contracts:emit`; never edited by hand |
| `com.halcyonic.client` | `apps/xr/Packages/com.halcyonic.client` | The client core, an embedded package of the Unity project |

Both assemblies set `noEngineReferences`, so neither can use `UnityEngine`. The Unity layer is the
only code that may. Like every client, the XR client talks only to the control plane, never to a
runtime, Salidium or Seorak.

## Contracts in C#

`packages/contracts/src/csharp.ts` generates the bindings from the same TypeBox definitions, and
the same definition names, as the JSON Schema document:

- Objects become sealed classes. Every property is `[JsonProperty(Required = ...)]`: `Always` for
  non-null values, `AllowNull` for `Nullable(...)`, so a missing key is always an error.
- String literal unions become enums whose `[EnumMember]` values are the wire strings.
- Discriminated unions (events, commands, realtime messages, sources, provenance, attention
  reasons, command results, approval subjects, understanding and evaluation results) become an
  abstract base class with one sealed class per variant. A generated converter reads the
  discriminator, creates the variant and populates it. Properties every variant shares move to the
  base, so `EventEnvelope.WorkstreamId` works without a cast. Variants of two unions may have the
  same shape, as the failures of understanding and evaluation do; each is still its own class, such
  as `NotFoundUnderstanding` and `NotFoundEvaluation`.
- Timestamps stay strings (`DateParseHandling.None`), `HalcyonicJson.FormatTimestamp` writes the
  canonical form, and type metadata such as `$type` is ignored.
- `HalcyonicJson.Tolerant` ignores unknown properties, for an older client reading a newer server;
  `HalcyonicJson.Strict` rejects them, for tests. Unknown enum values and discriminators are
  errors in both, because they change meaning; new values need a protocol version.

## Client core

- **`RealtimeSession`** runs the realtime protocol ([REALTIME.md](REALTIME.md)) in the background:
  hello with the resume cursor, snapshot or resume, events, command acknowledgements, and
  reconnection with capped exponential backoff and jitter. It pings every 10 seconds and abandons a
  connection that delivers nothing for 30 seconds, because a silently broken network is otherwise
  noticed only by TCP. With the access token (a development build, as over USB), every request
  that carries it, the realtime upgrade and each REST call, first asks the control plane to prove
  it holds the same token, for the literal loopback address and port that was dialled
  (`LoopbackProof`, `LoopbackProofHandler`; SECURITY.md); what can't prove it gets no token, and
  no redirect is followed. `LoopbackProofHandler` speaks HTTP/1.1 itself (`Http1`, as the pinned
  handler does): each request opens a connection, proves the control plane on it and sends the
  request on it, closing it after. An endpoint that is not `ws://` or `http://` at 127.0.0.1 or
  [::1], such as `localhost` or `wss://`, ends the session; a 101 must carry `Connection: Upgrade`.
  What answers without the proof stops the session with `AccessRefused`
  and "This headset's access code doesn't match your computer's, or something else is answering
  in its place, so the headset didn't send it. Put your computer's current access code on the
  headset, check that Halcyonic is running there, and restart the app." A control plane that
  answers the connection with 401 has refused the credential, so the session stops trying and its
  status says `AccessRefused`, with what to do in `ConnectionText`'s words: for the access token
  "Your computer refused this headset's access code: it doesn't match your computer's. Put your
  computer's current access code on the headset, then restart the app."; for a pairing, that the
  Mac no longer accepts it and to forget the Mac and pair again. Both transports perform the
  upgrade themselves, so they read the 401 from the upgrade's own answer. A Mac that does not answer reads "Can't reach your computer; trying again", with the technical
  reason after it. In the fifth headset session a stale token read as "Unable to connect to the
  remote server" ([quest-3-device.md](../validation/quest-3-device.md)).
- **Threading.** Received messages wait in a queue. `Pump()` applies them to `State` on the
  calling thread and returns what changed, so the Unity main thread calls it once per frame and no
  state is shared across threads. A consumer that falls more than 10,000 messages behind is
  resynchronized from a fresh snapshot instead of growing the queue without bound.
- **`ClientProjection`** holds projects, workstreams, executions, recent commands and runtimes. It
  changes only by replacing state from a snapshot or from the entity changes an event carries; it
  never derives state.
- **Commands.** `SubmitAsync` sends a command and waits for its acknowledgement. It fails
  immediately when not connected, and reports `CommandOutcomeUnknownException` when the connection
  drops or the acknowledgement does not arrive in time; resubmitting the same command, with the
  same id, then reports its real state. A command's outcome arrives later as events in `State`.
- **`CharacterPresenter`** maps a workstream to what its character conveys: an activity, the
  state's word (never color alone), the attention level with one explanation per reason, the same
  reasons without what the state already says (`AttentionDetails`, for the peek: "The model
  provider rejected the request." under Couldn't finish), and flags for simulated work, recorded
  fixture data, and a stale state while the session is not live. Reasons read "It wants to use
  {tool}: {summary}", "Couldn't finish: {reason}", "Can't tell what it's doing: {reason}" and
  "Checks: {summary}"; a question's is `AsksYou`.
- **`StateLanguage`** ([ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md))
  is the one mapping from a task's state to what every surface says and shows: Not started,
  Starting, Working, Checking its work, Waiting for you, Finished this round, Checks failed,
  Couldn't finish, Stopped and Can't tell yet, each with a tone, an icon, a fill and an edge (no
  two states share all of them, so none is told by color alone) and a motion: Starting and
  Working turn, Waiting for you breathes and counts what waits when more than one does. Something
  waiting for the person wins over what the work is doing, and a finished round with a notice is
  one whose checks failed. A finished round reads "Finished this round", because completion says
  nothing about correctness. Practice, Demo and Recorded are marks beside the state, never in its
  word, and a state that is only the last one known keeps its word, is ghosted and stands still.
- **`Glaze`** holds the interface's tokens, once: colors as roles (amber only for waiting for you,
  red only for what went wrong, the cobalt accent only for what can be acted on), sizes as angles at
  the eye (one of Meta's dp is 0.0625 degrees; body text 1.125, nothing under the caption's 0.94),
  targets, radii, plate opacity and motion. Its tests hold every color to its contrast and the sizes
  to Meta's minimums.
- **`CharacterLabel`** is what a character's label shows, three parts kept apart: the title, the
  state badge and the marks. **`PeekCard`** is what the peek shows: the badge and marks, the first
  reason with more to say than the state, with how many more wait ("(+1 more)"), else what it did
  last; "Last known:" before it while the session is not live; what the marks mean ("Practice run:
  nothing is built."); and what opening it is for ("Open it to answer.", "Open it to see why.").
- **`CharacterCues`** turns a presentation into what the character shows: its eyes, its motion, its
  halo, its surface (flowing, cracked or fogged), whether it faces the person, and whether it is
  paused or ghosted ([ADR 0013](../decisions/0013-characters-are-bots-with-a-living-surface.md)).
  No two activities differ only in color, and the last known state keeps its cues but stops moving.
- **`CharacterIdentity`** derives a character's body shape, hue, tone and motion phase from its
  workstream id alone (FNV-1a over the UTF-8 id, then MurmurHash3's finalizer), so a workstream
  looks the same in every session and version. The eight hues keep at least 25 degrees from the
  state colors: amber for waiting for you, red for failed, green for a finished round.
- **`CharacterLineup`** chooses which workstreams have a character and where each stands: needs
  you first, then failed, unknown or failing tests, then active work, then the most recently
  changed. Characters that need attention stand nearest the middle of the person's view; every
  other character keeps its slot for as long as it stays shown. A newcomer takes the free slot
  nearest the middle, or the slot of the one it replaces, and a character that comes to need
  attention trades places with the one nearest the middle that does not. A waiting workstream
  replaces a shown one only from a more important tier, or, at rest, when it changed more
  recently, because working ones change every few seconds and would otherwise swap in and out.
  The person can ask for one the lineup did not choose (`Request`, from More tasks): it takes the
  place of the character that ranks last and keeps a slot until another is asked for or it leaves;
  the one it replaced waits like any other. `Compare` orders workstreams as the lineup ranks them.
  Given the device's clock, the lineup also keeps new work in view: a workstream that appears after
  the journal's first update, as work the person just started, and one the person just opened
  (`Keep`, from the workspace), hold a slot for five minutes whatever their rank, taking the place of
  the lowest ranked character that is neither asked for nor kept; at most all slots but one are
  kept, newest first, so the work that ranks first keeps a place. Another journal's work is never
  new (`UseJournal`), and only the device's clock is used. More tasks stays exact, since it lists
  whatever has no slot. In the fifth headset session, older work flagged for attention pushed
  just-started work off the stage.
- **`WorkspacePresenter`** is the expanded form of the same workstream, for milestone 3: the
  character's cues plus the objective, the execution and its runtime, the actions the control plane
  would admit now (from declared capabilities and status; nothing while not live or when the
  runtime is gone), which of those actions need a deliberate confirmation (from the command
  policies in `welcome`; unknown counts as needed), feedback on recent commands in words, and the
  activity. Approving or denying answers the oldest pending approval.
- **`WorkspaceText`** writes every word the workspace shows, so the Unity layer only
  lays them out: the person's questions, whole as headings (What is it doing?, Help me understand,
  What was checked?, and What do you need from me? only while an approval or a question waits) and
  short on their tabs (Waiting for you, Doing, Understand, Checked), the goal, one plain answer
  (what needs the person, else "Nothing is waiting for you." and the latest activity), the answer to
  What do you need from me? (`NeedAnswer`: what it wants, the oldest request as the runtime reported
  it and what each answer does), the run's details (`RunDetails`, for Doing's details), what needs
  the person, activity lines with the local time and the agent's words
  quoted as "It says: “…”", action labels, a confirmation question that names exactly what would be
  sent (for approving or denying, "Approve the request above?" over the whole request, `Request`:
  the tool and what it would do, never shortened), and why no action is offered. Text
  from outside in any of them shows by `LabelText`'s rule, and a cut never splits a character in
  two. `Describe` writes one activity entry in one line, for the peek too.
- **`LabelText`** is the one rule for showing text Halcyonic did not write: workstream titles and
  objectives, anything an agent or a tool wrote (messages, approval requests, activity), refusals
  and failures, setup problems with exception text, what the understanding and evaluation sources
  say, and names from runtimes. `Plain` makes it one line of exactly what it says: a line break, a
  tab or any other white space than the space collapses with the whitespace around it into one
  space, and spaces stay as written; every control and format character, every default ignorable
  code point (a zero width space, a bidirectional override, a variation selector, a tag character)
  and every half of a surrogate pair shows as its code point, as ‹U+202E›; so does every character
  of the Private Use Areas (U+E000 to U+F8FF, planes 15 and 16), as ‹U+E769›, since Halcyonic's
  own icons are drawn from them and text from outside must never draw one among its words;
  everything else, markup and backslashes included, shows as it is. Which characters show by code
  is a fixed table, Unicode 17.0's, so the headset and the tests decide alike whatever Unicode
  version their runtime knows. `ForTextMeshPro` also doubles every backslash, for a TextMeshPro label with rich text off
  and escape parsing on, the only way such a label shows a backslash sequence as written
  ([workspace-interaction.md](../validation/workspace-interaction.md)). Characters that merely look
  alike, a Cyrillic letter for a Latin one or a no-break space for a space, show as they look.
- **`CommandSubmissions`** keeps this client's own view of each command it sent (sending, not
  sent, outcome unknown, acknowledged) until the control plane's record of it arrives in the
  projection. From then on only that record speaks, so a result reads as done only after the
  runtime confirmed it. `WorkspacePresenter.Present` merges the two, newest first.
- **`QuestionDraft`** holds the person's answers to one question an agent asked
  ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)), keeping to the rules the
  control plane admits answers by, so a send is never refused for its shape: every prompt answered
  once, labels among those offered, at most one unless the prompt takes several, typed text only
  where allowed, and for a prompt that takes one answer a label or text, never both (choosing one
  drops the other). Typed text that is not whole characters (a lone surrogate) is refused in words
  rather than sent as an invalid command. Each prompt must also have been shown whole
  (`ShownWhole`) before a send, as an approval's request must be read; focus going to another
  window keeps every choice. `WorkspaceSteering.SendAnswer` is the only way an answer is sent: on
  the deliberate Send answer press, for the question the workspace shows now, once the draft has no
  problem, after a second press only should the policy ask for one (it is low consequence today).
  Its feedback reads "Sent, waiting for the result…" until the runtime confirms ("The runtime took
  the answer"); a question that went away first reads "Refused: the agent no longer waits for this
  answer.", and an answer the runtime never confirmed (Codex's `question_unconfirmed` and
  `answer_ambiguous`) reads "Not confirmed: the agent may or may not have your answer. Check What
  is it doing?", never as sent. While this client's answer to the question shown may still take
  effect (being sent, accepted, or with an unknown outcome), Send answer is not offered, so a second
  press cannot race the first (`CommandSubmissions.AnswerPending`); it comes back after a refusal, a
  failure with no effect, or an answer that never left the headset. The panel follows
  `ExecutionView.pending_questions`, not resolution events: a question that leaves the list is gone,
  whether or not one was reported.
- **`WorkspaceSteering`** turns presses in an open workspace into commands. It takes only offered
  actions. One the control plane's policy marks for review waits for a second, deliberate press on
  a separate button whose question names what will be sent; the confirmation lapses after 15
  seconds, when the action is no longer offered, or when its approval is no longer pending, and
  says so. An approval is confirmed only once the whole request it answers has been shown: the
  workspace reports which part of it shows (`RequestShown`), each part turned to starts the 15
  seconds again, and until the last part has shown the question says to read the whole request
  first (`CanConfirm` is false) and a confirmation sends nothing and stays armed. Denying needs no
  reading, since refusing what one has not read in full can do no harm. Instruct asks for text
  first, and an empty text sends nothing. A typed instruction is sent as the keyboard closes, unless
  the policy asks for review; a spoken one (`Spoken`) is always held for the confirmation, which
  reads "Your computer heard: ... Send it?" with the text, so a mishearing is never sent unread.
- **`SpeechClip`** makes a held clip of speech into what `POST /api/transcriptions` takes (ADR
  0021): the microphone's samples, at its own rate and channels, mixed to mono and resampled to
  16 kHz (the mean of the input each output sample spans going down, a straight line going up),
  as 16-bit PCM in a WAV, cut at 30 seconds, and no clip at all under half a second.
  `ControlPlaneApi.TranscribeAsync` posts it and returns `HeardTranscription`, whose text is
  untrusted and shown through `LabelText`, or `NothingHeardTranscription`; a refusal throws
  `ControlPlaneRequestException` with the control plane's code, which **`VoiceText`** puts in
  words that offer typing or trying again.
- **`PeekChoice`** decides, frame by frame, which one character shows its peek, how visible it is,
  and what a look and pinch opens. A hand pointing at a character, or a finger about to poke it,
  peeks at once. The gaze peeks only after resting 0.4 s on one character within 10 degrees
  of where the head faces, and starts over while the head turns faster than 20 degrees a second,
  so turning the head across the stage brings up nothing. A peek fades in over 0.25 s and out over
  0.15 s, only one shows at a time, and a glance aside keeps it 0.45 s. The open character is never
  peeked, and while a workspace is open only hands peek. A pinch of either hand opens a character
  only while its gaze peek is at least a quarter visible, no hand ray or finger is on any target, no
  workspace is open, and the app has focus.
- **`WorkspacePlacement`** chooses where the workspace, or any foreground panel of its own size,
  opens, seen from the eyes: toward its character, at most 15 degrees to the side of where the
  person looks, and clear of every character it passes, below their labels or above their bodies,
  whichever keeps its center between 31 degrees below and 2 degrees above eye level (the nearer to
  15 degrees down when both do); never so low that its lower edge comes within 5 cm of the surface
  the characters stand on. It keeps 1.5 degrees from bodies, which move, measured at the panel's
  corners, and 1.2 from labels, which do not, each where it stands: a flat panel's edge is farther
  away from its middle, so below eye level it looks higher there (`CornerElevation`), and each label
  is cleared by the edge under its outer side, or under the corner where it reaches past the panel
  (ADR 0026, 2026-10-02; on the far arc the outer labels stand over the corners, so a wide plane
  stands where it did, and a deep label over the middle no longer lowers it). Where neither side clears, it
  moves the least into the band and may cover a character. The band's floor is for a panel 26
  degrees tall: a taller one, grown whole for the person's larger text or holding more, as Settings
  does, would not fit under the deepest labels above it, so its center may go lower by as much as it
  is taller (`WorkspacePlacement.Lowest`), half of that for its own upper half and the rest for
  titles grown with the text; a panel moved by hand stops at the same floor. To be judged on the
  headset. Once the headset's field of view is measured (`ViewField.Current`), the side that keeps
  every corner 1.5 degrees inside it with the head level wins when both sides clear, and where
  neither clears it moves into the band no lower than that (`Lowest`, which a dragged panel's limit
  uses too); the field never pushes a panel into a label, since below the labels is already as
  high as it can go. Placement may put a panel taller than designed lower than the field holds it
  with the head level, by half again as much as it is taller and at most 8 degrees
  (`WorkspacePlacement.FloorPitch`); a panel as tall as designed keeps the head-level floor. Where a
  panel stands, the person is taken to tip their head down as much as its bottom needs to come into
  the field, never more than 8 degrees (`WorkspacePlacement.ReadingPitch(size, elevation, field)`,
  ADR 0026, 2026-10-02, in place of half again as much as it is taller, under which some heights fit
  neither level nor tipped), and the renders' field checks hold it seen so. Settings with all its
  sections, about 33 degrees tall, would need more, and is not held to the field. Holding Move never lifts a panel for
  want of room: one that opened under the labels lower than the field allows stays where it was
  and goes no lower (`PanelDrag`).
- **The menu's models** (`MenuFrame.cs`, ADR 0026), engine-free, what to show and never where:
  `MenuBar`, the four places with their amber dots and the closed bar's line; `MenuFrame`, a
  column's subject (with a file's state pill), its row of `FrameSection`s (chosen, reached, waits),
  its page of `PageLine`s (an icon, words, a small fact, a tone, an evidence chip, the agent's claim,
  and what a press opens or raises: a row only takes the person somewhere), why a prompt can't be
  taken now as the page's last content line, one source line (`sourceIsData` where it is text from
  outside, as an answer's provenance), a `SidePanel` (facts, each a name over its value at 18 dp,
  or lines, nothing to press but its own Close details, `SidePanel.Footer`) that a chosen line
  opened or a chosen answer cut to fit slid out with all its words, and its `Footer`. A line's
  words with no `fromRow` show from their start, ending in an ellipsis past its rows; with one,
  they are a part of words shown in parts, exactly those rows of the words wrapped whole, so no word
  is lost between parts. `MenuFrame.RowsAPage(text, sourceLine)` says how many rows a list's page
  holds: 4, or 3 with larger text, a source line counting as one. A footer's `Prompt`s stand in slots, Close,
  Rare, Free, Secondary and the far right, and it refuses what would stand anywhere else: one
  prompt a slot, the one main action only at the far right and drawn with the accent, paging never
  the main action (a long list pages by Next page alone, First page on its last page, at the far
  right where nothing is the main action, else as the secondary prompt), Hold to talk only as the
  secondary prompt, and a confirmation's Yes only in the free middle, which held nothing on that page
  or since, with Cancel in the place of the press it would undo (`Footer.Confirm`); a request in
  parts pages by a row on the page. An unavailable prompt keeps its place, drawn quiet, and an
  action says why. What limits a footer is its prompts' width at 18 dp, not their count: four
  short ones fit in a 36 degree file, and Close, Hide from the stage and Add a task overrun the
  32 degree menu (the component render logs each footer's measure). Each kind of column keeps one
  width wherever it stands (`Glaze.Menu`): the menu 32 degrees, a file or New project's steps 36,
  a side panel 26, so a page's words wrap the same alone and beside the menu.
- **A page's measures** (`MenuPage`), engine-free, so a screen packs a page before the view lays
  it: a column's content width and the half two answers sharing a row each take, a line of words
  by `Glaze.Menu.LineSpacing` (the font's, which the component render holds it to), a row or an
  answer, the gaps, a subject in one row or two under its pill, and the content round a page.
  `MenuPage.Fits` says whether a composition stands inside a Quest 3S's field, its top
  `MenuPage.TopDegrees` (17.5) below eye level, where a designed 26 degree panel's top stands at
  the lowest a Quest 3S allows with the head level, and the head tipped by `ReadingPitch`, as the renders'
  field check sees it, and `MenuPage.Height` is the most a lone file's page holds so, by its
  subject's rows and the text size. `MenuColumns.Arrange` says which columns stand on the plane:
  never three; a side panel beside the frame in front; and the menu stepping aside, off the plane,
  while a file's side panel is open or while the menu and the file together would not fit (as
  Tasks' four rows under a file title in two rows), back once neither holds or the file closes. The tokens for these surfaces are `Glaze.Menu`'s: three sizes of type, one 8 dp grid,
  one radius, the glass, the selection treatment, which `GlazeChecks.OneSelectionTreatment` pins,
  and quiet words in the secondary colour, every word held to 4.5 to 1 on the glass over a white
  wall. `Surface.DrawGlass` draws the glass in one call of the one surface shader: the panel colour
  at 96 percent, a white hairline, a light from the top edge fading out by a third of the height,
  or within a content surface's top padding, and a sheen just inside the top edge; the component
  render samples it. A subject is `GlazeType.Subject`, 24 dp drawn light; only the chosen section and
  the main action are drawn heavier (`GlazeText.SetStrong`). A file's state pill is the stage's
  badge drawn `StateBadgeView.PillScale` larger, its word at 18 dp, on the subject's top edge, and
  `GlazeChecks.TypeStepsDown` reads it with its subject. `GlazeButton` takes the menu's three
  roles: `Prompt`, a round key cap holding its icon (a degree, the least any icon is drawn), then
  its words, with an unseen 60 dp hit area, the main action's cap filled with the accent, a plain
  one an outline, an unavailable one quiet in its place, the pointed frame round it, its cap sinking
  when pressed; `Row`, a page line's place to press, nothing at rest; and `Answer`, a hairline shape
  at rest; both lit when chosen and framed when pointed at, their words laid on them by the view
  (`ShowArea`). Each sets its shapes' `Surface.Selection`, so the selection check holds them.
  `MenuFrameView` draws a frame or a side panel as a column's parts, each a shape centred on its
  own transform for the plane to lay: the subject's glass with its title and pill, the row of
  sections, and the content's glass with its lines, reason, source line, and `FooterView`, which
  stands the footer's prompts in their slots and says whether they fit. Its static `RowsOf`,
  `FitsHalf` and `TitleRows` measure words as it lays them, before anything is built; it pairs two
  answers that each fit half a row in one row; and it keeps a small fact's or a chip's room at
  18 dp wherever it stands, drawing it at 15 dp where that reads as the eyes see it; a fact from
  outside takes at most 40 percent of its line. Once its words are laid as the plane has them it
  raises `Drawn`, which the director passes on to the screen that counts what was read. `MenuBarView`
  is the menu closed. The component render lays five compositions with them on one plane from the
  eyes at both sizes and holds each to one plane, type stepping down, one selection treatment,
  text as seen, its targets, nothing of Halcyonic's own cut, its footers fitting, the content's
  light ending above its first target (`GlazeChecks.GlowEndsAboveTargets`) and a Quest 3S's field
  (`GlazeChecks.InsideField`, which `FieldChecks` now calls), and checks that `MenuPage.Fits` agrees.
- **The menu's columns** (`IMenuColumn`, ADR 0026) are plain objects in the client core behind one
  contract, so each lane builds its own: Tasks, Usage, Settings and the closed bar (lane U), Projects
  (lane L), a task's file (lane W) and New project (lane C). A column gives its frame, with its side
  panel, and raises `Changed` and `Closed`; it takes every press (`Act`), each draw of the very
  frame it gave, its page or its side panel, as the plane has it (`Drawn`), which of its held
  prompts started and ended while the director's one voice records, the words heard (`Heard`,
  `Said`), a tick, and the app losing focus, presses and holds only while the app has focus. Its own rules decide whether a press may act, and only they send, through
  its host's `IMenuHost.Submit`, bound to the session it was made in; the director never sends around them. `IMenuHost` gives every
  column the session's projection, the clocks, the reading size, the computer's API, the keyboard,
  the view's measures, the page height on this stage read when the column opens, and the way to
  open a task's file or New project. A place's frame leaves its sections out; the director adds the
  menu's places (`MenuBar.Sections`, `MenuFrame.WithSections`) and handles choosing one.
  `MenuNavigator` (client core) is what the menu shows and where each press and draw goes: open on a
  place or closed to its bar, the column beside it, a press to the column that showed it (a side
  panel's to the frame in front), a draw only for the very frame a column gave, and nothing else.
  Each press carries the frame its view showed, or its side panel, and counts only if that is what
  was drawn last in its slot and still stands, so a press on a file swapped for another, or on a
  place left, reaches nothing; a view leaving the plane raises none. A column leaving the plane,
  swapped, closed or its place left, gets `FocusLeft`, so an armed confirmation lapses. A
  place's column is made when the menu first shows it after opening and let go when the menu or the
  column closes, so each opening starts afresh; what must outlive it, as Projects' memory of a
  Connect in flight, `MenuMemory` keeps for the app's run on one session and journal, across
  reconnects and never renewed then, since a socket's drop is when an outcome turns unknown; another
  session, another journal or a re-pairing starts afresh. The director makes Projects (lane L's `ProjectsColumn`) over it, and
  keeps New project's flow (lane C's `NewProjectFlow`, made by the host's `MakeNewProject` and opened
  by the director) in it once first opened (`NewProjectFor`), ticking it while it isn't beside the
  menu so a build confirmed in it goes on; another session, as the computer's live session taking the
  demonstration's place, or another journal, lets the flow go and takes it off the plane, and so does
  a kept flow that says it is for another session (`ForAnotherSession`), a second guard on the same
  rule.
  `MenuDirector` (Workspace) runs it on the stage over one `MenuPlane`. It sends nothing itself:
  every column it makes gets a `SessionBoundHost` (client core) over it, bound to the session shown
  then, which sends only to that very session through WorkspaceDirector's command submissions, so
  what is in flight shows as sent. Once another session shows, or that session moves to another
  journal, the bound host sends nothing, offers no API or voice and reads as away, so a column made
  in the demonstration never reaches a live control plane (ADR 0012); a reconnect keeps the same
  session, so sends go on. The director then also takes the column beside the menu off the plane and
  makes the menu's places afresh. It runs Hold to talk's one voice for any held
  prompt, and passes presses and holds only while the app has focus. A draw while focus is away or
  the plane is folded counts for nothing, and the plane draws again on return, so a column learns
  what was read only while the person is there. `MenuVoice` (client core) keeps
  the voice's words for the column that held: a hold while the voice still records or waits for the
  computer's answer starts nothing, a column learns its hold started only once the voice records,
  only that hold ends it, and the column leaving the plane, or focus leaving, drops what it records
  or awaits. Lane U's places are
  `TasksColumn` (every task, what waits first, its project where there are several, as many rows as
  fit beside a file on this stage), `UsageColumn` (each limit's share left in words, its side panel
  when it was seen, when it resets and whose account) and `SettingsColumn` (each setting under its
  group's heading, its one change the main action; the comfort settings from `ComfortSettings`, the
  rest from what owns them). `PageLine.besideNext` stands a line beside the next in half the row, as
  Type my answer beside a question's paging row, and `Prompt.pageExplains` keeps a prompt's reason
  undrawn where the page says it already.
- **`MenuPlane`** (Workspace) stands the menu, a task's file and a side panel on the stage as one
  composition: the columns by `MenuColumns`, the menu standing beside the file only where the two
  fit as the stage would place them in the headset's measured field (`MenuPage.Inside`), and a side
  panel taking its frame's place with text a step larger, or wherever the two don't fit, wearing
  the file's pill and keeping its light line. It places the composition as one panel of its size
  beside the file's character (`WorkspaceLayout.Place`), where the person looks
  (`PlaceForeground`), or beside a window straight ahead centred under the window's lane
  (`PlaceAhead`), and slides every part to its new place over 0.25 s, so the plane re-centres as
  one piece and the menu steps aside to the left and back (`Advance`, which the renders step
  themselves). The light line leaves from under the file's character's label, below any mark, and
  drops straight from the middle of their overlap to the subject's plate, else joins their nearer
  corners; over a desk, where the plane stands above the lineup, it rises from the top of the body
  to the file's bottom edge; beside a window there is none. Closed with no file open, the menu is
  its bar. `MenuPlane.TopLine` reads the plane's top line from where the stage would place it, alone
  or beside the menu, for `MenuPage.Height` to pack a page for the stage it shows on: Tasks beside a
  file holds 4 rows where they fit, 3 on a Quest 3S at the far stage. `WorkspaceRender` lays it on
  the far, desk and window stages in a Quest 3S's field at both text sizes, in four states each,
  halfway through the slide as well as at its end, and holds each to one plane, type, selection,
  text as seen, its targets, a degree from every label, body and the window by their outlines as
  the eyes see them (`GlazeChecks.OutlineApart`), the field, and the light line crossing nothing.
  `PanelModel` stays until nothing draws it.
- **`FileScreens`** builds a task's file as a `MenuFrame` (ADR 0026) from its presentation, the
  steering and what the file is in the middle of (`FileScreen`: the section chosen, the line whose
  side panel shows, the page of each, the instructions offered where no keyboard is, where the
  person is in the agent's question or in the request an approval answers, and the answers Changes
  and Checks read, brief and full, null while still being read). Its subject is the task's title
  under its state pill; its sections are Waiting, Activity, Changes and Checks, Waiting's amber dot
  on while something waits, and it opens on Waiting then, else on Activity. **Waiting** reads only
  the work's own state, never an answer still being read: an approval's request with Approve as
  the main action, Deny beside it and Stop beside Close, and while a decision sent on it may still
  take effect, Sent… in Approve's place and no Deny, so a second decision never races the first
  (`CommandSubmissions.ApprovalPending`, `WorkspacePresentation.ApprovalInFlight`); the agent's question with its answers as
  rows to choose, Send answer as the main action and Hold to talk beside it. A question with any
  secret prompt can't be answered here whatever its adapter says (`WorkspaceText.Answerable`, asked
  by every place that offers or sends an answer): it shows why and the way on, Stop; Sent… in its place, taking no press, while an answer sent may still take effect.
  The question shows a prompt at a time (`FileQuestion`). A question longer than two rows shows
  first on pages of its own, a part at a time, each ending in a row to the next and the last in "On
  to the answers"; its answers' pages are headed by its first row, cut, and a short question heads
  them whole. The answers keep the agent's order, none left out, each in at most two rows, paged by
  a row at the page's end ("More answers, 2 of 2"), with Type my answer last; for a question of
  several prompts, a row on to the next question and after the last to Your answers, which lists
  each prompt's answer whole, paged where it doesn't fit, and is the only page it sends from. Every
  prompt and every answer must be measured, or nothing is shown. Nothing counts as read until the
  view reports it drawn (`FileQuestion.Drawn`, `SideDrawn`): a question once every part has been,
  or the page heading its answers when it is short; a chosen cut answer, or a typed one longer than
  its row, once its side panel has shown all of it; and Your answers once every page has. A typed
  answer or a line of Your answers counts as cut and unread until the layout has measured it as it
  reads now (`FileQuestion.MeasureTyped`, `MeasureReview`), never as one row by default. A choice
  is taken only from the page in view; turning a prompt's page clears it, keeping the typed answer;
  laid out anew, the page shows what was chosen, or the choice is cleared. A part's row turns
  nothing until its part has been drawn and stood for 0.4 seconds. Send answer waits in its place
  with its reason until all of this holds (`FileScreens.WhySendWaits`), and the steering refuses
  too, given the same reason (`WorkspaceSteering.SendAnswer`'s `waits`). A chosen cut answer's side
  panel waits for lane U's side panel from a chosen answer; until it lands, such an answer can't be
  sent.
  Approving or denying shows the whole request again in parts, as rows of one measured line
  (`PageLine.FromRow`), each part ending in a row to the next ("Next part, 2 of 3", from the last
  back to the first), with Cancel in the place of the press and Yes in the free middle. Approve's
  Yes shows only once the layout has measured the request and its last part has shown
  (`WorkspaceSteering.CanConfirm`); Deny's shows at once, since denying runs nothing and a person who
  sees part 1 of something dangerous must be able to refuse it then
  (`FileWaitingTests.DenyingShowsYesAtOnceSoAPersonCanRefuseFromTheFirstPart`). A measurement counts
  only for the confirmation it was made in and the very text it measured, and a part counts as read
  only when the view reports it drawn (`FileScreen.RequestDrawn`), never by building the page or the
  press that turns to it; the whole request has shown once every part has. The part's row turns
  nothing until the part showing has been drawn and stood for 0.4 seconds, so a double press can't
  skip a part almost unseen. Armed again, even for the same request, it starts at the
  first part (`WorkspaceSteering.Armings`). Measured differently under one confirmation, as at
  another text size, a request drawn whole stays read and shows its last part, and one drawn only in
  part is read again from its first (`WorkspaceSteering.ReadAgain`), as lane C's review of Start
  building does. Should the runtime report the request differently under the same approval, the
  confirmation lapses (`WorkspaceSteering.ArmedRequest`). Every confirmation that no longer holds is dropped before the
  file is drawn, with why as its notice. Yes sends once. Approve, Deny and Stop always ask, whatever
  the control plane's policy says (`WorkspacePresenter.AlwaysConfirmed`), so a policy of low
  consequence can't send an approval from its preview.
  **Activity** says what it is doing, the last thing this headset sent and the newest of the log
  that fits, the agent's words quoted with their chip and time, with Tell it as the main action,
  Stop beside Close and Hold to talk beside Tell it; Tell it offers the recorded instructions as
  rows where there is no keyboard, each showing the very words it would send: a row only chooses,
  and Tell it sends the chosen words as shown (`FileScreen.PresetToSend`), waiting with its reason
  until one is chosen. **Changes** shows the brief answers to what changed, why and how
  it was built, and **Checks** what was checked with Refresh beside Close: each line keeps its
  evidence class and chip, the first of each answer opens its full answer in the side panel (a
  changed file with its kind's generic icon, `FileScreens.Icon`), pages fit the room with Next page,
  and the first answer's provenance is the one source line. A page's source line takes one of its
  rows, on the page and in its side panel alike (ADR 0026), so a page of four rows holds three lines
  beside it, and an approval's request shows as much as fits there before Approve shows it whole. Until an answer is read, the page says
  so in words ("Still reading what changed…"), never an empty page, since a read of Salidium while
  a session is live has taken up to 10 seconds. The source line of Waiting and Activity is "As the
  agent reported it", with no app's name in it. Nothing in the Unity layer draws it yet.
- **`PlaneComposition`** is the model of a composition on one plane facing the eyes, for the
  redesign ADR 0026 decides (the component render's frames and `MenuPlane` use it): at most
  two columns of parts, every part of a column as
  wide as it, columns 15 mm apart and parts a degree apart, every column starting on one top line
  and, its last part stretched down, ending on one bottom line, all grown whole by the reading text's
  step. Its centre is placed as one panel of its size by `WorkspacePlacement.Place`, so the band,
  `Lowest`, `ReadingPitch`, the clearance from labels and the field hold; the plane faces the eyes
  there, never rolled, 0.46 m away, and each part lies on it at its offset from the centre
  (`PlaneLayout` in Unity's terms). A side panel sliding out is a column more, so the whole plane
  re-centres. `GlazeChecks.OnePlane` holds a composition to it as built.
- **`ViewField`** is a headset's field of view about where the person looks, read from each eye's
  projection by `DeviceMeasures` and kept as `ViewField.Current` (null in the editor and the tests,
  so the layout keeps its own angles there). Meta gives a Quest 3 as 110 by 96 degrees and a Quest
  3S as 96 by 90, without the split, which only the device tells. `LowestCenter` says how low a
  plate that faces the eyes may stand with every corner inside the field less a 1.5 degree margin
  (a design decision), and `BelowWithin` keeps the rail inside it.
- **`SeatedPointing`** makes a hand ray for a seated person: through the index knuckle from a pivot
  0.40 m below the eyes, 0.10 m behind them and 0.13 m to the hand's side, so a hand resting a
  little above a desk points ahead; on only while the hand is tracked with high confidence, in
  front of the eyes, its palm at least 20 degrees from facing the floor (resting or typing) and not facing the eyes (the
  system gesture) ([workspace-interaction.md](../validation/workspace-interaction.md)).
- **`InFrontPlacement`** decides when the stage, standing in front of the person, is placed again:
  when the session starts and the head is tracked, after a real pause, when the person recenters,
  and after a head jump no person makes. A reference space change counts only when the head jumps
  within one frame, farther and faster than a head moves; one that moves nothing, as those that come
  in bursts while system windows take and give back focus, never moves the stage. When the
  tracking space does move, the stage moves with it at once, so it stays where it was around the
  person, and only a move with no focus change around it counts as a recenter.
- **`ActivityLog`** turns journaled events into readable activity per execution, marking agent text
  as a claim. A snapshot carries state but no history, so after a resynchronization the history of
  the workstream being looked at is read again through **`EventHistory`** and **`ControlPlaneApi`**
  (`GET /api/events`, paged, refused if the journal changed).
- **`ControlPlaneApi`** also reads what Salidium and Seorak say about an execution
  (`GetUnderstandingAsync`, `GetEvaluationAsync`). Each answer states its availability instead of
  failing. An evaluation spends three of Seorak's 60 requests a minute, so a client fetches it when
  a workstream is opened, never on a timer. As an **`IIntelligenceReader`** it returns each answer
  with when it arrived; `DemonstrationReads` answers the same interface from the recorded
  demonstration.
- **`ControlPlaneApi.GetUsageLimitsAsync`** reads the provider usage limits Seorak last observed,
  account wide (`GET /api/usage-limits`), and **`UsageLeftPresenter`** writes every word of the
  Usage left glance. A reading says "At most X% left, seen today at 15:18, resets 6 Oct at 09:00", with the day named whenever it is not today in the person's time zone, and each row names the agent and window ("Codex, 5-hour window", "Codex, weekly"): the
  most that was left when the provider reported it, rounded up so that "at most" stays true, never
  a current value, never an allowance; each row also carries that share (`UsageLeftRow.Left`), which
  its meter draws. A window past its reset, by the device's clock, is not shown ("No reading since
  the last reset. Refresh later." when none is left). The source is named with the readings ("From
  Seorak, as the provider reported", or "Simulated, not from Seorak", `Source`), and a note says the
  account is not identified, so no reading is tied to the selected runtime, account or model. Every
  setup problem, a missing credential, one without the scope, a restricted one or no source at all,
  reads "Usage left isn't set up on your computer yet. Set it up there to see it here.", said as plainly
  as any other answer; the scope details stay in the Mac runbook. Only a failure (`Failed`) is said
  in the failure colour: "Usage left can't be read right now. Try again later.", or "Couldn't reach
  your computer. Press Refresh to try again." When the source could read only some limits, the note
  starts "Some limits couldn't be read this time." and no missing window is inferred. No reading
  reads "No usage reading yet.", never 0%. While the recorded demonstration plays, Usage left shows
  the recording's limits for one practice agent ("Practice agent, 5-hour window", "Practice agent,
  weekly") as if read when opened, every time moved by as long as has passed since the recording
  read them (`DemonstrationRecording.UsageLimitsAt`), so a reading is always seen two minutes ago
  and resets ahead. They say "Recorded for the demo, not from any account" where a source is named
  and "These limits are part of the recording." in place of the account note; Refresh plays them
  again and sends nothing. Limits read from the control plane close when the demonstration starts,
  and the recording's when it stops. A recording without limits says "Usage left isn't part of
  the demo." with no Refresh. **`UsageLeftScreens`** builds its panel model.
- **`ControlPlaneApi.GetRuntimeModelsAsync`** reads the models a runtime whose `ModelChoice` is
  `Listed` can use now, from the runtime's own list
  ([ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)): each
  with an opaque `ModelRef`, a name that says what serves it, where it is served (`Served`: this
  Mac, remote or unknown) and whether it calls tools as the runtime declares. Listing may start
  the runtime, so a client reads it when a person opens the choice, never on a timer, and shows
  where a model runs from `Served`, never from its name. **`CommandFactory.StartExecution`**
  sends the chosen `ModelRef` back unchanged (`modelRef`, null only for a runtime that lists no
  models: a start without one on a runtime that lists them is rejected with `ModelRequired`), and
  the execution's `ModelRef` then holds the model the runtime reports using.
- **`ExecutionView.PendingQuestions`** lists the questions an agent waits on, each with its
  prompts (header, text, options, whether several or typed text are taken, whether it asks for a
  secret), all of it the agent's untrusted words, and whether Halcyonic can answer it
  ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)). **`CommandFactory.AnswerQuestion`**
  sends one `QuestionAnswer` per prompt, by its `Key`, with the chosen labels in `Selected` and any
  typed words in `Text`; the control plane rejects answers that do not fit with `InvalidAnswer`, a
  question no longer pending with `QuestionNotFound`, and an unanswerable one with
  `CapabilityUnsupported`. A client sends an answer only when the person presses the control that
  sends it, and offers no answer field for an unanswerable question; stopping the turn withdraws
  any question.
- **`ControlPlaneApi.GetLocationsAsync`** reads where projects may live on the Mac: each project
  root and the visible folders directly inside it
  ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)), on request.
  **`CommandFactory.CreateProject`** takes an optional `ProjectLocationChoice`, an
  `ExistingFolderChoice` or a `NewFolderChoice` naming a listed root's `Path` and a folder's
  `Name` (null for the root itself), and **`SetProjectLocation`** binds a project again; the
  client never composes a path. A runtime whose descriptor has `UsesProjectLocation` starts work
  only in a project with a folder. Folder names are untrusted text, shown through `LabelText`.
- **`NewWorkDraft`** keeps the headset's selected project, runtime, model and typed objective. A
  runtime change drops its previous model. It accepts a model only from the selected runtime's
  current list, builds a workstream with a short title from the objective, and sends the objective
  as the first instruction. The model's opaque reference goes back unchanged. When the list
  arrives, it keeps the models served on this Mac first, in the runtime's order, then the rest
  (`Elsewhere`): OpenCode lists hosted models before local ones, and in the fifth headset session
  a hosted model at the top of the list was chosen. A model on this Mac is chosen for the person
  (`Preferred`, `ModelPreselected`): one that declares tool calling first, then the larger context,
  then the runtime's order; the list says neither which model the runtime uses by default nor how
  large a model is. The recap says so; a model that runs elsewhere, or where it runs is not known, is never chosen for
  them: the first press only says where it runs and that the person's code and instructions go
  there, and a second press in a row chooses it. It never builds a start without a model for a
  runtime that lists them; the control plane refuses one too (`model_required`, lane A). A runtime
  whose `ModelChoice` is `None` leaves the choice to that runtime.
- **`NewWorkReview`** holds the full request as items, Halcyonic's own label and the value it names:
  the project, its folder (now and from now on for a move), the workstream title, runtime, model,
  where it runs, the model reference and the objective. Each value is given as it is, never already
  through `LabelText.Plain`, and spelled in ASCII once, every non-ASCII and control character as its
  code point and a typed backslash doubled, because the headset font cannot draw every glyph.
  Halcyonic's own words are never spelled: the ellipsis after a title it cut (`NewWorkDraft.TitleSource`,
  cut between two typed characters, never inside a spelled code point) shows as written, so a
  literal "…" in the request is always Halcyonic's; nothing else is shortened. The panel wraps each item at its own
  width, at word boundaries, and gives the review the lines each takes (`Paginate`); the review then
  fills each page with whole items, and splits an item across pages only when it alone is taller
  than a page, starting it on a page of its own. A line counts as read only once the panel has
  drawn it (`Drawn`), and the final action appears only on the last page with every line of the
  current layout drawn. Laid out again, as when the text size changes or a banner leaves room, an
  item drawn whole stays read, one drawn in part is read again from its start, and the review
  shows the first page with anything unread; Next part waits until the page showing is drawn and
  has shown 0.4 s, so a double press never passes one almost unseen. **`NewWorkSubmission`** looks up the command id
  in projected state before interpreting an acknowledgement: a completed event still counts when
  its acknowledgement is lost. An unknown acknowledgement keeps the request unresolved until a
  terminal record arrives or the person deliberately clears it after checking the workstreams.
- **`BuildSequence`** is Start building's command chain, moved out of the panel so it is tested:
  `project.create` for a new project, with the folder chosen, or `project.set_location` first for
  an existing project that is to work in another folder (which completes with no result), then
  `workstream.create` and `execution.start`, each sent only once the control plane recorded the one
  before it completed, a projected record winning over a lost acknowledgement (`NewWorkSubmission`).
  It begins, and sends again, only on a `NewWorkReview` read to its end whose Yes it takes, once
  (`Spend`), and that shows the new project's name, the folder it sends (compared as the command
  carries it, since two places can share a name), the model and the first task; it keeps what that
  Yes confirmed and builds every later step from it, never from the draft as it changes after, and
  reviewing changes nothing in the draft. The panel also refuses Yes for a review that is no longer
  the request as it stands (`SameRequest`), as after a change made by way of the steps, and shows
  that request afresh; starts nothing more while a build is on its way, Start building saying
  "Already starting. Wait to hear how it went."; shows a move only when the folder sent is not where
  the project already is (`EntryScreens.Moves`); and, once a project is made
  and a later step stops, keeps that project and the name it was made with (`ProjectIdea.ProjectMade`).
  So Try again opens the review rather than sending. A refusal, a failure known to have had no effect or a command never sent stops it, keeping the
  refusal's or failure's code, and can be sent again as a new command built from the draft as it is
  now, reusing the project and workstream already made (`Retry`); given a newly chosen folder, a
  project that exists is bound to it first, as after `location_required` or `location_missing`; a
  move that stopped, sent again with no folder because the person chose where the project already
  is, is dropped and the task goes on. An
  unknown outcome, a failure whose effect is unknown, or an unexpected result keeps the command id in
  `Unresolved` and offers no retry. Each step says how it went in words (`EntryText.StepStatus`):
  sent, waiting for the result, confirmed only by a completed record, effect unknown, not sent, or
  refused or failed: about a folder, what to do next, from the code and never from the control
  plane's message; otherwise the control plane's reason by the one rule.
- **`ProjectFolder`** is where a project's files live, as the person chose it from what the host
  lists (`GET /api/locations`, [ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)):
  a folder in one of the host's project roots, the root itself, or a new folder the host makes
  there. It sends back the root's path and the folder's name exactly as listed and never composes
  or takes apart a path. A new name must keep the host's rule (one plain segment, a letter or digit
  first, at most 64); `SuggestName` makes one from the project's name that always does, whatever the
  name holds, falling back to "project". `Options` lists, for each root, a new folder, the root
  itself and each folder in it, and shows a root the host cannot find without offering it; every
  name shows by the one rule.
- **`StageVisibility`** is which projects' work has characters: every project until the person
  chooses, then the chosen ones; kept on the device for each journal (the eight used last), since
  project ids mean nothing in another one, and read back as every project when damaged. A
  presentation choice, never journaled and never an authorization boundary: every authenticated
  client still receives all work.
- **`ProjectsColumn`** is the menu's Projects place as an `IMenuColumn`: it keeps the host's folders
  (read when it opens and when the person asks again, never on a timer), the chosen row and the page,
  and builds its frame with `ProjectsScreens`. The connection sent last lives in `ProjectsMemory`,
  which the director keeps for the app's run (one journal), across reconnects, and gives every Projects
  column, so an unknown outcome
  holds Connect back across a close and a reopen. Every press goes through `ProjectsScreens.Allows`
  with its key, on the state at the press, and a closed column takes none. Its one send is Connect's
  `project.create` through `IMenuHost.Submit`. Hide from stage and Show on stage change the stage on
  this device through a callback the director gives it, and send nothing; New project and Add a task
  open New project through `IMenuHost.OpenNewProject`. The folders already in use come from the
  projection (`ProjectView.location.path`). It draws again only when the projection, the connection,
  the reading size, the overview (the same object until something changes, compared by reference) or
  the minute moves, without building a frame each tick.
- **`FolderConnect`**, **`FolderConnection`**, **`ConnectScreens`** and **`ConnectText`** are Connect
  a folder in the client core, not yet drawn on the headset: from `GET /api/locations` they offer
  the folders directly inside the allowed roots that no project uses (`used_by` empty), and a root
  itself only when it is a repository no project uses, the latest changed first, with what the
  computer saw of each ("Repository · changed 3 days ago") and look-alike names marked; Connect sends
  one `project.create` with `{kind: 'existing_folder', root, folder_name}` exactly as listed and the
  folder's own name as the project's, counted as connected only once its record completed with the
  project, never sent again while its outcome may have run. Names are quoted inside Halcyonic's
  sentences and shown by `LabelText`'s rule.
- **`WorkOverview`** counts every project and workstream for the rail, Connect projects and More
  work from the projection alone: per project its work, active, needs you and to check counts by
  the lineup's tiers, whether it shows, and how much has no character; and every workstream
  without a character, ranked as the lineup ranks, with whether its project is hidden or the stage
  is full. Hidden projects keep their counts, so what needs the person never disappears with its
  project or behind the six slots. `ForRail` picks the projects a short rail shows, those that
  need the person first.
- **`ProjectIdea`** is what the person wants to make before anything is sent: an idea in their own
  words, named from its first words, or the answers to Help me figure it out's four fixed
  questions (kind, who it is for, what it should do first, with first steps offered for the kind,
  and a name that can be skipped), which always compose the same recap ("Make a website for my
  team. First, show one page that says what it is."). Typed answers go in as typed. The fixed
  questions involve no model and never present themselves as an assistant. With the companion
  ([ADR 0025](../decisions/0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)),
  it also holds the exchange, and a proposal fills the recap marked as the companion's suggestion
  (`NameSuggested`, `TaskSuggested`) until the person changes it, with the typed idea kept
  (`OwnWords`, Use my words). `Keep` and `Restore` give it to and take it from the device.
- **`CompanionExchange`** is one exchange with Create's companion: the person's words and the
  companion's replies in order, kept on the device and sent whole with each request (the computer
  keeps nothing). One request at a time (`Ask`, `Replied`, `Failed`, `Retry`), only after the
  person's words or when they ask for the recap, inside the request's bounds (20 messages, 2,000
  characters a message, about 23,000 in all); a reply to a request the person left behind
  (`Leave`) is dropped by its generation. After a proposal, the recap is where things change.
  Under a question, the person chooses one answer row (`Chosen`), as ADR 0026 has every answer:
  a suggestion (`Choose`), their own words typed or heard (`Write`, kept while another row is
  chosen), or Go on without it (`ChooseWithoutIt`). Choosing sends nothing; only `SendAnswer`
  says the chosen answer and asks for the next reply, so a stray press never reaches the
  companion. `EntryScreens.Companion`, until New project replaces it, still sends a suggestion
  when it is pressed.
- **`CompanionText`** writes the words around the companion, with the computer as `HostText`
  says it: its line quoted and tagged as its own ("The companion says: “…”"), its view as its
  opinion, the note that it is an AI that can be wrong, waiting, and every failure from its code
  with a next step. `EntryScreens.Companion` is its screen as a model; the headset's panel does
  not show it yet (below).
- **`CreationDraft`** and **`CreationDrafts`** keep every Create draft on the device across an
  app restart: the idea or answers, the recap, the exchange with an answer written for it but not
  yet sent (it comes back chosen, never sent), the folder, agent app and model
  chosen, and what the computer already made of it (a project, a task), for the journal it was
  made with, for 7 days without a change, in one JSON file in the app's private storage that is
  written only when something changed. A project the computer already made comes back as a task
  for it, and a task it made resumes at its start (`BuildSequence.Resume`), so nothing is made
  twice; a model that runs elsewhere is never chosen again without the person's second press.
- **`CompanionRecording`** plays the companion's part of the demonstration: one exchange recorded
  once from the real companion (`pnpm companion:record`), pressed through in its recorded order,
  labelled recorded, never asking a model, and never showing the model's name.
- **`AttentionWatch`** notices work that comes to need the person while they create, hidden
  projects included, so the entry panel can offer Open now or Keep creating; what already needed
  them is not offered again, and it never switches by itself.
- **`EntryText`** writes every word of the rail and the entry panel: plain verbs, statuses in
  words, a model's serving place in terms of where the person's code and instructions go, and
  nothing that claims discovery (Connect lists the projects already set up on the Mac). The words
  follow the glossary: a task, an agent app, your computer, never a workstream, a runtime or the control
  plane.
- **`PanelModel`** is what one screen of a foreground panel shows, never where (ADR 0023): its
  title and a short context, or, on a panel that stays beside its character, the work's state badge
  and marks and a notice in the title's place for a few seconds; its tabs (`PanelTab`), the one
  showing chosen and what waits for the person in the attention colour; the whole question a tab
  answers as a heading, with an action beside it such as Refresh; a lead line or a `PanelBanner` in
  its place; its list of `PanelRow`s (choices, facts with a line over the title and a word saying
  what pressing does, filters, steps and how they went, and lines of text, which may go on from the
  line before with no gap, lean as the agent's words, or give way where there is no room, as a log's
  older lines do) in one column or two; a pager for a body the screen pages itself, with a heading
  and a note at its left; a note at the bar's left; and its actions: an `ActionSet`, which refuses a
  second primary, a third secondary, a second destructive or a second Back, or a `ConfirmStep` in
  the bar's place, whose Yes stands where no control of the screen stood a moment before. Each
  `PanelAction` may name its icon by meaning (`GlazeIcon`, from the spec's table): Approve, Deny,
  Stop, Tell it, Send answer and their Yes the same icon, Hold to talk the microphone and nothing
  else (a `PanelAction` refuses the microphone unless it is held), Cancel and Close a cross, a locked Yes a lock, and words with no icon in the table, such as
  Done, Skip and Clear, none. The header's Close says its icon (`CloseIcon`, a clock for the
  welcome's Not now). Text from outside comes in as `LabelText.Plain` shows it and says it is data,
  which alone may end in an ellipsis.
- **`EntryScreens`** builds every entry screen as a `PanelModel` from the draft, the overview and
  the Mac's state, so the Unity layer only draws it and acts on the id a press raises, and decides
  what each offers: Start building at the right end, unavailable with what is missing
  (`StartProblem`, the recap's checks in their order); the whole request (`ReviewOf`, given names as
  they are, so the review spells each once); Yes, start building locked until the last part; the
  next action a step's outcome allows; and the two presses that clear a start that may have run.
- **`NewProjectFlow`** is New project as a column (`IMenuColumn`), opened by Projects' New project
  or Add a task (`Open`), in the file's place beside Projects with no light line, replacing the
  entry panel's creation screens. It keeps each place's draft and how far its start got, the step
  and page showing and what is chosen on it, and gives the frame `NewProjectScreens` builds for
  them, built once for each change, so `Drawn` counts a review's part read only for the very frame
  it gave. A press acts only when the frame the director last drew offers it, available, so nothing
  the person can't see or press now runs; a row is keyed by what it chooses (a suggestion or fixed
  answer by the question it answers and its words, a folder by its place's path and its own name),
  so a press on a frame drawn before a new reply or listing never takes another, even the same
  words on the next question. It asks the computer only to read (the
  companion's status, folders, and an agent app's models only when the person chooses the app or
  opens How it runs, since listing may start it), the companion's next reply, and the commands a
  review read to its end confirmed, which `BuildSequence` alone builds and sends through
  `IMenuHost.Submit`. A command whose outcome is unknown is kept on the device for the journal it
  was sent to, as drafts are (`IKeptCommand`; `KeptUnknownStart` over the device's preferences, in
  Unity its PlayerPrefs), so the same computer over USB or paired finds it and another never shows
  it; it is read afresh at every use, so the entry panel and New project each see the other's at
  once, and nothing is read or kept before a live journal shows. While one is kept and no build is on its
  way, it comes first on opening, Build shows it, Start building and Start over wait with "Your last
  start may have gone through.", and only the person's Clear, then Yes, clear removes it; the flow
  replaces or clears only an id it kept for its own build, never one kept by an earlier run or the
  entry panel. A build the person confirmed goes on after Close: the director keeps one flow for the
  app's run on one journal, in `MenuMemory`, across reconnects and never renewed on one, and ticks it
  every frame whether it shows or not, so an acknowledgement lost with the socket is settled by the
  command's record once the session is back, the next step is sent, and opening New project again
  shows where the build stands. A build left behind when the journal changes or the headset is
  paired again keeps its id under that journal, shown on no other, and it comes first when the
  person returns to that computer. A flow serves only the session it was made for
  (`ForAnotherSession`): one made in the demonstration never reads from or sends to a computer once
  a real session takes the demonstration's place, and a live one works on the first journal it is
  shown; once the session shows another, it closes for good and acts, reads and sends no more, so a
  late acknowledgement or record never sends the next step of one journal's build to another. The
  director makes it afresh through `MenuMemory` when the one kept is for another session, and on a
  re-pairing. The flow is that holder whole, not a part of it: `BuildSequence`
  builds from the flow's `NewWorkDraft`, and a companion's reply or a folder listing on its way
  belongs to the same draft. Where the system keyboard can't open (`IMenuHost.KeyboardOffered` false,
  as in the editor), a row that only types is left off, since it would do nothing; one already
  holding words, heard or kept, stays as a choice that a press only chooses, and choices and Hold to
  talk stay where offered. Hold to talk's heard words land where words are given, never sent unchecked; leaving
  for another window lapses an armed confirmation and sends a review back to the recap. Drafts are
  kept across a restart through `CreationDrafts`, for the computer they were made with. A kept
  folder's place is read again from every listing the computer gives (`ProjectIdea.ReadPlaces`,
  lane L's `ProjectFolder.Current`), once when a draft with a folder opens; a place it no longer
  lists shows as gone and Start building waits, "Choose where its files live.", until a listing
  shows it again, and a read that fails changes nothing.
- **`NewProjectScreens`** builds New project (ADR 0026) as `MenuFrame`s, from the same models
  and checks as `EntryScreens`, which it replaces: the steps as a row of shapes (Your idea,
  Questions, Recap, Build), each reached once there is something there, the chosen one lit
  and an earlier one the way back; every footer Close, one other prompt and the main action, the
  most an 18 dp footer holds. Your idea offers typing the idea and one way to figure it out, the
  companion or the fixed questions. Questions is the companion's turn: its line and question quoted
  as its own in two rows, the line dropping first so the question is never cut; its view only when
  it thinks the idea can't be built; its suggestions, the person's own answer and Go on without it
  as answers that choosing only lights, the main action following the one chosen (Send answer, Make
  the recap, Make the recap from my words); Hold to talk as the secondary prompt; and the note that
  it is an AI as the source line of any page showing its words. The Recap shows each fact as a row,
  Suggested beside what the companion suggested, opening a side panel with the whole of it; the
  chosen fact's change stands beside Close (Start over while none is chosen, confirmed in place),
  and changing a suggested first task, the folder or how it runs is a page of answers with Done.
  The fixed questions go forward only: choosing an answer, the person's own or the skip only
  lights it (`ProjectIdea.ChooseGuideAnswer`, `WriteGuideAnswer`, `ChooseGuideSkip`), and Next
  question gives it (`NextQuestion`), Make the recap on the last. Changing a first task composed
  from them walks them again with every answer chosen (`ChangeFor`, `TaskFromAnswers`); words the
  person wrote are never composed over. A name, a first task in the person's own words and a new
  folder's name are given on a words page: the words as a row that opens the keyboard, Hold to talk
  beside Done, and Done unavailable, saying the rule, while they break it. Hold to talk stands
  beside the main action wherever words are given, and heard words land chosen with the note to
  check them. Start building's review shows the whole request a part at a time as the Unity layer
  measured it, each part from the wrapped row where it was split, never cut, with Next part as the
  last row of every part but the last; Yes, start building appears in the middle only once the
  last part has shown, with Cancel where Start building was pressed. Start building then shows
  each step and the next action its outcome allows.
- **`WorkspaceScreens`** builds every screen of the open workspace as a `PanelModel` from its
  presentation, the steering and what the workspace is in the middle of (`WorkspaceScreen`: the
  tab chosen, a notice, the instructions offered where no keyboard is, and the agent's question or
  the whole request as the panel split them at its width): the header, the tabs, each tab's answer
  and the bar, by the rules under "The workspace" below. **`QuestionPlace`** keeps where the person
  is in an agent's question, a step at a time across its prompts: each part of a prompt's text, as
  the panel measured it, then each further page of its answers; a prompt counts as shown whole once
  the last part of its text has shown, and another draft starts from its first step.
- **Help me understand and What was checked?**, the workspace's two sections, answered from what
  the understanding source (Salidium) concluded and what the evaluation source (Seorak) measured,
  and named for the person's questions, never for the products. **`IntelligenceFeed`** decides, on
  the main thread, when a source is read: when a section shows for an execution it holds no answer
  about, and when the person refreshes; the understanding is read again once the execution changed
  and two seconds have passed, the evaluation never by itself. The last answer stays while a new one
  is read, and a failed read says why, "did not answer in time" for a timeout. Help me understand
  asks one of three questions at a time (`UnderstandPrompt`); What was checked? reads both sources.
  **`UnderstandingPresenter`**, **`CheckedPresenter`** and **`EvaluationPresenter`**
  (`WorkAnswers.cs`, `IntelligencePresentation.cs`) write every word, as a provenance line and lines
  with a tag, fitted to an **`AnswerRoom`**: the rows a page holds and how many rows each line takes,
  which the headset measures on its labels. Each gives two depths (`AnswerDepth`): `Brief`, a line
  or two a person reads first (What changed?: the count, and whether a check ran after the changes;
  Why?: the latest reason, quoted, and how many more; How was it built?: the explanation's own "how",
  said to be a model's and whether it is up to date, or why there is none; What was checked?: the
  latest check and any files changed since), and `Full`, everything below, for the panel that opens
  on request. Every line keeps its own class at both depths; nothing is blended into one sentence.
  - *What changed?* says how many files changed and how ("4 files changed: 1 new, 2 edited, 1
    removed"), the commits the work started from and stands at where the source watched them
    ("From commit 3f9a2c1 to 8b1e4d7 on main"), then each file, most recently changed first, as the
    source observed it: "New: src/middleware/rate-limit.ts (+57 −0)", Edited, Removed, Moved,
    "Moved and edited", or each way it changed, "(+5 −1 or more)" where the removed lines are a
    lower bound. A file shows by its path in its repository where the source resolved one, else by
    its name with as many folders as tell two files of one name apart. As many as fit show and the
    rest are counted ("And 4 more files"), then whether a check ran after the changes (inferred),
    and, where there is room, the commits and what is not done yet.
  - *Why?* quotes what the agent said before each change, in the order it said it, "Agent says:
    “…”", leaning, with "Said before it changed …" under it: the source binds a reason to a file by
    time, so the words never claim the reason is the file's. Files with no reason are named.
  - *How was it built?* steps through the source's explanation only when one was generated, a page a
    step: what it is about, why, each of its Why lanes, how, a change of approach, then the evidence
    (the changes and checks, observed). Each step's first line names it and says it was explained by
    a model and whether it is "up to date" or written "before the latest evidence"; every line of it
    is tagged explained and leans. Without an explanation it says why ("No explanation was written
    for this work.", "Explanations are turned off on your computer, …", "An explanation is being
    written. …") and shows the evidence. Nothing here asks for an explanation to be written.
  - *What was checked?* lists the checks the understanding source saw run, the earliest first, each
    with its outcome and time ("Tests failed at 05:00: …") and the change it ran after, from the
    observed times ("Ran after the last change, to tally.js at 05:00", "Then refunds.ts changed, so
    it no longer covers it"), then whether a check ran after every change, what needs a look and what
    the agent said about its checks. The evaluation follows on a page of its own under its own
    provenance line: the checks by kind, the cost as "About $0.39." with the source's note
    "Estimated from token counts at list prices. Not a bill.", and the outcome, each with its own
    availability, coverage and freshness, never combined, read as stale once its `stale_at` has
    passed. A value the source does not have reads as unknown, pending or "known once it ends",
    never as zero.

  A provenance line is the only place that names Salidium or Seorak ("From Salidium 0.6.1, 2
  minutes ago", "From Seorak, read just now"); a synthetic source's says "Simulated explanation",
  "Simulated checks" or "Simulated measurement" instead
  ([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md)),
  and a recorded answer says when it was recorded, with its relative times and staleness judged as
  of then. Where there is no answer, the provenance line still names the source, then says why in
  words: "From Salidium · No understanding yet", "Understanding unavailable", "unreadable" or "not
  allowed", with the control plane's reason, such as "No answer in time. Press Refresh in a
  moment." Every
  claim keeps the source's own epistemic class as its tag (observed, reported, inferred, planned,
  explained), never upgraded. No line of an answer is amber, which is for what waits for the person
  only: partial, stale, unavailable and simulated are said in words, in the secondary tone. **`AnswerPages`** splits an answer that pages, keeping a line with
  its detail, never a provenance line at a page's foot, and a step's heading again at the top of a
  step that goes on; the page showing is named at the head of the provenance line ("Step 2 of 7 ·
  …"). **`IntelligenceText`** makes every text from a source plain by `LabelText`'s rule, so a
  bidirectional override or a zero width character in it shows as its code point and markup as
  written.
- **`LoopbackWebSocketTransport`** implements `IRealtimeTransport` for the access token over
  `ws://`, the USB path: it opens a connection to the literal loopback address, has the control
  plane prove it holds the token on it (`LoopbackProof`), then performs the upgrade with the token on
  that same connection (`WebSocketUpgrade`) and frames messages with `WebSocket.CreateFromStream`, as
  the pinned transport does over TLS. It replaced a transport over `ClientWebSocket`, which works
  under IL2CPP on a Quest 3 ([quest-3-device.md](../validation/quest-3-device.md)) but opens a
  connection of its own after the proof; `CreateFromStream` is the same `ManagedWebSocket`
  `ClientWebSocket` used, and the upgrade over a plain socket has not yet run on a headset.
- **Pairing over the network**
  ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md),
  [network-pairing.md](../validation/network-pairing.md)). `PairingClient.PairAsync` takes the
  address the person typed and the eight-digit code `pnpm pair` shows, and runs the exchange of
  [REALTIME.md](REALTIME.md) on the network listener's `/pair`: SRP-6a (`Srp6a`, `SrpClient`) and
  the proofs and sealed credential (`PairingCrypto`), bound to the certificate its connection saw,
  which it returns as the pin with the device id and credential in a `PairedControlPlane`. It
  keeps nothing unless the control plane proves it knew the code. `RevokeAsync` asks the control
  plane to stop accepting the credential, as forgetting it does. Refusals arrive as a
  `PairingException` with the control plane's code and the attempts left.
- **The pinned transports.** `PinnedConnection` opens TCP and TLS with `SslStream`, whose own
  validation callback compares the certificate's SHA-256 with the pin, so another certificate ends
  the handshake before anything is sent (`CertificateMismatchException`); without a pin, for
  pairing only, it records the certificate. `PinnedWebSocketTransport` performs the WebSocket
  upgrade over it with the device credential and hands the stream to `WebSocket.CreateFromStream`;
  a refused upgrade reports the control plane's code, such as `device_revoked`.
  `PinnedHttpHandler` sends each REST request as HTTP/1.1 on a connection of its own, for
  `ControlPlaneApi`. They exist because Unity's Android class libraries ignore
  `ClientWebSocketOptions.RemoteCertificateValidationCallback` and throw inside
  `HttpClientHandler.ServerCertificateCustomValidationCallback`.
- **`ControlPlaneTarget`** is where a session connects and how it proves itself: `Local`, the
  access token over `LoopbackWebSocketTransport` as over USB, or `Paired`, the device credential over
  the pinned transports. It creates the session's transports and the `ControlPlaneApi`, and
  `SameAs` tells whether a client made for one target serves another.
- **`IPairingStore`** keeps the pairing, credential included; `FilePairingStore` writes it as one
  JSON file, replaced whole. The Unity layer chooses where.
- **The recorded demonstration** is what a device with no control plane shows, so that
  competition judges can run the app with nothing else and act in it: the scripted interactive
  demonstration of [ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md).
  `DemonstrationRecording` reads what the control plane records with `pnpm demonstration:record`
  ([LOCAL_DEVELOPMENT.md](../runbooks/LOCAL_DEVELOPMENT.md)): the welcome, the beginning's
  snapshot, and a tree of nodes, each a stretch of event messages with their times. Where a person
  could act, a node lists the answers it holds a continuation for (approve, deny, stop the turn, one
  of its recorded instructions, each with a short label, or one option of the agent's question of
  [ADR 0022](../decisions/0022-agent-questions-reach-the-person.md), with the answers sent), each
  leading to its own node. A node ends by holding its final state: until an answer at an approval
  or a question, for 60 seconds while it offers
  instructions, or, where the recording has nothing more, after a snapshot of its control plane
  started again without runtimes, for 20 seconds. The control plane computed every state in it,
  so the client still derives nothing. A recording whose journal is not a fixture, whose answers
  are not where an instant ends, or whose nodes do not form a tree that continues the journal, is
  refused; every character reads recorded, and its runtimes are synthetic, so it also reads
  simulated. The work a person directs runs on "Practice agent", with the mock
  runtime's capabilities; the work beside it runs first, on "Practice agent, watch
  only", which declares nothing to direct. `WorkspacePresenter` therefore offers, as it does live
  and with no special case, exactly the actions the recording holds an answer for; a test checks
  every point of every path. Beside the tree, the recording holds the control plane's REST answers
  about each execution's understanding and evaluation, keyed by execution id, each from the point of
  the playback where it took effect: the recorder read them through the control plane's own routes
  from stand-ins for Salidium and Seorak that speak their real contracts with invented content,
  and every one is marked synthetic
  ([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md)).
  A recording with an answer that is not a stand-in's, or that is about another execution, holds
  from where no instant ends, or is out of order, is refused; one without answers still plays.
  `DemonstrationPlayer` plays it as a `RealtimeSession`, through a `DemonstrationTransport` per
  connection that opens no socket and never uses the token. Hello is answered with the welcome and
  the beginning's snapshot, and the events follow at their recorded pace, one instant at a time. A
  command that matches an answer offered where the playback stands switches to that answer's node;
  it is answered with a `rejected` acknowledgement with the code `demonstration`
  ([REALTIME.md](REALTIME.md)), whose words say that nothing was sent to any agent and what the
  recording continues with, for example "Not sent to any agent; the recording continues as
  recorded for approving." or, for an option of the question, "Nothing is sent to an agent. The
  recording goes on as if you answered “1 hour”." `WorkspacePresenter.Feedback` shows those words as they are, without
  "Refused:", since the recording then plays its own recorded command, which its runtime confirmed.
  Typed text that matches no recorded instruction, ignoring case and spacing, continues with the
  first one offered and says so; any other command changes nothing and says that too. Nothing is
  journaled, and no command is ever reported accepted or done. Once a final state has held, the
  transport sends the beginning's snapshot again on the same connection, so the demonstration
  starts again without a disconnect; consumers see a rewind (`StateChanges.Rewound`: the same
  journal at an earlier position) and drop activity and submissions from before, as after a journal
  change. A new connection after a pause, as when the headset sleeps, goes on where the last one stood:
  the player keeps the path of answers, the node, its events played and how long it had played or
  held, and the transport answers the session's resume cursor with a resumed welcome and only the
  events the client missed, keeping the timing; a cursor off that path, or a recording that had
  reached an end, starts from the beginning. The
  player shares where the playback stands: `InstructionsFor` the recorded instructions offered for
  an execution, as `PresetInstruction`s; `Ended` while it holds one of its ends; `Plays`, how often
  it started from the beginning; and `Reads`, a `DemonstrationReads` that answers the workspace's
  understanding and evaluation reads with the answer in force where the playback stands, marked
  recorded, and says the demonstration recorded nothing there when there is none. It can take a
  recording still being read on another thread and connects once it is read. `DemonstrationFallback` chooses what is shown: the demonstration when
  no control plane is configured; otherwise the control plane, except while it has not been live
  since the start and its connection has failed, when the demonstration plays and the control plane
  is tried again behind it. Once the control plane is live the demonstration stops for good, and a
  control plane that drops later shows its last known state as usual. Each switch reaches consumers
  as a resynchronization, like a journal change. Its `Line` is what the line above the stage says
  while the demonstration is shown: "Demo: recorded work played on this headset. Nothing here is
  live." and "It follows your answers. Nothing reaches an agent.", and, while it holds
  an end, that it starts again shortly.
- **The room placement's decisions** ([ADR 0015](../decisions/0015-the-stage-stands-on-the-persons-desk.md)),
  on plain floor plan geometry the Unity layer converts to and from its vectors:
  - `StageSurfaces.Choose` picks where the characters stand from the room's surfaces facing up
    (desks and tables, then other furniture tops) and the objects standing on them: 0.4 to 0.9 m
    from the eyes, 0.15 to 1.0 m below them, within 45 degrees of where the person faces, and no
    steeper than 62 degrees down. A spot counts only when the whole lineup, as the stage draws it
    (an arc around the eyes through the spot, 60 degrees between the outermost characters), stays
    on the surface and outside every object on it, by a margin of half a label plate, which the
    stage scales with the distance, and 2 cm. Desks win over other furniture; among spots of one
    kind, the one nearest 0.55 m straight ahead wins, where 10 cm off that reach costs as much as
    10 degrees of turn, or 2.5 degrees of looking down beyond 45. `StillSuits` decides whether a
    placement kept from an earlier session still suits the seat, with looser limits and half the
    margin, so sitting a little differently keeps it and another seat chooses again.
  - `PlacementMemory` remembers which saved anchor keeps the stage's place in which room, one per
    room for the eight rooms used last, in plain text, and returns the anchors it stops
    remembering so they are erased.
  - `RoomStatus` holds what the person chose, passthrough's state, what is known of the room and
    where the stage stands, and derives the space shown (the real room where passthrough works and
    the person has not chosen the virtual space), what is offered (room access again, or space
    setup where it would help and the headset can run it), and one short line that says where
    the agents are and why.
- **The sound's decisions** (under "Sound" below): `GlazeSynthesizer` renders every cue of the
  Glaze direction to mono samples, the soundbook page's synthesis ported line by line, and
  `SoundCueSelector` chooses which cue plays, on whose note, from where and when, from what each
  pump changed and from what the person did in the workspace (`WorkspaceAct`).

## Verification

`apps/xr/dotnet` compiles the two packages' sources as .NET Standard 2.1 with C# 9 and warnings as
errors, the constraints Unity imposes, and tests them with NUnit on .NET 10:

- every event in the recorded trace, and every server message a real control plane sends, reads
  strictly and writes back to the same JSON;
- the session against an in-memory server: handshake, resume, journal change, acknowledgements,
  outcome-unknown cases, refusal, idle detection, backoff, malformed input, backlog
  resynchronization;
- character cues for every activity; identities that stay fixed across versions and spread over
  every shape and hue for time-ordered ids; the lineup's choice, order and stable slots;
- activity descriptions from both recorded traces, workspace actions for every status and
  capability combination, command feedback, and history paging;
- the workspace's words and peek, with text from outside shown by the one rule in titles, notes,
  the peek, activity and the objective; steering with its confirmations and their lapses, an
  approval sent only once every part of its request has shown, each part turned to starting the
  window again, and a denial needing no reading; and command submissions through a session against
  the in-memory server (accepted, refused, cut off, not connected);
- the new work draft refusing a blank or oversized objective, dropping a model after a runtime
  change, refusing a model outside the runtime's list, leaving a choice to a runtime that does not
  list models, and sending the chosen opaque reference unchanged; its create and start commands
  through a real control plane with the mock runtime's list; the full request as whole items, every
  line of every item on exactly one page for random item heights, an item split only when taller than
  a page, confirmation possible only on its final page, and projected completion winning over a lost
  acknowledgement while an unresolved outcome keeps the command id;
- the folder: a suggested name that keeps the host's rule and is never hidden for names with
  accents, emoji, spaces, leading dots or nothing usable; the rule itself; a choice sending back
  exactly what the host listed, Use that folder naming the same one; names by the one rule; the
  listing's options, a missing root offering nothing; the recap's and review's words, a move showing
  the folder now and from now on; a new project created in its folder, an existing one bound first,
  a start without a folder bound and started again on the same workstream, `location_exists`
  offering the folder, every folder code's next action from the code, other reasons shown plainly,
  a failure with an unknown effect never said to have done nothing, and binding completing with no
  result; and, against a real control plane with a project root, a taken name refused with
  `location_exists`, Use that folder creating the project there and starting its work, and new
  work in a new folder binding the project first;
- the entry: with 0, 1, 6, 7 and 40 workstreams across a shown and a hidden project, every
  workstream that needs the person either has a character or is listed first in More tasks and
  counted with its project; project counts by tier, names by the one rule, the rail's choice of
  projects; the visibility per journal, saved and read back, a damaged preference showing
  everything; a lineup request taking the weakest slot and ending when its work leaves; a precise
  idea straight to the recap, fixed questions giving the same recap for the same answers, typed
  answers as typed, a changed kind dropping its first step, no name asked for an existing project;
  the attention watch offering only what newly needs the person; the build sequence sending each
  command only after the one before completed, accepted never counting as done, a refusal sent
  again as a new command, an unknown outcome or unexpected result keeping the guard; the question
  words and What do you need from me? only while a request waits; and the entry words naming no
  brand;
- the one rule for text Halcyonic did not write: line breaks, tabs and other white space as one
  space and spaces as written; every control, format and default ignorable character and every
  half of a surrogate pair as its code point, including the end of text character that would end
  a TextMeshPro label; markup, backslashes and look-alikes as written; applying it twice changes
  nothing; hostile text escaped for TextMeshPro, read back through a model of TextMeshPro's escape
  handling, is exactly the plain text; its table covers every control and format character .NET 10
  knows, and its whitespace is exactly .NET's;
- the peek at 72 frames a second: nothing while the head sweeps the stage or turns slowly, a peek
  after 0.4 s of rest near the middle of the view, a glance aside kept, one peek at a time
  fading out before the next fades in, hands at once and first, nothing for the open character or
  from the gaze while a workspace is open or focus is lost, and a look and pinch only on a showing
  gaze peek with no hand on a target; the seated ray reaching the arc 2.4 m away and a desk
  lineup from a relaxed hand, where the headset's shoulder ray cannot, and off for hands resting or
  typing palm down, for the system gesture, beside the head or untracked, with low hands pointing
  at the floor; the workspace clear below the arc and above a desk lineup, in the band, above the
  desk, near where the person looks, and the least move where nothing clears; the stage kept
  through a half minute of focus flaps with reference space changes, moved with a tracking space
  that jumps, placed in front after a recenter with no focus change, after a jump no head makes and
  after a real pause, and never for natural head motion or a long frame;
- the evaluation read against a canned server: a full evaluation whose unknowns stay null, an
  answer without one, and a refusal;
- the two sections' words: a real source's answer with each claim tagged with the source's own
  class, whatever class it is and never upgraded, and the provenance naming the product and how long
  ago; the most important lines kept in reading order when they do not all fit; what the source
  says twice shown once; unknowns left out or said, never blank; every availability, reading and
  failed read in words; a simulated answer saying so and when it was recorded; each evaluation part
  with its own availability, coverage and freshness, stale after its `stale_at`, and a recorded
  answer judged as of its recording; the cost always an estimate with its note; only the provenance
  naming a product; source text made plain by the one rule, markup kept as written; the feed asking
  once when shown, again on refresh, again after a change only when it
  follows and the interval has passed, dropping a read for another execution, and saying why a read
  failed or timed out;
- the room placement's choices: a desk in front taking the lineup on its near half with the whole
  arc on it; a desk winning over furniture that sits better; the floor, shelves at eye level,
  surfaces out of reach, behind the person or too small for the lineup refused; a monitor on the
  desk, an L-shaped desk and a turned desk; sitting back, standing, and a low table moving the
  lineup; placements kept across a slightly different seat and chosen again from another seat or
  when the desk moved; the memory's rooms, replacements, limit and saved form; and the status's
  space, offers and words, all short and naming no brand;
- the demonstration: every message of the bundled recording reads strictly and writes back to the
  same JSON, it reads in well under a second, and a live journal, anything out of order, an answer
  inside an instant, a node that is not a tree's, or an ending from another journal is refused; at
  every instant of every path the workspace offers exactly the actions the recording holds an
  answer for; the transport holds at the approval, answers each kind of command in words, continues
  with the recorded answer, ends without a runtime and starts again without ending the connection;
  a judge's first minutes, through `WorkspaceSteering` and `CommandSubmissions`: an approval
  confirmed and answered in words, the tests failing, a recorded instruction offered as a preset,
  the tests passing, the recording ending with nothing offered, and the session live throughout;
  typed text answered with a recorded instruction that is named; starting again as a rewind of the
  same journal; the fallback shows it without a control plane, offers its instructions where it
  stands, says when it has ended, falls back to it from an unreachable control plane while trying
  that one again, switches to the control plane once it is live and never back, and plays it from
  the beginning after a pause; every recorded understanding and evaluation answer reads strictly,
  is a stand-in's, marked synthetic, with the version `simulated` and an instance of zeros; the
  answer in force follows the path through the tree (waiting at the approval, a failing test after
  approving, verified after the recorded instruction, unverified after denying); a real source's
  answer, a refusal naming a source, another execution's answer, an answer inside an instant or out
  of order is refused, and a recording without answers still plays; and `DemonstrationReads`
  answers where the playback stands as a judge answers, marked recorded and simulated in words;
- the sound: every render's length, peak and energy equal the soundbook page's own, computed by the
  page's code; renders repeat sample for sample; no sample is NaN or above the page's ceiling;
  each cue lasts as the soundbook says, starts and ends in silence, meets its loudness target and
  stays under its own spectral centroid ceiling; the room is the send convolved with its response,
  checked against a direct sum; the selector's cue for each change of activity, silence on
  snapshots, resynchronizations and rewinds, onsets 300 ms apart with the most pressing first,
  "Last known" once per loss and never for a session the application stopped, nothing while cues
  cannot be heard and nothing late, except an act sent as the system keyboard closes, which sounds
  once focus returns; repeats dropped unless the person acted, each character's own note, the
  person's actions in front of them, and the bundled demonstration heard event by event as its
  story;
- the session against a real control plane process with the mock runtime: an approval round trip
  to a finished turn with the workspace offering exactly the admissible actions, history over REST
  matching what arrived live, understanding and evaluation answering that their providers do not
  observe the mock runtime, read through `IIntelligenceReader` and said in the sections' words
  ("Understanding unavailable: Salidium does not observe sessions of the mock runtime."), resuming
  after a dropped connection without a snapshot, an approval
  and then an instruction steered from the workspace to results the runtime confirmed, and an
  execution in flight shown as stale during a control plane crash and as `unknown` after the
  restart;
- the real control plane process stopping by itself once its standard input closes, which the
  operating system does when the test host dies: the tests start every control plane with
  `HALCYONIC_EXIT_ON_STDIN_END=1` and a standard input only the test host holds, so none outlives
  a test host that is killed, crashes or is ended by a runner's timeout;
- pairing: SRP-6a against RFC 5054's test vectors, and the whole exchange against the vectors the
  control plane computed; the pinned transports against TLS servers of their own (the pin decides
  the handshake and an impostor receives nothing, HTTP bodies by length, chunks and to the end, a
  refused upgrade's code, a wrong accept key, an upgrade never answered); the pairing file; and
  against a real control plane serving its network listener on a loopback address: pairing,
  directing an approval to a finished turn as the device over pinned TLS with every command
  journaled with the device as its principal, history over the pinned REST handler, wrong codes
  closing the window, a relay in the middle refused, revocation ending the session, forgetting,
  and another identity on the same port refused with nothing sent.

Run `pnpm test:csharp` (the .NET 10 SDK and Node.js must be on `PATH`).

## Unity layer

`apps/xr` is a Unity 6000.3.25f1 project whose manifest pins OpenXR 1.18.0, the Meta XR Core and
Interaction SDKs and the MR Utility Kit 207.0.0, XR Hands 1.9.0 and Newtonsoft.Json 3.2.2. Its
scripts use only long-stable core Unity APIs:

- `HalcyonicBootstrap` adds the stage to any scene that lacks one. The stage scene carries its own,
  so that its `FocusGuard` can reference the rig's hands.
- `ControlPlaneConnection` owns what is shown through a `DemonstrationFallback`: the session with
  the control plane `ControlPlaneSettings.Target()` names, the one this device paired with or else
  the one its access token is for, and the demonstration, loaded from the text asset
  `Resources/HalcyonicDemonstration.json` when first needed, while no control plane is configured or
  reachable. It loads the asset on the main thread and reads it on another, so the recording's
  half megabyte never holds up a frame, and logs once if it cannot be read. It pumps both every
  frame and exposes the session shown, `DemonstrationLine`, the words for the line above the stage
  while the demonstration is shown, and `DemonstrationInstructions`, the recorded instructions the
  demonstration offers for an execution where it stands, empty otherwise. It passes the application's
  pause state to `RealtimeSession.SetPausedAsync`, which stops the session on a pause and resumes it
  from the last position afterwards. It ignores the resumes Unity reports without a pause, at app
  start and when an XR session starts, and never revives a session stopped in between. It logs, as
  `Halcyonic: ...` lines without a stack trace, whether the control plane or the demonstration is
  shown and why, and each change of either session's status as
  `Halcyonic: connection <phase>: <detail>` or `Halcyonic: demonstration <phase>`: the phase and its
  detail and nothing else (never the token, workstream titles, instructions or agent text), because
  on a headset the log (`adb logcat -s Unity`) is the main diagnostic. It logs the status each frame
  ends with, so a phase that begins and ends within one frame, such as `Connecting` when the
  connection is refused at once, has no line of its own. For the demonstration it also logs each
  start from the beginning, with its count, and each end it reaches, never what was answered.
- `CharacterStage` stands the characters on an arc of fixed slots in front of the person and says
  on a banner under their labels, in the ambient strip, whether the state is live ("Connected to
  your computer", "Last known: can't reach your computer. Trying again…"), or, while the demonstration is
  shown, its `DemonstrationLine`, with how many tasks wait for the person while another window has
  focus ([ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)).
  The banner steps aside while a foreground panel or the peek is where it goes (`AmbientCover`):
  a panel says itself whether it is live, and the peek says it of its character. The arc is 2.4 m
  away, beyond the system windows, such as Virtual Display's screens, that open within about 2 m
  ([horizon-os-multitasking.md](../validation/horizon-os-multitasking.md)); the characters' centers
  0.17 m below the eyes, about 4 degrees, so their labels end about 14 degrees down (15 with a mark
  and two lines of title) and the banner and any panel open under them in the comfortable band;
  and 60 degrees between its outermost characters, so every character and its labels stay within
  about 36 degrees of where the person faced, a comfortable field on narrower headsets too. All
  three are serialized settings (`DefaultDistance`, `DefaultHeightFromEyes`), to be judged on the
  headset. Characters and labels scale with their distance from the eyes, so they keep their
  apparent size near or far, above or below (`Stance`); seen from above, as on a desk, the arc
  spreads so neighbours look as far apart as in front of the person (`Spread`), and each label hangs
  a little lower so its body never covers its badge and leans back about its top edge to face the
  eyes (`CharacterLabelView.ViewFrom`): upright, seen from above, its words read at about two thirds
  of their size, under 14 dp as the eyes see it; leaned, at their size, which the stage render holds. A character the lineup moves glides along the arc, swinging out behind the
  others. Everything is looked at and pointed at from the seat; nothing needs the person to stand
  or reach. The arc is placed at the person's head, facing where they face, when
  `InFrontPlacement` says so, fed by `PersonPlacement` with the head's pose, whether it is tracked,
  focus changes, pauses and the runtime's reference space changes
  (`XRInputSubsystem.trackingOriginUpdated`): at the start, after a real pause, when the person
  recenters, and after a head jump no person makes. Reference space changes that move nothing,
  as the bursts that come while system windows take and give back focus, never move it; a tracking
  space that does move takes the stage with it, so it stays where it was around the person. The
  log says which: `placed the stage in front of the person because ...`, `moved the stage with the
  tracking space ...`, or `kept the stage where it stands: ...`. `SurfaceHeight` says how high the
  surface the characters stand on is, for the workspace. An `IStagePlacementSource` on the stage object, such as a room placement
  that found the person's desk, can give it a surface instead: the pose's position is where the
  middle of the lineup stands, the arc curves around the person's side of it at their distance
  when the pose arrived, and every label rests on the surface, with the banner above the highest a
  character reaches, risen included. While that pose is set, only
  the source moves the stage, and recenters leave it. The stage keeps animating and updating while
  the app lacks input focus. It raises `CharacterCreated` and offers `TryGetCharacter` and
  `SlotOf`, so other components add to characters without changing them. The lineup it keeps covers
  only the work of the projects its `Visibility` shows, plus the one work `Request` asks for, and
  it raises `Refreshed` after each update so the rail can count what has no character.
- `CharacterView` draws a `CharacterPresentation` as a bot
  ([ADR 0013](../decisions/0013-characters-are-bots-with-a-living-surface.md)): a body mesh
  generated for its identity's shape, with its eyes, satin flow, cracks, fog and halftone in one
  shader, a halo and a testing ring behind and around it, and its label underneath
  (`CharacterLabelView`, [ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)):
  the state badge on the title plate's top edge, the title on the plate in at most two lines,
  ending in an ellipsis, and the mark on the plate's bottom edge, whose edge is then dashed. The
  plate is at most 10.5 degrees wide, 1.5 less than the 12 between slots, and 96 percent opaque;
  the reason a task needs the person is not on the stage, only in the peek. Beside a badge, a mark
  would reach a neighbour's. The badge shows its state's icon before its word only while it stays
  within those 10.5 degrees (`StateBadgeView.MaxWidth`): Checking its work, Finished this round and
  Waiting for you with a count are longer, so on the stage they show their word alone, and their
  icon in the peek and the workspace. `Body` is the moving visual root, and `LookAtPerson` turns the
  character to the person for the workspace; the label stays where it is. Per-character values go
  through `MaterialPropertyBlock`s, so nothing allocates per frame.
- `Assets/Halcyonic/UI` is the interface's own assembly (`Halcyonic.XR.UI`), which the stage and
  the workspace use and which knows nothing of input: the tokens in Unity's terms (`GlazeTokens`),
  one shader for every flat shape (`Halcyonic/Glaze Surface`: a rounded rectangle with a fill, an
  edge inside its outline, solid or dashed, and a halftone, its properties instanced, shipped
  through a material in `UI/Resources`), text by type role (`GlazeText`: TextMeshPro in Liberation
  Sans, rich text off, escape parsing on, every text through `LabelText.ForTextMeshPro`, strong text
  thickened by its material, never TextMeshPro's bold, which finds no ellipsis in this font; a
  label's mesh is built again only when its words, look or box change, since panels lay every label
  out again every half second), icons (`GlazeIcons`: Material Symbols Rounded, filled, weight 500,
  drawn from the static atlas `UI/Resources/HalcyonicUI/GlazeIcons`, 13 glyphs in 256 by 256
  pixels with no font file and no fallback, each on a label of its own beside the words it goes
  with and never in their text, its em 1.2 degrees in a badge or tag and never under 1; only the
  generated `GlazeIconGlyphs` knows a code point, and `GlazeIconAtlas` builds the atlas from the
  font `apps/xr/tools/glaze_icons.py` makes, [material-symbols.md](../validation/material-symbols.md)),
  and the components without input: `StateBadgeView` (its state's icon, then its word; Starting's
  and Working's icon turns clockwise once every 1.2 seconds; with a `MaxWidth` it shows its word
  alone where the icon would take it past that), `MarkTag` (a flask for Practice, a film for Demo
  and Recorded), `CharacterLabelView`, `PeekCardView` and `StageBanner`. Components are built in
  units of their distance from the eyes and scaled by it, so every size is an angle.
- `Assets/Halcyonic/UI.Interaction` (`Halcyonic.XR.UI.Interaction`) holds what takes input, on the
  Interaction SDK, for the workspace, the entry panel, the rail and the room and pairing controls;
  the stage never references it. `PointerTarget` lives here: a ray, poke and gaze target whose
  `Selected` and `Released` say a press began and ended, let go or cancelled, as hold to talk needs,
  and which the interaction log names as its creator says (`LogAs`: a character by its work's id).
  One that drags (`EnableDrag`) reports the point a press holds as it moves (`Dragged`): a poke's
  on its surface, a ray's at the distance along the ray where it took hold, the ray kept on that
  point by the SDK's `MoveFromTargetProvider`
  ([workspace-interaction.md](../validation/workspace-interaction.md)).
  `GlazeButton` is the interface's button: its role sets its look (Primary in the accent, Secondary,
  Destructive outlined in red and its confirmation solid red, Filter outlined in the accent while on,
  Choice a raised tile edged in the accent while chosen, Attention in the attention colour, only to
  go to what waits for the person), it is 60 dp tall or 48 dp compact, a label in strong body text
  with an icon before it where its action has one (24 dp, or the badge's 1.2 degrees compact, the
  two centred together, never the icon alone) and an optional second line, and it shows each state: at rest, pointed at (lighter, with an
  accent ring), pressed (darker, its plate a little smaller, its words not), unavailable (outlined
  and quiet, taking no press), done (in the success colours) and set aside while the app lacks focus
  (faded). As a row of a panel's list (`ShowRow`) its words are left-aligned: a line over the title,
  the title and its detail wrapping to their lines, a shorter detail where the full one doesn't fit,
  and an end word in the accent; a static row only says something, with no tile and no press. No
  press counts within 0.35 s of it taking a new role or new words, or of becoming available (a new
  icon alone is no new action, so Stop losing its icon as a question arrives still takes the
  press), and a
  hold button's hold starts after 0.3 s and ends let go, dropped or taken away. It is every button
  of the interface; the workspace's first button, `PanelButton`, is gone. A meter (`MeterView`)
  draws a share as a slim bar on its track, 14 degrees wide wherever it stands so meters compare at
  a glance: a picture of words beside it, never in their place. The share it draws is an upper
  bound, as usage left's "at most" is, so its end is open, the last 1.5 degrees in dots, and while
  what it measures is read again it shows only its track. `PanelFrame` draws any `PanelModel` as a
  foreground panel 44 by 26 degrees at 0.46 m: the title, its context and the window controls
  (Move, Reset position, Close) along the top, or Close alone on a panel that stays put without
  tabs, as Usage left, or, on a panel that stays beside its character, the title over its context
  at the left (a notice in their place, two lines tall) and the state badge and marks at the right,
  with a row of tabs under them that ends in Close; the heading, its action at its right, or the
  lead or the banner; the list in one column or two, pressable rows in cells of equal height 12 mm
  apart, lines, with a meter at their right end where they have one, and, in one column, rows that
  only say something as tall as their words, a page at a time, a log's older lines left out rather
  than paged, with the pager in the body's bottom right cell, or a screen's own pager in a row under
  the body with its heading and note at the left; and the bar, Back and the destructive
  action at the left and the primary at a right end at least 14 degrees wide, or the confirm step in
  its place. A heading's action stands at the heading row's right; over a body the screen draws
  itself, or one that starts with lines, the body starts under the heading's words, its first lines
  running short of the action (`Notch`), so the action costs the body no row of its own. Every
  action shows its model's icon beside its words, Move, Reset position and Close
  included; a bar whose actions would not all fit 12 mm apart with their icons shows each word
  alone (`BarIcons`). Since a question's bar gave up its own Hold to talk, no screen the renders
  show needs that; they log any that does.
  The pager and the tabs show words only. Targets keep 12 mm apart; words need less, so a body that starts or ends in words sits
  closer to what is above or below it. The frame records where every control stood on each screen
  without a confirm step and every control it shows while one shows (`Recorded`); the step puts
  Cancel at the right end and Yes at the right-most place in the bar's row 12 mm clear of all of
  them where its question fits beside it, else in a row just above the bar, the question beside it
  and the bar's row keeping only Cancel. While the step pages through what it confirms, the pager
  stands at the top, in the tabs' row or a row under the header, never in Yes's rows. It raises the
  id of what was pressed, with the row's or the tab's key, and splits text into parts of whole lines
  at the list's width (`SplitLines`).
- A player build leaves out shaders that nothing in the build references; the first device build
  rendered characters magenta for that reason. The two character shaders, `Halcyonic/Character
  Body` and `Halcyonic/Soft Shape`, ship through materials in `Assets/Halcyonic/Characters/Resources`,
  which the build always includes, so exactly the variants those materials use are compiled and
  the Always Included Shaders list stays as it is.
- Cost on a Quest 3, estimated
  ([character-rendering.md](../validation/character-rendering.md)): the body shader is one pass
  without keywords, about 160 to 185 arithmetic operations and at most two texture reads per
  pixel in any state, from a 128 by 128 noise texture baked at startup. Six bodies cover about
  240,000 pixels a frame across both eyes. The lookbook computed fractal noise per pixel, about ten
  times the arithmetic. Edges are anti-aliased in the shaders, because the Android quality level
  has no MSAA.
- `FocusGuard` follows input focus through the client core's `FocusPresence`, so Halcyonic can sit
  beside another window (a Mac's Virtual Display, a browser video) in passthrough. When focus goes,
  to that window, the system keyboard or the Meta menu, it hides the assigned hand visuals and
  suspends input at once; work keeps running and updating, since losing focus is not a pause.
  Input stays suspended for half a second after focus returns, so the pinch that brings focus back
  never presses a control. After focus has stayed away for three seconds, large panels (the entry
  panel, an open workspace with its ring and link, the Usage left panel) fold out of the way with
  their content kept (`Folded`), and they come back exactly as they were once input is ready again.
  Focus that flaps, as the Quest's system windows make it, folds nothing. While panels are folded the
  stage's banner, under the characters or beside a window under its lane, also says which panel is
  still open ("Still open: Create a project", `AmbientCover.OpenPanel`) and, beside a window, how
  many more tasks have no character ("2 more tasks not shown here"); it only says, taking no press.
  The app's own system
  keyboard is tracked (`Track` wraps every `TouchScreenKeyboard.Open`), so typing folds nothing and
  drops no confirmation. Every other loss raises `Left`: a confirmation half done is dropped and
  must be given afresh once back (an armed approval, denial, stop or instruction says "You went to
  another window, so nothing was sent. Press it again to confirm."; a review whose final press
  waited goes back to the recap; a first press on a model elsewhere lapses; Forget this computer asks
  again), while the runtime's request itself stays pending. Hold to talk (`HoldToTalk`, lane B)
  stops and discards its recording when input is suspended. While panels are folded, the line
  above the stage also counts what needs the person across every project ("2 need you", in the
  attention color), so it stays findable when the window covers the characters. Whether Unity
  reports each of these as a focus change on the Quest is verified only on the device.
- **The glance (spike, development builds only).** A second client in the same APK, for use
  inside another immersive app: `GlanceActivity` (Java, `apps/xr/Android/glance`), a 2D window with
  the `OVERLAY_LAUNCHER` category, which `GlanceInDevelopmentBuilds` adds to development builds
  only and `BuildReleaseApk` refuses. It cannot share the C# client core, so it reads only the
  snapshot's computed fields (a test holds them to the JSON Schema), sends no command, takes the
  access token only from a mode-600 file in app-private storage, and sends it only on the plain
  socket on which the control plane has just proved it holds it (`GlancePoll`, `GlanceProof` held
  equal to `security.ts`). It shows titles under `LabelText`'s rule
  (`GlanceText`, held equal by one table in both languages). See XR_DEVELOPMENT.md, "The glance on
  a Quest (spike)".
- `DeviceMeasures` logs what a device session needs about the headset, in numbers only: the first
  frame's time after start, each eye's field of view once the headset renders in stereo (read from
  its projection by the client core's `ViewField`), each minute's frames, slowest frame and frames
  below 60 a second (`FrameTally`, which allocates nothing per frame), and pauses with how long the
  app was away. `pnpm quest:session` and `pnpm quest:cold-start` read these lines over adb
  ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md), "Device measures on a Quest").

### The workspace

`Assets/Halcyonic/Workspace` is its own assembly, the one that builds the Meta Interaction SDK's
targets. Three levels of detail show the same work,
all in place ([ADR 0014](../decisions/0014-hand-interaction-through-the-interaction-sdk.md)):

- **Ambient:** the characters as the stage shows them.
- **Peek:** once the person's gaze has rested on a character, or at once while a hand ray (or a
  finger about to poke) points at it, `PeekLabel` fades in a card (`PeekCard`, `PeekCardView`): its
  state badge and marks, the reason in at most two lines, what its marks mean and what opening it
  is for. `PeekChoice` decides which and when: turning the head across the stage peeks nothing,
  one peek shows at a time, a hand pointing wins over the gaze, and while a workspace is open only
  a hand peeks, so reading the workspace never brings up peeks behind it. A small head gaze
  reticle shows where the head points while no workspace is open. The card faces the eyes at
  1.6 m, or nearer than the characters on a desk, and hangs a little more than a degree under the
  character's label and any neighbour's it reaches across, where the banner steps aside for it;
  while a panel is open under the labels, or the characters stand on a surface, it stands over the
  highest the character reaches instead.
- **Open:** a pinch on the ray, a poke, or, while a gaze peek shows and no hand ray or finger is on
  a target, a pinch of either hand at any height (look and pinch) opens `WorkspacePanel` next to
  that character, at touch distance and clear of the other characters, facing the eyes: a
  `PanelFrame` drawing the screens `WorkspaceScreens` builds (ADR 0023). Along the top, the work's
  title over its goal and, at the right, its state badge and marks as its character wears them; a
  notice, such as why nothing was sent or what hold to talk is doing, takes the title's place for
  eight seconds. Under them, the person's questions as tabs in short names, each whole question
  heading its answer: Waiting for you (What do you need from me?), first and in the attention colour,
  only while an approval or an agent's question waits; Doing (What is it doing?); Understand (Help me
  understand, its three questions What changed?, Why? and How was it built? as pills in the
  heading's row) and Checked (What was checked?), with Refresh, and Previous and Next while an
  answer pages, at the heading row's right; and Close at the row's end. The workspace offers no Move or
  Reset position: it stays beside its character. Doing answers in one plain answer, what needs the
  person, else that nothing does and what it did last; what this headset sent and how it went,
  newest first, or "Nothing sent from here yet."; and Recent activity, a log whose newest line is
  last, the agent's words quoted and leaning, its older lines giving way where there is no room.
  Show details beside its heading turns it to How is it running?, the run's details a line each
  (`WorkspaceText.RunDetails`): the agent app and whether it is on the Mac now, the model it was
  given and that where the model runs isn't known here, the folder it works in, and when it started
  with the round it is in; Show the log turns it back, as does choosing a tab. Only what the
  presentation carries: where a model runs would need the server to say. For
  an approval, Waiting for you says what it wants ("It wants to run a command:", or "It wants to use
  {tool}:" for a tool it does not know), the oldest request as the runtime reported it over up to
  three lines, and what each answer does. For a question (and only while no approval waits, as the
  runtime blocks on that first), a step at a time (`QuestionPlace`): two lines of the agent's
  question, in parts when longer and never cut, over a page of its answers, two to a page, each
  label with the agent's description after it, chosen ones marked in words ("Chosen: ") and by
  their edge; Type an answer opens the system keyboard, and in development builds Hold to talk
  beside it drafts the answer from what the Mac heard. The row under them says which prompt shows,
  how it is answered ("Choose one, or type your own.") and how many more questions wait, with the
  pager. A question Halcyonic cannot answer shows why (a secret, or cut to fit) and that the agent
  waits; while it asks for a secret, the bar offers no Tell it or Hold to talk, so nothing invites
  typing or saying it where it would be journaled, and Stop is the way on. The bar holds Stop at its left, outlined in red; Deny, Hold to talk (in development builds,
  beside Tell it, only where the bar has room, and never while the question's answers show, where
  the Hold to talk beside them speaks the answer: one a screen, as the owner chose on 2026-10-02)
  and Tell it; and at its right end the action the work
  leads to: Approve, Send answer, or Tell it while nothing waits. While an answer this headset sent
  may still take effect, an unavailable Sent… stands where Send answer stood, so none races it.
  Pressing an action returns the answer showing to Doing, where its result shows, except Send
  answer, after which the workspace switches to Doing once the answer is sent. A workspace opens on
  Waiting for you when something waits. Close, or pointing at the character and pinching again,
  returns to ambient. `WorkspaceTransition` grows the panel out of the character's body, rings the character
  and links it to the panel while open, and shrinks the panel back on collapse; the character stays
  where the stage put it, and the panel follows it if the stage moves it, as after a recenter.
- **Project rail:** `ProjectRail`, low under the stage and within reach, in two rows of
  `GlazeButton`s ([ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)).
  Above, filters: the projects that matter most now, what waits for the person first, each a pill
  outlined in the accent while its work is on the stage and quiet while hidden, with its counts in
  full where the pill has room ("1 task is waiting for you", `EntryText.ChipDetailInFull`) and in
  their short form where it has not ("1 task waiting", "Hidden · 1 waiting"), in the attention
  colour while something waits, pressed to show or hide that project's work on the stage; and See
  other tasks, while some work has no character, with how much of it waits for the person. Below,
  actions: Connect projects, with how many projects show, and Create a project (Keep creating while
  a draft waits) at the left; Usage left, when its glance offers it, and Settings, compact, at the
  right, since they open sheets rather than act on work, each action with its icon before its words
  (the pills have none). Every button is 60 dp tall (48 compact) and 12 mm from its neighbours. It rests 0.43 m from the eyes, 44.5 degrees below eye level, 24 degrees
  to either side, its rows 40 to 49 degrees down; with a measured field of view too short for that, higher, its corners 1.5 degrees inside the field with the head level but never above 29 degrees down, so it stays a degree under the line above the stage at its tallest beside a window, nor above about 29.8 with text a step larger, as the banner's lines grow (`ProjectRail.Below`, `ProjectRail.HighestBelow`; about 39 degrees on a Quest 3S split evenly); over a desk, 0.3 m ahead and never into the desk,
  about 53 degrees down, under the lineup's labels. It is placed in front of the person when the app
  starts and when the stage moves onto or off a surface, and again by Reset position, and it steps
  out of the way while the entry panel, a workspace, the Usage left panel or Settings is open. Which
  projects show is kept on the device for each journal (`StageVisibility`); hiding a project hides
  its characters only.
- **Settings:** `SettingsSheet`, which the rail's Settings opens, is one foreground panel, 44
  degrees wide like every foreground panel and as tall as its sections, at 0.46 m, placed where the
  entry panel would be, clear of every character and label, with Close and its icon at its top
  right. It holds the controls that change how Halcyonic is arranged rather than act on work, each
  feature in a section of its own (`SettingsSection`): Your room, with the room's line, its switch
  and offer, and under them, as a part with no heading (`SettingsSheet.Continuation`), where the
  characters stand and a button for each other arrangement; in development builds, Your computer,
  with pairing; and Comfort. Sections stand 0.75 degrees apart, as a panel's parts do. With all four
  parts the sheet stands about 33 degrees tall, taller than a panel, so with the characters 2.4 m
  away it opens lower, its center about 34 degrees below eye level and its lower edge about 51
  (`WorkspacePlacement.Lowest`); a release build, without Your computer, has it about 7 degrees
  shorter. Their news no longer comes up in front of the person: a section's line also shows on the
  stage's banner as a short notice for eight seconds (`CharacterStage.ShowNotice`). It closes when
  the entry panel or a workspace opens and folds while another window keeps focus.
- **Comfort:** Settings' Comfort section (`ComfortControls`, the words and choices in the client
  core's `Comfort`) offers three settings, kept on the device and taking effect the moment they
  change and as the stage starts. Make text larger draws reading text a step larger, by 15 percent
  (`Comfort.LargerTextScale`, body text 18 dp to about 21): every foreground panel, the workspace,
  the entry panel, Usage left and Settings, grows whole by it at the same distance
  (`PanelFrame.Zoom`), so its layout, pages and parts stay as designed and it opens lower to stay
  under the titles; on the stage the titles, the peek and the banner, a step wider so its lines
  stay as many (`StageBanner.WidestDegrees`), take it (`GlazeText.Scale`,
  only for labels made with it), while the badges' words and the rail's buttons, which stand where
  space is fixed, keep their size. Keep badges still stops Starting's and Working's icons turning and
  Waiting for you breathing (`StateBadgeView.Still`). One button steps the sounds from on to quieter
  (half their amplitude, 6 dB down), to off and on again, named for the level it steps to
  (`AudioListener.volume`: Halcyonic's cues are the only sounds the app plays). The section's line
  says how all three stand ("Text is the standard size. Badges move, and sounds are on."). Every
  render runs at both text sizes.
- **Usage left:** `UsageLeftGlance` offers its "Usage left" chip to the project rail, which places it
  at its lower row's right end (`ProjectRail.OfferUsageLeft`), and nothing anywhere else: no
  floating control. Pressing it opens a panel on the frame, 44 by 26 degrees at 0.46 m, where the
  entry panel would open (`WorkspaceLayout.PlaceForeground`), clear of every character and its
  label, and reads the control plane once. The rail steps out of the way meanwhile. Each screen is a
  `PanelModel` from `UsageLeftScreens`: "Usage left" and where the readings come from along the top,
  Close in the header; each window's name with a meter at its right and, under it, what was seen,
  in words, the meter drawn from the same rounded-up share; the note that the account is not
  identified, and that some limits couldn't be read, under the list, so every page says it; and
  Refresh on the bar, which reads once more. Four windows show on one page; more page. While a read
  is in flight the rows read before stay, their meters show only their tracks, the bar says
  "Reading usage left…" and Refresh waits; with nothing read yet, the list says it. In the recorded
  demonstration there is nothing to read, so it offers no Refresh. It only lays out again every
  15 s, to drop a window that has reset. An agent name longer than 32 characters ends in an
  ellipsis. It closes by Close or the chip, and when the entry panel or a workspace opens; while
  another window has focus its controls take no input and the chip hides, and once focus stays away
  the panel folds with what it read and comes back as it was. It is not Workstream status and not
  part of starting work. Rendered off the device (`UsageLeftRender`); not yet seen on a Quest.
- **Beside a window:** while the stage stands in front of the person, Settings' Your room says where
  the characters stand, under the room's own line and switch, and offers the other two arrangements
  (`StageArrangement`, `CharacterStage.SetArrangement`, kept on the device), so a session on the
  headset can compare all three: Characters in front (the arc, the default), Make room for a window
  (the arc turned 32 degrees to the person's right) and Either side of a window. Beside a window, at
  most four characters stand either side of a window lane straight ahead (24 degrees to each side
  and 14 above and below eye level), two on each side at 32 degrees: one 4 degrees above eye level,
  one 15 below, which leaves a lower character room to rise without reaching the badge above it.
  Their labels show the badge, the title in one short line on a plate of its own, cut with an
  ellipsis to the widest plate's 10.5 degrees so the tasks can be told apart (the owner's
  coordinator chose this on 2026-10-02 over project initials, which can't tell two tasks of one
  project apart, and the character's hue, which the body above already shows), and the Practice,
  Demo or Recorded mark. The lineup has
  four slots then (`CharacterLineup.WithCapacity`, which keeps what it knew, kept and was asked
  for), and fills its middle two first, so what waits for the person stands in the upper places;
  work with no character waits in the rail's See other tasks. The spec's 28 and 37 degrees at eye
  level assumed badges of icons alone; with words a badge is about 11 degrees wide, and two side by
  side would reach past 45 degrees, so the render's own measure decided two rows, the outermost
  label at about 37 degrees, a little past the 36 of the arc. The peek card stands out from its
  character, never nearer the lane. The line says where they stand and that the window is assumed,
  in one row: "With a window straight ahead, the characters stand either side of it." On a desk the
  room placement decides where the stage stands, and the line says so. The first time focus comes
  back after it stayed away three seconds, with the characters in front, the banner says once
  "Window in the way? Settings can move the characters." Halcyonic cannot see the window, so every
  arrangement reduces overlap and guarantees nothing: with a window of 1.4 by 0.79 m at 1.6 m
  straight ahead, the render (`AmbientRender`) counts it covering 4 of 6 characters' bodies and 4 of
  their labels in front, 2 bodies and 3 labels aside, and none beside a window, whose outermost
  label reaches 37.5 degrees (37.0 before the short titles).
- **Entry panel:** `EntryPanel`, the one foreground panel for entering work. Each screen is a
  `PanelModel` from `EntryScreens`, drawn by a `PanelFrame` 0.46 m from the eyes, 44 by 26 degrees
  (ADR 0023), opened where a foreground panel goes, clear of every character and its label
  (`WorkspaceLayout.PlaceForeground` with the panel's size), so every screen puts the same things in
  the same places: Move (pressed, to the right, the left and back, 28 degrees about the eyes; held,
  the panel follows the hand round the eyes at touch distance, facing them, its center kept in the
  comfortable band and above a desk, `PanelDrag`), Reset position (the panel and the rail in front
  of where the person faces now) and Close at the top right, Move and Reset position unavailable
  while a confirmation is armed (`PanelModel.CanMove`); Back at
  the bar's left; the primary at its right end; the pager at the body's bottom right. An action that
  can't be taken now stays in its place, unavailable, with why beside it. Opening the panel
  collapses an open workspace; a workspace opened while it shows, by a pinch on a character or by
  Open now, hides it, and it comes back as it was, where it was, when that workspace closes. Running
  work keeps updating throughout.
  - **Welcome**, on the first live visit only (a device preference): one line and two cards,
    Connect projects and Create a project, and Not now in Close's place.
  - **Connect projects** lists the projects already set up on the Mac and says so, in two columns,
    each a filter like the rail's chips, edged in the accent while shown and saying Hidden while
    not, with its work in words, what waits in the attention colour; pressing one shows or hides it,
    Add a task beside it starts new work in it while connected, Show all shows every project and
    Done closes. It discovers and attaches nothing. Without a live control plane it says the list is
    last known; during the demonstration, that these are demo projects.
  - **More tasks** lists every task without a character, what needs the person first, with its
    status, project and why it has none (its project is hidden, no room on the stage); pressing one
    brings it to the stage (`WorkspaceDirector.OpenWork`, which asks the stage for it) and opens it
    on the next frame, once it stands in its slot. It stays on the stage after it is collapsed, until
    other work is brought forward or its project is hidden.
  - **Create a project** (or **New task in** a project, from Add a task) asks What would you like to
    make? under its title: Type my idea opens the system keyboard and goes straight to the recap,
    with Hold to talk beside it in development builds and what it is doing on a line under them;
    Help me figure it out asks the fixed questions of `ProjectIdea`, one at a time, the answer given
    before marked Chosen, with typing one's own, skipping the name and Back, and says "Fixed
    questions, not an AI." The recap (Check your project) shows four facts in two columns, each
    changed by pressing it: the project's name and its first task (Change), where its files live
    (Choose or Change) and how it runs (More options), which says where the work's code and
    instructions go ("On your computer") and what that means ("Chosen for you. Change it in More
    options.", "It runs on a remote service: your code and instructions go there."). While the
    needs-you banner shows, each fact takes one line and how it runs loses its note, so all four
    still show at once. More options lists the agent apps that can start work, real ones first and a
    simulated one last, named "Practice run: builds nothing" in a live session (the recorded
    demonstration keeps its names), then the chosen one's own models, read on demand, each with
    where it runs, the Mac's first and the rest under a line saying where they run
    (`ElsewhereDivider`). Nothing is chosen for the person but a model on the Mac, which says so; a
    model that runs elsewhere takes a second press, the first saying that the person's code and
    instructions go there. Start building stays at the bar's right end, unavailable and saying what
    is missing until nothing is; Start over, at the left, is confirmed in place. Start building shows
    the whole request (`NewWorkReview`, Check before starting, "Check every part before you
    start.", since its items are Halcyonic's account of the command, not the command), wrapped at the body's width between words, a part at a time, each item whole in one part
    unless it alone is taller than a part, with the pager in a row under the header. Change takes
    the right end where Start building stood, and Yes, start building stands where no control of
    the recap stood nor any shown since, the pager on every part included, so pressing twice in one
    place never confirms; until the last part it stays there locked, saying what is left to read
    ("Read to part 2 first") until every part has been drawn in the layout showing, and once
    unlocked it takes no press for its settle time. `BuildSequence` then sends the commands and each
    step shows how it went, in words and in its tone; a refusal offers Try again and Change, one
    about a folder the action its code names, and an unknown outcome only Next, to Not sure it
    happened. A project made here is shown on the stage whatever was chosen before. While a
    command's outcome is unknown its id stays in device storage for the journal it was sent to,
    shared with New project and read afresh, and blocks another start, even after a restart, until
    two separate presses in two places clear it after the person checks the work:
    Clear, then Yes, clear, left of Cancel, which takes Clear's place; clearing starts a blank idea,
    never a retry. Each place keeps its own draft in memory while the app runs, a new project's and
    each project's Add a task, with how far its start got, so going back never makes a project
    twice; Close, opening a character, Open now and working elsewhere keep it, and Create a project
    without a project returns to the one last worked on. An app restart loses it. Live work only
    while a real control plane is connected; the recorded demonstration does not stand in for
    creation.
  - **Where its files live** reads the folders the Mac lists (`ControlPlaneApi.GetLocationsAsync`)
    when the person opens it, never on a timer, and lists them in two columns, a page at a time:
    for each place the Mac allows, a new folder there, the place itself (Directly in Projects), and
    each folder in it; a place not on the Mac now shows, unavailable; a listing cut at 200 folders
    says so; and no places at all reads "Your computer doesn't allow any folder yet", pointing to the Mac,
    with Try again. A new folder's name is typed with the system keyboard, offered as one made from
    the project's name, and refused on the headset unless it keeps the host's rule, which the lead
    then says in the failure colour. A folder is needed only when the chosen agent app works in a
    project folder (`RuntimeDescriptor.UsesProjectLocation`); an existing project keeps its own,
    shown on the recap, unless the person chooses another, when the recap says every later task in
    the project uses the new folder and the review shows its folder now and from now on before
    anything is sent. The review shows where the files will live in every case. A refusal about a
    folder offers its next action from the code: Use that folder after `location_exists` (the same
    folder, now as an existing one), and Choose a folder after `location_required`,
    `location_missing`, `location_not_allowed` or a folder the Mac could not make; either returns
    through the review. A project already made is then moved to the new folder before its work is
    started again.
  - **Needs you while creating:** work that comes to need the person while a Create screen shows
    (`AttentionWatch`), hidden projects included, appears in the banner in the lead's place, in the
    attention colour, with Open now (the attention button) and Keep creating; nothing switches by
    itself, and the draft is kept.
  The mock runtime uses its generic `simulated_start` scenario when no runtime options are sent,
  which says no software work was performed, and needs no folder. Codex and OpenCode work in the
  project's folder ([ADR 0020](../decisions/0020-a-project-works-in-one-host-approved-folder.md)),
  which the panel asks for before it offers Start building.

`WorkspaceDirector`, on the stage object, attaches a `CharacterTarget` to the `Body` of each
character the stage creates, so it moves with the body: a sphere of `CharacterView.BodyRadius` for
the ray and the gaze, which neighbours on the arc never share, and a surface just in front of it,
facing the person, for a poke; on a desk its poke hovers only for a finger within 0.06 of its
scale, since typing hands are near. While a character's peek is wanted or it is open it looks at
the person (`CharacterView.LookAtPerson`). Panel buttons are the same `PointerTarget`s (`Assets/Halcyonic/UI.Interaction`), ray and poke, 4 mm in front of the
panel, whose background takes the ray so nothing behind it is pointed at. The director keeps an
`ActivityLog` from live events, reads the open workstream's history through `ControlPlaneApi` when
it opens and after a resynchronization (saying so in the activity caption while it reads, or why
it could not), and sends commands with `CommandSubmissions.SubmitAsync`. While the demonstration is
shown it reads no history, since the recording plays all of it through the session; its answers
read through the acknowledgement's command record, in the demonstration's own words; and Instruct
offers the instructions the demonstration recorded there as presets instead of opening the
keyboard. It raises `Acted` when the person opens a workspace, collapses it, or sends a command
from it, as the command is handed to the session; the sound follows it. It raises `WorkOpened` and
`WorkClosed` however a workspace opens or closes, which the entry panel and the rail follow, and
`OpenWork` opens any workstream by id, asking the stage for its character first. A switch of session arrives as a resynchronization: the open workspace follows its
workstream into the new state, or collapses when the workstream is not there. On a journal change
or a rewind it drops its activity and submissions, which no longer apply. Nothing is peeked, hinted or pressed while
`FocusGuard.InputSuspended`; the system keyboard's result counts anyway, since focus returns only
after the keyboard closes.

**The questions.** The tabs are the frame's compact buttons, 48 dp tall and 12 mm apart, pointed at
and pinched or poked like every other button; the one showing is edged in the accent, and its whole
question heads its answer, so it reads without its color. The sources' names stay in each answer's
provenance line, never on a tab. Look and pinch opens a character, never a tab. `WorkspaceSections`
reads the sections for the tab chosen through the director's `IIntelligenceReader`: the
demonstration's `DemonstrationReads` while it is shown, else `ControlPlaneApi` for the configured
control plane (`ControlPlaneSettings.Api()`, the same instance that reads history, made again when
pairing, forgetting or a new token changes the control plane). `SectionView` draws a page of a
section's answer under its heading in the space the frame leaves, its first lines beside the
heading row's controls on What was checked?, under the pills on Help me understand: the provenance
line, wrapping to two rows, then each line with its tag in a column beside it, on as many rows as
it may take. Before each draw the director measures that space and each line's rows on the labels
(`SectionView.Room`, cached by text and width), so lists keep what fits and count the rest, and only
How was it built? and What was checked? page, from the heading row: the frame lays Previous and Next
there, Refresh left of them, and no row under the body.
Every label shows its text by the one rule, as every workspace label does (under "Words" below), so
it shows exactly what the source wrote, and a claim leans. Viewing a section sounds nothing.

**The whole request.** While approving or denying asks to be confirmed, the whole request the
answer is for shows in place of the tabs and their answers: the tool and what it would do, as the
runtime reported it and never shortened, in parts of as many whole lines as the body holds
(`SplitLines`), with "The whole request, part 1 of 5", Previous and Next in the tabs' row, away from
Yes; Cancel brings the tabs back. Cancel takes the bar's right end, and Yes stands in a row just
above the bar with its question beside it, clear of every control the screen had and every one
shown since. Until the last part has shown, Yes stays in its place, locked, saying what is left to
read ("Read to part 5 first"), and the question reads "Read the whole request above before approving
it."; then "Approve the request above?" and "Yes, approve". Each part turned to starts the
confirmation's 15 seconds again. Denying shows the request too, and can be confirmed at once. The
request under Waiting for you keeps its three lines and ends in an ellipsis when it is longer.
`WorkspaceRender` renders a 1,694 character shell command in five parts at both distances.

**Gaze, and look and pinch.** `GazeHover` adds an Interaction SDK gaze interactor
(`GazeInteractor`, v207) that hovers the `GazeInteractable` on each character. It follows the
scene's `GazeConecaster` (a 3.5 degree cone, 0.2 s dwell) on Meta's `EyeGaze`, whose camera pose
emulation makes it head gaze: a Quest 3 has no eye tracking, and the app does not ask for it.
`GazeHover` is also the interactor's selector, as the SDK's hand gaze interactor pairs gaze with a
pinch: an `IndexPinchSelector` on each hand selects, but only while `PeekChoice` says a gaze peek
shows and no hand ray or finger is on a target, and never while the pinching palm faces the eyes,
the headset's menu gesture; the director opens the character only if it is the one the pinch was
for. A look alone never acts, and where there is no gaze the hand ray still peeks. The interaction
log records peek changes, ray target entry and exit, and look pinch acceptance or refusal with a
reason, using target ids and control instance numbers rather than message text. The adjusted cone,
reticle and pinch timing are verified in code and tests, not yet on a headset
([workspace-interaction.md](../validation/workspace-interaction.md)).

**Seated rays.** The rig's hand rays are `SeatedHandRay`s (`SeatedPointing`): through the index
knuckle from a pivot below the shoulder, so a person seated with a forearm resting points at the
characters without raising a hand to shoulder height, which the headset's own ray needs; a palm
within 20 degrees of facing the floor, resting or typing, has no ray. This smaller palm exclusion
is a device trial, not yet verified to separate pointing from typing on a desk.

**Seated, and within reach, clear of the stage.** The workspace opens at touch distance, 0.46 m from
the eyes (ADR 0023), so a seated person pokes its buttons without leaning or standing; built in
units of that distance, its buttons are 60 dp (48 for the tabs, Close and the pager), 30 mm tall
there. `WorkspaceLayout` hands every character as seen from the eyes, its body and how low and wide
its label reaches, to `WorkspacePlacement` with the frame's size, which opens it clear of all of
them: with the characters 2.4 m away, below their labels, its center about 30 degrees down, clear
of every title and badge; with the characters on a desk half a meter away, above them, its center
about 7 degrees down. The rest of the stage stays in view. `WorkspaceRender` renders both in the
editor and checks it.

**Field of view.** The workspace spans 44 by 26 degrees wherever it opens. Its center stays
within 15 degrees of where the person looks, and between 31 degrees below and 2 degrees above eye
level, so all of it, controls included, sits in the middle of a narrower field of view than the
Quest 3's (as on a Quest 3S), never at an edge; with the characters 2.4 m away, its actions row
is about 20 degrees below them, and its activity lines, at the bottom, may need the head tilted
down a little. With text a step larger it spans about 51 by 30 degrees and its center goes to about
33 degrees below eye level, so its bottom row is about 48 degrees down. The peek is a card of at most five short lines, and the hint three words.

**Hands first.** Everything works with hands alone: pointing, pinching and poking, looking and
pinching, and typing on the system keyboard. The rig supports controllers, but nothing needs one.

**First time.** Until the person first opens a workspace on the device, `OnboardingHint` shows a
thumb and index finger closing into a pinch, with "Look, then pinch", above the first character
that needs them; opening any workspace, by any of the three ways, retires it for good (a player
preference, not state).

**Words.** The app's own words name no brand (a test checks them); names in the data, such as a
runtime's display name, are shown as they arrive, by the one rule for text Halcyonic did not
write. Every workspace label that can show such text, the title, the goal, a notice, what needs
the person, the question, what was sent, the activity, the whole request, the sections, the peek
and the recorded instructions offered, gets it through `GlazeText.SetLiteral`: rich text off, escape
parsing on and `LabelText.ForTextMeshPro`, so it interprets no markup and no escape sequence, and
hides nothing. A line cut short ends in an ellipsis. Agent text in the activity and the sections
leans as a claim, its letters sheared after TextMeshPro lays them out (`GlazeText.Lean`), because no label
may use TextMeshPro's italics or bold: the font has no italic or bold typeface, so TextMeshPro
finds no ellipsis for them and switches the label to cutting text short without one, for good
([workspace-interaction.md](../validation/workspace-interaction.md)).

The workspace's sizes are the tokens' angles (ADR 0023). The other sizes here are designed at a
distance (1.6 m for the peek and the hint) for the
Quest 3's roughly 25 pixels per degree, and scaled by the actual distance, so the angular size stays
the same: body text has an x-height near 0.55 degrees (about 14 pixels), the smallest captions about
10 pixels, buttons are about 3 degrees tall. Text is TextMeshPro with Liberation Sans SDF, never
parsing markup or escapes in text from agents and tools (under "Words" above). Characters the committed static atlas
lacks, such as the minus sign U+2212 in Salidium's change summaries, come from the dynamic fallback
font asset at runtime; the editor renders draw them from the static atlas instead, so they never
write glyphs into the committed fallback. Plates and lines use `Sprites/Default`,
an always-included shader; the TextMeshPro shader reaches the build through the font asset in
`Resources`. The workspace's plate is opaque and draws after everything at the characters'
distance, so nothing behind it shows through: at 95 percent, in the project's linear color space,
white labels behind it read through it.

Instructions are typed on the Quest system keyboard (`TouchScreenKeyboard`, with Require System
Keyboard on in `OculusProjectConfig`, from which Meta's build step adds
`oculus.software.overlay_keyboard` to the manifest). Its prompt says "What to tell it:" and the
work's title, and while it is open the bar says "Type what to tell it, then press Enter." beside
Cancel. Where no keyboard is supported, such as the editor, the workspace offers three preset
instructions in place of the tab's answer, and the recorded demonstration offers the instructions it
recorded.

### The room

`Assets/Halcyonic/Room` is its own assembly, `Halcyonic.XR.Room`, the only one that references
the MR Utility Kit. It puts the stage in the person's real room: passthrough on, the characters on
their desk, and the place kept with a spatial anchor so they are there again the next session
([ADR 0015](../decisions/0015-the-stage-stands-on-the-persons-desk.md),
[mixed-reality-room.md](../validation/mixed-reality-room.md)). `RoomBootstrap` adds
`RoomPlacement` to the stage object after the scene loads, in any scene with Meta's camera rig, so
neither the scene nor the stage refers to it; the stage finds it as its `IStagePlacementSource`.

- **Space.** The real room is the default wherever passthrough works; the virtual space shows when
  the person chooses it, a choice kept on the device, or by itself when passthrough is
  unavailable (unsupported, failed, or not started within 6 seconds). `PassthroughView` turns on
  `OVRManager.isInsightPassthroughEnabled`, adds an `OVRPassthroughLayer` underlay, and clears the
  rig's cameras to transparent black while it runs; the virtual space gives them their background
  back.
- **Room access.** On the first visit to the real room, once the head is tracked, the controls
  show "To stand your agents on your desk, allow access to this room's layout." for 2.5 seconds,
  and then the app asks for the spatial data permission, which the headset explains once and
  confirms. A person who declines is not asked again; the controls offer to ask, once more per
  session, and after a second refusal say that the headset's settings can allow it.
- **Reading the room.** `RoomReader` creates MRUK in code, with loading on startup off, world lock
  off (the rig's tracking space is never moved) and space setup only on request, loads the scene
  model, and takes the room around the eyes. It describes, in world coordinates, every surface
  facing up that is a table (a desk) or other furniture top (storage, a bed, anything else), from
  its plane's outline or its volume's top, and every object with a volume, as its footprint and
  height. Couches are only obstacles: a couch's box top is its backrest.
- **Choosing and keeping the place.** `RoomPlacement` first restores the anchor saved for that
  room, or, without a room, the most recent one, when it localizes within 6 seconds and still
  suits the seat (`StageSurfaces.StillSuits`). Otherwise it chooses a spot
  (`StageSurfaces.Choose`), publishes it at once, so the stage moves there while the anchor is
  made, and keeps it with an `OVRSpatialAnchor` created there and saved on the headset
  (`StageAnchors`), remembered for the room by `PlacementMemory` in a player preference. An anchor
  the headset no longer holds is forgotten; an anchor a room no longer uses is erased when the
  new one is saved or the room is dropped from the memory's eight.
- **The pose.** `Preferred` is the spot on the surface where the middle of the lineup stands, with
  a yaw facing away from the person; the stage draws the arc around the person's side of it.
  Only the anchor's tracked pose moves it afterwards: after the anchor moves more than 2 cm or 2
  degrees and holds still for half a second with input focus, as after a recenter, the pose is
  published again. Reference space events never move it, because with system windows open the
  session's focus can flap many times a second and each flap reports one. An anchor untracked for
  5 seconds of focused time returns the stage in front of the person until it is tracked again; a
  placement without an anchor stays where it was put.
- **Fallbacks.** Without passthrough, room access, a room set up, the person inside a set-up room,
  a surface that fits, or an anchor, the stage stands in front of the person, as it does without
  a placement source, and the line says why. Space setup is offered where it would help (no room,
  outside the rooms, or no surface), started only when the person presses it
  (`OVRScene.RequestSpaceSetup`, which pauses the app until the person finishes or cancels), and
  the room is read again afterwards. A placement without a saved anchor holds for the session.
  Nothing waits on the room before the characters appear.
- **Controls.** `RoomControls` puts the switch ("Show a virtual space" or "Show my room") and the
  offer ("Allow room access" or "Set up this room") in the Your room section of Settings, under its
  line from `RoomStatus.Line`, and under them where the characters stand; they ignore input while
  `FocusGuard.InputSuspended` or the sheet is closed. When the line changes, and when the room is
  about to ask for access, the stage's banner shows it as a notice for eight seconds, instead of the
  controls coming up in front of the person as they used to.
- **Logs.** `Halcyonic: room ...` lines say what was shown, asked, read, chosen, restored, kept,
  lost and erased, and why, with distances and angles, and never an anchor or room id or a
  position.

### Sound

`Assets/Halcyonic/Sound` is its own assembly, `Halcyonic.XR.Sound`. `SoundBootstrap` adds
`StageSound` to the stage object after the scene loads, in any scene with an audio listener, so
neither the scene nor the stage refers to it. The sound is the Glaze direction, which the owner
chose on 2026-09-29 from the soundbook, a single page that played three directions (Glaze, Hum and
Tide) side by side, each synthesized by the page's own code with no recordings, samples or
libraries; the page stays outside the repository. In Glaze each character is a small glazed
ceramic object that events strike softly with a felt mallet, and silence means all is well. Each
cue is named by the words the person reads (`SoundCue`): a state's cue by its state's word, an act's
by its button's.

| Cue | When | What it sounds like | From |
| --- | --- | --- | --- |
| Working | A round starts, or resumes after the person answered | A soft double tap, like the hop | Its character |
| Checking its work | A test run starts | Four muted taps, up and back | Its character |
| Waiting for you | It rises and looks at the person | Two strikes rising; the second rings on | Its character |
| Finished this round | It settles; a finished round proves nothing, so no celebration | The pair falling onto its own note | Its character |
| Couldn't finish | The round failed | A dull, cracked strike, then a lower one | Its character |
| Can't tell yet | Halcyonic cannot see the work right now | A strike whose pitch will not settle | Its character |
| Stopped | Interrupted, as the runtime confirmed | A strike caught by a hand | Its character |
| Last known | The connection dropped: one cue for the whole room | All six notes through a wall, fading | The whole stage |
| Open | The person expands a bot into its workspace | A chord unfolding toward them | The workspace |
| Close | The person closes the workspace back into the bot | The chord folding back into the bot | The workspace |
| Approve | Sent, not yet confirmed | Two notes struck together, open and warm | The workspace |
| Deny | A decision, not an error | A short step down, damped | The workspace |
| Tell it | The person's words were sent | Three light taps | The workspace |
| Send answer | The person's answer to its question was sent | The question's rise answered: a light tap falling onto a warmer note | The workspace |
| Stop | The stop was sent; the bot confirms later | A hand pressed flat on it | The workspace |
| Touch | A button took a press that has no cue of its own | One soft felt tap, Working's first strike on D4 | The button |
| Not now | A button refused a press, being unavailable now | Deny's damped step, a note lower and quieter | The button |

Nothing else sounds: not a start completing, a test run ending, work going on, or a workstream
that has not started. The rules come from the soundbook's research, and `SoundCueSelector` applies
them:

- **Only what the person might act on**, and work starting. Working and running tests are silent
  while they go on; nothing loops and nothing escalates.
- **Sent, not done.** The person's actions sound in front of them as the command is handed to the
  session (`WorkspaceDirector.Acted`), and say only that it was sent. The result arrives later as
  the character's own cue, because an accepted command is not success.
- **Every press answered.** A press any button takes (`GlazeButton.AnyPressed`) sounds Touch from
  the button, unless it sent an act in the same frame, whose own cue answers it; a press on a button
  shown but unavailable now (`GlazeButton.AnyRefused`) sounds Not now. Both start at once, keep no
  gap and hold up no other cue, and neither is ever dropped as a repeat. A press refused because the
  app lacks focus, in the half second after it returns, or within a button's settle time reached
  nothing, so it sounds nothing; the button's look and the reason beside it are the visual twins.
  Both have one render, on D4, the key's own note, since a press belongs to no bot.
- **One at a time.** Cues that arrive together start at least 300 ms apart, the most pressing
  first (needs you, failed, unknown, finished, stopped, tests, work), and before the room's cue.
  The same cue from the same character within ten seconds is dropped, unless the person acted on
  that character in between, so the result of their act is always heard.
- **One key.** Every note comes from D major pentatonic, so overlapping cues agree. Each character
  keeps its own home note (A3, B3, D4, E4, F sharp 4 or A4, from the person's left) for as long as
  it is shown: the note of the slot where it appeared (`CharacterStage.SlotOf`), or the free note
  nearest to it, kept when the lineup moves it, so a bot is recognizable by ear.
- **One room.** A dropped connection is one cue for the whole room, once per loss: when the session
  shown goes from live to waiting to retry, or refused. A session the application stopped, as when
  the headset sleeps, was not lost, and coming back is silent.
- **What changed, never what was redrawn.** A snapshot, a resynchronization (another journal, or a
  switch between the demonstration and the control plane) or a rewind (the demonstration starting
  again) only sets what later changes are compared with. The recorded demonstration otherwise
  sounds exactly as live work does: the same data and flow, and nothing tells the selector the
  difference.
- **Focus.** No cue starts while the app lacks input focus (`FocusGuard.InputSuspended`, or
  `Application.isFocused` false), as while the system menu or a window such as Virtual Display's
  has it; cues scheduled but not yet started are cancelled when focus goes, and nothing missed plays
  later. An instruction typed on the system keyboard is sent as the keyboard closes, just before
  focus returns, so the person's act waits up to 2 s for focus and sounds then. Whether silence is
  right while the person works in Virtual Display is open
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)), so one option is there to try, off by
  default: with the file `waiting-for-you-sound-while-away` in the app's data directory (or
  `needs-you-sound-while-away`, its name before the cue was renamed, still read), a character that
  comes to wait for the person sounds Waiting for you once, at 60% of the usual level, while another
  window has focus; nothing else sounds and nothing repeats (`SoundCueSelector.Observe`'s
  `waitingForYouWhileAway`).
- **Calm.** Low energy: spectral centroids of 347 Hz on average and 649 Hz at most, power-weighted
  as the soundbook's own check measured them; loudness set by importance, from -20 LUFS for
  Waiting for you to -27 for Send answer, -29 for Tell it, -30 for Not now and -32 for Touch;
  peaks at most 0.6 before the room.
- **Never the only signal.** Every cue has a visual twin on the stage, the character's eyes, motion
  and light and its written status ([ADR 0013](../decisions/0013-characters-are-bots-with-a-living-surface.md)),
  so Halcyonic works on mute.

**Rendering.** `GlazeSynthesizer` is the page's synthesis ported line by line: six inharmonic modes
per strike, each with a slowly beating twin; a felt contact thud; a hand's damping; soft pink noise
through gliding band-pass filters; a seeded generator (mulberry32), so a render repeats sample for
sample on a platform (another platform's math library may differ in the last bits). Each cue's
layers mix to mono and are set to the cue's loudness target (K-weighting and the loudest 400 ms),
with a short fade at the end, then play in the page's short room: a 1.3 s impulse response of early
reflections and a tail that darkens as it fades, whose first channel each cue's send is convolved
with (FFT overlap-add), cut where it stays 60 dB below its peak. The page's per-layer pans are
left out, because in the headset a cue sounds from a place. On the development Mac the port matches
the page's own output bit for bit, at 48 kHz in all but one of 3,950,400 samples, which differs by
1e-16 of the peak, and at 44.1 kHz in all ([sound-rendering.md](../validation/sound-rendering.md)).
`StageSound` renders all 87 clips (14 cues for each of the 6 notes, and Last known, Touch and Not
now once) at the output sample rate on a worker thread at startup, and makes them audio clips on the main thread,
four a frame. Nothing is synthesized while sound plays; `OnAudioFilterRead` is not used.

**Voices.** Each character has two audio sources on its `Body`, so a cue can start while the last
one still rings: fully spatial (`spatialBlend` 1), no Doppler, and logarithmic rolloff at full level
within 2.5 m. The stage stands within that both on a desk, about 0.55 m away
([ADR 0015](../decisions/0015-the-stage-stands-on-the-persons-desk.md)), and in the virtual space,
2.4 m away, so where it stands is heard in each cue's direction and not in its loudness, as the
characters keep their apparent size. The person's actions sound 0.6 m in front of their head, where
the workspace opens; Last known sounds from the middle of the characters, spread over the arc's 60
degrees. Unity's built-in panning places them, with no spatializer plugin or package: the page
panned each bot by the sine of its angle on the arc, which positions reproduce. Cues are scheduled
on the audio clock (`AudioSource.PlayScheduled`), keeping their gap there too, so once the clips
exist nothing runs per frame; the selector keeps its time on the real-time clock, because the audio
clock can stand still while the output is suspended.
The level is a serialized setting, 0.5 by default, the soundbook's starting volume; the headset's
own volume applies on top. `Halcyonic: sound ...` log lines say when the clips are ready and how
long they took, and each cue played, its place and note, never a workstream.

**Cost.** Rendering every clip took 1.2 to 2.0 s of one core in .NET 10 and 1.9 to 2.0 s in the
Unity editor's Mono on the development Mac, while other builds loaded it; making the audio clips
took 2 to 7 ms; the clips hold 17.7 MiB of 32-bit samples, kept for the session. On a Quest 3
neither is measured; the `sound ready` log line reports both there.

The research the soundbook cites:

- Calm technology moves between the edge of attention and its center, and back:
  [Weiser and Brown, 1995](https://calmtech.com/papers/designing-calm-technology).
- Background cues must avoid the alarm pattern (sharp attacks, loudness, energy in the voice band);
  a breathing sound was found obnoxious:
  [Audio Aura, CHI 1998](https://ecl.cc.gatech.edu/sites/default/files/publications/C.14-Mynatt-CHI-1998.pdf).
- A continuous natural soundscape improved peripheral monitoring without hurting the main task:
  [Hildebrandt, Hermann and Rinderle-Ma, 2016](https://eprints.cs.univie.ac.at/4755);
  [Peep, 2000](https://www.usenix.org/conference/lisa-2000/peep-network-auralizer-monitoring-your-network-sound).
- Urgency rises with pitch, speed and irregular harmonics, so calm cues stay low, slow and pure:
  [Edworthy, Loxley and Dennis, 1991](https://researchportal.plymouth.ac.uk/en/publications/improving-auditory-warning-design-relationship-between-warning-so/).
- Sharpness, energy high in the spectrum, predicted annoyance best across 129 sounds:
  [Schell-Majoor et al., 2025](https://journals.publisso.de/en/journals/zaud/volume7/zaud000073).
- Abstract melodies are hard to learn: the medical alarm standard replaced them with auditory icons
  in 2020, and icons beat earcons in a longitudinal study:
  [Edworthy et al.](https://pearl.plymouth.ac.uk/handle/10026.1/10696);
  [Garzonis et al., CHI 2009](https://researchportal.bath.ac.uk/en/publications/auditory-icon-and-earcon-mobile-service-notifications-intuitivene/).
- Concurrent cues are told apart better when their onsets are staggered by 300 ms and when they
  come from different places: [McGookin and Brewster, 2003 to 2004](https://theses.gla.ac.uk/14).
- Alarm fatigue: with hundreds of alarm signals per patient a day, people stop responding to them:
  [Joint Commission, Sentinel Event Alert 50, 2013](https://jointcommission.org/sea_issue_50).
- Meta asks for spatial, comfortable, uncluttered audio, apps playable without sound, and feedback
  that combines sight, sound and touch: [Audio design](https://developers.meta.com/horizon/design/audio/);
  [Accessibility VRCs](https://developers.meta.com/horizon/blog/introducing-the-accessibility-vrcs/);
  [Game Accessibility Guidelines](https://gameaccessibilityguidelines.com/ensure-no-essential-information-is-conveyed-by-sounds-alone/).

Not verified on a headset: how the cues sound through the Quest 3's speakers and whether their
levels suit hours beside the characters; whether the built-in panning places characters 0.55 m
away convincingly, and whether a spread widens a mono clip as it does a stereo one; that
`PlayScheduled` keeps its timing on Android, including after the output was idle; that the system
menu and Virtual Display reach the app as focus changes that stop cues; and the render time and
memory on a Quest 3. The checks are in [XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md).

### Pairing

`Assets/Halcyonic/Pairing` is its own assembly, `Halcyonic.XR.Pairing`, added only to development
builds and the editor: `PairingBootstrap` adds `PairingPanel` to the stage object at runtime, as
the room's and the sound's bootstraps do, so neither the scene nor the stage refers to it. A
release build, such as the one judges run, offers no pairing
([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)).

- **The panel** is a button and a line in the Your computer section of Settings (ADR 0023); the line
  shows while something is in progress or for ten seconds after it changed, and the stage's banner
  shows each line as a notice too, for a result reached with the sheet closed. Moving into Settings
  changed only where it shows and how it looks: the code entry, the confirmation and every message
  are as they were. "Forget this computer" and its confirmation are outlined in red. The button ignores
  input while `FocusGuard.InputSuspended` or the sheet is closed.
- **Pairing.** "Pair with a computer" opens the system keyboard for the Mac's address, as `pnpm pair`
  prints it (the last one typed is offered, and the port may be left out), then the number pad for
  the code. The exchange runs in the background; its answer is shown in words, with the attempts
  left after a wrong code, and a refusal the app does not know, in the words of whatever answered
  at that address, shows by the one rule for text Halcyonic did not write. On success the pairing
  is saved through
  `ControlPlaneSettings.PairingStore` and the `ControlPlaneConnection` is disabled and enabled
  again, so it connects to the paired control plane as at startup.
- **Forgetting.** Once paired, the button reads "Forget this computer", and a second, deliberate press
  within six seconds ("Yes, forget this computer") asks the Mac to revoke this headset, deletes the
  pairing, and connects again as before pairing. If the Mac cannot be reached, the line says to
  revoke the headset there.
- **Storage.** `ControlPlaneSettings` keeps the pairing in `halcyonic-pairing.json` and the access
  token in `access-token`, both in app-internal storage on Android (`Context.getFilesDir()`, which no
  other app can read and which `adb` reaches only through `run-as` on a debuggable build), and in the
  persistent data directory elsewhere. A development build that finds a token an earlier build kept
  on shared storage moves it in at startup, before it reads a pairing, the new file made mode 600
  where it can be before the token is written, and removes the shared copy, or logs that a copy
  remains when it may not; a release build removes one it finds there without reading it.
  `AccessTokenFile` in the client core decides; `AndroidTokenStorage` reaches the files through
  `android.system.Os`, so only a regular file with one name holding a token in its own form is
  taken, and no link is followed or pipe waited on. A pairing takes
  the place of the access token; forgetting it returns to the token.
- **Logs.** `Halcyonic: pairing with the control plane at <address>`, `paired; connecting over the
  network`, `pairing refused: <code>` and whether forgetting revoked the headset on the Mac, never
  the code or the credential. The connection logs the paired address it connects to.

### Scene

Stage.unity carries Meta's comprehensive interaction rig, added the way the Interaction SDK's
"Interactions Rig" building block adds it, by `StageSetup` (**Halcyonic > Set Up Stage
Interaction**, also runnable in batch mode). It brings the hand data, hand visuals, and the ray and
poke interactors the targets answer to. The Hand Tracking building block keeps tracking but no
longer draws, as Meta's wizard does, and the rig's locomotion (hand microgestures, controller
sticks, and the locomotor with its tunneling) is deactivated, because the stage is stationary.
`FocusGuard` hides the rig's hands and controllers and deactivates its interactors. `StageSetup`
also adds the SDK's gaze as its gaze quick action builds it, Meta's eye gaze prefab beside the
rig's HMD with a `GazeConecaster`, and turns on its camera pose emulation; and it seats the hand
rays: a `SeatedHandRay` on each hand ray's pointer pose object, in the SDK's `HandPointerPose`'s
place in the ray's active state group, with the SDK's pointer pose disabled.

`WorkspaceRender` (**Halcyonic > Render the Workspace Over the Stage**, also runnable in batch mode)
renders the workspace open over the stage with the characters 2.4 m away and on a desk, every screen
drawn from the client core's models on the frame at touch distance, saves each with a close-up at a
Quest 3's 25 pixels per degree and a view of the whole panel in `apps/xr/Builds/WorkspaceRenders`,
and fails if a pixel of the workspace changes with the stage drawn behind it, a character's body or
label is behind it, or its center leaves the band. The workspace opens clear of every label, so a
bright backdrop drawn with the stage stands behind it for the checks that nothing shows through,
compared inside the panel's outline, a trapezoid since it leans back to face the eyes. On every
screen it fails if a target is under 60 dp (48 for the tabs, Close and the pager), two targets are
closer than 12 mm, a button runs past the panel's edge, a word is under the caption's size, any of
Halcyonic's own words is cut short, the list pages, or a line runs below the body. It fails unless
the four tabs show whole, Waiting for you first and chosen in the attention colour, 12 mm from
Close. It shows the Understanding and Evaluation sections with the bundled demonstration's answers
for the directed work, at its approval and after approving, and fails if a line of a section does
not fit under its heading, beside Refresh, or a part's own statement is cut short. It shows Doing
with a log of the agent's words and fails unless the newest line shows last under its caption, the
agent's words lean and keep their ellipsis, and nothing pages, also while Stop asks to be confirmed.
It shows the agent's questions: one with an answer chosen, which must say so in words; its typed
answer with Hold to talk beside it and none in the bar; three questions waiting, whose note must say more wait; the
second prompt reached with Next; a question longer than two lines, which must show in parts whose
text together is the question, never cut, and count as read only once its last part has shown; twenty
answers, eleven steps; a long label, which must show whole; a secret question, with no answers to
choose; and one whose answer was sent and may still take effect, where only an unavailable Sent…
may stand at the bar's right end. On real labels it checks that source text shows as written: 61
characters of backslash sequences, markup and control characters shown by code show as 61 once
escaped, where TextMeshPro showed 22 of them unescaped; and that a quote cut short ends in an
ellipsis. It shows a 1,694 character shell command under Waiting for you, presses Approve, and steps
through every part with Next, and fails unless the parts together hold every character, none is cut,
the tabs give way to the pager at the top, and "Yes, approve" is locked until the last part; the
demonstration's short request must fit one part and be confirmable at once, and Deny needs no
reading. Every confirmation, reached by showing its screen and then pressing (Approve, Deny, Stop
beside Hold to talk and without it, a spoken instruction, and answers a policy reviews), fails unless
Yes stands 12 mm clear of every control shown before its confirm step and since, never in the
pager's row, its question whole, and neither Yes nor Cancel shows the microphone, nor any other
button of the panel that is not held; the log names
every screen whose bar had no room for its actions' icons. Hold to talk must show beside Stop and Tell it and be left out where
Deny and Tell it fill the bar. Last, it puts hostile text on every
label that shows text from outside, through the code that shows it: markup, backslash sequences,
an end of text character, a carriage return and a line break, a bidirectional override, a zero
width space, a tag character and half a surrogate pair. It fails if any label interprets markup or
parses no escapes, uses italics or bold, shows text that did not go through the rule exactly once,
lays out other characters than that text or cuts it short without an ellipsis, or draws any of it
from the icon atlas; if a claim does not lean, or leans and loses its ellipsis; and it checks a
character's label and the peek card the same way, their TextMeshPro labels by the same rule. An
icon is no text, so it is checked on its own: one glyph of the icon set, drawn from the atlas, an
em of at least a degree as seen from the eyes, and words of its own beside it, level with it and
no more than an em away; every render that checks labels this way does the same.

`GlazeRender` (**Halcyonic > Render Every Component**, also runnable in batch mode) renders every
component in every state on a panel at touch distance, 0.46 m, each facing the eyes: every state's
badge, a count and a last known one, the marks, every role of button at rest, pointed at and pressed,
an unavailable, a done and a compact button and one with a second line, a tab in the attention
colour showing, and the banner's kinds,
saved at a Quest 3's 25 pixels per degree in `apps/xr/Builds/GlazeRenders`. It fails if a word is
under the caption's size, a button under 60 dp (48 compact), anything of ours cut short, a badge
missing its word, or a button's label under 4.5:1 on its own fill as drawn, as an off filter's grey
word was on its lighter fill when pointed at, before it brightened. Its marks are the client core's
(`StateLanguage.MarksOf`), and a third page (`gallery-actions.png`) shows every action's icon on a
button beside its words. It fails if the icon atlas lacks a glyph for an icon the client core
names, is not static, keeps its font file, falls back to another font or is a fallback of one; if
two states or marks, or two actions, share a glyph (an action may share a state's, as Stop shares
Stopped's); if an icon shows on no badge, mark or button; if an icon stands alone or under a
degree; if a label of words draws from the icon atlas; or if a button whose icon alone changes
waits to settle again, dropping a press, as Stop did when a question arrived.

`StageRender` (**Halcyonic > Render Every State on the Stage**, also runnable in batch mode) renders
every state of a task on a character at the stage's default distance and height, practice, demo and
last known work among them, with the banner under the labels and the peek under one, then the
characters on a desk with the peek over one, and saves them, with close-ups of the middle labels and
the peek at a Quest 3's 25 pixels per degree, in `apps/xr/Builds/StageRenders`. It checks the rules
of [ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)
through `GlazeChecks` (`Assets/Halcyonic/UI/Editor`), which every render can use: labels, bodies,
the banner and the peek at least a degree apart as seen from the eyes, measured from each mesh's own
corners and each body across the line of sight, and a label at least 0.3 degrees from its own body;
every word at least the caption's 0.94 degrees, at the distance of the plane it lies on as Meta's dp
are; every plate at least 96 percent
opaque; every badge showing its whole word; a plate's pixel in its token's color; and a title's
strokes at 7:1 or more on its plate as drawn. It fails if the banner still shows while the peek is
where it goes, or does not come back when the peek leaves; or unless every label's badge has the
plate's 10.5 degrees as its room and shows its icon exactly when the badge with it fits there, and
it logs each badge's width and whether its icon shows. With their icons, badges run from 6.2
degrees (Starting) to 10.7 (Checking its work), 11.0 (Waiting for you with a count) and 11.3
(Finished this round), which is why those three show their word alone.

`MeasureRender` (**Halcyonic > Measure the Interface**, also runnable in batch mode) measures what
the interface costs a Quest 3, off the device, for the stage with the rail, the stage beside a
window while another window has focus, the entry panel, a workspace, Usage left and Settings: the
draw calls at most, before batching, the text labels, vertices and triangles; for each panel, the
text meshes built again, the editor's time and the bytes allocated when the same screen is shown
again; and the bytes every per-frame method allocates, by Unity's own count of the managed heap
("GC Allocated In Frame"). It fails if a panel could draw more than 60 times or everything showing
more than 220, as ADR 0023 budgets, if showing an unchanged screen builds a text mesh again, if a
frame allocates, or if a line whose words stay the same does not lean, or stand upright, once drawn
after it becomes the agent's words or Halcyonic's: the lean alone builds its mesh again. Its results and what only the headset tells are in
[quest-3-performance.md](../validation/quest-3-performance.md). The client core's per-frame code
(`FocusPresence`, `PeekChoice`, `InFrontPlacement`, a section's `IntelligenceFeed`) is held to
allocate nothing by `PerFrameTests`.

`EntryRender` (**Halcyonic > Render the Entry Panel Over the Stage**, also runnable in batch mode)
renders the project rail and every screen of the entry panel over the same two stages, and saves
each in `apps/xr/Builds/EntryRenders`, with a close-up at a Quest 3's 25 pixels per degree and a
view of the whole panel. On every screen it fails if the panel lets anything behind it show through
(compared inside its own outline), covers a character's body, comes within a degree of a character's
body or label as the eyes see them, or leaves the comfortable band; if a target is under 60 dp (48
for the window controls, the banner's and the pager's), two targets are closer than 12 mm, a button
runs past the panel's edge, or a word is under the caption's size; if any of Halcyonic's own words
is cut short (only text from outside, which the model says is data, may end in an ellipsis); unless
Close, the bar's right end, Back and the pager stand in the same place on every screen (a
confirmation's pager, which stands at the top, apart). Every confirmation, reached by showing its
screen and then pressing (Start building on the recap, then Next through every part; Start over;
the second press that clears a start that may have run), fails unless Yes stands 12 mm clear of
every control shown before its confirm step and since, never in the pager's row, with no
microphone on Yes or Cancel or on any button that is not held, and, while it pages, its pager
stands above the body. It holds Move on the recap and drags it 10
degrees right and 4 up, and fails unless the panel follows by as much, at touch distance, facing
the eyes, and unless, while Start over asks to be confirmed, Move and Reset position take no press
and a drag moves nothing. It fails if the rail
reaches more than 24 degrees from its middle, runs past its ends, puts two buttons closer than
12 mm, has a target under 60 dp (48 compact) or a word under the caption's size, or comes within a
degree of a character's body or label. It opens Settings from the rail, with the sections the room
and pairing fill, and fails if the sheet comes within a degree of a character or label, leaves the
comfortable band, has a target or word too small, cuts our words short, or shows a refusal from
outside otherwise than as written. It reaches the whole request from the recap with Start building
and steps through it with Next, for the longest name and task in one unbroken word, a task made only
of characters shown as code points, a task with no place to break, and a long task in words, saving
the first and last part of each, and fails unless every character of every item shows exactly once
across the parts, inside the space above the pager, no item overlaps another, a line breaks inside a
word only where the word is longer than a line, the pager stands above the body, and Yes, start
building is unlocked on the last part only, clear of every control shown before and since. It renders Where its files live with a listing cut short and a place no longer on the Mac, and
with no places; a recap and review that move a project to a new folder; and a start refused because
the new folder's name is taken, offering Use that folder. With hostile project, folder and place
names and titles, every label must show them by the one rule.

The room placement is not in the scene: `RoomBootstrap` adds it at runtime, and it creates MRUK,
the passthrough layer, the stage's anchor and its controls under an object of its own. Nor is the
sound: `SoundBootstrap` adds `StageSound`, which puts its sources on the characters' bodies and
under the stage object.

The project compiles in Unity and runs on a Meta Quest 3 against a live control plane
([quest-3-device.md](../validation/quest-3-device.md)), where opening a workspace by pointing and
pinching, approving and the runtime-confirmed result are verified; the calm peek, look and pinch,
the seated rays, the workspace clear of the stage, the stage's placement with system windows open,
the room placement, the sound, the Understanding and Evaluation sections, the whole request in
parts and text from outside shown by the one rule compile and build but are not verified on a
headset yet. The Meta XR Simulator fails every frame
on the development Mac ([meta-xr-platform.md](../validation/meta-xr-platform.md)). `QuestBuild`,
in an editor-only assembly, builds a development APK, and a release APK that leaves Meta's
development tools out, except the Immersive Debugger's runtime, disabled, which MRUK needs
([horizon-store-release.md](../validation/horizon-store-release.md)). Project settings, `.meta`
files and the lock file are committed ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md)).

### Voice

Hold to talk, in development builds only (ADR 0021), as another way to give an idea, a task or an
instruction; typing always stays:

- **`GlazeButton`'s hold mode** (`Holds`): a press held for 0.3 s starts the hold (`HoldStarted`);
  letting go ends it (`HoldEnded(true)`); the hand leaving the button, the button no longer
  accepting (as when input is suspended) or the button going away drops it (`HoldEnded(false)`).
  A press let go sooner is a tap, which says to hold while speaking. `PointerTarget.Released`
  reports the end of a press.
- **`HoldToTalk`** owns the microphone while a hold lasts, one clip at a time, at most 30
  seconds, then sends it through `SpeechClip` and `TranscribeAsync`. The microphone permission is
  asked on the first hold, which records nothing; the person holds again once they allow it.
  Capture stops and the clip is discarded, never sent, the frame input is suspended (the rule
  agreed with the focus work), and a hold that starts while suspended is ignored.
- **Where it shows.** On Create a project's start screen, beside Type my idea, with what it is
  doing on a line across the panel under them (`PanelFrame` raises its hold by the action's id); a
  heard idea or task becomes the recap's first task, which says
  "This is what your computer heard. Check it before you go on.", and nothing is sent until Start building and
  the review. In an open workspace, at the end of the action row when the work takes
  instructions and the row has room; a heard instruction always asks "Your computer heard: ... Send it?".
  Never in the recorded demonstration, and never for approve, deny, stop or any
  confirmation.
- **Release builds carry none of it.** The microphone code compiles only with `DEVELOPMENT_BUILD`
  or in the editor, so a release player never uses `Microphone`, which is what makes Unity add
  `RECORD_AUDIO`; `QuestBuild.BuildReleaseApk` also deletes and fails a release APK whose manifest
  asks for it.

The editor's renders show the start screen with hold to talk and its longest words, and the
workspace's action row with it beside running work's actions and left out where approve, deny, stop
and instruct fill the row. Not checked on a headset: the Quest microphone's rate and level, the
permission prompt, and whether a ray that leaves the button while pinching ends the hold.

## Not built yet

Code, diffs, tests and output in the workspace; the Understanding section's full lists (every
changed file, every review item, the explanation's diagrams), which it summarizes in seven lines;
reading a real execution's understanding and evaluation end to end, which waits for a real Claude
Code or Codex run ([understanding-and-evaluation.md](../validation/understanding-and-evaluation.md));
choosing a folder deeper than one level inside a place the Mac allows; the companion and kept drafts on the headset's panel (the control plane and the client core have
both; the panel's screens wait for the headset's redesign, so Help me figure it out still asks the
fixed questions and a restart still loses a draft); voice for the companion's answers, the fixed
questions' own answers, a folder's name and the recap's Change, and voice in release builds; discovering or attaching work Halcyonic did not
start; the soundbook's softer repeat of "Waiting for you" once nobody has
looked at the character for two minutes, and a volume and mute for sound in the headset; finding
the Mac without typing its address (mDNS), changing a paired Mac's address without pairing again,
and keeping the credential under an Android Keystore key. On a Quest, the control plane is
reachable over USB with `adb reverse tcp:47800 tcp:47800` and the access token written to it, or over Wi-Fi once
paired ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md)).
