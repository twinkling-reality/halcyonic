# A headset session

One session on a Meta Quest, in one order, so the session refines what is already built and tested
off the headset instead of discovering how to run it. Everything here has passed on the Mac; the
session shows what only the headset can. It covers the access token, the demonstration's first
visit, the loopback proof, the glance, the menu plane's comfort, motion, the words Hold to talk
writes for one word, labels that read alike, Codex on a local model, a paired request that times
out, the judge's path and the experience from the eyes, and points to the other checks on a Quest to
fit in where time allows.
A check for work not yet on main says "if present" and how to tell; where the session's commit
lacks it, record "not in this build" and go on. Each step says what to do, what to look at, what passes, and where to
record it. Setup happens once at the start and teardown once at the end; the few steps that change
the setup on purpose say how to put it back.

What needs no person is in one command, run whenever a step says so:

```bash
pnpm quest:check
```

It prints one line per check, each a plain pass or FAIL, and exits 1 when any fails:
- one `adb reverse` mapping, 47800 to 47800;
- the access token files in the app's private storage at mode 600 and 44 bytes, read with `stat`
  through `run-as`, and no `.tmp` file a write left before its move;
- nothing at the token's old place on shared storage;
- from the running app's own log lines since it started: what the token's move did, whether the
  connection is live (the control plane proved it holds the token), the glance's last poll, and no
  cleartext or StrictMode line.

It reads modes and sizes only, never a token or a file's contents, and repeats no log line. A step
that expects something else says so: `--expect unproved` (or `refused`, `not-loopback`,
`unreachable`, `none`) for the connection, `--move moved` (or `kept`, `released`, `not-a-token`,
`nothing`) for the move, `--release` on the release build, and `--closed` at the end. It reads the
log since the app started. The app says at its start what the move did, "nothing was at the access
token's old place" included (builds from 2026-10-04), and the check reads the move only from that
line. Where it isn't there, the move line says it can't be judged from this log, a FAIL only where
the step names a move, and why: the headset's log has dropped the app's start, which it tells from
times alone (the main log's oldest stamp against the headset's clock less how long the app has run,
`ps -o ETIME`, and 2 seconds more), or the build is older than the line. Restart the app and check
at once. A `stat` reads a file as not there only where it names that file, so `run-as` failing to
reach the app's folder never reads as a file removed.

Rules for the whole session:
- Never print, paste or `cat` a token. Nothing here needs it.
- One reverse mapping, 47800 to 47800, and nothing else on the Mac forwarding to 47800 (`ssh -L`,
  `socat`, a proxy): anything that routes there can relay the control plane's proof
  ([SECURITY.md](../architecture/SECURITY.md)).
- One adb: `pnpm quest:check` uses Unity's, so put the same one first on the path, or two adb
  servers take turns and drop the mapping. In the session's terminals:
  ```bash
  export PATH="/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools:$PATH"
  ```
  (`HALCYONIC_ADB` points the tools at another adb, if the owner uses another.)
- Sit, or set a roomscale boundary. Leaning out of a stationary boundary made everything vanish in
  the sixth session, with no focus change logged; the boundary showing passthrough in the app's
  place is likely, not verified ([quest-3-device.md](../validation/quest-3-device.md)).
- Run the control plane without `--watch` (`pnpm start`), so a merge doesn't restart it mid-session.
- Keep captures out of the repository. `adb exec-out screencap` does not work on a Quest; Meta's
  capture service saves the wearer's view, so whoever is at the Mac runs this where a step says
  "capture", and the pictures are pulled once at the end ("Close"):
  ```bash
  adb shell am startservice -n com.oculus.metacam/.capture.CaptureService -a TAKE_SCREENSHOT
  ```
  A capture is a still: for motion, write down what you saw.
- Record as you go, in the record each step names. At the end, move what was shown from "Not
  verified" to "Verified" with the date, the main commit and the headset's model and OS build, and
  resolve each answered row of [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md) as its own rule says.

## Prepare, before the headset

1. **One commit.** `git switch main && git pull --ff-only && git log --oneline -1`. Record the
   commit; the APKs and the control plane all come from it.
2. **The development APK**, with the Unity editor closed:
   ```bash
   /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Editor.QuestBuild.BuildDevelopmentApk -logFile ~/Library/Logs/Unity/halcyonic-xr-build.log
   ```
   Pass: the log's `Halcyonic: built Builds/Halcyonic.apk …` line, which comes after Unity's own
   `Build Finished, Result: Success.` and the build's glance check (an exit status of 134 after
   `Exiting batchmode successfully now!` is harmless).
3. **The release APK**, for the judge's path (check 10), right after, so Unity runs twice before the
   session and never during it. Leave `HALCYONIC_VERSION_CODE` unset, so it keeps the project's code
   and installs over the development build and back:
   ```bash
   /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath "$PWD/apps/xr" -buildTarget Android -executeMethod Halcyonic.XR.Editor.QuestBuild.BuildReleaseApk -logFile ~/Library/Logs/Unity/halcyonic-xr-release.log
   ```
   Pass: `Halcyonic: built Builds/Halcyonic-release.apk …` in its log. Both APKs are signed with the
   same debug key, which is fine on the owner's headset and not for an upload
   ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "Before an upload"). Each Unity run stops the adb server,
   so build before connecting.
4. **The control plane from the same commit.** Stop the running one (Ctrl-C in its terminal), then
   `pnpm start`. Pass: its `control plane ready` line.
5. **The Mac's side.** `pnpm mac-setup`: read what it says of the folders, the agent apps, the local
   models (Ollama answering), voice, the companion and Salidium's credential. Ollama runs as
   [LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md) says (`OLLAMA_CONTEXT_LENGTH=65536 OLLAMA_NO_CLOUD=1
   ollama serve`, and `OLLAMA_MAX_LOADED_MODELS=2` with the companion); Salidium is 0.6.0 or later
   and running ("Connect Salidium"). Pass: nothing it flags matters for this session; voice is set
   up for checks 5 and 6, and a local model for check 8. Record what it flagged.
6. **Labels that read alike, if present** (check 7). This lists `WorkspaceText.cs`; no line means
   the session's commit lacks them, so skip this step and check 7:
   ```bash
   git grep -nF "AsShown(" -- apps/xr/Packages/com.halcyonic.client/Runtime/WorkspaceText.cs
   ```
   Copy the mock runtime's scenarios outside the repository and give `question_asked`'s first
   question four answers: "Café" with its accent composed (U+00E9), "Café" with it decomposed (an e
   then U+0301), a noncharacter (U+FFFE) alone, and "Dark":
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

1. `adb devices` lists the headset as `device`, not `unauthorized` (accept USB debugging in the
   headset). Record `adb shell getprop ro.product.model` and `adb shell getprop ro.build.display.id`.
2. Look at the token's old place before anything runs, names and sizes only:
   `adb shell ls -l /sdcard/Android/data/com.halcyonic.xr/files/`. Record whether an
   `access-token` is there: earlier sessions pushed one, and the next start deals with it.
3. Install, with exactly one mapping:
   ```bash
   adb install -r apps/xr/Builds/Halcyonic.apk
   adb reverse --remove-all
   adb reverse tcp:47800 tcp:47800
   ```
   If the install says `INSTALL_FAILED_VERSION_DOWNGRADE`, an upload's build with a higher code is
   on the headset: `adb uninstall com.halcyonic.xr`, which removes its data, then install again.
4. Write the token into the app's private storage with `run-as`, whole and private before it takes
   the old one's place, then start the app. The write is three commands
   ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "Install and connect", says why); the last prints
   `written`, and only then start the app:
   ```bash
   adb shell run-as com.halcyonic.xr rm -f files/access-token.tmp
   adb exec-in "run-as com.halcyonic.xr sh -c 'umask 077; mkdir -p files && rm -f files/access-token.tmp && cat > files/access-token.tmp'" < ~/.halcyonic/access-token
   adb shell "run-as com.halcyonic.xr sh -c 'for i in 1 2 3 4 5 6 7 8 9 10; do test \"\$(stat -c %s files/access-token.tmp 2>/dev/null)\" = 44 && break; sleep 1; done; if test \"\$(stat -c %s files/access-token.tmp 2>/dev/null)\" = 44 && chmod 600 files/access-token.tmp && mv -f files/access-token.tmp files/access-token; then echo written; else rm -f files/access-token.tmp; echo not written: the token file is not the 44 bytes the control plane makes, or it never arrived, so write it again; fi'"
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```
5. `pnpm quest:check`. Pass: every line passes; the glance token reads "not written" and the
   connection "live". The move reads "nothing was on shared storage", or, if step 2 found a token
   there, "a private token was there, and the shared one was removed unread"; a FAIL saying a copy
   is still there means the app may not remove a file `shell` made: remove it with
   `adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token` and record it. The
   control plane logs `realtime client connected` for `halcyonic-xr`.
   - Record in [headset-token-storage.md](../validation/headset-token-storage.md): the `run-as`
     write (mode 600, 44 bytes, the app reads it), how many seconds the last command waited for
     the second, if it printed late; whether `adb shell ps -A -o USER,NAME` still lists a `cat` as
     the app's user after `written`, since a `cat` that never sees the end of its input holds the
     token's file open until `adb kill-server`; and what was on shared storage, and what the start
     did with it.
   - Record in [xr-loopback-proof.md](../validation/xr-loopback-proof.md): the connection is live
     over `adb reverse`, so adb delivers the headset's connections to 127.0.0.1:47800, and the
     upgrade, the HMAC and the app's own HTTP run under IL2CPP.

The token is replaced once, in step 1.5, after the move check has put one on shared storage on
purpose; the token in use from then on never touched shared storage.

## The demonstration's first visit ([competition-judge-build.md](../validation/competition-judge-build.md))

The first time the demonstration plays on a headset, the menu opens by itself on Projects, once
(ADR 0026). Later steps play the demonstration too (a token set aside, a connection that fails
before it was live), and the headset remembers the visit in the app's data, so see it here, before
any of them. A judge sees it the same way on the release build. Check the build has it: this lists
`WorkspaceDirector.cs`, and no line means record "not in this build" and go on to the checks.

```bash
git grep -nF "halcyonic.demo.welcomed" -- apps/xr/Assets
```

1. **Play the demonstration.** Set the token aside and restart the app:
   ```bash
   adb shell run-as com.halcyonic.xr mv files/access-token files/access-token.off
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```
2. **The welcome.** Touch nothing. Pass: the menu opens on Projects, under "What would you like to
   work on?", with New project at its right. Its one row reads "Storefront API" and "3 tasks not
   started", and every character's label reads "Not started" with the Demo mark. The stage's lines,
   "Demo: recorded work played on this headset. Nothing here is live." and "It follows your answers.
   Nothing reaches an agent.", stand above the characters, clear of every body and of the menu, and
   read without leaning back. Capture.
3. **Work starts.** Within about a second two characters start working, and the row reads "2 tasks
   running". If present (this lists `ProjectsText.cs`), press the row while they work, within about
   5 seconds: its side panel's Its work reads "2 tasks running, 1 not started". Close details.
   ```bash
   git grep -nF "var paused = project.Work - project.NeedsYou" -- apps/xr/Packages/com.halcyonic.client/Runtime/ProjectsText.cs
   ```
4. **A task comes to wait.** At about 7 seconds "Add rate limiting to the sign-in endpoint" waits.
   Pass: its character rises and its badge changes from Working to Waiting for you as a quick
   cross-fade, never a jump; Tasks, at the menu's top, takes its amber dot; the menu stays on
   Projects; and the raised lines stay clear of the risen character. Capture. If present (step 3),
   the row's side panel reads "1 task waiting for you, 2 paused".
5. **The lines come back down.** Close the menu: the bar stands under the stage, and the lines hang
   under the labels again. Look at a character until its peek shows: the lines step aside for it.
   Open the menu again: they rise again.
6. **Log.** `adb logcat -d -s Unity | grep "Halcyonic: demonstration plays from its beginning"`:
   one line. Put the token back and restart the app:
   ```bash
   adb shell run-as com.halcyonic.xr mv files/access-token.off files/access-token
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```
   `pnpm quest:check`: live again.

Record in competition-judge-build.md: the welcome as a judge first sees it, and whether the raised
lines read as part of the stage.

## The checks

### 1. Token storage ([headset-token-storage.md](../validation/headset-token-storage.md))

1. **The write survives a reinstall, and nothing reads as nothing.** `adb install -r
   apps/xr/Builds/Halcyonic.apk`, then start the app. `pnpm quest:check -- --move nothing`. Pass:
   the access token passes, the move reads "nothing was on shared storage", and the connection is
   live. Record both: the token survives `install -r`, and nothing on shared storage read as nothing
   there (`ENOENT`).
2. **The move.** Put a token where earlier builds kept it, with no private token, and start the app:
   ```bash
   adb shell run-as com.halcyonic.xr rm -f files/access-token
   adb push ~/.halcyonic/access-token /sdcard/Android/data/com.halcyonic.xr/files/access-token
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```
   `pnpm quest:check -- --move moved`. Pass: every line passes. A FAIL line says which way it went:
   - "a copy is still on shared storage": the app may not remove a file `shell` made. Remove it with
     `adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token`, and record that.
   - "(open failed with EACCES)": the app may not read it. Remove it the same way, and record that.
   - Any other call and errno is a failure to look into: record it whole.
   On a paired headset, the same holds; repeat it there if the session includes the pairing checks
   (below, "Where time allows").
3. **Links and pipes.** Set the private token aside, so the start looks at what is on shared storage
   instead of keeping its own, and try a link at the old place:
   ```bash
   adb shell run-as com.halcyonic.xr mv files/access-token files/access-token.off
   adb shell ln -s /dev/null /sdcard/Android/data/com.halcyonic.xr/files/access-token
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```
   Record whether `ln -s` could make it. If it could, `pnpm quest:check -- --move not-a-token
   --expect none`: the app started at once, and the move reads "was not a token, and was removed
   unused". Then the same with a pipe in place of the link,
   `adb shell mkfifo /sdcard/Android/data/com.halcyonic.xr/files/access-token`, which must not hold
   the app up. Clean up and put the token back:
   ```bash
   adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token
   adb shell run-as com.halcyonic.xr mv files/access-token.off files/access-token
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```
   `pnpm quest:check`: live again.
4. **Record only.** Whether a Meta or Horizon backup ever copied the shared file (the app sets
   `android:allowBackup="false"`), if the owner's account shows backups; otherwise "not known".
   The mode the app's umask gives a new file before its `chmod` can't be seen from adb: record "not
   seen".
5. **Replace the token once, and a stale token on the headset.** On the Mac: stop the control plane,
   `rm ~/.halcyonic/access-token`, then `pnpm start`, which makes a new one. Restart the app, which
   still holds the old one:
   ```bash
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```
   `pnpm quest:check -- --expect unproved`. Pass: the line above the stage says "This headset's
   access code doesn't match your computer's, or something else is answering in its place …" and the
   app stops trying; the control plane logs no `realtime client connected` and no 401. Then write the
   new token as in Connect step 4, and `pnpm quest:check`: live. Record the stale token in
   xr-loopback-proof.md and the replacement in headset-token-storage.md. Every other headset that
   held the old token needs the new one the same way; paired headsets keep their own credential.

### 2. The loopback proof and the realtime upgrade ([xr-loopback-proof.md](../validation/xr-loopback-proof.md))

1. **In use.** Open a workstream's history and the choice of folders. Pass: both load, and
   `pnpm quest:check` reads live. Record: REST and the realtime upgrade each prove the control plane
   first, under IL2CPP.
2. **Another program on the port.** Stop the control plane, and in a second terminal run a listener
   that answers every request without a proof:
   ```bash
   while true; do printf 'HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n' | nc -l 127.0.0.1 47800; done
   ```
   Restart the app. `pnpm quest:check -- --expect unproved`. Pass: the app shows the "doesn't match
   … something else is answering" line and stops, and the listener's terminal shows only
   `GET /api/health` with an `x-halcyonic-challenge` header, never `Authorization`. Ctrl-C it.
3. **A listener that never answers.** `nc -l 127.0.0.1 47800` in the second terminal, restart the
   app, `pnpm quest:check -- --expect unreachable`. Pass: "Can't reach your computer; trying again",
   and the app keeps trying. Ctrl-C it.
4. **A paused control plane.** `pnpm start`, wait for `control plane ready`, then pause whatever
   listens on 47800, which is this control plane and no other worktree's:
   `kill -STOP $(lsof -tiTCP:47800 -sTCP:LISTEN)`, and restart the app.
   `pnpm quest:check -- --expect unreachable`. Pass: "Can't reach your computer" within a few
   seconds. Then `kill -CONT $(lsof -tiTCP:47800 -sTCP:LISTEN)`; pass: `pnpm quest:check` reads live
   within the retry delay. Record: under Mono, closing a connection ends a read that is never
   answered.

### 3. The glance ([horizon-os-multitasking.md](../validation/horizon-os-multitasking.md))

The steps are [XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "The glance on a Quest (spike)", 1 to 14, in
this order. First write its token the same careful way, three commands, and wait for `written`:

```bash
adb shell run-as com.halcyonic.xr rm -f files/glance-access-token.tmp
adb exec-in "run-as com.halcyonic.xr sh -c 'umask 077; mkdir -p files && rm -f files/glance-access-token.tmp && cat > files/glance-access-token.tmp'" < ~/.halcyonic/access-token
adb shell "run-as com.halcyonic.xr sh -c 'for i in 1 2 3 4 5 6 7 8 9 10; do test \"\$(stat -c %s files/glance-access-token.tmp 2>/dev/null)\" = 44 && break; sleep 1; done; if test \"\$(stat -c %s files/glance-access-token.tmp 2>/dev/null)\" = 44 && chmod 600 files/glance-access-token.tmp && mv -f files/glance-access-token.tmp files/glance-access-token; then echo written; else rm -f files/glance-access-token.tmp; echo not written: the token file is not the 44 bytes the control plane makes, or it never arrived, so write it again; fi'"
```

Open the glance (step 1), then `pnpm quest:check`. Pass: the glance token passes, the glance
"polled ok", and no cleartext or StrictMode line. That is also the safety section's first check.

1. **Notify over a game** (steps 2, 3, 6, 7 and 8): the window over a game, the toast within one
   poll and heard, opening Halcyonic from the toast and the button, Do Not Disturb, and whether a
   notification ever fails (`pnpm quest:check` fails its glance line if one did).
2. **The poller while minimised** (steps 4, 5 and 14): `glance polled` lines continue at the hidden
   pace and stop when the window closes; `pnpm quest:session -- --minutes 60` for how long it lives.
3. **The watchdog** (step 9): the poll ends `too_slow` near 10 seconds against a listener that answers
   one byte a second. `pnpm quest:check` reads the mapping as wrong while it points there; put
   `adb reverse tcp:47800 tcp:47800` back after, and the check passes again.
4. **adbd and the token file** (steps 10 and 11): 127.0.0.1:47800 on loopback only, and
   `token_not_private` for a link in place of the file (the check's glance line reads
   `last poll token_not_private` then); write the glance token again after.
5. **Recents and capture, and backups** (steps 12 and 13).
6. **No cleartext lines.** Restart the app and the glance, then `pnpm quest:check` once more.

Record each in horizon-os-multitasking.md.

### 4. The menu plane's comfort (ADR 0026, [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md))

The menu, a task's file and a side panel stand on one plane (ADR 0026), hosted by the workspace
since main 0bf6820. Check the session's build has it: this lists `WorkspaceDirector.cs`, and no line
means only the editor's renders make it, so record "not in this build" for each item and walk
[XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "The interface on a Quest", instead.

```bash
git grep -nF "MenuDirector.Create(" -- apps/xr/Assets | grep -v /Editor/
```

Walk it in the recorded demonstration, which has a waiting question and recorded Changes and Checks
answers to open side panels from; live tasks on the mock runtime have no such answers. Set the token
aside and restart the app, which then plays the demonstration:

```bash
adb shell run-as com.halcyonic.xr mv files/access-token files/access-token.off
adb shell am force-stop com.halcyonic.xr
adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
```

Where everything is, all with a pinch:
- **The bar.** With no file open, the menu is its bar: what waits for you, or "Nothing is waiting
  for you.", and Open. It stands where you looked as the menu closed, or straight ahead under a
  window's lane beside a window, and stays there as you turn (Settings' Reset position brings the
  plane to where you look). Press it to open the menu, on Tasks if something waits, else on the
  place chosen last.
- **The places,** across the menu's top: Tasks, Projects, Usage and Settings.
- **Tasks:** every task, what waits first. A row opens its task's file.
- **A file:** press a character, or its row in Tasks. The file opens beside the menu, and the plane
  moves beside that character, with the light line from under its label to the file. Its sections
  are Waiting, Activity, Changes and Checks. Press the character again to close the file.
- **A side panel:** in a file's Changes or Checks, press an answer's line: its whole answer opens in
  the side panel beside the page. In Waiting, a chosen answer cut short shows all its words there.
  Where the side panel and its file don't both fit, the side panel takes the file's place, with
  text a step larger.
- **New project and Projects:** in Projects, New project is the main action while no row is chosen;
  a project's row offers adding a task to it, which opens New project for it. The demonstration
  can't start new work, so read the flow without building.
- **Text size:** Settings, Comfort, Text size, Make text larger: text 15 percent larger, and 3 rows
  a page. Put it back the same way after item 7.

Seated, in this order, so nothing is set up twice: look, then record yes or no with a sentence of
why, in the OPEN_QUESTIONS row and ADR 0026.
1. **Reading at 0.46 m.** Open the menu on Tasks: its text reads without leaning in or squinting.
2. **The light line.** Open the waiting task's file from its character: the line from its label to
   the file reads calm, never drawing the eye on its own.
3. **The 50 degree low edge.** The file's lowest rows and the menu's beside it, about 50 degrees
   below eye level, read without strain.
4. **Turning to a side panel.** In Changes or Checks, open an answer's side panel and turn between
   the file and it, and back: easy, with no hunting.
5. **Unseen hit areas.** Pinch at the edges of the bar, a place, a row, a file's section and a side
   panel's controls: each lands where intended, and nowhere it shouldn't.
6. **Glass in passthrough.** Face a bright wall, close the menu to its bar and open it again so it
   stands against the wall: the glass keeps its contrast and the text stays readable.
7. **The head tip, at the larger size.** Make text larger, then read Tasks and a file's lowest row
   again: reaching it needs no more than a small tip of the head (a Quest 3S needs about 5 degrees,
   inside the 8 its field allows). Record the `device view field` line's numbers for both eyes and
   whether items 1 and 3 still hold. Put the text size back.
8. **Turning back to the closed bar.** In a build where the closed bar stays where the menu closed
   rather than following your head, this lists `ResetPosition`; no line means the bar still follows,
   so record "not in this build":

   ```bash
   git grep -nF "public void ResetPosition()" -- apps/xr/Assets/Halcyonic/Workspace/MenuDirector.cs
   ```

   Restart the app so the demonstration plays from its beginning, close the menu to its bar, and
   turn the chair until the bar is behind you. When a character comes to wait, find your way back
   to the bar and open the menu. Judge whether turning back to it is acceptable, or whether the menu
   needs a summon gesture that brings it to where you look, and whether the waiting character's glow
   and its Waiting for you sound were enough to find your way back (if it was already waiting when
   you turned, only its glow called). Record the answer in the closed bar's OPEN_QUESTIONS row.
9. **Dragging the menu.** In a build where the menu's plane can be dragged, this lists
   `MenuDrag`; no line means it can't, so record "not in this build":

   ```bash
   git grep -nF "public sealed class MenuDrag" -- apps/xr/Packages/com.halcyonic.client/Runtime/MenuDrag.cs
   ```

   Open a task's file beside the menu, then hold the file's subject plate, its title, for about a
   third of a second and move your hand: the whole plane comes round you at the same distance,
   still facing you. Drag it toward a character or its label, toward the edge of your view, and so
   its light line would cross another character: it should stop there rather than go. Let go, then
   put it back with Settings, Your space, Reset position, which brings it to where you look. Judge
   whether the drag feels right: easy to start, following the hand, stopping sensibly where it
   can't go. Then judge whether being able to drag the menu answers item 8's question, or turning
   back to the closed bar is still a problem. Record both in the closed bar's OPEN_QUESTIONS row.

A failure falls back as ADR 0026 says (a page shows 3 rows). Put the token back and restart the app:

```bash
adb shell run-as com.halcyonic.xr mv files/access-token.off files/access-token
adb shell am force-stop com.halcyonic.xr
adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
```

`pnpm quest:check`: live again.

### 5. Motion ([quest-3-device.md](../validation/quest-3-device.md), ADR 0027)

In the sixth session the owner said "nothing moves". Since ADR 0027 the interface moves to answer the
person and to show a wait, and nothing else. Check the build has it: this lists `Glaze.cs`, and no
line means record "not in this build".

```bash
git grep -nF "ListeningPulseSeconds" -- apps/xr/Packages/com.halcyonic.client/Runtime/Glaze.cs
```

Live, on the development build with the token in place, voice set up on the Mac.
1. **Slides.** Open the menu from its bar, then a task's file from Tasks, then close the file and
   the menu. Pass: each part slides a short way into place, easing in and out, nothing overshoots
   or bounces, and nothing moves while you read.
2. **A state changing.** `pnpm demo --scenario approval_required` on the Mac. Pass: the character's
   badge goes from Working to Waiting for you as a quick cross-fade, its word readable throughout,
   then Waiting for you breathes.
3. **Hold to talk's three states.** `pnpm demo --scenario question_asked`, then open its file on
   Waiting. Hold Hold to talk. Pass: it reads "Listening", its cap filled and its words in the
   active blue, its microphone pulsing; capture while held. Say "Dark", let go. Pass: it shows the
   transcribe icon and "Writing down", a band of brightness sweeping across its words; capture. It
   keeps its width and place throughout, and no line is added to the page.
4. **Keep things still.** Settings, Comfort, Motion, Keep things still, then steps 2 and 3 again.
   Pass: the breath, the turning icons, the microphone's pulse and the shimmer stop, a wait's words
   standing highlighted in the active tone instead; every character stands at rest, its surface,
   its sweep ring and Waiting for you's halo still, and Working, Running tests and State unknown
   still tell apart by eyes, light and words; slides, presses, a file opening and the badge's
   cross-fade remain. Put it back: Let things move.
5. **The owner's judgement.** Does it move enough now, too much, or anywhere it shouldn't? Does the
   shimmer read as noise beside the agent's own words, or the pulse draw the eye from the page?

Record in quest-3-device.md, beside the sixth session's "nothing moves", and any value to change in
ADR 0027's consequences.

### 6. The words heard for one word ([voice-transcription.md](../validation/voice-transcription.md))

On the same `question_asked` file, on its colour scheme question (its answers are Light and Dark,
and it takes typed words). For each of "Dark", "Dark", "Dark", "Light" and "Yes": hold Hold to talk,
say the one word, let go, and read what lands in the typed row, character for character: a capital
added or not, a period or any other mark at its end. Capture one. Record each word before the next
hold, and send nothing until the last; then press Send answer and record what the file says was sent.

Record the words exactly as shown, never the clip, in voice-transcription.md: they decide whether a
spoken answer can match an answer's label as written.

### 7. Labels that read alike, under IL2CPP (if present)

Only with Prepare step 6 done. The headset compares answers as their rows show them, composed with
Mono's `String.Normalize(NormalizationForm.FormC)`, which only an IL2CPP build on the headset runs.
Stop the control plane and start it on the scenarios' copy:

```bash
HALCYONIC_MOCK_SCENARIOS_DIR="$HOME/halcyonic-scenarios" pnpm start
```

If it refuses the copy, record its message: the labels never reached the headset. Then
`pnpm demo --scenario question_asked`, and open its file on Waiting.
1. **The two Cafés.** Pass: they show as rows that can't be chosen, a line under the answers says
   why, and Stop stands on Waiting as the way on; "Dark" can be chosen. That they read alike means
   `Normalize` composed the decomposed one on the headset. Capture.
2. **The noncharacter.** Pass: its row shows ‹U+FFFE›, and the file draws its page.
3. **The log.** `adb logcat -d -s Unity | grep -i -E "exception|normaliz"`: nothing from the file.
   Record any line whole.

Stop the control plane and `pnpm start` it again without the variable. Record in
quest-3-device.md: whether `String.Normalize` composed under IL2CPP, and what U+FFFE did.

### 8. Codex as a second agent app, on a local model (if registered)

Codex runs only on a model the Mac serves ([LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md#codex)).
Pass first: the control plane's `control plane ready` line lists the runtime `codex`, and its
`agent_binaries` reads `matches` for it. Otherwise record "not registered" and go on.

In a project with a folder, add a task (Projects, the project, Add a task), choose Codex and the
local model in More options, and start it with a small change, such as adding a README line.
Pass: its character starts and works under Codex's name with no Practice mark; whatever it asks
waits in its file and is answered there; its round finishes, or says why it couldn't; its
Activity shows what it did. Capture its file once it has finished.

Record in [codex-capabilities.md](../validation/codex-capabilities.md) and
[local-models.md](../validation/local-models.md): how long to its first activity and to the
round's end, what it asked, and whether the local model finished the change.

### 9. A paired request that times out (if the session pairs, [network-pairing.md](../validation/network-pairing.md))

After [XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "Pairing checks on a Quest", with the headset paired
and live over Wi-Fi, and the app restarted so no file's earlier activity has been read yet. A
request over the pinned transport (`PinnedHttpHandler`) that outlives its 15 seconds should end
cancelled, and Mono may fault instead.
1. Pause the control plane: `kill -STOP $(lsof -tiTCP:47800 -sTCP:LISTEN)`.
2. At once, press a character to open its file. Its Activity reads earlier activity, and after
   about 15 seconds says "earlier activity unavailable, reopen to try again".
3. `adb logcat -d -s Unity | grep "earlier activity could not be read"`. Pass: it names
   `TaskCanceledException` or `OperationCanceledException`, a cancel. Any other type means Mono
   faulted where the handler expects a cancel: record it whole.
4. `kill -CONT $(lsof -tiTCP:47800 -sTCP:LISTEN)`: live again within the retry delay.

Record in network-pairing.md, and in the pinned transports' row of OPEN_QUESTIONS.md.

### 10. The judge's path, on the release build ([competition-judge-build.md](../validation/competition-judge-build.md))

1. **A release build over development data.** Set the token aside (a release build still reads a
   private token), stop the control plane, put a token-shaped stand-in at the old place (the release
   build removes it unread, so it needs no real token), and install the release build over the
   development one:
   ```bash
   adb shell run-as com.halcyonic.xr mv files/access-token files/access-token.off
   adb shell "printf 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_-Z\n' > /sdcard/Android/data/com.halcyonic.xr/files/access-token"
   adb install -r apps/xr/Builds/Halcyonic-release.apk
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```
   `pnpm quest:check -- --release`. Pass: every line passes: `run-as` is refused, the move reads "a
   release build removed the shared one unread", and there is no connection. A copy the app could not
   remove reads as in step 1.2; remove it from the Mac the same way. Record in
   headset-token-storage.md.
2. **The walk.** Hands only, no token, no pairing: the walk in
   [XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "The demonstration judges see". Its welcome was seen in
   "The demonstration's first visit" and doesn't come again, so the walk starts from the closed bar. Look at the timeline in
   competition-judge-build.md. Pass: each beat when the timeline says. Record: whether a newcomer
   finds the satisfying moment, the field of view split as the headset shows it, and a clean resume
   after sleep, the system menu and another app.
3. **The file's confirmations,** after the walk, in the same demonstration, on the waiting task's
   file. Stop comes last, since its Yes ends the story.
   1. *Another window drops a confirmation.* Press Approve and read to the request's second part,
      then open the system menu, or another app, and come back. Pass: the page reads "You went to
      another window, so nothing was sent. Press it again to confirm." Pressing Approve again reads
      the request from its first part, and an answer already chosen on the question stays chosen.
   2. *At the larger text size the request stays put.* Settings, Comfort, Text size, Make text
      larger; press Approve and read to the last part. Pass: Yes appears, and the parts neither lay
      out again nor jump back to the first part as it does. Put the text size back. At the standard
      size the request fits one part, so this can't be seen there.
   3. *Stop asks first.* On Activity, press Stop. Pass: Cancel stands in Stop's place and "Yes, stop"
      in the middle. Press Cancel: nothing is sent and the recording goes on. Press Stop again and
      leave it: after about 15 seconds the ask drops and the page reads "Nothing was sent: you
      didn't confirm in time. Press it again." Last, press Stop and "Yes, stop", and record what the
      task shows; restart the app before walking the demonstration again.

### 11. The experience, from the eyes (ADR 0013, ADR 0026, ADR 0027; the owner's picks of 2026-10-08)

About 20 minutes with the headset kept on, after the checks above, so the owner judges what was built from
"One kit, four forms" and its outside words against their own tests: clean, makes sense, not crowded,
distinct, and "wow this is the future". Lane V wrote it; the owner's words on each step are the record.
Whoever is at the Mac runs the commands and writes down what the owner says. Each part names what it
needs; where the build lacks it, record "not in this build" and go on. What is in, from the session's
commit:

```bash
git grep -nF "PartsAfterDraw" -- apps/xr/Packages/com.halcyonic.client/Runtime/Glaze.cs
git grep -nF "Keep things still" -- apps/xr/Packages/com.halcyonic.client/Runtime/Comfort.cs
git grep -nF "BlinkSeconds" -- apps/xr/Packages/com.halcyonic.client/Runtime/Glaze.cs
git grep -nF "Something new" -- apps/xr/Packages/com.halcyonic.client/Runtime/
git grep -nF "format_quote" -- apps/xr/tools/glaze_icons.py
git log --oneline -40
```

The first lines say the opening, Keep things still, the stage's new look, the first visit and outside
words are in; the log names the rest by its lane's words (looking back, places and travel, the
spatializer, and the trials behind development settings). Keep OVR Metrics' frame rate in view
throughout: it stays at 72 with six characters on the stage, or the part where it drops is recorded and
the trials are skipped.

**A. The first minute** (the first visit; needs a computer with no task). Stop the control plane and
start it on a fresh data folder holding a copy of the Mac's settings, never printed:

```bash
mkdir -m 700 ~/halcyonic-fresh && cp -p ~/.halcyonic/settings.json ~/halcyonic-fresh/
HALCYONIC_DATA_DIR=~/halcyonic-fresh pnpm start
```

It makes its own access token: put it on the headset as check 1's step 5 does, then restart the app.
1. **Put it on.** Pass: one plate below eye level asks "What would you like to work on?", Something new
   chosen, A project on your computer under it, and Close, Hold to talk and Start a project; nothing on
   the stage; "Connected to your computer" above the stage for about 3 seconds, then gone. The plate's
   parts arrive top to bottom in about a quarter of a second, nothing sliding.
2. **Settings from the plate.** Settings stands alone at the right end of the row of places. Choose it,
   make text larger and standard again, then Close. Pass: the bar reads "Nothing is running yet"; its
   Open brings the plate back.
3. **Say the idea.** Hold Hold to talk, say a short idea, let go. Pass: New project opens alone on Your
   idea, the words as yours. Close it there; the full start is check 10's ground.
4. **The owner's judgement.** Is it clear what to do, calm, and does an empty desk read as broken?

Stop the control plane, start it again on `~/.halcyonic` (`pnpm start`), put its token back as before,
restart the app, and remove the fresh folder at the end ("Close").

**B. The stage, by eye** (the owner's own tasks; the stage's new look, then looking back).
5. **At rest.** Look across the stage without stopping on a character. Pass: each stands on its ring on
   the desk with a soft shadow under it, no glow behind it; the ring's colour and the pill's word say
   its state; no title shows; working ones float slowly on a thread of light and blink now and then.
6. **A look back.** Rest your eyes on a working character for half a second. Pass: its eyes come to you,
   then it turns and lifts a little; its label grows into the glance: the pill, its title leaning, what
   its agent last said under Agent says, its project. Look away: it holds your look for half a second,
   then goes back to its work. Look at a Can't tell yet or Couldn't finish: it doesn't answer.
7. **A sweep.** Turn your head slowly across the whole stage and back. Pass: nothing looks back at you
   unless you stopped on it. Count any that did.
8. **The owner's judgement.** Alive, or busy? Does a look back feel like being noticed, or watched? Is
   the floating calm in the corner of the eye?

**C. A question, from start to confirmation.** `pnpm demo --scenario question_asked` on the Mac.
9. **Where the sound comes from.** Before it comes, close your eyes. When Waiting for you sounds, point
   at it, then open them. Pass: you point at the character. Do it once with the spatializer, then
   once with Unity's panning, and say which you could place, and whether a cue above or behind read
   as above or behind. The start's log line says which is in use:
   `adb logcat -s Unity | grep --line-buffered "Halcyonic: sound placed by"`, "Meta XR Audio's
   head-related spatializer" or "Unity's panning, because of the option sound-panned". The option is
   a file in the app's data directory, `Application.persistentDataPath`, which on the headset is
   shared storage, as for the while-away option, not the private folder `run-as` reaches (the
   token's). To pan:
   ```bash
   adb shell touch /sdcard/Android/data/com.halcyonic.xr/files/sound-panned
   adb shell ls -l /sdcard/Android/data/com.halcyonic.xr/files/sound-panned
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```
   To go back to the spatializer, and before the session ends:
   ```bash
   adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/sound-panned
   adb shell am force-stop com.halcyonic.xr
   adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
   ```
   Then the cost, with six characters on the stage and their cues sounding, each way for a minute:
   `adb shell top -H -b -d 1 -n 60 -p $(adb shell pidof com.halcyonic.xr) > ~/halcyonic-captures/audio-<way>.txt`,
   reading the audio mixer thread's share (Unity's FMOD mixer thread; record its name as `top` shows
   it). Keep the spatializer only if it is clearly better by ear and its extra cost stays under 1 ms
   of CPU a frame (about 7% of one core at 72 frames a second). If it is silent, distorts or crashes
   while cues play, put the file in place as above: every cue then goes through Unity's panning, with
   no rebuild. If the app crashes at start before any cue, the file cannot help, since Unity loads
   the spatializer as its audio starts: install the previous APK and record the crash
   (`adb logcat -b crash`).
10. **It comes to you.** Pass: it comes once to the front of the desk, amber, looking at you; its
    project-mates look at it once as it goes; its glance shows its question under Agent asks.
11. **Its file.** Pinch it. Pass: a line of light draws from it, then the file's parts arrive top to
    bottom, alone, the menu staying closed. Is a third of a second too slow?
12. **The answer.** Choose one and Send answer. Pass: the cap sinks at once, "Sent…" shimmers, and only
    when your computer confirms does the character turn once and go back to work.
13. **A request.** `pnpm demo --scenario approval_required`, then the same path with Approve. Pass: the
    request's own words lean on their ground; Halcyonic's stand upright.

**D. Keep things still.** Settings, Comfort, Motion, Keep things still; then 10 to 12 again.
14. Pass: nothing floats, blinks, breathes or shimmers; "Sent…" stands in the active blue; a character
    fades from home to the front instead of travelling and makes no turn; a look back moves only its
    eyes; presses and the file's opening remain. Put it back: Let things move.

**E. Words from outside, over a bright room.** Face a window or a lit wall.
15. Pass: a leaning title and an agent's answer read at a glance on the plane and on a glance, and the
    ground's edges show; the owner says whether the slant blurs at the smallest size.

**F. The trials, behind development settings** (only if present; one at a time).
16. **Hands in front.** With depth occlusion on, move a hand slowly in front of a character. Pass: the
    hand hides it cleanly; record flicker at its edges and the frame rate.
17. **The room quieting.** With it on, open a file. Pass, or not: the owner says whether the darker room
    helps focus or feels heavy. It stays off unless they ask to keep it.

**G. The verdict.** In the owner's words: which moments felt like the future, which felt fake, busy or
crowded, and what to cut before the judge build.

Record in quest-3-device.md as its own session; the owner's words beside each step; the values to change
(the look's dwell, the opening's time, the travel speed, the float) in ADR 0013's and ADR 0027's
consequences; and the OPEN_QUESTIONS.md rows on calm motion, outside words over a bright room, and the
shimmer under Keep things still, each resolved or narrowed as its own rule says.

## Where time allows

Each is its own section with what to see and record; fit them in between blocks, with the
development build and the token in place:
- [XR_DEVELOPMENT.md](XR_DEVELOPMENT.md): "Pairing checks on a Quest", "Milestone 3 checks on a
  Quest", "The first-time journey on a Quest", "Hold to talk on a Quest", "Room placement checks on a
  Quest", "Sound checks on a Quest", "Beside a window on a Quest", "The interface on a Quest" and
  "Device measures on a Quest" (`pnpm quest:cold-start`, `pnpm quest:session`).
- [horizon-os-multitasking.md](../validation/horizon-os-multitasking.md), "To check on the headset",
  1 to 6: a Virtual Display beside the characters, focus while typing on the Mac, another immersive
  app, the stage staying put, returning focus with a pinch, recentering.
- [quest-3-device.md](../validation/quest-3-device.md): what it lists as not exercised.
- [OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md): the rows a session answers, such as calm
  motion, windows over the desk, sound while away, arrangements, the fold and its grace, the
  closed bar after turning away, the passthrough window and returning focus, cues during focus, repeating Waiting, and the
  socket across sleep, look and pinch, and recentering.

## Close

1. If the release build is installed, put the development build back over it, which keeps the data
   and lets `run-as` reach it: `adb install -r apps/xr/Builds/Halcyonic.apk`.
2. Remove the tokens, anything half written, and anything left on shared storage:
   ```bash
   adb shell run-as com.halcyonic.xr rm -f files/access-token files/access-token.off files/access-token.tmp files/access-token.new files/glance-access-token files/glance-access-token.tmp
   adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token
   ```
3. `pnpm quest:check -- --closed`. Pass: every line passes.
4. Undo what the session turned on: `adb shell am broadcast -a
   com.oculus.vrpowermanager.automation_disable` if the headset was kept awake, and remove the sound
   option files if they were made: the while-away one ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "Beside
   a window on a Quest") and `sound-panned` (check 11, step 9).
5. `adb kill-server`. The Mac's adb server answers every local account while it runs.
   If check 11's first part made `~/halcyonic-fresh`, stop any control plane using it and remove it:
   `rm -rf ~/halcyonic-fresh`.
6. Pull the captures to a folder outside the repository:
   ```bash
   adb pull /sdcard/Oculus/Screenshots ~/halcyonic-captures
   ```
7. Record: the validation records above, the OPEN_QUESTIONS rows the session answered, and the
   commit, date and headset.
