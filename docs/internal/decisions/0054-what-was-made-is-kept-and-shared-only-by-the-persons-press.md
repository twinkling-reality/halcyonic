# ADR 0054: What was made is kept and shared only by the person's press

- Status: Accepted; Show in Finder, Save a Copy, Share a Link and Share on This Wi-Fi built and
  tested off the headset; the headset's buttons pending
- Date: 2026-11-11

## Context

A person who made something with Halcyonic's agents had no way to take it out of Halcyonic: to find
its folder, keep a copy of it, or show the game to someone else (keeping and sharing what was made,
F1 and F3). The project's folder is on the Mac; a checked game is served only on the Mac's loopback,
by its preview (ADR 0037).

Facts, verified on this Mac on 2026-11-11:

- `git archive` of the checkpoint store's tree of a folder writes exactly the files a checkpoint
  keeps, never `.git` nor what the project ignores, and leaves out secrets by pathspec
  ([record](../validation/keeping-a-copy.md)).
- A quick tunnel of the pinned `cloudflared` 2026.10.7 gives a temporary `https://` address to one
  loopback origin with no account; the address answered over verified TLS within 18 s, and stopped
  answering within 4 s of the program stopping
  ([record](../validation/sharing-a-link.md)).
- WebXR needs a secure context: an `https://` link can enter VR on another headset; a plain
  `http://` address on the Wi-Fi cannot.

The owner approved Share on This Wi-Fi and Share a Link, and the download of the tunnel program, on
2026-11-11.

## Decision

1. **Show in Finder** (`project.show_in_finder`, low consequence) shows the project's own folder in
   Finder, selected. Never a task's copy or Halcyonic's data.
2. **Save a Copy** (`project.save_copy`, review required) writes a zip of the folder's checkpoint
   tree to the account's Downloads folder, under a name nothing has, and shows it in Finder. Never
   `.git`, what the project ignores, a file the one secret-file list names (`secretShapedPath`,
   whatever the case of its name), or another project's folder inside this one. Refused while a
   task in the project is at work, or a restore of it or an add to it awaits its outcome. The list
   goes by names only: a token kept in a file of another name is in the copy.
3. **Sharing** (`project.share.start` with a `way`, review required; `project.share.stop`, low
   consequence) gives the project's running preview an address, one share per project at a time.
   **Share a Link** (`link`) gives it a temporary public `https://` address:
   - Only the latest check, passed, still current, and `reads_confined` (an agent that could read
     outside its folder may have put the person's files in the build), and only when the running
     preview serves that check. Otherwise it is refused in the product's words.
   - Only by the person's press. Nothing in the host starts a share by itself.
   - The tunnel forwards to the preview's one origin, which serves that check's built copy, read
     only, with its own Content Security Policy. Nothing else of the Mac is behind it.
   - Recorded (`project.share.started`, with the address) only once the address answered 200 over
     verified `https://`. A start that gets no answer within 90 s records nothing and stops the
     program.
   - It stops, recorded as `project.share.stopped` with why, on the person's press (`asked`), when
     the preview stops (`preview_stopped`, so a new check ends it), when the program ends
     (`ended`), and when the control plane closes or a restart finds one recorded live
     (`host_stopped`). No tunnel survives a restart.
   - The program runs only when its bytes match the pin, under a Seatbelt profile that lets it write
     only a home of its own, read nothing of Halcyonic's data folder, and read none of the system
     folders it would take a configuration from (`/etc/cloudflared`, `/usr/local/etc/cloudflared`,
     `/opt/homebrew/etc/cloudflared`), so no configuration can add ingress or an origin to a share;
     no update of itself. It
     is installed as the runtimes are, by a setup step the person presses, never committed.
4. **Share on This Wi-Fi** (`wifi`) gives it the Mac's own address on its local network, on the
   same conditions, by the same press and stopped the same ways:
   - A listener on the Mac's private IPv4 address (10/8, 172.16/12, 192.168/16, the Wi-Fi's own
     first), at a port of 47970 to 47979, never every interface.
   - It passes only `GET` and `HEAD` to the preview on 127.0.0.1, under the preview's own Host, with
     no body, no cookie and only the headers a page load reads with; anything else is answered 405.
     So it reaches the same one built copy, read only, and nothing else of the Mac.
   - At most 32 connections at once, a 5 s keep-alive, headers within 10 s and a request within
     30 s, as the network listener bounds its own.
   - Recorded only once the address answered 200 from the Mac itself.
   - The headset says truly that a game opened from it plays in the browser but cannot enter VR: a
     plain `http://` page is not a secure page.

## Alternatives considered

- **Wi-Fi only.** No outside service, but no VR on another headset, which is what sharing a VR game
  is for. Kept as the second, local way.
- **A tunnel with an account (a named tunnel, or another provider).** A stable address, but a
  credential on the Mac and an account the person must make. Not needed to show a game for minutes.
- **Publishing to a static host.** A lasting address, but an account, a push of the person's files to
  a third party, and a takedown to manage. Asked separately; not built.
- **Sharing the live folder or a development server.** Rejected: it would serve unchecked work and
  whatever an agent writes next.

## Consequences

- Anyone with a live link can play the checked game, and Cloudflare carries its traffic. That is the
  person's choice at each press, and it ends with the preview.
- The address carries the tunnel service's domain. The headset must not print it as text (no brand
  names on screen); how it hands the link over is open (OPEN_QUESTIONS.md).
- Quick tunnels are for testing: no uptime guarantee and a cap of 200 requests in flight. A share is
  for showing a game to a few people.
- Revisit if the tunnel program's terms change, if quick tunnels need an account, or when a lasting
  address is wanted.
