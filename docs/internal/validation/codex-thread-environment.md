# A Codex thread's own shell environment

- **Question:** can one Codex app-server give one thread's shell commands an environment variable that no other
  thread sees, without a restart, and does Codex keep the value on disk?
- **Date:** 2026-11-11.
- **Versions:** `codex-cli` 0.157.0, the native binary from the public npm registry, installed with
  `--ignore-scripts`. Its SHA-256, `ad0be20d04e2ba6146ecdb51d7f8b7b0fe15420a15dc9b0057518d858f1f3714`, equals the
  repository's pin (`apps/control-plane/src/pins.ts`).
- **Method:**
  - One bare app-server in the Codex end to end suite's sandbox (`packages/integrations/codex/src/testing/sandbox.ts`),
    with its fake Responses provider recording every request. The approval policy was `never`.
  - Five threads on that one server ran a shell command the fake provider asked for:
    `echo marker=[$HALCYONIC_PROBE_MARKER]`. The variable is a harmless marker.
  - The probe is not in the e2e suite.
- **Status:** verified on the fake provider, on the open server only.

## Results
- **`thread/start` sets it, for that thread.** With `config: {shell_environment_policy: {set: {NAME: value}}}`, the
  thread's command printed its own value. The dotted key `shell_environment_policy.set.NAME` works too.

| Thread | Started with | The command printed |
|---|---|---|
| A | the nested object, `thread-a` | `marker=[thread-a]` |
| B | nothing | `marker=[]` |
| C | the dotted key, `thread-c` | `marker=[thread-c]` |
| D | nothing, after A and C | `marker=[]` |

- **No restart is needed.** All the threads ran on the same server process.
- **Codex does not keep the setting on disk.** Thread E set `unprinted-e7f3` and ran a command that does not print
  it. Afterwards the string was in no file of Codex's home: its rollouts, thread history and logs.
- **What a command prints is kept, as all command output is:** in that thread's rollout and history database.
- **Nothing else changed:** no approval was asked, and nothing left loopback.

## What it means
- **The agent sees the value.** A value set this way is in the thread's commands, so the agent can read it and
  repeat it. It is not a way to keep something from the agent.
- **Only a thread start or resume can set it.** `turn/start` takes no `config`; `thread/start` and `thread/resume`
  do. A thread re-attached after a restart (ADR 0046) would need it passed again.

## Not verified
- Whether a resumed thread keeps the value without it being passed again.
- The confined server, inside Halcyonic's Seatbelt profile.
- The Ollama provider in place of the fake one.
