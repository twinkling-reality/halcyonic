# The headset's loopback proof

- **Question:** Can the XR client ask the control plane to prove it holds the access token, for the
  address and port it dialled, before the token goes on the realtime upgrade or a REST request, as
  `pnpm devices` and `pnpm mac-setup --with-token` do, so a listener that took the port gets no
  token?
- **Date:** 2026-10-02.
- **Versions:** Halcyonic branch lane-g-xr-proof on main 02a8d3c, then lane-g-xr-upgrade on main
  483302c; .NET 10 for the client core's tests; Node.js 24
  for the real control plane in them; Unity 6000.3.25f1 for Android (IL2CPP), whose
  `unityaot-linux` class libraries were read with its `monodis`.
- **Method:** Code and tests against servers on loopback that answer as each test needs, and
  against a real control plane; mutations that each remove one check; an independent security
  review, whose High finding was reproduced and fixed. Nothing was run on a headset.
- **Status:** Verified in code and tests. On a Quest 3, in part, on 2026-10-04 (below).

## Verified

- `LoopbackProof.Compute` gives the control plane's own proof: the vectors in `LoopbackProofTests`
  come from `loopbackProof` in `apps/control-plane/src/http/security.ts`, for 127.0.0.1:47800 and
  [::1]:47800.
- Only a literal loopback address is asked or sent the token. `localhost`, another address,
  0.0.0.0 and an IPv4 address written as IPv6 (which the control plane's host check refuses) are
  refused before any connection is opened, and a session given one ends instead of trying again.
- `LoopbackProofHandler`, which `ControlPlaneApi` uses whenever it has no handler of its own (the
  access token's case; a paired control plane keeps its pinned handler), speaks HTTP/1.1 itself
  through `Http1`, as the pinned handler does, over a `TcpClient` to the literal address, so no
  proxy and no connection pool are involved:
  - each request opens a connection, asks the proof on it without the token, and sends the
    request with the token on that same connection, then closes it;
  - two requests use two connections; a server that stops proving gets a new connection for each
    proof and no token after it fails;
  - no token goes to an answer without a proof, a proof under another token, the real proof for
    another port or address (as a relay passes it on), two proofs, a proof in capitals, an answer
    whose head is not HTTP, or a proof on a connection the server is closing (a peer that never
    ends its head times out as nothing answering, and is tried again, sending nothing);
  - a caller that cancelled, before or during the proof, hears it was cancelled, and an answer
    after the proof that is too large, not HTTP or badly chunked is a failed request
    (`HttpRequestException`), as the pinned handler now reports it too;
  - an authorization the caller set is left out, and the token appears once;
  - a 307 comes back as it is; the server it names gets no connection.
- The review's case: an impostor on the port answers with keep-alive and no proof, then stops
  listening and keeps the connection; the real control plane starts on the same port. On the
  reviewed commit, which sent REST requests through `HttpClient`'s pooled handler, the next proof
  went over the impostor's kept connection (a relaying impostor would then have received the
  token, as the reviewer showed); now the next request opens its own connection, the control
  plane proves itself, and the kept connection carries nothing more.
- `LoopbackWebSocketTransport`, which replaced the transport over `ClientWebSocket`, asks the proof
  on a connection of its own and performs the upgrade with the token on that same connection, so
  the realtime path has no moment between proof and token either; a 401 is read from the upgrade's
  own answer. Node accepts the upgrade as the second request on a kept-alive connection: every live
  session test reaches the real control plane this way. The transport it replaced asked the proof
  just before `ClientWebSocket` opened a connection of its own (which, in
  `unityaot-linux/System.dll`, reads no proxy and accepts only `HTTP/1.1 101`, so it follows no
  redirect).
- `Http1`, shared with the pinned transports, refuses a response framed more than one way (two
  lengths, or a length with chunks), a transfer coding other than chunked, a folded header, a bare
  line break in the head or a chunk line, a chunk size longer than 8 hex digits, or chunk data not
  followed straight away by CRLF, and reads a trailer to its blank line, so a request that follows
  on the connection reads its own answer. A 101 must carry `Upgrade: websocket`,
  `Connection: Upgrade` (as a token in the list) and the right `Sec-WebSocket-Accept`. An access-token
  endpoint that is not `ws://` (or, for REST, `http://`) at 127.0.0.1 or [::1] ends the session with
  the line WORDS.md gives, and REST reports it as a failed request; nothing is dialled. Only 127.0.0.1 and ::1 themselves are loopback addresses here, not the rest of
  127.0.0.0/8. Mutations that undo each of these fail the tests.
- Another program on the port answering everything receives only `GET /api/health` with a
  challenge from the REST client, the transport and a session, never the token or an
  authorization header. The session stops with `AccessRefused` and
  `ConnectionText.AccessTokenUnproved`.
- A real control plane proves its token, and a stale token is not proved. A session with a stale
  token stops with `AccessTokenUnproved`, and the control plane's request log shows proofs asked
  and no `/realtime` request and no 401, so it never received the token. A relay on another port
  that passes every byte to the real control plane, naming the control plane's port as the host,
  carries the control plane's real proof back, and the client refuses it, since it names the
  control plane's port; no token passes the relay.
- Mutations: sending without the proof in the handler, or connecting without it in the transport,
  fails the impostor tests; sending the request on a new connection after the proof fails the
  connection tests; letting a cancellation out as it comes fails the failure-kind test.
- A second pass of the same review found the High finding fixed, on Mono too, and the Lows it
  raised (cancellation and unread answers surfacing as other exceptions, test weaknesses) are
  fixed here.
- The C# suite passes, including every live test against a real control plane, which reaches it
  through the proof; a development APK builds.

## On a Quest, 2026-10-04

A Quest 3 on build `UP1A.231005.007.A1`, a development APK from main `ee1acdb9`
([quest-3-device.md](quest-3-device.md), sixth session):
- **Over USB, the proof held:** over `adb reverse tcp:47800 tcp:47800`, the control plane proved it
  holds the token for the headset's connection, and the realtime upgrade followed on that
  connection. `pnpm quest:check` read "connection live".
- **REST after the proof:** the headset's REST requests were answered: the folders, the history,
  an execution's understanding, the companion's status, and one transcription.
- **What that shows:**
  - adb delivers the headset's connections to 127.0.0.1:47800.
  - The app's own HTTP over `TcpClient`, `HMACSHA256` and `FixedTimeEquals` run under IL2CPP.

Still not seen on a Quest:
- the impostor listener;
- the listener that never answers;
- the paused control plane, and so how Mono ends a read that is never answered.

## Not verified

On a Quest; the check is in XR_DEVELOPMENT.md, "Token storage on a Quest":

- that the control plane sees a connection through `adb reverse tcp:47800 tcp:47800` arrive at
  127.0.0.1:47800, so the proof holds over USB (inferred from adb dialling the Mac's loopback for
  a `tcp:` target, IPv4 first, and the control plane listening on 127.0.0.1);
- that the REST requests' own HTTP and the WebSocket upgrade over `TcpClient`, `HMACSHA256` and
  `CryptographicOperations.FixedTimeEquals` work under IL2CPP on the headset (the APK compiles
  them; REST has not run on a headset before either, through `HttpClient`, while the realtime path
  this replaces, over `ClientWebSocket`, had);
- how Mono's `NetworkStream` ends a read when the connection is closed to cancel it, which both
  the REST handler and the realtime upgrade rely on to end a read that is never answered (on .NET
  the read honours the token itself, so the tests can't show it); this is inferred for Mono, not
  seen, and the Quest check pauses the control plane to see it.
- Run "Token storage on a Quest" before a headset session depends on a build with this change: the
  realtime path over USB now runs code that has not run on a headset, in place of one that had.

## Risks that remain

- The proof's address binding does not reach across `adb reverse`: anything that routes to the
  Mac's 47800, a second reverse mapping as much as an `ssh -L`, `socat` or a proxy on the Mac, lets
  whatever listens on the headset's 127.0.0.1:47800 relay a valid proof. And the Mac's adb server
  answers every local account without authenticating it, so another account can read the token
  with `run-as` or add such a mapping itself. SECURITY.md states these limits once, for the app and
  the glance.
- The proof can't tell a control plane holding another token from another program, so a stale
  token and an impostor read the same; the line says both.
