# ADR 0011: Target Codex's app-server stable surface, pinned, on a server Halcyonic launches

- Status: Accepted
- Date: 2026-09-27

## Context

Codex offers two developer surfaces: `codex exec --json` (and the TypeScript SDK that spawns it),
documented as stable, and `codex app-server`, a JSON-RPC server documented as experimental and not
supported for production. Both were run on 2026-09-27 against a fake provider
([codex-capabilities.md](../validation/codex-capabilities.md)).

Observed: exec forces its approval policy to `never`, so a person can never be asked to approve
anything, and its interrupt ends the stream without a final event. App-server's stable surface,
without the experimental opt-in, supported approvals, steering, interrupt, discovery, diffs and live
output. It has no protocol version and ships every few days, and it has defects: interrupt leaves
running commands running, an interrupt for a finished turn goes unanswered, `turn/start` on a busy
thread silently steers, SIGKILL can orphan commands, and a thread loaded by another Codex process
cannot be resumed.

Salidium and Seorak observe Codex through rollouts under the default `CODEX_HOME`, so Halcyonic's
Codex threads must use it too.

## Decision

- The Codex adapter targets `codex app-server` over stdio, using only methods on its stable surface
  and never the `experimentalApi` opt-in, pinned to one exact Codex version (0.157.0 as tested) and
  its binary checksum, with client types generated from that binary.
- Halcyonic launches the server from a configured binary, in its own process group, with the
  developer's default `CODEX_HOME`, and supervises it so that no server or command outlives the
  control plane.
- Capabilities: start_execution, instruct_at_rest, instruct_while_running (`turn/steer` with the
  expected turn id), respond_to_approval (approve as `accept`, deny as `decline`) and interrupt.
  An interrupt is recorded as ending the turn only; commands still running are reported as unknown.
- Halcyonic tags its threads with its own `clientInfo.name` and `threadSource`, keeps the thread id
  from `thread/start` as the native id, and drives only threads no other Codex process holds.
- Upgrading means repeating the smoke test first.

## Alternatives considered

- **exec and the TypeScript SDK.** Documented as stable, but without approvals the person in the
  headset could only watch; Halcyonic's defining interaction needs approvals and steering.
- **Both surfaces.** Twice the verification for no user need.
- **Attaching to the user's running Codex app.** Blocked by the one-writer lock, and its version and
  configuration are unknown.

## Consequences

- One pinned binary and a repeatable smoke test per upgrade, as for OpenCode.
- The adapter must guard each defect: interrupt only the turn it knows is active, treat an
  unanswered interrupt as unknown effect, steer only with `turn/steer`, stop the server with stdin
  end of input and its process group on exit, and show a thread held by another process as busy.
- Codex's plugin traffic to chatgpt.com and GitHub, and the workspace path and commit hash it sends
  to the provider, are documented in SECURITY.md when the adapter lands.
- Revisit if app-server gains a versioned contract, or if the stable surface loses a method the
  adapter needs.

## Note, 2026-09-27

Building the adapter corrected one consequence. Codex 0.157.0 starts every command in a session of
its own, so a signal to the server's process group reaches the server but never its commands. Of
"stop the server with stdin end of input and its process group on exit", only the end of input
stops commands, because Codex stops them as it shuts down. The adapter therefore stops the server
by ending its input, and when it has to kill the server it kills the server's descendants too. A
server killed by anything else still leaves its running commands behind
([record](../validation/codex-capabilities.md)). The adapter also relaunches a server that exits
and resumes its threads, settling a turn that was running from Codex's own record, where it reads
`interrupted`. The provider metadata and the plugin traffic are documented in
[SECURITY.md](../architecture/SECURITY.md). The decision is otherwise unchanged.

## Note, 2026-10-07

A decision the owner delegated to the coordinator on 2026-10-07 replaces "with the developer's
default `CODEX_HOME`" and the context's "Halcyonic's Codex threads must use it too": **Codex is
offered for local models only, from a home of its own.**

- The adapter launches the pinned 0.157.0 with `CODEX_HOME` set to `<data dir>/codex-home`
  (`~/.halcyonic/codex-home`), chosen by the control plane, never the person's `~/.codex`. It makes
  the folder with mode 700 and, before every launch, refuses a link, another user's folder, one
  others can open, or one holding a sign-in (`auth.json`). `CODEX_HOME` is no longer inherited,
  and configuring it, or a variable Codex signs in with, is refused.
- Every launch passes as `-c` overrides, which outrank the home's `config.toml`:
  `features.plugins=false` and the other network-reaching features 0.157.0 has on by default set
  to false, the update check, analytics and web search off, and sign-in and MCP credentials kept
  out of the keychain. On 0.157.0 the undocumented `features.plugins = false` was the only setting
  that stopped the startup connection to GitHub ([local-models.md](../validation/local-models.md)).
  A managed layer outranks the overrides, so every start checks in `config/read` that each is
  applied, and refuses otherwise. Variables that move Codex's work or data off the Mac or out of
  the home, and proxies, never reach it.
- A thread runs only on a model provider served on this Mac (a loopback address in Codex's
  configuration; Ollama in practice), named on the thread; models Ollama runs on its own remote
  service are refused, and the model list holds only models served on this Mac. Hosted Codex
  models are not offered while the question of the ChatGPT sign-in stays open
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- `pnpm mac-setup local-model` writes the home's `config.toml`: the `ollama` provider, the model,
  a context of 65,536 tokens and compaction at 52,000.
- The network probe in the Codex end to end suite (nothing beyond loopback from the server's
  process tree through startup, idle and a full run on a local model) is the check to re-run on
  every Codex upgrade, beside the smoke test.

Consequences: rollouts are written under Halcyonic's home, and neither Salidium nor Seorak reads
it as installed (each reads one Codex location, the person's,
[understanding-and-evaluation.md](../validation/understanding-and-evaluation.md)), so the control
plane answers Understand and Checks as `runtime_not_observed`, without asking either, for a Codex
execution whose rollout it finds in Halcyonic's home; one run in `~/.codex` before this change is
asked about as before. Whether they should also read Halcyonic's home is their owners' question
([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)). The person's own `~/.codex` settings, sign-in and plugins no
longer apply to Halcyonic's threads. A project's own `.codex/config.toml` does, with its MCP
servers, the sandbox's network access, hooks and command rules: on `thread/start` Codex records a
folder it can write as trusted in the home's `config.toml`. Keeping projects untrusted is open
([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)). The decision is otherwise unchanged.
