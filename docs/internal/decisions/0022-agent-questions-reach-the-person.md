# ADR 0022: An agent's questions reach the person, who answers them through the runtime's own surface

- Status: Accepted on 2026-10-01 by the owner.
- Date: 2026-10-01

## Context

In the fifth headset session an OpenCode agent called its `question` tool. Halcyonic recorded only
a tool start, so the character read Working while the agent waited for an answer nobody could give,
until the owner stopped the turn ([quest-3-device.md](../validation/quest-3-device.md)).

Each runtime Halcyonic hosts asks through a structured surface, verified on 2026-10-01 against the
pinned binaries on a local model ([agent-questions.md](../validation/agent-questions.md)):

- OpenCode 2.0.18 opens a form on the session; a reply carries the answers, a cancel or an
  interrupt withdraws it, and the session's pending forms can be read back.
- The Claude Agent SDK's `AskUserQuestion` arrives through `canUseTool`, the callback approvals use.
  Allowing it as an approval tells the model "The user did not answer the questions."; the answers
  go back in the tool's input, keyed by each question's text.
- Codex 0.157.0 sends `item/tool/requestUserInput`, a server request on the app-server's stable
  surface. In the default collaboration mode its handler refuses the tool unless the thread's
  configuration enables `features.default_mode_request_user_input`, a feature Codex marks under
  development and warns about when the thread starts. Without it the model is refused and asks in
  plain text instead. Codex may also mark a question secret (`isSecret`).

Every runtime withdraws a pending question when its turn is interrupted.

## Decision

- **Questions are runtime events.** `runtime.question.asked` carries `{question_id, prompts,
  answerable}` and `runtime.question.resolved` carries `{question_id, outcome}`, `answered` or
  `dismissed`. Each prompt is `{key, header, text, options: [{label, description}], multiple,
  free_text, secret}`. The event is observed, because the runtime's surface says a question waits;
  its text is the agent's words, shown as such and never interpreted. The question id is the
  runtime's, kept as an opaque reference.
- **A question needs the person.** While a question is pending the execution is
  `waiting_for_human`, and its attention is `question_pending` at `action_required`, as for an
  approval. A turn's end clears pending questions, as it clears approvals.
- **What is journaled and sent stays small.** Every snapshot and change carries an execution's view
  whole, so the view lists at most three pending questions, those Halcyonic can answer first, then
  the others, each oldest first, the rest showing as those are resolved; only those shown can be
  answered and are named in the attention reasons. A question longer than 16,000 characters in all
  is shortened, each cut marked, and unanswerable, so its event is bounded too: the control plane
  fits it as it journals it, after taking out the secrets it holds, and adapters report it whole.
- **One new command.** `execution.answer_question {execution_id, question_id, answers: [{key,
  selected, text}]}`, policy `low_consequence`. Admission refuses it for a runtime without the
  capability, an unknown question (`question_not_found`), an execution not waiting for the person,
  a question the adapter marked unanswerable (`capability_unsupported`), and answers that do not
  fit the prompts (`invalid_answer`): every prompt answered exactly once, chosen labels among those
  offered, at most one unless `multiple`, typed text only when `free_text`, and not both a choice
  and text for a question that takes one answer. Accepting the command is not success: the answer
  counts once the runtime confirms it, by `runtime.question.resolved`. A turn that ends first
  withdraws the question with no resolution reported, as Codex does when interrupted, and an answer
  not yet confirmed then fails with an unknown effect.
- **There is no dismiss command.** The person stops the turn, which every runtime confirms
  withdraws the question. A question the person cannot answer through Halcyonic shows that the
  agent is waiting, and stopping stays available.
- **The capability is declared.** `answer_question` in `RuntimeCapabilities`, through each runtime's
  own surface: OpenCode's form reply, the Claude Agent SDK's `AskUserQuestion` input, Codex's
  `requestUserInput` response, and the mock's scripted questions. A question is unanswerable when
  the person could not see whole what they would answer, or the adapter cannot carry an answer
  back faithfully: anything in it cut to fit the contract (a header, a text, a label or a
  description, each shown with a cut mark, cut by the control plane), a text or label repeated,
  more than 10 prompts or 20 options, or a field type other than a choice or text.
- **Secrets are never asked for in the headset.** A secret prompt is shown with `secret: true` and
  the question is unanswerable: the client says the agent asks for something secret and offers no
  answer field.
- **Answers are deliberate.** A client sends an answer only when the person presses a control that
  sends it, never on a gesture that only returns focus. A spoken answer stays a draft until then
  ([ADR 0021](0021-speech-becomes-a-draft-transcribed-on-the-mac.md)).
- **Codex relies on an under-development feature, knowingly.** The Codex adapter starts every
  thread with `features.default_mode_request_user_input` set. This is not the `experimentalApi`
  opt-in that [ADR 0011](0011-codex-app-server-stable-surface.md) rules out: the request and its
  response are on the stable surface, and only the switch that lets the default mode raise it is
  under development in 0.157.0. It is a deliberate, narrow exception to ADR 0011's rule of relying
  only on what Codex calls stable, held by these conditions:
  - an end to end test against the pinned binary fails if the switch stops being needed, stops
    working, or its warning changes;
  - the switch and the questions are verified again on every Codex upgrade, with the smoke test
    ADR 0011 already requires;
  - one adapter option, `answerQuestions: false`, stops passing the switch and declares
    `answer_question` false, should the feature misbehave;
  - the warning Codex sends at thread start is not agent activity, so the adapter never reports
    it; if a runtime notice ever reaches the activity stream, it is labeled as one.

## Alternatives considered

- **Show a question as an approval.** What Halcyonic did for Claude Code: approving answers nothing,
  and the agent carries on as if the person refused to answer.
- **Show only that the agent waits, with no answer path.** Truthful, but the person must leave the
  headset for a question with two options, and no other client is set up to answer the OpenCode and
  Codex threads that Halcyonic hosts.
- **A dismiss command.** Each runtime has a dismissal (OpenCode's cancel, a Claude Code denial, an
  empty Codex answer), but they differ: OpenCode ends the turn, Claude Code tells the model and goes
  on, and Codex cannot tell a decline from silence. Stopping the turn means the same everywhere.
- **Codex without the switch.** Codex then asks in plain text, which is truthful but not
  answerable as a question; plan mode would raise it without the switch, but needs the
  experimental API that ADR 0011 rules out.
- **Answer Codex's secret questions in the headset.** The answer would be journaled and pass
  through the headset's keyboard or microphone.

## Consequences

- The person can answer the agent where they direct it, and a waiting agent no longer reads as
  working.
- The journal holds the person's answers, as it holds their instructions.
- Contracts gain two events, a command, a capability, a view and three rejection codes (with
  `model_required`, added at the same time for a separate reason); no stored event changes, so no
  migration is needed.
- Every Codex upgrade can break questions without notice; the end to end test is the guard.
- Revisit when Codex makes the default mode's questions stable or removes them, if a runtime gains a
  dismissal that means the same as everywhere else, or if people need to answer secrets, which would
  need a channel that is neither journaled nor typed in the headset.
