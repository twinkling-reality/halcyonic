# Checking the headset without wearing it

- **Question:** Which commands let a developer, or an agent, check a Meta Quest 3 without anyone
  wearing it: whether it is awake and what is in front, installing an APK and putting the previous
  one back exactly, launching and stopping, seeing what it shows, recording it, and reading the
  app's log? Which adb server does each go to when Unity's Android builds run beside it?
- **Date:** 2026-10-28.
- **Environment:** a Meta Quest 3 on Horizon OS v207, charging over a USB-C data cable on an Apple
  silicon Mac; `adb` from Unity 6000.3.25f1's Android module; the headset on an adb server of its own
  on port 5038, Unity's Android builds on a server of their own on 5039; the installed development
  build of Halcyonic of 2026-10-15; Meta's capture service (com.oculus.metacam) 206.0.0.27.652.
- **Method:** real runs, one command at a time, each with its exact form, exit status, output
  shape and time recorded; the captures and logs are kept out of the repository because they show
  the room. Meta's [Scriptable Testing](https://developers.meta.com/horizon/documentation/unity/ts-scriptable-testing/)
  page (updated 2026-10-06) for the testing service; the Meta XR Core SDK 207.0.0 sources for Meta XR
  Operator. Where no Meta page documents a command, the run is the only evidence, and this record
  says so.
- **Status:** Verified on the headset: every command in `pnpm quest:device` of 2026-10-28; the adb
  servers kept apart. Not verified: the longest recording and the capture service's rate limit;
  whether the microphone is heard while nobody wears the headset; what a 5039 server does when 5038
  is not running. Added 2026-11-03 and run on the headset that day: `connect`, the forwarded `--`, the
  refusal to launch under a system dialog, reading a launch's hold with `logcat -e` from the
  headset's clock (`-t <epoch>`), and `clip`.

## Findings

### Which server a command reaches

- With the headset's server on 5038, an adb server started as Unity starts one
  (`ANDROID_ADB_SERVER_PORT=5039` and `ADB_SERVER_SOCKET=tcp:localhost:5039`) lists no device, and
  stopping it leaves 5038 and the headset as they were. A Unity batch build's own 5039 server listed
  no device either, while 5038 still had the headset.
- In a shell that exports `ADB_SERVER_SOCKET=tcp:localhost:5039`, a bare `adb` reaches 5039, while
  `adb -P 5038` and `adb -L tcp:localhost:5038` reach 5038. So the device tools give every command
  `-L tcp:localhost:<port> -s <serial>`, and the runbook's shell exports the headset's port.

### State

- `dumpsys power`: `mWakefulness=Asleep` or `Awake`.
- `dumpsys activity activities`: only the first `topResumedActivity` says what is in front. While
  Meta's Guardian dialog (`com.oculus.guardian/...GuardianDialogActivity`, "Finding position in
  room") was on top, `ResumedActivity` and `mFocusedApp` still named Home. `dumpsys window`'s
  `mCurrentFocus` was always null. (2026-11-03: a dialog that holds launches can show below the
  first; see "The Headset Power menu" below.)
- A headset lying on a desk loses tracking: the Guardian dialog takes the foreground after it wakes,
  and `am start` is held, with Android's `Launch is blocked because: a Guardian dialog is currently
  showing. Caching launch`. Twice it cleared by itself within about two minutes, nothing pressed;
  a third time it was still there after five.
  The main log keeps about fifteen minutes, so the line is read right after a launch.
- `content call --uri content://com.oculus.rc --method GET_PROPERTY` reads `disable_guardian`,
  `set_proximity_close`, `disable_dialogs` and `disable_autosleep` without the account's PIN. Setting
  them needs the PIN (Meta's page); the tool never sets them.
- `dumpsys battery` and `dumpsys thermalservice` as in `pnpm quest:session`.

### Awake and given back

- `am broadcast -a com.oculus.vrpowermanager.prox_close` keeps it awake on the desk; GET_PROPERTY
  then reads `set_proximity_close=true`. `input keyevent KEYCODE_WAKEUP` wakes it within 0.1 s.
  `am broadcast -a com.oculus.vrpowermanager.automation_disable` gives the sensor back
  (`set_proximity_close=false`).
- One `automation_disable` read off at once and, three minutes later, on again, with no other
  `prox_close` in the system log in between (ActivityManager's enqueued broadcasts); the power
  manager stayed in `HEADSET_MOUNTED` until a second `automation_disable`, after which it moved to
  `HEADSET_UNMOUNTED` within 2 s. Why the first one did not end it is unknown. A reading taken
  straight after the broadcast can also still show the old value. So the tool reads twice, 5 s
  apart, and sends it again while either says on.
- 2026-10-29 09:36Z: while the Guardian dialog held the headset, two `give-back` runs (four
  `automation_disable` broadcasts) each read `set_proximity_close=true`; about ten seconds after the
  last, the headset was asleep and read `false`. Observed once: the override reads on until the
  headset sleeps. So `give-back` names the dialog instead of failing for that reading.

### Installing and putting back

- `pm path <package>` lists the installed APKs; with a single `base.apk`, `sha256sum <that path>`
  on the headset gives its bytes in about 0.1 s. `install -r` of an APK signed with the same
  certificate kept the app's data and its `firstInstallTime`, and the headset's `base.apk` then had
  exactly the APK's sha256; `install -r` of the saved copy put the original bytes back, again with
  the same `firstInstallTime`. Each took about 6 s.
- Launching the installed app changes its data: Halcyonic rewrites `files/halcyonic-panel-motion.json`
  (the native panel's copy of Keep things still) at every start.

### Launch and stop

- `am start -W -n <package>/<activity>` printed `Status: ok`, `LaunchState: COLD` and about 0.5 s
  for both apps tried; `cmd package resolve-activity --brief <package>` names the activity.
  `am force-stop <package>` stops it; `pidof <package>` then exits 1.

### Seeing what it shows

- `adb exec-out screencap -p` fails (quest-3-device.md).
- `screenrecord --time-limit <s> /sdcard/Download/<name>.mp4` works: 4128 by 2208 H.264, both eyes
  side by side with the lens distortion, about 80 fps, no sound. One frame of the left half is a
  usable picture of what the wearer sees.
- Meta's capture service: `am startservice -n com.oculus.metacam/.capture.CaptureService -a
  TAKE_SCREENSHOT` writes `/sdcard/Oculus/Screenshots/<package in front>-<date>-<time>.jpg`, 1440 by
  1440, one eye, within 2 s.
- Recording: `-a START_INTERNAL_CAPTURE_TO_DISK`, later `-a STOP_INTERNAL_CAPTURE_TO_DISK`, writes
  `/sdcard/Oculus/VideoShots/<package in front>-<date>-<time>-0.mp4`: 1920 by 1080 H.264 at 30 fps
  and about 5 Mbit/s, AAC 48 kHz stereo of the app's sound mixed with the headset microphone
  (`includeMic true`), one eye, without stabilization. The service logs `Start internal capture to
  disk for debugger`; no Meta page documents these actions. The service also declares
  `horizonos.appactions.START_RECORDING` and `STOP_RECORDING`, but from adb they log `Missing result
  messenger` and record nothing. A short test recording's sound measured almost silent (mean
  -90 dB), so whether the microphone is heard while nobody wears the headset is not established.
- **The capture service uploads.** With the headset's media sync on (`media sync preference is
  enabled: true`), a screenshot and a recording each reached `media_sync_succeed` within 3 s of
  being saved: they were uploaded to the headset's account, and removing them from the headset does
  not remove the uploaded copy. `screenrecord`'s file in /sdcard/Download showed no sync activity.
  So the tool uses `screenrecord` unless the owner has turned media sync off.

### Logs

- `logcat -d -v threadtime --pid=<pid>` keeps one app's lines. What matters for Halcyonic: its own
  `Unity   : Halcyonic: ...` lines, `OpenXR_SessionImpl: PostSessionStateChange` and the compositor's
  `VrApi   : FPS=72/72,...` once a second.

### Meta XR Operator

- Meta XR Core SDK 207.0.0's build processor puts Meta XR Operator's OpenXR layer
  (`XrApiLayer_METAX_operator_unity_android.aar`) into every Android development build and leaves it
  out of release builds; nothing opts in.
- While the development build runs, it serves MCP over SSE on port 8720 on every address (0.0.0.0
  and [::]), not only through `adb forward`. Over `adb forward tcp:<port> tcp:8720`, `GET /sse`,
  `initialize` and `tools/list` needed no credentials, and every response allows any web origin
  (`access-control-allow-origin: *`). It offered 36 tools: reading head, hand and controller poses,
  frame, session and system information, capturing the composited image, ending the session, hand
  poses and gestures, reading Unity's scene (objects, canvases, interactables, sliders, toggles), and
  pose and maths helpers. Controller, head and eye input were not offered on this retail headset.
- So anything on the headset, and anything on its network, can read the scene and capture what the
  wearer sees while a development build runs. Whether the layer can listen on loopback only is an
  open question for Meta.

### Meta XR Operator left out (2026-10-28)

- Two development builds with Unity 6000.3.25f1 on its own adb server: without
  `HALCYONIC_OPERATOR`, Meta's step still logged that it was including the Operator's library, then
  `OperatorInDevelopmentBuilds` left it out, and the APK's file listing held neither
  `lib/arm64-v8a/libXrApiLayer_METAX_operator.so` nor
  `assets/openxr/1/api_layers/implicit.d/XrApiLayer_METAX_operator.json`; with
  `HALCYONIC_OPERATOR=1` it held both. `BuildDevelopmentApk` passed both builds.
- On the headset (2026-10-29), with check builds installed beside the owner's app: a check build
  without `HALCYONIC_OPERATOR` running showed no listener on port 8720 in `/proc/net/tcp` or
  `/proc/net/tcp6`; a journey check build running showed one in each, under the check package's
  uid, gone once it stopped. The owner's app's bytes were unchanged throughout.

### A check build beside the owner's app (2026-10-28)

- Three builds with Unity 6000.3.25f1 on its own adb server, read with `aapt2`: without
  `HALCYONIC_TEST_ID`, `BuildDevelopmentApk` wrote `Builds/Halcyonic.apk`, package `com.halcyonic.xr`,
  task affinities `com.halcyonic.xr.glance`, `.panel` and `.panel.preview` as before; with
  `HALCYONIC_TEST_ID=1` it wrote `Builds/Halcyonic-check.apk`, package `com.halcyonic.xr.check`,
  affinities `com.halcyonic.xr.check.glance`, `.check.panel` and `.check.panel.preview`, and
  `BuildReleaseApk` wrote `Builds/Halcyonic-release-check.apk`, package `com.halcyonic.xr.check`,
  affinity `com.halcyonic.xr.check.panel`. The manifest merger filled `${applicationId}` in each.
- The check builds left the development APK's bytes, and a file put at the release APK's path, as
  they were, and no build changed ProjectSettings.asset.
- On the headset (2026-10-29) the check package installed beside the owner's app with an appId of
  its own, started straight into its demonstration with no permission dialog in front 12 s later,
  so nothing was granted, and was uninstalled at the end; `pm path` then listed nothing for it.
- `pnpm quest:device -- install` reads an APK's own package with `aapt2 dump badging` before anything
  reaches the headset and refuses unless it is the package named, so an APK of the owner's package
  given as the check build is refused; a check install also fails if the owner's app's bytes changed.
- Granting the check build `android.permission.RECORD_AUDIO` lets its hold to talk record the room:
  development builds only, and what it hears stays private. An `adb reverse` on 47800 serves every
  app on the headset, not only the check build, so it exists only while a step needs it.

### An agent build over the owner's app, data set aside (2026-10-29)

- Through `run-as` the app's private files can be listed, copied aside with `cp -p` and compared with
  `cmp -s`, and the copies put back with names, sizes, modes and times equal to before. Its folder
  on shared storage, `/sdcard/Android/data/com.halcyonic.xr`, could not be read through `run-as`, so
  it was not set aside: an agent build's first start there may leave Unity's extracted IL2CPP data
  for its own build in `files/il2cpp`, which the owner's app extracts again at its next start.
- With no control plane reachable a development build shows the recorded demonstration and logs
  that nothing answered at 127.0.0.1:47800 and its access code was not sent.

### Recording with media sync off (2026-10-29)

- With media sync turned off on the headset, the capture service logged `media sync preference is
  enabled: false` and `auto_sync_not_allow` after each recording, and nothing was uploaded.
- A 200 s recording through `START_INTERNAL_CAPTURE_TO_DISK` and `STOP_INTERNAL_CAPTURE_TO_DISK` came
  back whole: 199.7 s, H.264 1920 by 1080 at about 30 fps, AAC 48 kHz stereo, 159 MB; the capture
  service's own check said `video: true, audio: true, duration Ms: 199724`. No length limit was met.
- With nobody wearing the headset its sound was almost silent (mean -90 dB, peak -64 dB): whether
  the microphone is heard while it is worn is still to see on the owner's first take.
- `record start` and `record stop`, run as two commands with a recording in between, pulled the clip
  with its sha256 beside it and removed it from the headset; a `record stop` with nothing recording
  stopped nothing.

### The Headset Power menu, and a hold under another tag (2026-11-03)

A Quest 3 on build `UP1A.231005.007.A1` (incremental 52641960031600520) on its own adb server on
5038, nobody wearing it, lying where its cameras saw the room; the installed development build of
2026-11-01; the control plane from main `466e5326` on 127.0.0.1:47800.

- **The power menu held a launch.** Meta's Headset Power menu
  (`com.oculus.vralertservice/.ShutdownConfirmActivity`; Sleep, Cancel, Power Options) had been
  started by the system (uid 1000, as at a power-button hold) 13 minutes before the session, and
  was still open after the headset had slept and was woken. A launch logged
  `Launch is blocked because: a Reprojected OS dialog is currently showing. Caching launch for
  component com.halcyonic.xr/...` under vrshell's own tag, `[SEO] SystemUXController`, not
  ActivityTaskManager's; `am start -W` did not say ok after about 20 s, and nothing started. Nothing
  was pressed; the menu waits for a person.
- **The first task is not always the dialog.** With the menu open and the Guardian dialog started
  by the wake (it cleared by itself after about four minutes), the first `topResumedActivity` named
  the menu while `ResumedActivity` named the Guardian dialog; Home and Meta's panels each had their
  own `topResumedActivity` below. Each dialog showed as a visible task.
- **screenrecord shows the menu.** A frame showed it in passthrough, legible, once
  `ResumedActivity` named it; while `ResumedActivity` named the Guardian dialog, the three frames
  looked at showed the room and no dialog. In every clip the right eye's image is rotated about 25
  degrees against the left.
- **`--no-rebind` runs here.** `adb reverse --no-rebind tcp:47800 tcp:47800` made the control
  plane's mapping, listed as `UsbFfs tcp:47800 tcp:47800`; `reverse --remove tcp:47800` removed it.
- **pnpm forwards `--`.** pnpm 11.27.1 ran `pnpm quest:device -- status` as
  `node tooling/quest/device.ts -- status`, which the tool took as a command named `--`; the tests
  ran the tool with node directly, so none had met it.
- **Without the app running**, `pnpm quest:check` passed the mapping, the private token at mode 600
  and 44 bytes, no glance token, no write's temporary file and nothing at the token's old place, and
  failed only its log line, the app not running.
- **The cached launch did not start.** The menu's task closed at 17:42:07Z, when the owner closed
  it; the log has no start of Halcyonic after the force-stop at 17:23:38Z. Seen once: whether a
  force-stop always drops a cached launch is not known.
- **Woken again at 18:16Z**, the headset showed Meta's tracking-lost dialog over black ("Continue
  without tracking", "Turn on travel mode") for 8 minutes, nothing pressed. The fixed tool ran on it:
  `pnpm quest:device -- status` through pnpm, `launch` refusing before `am start`, and `connect`
  making `UsbFfs tcp:47800 tcp:47800`, which `give-back` removed.
- **Awake on the cable**, the battery fell from 85% to 79% in those 8 minutes and the headset warmed
  from 28 to 39 C (thermal status 0).
- **Clips without a wearer use screenrecord.** The capture service logged its media sync setting
  after each capture (2026-10-29), and no line shows it before the first capture of a session, so
  the log cannot say sync is off until a capture has been made. `clip` records both eyes through
  `screenrecord` instead, with no sound and nothing to upload.
- **A first-time tip held a launch too.** Stood facing the lit room at 19:09Z, the headset tracked
  (passthrough of the room, no Guardian dialog), but Meta's first-time tip "Customize your Library"
  (`com.oculus.firsttimenux/.FirstTimeNuxExclusiveSystemDialogActivity`) was in front, and the launch
  logged the same `a Reprojected OS dialog is currently showing. Caching launch`. The tool did not
  know the tip yet; it found the hold by the line's words, read with `logcat -e` from the headset's
  clock, stopped the app and failed.
- **With nothing in front (19:14Z)** the launch started, and `pnpm quest:check` passed every line on
  the installed build: the one mapping, the private token at mode 600 and 44 bytes, nothing at the old
  place, the move ("nothing was on shared storage"), the connection live over `adb reverse`, and no
  cleartext or StrictMode line; the glance had not polled, so its checks were skipped. Nothing
  listened on 8720 while the app ran (both tables read). The compositor held 72 to 73 fps after a
  first second of 31 with 44 stale frames, and the app logged its device view field (108 by 99
  degrees for both eyes). A 5 s `clip` came back whole: 4128 by 2208, about 67 fps, 12.9 MB.

So the device tool reads every task's resumed activity and the one `ResumedActivity` for the
Guardian's, the alert service's and the first-time tips' dialogs, reads a hold by its words since
the launch, fails a launch `am start` does not confirm, drops the `--` pnpm forwards, and makes the
mapping with `connect`.

## Consequences

- `pnpm quest:device` gives every command `-L tcp:localhost:<port> -s <serial>`, refuses 5037,
  never starts an adb server (it connects to the port first and stops when nothing listens), takes
  only an Android package name for `--package`, saves an installed APK before installing over it and
  checks the bytes both ways, fails a launch a system dialog holds or `am start` does not confirm,
  removes only the captures it made after checking their pulled bytes, uses `screenrecord` unless the
  owner has turned media sync off, records clips of both eyes through `screenrecord` (`clip`, up to
  180 s), and keeps only Halcyonic's own log lines.
- Development builds leave Meta XR Operator out unless a journey build asks for it
  (`OperatorInDevelopmentBuilds`, XR_DEVELOPMENT.md).
