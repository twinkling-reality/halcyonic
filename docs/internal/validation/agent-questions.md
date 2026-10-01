# Agent questions

- **Question:** When an agent stops to ask the person something, through what structured surface
  does each runtime Halcyonic hosts ask, how does a client answer, and what happens when nobody
  answers, the question is dismissed, or the turn is interrupted?
- **Date:** 2026-10-01.
- **Versions:** OpenCode `@opencode/cli` 2.0.18 (the pinned binary); Codex `codex-cli` 0.157.0 (the
  pinned binary) on its app-server's stable surface; Claude Agent SDK 0.3.283 with the Claude Code
  2.1.283 it bundles; Ollama 0.34.4 serving `qwen3.6:35b-a3b-nvfp4`; macOS 26.7 on an Apple M5 Max.
- **Method:** each runtime run under `env -i` with temporary HOME and configuration directories on
  the local model only, driven over its own structured surface by a small script (OpenCode's HTTP
  API and event stream, Codex's JSON-RPC over stdio, the Agent SDK's `query()` with `canUseTool`),
  instructing the agent to ask a question with options before doing anything else; every message
  logged. Schemas were read from what each pinned binary generates or serves (OpenCode's
  `/openapi.json`, `codex app-server generate-ts`, the SDK's `sdk-tools.d.ts`), and behavior the
  runs did not show from the source at the matching tag. Claude Code reached the local model
  through Ollama's Anthropic-compatible Messages API (`ANTHROPIC_BASE_URL` set on purpose, a
  placeholder key); a socket monitor saw nothing beyond loopback in 225 samples. No hosted model was
  called. The raw evidence is kept outside the repository.
- **Status:** Runtime verified for the flows below on the local model. A hosted Claude model, and
  how long Codex waits for an answer that never comes, were not run.

## The fifth headset session

OpenCode's agent called its `question` tool; the adapter recorded only `runtime.tool.started`, so
the character read Working while the agent waited for an answer nobody could give
([quest-3-device.md](quest-3-device.md), fifth session).

## OpenCode 2.0.18

- **The tool.** `question`, input `{questions: [{question, header, options: [{label, description}],
  multiple?}]}`; its description tells the model that a typed answer is always possible. It first
  asks the permission system for the action `question` (allowed by default), then opens a
  **form** on the session.
- **The form.** `form.created` on the event stream carries `{id: "frm_…", sessionID, title:
  "Questions", metadata: {kind: "question", tool: {messageID, id}}, fields}`, one field per
  question: `key` `q0`, `q1` and so on, `title` the header, `description` the question, `type`
  `string`, or `multiselect` when several answers are allowed, `options` `[{value, label,
  description}]` with `value` equal to the label, and `custom: true`. The session reports no waiting
  status; the pending form is the only sign.
- **Answering.** `POST /api/session/{sessionID}/form/{formID}/reply` with `{answer: {q0: "blue",
  q1: ["apple", "pear"]}}` answers 204 and `form.replied` follows with the same answer. The model
  receives the answers and used them (it wrote the chosen colour, and in a two question run the
  typed title and both chosen fruits). A typed answer outside the options is accepted. Replying
  again answers 409 `FormAlreadySettledError`.
- **Dismissing and interrupting.** `DELETE` on the form answers 204, then `form.cancelled`; the tool
  fails with "The user dismissed this question" and the turn ends interrupted. Interrupting the
  session while the form waits cancels it the same way (`form.cancelled`, the turn interrupted); a
  late reply answers 409.
- **Reading it back.** `GET /api/session/{sessionID}/form` lists the session's pending forms and
  `GET …/form/{formID}` gives its state, `pending`, `answered` or `cancelled`. `GET /api/form`
  without a location listed none.
- Forms can also carry number, integer, boolean and external link fields; only the question tool's
  forms were observed.

## Codex 0.157.0

- **The request.** `item/tool/requestUserInput` is a server request on the stable surface (in the
  pinned binary's generated schema, and not gated as experimental in the source at `rust-v0.157.0`,
  although its documentation still calls it experimental). Parameters: `{threadId, turnId, itemId,
  questions: [{id, header, question, isOther, isSecret, options: [{label, description}] | null}],
  isBlocking, autoResolutionMs}`. Response: `{answers: {[questionId]: {answers: string[]}}}`.
  Codex sets `isOther` on every question, so a typed answer is accepted.
- **When Codex asks.** The model is offered `request_user_input` by default, but in the default
  collaboration mode its handler refuses unless the thread's configuration enables the feature
  `default_mode_request_user_input`, which Codex marks under development and warns about at thread
  start. Without it the model called the tool twice, was refused, asked in plain text and completed
  its turn. With `config: {"features.default_mode_request_user_input": true}` on `thread/start`,
  the request arrived and the answer reached the model. Plan mode needs the experimental API and
  was not used.
- **Waiting and resolution.** `thread/status/changed` with `activeFlags: ["waitingOnUserInput"]`
  comes just before the request; after the answer, `serverRequest/resolved {threadId, requestId}`
  and the flags clear. An empty answer, or an error in reply, reaches the model as no answers: a
  decline cannot be told apart from silence. Interrupting while it waits ends the turn interrupted
  and then resolves the request; a later answer is ignored. An answer holding a lone surrogate
  (`"\udc00"` in JSON) is dropped without a word, the request stays pending, and a later
  well-formed answer to it is taken (an end to end test pins this). `thread/read` keeps neither the question
  nor the answer.
- **MCP elicitation.** `mcpServer/elicitation/request` is stable and was answered (`accept` with
  content reached a test MCP server), but only a configured MCP server raises it, which Halcyonic
  does not configure.

## Claude Agent SDK 0.3.283

- **The tool.** `AskUserQuestion`, input `{questions: [{question, header, options: [{label,
  description}], multiSelect}]}`: one to four questions of two to four options, question texts
  unique. It always reaches `canUseTool`, whatever the permission rules, except in `dontAsk` mode
  where it is denied; it is not available to subagents.
- **Answering.** `canUseTool` resolving `{behavior: 'allow', updatedInput: {...input, answers:
  {[question text]: answer}}}`, the answer a label, labels joined with ", " for several, or typed
  text. The model receives "Your questions have been answered: …" and used the answer. Allowing the
  input unchanged, as an approval does, tells the model "The user did not answer the questions."
- **Declining and interrupting.** A deny with a message reaches the model as a tool error with that
  message, and the turn goes on. Interrupting the query while the callback waits aborts the
  callback's signal within milliseconds and ends the turn.

## Through the adapters

Built from the surfaces above ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md))
and tested on 2026-10-01:

- **OpenCode**, end to end against the pinned binary and a fake provider whose model calls the
  question tool: the question reported with its prompts, answered through the adapter, and the
  agent receiving the answer; an interrupt while it waits reported as `dismissed` and the turn
  interrupted. The event mapping is also tested on streams captured from the runs above.
- **Codex**, end to end against the pinned binary and a fake provider whose model calls
  `request_user_input`: the question reported with its prompts (Codex marks every one as taking
  typed text too), the answer reaching the model as the tool's output, and a second answer refused.
  An interrupt while it waits ends the turn before Codex settles the request, so the turn's end
  withdraws the question and no `runtime.question.resolved` is reported. A test drives a bare
  app-server client with the feature switch off and on, and fails if the tool stops being refused
  without it ("request_user_input is unavailable in Default mode"), if the request stops arriving
  with it, or if the start warning changes. Secret questions and the switch that turns questions off
  are tested against a stand-in binary only.
- **Claude Agent SDK**, against a scripted SDK only: `AskUserQuestion` reported as a question
  rather than an approval, the answers returned in the tool's input keyed by question text, several
  labels and typed text joined with ", ", a withdrawn question no longer answerable, and input of
  another shape shown as an approval. The adapter has not run a question with a real model.

## Consequences

- All three runtimes have a structured, answerable question surface; for Codex it needs the per
  thread feature above.
- A question is not an approval: answering one carries the person's words to the agent, while the
  actions it then takes still need their approvals.
- Every runtime cancels a pending question when the turn is interrupted, so stopping is always
  available to the person, answer path or not.
- Codex's `isSecret` questions ask for something the person should not type into a headset whose
  answers are journaled.
- The adapters' changes follow from these surfaces
  ([ADR 0022](../decisions/0022-agent-questions-reach-the-person.md)).
