# ADR 0008: An engine-independent C# client core with generated contract bindings

- Status: Accepted
- Date: 2026-09-26

## Context

The XR client is a Unity application (Unity 6000.3, OpenXR, Meta XR SDK; see
[meta-xr-platform.md](../validation/meta-xr-platform.md)). It must speak the realtime protocol
exactly, survive disconnects truthfully, and present state it never derives itself. Unity is not
installed on the development machine yet, and nothing has run on a headset.

Verified on 2026-09-26:

- Unity's `com.unity.nuget.newtonsoft-json` 3.2.2 is the current release and ships Newtonsoft.Json
  13.0.2 (Unity package registry).
- Unity 6 compiles C# 9 against the .NET Standard 2.1 API profile.
- The .NET 10 SDK (10.0.401) compiles the generated bindings and the client core as .NET Standard
  2.1 with C# 9 without warnings, and 37 NUnit tests pass, including two against a real control
  plane process.

## Decision

- The client core (protocol session, client projection, character presentation, command
  building) is plain C# in the Unity package `com.halcyonic.client`, with `noEngineReferences`.
  Only the Unity layer may reference `UnityEngine`.
- C# contract types are generated from the TypeBox definitions by `pnpm contracts:emit` into the
  Unity package `com.halcyonic.contracts`, with Newtonsoft.Json attributes and generated
  converters for discriminated unions. They are never written by hand, and a test fails if they
  are stale.
- Networking runs in the background; `RealtimeSession.Pump()` applies received messages on the
  caller's thread, so Unity state is touched only on the main thread.
- The WebSocket sits behind `IRealtimeTransport`; the first implementation uses
  `ClientWebSocket`.
- `apps/xr/dotnet` compiles both packages under Unity's constraints and runs their tests on .NET,
  without Unity.

## Alternatives considered

- **Unity's `JsonUtility`.** No dictionaries, no polymorphism and no nulls for value types, so it
  cannot represent the discriminated unions or the explicit-null convention.
- **System.Text.Json.** Not an officially supported Unity package; polymorphism by discriminator
  needs custom converters anyway, and IL2CPP stripping needs extra care.
- **Hand-written C# types.** Drift from the contracts is inevitable; ADR 0005 rules it out.
- **Game logic in MonoBehaviours.** Testable only inside the Unity editor, which is not available
  yet, and hard to test at all for reconnection and timing.
- **Applying messages on the networking thread with locks.** Every reader in the Unity layer would
  need to lock, and Unity objects must not be touched off the main thread anyway.

## Consequences

- Protocol and presentation logic can be tested now, in seconds, against real control plane data.
- The Unity layer stays thin: it reads `State`, renders `CharacterPresentation`, and calls
  `SubmitAsync`.
- Contract changes regenerate C# automatically; the .NET tests catch changes that break the
  client.
- Revisit if `ClientWebSocket` fails under IL2CPP on Quest (replace the transport, not the core),
  or if Newtonsoft's reflection costs too much on device.
