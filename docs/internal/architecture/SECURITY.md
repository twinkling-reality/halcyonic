# Security

## Scope today

The control plane runs on the developer's own machine and serves loopback only. The threats it
defends against now are:

- web pages in a browser on the same machine (cross-site requests, DNS rebinding, cross-site
  WebSocket hijacking);
- other local user accounts;
- malformed or malicious input on every interface;
- leaking secrets or work content into logs.

It is not a sandbox against other processes running as the same user; those can read the token,
as with Salidium.

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
| DNS rebinding | Every request's `Host` must name this server's loopback address and port, otherwise 403 |
| Browser requests | Any `Origin` header or `Sec-Fetch-Site: cross-site` is refused with 403, which also blocks browser WebSocket upgrades |
| Authentication | A bearer token on every request and WebSocket upgrade except `/api/health`; compared in constant time |
| Token storage | 32 random bytes in `<data dir>/access-token`, mode 0600; created once; never logged; `authorization` headers are redacted from logs |
| Content types | JSON only; `text/plain` and form bodies are refused with 415 |
| Input validation | Every command, client message and query is validated against the contracts |
| Size limits | 1 MiB request bodies; 256 KiB WebSocket messages; slow WebSocket clients are disconnected |
| Data at rest | Data directory mode 0700; journal, WAL and SHM files mode 0600 |
| Logging | Log context carries identifiers only, never tokens, instructions or agent text |
| Agent working directories | Only directories whose real path lies under `HALCYONIC_PROJECT_ROOTS`; `..` and symbolic links cannot escape a root; with no roots configured, no real runtime can start |
| Agent permissions | Runtime permission modes that take decisions away from the supervising person (`bypassPermissions`, `auto`) are refused as start options, and so are Codex's approval policy `never`, its granular policies and `danger-full-access` with `on-request`, under which Codex runs every command it does not flag as dangerous without asking |
| Agent processes | Stopped on close and when the control plane exits, including on a second signal during shutdown. Every Claude Code process and the OpenCode and Codex servers are recorded before they receive work and watched by a small process that stops them if the control plane dies, even by SIGKILL; the next start stops anything recorded that survived. Identity is checked before any signal. Codex starts each command in a session of its own, beyond the reach of a signal to its server's process group: ending the server's input makes Codex stop them, and a server that has to be killed is killed with all its descendants. A Codex server killed by anything else leaves its running commands behind |
| OpenCode server | Launched from the configured binary only, never from PATH; bound to 127.0.0.1 on a free port with a password generated per launch and kept in memory; refused unless it reports version 2.0.18 and the process id Halcyonic started; recorded (without the password, mode 0600) so the next start stops it after a crash, and watched by a small process that stops it if the control plane dies |
| Codex server | Launched from the configured native binary only, never from PATH, in its own process group, speaking JSON-RPC over its stdin and stdout, so it listens on no port; refused unless both `codex --version` and its answer to `initialize` report 0.157.0 and `ps` shows the launched binary; remote control switched off; only methods on the stable API surface, never the experimental opt-in; requests Halcyonic does not show the person (permission grants, questions, MCP elicitations) are refused, which Codex takes as a denial or an empty answer; recorded (mode 0600, no secrets) so the next start stops it after a crash, and watched by a small process that stops it if the control plane dies |

## Authorization

There is one principal today: whoever holds the local token. Every accepted command records its
policy category (`low_consequence`, `review_required`, `high_consequence`) and the client's
self-declared identity, which is recorded for audit and never trusted. Categories do not yet
restrict anyone. No `high_consequence` command exists; merge, deploy, delete and destructive
commands must not be added until explicit human confirmation and per-device authorization exist.

A conversational model must never turn vague speech into permission for an irreversible action.

## Audit

The journal is the audit log. For each command it records the full command, when it was received
and through which transport, its policy category, the admission decision, and the runtime
confirmed outcome or failure (including whether the effect is unknown). Instructions are work
content and are journaled locally; they are never logged.

## Not yet built

Required before Halcyonic serves anything beyond this machine:

- **Device pairing**: short-lived enrollment credentials, then a persistent per-device identity.
- **Encrypted transport** for any non-loopback connection.
- **Per-device authorization** that enforces policy categories.
- **Remote relay**, where the developer's machine connects outward and nothing is exposed
  unauthenticated.
- **Token rotation** other than deleting the token file.

Provider credentials (Anthropic, OpenAI and others) must stay with the runtime on the machine that
needs them. XR clients must never receive provider keys, repository secrets or SSH keys.
