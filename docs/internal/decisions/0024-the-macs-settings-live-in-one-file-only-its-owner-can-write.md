# ADR 0024: The Mac's settings live in one file only its owner can write

- Status: Proposed
- Date: 2026-10-02

## Context

Until now the control plane took every setting from its environment. Running it for real work
meant one long command line: the project roots, the pinned OpenCode and Codex binaries, the three
voice files, a configuration directory for OpenCode, and the network listener, each as a
`HALCYONIC_` variable or `XDG_CONFIG_HOME`. A developer can keep that in a shell script; a person
setting up a Mac for the first time cannot be expected to, and nothing told them which step was
missing or what each one allows. The headset, meanwhile, already tells the person to act on their
computer: "Your computer doesn't allow any folder yet. Allow one on your computer, then press Try
again.", "No agent app on your computer can start work right now. Set one up on your computer, then
open this again."

Facts that shaped the decision:

- The project roots decide what agents may read and change, and what a paired headset can see and
  create ([ADR 0020](0020-a-project-works-in-one-host-approved-folder.md)). Whoever can change the
  settings can widen them, or point the control plane at another binary to run. The data directory
  is already mode 700, and the access token, the device key and the provider credentials in it are
  refused when other users can read them ([SECURITY.md](../architecture/SECURITY.md)).
- OpenCode's own defaults do not keep work on the Mac or ask before commands: without a configured
  model it uses a free model of its own remote service, and it runs every tool without asking
  ([local-models.md](../validation/local-models.md)). The owner's working setup therefore gives the
  whole control plane `XDG_CONFIG_HOME` pointing at a Halcyonic-only OpenCode configuration.
- A start on a runtime that lists its models must name one (`model_required`,
  [ADR 0016](0016-a-person-chooses-a-runtimes-model-from-its-own-list.md)), and the headset lists
  the models on the Mac first and takes a second press for a remote one. A paid model is therefore
  never chosen by a default inside Halcyonic, but the Claude Agent runtime exists only to call
  Anthropic's paid API.
- What was run on this Mac to check the design is in
  [mac-host-setup.md](../validation/mac-host-setup.md).

## Decision

1. **One settings file.** `pnpm mac-setup` writes `<data dir>/settings.json`, and the control plane
   reads it at startup (`apps/control-plane/src/settings.ts`). It holds the same settings as the
   environment variables of the same names, so the runbooks and the code keep one vocabulary.
2. **The environment wins.** A variable present in the environment, even empty, is used instead of
   the file's. Every existing command line, test harness and scratch control plane behaves as
   before, and `HALCYONIC_PROJECT_ROOTS= pnpm start` runs with no roots whatever the file says.
3. **Only its owner can read or write it.** The control plane refuses to start, saying why, unless
   the file is a regular file (checked before it is opened, so a named pipe cannot hold startup),
   opened without following a link, owned by the user running it, mode 600, at most 64 KiB, in a
   data directory that user owns and that is closed to others (mode 700), says `"format": 1`, and
   holds only known settings with well-formed values.
4. **It can never start paid model use.** It may hold only the project roots, the two pinned agent
   binaries, Halcyonic's own OpenCode settings, the three voice files and the network listener's
   address. `HALCYONIC_CLAUDE_AGENT`, `HALCYONIC_CLAUDE_EXECUTABLE` and `HALCYONIC_AGENT_ENV` are
   refused there by name, so the Claude Agent runtime and anything passed through to agents stay a
   deliberate choice made in the environment. No setting names a model.
5. **Halcyonic's own OpenCode settings are local by construction.** `HALCYONIC_OPENCODE_CONFIG_HOME`
   names a directory given to OpenCode alone as its `XDG_CONFIG_HOME`, so the person's own OpenCode
   settings and every other process keep theirs. The control plane holds it to the settings file's
   standard: real folders, not links, owned by the user and closed to others, with nothing in the
   `opencode` folder but `opencode.json`, mode 600, which may hold only `model`, `small_model`, `permissions` and
   Ollama's context limits. Both models must be served on this Mac through Ollama (`ollama/...`,
   never a `cloud` tag). `pnpm mac-setup local-model <name>` writes it: that model as the default and
   the small model, a question before every shell command, and no `webfetch` or `websearch`; the
   check reads the permissions actually there and says what they allow.
6. **Project roots are checked by the control plane, wherever they come from.** A root from the
   environment or the file must be an existing folder that may hold projects: never the disk, a
   shared or system folder (in the forms `realpath(3)` gives, so `/etc` is `/private/etc`), a whole
   drive, another person's home, the account's home folder (never taken from `$HOME`) or a folder
   holding it, Halcyonic's data, a hidden
   folder or Library in the home folder, a folder another user owns, or one any user can change
   (`apps/control-plane/src/folder-safety.ts`). The setup refuses the same folders before it asks.
7. **The startup log names each agent binary** and whether its SHA-256 is the pinned one, with a
   warning when it is not, whoever named it.
8. **The setup checks and explains; it never acts behind the person's back.** `pnpm mac-setup`
   checks every step and says, in the words of [WORDS.md](../product/WORDS.md), what is ready, what
   each step allows, and the next command. Allowing a folder and turning pairing on each say what
   they open and ask before changing anything. It never opens a credential (it checks only that a
   file exists and that other users cannot read it), downloads nothing, and checks the pinned
   binaries and voice models against their SHA-256 before recording them. By default it reads no
   access token; with `--with-token` it sends the token only to a server that first proves it holds
   it at the very address and port dialled, as `pnpm devices` now does too
   ([SECURITY.md](../architecture/SECURITY.md)).
9. **Changes take effect at the next start.** Settings are read once, as the environment was.

## Alternatives considered

- **Keep the environment only, and generate a launch script.** No control plane change, but a
  person who starts it any other way gets different settings, a script is harder to check than a
  file the control plane validates itself, and a launch at login would still need its own copy.
- **Let the file hold everything the environment can.** Simpler to explain, but one hand edit could
  then turn on a runtime that spends money or pass a secret to every agent. The two sources differ
  on purpose: the file for what a person sets up once, the environment for deliberate exceptions.
- **A settings contract in `packages/contracts`.** The file is read only by the control plane on the
  same Mac and crosses no wire; a TypeBox contract and generated C# would serve no client.
- **Settings changed at run time, through the API.** It would let the headset change what agents may
  touch, which [ADR 0020](0020-a-project-works-in-one-host-approved-folder.md) keeps with the host,
  and every running adapter would need to pick changes up safely.
- **Impose local-only on the whole control plane** (refuse any start on a remote model). It may be
  the right default for a person who chooses it, but it is a domain rule over every runtime, not a
  setting; it is an open question ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)).

## Consequences

- A person can set up a Mac with `pnpm mac-setup` and start Halcyonic with `pnpm start`, and the
  check tells them what is missing in the same words the headset uses.
- An independent security review on 2026-10-02 found the first version let a hand-edited file allow
  `/` or the home folder, held Halcyonic's own OpenCode settings to no standard, and sent the access
  token to whatever answered on the port; decisions 3, 5, 6, 7 and 8 above are its fixes. Its
  re-check found a relay on another port could pass the proof on, so the proof now names the address
  and port the connection reached.
- The settings file is a new place that decides what agents may change. It is held to the same
  standard as the access token, and the control plane logs which settings it took from it
  (`control plane ready`, `settings.used`).
- The control plane now refuses some roots it used to accept from the environment, such as the
  home folder itself. A developer who relied on one chooses a folder inside it instead.
- A root that is missing at startup still stops the control plane, as an environment root does; the
  check says so and offers `disallow`.
- Changing a setting needs a restart, which stops any agent at work.

### Not decided here: how Halcyonic reaches a person's Mac

Packaging and launching are the owner's decisions. Today a person clones the repository and runs it
with Node and pnpm. The options, with what each brings:

| Option | What the person does | Consequences |
| --- | --- | --- |
| Repository checkout (today) | Install Node 24 and pnpm, clone, `pnpm install`, `pnpm mac-setup`, `pnpm start` | Developers only; updates by `git pull`; nothing to sign; the person sees every command |
| npm package | `npm install -g` a published package, then the same commands | Needs Node; publishing needs the name, which is not cleared ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md), Legal); an install script would run in the person's environment, so it should have none; unsigned |
| Homebrew formula in the owner's tap | `brew install`, then `brew services start` for launch at login | Needs Homebrew; the tap is the owner's to maintain; Homebrew can also install Ollama and ripgrep; unsigned |
| Signed, notarized app (a menu bar app in a disk image) | Drag to Applications; the app walks the same steps with a folder picker | Needs an Apple Developer Program membership (a fee and its terms, both the owner's); bundles a Node runtime; macOS asks for folder access through its own prompt; the setup's words move into the app; most work |
| Signed installer package | Run the installer | Same membership; an installer that writes outside the home folder needs an administrator password; uninstalling is manual |
| Launch at login (with any of the above) | A user LaunchAgent, or the app's login item | Halcyonic runs whenever the Mac is awake, so a paired headset can start agents without the person opening anything; it starts with a minimal PATH (ripgrep and Ollama by absolute path); macOS shows a background item notice; logs go to a file instead of a window |

Whichever is chosen, the pinned OpenCode and Codex binaries are either downloaded on first run and
checked against their pins, as the setup does now, or redistributed in the package, which makes it
larger and makes their licences part of the release. Ollama and its models stay the person's own
install.
