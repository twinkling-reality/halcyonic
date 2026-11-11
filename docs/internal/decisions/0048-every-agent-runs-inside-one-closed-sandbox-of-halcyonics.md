# ADR 0048: Every agent runs inside one closed sandbox of Halcyonic's, whatever its runtime

- Status: Accepted (the lead's decision of 2026-11-10; the package, the owner's choice of 2026-11-11)
- Date: 2026-11-11

## Context

- On 2026-11-10 what an agent could read depended on its runtime
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md), the preview row):
  - **Codex on macOS**, in a starter's project, ran inside a profile of Halcyonic's that is closed by default
    ([ADR 0041](0041-a-new-apps-agent-is-confined-to-its-project.md)): the folder, its own Codex home and temporary
    folder, the tool folders and system paths, never the person's home or other projects.
  - **OpenCode** ran inside a profile of Halcyonic's that is open by default, but for Halcyonic's data and a list of
    credential paths ([ADR 0028](0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md)): almost the whole disk.
  - **Claude Code** ran with no sandbox of Halcyonic's.
- Two decisions need the closed kind everywhere:
  - Working without asking ([ADR 0043](0043-a-person-may-let-a-tasks-agent-work-without-asking-where-its-runtime-keeps-it-in-bounds.md))
    is offered only where the runtime keeps the agent in bounds.
  - A preview reaches a person only where every run that wrote it was confined (`reads_confined`), since an agent
    that can read outside its folder can put any file's text in a page the headset's browser runs with the network
    on.
- The closed profile was code of the Codex adapter. Integrations never depend on each other (SYSTEM.md), so OpenCode
  and Claude Code could not use it from there, and the control plane already imported its tool folders from that
  adapter.
- Verified on this Mac, pinned OpenCode 2.0.18, no model (VRIFY, BOX-3b experiment, 2026-11-11): an OpenCode server
  runs whole turns inside the closed profile once it has its own port to listen on, a home of its own, and
  Halcyonic's OpenCode settings to read. Without a home of its own it needs a listing of the person's home and its
  parents, where it looks for instructions.

## Decision

1. **One profile.** Halcyonic's closed-by-default Seatbelt profile for one execution's folder lives in
   `packages/seatbelt` (`@halcyonic/seatbelt`): the profile (`confinedProfile`), the base policy it builds on, the
   rule that opens a worktree's or a submodule's git data only when the repository names the folder back
   (`gitDataOutside`), the secret-shaped files hidden in an original (`secretsUnder`), and the tool folders
   (`toolFoldersFor`).
2. **Every runtime uses it.** On macOS each runtime's agent runs inside that profile, for whatever folder its
   execution was given, in these steps:
   - BOX-1: a home of its own for a confined server's commands, and a worktree's git data read only (Codex; ADR 0041,
     amendment).
   - BOX-2: every Codex execution's folder confined, and asking means asking (Codex; ADR 0041, amendment).
   - BOX-3: one OpenCode server per execution folder, writing that folder alone, and every other bound project
     inside a folder denied (OpenCode, writes; Codex, the inner projects).
   - BOX-3b: OpenCode's reads closed by the same profile, each folder's server with a home of its own.
   - BOX-4: Claude Code wrapped in the same profile, its network only through a gate of Halcyonic's to its API host.
3. **What the package may depend on.** `@halcyonic/runtime-core`, for the one list of secret-shaped files it shares
   with the control plane's project reads (`SECRET_FILE_RULES`), and Node's own `node:crypto`, `node:fs` and
   `node:path`. Nothing else. `tooling/architecture.test.ts` holds that allowlist; widening it is an architecture
   change. It holds no runtime's
   vocabulary: a runtime's own folders reach it in the scope (`home`, `temp`, `runtimeFolder`, `readable`), never by
   name.
4. **Who may depend on it.** Runtime adapters and the control plane. The core packages (`contracts`, `domain`,
   `runtime-core`) never do: a sandbox is an operating system's mechanism, not the domain's.
5. **A runtime offers `never_ask` only once its agent runs inside the profile**, and a run records `confined` only
   where it did. Nothing about a profile is declared ahead of a run that proves it.

## Alternatives considered

- **Keep the profile in the Codex adapter and import it from OpenCode's.** Integrations depending on each other is
  against SYSTEM.md's rules, and OpenCode would load Codex's adapter to start a server.
- **Put it in `runtime-core`.** The core holds what a runtime adapter must be, with no operating system, storage or
  vendor in it. A Seatbelt profile is macOS's, and its base policy is Codex's own.
- **A copy of the profile per adapter.** Three copies of a security boundary drift apart; an AUDIT fix (the git data
  back-link, the inner projects) would have to land three times.
- **Each runtime's own sandbox** (Codex's `workspace-write`, OpenCode's permissions, Claude Code's settings). They
  differ in what they bound and are not all closed for reads; OpenCode's raises an ask only for the commands its
  parse finds (ADR 0028). Halcyonic's profile is the one bound it can show is the same for every runtime.

## Consequences

- A fix to the profile lands once and holds for every runtime that uses it.
- `packages/seatbelt` has a rule of its own in `tooling/architecture.test.ts` (dependency `@halcyonic/runtime-core`;
  imports it, `node:crypto`, `node:fs`, `node:path`), added deliberately in the move.
- Each runtime still needs its own run inside the profile, pinned by its end to end suite: what its binary reads to
  start differs (OpenCode's instruction discovery, Codex's canonicalized home).
- Off macOS there is no profile. Codex keeps its `workspace-write`, OpenCode its permissions, and none records
  `confined`.
- Revisit when Halcyonic runs agents off macOS, or when a runtime ships a closed-by-default sandbox of its own that
  bounds reads the same way.

## Status of the steps, 2026-11-11

- BOX-1, BOX-2 and BOX-3 built and landed. The move to `@halcyonic/seatbelt` built.
- BOX-3b built: OpenCode's folder servers inside the profile, each with a home of its own and listening on its own port
  ([ADR 0028](0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md), amendment of 2026-11-11).
- BOX-4 built for a new app's project: the amendment below. Never asking for Claude Code stays refused until a real
  run, which needs the owner's own key.

## Amendment, 2026-11-11: Claude Code, confined only where a start asks for it (BOX-4)

The lead's choice of 2026-11-11, on VRIFY's question: Salidium's and Seorak's evidence of a Claude Code session comes
from the person's own Claude Code hooks, and inside the closed profile those do not load. That evidence is one of the
things that sets the product apart, so it is kept wherever the person works in their own repositories.

1. **Confined only on a start that asks for it:** a new app's project, made from a starter
   (`claudeConfinement` in `apps/control-plane/src/runtimes.ts`). There Claude Code runs inside the profile with a
   home, configuration and temporary folder of its own (`<data>/claude-sandbox-<key>`), the person's hooks off, and
   it would record `confined: true`, but see the key below. Everywhere else it runs as before, with the person's own
   settings and hooks, and records `confined: false`; what needs confinement stays refused in words.
   - **The sign-in key is inside (AUDIT BOX-4 F1):** `ANTHROPIC_API_KEY` reaches the confined process, and the agent's
     own commands can read it. An agent could write it into the project, and a preview opens on the headset with the
     network on. So while the key is inside, such a run is recorded `confined: false` with `unconfined_reason`, and
     its previews, shares and never asking stay refused, as for any unconfined run. It still runs inside the profile.
   - Keeping the key out, in a gate that holds it and adds it to each request, is Halcyonic handling a person's key in
     a new way: the owner's decision, proposed in VRIFY's report, not built.
2. **Its one way out** is a gate of the adapter's on loopback (`api-gate.ts`): a CONNECT tunnel to exactly the API
   host and port of `ANTHROPIC_BASE_URL`, else `api.anthropic.com:443`, or absolute-form requests to exactly a plain
   HTTP one. Everything else gets 403. The profile reaches the gate's port alone.
3. **Signing in** inside the profile is the API key the adapter already requires, passed in its environment, so it
   is inside with the agent (point 1). No saved login, keychain item or file of the person's is read
   ([claude-code-sandbox.md](../validation/claude-code-sandbox.md)). A start that cannot be confined, or is not
   counted confined, runs as before or inside the profile, recorded `confined: false` with `unconfined_reason` on
   `runtime.execution.started` for the headset to say; it is never refused for that, and never called confined.
4. **Cloud providers** (Bedrock, Claude Platform on AWS, Vertex, Foundry): their hosts are not known ahead, so the
   sandbox is not given to them. Such a start runs as before, recorded `confined: false` with the reason.
5. **Re-attach** ([ADR 0046](0046-a-restart-re-attaches-each-tasks-session-where-the-runtime-keeps-it.md)): a
   session goes on only with the confinement it was recorded with, as Codex's and OpenCode's do. A confined session
   is found in its folder's own store by a small reader of the adapter's (`findInConfinedStore`), since the SDK's
   reader sees only this process's configuration folder.

- **Evidence for confined sessions:** Salidium could read the folder's own store, as it reads the other tools' folders.
  That is a request for the Salidium project and the owner's call there; nothing is built for it here.
- **Tests:** unit tests of the gate, the launch, the store reader, the scope and the re-attach rule; an e2e test on
  the bundled CLI with a fake API (`confined.e2e.test.ts`).
- **Revisit** when Salidium reads confined sessions, or when a real run allows never asking.
