# Salidium consumer contract v1

- **Question:** Can Halcyonic read what Salidium understood about an execution's session through a
  versioned, read-only contract, without depending on Salidium's source?
- **Date:** 2026-09-26.
- **Versions:** contract `salidium.consumer` 1.0 (report v2, other documents v1), package
  `@salidium/consumer-contract` 1.0.0-rc.0, uncommitted in the Salidium repository on top of
  `27c9dbb` (release 0.5.0). Not published; no released Salidium serves `/consumer/v1` yet.
- **Method:** Halcyonic's client (`packages/integrations/salidium`) was written from the contract's
  documentation, source and recorded fixtures, with its own tolerant readers. It was then run against
  Salidium's consumer test daemon (`scripts/consumer-test-daemon.mjs`) from the Salidium checkout,
  with a scratch HOME, SALIDIUM_HOME and TMPDIR and a PATH without agent CLIs. The daemon serves
  synthetic sessions, installs no hooks and writes only to a temporary directory.
- **Status:** Verified against the release candidate and synthetic sessions. Real Claude Code or
  Codex sessions and a released Salidium are not verified. Salidium is revising the wire before its
  freeze in response to the findings below, so the fixtures and client will be updated.

## Findings

- **Consent and discovery.** The owner creates a credential per consumer
  (`salidium consumer create <label>`, shown once, revocable). While Salidium runs,
  `$SALIDIUM_HOME/consumer.json` (default `~/.salidium/consumer.json`) names the base URL and an
  instance id; the same document is served without a credential, and a client sends its token only
  after the two instance ids match.
- **Endpoints.** GET only, loopback only, bearer token: discovery, lookup by (provider, native
  session id), report, and a server-sent event feed.
- **Feed.** Starts with `resync`, then `session.changed`, `session.removed`, a heartbeat every
  15 s, and `closing` on revocation or shutdown. Observed: the first heartbeat arrived 14 to 17 s
  after `resync`; revocation closed the feed and the next request got 401; a change raised the
  report's evidence sequence and the refreshed report agreed.
- **Reports.** Live reports mapped to exactly the understanding the recorded fixtures produce,
  apart from the generation time. Unknown and internal sessions answered `not_found`; a foreign
  token was refused.
- **Epistemic classes.** Salidium labels claims `observed`, `reported`, `inferred`, `planned` or
  `explained`. Model-written explanations are `explained`, a class of their own, never `reported`.
- **Node's fetch** sends no `Origin` or `Sec-Fetch-Site` header, so Salidium's browser defenses
  let a server-side client through.

## Disagreements between Salidium's guidance and its source (reported to Salidium)

1. A `question` wait can come from Salidium's classifier reading the agent's last message, yet
   `waiting` carries no provenance, so Halcyonic leaves `waiting` out for now.
2. In that case the verdict labels the agent's text `observed`.
3. The verdict is only ever `observed` or `inferred` in current code, never `reported`.
4. A changed file's reason can be `observed` (a subagent's description), not only `reported`.

Salidium also received six contract questions (error documents for 500, 421 and 403 answers; a
provenance for `waiting`; discovery with several major versions; text length changes within a
major version; an unchecked cast; the test daemon starting a Codex adapter from PATH). It has said
it is fixing them before the freeze.

## Consequences for Halcyonic

- Halcyonic reads understanding through at `GET /api/executions/:execution_id/understanding` and
  never journals it ([ADR 0010](../decisions/0010-external-intelligence-is-read-through.md)).
- Halcyonic's runtime kinds `claude-code` and `codex` map to Salidium's providers; every other kind,
  including `mock` and `opencode`, is not observed by Salidium.
- The credential lives in `<data dir>/salidium-credential` with mode 0600, is read on every request,
  and is never logged or passed to launched agents.
- Re-run the real-wire tests (`SALIDIUM_CHECKOUT=<checkout> node --test
  packages/integrations/salidium/src/live-salidium.test.ts`) and re-copy the fixtures whenever
  Salidium changes the wire, and pin the published version when it exists.
