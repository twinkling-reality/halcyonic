# XR client

How the Unity client is structured, what is verified, and what is not built yet. Decision and
alternatives: [ADR 0008](../decisions/0008-engine-independent-csharp-client-core.md).

## Layers

```text
Unity layer (apps/xr/Assets)          stage, placeholder characters, focus guard      skeleton, compiles in Unity
        │
        ▼
Client core (com.halcyonic.client)    RealtimeSession, ClientProjection,             built, .NET tested
        │                             CharacterPresenter, CharacterCues,
        │                             CharacterIdentity, CharacterLineup,
        │                             WorkspacePresenter, ActivityLog, EventHistory,
        │                             CommandFactory
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
  changed. A shown workstream keeps its slot for as long as it stays shown; a newcomer takes the
  free slot nearest the middle, or the slot of the one it replaces. A waiting workstream replaces
  a shown one only from a more important tier, or, at rest, when it changed more recently, because
  working ones change every few seconds and would otherwise swap in and out.
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
- the evaluation read against a canned server: a full evaluation whose unknowns stay null, an
  answer without one, and a refusal;
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
- `ControlPlaneConnection` owns the session and pumps it every frame. It passes the application's
  pause state to `RealtimeSession.SetPausedAsync`, which stops the session on a pause and resumes it
  from the last position afterwards. It ignores the resumes Unity reports without a pause, at app
  start and when an XR session starts, and never revives a session stopped in between. After each
  frame's pump that changed the connection status, it logs the phase and its detail, and nothing
  else (never the token, workstream titles, instructions or agent text), as
  `Halcyonic: connection <phase>: <detail>` without a stack trace, because on a headset the log
  (`adb logcat -s Unity`) is the main diagnostic. It logs the status each frame ends with, so a
  phase that begins and ends within one frame, such as `Connecting` when the connection is refused
  at once, has no line of its own.
- `CharacterStage` places one placeholder character per workstream in an arc, and says above them
  whether the state is live; `CharacterView` renders a `CharacterPresentation` as a sphere whose
  motion follows the activity, with the title, status and attention notes written out. The sphere
  uses `Legacy Shaders/Diffuse`, one of the always-included shaders: a player build leaves out the
  Standard shader of a primitive's default material, which then renders magenta.
- `FocusGuard` hides the assigned hand visuals and suspends input when the app loses focus.

The project compiles in Unity and runs on a Meta Quest 3 against a live control plane
([quest-3-device.md](../validation/quest-3-device.md)); the Meta XR Simulator fails every frame on
the development Mac ([meta-xr-platform.md](../validation/meta-xr-platform.md)). `QuestBuild`, in an
editor-only assembly, builds a development APK, and a release APK that leaves Meta's development
tools out ([horizon-store-release.md](../validation/horizon-store-release.md)). Project settings,
`.meta` files and the lock file are committed ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md)).

## Not built yet

Hand interaction with characters; the expanded workspace; real character art; token provisioning
on a headset; `wss://`. On a Quest, the loopback-only control plane is reachable over USB with
`adb reverse tcp:47800 tcp:47800`; there is no LAN serving yet ([SECURITY.md](SECURITY.md)).
