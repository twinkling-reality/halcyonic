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

## Halcyonic source facts

`MenuVoice` binds one recording to the column that started it. Its captured delivery callback
checks the recording generation and column identity. Leaving the column or losing focus drops
the recording or pending result. File editing also binds the execution and input epoch, and a
spoken instruction remains a draft for explicit review and Send.

The authenticated `/api/transcriptions` route uses the configured local whisper.cpp engine.
[voice-transcription.md](voice-transcription.md) records synthetic speech measurements and their
limits. Those numbers are not a measurement of natural-language intent resolution or real Quest
microphone accuracy. The existing local companion handles project creation; no workspace intent
resolver or voice navigation contract exists at this review.

## Design inference and validation required

A stable workspace with contextual views can make the result of speech visible and correctable.
This is a design hypothesis. Contextual interpretation must select from implemented views and
actions, retain task identity and distinguish a navigation request from a draft instruction.
No model interpretation grants command authority. Ambiguous names, stale results and unsupported
requests must be handled explicitly without guessing a target or reporting an action as complete.

The relevant acceptance measurements are real headset transcription and target accuracy,
time from release to useful feedback, recovery from ambiguity, preservation of drafts and context,
and absence of actions after cancellation or connection/execution changes. Microphone behavior
beside other apps and after Unity suspension requires hardware tests. Always-listening activation,
speech output, arbitrary external-screen context and release microphone support remain separate
unproven decisions. No permissions or release build policy were changed by this reference review.
