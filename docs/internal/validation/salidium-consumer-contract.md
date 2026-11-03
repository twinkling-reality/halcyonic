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

## A real OpenCode lookup (2026-10-02)

- **Verified on the owner's daemon:** Salidium 0.8.1 serves `salidium.consumer` 1.1 and lists
  `claude-code`, `codex` and `salidium/opencode`. With Halcyonic's consumer credential (token only,
  in the control plane's data directory, mode 600), the control plane's own Salidium source
  (`salidiumUnderstandingFor`) asked about the two OpenCode sessions in OpenCode's store by their
  `ses_` ids. Both answered `available` within 400 ms, read by Halcyonic's validating reader without
  error, `synthetic` false. Neither had changed files or commits, as OpenCode's own session summary
  agrees; one carried the agent's latest statement, `reported`. An id OpenCode does not have
  answered `not_found` (`not_observed`). The gate this lookup passes was checked first on 0.7.0,
  where OpenCode was off: `runtime_not_observed`, and the credential was not read.
- **Why anchors and explanation were empty, from Salidium's coordinator reading its code (not a
  live run):** both sessions were imported after the fact, since OpenCode was turned on later that
  day. Salidium records a commit only for a session start or a round's end it ingests within 120 s of
  the event, reading git in the session's folder, and never for imported history; it schedules an
  explanation only on live events too, and a missing attempt reads `none`. So null anchors and
  `none` are right here, and Halcyonic says nothing about commits. A live OpenCode session in a git
  working tree should carry both anchors; one that ends while watched, with the explainer set to a
  hosted CLI writer, should read `unavailable`, since no writer may explain `salidium/opencode`.
- **A live run, verified (2026-10-02):** Halcyonic's OpenCode adapter drove the pinned OpenCode
  2.0.18 on a local Ollama model, with a scratch copy of Halcyonic's OpenCode settings that enables
  Ollama alone and sets the title model to the same local model, in a scratch git repository with
  one commit, keeping OpenCode's own session store so Salidium watched it live. The agent edited two
  files, ran the tests and committed, with each shell command approved, in about 83 s. Read through
  the control plane's Salidium source just after the round, 15 s later and after OpenCode closed,
  Salidium (0.8.2 by then) answered `available`: both files by their repository paths with exact
  line counts and checked after the change (`inferred`), the commit, whose hash begins the
  repository's head, the test run observed passing 2 of 2 with exit 0, the agent's words
  `reported`, and the verdict passing.
- **Not as expected, reported to Salidium's coordinator:** both commit anchors were null on every
  read although the session ran live in a repository, which Salidium's coordinator called a bug;
  the explanation stayed `none` rather than `unavailable`, perhaps because nothing ended the
  session; and while the session was live a read took up to 10 s, so the read during the approval
  wait ran out of Halcyonic's budget (`timed_out`) and the waiting state was not seen. A read 15 s
  after the round took 34 ms. Salidium's coordinator then found its service had been reinstalled
  during the run, starting 34 s after the session did, so both slow reads fell in a cold daemon's
  first minute; that does not explain the null anchor at the round's end, 43 s after the restart.
  Halcyonic shows null anchors and `none` as given.
- **Not yet verified:** a waiting request or the `unavailable` explanation read from a live OpenCode
  session, and commit anchors from one.

## The 0.8.2 baseline, repeated (2026-10-03)

Salidium's fix for these (abb7a93, "Fix live OpenCode anchors, explanation status, and reads held
until a step finishes") is on its main but in no release on this date; the owner's service runs
0.8.2. This run is the baseline a run after the upgrade compares with, read the same way.

- **How:** the same task as before (change `add`, add a test, run `node --test`, commit), driven by
  Halcyonic's OpenCode adapter on the pinned OpenCode 2.0.18 and the local
  `qwen3.6:35b-a3b-nvfp4`, its settings enabling Ollama alone, in a fresh scratch git repository,
  read through the control plane's own Salidium and Seorak sources from a scratch data directory
  (links to the owner's credential files, mode 600, never copied or printed), not the owner's
  control plane or journal. The two shell commands were approved; the round took about 6 minutes
  with the Mac's load average between 20 and 167 from other work.
- **Verified, read 4 minutes after the round (330 ms):** `available`, Salidium 0.8.2, contract
  1.1, `synthetic` false; both files by their repository paths with the line counts git gives
  (`math.test.js` +6, `math.js` +3), checked after the change (`inferred`); the one commit; the test
  run observed passing 2 of 2 with exit 0; the agent's words `reported`; the verdict passing.
- **As before, the known bug:** both commit anchors null, and the explanation `none` with nothing
  generated. Expected on 0.8.2.
- **Under load:** the read while the approval waited ran out of Halcyonic's 10 s budget
  (`timed_out`), and two reads just after the round failed after about 8 s (`unreachable`: the
  connection failed without an answer). The daemon had run for 10 hours and did not restart. With
  the load as high as it was, this says nothing about Salidium, and the waiting state was again not
  seen.
- **Still not verified:** a waiting request, the `unavailable` explanation and commit anchors from a
  live OpenCode session. All three wait for a Salidium release with abb7a93 and the service upgraded.

## Salidium 0.8.3 source re-baseline (2026-10-31)

Salidium's checkout on this Mac is at `24cbd10` (`release: 0.8.3`, `@salidium/consumer-contract`
`1.1.0`), which includes `abb7a93` ("Fix live OpenCode anchors, explanation status, and reads held
until a step finishes") along with experimental `salidium.project-map` and `salidium.execution-links`
v0 ([salidium-project-map.md](salidium-project-map.md)). Read-only verification against `24cbd10`:

- **Consumer contract v1.1 stability:** `packages/consumer-contract/src/index.ts` (sha256
  `26fa6dd6092531ec3febd72a5b9be7961c4525fb19119728cfe3907f8c92ab3d`) is unchanged at
  `CONTRACT_VERSION = "1.1.0"`, `CONTRACT_NAME = "salidium.consumer"`, `CONTRACT_MAJOR = 1`,
  `CONTRACT_MINOR = 1`.
- **1.0 and 1.1 fixtures match value for value:** in both
  `packages/integrations/salidium/fixtures/v1/` (12 files) and `fixtures/v1/1.1/` (12 files), 10 of
  12 files are byte-for-byte identical to Salidium's `packages/consumer-contract/fixtures/v1/` and
  `fixtures/v1/1.1/` at `24cbd10`, and the remaining 2 (`session-report-failing.json` and
  `session-report-verified.json`) are JSON-identical after Biome single-line array formatting
  (`session-list.json` is omitted because Halcyonic never lists all sessions).
- **What remains:** live verification of a waiting request, the `unavailable` explanation and
  commit anchors from a live OpenCode session against the running 0.8.3 daemon.

## Halcyonic's sandboxed OpenCode store (2026-11-03)

- **Live:** the owner's daemon is Salidium 0.8.3 (`~/.salidium/service/current`), serving
  `salidium.consumer` 1.1 on 127.0.0.1:47822 and listing `claude-code`, `codex` and
  `salidium/opencode`. Read through the control plane's own Salidium source with a copy of
  Halcyonic's credential (mode 600, removed after), a Claude Code session on this Mac answered
  `available` in 53 ms: verdict `working` and `observed`, five files with repository paths and exact
  line counts, `revision.at_start` present and `at_latest_turn_end` null while its turn ran,
  explanation `none`. Unknown Claude Code, Codex and OpenCode ids answered `not_found`.
- **Not readable:** a real OpenCode execution started through a scratch control plane
  ([local-models.md](local-models.md), the same day) answered `not_found`, "may not have reported
  yet", which never changes. Salidium reads `opencode/opencode.db` under its own process's
  `XDG_DATA_HOME`, else under `~/.local/share` (`adapters/opencode/src/storeSource.ts` in its
  checkout at `a078498`, the same function in the 0.8.3 service's bundle), and its daemon's
  environment sets no `XDG_DATA_HOME`. On macOS Halcyonic's OpenCode runs only inside its sandbox,
  whose data folder is `<data dir>/opencode-sandbox/data` (ADR 0028), so its sessions are in
  `<data dir>/opencode-sandbox/data/opencode/opencode.db`.
- **What Halcyonic does now:** an OpenCode execution that started no earlier than that store was
  made answers `unavailable` (`runtime_not_observed`), "Salidium, as installed, does not read
  Halcyonic's own OpenCode store, where this session is kept.", without asking Salidium and without
  reading the credential. Only metadata is read: the store must be a regular file and each folder
  from the sandbox's down to it a real one, never a link. A session that started before the store
  existed, as one from before the sandbox, is asked about as before. Tested against the stand-in
  daemon listing `salidium/opencode` (`apps/control-plane/src/intelligence/understanding.test.ts`);
  whether Salidium should read Halcyonic's store stays an open question.
- **Through the routes:** the opt-in `apps/control-plane/src/intelligence/live-routes.test.ts` reads
  `GET /api/executions/:execution_id/understanding` for an execution whose synthetic stand-in runs
  nothing, its native id a Claude Code session on this Mac. Later the same day that session
  answered `available` from 0.8.3 with 34 files, all with repository paths and exact line counts,
  five commits, both revision anchors present, verification runs for tests (passed, scope partial)
  and lint (passed, scope unknown), and explanation `none`; an unknown session answered `not_found`.
  So commit anchors and verification runs are verified live for a Claude Code session; for an
  OpenCode one they are not, since none Halcyonic starts can be read.

## If Salidium read Halcyonic's folders (2026-11-03)

- **Question:** what would Understand show for a real task on a local model if Salidium read the
  Codex home and OpenCode store Halcyonic keeps its sessions in?
- **Method:** a copy of the released 0.8.3 service bundle, run apart from the owner's: a scratch
  `HOME`, a `SALIDIUM_HOME` of its own on port 47895 with the OpenCode source on and the explainer
  off (`SALIDIUM_EXPLAIN=0`), no agent command on `PATH`, and `CODEX_HOME` and `XDG_DATA_HOME`
  pointed at copies of a scratch control plane's Codex home and sandboxed OpenCode store, which held
  the two real tasks of the morning ([local-models.md](local-models.md), same day). A consumer
  credential of that instance's own; Halcyonic's Salidium client read both sessions. The daemon held
  only its own loopback listener and wrote nothing in the scratch homes; the owner's homes, Salidium's
  launchd jobs and its daemon were unchanged.
- **Result:** both `available` from 0.8.3, contract 1.1, `synthetic` false, verdict `pass`
  (`observed`), the agent's latest words `reported`, a full test run observed passing. The OpenCode
  task: two changed files with exact line counts (+5 -1 and +4 -0), each checked after the change
  (`inferred`). The Codex task: no file, since its local model wrote both files with shell heredocs,
  which Salidium records as commands (as on 2026-10-02 with `qwen3.8`). Neither had commit anchors or
  repository paths, the sessions being read after the fact, and the explanation read `disabled`.
- **So:** reading those folders would give Understand real answers for tasks on local models, which
  it has none of today. It needs a change in Salidium, a second watched folder, and the owner's yes:
  the open question stays theirs. Not tried: a session watched live, its anchors and waiting states.

## The comprehension brief and its delta (2026-11-03)

- **Question:** can Halcyonic read the comprehension brief that Salidium's owner deployed on this Mac today,
  `/api/sessions/:id/brief` with `?delta=`, `?query=` and `?steer=`, and does any of it change anything?
- **Running on 47822:** Salidium 0.8.3 (the discovery document's `salidium.version`), `salidium.consumer` 1.1 at
  `/consumer/v1`, experimental `salidium.project-map` 0.0, providers `claude-code`, `codex` and `salidium/opencode`. The
  daemon started at 20:18:11Z from a service bundle replaced at 20:18Z (`~/.salidium/service/current/salidium.mjs`,
  1,770,272 bytes, sha256 `c70dda0af1e081d1d99e4a299595072e4229483b748b6d55c0dc650a6bf29437`). That bundle names the
  brief's route, its delta scopes, the view algebra and steering; the bundle it replaced (2026-11-02) names none of
  them. Read from the discovery document and the installed files only: no request was sent and no credential read.
- **Not committed:** the canonical checkout is at `a078498` (`v0.8.3-3`) with 24 tracked files changed and 6 new
  paths (one a folder), none committed, among them the route (`packages/daemon/src/server/httpServer.ts`),
  `packages/core/src/canvas/computeSessionDelta.ts` and `viewAlgebraAndSteering.ts`, the brief's schema in
  `packages/protocol/src/events.ts`, and Salidium's ADR 0007 (dated 2026-11-02). The daemon on this Mac is 0.8.3 with
  that work on top, not a release.
- **Not in the consumer contract:** `packages/consumer-contract` is unchanged and nothing under `/consumer/v1` serves a
  brief or a delta. The route is Salidium's own `GET /api/sessions/:id/brief`, and every request under `/api/` must
  carry the daemon's own owner token (`authorized()` in `httpServer.ts`), so a consumer credential is refused there (from
  the code, not tried). Salidium's CLI, `salidium show [session] --brief --delta=... --json`, calls the same route with
  that token. The same `/api/sessions/:id` routes also answer `DELETE` (forget a session) and a `POST`; the brief is
  GET only.
- **What it answers:** `{ sessionId, brief, delta, viewAlgebra, steering }`. `brief` is a `CanvasBriefSpec`, with a
  schema in the protocol package: an `archetype` (`verified-edit`, `failing-or-unverified-edit`, `blast-radius-edit`,
  `read-only-exploration` or `active-mid-flight`), a `mode` (`deterministic`, or `hybrid-explained` when a move uses the
  explainer's prose), and two to four moves (what changed and why, what it touches, what was checked, what to look at
  next), each with its epistemic class, text and tokens, plain text and references. `delta`, null without a scope, is a
  `SessionDeltaSummary`, a TypeScript interface with no schema: a label; each file that drifted, with its drift
  (`invalidated`, `regressed`, `blast-expanded`, `newly-touched` or `newly-proven`), lines added and removed, whether it
  was checked after its change and how (`test`, `static-check` or `none`), how many callers it has that were not
  changed, its turns and its subagent; counts of each drift and of files unchanged; the checks that failed and those
  that passed within the scope; and the callers newly exposed, from the project map. In the code, a file changed in the
  scope and not checked after its change is `regressed` whenever any check in the scope failed or was partial, whether
  or not that check concerns the file; Salidium's ADR 0007 says a `regressed` file is implicated in the failing check.
  Without the project map's links a file's caller count is 0 and `blast-expanded` never appears.
- **Scopes:** `since-last-pass` is since the latest full passing check (passed, not partial, not lint), labelled "Since
  last passing check", and covers everything when no check has passed. `since-seq:<seq>` is since an evidence sequence
  the caller names, labelled "Since last seen (#seq)": Salidium's own interface keeps that mark for its viewer, and the
  daemon keeps none. Also `latest-turn`, `turn:<N>` (from 1), `subagent` (the first) and `subagent:<id>`; any other
  value gives no delta.
- **Steering only computes:** each `?steer=` directive (several, or joined with `;`) is read as a revert, reroute,
  decouple or verify of a named file and compiled into a markdown steering brief marked `planned`, returned in the
  answer. The functions import only types and one pure helper (no file, network, process or clock), and the route
  stores nothing and sends nothing to an agent. Not called here: it needs the owner token.
- **So:** Halcyonic neither reads nor depends on the brief as deployed; reading it would take the owner token, which is
  Salidium's owner's own, never a consumer's. A delta Halcyonic could show would have to be published in the consumer
  contract, for example on the report by evidence sequence, which is for Salidium's owner to decide
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
