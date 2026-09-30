# XR client

How the Unity client is structured, what is verified, and what is not built yet. Decision and
alternatives: [ADR 0008](../decisions/0008-engine-independent-csharp-client-core.md).

## Layers

```text
Unity layer (apps/xr/Assets)          stage, characters, focus guard;                compiles and builds;
        │                             workspace: gaze and hand peek, panel,           the workspace, the
        │                             transition, first-time hint; Meta's rig;        room and the sound
        │                             room: passthrough, MRUK, stage anchor;          are not verified on
        │                             sound: the characters' voices                   a headset
        ▼
Client core (com.halcyonic.client)    RealtimeSession, ClientProjection,             built, .NET tested
        │                             CharacterPresenter, CharacterCues,
        │                             CharacterIdentity, CharacterLineup,
        │                             WorkspacePresenter, WorkspaceText,
        │                             WorkspaceSteering, CommandSubmissions,
        │                             PeekChoice, WorkspacePlacement, SeatedPointing,
        │                             InFrontPlacement,
        │                             ActivityLog, EventHistory, CommandFactory,
        │                             DemonstrationRecording, DemonstrationPlayer,
        │                             DemonstrationTransport, DemonstrationFallback,
        │                             StageSurfaces, PlacementMemory, RoomStatus,
        │                             GlazeSynthesizer, SoundCueSelector
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
  noticed only by TCP.
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
- **`CharacterPresenter`** maps a workstream to what its character conveys: an activity, a text
  label for every status (never color alone), the attention level with one explanation per reason,
  and flags for simulated work, recorded fixture data, and a stale state while the session is not
  live. A completed turn reads "Turn finished", because completion says nothing about correctness.
- **`CharacterCues`** turns a presentation into what the character shows: its eyes, its motion, its
  halo, its surface (flowing, cracked or fogged), whether it faces the person, and whether it is
  paused or ghosted ([ADR 0013](../decisions/0013-characters-are-bots-with-a-living-surface.md)).
  No two activities differ only in color, and the last known state keeps its cues but stops moving.
- **`CharacterIdentity`** derives a character's body shape, hue, tone and motion phase from its
  workstream id alone (FNV-1a over the UTF-8 id, then MurmurHash3's finalizer), so a workstream
  looks the same in every session and version. The eight hues keep at least 25 degrees from the
  state colors: amber for needs you, red for failed, green for a finished turn.
- **`CharacterLineup`** chooses which workstreams have a character and where each stands: needs
  you first, then failed, unknown or failing tests, then active work, then the most recently
  changed. Characters that need attention stand nearest the middle of the person's view; every
  other character keeps its slot for as long as it stays shown. A newcomer takes the free slot
  nearest the middle, or the slot of the one it replaces, and a character that comes to need
  attention trades places with the one nearest the middle that does not. A waiting workstream
  replaces a shown one only from a more important tier, or, at rest, when it changed more
  recently, because working ones change every few seconds and would otherwise swap in and out.
- **`WorkspacePresenter`** is the expanded form of the same workstream, for milestone 3: the
  character's cues plus the objective, the execution and its runtime, the actions the control plane
  would admit now (from declared capabilities and status; nothing while not live or when the
  runtime is gone), which of those actions need a deliberate confirmation (from the command
  policies in `welcome`; unknown counts as needed), feedback on recent commands in words, and the
  activity. Approving or denying answers the oldest pending approval.
- **`WorkspaceText`** writes every word the peek and the workspace show, so the Unity layer only
  lays them out: the status with its qualifiers ("simulated", "recorded", "last known"), the
  execution and its runtime, what needs the person, activity lines with the local time and agent
  text quoted as "Agent says: “…”", action labels, a confirmation question that names exactly what
  would be sent, why no action is offered, and the one-line peek: what the work needs first, else
  the latest activity that is not a turn boundary, else the status, prefixed "Last known:" when
  stale.
- **`CommandSubmissions`** keeps this client's own view of each command it sent (sending, not
  sent, outcome unknown, acknowledged) until the control plane's record of it arrives in the
  projection. From then on only that record speaks, so a result reads as done only after the
  runtime confirmed it. `WorkspacePresenter.Present` merges the two, newest first.
- **`WorkspaceSteering`** turns presses in an open workspace into commands. It takes only offered
  actions. One the control plane's policy marks for review waits for a second, deliberate press on
  a separate button whose question names what will be sent; the confirmation lapses after 15
  seconds, when the action is no longer offered, or when its approval is no longer pending, and
  says so. Instruct asks for text first, and an empty text sends nothing.
- **`PeekChoice`** decides, frame by frame, which one character shows its peek, how visible it is,
  and what a look and pinch opens. A hand pointing at a character, or a finger about to poke it,
  peeks at once. The gaze peeks only after resting half a second on one character within 7 degrees
  of where the head faces, and starts over while the head turns faster than 20 degrees a second,
  so turning the head across the stage brings up nothing. A peek fades in over 0.25 s and out over
  0.15 s, only one shows at a time, and a glance aside keeps it 0.3 s. The open character is never
  peeked, and while a workspace is open only hands peek. A pinch of either hand opens a character
  only while its gaze peek is at least half visible, no hand ray or finger is on any target, no
  workspace is open, and the app has focus.
- **`WorkspacePlacement`** chooses where the workspace opens, seen from the eyes: toward its
  character, at most 15 degrees to the side of where the person looks, and clear of every
  character's body, below the ones it passes or above them, whichever keeps its center between 30
  degrees below and 2 degrees above eye level (the nearer to 15 degrees down when both do); never so
  low that its lower edge comes within 5 cm of the surface the characters stand on. Where neither
  clears, it moves the least into the band and may cover a body.
- **`SeatedPointing`** makes a hand ray for a seated person: through the index knuckle from a pivot
  0.40 m below the eyes, 0.10 m behind them and 0.13 m to the hand's side, so a hand resting a
  little above a desk points ahead; on only while the hand is tracked with high confidence, in
  front of the eyes, its palm neither facing the floor (resting or typing) nor facing the eyes (the
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
  a workstream is opened, never on a timer.
- **`ClientWebSocketTransport`** implements `IRealtimeTransport` over `ClientWebSocket` with the
  bearer token on the upgrade request. `ClientWebSocket` works under IL2CPP on a Quest 3
  ([quest-3-device.md](../validation/quest-3-device.md)); `wss://` is not verified there yet, and
  the interface remains the seam for a native replacement.
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
  every point of every path.
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
  it started from the beginning. It can take a recording still being read on another thread and
  connects once it is read. `DemonstrationFallback` chooses what is shown: the demonstration when
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
- the workspace's words and peek, steering with its confirmations and their lapses, and command
  submissions through a session against the in-memory server (accepted, refused, cut off, not
  connected);
- the peek at 72 frames a second: nothing while the head sweeps the stage or turns slowly, a peek
  after half a second of rest near the middle of the view, a glance aside kept, one peek at a time
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
  the beginning after a pause;
- the sound: every render's length, peak and energy equal the soundbook page's own, computed by the
  page's code; renders repeat sample for sample; no sample is NaN or above the page's ceiling;
  each cue lasts as the soundbook says, starts and ends in silence, meets its loudness target and
  stays under its own spectral centroid ceiling; the room is the send convolved with its response,
  checked against a direct sum; the selector's cue for each change of activity, silence on
  snapshots, resynchronizations and rewinds, onsets 300 ms apart with the most pressing first,
  "Last known" once per loss and never for a session the application stopped, nothing while cues
  cannot be heard and nothing late, repeats dropped unless the person acted, each character's own
  note, the person's actions in front of them, and the bundled demonstration heard event by event
  as its story;
- the session against a real control plane process with the mock runtime: an approval round trip
  to a finished turn with the workspace offering exactly the admissible actions, history over REST
  matching what arrived live, understanding and evaluation answering that their providers do not
  observe the mock runtime, resuming after a dropped connection without a snapshot, an approval
  and then an instruction steered from the workspace to results the runtime confirmed, and an
  execution in flight shown as stale during a control plane crash and as `unknown` after the
  restart;
- the real control plane process stopping by itself once its standard input closes, which the
  operating system does when the test host dies: the tests start every control plane with
  `HALCYONIC_EXIT_ON_STDIN_END=1` and a standard input only the test host holds, so none outlives
  a test host that is killed, crashes or is ended by a runner's timeout.

Run `pnpm test:csharp` (the .NET 10 SDK and Node.js must be on `PATH`).

## Unity layer

`apps/xr` is a Unity 6000.3.25f1 project whose manifest pins OpenXR 1.18.0, the Meta XR Core and
Interaction SDKs and the MR Utility Kit 207.0.0, XR Hands 1.9.0 and Newtonsoft.Json 3.2.2. Its
scripts use only long-stable core Unity APIs:

- `HalcyonicBootstrap` adds the stage to any scene that lacks one. The stage scene carries its own,
  so that its `FocusGuard` can reference the rig's hands.
- `ControlPlaneConnection` owns what is shown through a `DemonstrationFallback`: the session with
  the control plane when an access token is found, and the demonstration, loaded from the text asset
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
  above them whether the state is live, or, while the demonstration is shown, its
  `DemonstrationLine`. The arc is 2.4 m away, beyond the system windows, such as Virtual Display's
  screens, that open within about 2 m
  ([horizon-os-multitasking.md](../validation/horizon-os-multitasking.md)); 0.45 m below the eyes;
  and 60 degrees between its outermost characters, so every character and its labels stay within
  about 36 degrees of where the person faced, a comfortable field on narrower headsets too. All
  three are serialized settings, and characters and labels scale with the distance, so they keep
  their apparent size. A character the lineup moves glides along the arc, swinging out behind the
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
  when the pose arrived, and every label plate rests on the surface. While that pose is set, only
  the source moves the stage, and recenters leave it. The stage keeps animating and updating while
  the app lacks input focus. It raises `CharacterCreated` and offers `TryGetCharacter` and
  `SlotOf`, so other components add to characters without changing them.
- `CharacterView` draws a `CharacterPresentation` as a bot
  ([ADR 0013](../decisions/0013-characters-are-bots-with-a-living-surface.md)): a body mesh
  generated for its identity's shape, with its eyes, satin flow, cracks, fog and halftone in one
  shader, a halo and a testing ring behind and around it, and the title, the status and the
  attention notes on a plate underneath, wrapped to 11 degrees, less than the 12 between slots. `Body` is the moving visual
  root, and `LookAtPerson` turns the character to the person for the workspace. Per-character
  values go through `MaterialPropertyBlock`s, so nothing allocates per frame. Labels use Unity's
  built-in font through `TextMesh`, rasterized at 48 pixels, close to their size on the headset.
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
- `FocusGuard` hides the assigned hand visuals and suspends input when the app loses focus.

### The workspace

`Assets/Halcyonic/Workspace` is its own assembly, the one that builds the Meta Interaction SDK's
targets; the room's controls reuse its `PanelButton`. Three levels of detail show the same work,
all in place ([ADR 0014](../decisions/0014-hand-interaction-through-the-interaction-sdk.md)):

- **Ambient:** the characters as the stage shows them.
- **Peek:** once the person's gaze has rested on a character, or at once while a hand ray (or a
  finger about to poke) points at it, `PeekLabel` fades in one line beside it, on the side toward
  the middle of the view: `WorkspaceText.Peek`. `PeekChoice` decides which and when: turning the
  head across the stage peeks nothing, one peek shows at a time, a hand pointing wins over the
  gaze, and while a workspace is open only a hand peeks, so reading the workspace never brings up
  peeks behind it.
- **Open:** a pinch on the ray, a poke, or, while a gaze peek shows and no hand ray or finger is on
  a target, a pinch of either hand at any height (look and pinch) opens `WorkspacePanel` next to
  that character, within reach and clear of the other characters, facing the eyes. It shows the title and status, the execution and its runtime,
  the objective, what needs the person, the actions offered (with a confirmation step on a separate
  button where the policy asks for one), how requests are going, and the recent activity with
  agent text in italics as a claim. Collapse, or pointing at the character and pinching again, returns to
  ambient. `WorkspaceTransition` grows the panel out of the character's body, rings the character
  and links it to the panel while open, and shrinks the panel back on collapse; the character stays
  where the stage put it, and the panel follows it if the stage moves it, as after a recenter.

`WorkspaceDirector`, on the stage object, attaches a `CharacterTarget` to the `Body` of each
character the stage creates, so it moves with the body: a sphere of `CharacterView.BodyRadius` for
the ray and the gaze, which neighbours on the arc never share, and a surface just in front of it,
facing the person, for a poke; on a desk its poke hovers only for a finger within 0.06 of its
scale, since typing hands are near. While a character's peek is wanted or it is open it looks at
the person (`CharacterView.LookAtPerson`). Panel buttons are the same `PointerTarget`s, ray and poke, 4 mm in front of the
panel, whose background takes the ray so nothing behind it is pointed at. The director keeps an
`ActivityLog` from live events, reads the open workstream's history through `ControlPlaneApi` when
it opens and after a resynchronization (saying so in the activity caption while it reads, or why
it could not), and sends commands with `CommandSubmissions.SubmitAsync`. While the demonstration is
shown it reads no history, since the recording plays all of it through the session; its answers
read through the acknowledgement's command record, in the demonstration's own words; and Instruct
offers the instructions the demonstration recorded there as presets instead of opening the
keyboard. It raises `Acted` when the person opens a workspace, collapses it, or sends a command
from it, as the command is handed to the session; the sound follows it. A switch of session arrives as a resynchronization: the open workspace follows its
workstream into the new state, or collapses when the workstream is not there. On a journal change
or a rewind it drops its activity and submissions, which no longer apply. Nothing is peeked, hinted or pressed while
`FocusGuard.InputSuspended`; the system keyboard's result counts anyway, since focus returns only
after the keyboard closes.

**Gaze, and look and pinch.** `GazeHover` adds an Interaction SDK gaze interactor
(`GazeInteractor`, v207) that hovers the `GazeInteractable` on each character. It follows the
scene's `GazeConecaster` (a 2 degree cone, 0.2 s dwell) on Meta's `EyeGaze`, whose camera pose
emulation makes it head gaze: a Quest 3 has no eye tracking, and the app does not ask for it.
`GazeHover` is also the interactor's selector, as the SDK's hand gaze interactor pairs gaze with a
pinch: an `IndexPinchSelector` on each hand selects, but only while `PeekChoice` says a gaze peek
shows and no hand ray or finger is on a target, and never while the pinching palm faces the eyes,
the headset's menu gesture; the director opens the character only if it is the one the pinch was
for. A look alone never acts, and where there is no gaze the hand ray still peeks. Verified in the
SDK's source and in the editor, not yet on a headset
([workspace-interaction.md](../validation/workspace-interaction.md)).

**Seated rays.** The rig's hand rays are `SeatedHandRay`s (`SeatedPointing`): through the index
knuckle from a pivot below the shoulder, so a person seated with a forearm resting points at the
characters without raising a hand to shoulder height, which the headset's own ray needs; a palm
facing the floor, resting or typing, has no ray.

**Seated, and within reach, clear of the stage.** The workspace opens 0.6 m from the eyes, about
two feet, so a seated person pokes its buttons without leaning or standing. It is scaled to keep
its designed angular size, which puts its buttons about 33 mm tall there. `WorkspaceLayout` hands
every character's body, as seen from the eyes, to `WorkspacePlacement`, which opens it clear of all
of them: with the characters 2.4 m away, below them, its center 30 degrees down, over the labels of
the characters it passes but none of their bodies; with the characters on a desk half a meter
away, above them, its center 10 to 15 degrees down, its lower edge at least 5 cm above the desk.
The rest of the stage stays in view. `WorkspaceRender` renders both in the editor and checks it.

**Field of view.** The workspace spans about 34 by 27 degrees wherever it opens. Its center stays
within 15 degrees of where the person looks, and between 30 degrees below and 2 degrees above eye
level, so all of it, controls included, sits in the middle of a narrower field of view than the
Quest 3's (as on a Quest 3S), never at an edge; with the characters 2.4 m away, its actions row
is about 20 degrees below them, and its activity lines, at the bottom, may need the head tilted
down a little. The peek is one line, and the hint three words.

**Hands first.** Everything works with hands alone: pointing, pinching and poking, looking and
pinching, and typing on the system keyboard. The rig supports controllers, but nothing needs one.

**First time.** Until the person first opens a workspace on the device, `OnboardingHint` shows a
thumb and index finger closing into a pinch, with "Look, then pinch", above the first character
that needs them; opening any workspace, by any of the three ways, retires it for good (a player
preference, not state).

**Words.** The app's own words name no brand (a test checks them); names in the data, such as a
runtime's display name, are shown as they arrive.

Sizes are designed at a distance (1.3 m for the panel, 1.6 m for the peek and the hint) for the
Quest 3's roughly 25 pixels per degree, and scaled by the actual distance, so the angular size stays
the same: body text has an x-height near 0.55 degrees (about 14 pixels), the smallest captions about
10 pixels, buttons are about 3 degrees tall. Text is TextMeshPro with Liberation Sans SDF, never
parsing markup, since it shows text from agents and tools. Plates and lines use `Sprites/Default`,
an always-included shader; the TextMeshPro shader reaches the build through the font asset in
`Resources`. The workspace's plate is opaque and draws after everything at the characters'
distance, so nothing behind it shows through: at 95 percent, in the project's linear color space,
white labels behind it read through it.

Instructions are typed on the Quest system keyboard (`TouchScreenKeyboard`, with Require System
Keyboard on in `OculusProjectConfig`, from which Meta's build step adds
`oculus.software.overlay_keyboard` to the manifest). Where no keyboard is supported, such as the
editor, the workspace offers three preset instructions instead.

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
- **Controls.** `RoomControls` shows the switch ("Show a virtual space" or "Show my room") and the
  offer ("Allow room access" or "Set up this room") as the workspace's `PanelButton`s, pointed at
  and pinched or poked and ignoring input while `FocusGuard.InputSuspended`. They rest 26 degrees
  to the right, 0.4 m out and 0.4 m below the eyes, about 45 degrees down, within a seated
  person's reach and under the lineup on a desk; they come up in front of the person, a little
  below the eyes, for 6 seconds when there is something to read or offered, and return to rest
  when the person has faced more than 50 degrees away for 1.5 seconds. Above them, one line from
  `RoomStatus.Line` shows for 8 seconds after it changes.
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
  later. Whether that is right while the person works in Virtual Display is open
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
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
1e-16 of the peak, and at 44.1 kHz in all ([sound-rendering.md](../validation/sound-rendering.md)). `StageSound` renders all 79
clips (13 cues for each of the 6 notes, and Last known once) at the output sample rate on a worker
thread at startup, and makes them audio clips on the main thread, four a frame. Nothing is
synthesized while sound plays; `OnAudioFilterRead` is not used.

**Voices.** Each character has two audio sources on its `Body`, so a cue can start while the last
one still rings: fully spatial (`spatialBlend` 1), no Doppler, and logarithmic rolloff at full level
within 2.5 m. The stage stands within that both on a desk, about 0.55 m away
([ADR 0015](../decisions/0015-the-stage-stands-on-the-persons-desk.md)), and in the virtual space,
2.4 m away, so where it stands is heard in each cue's direction and not in its loudness, as the
characters keep their apparent size. The person's actions sound 0.6 m in front of their head, where
the workspace opens; Last known sounds from the middle of the characters, spread over the arc's 60
degrees. Unity's built-in panning places them, with no spatializer plugin or package: the page
panned each bot by the sine of its angle on the arc, which positions reproduce. Cues are scheduled
on the audio clock (`AudioSource.PlayScheduled`), so once the clips exist nothing runs per frame.
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
renders the workspace open over the stage with the characters 2.4 m away and on a desk, saves the
renders in `apps/xr/Builds/WorkspaceRenders`, and fails if a pixel of the workspace changes with the
stage drawn behind it, a character's body is behind it, or its center leaves the band. With the
plate at its former 95 percent, it failed in both places.

The room placement is not in the scene: `RoomBootstrap` adds it at runtime, and it creates MRUK,
the passthrough layer, the stage's anchor and its controls under an object of its own. Nor is the
sound: `SoundBootstrap` adds `StageSound`, which puts its sources on the characters' bodies and
under the stage object.

The project compiles in Unity and runs on a Meta Quest 3 against a live control plane
([quest-3-device.md](../validation/quest-3-device.md)), where opening a workspace by pointing and
pinching, approving and the runtime-confirmed result are verified; the calm peek, look and pinch,
the seated rays, the workspace clear of the stage, the stage's placement with system windows open,
the room placement and the sound compile and build but are not verified on a headset yet. The Meta XR Simulator fails every frame
on the development Mac ([meta-xr-platform.md](../validation/meta-xr-platform.md)). `QuestBuild`,
in an editor-only assembly, builds a development APK, and a release APK that leaves Meta's
development tools out, except the Immersive Debugger's runtime, disabled, which MRUK needs
([horizon-store-release.md](../validation/horizon-store-release.md)). Project settings, `.meta`
files and the lock file are committed ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md)).

## Not built yet

Code, diffs, tests and output in the workspace; what Salidium and Seorak say about an execution,
which `ControlPlaneApi` reads but the workspace does not show, and recorded answers for them in the
demonstration, whose format leaves room for them; token provisioning on a headset; `wss://`; the
soundbook's softer repeat of "Needs you" once nobody has looked at the character for two minutes,
and a volume and mute for sound in the headset. On a Quest,
the loopback-only control plane is reachable over USB with `adb reverse tcp:47800 tcp:47800`; there
is no LAN serving yet ([SECURITY.md](SECURITY.md)).
