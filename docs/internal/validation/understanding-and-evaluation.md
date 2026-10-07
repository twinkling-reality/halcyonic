# Understanding and Evaluation in the workspace

- **Question:** Can the XR workspace show what the understanding source (Salidium) concluded and
  what the evaluation source (Seorak) measured about an execution, each with its provenance and
  availability, from a control plane and in the recorded demonstration; and what of that is
  verified?
- **Date:** 2026-09-29; real-source check on 2026-09-30.
- **Environment:** an Apple M5 Max with macOS 26.7; Node.js 24.15.0; .NET SDK 10.0.401; Unity
  6000.3.25f1 in batch mode with `com.unity.ugui` 2.0.0; Halcyonic on top of `d3fd5ca`. On the same
  Mac: Salidium 0.6.0 installed, its menu bar app running and its daemon not; Seorak's local plane
  running on 127.0.0.1:4317 from the Seorak checkout.
- **Method:** node and .NET tests; `pnpm demonstration:record`, which reads stand-in sources through
  the control plane's routes; a real control plane process with the mock runtime (the C# live
  test); `WorkspaceRender.Check` in batch mode, with close-ups at a Quest 3's pixel density; the
  real sources read only: Salidium's discovery file, and Seorak's opt-in live test with the owner's
  credential read inside the test process; TextMeshPro's source in the Unity installation.
- **Status:** Verified from the contracts to rendered labels with stand-in sources, and against a
  real control plane process. One real Codex execution was read through both sources on 2026-09-30.
  The real answers have not been checked on a headset.

## Findings

### The real sources on this Mac, read only

- **Salidium.** `~/.salidium/consumer.json` does not exist, so the daemon is not running: a
  control plane on this Mac answers understanding for a Claude Code or Codex execution
  `unavailable` (`not_running`), and the section says "Understanding unavailable: Salidium is not
  running: it has not published its discovery file." Nothing was started, written or configured.
- **Seorak.** The opt-in live test (`packages/integrations/seorak/src/live-seorak.test.ts`) passed
  its three checks against the running plane with the owner's integration credential, read in the
  test process and sent only to the plane, never printed: 401 without a credential, `not_found`
  (`not_captured`) for a session it has not captured, and `unauthorized` (`credential_rejected`) for
  a credential it did not issue. Its captured-session check was skipped, since no captured session id
  was at hand. About five requests in all.
- **The mock runtime.** A real control plane process answers both routes `unavailable`
  (`runtime_not_observed`) for a mock execution. The C# live test reads both through
  `ControlPlaneApi` as an `IIntelligenceReader`, and the sections say "Understanding unavailable:
  Salidium does not observe sessions of the mock runtime." and "Evaluation unavailable: Seorak does
  not observe sessions of the mock runtime."

### A real execution end to end

On 2026-09-30, a scratch control plane started one Codex 0.157.0 execution on the local Ollama
`qwen3.6:35b-a3b-nvfp4` model. It created a file and checked its contents in an ignored scratch
directory. The control plane and both reads used a separate data directory. Salidium's 0.6.0
daemon read its normal Codex hooks and rollout, with `SALIDIUM_EXPLAINER=off` to prevent optional
model calls; its read-only consumer credential was created for this check and revoked afterward.
The already running Seorak local daemon used the owner's existing read-only integration credential.
Neither answer was synthetic.

- The execution and its workstream completed. `GET /api/executions/:id/understanding` returned
  `available`, with Salidium 0.6.0 as source, a verdict, a `reported` agent statement, changes,
  verification, review and remaining sections. It reported "No files changed" and "No checks yet":
  the scratch file was inside the repository's ignored `.private` directory, and no test framework
  ran. Its explanation status was `disabled`, as intended to avoid model spend.
- `GET /api/executions/:id/evaluation` returned `available` with a nonsynthetic Seorak source.
  Cost, outcome and verification each reported available, complete coverage for one matched and
  included session, and fresh data. The cost field `estimated_usd` was null. Seorak's known Ollama
  token parsing gap may explain this, but this run did not isolate the cause. The verification lens
  existed and had no verification run to describe.
- The real answers were read through Halcyonic's routes, not rendered in the headset. The live
  source sections, a nonempty change report, a verification run and a numeric cost estimate remain
  to be checked in the workspace after the Seorak parser fix.

### The demonstration's answers

- `pnpm demonstration:record` reads them through the control plane's own routes from stand-ins for
  Salidium and Seorak (the integrations' test doubles, serving the real wire contracts over
  loopback with documents derived from the demonstration's journal and story) and keeps one where it
  differs from the answer before it: 58 understanding and 47 evaluation answers for the story's three
  executions across the recording's 27 nodes. It records in about 4 s.
- The recording grew from 485 to 719 KiB and reads in 91 ms on this Mac with .NET 10.
- Every answer is available and marked synthetic, the stand-in for Salidium reports the version
  `simulated` and an instance id of zeros, and together they show all five of Salidium's classes.
  The directed work reads "Waiting for you" at its approval, "1 test failing" after approving, "2
  files changed, verified" after the first recorded instruction, and "2 files changed, unverified"
  after denying; its evaluation's outcome is "unavailable: not yet computed" until its first turn
  ends, and its checks go from none to 1 of 1 failing to 1 of 2 passing.

### TextMeshPro

- Rich text off stops markup but not backslash escapes: TextMeshPro turns `\uXXXX` and
  `\UXXXXXXXX` into characters on any label, and `\n`, `\r`, `\t`, `\v` and `\\` with its default
  escape parsing ([workspace-interaction.md](workspace-interaction.md)). On a real label, 45
  characters of such sequences, markup and control characters from a source showed as 22 characters
  as they were, and as exactly 45 once the client core made them plain and doubled every backslash.
  The sections do that for every label. Since 2026-09-30 the one rule for text from outside shows
  control and format characters by their code points instead of dropping them, so the same text
  shows as 61 characters ([workspace-interaction.md](workspace-interaction.md)).
- A quote cut short in italics ended without its ellipsis, so the sections set claims apart by tag
  and color instead; the check confirms a quote cut short ends in "…".
- The minus sign U+2212 in Salidium's change summaries is not in the static atlas of Liberation Sans
  SDF; the dynamic fallback draws it. Drawing it in the editor wrote the glyph into the committed
  fallback asset, so the render check swaps such characters for the render only and logs them.

### The renders

- With the characters 2.4 m away, the workspace's center is 30.0 degrees below eye level, below
  them; on a desk, 11.1 degrees below, above them. In both, 0 pixels of the workspace, and of each
  section, change when the stage behind it is drawn.
- Each section's lines fit: seven under the provenance, a part's own statement on two rows where it
  needs them, none cut short. Long claims end in an ellipsis.
- At about 25 pixels per degree, the headset's density near the middle of its lenses, the tabs, the
  provenance line, the tags and every line read clearly in both placements.

## Consequences

- The workspace shows Understanding and Evaluation from a control plane and in the demonstration
  through the same contracts and client code
  ([ADR 0019](../decisions/0019-the-demonstration-reads-simulated-sources-through-the-real-flow.md),
  [XR_CLIENT.md](../architecture/XR_CLIENT.md)).
- The headset checks are in [XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md): the sections by
  hand in both placements, a glyph from the dynamic fallback, and the demonstration's sections.
- Run the real check above when a real execution can run without model spend, and re-run Seorak's
  captured-session check on a session that ran tests.

## The answers from evidence, and a second real run (2026-10-02)

The workspace now answers Help me understand as three questions (What changed?, Why?, How was it
built?) and What was checked? from both sources ([XR_CLIENT.md](../architecture/XR_CLIENT.md)). The
demonstration's stand-ins gained what those answers show: Why lanes on every explanation, a removed
file and a moved one in the two background stories, and an agent question read as waiting. The
headset's layout of these answers is interim: the owner judged the one-panel workspace too crowded,
and a redesign lane sets the surface; the presenters stay as they are.

A scratch control plane (its own data directory and project root inside the ignored `.private/`
folder, port 47893) ran Codex 0.157.0 on Ollama, twice, on a tracked file in a scratch git
repository: `tally.js` and its test. Codex used a scratch `CODEX_HOME` whose `config.toml` names the
`ollama` provider and model, since a listed runtime needs its model from Codex's own list, with only
today's folder of `sessions` linked into `~/.codex/sessions`, so the rollout landed where Salidium
reads it (linking the whole folder made Codex index 4.5 GB of history before it would start).
Salidium 0.6.1, the owner's service, read it through a read-only consumer credential created for the
check and revoked afterward; Seorak through the owner's existing credential. No hosted model.

- **qwen3.6:35b-a3b-nvfp4** read the two files again and again for 17 minutes, then called
  `exec_command` with empty arguments in a loop (Codex answered each "missing field `cmd`"). The
  round was interrupted through the control plane, and the runtime confirmed it. Salidium answered
  "No files changed · 266 commands", no checks, no explanation; while it reingested its history
  earlier, its port did not answer even the discovery request within 3 seconds, and the read was
  `unavailable` (`unreachable`).
- **qwen3.8:27b-nvfp4** finished in 14 minutes. It was told to edit with `apply_patch` but rewrote
  both files with `printf … >` shell commands, then ran `node --test` once. Salidium answered
  `available`, not synthetic: "2/2 tests passed (node-test)", the run observed with exit code 0
  ("explicit"), not stale, one agent statement about it, and "No files changed · 4 commands". That
  is true to its evidence: Codex records a shell redirect as a command, never as a file change, so
  What changed? would say no files changed although two did. Whether Codex offers an `apply_patch`
  tool to an Ollama model is not visible in the rollout, which records calls, not the tools offered:
  open.
- Seorak answered `available`, not synthetic, for both: one matched and included session, complete
  and fresh; cost `null` (an Ollama model has no list price; shown as unknown, never zero); outcome
  `error_count` 0 with every other measure pending; the verification lens empty, "No verification
  result was captured.", as Seorak measures verification for Claude Code only.

So a real "What changed?" with files from a local model has still not been seen: it needs a model
that edits through Codex's patch tool, or Salidium reading file changes another way. The check line
("Tests passed at 16:16: 2/2 tests passed (node-test)", ran after no recorded change) and the
measurement are real.

## A Codex run for Checks that did not finish (2026-10-03)

To read Seorak's measurements of a live Codex session again (Seorak collects Codex and Claude
Code, not OpenCode, so an OpenCode session reads "not observed" by design), Halcyonic's Codex
adapter drove the pinned Codex 0.157.0 with `model_provider: "ollama"`,
`qwen3.6:35b-a3b-nvfp4`, a 65,536 token context and `approval_policy: "untrusted"`, on the same
small task in a scratch git repository, read through the control plane's own sources from a scratch
data directory.

- **The model never answered.** The Mac's load average was between 280 and 530 and its swap nearly
  full from other work; the thread recorded its context and then nothing for 14 minutes, when the
  run's time limit closed it (`turn_aborted`). The repository was untouched.
- **The provider was local:** the thread's start reported `ollama/qwen3.6:35b-a3b-nvfp4`, the
  rollout records `model_provider` `ollama` and that model, and the adapter refuses a thread whose
  provider or model differs from what it asked. No model request completed at all.
- **A gap, as it was:** Codex ran with its default `CODEX_HOME`, `~/.codex`, so that Seorak and
  Salidium would see the rollout, and that folder holds the owner's Codex login (`auth.json`) and
  configuration. The 2026-09-29 runs that saw app-server make no request beyond loopback used a
  scratch `CODEX_HOME` with no login, and nothing captured the network this time. Non-model
  requests to OpenAI at startup, such as an account or feature check made with that login, are
  therefore unverified for this run. Nothing found suggests a hosted model call.
- **The repeat's home:** a scratch `CODEX_HOME` with no login, naming only the `ollama` provider,
  with `features.plugins = false`, `analytics.enabled = false` and `check_for_update_on_startup =
  false`, its `sessions` folder holding only a link to the day's folder of `~/.codex/sessions`, so
  Seorak's collector and Salidium see the rollout. Without `features.plugins = false` app-server
  reached GitHub at startup; the runtime test is in [local-models.md](local-models.md). A watcher
  listed the app-server's internet sockets every second for the whole of each run.

### The repeats (2026-10-03)

- **Network, verified:** in both watched runs the only socket was 127.0.0.1:11434 (Ollama); nothing
  left loopback. One short run went unwatched: the watcher read the previous run's log, saw it had
  ended and stopped at once; the run was stopped seconds into its turn, with the same home.
- **A run that did nothing:** under `approval_policy: "untrusted"` Codex asks before even reading,
  and the run's allowlist then approved only `node --test` and git, so all 19 requests (`cat`,
  `ls`, `find`, `pwd`, `echo`) were denied and nothing was read, changed or committed.
- **A run that stalled:** with the coordinator's allowlist (`cat`, `ls`, `pwd`, `head`, `echo`,
  print-only `sed -n`, `find` without actions, `node --test`, and git `add`, `commit`, `status`,
  `diff` and `log`, each argument a relative path inside the repository, no shell operator), the
  model read the files, then asked for multi-line commands and one with an operator, all denied,
  and after 42 s produced nothing more for 14 minutes, when the run closed it.
- **Seorak, as Checks would read it:** for the run that did nothing, `available`, not synthetic,
  one matched and included session, complete and fresh, while the approval waited, just after the
  turn and 3 minutes later; cost `estimated_usd` `null` (a local model has no list price: unknown,
  never zero); every outcome measure `null` (commits landed, uncommitted, line survival, errors,
  end reason); the verification lens empty. 12 s into the stalled run it was still `not_found`
  (`not_captured`): Seorak had not yet captured the session.
- **Salidium 0.8.2:** `available` for both, true to sessions that changed nothing: no files, no
  commits, no checks, the explanation `none`, both anchors null.

So neither run gives Checks a measured outcome. The evidence for Checks stays the 2026-10-02 run
above: Seorak's measurements with their provenance, the cost honestly unknown for a local model,
and a measured outcome (`error_count` 0, the rest pending). A numeric cost needs a hosted model and
the owner's approved spend.

## Which Codex folders Salidium and Seorak read (2026-10-07)

Since 2026-10-07 Halcyonic runs Codex in a home of its own, `~/.halcyonic/codex-home`, never the
person's `~/.codex` ([ADR 0011](../decisions/0011-codex-app-server-stable-surface.md), note of
2026-10-07), so its rollouts land in `~/.halcyonic/codex-home/sessions`. Whether either source can
see them was read in their source code, read only; nothing was run, installed or reconfigured. The
two launchd plists were checked for key names only, since Seorak's holds its worker keys.

- **Salidium** at `abb7a93` (branch `main`) reads `sessions` and `archived_sessions` of one Codex
  home: `$CODEX_HOME` of its own process, or `~/.codex`
  (`packages/adapters/codex/src/codexAdapter.ts:24-27`; the CLI's status check applies the same
  rule, `packages/cli/src/integrations.ts:170-174`). Its daemon configuration has no setting for an
  extra folder (`packages/daemon/src/config/daemonConfig.ts:52-69`), and it refuses a hook's
  transcript path outside those folders (`packages/daemon/src/ingest/hookIngress.ts:164-172`). Its
  launchd service passes only `HOME`, `SALIDIUM_HOME`, `PATH` and the `SALIDIUM_*` variables
  (`packages/cli/src/macosService.ts:469-485`), and the installed
  `~/Library/LaunchAgents/com.salidium.daemon.plist` names no `CODEX_HOME`, so the running daemon
  reads `~/.codex`. It filters rollouts on no `thread_source` or `originator`.
- **Seorak** at `e4f33e92` (branch `main`) reads one sessions folder, `~/.codex/sessions`, and
  ignores `CODEX_HOME` (`packages/collector/src/codex-tailer.ts:90-92`). `SEORAK_CODEX_DIR`
  replaces that folder (`:91`); the installed `~/Library/LaunchAgents/app.seorak.collector.plist`
  sets neither it nor `CODEX_HOME`. It skips subagent, forked and child threads
  (`packages/collector/src/adapters/codex.ts:817-828`), not Halcyonic's.

So, as installed, neither reads Halcyonic's Codex home. Each can be pointed at another folder only
in place of the person's own, which would stop it reading their Codex sessions, so nothing was
changed: their settings are the owner's call, and a second watched folder is a change to each
product ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

What the control plane does about it: nothing journaled says which home a Codex execution used, so
it decides by Codex's own record of the thread. When the thread's rollout,
`rollout-<time>-<thread id>.jsonl`, is in Halcyonic's Codex home, in `sessions/YYYY/MM/DD` for the
local date the execution started, the day before or the day after, Understand and Checks answer
`unavailable` with `runtime_not_observed` at once, without asking Salidium or Seorak and without
reading either credential (`intelligence/codex-home.ts`). Only names are read, never a file's
contents; no link is followed at any level; the native id must be a thread id (a UUID) before it
reaches a path. A Codex execution whose rollout is not there, as for a thread run in `~/.codex`
before this change, is asked about as before. Tests cover each case: a rollout there, none, an id
that is not a UUID (no filesystem call at all), and a link in place of the home, `sessions`, each
date folder or the rollout. Lane W's headset line for `runtime_not_observed` is "it doesn't follow
tasks this agent app runs". The line "it hasn't seen this task yet", with Refresh, would be a false
way on, since a refresh can never find the session.

Two residuals, accepted: in the first moments of a new execution, before Codex writes the
thread's rollout, the sources are asked and answer that they haven't seen the task yet (on
2026-10-07 the network probe found the rollout already written when the start returned, so the
window closes by the time the start returns); and a
rollout the person deleted from Halcyonic's home makes its execution read the same way, though
neither source ever had it.
