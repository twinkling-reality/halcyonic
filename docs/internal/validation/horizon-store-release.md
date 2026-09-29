# Meta Horizon Store release build

- **Question:** What does the Meta Horizon Store require of a Quest 3 build uploaded to a release
  channel (the competition entry goes to one named "Competition"), and does the release APK meet
  it without the development tools that Meta's SDK puts in a development build?
- **Date:** 2026-09-29.
- **Environment:** Unity 6000.3.25f1 on macOS 26.7 (Apple M5 Max) with its own Android SDK:
  build-tools 36.0.0 (`aapt2`, `apksigner`) and platforms 34 to 37; OpenXR 1.18.0; the Meta XR
  Core and Interaction SDKs 207.0.0.
- **Method:** Meta's developer documentation, read on 2026-09-29, with each page's own
  last-updated date below; the Meta XR Core SDK's editor code in the package cache; three APKs
  built in batch mode with `QuestBuild`: a development APK from `main` (4008655) as the baseline,
  then a release and a development APK with this change. They were compared with
  `aapt2 dump badging`, `aapt2 dump xmltree`, `aapt2 dump permissions`, `apksigner verify`, their
  file lists and Unity's player data, and searched for this Mac's LAN address and the dev agent's
  token without printing either. A throwaway macOS player showed what Unity does with the startup
  hooks of an assembly left out of a build. Nothing was uploaded and nothing was installed on the
  headset.
- **Status:** Requirements verified in Meta's documentation. The release APK meets every packaging
  requirement checked here except signing, which needs the owner's release key; whether the store
  accepts the Meta VR Glasses identifier is open. Not verified: the upload validator's verdict, and
  either APK on the headset.

## Store requirements

Meta's release channel page says that every upload, even an alpha build, must meet the release
packaging requirements, so they apply to a "Competition" channel as they do to production.

| Requirement | Source | Release APK |
| --- | --- | --- |
| `targetSdkVersion` 34 for apps created since 2026-03-01, enforced at upload; 32 to 34 accepted for immersive apps | [Manifest][manifest]; [Android 14][android14] | 34 |
| `minSdkVersion` 32 recommended, 29 to 34 accepted | [Manifest][manifest] | 32 |
| `compileSdkVersion`, when listed, at least the target | [Manifest][manifest] | 34 |
| `android:debuggable` false or unset: a release build | [Manifest][manifest] | unset |
| `installLocation` auto | [Manifest][manifest] | auto |
| Head tracking feature, required, version 1 | [Manifest][manifest] | yes |
| MAIN and LAUNCHER on the launch activity, and `com.oculus.intent.category.VR` for OpenXR apps | [Manifest][manifest] | yes |
| `excludeFromRecents` on the launch activity; a unique `android:label` | [Manifest][manifest] | true; Halcyonic |
| `com.oculus.supportedDevices` with the identifiers quest2, questpro, quest3, quest3s | [Manifest][manifest] | those four and `stanley` (below) |
| APK Signature Scheme v2 (VRC.Quest.Packaging.2); v1 or v2, v3 allowed; the developer's own certificate, the same for every update | [Requirements][req]; [Signing][signing] | v2, Unity's debug key |
| A supported SDK and engine, and a valid network security configuration (Packaging.4) | [Packaging.4][pkg4] | Meta's configuration, cleartext refused |
| APK under 1 GB (Packaging.5); 64-bit binaries (Packaging.6) | [Requirements][req] | 50 MB; native code arm64-v8a only |
| The fewest permissions, each one used, none unsupported (Security.2, required for immersive apps), checked against lists of [prohibited][prohibited] and [review-required][review] permissions | [Security.2][sec2] | INTERNET, HAND_TRACKING and AndroidX's own receiver permission |
| Focus aware (VRC.Quest.Input.4) | [Requirements][req] | `com.oculus.vr.focusaware` true |

Pages, with the date each gives as its last update: [Quest requirements][req] 2026-08-19;
[Manifest for release builds][manifest] 2026-08-31; [Android 14 target][android14] 2025-11-24,
updated 2026-02-06; [VRC.Quest.Security.2][sec2] 2026-03-30; [prohibited][prohibited] 2025-04-30
and [review-required][review] 2025-11-26 permissions; [removing permissions][remove] 2025-03-25;
[VRC.Quest.Packaging.4][pkg4] 2024-07-31; [app signing][signing] 2024-07-31;
[release channels][channels] 2026-03-23; [uploads][upload] 2026-03-26;
[platform utility][utility] 2026-07-20; [Meta XR Operator on Quest][operator] 2026-09-10;
[Immersive Debugger][debugger] 2026-07-24; [Meta VR OS SDK versioning][osversioning] 2026-09-14;
[app compatibility][compat] 2026-09-04.

## Findings

- **Target API level.** Left automatic, Unity targeted 36: outside the 32 to 34 that Meta accepts
  for immersive apps, and not the 34 that an app created since March 1, 2026 must set. The upload
  refuses a lower target; the pages do not say what it does with a higher one. The project now sets
  34, and Unity then compiles against platform 34 as well, which its SDK already had.
- **Media projection.** No store page names `FOREGROUND_SERVICE_MEDIA_PROJECTION` or
  `FOREGROUND_SERVICE`; neither is prohibited or review-required. Declared by an app that never
  captures the screen, they fail Security.2, under which every declared permission must be used.
- **Meta XR Operator** is where the agentic components come from. Its Android library
  (`XrApiLayer_METAX_operator_unity_android.aar`, manifest package `com.meta.agenticxr`) declares
  INTERNET, FOREGROUND_SERVICE, FOREGROUND_SERVICE_MEDIA_PROJECTION, the optional feature
  `com.oculus.experimental.enabled`, the translucent `AgenticMediaProjectionActivity` and the
  `mediaProjection` foreground service, and carries an implicit OpenXR API layer. Meta calls the
  Operator experimental, says to avoid depending on it in production apps, requires a development
  build for it, and says its MCP server listens on port 8720 inside the app. The SDK's
  `MetaXROperatorBuildProcessor` includes the library only in builds with
  `BuildOptions.Development`.
- **Immersive Debugger.** Since v77 it no longer requires a development build; its settings can
  restrict it to development builds, and a scripting define allows it in production. Its runtime
  assemblies go into every build that has them: the baseline registers 13 startup hooks from them,
  one of which captures every log message for the Operator, although the project has the debugger
  disabled. Only the dev agent references `Meta.XR.ImmersiveDebugger`, and nothing at runtime
  references the dev agent or the agent bridge. Meta's building blocks reference
  `Meta.XR.ImmersiveDebugger.Interface`, which therefore stays.
- **DevAgentSettings.** Before every build, development or not, Meta's `DevAgentBuildProcessor`
  writes this Mac's LAN address and the token of the editor's remote agent server, kept in
  EditorPrefs, into `Assets/Resources/DevAgentSettings.asset`, and restores the old values
  afterwards. That server, when started, listens on every interface on port 48735 and accepts
  requests that carry the token. The baseline development APK carries the address and the token in
  the asset's packed data, and the address again in `boot.config` for Unity's profiler connection.
  While a player builds, Meta's settings loader never creates the asset.
- **Startup hooks of left-out assemblies.** Unity writes `RuntimeInitializeOnLoads.json` before
  `IFilterBuildAssemblies` runs, so the release APK still lists the 13 hooks while
  `ScriptingAssemblies.json` no longer lists their assemblies. A macOS player from the same editor,
  built with one of two assemblies filtered out, ran the kept assembly's hooks and skipped the
  other's without a message. The Quest player is IL2CPP rather than Mono; that it skips them too
  is an inference until the headset shows it.
- **Kept, and inert.** Meta's Runtime Optimizer (its assemblies, `LibRuntimeOptimizer.so` and an
  optional `libhzos.meta.so` native library entry) does nothing unless the
  `ENABLE_RUNTIME_OPTIMIZER` define is set, which this project does not set. The Performance
  Testing package, a dependency of Collections, puts its run information (editor version and
  package list) into Resources for every build. The committed `ImmersiveDebuggerSettings.asset`
  (debugger disabled, no secrets) still ships, 388 bytes that nothing in the release build can
  load. Meta's `OVRPlugin` library puts 32-bit copies of helper libraries under `assets/lib`, as
  files, not native code.
- **Glasses identifier.** Meta's build step writes `com.oculus.supportedDevices` from the project's
  target devices, and the v207 default adds Meta VR Glasses as `stanley`, which the SDK source
  calls the compatibility key the OS matches on. The store's manifest page lists only the four
  Quest identifiers, and its app compatibility page says Glasses are chosen in the Developer
  Dashboard as Future devices once a build ready for them has been tested. Whether an upload that
  declares `stanley` passes was not verified. The committed manifest listed Quest 1 (`quest`) as
  well, which the build dropped because the project does not target it; it now matches the build.
- **Horizon OS SDK element.** Meta's build writes `horizonos:uses-horizonos-sdk` (minimum 60,
  target 207). Meta's documentation now describes `metavr:uses-metavr-sdk`, and says Horizon OS and
  the Developer Dashboard still accept the older element.
- **Signing and version code.** All three APKs are signed with Unity's debug key, scheme v2 only
  (v1 is not needed at minimum API 32). Meta requires the developer's own certificate, kept for
  every update; developers report that uploads signed with the debug certificate are refused.
  Developers also report that an upload is refused when its version code was already uploaded;
  the pages read do not say so. `AndroidBundleVersionCode` is 1.

## Development and release APKs

| | Development, `main` | Development | Release |
| --- | --- | --- | --- |
| `targetSdkVersion`, `compileSdkVersion` | 36, 36 | 34, 34 | 34, 34 |
| `minSdkVersion` | 32 | 32 | 32 |
| Debuggable | yes | yes | no |
| Permissions | HAND_TRACKING, INTERNET, FOREGROUND_SERVICE, FOREGROUND_SERVICE_MEDIA_PROJECTION, receiver | the same | HAND_TRACKING, INTERNET, receiver |
| `com.oculus.experimental.enabled` (optional) | yes | yes | no |
| `AgenticMediaProjectionActivity` and `AgenticMediaProjectionService` | yes | yes | no |
| Operator library, its helper library and implicit API layer | yes | yes | no |
| Script assemblies | 125 | 125 | 121 |
| DevAgentSettings, the LAN address and the token in the data | yes | yes | no |
| Profiler connection in `boot.config` | yes | yes | no |
| Size | 63.2 MB | 63.4 MB | 50.3 MB |

"Receiver" is `com.halcyonic.xr.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION`, a signature permission
that AndroidX Core declares for the app itself. Between the baseline and the release, the compiled
manifests differ in exactly these lines: the target and compile SDK, `android:debuggable`, the two
foreground service permissions, the experimental feature, and the Operator's activity and service.
Everything Halcyonic needs is in all three: INTERNET, HAND_TRACKING with the optional hand tracking
feature and `com.oculus.handtracking.frequency`, the required head tracking feature, the VR
launcher category, `com.oculus.vr.focusaware`, the network security configuration, Halcyonic's
three assemblies with Newtonsoft.Json, and `HalcyonicBootstrap`'s startup hook.

The release build's own safeguards were exercised by a temporary editor script: its APK check
reported nine findings in the development APK (the Operator's library and API layer, its two
manifest entries, the four assemblies and DevAgentSettings) and none in the release APK; a
leftover `Assets/DevAgentSettings.asset` stopped the release build before it moved anything; and a
non-development build started outside `BuildReleaseApk`, with the asset in `Resources`, failed
before building anything.

## Consequences

- Upload only APKs from `QuestBuild.BuildReleaseApk`
  ([XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md)). The development APK carries this Mac's LAN
  address and a token, and serves agents from inside the app; it stays on the owner's headset.
- Before an upload, the owner signs the release APK with a release key kept outside the
  repository, and raises the version code above every earlier upload.
- Before the submission, on the headset: the release APK starts with no errors from the missing
  startup hooks, connects, renders and passes the milestone 2 checks; the development APK still
  does all of that at API level 34. Both are signed with the same debug key today, so either
  installs over the other. Once the release APK carries the owner's key, installing it needs an
  uninstall first, which deletes the pushed access token.
- Decide whether the store build declares Meta VR Glasses (`stanley`) before submitting
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).
- Re-read these pages before the upload: their requirements change.

[req]: https://developers.meta.com/horizon/resources/publish-quest-req
[manifest]: https://developers.meta.com/horizon/resources/publish-mobile-manifest/
[android14]: https://developers.meta.com/horizon/blog/meta-quest-apps-android-14-march-1
[sec2]: https://developers.meta.com/horizon/resources/vrc-quest-security-2
[prohibited]: https://developers.meta.com/horizon/resources/permissions-prohibited
[review]: https://developers.meta.com/horizon/resources/permissions-review-required
[remove]: https://developers.meta.com/horizon/resources/permissions-remove
[pkg4]: https://developers.meta.com/horizon/resources/vrc-quest-packaging-4
[signing]: https://developers.meta.com/horizon/resources/publish-mobile-app-signing/
[channels]: https://developers.meta.com/horizon/resources/publish-release-channels
[upload]: https://developers.meta.com/horizon/resources/publish-upload-overview/
[utility]: https://developers.meta.com/horizon/resources/publish-reference-platform-command-line-utility/
[operator]: https://developers.meta.com/horizon/documentation/unity/meta-xr-operator/quest/
[debugger]: https://developers.meta.com/horizon/documentation/unity/immersivedebugger-overview/
[osversioning]: https://developers.meta.com/horizon/documentation/android-apps/metavr-os-sdk-versioning
[compat]: https://developers.meta.com/horizon/essentials/app-compatibility
