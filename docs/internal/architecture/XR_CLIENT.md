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
  noticed only by TCP. A control plane that answers the connection with 401 has refused the
  credential, so the session stops trying and its status says `AccessRefused`, with what to do in
  `ConnectionText`'s words: for the access token (a development build, as over USB) "Your Mac
  refused this headset's access token: it doesn't match the Mac's. Put the Mac's current access
  token on the headset, then restart the app."; for a pairing, that the Mac no longer accepts it and
  to forget the Mac and pair again. `ClientWebSocket` reports only that it could not connect, so
  after a failed connect `ClientWebSocketTransport` asks the control plane's REST API once with the
  same token to tell a 401 from a Mac that does not answer; the pinned transport reads the status
  itself. A Mac that does not answer reads "Can't reach your Mac; trying again", with the technical
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
  lays them out: the person's questions (What is it doing?, Help me understand, What was
  checked?, and What do you need from me? only while an approval waits, each in two lines for its
  tab, never shortened), the goal, one plain answer under it (what needs the person, else "Nothing
  is waiting for you." and the latest activity), the answer to What do you need from me? (`NeedAnswer`:
  the oldest request as the runtime reported it and what each answer does), the status with its
  qualifiers ("simulated", "recorded", "last known"), the
  execution and its runtime, what needs the person, activity lines with the local time and agent
  text quoted as "Agent says: “…”", action labels, a confirmation question that names exactly what
  would be sent (for approving or denying, "Approve the request below?" over the whole request,
  `Request`: the tool and what it would do, never shortened), and why no action is offered. Text
  from outside in any of them shows by `LabelText`'s rule, and a cut never splits a character in
  two. `Describe` writes one activity entry in one line, for the peek too.
- **`LabelText`** is the one rule for showing text Halcyonic did not write: workstream titles and
  objectives, anything an agent or a tool wrote (messages, approval requests, activity), refusals
  and failures, setup problems with exception text, what the understanding and evaluation sources
  say, and names from runtimes. `Plain` makes it one line of exactly what it says: a line break, a
  tab or any other white space than the space collapses with the whitespace around it into one
  space, and spaces stay as written; every control and format character, every default ignorable
  code point (a zero width space, a bidirectional override, a variation selector, a tag character)
  and every half of a surrogate pair shows as its code point, as ‹U+202E›; everything else,
  markup and backslashes included, shows as it is. Which characters show by code is a fixed table,
  Unicode 17.0's, so the headset and the tests decide alike whatever Unicode version their runtime
  knows. `ForTextMeshPro` also doubles every backslash, for a TextMeshPro label with rich text off
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
  reads "Your Mac heard: ... Send it?" with the text, so a mishearing is never sent unread.
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
  the characters stand on. It keeps 1.5 degrees from bodies, which move, and 1.2 from labels, which
  do not, measured at the panel's corners: a flat panel's corners are farther than its edges'
  middles, so below eye level they look higher (`CornerElevation`). Where neither side clears, it
  moves the least into the band and may cover a character.
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
  a current value, never an allowance. A window past its reset, by the device's clock, is not
  shown. Under the readings, a note names the source ("From Seorak, as the provider reported", or
  "Simulated, not from Seorak") and says the account is not identified, so no reading is tied to
  the selected runtime, account or model. Every setup problem, a missing credential, one without
  the scope, a restricted one or no source at all, reads "Usage left isn't set up on your Mac.";
  the scope details stay in the Mac runbook. When the source could read only some limits, the note
  starts "Some limits couldn't be read this time." and no missing window is inferred. No reading reads "No usage reading yet.", never 0%.
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
  than a page, starting it on a page of its own. The final action appears only on the last page,
  after the person has advanced through every preceding page. **`NewWorkSubmission`** looks up the command id
  in projected state before interpreting an acknowledgement: a completed event still counts when
  its acknowledgement is lost. An unknown acknowledgement keeps the request unresolved until a
  terminal record arrives or the person deliberately clears it after checking the workstreams.
- **`BuildSequence`** is Start building's command chain, moved out of the panel so it is tested:
  `project.create` for a new project, with the folder chosen, or `project.set_location` first for
  an existing project that is to work in another folder (which completes with no result), then
  `workstream.create` and `execution.start`, each sent only once the control plane recorded the one
  before it completed, a projected record winning over a lost acknowledgement (`NewWorkSubmission`).
  A refusal, a failure known to have had no effect or a command never sent stops it, keeping the
  refusal's or failure's code, and can be sent again as a new command built from the draft as it is
  now, reusing the project and workstream already made (`Retry`); given a newly chosen folder, a
  project that exists is bound to it first, as after `location_required` or `location_missing`. An
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
  team. First, show one page that says what it is."). Typed answers go in as typed. No model is
  involved and nothing presents it as an assistant. It is kept in memory only.
- **`AttentionWatch`** notices work that comes to need the person while they create, hidden
  projects included, so the entry panel can offer Open now or Keep creating; what already needed
  them is not offered again, and it never switches by itself.
- **`EntryText`** writes every word of the rail and the entry panel: plain verbs, statuses in
  words, a model's serving place in terms of where the person's code and instructions go, and
  nothing that claims discovery (Connect lists the projects already set up on the Mac). The words
  follow the glossary: a task, an agent app, your Mac, never a workstream, a runtime or the control
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
  the bar's place, whose Yes stands where no control of the screen stood a moment before. Text from
  outside comes in as `LabelText.Plain` shows it and says it is data, which alone may end in an
  ellipsis.
- **`EntryScreens`** builds every entry screen as a `PanelModel` from the draft, the overview and
  the Mac's state, so the Unity layer only draws it and acts on the id a press raises, and decides
  what each offers: Start building at the right end, unavailable with what is missing
  (`StartProblem`, the recap's checks in their order); the whole request (`ReviewOf`, given names as
  they are, so the review spells each once); Yes, start building locked until the last part; the
  next action a step's outcome allows; and the two presses that clear a start that may have run.
- **`WorkspaceScreens`** builds every screen of the open workspace as a `PanelModel` from its
  presentation, the steering and what the workspace is in the middle of (`WorkspaceScreen`: the
  tab chosen, a notice, the instructions offered where no keyboard is, and the agent's question or
  the whole request as the panel split them at its width): the header, the tabs, each tab's answer
  and the bar, by the rules under "The workspace" below. **`QuestionPlace`** keeps where the person
  is in an agent's question, a step at a time across its prompts: each part of a prompt's text, as
  the panel measured it, then each further page of its answers; a prompt counts as shown whole once
  the last part of its text has shown, and another draft starts from its first step.
- **Understanding and Evaluation**, the workspace's two sections, named for the capabilities and
  never for the products. **`IntelligenceFeed`** decides, on the main thread, when a section reads:
  when it is shown for an execution it holds no answer about, and when the person refreshes; the
  Understanding section also reads again once the execution changed and two seconds have passed,
  while the Evaluation section never reads by itself again. The last answer stays while a new one is
  read, and a failed read says why, "did not answer in time" for a timeout.
  **`UnderstandingPresenter`** and **`EvaluationPresenter`** write every word a section shows, as a
  provenance line and lines with a tag. The provenance line is the only place that names Salidium
  or Seorak ("From Salidium 0.6.0, 2 minutes ago", "From Seorak, read just now"); an answer from a
  synthetic source says "Simulated, not from Salidium" or "Simulated, not from Seorak" instead
  ([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md)),
  and a recorded answer says when it was recorded, with its relative times and staleness judged as
  of then. Where there is no answer, the provenance line says why in words: "No understanding yet",
  "Understanding unavailable", "unreadable" or "not allowed", with the control plane's reason, or
  that it is being read, or could not be. Understanding shows each claim with the source's own
  epistemic class as its tag (observed, reported, inferred, planned, explained), never upgraded:
  the verdict, what the session waits for, the agent's latest statement quoted as "Agent says: “…”",
  the changes, which files no passing check covers (inferred), the latest check of each kind, what
  needs attention, a model's explanation (explained, never evidence, and marked when it predates
  the latest evidence) and what remains. When they do not all fit, the most important stay, and
  what the source says twice (a reason that is a check's label, a failing check listed as remaining)
  is shown once. Evaluation shows the cost as "About $0.39." with the source's note "Estimated
  from token counts at list prices. Not a bill.", the outcome and the checks, each followed by its
  own availability, coverage and freshness, never combined, and read as stale once its `stale_at`
  has passed. A value the source does not have reads as unknown, pending or "known once it ends",
  never as zero; a measured zero reads as one ("no tool errors"). **`IntelligenceText`** makes
  every text from a source plain by `LabelText`'s rule, so a bidirectional override or a zero width
  character in it shows as its code point and markup as written.
- **`ClientWebSocketTransport`** implements `IRealtimeTransport` over `ClientWebSocket` with the
  bearer token on the upgrade request. `ClientWebSocket` works under IL2CPP on a Quest 3
  ([quest-3-device.md](../validation/quest-3-device.md)), over `ws://`, the USB path. It cannot pin
  a certificate on the headset, so paired connections use the pinned transports below.
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
  access token over `ClientWebSocketTransport` as over USB, or `Paired`, the device credential over
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
  could act, a node lists the answers it holds a continuation for (approve, deny, stop the turn, or
  one of its recorded instructions, each with a short label), each leading to its own node. A node
  ends by holding its final state: until an answer at an approval, for 60 seconds while it offers
  instructions, or, where the recording has nothing more, after a snapshot of its control plane
  started again without runtimes, for 20 seconds. The control plane computed every state in it,
  so the client still derives nothing. A recording whose journal is not a fixture, whose answers
  are not where an instant ends, or whose nodes do not form a tree that continues the journal, is
  refused; every character reads recorded, and its runtimes are synthetic, so it also reads
  simulated. The work a person directs runs on "Simulated agent (demonstration)", with the mock
  runtime's capabilities; the work beside it runs first, on "Simulated agent (demonstration, watch
  only)", which declares nothing to direct. `WorkspacePresenter` therefore offers, as it does live
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
  recorded for approving." `WorkspacePresenter.Feedback` shows those words as they are, without
  "Refused:", since the recording then plays its own recorded command, which its runtime confirmed.
  Typed text that matches no recorded instruction, ignoring case and spacing, continues with the
  first one offered and says so; any other command changes nothing and says that too. Nothing is
  journaled, and no command is ever reported accepted or done. Once a final state has held, the
  transport sends the beginning's snapshot again on the same connection, so the demonstration
  starts again without a disconnect; consumers see a rewind (`StateChanges.Rewound`: the same
  journal at an earlier position) and drop activity and submissions from before, as after a journal
  change. A new connection, as after the headset sleeps, also starts from the beginning. The
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
  while the demonstration is shown: that it is recorded, simulated work played on the device, not
  live, that it follows the person's answers and that nothing reaches an agent, and, while it holds
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
  your Mac", "Last known: can't reach your Mac. Trying again…"), or, while the demonstration is
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
  a little lower so its body never covers its badge. A character the lineup moves glides along the arc, swinging out behind the
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
  would reach a neighbour's. `Body` is the moving visual root, and `LookAtPerson` turns the
  character to the person for the workspace; the label stays where it is. Per-character values go
  through `MaterialPropertyBlock`s, so nothing allocates per frame.
- `Assets/Halcyonic/UI` is the interface's own assembly (`Halcyonic.XR.UI`), which the stage and
  the workspace use and which knows nothing of input: the tokens in Unity's terms (`GlazeTokens`),
  one shader for every flat shape (`Halcyonic/Glaze Surface`: a rounded rectangle with a fill, an
  edge inside its outline, solid or dashed, and a halftone, its properties instanced, shipped
  through a material in `UI/Resources`), text by type role (`GlazeText`: TextMeshPro in Liberation
  Sans, rich text off, escape parsing on, every text through `LabelText.ForTextMeshPro`, strong text
  thickened by its material, never TextMeshPro's bold, which finds no ellipsis in this font), and
  the components without input: `StateBadgeView`, `MarkTag`, `CharacterLabelView`, `PeekCardView`
  and `StageBanner`. Components are built in units of their distance from the eyes and scaled by it,
  so every size is an angle.
- `Assets/Halcyonic/UI.Interaction` (`Halcyonic.XR.UI.Interaction`) holds what takes input, on the
  Interaction SDK, for the workspace, the entry panel, the rail and the room and pairing controls;
  the stage never references it. `PointerTarget` lives here: a ray, poke and gaze target whose
  `Selected` and `Released` say a press began and ended, let go or cancelled, as hold to talk needs,
  and which the interaction log names as its creator says (`LogAs`: a character by its work's id).
  `GlazeButton` is the interface's button: its role sets its look (Primary in the accent, Secondary,
  Destructive outlined in red and its confirmation solid red, Filter outlined in the accent while on,
  Choice a raised tile edged in the accent while chosen, Attention in the attention colour, only to
  go to what waits for the person), it is 60 dp tall or 48 dp compact, a label in strong body text
  and an optional second line, and it shows each state: at rest, pointed at (lighter, with an
  accent ring), pressed (darker, its plate a little smaller, its words not), unavailable (outlined
  and quiet, taking no press), done (in the success colours) and set aside while the app lacks focus
  (faded). As a row of a panel's list (`ShowRow`) its words are left-aligned: a line over the title,
  the title and its detail wrapping to their lines, a shorter detail where the full one doesn't fit,
  and an end word in the accent; a static row only says something, with no tile and no press. Its
  presses keep `PanelButton`'s rules: none within 0.35 s of taking a new role or new words, or of
  becoming available, and a hold button's hold starts after 0.3 s and ends let go, dropped or taken
  away. `PanelFrame` draws any `PanelModel` as a foreground panel 44 by 26 degrees at 0.46 m: the
  title, its context and the window controls (Move, Reset position, Close) along the top, or, on a
  panel that stays beside its character, the title over its context at the left (a notice in their
  place, two lines tall) and the state badge and marks at the right, with a row of tabs under them
  that ends in Close; the heading, its action at its right, or the lead or the banner; the list in
  one column or two, pressable rows in cells of equal height 12 mm apart, lines and, in one column,
  rows that only say something as tall as their words, a page at a time, a log's older lines left
  out rather than paged, with the pager in the body's bottom right cell, or a screen's own pager in a
  row under the body with its heading and note at the left; and the bar, Back and the destructive
  action at the left and the primary at a right end at least 14 degrees wide, or the confirm step in
  its place. Targets keep 12 mm apart; words need less, so a body that starts or ends in words sits
  closer to what is above or below it. The frame records where every control stood on each screen
  without a confirm step and every control it shows while one shows (`Recorded`); the step puts
  Cancel at the right end and Yes at the right-most place in the bar's row 12 mm clear of all of
  them where its question fits beside it, else in a row just above the bar, the question beside it
  and the bar's row keeping only Cancel. While the step pages through what it confirms, the pager
  stands at the top, in the tabs' row or a row under the header, never in Yes's rows. It raises the
  id of what was pressed, with the row's or the tab's key, and splits text into parts of whole lines
  at the list's width (`SplitLines`). `PanelButton` stays until every surface has moved.
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
  Focus that flaps, as the Quest's system windows make it, folds nothing. The app's own system
  keyboard is tracked (`Track` wraps every `TouchScreenKeyboard.Open`), so typing folds nothing and
  drops no confirmation. Every other loss raises `Left`: a confirmation half done is dropped and
  must be given afresh once back (an armed approval, denial, stop or instruction says "You went to
  another window, so nothing was sent. Press it again to confirm."; a review whose final press
  waited goes back to the recap; a first press on a model elsewhere lapses; Forget this Mac asks
  again), while the runtime's request itself stays pending. Hold to talk (`HoldToTalk`, lane B)
  stops and discards its recording when input is suspended. While panels are folded, the line
  above the stage also counts what needs the person across every project ("2 need you", in the
  attention color), so it stays findable when the window covers the characters. Whether Unity
  reports each of these as a focus change on the Quest is verified only on the device.

### The workspace

`Assets/Halcyonic/Workspace` is its own assembly, the one that builds the Meta Interaction SDK's
targets; the room's controls reuse its `PanelButton`. Three levels of detail show the same work,
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
  understand, the understanding section) and Checked (What was checked?, the evaluation section),
  with Refresh beside their heading; and Close at the row's end. The workspace offers no Move or
  Reset position: it stays beside its character. Doing answers in one plain answer, what needs the
  person, else that nothing does and what it did last; what this headset sent and how it went,
  newest first, or "Nothing sent from here yet."; and Recent activity, a log whose newest line is
  last, the agent's words quoted and leaning, its older lines giving way where there is no room. For
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
  beside Tell it, only where the bar has room) and Tell it; and at its right end the action the work
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
  right, since they open sheets rather than act on work. Every button is 60 dp tall (48 compact) and
  12 mm from its neighbours. It rests 0.43 m from the eyes, 44.5 degrees below eye level, 24 degrees
  to either side, its rows 40 to 49 degrees down; over a desk, 0.3 m ahead and never into the desk,
  about 53 degrees down, under the lineup's labels. It is placed in front of the person when the app
  starts and when the stage moves onto or off a surface, and again by Reset position, and it steps
  out of the way while the entry panel, a workspace, the Usage left panel or Settings is open. Which
  projects show is kept on the device for each journal (`StageVisibility`); hiding a project hides
  its characters only.
- **Settings:** `SettingsSheet`, which the rail's Settings opens, is one foreground panel, 36 by
  about 20 degrees at 0.46 m, placed where the entry panel would be, clear of every character and
  label, with Close at its top right. It holds the controls that change how Halcyonic is arranged
  rather than act on work, each feature in a section of its own (`SettingsSection`): Your room, with
  the room's line and its switch, offer and Make room for a window, and, in development builds, Your
  Mac, with pairing. Their news no longer comes up in front of the person: a section's line also
  shows on the stage's banner as a short notice for eight seconds (`CharacterStage.ShowNotice`). It
  closes when the entry panel or a workspace opens and folds while another window keeps focus.
- **Usage left:** `UsageLeftGlance` offers its "Usage left" chip to the project rail, which places it
  at its lower row's right end (`ProjectRail.OfferUsageLeft`), and nothing anywhere else: no
  floating control. Pressing it opens a
  panel 0.7 wide where the entry panel would open (`WorkspaceLayout.PlaceForeground`), clear of every
  character, at the edge of that space away from them (below the characters 2.4 m away, above a desk
  lineup) so it clears their label plates too, and reads the control plane once. The rail steps out of
  the way meanwhile. The panel shows its title and Close, the readings, the note and Read again, which
  reads once more; it only lays out again every 15 s, to drop a window that has reset. An agent name
  longer than 32 characters ends in an ellipsis. It closes by Close, and when the entry panel or a
  workspace opens; while another window has focus its controls take no input and the chip hides,
  and once focus stays away the panel folds with what it read and comes back as it was. The recorded demonstration offers no usage limits. It is not Workstream status and not part
  of starting work. Rendered off the device (`UsageLeftRender`); not yet seen on a Quest.
- **Make room for a window:** while the stage stands in front of the person, Settings' Your room
  offers Make room for a window, which turns the lineup 32 degrees to their right
  (`CharacterStage.SetAside`, kept on the device), and Characters in front, which turns it back. On
  a desk the room placement decides where it stands. Halcyonic cannot see the window, so this
  reduces overlap and guarantees nothing: with a window of 1.4 by 0.79 m at 1.6 m straight ahead,
  the render (`AmbientRender`) counts it covering 4 of 6 characters' bodies and 4 of their labels in
  front, and 2 bodies and 3 labels aside: since ADR 0023 raised the stage, the labels stand at the
  window's height too.
- **Entry panel:** `EntryPanel`, the one foreground panel for entering work. Each screen is a
  `PanelModel` from `EntryScreens`, drawn by a `PanelFrame` 0.46 m from the eyes, 44 by 26 degrees
  (ADR 0023), opened where a foreground panel goes, clear of every character and its label
  (`WorkspaceLayout.PlaceForeground` with the panel's size), so every screen puts the same things in
  the same places: Move (to the right, the left and back, 28 degrees about the eyes), Reset position
  (the panel and the rail in front of where the person faces now) and Close at the top right; Back at
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
    instructions go ("On your Mac") and what that means ("Chosen for you. Change it in More
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
    the whole request (`NewWorkReview`, Check before starting, "This is exactly what will be
    sent."), wrapped at the body's width between words, a part at a time, each item whole in one part
    unless it alone is taller than a part, with the pager in a row under the header. Change takes
    the right end where Start building stood, and Yes, start building stands where no control of
    the recap stood nor any shown since, the pager on every part included, so pressing twice in one
    place never confirms; until the last part it stays there locked, saying what is left to read
    ("Read part 2 of 2"), and once unlocked it takes no press for its settle time. `BuildSequence` then sends the commands and each
    step shows how it went, in words and in its tone; a refusal offers Try again and Change, one
    about a folder the action its code names, and an unknown outcome only Next, to Not sure it
    happened. A project made here is shown on the stage whatever was chosen before. While a
    command's outcome is unknown its id stays in device storage and blocks another start, even after
    a restart, until two separate presses in two places clear it after the person checks the work:
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
    says so; and no places at all reads "Your Mac doesn't allow any folder yet", pointing to the Mac,
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
pairing, forgetting or a new token changes the control plane). `SectionView` draws a section under
its heading in the space the frame leaves, its first lines beside Refresh: the provenance line,
wrapping to two rows, then each line with its tag in a column beside it, a part's availability,
coverage and freshness in at most two rows; six lines of Understanding fit under the provenance.
Every label shows its text by the one rule, as every workspace label does (under "Words" below), so
it shows exactly what the source wrote, and a claim leans. Viewing a section sounds nothing.

**The whole request.** While approving or denying asks to be confirmed, the whole request the
answer is for shows in place of the tabs and their answers: the tool and what it would do, as the
runtime reported it and never shortened, in parts of as many whole lines as the body holds
(`SplitLines`), with "The whole request, part 1 of 5", Previous and Next in the tabs' row, away from
Yes; Cancel brings the tabs back. Cancel takes the bar's right end, and Yes stands in a row just
above the bar with its question beside it, clear of every control the screen had and every one
shown since. Until the last part has shown, Yes stays in its place, locked, saying what is left to
read ("Read part 5 of 5"), and the question reads "Read the whole request above before approving
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
down a little. The peek is a card of at most five short lines, and the hint three words.

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
- **Controls.** `RoomControls` puts the switch ("Show a virtual space" or "Show my room"), the
  offer ("Allow room access" or "Set up this room") and Make room for a window in the Your room
  section of Settings, under its line from `RoomStatus.Line`; they ignore input while
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
ceramic object that events strike softly with a felt mallet, and silence means all is well.

| Cue | When | What it sounds like | From |
| --- | --- | --- | --- |
| Working | A turn starts, or resumes after the person answered | A soft double tap, like the hop | Its character |
| Running tests | A test run starts | Four muted taps, up and back | Its character |
| Needs you | It rises and looks at the person | Two strikes rising; the second rings on | Its character |
| Turn finished | It settles; a finished turn proves nothing, so no celebration | The pair falling onto its own note | Its character |
| Failed | The turn failed | A dull, cracked strike, then a lower one | Its character |
| State unknown | Halcyonic cannot see the work right now | A strike whose pitch will not settle | Its character |
| Stopped | Interrupted, as the runtime confirmed | A strike caught by a hand | Its character |
| Last known | The connection dropped: one cue for the whole room | All six notes through a wall, fading | The whole stage |
| Open | The person expands a bot into its workspace | A chord unfolding toward them | The workspace |
| Collapse | The person folds the workspace back into the bot | The chord folding back into the bot | The workspace |
| Approve | Sent, not yet confirmed | Two notes struck together, open and warm | The workspace |
| Deny | A decision, not an error | A short step down, damped | The workspace |
| Instruct | The person's words were sent | Three light taps | The workspace |
| Interrupt | The stop was sent; the bot confirms later | A hand pressed flat on it | The workspace |

Nothing else sounds: not a start completing, a test run ending, work going on, or a workstream
that has not started. The rules come from the soundbook's research, and `SoundCueSelector` applies
them:

- **Only what the person might act on**, and work starting. Working and running tests are silent
  while they go on; nothing loops and nothing escalates.
- **Sent, not done.** The person's actions sound in front of them as the command is handed to the
  session (`WorkspaceDirector.Acted`), and say only that it was sent. The result arrives later as
  the character's own cue, because an accepted command is not success.
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
  default: with the file `needs-you-sound-while-away` in the app's data directory, a character that
  comes to need the person sounds Needs you once, at 60% of the usual level, while another window
  has focus; nothing else sounds and nothing repeats (`SoundCueSelector.Observe`'s
  `needsYouWhileAway`).
- **Calm.** Low energy: spectral centroids of 350 Hz on average and 649 Hz at most, power-weighted
  as the soundbook's own check measured them; loudness set by importance, from -20 LUFS for needs
  you to -29 for instruct; peaks at most 0.6 before the room.
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
`StageSound` renders all 79 clips (13 cues for each of the 6 notes, and Last known once) at the
output sample rate on a worker thread at startup, and makes them audio clips on the main thread,
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

- **The panel** is a button and a line in the Your Mac section of Settings (ADR 0023); the line
  shows while something is in progress or for ten seconds after it changed, and the stage's banner
  shows each line as a notice too, for a result reached with the sheet closed. Moving into Settings
  changed only where it shows and how it looks: the code entry, the confirmation and every message
  are as they were. "Forget this Mac" and its confirmation are outlined in red. The button ignores
  input while `FocusGuard.InputSuspended` or the sheet is closed.
- **Pairing.** "Pair with a Mac" opens the system keyboard for the Mac's address, as `pnpm pair`
  prints it (the last one typed is offered, and the port may be left out), then the number pad for
  the code. The exchange runs in the background; its answer is shown in words, with the attempts
  left after a wrong code, and a refusal the app does not know, in the words of whatever answered
  at that address, shows by the one rule for text Halcyonic did not write. On success the pairing
  is saved through
  `ControlPlaneSettings.PairingStore` and the `ControlPlaneConnection` is disabled and enabled
  again, so it connects to the paired control plane as at startup.
- **Forgetting.** Once paired, the button reads "Forget this Mac", and a second, deliberate press
  within six seconds ("Yes, forget this Mac") asks the Mac to revoke this headset, deletes the
  pairing, and connects again as before pairing. If the Mac cannot be reached, the line says to
  revoke the headset there.
- **Storage.** `ControlPlaneSettings` keeps the pairing in `halcyonic-pairing.json` in app-internal
  storage on Android (`Context.getFilesDir()`, which no other app can read and which `adb` reaches
  only through `run-as` on a debuggable build), and in the persistent data directory elsewhere. A
  pairing takes the place of a pushed access token; forgetting it returns to the token.
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
answer with Hold to talk beside it; three questions waiting, whose note must say more wait; the
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
pager's row, its question whole. Hold to talk must show beside Stop and Tell it and be left out where
Deny and Tell it fill the bar. Last, it puts hostile text on every
label that shows text from outside, through the code that shows it: markup, backslash sequences,
an end of text character, a carriage return and a line break, a bidirectional override, a zero
width space, a tag character and half a surrogate pair. It fails if any label interprets markup or
parses no escapes, uses italics or bold, shows text that did not go through the rule exactly once,
lays out other characters than that text or cuts it short without an ellipsis; if a claim does not
lean, or leans and loses its ellipsis; and it checks a character's label and the peek card the
same way, their TextMeshPro labels by the same rule.

`GlazeRender` (**Halcyonic > Render Every Component**, also runnable in batch mode) renders every
component in every state on a panel at touch distance, 0.46 m, each facing the eyes: every state's
badge, a count and a last known one, the marks, every role of button at rest, pointed at and pressed,
an unavailable, a done and a compact button and one with a second line, a tab in the attention
colour showing, and the banner's kinds,
saved at a Quest 3's 25 pixels per degree in `apps/xr/Builds/GlazeRenders`. It fails if a word is
under the caption's size, a button under 60 dp (48 compact), anything of ours cut short, a badge
missing its word, or a button's label under 4.5:1 on its own fill as drawn, as an off filter's grey
word was on its lighter fill when pointed at, before it brightened.

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
where it goes, or does not come back when the peek leaves.

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
every control shown before its confirm step and since, never in the pager's row, and, while it
pages, its pager stands above the body. It fails if the rail
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

- **`GlazeButton`'s and `PanelButton`'s hold mode** (`Holds`): a press held for 0.3 s starts the hold (`HoldStarted`);
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
  "This is what your Mac heard. Check it before you go on.", and nothing is sent until Start building and
  the review. In an open workspace, at the end of the action row when the work takes
  instructions and the row has room; a heard instruction always asks "Your Mac heard: ... Send it?".
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
choosing a folder deeper than one level inside a place the Mac allows; a companion that converses (Help me figure it out asks fixed questions); voice for the
fixed questions' own answers, a folder's name and the recap's Change, and voice in release builds; and a
creation draft that survives an app restart; discovering or attaching work Halcyonic did not
start; the soundbook's softer repeat of "Needs you" once nobody has
looked at the character for two minutes, and a volume and mute for sound in the headset; finding
the Mac without typing its address (mDNS), changing a paired Mac's address without pairing again,
and keeping the credential under an Android Keystore key. On a Quest, the control plane is
reachable over USB with `adb reverse tcp:47800 tcp:47800` and the pushed token, or over Wi-Fi once
paired ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md)).
