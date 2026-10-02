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
