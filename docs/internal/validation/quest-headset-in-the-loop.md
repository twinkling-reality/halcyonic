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
- **Status:** Verified on the headset: every command in `pnpm quest:device`; the adb servers kept
  apart. Not verified: the longest recording and the capture service's rate limit; whether the
  microphone is heard while nobody wears the headset; what a 5039 server does when 5038 is not
  running.

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
  `mCurrentFocus` was always null.
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
- Not yet checked on the headset: that an app built without it opens no listener on 8720.

## Consequences

- `pnpm quest:device` gives every command `-L tcp:localhost:<port> -s <serial>`, refuses 5037,
  saves an installed APK before installing over it and checks the bytes both ways, fails a launch a
  system dialog holds, removes only the captures it made after checking their pulled bytes, uses
  `screenrecord` unless the owner has turned media sync off, and keeps only Halcyonic's own log lines.
- Development builds leave Meta XR Operator out unless a journey build asks for it
  (`OperatorInDevelopmentBuilds`, XR_DEVELOPMENT.md).
