# XR client

How the Unity client is structured, what is verified, and what is not built yet. Decision and
alternatives: [ADR 0008](../decisions/0008-engine-independent-csharp-client-core.md).

## Layers

```text
Unity layer (apps/xr/Assets)          stage, placeholder characters, focus guard      skeleton, compiles in Unity
        │
        ▼
Client core (com.halcyonic.client)    RealtimeSession, ClientProjection,             built, .NET tested
        │                             CharacterPresenter, WorkspacePresenter,
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
- **`WorkspacePresenter`** is the expanded form of the same workstream, for milestone 3: the
  character's cues plus the objective, the execution and its runtime, the actions the control plane
  would admit now (from declared capabilities and status; nothing while not live or when the
  runtime is gone), which of those actions need a deliberate confirmation (from the command
  policies in `welcome`; unknown counts as needed), feedback on recent commands in words, and the
  activity.
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
- activity descriptions from both recorded traces, workspace actions for every status and
  capability combination, command feedback, and history paging;
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
  observe the mock runtime, resuming after a dropped connection without a snapshot, and an
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
- `CharacterStage` places one placeholder character per workstream in an arc, and says above them
  whether the state is live; `CharacterView` renders a `CharacterPresentation` as a sphere whose
  motion follows the activity, with the title, status and attention notes written out. The sphere
  uses `Legacy Shaders/Diffuse`, one of the always-included shaders: a player build leaves out the
  Standard shader of a primitive's default material, which then renders magenta.
- `FocusGuard` hides the assigned hand visuals and suspends input when the app loses focus.

The project compiles in Unity and runs on a Meta Quest 3 against a live control plane
([quest-3-device.md](../validation/quest-3-device.md)); the Meta XR Simulator fails every frame on
the development Mac ([meta-xr-platform.md](../validation/meta-xr-platform.md)). `QuestBuild`, in an
editor-only assembly, builds the development APK. Project settings, `.meta` files and the lock file
are committed ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md)).

## Not built yet

Hand interaction with characters; the expanded workspace; real character art; token provisioning
on a headset; `wss://`; a demonstration in which a person can act, which a recording cannot confirm
([ADR 0012](../decisions/0012-judges-run-a-labeled-demonstration-on-the-headset.md)). On a Quest,
the loopback-only control plane is reachable over USB with `adb reverse tcp:47800 tcp:47800`; there
is no LAN serving yet ([SECURITY.md](SECURITY.md)).
