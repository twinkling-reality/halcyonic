# A project's own check inside the agents' sandbox

## 2026-11-11: npm, pnpm and TypeScript under `confinedProfile` on this Mac

- **Question:** can a project's own test, type check and build scripts run inside the closed Seatbelt profile
  every agent runs in (ADR 0048)? And do its ways out stay shut? This is for
  [ADR 0055](../decisions/0055-a-projects-own-check-is-chosen-by-the-person-and-confined-as-an-agent.md).
- **Environment:** macOS 26 on Apple silicon, Homebrew's Node 24.15.0, npm from it, pnpm 10.7.1 (Homebrew).
  yarn is not installed. Scratch projects of LOOP's own in its scratch folder, never one of the owner's.
- **Method:**
  - The profile is `confinedProfile` with the project as its folder, a home and a temporary folder of its own,
    `toolFoldersFor(node)` (Homebrew's `Cellar`, `opt`, `lib`, `bin`, `libexec`, `share` and OpenSSL's
    configuration file), and no loopback or listen ports.
  - Each script ran through `/usr/bin/sandbox-exec -f <profile> /usr/bin/env -i PATH=... HOME=... TMPDIR=...
    <manager> run -s <name>`. Its output came over a pipe. Only exit codes and the refusal's code are kept.
- **Results:**
  - **npm:**
    - `npm run test` (`node --test`, one test, writing a file in the project): exit 0, and the file landed.
    - A fetch to 1.1.1.1: `EPERM`, exit 3.
    - A write to a marker folder outside: `EPERM`, exit 1, and the folder stayed empty.
    - A listen on a loopback port: `EPERM`, exit 4.
  - **pnpm** (a project with TypeScript 5.9.3 installed from the local store before the run, outside the
    profile):
    - `pnpm run -s typecheck` (`tsc --noEmit`): exit 0.
    - `test` (`node --test`): exit 0.
    - `check` (`tsc --noEmit && node --test`): exit 0.
    - With a type error added: exit 2, and its first line was the error with file and line.
  - **Standard output as a file outside the folder:** Node aborted at start, in `InitializeOncePerProcess`,
    on every script. Through a pipe the same scripts ran, so the host reads output over pipes.
  - Homebrew's Node needs its libraries from `/opt/homebrew/opt`. With only the Node installation's own folder
    readable, it stopped at `libuv.1.dylib`. `toolFoldersFor` covers it.
- **The suite:** `apps/control-plane/src/own-check/own-check.test.ts` runs the same profile through the
  control plane on this Mac with npm:
  - a choice and its first run;
  - a failure after a pass sent back with the script's lines;
  - a never-passed check and an already-failing one not sent;
  - a changed command not run;
  - the network, an outside write and a port read as refused.
- **Limits:**
  - yarn was not probed; it is not installed here.
  - A real repository's larger suite (a test runner with workers, a bundler's build) was not run, only
    scratch projects.
  - Tests that open a port fail inside the profile, as agents' do (OPEN_QUESTIONS, chosen ports).
  - Other ecosystems are not covered.

## 2026-11-11 (later): what runs, under AUDIT3's probes

- **Question:** do a project's own `pretest`, `posttest` and `.npmrc` `script-shell` decide what an own check
  runs?
- **Method:** scratch projects of LOOP's, the same profile; npm 11 and pnpm 11.27.1 from Homebrew. A
  `pretest` and `posttest` each write a mark; `test` writes a mark and exits 1. A project `.npmrc` holds
  `script-shell=/usr/bin/true` and `enable-pre-post-scripts=true`.
- **Results:**
  - Plain, both managers exited 0 and nothing ran: the shell override made a failing test pass.
  - With `npm_config_ignore_scripts=true`, `npm_config_script_shell=/bin/sh` and an empty user config:
    - npm ran `test` alone (exit 1, no pre or post mark);
    - pnpm ran `test` (exit 1) but also `pretest`.
  - Adding `npm_config_enable_pre_post_scripts=false`, pnpm ran `test` alone.
  - npm refuses one file as both the user and the global config ("double-loading config"), so each is its own
    empty file.
- **The suite:** own-check.test.ts runs each finding as a test on real runs.
- **Limits:**
  - yarn is still not probed.
  - A child started in a new session is not stopped. It is seen only by a folder change within 2 s.

## 2026-11-11 (AUDIT3's reread): node-options, and the keys that change what runs

- **Probe:** npm 11 and pnpm 11.27.1, scratch projects, the same profile. A project `.npmrc` with
  `node-options=--require=<folder>/pre.cjs`, where `pre.cjs` is `process.exit(0)`, and a `test` that writes a mark
  and exits 1.
- **With the earlier overrides:** both exited 0 and the mark was not written. An empty `npm_config_node_options`
  changed nothing. `npm_config_node_options=--stack-trace-limit=10` (Node's default) gave exit 1, with the mark
  written, on both.
- **The keys that change what `run` runs, and how each is overridden in the run's environment:**

  | Key | Override |
  | --- | --- |
  | `script-shell` | `/bin/sh` |
  | `node-options` | `--stack-trace-limit=10`, a no-op |
  | `ignore-scripts` | `true`: npm skips pre and post scripts and runs the named one |
  | `enable-pre-post-scripts` (pnpm) | `false` |
  | `shell-emulator` (pnpm) | `false` |
  | `userconfig` and `globalconfig` | an empty file each, the run's own |

  The person's environment does not reach the script (`env -i`), so no `NODE_OPTIONS` or `npm_config_*` of theirs
  does either.
- **Not overridden:** pnpm's `use-node-version`, which downloads a Node and so fails inside the profile, a failure
  and not a pass; and a project's own `node_modules/.bin`, the project's own commands, which run as the script
  names them.

## 2026-11-11 (later): a Go module

- **Setup:** Go 1.26.5 from Homebrew, a scratch module with no dependencies, the same profile with
  `toolFoldersFor` of Node and of go's real path. The environment held only PATH, HOME, TMPDIR, the caches in the
  run's own folders, `GOTOOLCHAIN=local`, `GOPROXY=off`, `GOSUMDB=off` and `GOENV=off`.
- **Results:** `go test ./...` printed "ok", exit 0. With the test changed to fail, it exited 1 with
  `--- FAIL: TestAdd` and the test's line.
- **The suite:** own-check.test.ts runs both through the control plane, the failure sent to the agent with its
  FAIL line.
- **Limits:** not probed are a module with dependencies (the module cache is the run's own and empty, and there
  is no network) and a vendored one.

