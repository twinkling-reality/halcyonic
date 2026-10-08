# Meta XR Audio's spatializer for the stage's sounds

- **Question:** Can the stage's cues go through a head-related (HRTF) spatializer, Meta XR Audio's,
  under terms the project already carries, without a permission, manifest entry or network call the
  release build would not otherwise have, and at what cost in size? (Lane V's brief of 2026-10-08:
  keep it only if it is clearly better by ear than Unity's panning and under 1 ms of CPU a frame.)
- **Date:** 2026-10-08.
- **Environment:** `main` at 95602300 on an Apple M5 Max with macOS 26.7; Unity 6000.3.25f1 in batch
  mode with the Android target; `com.meta.xr.sdk.audio` 85.0.0 from Meta's registry, beside
  `com.meta.xr.sdk.core`, `interaction`, `interaction.ovr` and `mrutilitykit` at 207.0.0. No
  headset.
- **Method:** The package's tarball (13,655,136 bytes, SHA-256
  `dbd4f0e8b925292ade7706d2b37900f761ce2a196d212709a80fc82bc8d76e0b`) read as data before anything
  was installed: its licence, notices, manifest pieces, editor scripts, and the Android library's
  exported symbols and strings (`llvm-nm`, `strings`). Then the package in the project: `SoundCheck`
  (both settings), `WorkspaceRender`, and release APKs built with `BuildReleaseApk` before and
  after,
  read with `aapt2`.
- **Status:** Off the device. By ear, the turn-to-sound check against panning and the CPU and audio
  DSP cost wait for the headset session (HEADSET_SESSION.md, check 11, step 9).

## The licence

- `LICENSE.md` is byte for byte the one `com.meta.xr.sdk.core` 207.0.0 carries: the Oculus SDK
  License Agreement. `package.json`'s `licenseMessage` is the same sentence as core's.
- `THIRD_PARTY_NOTICES.txt` lists Kiss FFT, libjpeg, OOURA FFT, Pretty Fast Fourier Transform
  (PFFFT), sfft, the Steinberg VST Plugin SDK, tinythread++ and zlib, most as "may be included".
  The Android library (`libMetaXRAudioUnity.so`, arm64) exports PFFFT's functions, which its HRTF
  code calls. It holds no trace of libjpeg (no `jpeg_` symbol, no "JFIF", no IJG text) or of VST,
  and none of the others' names or zlib's strings; a stripped static copy cannot be ruled out.
- So the project reproduces PFFFT's notice for certain and the other BSD and zlib ones
  conservatively, in `apps/xr/Licenses/MetaXRAudio-THIRD_PARTY_NOTICES.txt`, named in `NOTICE`.
  libjpeg's notice, whose condition is a sentence crediting the Independent JPEG Group when its
  binary ships, is left out, since it does not ship in the Android library; so is VST's trademark
  line. The coordinator accepted the notices on 2026-10-08.

## What it adds to the release build

- No `.aar`, `.jar` or manifest fragment, and no editor script that touches the Android manifest or
  Gradle. The release APK's permissions and manifest after: identical to the release APK before
  it, as `aapt2` reads both (permissions, features and the whole manifest tree), and
  `BuildReleaseApk`'s own checks passed.
- The Android library: arm64 2,705,728 bytes (the armeabi-v7a copy is not built, since the project
  builds arm64 only).
- The release APK: 72,136,713 bytes before (SHA-256
  `602dbfbdc13405262fa45ad0c3d1dd71d742f21be9e0ffebdc2dd4cd7b05ad11`, at 95602300) and 73,935,715
  after (SHA-256 `8a4b357a5955ab7fb5d79644a67da75d4ac799779ddd16a2c6d702bbb64be63c`), 1,799,002
  bytes
  more. Uncompressed, it gains `libMetaXRAudioUnity.so` (2,705,728 bytes), 408,448 bytes of
  `libunity.so` (the Terrain module's native code) and 83,280 of `libil2cpp.so`; the managed
  assemblies `Meta.XR.Audio`, `UnityEngine.TerrainModule` and `UnityEngine.TerrainPhysicsModule`
  join the build.
- **Telemetry.** The library calls Horizon OS's `com.oculus.os.UnifiedTelemetryLogger` and
  `AnalyticsEvent` over JNI (`MetaXRAudio::initializeTelemetry` and `shutdownTelemetry`), and no
  script or setting turns that off. It hands events to the headset's system service and opens no
  connection itself: no URL or network call appears in the library or the scripts. The release build
  already reports through two Meta paths, the Core SDK's `OVRPlugin.SendUnifiedEvent` (MRUK's scene
  events) and the Interaction SDK's `libISDKEngineTelemetry.so`; this is a third
  ([competition-judge-build.md](competition-judge-build.md), "What the release build reports"). The
  coordinator accepted it on 2026-10-08, with the owner's leave.

## In the project

- **It needs Unity's Terrain module.** `MetaXRAcousticGeometry.cs` uses `UnityEngine.Terrain`, and
  the package does not declare the module, which this project had never included: the first import
  failed with `CS1069` ("forwarded to assembly 'UnityEngine.TerrainModule'"). The manifest now
  includes `com.unity.modules.terrain`; its cost is in the release APK's size above.
- **The version lines are separate.** The audio package's numbers (85.0.0, published 2026-02-10)
  run apart from the Core SDK's (207.0.0), and it declares no dependency on the Core SDK; it states
  Unity 2022.3.15f1 or later. With the Terrain module it compiles and `SoundCheck`,
  `WorkspaceRender` (both text sizes) and the release build pass beside the 207.0.0 packages, in
  the editor only; on the device it is unproven until the headset session.
- **The editor's plugin updater** (`MetaXRAudioPluginUpdater`, `[InitializeOnLoad]`) returns at
  once in batch mode (`Application.isBatchMode`). In an interactive editor it reads and writes an
  editor preference and renames the Windows plugin inside the package's own folder, as its source
  reads; it names no path in the project. The first batch open wrote, outside Unity's own folders, only
  `packages-lock.json` (the Terrain module's entry) and the `.meta` files of the new
  `Sound/Editor` folder; nothing else in the project or under `Assets/`.

## How the stage uses it

- `AudioManager.asset` names "Meta XR Audio" as the spatializer, the effect name the library
  registers. A source goes through it when `AudioSource.spatialize` is on.
- `StageSound.Configure` turns it on for every point source (a character's voice, the person's
  actions, a button's tap) and leaves it off for Last known, whose 60 degree spread only Unity's
  panning keeps. Everything else is as before: `spatialBlend` 1, no Doppler, logarithmic rolloff at
  full level within 2.5 m and no quieter beyond 25 m, the level, the audio-clock schedule.
- The package documents its own inverse-square attenuation as unused (`P_USEINVSQR`, "[UNUSED]",
  off by default), so distance is Unity's curve either way; and its room acoustics (early
  reflections and reverberation) need its reflection effect on an audio mixer, which the stage does
  not use. Both are read from the package's documentation, not measured; the headset check hears
  the result.
- One setting chooses: the file `sound-panned` in the app's data directory turns the spatializer
  off, read at start, for comparing the two by ear; without the spatializer loaded the panning is
  used anyway. `StageSound.HeadRelated` decides it and the start's `Halcyonic: sound placed by ...`
  line says which. `SoundCheck` checks both settings' sources and the choice.

## Consequences

- The stage's point cues are spatialized with HRTF in builds from this change, with today's panning
  one file away.
- Whether it stays is the headset's to say: by ear against panning, and its CPU and audio DSP cost
  with six characters, under 1 ms a frame.
- The release build carries a third Meta telemetry path and Unity's Terrain module.
