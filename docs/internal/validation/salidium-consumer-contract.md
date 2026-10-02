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
- **Status:** Verified against the release candidate as revised on 2026-09-26 and synthetic sessions,
  then against the released `salidium@0.6.0` daemon (below). Real Claude Code or Codex sessions are
  not verified.

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
- Halcyonic's runtime kind `claude-agent` maps to Salidium's provider `claude-code`, and `codex` to
  `codex`; `opencode` maps to `salidium/opencode` only where the running daemon lists it (below);
  every other kind, including `mock`, is not observed by Salidium.
- The credential lives in `<data dir>/salidium-credential` with mode 0600, is read on every request,
  and is never logged or passed to launched agents.
- Re-run the real-wire tests (`SALIDIUM_CHECKOUT=<checkout> node --test
  packages/integrations/salidium/src/live-salidium.test.ts`) whenever Salidium changes the wire,
  and re-copy the fixtures from the published `@salidium/consumer-contract` of the new version.

## Revised release candidate (2026-09-26)

Salidium revised the wire before its freeze in response to the findings above, keeping the version
at 1.0.0-rc.0 (unpublished), and Halcyonic's client and fixtures followed. Tested against Salidium
HEAD `27c9dbb` plus its uncommitted changes.

- **Discovery** lists every major version: `contracts: [{name, major, minor, baseUrl}]` replaces
  `contract`, `baseUrl` and `endpoints`. Halcyonic takes the `salidium.consumer` major 1 entry,
  ignores the others, applies its loopback check to that entry's base URL, and answers
  `incompatible` (`unsupported_contract`) when there is none.
- **`waiting`** now carries provenance (observed for a permission request, a notification or a
  question tool call; reported when read from the agent's last message), and the verdict follows
  it. Halcyonic's understanding carries `waiting` again.
- **Statements**: only reported agent or subagent statements cross; user-authored ones and
  unlabelled reasons do not. The working headline is Salidium's own wording, no longer a tool title.
- **Errors**: a handler failure is a contract error (500 `internal`), and the loopback guard's
  refusals are contract errors too (421 `host-not-allowed`, 403 `origin-not-allowed`). Halcyonic
  reports all three as `unavailable`.
- **Bounds** never loosen within a major version, so Halcyonic's contract now carries Salidium's
  maximum lengths on every text field.
- **Test daemon** runs no providers, sets its own SALIDIUM_HOME, fixes its clock, and seeds a third,
  working session. All five real-wire tests passed, and live reports matched the fixtures exactly.

## Released (2026-09-27)

`salidium@0.6.0` is on npm (`latest`, SLSA provenance, tag v0.6.0) and its CLI serves
`/consumer/v1`. `@salidium/consumer-contract@1.0.0-rc.0` followed the same day, on the dist-tags
`next` and `latest`. Both provenance attestations name commit `0e9269a` on `main`. Halcyonic does not
depend on the package, since it reads the wire with its own schemas, but the twelve fixtures it keeps
equal the published package's value for value. Halcyonic's client ran against the released daemon,
isolated (temporary HOME, SALIDIUM_HOME, CLAUDE_CONFIG_DIR and CODEX_HOME, a PATH without agent
CLIs, loopback): `consumer.json` was published, a credential created with `salidium consumer create
--json` was accepted, an unknown Claude Code session answered `not_found`, an OpenCode execution
answered `unavailable` (`runtime_not_observed`), and the feed opened with `resync`. Halcyonic's
target is Salidium 0.6.0, contract `salidium.consumer` 1.0.

## On the development Mac (2026-09-29)

Salidium 0.6.0's menu bar app was running and its daemon was not: `~/.salidium/consumer.json` did
not exist, so a control plane there answers `unavailable` (`not_running`). Nothing was started.
Answers now carry `source.synthetic`, false for everything read from Salidium
([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md),
[understanding-and-evaluation.md](understanding-and-evaluation.md)).

## The OpenCode provider, gated on what the daemon declares (2026-10-02)

- **Source:** the Salidium coordinator's answers on 2026-10-02 in a session message. None of it is
  released or in Salidium's public text: Salidium targets 0.7.0 with consumer contract 1.1 before
  2026-11-04, its OpenCode provider is in development, experimental and off unless a person turns it
  on. Salidium's owner decided the provider id (D11) and approved the declaration for implementation
  (D12).
- **What Salidium said:** the provider id is `salidium/opencode`, which consumer v1's provider id
  pattern already accepts (`claude-code`, `codex`, or a namespaced `owner/name`). From contract 1.1,
  the discovery document (`consumer.json` and `GET /consumer/v1/discovery`) carries a top-level
  `providers` list beside `instanceId`: `[{ "id": "claude-code" }, { "id": "codex" }, { "id":
  "salidium/opencode" }]`, sorted, unique, at most 32, never null, each entry open for later facts.
  It lists every provider the instance observes now, Salidium's own two included, and is fixed for
  the instance's life; turning a provider on or off restarts the daemon with a new instance id. A
  consumer trusts it only beside a major 1 entry of minor 1 or later; without it, what the daemon
  observes is unknown. The native session id is OpenCode's own `ses_` id: Salidium's OpenCode lane
  ran the pinned OpenCode 2.0.18 server on a scratch store and found it equal to the id `POST
  /api/session` returns and every event carries, which is the id Halcyonic's OpenCode adapter keeps
  as the execution's native id. Subagents are child sessions with their own ids, and a fork makes a
  new one.
- **What Halcyonic does:** `opencode` maps to `salidium/opencode`, asked about only when the
  verified discovery document lists it under a minor of 1 or later. Otherwise the answer is what it
  always was, `unavailable` (`runtime_not_observed`), including when Salidium is not running, and the
  credential is not read. A daemon that lists its providers and leaves out `claude-code` or `codex`
  is no longer asked about it (`runtime_not_observed`, "not observing ... now"). A list that breaks
  the stated shape is `incompatible`. One credential-free discovery read is the only new request for
  an OpenCode execution.
- **Status:** tested against the stand-in daemon (`FakeSalidium` with a minor and a providers list,
  `packages/integrations/salidium/src/client.test.ts`), not against any Salidium release. When 1.1 is
  published, re-copy its fixtures, replace the stand-in's list with the published discovery fixture,
  and run the real-wire test with an OpenCode session.

## Waiting for a Salidium that is catching up (2026-10-02)

Salidium's coordinator reported that after the upgrade to 0.7.0, the first read of an older session
replays it on the daemon's main thread for 5 to 8 seconds until a background warm-up finishes. On
the development Mac on 2026-10-02, Salidium 0.6.1 did not answer even its discovery request within 3
seconds while it reingested its history. Halcyonic's client now gives each request 10 seconds and the
whole read (discovery, lookup, report) a budget of 12, so an answer always reaches a headset inside
its own 15 seconds. A read that runs out of time answers `unavailable` with its own reason,
`timed_out`, apart from `unreachable` (nothing listening), in words without the product's name,
since the provenance line names the source: "No answer in time. Press Refresh in a moment." It
guesses no cause, since an impostor on the port that never answers reads the same. For OpenCode,
only a Salidium that is not running keeps the old answer, "not observed"; a timeout, another
process on the port or a broken document is said as it is. Anything on the port may answer while
the discovery file is stale, so an answer is read only up to a size (64 KiB for discovery, 8 MiB
for any other), a discovery answer must name the file's instance before it is validated at all,
and the discovery document may list at most 32 contract versions; TypeBox gathers every error of
a document it refuses, so these bounds, not a cap on the issues kept, bound the work. Tested with a
stand-in that answers late, floods, or names another instance
(`packages/integrations/salidium/src/client.test.ts`).

## Contract 1.1: commits and repository paths (2026-10-02)

Salidium 0.7.0 serves `salidium.consumer` 1.1, and the owner's service runs it. Its discovery
document lists the providers (above) and an `experimental` list this client ignores. A session
report adds, all observed and resolved when the change or boundary happened, without running git:
`revision.atStart` and `revision.atLatestTurnEnd` (the repository root, the full commit, the branch
and when, or null where Salidium did not watch that boundary live), per changed file `repository`
(the root of the working tree that held it, the file's path in it, the main repository when it is a
linked worktree, or null) and `linesRemovedExact`, false when a change replaced a file without
Salidium seeing what it held, so `linesRemoved` is a lower bound.

Halcyonic's understanding now carries, provider-neutrally, each anchor's commit, branch and time
(`revision.at_start`, `revision.at_latest_turn_end`), each file's `repository_path` and
`lines_removed_exact`. No repository root or worktree path crosses into Halcyonic's contract: the
headset needs the commit and the path within the repository, not where it lies on the host. A 1.0
report maps them all to null. The reader takes them as optional, so 1.0 and 1.1 daemons both read.
Tested against the 1.1 fixtures copied from Salidium at `5b53b77` (`fixtures/v1/1.1/`; see its
README), including a file in a linked worktree (its path in the worktree) and one outside any
repository (null); `@salidium/consumer-contract@1.1.0` was staged, not yet published, when they were
copied. What changed? now says "From commit 3f9a2c1 to 8b1e4d7 on main" under its count, shows each
file by its repository path where there is one, and "(+5 −1 or more)" for a lower bound.
