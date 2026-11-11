# Project secrets: what was verified, and how (2026-11-11)

For [ADR 0057](../decisions/0057-a-projects-secrets-are-kept-on-the-mac-and-given-to-a-tasks-commands-at-its-start.md).
Test values only (`test-value-not-real-…`); no real key was set, given or printed by any test or probe.

## Each runtime's way in, and what shows it

| Runtime | How a secret reaches its commands | What shows it | Kind |
| --- | --- | --- | --- |
| Claude Code (SDK 0.3.283) | The session's process environment (`env`), under the adapter's own variables | `sdk-process.test.ts`: through the real SDK, the fake executable's process holds the name and keeps the adapter's `HOME`. That tool commands inherit the CLI's environment is Claude Code's own behaviour, read in its source, not run | runtime (process), static (commands) |
| OpenCode 2.0.18 | The session's own command environment, `PUT /api/session/:id/environment`, under the folder server's command variables | `opencode-runtime.e2e.test.ts`, 2026-11-11: through the adapter on the pinned binary and the fake provider, a shell command printed the first 16 hex digits of the secret's SHA-256, the next task's command on the same folder printed those of an empty value, and a secret named `HOME` did not replace the commands' own. The route was first verified on 2026-10-07 ([opencode-permissions.md](opencode-permissions.md)) | runtime |
| Codex 0.157.0 | `shell_environment_policy.set.<NAME>` in `thread/start` and `thread/resume` `config` | `codex-runtime.e2e.test.ts`, 2026-11-11: through the adapter on the pinned binary and the fake provider, a command printed the digest of `PROJECT_TEST_KEY`, so a name holding `KEY` survives Codex's default excludes when it is `set`, and another thread on the same server printed that of an empty value. Before it, CORE's probe on the same binary: per-thread, the dotted form, and a value never printed found nowhere in Codex's home. `codex-runtime.test.ts`: the adapter puts it in the thread's `config` alone | runtime |

The two end to end tests ran on CORE's fresh pinned copies in the lead's folder, each binary's SHA-256
checked against `pins.ts` first, never a copy from the owner's data folder. Each fails when its adapter's
change is taken out ("the command did not read the secret"), checked by changing the code, then restored.
Commands print a digest of the value, never the value.

After both end to end runs, a search of the runtime's own sandbox folder (its store, its home, its logs)
finds no file holding the value, while the same search finds the variable's name in the command each kept,
the control (AUDIT3 S2).

Not proven:
- Codex inside Halcyonic's confined server, and a resumed thread after a restart (the adapter passes the
  secrets again on every resume; not run).
- The real Ollama provider path for any of the three.

## The Mac's side (`secrets/secrets.test.ts`, `secrets/store` through it, `network/setup-page.test.ts`, `packages/domain/src/secrets.test.ts`)

1. **A set on the page** writes the value to `<data dir>/secrets/<project>/<name>`, mode 600, the folder
   700, and nothing else there. The journal holds `project.secret.stored` with the name and `replaced`,
   and the value appears nowhere in the whole journal, also after it is replaced.
2. **A start on a runtime that declares it** gets exactly the project's secrets, and `execution.created`
   names them; a runtime that does not declare it gets none.
3. **Redaction**: a runtime's error text repeating the value is journaled as `[redacted: PROJECT_TEST_KEY]`.
   A staged value is among the held secrets before its command is submitted.
4. **A removal** deletes the file and the name; a later start gets nothing; removing it again is refused
   in words.
5. **A set with no value staged for it**, as a local client over the realtime protocol would send it, fails
   "A secret is typed on this Mac's setup page." and keeps nothing. A paired device's set is refused
   before that, as any command of an unknown device is.
6. **Refusals in words, nothing left staged**: a reserved name (`PATH`, `OPENCODE_PASSWORD`), a name not in
   capitals, a value under 8 characters, a project not on this Mac, a value over 8 KiB.
7. **A restart** keeps only the values the journal lists, and deletes what a press left staged.
10. **A task still running with a value** (AUDIT3's probe, kept as a test): a message repeating it is
    redacted while it is set, after the secret is set to another value, and after Remove. Without the retired
    values the test fails (checked by changing the code, then restored).
11. **The page** names, from the runtimes' own names, the agents that start without the secrets (`without`)
    and those that run outside the sandbox on a connected folder and can read every project's (`outside`, a
    row with the alert mark). A refusal is said in that project's card (`problem`), not in What Setup Said.
    Remove asks first (`confirm`); a second press of another secret asks again; Cancel forgets it; only a
    second press of the same secret removes it. The test fails when Remove acts on the first press (checked
    by changing the code, then restored).
12. **Reserved too:** `HTTPS_PROXY`, `ALL_PROXY`, `SSL_CERT_FILE` and `BUN_*` (the domain test).
8. **The page**: a set or removal without the page's session, or from another site, is refused 403; a
   3,000-character value fits the route's own body limit while the page keeps 1 KiB elsewhere; no answer
   of the page, the set's or the state's, holds the value.
9. **The domain**: names sorted and once each, removal, a checkpoint keeps them, the cap of 32 (a
   replacement still allowed at the cap), and ordinary key names (`OPENAI_API_KEY`, `GITHUB_TOKEN`) free.
