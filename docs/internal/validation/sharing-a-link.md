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
5. While it runs, the program also opens a metrics listener on 127.0.0.1 (port 20241), on loopback
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

## Not verified

- A real project's preview behind the tunnel on the owner's Mac (by design: never before AUDIT2).
- How long a quick tunnel lives when left running, and whether Cloudflare ends one on its own.
- Two shares at once: the second program's metrics listener takes the next free port.
