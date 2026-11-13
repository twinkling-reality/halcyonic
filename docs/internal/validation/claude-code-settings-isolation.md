# Claude Code loads none of the person's settings wholesale: the stored key alone pays (VRIFY, 2026-11-12)

Until this change an unconfined Claude Agent task (an existing project's) and the model listing loaded the person's
own Claude Code settings (`settingSources: ['user']`), so Salidium's and Seorak's hooks observed the session. That
also loaded the settings' `env`, which Claude Code writes into its own environment, and a credential there outranks
the stored key: a task could be paid for by a credential the person never chose for Halcyonic.

## What the official documentation says (read 2026-11-12)

- Credential order when several are present (code.claude.com/docs/en/authentication): a cloud provider switch,
  then `ANTHROPIC_AUTH_TOKEN` (sent as `Authorization: Bearer`), then `ANTHROPIC_API_KEY` (`X-Api-Key`), then
  `apiKeyHelper`, then `CLAUDE_CODE_OAUTH_TOKEN`, then the `/login` subscription. In non-interactive mode the key "is
  always used when present" above the subscription.
- A settings file's `env` is copied into Claude Code's environment as written (code.claude.com/docs/en/env-vars). The
  documentation does not say whether it may set a credential or a provider switch; the run below shows it may.
- The Agent SDK (0.3.283, `sdk.d.ts`): `settingSources: []` loads no settings file ("SDK isolation mode"); the managed
  (policy) tier is still read; `settings` passes settings in the flag tier (the `--settings` flag);
  `resolveSettings` (alpha) resolves the settings a query would see without starting Claude Code, but takes no
  environment, so it reads the plane process's own home.
- Not stated: whether Claude Code falls back to another credential after a key is refused (401).

## What the bundled Claude Code 2.1.283 did (fake values only, a scratch HOME and config folder)

- In a loopback-only sandbox, with a fake key and a fake API answering 401: with the settings `env` setting
  `ANTHROPIC_AUTH_TOKEN`, every `/v1/messages` request carried the key in `x-api-key` AND the settings token as
  `Authorization: Bearer` (8 of 8); without it, the key alone (8 of 8). (`.private` evidence.)

## What else of the person's set-up loads with no settings file (2026-11-13, AUDIT3's question)

`settingSources: []` keeps every settings file out; the rest of a person's Claude Code set-up is not a settings
file. Seen on the bundled 2.1.283 through the SDK's own query, fake values, a scratch HOME and configuration folder,
each planted with a marker, both layouts (`CLAUDE_CONFIG_DIR`, and `~/.claude` with `~/.claude.json`):
- With `settingSources: ['user']` (the control): a user-scope MCP server in the global configuration
  (`.claude.json`) was started (its marker file made); the person's agent, command and skill were listed at start and
  offered in the request; a plugin installed from a local marketplace with Claude Code's own `plugin install` (user
  scope) was loaded with its skill.
- With `settingSources: []` (every Claude Agent run since 2026-11-12): none of them. No MCP server started, no agent,
  command or skill listed or offered, no plugin but Claude Code's built-in `agents-md`.
So nothing of the person's own set-up loads but what the adapter passes (`personalSetupUsed()`'s `restUsed: false`
stands). `project-settings.e2e.test.ts` now plants the MCP server, agent, command and skill and asserts none
loads; the plugin case is the recorded probe only (`.private` evidence), as installing a plugin inside a test needs
Claude Code's own install step.

## What is built

- Every Claude Agent start, resume and model listing passes `settingSources: []`, inside the sandbox and out.
- Outside the sandbox the person's set-up is passed explicitly (`personal-settings.ts`), read from where Claude Code
  reads it (`CLAUDE_CONFIG_DIR`, else `~/.claude`) in the run's own environment:
  - Salidium's and Seorak's hooks only, each recognised as its project's installer writes the WHOLE command, anchored
    at both ends, so nothing can come before or after it (AUDIT3 F1): Salidium,
    `SALIDIUM_HOOK=1 '<absolute>/hooks/relay.sh' claude-code <that event> <one of its four pressures>`; Seorak,
    `'<absolute node>' '<absolute script>' --seorak-hook-path=<base64url file URL>`, the script that event's own and
    the marker naming the very same file. Each entry's type, command, async and timeout are copied and passed in the
    flag tier as `settings: { hooks }`. Salidium's older form (a `/.salidium/hooks/` path through `/bin/sh`) is not
    recognised, so not passed. A listing is no task and carries none.
  - The person's own `CLAUDE.md`, appended to Claude Code's preset system prompt, at most 64 KB (larger refuses the run
    in words); its `@` imports are not followed.
  - Nothing else: no `env`, `apiKeyHelper`, permissions, model, other hooks, plugins or status line.
  - A settings file that is there but is not JSON refuses the run in words.
  - What a task uses is declared once, from what passes: `personalSetupUsed()` (exported by the package) names the
    instructions file read and the integrations whose hooks pass (`OBSERVERS`, the same list the hook filter uses),
    and says nothing else is used, so a page saying it reads the adapter, never a list of its own.
- Before every unconfined start, resume and listing the settings Claude Code still applies with no file loaded (the
  managed tier, through the SDK's `resolveSettings`) are checked (AUDIT3 F2). An `env` naming a credential or a
  provider (`ANTHROPIC_*`, `CLAUDE_CODE_USE_*`, `CLAUDE_CODE_OAUTH_TOKEN`, `CLAUDE_CODE_CLIENT_*` and any other
  `CLAUDE_CODE_*` naming a certificate, key, token, host, URL, proxy or auth, `AWS_*`,
  `GOOGLE_APPLICATION_CREDENTIALS`, `CLOUD_ML_REGION`), a proxy (`HTTP(S)_PROXY`, `ALL_PROXY`, either case), what lets
  a proxy read the requests (`NODE_EXTRA_CA_CERTS`, `SSL_CERT_FILE`, `SSL_CERT_DIR`, `NODE_TLS_REJECT_UNAUTHORIZED`)
  or code loaded beside the key (`NODE_OPTIONS`); or a command of the tier's own (`apiKeyHelper`, `awsAuthRefresh`,
  `awsCredentialExport`, `gcpAuthRefresh`, `otelHeadersHelper`, `hooks`, `statusLine`): each refuses the run in words
  naming the setting, never its value. A resolver that fails refuses too.

## Seen

`project-settings.e2e.test.ts` on the bundled Claude Code, failing first on the code before this change (the
person's other hook ran): the person's settings hold a fake `ANTHROPIC_AUTH_TOKEN` in `env`, a hook of their own,
and a Salidium-shaped and a Seorak-shaped hook. An unconfined run: Salidium's and Seorak's hooks ran, the other did
not, no request carried `Authorization`, the person's `CLAUDE.md` text was sent, and none of the project's own files
loaded. The other Claude end to end tests (confined, plan, resume, credential, pictures) pass on it.
`personal-settings.test.ts` covers the two rules and their near misses, the four copied fields, the refusal list,
and the file that is not JSON.

## Not verified

- Until 2026-11-13 an unconfined run held the person's key itself in its environment; since then it goes through the
  key gate too ([claude-code-connected-folders.md](claude-code-connected-folders.md)).

- Whether Claude Code with the person's real config folder ever sends their `/login` subscription token beside a
  key. Not tested: it would send their token to a test's process.
- The managed-tier check against a real managed settings file or device profile: the tests hand the adapter their
  own resolver, so the SDK's reading of this Mac's policy tier is not exercised.
- A listing without `Authorization` is shown by its options (`settingSources: []`), not by an end to end run.
