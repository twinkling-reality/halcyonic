# A headset session

One session on a Meta Quest, in one order, so the session refines what is already built and tested
off the headset instead of discovering how to run it. Everything here has passed on the Mac; the
session shows what only the headset can. It covers the access token, the loopback proof, the
glance, the menu plane's comfort and the judge's path, and points to the other checks on a Quest to
fit in where time allows. Each step says what to do, what to look at, what passes, and where to
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
log since the app started. The app writes no line when nothing is on shared storage, so the move is
read as nothing there only from a log that still holds the app's start, which it tells from times
alone: the main log's oldest stamp against the headset's clock less how long the app has run (`ps
-o ETIME`). Where the headset's log has already dropped the start, the move line says it can't be
judged from this log, a FAIL only where the step names a move; restart the app and check at once.

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
- Keep captures out of the repository.
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
3. **The release APK**, for the judge's path (step 5), right after, so Unity runs twice before the
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
   and running ("Connect Salidium"). Pass: nothing it flags matters for this session. Record what it
   flagged.

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
   the old one's place, then start the app. The write is two commands, each quoted whole
   ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "Install and connect", says why); the second prints
   `written`, and only then start the app:
   ```bash
   adb exec-in "run-as com.halcyonic.xr sh -c 'umask 077; mkdir -p files && cat > files/access-token.tmp'" < ~/.halcyonic/access-token
   adb shell "run-as com.halcyonic.xr sh -c 'for i in 1 2 3 4 5 6 7 8 9 10; do test \"\$(stat -c %s files/access-token.tmp 2>/dev/null)\" = 44 && break; sleep 1; done; test \"\$(stat -c %s files/access-token.tmp)\" = 44 && chmod 600 files/access-token.tmp && mv -f files/access-token.tmp files/access-token && echo written || echo not written, write it again'"
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
     write (mode 600, 44 bytes, the app reads it), how many seconds the second command waited for
     the first, if it printed late; and what was on shared storage, and what the start did with it.
   - Record in [xr-loopback-proof.md](../validation/xr-loopback-proof.md): the connection is live
     over `adb reverse`, so adb delivers the headset's connections to 127.0.0.1:47800, and the
     upgrade, the HMAC and the app's own HTTP run under IL2CPP.

The token is replaced once, in step 1.5, after the move check has put one on shared storage on
purpose; the token in use from then on never touched shared storage.

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
this order. First write its token the same careful way, and wait for `written`:

```bash
adb exec-in "run-as com.halcyonic.xr sh -c 'umask 077; mkdir -p files && cat > files/glance-access-token.tmp'" < ~/.halcyonic/access-token
adb shell "run-as com.halcyonic.xr sh -c 'for i in 1 2 3 4 5 6 7 8 9 10; do test \"\$(stat -c %s files/glance-access-token.tmp 2>/dev/null)\" = 44 && break; sleep 1; done; test \"\$(stat -c %s files/glance-access-token.tmp)\" = 44 && chmod 600 files/glance-access-token.tmp && mv -f files/glance-access-token.tmp files/glance-access-token && echo written || echo not written, write it again'"
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

A failure falls back as ADR 0026 says (a page shows 3 rows). Put the token back and restart the app:

```bash
adb shell run-as com.halcyonic.xr mv files/access-token.off files/access-token
adb shell am force-stop com.halcyonic.xr
adb shell am start -n com.halcyonic.xr/com.unity3d.player.UnityPlayerGameActivity
```

`pnpm quest:check`: live again.

### 5. The judge's path, on the release build ([competition-judge-build.md](../validation/competition-judge-build.md))

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
   [XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "The demonstration judges see". Look at the timeline in
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
   adb shell run-as com.halcyonic.xr rm -f files/access-token files/access-token.off files/access-token.tmp files/glance-access-token files/glance-access-token.tmp
   adb shell rm -f /sdcard/Android/data/com.halcyonic.xr/files/access-token
   ```
3. `pnpm quest:check -- --closed`. Pass: every line passes.
4. Undo what the session turned on: `adb shell am broadcast -a
   com.oculus.vrpowermanager.automation_disable` if the headset was kept awake, and remove the sound
   option file if it was made ([XR_DEVELOPMENT.md](XR_DEVELOPMENT.md), "Beside a window on a Quest").
5. `adb kill-server`. The Mac's adb server answers every local account while it runs.
6. Record: the validation records above, the OPEN_QUESTIONS rows the session answered, and the
   commit, date and headset.
