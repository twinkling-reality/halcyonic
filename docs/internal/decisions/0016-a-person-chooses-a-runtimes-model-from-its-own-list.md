# ADR 0016: A person chooses a runtime's model from the runtime's own list

- Status: Accepted on 2026-09-30, a decision the owner delegated.
- Date: 2026-09-29

## Context

A runtime reaches several models: OpenCode and Codex reach open models that Ollama serves on the
Mac as well as hosted ones, and Claude Code reaches several Claude models. Until now a client could
choose only through a runtime-specific start option (`model`), spelled in the runtime's own syntax
and checked by nothing before the runtime ran. What that allowed was verified on 2026-09-29
([local-models.md](../validation/local-models.md)):

- OpenCode 2.0.18 accepts a session for a model it does not offer and fails its first turn with
  `provider.no-route`. Its list of models (`GET /api/model`, per directory) names each model's
  provider, whether it calls tools and its context, and also carries each provider's settings,
  API key included. `GET /api/provider` carries settings and headers.
- Codex 0.157.0 accepts any model name. `model/list` returns the catalog built into the binary,
  OpenAI's models, whichever provider is configured; `config/read` names the configured provider
  and model.
- The Agent SDK 0.3.283 lists Claude Code's models with `Query.supportedModels()`
  (`value`, `displayName`, `description`), which needs a running Claude Code process.
- A local model's name says nothing about where it runs: this Mac's Ollama serves the weights of
  `llama3.2:1b` under the names `gpt-4o:latest` and `gpt-3.5-turbo:latest`, and with no
  configuration OpenCode's default model is a hosted one of its own.

The headset's "new work" panel will need to offer the models a runtime can use, say truthfully
where each runs and whether it calls tools, and start work on the one chosen.

## Decision

- **The descriptor says whether a runtime lists models.** `RuntimeDescriptor.model_choice` is
  `'none'` or `'listed'`. An adapter whose descriptor says `'listed'` implements `listModels`;
  registration refuses one that does not.
- **The list is read through, on demand.** `GET /api/runtimes/:runtime_id/models` answers
  `{runtime_id, result}`, where `result` is `{availability: 'available', models}` or
  `{availability: 'unavailable', reason}`, and each model is
  `{model_ref, display_name, served, tool_calling, context_tokens}`. The adapter reads it from the
  runtime at each request; nothing is journaled or cached, as for external conclusions
  ([ADR 0010](0010-external-intelligence-is-read-through.md)). A runtime that does not list models,
  or does not exist, answers 404.
  - `model_ref` is opaque: the adapter chooses it, a client sends it back unchanged and never parses
    it.
  - `display_name` names the model and what serves it, so a local model named after a hosted one
    never reads as the hosted one.
  - `served` is `'this_mac'`, `'remote'` or `'unknown'`, decided from where the runtime sends the
    model's requests, never from the model's name. `tool_calling` is `'declared'`,
    `'not_declared'` or `'unknown'`, as the runtime's list states it. `context_tokens` is the
    runtime's figure, or null.
  - Only fields the adapter reads one by one reach the list: never a provider's settings, keys or
    headers.
- **A start may carry the choice.** `execution.start` has a `model_ref`, null when no model is
  chosen. Admission refuses one for a runtime whose `model_choice` is `'none'`. The adapter checks
  it with the start options, refuses it together with a runtime-specific `model` option, and checks
  it again against a fresh list before anything runs, refusing it in words (`model_unavailable`)
  when it is no longer listed.
- **The model a runtime reports is recorded.** A new runtime event, `runtime.model.used`, carries
  the `model_ref` of the model the runtime says it uses, with `observed` provenance, and the
  execution's view holds the latest. It comes from the runtime's own report, never from the choice.
- **Stored commands are migrated, without a new version.** A journal migration gives every
  `execution.start` command already stored `model_ref: null`, which is what they meant. No client
  in the field sends `execution.start` (the XR client does not yet), so the command schema version
  stays 1.
- **Per runtime:**
  - OpenCode lists `GET /api/model`, never `/api/provider`; `model_ref` is `provider/model`.
  - Codex lists the configured model under the configured provider, and the catalog's models only
    when they belong to that provider (its built-in catalog is OpenAI's); `model_ref` is
    `provider/model`.
  - Claude Code lists `supportedModels()`, served by Anthropic or the cloud provider its
    environment selects, remotely, unless a gateway passed on purpose (`ANTHROPIC_BASE_URL` or a
    provider's base URL) receives its requests: the list then names the gateway and is served
    where the gateway's address is.
  - The mock runtime lists the synthetic models it is given, labeled as such.

## Alternatives considered

- **Keep runtime-specific `model` options only.** Nothing to show, nothing checked before the
  runtime runs, and a name is all a client knows about where a model runs.
- **A catalog of Halcyonic's own.** It would duplicate what each runtime already knows and go stale
  whenever a model is pulled, removed or configured.
- **Ask Ollama directly.** Only one of several model servers, and not what the runtime will use:
  the runtime's own list is the one that decides whether a start works.
- **Journal the list.** It is neither work nor a fact about work, and it changes under the
  control plane. Reading through keeps the journal to what happened.
- **`model_ref` inside the runtime options.** Options are opaque and runtime-specific; a choice
  that admission checks and every runtime understands belongs in the command.
- **An optional `model_ref` key instead of a nullable one.** Every contract uses explicit nulls so
  each consumer sees one shape, and the C# bindings refuse optional properties.

## Consequences

- Clients offer only models a runtime can use now, with where each runs and whether it calls tools,
  and a start with a stale choice fails in words before anything runs.
- Listing costs a request to the runtime, and for OpenCode and Codex may launch its server.
- The contracts, the generated schema and C# bindings, the recorded traces and the demonstration
  change together, and the journal gains its second migration.
- Codex cannot say which models a local provider serves, so its list is as good as its
  configuration. Claude Code's list is verified only against the SDK's types.
- Revisit when Codex lists a provider's own models, when OpenCode reports whether Ollama serves a
  model locally, and when the XR client starts work.

## Note, 2026-10-01

In the fifth headset session a start reached OpenCode with a hosted model the person had not
meant to choose ([quest-3-device.md](../validation/quest-3-device.md)). The client now lists the
Mac's models first and preselects the best local one; as a backstop, admission refuses a start
without a model on a runtime whose `model_choice` is `'listed'`, in words, with the rejection code
`model_required` ("Choose a model: this runtime lists the models it can use."). A start may still
carry no model on a runtime that does not list them. The decision is otherwise unchanged.

## Note, 2026-10-07

Codex now runs only on models served on this Mac, from a home of Halcyonic's own
([ADR 0011](0011-codex-app-server-stable-surface.md), note of 2026-10-07), so its list holds only
the models whose `served` is `'this_mac'`, never a model Ollama runs on its own remote service. In
practice that is the one model `pnpm mac-setup local-model` names in that home's `config.toml`.
The decision is otherwise unchanged.
