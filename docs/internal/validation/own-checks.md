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
