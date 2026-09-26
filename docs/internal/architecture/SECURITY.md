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
as with Salidium. The agent runtimes themselves are not yet integrated, so no provider credential
exists in Halcyonic.

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
