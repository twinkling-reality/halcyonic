# A headset session

Ordered verification procedure for a single session on a Meta Quest, recording build evidence and
wearer usability separately. Passing an Editor render or installing an APK does not establish
usability on hardware.

This runbook covers workspace acceptance, private token storage, the demonstration's first visit,
loopback proof verification, the 2D glance window, workspace comfort, motion, single-word speech
transcription, Unicode label normalization under IL2CPP, Codex on a local model, Wi-Fi paired
request timeouts, the release demonstration walk, and spatial presence and audio, plus unattended
automation via `pnpm quest:check` and `pnpm quest:device`. Setup runs once at the start and teardown
once at the end ([Close](#close)); steps that intentionally alter state specify how to restore it.

Run the automated state check whenever a step calls for it:

```bash
pnpm quest:check
```

It prints one `pass` or `FAIL` line per invariant and exits `1` if any fails:

- a single Halora `adb reverse` mapping (`47800` to `47800`) with unrelated routes preserved;
- app-private access token files at mode `600` and `44` bytes (`stat` via `run-as`), with no
  leftover `.tmp` file;
- no token file at `/sdcard/Android/data/com.halcyonic.xr/files/access-token`;
- from the running process's `logcat` lines after app launch: startup shared-storage migration
  outcome, live proved connection, latest glance poll, and zero `cleartext` or `StrictMode` lines.

`pnpm quest:check` inspects file modes, byte counts, and timestamps only; it never reads or prints
token contents. Flags override expected states for negative tests: `--expect unproved` (or `refused`,
`not-loopback`, `unreachable`, `none`), `--move moved` (or `kept`, `released`, `not-a-token`,
`nothing`), `--release` on a release APK, and `--closed` at teardown. If `logcat` rotated past app
startup (detected by comparing the oldest main-log timestamp against `ps -o ETIME`), restart Halora
and re-run `pnpm quest:check` immediately.

Session rules:

- **Never print, paste, or `cat` a token.**
- **Single loopback route:** keep exactly `adb reverse tcp:47800 tcp:47800` and run no local forwarder
  to port `47800` (`ssh -L`, `socat`, proxies), which could relay loopback challenge proofs
  ([SECURITY.md](../architecture/SECURITY.md)). Leave unrelated ports untouched.
- **Single ADB binary and isolated Editor server:** put Unity's `adb` first on `PATH` (or set
  `HALCYONIC_ADB`) so `adb` versions match `pnpm quest:check`, and run Android Editor builds on an
  isolated no-device server ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md#build)):

  ```bash
  export PATH="/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools:$PATH"
  ```

- **Boundary:** sit, or set a roomscale boundary; leaning outside a stationary Guardian boundary
  causes Horizon OS to replace the app view with passthrough without logging a focus change
  ([quest-3-device.md](../validation/quest-3-device.md)).
- **Control plane:** run `pnpm start` (without `--watch`) so file edits cannot restart it mid-session.
- **Captures:** keep captures out of git. `adb exec-out screencap` does not capture Quest views;
  trigger Meta's capture service and pull stills during [Close](#close):

  ```bash
  adb shell am startservice -n com.oculus.metacam/.capture.CaptureService -a TAKE_SCREENSHOT
  ```

- **Evidence:** record results in the validation file named by each step with the git commit, date,
  headset model, and Horizon OS build, and update resolved rows in
  [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md).

## Workspace acceptance

Install the target APK and record its commit, headset model, and Horizon OS build. Any missing path
or unreadable control fails acceptance regardless of lower-level unit or render checks:

1. **Find work without characters.** Launch with **Agents** off. Verify **Projects** and **Tasks**
   are available immediately, inspect an existing project and task, and confirm state and source
   labels (`Live`, `Practice`, `Demo`, `Recorded`). Repeat with an empty project list and a
   disconnected host.
2. **Keep context.** Open **Activity**, **Changes**, and **Checks** on a task and distinguish
   observed, reported, and unavailable data. Switch to another task and return: verify ordinary
   instruction drafts stay bound to their original tasks and never carry over to a replacement
   execution.
3. **Clear the workspace.** Press **Minimize Workspace** with an unsent draft open; verify panels,
   peeks, and optional agents collapse to a single dock control, and **Open Workspace** restores the
   task and draft while requiring a fresh read and confirmation for any pending approval.
4. **Place agents deliberately.** Enable **Agents** and preview the shelf (**Place Agents** / **Place
   Shelf**). Verify distinct **Apply** and **Cancel** outcomes, direct **Move Shelf** dragging with
   character contact, refusal with a stated reason when overlapping the console or field edge, and
   stationary console placement when opening a task from a character. Repeat at **Larger Text**.
5. **Work beside another app.** Open **Other Apps** and verify the `FLAG_SECURE`-protected native
   panel is visible and readable on the headset display alongside both a 2D app and an immersive app
   (a synthetic screenshot preview does not verify `FLAG_SECURE` visibility). Inspect a task and run
   one supported action on test work, recording command acceptance and runtime confirmation
   separately.
6. **Return safely.** Edit an ordinary draft in the native panel and return to Workspace (**Open
   Halora**). Verify synchronized selection and draft text across app pause and a controlled Halora
   control-plane restart, verify failed saves surface unsaved status while retaining the draft, and
   confirm no question answer, secret, read proof, or armed confirmation transfers across surfaces.
7. **Check focus.** Switch focus to another window during an open review and return: verify the armed
   confirmation is cancelled. Replace a task's active execution while a review is open and verify no
   retained callback targets the replacement run while ordinary draft text remains recoverable.
8. **Judge the experience.** Without coaching, verify the wearer identifies the active task, next
   available action, and how to leave and return; record any confusion, hand misses, clipping, eye
   strain, or lost context verbatim.

In the immersive Unity workspace, **Hold to Talk** ships in every build
([ADR 0021](../decisions/0021-speech-becomes-a-draft-transcribed-on-the-mac.md)); in the native
Android panel, **Hold to talk** is exposed in debuggable builds only
([native-panel-voice.md](../validation/native-panel-voice.md)). Contextual voice navigation and
background microphone capture beside other apps are not implemented.

## Prepare, before the headset

1. **Checkout:** `git switch main && git pull --ff-only && git log --oneline -1`. Build both APKs and
   run the control plane from this single commit.
2. **Development APK** (with Unity Editor closed, using isolated Editor ADB from
   [XR_DEVELOPMENT.md](XR_DEVELOPMENT.md#build)):

   ```bash
   /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Editor.QuestBuild.BuildDevelopmentApk -logFile ~/Library/Logs/Unity/halcyonic-xr-build.log
   ```

   Pass: `Halcyonic: built Builds/Halcyonic.apk …` after `Build Finished, Result: Success.` (exit
   status `134` after `Exiting batchmode successfully now!` is benign shutdown teardown).
3. **Release APK** (for check 10; leave `HALCYONIC_VERSION_CODE` unset so debug-signed development
   and release builds install over one another cleanly; see [XR_DEVELOPMENT.md](XR_DEVELOPMENT.md),
   "Before an upload"):

   ```bash
   /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Editor.QuestBuild.BuildReleaseApk -logFile ~/Library/Logs/Unity/halcyonic-xr-release.log
   ```

   Pass: `Halcyonic: built Builds/Halcyonic-release.apk …` in the log.
4. **Control plane:** stop any running instance and start `pnpm start`. Pass: `control plane ready`.
5. **Host readiness:** run `pnpm mac-setup` and verify allowed folders, agent apps, local Ollama
   models (`OLLAMA_CONTEXT_LENGTH=65536 OLLAMA_NO_CLOUD=1 ollama serve`, plus
   `OLLAMA_MAX_LOADED_MODELS=2` with the companion; [LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md)),
   voice (for checks 5 and 6), companion, and Salidium (`0.6.0+`).
6. **Unicode normalization scenario copy** (for check 7): copy `fixtures/scenarios` outside the
   repository and set `question_asked`'s first prompt options to composed `Café` (`U+00E9`),
   decomposed `Café` (`e` + `U+0301`), `U+FFFE`, and `Dark`:

   ```bash
   rm -rf ~/halcyonic-scenarios && cp -R fixtures/scenarios ~/halcyonic-scenarios
   python3 - <<'EOF'
   import json, pathlib
   path = pathlib.Path.home() / 'halcyonic-scenarios' / 'question_asked.json'
   scenario = json.loads(path.read_text())
   scenario['steps'][1]['await_answer']['prompts'][0]['options'] = [
       {'label': 'Caf\u00e9', 'description': 'The accent composed'},
       {'label': 'Cafe\u0301', 'description': 'The accent decomposed'},
       {'label': '\ufffe', 'description': 'A noncharacter'},
       {'label': 'Dark', 'description': 'Light text on a dark background'},
   ]
   path.write_text(json.dumps(scenario, ensure_ascii=True, indent=1))
   EOF
   ```

## Connect

Point bare `adb` commands in this shell at the headset's dedicated ADB server on port `5038`
([quest-headset-in-the-loop.md](../validation/quest-headset-in-the-loop.md); `pnpm quest:*` tools
target `5038` automatically):

```bash
export ANDROID_ADB_SERVER_PORT=5038 ADB_SERVER_SOCKET=tcp:localhost:5038
```

1. Confirm `adb devices` reports `device`, and record `adb shell getprop ro.product.model` and
   `adb shell getprop ro.build.display.id`.
2. Inspect `/sdcard/Android/data/com.halcyonic.xr/files/` (`adb shell ls -l /sdcard/Android/data/com.halcyonic.xr/files/`)
   for any pre-existing shared-storage `access-token`.
3. Install `Halcyonic.apk` while preserving app data and unrelated `adb reverse` routes:

   ```bash
   adb install -r apps/xr/Builds/Halcyonic.apk
   adb reverse --list
   adb reverse tcp:47800 tcp:47800
   ```

   (`pnpm quest:device -- connect` creates the same mapping, and `give-back` removes it.)
4. Provision the private access token with `run-as` ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md),
   "Install and connect"), confirm `written`, and launch Halora (an update that already holds a
   valid private token can skip rewriting it):

   ```bash
   adb shell run-as com.halcyonic.xr rm -f files/access-token.tmp
   adb exec-in "run-as com.halcyonic.xr sh -c 'umask 077; mkdir -p files && rm -f files/access-token.tmp && cat > files/access-token.tmp'" < ~/.halcyonic/access-token
   adb shell "run-as com.halcyonic.xr sh -c 'for i in 1 2 3 4 5 6 7 8 9 10; do test \"\$(stat -c %s files/access-token.tmp 2>/dev/null)\" = 44 && break; sleep 1; done; if test \"\$(stat -c %s files/access-token.tmp 2>/dev/null)\" = 44 && chmod 600 files/access-token.tmp && mv -f files/access-token.tmp files/access-token; then echo written; else rm -f files/access-token.tmp; echo not written: the token file is not the 44 bytes the control plane makes, or it never arrived, so write it again; fi'"
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```

5. Run `pnpm quest:check`. Pass: all checks pass, glance token reads `not written`, connection reads
   `live`, and control plane logs `realtime client connected` for `halcyonic-xr` (if a `shell`-owned
   shared-storage copy could not be unlinked by the app, remove it with
   `adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token` and record it).
   - Record private write, `adb shell ps -A -o USER,NAME` check for lingering `cat`, and migration
     behavior in [headset-token-storage.md](../validation/headset-token-storage.md).
   - Record live proved USB upgrade and HMAC behavior under IL2CPP in
     [xr-loopback-proof.md](../validation/xr-loopback-proof.md).

## The demonstration's first visit ([demonstration-build.md](../validation/demonstration-build.md))

With no computer connected, the workspace opens **Projects** with spatial characters optional
([ADR 0029](../decisions/0029-workspace-is-complete-without-characters-and-continues-in-a-native-panel.md)).
If testing on a development headset that holds a USB token, move `files/access-token` to
`files/access-token.off` without overwriting any existing backup.

1. **Launch:** verify **Projects**, **Tasks**, and the explicit **Recorded** / **Demo** source label.
2. **Waiting task:** open the recorded project and read the waiting decision without console jumps.
3. **Evidence:** inspect simulated **Activity**, **Changes**, and **Checks**; confirm simulated
   badges never claim live runtime execution.
4. **Minimize and restore:** keep an unsent draft, press **Minimize Workspace**, and restore via
   **Open Workspace** (both with and without optional agents) with zero stray panels or characters.
5. **Reconnect:** restore the live connection and verify source labels switch only after a live
   snapshot arrives and demonstration drafts never target live work.

Record navigation and wearer comprehension in [demonstration-build.md](../validation/demonstration-build.md).

## The checks

### 1. Token storage ([headset-token-storage.md](../validation/headset-token-storage.md))

1. **Reinstall retention and `ENOENT`:** run `adb install -r apps/xr/Builds/Halcyonic.apk`, launch
   Halora, and run `pnpm quest:check -- --move nothing`. Pass: private token survives `install -r`,
   move reports nothing on shared storage, and connection is `live`.
2. **Shared-storage migration:** remove the private token, push a token to the legacy shared-storage
   path, and launch:

   ```bash
   adb shell run-as com.halcyonic.xr rm -f files/access-token
   adb push ~/.halcyonic/access-token /sdcard/Android/data/com.halcyonic.xr/files/access-token
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```

   Run `pnpm quest:check -- --move moved` (if `shell` ownership leaves a copy or blocks reading with
   `EACCES`, remove `/sdcard/Android/data/com.halcyonic.xr/files/access-token` via `adb shell rm -f`
   and record it; repeat on a Wi-Fi paired headset if pairing is tested).
3. **Symlinks and FIFOs:** set the private token aside and place a symlink at the shared path:

   ```bash
   adb shell run-as com.halcyonic.xr mv files/access-token files/access-token.off
   adb shell ln -s /dev/null /sdcard/Android/data/com.halcyonic.xr/files/access-token
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```

   If `ln -s` succeeds on `/sdcard`, verify `pnpm quest:check -- --move not-a-token --expect none`
   passes without blocking startup; repeat with `mkfifo`, then restore the private token:

   ```bash
   adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token
   adb shell run-as com.halcyonic.xr mv files/access-token.off files/access-token
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```

   Verify `pnpm quest:check` reports `live`.
4. **Backup note:** record whether Meta/Horizon backups show any app data (`android:allowBackup="false"`).
5. **Token rotation and stale-token refusal:** stop the control plane, `rm ~/.halcyonic/access-token`
   (rotating away the token exposed to shared storage in step 1.2), and restart `pnpm start`.
   Relaunch Halora with the stale token:

   ```bash
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```

   Run `pnpm quest:check -- --expect unproved`: verify the mismatch notice on screen and zero `401`
   or `realtime client connected` lines on the Mac. Provision the rotated token as in Connect step 4
   and confirm `pnpm quest:check` reports `live`.

### 2. The loopback proof and the realtime upgrade ([xr-loopback-proof.md](../validation/xr-loopback-proof.md))

1. **Proved REST and WebSocket:** open a task's history and folder picker; verify both load and
   `pnpm quest:check` reports `live`.
2. **Unproved listener on `47800`:** stop the control plane and run a dummy HTTP `200` responder:

   ```bash
   while true; do printf 'HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n' | nc -l 127.0.0.1 47800; done
   ```

   Relaunch Halora and run `pnpm quest:check -- --expect unproved`. Pass: Halora halts with the
   mismatch notice and sends only `GET /api/health` with `x-halcyonic-challenge`, never
   `Authorization`. Stop `nc`.
3. **Silent listener:** run `nc -l 127.0.0.1 47800`, relaunch Halora, and run
   `pnpm quest:check -- --expect unreachable`. Pass: `"Can't reach your computer; trying again"`
   while retrying. Stop `nc`.
4. **Paused control plane:** start `pnpm start`, pause the listener with
   `kill -STOP $(lsof -tiTCP:47800 -sTCP:LISTEN)`, relaunch Halora, and verify
   `pnpm quest:check -- --expect unreachable` within a few seconds; run
   `kill -CONT $(lsof -tiTCP:47800 -sTCP:LISTEN)` and verify `pnpm quest:check` returns to `live`.

### 3. The glance ([horizon-os-multitasking.md](../validation/horizon-os-multitasking.md))

Provision the glance token using the three-step private write (`pnpm quest:device -- glance-token write`
performs the same write via stdin) and follow [XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "The glance on
a Quest", steps 1 to 14:

```bash
adb shell run-as com.halcyonic.xr rm -f files/glance-access-token.tmp
adb exec-in "run-as com.halcyonic.xr sh -c 'umask 077; mkdir -p files && rm -f files/glance-access-token.tmp && cat > files/glance-access-token.tmp'" < ~/.halcyonic/access-token
adb shell "run-as com.halcyonic.xr sh -c 'for i in 1 2 3 4 5 6 7 8 9 10; do test \"\$(stat -c %s files/glance-access-token.tmp 2>/dev/null)\" = 44 && break; sleep 1; done; if test \"\$(stat -c %s files/glance-access-token.tmp 2>/dev/null)\" = 44 && chmod 600 files/glance-access-token.tmp && mv -f files/glance-access-token.tmp files/glance-access-token; then echo written; else rm -f files/glance-access-token.tmp; echo not written: the token file is not the 44 bytes the control plane makes, or it never arrived, so write it again; fi'"
```

Open the glance (`pnpm quest:device -- glance --owner-answers` when worn to grant
`POST_NOTIFICATIONS`) and run `pnpm quest:check` (`polled ok`, zero `cleartext` or `StrictMode`
lines), then verify:

1. **Overlay and notifications** (steps 2, 3, 6, 7, 8): window over an immersive game, toast within
   one poll, **Open Halora** from toast and button, Do Not Disturb, and zero failed notifications.
2. **Minimised polling** (steps 4, 5, 14): `30 s` background polls while minimised, clean stop on
   close, and 60-minute lifetime check (`pnpm quest:session -- --minutes 60`).
3. **Socket deadline** (step 9): `too_slow` near `10 s` against a 1-byte/s responder, then restore
   `adb reverse tcp:47800 tcp:47800`.
4. **Loopback and symlink guards** (steps 10, 11): `127.0.0.1:47800` loopback-only listener and
   `token_not_private` when replaced by a symlink, then rewrite `files/glance-access-token`.
5. **Recents, captures, and backups** (steps 12, 13), followed by a clean restart and `pnpm quest:check`.

### 4. Workspace comfort ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md))

Verify first with **Agents** off, then with a deliberately placed optional shelf:

- Navigate **Projects**, **Tasks**, **Usage**, and **Settings**: console position stays fixed and
  selection/source badges stay readable regardless of title length.
- Open the same task from **Tasks** and from an optional character without moving either console or
  shelf; page through long questions, approvals, **Changes**, and **Checks** without shrinking glyphs
  below `14 dp` or dropping text.
- Test hand ray and poke hit targets across edges, verify hidden/minimised panels accept no input,
  drag and **Reset Position** on the workspace (checking shelf/field-edge refusals), and **Minimize
  Workspace** / **Open Workspace** after turning away and back.
- Repeat facing a bright passthrough wall, at **Larger Text**, and within Quest 3S field margins;
  record device evidence in [quest-3-device.md](../validation/quest-3-device.md) and comfort findings
  in [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md).

### 5. Motion ([quest-3-device.md](../validation/quest-3-device.md), [ADR 0027](../decisions/0027-the-headset-moves-only-to-answer-the-person-or-show-a-wait.md))

Interface motion answers direct input and active waits while remaining still during reading:

1. **Panel transitions:** open and close **Tasks** and a task file; verify smooth ease-in/out slides
   with zero overshoot and zero motion while reading.
2. **State cross-fade:** run `pnpm demo --scenario approval_required`; verify the badge cross-fades
   cleanly from **Working** to **Needs You** and breathes softly.
3. **Hold to Talk states:** run `pnpm demo --scenario question_asked`, hold **Hold to Talk** (verify
   **Listening** with pulsing microphone; capture), speak `"Dark"`, and release (verify **Typing It
   Out** shimmer with stable button width; capture).
4. **Keep Still:** toggle **Settings > Comfort > Motion > Keep Still** and repeat steps 2 and 3;
   verify ambient breathing, turning icons, microphone pulse, shimmer, and character float stop
   (replaced by steady active-tone highlights and distinct eye/light cues across **Working**,
   **Checking**, and **Unknown**), while direct press/open transitions remain. Restore **Allow
   Motion** and record wearer judgment in [quest-3-device.md](../validation/quest-3-device.md).

### 6. The words heard for one word ([voice-transcription.md](../validation/voice-transcription.md))

On `question_asked`'s colour-scheme prompt (`Light` / `Dark` plus custom input), hold **Hold to
Talk**, speak `"Dark"` three times, `"Light"` once, and `"Yes"` once (recording exact capitalization
and trailing punctuation after each hold without sending; capture one), then press **Send Answer** on
the final hold and record the exact transcribed strings (never audio clips) in
[voice-transcription.md](../validation/voice-transcription.md).

### 7. Labels that read alike, under IL2CPP

With Prepare step 6's scenario copy in place, restart the control plane against it:

```bash
HALCYONIC_MOCK_SCENARIOS_DIR="$HOME/halcyonic-scenarios" pnpm start
```

Run `pnpm demo --scenario question_asked` and open the waiting question:

1. **Composed vs decomposed `Café`:** verify both `Café` rows are disabled as duplicate choices
   (`String.Normalize(NormalizationForm.FormC)` under IL2CPP) with an explanation and **Stop**
   available, while `Dark` remains selectable; capture.
2. **Noncharacter `U+FFFE`:** verify the row renders literally as `‹U+FFFE›`.
3. **Logs:** verify `adb logcat -d -s Unity | grep -i -E "exception|normaliz"` reports zero errors.

Restart `pnpm start` without `HALCYONIC_MOCK_SCENARIOS_DIR` and record results in
[quest-3-device.md](../validation/quest-3-device.md).

### 8. Codex as a second agent app, on a local model

Confirm `control plane ready` lists `codex` with `agent_binaries` `matches`
([LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md#codex)); if unconfigured, record `not registered`. In a
folder-bound project, open **New Task**, select Codex and the local Ollama model under **How It
Runs** / **Use Model**, and run a small edit (such as adding a `README` line). Verify no **Practice**
mark appears, answer any prompt or approval, inspect **Activity** on completion, capture, and record
timings and outcome in [codex-capabilities.md](../validation/codex-capabilities.md) and
[local-models.md](../validation/local-models.md).

### 9. A paired request that times out ([network-pairing.md](../validation/network-pairing.md))

When testing Wi-Fi pairing ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "Pairing checks on a Quest") on a
freshly launched app before task history is cached:

1. Pause the control plane: `kill -STOP $(lsof -tiTCP:47800 -sTCP:LISTEN)`.
2. Immediately open a task file and wait ~15 seconds for `PinnedHttpHandler`'s timeout
   (`"earlier activity unavailable, reopen to try again"`).
3. Check `adb logcat -d -s Unity | grep "earlier activity could not be read"` for
   `TaskCanceledException` or `OperationCanceledException` (any other exception indicates a Mono fault).
4. Resume with `kill -CONT $(lsof -tiTCP:47800 -sTCP:LISTEN)` and confirm live reconnection.

### 10. The first-time user's path, on the release build ([demonstration-build.md](../validation/demonstration-build.md))

1. **Release APK over development data:** set the private token aside, stop the control plane, write
   a dummy token-shaped string to shared storage, and install `Halcyonic-release.apk`:

   ```bash
   adb shell run-as com.halcyonic.xr mv files/access-token files/access-token.off
   adb shell "printf 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_-Z\n' > /sdcard/Android/data/com.halcyonic.xr/files/access-token"
   adb install -r apps/xr/Builds/Halcyonic-release.apk
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```

   Run `pnpm quest:check -- --release`: confirm `run-as` is refused, the shared file is removed
   unread (clean up via `adb shell rm -f` if `shell`-owned), and no connection is active.
2. **Demonstration walk:** with hands only and no token or pairing, walk
   [XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "The demonstration judges see", against the timeline in
   [demonstration-build.md](../validation/demonstration-build.md), including sleep/wake and
   system-menu resume.
3. **Confirmation guards:**
   - *Focus loss:* press **Approve**, page partway through, open and close the system menu, and
     verify `"You went to another window, so nothing was sent. Press it again to confirm."`
   - *Larger text:* enable **Settings > Comfort > Larger Text**, page **Approve** to its final part,
     verify confirmation becomes available without relaying or jumping pages, then restore **Standard Text**.
   - *Stop confirmation and timeout:* press **Stop** then **Cancel** (nothing sent); press **Stop**
     and wait `15 s` (`"Nothing was sent: you didn't confirm in time."`); press **Stop** and confirm
     to end the round.

### 11. Spatial presence, audio and passthrough ([ADR 0013](../decisions/0013-characters-are-bots-with-a-living-surface.md), [ADR 0026](../decisions/0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md), [ADR 0027](../decisions/0027-the-headset-moves-only-to-answer-the-person-or-show-a-wait.md))

Keep OVR Metrics' frame-rate overlay visible throughout (`72 fps` with up to six characters) and
record wearer observations in [quest-3-device.md](../validation/quest-3-device.md):

**A. Workspace entry.** Confirm uncoached navigation from [Workspace acceptance](#workspace-acceptance).

**B. Optional agents, by eye** (after enabling and placing the agent shelf):
5. **At rest:** characters stand on soft-shadowed rings with state pills, slow vertical float, and
   occasional blinks, without background glow or cluttering titles.
6. **Gaze dwell (`0.5 s`):** resting gaze on a working character draws eye contact, a slight turn and
   lift, and its peek card, releasing `0.5 s` after looking away (`Unknown` and `Failed` characters
   do not turn).
7. **Head sweep:** sweeping gaze across the shelf triggers zero unintended look-backs.
8. **Wearer judgment:** record whether idle motion feels calm in peripheral view.

**C. Spatial audio and decision flow** (`pnpm demo --scenario question_asked`):
9. **Where the sound comes from ([meta-xr-audio.md](../validation/meta-xr-audio.md)):** with eyes
   closed, point toward the **Needs You** cue under Meta XR Audio's spatializer and compare against
   Unity stereo panning (`adb logcat -s Unity | grep --line-buffered "Halcyonic: sound placed by"`).
   Switch to panning with `/sdcard/Android/data/com.halcyonic.xr/files/sound-panned`:

   ```bash
   adb shell touch /sdcard/Android/data/com.halcyonic.xr/files/sound-panned
   adb shell ls -l /sdcard/Android/data/com.halcyonic.xr/files/sound-panned
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```

   Restore the spatializer before ending the session:

   ```bash
   adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/sound-panned
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```

   Measure mixer-thread CPU over 60 seconds with six active characters each way:
   `adb shell top -H -b -d 1 -n 60 -p $(adb shell pidof com.halcyonic.xr) > ~/halcyonic-captures/audio-<way>.txt`
   (spatializer overhead must remain under `1 ms`/frame, ~7% of one core at `72 fps`).
10. **Character approach:** verify the asking character moves beside the panel/front of the shelf
    without crossing the wearer's face.
11. **Panel open:** pinch the character and check the light line and `~0.33 s` arrival cadence.
12. **Answer confirmation:** submit an answer and verify `Sent…` shimmers until host/runtime
    confirmation returns the character to work.
13. **Outside-text distinction:** run `pnpm demo --scenario approval_required` and verify outside
    command/agent text is visually distinct from Halora's own labels.

**D. Keep Still, bright passthrough, and development trials:**
14. Enable **Settings > Comfort > Motion > Keep Still**, repeat steps 10 to 12 (verifying zero float,
    blink, pulse, shimmer, or travel turn), then restore **Allow Motion**.
15. Against a bright window or lit wall, check edge visibility and outside-text legibility.
16. In development builds exposing depth occlusion or room dimming, verify hand occlusion edges,
    frame rate, and wearer comfort.

## Recording what the headset shows

With headset media cloud sync confirmed off in the device log, record `1920x1080` `30 fps` single-eye
footage (including app audio and headset microphone, ~48 MB/min;
[quest-headset-in-the-loop.md](../validation/quest-headset-in-the-loop.md)):

- Start: `pnpm quest:device -- record start --sync-is-off`
- Stop and pull with SHA-256: `pnpm quest:device -- record stop --out <footage folder>` (or run fixed
  duration `pnpm quest:device -- record --seconds <n> --sync-is-off`). If no clip lands within two
  minutes, `status` flags an active recording; re-run `record stop`.

## Checking the headset without wearing it

`pnpm quest:device` inspects and drives a Quest without a wearer
([quest-headset-in-the-loop.md](../validation/quest-headset-in-the-loop.md)). It connects to the
headset's ADB server on port `5038` (`HALCYONIC_ADB_PORT`, serial `HALCYONIC_QUEST_SERIAL`), refuses
port `5037`, and verifies `host:version` over TCP first so it never implicitly starts or restarts an
ADB server:

```bash
pnpm quest:device -- status
pnpm quest:device -- awake
pnpm quest:device -- save
pnpm quest:device -- install apps/xr/Builds/Halcyonic.apk --saved .private/quest/com.halcyonic.xr-installed-<sha>.apk
pnpm quest:device -- connect
pnpm quest:device -- launch
pnpm quest:device -- shot
pnpm quest:device -- clip --seconds 10
pnpm quest:device -- logs --out .private/quest/halcyonic.log
pnpm quest:device -- stop
pnpm quest:device -- restore .private/quest/com.halcyonic.xr-installed-<sha>.apk
pnpm quest:device -- give-back
```

Key subcommands and invariants:

- **`status` and `awake`:** reports wake state and foreground/background system dialogs (Guardian,
  Power menu, first-time tips; never synthesize presses into system dialogs). `awake` enables the
  proximity override; always run `give-back` at teardown to release the sensor and stop started apps.
- **`save`, `install`, `restore`, and check builds:** `save` backs up and hashes the installed APK;
  `install` requires `--saved` (unless targeting `--package com.halcyonic.xr.check` built with
  `HALCYONIC_TEST_ID=1`, which installs alongside `com.halcyonic.xr` and supports `grant` and
  `uninstall`) and verifies package name via `aapt2` and post-install bytes.
- **`connect` and `launch`:** `connect` manages `tcp:47800` keyed by USB transport ID (`adb devices -l`);
  `launch` aborts and stops the app if a system dialog holds startup. Never launch a personal install
  against a live runtime.
- **Development capture presets (`CapturePresets`, excluded from release builds):** stop first, then
  launch with `--extra` / `--extra-bool` flags:
  - `--extra halcyonic.open=task:<execution id>:<section>` (`activity`, `waiting`, `changes`, `checks`),
    `--extra halcyonic.open=screen:<project id>` (up to `WorkspacePresentationState.ScreensMost = 3`
    concurrent project screens with `PreviewRelay`, [ADR 0052](../decisions/0052-a-preview-reaches-a-paired-headset-through-a-loopback-relay.md)),
    and `--extra-bool halcyonic.bar=true`.
  - `--extra-bool halcyonic.compose=true`, `halcyonic.draft=true`, `halcyonic.heard=true`,
    `halcyonic.choose=true`, `--extra halcyonic.agent=<runtime id>`, and `--extra-bool halcyonic.arm=true`
    (presentation only: presets never approve, answer, start, or send commands).
  - `--extra-bool halcyonic.generic=true` substitutes generic placeholder names (`CaptureNames`);
    `--extra-bool halcyonic.panelcapture=true` enables recording the real native Android panel in that
    process ([ADR 0029](../decisions/0029-workspace-is-complete-without-characters-and-continues-in-a-native-panel.md));
    `--extra-bool halcyonic.noask=true` suppresses startup microphone prompts (`MicrophoneAtOpen`).
- **`shot`, `clip`, `logs`, `glance`, and `preview`:** `shot` and `clip --seconds <n>` record stereo
  frames via `screenrecord` into `/data/local/tmp` (`umask 077`, no audio, no cloud sync), verify
  bytes on pull, and delete the remote file; `logs` filters Halora's own PID (`Unity`, OpenXR,
  `VrApi`); `glance` and `glance-token write|remove` manage the 2D glance; `preview <project id>` and
  `preview stop` manage Quest Browser loopback previews ([quest-browser-preview.md](../validation/quest-browser-preview.md)).

## Where time allows

Fit additional targeted suites between blocks with `Halcyonic.apk` and the private token installed:

- [XR_DEVELOPMENT.md](XR_DEVELOPMENT.md): "Pairing checks on a Quest", "Interaction and outside-text
  checks on a Quest", "The first-time journey on a Quest", "Hold to talk on a Quest", "Room placement
  checks on a Quest", "Sound checks on a Quest", "Beside a window on a Quest", "The interface on a
  Quest", and "Device measures on a Quest" (`pnpm quest:cold-start`, `pnpm quest:session`).
- [horizon-os-multitasking.md](../validation/horizon-os-multitasking.md): Virtual Display coexistence,
  Mac keyboard focus, immersive app switching, stage stability, pinch focus return, and recentering.
- [quest-3-device.md](../validation/quest-3-device.md) and
  [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md): remaining hardware verification rows.

## Close

1. If `Halcyonic-release.apk` is installed, reinstall `Halcyonic.apk` over it so `run-as` can clean
   private storage: `adb install -r apps/xr/Builds/Halcyonic.apk`.
2. Remove all private, backup, temporary, and shared-storage token files:

   ```bash
   adb shell run-as com.halcyonic.xr rm -f files/access-token files/access-token.off files/access-token.tmp files/access-token.new files/glance-access-token files/glance-access-token.tmp
   adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token
   ```

3. Run `pnpm quest:check -- --closed`. Pass: every line passes.
4. Disable proximity wake override (`adb shell am broadcast -a com.oculus.vrpowermanager.automation_disable`)
   and remove any test option files (`waiting-for-you-sound-while-away`, `sound-panned`, and
   `~/halcyonic-scenarios` on the Mac).
5. Leave the headset's ADB server on port `5038` running (`adb kill-server` disrupts shared tooling).
6. Pull session screenshots to a directory outside the repository:

   ```bash
   adb pull /sdcard/Oculus/Screenshots/<capture name> ~/halcyonic-captures/
   ```

7. Record commit SHA, date, headset model, Horizon OS build, validation results, and resolved
   [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md) rows.
