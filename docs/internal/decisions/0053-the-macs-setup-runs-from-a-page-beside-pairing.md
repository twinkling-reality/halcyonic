# ADR 0053: The Mac's setup runs from a page beside pairing, and a changed setting applies by a restart the page offers

- Status: Accepted
- Date: 2026-11-11

## Context

Setting up the Mac was a terminal job: `pnpm mac-setup` checks each step and prints the command for
the next ([ADR 0024](0024-the-macs-settings-live-in-one-file-only-its-owner-can-write.md)), and
every download (the pinned OpenCode and Codex, the VR starter's packages, the local models) was a
command the person copied. The pairing page ([ADR 0047](0047-pairing-starts-from-a-page-on-the-mac.md))
already serves the Mac a page of its own: loopback only, opened by a one-time link that becomes a
cookie, every request checked for its Host, Origin and `Sec-Fetch-Site`, under a strict CSP.

The settings file is read once, at the start. Pairing already applies its own setting live: the
page starts and stops the network listener and writes the setting after. Every other setting
(folders, agent apps, voice, the companion, undo, the browser) takes effect at the next start.
A restart no longer loses work: each task's session is re-attached where its runtime keeps it, and
said plainly to be lost where it does not
([ADR 0046](0046-a-restart-re-attaches-each-tasks-session-where-the-runtime-keeps-it.md)).

Verified on this Mac on 2026-11-11: `pnpm start` passes the control plane's exit code through
(75 came back as 75); the setup view's routes refuse without the page's session, from another
origin or as a cross-site request; a faked download ran, showed its lines, ended and was recorded
by `pnpm mac-setup agent-apps` in a browser on a scratch page.

## Decision

1. **One structured check.** `mac-setup.ts` exports `macSetupSteps(io)`: the same steps `pnpm
   mac-setup` prints, each with an id, a title, a status and its lines. The terminal and the page
   read the same check, so they cannot disagree.
2. **A setup view on the pairing page's own server.** `PairingPage` takes a `mount` that may add
   routes only under `/setup` (`/setup`, `/setup/...`, `/setup.<ext>`; anything else throws at the
   start). Every mounted route is behind the page's session and its app-wide checks and CSP. The
   view (`network/setup-page.ts`) shows each step with its presses and a Check Again; it reads the
   last check (a check hashes pinned files and asks the firewall) and checks again after a press,
   after a download ends, on Check Again, or after five minutes.
3. **Presses are the setup actions, nothing else.** Each press maps to one `pnpm mac-setup`
   subcommand from a fixed list (`allow --yes`, `agent-apps`, `local-model <checked model>`,
   `voice`, `browser`, `launcher`, `starter`, `undo [off]`, `companion <its model | off>`). The
   first press says what it will run; only a second press of the same action and argument acts.
   No press sets the paid runtime's switches (`HALCYONIC_CLAUDE_AGENT`, its executable, the agent
   environment): the settings file refuses them anyway, and the page says Claude Agent is set up by
   hand. (Amended 2026-11-12, below: a key the person adds on the page turns its agent app on.)
4. **Downloads are presses too, one at a time.** Each download button says before the press what
   it fetches: name, pinned version, size, and from where. It runs exactly the pinned command the
   terminal prints: the npm installs with `--ignore-scripts`; the starter's `npm ci` with its
   tracked lockfile, no user or global npm settings, a fresh cache removed after, and the public
   registry; `ollama pull` for the checked models. It runs with no shell, in a process group of its
   own, with only `PATH`, `HOME`, `TMPDIR` and `LANG` in its environment. The page shows its last
   12 lines, cleaned of terminal sequences and unseen characters, with the home folder as `~`, and
   Stop ends its whole group. A second download waits for the first. A download never starts on
   its own. Once it ends well, setup's own action records it (`agent-apps`, `starter`), and that
   action checks the pins, as in the terminal. The job reads Done only when that action recorded it
   and, for OpenCode and Codex, the download's own binary matches its pin: a copy that does not match
   is left out of the settings, setup's words say why, and the job reads Not Set Up. OpenCode's and
   Codex's `npm install` use the person's npm settings, as the printed commands do, so the pin is what
   keeps a changed package out. A model is pulled by name and tag only: Halcyonic pins no model
   digest, and the model's button says so ([mac-host-setup.md](../validation/mac-host-setup.md)).
   The pending second press is kept per session of the page (an opaque key the page hands the
   view, a hash of the session, never its cookie), so a press made in one browser is not confirmed
   from another.
5. **Restart to Apply.** The page compares the settings that take effect at a start, as the control
   plane started with them, with the file now, leaving out pairing's setting, which applies live.
   When they differ it offers Restart to Apply. The first press lists the tasks that will pause
   (every execution starting, running, verifying or waiting for the person). The second closes the
   control plane cleanly and exits with code 75. `Start Halcyonic.command` is a loop that starts it
   again on 75 and on no other code, passing `--restartable`. Without that flag (`pnpm start` or
   `pnpm dev` by hand) the page offers no restart and says to stop and start Halcyonic. A close that
   fails exits 1, so the loop stops.

6. **What is shared now** ([ADR 0054](0054-what-was-made-is-kept-and-shared-only-by-the-persons-press.md)).
   Above the steps, since a live link is the most time-bound thing there, Shared Now lists each
   game shared now: its name, its way (Link or This Wi-Fi), the Wi-Fi address as text, Copy and
   Stop. A link's address is never shown, only copied: it is in the page's state for Copy alone. Stop is `POST /setup/shares/stop`, behind the page's session and origin like every
   press, and submits `project.share.stop` as what it is, the local person's press over HTTP
   (`pageStopShare`), never a host-made stop. Written by TASKS, read by CORE.
7. **Link sharing's tunnel** (ADR 0054). A step, Share a Link, with Get Link Sharing and Turn Link
   Sharing Off. The download is the checked release's archive from its GitHub release
   (`/usr/bin/curl -q`, so none of the person's own curl settings apply, https only, and stopped
   past 30 MB), checked by its SHA-256 with `/usr/bin/shasum` before
   `/usr/bin/tar` unpacks the program alone. `pnpm mac-setup tunnel` then records
   `HALCYONIC_TUNNEL_BIN` only when the program's bytes match the pin, and the job says Done only
   then. It applies through Restart to Apply. The owner's yes to this download is on record. Written
   by TASKS, read by CORE.

## Alternatives considered

- **Apply every setting live.** Each setting reaches a different part of the control plane (the
  admission of folders, the adapters' binaries, the speech engine, the companion), most built once
  at the start. Making each live is a change in each of them, and a half-applied setting is worse
  than a restart now that a restart keeps the tasks. Pairing stays live because it was built so.
- **A separate setup server or port.** That would be a second page to open by a link, with its own
  session and checks. Mounting on the pairing page reuses the one that is already audited.
- **Have the page run `pnpm mac-setup` as a process.** It would give the page the terminal's text
  only, and its questions would have no one to answer them. The page calls the same functions with
  a page-shaped IO instead.
- **A launcher that restarts on any exit.** A crash would then loop. Only 75 means "start me again".

## Consequences

- The person can set up the Mac without a terminal once Halcyonic is running. Setting up Halcyonic
  itself (Node, pnpm, the repository) still needs one, as packaging is not decided.
- `SECURITY.md`'s "It downloads nothing" is now true of `pnpm mac-setup` only: the page downloads on
  the person's second press, with the same pinned commands.
- In tests nothing is downloaded: the runner is faked and the Mac is a scratch folder. On the
  owner's Mac it has not been pressed for real; the first real download is the owner's.
- A new setup step gets a button only by adding it to the view's fixed list; a download gets one only
  by adding its pinned command, size and source to `setupDownloads`.
- Worth revisiting when Halcyonic is packaged: the launcher loop then belongs to the package.

## Amendment, 2026-11-11: commands shown, and the two sources of Halcyonic's own sessions

- The page shows each step's commands, the ones `pnpm mac-setup` prints under it, as text. The page never runs
  them.
- What changed and why says whether the running Salidium can read Halcyonic's own Codex and OpenCode tasks. It
  tells by the version in Salidium's discovery file, read without the credential: only a Salidium after 0.8.3
  can. For such a Salidium it shows the two settings for the data directory in use, and `salidium service
  install`.
- Usage left shows the one Seorak setting for Halcyonic's own Codex tasks. Seorak publishes no version to tell
  by, and reads no OpenCode tasks.
- Neither step can see whether the person ran the lines. A task's own answer says "not set up yet" until they
  have.


## Amendment, 2026-11-12: an agent app's own key, added on the page

This replaces decision 3's "the page says Claude Agent is set up by hand". The owner asked for one place to give a
key, and for it to serve any agent app, not one vendor's.

- **The adapter says it takes a key.** An integration package declares an `AgentKey` (`packages/runtime-core`): its
  runtime id, the app's name, the variable the runtime reads, its file in the data folder, and the field's caption.
  The control plane lists the declarations it hosts (`AGENT_KEYS` in `runtimes.ts`). The page draws one card for
  each, in those words only. Its own code names no vendor, and a test checks that.
- **One field, never echoed.** Under Agent apps, a card without a key shows a password field and Add Key. A key is one
  run of printable characters, at most 4096. Anything else is refused in that app's card, in words that never repeat
  it. The field is cleared before the key is sent. Add Key is quiet at rest and takes the accent only while its own
  field holds text, so at rest the page keeps its one accent.
- **Kept as the private file of today.** The key is written to its declared file (Claude Agent's is
  `anthropic-api-key`), mode 0600, through a temporary file renamed into place. Nothing else keeps it: it is not in
  the page's state, a log, the journal or an event.
- **Shown afterwards as Key Stored, with when.** The when is the file's own time. Remove Key asks first, as a
  secret's Remove does. Only the second press for the same app removes the file.
- **A stored key turns its app on at the next start.** For Claude Agent, the file's presence registers the runtime
  as `HALCYONIC_CLAUDE_AGENT=1` does. A kept, replaced or removed key counts as a change for Restart to Apply. The
  settings file still refuses the switch, so paid use starts only from the person's own press or environment. Each
  task on a hosted model still takes its second press on the headset (ADR 0016).
- **The steps say what is on now, and what waits.** The page tells setup which keyed apps the running Halcyonic
  started with (`startedWith`), so a key added since reads "turns on when Halcyonic starts again" and a key removed
  since reads "turns off when Halcyonic starts again", never "is on" or "is off" before the restart makes it so. In
  Terminal, which has no running start to ask, setup says "on from Halcyonic's next start", as its other start-time
  settings do. A keyed app that is on counts as an agent app that can start work. A key in Halcyonic's own
  environment takes the stored one's place, and the step says so.
- **A key file placed by hand turns its app on too, only when the app would take it.** Before this, the runbook had a
  person write `anthropic-api-key` by hand and also set `HALCYONIC_CLAUDE_AGENT=1`. A file kept with the switch off
  now turns Claude Agent on at its next start. The step says so before that start ("turns on when Halcyonic starts
  again", or in Terminal "is on from Halcyonic's next start"), and Remove Key takes it away. A file the app would
  refuse (other users can read it, or it is empty) counts as no key: it turns nothing on, so on its own it never stops
  a start. With `HALCYONIC_CLAUDE_AGENT=1` and no `ANTHROPIC_API_KEY` in the environment, the start still stops on
  such a file, as before, and on no key and no cloud provider at all; the steps say so before that start. The app's
  card says its file isn't used, and Add Key replaces it. The owner had given no key when this was decided.
- A second provider gets a field by declaring its `AgentKey` and being listed in `AGENT_KEYS`. Its runtime
  registers when its key is stored, in its own branch of `createRuntimeAdapters`. The tests prove the page and the
  keeping with a test adapter's declaration beside Claude Agent's. No second hosted runtime exists yet.
