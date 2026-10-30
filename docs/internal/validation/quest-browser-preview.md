# Opening a checked preview in Quest Browser

- **Question:** How does `pnpm quest:device -- preview <project id>` open a project's checked copy
  (ADR 0037) in Quest Browser on the headset, and what makes it open only a copy whose agents' reads
  were confined to the project (`reads_confined`, ADR 0041)?
- **Date:** 2026-10-30.
- **Environment:** this checkout's control plane and device tool on an Apple silicon Mac; no headset
  attached.
- **Method:** the tool's tests (`tooling/quest/preview.test.ts` against a real control plane's journal
  and a real listener on the preview range, `tooling/quest/device.test.ts` against a stand-in adb);
  Android's [adb page](https://developer.android.com/tools/adb) (updated 2026-09-25) for `am start`;
  W3C [Secure Contexts](https://www.w3.org/TR/secure-contexts/) (Candidate Recommendation Draft,
  2023-11-10) for which origins count as secure; this repository's earlier headset records for
  `adb reverse` and `/proc/net/tcp`.
- **Status:** The gate and the command order are verified by tests on the Mac. **Not run on the
  headset yet:** the `VIEW` intent to Quest Browser, whether the page is a secure context there, and
  whether Quest Browser offers WebXR from it.

## What the tool checks before anything reaches the headset

From the control plane's journal (`GET /api/events`, every page, each request sent only after the
server proves it holds the access token, as `pnpm demo` does; the token is never printed):

- the project has a preview running, and it serves the project's latest check;
- that check passed, and recorded `reads_confined: true`;
- the preview's port is in `PREVIEW_PORT_FIRST` to `PREVIEW_PORT_LAST` (47930 to 47939).

On the Mac:

- the host's preview record (`<data dir>/previews/<project>.json`) names that check and port;
- the only process listening on `127.0.0.1:<port>` (`lsof`) is the one that record names, before
  and after the page is read;
- `/` there answers with exactly the bytes of the check's built `index.html`;
- a request carrying `Service-Worker: script`, which a browser sends when it fetches a service
  worker script, is refused there with a 4xx. Every project that later gets the same port shares
  Quest Browser's origin `http://127.0.0.1:<port>`, and a service worker one preview registered
  could answer the next one's loads with bytes the gate never checked. The host's preview server
  does not refuse that request yet, so until it does the tool refuses every preview.

Any other answer refuses in plain words, and no adb command beyond finding the headset runs.

## On the headset, in order

1. Refuses while anything listens on 8720 (Meta XR Operator): local port `:2210` in state `0A` in
   `/proc/net/tcp` or `/proc/net/tcp6`, any uid. That reading was seen on this headset
   ([quest-headset-in-the-loop.md](quest-headset-in-the-loop.md)). It also refuses unless both tables
   were read, each with its header, so a failed read never counts as nothing listening.
2. Refuses while a system dialog is in front, as `launch` does.
3. Notes whether Quest Browser (`com.oculus.browser`) runs (`pidof`).
4. Refuses when `adb reverse --list` cannot be read, or shows a reverse of the headset's port that
   this tool did not make. Otherwise records the port in the tool's state, then
   `adb reverse --no-rebind tcp:<port> tcp:<port>`, so it never replaces anyone's rule; if that
   fails the port is forgotten again. Android's adb page does not document `reverse`; plain
   `reverse` ran on this headset for 47800 ([xr-loopback-proof.md](xr-loopback-proof.md),
   [quest-3-device.md](quest-3-device.md)); `--no-rebind` has not run on it yet.
5. `am start -W -a android.intent.action.VIEW -d http://127.0.0.1:<port>/ com.oculus.browser`.
   Android's page documents `-W`, `-a` and `-d`, and a package given as a plain argument; it
   documents no `-p` for `am start`, so the tool does not use one.
6. Quest Browser goes into the tool's started apps only if it was not running before.

`preview stop` and `give-back` run `adb reverse --remove tcp:<port>` for each recorded port, and
forget it only once a `reverse --list` that was read shows it gone. They force-stop Quest Browser
only if this tool started it; a browser that was running before keeps the preview's tab. Rules this
tool did not record are left alone. A frame of what the headset shows is `shot`, as for any app.
No browser log and no DevTools forward: either would show the owner's other pages.

## What the gate does not hold

- It holds when the page opens. The reverse lives on until `preview stop` or `give-back`: if the
  host's preview stops and something else then listens on the Mac's port, the headset's next load
  of that address reaches it. Run `preview stop` as soon as the look is done.

## Why 127.0.0.1 and never localhost

- Secure Contexts treats an origin whose host is in `127.0.0.0/8` or `::1/128` as potentially
  trustworthy. `localhost` counts only where the browser resolves it to loopback by its own rules,
  which the spec leaves optional.
- On the Mac a name could reach another listener (for example an agent's server on `::1`); an
  address cannot. The host's preview server listens on 127.0.0.1 only.

## Open

- Whether Quest Browser treats `http://127.0.0.1:<port>/` through `adb reverse` as a secure context
  and offers `immersive-vr` (the starter's Enter VR control shows only then).
- Whether the `VIEW` intent opens a 2D panel or the immersive browser.
- `adb reverse` reaches the Mac's loopback on the host side; that it reaches 127.0.0.1 rather than
  `::1` is assumed until the run shows the page. Whether adb accepts `tcp:127.0.0.1:<port>` as the
  host side, which would pin it, is not verified.
- What an opened preview leaves in the owner's Quest Browser profile for `http://127.0.0.1:<port>`
  besides a service worker (storage, cookies, cache, permission grants) stays there, shared by every
  project that later gets the same port. Not prevented.
