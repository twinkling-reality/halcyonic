# ADR 0052: A preview reaches a paired headset through a relay on the headset's loopback address

- Status: Accepted
- Date: 2026-11-11

## Context

A person in VR never saw the app they made: a project's preview is served on the Mac's 127.0.0.1
(47930 to 47939), and the headset reached it only through `adb reverse` over USB
([quest-browser-preview.md](../validation/quest-browser-preview.md)). Opening the Mac's LAN address
instead is no answer: a page there is not a secure context (W3C Secure Contexts), so WebXR, which the
VR starter's Enter VR needs, is not offered; a self-signed HTTPS address asks the person to click
through a warning, and whether Quest Browser then offers WebXR is unknown. The headset's own
`http://127.0.0.1:<port>/` is potentially trustworthy, so a page there can offer WebXR. The owner's
direction (2026-11-11): the person sees the app they made in the headset, with no cable; the lead's
rules: only for a check whose agents' reads were confined (`reads_confined`, ADR 0041), reaching the
preview's port and nothing else on the Mac.

## Decision

- **The Mac relays one preview's files to a paired device.** The network listener serves
  `GET` and `HEAD /api/projects/:project_id/preview/...` to a paired device's credential only (not on
  loopback). Each request is gated (`confinedPreviewTarget`): the project's preview runs, serves the
  latest check, which passed and recorded `reads_confined: true`, on a preview port, and the host's
  own record of its server names that port and check. Refused, it answers 409 with a code
  (`preview_not_running`, `preview_not_latest`, `preview_not_passed`, `preview_not_confined`). Allowed,
  it asks `127.0.0.1:<that port>` alone, passing only `Accept`, `Accept-Language`, `Range`,
  `If-None-Match` and `If-Modified-Since`, never the credential or a cookie, and passes back only the
  file's description (`Content-Type`, `Content-Length`, `Content-Range`, `Accept-Ranges`, `ETag`,
  `Last-Modified`), with `Cache-Control: no-store` and `nosniff`; no `Set-Cookie`, no CORS header. A
  request for a service worker script (`Service-Worker` header) is refused, so nothing registered on
  the headset's address can answer a later preview. Files over 32 MiB are cut off; 15 seconds a request.
- **The headset relays its browser to it.** `PreviewRelay` (client core) listens on the headset's
  127.0.0.1, takes only GET and HEAD with `Host: 127.0.0.1:<its port>` (a page renamed to that address
  reads nothing), one request a connection, and asks the computer for the same path over the pinned
  connection (`PairedReach`, the credential in its own header). The path must stay inside the preview
  before anything is sent (`PreviewRelay.InsidePreview`): no `.` or `..` segment, no backslash, no
  escaped dot, slash or backslash, and, once built, a path at or below the preview's own. Without that a
  URI's collapsing of dot segments let another app on the headset reach every GET route of the computer
  with the device's credential (AUDIT2, 2026-11-11). A refusal reaches the browser in the product's
  words, by its code (`PreviewRelay.WhyNot`), a 401 or 403 as "pair it again", never the computer's
  message.
- **The process stays up while the browser is in front.** The relay runs on threads of Halcyonic's own
  process. When Quest Browser takes the front, Halcyonic's activity stops and Android may freeze or end
  a process with nothing in the foreground, so a foreground service, `PreviewRelayService` (native layer,
  `specialUse`), is held only while a relay is open, with a quiet notification. Chosen over a relay
  inside the service in Java: the relay stays one tested implementation in the client core, and the
  service holds nothing but the process. Whether Horizon OS freezes the app without it is DEVICE's
  probe to tell; the service holds either way.
- **The headset's action that opens it** (start the relay, hold the service, open the URL in Quest
  Browser, release both when done) is DEVICE's through XR's plug; the call is agreed in LINK's notes.

## Alternatives considered

- **A raw TCP tunnel over a WebSocket per connection:** passes everything, including what the gate
  cannot see, and a page opens many connections at once, more than the listener holds per address.
- **HTTPS on the Mac's LAN address:** a certificate the browser does not trust; WebXR uncertain; a
  warning to click through.
- **A relay in the Android service in Java:** a second implementation of the pinned transports and the
  relay, outside the tested client core.

## Consequences

- With pairing on and a confined, passed check's preview running, the headset shows the app with no cable.
- The network listener now reaches one more thing on the Mac, a preview's port, gated per request: a
  surface for AUDIT2.
- Anything on the headset that can reach its loopback address can read the open preview while it is
  open: the person's own app, from a check that confined its agents, and nothing else of the computer's.
  So the relay is closed as soon as the browser leaves the preview, and the foreground service is held
  only that long (AUDIT2 S2).
- Not yet verified on a headset: Quest Browser offering WebXR on the relay's address, the foreground
  service under Horizon OS, the manifest entries XR adds.
