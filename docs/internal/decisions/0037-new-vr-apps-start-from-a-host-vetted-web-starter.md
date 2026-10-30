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

A port of the range is not proof of who answers on it: a confined agent may listen on loopback too (AUDIT
M15B2-M1). The check opens the page, and a preview is recorded started, only once the server the host spawned
still runs, every listener on 127.0.0.1 at that port is its own pid (by `lsof`), and `/index.html` answers with the
built copy's bytes; otherwise the server is stopped and nothing is recorded. Ports are taken one per check or preview,
a project has one start at a time and none while its check runs, and closing the host stops every server it spawned.
Clients open `http://127.0.0.1:port`, never `localhost`.

## Amendment, 2026-10-30: the check presses the game and offers VR to a stand-in (M1.8b)

The check records three more findings, each optional so checks recorded before them still read, as not checked:
`laptop` (a real mouse press on a target the starter's layer reports, Space and E, the layer's files compared with
the template's, and whether the screen changed after the press, observed in the host against a control pair taken
with no input), `vr` (whether the page offered `immersive-vr` to a stand-in WebXR in a second load) and `notes`
(a scan of the project's `src/` for listeners and controller reads around the layer, never a pass or a fail). `ready`
is `passed` and both steps ready, and is carried on `project_checked`. What the layer reports is `reported`; only the
screen change is the computer's own, so only it may be worded as checked by your computer. Offered to a stand-in on
the Mac is never shown on a headset. Thresholds and cases: [validation/iwsdk-starter.md](../validation/iwsdk-starter.md).
So that the C# bindings can carry them, the generator now declares an optional member of any type as nullable (it
allowed only strings and booleans before); the rule is in [EVENTS.md](../architecture/EVENTS.md).

## Amendment, 2026-10-30: a press seen against a twin (M1.8g)

A scene that moves on its own leaves M1.8b's screen change unknown, so the check now replays the page under a clock
the host steps: time, timers, animation frames and every seeded source of chance move only to frames the host asks
for. It loads the page with no input, then once per target with a press (up to five), then with no input again, and
compares each with the first no-input twin at the same frame; the second no-input load runs after the presses, so a
change that follows load order or count shows. The two no-input loads must match within 0.01% before input and at the end, or the
response is unknown: a page that changes between loads with no input at all has a source of time or chance the clock
does not hold. `response_seen` is observed when a press that reached the layer changes the end frame by more than
0.05%, only when the press run matched the twin within 0.01% before the press, and only when a second load with the
same press ends the same way; a page with another source of time or chance reads unknown. An unknown read says why
(`response_unknown`: the controls differed, a press run differed before input, the repeat differed, out of time, or
failed). The whole twin step has three minutes by the wall clock, a stopped load's settling included, and the page must
reach a further frame at least every 10 s; past either it reads unknown and its Chrome is stopped. The clock holds its
frames while the page's own downloads are pending, so an asset lands at the same frame in every load. Each picture of a
held frame is taken again until two in a row match, for up to 2 s. Laptop ready accepts a response seen this way, as
it accepts a still scene's observed change.
The host steps the clock with a trusted key event the page never receives and cannot make, and the clock reports
through a binding the page cannot reach. Numbers: [validation/iwsdk-starter.md](../validation/iwsdk-starter.md).

Nothing the check concludes is computed in the page, which is the agent's code (AUDIT M18B-M1, M18B-M2): the
edges are counted in the host from the PNGs Chrome returns (`preview/png.ts` decodes 8-bit RGB and RGBA; any other
picture is unknown), canvases are counted through the DevTools DOM domain, at most 100 exception texts are kept
(each clipped; the rest only counted), and a message from Chrome over 16 MB ends the run as unknown.

## Amendment, 2026-10-30: no service worker on a preview's origin

Previews share a few ports on 127.0.0.1, so every project that lands on a port shares its origin with every earlier
one there. A service worker registered by one game would outlive its preview and could answer a later project's
preview, on the headset too, with bytes the check never saw. The host's preview and dev servers therefore answer 404,
with no body, to any request carrying the `Service-Worker` header, which a browser sends with every worker script
fetch, so no registration succeeds (AUDIT M16A-C1). Storage a game leaves on the origin is not cleared by this; that
is a separate unit, once a browser is shown to honour clearing it over plain HTTP on 127.0.0.1.
