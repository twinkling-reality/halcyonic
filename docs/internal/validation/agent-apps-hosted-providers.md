# Agent apps on a hosted provider, through the key gate (VRIFY, 2026-11-12)

An agent app inside Halcyonic's closed sandbox reaches a hosted provider only through a key gate of its own
(`packages/seatbelt/src/key-gate.ts`): the gate holds the person's key, the app holds only its server's run
credential. Which providers an app reaches is read from the host's one list of declared providers (ADR 0061,
`HostProviders` over `providers/<id>/provider.json`, the assistant's list too): each provider the app's own key
reader reaches (it serves a wire shape the app needs) and whose key the person stored (`gatedProviders` in
`apps/control-plane/src/runtimes.ts`). OpenCode declares its reader in its own package (`OPENCODE_KEY_READER`: every
provider, `chat_completions`, read at start, never turning OpenCode on). A provider is therefore one declaration
file for the assistant and the agent apps alike, with no code of its own.

## What each app's documentation says (read 2026-11-12)

- OpenCode: a custom OpenAI-compatible provider in its configuration (opencode.ai/docs/providers): `providers.<id>`
  with `package: "@opencode/ai/providers/openai-compatible"`, `settings.baseURL` and `models`; the key from an
  environment variable named in `env`, or a setting. The pinned 2.0.18 takes exactly this shape: its own test
  configuration (`testing/sandbox.ts`) and the adapter's Ollama provider are written so.
- Codex: a custom provider in `config.toml` (`model_providers.<id>`: `base_url`, `env_key`, `http_headers`);
  `wire_api` "responses is the only supported value" (learn.chatgpt.com/docs/config-file/config-reference). The
  pinned 0.157.0 itself, with a scratch home and a dead loopback port: `wire_api = "chat"` fails to load ("is no
  longer supported"), `"responses"` loads (2026-11-12). Built 2026-11-13, below.
- Claude Code: no. Its gateway must speak Anthropic Messages, Bedrock or Vertex, and the documentation says
  Anthropic "doesn't support routing Claude Code to non-Claude models through any gateway"
  (code.claude.com/docs/en/llm-gateway).
- Meta's hosted models, by Meta's own documentation (dev.meta.ai): `muse-wonder-1.0` among them, OpenAI-compatible
  Chat Completions and Responses at `https://api.meta.ai/v1`, the key as `Authorization: Bearer`, tool calling and
  streaming stated. LOOP's declaration of it is the one the route reads.

## What is built

The OpenCode adapter takes `hostedProviders`, read at each server launch. Inside the sandbox, for each provider it
opens a key gate (`startProviderGate`) holding the person's key in the declared header, issues that server a run
credential, adds the provider to OpenCode's own configuration (`OPENCODE_CONFIG_CONTENT`, the server's environment
alone, never its commands') with the gate as `baseURL` and the run credential as `apiKey`, and adds the gate's port
to the server's profile. The gates close with the server. Unsandboxed, no provider is offered.

## Seen (the pinned OpenCode 2.0.18 binary inside the closed profile; a stand-in of Meta's shape; a fake key)

`opencode-runtime.e2e.test.ts`, "OpenCode reaches a hosted provider only through Halcyonic's key gate", passed, and
failed first on the adapter before this change (the model was never offered). In it a stand-in served
OpenAI-compatible Chat Completions under `/v1`, answering only `Authorization: Bearer <fake key>`:
- OpenCode listed `meta/muse-wonder-1.0`, served `remote` (the gate listens on loopback, so an address-only rule would
  have read it as this Mac's: `servedBy` names every provider the server reaches through a gate remote; `models.test.ts`
  failed first on that, and the e2e asserts it), and ran a turn on it to its end;
- every request that reached the stand-in carried the fake key; the server's profile names the gate's port, never
  the stand-in's;
- no file of the sandbox, OpenCode's own place among them, held the fake key; nothing left the Mac.
`runtimes.test.ts` shows, through the real `HostProviders` over declaration files in a temporary folder, that
OpenCode is offered exactly the declared providers serving Chat Completions with a stored key; that a third
provider added as one `provider.json` and its key, nothing else changed, is offered too; and that one serving
another shape only, a key scheme the gate does not write, or a removed key, is not.
On the repository's own declarations (`providers/meta`, `providers/anthropic`), a stored Meta key gives OpenCode
Meta's two models at `https://api.meta.ai/v1` with the key as `Authorization: Bearer`; an Anthropic key alone gives it
nothing, since Anthropic serves Messages only.

## Not verified

- Meta's API itself, with a real key: none is used. The owner enters theirs in the product.
- Tool calls on a hosted provider through OpenCode (the turn above is text); Meta states tool calling.
- Codex against a real non-OpenAI Responses host: what its requests need of one (stored responses,
  `previous_response_id`, encrypted reasoning, its websocket transport, which the gate refuses) is known only for
  the fake provider.

## Meta's address refuses a wrong key (2026-11-13, CORE)

A scratch control plane with the pinned OpenCode 2.0.18 and a FAKE Meta key, stored 0600 in its scratch data
folder. It read the agents' model list once. Through the host's key check (`KeyChecks`, the assistant's
Chat Completions `check`), that sent ONE metadata request carrying the fake key: `GET
https://api.meta.ai/v1/models/muse-wonder-1.0`, the first model of `providers/meta/provider.json`. Verified:
- the address answers;
- it refuses a wrong key: the check's answer was the refusal it maps from a 401 or 403 status (which of the two was
  not recorded);
- no inference was asked, so nothing was spent.

Not verified: what it answers a valid key, and what it answers a revoked one.


## Codex (VRIFY, 2026-11-13; AUDIT3 read the shape first, with additions A, B and C)

- `CODEX_KEY_READER` (every provider serving Responses, read at start, never turning Codex on) is in main.ts's one
  list; `gatedProviders` hands the Codex adapter the providers it reaches with a stored key.
- Only a confined server (a new app's project) gets them: per provider a key gate of its own (`startProviderGate`,
  Codex sends `env_key` as `Authorization: Bearer`), the provider as `halcyonic_<id>` in `-c` overrides (name, the
  gate as `base_url`, `env_key` naming `HALCYONIC_GATE_<ID>_TOKEN`, `wire_api = "responses"`), the credential in
  the server's environment alone, the gate's port in the profile, the gates closed with the server.
- A: `config/read` must show every `halcyonic_*` entry exactly as passed (at its gate, its variable, Responses, no
  `http_headers`, `env_http_headers`, `query_params` or `experimental_bearer_token`), no other provider naming a gate
  variable, and none missing; else the server stops (`gatedEntriesProblem`). A `halcyonic_*` entry on the open
  server is refused the same way.
- B: each thread sets every gate variable empty for its commands (`shell_environment_policy.set.<var> = ""`). Codex
  0.157.0 applied neither a launch's nor a thread's `shell_environment_policy.exclude = ["HALCYONIC_GATE_*"]` to a
  command's environment, nor dropped the `_TOKEN` name by default: seen in the end to end test before this fix, the
  command printed the run credential.
- C: a hosted model chosen in a project that is not confined: "Hosted models run in a new app's project only. Make a
  new app here, then choose this model there." Nothing reaches a provider.
- The listing adds each reachable provider's declared models (`halcyonic_<id>/<model>`), served `remote`, the
  provider named; `servedBy` names a gated provider remote before its address (the gate is loopback), failing first.

Seen on the pinned Codex 0.157.0 inside the closed profile, a Meta-shaped stand-in in front of the fake provider
(Responses under `/v1`, answering only `Bearer <fake key>`): `codex-runtime.e2e.test.ts` "a confined thread on a
hosted provider reaches it only through its key gate", failing first on main (the model never listed): listed
`halcyonic_meta/muse-wonder-1.0` remote with provider `meta`; a thread's command printed `gate=none openai=none`;
every request reached the stand-in with the fake key and no run credential; the turn ran on the hosted model; no file
of the sandbox held the key. And "a hosted model chosen in a project that is not confined is refused in words".
Seen 2026-11-13 (the same end to end test now asserts it, counts only): a command on the confined thread ran
`ps -axeww -o command` and saw no process at all (0 lines), so it could not read the server's environment, and found
no run credential (0 matches). Were it readable, the credential is still worthless off the gate and revoked when the
server exits; the key itself is never in the sandbox.
