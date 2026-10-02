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
- **Launch at login, and every packaging option** in
  [ADR 0024](../decisions/0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md).
