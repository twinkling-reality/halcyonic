# Seorak integration API v1

- **Question:** Can Halcyonic read what Seorak measured about an execution's session (estimated
  cost, outcome, verification runs) through Seorak's versioned, read-only integration API,
  correlating by the runtime's own session id, without depending on Seorak's source?
- **Date:** 2026-09-27 (UTC).
- **Versions:** `seorak` 0.3.0, the CLI whose local plane serves the API, on npm with SLSA
  provenance, published through trusted publishing from public commit `aa2d57e` of
  <https://github.com/twinkling-reality/seorak>; integration API v1; `@seorak/types` 0.2.0
  (Apache-2.0), published from the same commit. What a v1 client may rely on beyond the types is
  stated in ADR 007, section 2, at commit `8199865`, which changed nothing else. The plane checked
  live, on 127.0.0.1:4317, ran the same code from the Seorak checkout.
- **Method:** Halcyonic's client (`packages/integrations/seorak`) was written from public sources
  only: the declarations and compiled constants of `@seorak/types` 0.2.0, the documents at
  `aa2d57e` (ADR 002, ADR 007, the self-hosted plane hardening reference, the collector's README
  and SETUP), and ADR 007 at `8199865`. Halcyonic does not depend on `@seorak/types`; its readers
  are its own TypeBox schemas. Nothing from Seorak's private repository was read. Seorak's team
  answered Halcyonic's questions, and each answer was checked against the public text before it
  changed the client. The client then ran against the owner's plane with an integration credential
  the owner issued for Halcyonic, read only inside the test process and never printed: 25 requests
  in all (a probe without a credential, then four runs of the live test), far inside the budget.
- **Status:** Verified live: resolve (a hit and a miss), the outcome, the verification lens of a
  session without verification runs, and 401. Documented by Seorak but not observed live:
  verification lens rows, 403 and 429 (below).

## Findings

- **Correlation.** `POST /api/v1/sessions/resolve` takes exactly `{agent, nativeSessionId}` as
  JSON of at most 1 KiB (413, 415 or 400 otherwise), needs `sessions:read`, and returns the
  session's content-free summary with an opaque `ses_` reference. The native id travels only in
  the body and never comes back (ADR 007). The agents are `claude-code` and `codex`
  (`INTEGRATION_RESOLVABLE_AGENTS`), and an id must match `NATIVE_SESSION_ID_PATTERN`, whose value
  is published only in the package's compiled code. A miss is always `unavailable` with reason
  `not-captured` or `outside-credential-restriction`, a null `coverage.observed` and
  `freshness.dataThrough`, both session counts zero, and a null session.
- **Reads.** `GET /api/v1/sessions/{ref}/outcome` needs `sessions:read` and
  `GET /api/v1/sessions/{ref}/replay/verification` needs `replay:read`, the same scopes as over
  MCP. Every answer carries `apiVersion`, `availability` (a state and a reason), `coverage` and
  `freshness`, whose `staleAt` is the instant after which the answer must be described as stale.
  Null means unknown, never zero, and clients ignore keys they do not know.
- **Available means non-null.** When `availability.state` is `available` or `partial`, the
  payload (`session`, `outcome`, `result`) is non-null; only `unavailable` carries a null payload.
  `partial` means the credential's own restriction bounded the answer, and `coverage.omissions`
  says how.
- **Closed unions may grow.** Within v1, Seorak may add values to `availability.reason`,
  `coverage.omissions`, `endReason`, a lens `unit` and `fate`, but never removes a value or changes
  what one means. A client reads an unknown reason as unavailable for a reason it cannot name, an
  unknown omission as incomplete coverage, and an unknown `endReason` or `fate` as unknown, and
  does not reject the answer.
- **The verification lens.** One row per verification kind that was measured, labeled `test`,
  `build`, `typecheck` or `lint`, or `Verification` for a run of no kind, with the metrics `runs`
  and `passed` in the unit `count` and `passRate` in the unit `percent`, whose value is
  nonetheless a fraction from 0 to 1, or null when nothing ran. No rows means nothing was measured,
  and `emptyReason` says so. It is measured for Claude Code only. Lenses do not page in v1:
  `nextCursor` is always null.
- **Cost.** A session's `costUsd` is derived from token counts at list prices, so it is always an
  estimate, labeled with `COST_ESTIMATE_NOTE`: "Estimated from token counts at list prices. Not a
  bill." Null means unpriced or unknown.
- **Credentials and status codes.** The owner issues `srkx_` bearers with an exact audience, a
  mandatory expiry, closed scopes and optional project or date restrictions (ADR 002). On the
  local plane the audience is `http://127.0.0.1:<port>/api/v1` (the collector's README), with port
  4317 unless `SEORAK_LOCAL_PLANE_PORT` says otherwise (SETUP). Every credential failure (missing,
  malformed, unknown, expired, revoked, or issued for another audience) is 401 with
  `WWW-Authenticate: Bearer error="invalid_token"`. 403 with `error="insufficient_scope"` and the
  required `scope` in the header means a valid credential that lacks the scope; on an owner cell it
  can also mean the credential's restriction refuses the request, which the local plane states in
  the envelope instead. Error bodies are for people; a client branches on the status and the
  header.
- **Budget.** Each credential gets 60 requests a minute with a burst of 60, on top of a per route
  class ceiling that every credential on the plane shares, so a 429 can come from either. A 429
  always carries `Retry-After` in whole seconds.
- **Position.** The loopback plane requires the `Host` to carry the listener's port and refuses a
  foreign or cross-port `Origin`. Node's fetch sends no `Origin` and no `Sec-Fetch-Site`.

Unless another source is named, these findings are ADR 007 at `8199865`; the routes and scopes
also match `seorakRoutes` and `PRIVATE_MCP_TOOL_SCOPES` in `@seorak/types` 0.2.0.

## Live checks

| Check | Result |
| --- | --- |
| Resolve without a credential | 401, `WWW-Authenticate: Bearer error="invalid_token"`, body `{"error":"unauthorized"}` |
| Resolve of a random Claude Code session id | `unavailable` with `not-captured`; Halcyonic answers `not_found` (`not_captured`) |
| A well-formed credential Seorak did not issue | 401; Halcyonic answers `unauthorized` (`credential_rejected`) |
| A captured Claude Code session: resolve, outcome, verification lens | All three `available`, `fresh` and complete, with no key beyond the published types. The outcome had `commitsLanded`, `uncommitted`, `lineSurvival` and `firstErrorAt` null, a numeric `errorCount` and an `endReason` from the published values. The lens had no rows and the `emptyReason` "No verification result was captured." All 11 instants were in `toISOString()` form. |

## Documented, not observed live

- **Verification lens rows.** The session checked live had none. The client reads rows as ADR
  007 states them and is tested against the ADR's example outcome and lens
  (`packages/integrations/seorak/fixtures/v1/`), which are illustrative, not recorded.
- **403 and 429.** Neither was provoked against the real plane: the owner's credential carries
  both scopes, and the budget was never approached.
- **Codex sessions.** None was checked live.

## The port holder: considered and rejected

Seorak publishes nothing that proves the process on its port is Seorak, as Salidium's discovery
file does. Seorak's team says it has no such file yet and has recorded the request as deferred;
that is not in the public text. The plane's `GET /data-plane` descriptor answers on the same origin
without a credential on loopback, but it is first-party and outside the integration contract, and
anything listening on the port could answer it just as well, so Halcyonic does not read it. The
residual risk stays documented in [SECURITY.md](../architecture/SECURITY.md), and the request stays
in [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md).

## Consequences for Halcyonic

- Evaluation is read through at `GET /api/executions/:execution_id/evaluation` and never
  journaled ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)). Each answer
  has one availability: a refused or failed read makes the whole answer `unauthorized`,
  `unavailable` or `incompatible`, while an available one keeps Seorak's own availability,
  coverage and freshness for each of its parts. Nothing is combined into a score.
- Runtime kinds map explicitly: `claude-agent` to `claude-code` and `codex` to `codex`. Every
  other kind answers `unavailable` (`runtime_not_observed`) without a request or a credential.
- A resolve without a session reads by its reason: `not-captured` as `not_found` and
  `outside-credential-restriction` as `unauthorized`. The other reasons Seorak publishes, which a
  miss does not carry today, read by what they say, and a reason added later reads as
  `unavailable` (`unknown_reason`).
- Halcyonic's contract has an explicit `unknown` wherever it mirrors a union Seorak may grow: an
  availability reason, a coverage omission (which also makes the coverage incomplete), an end
  reason and a line survival fate, where Seorak's own `unknown` and a fate Halcyonic does not know
  yet read alike. A verification metric in a unit Halcyonic does not know is null; the rest of its
  row stands.
- An answer that contradicts the request or the stated contract is `incompatible`: another agent,
  another session, a payload under `unavailable`, a null payload under `available` or `partial`, a
  verification metric in another known unit, or a rate outside 0 to 1.
- One client per process keeps the credential's budget: at most 60 requests in any rolling
  minute, an evaluation's three reserved before the first, and nothing sent until the time a 429
  names (its `Retry-After`, a minute without one, an hour at most). An evaluation costs three
  requests, so at most 20 fit in a minute: clients fetch one on demand, for example when a
  workstream is opened, and never poll.
- The credential lives in `<data dir>/seorak-credential` with mode 0600 and is read on every
  request. It is sent only to 127.0.0.1 on Seorak's port, never follows a redirect, and is never
  logged or passed to launched agents.
- Re-run the live test whenever Seorak changes the wire, and confirm the lens rows on a session
  that ran checks:
  `HALCYONIC_SEORAK_CREDENTIAL_FILE=<file> HALCYONIC_SEORAK_SESSION_ID=<captured Claude Code session id> node --test packages/integrations/seorak/src/live-seorak.test.ts`.
