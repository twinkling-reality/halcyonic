# ADR 0012: Judges run a labeled demonstration on the headset

- Status: Accepted by the owner on 2026-09-29: the on-device demonstration, with the scripted
  interactive variant, so judges act.
- Date: 2026-09-29

## Context

Halcyonic is being entered in the Meta VR Start Developer Competition 2026. Read on 2026-09-29 on
the competition's Devpost pages (overview, rules and FAQ) and in Meta's announcement on
developers.meta.com:

- The entry period ends on 2026-11-18 at 12:00 PT. Judging runs from about 2026-11-18 to about
  2026-12-09, and the winners are announced about 2026-12-11.
- Judges install the APK from a release channel named "Competition" through an invite URL, on their
  own Meta devices. They are many and spread around the world, and the FAQ says an entry "should
  not require a third-party device to operate".
- An entry must be fully usable with hands, must operate consistently, must stay available to
  judges until the winners are announced, and may not be changed after the entry period ends.
- Innovation and creativity, experience design, technical implementation, and polish and
  presentation weigh equally.

Today the XR client cannot run without the owner's Mac. The control plane serves loopback only
([SECURITY.md](../architecture/SECURITY.md)); a headset reaches it over USB through `adb reverse`,
and the access token is pushed with `adb` ([quest-3-device.md](../validation/quest-3-device.md)).
A judge has neither. Without a token the client shows a setup problem and nothing else.

Facts that bound the options:

- `ClientWebSocket` works under IL2CPP on a Quest 3 over `ws://`. `wss://` and REST through
  `HttpClient` are not verified on a headset ([quest-3-device.md](../validation/quest-3-device.md)).
- None of what [SECURITY.md](../architecture/SECURITY.md) requires before Halcyonic serves beyond
  one machine exists: device pairing, encrypted transport, per-device authorization, a remote
  relay, token rotation. There is one principal, whoever holds the token; policy categories
  restrict no one; every client receives every event; there are no rate limits beyond size limits.
- `pnpm replay` serves a recorded trace as a `fixture` journal with no runtime registered, so every
  surface labels it as recorded and commands against its executions are rejected rather than
  faked. The mock runtime is synthetic and labeled so everywhere.
- The client never derives state: it applies the snapshots and entity changes the control plane
  computes ([REALTIME.md](../architecture/REALTIME.md), [ADR 0008](0008-engine-independent-csharp-client-core.md)).

## Options

### (a) An on-device demonstration

The headset plays a recording of what the control plane sent a client, with no control plane and no
network: a clearly labeled demonstration, never presented as live work.

- **What a judge experiences.** The app starts with nothing else. Characters appear and move through
  the recorded work: working, running tests, one waiting for an approval that the recording's own
  operator grants, turns finishing, one flagged for failing tests. Every character reads recorded,
  and simulated because the recording comes from the mock runtime; the line above the stage says it
  is a demonstration played on the headset and that nothing done here reaches an agent. The final
  state holds, then the recording starts again. A judge can look and, once milestone 3 lands, open
  and collapse a character, but cannot act: a replay has no runtime, so no action is offered, and
  a command that reached it would be refused in words.
- **What it proves.** The presentation on the target hardware: state conveyed in words and motion,
  attention, hands-only use, frame rate, truthful labeling, and the compressed and expanded
  mechanic once it exists. Nothing about control, networking or real work.
- **Security.** No boundary moves. The headset holds no token, opens no socket, serves nothing, and
  the control plane stays loopback only. The bundled recording is a sanitized fixture of synthetic
  work. None of the unbuilt controls is needed.
- **Cost to 2026-11-18.** A prototype accompanies this proposal: the recorder, the player, the
  choice between control plane and demonstration, and their tests. What remains is days of work:
  one line in the stage to show the demonstration line, the workspace's wording for refusals, the
  headset check, and the release build clean-up every option needs.
- **Risks.** Judges may score a recording lower on experience design and technical implementation
  because "act" is missing. The labels must be unmistakable, or a judge may take the demonstration
  for live behavior. About twelve seconds of recorded work is short, even looped.

A variant keeps (a) and lets judges act: **a scripted interactive demonstration.** The recording
holds at a recorded decision until the judge answers, then plays the continuation that the control
plane recorded for that answer. The control plane records every branch under virtual time with
seeded identifiers, so the shared beginning is identical and no state is invented on the headset.
The judge's command is never reported as accepted or done: it is refused in words ("not sent to any
agent; the recording continues with the answer recorded for that choice"), and the stage says the
demonstration follows the judge's answers. Estimated three to five days more: branch recording, a
player that holds and branches, and the workspace's wording.

### (b) A hosted control plane with the mock runtime

The owner runs a control plane on a server with only the mock runtime; the app connects over
`wss://` with a judge credential.

- **What a judge experiences.** The app connects over the internet and shows whatever work is on the
  server, shared with every other judge. A judge can approve, deny and interrupt the mock runtime's
  work and see outcomes the runtime confirms. Something on the server has to keep starting work,
  because the client cannot create workstreams yet. When the server or the judge's network fails,
  the stage says disconnected and shows nothing useful.
- **What it proves.** The control loop over a real network: commands admitted by the control plane
  and confirmed by a runtime, reconnection, `wss://` on a headset. Still no real agent: only the
  synthetic mock runtime.
- **Security.** It is the first time Halcyonic serves beyond one machine, with none of the required
  controls:
  - *Encrypted transport:* TLS has to be terminated in front of the control plane, and `wss://` is
    unverified on the headset. The loopback and `Host` checks then hold only as well as the proxy
    that rewrites the `Host` header is configured.
  - *Device pairing:* a judge cannot receive a credential without another device, so it ships inside
    the APK, and anyone who obtains the APK can extract it. The service is open to the public in
    effect.
  - *Per-device authorization:* every judge is the same principal. Any judge can answer or interrupt
    another's work, and see it and any text they send, because every client receives every event.
    The XR client names the headset model as its device label, which the journal records with every
    command it sends.
  - *No quotas:* anyone with the credential can grow the journal without bound.
  - Registering only the mock runtime, with no project roots and no provider, Salidium or Seorak
    credentials on the server, limits the damage to availability and to what judges send.
- **Cost to 2026-11-18.** Estimated two to three weeks of the seven that remain, plus operations: a
  server with TLS, a release build carrying the endpoint and credential, `wss://` and `HttpClient`
  verified on a Quest, something that keeps work flowing, journal resets and abuse limits,
  monitoring, then keeping the service up and unchanged from 2026-11-18 until about 2026-12-11.
- **Risks.** An outage, or a judge's network that blocks the service, makes the entry fail the rule
  that it operate consistently. The FAQ speaks only of devices, so whether a service the entrant
  runs is acceptable is not stated. Fixing the service during judging may count as changing the
  submission. It serves beyond loopback before pairing exists, which is the precedent SECURITY.md
  exists to prevent.

### (c) Both

The app connects to the hosted control plane when it can and plays the demonstration otherwise, and
the stage says which. Judges on a working network get (b); the rest get (a). It costs (b) plus the
little (a) still needs, carries every security consequence and risk of (b), and adds a mode switch
that must stay visible. The prototype's choice between control plane and demonstration already
behaves this way once a hosted endpoint and credential are configured.

## Decision

Judges run (a), the on-device demonstration, extended with the scripted interactive variant: the
owner chose on 2026-09-29 that judges must act. The proposal read: judges run (a), the on-device
demonstration. It is the only option that satisfies the FAQ
without qualification, it costs days rather than weeks, it moves no security boundary, and it leaves
no service to keep alive through a judging window in which the submission cannot change. What it
gives up, judges acting, is what (b) would show only with synthetic work behind a public credential.
If judges must act, extend (a) with the scripted interactive variant rather than host a control
plane.

As prototyped alongside this proposal:

- `pnpm demonstration:record` has the control plane record a demonstration from a committed trace:
  the welcome, the snapshot and every event message a client receives from `pnpm replay`, at the
  replay's pace, computed by the control plane's own journal, projection and publisher under virtual
  time with seeded identifiers. The result is committed into the Unity project; a test fails when
  it is stale.
- In the client core, `DemonstrationTransport` implements `IRealtimeTransport` over that recording.
  It answers `hello` with the recorded welcome and snapshot, paces the events, answers pings, holds
  the final state and then ends the connection so the demonstration starts again. It refuses every
  command with a `rejected` acknowledgement that says why, never claims a command was accepted or
  done, opens no socket, and refuses a recording whose journal is not a fixture.
- `DemonstrationFallback` shows the demonstration when no access token is provisioned, or when the
  configured control plane's connection fails before it has been live since the app started, and
  keeps trying that control plane. It switches to the control plane once it is live, and never back.
  Consumers see each switch as a resynchronization, like a journal change.

## What the owner had to decide

Decided on 2026-09-29: (a), with the scripted interactive variant; no hosted control plane.

1. Whether judges get (a), (b) or (c).
2. With (a): whether judges only watch, with commands refused in words, or act in the scripted
   interactive variant, which follows their answers and says so (chosen, and since built).
3. With (b) or (c): whether Halcyonic may serve beyond loopback before device pairing,
   per-device authorization and a relay exist, with a credential that ships in the app and is
   therefore public, and whether the owner will run that service, unchanged, from 2026-11-18 until
   the winners are announced.

## Alternatives considered

- **Judges run the control plane on a computer.** A third-party device, which the FAQ rules out, and
  `adb` besides.
- **Run the control plane on the headset.** It is a Node.js process with a SQLite journal and
  runtime adapters; running it inside an Android app is neither a supported nor a verified path, and
  it would still have only the mock runtime.
- **Emulate the control plane in C# on the headset**, so a scripted demonstration could accept
  commands. The client would derive state and re-implement admission and the projection, which the
  architecture forbids, and its confirmations would come from a simulation of the control plane
  rather than from it.
- **Offer actions in the demonstration that are always refused.** Showing controls that cannot work
  misrepresents what the build can do. The recording has no runtime, so the workspace offers none.

## Consequences

- Judges, and anyone the owner shows the app to without the Mac, can run it with nothing else.
- The demonstration shows only what the control plane recorded. Regenerate it with
  `pnpm demonstration:record` after changes to the contracts, the pipeline, the mock runtime, the
  scenarios it plays or its plan; `pnpm check` fails while it is stale.
- The scripted interactive variant is built ([XR_CLIENT.md](../architecture/XR_CLIENT.md)). The
  recorder no longer replays a trace: it records the demonstration from mock scenarios written for
  it, a small web service whose sign-in gets rate limiting, with a branch for every answer a judge
  can give, as a tree of about 490 KiB that shares its beginning. What it cost and settled:
  - *Only one workstream can be directed.* Work at rest always admits an instruction, so every
    workstream a judge could direct multiplies the branches of everything after it. The story's two
    other workstreams run first, on a simulated runtime that declares nothing to direct and reads
    as watch only, and the directed one starts when they have finished.
  - *Every offered action has its continuation, and nothing else is offered.* The recording carries
    the runtimes' descriptors, so the workspace computes actions as it does live: stop the turn at
    every instant a turn runs, approve and deny at the approval, where the recording holds until
    someone answers, and recorded instructions once the first turn has ended. Where a path would
    leave an action open that it has no answer for, after a stop or a second turn, it ends with its
    control plane started again without runtimes, so its final state holds with nothing offered,
    then the demonstration starts again.
  - *The judge's command is answered, never accepted.* The contracts gained an additive rejection
    code, `demonstration`, because none of the existing codes was true of a command the recording
    had just offered ([REALTIME.md](../architecture/REALTIME.md)); the words say that nothing
    reached an agent and what the recording continues with. The recording then plays its own
    command, which the recorded runtime confirmed.
  - *Instructions.* The recording cannot follow arbitrary text. The workspace offers its recorded
    instructions as buttons instead of the keyboard; typed text that matches none continues with
    the first recorded one and says which.
  - *Starting again.* The demonstration starts again within its connection, with the beginning's
    snapshot, so it never reads as disconnected; clients treat the same journal at an earlier
    position as a rewind. After the headset sleeps it plays from the beginning.
  - *The runtime's name.* The recorder names the recorded runtimes "Simulated agent
    (demonstration)" and "Simulated agent (demonstration, watch only)"; both are the synthetic mock
    runtime, so every surface still labels the work as simulated.
- *Understanding and Evaluation* (milestone 5) show in the demonstration too, through the same
  contracts, routes, clients and client code as live: the recording also holds the control plane's
  REST answers about each execution's understanding and evaluation wherever the playback can stand,
  read through its own routes from stand-ins for Salidium and Seorak that speak their real contracts
  with invented content, and every answer is marked synthetic, so the workspace says "Simulated, not
  from Salidium" and "recorded at" in the provenance line
  ([ADR 0019](0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md)). The
  recording grows to about 720 KiB and keeps its format version; a reader that does not know the
  answers plays the rest.
- Still required before a competition build: checking the scripted demonstration on a headset
  with hands only, its Understanding and Evaluation sections included, and the release build that
  leaves Meta's development tools out
  ([horizon-store-release.md](../validation/horizon-store-release.md)).
- 2026-10-02: the story asks an agent question first (ADR 0022): one prompt with two options, a
  continuation recorded for each, answered in words like the others ("Nothing is sent to an agent.
  The recording goes on as if you answered “15 minutes”."). The recording grows to about 1.4 MB.
  For the competition's rule on brand names, the runtimes are now "Practice agent" and "Practice
  agent, watch only", and the line above the stage reads "Demo: recorded work played on this
  headset. Nothing here is live." ([competition-judge-build.md](../validation/competition-judge-build.md)).
- Revisit if the organizers accept a hosted service and the owner wants the control loop shown, or
  once device pairing and encrypted transport exist.
