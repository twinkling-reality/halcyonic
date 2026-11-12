# ADR 0047: Pairing starts from a page on the Mac, and the listener turns on without a restart

- Status: Accepted
- Date: 2026-11-10

## Context

Pairing a headset over Wi-Fi ([ADR 0017](0017-pair-a-headset-over-the-local-network.md)) needed a
terminal three times: `pnpm mac-setup pairing on`, stopping and starting the control plane (the
network listener was read only at start, and a restart stops every agent at work), and `pnpm pair`,
which printed the address and the code in that terminal. Someone who never opens a terminal could
not pair, and the owner's direction (2026-11-10) is that anyone, at any level, can enjoy developing in
VR. The facts, read in the code on 2026-11-10: the control plane refuses every browser request (any
`Origin`, a cross-site fetch, a Host other than its loopback address), so no page can talk to it; the
play viewer (P-A, P-B, reviewed by AUDIT) is the one precedent for a page on the Mac, a small server of
its own on 127.0.0.1 opened in the default browser with a one-time link; opening a pairing window is
one call on `Pairing` in the same process.

## Decision

- **The pairing page.** The control plane serves, from its start, a page of its own on 127.0.0.1,
  port 47802 or the next free one up to 47809 (47800 the control plane, 47801 the network listener);
  the ready log names it. (Amended 2026-11-12: the range follows the control plane's own port, its port
  plus 2 to plus 9, so a second control plane, such as a test's or a scratch one, never takes a port of
  47800's; a control plane on port 0 gets a free port for its page as well. The same holds for the whole
  family, `portFamily` in config.ts: the network listener's default is the plane's port plus 1; previews,
  play viewers and shares on this Wi-Fi keep 47800's layout (plus 130, 140 and 170) moved to the plane's
  port and 1000 higher, so they never fall in 47800's family, which spans the lanes' and tests' ports,
  and stay below the system's own (49152 up); a plane on port 0 lays them out from 47800's, 1000 higher.) Like the play viewer's: it holds no access token and no device credential;
  a link carries a one-time code (60 seconds) that becomes an HttpOnly, SameSite=Strict cookie for its
  port; every request must name `127.0.0.1:<its port>` as Host and come from no page or its own origin;
  every press is a POST from its own origin; a strict Content-Security-Policy; at most 8 browsers
  hold it at once. It shows pairing on or off, the headsets paired (connected or not), and, after the
  person presses Pair Headset, the headset's route ("In Headset · Settings · Pair Computer", one
  constant), the name to type (the Mac's `.local` name first) and the code, with the time left and
  the tries left. It never shows a loopback address, which no headset can reach, and shows no code
  beside no name: it says to join Wi-Fi instead. It can turn pairing on, turn it off after a second
  press that says paired headsets disconnect (the one way to end pairing, a window included), open a
  pairing window, and forget a headset after a second press. It reaches nothing else: no project,
  command or journal read. Its words follow the product's voice and never name a tool, the Mac or
  the product; its look follows the headset's panels (FIELD, 2026-11-10), in Inter, served with its
  licence from the page's own folder.
- **Nothing opens on its own.** Loading the page, opening its link and starting the control plane
  turn on no listener, open no window and show no code: each waits for the person's press.
- **One path for the listener.** `NetworkSwitch` turns the network listener on (the same certificate,
  a new `Pairing` and server) and off, at start when the settings say so and later from the page.
  Turning on writes the same setting `pnpm mac-setup pairing on` writes, only after the listener has
  started, so one that fails never stops the next start; turning off removes it after the listener has
  stopped. No restart: agents at work keep working. Turning off ends every paired device's connection;
  their pairings stay, and a device connects again, under the same pin, once pairing is on again.
- **Links.** `POST /api/pairing/page`, on loopback with the access token, answers a
  `PairingPageResponse`, one link. `pnpm pair` asks for one and opens it in the browser; `pnpm pair
  --here` pairs in the terminal as before, and so does `pnpm pair` against a control plane with no
  page (409 `page_off`). `pnpm start --open-pairing-page` opens a link once the control plane is ready.
- **The link never travels in a process's arguments,** where any process on the Mac could read it for
  a moment (AUDIT S4 on the play viewer): `openLinkPrivately` writes it into a small page, mode 600,
  in `<data dir>/open-links` (mode 700), opens that file by its path, which tells nothing, and removes
  it after a minute, when the code has run out. The browser goes on from the file to the link, a
  navigation from another origin, so the trade of a code alone is taken from any site as a top-level
  navigation, and answered with the cookie and a page that goes on to the page itself, never a
  redirect, which would keep that navigation's other origin. Every cookie of the page's name is read,
  so one another 127.0.0.1 port planted ahead of the person's own keeps nobody out (AUDIT S2).
- **`Start Halcyonic.command`** at the repository root starts the control plane from Finder with a
  double-click, in a login shell, with `--open-pairing-page`.

## Alternatives considered

- A page served by the control plane itself: it would have to accept browsers, which its loopback
  rules refuse on purpose (DNS rebinding, a page driving agents); a separate server that can do only
  pairing keeps that rule whole.
- A native window or menu bar app: needs packaging and signing, which ADR 0024 leaves open.
- Printing the code at start: opens a window nobody asked for, and the terminal is what this removes.
- Restarting to turn the listener on: stops every agent at work.

## Consequences

- Pairing needs no terminal once the control plane runs; the first install still does (Node, pnpm,
  the clone, the agent apps), which is ADR 0024's packaging.
- A second loopback server on the Mac, with its own checks, for AUDIT to read with every change.
- The listener can now start after the control plane: its startup order changed, and stopping it at
  run time is new. A connection whose TLS handshake completes while it stops waits for the idle
  timeout (60 seconds) before the stop finishes, as at shutdown.
- Revisit when Halcyonic is packaged as an app, which would carry this page as its own window.
