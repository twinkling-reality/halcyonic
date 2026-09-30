# Sound rendering

- **Question:** Does the C# port of the Glaze direction render what the owner heard on the
  soundbook page, and what do rendering every cue at startup and keeping the clips cost?
- **Date:** 2026-09-29.
- **Environment:** an Apple M5 Max (18 cores) with macOS 26.7; .NET SDK 10.0.401; Unity
  6000.3.25f1 in batch mode for Android, whose editor runs scripts on Mono; Node 24.15.0 running the
  page's own synthesis code. Other sessions' builds and tests ran throughout, with load averages
  from 70 to 158, so the times are upper bounds for this machine. No headset.
- **Method:**
  - The page's synthesis code, run in Node, wrote each Glaze render's dry mix and its send to the
    room, and the room's first channel, as raw 32-bit samples at 48 kHz and at 44.1 kHz; the port
    rendered the same, and the two were compared sample by sample.
  - `GlazeSynthesizer.RenderAll` at 48 kHz, timed three times in one .NET process built for
    release, and three times in the editor by a temporary editor script, not kept, which also made
    each clip an `AudioClip` (`AudioClip.Create`, `SetData`) and summed
    `Profiler.GetRuntimeMemorySizeLong` over them.
  - Each played clip's spectral centroid, power-weighted over 2048-sample Hann frames on a 512 hop,
    as the soundbook's own check measured it.
- **Status:** Verified on the development Mac, in .NET and in the editor. Not verified on a Quest 3.

## Findings

- **Fidelity.** At 48 kHz, of 3,950,400 dry samples across the 79 renders, 3,950,399 are
  bit-identical to the page's; the other differs by 1.3e-16 of its render's peak. At 44.1 kHz all
  3,629,430 are. The room's samples are identical at both rates. Samples are 32-bit, as on the
  page, and every step keeps the page's order of operations, its seeded generator and its rounding
  of halves up. A test compares every render's length, peak and energy with the page's own figures.
- **The room.** Convolving each cue's send with the 1.3 s room dominated the first version: one
  transform per clip, of up to 262,144 points, with fresh buffers each time, took 5.2 s for all
  clips in .NET. Overlap-add with one transform size (131,072 points at 48 kHz), two segments per
  complex transform (one as its real part, one as its imaginary part), reused buffers and
  precomputed twiddle factors and bit reversals cut that to the times below.
- **Render time.** All 79 clips (13 cues for each of the 6 notes, and Last known once): 1.2 to
  2.0 s in .NET 10, of which the dry synthesis took 0.5 to 1.2 s; 1.9 to 2.0 s in the editor.
  Making them audio clips on the main thread took 2 to 7 ms for all of them.
- **Size.** 4,644,319 samples, 17.7 MiB as 32-bit floats. In the editor the clips held 17.8 MiB,
  4.02 bytes per sample, as decompressed PCM (`DecompressOnLoad`, loaded).
- **Calm.** Centroids of the played clips: 350 Hz on average, 649 Hz at most (Open, for the highest
  note). The room adds up to 2.5 LU of loudness to a cue's dry target, as it did on the page, and no
  played sample exceeds 0.41.
- **Unity's settings.** The editor reported `AudioSettings.outputSampleRate` as 48,000 Hz, the rate
  the clips are rendered at; the Quest 3's is to be read from the log. The project's audio manager
  has 32 real voices, output suspension on, and no spatializer plugin.

## Consequences

- Rendering at startup on a worker thread is affordable on the Mac, and nothing is synthesized while
  sound plays. On a Quest 3, whose cores are slower and whose build compiles scripts ahead of time,
  the time is unmeasured: `StageSound` logs it (`Halcyonic: sound ready: ...`). If it proves too slow
  there, the clips could be rendered once at build time instead, since they depend only on the
  sample rate.
- 17.7 MiB of clips is kept for the whole session.
- Relied on and not yet seen on a headset: `AudioSource.PlayScheduled` keeping its timing on
  Android, also after output suspension let the output idle; `spread` widening a mono clip, as it
  does a stereo one; focus changes from the system menu and Virtual Display reaching
  `OnApplicationFocus`. The checks are in [XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md), under
  "Sound checks on a Quest".
