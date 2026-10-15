# Interaction SDK editor telemetry shutdown

- **Question:** Why can an editor render fixture pass every assertion and Unity still exit 134?
- **Date:** 2026-10-15.
- **Environment:** macOS Apple silicon, Unity 6000.3.25f1, Meta Interaction SDK Essentials
  (`com.meta.xr.sdk.interaction`) 207.0.0, bundled native telemetry 1.207.0.
- **Method:** Read the two matching macOS crash reports and editor logs; inspect the installed
  package's C# lifecycle code. Compile and exercise the Halcyonic hook with a stub editor lifecycle.
- **Status:** Crash attribution and managed lifecycle behavior verified. The workaround has not
  yet been run in Unity; whether it prevents the native crash remains unverified.

## Observed failure

Two consecutive editor exits on this date had identical failing native frames:
`ISDKEngineTelemetry.dylib` worker frames, `std::mutex::lock`, `std::__throw_system_error`, then
`SIGABRT`. The main thread was already in `Application::Exit`, `exit` and
`__cxa_finalize_ranges`. The later fixture logged `responsive workspace: every check passed`
before shutdown. The earlier fixture reported assertion failures and then the same native abort.
The adjacent debugger-agent warning is not the failing stack. These observations distinguish
fixture outcomes from process outcomes: a passed fixture plus a shutdown crash is still a failed
process invocation. Never convert exit 134 into success solely from a log marker.

## Verified package surface

`Editor/Telemetry/ISDKTelemetryEditorEvents.cs` initializes the internal
`Oculus.Interaction.Telemetry.ISDKEngineTelemetryNative` from `[InitializeOnLoad]`, but installs
no editor quitting handler. `Runtime/Telemetry/ISDKTelemetryInitializer.cs` registers
`Application.quitting` only when its `BeforeSceneLoad` runtime initializer runs. Edit-mode render
fixtures do not enter that runtime lifecycle. The wrapper's `ShutDown()` checks `_initialized`,
invokes native shutdown, and clears that flag in `finally`. The separately declared native
`engineTelemetry_OnEditorShutdown` has no managed callers in the installed packages; its
semantics were not verified, so the workaround does not call it.

## Scoped workaround and limits

`InteractionTelemetryShutdown` registers `EditorApplication.quitting` only in macOS batch mode.
At quit it finds the already loaded internal wrapper by its exact name, checks the owning package
name and version 207.0.0 and the literal expected native version 1.207.0, then invokes the SDK's
existing public guarded `ShutDown()`. Reflection is necessary because the declaring SDK type is
internal. No SDK package cache, telemetry consent, persistent settings or process exit code is
changed. A package mismatch produces a review warning instead of guessing compatibility.
Invocation failure produces an error with fixed text. The SDK itself suppresses native shutdown
exceptions, so this hook cannot establish native success from its managed return value.

Managed tests compile the production hook unchanged against minimal editor stubs and a fake
SDK wrapper with the verified ownership guard. They cover editor quit, repeated quit, runtime
shutdown before editor quit, rejected package/version and invocation failure. They do not prove
native thread teardown, actual Unity callback ordering or assembly discovery in the editor.
The next required proof is a single affected fixture in Unity with both assertions passed and
process exit 0, followed by checking the absence of the matching native crash. Until that proof,
the missing editor lifecycle shutdown is a grounded explanation and the remedy remains a hypothesis.
