# Contextual voice references and current implementation

Verified 2026-10-12 by reading official product/platform documentation and Halcyonic source.
No third-party voice product was installed, benchmarked or connected. Product descriptions are
evidence of documented behavior, not independent usability or reliability measurements.

## References

- [Wispr Flow Command Mode](https://docs.wisprflow.ai/articles/4816967992-how-to-use-command-mode)
  documents natural-language editing, reply drafting and search on desktop. It sends the active
  field and selection with the instruction, has explicit activation and cancellation, and states
  platform differences. This supports using deliberate context and recoverable interactions; it
  does not establish Quest support or authorize a new cloud integration.
- [VoiceOS product architecture](https://www.voiceos.com/blog/how-voiceos-is-building-the-voice-operating-system)
  describes dictation, selected-text editing, visible-screen context and actions through connected
  applications, with confirmation before consequential actions. It documents Mac and Windows
  behavior. It is a useful interaction reference, not proof that Quest permits the same access.
- [Meta Voice SDK activation](https://developers.meta.com/vr/documentation/unity/voice-sdk-activation/)
  documents explicit microphone activation and recognition through Wit.ai. The current Halcyonic
  local transcription decision is unchanged; these docs are not evidence of a local recognizer.
- [Meta hybrid apps](https://developers.meta.com/vr/documentation/spatial-sdk/hybrid-apps-overview/)
  documents native panel surfaces and lifecycle transitions. It does not establish continuous
  microphone ownership across those transitions or access to other apps' contents.
- [OpenClaw agent loop](https://docs.openclaw.ai/agent-loop) documents context assembly, model
  inference, tool execution, streaming and persistence across a run. Its
  [runtime architecture](https://docs.openclaw.ai/agent-runtime-architecture) separates tool
  schemas and policy from the loop and model transport. This supports the owner's clarified
  requirement for open-ended requests executed through tools, rather than a voice phrase catalogue.
  It does not establish a Halcyonic integration or permission to install, configure or run OpenClaw.

## Historical source snapshot — 2026-10-12, before the workspace loop

`MenuVoice` binds one recording to the column that started it. Its captured delivery callback
checks the recording generation and column identity. Leaving the column or losing focus drops
the recording or pending result. File editing also binds the execution and input epoch, and a
spoken instruction remains a draft for explicit review and Send.

The authenticated `/api/transcriptions` route uses the configured local whisper.cpp engine.
[voice-transcription.md](voice-transcription.md) records synthetic speech measurements and their
limits. Those numbers are not a measurement of natural-language intent resolution or real Quest
microphone accuracy. The existing local companion handles project creation; no workspace intent
agent loop exists at this review. Its prompt explicitly says it cannot read, run or build anything,
and its transport submits no tools. Renaming or widening that ask/propose reply would not supply
the missing execution loop.

## Current implementation — 2026-10-15

The historical snapshot above describes the earlier implementation, not the current product.
The integrated journaled coordinator now consumes local model-selected tools within one selected
project/task/current execution. Unity and native panels expose Request, Reply and Actions;
[workspace request verification](workspace-requests.md) records the actual authority and runtime
witnesses. The loop does not provide global workspace navigation, project creation or arbitrary
other-app access. Its reported reply is distinct from observed action receipts.

Development builds also implement explicit native [held dictation](native-panel-voice.md) through
the existing local transcription route. Focus loss, editor changes and transitions cancel capture;
background microphone continuity and release microphone support are not implemented. Neither
synthetic transcription nor the deterministic runtime witness establishes real headset speech,
model reliability or the complete other-app return journey.

## Local tool-call protocol proof

On 2026-10-12, Ollama 0.34.4 listed the already-installed `qwen3.5:9b` and
`qwen3.6:35b-a3b-nvfp4` as local models, with no `remote_host` or `remote_model` values.
The actual `/api/show` responses declared `tools` capability for both. Only the already-loaded
`qwen3.6:35b-a3b-nvfp4` was used for the initial inference check; no model was downloaded or
configured. [Ollama's tool-calling guide](https://docs.ollama.com/capabilities/tool-calling) and
[chat API](https://docs.ollama.com/api/chat) were checked against the actual round trip.

A standalone bounded probe exposed one read-only tool over synthetic task state. The model chose
that tool with schema-valid arguments, then received its result in a `tool` message with
`tool_name`, following the original assistant tool call. Its second turn identified the pending
human decision. The probe passed in 2.539 seconds: two model turns, one tool call, no action tool,
no real project data and no message text in the evidence log. Generation used `think: false`,
an 8192-token context, a 256-token output limit and temperature zero. The probe imposed a 60-second
whole-run deadline, at most 45 seconds per request, at most three model turns and a 128 KiB chat
response limit. It did not submit a Halcyonic command or alter the running control plane.

The running control plane's authenticated companion status identified `qwen3.5:9b` as its
configured local model. The same bounded synthetic probe then passed against that model in
7.354 seconds, again with two turns, one schema-valid read call and a tool-informed answer about
the pending decision. This second check changed no service configuration or project state.

This verifies one local tool/result/continuation exchange. It does not establish prompt robustness,
production request persistence, permission enforcement, mutation deduplication, runtime completion,
multi-request scheduling, voice latency or headset usability. Those remain implementation and
acceptance work; an advertised `tools` capability alone is not sufficient evidence for them.

## Design inference and validation required

A stable workspace with contextual views can make the result of speech visible and correctable.
This is a design hypothesis. A general workspace agent must compose implemented tools around the
request, inspect actual results and retain task identity. A keyword router or one model call that
selects a predetermined menu action does not satisfy the clarified requirement. Tool availability
and permissions remain explicit; open-ended reasoning cannot manufacture access or runtime support.
No model interpretation grants command authority. Ambiguous names, stale results and unsupported
requests must be handled explicitly without guessing a target or reporting an action as complete.

The relevant acceptance measurements are real headset transcription and target accuracy,
time from release to useful feedback, recovery from ambiguity, preservation of drafts and context,
and absence of actions after cancellation or connection/execution changes. Microphone behavior
beside other apps and after Unity suspension requires hardware tests. Always-listening activation,
speech output, arbitrary external-screen context and release microphone support remain separate
unproven decisions. No permissions or release build policy were changed by this reference review.

## Native held dictation

Development builds now consume the existing local transcription route from an explicit native
Hold to talk gesture. Consent, exact editor/tuple lifetime, cancellation, bounded WAV disposal and
the independent source/API evidence are recorded in [native-panel-voice.md](native-panel-voice.md).
Words remain an ordinary spoken draft for review; no model interpretation or gesture sends a
command. Release microphone permission and cross-app/device acceptance remain separate gates.
