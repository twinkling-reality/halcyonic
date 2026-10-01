# Voice transcription on the Mac

- **Question:** Which local engine should turn a held clip of speech into a draft
  ([ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md)): Apple's
  SpeechTranscriber through a small helper, or whisper.cpp with the large-v3-turbo q5_0 model? How
  accurate, fast and heavy is each on this Mac, does either reach the network, and what does each
  return when nothing is said?
- **Date:** 2026-10-01.
- **Environment:** an Apple M5 Max (18 cores) with 64 GB and macOS 26.7 (25G229); Xcode 26.6 with
  Swift 6.3.3; Python 3.14.7; ffmpeg 8.1.1. The Mac's locale is `en_US` (languages en-US, ko-US).
  Other projects' test suites ran throughout, at a load average of 20 to 33, so the engines took
  turns clip by clip and saw the same load.
- **Method:** synthetic speech made with macOS `say`, each clip run through each engine in a fresh
  process, as the control plane would launch it, and again in one warm process per engine. The
  engines, the model, the clips and the scripts live in `~/.halcyonic/speech/`, outside the
  repository. No hosted model or service was called.
- **Status:** Runtime verified on this Mac with synthetic voices. Not checked with people's voices,
  the Quest's microphone or a room; real clips come at a headset session.

## The engines

**Apple SpeechTranscriber.** A trial helper of about 110 lines of Swift, built with `swiftc -O` and run as a plain
command-line binary: no app bundle, no `Info.plist`, no authorization request. It asks for
`SpeechTranscriber.supportedLocale(equivalentTo: Locale.current)`, which gave `en_US`, and uses the
`.transcription` preset and `SpeechAnalyzer.analyzeSequence(from:)` on the WAV file.

- Setup: `AssetInventory.reserve(locale:)`, then `assetInstallationRequest(supporting:)` and
  `downloadAndInstall()`, took 6.8 s. The status went from `supported` to `installed`.
- What setup downloaded: nothing measurable. The English asset was already on the Mac, installed
  by the system on 2026-09-19, and its folder did not change size.
- The asset: `com.apple.speech.asr.transcription.en`, version 3100.44005.81730 (build 13M302153).
  The system's catalog lists a 132 MiB download and 196 MiB unpacked.
- The model sits in a cryptex image, `UC_SPEECH_ASR_TRANSCRIPTION_EN_GENERIC_H17SCD_Cryptex.dmg`:
  205,520,896 bytes, SHA-256 `7002dcf00a45a8d4830b9a640fe1a8770b8c536a7828d4a5df97409fbf5ca9b8`.
  The asset's other files are tickets personal to this Mac, so its version and that image pin it.
  Apple, not Halcyonic, decides when it changes.
- No prompt appeared. It ran from a terminal inside the Claude app; whether a control plane
  started by launchd gets the same was not checked.

**whisper.cpp 1.9.4** (MIT), built from source with CMake, Release, static, Metal and Accelerate,
only `whisper-cli`:

- Source: `https://github.com/ggml-org/whisper.cpp/archive/refs/tags/v1.9.4.tar.gz`, 9,353,438
  bytes, SHA-256 `57e280cee375ab02425b806ad5146b99f6eb9357e3c2b31357c8a6af2e2e44ae`, the hash
  Homebrew's formula pins.
- Binary: reports `1.9.4-dev` because it was built from the archive, not a git checkout.
  4,606,840 bytes, SHA-256 `7ee0ac7d7f23c587d68d04444cf979376a6b9d4f81ceee7840c549e447ba897e`.
  It links only system frameworks.
- Model: `ggml-large-v3-turbo-q5_0.bin` from `huggingface.co/ggerganov/whisper.cpp` at commit
  `5359861c739e955e79d9a303bcbc70fb988958b1`. 574,041,195 bytes, SHA-256
  `394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2` (as Hugging Face lists),
  SHA-1 `e050f7970618a659205450ad97eb95a18d69c9ee` (as whisper.cpp's `models/README.md` lists).
- Run as `whisper-cli -m <model> -l en -nt -np -f <clip>`. Defaults otherwise: beam search of 5,
  4 threads, flash attention, the GPU through Metal.
- Language: `en`. Whisper takes a language, not a region.

## The clips

29 utterances in three groups:

- **Ideas (8):** "A website for my bakery that shows the menu and the opening hours.", "A small
  app that tracks how much water I drink each day.", "A recipe book where my family can add their
  own recipes.", "A tool that renames my holiday photos by the date they were taken.", "A landing
  page for a weekend yoga retreat in the mountains.", "A budget tracker that reads my bank
  statements and groups my spending.", "A game where you guess the capital city of each
  country.", "A dashboard that shows the weather and my calendar on one screen."
- **Instructions (13):** "Add a contact form to the home page and run the tests.", "Use TypeScript
  instead of JavaScript for the new files.", "Stop changing the database schema and ask me
  first.", "Write a README that explains how to install and run it.", "Make the buttons larger
  and use a darker blue for the header.", "Fix the failing test in the login module before
  anything else.", "Install the dependencies with pnpm, not npm.", "Don't deploy anything yet.
  Just show me the plan.", "Rename the function get user to fetch user everywhere.", "Limit the
  list to twenty items per page.", "Switch the model to Qwen running on Ollama.", "Commit the
  changes to a new branch called landing page.", "Open Halcyonic and show me what Codex is doing."
- **Folder names (8):** bakery website, water tracker, family recipes, photo renamer, halcyonic
  demo, yoga retreat landing page, capital quiz, budget tracker two.

How the clips were made:

- Each utterance is spoken three times: at 185 words a minute (normal), 150 (slow) and 230
  (fast). The voice rotates through Samantha (en_US), Daniel (en_GB), Karen (en_AU), Moira
  (en_IE), Rishi (en_IN), Tessa (en_ZA), Reed (en_US) and Flo (en_US).
- The normal rendition is spoken once more with seeded noise from ffmpeg's `anoisesrc`, pink at
  10 dB SNR or brown at 5 dB, alternating.
- That makes 116 clips. Each has 0.4 s of silence before and 0.6 s after, and runs 1.7 to 5.9 s
  (3.7 s on average).
- The format is the route's, 16 kHz mono PCM16, straight from `say` with
  `--data-format=LEI16@16000`.
- Reproducibility: the set reproduces bit for bit from the second generation on; the SHA-256 of
  its manifest is `330bc43a49989b61f26daab7a47161a908e7dfeeecb662b4450b9ce2b2dbe3ac`. The first
  generation on this Mac differed in 25 clips, all from five of the voices, likely voices loading
  on first use.
- Synthetic voices are cleaner and more regular than a person speaking into a headset, so these
  figures are a best case for both engines.

**Scoring.**

- **Word error rate** is measured after lowercasing, removing punctuation and hyphens, and
  spelling digits as words.
- **Needs a fix** means the draft still differs from what was said once spaces and ordinals
  written as digits are also ignored. "homepage", "1st" and "getUser" therefore count as right:
  the person would keep them.

## Accuracy

| | Apple SpeechTranscriber | whisper.cpp |
| --- | --- | --- |
| Word error rate, all 116 | 9.1% | 5.1% |
| Drafts needing a fix, all 116 | 43 | 16 |
| Ideas (32) | 13 | 1 |
| Instructions (52) | 19 | 10 |
| Folder names (32) | 11 | 5 |
| Without Halcyonic, Qwen and Ollama (104) | 31 | 4 |
| ... normal / slow / fast (26 each) | 8 / 5 / 6 | 1 / 0 / 0 |
| ... pink noise at 10 dB / brown at 5 dB (13 each) | 9 / 3 | 2 / 1 |

Apple's mistakes are mostly ordinary words:

- "shows the menu in the opening hours" and "the weather on my calendar", for "and".
- "A small lab that tracks our water", "Water cracker", "Yogurt 3 landing page" and "Budget
  tracker too".
- "Install the dependencies with PNPM, not 2 PM" and "Make the button larger, a new, a darker blue
  for the air".

whisper.cpp's mistakes on ordinary words were these four:

- "re-adm" for README, twice.
- "The recipe book" for "A recipe book", once.
- "Budget tracker pool", once.

Its other differences are formatting a person would keep: "getUser to fetchUserEverywhere".

Neither engine knew Halcyonic, Qwen or Ollama: "whole psionic", "Quinn running on Olima", "Hall
Psyonic", "when running on Olimar".

**A hint of Halcyonic's own words.** Both engines were given the words the control plane itself
knows: the product's name, the runtimes' names and the local model's provider and family
(Halcyonic, Codex, OpenCode, Claude Code, Ollama, Qwen). No word from the clips' other sentences
or folder names was included.

- Apple: changed no clip, whether the `AnalysisContext` with these `contextualStrings` was set
  before analysis or given when the analyzer was made.
- whisper.cpp, given them as `--prompt`: got Qwen and Ollama right in three of four clips and
  came closer on Halcyonic, never exactly ("Hallcyonic", "OpenLcyonic"). The word error rate fell
  to 4.2% and the drafts needing a fix to 13, and no clip that was right became wrong.

## Latency

Launch to text, a fresh process per clip:

| | Apple | whisper.cpp |
| --- | --- | --- |
| Median, 90th percentile, slowest (116 clips) | 0.26, 0.30, 0.45 s | 0.55, 0.62, 0.80 s |
| A 29.1 s clip, three runs | 0.70 to 0.84 s | 0.75 to 0.90 s |
| Warm, every clip in one process, per clip | 0.10 s | 0.22 s |

- **whisper.cpp's first run:** the very first run after building took 23.5 s; every later one
  took about 0.5 s. That is most likely Metal compiling ggml's shaders once and caching them
  (inference, not checked). Whether a restart or a macOS update brings it back was not checked.
- **The long clip:** both engines stay far inside the route's 15 s budget. whisper.cpp's
  transcript of it was word for word; Apple's dropped the first word and missed four others.
- **Warm runs:** both gave the same text warm as in a fresh process, for every clip.

## Memory

| | Apple | whisper.cpp |
| --- | --- | --- |
| Process launched, per clip | 6 MiB peak footprint, 19 MiB resident at most | 922 MiB peak footprint (median; 926 MiB at most), 833 MiB resident at most |
| Elsewhere | `localspeechrecognition`, a Speech framework XPC service, up to 87 MiB resident while transcribing, and the Neural Engine daemon `aned`; the service exits after | Nothing; the memory is held only for the half second it runs |

## Network

`lsof` sampled every 0.1 s while each engine transcribed all 116 clips, covering the helper,
`localspeechrecognition` and `whisper-cli`. None of them had an internet socket.

## When nothing is said

A hold with no speech, as silence and as seeded steady noise, of 1, 3 and 10 s:

| | Apple | whisper.cpp | whisper.cpp with the hint |
| --- | --- | --- | --- |
| Silence | nothing | "you" | "Thank you." |
| Pink noise | nothing | "Thank you.", ".", "so" | "Thank you.", "Qwen." |
| Brown noise | nothing | "Thank you.", "Okay.", "so" | "Qwen." |

whisper.cpp invents text when nothing is said; Apple does not. Two ways to stop that:

- **Voice activity detection.** whisper.cpp 1.9.4 offers it (`--vad --vad-model`). It needs a
  Silero model, `ggml-silero-v6.2.0.bin` from `huggingface.co/ggml-org/whisper-vad` (MIT, 885,098
  bytes, SHA-256 `2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987` as listed, at
  commit `9ffd54a1e1ee413ddf265af9913beaf518d1639b`). It was not downloaded, because it was not
  among the downloads approved for this trial.
- **A loudness gate**, with no download. Each 30 ms frame's level is taken; the spread is the
  95th percentile level minus the 10th:
  - Every speech clip spread at least 9.5 dB, including those at 5 dB SNR.
  - Steady noise of any colour and level spread at most 5.9 dB.
  - Silence has no level at all.
  
  Real rooms are not steady (keys, voices, a door), so the gate catches silence and steady noise
  only.

## Consequences

- **Engine: whisper.cpp, large-v3-turbo q5_0.** On the words people use it is far more accurate:
  4 of 104 drafts to fix against 31, and 1 of 32 ideas against 13. Apple's advantages are 0.3 s
  and about 900 MiB that whisper.cpp holds for half a second. Both stay well inside the 15 s
  budget, and neither reaches the network.
- **`whisper-cli` is launched once per clip, from a configured binary and model,** with
  `-l en -nt -np`. No server listens on a port.
- **The prompt names Halcyonic's own words.**
- **Nothing said is answered as nothing heard,** never with invented text. The control plane
  gates on loudness before launching the engine, and voice activity detection is added if its
  model is approved.
- **Setup runs one transcription after installing,** so the first person to speak does not wait
  for the shaders.
- **The Apple helper is not built.**

**Open:**

- Accuracy with people's voices and the Quest's microphone, which may apply its own gain and noise
  suppression.
- The gate's threshold on real clips.
- Whether whisper.cpp still invents text in a real room once gated.
- Whether a restart or a macOS update makes the first transcription slow again.
