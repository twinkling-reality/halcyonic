# Claude Code's key, held by the gate (VRIFY, 2026-11-12)

A confined Claude Code (a new app's project, ADR 0048) holds no key of the person's. Its gate on loopback, the one
place the closed profile lets it reach, adds the person's key to each request on its way to the API. What the agent
holds instead is a credential the gate made for that one run: random, checked on every request, worthless anywhere
else and revoked when the run's process ends (AUDIT3's finding, 2026-11-12: a gate that added the key for any
caller would let any process on the Mac, or a web page through a rebound address, spend it). So a run inside
is recorded confined, and what it makes can be checked, played, published and shared like any other run's.

Since 2026-11-13 a Claude Code on a connected folder, outside the profile, reaches its API through the same gate with
a run credential of its own: [claude-code-connected-folders.md](claude-code-connected-folders.md).

## What the official documentation says (read 2026-11-12)

- Claude Code is pointed at another API address with `ANTHROPIC_BASE_URL`: "Override the API endpoint to route
  requests through a proxy or gateway." (code.claude.com/docs/en/env-vars)
- `ANTHROPIC_API_KEY` is sent as the `X-Api-Key` header; `ANTHROPIC_AUTH_TOKEN` as `Authorization: Bearer`, and the
  token ranks above the key when both are set. (code.claude.com/docs/en/authentication)
- A gateway for the Anthropic Messages format serves `/v1/messages` (and optionally `/v1/messages/count_tokens`),
  forwards `anthropic-version`, `anthropic-beta` and the other `anthropic-*` headers as an open list, and relays a
  stream as it comes: "If your gateway buffers a response until it is complete, Claude Code stalls."
  (code.claude.com/docs/en/llm-gateway, code.claude.com/docs/en/llm-gateway-protocol)
- The Messages API takes an API key in `x-api-key` (documented as still supported beside `Authorization`), needs
  `anthropic-version`, and answers a malformed, revoked or expired key with 401 `authentication_error`.
  (platform.claude.com/docs/en/api/overview, platform.claude.com/docs/en/api/errors)
- Not stated in the documentation: whether Claude Code checks a key's form before it sends anything. The fake-API run
  below shows the run credential is sent without complaint.

## What is built

- One mechanism, declared per app (`packages/seatbelt/src/key-gate.ts`, `KeyGateDeclaration`): the variable the app
  reads its API's base address from, the one it reads its key from, its other credential variables, the header its
  API takes the key in (as it is, or after `Bearer `), and the API's address when the plane names none. Claude Code's
  declaration (`CLAUDE_CODE_KEY_GATE`): `ANTHROPIC_BASE_URL`, `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `x-api-key`,
  `https://api.anthropic.com`. A second, different declaration (a test stand-in: `OPENAI_BASE_URL`, `OPENAI_API_KEY`,
  `authorization` with `Bearer `) passes through the same gate and environment with no code of its own
  (`confinement.test.ts`, "one mechanism, two declarations").
- The confined launch (`confinedLaunch`) sets the declared base variable to the gate's own address and the key
  variable to this run's credential (`halcyonic-gate-run-` and 64 random hex digits, `ApiGate.issue`, revoked when
  the process exits), drops the other declared credentials, and keeps `HTTP_PROXY` and `HTTPS_PROXY` at the gate.
- The gate takes a request only when its Host is exactly its own loopback address (`127.0.0.1:<port>`) and it
  carries a live run credential in the declared header, compared in constant time; else 403 with an empty body,
  before the key is touched and before anything reaches the upstream. It takes a request to its own address
  (origin-form, or absolute-form for its own host and port), drops `x-api-key`, `authorization` and the hop's own headers, adds the credential the adapter declares
  (Claude Code: `x-api-key`, the person's key), and sends it to the declared upstream (`ANTHROPIC_BASE_URL` of the
  plane, else `https://api.anthropic.com`), a gateway's own path first. It streams the answer back unbuffered.
  Anything else, a proxied request for another host or any CONNECT, gets 403.
- The gate opens only on an upstream the one rule a provider's declaration is read by allows (`upstreamProblem` in
  runtime-core, ADR 0061): https, or http to this Mac for proofs, and no address carrying a name, password, query or
  fragment. A plane's `ANTHROPIC_BASE_URL` in clear text off the Mac opens no gate, so no key is sent unencrypted
  (AUDIT2's note N2, 2026-11-12; `confinement.test.ts`, failed first: the old gate opened on `http://` anywhere).
- The header an app presents its run credential in never goes upstream, whatever its name, beside the fixed list of
  credential headers (AUDIT3's S1; `confinement.test.ts`, failed first: a credential presented in a header of its own
  reached the upstream).
- The adapter records a run inside the profile as confined. Without a key there is no gate and no confined start.

## Seen (the real Claude Code CLI the Agent SDK bundles, 2.1.283, inside the closed profile; a fake API; a fake key)

`packages/integrations/claude-code/src/confined.e2e.test.ts`, 2026-11-12, passed; the same test on the code before
this change failed (the run recorded `confined: false`). In it:
- the command inside printed `key=halcyonic-gate-run-<64 hex> token=none`, and the gate asked from inside without
  that credential answered 403; the fake key appeared nowhere in what it
  printed, though the test's own process (standing for the plane) held the fake key in its environment;
- `ps eww` on the test's process from inside was refused (exit 127, the profile's `process-info` for its own
  sandbox only);
- a request through the gate as a proxy for another host was answered 403;
- every request reached the fake API through the gate with the fake key in `x-api-key` and no `Authorization`;
- no file of the agent's own place (its home, configuration and temporary folder) held the fake key;
- the run was recorded `confined: true`, and a second adapter re-attached it from its folder's own store.
The plan, resume and project-settings e2e tests passed on it too, and none of the four tests' logs holds the fake
key. The gate's own tests (`confinement.test.ts`) cover the forwarded headers, a gateway's path, the stream, and the
refusals: for both declarations, no credential, the person's key itself, a wrong credential, another gate's, an
ended run's, a foreign Host and `localhost` as Host each get 403 with nothing reaching the upstream. Before the run
credential, a probe of the gate answered 200 to a request with no credential and to one with a foreign Host, and
sent the key on with both (`.private` evidence, the AUDIT3 finding reproduced).

## Not verified

- An `https` upstream: no test reaches one (no network, no certificate here), so `api.anthropic.com` itself is not
  seen through the gate.
- A real key and a hosted model: none was used. The owner enters theirs into the product when the path is read safe.
- A session recorded not confined before this change (inside the profile with the key) is not re-attached after it:
  the adapter refuses to move a run into the sandbox.
- Claude Code's own other traffic (telemetry, the WebFetch preflight to `api.anthropic.com`): it goes to the gate as
  a proxy and is refused there; whether any feature then degrades is not checked.
