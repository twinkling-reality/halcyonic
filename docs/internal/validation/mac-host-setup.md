# Setting up the Mac

- **Question:** Can a person's Mac be set up for Halcyonic with `pnpm mac-setup`, step by step and in
  plain words, without weakening a security control, and does a control plane started from the
  settings file it writes alone do real work on a local model?
- **Date:** 2026-10-02.
- **Versions:** Halcyonic `lane-g-host-setup` on main 8e9ba39; macOS 26.7 on an Apple M5 Max with
  64 GB; Node.js 24.15.0; Ollama 0.34.4 (already running, with seven models); OpenCode 2.0.18 and
  Codex 0.157.0 as installed in `~/.halcyonic/runtimes` on 2026-09-29; whisper.cpp 1.9.4 and its two
  models as installed in `~/.halcyonic/speech`.
- **Method:** The check was run read-only against the owner's real data directory. Every action was
  run against a scratch data directory holding APFS clones of the pinned binaries and voice files, a
  real control plane was started from it on loopback ports of its own, and one real task was started
  through it. Nothing was written to `~/.halcyonic`, the running control plane was not restarted, and
  no remote model was called.
- **Status:** Verified on the owner's Mac for the steps below. Not tried by a person new to
  Halcyonic, which is what the setup is for; it stays a prototype until then.

## The check on the owner's Mac

`pnpm mac-setup --no-token` against `~/.halcyonic` (now the default), which reads no credential, no
access token and no journal: it checks file modes and owners, hashes the pinned files, lists Ollama's models and asks
only the unauthenticated `GET /api/health`.

- It took 1.6 s, most of it hashing about 1 GB: the installed OpenCode 2.0.18
  (`6759c7f8…a96bf`, the hash the 2026-09-26 smoke test recorded) and Codex 0.157.0 (`ad0be20d…3714`,
  the runbook's) match their pins, and so do both voice models.
- It reported what an environment-started control plane leaves invisible to it: no folder in the
  settings, the two agent apps installed and checked but not set up, voice ready to record, a Seorak
  credential saved (whether Seorak accepts it needs the token), and Halcyonic running, "checked without
  its access token". The owner runs the control plane with environment variables, so the check is
  right that the settings file allows nothing yet.
- Ollama's seven models were listed with their sizes; `smollm2:135m` as unable to use tools, and the
  two aliases (`gpt-4o:latest`, `gpt-3.5-turbo:latest`, both `llama3.2:1b`) as running on this Mac.

## Every step, in a scratch data directory

`allow <scratch>/projects --yes`, `agent-apps`, `local-model qwen3.6:35b-a3b-nvfp4`, `voice` and
`pairing on` (run before it asked first; it now needs a yes) each wrote `settings.json` mode 600, in a data directory of mode 700; `local-model`
wrote `opencode-config/opencode/opencode.json` mode 600 in directories of mode 700, naming
`ollama/qwen3.6:35b-a3b-nvfp4`, asking before `shell`, denying `webfetch` and `websearch`, and
giving each of the six tool-capable Ollama models a 65,536-token context.

A control plane started from that file alone, with `HALCYONIC_NETWORK_HOST=127.0.0.1` in the
environment so nothing listened on the network:

- registered `mock`, `opencode` and `codex`, allowed the scratch folder, opened whisper.cpp 1.9.4 (its
  warm-up took 22.6 s, the first in a new place), and logged the seven settings it took from the
  file; the listener's address came from the environment, which won over the file's `0.0.0.0`.
- `pnpm mac-setup`, with the scratch access token, then read "Halcyonic is running with these
  settings", every step ready but the headset ("no headset is paired yet", next `pnpm pair`), and
  where work goes marked "Look at this": the owner's own Codex settings name no provider, so Codex's
  models through Halcyonic run on OpenAI's remote service. The check read only the top-level
  `model_provider` line of `~/.codex/config.toml` for this.
- `GET /api/runtimes/opencode/models` launched the pinned OpenCode, which listed every tool-capable
  Ollama model at 65,536 tokens of context, the limit Halcyonic's own OpenCode settings set, where
  OpenCode otherwise assumes the model's own (262,144 for `qwen3.6:35b-a3b-nvfp4`). So OpenCode read
  the settings given to it alone as `XDG_CONFIG_HOME`, and the owner's `~/.config/opencode` was not
  involved. Its seven OpenCode Zen models were listed as remote, as before.
- **A real start.** `project.create` with a new folder in the scratch root, `workstream.create`,
  then `execution.start` on `opencode` with `model_ref` `ollama/qwen3.6:35b-a3b-nvfp4` and the
  instruction to run `ls -la`: 24.3 s later the execution was waiting for the person with a `shell`
  approval for `ls -la`, on the model named, because Halcyonic's own OpenCode settings ask before
  shell commands. The approval was denied without a message, and the turn ended interrupted, as
  OpenCode 2.0.18 does ([local-models.md](local-models.md)). The new folder was made empty and stayed
  empty. The model was already loaded by another session's work.
- Stopped with SIGTERM, it shut down and left no OpenCode or Codex process behind.

## Automated checks

`pnpm check` (746 tests at the time) covered the settings file's refusals (mode, owner, link, data directory,
size, format, unknown and environment-only settings), the environment winning even when empty, the
control plane starting from the file and refusing a readable one, Halcyonic's own OpenCode settings
refusing every kind of remote default, and the setup's steps with a fake Ollama, a fake running
Halcyonic and fake pinned files: what each step says, the folders it refuses, that nothing changes
without a yes, that it never opens a credential or reads the access token with `--no-token`, and
that every line keeps to [WORDS.md](../product/WORDS.md). The C# suite (615) holds the headset's words
for the host, now "your computer", and all seven Unity renders passed with the longer word.

## After the security review

An independent review of the first version found that a hand-edited settings file could allow `/`
or the home folder, that Halcyonic's own OpenCode settings were held to no standard, and that the
check sent the access token to whatever answered on the port (SECURITY.md). After the fixes, on
the same scratch data directory:

- The control plane started from the file and logged both agent binaries, the clones of the pinned
  OpenCode and Codex, as `matches`.
- `pnpm mac-setup --with-token` and `pnpm devices list` reached it through the proof: "Halcyonic is
  running with these settings", and "No device has paired."
- With it stopped and a small Node server answering `GET /api/health` on its port, both refused:
  the check said something "can't prove it holds this Mac's access token, so the token was not
  sent", and `pnpm devices` said the same. The impostor received three health checks, none
  carrying an `authorization` header.
- The review's re-check found that a listener on another port could relay the challenge to the
  real control plane and pass its proof on; the proof now names the address and port the connection
  reached, and a test relays it through a second port and is refused.
- `pnpm check` covers the rest: the folder rules, including `/etc` as `/private/etc`,
  `/private/var/db`, `/opt/homebrew`, the caches and app data beside a user's temporary folder, other
  people's homes, whole drives and folders another user owns, with the home folder taken from the
  account rather than `$HOME`; the control plane refusing such a root from the environment and from the file; Halcyonic's
  own OpenCode settings refused for a link, a mode, another file beside them, an unknown key or a
  malformed rule; a named pipe as the settings file refused without hanging; the proof refused
  without the right token and given only by the health check; and the check's words following the
  permissions actually in Halcyonic's own OpenCode settings.

## The companion's step

On main 7c7d363 with lane C's companion, in a fresh scratch data directory, against the Mac's
Ollama, which listed `qwen3.5:9b` among ten models:

- `pnpm mac-setup` showed the Companion step as optional, quoting the headset's "The companion isn't
  set up on your computer." and recommending `qwen3.5:9b` with `OLLAMA_MAX_LOADED_MODELS=2`; since the
  model was already there, it offered only `pnpm mac-setup companion qwen3.5:9b`.
- That command wrote `HALCYONIC_COMPANION_MODEL` to the settings file. A control plane started from
  it logged the setting among `settings.used`, and `GET /api/companion` answered `available`, served
  on this Mac, without asking the model. `pnpm mac-setup --with-token` then read the step as ready.
- The tests cover the refusals: a `cloud` tag in any case, a model Ollama lists with `remote_host`, a
  model Ollama doesn't have (the setup shows `ollama pull` for the person), an Ollama address that
  is not http on loopback with a port, and a companion sharing the agents' model.

## The browser's launcher (S1, 2026-10-31)

`pnpm mac-setup launcher` builds `apps/control-plane/native/disclaim.c` and `native/audio-output.c` (linked with
CoreAudio) with `/usr/bin/clang` (`-Wall -Wextra -Werror -O2`) into `<data dir>/bin/halcyonic-disclaim` and
`halcyonic-audio-output`, mode 0500 in a 0700 folder, and records each one's SHA-256 beside it; the control plane uses
each only while it is a plain file of the user's that no one else can change and hashes as recorded.
The check lists it as "New apps' browser": Optional with no browser set, To do while a browser is set and the launcher
is missing or changed, Ready once built. Tested in `mac-setup.test.ts` and `launcher.test.ts` on this Mac, with Apple
clang 21.0.0 on macOS 26.7; without Apple's command line tools the command says to run `xcode-select --install`.

## New apps' browser in one command (2026-10-31)

`pnpm mac-setup browser` lists `~/Library/Caches/ms-playwright/chromium_headless_shell-*/chrome-headless-shell-mac-arm64`
as plain folders (a link is passed over) and compares the digest of each one's manifest, every entry's kind, mode and
relative path and every file's SHA-256, with any link refusing it, against the browser pin in `pins.ts` before it copies or records anything (AUDIT M1). A match is copied into
`<data dir>/browser/current` (checked again after the copy, a 0700 folder), its folder alone is written to the
settings file as `HALCYONIC_BROWSER`, and the launcher and the sound helper are built. The settings file refuses
`HALCYONIC_BROWSER_SHA256`; the start takes the hash from the pin, and refuses one in the environment that is not the pin.
With no match it names the folder it looked in and the build it needs, and sets up nothing. Tested in
`mac-setup.test.ts` with a stand-in cache (a matching build, another build, and a link to the match, passed over), in
`config.test.ts` and in `settings.test.ts`; on this Mac Playwright's `chromium_headless_shell-1248` holds the pinned build.

## The VR starter and undo in the setup (2026-11-03)

`pnpm mac-setup starter` reads the VR starter's installed set in `<data dir>/starters/iwsdk-vr` against the tracked
lockfile, each package's own `package.json` version against the version the lockfile pins (an optional platform
build may be absent), and writes `installed.json` with the lockfile's SHA-256 only when all match; otherwise it names
what differs, or says the set is missing, and prints the commands that install it (npm from the public registry, no
install scripts, an empty user and global npm configuration, a cache of its own), changing nothing. `pnpm mac-setup
undo` records `HALCYONIC_GIT_BIN`, `/usr/bin/git` or the absolute path named, only when it is a file this Mac can run;
it never runs git, since `/usr/bin/git` without Apple's command line tools would ask on the person's screen to install
them. `undo off` takes it away. The check shows both as steps, VR starter and Undo, Optional until set up.

Run on a scratch data directory holding a set installed by those commands (80 packages, every version the
lockfile's): `starter` recorded it, `undo` recorded `/usr/bin/git`, and the check read both Ready. Tested in
`mac-setup.test.ts`: nothing installed (the commands printed, nothing recorded), a set stand-in matching the lockfile
(recorded, then Ready), one package at another version (named, nothing recorded), undo on and off, and a missing
path, a folder, a file that cannot run and a relative path refused.

## The starter's set and undo's git, made strict (AUDIT SETUP, 2026-11-03)

AUDIT passed the steps above with six SHOULDs; all six are in.

- **The record** (`installed.json`) is written as the settings file is: a new file of its own, mode 600, created
  exclusively and renamed over the old, so a link at its name is never followed.
- **The set** must be exactly what the lockfile lists. Walking `node_modules`, every entry is looked at with `lstat`
  before anything is read: only folders and regular files, no link but npm's `.bin` links that lead to a file inside
  the set, and no name in a `node_modules` folder or scope that the lockfile doesn't list (`.bin` and npm's own
  `.package-lock.json` aside). A starter folder that is a link or a file is refused and named as such. A planted pipe
  for a `package.json` is named as differing instead of hanging the command.
- **A lockfile entry without a version** differs, even an optional one; an optional package that is there must match.
- **Undo's git** is recorded by its real path, after every link, so a link pointed elsewhere later changes nothing;
  `undo` says when it isn't `/usr/bin/git`.
- **The check's Undo step** uses the same test as `undo` (a regular file this user may run, after every link) and
  names the path with `~`. A git that can't run reads "Look at this". A folder or a missing path in the settings
  file still stops the settings from loading, which the check's first step reports; the Undo step then reads off.
- **The printed commands** quote their paths: under the home folder as `"$HOME/..."`, otherwise bare when plain and
  in single quotes when not, so a data folder with a space in its name works.

Run on the lane's scratch data directory with its real set (80 packages, 16,820 entries): `starter` recorded it
(the walk 455 ms), the record a regular file with mode 600; `undo` recorded `/usr/bin/git`; the check read both
Ready. Tested in `mac-setup.test.ts` and `starters.test.ts`: a link at the record's name (left alone, a new file
written), a package the lockfile doesn't name, a package folder that is a link, a link inside a package out of the
set, a pipe for a `package.json`, npm's `.bin` links inside the set (allowed), the starter folder a link (refused,
nothing written through it), a data folder with a space (quoted commands), a link to a git (its real path recorded,
the word about `/usr/bin/git`), the Undo step for a folder, a file that can't run and one that can, and a lockfile
entry without a version. The final tests failed on the unit's old sources (7 of 61) and pass on the new.

## The setup page (ADR 0053, 2026-11-11)

Built and tested with a faked runner on a scratch Mac (`network/setup-page.test.ts`); no download was pressed for
real. What each download is checked by:

- **OpenCode and Codex**: the installed binary's SHA-256 against the pin, by setup's own `agent-apps`, which
  records only a match. The page then checks the download's own binary again, so a setting left from an earlier
  install never makes a changed copy read Done: a copy that does not match is left out of the settings, setup's
  words say so, and the job reads Not Set Up. Their `npm install` uses the person's npm settings (registry,
  sign-in), as the commands the terminal prints do; the pin is what keeps a changed package out.
- **The VR starter**: its tracked lockfile's integrity through `npm ci`, with neither of the person's npm settings,
  then setup's `starter`, which records the set only when every package is the version the lockfile pins.
- **Models**: none. `ollama pull` fetches each checked model by name and tag, which Ollama's library can point at
  another build; Halcyonic pins no model digest, and the page's button says so. A pinned digest compared with
  Ollama's `/api/tags` after the pull would close this; not built.

## Start at login (ADR 0024 amended, 2026-11-12)

`pnpm mac-setup login on|off` (`apps/control-plane/src/cli/login-agent.ts`). The keys it writes are
read from `man launchd.plist` on macOS 26.7: `ProgramArguments`, `WorkingDirectory`,
`EnvironmentVariables`, `RunAtLoad`, `KeepAlive` with `SuccessfulExit` false ("restarted in the
inverse condition", so after any exit that is not zero), `ThrottleInterval` (by default a job is not
spawned more than once every 10 seconds; the agent sets 30), `ExitTimeOut` (the time between the
stop signal and a kill; the default is "system-defined", the agent sets 30), and `StandardOutPath`
and `StandardErrorPath` (launchd creates the file if it is missing; On makes the folder first).

Verified on the owner's Mac, 2026-11-12 04:58Z, with a scratch label
(`com.halcyonic.control-plane.scratch`), its file in a lane's own git-ignored folder (never
`~/Library/LaunchAgents`), port 47841 and a scratch data folder, through the command's own `on` and
`off` (`login-agent.ts on|off <folder> --label --port --data-dir`):
- `~/Library/LaunchAgents` held no Halcyonic file before the run or after it.
- On, run under `pnpm exec` as `pnpm mac-setup` runs: `launchctl bootstrap gui/<uid>` took the
  file and launchd started the plane at once (state running, runs 1) as
  `/opt/homebrew/bin/node .../apps/control-plane/src/main.ts --restartable`, the node found on PATH
  rather than Homebrew's versioned Cellar folder `process.execPath` gives; `/api/health` answered
  within seconds and On said so. The written PATH had no `node_modules` folder and none inside
  the checkout, though pnpm had added them; `ExitTimeOut` and `ThrottleInterval` read 30.
- On a second time: refused, "already starts when you log in", with nothing changed.
- A crash: `kill -KILL` of the plane; launchd had a new process within 24 seconds (runs 2),
  which fits the 30 second throttle counted from the first start, and it answered its health check.
- Off: `launchctl bootout` stopped it, the job was no longer loaded, the port was free and the file
  was gone. The plane's log, written by launchd to the scratch data folder, held no error line.
- Unit tests (`login-agent.test.ts`, launchctl stood in, every data folder the test's own): the file
  read back by `plutil`, PATH's folders kept and dropped, the settings left out named, strict
  arguments and ports (`abc` and 0) refused before anything is written, On refusing while launchd
  has the agent or a plane answers, launchd's refusal said in its words with the file removed, no
  write through a link, Off.

Not verified:
- The real label and `~/Library/LaunchAgents`, so also a start at an actual login and macOS's
  background item notice: installing it on the owner's Mac is the owner's choice.
- Restart to Apply (exit 75) and a failed close under launchd; only a KILL's restart was seen.
- On from an ssh session or any session with nobody logged in at the Mac (launchd should refuse
  `gui/<uid>` there; On then removes the file).
- Folder access for a node launchd starts: projects in Documents, Desktop or Downloads were read
  through Terminal's grant before; whether macOS asks again, or refuses, for this node is not known.
- Voice (`voice-transcription.md` lists its microphone and speech access as unchecked already).
- A Homebrew upgrade of node (whether `/opt/homebrew/bin/node` follows it), and a version manager's
  node (nvm, fnm, asdf, mise, volta): the path written may not last.

## Not verified

- **A person new to Halcyonic** following the runbook's "First run on a Mac" without help. Words
  that read well to the people who wrote them may not.
- **A fresh Mac**: no Node, pnpm, Homebrew, Ollama or ripgrep, and no Xcode command-line tools for
  voice. The pinned binaries were cloned, not downloaded.
- **Intel Macs**, which have no pins, and any Mac with less memory than 64 GB.
- **Pairing from the settings file**: the listener on `0.0.0.0`, the macOS firewall prompt, and a
  headset pairing over Wi-Fi with it. The listener was kept on loopback.
- **Codex on a local model through the person's own Codex settings**, and voice transcription through
  the scratch control plane beyond its warm-up.
- **The headset's new words on a Quest**: only the editor renders show them.
- **Every packaging option other than start at login** in
  [ADR 0024](../decisions/0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md).
