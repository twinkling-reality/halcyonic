# Pairing a headset over the local network

- **Question:** Can the owner's Quest reach the control plane on the Mac over Wi-Fi, securely,
  with what the headset's Unity player and the control plane's Node.js offer and no new
  dependency? In particular, do Unity 6000.3's `ClientWebSocket` and `HttpClient` under IL2CPP on
  Android honor a custom certificate validation callback, and if not, what does?
- **Date:** 2026-09-29.
- **Environment:** macOS 26.7 on an Apple M5 Max; Node.js 24.15.0; the .NET 10.0.401 SDK;
  Unity 6000.3.25f1 with its MonoBleedingEdge class libraries and tools; OpenSSL 3.6.4 from
  Homebrew, for inspecting certificates only. No headset.
- **Method:** the class libraries the Android player links, disassembled with the `monodis`
  Unity ships, and compared with the editor's; the specifications (RFC 5054 from rfc-editor.org,
  Node's crypto documentation, Apple's platform security guide); both SRP implementations run
  against RFC 5054's test vectors and each other; the control plane's listener exercised over real
  TLS by node tests; the client core exercised on .NET 10, against in-process TLS servers and a
  real control plane; and the client core run inside the Unity editor, on Unity's Mono and TLS,
  against a real control plane (a scratch project, not committed). The Unity project compiled in
  batch mode, and a development APK built.
- **Status:** Verified off the headset: the protocol, the listener, the pinned transports on .NET
  and in Unity's Mono with UnityTLS, and the Android class libraries' behavior by disassembly.
  Not verified: anything on the headset (listed at the end).

## Findings

### Which class libraries the headset runs

The Android player is built against `MonoBleedingEdge/lib/mono/unityaot-linux`: the project's
player build graph (`Library/Bee/Player*.dag.json`) names only that profile's assemblies. The
editor on macOS runs `unityjit-macos`.

### Certificate callbacks in the Android class libraries

From `monodis` of `unityaot-linux/System.dll` and `System.Net.Http.dll`:

- **`ClientWebSocket` does not pin.** `WebSocketHandle.ConnectAsyncCore` wraps the socket with
  `new SslStream(stream)` and calls `AuthenticateAsClientAsync(host, clientCertificates, Tls |
  Tls11 | Tls12, false)`. `ClientWebSocketOptions.RemoteCertificateValidationCallback` is stored
  and never read.
- **`ServicePointManager.ServerCertificateValidationCallback` does not reach it either.**
  `Mono.Net.Security.ChainValidationHelper` falls back to it only for an `HttpWebRequest`, or
  when the stream's settings set `UseServicePointManagerCallback`, which only `SmtpClient` does.
- **`HttpClientHandler.ServerCertificateCustomValidationCallback` fails.** The setter wraps the
  callback in `ConnectHelper.CertificateCallbackMapper`, whose delegate casts its sender to
  `string` (`castclass [mscorlib]System.String`). The default handler, `MonoWebRequestHandler`,
  hands it to `HttpWebRequest`, and `ChainValidationHelper.InvokeCallback` passes the
  `HttpWebRequest` as the sender, so the cast throws during validation and the handshake fails.
  The editor's `unityjit-macos` delegate tests the sender with `isinst` instead, so the editor
  works where the headset would fail; an editor test of this proves nothing about the headset.
- **A callback given to `SslStream`'s constructor decides.** `UnityTlsContext.VerifyCallback`
  calls `MobileTlsContext.ValidateCertificate`, which goes through `ChainValidationHelper` to the
  callback, and returns success or `NOT_TRUSTED` from its answer.
- **`WebSocket.CreateFromStream` is implemented,** by `ManagedWebSocket`, the class
  `ClientWebSocket` uses and that the Quest already runs over `ws://`
  ([quest-3-device.md](quest-3-device.md)).
- `WebSocketHandle` and `UnityTlsContext` disassemble identically in both profiles, and
  `ChainValidationHelper` differs only in how `monodis` prints one type reference, so the editor's
  runtime behavior for these stands for the headset's; the HTTP mapper above is the exception.

So Halcyonic's pinned transports open the socket, wrap it in `SslStream` with their own callback,
and speak the WebSocket upgrade and HTTP/1.1 themselves (ADR 0017).

### Cryptography on each side

- `unityaot-linux`: `ECDiffieHellman.Create()` and `ECDsa.Create()` throw
  `NotImplementedException` (`System.Core.dll`). `BigInteger.ModPow`, the
  `BigInteger(ReadOnlySpan<byte>, isUnsigned, isBigEndian)` constructor and
  `ToByteArray(isUnsigned, isBigEndian)` are implemented (`System.Numerics.dll`).
- Node.js 24 ([Crypto](https://nodejs.org/docs/latest-v24.x/api/crypto.html)): ECDH, key
  generation and signatures, but no point addition, no scalar multiplication of an arbitrary
  point and no hash to curve; `X509Certificate` parses and cannot issue. `crypto.sign` with an EC
  key returns a DER signature by default.
- Apple's [HomeKit communication security](https://support.apple.com/guide/security/sec3a881ccb1/web)
  pairs with "the Secure Remote Password (3072-bit) protocol with an eight-digit code".
- Both SRP-6a implementations (`apps/control-plane/src/network/srp.ts`,
  `Halcyonic.Client.Srp6a`) reproduce every value of RFC 5054 Appendix B (k, x, v, A, B, u and
  the premaster secret, with its 1024-bit group and SHA-1), and compute the same pairing vectors
  (`fixtures/pairing/vectors.json`, the 3072-bit group with SHA-256). A full exchange in the
  3072-bit group took about 90 ms of CPU in Node on this Mac.

### The listener's certificate

The control plane builds its certificate as DER itself: ECDSA P-256, version 3, not a
certificate authority, digital signature only, server authentication. Node parses and verifies
it, `openssl x509 -text` reads the extensions as intended, Node serves TLS 1.2 and 1.3 with it,
and .NET 10's `SslStream` and Unity's TLS both complete handshakes with it through a pinning
callback. The pin is the SHA-256 of the DER; it survives restarts because the key and certificate
are kept in the data directory, and the tests check both.

### Runtime checks off the headset

- **Control plane (node tests, real TLS):** pairing with the code; a wrong code, three of them
  closing the window, and the right code refused after; a relay that terminates TLS with its own
  certificate and passes every message on, refused; a recorded exchange replayed on a new
  connection, refused; pairing outside a window refused before any cryptography; one exchange per
  address at a time and six connections a minute; a 30 second time box; another protocol version,
  out of order messages and an `A` of 0 mod N refused; control characters in the label refused;
  device credentials only on the network listener and the access token only on loopback; foreign
  `Host` values, `Origin` and cross-site fetches refused; device management absent from the
  network listener; the principal journaled for REST and WebSocket commands; revocation closing a
  live connection and refusing the credential; a device revoking itself; a pinned client sending
  nothing to another certificate; 30 failed credentials a minute, then 429; device events kept
  from realtime clients; nothing logged holding the code, the credential, its hash or the token.
- **Client core (.NET 10):** the pin decides the handshake and nothing reaches an impostor; HTTP
  bodies by length, chunks and to the end; a refused upgrade reports the control plane's code; a
  wrong accept key is refused; an upgrade never answered ends with its token; the pairing file;
  against a real control plane: pairing, directing an approval to a finished turn over the pinned
  WebSocket, history over the pinned REST handler, every command journaled with the device as its
  principal, wrong codes and the lock, a relay in the middle, revocation, forgetting, and another
  identity on the same port refused without anything sent.
- **Inside the Unity editor** (Mono 6.13.0, `Mono.Unity.UnityTlsProvider`), against a real
  control plane on scratch ports: a wrong code refused; pairing, with the listener's certificate
  pinned; a realtime session live over the pinned WebSocket and a command acknowledged; REST
  answering over the pinned handler; a certificate other than the pin refused in the handshake;
  forgetting revoking the credential. For comparison, `ClientWebSocket` with its options' callback,
  and again with `ServicePointManager`'s, failed with `UNITYTLS_X509VERIFY_FLAG_NOT_TRUSTED`
  without calling either; `HttpClientHandler`'s callback worked there, as its editor library
  predicts and the Android one does not.
- **Unity:** the project compiled in batch mode with no error and no warning in Halcyonic's
  assemblies, `Halcyonic.XR.Pairing` included, and a development APK built, so the Android-only
  path to app-internal storage compiled and IL2CPP converted the new code. That path asks
  `com.unity3d.player.UnityPlayer.currentActivity` for its files directory; the Android player's
  `UnityPlayer` constructor sets that field to the activity it runs in (`javap` of 6000.3.25f1's
  `classes.jar`).

## What needs a headset

- The pinned transports under IL2CPP with the Android build of UnityTLS: pairing, the realtime
  session and REST over Wi-Fi, and a certificate mismatch refused.
- Reaching the Mac over Wi-Fi at all: the Mac's firewall, the router (client isolation on guest
  networks blocks it), and whether Horizon OS resolves `.local` names.
- The pairing panel: the system keyboard for the address and the number pad for the code, with
  hands; the second press that forgets; the panel out of the way of the stage and the room
  controls.
- The pairing file in app-internal storage, kept across restarts and reinstalls with `-r`, and
  removed by clearing the app's data.
- `SystemInfo.deviceModel` as the label the Mac lists.
- The SRP exchange's time on the headset's CPU (estimated well under a second).

## Consequences

- Keep `ClientWebSocketTransport` for `ws://` over USB, which works; use the pinned transports for
  every paired connection. Do not pass certificate callbacks to `ClientWebSocket` or
  `HttpClientHandler` anywhere in the XR client.
- A check of certificate handling in the editor is not a check of the headset where the class
  libraries differ; this record names where they do.
- Re-verify after any Unity upgrade: the disassembly is of 6000.3.25f1's libraries.
