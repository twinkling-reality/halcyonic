# XR client

How the Unity client is structured, what is verified, and what is not built yet. Decision and
alternatives: [ADR 0008](../decisions/0008-engine-independent-csharp-client-core.md).

## Layers

```text
Unity layer (apps/xr/Assets)          stage, characters, focus guard;                compiles and builds;
        │                             workspace: peek, panel, transition,             the workspace is not
        │                             Meta's interaction rig                          verified on a headset
        ▼
Client core (com.halcyonic.client)    RealtimeSession, ClientProjection,             built, .NET tested
        │                             CharacterPresenter, CharacterCues,
        │                             CharacterIdentity, CharacterLineup,
        │                             WorkspacePresenter, WorkspaceText,
        │                             WorkspaceSteering, CommandSubmissions,
        │                             ActivityLog, EventHistory, CommandFactory,
        │                             DemonstrationTransport, DemonstrationFallback
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
- **The recorded demonstration** is what a device with no control plane shows, proposed in
  [ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md) so that
  competition judges can run the app with nothing else. `DemonstrationRecording` reads the recording
  the control plane makes with `pnpm demonstration:record`: the welcome, snapshot and event messages
  a client receives from `pnpm replay` of the demo trace, and when. The control plane computed every
  state in it, so the client still derives nothing. A recording whose journal is not a fixture is
  refused, so every character reads recorded; the recorded work is the mock runtime's, so it also
  reads simulated. `DemonstrationTransport` implements `IRealtimeTransport` over it without opening a
  socket or using the token: each connection answers hello with the recorded welcome and snapshot,
  sends the events at their recorded pace, holds the final state for 30 seconds and then ends, so
  the session plays it again. It answers pings, and refuses every command with a `rejected`
  acknowledgement that says, in words, that nothing was sent to an agent; nothing is journaled, and
  no command is ever reported accepted or done. The recording has no runtime, so the workspace
  offers no action. `DemonstrationFallback` chooses what is shown: the demonstration when no control
  plane is configured; otherwise the control plane, except while it has not been live since the
  start and its connection has failed, when the demonstration plays and the control plane is tried
  again behind it. Once the control plane is live the demonstration stops for good, and a control
  plane that drops later shows its last known state as usual. Each switch reaches consumers as a
  resynchronization, like a journal change. Its `Line` is what the line above the stage says while
  the demonstration is shown.

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
- the evaluation read against a canned server: a full evaluation whose unknowns stay null, an
  answer without one, and a refusal;
- the demonstration: the bundled recording holds every event of the demo trace, each message reads
  strictly and writes back to the same JSON, and a live journal or anything out of order is
  refused; played through a session it reaches the trace's final state at the recorded pace, with
  every character recorded, simulated and not stale and no action offered, refuses each kind of
  command in words without changing anything, and plays again after holding its final state; the
  fallback shows it without a control plane, falls back to it from an unreachable one while trying
  that one again, switches to the control plane once it is live and never back, and follows pauses;
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
Interaction SDKs 207.0.0, XR Hands 1.9.0 and Newtonsoft.Json 3.2.2. Its scripts use only long-stable
core Unity APIs:

- `HalcyonicBootstrap` adds the stage to any scene that lacks one. The stage scene carries its own,
  so that its `FocusGuard` can reference the rig's hands.
- `ControlPlaneConnection` owns what is shown through a `DemonstrationFallback`: the session with
  the control plane when an access token is found, and the demonstration, loaded from the text asset
  `Resources/HalcyonicDemonstration.json` when first needed, while no control plane is configured or
  reachable. It pumps both every frame and exposes the session shown, and `DemonstrationLine`, the
  words for the line above the stage while the demonstration is shown. It passes the application's
  pause state to `RealtimeSession.SetPausedAsync`, which stops the session on a pause and resumes it
  from the last position afterwards. It ignores the resumes Unity reports without a pause, at app
  start and when an XR session starts, and never revives a session stopped in between. It logs, as
  `Halcyonic: ...` lines without a stack trace, whether the control plane or the demonstration is
  shown and why, and each change of either session's status as
  `Halcyonic: connection <phase>: <detail>` or `Halcyonic: demonstration <phase>`: the phase and its
  detail and nothing else (never the token, workstream titles, instructions or agent text), because
  on a headset the log (`adb logcat -s Unity`) is the main diagnostic. It logs the status each frame
  ends with, so a phase that begins and ends within one frame, such as `Connecting` when the
  connection is refused at once, has no line of its own.
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
  or reach. The arc is placed at the person's head, facing where
  they face, when the session starts and the head is tracked, when the tracking origin changes
  (a recenter or a new boundary, through `XRInputSubsystem.trackingOriginUpdated`), when the app
  resumes, and when the head seems to jump farther in one frame than a person can move; the log
  says why each time. An `IStagePlacementSource` on the stage object, such as a room placement
  that found the person's desk, can give it a surface instead: the pose's position is where the
  middle of the lineup stands, the arc curves around the person's side of it at their distance
  when the pose arrived, and every label plate rests on the surface. While that pose is set, only
  the source moves the stage, and recenters leave it. The stage keeps animating and updating while
  the app lacks input focus. It raises `CharacterCreated` and offers `TryGetCharacter`, so other
  components add to characters without changing them.
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

`Assets/Halcyonic/Workspace` is its own assembly, the only one that uses TextMeshPro and the Meta
Interaction SDK. Three levels of detail show the same work, all in place
([ADR 0014](../decisions/0014-hand-interaction-through-the-interaction-sdk.md)):

- **Ambient:** the characters as the stage shows them.
- **Peek:** while a hand ray (or a finger about to poke) points at a character, `PeekLabel` shows
  one line beside it, on the side toward the middle of the view: `WorkspaceText.Peek`.
- **Open:** a pinch on the ray, or a poke, opens `WorkspacePanel` beside that character, a little
  nearer the person and facing them. It shows the title and status, the execution and its runtime,
  the objective, what needs the person, the actions offered (with a confirmation step on a separate
  button where the policy asks for one), how requests are going, and the recent activity with
  agent text in italics as a claim. Collapse, or a second pinch on the character, returns to
  ambient. `WorkspaceTransition` grows the panel out of the character's body, rings the character
  and links it to the panel while open, and shrinks the panel back on collapse; the character stays
  where the stage put it.

`WorkspaceDirector`, on the stage object, attaches a `CharacterTarget` to each character the stage
creates: a sphere around the body for the ray, and a surface in front of it, facing the person, for
a poke. Panel buttons are the same `PointerTarget`s, ray and poke, 4 mm in front of the panel,
whose background takes the ray so nothing behind it is pointed at. The director keeps an
`ActivityLog` from live events, reads the open workstream's history through `ControlPlaneApi` when
it opens and after a resynchronization (saying so in the activity caption while it reads, or why
it could not), and sends commands with `CommandSubmissions.SubmitAsync`. Nothing is peeked or
pressed while `FocusGuard.InputSuspended`; the system keyboard's result counts anyway, since focus
returns only after the keyboard closes.

Sizes are designed at a distance (1.3 m for the panel, 1.6 m for the peek) for the Quest 3's
roughly 25 pixels per degree, and scaled by the actual distance, so the angular size stays the
same: body text has an x-height near 0.55 degrees (about 14 pixels), the smallest captions about
10 pixels, buttons are about 3 degrees tall. Text is TextMeshPro with Liberation Sans SDF, never
parsing markup, since it shows text from agents and tools. Plates and lines use `Sprites/Default`,
an always-included shader; the TextMeshPro shader reaches the build through the font asset in
`Resources`.

Instructions are typed on the Quest system keyboard (`TouchScreenKeyboard`, with Require System
Keyboard on in `OculusProjectConfig`, from which Meta's build step adds
`oculus.software.overlay_keyboard` to the manifest). Where no keyboard is supported, such as the
editor, the workspace offers three preset instructions instead.

### Scene

Stage.unity carries Meta's comprehensive interaction rig, added the way the Interaction SDK's
"Interactions Rig" building block adds it, by `StageSetup` (**Halcyonic > Set Up Stage
Interaction**, also runnable in batch mode). It brings the hand data, hand visuals, and the ray and
poke interactors the targets answer to. The Hand Tracking building block keeps tracking but no
longer draws, as Meta's wizard does, and the rig's locomotion (hand microgestures, controller
sticks, and the locomotor with its tunneling) is deactivated, because the stage is stationary.
`FocusGuard` hides the rig's hands and controllers and deactivates its interactors.

The project compiles in Unity and runs on a Meta Quest 3 against a live control plane
([quest-3-device.md](../validation/quest-3-device.md)); the workspace compiles and builds but is not
verified on a headset yet. The Meta XR Simulator fails every frame on the development Mac
([meta-xr-platform.md](../validation/meta-xr-platform.md)). `QuestBuild`, in an editor-only
assembly, builds a development APK, and a release APK that leaves Meta's development tools out
([horizon-store-release.md](../validation/horizon-store-release.md)). Project settings, `.meta`
files and the lock file are committed ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md)).

## Not built yet

Code, diffs, tests and output in the workspace; what Salidium and Seorak say about an execution,
which `ControlPlaneApi` reads but the workspace does not show; token provisioning on a headset;
`wss://`; a demonstration in which a person can act, which a recording cannot confirm
([ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md)). On a Quest,
the loopback-only control plane is reachable over USB with `adb reverse tcp:47800 tcp:47800`; there
is no LAN serving yet ([SECURITY.md](SECURITY.md)).
