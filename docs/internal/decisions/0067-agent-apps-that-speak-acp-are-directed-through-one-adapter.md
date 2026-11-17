# ADR 0067: Agent apps that speak the Agent Client Protocol are directed through one adapter

- Status: Accepted (the AGENTS lane, 2026-11-17, on the owner's ask that Halora direct Kiro and Antigravity)
- Date: 2026-11-17

## Context

- The owner asked for Kiro (AWS) and Google's Antigravity beside Claude Code, Codex and OpenCode: Halora's point is
  directing the agents people use, natively, not mirroring a laptop's screen.
- Verified on this Mac with the pinned binaries ([acp-agents.md](../validation/acp-agents.md)): both serve the Agent
  Client Protocol version 1 over stdio (`agy acp`, `kiro-cli acp --agent-engine v3`). Over it a call that needs the
  person is a `session/request_permission` request the client answers, a turn ends when `session/prompt` is answered
  with its `stopReason`, and `session/cancel` stops it.
- Both apps' headless modes refuse every ask unasked (Kiro denies it, Antigravity soft-denies it and exits 0), so
  they cannot ask the person, and scraping either app's terminal is forbidden (AGENTS.md).
- Antigravity runs whole turns against a stand-in Gemini API on loopback (`GOOGLE_GEMINI_BASE_URL`, a dummy key),
  so its adapter can be tested end to end with no sign-in and no spend. Kiro has no such switch: past `initialize`
  it needs the person's sign-in.

## Decision

1. One adapter, `packages/integrations/acp` (`AcpRuntimeAdapter`), directs every agent app that speaks ACP. What
   differs between apps is a profile (`profiles.ts`): its runtime id, name, pinned version, launch arguments, the
   variables it owns, and what a runtime test of that version showed its requests leave unsaid (a replacement made
   at every occurrence, a shell tool's command field).
2. A profile declares nothing the protocol does not carry. Capabilities are the adapter's, the same for every app,
   and each was proven by a runtime test (start, instruct at rest, approve or deny, stop); anything else waits for a
   run that shows it.
3. One app process per execution, in the project's folder, of the pinned version only. Approvals are answered with
   the app's once-only options, never a lasting rule, and count as taken only when the app shows it acted.
4. The app signs in its own way on the Mac; Halora never holds or passes its key. It is launched with a settings
   home of Halora's own (Antigravity's `--gemini-dir`, Kiro's `KIRO_HOME`), so the person's own settings for it (an
   allow-everything mode, hooks, tool servers) never apply to a task. On macOS it runs under the agent-app profile:
   the light profile, with the person's push and cloud credentials and the project's own settings for the app
   refused, read and write (AUDIT3 AC1 and AC2, 2026-11-17). Reads outside the folder ask the person, as a run
   shows; the credentials are refused even to an approved one. Recorded not confined.
5. Each app is registered by its own environment variable only (`HALCYONIC_ANTIGRAVITY_BIN`, `HALCYONIC_KIRO_BIN`),
   never by a settings file, since it spends the person's own plan.

## Alternatives considered

- **One adapter per app on its headless surface.** Neither headless mode can ask the person, so neither could carry
  an approval: every gated action would be refused unseen.
- **Hooks that call back to Halora** (Kiro's `PermissionRequest` hook, Antigravity's `PreToolUse` hook). They need
  hook files in the person's or the project's configuration, which the rules for a new adapter forbid, and neither
  app documents what its hook does in headless mode.
- **Antigravity's Python SDK.** It drives a separate bundled runtime over a local WebSocket, not `agy`, and would
  bring a Python process into the control plane for one app.
- **The closed profile with a key gate, as Claude Code's.** Each app reaches its own maker's service with the
  person's sign-in, not a model API whose address and key a gate can stand in for; there is nothing to gate.

## Consequences

- A further app that speaks ACP is a profile and a runtime test, not an adapter.
- A run is never confined, so it never counts toward `reads_confined` and gets no task copy (`holdsFolder` absent),
  and `never_ask` is not offered.
- Kiro's tool calls are unseen until the owner signs in once; until then its requests are shown as their whole
  input, and its edits can only be denied.
- Revisit when an app offers a sandbox of its own Halora can verify, a key-based model endpoint a gate could serve,
  or ACP gains steering or a question surface.
