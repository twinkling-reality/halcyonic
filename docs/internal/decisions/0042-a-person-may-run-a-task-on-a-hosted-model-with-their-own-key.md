# ADR 0042: A person may run a task on a hosted model, with their own key, when they turn it on

- Status: Proposed
- Date: 2026-10-29

## Context

Halcyonic runs its two pinned coding runtimes on models served on the Mac only. Codex is held to
models on this Mac, from a home of its own, never signed in, with web search off
([INTEGRATIONS.md](../architecture/INTEGRATIONS.md), [SECURITY.md](../architecture/SECURITY.md)).
Halcyonic's own OpenCode settings may name only Ollama models, and the settings file can never start
paid model use ([ADR 0024](0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md),
decisions 4 and 5). The one hosted path is the Claude Agent runtime, turned on in the environment on
purpose (`HALCYONIC_CLAUDE_AGENT=1`), with the Anthropic key from the environment or
`<data dir>/anthropic-api-key`, refused when other users can read it.

On 2026-10-29 the owner asked for Halcyonic to show Meta's models, and offered their own Meta developer
account and its credits for use and testing. What Meta's pages say, read that day and recorded with
nothing run, is in [meta-models.md](../validation/meta-models.md):

- Meta's Model API serves `muse-wonder-1.0` (preview) and `muse-spark-1.3` at
  `https://api.meta.ai/v1`, with a Bearer key, over OpenAI's Responses and Chat Completions shapes and
  Anthropic's Messages shape, with tool calls and streaming. It costs $4 and $20 per million input
  and output tokens for Muse Wonder, $1.25 and $4.25 for Muse Spark.
- In Standard mode the content is not used to train Meta's models; Meta retains it as needed to
  provide the service, for the law and against misuse. The `-contributor` ids, and a Muse Code
  subscription by default, let Meta train on it. Zero Data Retention is for organizations Meta
  enables, not a person's own account. `store: false` asks the Responses API to keep no copy. Meta
  adds steering text of its own to every prompt.
- Meta documents a configuration for Codex (`model_providers`, `wire_api = "responses"`) and for
  OpenCode, and the pinned OpenCode 2.0.18 already carries a built-in `meta` provider whose Responses
  requests send `store: false` (read from its strings, not run).
- A person already chooses each task's model from the runtime's own list, which says where each
  model is served from the address its requests go to, and the headset takes a second press for a
  remote one ([ADR 0016](0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)). The model the
  runtime reports is recorded as `runtime.model.used`.

The forces: a hosted model sends the project's files, the agent's reading and its commands' output
to the provider, spends real money, and follows the provider's text beside Halcyonic's. A key in a
runtime's environment can reach every command its agent runs unless the runtime keeps it out.

## Decision

1. **Off unless the person turns it on, on the Mac.** Hosted models through Codex and OpenCode are
   off by default. The person turns them on in the control plane's environment
   (`HALCYONIC_HOSTED_MODELS=meta`), the way the Claude Agent runtime is turned on. The settings file
   refuses the variable by name, so ADR 0024's rule that the file never starts paid model use stands.
   No Halcyonic command sets the variable for the person, `pnpm mac-setup` included, and nothing a
   headset or any client sends can turn it on.
2. **The person's own key, from one file, held like the access token.** The key is read only from
   `<data dir>/meta-model-api-key` (`~/.halcyonic/meta-model-api-key` by default), once at startup:
   a regular file, not a link, owned by the user running the control plane, mode 600, small, holding
   one key of Meta's shape (`LLM_`). Anything else refuses the provider at startup, saying why, and
   Codex and OpenCode then run on local models only. The key is never printed, logged, journaled,
   sent to a client or written anywhere else; error text loses every copy of it, named
   ("[redacted: Meta key]"), as for the other keys Halcyonic holds. Halcyonic never reads another
   tool's key or another project's files for it.
3. **Standard mode only.** The adapters offer only Meta's Standard model ids that the adapter names
   (`muse-wonder-1.0` and `muse-spark-1.3` at first) and refuse every `-contributor` id, at listing
   and again at start, so no content goes to training through Halcyonic.
4. **No copy kept at Meta when Halcyonic can ask for none.** Every Responses request carries
   `store: false`. Meta's web search tool stays off, as Codex's own does today.
5. **The key stays out of what the agent runs.** The key reaches only the runtime's model client, never
   the environment of a command, tool or child process the agent starts. Each runtime is enabled for
   hosted models only after a runtime test on its pinned version shows that a command the agent runs
   cannot read the key by any of three ways:
   - the key file itself (`cat <data dir>/meta-model-api-key`): Codex's `workspace-write` sandbox
     reads the whole disk, and OpenCode's profile
     ([ADR 0028](0028-opencode-runs-inside-a-sandbox-of-halcyonics-own.md)) reads outside the
     project roots;
   - the runtime process's environment, from another process (`ps eww <pid>`, or `KERN_PROCARGS2`):
     Codex takes the key from an environment variable (`env_key`), and macOS lets a process read the
     environment of another process of the same user unless a sandbox stops it;
   - the runtime's configuration, its authentication files and its home.

   With today's sandboxes, only a project whose agent is confined to it
   ([ADR 0041](0041-a-new-apps-agent-is-confined-to-its-project.md)) can pass, unless the key reaches
   the runtime by a channel no command it runs can read; the adapter that uses such a channel names it
   and the test covers it. Until the test passes for a runtime, and for a project, its hosted models
   stay off whatever the switch says.
6. **Every hosted turn says so.** A hosted model is listed as served `remote` with a name that says
   who serves it ("Muse Wonder, hosted by Meta"). The headset's second press for a remote model says
   that the project's files and commands' output go to Meta. Every turn on it is shown as hosted, with
   the model the runtime reports (`runtime.model.used`), never only the model chosen.
7. **The cost is shown, as an estimate.** The adapter reports each turn's input, cached and output
   tokens as the provider reports them, and the control plane shows an estimated cost from Meta's
   published prices, dated. It is reported, never observed: Meta's bill is the source of truth. The
   person may set a spending limit on the Mac, in the same environment as the switch; once the
   estimate reaches it, Halcyonic interrupts the running hosted turn and refuses new ones until the
   person raises it.
8. **What changes in the local-only rules, and nothing else.** When decisions 1 and 5 hold:
   - Codex may run, besides models served on this Mac, the Standard Meta models of decision 3, through
     a `meta` provider the adapter writes into its own home (`wire_api = "responses"`); its home of its
     own, never signed in, and plugins, the update check, analytics and web search off on every launch
     all stay.
   - OpenCode may run the same models through its built-in `meta` provider, configured by the control
     plane at launch. Halcyonic's own OpenCode settings file stays local by construction (ADR 0024,
     decision 5), catalog fetches stay off, `webfetch` and `websearch` stay refused.
   - Everything else that is local stays local: the companion
     ([ADR 0025](0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)), speech
     ([ADR 0021](0021-speech-becomes-a-draft-transcribed-on-the-mac.md)) and every default.
   [INTEGRATIONS.md](../architecture/INTEGRATIONS.md) and [SECURITY.md](../architecture/SECURITY.md)
   change with the implementation, in the same change, not before.

Not decided here: Muse Code as a runtime of its own (a separate adapter over its session protocol,
pinned and checked by its schema fingerprint, with its approval judge and Contributor mode off), and
any hosted provider other than Meta.

## Alternatives considered

- **Keep everything local.** Simplest and safest, but the owner asked for Meta's models, and the
  person already chooses each task's model with a second press for a remote one; the gap is a key and
  the rules for it, not the choice.
- **Turn it on in the settings file.** One place for a person's setup, but it would undo ADR 0024's
  rule that the file never starts paid model use, which keeps spending a deliberate act.
- **Let the person paste the key on the headset.** Easier to reach, but a key would then cross the
  network to the Mac and live in a client, where Halcyonic keeps no credential.
- **Allow Contributor mode for its lower price.** Cheaper, but it sends a person's code to training;
  a person who wants that can use Meta's own tools.
- **Run Muse Code instead of Codex or OpenCode.** Its session protocol is a structured surface an
  adapter could drive, but it is pre-1.0, decides approvals with a judge by default, and would be a new
  runtime rather than a model choice; it is its own unit.

## Consequences

- A person with their own Meta key can run a task on Muse Wonder or Muse Spark through the runtimes
  Halcyonic already drives, chosen per task, seen as hosted, with its cost in view.
- The project's content leaves the Mac for Meta on every such turn; Halcyonic can show it and ask
  for `store: false`, not make Meta keep less than its terms say.
- A new credential file and a new redaction name; a runtime test per runtime that the agent cannot
  read the key, before it is enabled; and a capped live test of the API before any of this is built,
  recorded in [meta-models.md](../validation/meta-models.md).
- In practice hosted models start with confined projects only (decision 5). When this is built,
  [SECURITY.md](../architecture/SECURITY.md) also says what is already true today: the keys in the data
  directory, the Anthropic key among them, can be read by an agent that is not confined to its
  project.
- Turns on a hosted model are no longer reproducible offline, and prices and models change: the
  dated price table and the model list need re-checking.
- Revisit if Meta's terms change, if Zero Data Retention becomes available to a person's own
  account, if a pinned runtime cannot keep the key from its commands, or when a second hosted provider
  is wanted.
