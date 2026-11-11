# ADR 0055: A project's own check is chosen by the person and runs confined as an agent is

- Status: Accepted
- Date: 2026-11-11

## Context

Until now only a project made from a starter was checked (ADR 0037), so only such a project fixed
itself after a failed check (ADR 0044). Every check, the automatic check after a turn, and auto-fix's
tracking were gated on `project.starter`. A person's own repository had no check and no fix loop, and
the Mac's starter check runs the starter's own tools, never a project's.

Facts, read and probed on 2026-11-11 ([own-checks.md](../validation/own-checks.md)):
- A project's own `npm test`, and a pnpm project's `tsc --noEmit` and `node --test`, run and pass inside the
  agents' closed Seatbelt profile (`confinedProfile`, ADR 0048), with no loopback or listen ports and
  only PATH, HOME and TMPDIR set. A type error fails with its line.
- Inside it, a fetch, a write outside the folder and a listen on a port are each refused with `EPERM`.
- Node aborts at start when its standard output is a file outside the folder, so output must come over pipes.
- yarn is not installed on this Mac.

## Decision

- **What may run:** only scripts of the project's own `package.json` named `test`, `check`, `typecheck`,
  `lint` or `build`, each a non-empty string of at most 2,000 characters.
  - The host reads that file at the folder's top only, never through a link, at most 256 KiB.
  - The package manager is the lockfile's (`pnpm-lock.yaml`, `yarn.lock`, else npm), and only one found
    on the host's own PATH.
  - Never a command the model or an agent wrote.
- **Shown before the first run:** `GET /api/projects/:project_id/own-check` gives the offered scripts with
  their commands, the project's own words.
  - The person's `project.own_check.choose` (review required) names the scripts.
  - The host journals `project.own_check.chosen` with each command and the SHA-256 of the command as
    written. A command is journaled and shown with any secret Halcyonic holds taken out; the hash is of the
    raw text.
  - It then runs the check once (`first`), so the person sees whether it passes before any turn.
- **A changed command is not run.** Before every run each chosen command's hash is compared with the
  folder's `package.json`. A change or a removal records `command_changed` and runs nothing; the person
  chooses again.
- **How it runs:** `<manager> run -s <name>` per chosen script, in order, stopping at the first that fails.
  - It runs in the project's folder, or a task's own copy (ADR 0045). It runs under the same admission as
    a check: never while a turn runs or the project is busy.
  - The sandbox is `confinedProfile`. It reads the folder and the tools' folders (`toolFoldersFor` of Node
    and the manager), and writes only the folder and a home and a temporary folder of the check's own under
    the data folder. Halcyonic's data folder is hidden, as it is from every agent; other bound projects
    inside are refused. There is no network and no port.
  - Standard input is closed. The environment is only PATH, HOME, TMPDIR, an npm cache in its home, `CI=1`
    and `NO_COLOR=1`.
  - Output is read over pipes, at most 1 MiB per script, kept in the check's own folder, never journaled
    or logged.
  - Each script is stopped with its whole process group after 10 minutes.
- **What is recorded as observed:** `project.own_check.recorded` with `findings`:
  - `outcome`: `ran`, `command_changed`, `manager_missing` or `manifest_unreadable`;
  - each run's name, exit, seconds, `timed_out`, and `refused` when its output names a sandbox refusal
    (`EPERM`);
  - `passed`: every chosen script exited 0, and nothing more;
  - `written`: what the check itself changed in the folder, read before and after it as the Mac reads a
    turn (ADR 0038), as counts and the first 20 paths.
  - `ProjectView.own_check` shows the choice, the latest run with its files state, and `ever_passed`.
- **The check's writes are the check's.** They are recorded with the check, never as a turn's. A turn
  started from rest reads the folder again before it begins (`beforeTurn`), so they are not in its changes.
  Where a turn runs on from an earlier one without that reading, the journal cannot tell them apart. No
  check's write is undone automatically.
- **The fix loop:**
  - A project with a chosen check is checked on its own after a turn that changed its files, as a
    starter's is.
  - A failure goes back to the agent with the host's words and the failed script's first 40 lines, marked
    as the Mac's own check, cap 2, with every stop rule of ADR 0044.
  - A failure starts a chain only when the run before it passed. Otherwise the chain ends at once as
    `failing_before` (an earlier run passed) or `never_passed`, so a check that was failing before the
    task is never sent as the agent's.
  - A run that timed out, met a refusal of the sandbox, or found the command changed ends as
    `check_refused`, and nothing is sent.
  - The person may still tell the agent in their own words. Their next instruction carries the same
    first lines (FX1).

## Alternatives considered

- **Any command the person types.** It is wider than needed, and a typed command is easy to get subtly wrong.
  Named scripts are what the repository's own authors declared.
- **Running the check unconfined, as the person would in a terminal.** It would reach the network, the
  person's home and their credentials whenever an agent had rewritten a script. The same profile as an
  agent means a check can do no more than the agent could.
- **A copy of the folder, as the starter check builds.** A person's repository and its `node_modules` can be
  large, and its tests may need their own paths. The writes are recorded instead.
- **Sending every failure.** A check that was already failing would be sent as the agent's fault, and the
  agent would chase a failure it did not cause.

## Consequences

- A person's own JavaScript or TypeScript repository can verify and retry, within the cap, as a starter's
  project does.
- Tests and servers that open a port fail inside the profile until a person may open chosen ports
  (OPEN_QUESTIONS). They read as refused, never as the agent's fault. Installs need the network and are
  refused; the dependencies must already be installed.
- Other ecosystems (pytest, Cargo, Go, a Makefile's test target) are each a unit of their own, each after a
  probe inside the profile.
- The headset needs Choose a Check and steps named by their script names as the project's words (XR, FIELD).
  A generated C# client older than these bindings fails on the two new event types, so the headset is rebuilt
  with the regenerated bindings before a host that sends them.
- Revisit if a chosen-ports answer lands, or if a script needs a variable the person sets.

## Amendment, 2026-11-11 (AUDIT3's six findings)

- **What runs is the chosen script alone.**
  - The manager gets, in its environment, the settings that outrank a project's `.npmrc`:
    `npm_config_ignore_scripts=true` (npm then skips pre and post scripts but runs the named one) and
    `npm_config_enable_pre_post_scripts=false` (pnpm, whose project `.npmrc` could turn them on).
  - It also gets `npm_config_script_shell=/bin/sh`, and an empty user and global config file of the run's own.
  - Probed on this Mac, npm 11 and pnpm 11.27.1: a `pretest`, a `posttest` and a project `script-shell=/usr/bin/true`
    no longer run or decide a pass.
- **A run's home and temporary folder are made empty for every run**, so nothing one run leaves (a `~/.npmrc`)
  reaches the next.
- **A run settles on the script's own exit, never on its pipes.**
  - The pipes get 200 ms for trailing output, then are closed, and the whole process group is stopped.
  - At the limit the group is stopped and the run settles as timed out. So a child holding the pipes no longer
    keeps the command pending.
  - A run records `left_running` when a pipe was still held after the script exited, or when the folder changed
    in the 2 s after the run.
  - A child that left the process group (a new session) is not stopped by this and can write in the folder
    later. Seatbelt still holds it to the profile, and `left_running` is the only sign of it.
- **Close and restart.** The control plane's close stops every running check. Each run's process is recorded
  under the data folder and, at the next start, its group is stopped while that process still runs this check's
  profile.
- **The folder is held.** While a project's own check runs, no turn starts or is instructed in a folder that
  overlaps the one it checks (admission's folder rule).
- **The baseline is the folder's own.** Auto-fix judges a failure by the previous run of the same folder, the
  project's folder or a task's own copy. A folder with no run of its own has none (`never_passed`).
- **The choice is bound to what the person read.** `project.own_check.choose` carries each script's
  `command_sha256` as `GET .../own-check` gave it, and a mismatch fails with `command_changed`, recording no
  choice.
- **`refused` is read from the failed script's last 20 lines only.**
- **Format.** PROJECTION_STATE_FORMAT is taken at this unit's rebase, by hand, past the base's (9 as the lead
  names it).

## Amendment, 2026-11-11 (AUDIT3's reread)

- **`node-options` is overridden too.** A project's `node-options` would become `NODE_OPTIONS` for the script. An
  empty `npm_config_node_options` does not clear it on npm 11 or pnpm 11 (probed), so the environment gives Node's
  own default as a no-op, `--stack-trace-limit=10`. pnpm's `shell-emulator` is set off.
  [own-checks.md](../validation/own-checks.md) lists each key that changes what a script runs, with its override.
- **The restart match.** A running script's record holds its pid and its start time from `ps -o lstart`.
  `sandbox-exec` execs the manager, so the profile is not in its arguments. At the next start the group is stopped
  only while that pid still has that start time; a pid since reused started later and is left alone. The record
  goes either way.
- **Passed needs nothing left running.** Passed means every chosen script exited 0 and none has `left_running`.
- **The settle window is 2 s.** A child in a new session that writes later than 2 s after the run is not seen.

