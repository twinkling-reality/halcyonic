# ADR 0021: Speech becomes a draft, transcribed on the Mac, that the person confirms like typed text

- Status: Accepted on 2026-10-01 by the owner, with whisper.cpp as the engine.
- Date: 2026-09-30

## Context

The owner asked for voice input in the headset: hold to talk instead of typing an idea, a task, a
folder's name or an instruction on the Quest keyboard. Typing on the system keyboard works today,
and that keyboard already has a microphone button whose recognition is Meta's: online dictation is
processed by Meta and offered only in the United States, and an on-device dictation is opt-in
(Meta's help article on the Quest keyboard, read 2026-09-30).

Read in official documentation on 2026-09-30, nothing installed or run:

- Apple's SpeechAnalyzer with SpeechTranscriber (macOS 26 and later) transcribes on device; its
  models are fetched from Apple and managed by the system, outside the app's memory, through
  `AssetInventory`, in a size Apple does not document. On macOS 26 it takes PCM buffers or an
  `AVAudioFile`. Whether it needs the speech recognition authorization, and whether a command-line
  tool can reserve its assets, the documentation leaves unclear.
- whisper.cpp (MIT, Whisper weights MIT) runs on the Mac's GPU through Metal, from a model file of
  547 MiB (large-v3-turbo, q5_0) to 2.9 GiB (large-v3); its `whisper-server` listens on a port
  without authentication. No benchmark exists for this Mac's M5 Max.
- MLX Whisper runs the same models on a Python stack (mlx, torch and others).
- Meta's Voice SDK recognizes speech on Wit.ai's servers, needs internet and a Wit.ai token, and
  documents no on-device recognizer.
- Unity adds `RECORD_AUDIO` to an Android manifest whenever code uses `Microphone`;
  `RECORD_AUDIO` is a permission Meta's store reviews; Meta's focus requirement restricts hands and
  controllers and allows audio input without focus.

Measured on this Mac on 2026-10-01 with synthetic speech ([voice-transcription.md](../validation/voice-transcription.md)):
whisper.cpp with large-v3-turbo q5_0 left 4 of 104 drafts of ordinary words needing a fix against
Apple's 31, in 0.55 s against 0.26 s per clip with launch, holding about 900 MiB for that half
second; neither reached the network; Apple returned nothing for a hold with no speech, while
whisper.cpp invented a word or two. The control plane accepts JSON
bodies only, at most 1 MiB, and logs no instruction or agent text ([SECURITY.md](../architecture/SECURITY.md)).

## Decision

- **Speech becomes a draft.** A clip of the person's speech becomes text in the field it was
  spoken for, marked as heard, which the person fixes or keeps. Nothing is sent until they confirm
  through the same step typed text takes; a spoken instruction to a running agent always asks "Send
  this instruction?" first, though a typed one is sent as the keyboard closes. Voice is never wired
  to approve, deny, stop or confirm anything.
- **Transcribed on the Mac, never by a cloud service.** The headset sends one clip to the control
  plane, which hands it to a local engine and returns the draft. The engine, chosen by the
  measurement, is whisper.cpp's `whisper-cli` with the pinned large-v3-turbo q5_0 model, launched
  once per clip from a configured binary and model, in English, prompted with Halcyonic's own
  words (the product's, the runtimes' and the local models' names). It listens on no port and
  downloads nothing because someone spoke; the model is fetched only in a setup step the owner
  runs, which ends with one transcription so the first person to speak does not wait. A clip
  with no speech is answered as nothing heard, never with invented text: the control plane gates
  on loudness before launching the engine. Meta's Voice SDK is not used.
- **One bounded route, nothing kept.** `POST /api/transcriptions` accepts, from any authenticated
  principal, one `audio/wav` clip of 16-bit mono PCM at 16 kHz, 0.5 to 30 s, at most 960,044
  bytes, and answers with the text, the locale and the engine's name and version, or a reason in
  words. It is the only route that takes anything but JSON. One clip at a time per principal, six
  a minute, one transcription at a time on the Mac, 15 s for the engine. Audio and transcripts are
  never journaled, stored or logged; a temporary file, if the engine needs one, is private and
  deleted at once.
- **The transcript is untrusted text.** It is shown through the one rule for text Halcyonic did not
  write, as any text from outside is.
- **Hold to talk on the headset, development builds first.** Capture starts after the button has
  been held for 0.3 s, shows that it is listening, stops on release or at 30 s, and is discarded if
  the app loses input focus or the hand leaves the button. The microphone permission is asked on
  the first hold, never at launch, and typing always remains. Release and demonstration builds
  carry no voice and no `RECORD_AUDIO` unless the owner decides otherwise.

## Alternatives considered

- **Apple's SpeechTranscriber through a small helper.** Twice as fast, light, silent when nothing
  is said, and it needed no app bundle or authorization in the trial; but it got ordinary words
  wrong in about a third of the drafts, and a hint of Halcyonic's own words changed nothing.
- **The Quest keyboard's own dictation.** Exists today and needs no work, but it is not hold to
  talk, its online recognition is Meta's and United States only, and Halcyonic cannot see or
  control it. It stays available beside this.
- **Meta's Voice SDK.** Sends the person's audio to Meta, needs internet and an account token.
- **Recognition on the headset.** No on-device recognizer is documented for Unity on Quest beyond
  the keyboard's; running a model there would cost the frame budget the stage needs.
- **`whisper-server` on a local port.** It has no authentication; a port any local process can
  reach would bypass the control plane's bounds.
- **Base64 audio inside JSON.** Keeps one content type, but a 30 s clip grows past the 1 MiB body
  limit; raising that limit for every route is worse than one route taking `audio/wav`.
- **Streaming partial results over the realtime socket.** Faster feedback, but messages are capped
  at 256 KiB and the socket carries journaled state; a short clip and one answer are enough for
  fields of a sentence or two.

## Consequences

- A person can speak where they typed, with the same confirmation, and nothing they say leaves
  the Mac or is kept.
- The control plane gains a non-JSON route with its own parser and limits, an engine process to
  launch, pin and stop, and a setup step that downloads speech assets or a model, which the owner
  approves.
- The Unity client gains a microphone permission flow, a hold gesture on `PanelButton`, a capture
  and resampling path in the client core, and a build-time guard that keeps `RECORD_AUDIO` out of
  release builds.
- Lane F's focus rules decide when capture may run; this decision only stops capture when focus
  is lost.
- Revisit when real clips from the headset are measured, if whisper.cpp still invents text in a
  real room once gated (voice activity detection needs a further model the owner approves), if
  voice is wanted in the release build (store review of `RECORD_AUDIO` and a privacy policy), or
  if people want to speak beyond about 30 s.
