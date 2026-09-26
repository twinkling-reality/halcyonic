# ADR 0003: REST for bootstrap and commands, WebSocket for live state

- Status: Accepted
- Date: 2026-09-26

## Context

XR clients need current state at start, live updates, and a way to act. Headsets sleep, lose focus
and change networks, so reconnection must not be assumed to mean synchronization. The first XR
client is Unity; its WebSocket support on Quest (IL2CPP) is not yet verified.

## Decision

- REST (Fastify) for snapshot bootstrap, journal history and command submission.
- One WebSocket at `/realtime` for live events and commands. On connect: `hello`, then `welcome`
  and a snapshot, unless the client's cursor is current, then every event with the full current
  state of the entities it changed.
- Clients never re-run the projection; they replace entities from `changes`. One reducer exists,
  on the server.
- Snapshot on reconnect rather than replaying missed events with historical state. History is
  available from REST.

## Alternatives considered

- **Server-sent events only.** Commands and acknowledgements would need a second channel;
  interaction is two-way.
- **Polling.** A poor fit for state that changes second by second, and wasteful on a headset.
- **WebRTC or MQTT.** No media or peer-to-peer need, and a broker adds operations without evidence
  it is needed.
- **Streaming only events and letting clients reduce.** It would duplicate the projection in C#,
  where it would drift.

## Consequences

- A snapshot costs more than a delta at large scale; at local scale it is a few kilobytes.
  Revisit if snapshots grow large.
- The XR transport must be abstracted (`IRealtimeTransport` in the client), since
  `ClientWebSocket` on Quest is unverified.
