# Security

## Scope today

The control plane runs on the developer's own machine and serves loopback by default. When the
owner turns it on, a second listener serves devices paired with it over the local network, TLS
only ([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md), "Devices on the
network" below). The threats it defends against now are:

- web pages in a browser on the same machine, or on another machine on the network (cross-site
  requests, DNS rebinding, cross-site WebSocket hijacking);
- other local user accounts;
- other devices on the same network, including one that intercepts traffic;
- a paired device that is lost or stolen, once the owner revokes it;
- malformed or malicious input on every interface;
- leaking secrets or work content into logs.

It is not a sandbox against other processes running as the same user; those can read the token,
the network listener's key and the journal, as with Salidium.

When the Claude Agent runtime is enabled, the Anthropic API key (from the environment, or else
from `<data dir>/anthropic-api-key`, refused when other users can read it) or cloud provider
credentials reach each launched Claude Code process through an explicitly built environment: an allowlist of variables, plus the names listed in
`HALCYONIC_AGENT_ENV`. Nothing else in the control plane's environment reaches an agent, and a
claude.ai login is never used. Launched Claude Code sessions carry `SEORAK_LAUNCHER=halcyonic`, so
Seorak attributes them to Halcyonic. `ANTHROPIC_BASE_URL` is not inherited, because it decides where the
API key is sent and a tool that launches the control plane may set it for its own endpoint; a
gateway must be passed on purpose.

When the Codex runtime is enabled, its app-server also gets an explicitly built environment: an
allowlist (`PATH`, `HOME`, `USER`, `LOGNAME`, `SHELL`, the locale variables, `TMPDIR`, `TZ`,
`CODEX_HOME` and the XDG directories), plus the names listed in `HALCYONIC_AGENT_ENV`. Configuration
may not set `SALIDIUM_INTERNAL`, `CODEX_INTERNAL_ORIGINATOR_OVERRIDE`, which would replace
Halcyonic's identity on its threads, or `CODEX_INTERNAL_APP_SERVER_REMOTE_CONTROL_DISABLED`, which
the adapter always sets so the server never enables remote control, a second control channel
through chatgpt.com. Codex runs with the developer's own `CODEX_HOME`, so the developer's
`config.toml` applies: sign-in and keys, model providers, the MCP servers configured there, plugins
and their startup sync (chatgpt.com, github.com and api.github.com, observed without credentials), the
analytics events client, which runs unless `analytics.enabled = false`, metrics sent to
ab.chatgpt.com when `analytics.enabled = true`, and saved rules that let matching commands run
without asking. Halcyonic sets only what keeps the person in control: the working directory, the
sandbox mode, an approval policy that asks (`on-request` or `untrusted`), and approvals routed to
the person rather than to a reviewer agent; it refuses a thread for which Codex reports other
settings. When a start names a model provider or a model, a thread Codex reports running on
another is refused too, so work meant for a local model never reaches a hosted one. Every request Codex sends to the model provider carries the originator `halcyonic`, a
user agent with the Codex version and the operating system, and turn metadata with the
installation id, the thread and session ids, the sandbox mode, whether analytics is on and, for a
workspace that is a git repository, its path, latest commit hash and whether it has uncommitted
changes; the working directory also reaches the provider in the conversation's environment
context. Codex writes each thread's rollout to the developer's `CODEX_HOME`, tagged `halcyonic`. The
adapter writes no logs; when a server fails to start, the end of its error output becomes part of
the start failure's message.

When the OpenCode runtime is enabled, its server gets the same kind of allowlist, plus the names in
`HALCYONIC_AGENT_ENV`, and uses the developer's own OpenCode configuration and providers. What
OpenCode 2.0.18 itself sends off the Mac, whatever model runs ([local-models.md](../validation/local-models.md)):
its model catalog, fetched from `models.opencode.ai` at launch and every five minutes unless
`OPENCODE_DISABLE_MODELS_FETCH=true` reaches it through `HALCYONIC_AGENT_ENV`; and ripgrep,
downloaded from GitHub the first time an agent searches files when no `rg` is on the PATH it
inherits. Its configuration decides the rest, and its defaults do not keep work local: without a
configured model it uses a free hosted model of its own service (OpenCode Zen) even when local
models are listed, and it runs every tool without asking, `webfetch` and `websearch` included,
unless its permissions say otherwise. Halcyonic does not yet impose permission rules on OpenCode
sessions ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

A runtime's list of models (`GET /api/runtimes/:runtime_id/models`,
[ADR 0016](../decisions/0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)) is read from
the runtime at each request and never cached or journaled; a failure is logged with the runtime id
and its code only. Adapters read each model field by field, so a provider's settings, keys and
headers never reach a client: OpenCode's `GET /api/model` carries each provider's settings, API
key included, of which only the model's own fields are kept, and `/api/provider` is never read.
Where a model is served is decided from the address the runtime sends its requests to, never from
the model's name, because a local model may carry a hosted model's name (this Mac's Ollama serves
`llama3.2:1b` as `gpt-4o:latest`), and each name says what serves the model. Listing launches the
OpenCode or Codex server when none runs. Listing Claude Code's models starts a short-lived Claude
Code process with an execution's environment, key included, in a temporary directory, so it may
reach Anthropic; the tests never list against the real CLI. A chosen model is checked against a
fresh list before anything runs, so a start never falls back to another model.

The control plane also holds one credential for each product whose conclusions it reads through.
Each is read from its file on every request, refused when other users can read the file, and never
logged or passed to launched agents:

- Salidium's consumer credential, in `<data dir>/salidium-credential`, reads Salidium's reports
  and nothing else. It is sent only after Salidium's discovery file and endpoint prove the same
  instance on loopback.
- Seorak's integration credential, in `<data dir>/seorak-credential`, reads Seorak's content-free
  measurements with the `sessions:read` and `replay:read` scopes, for the audience
  `http://127.0.0.1:4317/api/v1`, until it expires or the owner revokes it. It is sent only if it
  has Seorak's `srkx_` form, only to 127.0.0.1 on Seorak's port, and never along a redirect.
  Seorak publishes no way to prove that the process on that port is Seorak (the request is open
  with Seorak), so while Seorak is stopped another local account could listen there and receive
  the credential. With it, that account could read the same measurements through Seorak's loopback
  plane until the credential expires or is revoked. Seorak's unauthenticated `GET /data-plane`
  proves nothing, since any listener could answer it, and is not read.

## Controls

| Control | Implementation |
| --- | --- |
| Loopback only | `HALCYONIC_HOST` must be `127.0.0.1`, `::1` or `localhost`; anything else is refused at startup |
| DNS rebinding | Every request's `Host` must name this server's loopback address and port, otherwise 403; on the network listener, an IP address or a `.local` name with the listener's port |
| Browser requests | Any `Origin` header or `Sec-Fetch-Site: cross-site` is refused with 403, which also blocks browser WebSocket upgrades, on both listeners |
| Authentication | Loopback: a bearer token on every request and WebSocket upgrade except `/api/health`; compared in constant time. Network listener: a paired device's credential, checked by its SHA-256 against the device registry, on everything except `/api/health` and `/pair`; the access token is never accepted there |
| Token storage | 32 random bytes in `<data dir>/access-token`, mode 0600; created once; never logged; `authorization` headers are redacted from logs |
| Network listener | Off unless `HALCYONIC_NETWORK_HOST` names an IP address; TLS only (1.2 or later), with a self-signed ECDSA P-256 certificate generated once into `<data dir>/network-key.pem` and `network-certificate.pem`, mode 0600; the key is never logged; 5 seconds for the TLS handshake, 10 for a whole request, 60 for a connection on which nothing moves, 5 between requests; 32 connections at once, 8 from one address, and 4 realtime connections per device; a WebSocket it closes waits a second for the client's answer |
| Pairing | Opened only from loopback with the access token (`pnpm pair`), one window at a time, for five minutes, closed by the first device that pairs, by three failed proofs, or by the owner; an eight-digit code, never logged or journaled; SRP-6a (RFC 5054, 3072-bit group, SHA-256) with the TLS certificate bound into both proofs; the salt and verifier derived once per window, so no exchange's work depends on the code; `/pair` refused outside a window before any cryptography; four exchanges at once, one per address, six connections per address a minute, 30 seconds each; what it turns away or cuts short without checking a code is counted by address and shown by `pnpm pair` |
| Device credentials | 32 random bytes per device (`hlcd_` and base64url), sent once, encrypted under the SRP session key inside TLS; the journal keeps only their SHA-256; revoked from loopback (`pnpm devices revoke`) or by the device itself, at once and for what is already open: its realtime connections handle nothing more and are cut off, its commands are rejected where they would act (`device_revoked`), and any answer to a request it opened before is replaced by 401 |
| Failed credentials | 30 refused credentials from one address in a minute, and it gets 429 for the rest of the minute |
| Content types | JSON only; `text/plain` and form bodies are refused with 415 |
| Input validation | Every command, client message and query is validated against the contracts |
| Size limits | 1 MiB request bodies; 256 KiB WebSocket messages; slow WebSocket clients are disconnected |
| Data at rest | Data directory mode 0700; journal, WAL and SHM files mode 0600 |
| Logging | Log context carries identifiers only, never tokens, pairing codes, device credentials or their hashes, keys, instructions or agent text |
| Agent working directories | Only directories whose real path lies under `HALCYONIC_PROJECT_ROOTS`; `..` and symbolic links cannot escape a root; with no roots configured, no real runtime can start |
| Agent permissions | Runtime permission modes that take decisions away from the supervising person (`bypassPermissions`, `auto`) are refused as start options, and so are Codex's approval policy `never`, its granular policies and `danger-full-access` with `on-request`, under which Codex runs every command it does not flag as dangerous without asking |
| Agent processes | Stopped on close and when the control plane exits, including on a second signal during shutdown. Every Claude Code process and the OpenCode and Codex servers are recorded before they receive work and watched by a small process that stops them if the control plane dies, even by SIGKILL; the next start stops anything recorded that survived. Identity is checked before any signal. Codex starts each command in a session of its own, beyond the reach of a signal to its server's process group: ending the server's input makes Codex stop them, and a server that has to be killed is killed with all its descendants. A Codex server killed by anything else leaves its running commands behind |
| OpenCode server | Launched from the configured binary only, never from PATH; bound to 127.0.0.1 on a free port with a password generated per launch and kept in memory; refused unless it reports version 2.0.18 and the process id Halcyonic started; recorded (without the password, mode 0600) so the next start stops it after a crash, and watched by a small process that stops it if the control plane dies |
| Codex server | Launched from the configured native binary only, never from PATH, in its own process group, speaking JSON-RPC over its stdin and stdout, so it listens on no port; refused unless both `codex --version` and its answer to `initialize` report 0.157.0 and `ps` shows the launched binary; remote control switched off; only methods on the stable API surface, never the experimental opt-in; requests Halcyonic does not show the person (permission grants, questions, MCP elicitations) are refused, which Codex takes as a denial or an empty answer; recorded (mode 0600, no secrets) so the next start stops it after a crash, and watched by a small process that stops it if the control plane dies |

## Authorization

There are two kinds of principal: `local`, whoever holds the access token, which only loopback
accepts, and `device`, a paired device, which only the network listener accepts. Every command
records the principal that sent it, as the control plane authenticated it, beside the client's
self-declared identity, which is recorded for audit and never trusted. What differs between them
today is device management: opening a pairing window, listing devices and revoking one are served
on loopback only, and a device can revoke no credential but its own. Commands are admitted alike
for both. Every accepted command records its policy category (`low_consequence`,
`review_required`, `high_consequence`); categories do not yet restrict anyone. No
`high_consequence` command exists; merge, deploy, delete and destructive commands must not be added
until explicit human confirmation and a policy that tells principals apart exist.

A conversational model must never turn vague speech into permission for an irreversible action.

## Audit

The journal is the audit log. For each command it records the full command, when it was received,
through which transport and from which principal, its policy category, the admission decision,
and the runtime confirmed outcome or failure (including whether the effect is unknown). Commands
journaled before principals were recorded read with a null principal. Pairing and revoking
devices are journaled too (`device.paired`, `device.revoked`, [EVENTS.md](EVENTS.md)); pairing
windows and failed attempts are logged, without the code. Instructions are work content and are
journaled locally; they are never logged.

## Devices on the network

What each party can do with the network listener on
([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md),
[network-pairing.md](../validation/network-pairing.md)):

- **A hostile device on the same network** sees that a TLS service listens on the port, and its
  self-signed certificate. Without a credential it gets `/api/health` and nothing else. It cannot
  pair outside a window the owner opened; inside one it gets three guesses at an eight-digit code
  in all, so one chance in 33 million, and SRP gives it nothing to test offline, even when it sits
  in the middle and relays every message: the device binds the certificate it saw into its proof,
  and the relay's is not the control plane's. It cannot read or change a paired device's traffic,
  because the device refuses any certificate but the one it pinned before sending anything. It can
  deny service: flood the port, use up a window's three attempts (the owner sees the window close
  after three failures), hold the four exchange slots open without guessing (`pnpm pair` shows
  each connection turned away or cut short, with its address), hold the 8 connections one
  address may, each for at most 5 seconds without finishing TLS and 10 without a request, fill all
  32 from several addresses, or block traffic. Its timing tells it nothing about the code: an
  exchange's work depends only on a secret it draws itself.
- **A stolen or lost headset** holds its credential in app-internal storage and can do whatever
  its owner could over that network, until the owner revokes it with `pnpm devices revoke`, which
  refuses the credential and ends what it has open at once: a realtime connection handles nothing
  more, even if the headset ignores the close frame, a command in a request opened earlier is
  rejected and journaled as such, and no answer reaches it. What the headset started before the
  revocation keeps running: revoking stops the device, not work it already set going, which the
  owner can interrupt from loopback. Paired devices cannot see one another: no device reads device
  events. It reaches the control plane only
  where the listener is reachable, the owner's network. A development build is debuggable, so
  anyone with `adb` access to the unlocked headset can read the credential with `run-as`; the
  headset's own lock is the first defense. It holds no provider key, repository secret or SSH
  key.
- **A web page**, on this machine or another, cannot drive either listener: both refuse any
  `Origin` and cross-site fetches, the network listener refuses `Host` names other than addresses
  and `.local` names, and browsers refuse its self-signed certificate.
- **Another account on this machine** can reach the network listener as a device on the network
  can. It cannot read the data directory (mode 0700), so neither the access token nor the key.
- **Someone who sees the code** during the five minutes it is valid, and is on the network, could
  pair first. `pnpm pair` names the device that paired; revoke it and pair again.

Remaining risks: a device that pairs again gets a second credential, and the first stays valid
until revoked; credentials do not expire; the device's label is self-declared; a paired device can
hold the 8 connections its address may with slow requests, and devices on several addresses can
fill the listener's 32, a connection that sends no request for at most 10 seconds at a time.

## Untrusted text in the client

The XR client shows text Halcyonic did not write: workstream titles and objectives, which come
from commands; anything an agent or a tool wrote, such as messages, activity and approval requests
naming a shell command or a file path; refusals and failures; setup problems carrying exception
text; what Salidium and Seorak say; names from runtimes; and, while pairing, a refusal in the words
of whatever answers at the typed address. TextMeshPro interprets text even with
rich text off: a backslash with u and four hex digits becomes that character whatever its
settings, a backslash with n, r, t or v a control character while escape parsing is on, the end of
text character U+0003, typed or escaped, ends a label there without an ellipsis, a carriage return
draws what follows over the start of the line, and zero width and bidirectional control characters
draw nothing or a mark over their neighbors ([workspace-interaction.md](../validation/workspace-interaction.md)).
An agent could hide the end of its own approval request that way, or make two commands read alike.
So ([XR_CLIENT.md](XR_CLIENT.md), "Words"):

- Every label that can show such text gets it through one rule in the client core (`LabelText`):
  no markup, in TextMeshPro and in Unity's `TextMesh` alike; every backslash doubled for
  TextMeshPro's escape parsing, so a backslash sequence shows as written; line breaks and tabs as
  spaces; and every control, format or default ignorable character, and half a surrogate pair,
  shown as its code point, as ‹U+202E›, so nothing is hidden or reordered.
- Nothing is cut short silently: a label cut short ends in an ellipsis, and no label uses
  TextMeshPro's italics or bold, which lose it. An approval is confirmed only once the whole
  request it answers has shown, in parts when it is long.
- The editor's render check puts hostile text on every workspace label and on a character's, and
  fails if one interprets any of it or cuts it short without an ellipsis. The line above the stage
  and the pairing line go through the same code but are not rendered by it.

Not covered: characters that only look alike, such as a Cyrillic letter for a Latin one or a
no-break space for a space, show as they look. And the client shows a request as the control plane
recorded it, which the contract limits to 2000 characters: the Codex, OpenCode and Claude Code
adapters end a longer one with "[truncated]"
([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

## Not yet built

- **Remote relay**, where the developer's machine connects outward and nothing is exposed
  unauthenticated. Pairing is for the local network only.
- **Policy per principal**, for example a `high_consequence` command that only the local principal,
  or a confirmation on the Mac, may send.
- **Rotation**: a device credential rotates by revoking it and pairing again, and the listener's
  certificate by deleting its key and certificate, after which every device pairs again; the
  access token still rotates only by deleting its file. Nothing expires.
- **Keystore protection** of the credential on the headset, and discovery without typing the
  address (mDNS).

Device pairing, encrypted transport for devices on the network, and per-device identity exist
([ADR 0017](../decisions/0017-pair-a-headset-over-the-local-network.md)); serving anything to the
internet still needs the relay and more.

Provider credentials (Anthropic, OpenAI and others) must stay with the runtime on the machine that
needs them. XR clients must never receive provider keys, repository secrets or SSH keys.
