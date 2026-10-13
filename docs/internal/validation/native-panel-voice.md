# Native panel held dictation

- Date: 2026-10-13.
- Question: Can the native hybrid panel add speech to an ordinary draft through the existing local
  transcription endpoint, retaining its current task and review authority?
- Environment: Java 11 source compiled against Android API 36 from Unity 6000.3.25f1's bundled
  Android toolchain. No Unity player, microphone, headset, live speech engine or provider was run.
- Status: Source, Android API compilation, JVM and ephemeral-host regressions verified. Hardware behavior and release suitability remain unverified.

## Verified platform facts and product decision

[Meta's review-requiring permissions](https://developers.meta.com/vr/resources/permissions-review-required/)
expressly permits `RECORD_AUDIO` for microphone-dependent features. It is not a prohibited
permission. [VRC.Quest.Security.2](https://developers.meta.com/vr/resources/vrc-quest-security-2/)
requires a justification and graceful handling of runtime denial for both immersive and panel apps.
[VRC.Quest.Input.4](https://developers.meta.com/vr/resources/vrc-quest-input-4/) concerns immersive
hand/controller focus and explicitly permits audio input without focus. Halcyonic deliberately
cancels dictation when its originating surface loses focus; that is a product/security choice.
ADR 0021's development-first release microphone exclusion remains in force.

[Android's AudioRecord API](https://developer.android.com/reference/android/media/AudioRecord)
supports mono PCM16 reads but does not guarantee 16 kHz on every device. The native consumer checks
initialized state and actual rate; failure explains unavailability rather than inventing a fallback
format. [Runtime permissions](https://developer.android.com/training/permissions/requesting) are
requested in context and rechecked; the first permission gesture records nothing and grant requires
a new hold. [Audio input sharing](https://developer.android.com/media/platform/sharing-audio-input)
can silence ordinary apps competing for the microphone. Privacy-sensitive capture limits concurrent
capture; these documented Android rules do not prove Quest behavior alongside another app.

[Meta's voice guidance](https://developers.meta.com/vr/design/voice-technology/) requires visible
microphone status while collecting audio. [Privacy.2](https://developers.meta.com/vr/resources/vrc-quest-privacy-2/)
covers processing as well as storing data. Transient audio/transcription processing and retained
spoken drafts are different lifetimes: audio is in memory, host temporary audio is private and
removed, while an accepted transcript becomes a deliberately retained local draft and can later
be explicitly submitted. This record does not claim dashboard/store acceptance or change a policy.

## Consumed implementation and checks

`PanelVoice` is the actual Activity's gesture/lease/WAV/response/append helper. `PanelVoiceCapture`
records initialized 16 kHz mono PCM16 on a separate worker with 8,000–480,000 samples and a
30-second wall limit, stopping/releasing the recorder on release, cancellation and failure. Only
the debuggable non-preview Activity exposes it. Existing Unity development microphone inclusion
provides the development permission; the native Java source adds no permission to the manifest and
the release APK manifest guard still refuses it.

`PanelTransport.transcribe` shares the audited pin/proof, exact pairing checks and socket cancellation
boundary. Its only route is `/api/transcriptions`, only body is canonical WAV up to 960,044 bytes,
and only response is bounded JSON. It uses a 30-second total deadline including response reads,
without the ordinary command path's 4-second idle cutoff. JSON command size/allowlist/deadline
remain unchanged. Ordinary polling continues while microphone/transcription work is in flight.
Delivery requires current pairing, fresh matching task context and unchanged Activity/editor lease;
editing or any observed lifetime loss permanently cancels it. Heard words append locally with
spoken origin, through existing draft retention. Existing reviewed send routes receive no voice
callback and no command is fabricated.

Compile-only check passed: bundled `javac --release 11 -cp <AndroidPlayer>/SDK/platforms/android-36/android.jar`
over every production and development panel Java source, in an automatically cleaned temporary
classes directory. The compiler reported only the Activity's deprecated API note.

The Activity owns the pending WAV and cancels its transport before wiping it, including when
executor shutdown removes a queued upload without executing its cleanup. Rejected nonnull capture
results and completed buffers are also wiped. A consumed regression verifies this never-run queue
case, not merely worker-finally disposal.

The focused regression suite consumes these actual helpers, not copied implementations. It covers
all tuple fields, pairing replacement, Activity focus/top-resume/pause/modal epochs, editor/route,
starting words, request references, generation and eligibility revocation without revival; pointer
maturity/second-pointer/cancel; exact minimum/maximum PCM/WAV output and bounds; strict transcripts
and append overflow/no-speech preservation. An ephemeral real control plane with a scripted speech
engine exercises development USB proof and actual `audio/wav` parsing, checking canonical audio,
unchanged journal and no command. Pinned transport cases cover wrong pin/rotated pairing, early
oversize refusal, cancellation, a response beyond the command idle timeout and redirects/no retry.

Regression execution: `node --test --test-reporter=spec tooling/panel/panel.test.ts` passed
84/84 tests, one suite, zero failures/cancellations/skips/todos, in 31.6 seconds on the final source. The suite compiled
both production and development source sets against Android API 36. The five new voice cases
passed, including canonical maximum audio through the real host route and the 4.2-second response
that distinguishes transcription from the ordinary command idle deadline. No skipped result is
treated as a pass. Full repository and C# validation belong to the integration gate; they were
not run by this focused lane.

## Remaining device checks

No real Quest microphone, permission prompt, room speech, native hand pointer or another-app
microphone arbitration was tested. Verify first permission/denial/revocation, real hold and target
exit/cancel, rate/level and no-speech behavior, both 30-second limits, panel focus beside another app,
panel↔immersive switches during capture/upload/result, concurrent edits, process recreation with
retained spoken text and absent capture/confirmation, and no release microphone permission. Store
justification and published processing/draft-retention disclosures require separate verification.


## Integrated Android packaging (2026-10-13)

The integrated `32169294` development and release Android builds succeeded after the compact
panel change. Development APK: 114,630,718 bytes, SHA-256
`54c02db2823984967181d6561ef1ca465221903ada9c1c6639af826c322cf296`.
Release APK: 74,426,505 bytes, SHA-256
`9b72fc98263c846b91f80c46e6cb6b35857176082e097fa07f5edd7984056917`.
Actual DEX inspection found the production Activity, voice lease/capture and UI authority classes
in both. The development manifest includes microphone permission and the synthetic preview;
the release omits microphone and media-projection service permissions, the preview Activity/class
and its exact asset. Neither requests all-package visibility. Presence of shared voice code in
release does not enable recording: the existing development guard and permission exclusion remain.
The full integrated repository check passes 1,172 tests with no failures or skips, type checking
and lint included. These packages were archived, not installed; they precede the separate installed
app-link and optional character-feature candidates. Hardware audio/focus/IME and wearer acceptance
remain outstanding.


## Integrated Results and character package follow-up (2026-10-13)

Both Android builds succeeded at `61646671`, after the Results, character and focused-page fixes.
The development APK is 114,691,807 bytes, SHA-256
`ba430286db18e5ce09544659c55c3bea4aaf3358796f52397c7a08b5e400e785`.
The release APK is 74,532,745 bytes, SHA-256
`dab14de1773490f44d4dfd6e6fccb0d0a236b6e07ef62ac87dfb8edaa803627f`.
Manifest and DEX descriptor inspection retained the native panel, voice and state code. Development
retained its preview Activity, preview asset and microphone permission. Release excluded all three,
the preview DEX descriptor and the media-projection service permission. Neither requested
`QUERY_ALL_PACKAGES`; the release DEX also retained the Results consumer descriptor. Descriptor
presence is packaging evidence, not an Android behavior test.

The development package was installed with replacement while preserving app data. The installed
base APK's independently read SHA-256 matched the built artifact exactly; the previous installed
APK was preserved before replacement. Explicit Unity and native Activity launch requests were
accepted. Unity then lost focus and paused before a live connection was observed. This does not
establish a crash, readable rendering, voice, private-state recovery or wearer acceptance. Those
remain open device gates. No release package was installed.
