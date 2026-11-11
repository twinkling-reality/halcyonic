# Sharing a link to a checked game: a quick tunnel on this Mac (2026-11-11)

Verified for Share a Link (keeping and sharing what was made, F1, [ADR 0054](../decisions/0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md))
with Cloudflare's `cloudflared` 2026.10.7 for macOS on Apple silicon, on this Mac, against a
synthetic test page only. No project of the owner's and no preview of the owner's control plane was
ever behind a tunnel. The owner approved the download and the use in the TASKS session on 2026-11-11.

## The program

- Release 2026.10.7 of github.com/cloudflare/cloudflared, asset `cloudflared-darwin-arm64.tgz`,
  18.9 MB. SHA-256 of the archive `3586ccf397e98e5f437e60e7c307ca6ac463e110580503af8bb8e6f9ebf831bd`;
  of the binary in it `2136431d5eb467bf447d2ee514b713e192719b143aec1d9e09bf7dc290d5bb12`, which the
  binary also prints as its own checksum at start. Code-signed, team identifier `68WVV388M8`.
- The host runs it only when its bytes hash to that pin (`TUNNEL_PIN`, `isPinnedTunnel`); any other
  file at `HALCYONIC_TUNNEL_BIN` turns sharing off with a warning. It is never in the repository or
  a fixture; it is installed as the runtimes are, by a setup step.

## Verified

1. `cloudflared tunnel --no-autoupdate --url http://127.0.0.1:<port>` with `HOME` set to a folder of
   its own makes a quick tunnel with no account and no login: it prints
   `https://<words>.trycloudflare.com` on stderr about 4 s after it starts. It reads no
   configuration (it reports none found).
2. The new name may not resolve at once on this Mac's own resolver; Cloudflare's `1.1.1.1` knows it
   within seconds. The host resolves it there and connects to that address with the name kept for
   TLS, so the certificate is still verified for the name.
3. Node 24's `https.request` calls a custom `lookup` with `all: true` and refuses a lone address then
   (`ERR_INVALID_IP_ADDRESS`); `pinnedLookup` answers both forms. Tested offline.
4. The full path, `ProjectShares` with the real binary under its Seatbelt profile (writes only its
   home and `/dev/null`; no reads of the data folder), on a scratch control plane, against a
   synthetic page on 127.0.0.1:47911: `project.share.started` was recorded after the address
   answered 200 over verified `https://`, 18 s after the press. The project's view held the link.
   Stopped as asked: `project.share.stopped`, the view lost the link, and 4 s later the address
   answered Cloudflare's 530 (no origin). No tunnel process was left. The address was masked in every
   output and written nowhere.
5. The tunnel's profile refuses reads of the system folders it takes a configuration from (its log
   names them). Unit test: a file in a stand-in folder is readable outside the profile and refused
   under it ("Operation not permitted"). The real runs used the profile before that rule.
6. While it runs, the program also opens a metrics listener on 127.0.0.1 (port 20241), on loopback
   only, and an ICMP proxy for the tunnel's own traffic. Neither is reachable through the tunnel.

## From the documentation, not run here

- Cloudflare describes quick tunnels as for testing and development: no uptime guarantee, a limit of
  200 requests in flight. A shared link is for showing a game to a few
  people, never for hosting it.
- Vite's preview answers only hosts it allows (`preview.allowedHosts`: `localhost`, `.localhost` and
  IP addresses by default), so the tunnel rewrites the Host header to the preview's own
  `127.0.0.1:<port>` (`--http-host-header`). Not run through a tunnel against a real preview: the
  real runs used the synthetic page.
- WebXR needs a secure context. A link over `https://` is one, so another headset can enter VR from
  it; a plain `http://` address on the Wi-Fi is not one (Share on This Wi-Fi says so).

## Share on This Wi-Fi (2026-11-11)

Verified in unit tests only, on 127.0.0.1 with a stand-in preview: the listener passes `GET` and
`HEAD` with the preview's own Host and no cookie, answers `POST` and `PUT` with 405, passes back the
preview's own headers (its Content Security Policy), is recorded only once its address answered 200,
and refuses connections once stopped. Off any network it fails `no_network` and records nothing.
It takes at most 32 connections at once, with a 5 s keep-alive.

Not verified: a real share on the Mac's Wi-Fi address. Whether macOS's application firewall asks
the person to allow incoming connections the first time, a device on the network reaching it (a
router that isolates its clients blocks it), and on the headset that a page at a LAN `http://`
address has no immersive session (asked of FIELD).

## The code to scan (2026-11-11)

Verified on this Mac: `share-code.ts` encodes the address as a QR code (byte mode, level M, versions
1 to 13, the smallest that holds it), and macOS's own detector (Core Image's `CIDetector` of type QR
code, through JavaScript for Automation, nothing installed) reads back exactly the text it was given:
- a link, a Wi-Fi address, and UTF-8 text;
- each version from 1 to 13 at its full capacity, so every version's block table, alignment
  positions and, from version 7, version information are right.

The test renders 8 pixels a cell with a 4-cell margin. Not verified: a real phone or headset camera
reading one from the headset's panel, which waits for the owner (size on the panel, glare, the
headset's own scanner).

## Not verified

- A real project's preview behind the tunnel on the owner's Mac (by design: never before AUDIT2).
- How long a quick tunnel lives when left running, and whether Cloudflare ends one on its own.
- Two shares at once: the second program's metrics listener takes the next free port.
