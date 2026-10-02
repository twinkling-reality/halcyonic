# The access token on the headset

- **Question:** Can a development build keep the access token it uses over USB in app-private
  storage instead of shared storage, move a token earlier builds left on shared storage, and be given
  a new one from the Mac without the token passing through any other file?
- **Date:** 2026-10-02.
- **Versions:** Halcyonic branch lane-g-private-token on main 9ffb25c; Unity 6000.3.25f1 for Android (IL2CPP); .NET 10 for the
  client core's tests; the adb in Unity's Android module.
- **Method:** Code and tests, a development APK build, and an independent security review that read
  adb's `commandline.cpp`. Nothing was run on a headset.
- **Status:** Verified in code, in tests and by the build. Not verified on a Quest.

## Verified

- `AccessTokenFile` in the client core (7 tests on .NET 10): a token on shared storage moves into
  private storage once, the new file is restricted while still empty, before the token is written,
  and the shared copy is removed. A token already in private storage wins, and the shared copy goes.
  An empty file, or one larger than 1 KiB, is removed and nothing is written. If restricting fails,
  the token still leaves shared storage. Two names for one file, directly or through a linked
  folder, never lose the token, and a link at the old place is removed without being followed. A move interrupted by the owner writing a token at the same moment
  leaves no `.new` file, and the next run removes the shared copy.
- A development APK built from this branch compiles the Android-only code: `Context.getFilesDir()`
  for private storage and `android.system.Os.chmod(path, 0600)`.
- The reviewer read how adb passes `exec-in`: the command is sent as `exec:` and the first argument,
  then each later argument quoted, so the `sh -c` string reaches the headset intact. The Mac's file
  goes in as raw standard input, with no terminal and no temporary file on either side. `exec-in`
  returns 0 even when `run-as` fails, hence the runbook's `ls -l` check.
- The app's manifest sets `android:allowBackup="false"`, so a backup does not carry the token off
  the headset.

## Not verified

- On a Quest: that `run-as` writes `files/access-token` with mode 0600 and that the app reads it
  there; that a token pushed to shared storage by an earlier build is moved, logged and removed at
  the next start, also on a paired headset; that the token survives `adb install -r`; that `cat`
  ends when the Mac's adb closes its input. These go on the next headset session's checklist.
- Inferred, not checked: Horizon OS keeps other apps out of `Android/data/<package>` (Android 11 and
  later), so the exposure removed is `adb shell` without `run-as` and file browsing over USB.
