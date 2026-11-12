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
  `wire_api` "responses is the only supported value" (learn.chatgpt.com/docs/config-file/config-reference). Not
  built here yet.
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
- Codex on a hosted provider (Responses): next.
