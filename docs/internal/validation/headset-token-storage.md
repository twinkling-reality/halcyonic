# The access token on the headset

- **Question:** Can a development build keep the access token it uses over USB in app-private
  storage instead of shared storage, move a token earlier builds left on shared storage, and be given
  a new one from the Mac without the token passing through any other file?
- **Date:** 2026-10-02.
- **Versions:** Halcyonic branch lane-g-private-token, rebased on main 5877b09, then lane-g-token-errno
  from main 841e9a6; Unity 6000.3.25f1 for Android (IL2CPP); .NET 10 for the client core's tests;
  the adb in Unity's Android module.
- **Method:** Code and tests, a development APK build, and an independent security review that read
  adb's `commandline.cpp`. Nothing was run on a headset.
- **Status:** Verified in code, in tests and by the build. On a Quest 3, in part, on 2026-10-04
  (below).

## Verified

- `AccessTokenFile` in the client core (16 tests on .NET 10, through `ManagedTokenStorage`; the
  tests that rely on file modes are skipped for root, which they don't bind):
  - A token on shared storage moves into private storage once; the new file is restricted while
    still empty, before the token is written, and the shared copy is removed.
  - A token already in private storage wins, and the shared copy goes; an empty private file, as a
    cut-off write leaves, counts as none.
  - Only a token in its own form, 43 characters of base64url, is taken: an empty or oversized file,
    the pairing's JSON, a symbolic link, a second name for the pairing file and a named pipe are each
    removed without being followed, copied or opened, and the pipe returns at once.
  - Nothing in a folder that is a link is read or removed. The folder is looked at once, before
    the file is opened, so a folder swapped for a link in between would be followed.
  - A shared copy the app can't remove, as in a folder it may not write, is reported, never thrown,
    on this start and every later one.
  - If restricting fails, the token still leaves shared storage.
  - Two names for one file, directly or through a linked folder higher up, keep the token, also
    when looking after the removal fails, unless the app is killed in the moment between removing
    the old name and writing it back.
  - A token written through `run-as` while a move runs is never replaced, and no `.new` file stays.
  - A release build's `Discard` removes a file, a link (never its target) or a pipe unread, and
    reports one it can't remove.
  - A call that fails is never taken for an outcome: a folder that can't be searched throws from
    `Migrate` and `Discard`, a token that can't be read is not removed, and a removal that fails
    other than by refusal throws after the token is moved, and the next start removes the copy. A
    mutation that took the failed look for "nothing there" again fails the test, as does one that
    puts the token back only after a removal that reported success.
  - What each errno means is one function, `TokenStorageException.Classify`, which
    `AndroidTokenStorage` calls and the tests read whole: only `ENOENT` and `ENOTDIR` are nothing
    there, only `ELOOP` from `open` is a link, and only `EACCES`, `EPERM` and `EROFS` from `remove`
    are a refusal.
- How `AndroidTokenStorage` tells the errno: Unity's `AndroidJNISafe.CheckException` makes an
  `AndroidJavaException` whose message is the Java exception's `toString`
  (UnityCsReference, `Modules/AndroidJNI/AndroidJNISafe.cs`; the strings `toString` and
  `getStackTraceString` are in this Unity's `UnityEngine.AndroidJNIModule.dll`), and libcore's
  `ErrnoException.getMessage` returns the call, " failed: ", the errno name and its description in
  brackets (AOSP `libcore/luni/src/main/java/android/system/ErrnoException.java`, main). An errno
  with no name, or a message in any other form, counts as an unexpected failure.
- Development APKs built from lane-g-private-token and from lane-g-token-errno compile the
  Android-only code: `AndroidTokenStorage` (`android.system.Os` `lstat`, `open` with
  `O_NOFOLLOW | O_NONBLOCK`, `fstat`, `read` through raw JNI with the thrown exception's
  `toString`, `chmod`, `remove`) and `Context.getFilesDir()`.
- Three independent reviews read the code. The first read how adb passes `exec-in`: the command is
  sent as `exec:` and the first argument, then each later argument quoted, so the `sh -c` string
  reaches the headset intact, and the Mac's file goes in as raw standard input with no terminal and
  no temporary file. `exec-in` returns 0 even when `run-as` fails, hence the runbook's `ls -l` check.
  The second found that IL2CPP's delete refuses a file with no write bit and that `adb push` left
  the old file owned by `shell` (quest-3-device.md), hence the reported copy and the runbook's `rm`.
  The third read the errno handling against Unity's and libcore's sources, found that a failed look
  after removing one of two names for the file lost the token, now put back in any case, and that
  the checklist took a copy line alone for proof that the calls work.
- The app's manifest sets `android:allowBackup="false"`, so an app backup does not carry the token.

## On a Quest, 2026-10-04

A Quest 3 on build `UP1A.231005.007.A1`, a development APK from main `ee1acdb9`, the computer's adb
1.0.41 from Unity's SDK ([quest-3-device.md](quest-3-device.md), sixth session).

- **The old copy moved:** a token an earlier session had pushed to shared storage (44 bytes,
  readable by all) was moved into private storage at the next start, logged as "moved the access
  token from shared storage into app-private storage", and removed. The app may remove that file.
- **The private token:** the app read it at mode 600 and 44 bytes, and connected.
- **The runbook's one-step `run-as` write did not take:**
  - It left `files/access-token.tmp` (44 bytes, mode 600) and no `files/access-token`. The app found
    no private token, which is why it moved the old copy.
  - `adb shell run-as <package> sh -c '<a>; <b>'`, given as separate words, runs only `<a>` inside
    `run-as`, since adb joins the words and the inner quotes are lost. One quoted string,
    `adb shell "run-as <package> sh -c '...'"`, runs whole.
  - With `adb exec-in`, a file written by the second part of the command appeared only after later
    adb commands had run. The command goes on after `adb exec-in` returns, so the app can start
    before the write ends.
  - What worked: `adb exec-in` writing the content to `files/access-token.tmp`, then one quoted
    `adb shell` that checks, restricts and renames it.
  - The runbooks need that change before the next session.
- **`pm clear`:** it removed the app's private files and the shared folder.
- **The closing check:** removing the tokens left `pnpm quest:check -- --closed` passing.

Still not seen on a Quest:
- the runbooks' write since then (2026-10-04, lane C): the first command, quoted whole, writes
  `files/access-token.tmp` through `adb exec-in`; the second, quoted whole, waits up to 10 seconds for
  its 44 bytes, then sets mode 600 and moves it into place, printing `written`. Only simulated on the
  Mac, with a stand-in that joins adb's words as adb does: there the old line ran only `umask` inside
  `run-as`, and the new lines wrote the file whole at mode 600, waited for a write landing 3 seconds
  late, and wrote nothing when there was nothing to move;
- `pnpm quest:check` reading the log's reach from times (the main log's oldest `-v epoch` stamp,
  `date +%s` and `ps -o ETIME`), and its failure on a `.tmp` file left over;
- the write surviving `adb install -r`;
- links and named pipes at the old place;
- how a missing file reads through JNI;
- the umask;
- backups.

## Not verified

Everything on a Quest; the checks are in XR_DEVELOPMENT.md, "Token storage on a Quest":

- that `run-as` writes `files/access-token` with mode 0600 and the app reads it, and that it
  survives `adb install -r`;
- that a token an earlier build left is moved, logged and removed at the next start, also on a
  paired headset, and whether the app may remove a file `shell` owns;
- whether `adb` or file transfer can make links or named pipes in the app's shared folder;
- that the `android.system.Os` calls work through JNI under IL2CPP, that Horizon OS words
  `ErrnoException` as AOSP does, so a missing file reads as nothing there, and the app's umask;
- that `cat` ends when the Mac's adb closes its input;
- whether a Meta or Horizon backup copied the shared file.

Inferred, not checked: Horizon OS keeps other apps out of `Android/data/<package>` (Android 11 and
later), so the exposure removed is `adb shell` without `run-as` and file browsing over USB.
