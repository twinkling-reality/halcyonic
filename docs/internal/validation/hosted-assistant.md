# The assistant on a hosted provider (2026-11-12)

What was read, tried and seen before the assistant could run on a hosted model with the person's own key
([ADR 0030](../decisions/0030-workspace-requests-use-a-journaled-agent-tool-loop.md), amendment of 2026-11-12). No real
key was used: every call below carried a fake value, and the plane's runs went to a stand-in on this Mac.

## Read from the official documentation, 2026-11-12

Anthropic's pages, read that day: Models overview, Define tools, Handle tool calls, and Thinking.

- **Models.** The current ids are `claude-opus-5-5` (the page's suggested start), `claude-sonnet-5-5`,
  `claude-haiku-5-5` and `claude-fable-5-2`, each with a 1M-token context and 128K output. Prices per million input
  and output tokens: $4 and $20, $2 and $10, from $0.10 and $0.50, and $8 and $40.
- **Requests.** `POST /v1/messages`, with the key in `x-api-key` and an `anthropic-version` header (the examples
  send `2026-10-21`).
  - A tool is `{name, description, input_schema}`, with the name matching `^[a-zA-Z0-9_-]{1,128}$`.
  - A call comes back as `stop_reason: "tool_use"` with `tool_use {id, name, input}` blocks.
  - Results go back in one `user` message as `tool_result {tool_use_id, content, is_error}`. The results come first
    in that message, and nothing may come between a call and its results. There is no `tool` role.
- **Forced tool choice.** `tool_choice` set to `any` or `tool` returns a 400 on Claude Opus 5.5, Sonnet 5.5 and
  Fable 5.2. `auto`, the default, is accepted.
- **Thinking.** It is always on for Opus 5.5 and Fable 5.2, and its tokens count toward `max_tokens`. Within a
  tool-use turn the model's thinking blocks must be passed back "complete and unmodified"; a modified block is
  rejected with a 400. Outside tool use, earlier turns' thinking may be omitted.
- **Refusals.** A refusal is a 200 response with `stop_reason: "refusal"`.

## The shape that follows from it

- **Each turn as a fresh conversation.** The host rewrites its own lines between turns: the limits line, the packing
  of a long request, and the answering turn's history. So a native replay of earlier tool calls would have to carry
  the model's thinking blocks unchanged past those edits.
  - The Messages protocol (`providers/anthropic/messages.ts`) therefore sends every turn as a new conversation. The
    host's instructions are the system prompt. The request so far is one record in a user message: the model's own
    earlier words and calls, and what the host returned for them, marked as data. The tools offered now are attached.
  - No thinking block is ever sent back, and the model still calls tools natively.
- **The OpenAI-compatible protocol** (`providers/openai-compatible/chat.ts`) replays earlier calls natively, pairing
  each result with the call it answers by order, since that shape keeps no reasoning that must come back.
- **No forced tool choice and no fallback.** No forced `tool_choice` is ever sent, and no server-side fallback is
  asked for, since the host never lets another model answer without saying so. A refusal is a failed turn, in words.
- **Effort and output.** The Messages protocol asks for effort `low` and allows 8,000 output tokens, so a model's
  thinking has room within the output it is allowed. Input is packed to the same 12,288 bytes as the local model.

## Tried with a fake key against the real address

- **The call.** With the SDK the lockfile already held (`@anthropic-ai/sdk` 0.128.0), one `messages.create` was
  sent with a fake key, `model: claude-opus-5-5`, effort `low` and one tool.
- **The result.** `AuthenticationError`, status 401, type `authentication_error`. So the request reaches the API, and
  a key the API refuses arrives as a typed error that maps to `key_refused` without its text.

## Seen on a scratch plane against a stand-in (`.private`, not tracked)

- **Setup.** A plane on its own scratch data, with a fake key in `anthropic-api-key` (mode 600), had its provider
  pointed by `HALCYONIC_ASSISTANT_UPSTREAM` at a stand-in on 127.0.0.1 that speaks the Messages shape.
- **The assistant row.** It read available, `served: remote`, provider Anthropic, model `claude-opus-5-5`.
- **New Task.** "Make a task called Garden Lights to light the garden path at night":
  - the stand-in proposed New Task from the host's list;
  - the card said Title "Garden Lights" and Goal "Light the garden path at night";
  - the person's one press made the task;
  - the reply after it read in plain words.
- **A refused key.** With the stand-in refusing the key, the row read unavailable, "The provider refused the stored
  key.", and the request failed with the same words in its step.
- **The key's text.** The fake key's text was found in no file but its own: not in the journal, the plane's log or
  the evidence.
- **What the stand-in received.** The key only in `x-api-key`, never a Bearer header. No `tool_choice`, no
  `fallbacks`, effort `low`, and no thinking block sent back.

## After review (2026-11-12)

A fresh reviewer read f0371a2a2 and found no path for the key into an event, the journal, a log, an error or a reply.
Four changes followed, each with a test that fails without it:

- **The environment is never read for the assistant.** A developer's shell or the C# suite's plane that inherits
  `ANTHROPIC_API_KEY` would otherwise have sent every request to the paid API unasked.
- **The SDK refuses redirects.** On a cross-origin redirect, fetch drops `Authorization` but would carry
  `x-api-key` to the new host.
- **Marks in the record cannot be forged.** Agent or project text holding `</host_returned>` could otherwise have
  posed as the model's own earlier calls.
- **The Ask column says where a reply ran:** "Answered by test-model-1 on Test Hosted".

## A second, different declaration

`hosted-model.test.ts` runs the same tests through the one `HostedWorkspaceModel` for each declaration: the real ones
for the owner's keys (Anthropic's, and Meta's below) and a test provider with its own variable and file, against
stand-ins. The test provider needs nothing but its declaration. As amended for D1 and D2 (below), for each:

- a stored key alone moves nothing; the person's choice runs a tool round on it;
- a chosen model with its key removed says so at the next read, never falling back;
- a refused key says so, is never sent again, and a new key is tried;
- a key file others can read sends nothing;
- a refusal is a failure in words;
- a key in the environment is never read, so a plane that inherits one stays local and sends nothing.

## Meta's Model API, the second real provider (2026-11-12)

Meta's pages were read again that day ([meta-models.md](meta-models.md)). Its Chat Completions surface is the
OpenAI-compatible shape the seam already has, so Meta is a declaration (`providers/meta/declaration.ts`), not code of
its own:

- **Protocol and key.** `openai_chat` at `https://api.meta.ai/v1`. The key is `META_API_KEY`, the variable Muse Code
  and OpenCode's built-in `meta` provider read, kept in `<data dir>/meta-api-key` and sent as `Authorization: Bearer`.
- **Models.** `muse-wonder-1.0` first, then `muse-spark-1.3`. No `-contributor` id is declared, since those let Meta
  train on the content.
- **Effort and output.** Effort `low`, sent as `reasoning_effort`, since Muse Wonder otherwise thinks at `max`. The
  output is bounded by `max_completion_tokens`, which counts that thinking.
- **Refusals.** A refusal is read as `content_filter` or the message's `refusal` text, and that text is never relayed.
- **Which provider runs.** Whichever model the person chose with Use Model. Meta's models are listed first, since
  Meta is declared first.

The seam gained a declared `effort`, which each protocol puts where its shape says, and a declared output field. Each
protocol refuses an effort or field it does not document. After review, two changes avoid shapes Meta does not
confirm:

- The status check reads the one model's record (`GET /models/{model}`) instead of the list, whose shape and paging
  are not shown.
- The host's lines go first, as one system message, instead of a system message after the person's words.

**Seen on the scratch plane against a stand-in that speaks the Chat Completions shape Meta documents, fake `LLM_`
key** (`.private`):

- **Assistant row.** It read available, `served: remote`, provider Meta, model `muse-wonder-1.0`.
- **New Task.** It was proposed with Title "Garden Lights" and Goal "Light the garden path at night"; the one press
  made the task, and the reply after it read in plain words.
- **Refused key.** The row read unavailable with the stand-in refusing the key, and the request failed in the same
  words.
- **The key's text.** It was found in no file but its own.
- **What the stand-in received.** The key only as a Bearer token, `reasoning_effort: low`, `max_completion_tokens:
  8000`, no `tool_choice`, and the earlier call replayed natively with its `tool_call_id`.

Unknown until a real call to Meta:

- whether Muse Wonder proposes from the list as the stand-in did;
- how long a turn takes at `low`;
- the shape of the models list;
- how a refusal really arrives;
- whether Meta accepts the host's system message after the person's words, as OpenAI's shape does.

Meta adds steering text of its own, unpublished, to every prompt.

## The person's choice and a refused key (the lead's D1 and D2, 2026-11-12)

`assistant-choice.test.ts` runs both in one plane with a test provider and an in-process protocol:

- **D1.** A stored key offers its model in `assistant_models`, after "This Computer's Model". A request then runs on
  this computer and the key spends nothing. Use Model is pressed as any entry: prepared from a row and the list's
  digest, then reviewed. The next request runs on the hosted model, and the choice is in the projection's exported
  state.
- **A model not offered.** A choice of a model the list does not offer is refused, and the choice stays.
- **D2.** A refused key leaves the request waiting, not failed. Its step says the provider refused the key, and the
  host's own proposal of this computer's model waits beside it. A status read and later turns send nothing more with
  that key. The one press brings the choice back to this computer, and the request answers there.

The test fails without the host's offer.

## Not verified

- No run with a real key. That run is the owner's, once the key is stored through the product's own Add Key.
- Whether a real model proposes as the stand-in did, and how long a real turn takes.
- What a request costs. Estimated from the bounds at $0.10 to $0.50 on Opus 5.5, not measured.
