# Claude Code inside Halcyonic's closed sandbox

- **Question:** can the Claude Code CLI the agent SDK bundles run whole turns inside the closed profile of
  `packages/seatbelt`, reaching only its API host through a gate of Halcyonic's? What does it need there to start, to
  sign in, and to be re-attached after a restart
  ([ADR 0048](../decisions/0048-every-agent-runs-inside-one-closed-sandbox-of-halcyonics.md), amendment of
  2026-11-11)?
- **Date:** 2026-11-11.
- **Versions:** macOS 26.7 on Apple silicon, `/usr/bin/sandbox-exec`; `@anthropic-ai/claude-agent-sdk` 0.3.283 and
  the Claude Code 2.1.283 it bundles (darwin arm64); Node 24.15.0 from Homebrew.
- **Method:** probes by hand, then the adapter's end to end test (`confined.e2e.test.ts`, opt-in with
  `CLAUDE_CODE_BIN`). A fake Messages API on loopback; a dummy key; every proxy variable the test sets points at a
  trap that refuses and records. No model and no hosted call: the real API host was never reached.
- **Status:** runtime verified on the fake host, as below. Not run against the real API host.

## What it needs inside the profile

- **Its own temporary folder, named to it:** Claude Code makes a per-user folder under `/tmp` unless
  `CLAUDE_CODE_TMPDIR` points elsewhere. Inside the profile `/tmp` is closed, and without the variable it fails at
  once ("EPERM ... open '/tmp/claude-501'").
- **A home and configuration folder of its own:** `HOME` and `CLAUDE_CONFIG_DIR` inside the folder's own place, so
  nothing of the person's home is opened. The person's own settings and hooks therefore do not load there.
- **One way out:** its traffic goes through `HTTP_PROXY` and `HTTPS_PROXY`, even to a 127.0.0.1 base URL, once
  `NO_PROXY` is unset. Plain HTTP is sent as absolute-form requests; HTTPS as a CONNECT tunnel, with TLS end to end
  (checked against a certificate of the probe's own through `NODE_EXTRA_CA_CERTS`). A named host needs no DNS lookup
  inside: the gate resolves it.
- **Nothing else** was refused in the runs: streaming input through the SDK, a Bash call, and a resumed session all
  ran.

## Signing in

- The adapter signs Claude Code in only with `ANTHROPIC_API_KEY` from its environment, or a cloud provider's
  switches (`environment.ts`); a claude.ai login is refused, as before. That variable reaches the confined process
  unchanged, and a turn signs in with it on the fake host.
- Claude Code reads a saved login with `security find-generic-password`. Inside the profile, with a home of its own,
  the same command for a name of the probe's own that cannot exist answers "One or more parameters passed to a
  function were not valid" (exit 44): no keychain is in its search list, so no saved login is found there. This shows
  the search list is empty, not that the keychain service is blocked. No real keychain item, credential or
  `~/.claude` was read, copied or touched.
- So a confined start cannot sign in only where the person has no API key and no cloud provider, which the adapter
  already refuses at its construction, for every start.

## The sign-in key inside (AUDIT BOX-4 F1)

The key the adapter signs Claude Code in with is in the confined process's environment, and its Bash tool inherits
it: AUDIT's line `echo "key=${ANTHROPIC_API_KEY:-none}"` inside the e2e's command prints the test's dummy key, and the
test now asserts that it does. So a run inside the sandbox is recorded `confined: false`, with `unconfined_reason`
saying the key is inside with it, until the key is kept out of the process (a gate of Halcyonic's that holds it and
adds it to each request: the owner's decision, not built).

## A command inside

From the e2e test, an approved Bash call: a file outside the folder and one in the person's home are refused
("Operation not permitted"), `ls /Users` is refused, its `HOME` is its own, a direct `curl` to 1.1.1.1 gets no
connection, and a write inside the folder works. Every request went through the gate to the fake API; none reached
the trap.

## Re-attach

The SDK's `getSessionInfo` reads this process's own `CLAUDE_CONFIG_DIR`, so it cannot see a confined folder's own
store without changing this process's environment. That store holds one folder's sessions only, so the adapter's reader
looks for `projects/<any>/<session id>.jsonl` there and takes the first entry's `cwd`. In the e2e test a second
adapter re-attached the session from it and the next instruction resumed it with the first turn in its history.

## Not verified

- The real API host through the gate, with real TLS trust inside the profile (the system's roots, through
  `trustd`).
- A real model's turns: builds, tests and other tools a task trips on.
- Claude Code's own background traffic beyond the Messages API when `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC` is
  not set: the gate refuses any other host, which may show as errors.
