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
   it records `confined: true`: its gate holds the key (below). Everywhere else it runs as before, with the person's own
   settings and hooks, and records `confined: false`; what needs confinement stays refused in words.
   - **The gate holds the key, the agent holds none (VRIFY, 2026-11-12; AUDIT BOX-4 F1 closed):** until then
     `ANTHROPIC_API_KEY` reached the confined process, where the agent's own commands could read it, so such a run was
     recorded not confined. Now the agent's environment holds only its run's credential from the gate (random, per
     run, revoked when the run's process ends; the gate takes nothing else, nor a request whose Host is not its own,
     AUDIT3 2026-11-12) and `ANTHROPIC_BASE_URL` points at the gate; the gate drops every credential the agent sends and adds the person's key
     in the header the adapter declares (Claude Code's declaration, `CLAUDE_CODE_KEY_GATE`: `x-api-key`, how the
     Anthropic API takes a key and how Claude Code sends `ANTHROPIC_API_KEY`), so a run inside is recorded confined and what it makes can be played, published and shared
     like any other. The owner's word, 2026-11-12 (lead's answer of 14:33Z); the record is
     [claude-code-key-gate.md](../validation/claude-code-key-gate.md).
   - **On a connected folder too (VRIFY, 2026-11-13; the lead's 18:21Z step 3):** a Claude Code process outside the
     closed profile reaches its API through the same gate with a run credential of its own, never the key, and runs
     under a light profile that denies reading and writing the host's own folders (its data folder, the task copies'
     folder, every `.halcyonic*` home) and takes nothing else away. It is still recorded `confined: false`.
     [claude-code-connected-folders.md](../validation/claude-code-connected-folders.md).
2. **Its one way out** is a gate on loopback (since 2026-11-12 `packages/seatbelt/src/key-gate.ts`, one mechanism for
   every confined agent app that takes a key, each adapter declaring how its app takes one: `KeyGateDeclaration`;
   the seatbelt package may import `node:http`, `node:https` and `node:net` for it, an architecture rule changed on
   purpose), a gateway, not a tunnel: the agent's API base address is the gate, which sends each request to its own address on to
   `ANTHROPIC_BASE_URL`, else `https://api.anthropic.com`, with the person's key added and the answer streamed back.
   A proxied request for any other host, and every CONNECT, gets 403. The profile reaches the gate's port alone.
3. **Signing in** inside the profile is the API key the adapter already requires, held by the gate alone (point
   1). No saved login, keychain item or file of the person's is read
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

## Amendment, 2026-11-13: what runs later is never written (AUDIT3, the lead's 12:52Z)

The closed profile let a confined run write all of its folder, the folder's own `.git` included. Some files there run
later outside any sandbox without the person choosing to run anything: the project's git configuration and hooks (in
the person's git, their editor's and Halcyonic's own send) and the files an editor or a shell runs when the folder is
opened (`.envrc`, `.vscode/`, `.idea/`, `.devcontainer/`, `*.code-workspace`, `.gitmodules`). The profile now refuses
every write to them in the confined folder, a task's copy as well as the project's (`RUNS_LATER` and `runsLaterUnder`
in `packages/seatbelt/src/profile.ts`, one list; AUDIT3 owns it, by "runs without the person choosing to run the
app"). The app's own code and package scripts are not on it: they run when the person runs the app, or in the host's
confined preview. A git directory a `.git` file names outside the folder was read only already.

- **The agent is told:** both starters' `AGENTS.md` say these are kept by Halcyonic, and a confined Claude Code
  session refuses a Write, Edit or NotebookEdit aimed at one at once, in words the model reads (`KEPT_BY_HALCYONIC`),
  instead of asking; a shell's write gets the kernel's refusal.
- **Tests:** `profile.test.ts` writes each under `sandbox-exec` with the profile (refused; failing first without the
  rule) and the rest of the folder as before; `confined.e2e.test.ts`'s confined shell writes `.git/config` and
  `.envrc` and both are refused; `keptPath` is unit tested.
- **At every depth, the whole list (AUDIT3's 17:32Z alert, the lead's 17:33Z):** the first rule covered the folder's
  top only, so a confined run could write `lib/.git/config` or a nested hook, which the person's git and their
  editor find. The list is now AUDIT3's whole one (version control `.git`, `.hg`, `.svn`, `.jj`, `.gitmodules`; what a
  shell runs on entering a folder, `.envrc`, `.autoenv*`, mise's files; editors' `.vscode/`, `.idea/`, `.devcontainer/`
  and `.devcontainer.json`, `.zed/`, `.fleet/`, `.nova/`, `*.code-workspace`; the person's other agent apps' `.claude/`,
  `.mcp.json`, `.cursor/`, `.windsurf/`, `opencode.json(c)`, `.opencode/`, `.codex/`; CI that runs with the repository's
  secrets once sent, `.github/workflows/`, `.github/actions/` and the others), refused at ANY depth and in any letter
  case, in the profile and in `runsLater` alike, one list. A git repository of the run's own lives outside the project
  (its own home). Package managers' configuration files (`.npmrc`, `.yarnrc.yml`, `.pnpmfile.cjs`, `bunfig.toml`) are
  not on it: the lead's call, open. `profile.test.ts` makes or writes every entry, at the top and two folders down, in
  its own case and in upper case, under `sandbox-exec` (each refused; the test failed first on the top-only rule).
- **What it does not close (AUDIT3, 2026-11-13T17:53Z):** a bare repository needs no `.git` name. A confined run can
  make a folder anywhere in the project holding `HEAD`, `config`, `objects` and `refs` (AUDIT3's probe made one), and
  git treats such a folder as a repository when a person runs git inside it (`safe.bareRepository` is `all` by
  default) and reads its `config`. It is narrow: editors' repository scans look for `.git` and do not find it; and of
  the git commands a person runs inside that folder, `status`, `log`, `rev-parse` and `branch` ran none of the
  programs its settings named, while a network command (`fetch`, and so `pull` and `push`) ran its `core.sshCommand`
  (AUDIT3's probe, git 2.43.0 on this Mac, 2026-11-13). Closing it would mean refusing files named `HEAD` or `config`
  at any depth, which breaks ordinary app files of those names and still leaves other layouts; decided not to
  (AUDIT3, 2026-11-13T17:59Z), to be reopened if any product path ever runs git inside a sub-folder of a project.
