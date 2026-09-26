# XR client

How the Unity client is structured, what is verified, and what is not built yet. Decision and
alternatives: [ADR 0008](../decisions/0008-engine-independent-csharp-client-core.md).

## Layers

```text
Unity layer (apps/xr/Assets)          stage, placeholder characters, focus guard      skeleton, not yet compiled by Unity
        │
        ▼
Client core (com.halcyonic.client)    RealtimeSession, ClientProjection,             built, .NET tested
        │                             CharacterPresenter, WorkspacePresenter,
        │                             ActivityLog, EventHistory, CommandFactory
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
  reasons, command results, approval subjects) become an abstract base class with one sealed class
  per variant. A generated converter reads the discriminator, creates the variant and populates
  it. Properties every variant shares move to the base, so `EventEnvelope.WorkstreamId` works
  without a cast.
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
  runtime is gone), feedback on recent commands in words, and the activity.
- **`ActivityLog`** turns journaled events into readable activity per execution, marking agent text
  as a claim. A snapshot carries state but no history, so after a resynchronization the history of
  the workstream being looked at is read again through **`EventHistory`**
  (`GET /api/events`, paged, refused if the journal changed).
- **`ClientWebSocketTransport`** implements `IRealtimeTransport` over `ClientWebSocket` with the
  bearer token on the upgrade request. Whether `ClientWebSocket` works under IL2CPP on Quest is
  unverified ([meta-xr-platform.md](../validation/meta-xr-platform.md)); the interface is the seam
  for a native replacement.

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
- the session against a real control plane process with the mock runtime: an approval round trip
  to a finished turn with the workspace offering exactly the admissible actions, history over REST
  matching what arrived live, resuming after a dropped connection without a snapshot, and an
  execution in flight shown as stale during a control plane crash and as `unknown` after the
  restart.

Run `pnpm test:csharp` (the .NET 10 SDK and Node.js must be on `PATH`).

## Unity layer

`apps/xr` is a Unity 6000.3.25f1 project whose manifest pins OpenXR 1.18.0, the Meta XR Core and
Interaction SDKs 207.0.0, XR Hands 1.9.0 and Newtonsoft.Json 3.2.2. Its scripts use only long-stable
core Unity APIs:

- `HalcyonicBootstrap` adds the stage to any scene that lacks one, so no scene file carries it.
- `ControlPlaneConnection` owns the session, pumps it every frame, stops it when the application
  pauses and resumes it from the last position afterwards.
- `CharacterStage` places one placeholder character per workstream in an arc, and says above them
  whether the state is live; `CharacterView` renders a `CharacterPresentation` as a sphere whose
  motion follows the activity, with the title, status and attention notes written out.
- `FocusGuard` hides the assigned hand visuals and suspends input when the app loses focus.

Unity is not installed on the development machine yet, so none of this has been compiled or run
by Unity. Project settings, `.meta` files and the lock file are generated on first open and then
committed ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md)).

## Not built yet

Hand interaction with characters; the expanded workspace; real character art; token provisioning
on a headset; `wss://`. On a Quest, the loopback-only control plane is reachable over USB with
`adb reverse tcp:47800 tcp:47800`; there is no LAN serving yet ([SECURITY.md](SECURITY.md)).
