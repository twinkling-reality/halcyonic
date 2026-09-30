# ADR 0017: Pair a headset over the local network with a code, SRP and a pinned certificate

- Status: Proposed
- Date: 2026-09-29

## Context

The control plane serves loopback only. A Quest reaches it over USB: `adb reverse` makes the
headset's loopback reach the Mac, and the owner pushes the access token with `adb`
([quest-3-device.md](../validation/quest-3-device.md)). The owner wants the headset to reach the
control plane on the Mac over Wi-Fi, without a cable, securely, with loopback only remaining the
default. [SECURITY.md](../architecture/SECURITY.md) lists what must exist before Halcyonic serves
beyond one machine: device pairing, encrypted transport, per-device authorization and token
rotation (a remote relay stays out of scope: the Mac and the headset share a network).

Verified on 2026-09-29, with the evidence in
[network-pairing.md](../validation/network-pairing.md):

- **The Android player's class libraries.** Unity 6000.3.25f1 builds the Android IL2CPP player
  against `MonoBleedingEdge/lib/mono/unityaot-linux` (the project's build graph lists those
  assemblies). Disassembled with the `monodis` Unity ships:
  - `ClientWebSocket` opens `wss://` with `new SslStream(stream)`, without passing
    `ClientWebSocketOptions.RemoteCertificateValidationCallback`. The property is stored and
    ignored.
  - A plain `SslStream` consults `ServicePointManager.ServerCertificateValidationCallback` only
    when its settings ask for it, and only `SmtpClient` asks. That callback does not reach
    `ClientWebSocket` either.
  - `HttpClientHandler.ServerCertificateCustomValidationCallback` is wrapped in a delegate that
    casts its sender to `string`, while the handler it delegates to (`MonoWebRequestHandler`, over
    `HttpWebRequest`) passes the `HttpWebRequest` as the sender: the cast throws during validation,
    so every request fails. The editor's libraries (`unityjit-macos`) test the sender with
    `isinst` instead, so the editor passes where the headset would fail.
  - The callback given to `SslStream`'s constructor is honored: UnityTLS's verify callback calls
    `MobileTlsContext.ValidateCertificate`, which calls it, and its answer decides the handshake.
  - `WebSocket.CreateFromStream` is implemented, by the same `ManagedWebSocket` that
    `ClientWebSocket` uses and that already works on the Quest over `ws://`.
  - `ECDiffieHellman.Create` and `ECDsa.Create` throw `NotImplementedException`.
    `BigInteger.ModPow` is implemented.
- **At runtime in the Unity editor** (Mono with UnityTLS, whose WebSocket and TLS classes
  disassemble the same as the headset's): `ClientWebSocket` refused a self-signed certificate
  with `UNITYTLS_X509VERIFY_FLAG_NOT_TRUSTED` without calling its options' callback or
  `ServicePointManager`'s, while a pinning `SslStream` callback with `WebSocket.CreateFromStream`
  paired with a real control plane and carried a live session.
- **Node.js 24's crypto** offers ECDH, signatures and certificate parsing, but no elliptic curve
  point addition, no scalar multiplication of an arbitrary point and no hash to curve, and it
  cannot issue a certificate ([nodejs.org, Crypto](https://nodejs.org/docs/latest-v24.x/api/crypto.html)).
- **Precedent.** HomeKit pairs an accessory "using the Secure Remote Password (3072-bit) protocol
  with an eight-digit code" ([Apple Platform Security, HomeKit communication
  security](https://support.apple.com/guide/security/sec3a881ccb1/web)). RFC 5054 defines SRP-6a's
  formulas, its 3072-bit group and test vectors.
- The rules for this change allow no new dependency and no download.

## Options

### Discovery: how the headset finds the Mac

| Option | For | Against |
| --- | --- | --- |
| mDNS/NSD: the control plane advertises, the headset browses | Nothing to type | On the headset, Android's `NsdManager` through JNI or a plugin, and a multicast lock (`CHANGE_WIFI_MULTICAST_STATE`); on the Mac, an advertiser (`dns-sd` as a child process, or an mDNS library, a new dependency). Unverified on Horizon OS. Announces the service to everyone on the network. Proves nothing: any device can advertise, so pairing must authenticate the Mac anyway |
| A typed address | No permission, no multicast, works wherever the headset can reach the Mac; `pnpm pair` prints the address beside the code | The person types `192.168.1.23:47801` once with the system keyboard; an address the router changes later needs pairing again |
| adb-assisted first pairing | Nothing crosses the network before it is authenticated | Needs the cable once, which the goal rules out; the USB path stays available as it is |

**Chosen: the typed address**, remembered with the pairing. mDNS can be added later as a
convenience without changing the protocol, since discovery is never trusted.

### Pairing proof: how the headset proves it saw the code, and learns the Mac is the Mac

| Option | Security against an active attacker on the network | Cost here |
| --- | --- | --- |
| A PAKE: SPAKE2 (RFC 9382) or CPace | One online guess per attempt; nothing to test offline | Elliptic curve group operations that neither Node's crypto nor the headset's class libraries expose: a new dependency on both sides, or curve arithmetic written by hand |
| A PAKE: SRP-6a (RFC 5054), as HomeKit | One online guess per attempt; nothing to test offline | Modular exponentiation and SHA-256, which both sides have (`BigInt`, `BigInteger.ModPow`); about a hundred lines on each side, checked against RFC 5054's test vectors and against each other |
| An HMAC of the code over the certificate fingerprint | Whichever side proves first hands an attacker in the middle an offline test: eight digits fall in milliseconds, then the attacker pairs itself | Trivial; safe only with a code of 12 or more random characters and a slow key derivation |
| Numeric comparison: both screens show six digits derived from committed nonces over TLS | One in a million per attempt, if the person compares | Nothing is typed, but the person must compare two screens and confirm on both; the Mac needs an interactive confirmation |
| A credential pushed with adb, or read from a QR code | Strong | A cable, or the passthrough camera API with its permission and a QR decoder |

**Chosen: SRP-6a** with RFC 5054's 3072-bit group and SHA-256, an eight-digit code, and the
TLS certificate the headset saw bound into the proof.

### Transport

| Option | For | Against |
| --- | --- | --- |
| `wss://` and `https://` with a self-signed certificate the headset pins at pairing | Standard TLS on both sides; the pin replaces a certificate authority | On the headset, `ClientWebSocket` and `HttpClientHandler` cannot pin (above), so Halcyonic opens the socket, wraps it in `SslStream` with its own callback, performs the WebSocket upgrade itself and hands the stream to `WebSocket.CreateFromStream`; REST gets a small HTTP/1.1 client over the same pinned stream |
| `ClientWebSocket` with a process-wide callback in `Mono.Security`'s `MonoTlsSettings.DefaultSettings` | No transport code | Global state for every TLS connection in the process, Mono-specific, and impossible to test on .NET |
| A certificate from a public authority | Ordinary validation | Needs a public name for a private address; nothing on a LAN offers one |
| Mutual TLS with a client certificate | Standard | `SslStream` needs the private key in managed memory anyway, and UnityTLS client certificates are unverified; a bearer credential inside a pinned channel gives the same assurance |
| Application-level encryption over `ws://` | No TLS | Custom cryptography for no gain |

**Chosen: pinned `wss://` and `https://`** on a second listener, opt-in, with a self-signed ECDSA
P-256 certificate kept in the data directory. The pin is the SHA-256 of the certificate.

### The credential, and where it lives on the headset

- **A bearer credential per device**: 32 random bytes, written `hlcd_` and 43 base64url
  characters. It is sent once, at pairing, encrypted under the SRP session key inside the TLS
  channel, and then on every request as `Authorization: Bearer`. The control plane keeps only its
  SHA-256, in the journal.
- **Storage on the headset**: app-internal storage (`Context.getFilesDir()`, readable only by the
  app's Linux user), behind `IPairingStore` in the client core. The Android Keystore would wrap
  the file with a key that never leaves the device: that defeats copying the file off the headset,
  not someone who runs the app on it, needs JNI calls unverified on Horizon OS, and stays behind
  the interface for later.

### Authorization, rotation and revocation

- **Principals.** `local` is whoever holds the access token, which is accepted only on loopback.
  `device` is a paired device's credential, accepted only on the network listener. Every command's
  record names its principal. Devices cannot open pairing, list devices or revoke another; each
  can revoke itself, which is what forgetting the Mac on the headset does. Policy categories still
  apply alike to both (no `high_consequence` command exists).
- **Revocation** (`pnpm devices revoke`) is journaled and immediate: the credential is refused and
  the device's open connections close.
- **Rotation**: revoke and pair again. The TLS identity rotates by deleting its key and certificate,
  after which every device pairs again. Credentials do not expire.

### What a stolen headset or a hostile device on the same Wi-Fi can do

Summarized in [SECURITY.md](../architecture/SECURITY.md): a hostile device learns that a TLS
service listens, cannot pair outside a window, gets at most three guesses at an eight-digit code
inside one, cannot read or alter traffic to a paired device, and can deny service. A stolen
headset can do what its owner could over that network until the owner revokes it.

## Decision

- **Opt-in network listener.** Loopback stays the default and unchanged. `HALCYONIC_NETWORK_HOST`
  (an IP address such as `0.0.0.0`) turns on a second listener, TLS only, on
  `HALCYONIC_NETWORK_PORT` (default 47801). Its certificate is self-signed, ECDSA P-256, generated
  on first use into `<data dir>/network-key.pem` (mode 0600) and `network-certificate.pem`.
- **What each listener serves.** Loopback: everything as before with the access token, plus
  pairing windows and the device list and revocation. Network: `GET /api/health`, the pairing
  WebSocket `/pair`, and, with a device credential, the realtime stream, REST reads and commands,
  and `POST /api/device/revoke` (a device revokes itself). The access token is never accepted on the
  network listener. Both refuse any `Origin` and cross-site fetches; the network listener accepts
  only an IP address or a `.local` name, with its port, as `Host`. It waits 10 seconds for a whole
  request, closes a silent connection after 30 and an idle one after 5, and holds 32 connections,
  4 of them realtime per device.
- **Pairing window.** `pnpm pair` opens one through loopback and prints the addresses, the port and
  an eight-digit code. One window at a time, for five minutes, closed by the first device that
  pairs, by three failed attempts, by `Ctrl-C`, or by the control plane stopping. Outside a window
  `/pair` is refused before any cryptography. At most four pairing connections at once, one per
  address, six per address a minute, 30 seconds each. The window derives the salt and the SRP
  verifier from the code when it opens, so an exchange's work depends only on its own secret `b`
  and tells a timing observer nothing about the code. `pnpm pair` prints each connection the
  window turned away or cut short without checking a code, with its address, so the owner sees a
  device holding the slots.
- **Pairing protocol** over the `/pair` WebSocket, one exchange per connection: the headset sends
  its label; the control plane answers with the window's salt and a fresh `B`; the headset sends
  `A` and its proof; the control plane checks the proof, records the device, and answers with its
  own proof and the credential encrypted under the session key. Both proofs are HMACs keyed by the
  SRP session key over a transcript that includes the label, the salt, `A`, `B` and the SHA-256 of
  the TLS certificate as each side knows it, so a proof computed through a relay that terminates
  TLS fails. The code, the session key and the credential are never logged or journaled.
- **Pinning.** The headset keeps the address, the certificate's SHA-256, its device id and the
  credential. Every later connection refuses a certificate with another hash.
- **Revocation** applies to what a device has open, not only to its next request. Its realtime
  connections stop handling anything when the revocation is journaled, get a close frame, and are
  cut off a second later. A device is authorized again where a command would act, so a command
  from a request or connection authenticated before the revocation is rejected with
  `device_revoked`, and any answer to such a request is replaced by 401.
- **Journal.** `device.paired` (device id, self-declared label, SHA-256 of the credential and of
  the certificate) and `device.revoked` (device id, who revoked it) are journaled through
  `Recorder`; the device registry is part of the projection. `command.accepted` and
  `command.rejected` record the principal (`local`, `device` with its id, or null for commands
  from inside the control plane and for commands recorded before principals existed, which read
  as null). Device events are not sent to realtime clients, and a paired device does not read
  them from the event history.
- **Headset.** The client core holds the SRP client, the pinned transports and the pairing
  client, with no engine reference; `Halcyonic.XR.Pairing` adds a small pairing panel at runtime,
  in development builds.

## Alternatives considered

The tables above. SPAKE2 and CPace lost only on what the platforms expose; if a vetted
implementation becomes available on both sides, CPace is the successor. An HMAC over the
certificate with a short code lost because it is broken offline. Numeric comparison lost because it
depends on the person comparing two screens. mDNS lost for now on cost and verification, not on
security.

## Consequences

- The owner's headset can reach the Mac over Wi-Fi without `adb`, and a headset can be told apart
  from the owner's local token in the journal.
- A second listener and its certificate are new attack surface, off unless the owner turns them
  on. The owner's Mac firewall may ask whether `node` may accept incoming connections.
- Halcyonic now carries a WebSocket upgrade, an HTTP/1.1 client and an SRP implementation of its
  own, each tested against a real control plane and SRP's published vectors.
- Every command event gained `principal`, so the traces and the recorded demonstration were
  recorded again; journals written before this change read as before.
- Still to verify on a Quest 3: the pinned transports under IL2CPP and UnityTLS, reaching the Mac
  over Wi-Fi, and the pairing panel with the system keyboard.
- Revisit for a remote relay, for more than one person per control plane, for mDNS, for the
  Keystore, or when a vetted CPace is available on both sides.
