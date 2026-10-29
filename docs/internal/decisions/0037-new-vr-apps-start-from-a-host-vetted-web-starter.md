# ADR 0037: New VR apps start from a web starter the host vets, and their code runs only in the headset's browser

- Status: Accepted
- Date: 2026-10-29
- Complements ADR 0020, ADR 0028 and ADR 0036

## Context

A person should be able to say one sentence in the headset and get a new VR app to try in the same headset. The
fastest loop for that is WebXR: a page the headset's browser opens, rebuilt in seconds. Meta's Immersive Web SDK
1.0 (MIT) ships a starter for it, but installed as Meta's own command makes it, it brings a binary under another
licence that edits the shell's settings, telemetry that is on by default, a browser download, and a release
candidate ([iwsdk-starter.md](../validation/iwsdk-starter.md)).

Halcyonic's agents run on a local model, in a sandbox with no network (ADR 0028; Codex's `workspace-write`), so an
agent cannot install anything. An agent's code is untrusted: if the Mac ran it, through a build configuration or a
plugin of the project's, it would run with the person's access.

## Decision

**A starter is vetted once and kept by the host.** Its source is tracked in this repository under `starters/`: the
template, adapted from Meta's with its licence and notices, the pinned `package.json` and lockfile, and the host's
own build configuration. Its dependencies are installed on each Mac, from the registry only, with no install scripts
and the lockfile's integrity hashes, into the host's data directory. A starter's own files fetch nothing at run
time: the template's models from a content network are left out. Two of its libraries still name outside hosts:
`@iwsdk/xr-input` loads controller profiles from a content network when a controller connects, and `@iwsdk/core`'s
glTF loader loads Draco and KTX2 decoders from another for a model compressed that way. The preview's
Content-Security-Policy blocks both until local copies are served.

**A project made from a starter** is an ordinary project in a folder the host makes (ADR 0020): the template's
files are copied in, and `node_modules` is a link to the shared, read-only set. No agent can change a dependency:
the set is outside every sandbox's writable folders. With undo on, the starter's files are checkpoint 0 (ADR 0036).

**The project never configures the Mac.** Previews and checks run Vite and TypeScript from the vetted set, with the
host's configuration passed explicitly, inside a sandbox with no outbound network that writes only to a cache
outside the project. The project's own Vite configuration, plugins, scripts and environment files are never loaded.
Agent-written code is transformed on the Mac and runs only in the headset's browser.

**The preview keeps the page to itself.** It is served on loopback, plain HTTP, from one port of a host range, with
a Content-Security-Policy that allows the page only its own origin. It reaches the headset through `adb reverse`
during development, opened in Quest Browser at `http://localhost:<port>/`.

**A check follows each turn:** `tsc --noEmit` and a build, each recorded with its exit code as the host's
observation. It says the app type checks and builds; it does not say it works or looks right.

## Alternatives considered

- **Meta's starter as its command installs it.** Rejected for the binary, the telemetry, the browser download and
  the release candidate above.
- **The agent installs dependencies.** It needs the network, so the sandbox would have to allow it, and every
  project would bring whatever the model asked for.
- **Serve the preview from the control plane.** The page would share the control plane's origin; the control plane
  refuses every request with an `Origin` header and must keep doing so.
- **Unity for new apps.** A one-script change takes about 25 seconds to build and 6 to install on this Mac, before
  the app starts; a web page reloads in under a second.

## Consequences

- A new app starts from a working page with no install, and every turn can be undone.
- Meta's agent tools for the SDK (its MCP servers, screenshots, console capture) are not offered; nothing emulates
  them. The skills' text may be given to the agent as reading.
- The headset leaves Halcyonic to try the app, as for any immersive app.
- `@drawcall/uikitml`, required by the SDK, states no licence; nothing containing it is published until the owner
  decides.
- Open: a trusted HTTPS route over Wi-Fi, opening Quest Browser straight into VR from an app, a one-button return,
  and whether Quest Browser treats `http://localhost` as a secure context for WebXR.
- Revisit when the SDK moves to 1.1 (a deliberate new vetting), or if a runtime gains verified network isolation
  that would let an agent install from a pinned list.

## Amendment, 2026-10-29: every game plays with a mouse and keyboard and in VR

The owner asked that every new game play both on a laptop and in the headset. The starter now carries one input
layer, `src/play-input.ts`, that a game reads every input through: pointing and pressing (a click, or the trigger or
a pinch), touching (a click on a laptop, a controller or hand within reach in VR), moving (W A S D or the arrows, or
the left thumbstick), two buttons (Space and E, or A and B), and dragging on empty space to turn the view on a
laptop. It builds on what the SDK already gives (`input.canvasPointerEvents` turns a mouse into the same scene
pointer events a ray makes; validation/iwsdk-starter.md). The template's `AGENTS.md` tells the agent to use only
this layer. Where the browser cannot start VR, the panel says how to play on a laptop instead of showing a VR button.

This amendment covers the layer and one exception to "runs only in the headset's browser": the host's check may
load the page in a headless browser on the Mac, to see that it draws and that a mouse press and a key reach the
layer. That browser runs inside a sandbox of the host's own that lets it connect only to the preview's port and
reads no private folder, with a profile of its own, and shows nothing to the person; the check's picture is its
only output. The amendment does not let the person play the app in a browser on the Mac: that would run
agent-written code where it can reach the Mac's loopback services and the person's browser profile, and needs its
own amendment for a contained browser.

The controller models `@iwsdk/xr-input` loads are now served by the host from a tracked, hash-checked listing
(SECURITY.md, Starters); the Draco and KTX2 decoders above are still blocked.

## Amendment, 2026-10-29: what the check proves

A type check and a build passed games that crashed in the browser (validation/iwsdk-starter.md: three of six showed a
white screen or no canvas). The check (`apps/control-plane/src/preview/check.ts`) now also opens the page:
`tsc --noEmit` and `vite build` inside a Seatbelt profile with no outbound connection, writes only in the check's
cache and the person's home folder unreadable but the project and the starter; then the built copy, served by
`vite preview` inside the same profile, opened for 10 seconds in headless Chrome inside its own profile (outbound only
to the preview's port, no LaunchServices, inbound only on loopback and its own sockets), driven over
`--remote-debugging-pipe` (no DevTools port) and stopped by its own pid. It records each uncaught exception and console
error, takes pictures at 3 and 10 seconds, and counts sharp edges outside the welcome panel in each.

The check passes only when the type check and build exit 0, the page opened, it threw nothing, and something is drawn
in view of the laptop camera (more than 200 edges in either picture). **Drawn means something is drawn, never that the
game works:** a scene of the wrong objects passes it (SH's second whack-a-mole run drew floating spheres). Nothing the
check reports says more: no event, field, word on the headset or document may call a passed check a working game.

The preview's Content-Security-Policy allows `data:` in `connect-src`: the SDK's panels lay out with yoga-layout,
whose WASM is inlined and fetched as a `data:` URL, which reaches no network and was blocked in every project.

