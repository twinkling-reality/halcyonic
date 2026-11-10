# Sessions after a restart

- **Question:** When the processes running an agent's session end mid-turn, as they do when the
  control plane restarts (its start stops every runtime process the last one left), can a new
  process continue the session, with its history, by its native id? What becomes of the cut turn?
- **Date:** 2026-11-10
- **Versions:** Codex `codex-cli` 0.157.0; OpenCode `@opencode/cli` 2.0.18 (darwin arm64); Claude
  Code CLI 2.1.283 as bundled by `@anthropic-ai/claude-agent-sdk` 0.3.283 (darwin arm64); macOS 26.
- **Decision:** [ADR 0046](../decisions/0046-a-restart-re-attaches-each-tasks-session-where-the-runtime-keeps-it.md)

## Codex

Already verified for a server that dies while the control plane runs
([record](codex-capabilities.md)): after the server is killed mid-turn, a new server over the same
`CODEX_HOME` resumes the thread with `thread/resume`, `thread/turns/list` reads the killed turn
`interrupted`, and the thread takes a new turn with its history. A new server cannot resume a
thread another Codex process still holds ("active writer") until that process exits. A control
plane restart differs only in that the adapter keeps no record of the thread: it is rebuilt from
the journal. Not yet run: a resume from a second adapter instance.

## OpenCode

Method: a probe (kept outside the repository) launched the pinned binary through the adapter's own
`launchServer`, unsandboxed, with temporary HOME and XDG folders, the end to end tests' fake
OpenAI-compatible provider on loopback and every proxy variable pointed at a trap that refuses and
records. One session, one turn that finished, then a turn the provider streams slowly; 1.5 s into
it, SIGKILL to the server's process group; then a second server with the same environment.

- The second server read the session back (`GET /api/session/{id}` 200) and listed it in
  `GET /api/session`; `GET /api/session/active` listed nothing.
- The session's `outcome` read `succeeded` and `time.idle` the first turn's: the cut turn left no
  record of its end. A re-attach knows it ended only from its absence from the running sessions.
- A new prompt was accepted (200) and its turn finished; the model's request carried the first
  prompt and the cut one before the new one.

So a session outlives its server where its data folder is kept, as the sandbox's is
(`<data dir>/opencode-sandbox/data`). This corrects the earlier reading that a server that exits
loses its sessions for good: that is the adapter's behaviour, not OpenCode's.

## Claude Code

Method: a probe (kept outside the repository) drove the agent SDK's `query()` in streaming input mode
with `pathToClaudeCodeExecutable` at the bundled binary, temporary HOME, CLAUDE_CONFIG_DIR and
TMPDIR, a dummy API key, `ANTHROPIC_BASE_URL` at a fake Messages API on loopback (streaming text,
slow for one prompt), nonessential traffic, telemetry, error reporting and updates off, and every
proxy variable pointed at a trap that refuses and records. The session id was chosen with
`sessionId`. One turn that finished, then a turn the fake API streams slowly; once its request
arrived, SIGKILL to the CLI process (found by the session id on its command line); then a new
`query()` with `resume: <session id>` and a new prompt.

- The transcript `projects/<encoded cwd>/<session id>.jsonl` was there after the kill, ending with
  the cut turn's prompt.
- The resumed query's `system/init` carried the same session id; its turn ended `result/success`.
- The model's request held five messages, user, assistant, user, assistant, user: the first
  prompt and its answer, the cut prompt, a placeholder answer the CLI wrote for it ("No response
  requested."), then the new prompt.
- No request reached the trap, and the fake API saw no other request.

The SDK documents `resume` and that `persistSession: false` would turn it off; the adapter leaves
persistence on.

## Not verified

- A resume through each adapter from a second adapter instance (each adapter's end to end test
  when it declares `resumes_after_restart`).
- A resume on a real model.
