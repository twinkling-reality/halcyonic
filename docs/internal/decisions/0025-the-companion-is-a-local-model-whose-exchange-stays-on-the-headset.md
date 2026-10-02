# ADR 0025: Create's companion is a local model the control plane asks one reply at a time, and its exchange stays on the headset

- Status: Accepted on 2026-10-02 by the owner, with the 7-day draft retention and the two
  amendments at the end.
- Date: 2026-10-02

## Context

Create a project is meant to happen "with an AI companion": a person brings an idea "or work[s]
them out together through conversation and simple choices", edits a short recap, and starts the
first real task ([PRODUCT.md](../product/PRODUCT.md), "Entry paths" and "Intended experience").
The owner asked for a short exchange that asks only questions whose answers matter, may say an idea
is unclear or cannot be built, and ends in the same editable recap and review. The companion is a
guide, not a Workstream character. Today Help me figure it out asks four fixed questions, labelled
"Fixed questions, not an AI", and composes the recap from the answers without a model
(`ProjectIdea`); the draft lives in the panel's memory and an app restart loses it
(`EntryPanel.Create`, [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

Rules that bound the choice: no paid or hosted model, by default or silently; model text is
`reported`, never `observed`, and never a source of truth; every change to work is a command,
confirmed by the person and journaled; nothing in the companion becomes a new domain object by
default ([AGENTS.md](../../../AGENTS.md), [SECURITY.md](../architecture/SECURITY.md): "A
conversational model must never turn vague speech into permission for an irreversible action").

Measured on this Mac on 2026-10-02 ([companion-model.md](../validation/companion-model.md)), with
Ollama 0.34.4 and the agents' local default model `qwen3.6:35b-a3b-nvfp4`:

- Ollama refuses structured output for that model with HTTP 501 ("structured output is
  unavailable"): its MLX engine cannot constrain the reply. The shape can only be asked for in the
  prompt and checked afterwards. With two example replies in the prompt, 21 of 21 replies parsed
  and had the right shape; written as a type, 2 of 7 did not parse.
- Thinking off, a turn took 1.1 to 2.3 s once the prompt's prefix was cached, 6.8 s for the first;
  loading the model when it is not loaded adds about 6 s ([local-models.md](../validation/local-models.md)).
  With thinking on, 7.5 to 32.7 s, and 2 of 7 replies ran out of tokens before their JSON.
- It flagged an idea that cannot be built, asked one question at a time about vague ones, and
  proposed at once for a precise one. Given an idea that dictated `rm -rf ~` and an upload, it
  refused all three times but proposed an unrelated project of its own. A 1.2B model with the
  shape enforced answered in under a second but put the dictated commands into its proposed first
  task and said it had checked the Mac.
- Ollama serves one request per model at a time: a companion turn waited 22 s behind an agent's
  1,500-token request. Closing a request stops its generation: the next request was served within
  0.25 to 2.2 s.
- Ollama's documentation (read the same day) lists a remote model in `GET /api/tags` with
  `remote_host` and `remote_model`, names cloud models with a `cloud` tag, turns cloud features
  off with `OLLAMA_NO_CLOUD=1`, binds 127.0.0.1:11434 by default and documents no authentication.

A runtime Halcyonic already runs could host the companion instead, but each of them runs a prompt
as an agent's execution: OpenCode with about 6,500 tokens of its own prompt, tools and a working
directory, Codex with a rollout kept in the developer's `CODEX_HOME` that Salidium and Seorak read as
agent work, and Claude Agent on hosted models only ([companion-model.md](../validation/companion-model.md)).

## Decision

**Where it runs.** The control plane asks a local model through Ollama's `POST /api/chat`, one reply
per request from the headset, with thinking off, no tools and no images, and checks the reply
itself.

- Off unless the host names the model (`HALCYONIC_COMPANION_MODEL`, or the Mac's settings file if
  the host setup's ADR 0024, in progress, makes one). Ollama's address defaults to
  `http://127.0.0.1:11434` and must be loopback; any other address is refused at startup. The
  recommended model is the one the agents already use on the Mac, so one loaded model serves both.
- Before every turn the control plane reads `GET /api/tags` and refuses a model that is not listed,
  that carries `remote_host` or `remote_model`, or whose name has a `cloud` tag. It never pulls,
  creates or deletes a model, and never sets `keep_alive`, so it neither downloads nor holds memory
  beyond what Ollama already does.
- `GET /api/companion` answers whether the companion can be asked (available, not set up, not
  running, model missing, model not on this Mac) from that read alone, without asking the model;
  `POST /api/companion/replies` asks for one reply. Both are REST routes for any authenticated
  principal, with contracts in `packages/contracts` and generated C# bindings (to be agreed with the
  coordinator before they change).

**What it is.** A guide inside Create, never a task. It is no Project, Workstream or Execution, no
event and no command; it never becomes a character or a rail entry. It has no tools and sees no file,
folder, project, runtime, model list or other work: its whole input is the person's own words and its
own earlier replies, under a fixed system prompt. Its output is advice in one shape: a line to the
person of at most two sentences, its view of the idea (clear, unclear, or cannot be built as
software), and either one question with at most four short choices or a proposal of a project name
and a first task. The person can always answer in their own words, ask for the recap, or leave.

**Its record.**

- **The Mac keeps nothing.** The route holds no session: the headset sends the exchange so far with
  each turn. The exchange is never journaled, stored or logged; the log carries the outcome code,
  timings, token counts and the model's name only. Ollama holds the prompt in memory while the model
  stays loaded, as it does any request, and at its default log level logs counts and timings, not
  text (checked with a marker word).
- **The headset keeps the draft.** The person's words, the companion's replies, the fixed answers,
  the recap and the choices made are written on each change to one file in the app's private
  storage, for the Mac they were made with, so an app restart resumes them. The file is deleted
  once the Mac confirms the whole Start building, on Start over, or after 7 days without a change.
  Choices that depend on the Mac (the folder, the agent app, the model) are checked with the Mac
  again before they read as chosen. The demonstration keeps no draft.
- **The journal gets only what it gets today.** When the person confirms the review, the ordinary
  `project.create`, `project.set_location`, `workstream.create` and `execution.start` commands carry
  the name and first task as the person confirmed them. Nothing marks them as drafted with the
  companion.

**Its bounds.**

| Bound | Value |
| --- | --- |
| One request | At most 20 messages and 24,000 characters; a message of the person's at most 2,000; the companion's earlier replies must have the reply shape |
| Questions | At most 4 in one exchange; after the fourth, the control plane asks for a proposal only |
| The model | `num_ctx` 8,192, `num_predict` 512, temperature 0.3, `think: false` |
| Time | 30 s until the first token (waiting, loading, reading the prompt), 45 s for the whole turn; then the request to Ollama is closed, which stops it |
| The reply | At most 4,096 characters read; the line 300, a question 160, 4 choices of 48, a name 60 and a first task 1,000 characters |
| Retry | One, inside the same 45 s, when a reply does not parse or has the wrong shape |
| Who asks | One turn at a time per principal, one at a time on the Mac (another principal's is refused as busy), 12 turns a minute per principal |

**How it fails.** Creating never depends on the companion. Typing an idea and the fixed questions,
still labelled as not an AI, remain; the recap is always editable; Start building never needs a
reply. When Create opens it reads `GET /api/companion`; when the companion is not set up, not
running, missing its model, or the model is not on this Mac, Help me figure it out offers the fixed
questions and one line says why. A turn that waited too long, met another person's turn, or could
not be read, or a Mac that cannot be reached, is said in words with Try again and Go on without
it, and the exchange so far is kept. A reply that cannot be read is never shown in part.

**How its words stay its own.** Every reply carries `reported` provenance and the model's name,
served on this Mac. The headset shows its words through `LabelText` as text Halcyonic did not
write, quoted and tagged as the companion's, never in Halcyonic's voice. Its view of the idea is
said as the companion's opinion and never stops anything. What it proposed reads as suggested by
the companion in the recap until the person changes it, and the person's own typed words stay one
press away. The review shows the whole first task in parts before Yes, as it does today, because a
proposal can carry text the person did not intend.

**Voice.** Hold to talk follows [ADR 0021](0021-speech-becomes-a-draft-transcribed-on-the-mac.md):
a spoken answer becomes a draft in its field and reaches the companion only when the person sends
it as they would a typed one; voice never confirms the recap or Start building.

**The demonstration.** It plays an exchange recorded once from the real companion, stored with the
demonstration's recording and labelled recorded, with fixed choices to press; it never asks a model,
and Start building stays refused there as today. Its format is agreed with the lane that builds the
competition demonstration.

**Words.** New words for the owner's approval, beside what they replace ([WORDS.md](../product/WORDS.md)):

| Where | Today | Proposed |
| --- | --- | --- |
| Help me figure it out, its second line | A few fixed questions | Talk it through with the companion (the fixed questions keep "A few fixed questions") |
| Over the exchange | Fixed questions, not an AI. You can change every answer. (stays on the fixed questions) | The companion is an AI on your Mac. It can be wrong, and you can change everything before you start. |
| Its line | | The companion says: “{line}” |
| Its view | | The companion thinks this is unclear. / The companion thinks this can't be built as software. |
| Waiting | | Waiting for the companion… |
| Buttons | | Make the recap · Go on without it · Use my words · Try again |
| In the recap | | Suggested by the companion |
| Not set up | | The companion isn't set up on your Mac. Type your idea, or answer a few fixed questions. |
| Not running or no model | | The companion can't run on your Mac right now. Type your idea, or answer a few fixed questions. |
| Too slow | | The companion took too long. Your Mac's model may be busy with a task. Try again, or go on without it. |
| Unreadable | | The companion's answer didn't make sense, so it isn't shown. Try again, or go on without it. |
| Busy | | The companion is answering someone else. Try again in a moment. |
| The person-facing word | | companion: the AI on your Mac that helps shape an idea in Create; it builds nothing |

## Alternatives considered

- **Through OpenCode or Codex.** Each turn would be an agent execution: a task, or a pretend one;
  tools to deny; a working directory a new project does not have yet; OpenCode's 6,500-token prompt
  and its hosted default model; Codex's rollout kept and read by Salidium and Seorak as agent work.
- **Claude Agent, or any hosted model.** Not allowed by default, and never silently.
- **A small model with the shape enforced.** In shape every time and under a second, but it obeyed
  the injection and judged an impossible idea feasible.
- **A model of its own, apart from the agents'.** Possible by configuration, but a second large
  model costs 17 to 23 GiB more, and where Ollama keeps one model loaded each switch costs a load.
- **Apple's Foundation Models.** On-device with guided generation, but it needs a Swift helper and
  Apple Intelligence turned on, and the same framework reaches Private Cloud Compute and server
  providers. Read in its documentation, not tried.
- **Keeping the exchange on the Mac.** In the journal it would become permanent work history sent to
  every client; in memory it would be lost on restart and is state the route does not need. The
  headset has to keep the draft across a restart anyway.
- **Streaming the reply to the headset.** Faster feedback, but the realtime socket carries journaled
  state, and a reply takes about 2 s.
- **Marking the commands as drafted with the companion.** A contract change with no consumer; the
  person's confirmation makes the text theirs. Revisit if an audit needs it.
- **Only the fixed questions.** Predictable, but not the companion the product describes; they stay
  as the fallback.

## Consequences

- A person can work out an idea with an AI on their Mac and still create through the same recap,
  review and commands; when it cannot run, Create is what it is today.
- The control plane gains a direct dependency on Ollama's HTTP API, which has no authentication on
  loopback (any local process can call it, as with the agents' use of it), a prompt to maintain and
  test, a reply checked in code, a fake Ollama for tests, and new rows in SECURITY.md's controls.
- The headset gains a stored draft with its own tests, the companion's screens in EntryScreens,
  new words in WORDS.md and EntryText, and a recorded exchange in the demonstration.
- Prompt injection cannot reach a command without the person reading and confirming the whole first
  task, but it can change what the companion suggests; the review is the boundary, as it is for
  every typed task.
- Revisit when Ollama's engine for the agents' model supports structured output (then also send
  `format`), when another local model resists injection better, when people want the companion for
  Add a task, when real ideas show four questions are too few or too many, if the owner wants the
  exchange kept on the Mac, or if Ollama gains authentication.

## Amendments at acceptance

1. **Contention is a gate.** The companion shares the agents' model, and Ollama keeps one model
   loaded and serves one request per model at a time. Creating while other tasks run is the
   product's own promise, so a companion that often says it took too long would break it. Before
   the companion is called done, its turn times (to the first token and whole, median and 95th
   percentile) are measured with one agent task generating, on the validated Ollama settings, and
   recorded in [companion-model.md](../validation/companion-model.md) and here. If the 30 s bound
   to the first token is often reached, `OLLAMA_NUM_PARALLEL=2` is evaluated with its memory cost
   measured, rather than a small model, which obeyed the injection.

   Measured on 2026-10-02 with one Codex task generating on the same model at
   `OLLAMA_NUM_PARALLEL` 1: 10 of 14 turns reached the 30 s bound and were refused; the 4 answered
   had their first token after 12.7 to 24.9 s. The gate is not met there; `OLLAMA_NUM_PARALLEL=2`
   is next ([companion-model.md](../validation/companion-model.md)).
2. **Words.** The table above is approved, except that every phrase saying "your Mac" follows the
   owner's decision on the brand finding of the competition build's review; the host's wording is
   agreed with the host setup and competition build lanes before it reaches `EntryText`.
