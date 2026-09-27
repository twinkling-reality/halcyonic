# Seorak integration API v1

- **Question:** Can Halcyonic read what Seorak measured about an execution's session (estimated
  cost, outcome, verification runs) through Seorak's versioned, read-only integration API,
  correlating by the runtime's own session id, without depending on Seorak's source?
- **Date:** 2026-09-27 (UTC).
- **Versions:** `seorak` 0.3.0, the CLI whose local plane serves the API, on npm with SLSA
  provenance, published through trusted publishing from public commit `aa2d57e` of
  <https://github.com/twinkling-reality/seorak>; integration API v1; `@seorak/types` 0.2.0
  (Apache-2.0), published from the same commit. The plane checked live, on 127.0.0.1:4317, ran the
  same code from the Seorak checkout.
- **Method:** Halcyonic's client (`packages/integrations/seorak`) was written from public sources
  only: the declarations and compiled constants of `@seorak/types` 0.2.0, and the documents at
  `aa2d57e` (ADR 002, ADR 007, the self-hosted plane hardening reference, the collector's README
  and SETUP). Halcyonic does not depend on `@seorak/types`; its readers are its own TypeBox
  schemas. Nothing from Seorak's private repository was read. The client then ran against the
  owner's plane with an integration credential the owner issued for Halcyonic, read only inside the
  test process and never printed: 19 requests in three runs, far inside the budget.
- **Status:** Verified live: resolve (a hit and a miss), the outcome, the verification lens of a
  session without verification runs, and 401. Not verified: lens rows, 403 and 429 (below).

## Findings

- **Correlation.** `POST /api/v1/sessions/resolve` takes exactly `{agent, nativeSessionId}` as
  JSON of at most 1 KiB (413, 415 or 400 otherwise), needs `sessions:read`, and returns the
  session's content-free summary with an opaque `ses_` reference. A miss is the same envelope,
  `unavailable` with reason `not-captured`, and a null session. The native id travels only in the
  body and never comes back (ADR 007). The agents are `claude-code` and `codex`
  (`INTEGRATION_RESOLVABLE_AGENTS`), and an id must match `NATIVE_SESSION_ID_PATTERN`, whose value
  is published only in the package's compiled code.
- **Reads.** `GET /api/v1/sessions/{ref}/outcome` needs `sessions:read` and
  `GET /api/v1/sessions/{ref}/replay/verification` needs `replay:read` (`seorakRoutes`,
  `PRIVATE_MCP_TOOL_SCOPES`). Every answer carries `apiVersion`, `availability` (a state and a
  reason), `coverage` and `freshness`, whose `staleAt` is the instant after which the answer must
  be described as stale. Null means unknown, never zero, and clients ignore keys they do not know
  (ADR 007, section 2).
- **Cost.** A session's `costUsd` is derived from token counts at list prices, so it is always an
  estimate, labeled with `COST_ESTIMATE_NOTE`: "Estimated from token counts at list prices. Not a
  bill." Null means unpriced or unknown.
- **Credentials.** The owner issues `srkx_` bearers with an exact audience, a mandatory expiry,
  closed scopes and optional project or date restrictions (ADR 002). On the local plane the
  audience is `http://127.0.0.1:<port>/api/v1` (the collector's README), with port 4317 unless
  `SEORAK_LOCAL_PLANE_PORT` says otherwise (SETUP). Each credential has a persistent token bucket
  of 60 requests a minute and the plane a route-class backstop (the hardening reference);
  `IntegrationRateLimit` also carries a burst.
- **Position.** The loopback plane requires the `Host` to carry the listener's port and refuses a
  foreign or cross-port `Origin`. Node's fetch sends no `Origin` and no `Sec-Fetch-Site`.

## Live checks

| Check | Result |
| --- | --- |
| Resolve without a credential | 401, `WWW-Authenticate: Bearer error="invalid_token"`, body `{"error":"unauthorized"}` |
| Resolve of a random Claude Code session id | `unavailable` with `not-captured`; Halcyonic answers `not_found` (`not_captured`) |
| A well-formed credential Seorak did not issue | 401; Halcyonic answers `unauthorized` (`credential_rejected`) |
| A captured Claude Code session: resolve, outcome, verification lens | All three `available`, `fresh` and complete, with no key beyond the published types. The outcome had `commitsLanded`, `uncommitted`, `lineSurvival` and `firstErrorAt` null, a numeric `errorCount` and an `endReason` from the published values. The lens had no rows and the `emptyReason` "No verification result was captured." All 11 instants were in `toISOString()` form. |

## Not verified

- **Verification lens rows.** The published types leave a row's metric keys open
  (`key: string`). Seorak's team describes one row per kind of check with `runs`, `passed` and
  `passRate` (a fraction or null), the field names of the published `VerificationRollup`, and the
  session checked live had no rows. Halcyonic reads those three keys and answers `incompatible` to
  a row without them, or with a rate outside 0 to 1, rather than guessing. How rows are labeled and
  the unit of `passRate` are unknown.
- **403 and 429.** That 403 means the credential lacks the scope a read needs, and that 429
  carries `Retry-After`, come from Seorak's team, not from the published sources, and neither was
  observed. Error bodies are not published; Halcyonic reads none.
- **The port holder.** Seorak publishes nothing that proves the process on its port is Seorak,
  as Salidium's discovery file does ([SECURITY.md](../architecture/SECURITY.md)).
- **Codex sessions** were not checked live. Seorak's published capability registry says it does
  not measure Codex verification.

The questions are with Seorak's team; the unresolved ones are in
[OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md).

## Consequences for Halcyonic

- Evaluation is read through at `GET /api/executions/:execution_id/evaluation` and never
  journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)). Each answer
  has one availability: a refused or failed read makes the whole answer `unauthorized`,
  `unavailable` or `incompatible`, while an available one keeps Seorak's own availability,
  coverage and freshness for each of its parts. Nothing is combined into a score.
- Runtime kinds map explicitly: `claude-agent` to `claude-code` and `codex` to `codex`. Every
  other kind answers `unavailable` (`runtime_not_observed`) without a request or a credential.
- A resolve without a session reads by its reason: `not-captured`, `not-retained` and
  `not-yet-computed` as `not_found`, `outside-credential-restriction` as `unauthorized`,
  `temporarily-unavailable` and `result-limit` as `unavailable`. An answer that contradicts the
  request or itself (another agent, another session, a value Seorak calls unavailable) is
  `incompatible`.
- One client per process keeps the credential's budget: at most 60 requests in any rolling
  minute, an evaluation's three reserved before the first, and nothing sent until the time a 429
  names (its `Retry-After` in seconds or as an HTTP date, a minute without one, an hour at most).
  An evaluation costs three requests, so at most 20 fit in a minute: clients fetch one on demand,
  for example when a workstream is opened, and never poll.
- The credential lives in `<data dir>/seorak-credential` with mode 0600 and is read on every
  request. It is sent only to 127.0.0.1 on Seorak's port, never follows a redirect, and is never
  logged or passed to launched agents.
- Re-run the live test whenever Seorak changes the wire, and confirm the lens rows on a session
  that ran checks:
  `HALCYONIC_SEORAK_CREDENTIAL_FILE=<file> HALCYONIC_SEORAK_SESSION_ID=<captured Claude Code session id> node --test packages/integrations/seorak/src/live-seorak.test.ts`.
