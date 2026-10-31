# Kid's Game Quality Rehearsal on the Mac (K1, GQ1a, GQ1b, GQ1c)

Dated runtime verification of the end-to-end kid's game flow on a Mac host using the `iwsdk-vr`
starter, a Seatbelt-confined Codex runtime on a local Ollama model, automatic project checks,
check-error feedback, starter scene and palette helpers, and automatic local-model unloading.

## Questions

1. Can a confined Codex agent on a local Ollama model turn a child's one-sentence game request into
   a checked, playable 3D browser game from the `iwsdk-vr` starter without network access, host
   credentials, or manual intervention between turns and checks?
2. After `GQ1a` and `GQ1b` (the meadow island starter scene, `palette.ts`, `motion.ts`, `look.ts`,
   `decor.ts`, and hiding the welcome card at 6 seconds or first input), `T2` (file-staleness
   tracking on checks and previews), `A1` (automatic internal `project.check` after file-changing
   turns), `FX1` (check compiler, build, and page errors appended to fix-up instructions), `SC1`
   (`AGENTS.md` rules to keep the game in `src/index.ts`, avoid `.py`/`.txt`/`.sh`/`.patch`
   helpers, and run `npx tsc --noEmit`), and `UL1` (`OllamaUnloader`), what happens in one real
   confined run on `ollama/muse-glimmer:30b-mlx`?

## Environment and Confinement Gates

- **Date:** 2026-10-31, macOS 26.7 (25G229), Apple silicon Mac.
- **Runtime and model:** pinned Codex `0.157.0` (`aarch64-apple-darwin`), `model_provider = "ollama"`,
  `ollama/muse-glimmer:30b-mlx` on loopback (`127.0.0.1:11434`).
- **Browser for checks:** pinned `chrome-headless-shell` `156.0.8078.4` (`chromium_headless_shell-1248`,
  manifest SHA-256 `0966bb448b30d115b303849668c4ebf1f00cf739bc728e4b747c1a502ee00ef2`) launched
  through `halcyonic-disclaim` and Seatbelt.
- **Confinement gates enforced on every run:**
  - **G1 (owner state untouched):** directory listing (names, sizes, mtimes, three levels deep) of
    the owner's `~/.halcyonic` and `~/.codex` taken before and after the run; `diff -u` exit `0`
    (`0` lines changed across `1,636` entries). The rehearsal control plane uses its own isolated
    data folder (`0700`) and project root.
  - **G2 (confined Codex proven at start):** before `execution.start`, verified `model_provider = "ollama"`,
    no `auth.json`, and `0` key-like environment variable names (`*API_KEY*`, `*_TOKEN*`,
    `*SECRET*`) in the control-plane process environment. Within 30 seconds of `execution.start`,
    verified by kernel inspection that the project's Codex app-server runs under Seatbelt with its
    confined `CODEX_HOME` and `.sb` profile inside the scratch data folder.
  - **G3 (confined reads):** no preview or play step runs unless the check records
    `reads_confined: true`.
  - **Memory gate:** `execution.start` and `execution.send_instruction` wait until
    `/usr/bin/memory_pressure` reports at least 40% system-wide memory free.
  - **Play on my laptop evaluated read-only:** to avoid opening a browser window or playing audio on
    the owner's desktop during automated verification, `project.open_on_mac` is not sent; its
    admission conditions (`passed`, `laptop.ready`, `reads_confined`, `check.files === 'current'`,
    `preview.files === 'current'`) are read from the project view after `project.preview.start`.

## Baseline Before `GQ1a`/`GQ1b`/`T2`/`A1`/`FX1`/`SC1`/`UL1` (`K1`, `0b38be8a`)

On the initial confined rehearsal (`K1` rerun after `CX1` fixed confined `CODEX_HOME`
canonicalization):

- **`ollama/qwen3.6:35b-a3b-nvfp4`:** completed Turn 1 in 395 s, failed Check 1 (`tsc` exit 2 with
  syntax errors in a new `src/mole-game.ts`, `build` exit 1). Turn 2 received only the child's
  fix-up words without the check's compiler output, timed out at the 15-minute turn cap (903 s), and
  failed Check 2 (`tsc` exit 2). After the run ended, `qwen3.6` stayed loaded in Ollama (`23.6 GB`)
  and held the next run at the 40% memory gate for 270 s.
- **`ollama/muse-glimmer:30b-mlx`:** completed Turn 1 in 503 s and passed Check 1 (73 s, `tsc` 0,
  `build` 0, `response_seen: true`, `reads_confined: true`, `vr.ready: true`). However:
  1. The 10-second check picture showed nine dark grey discs and two pink spheres floating in a pale
     sky with no ground, minimal shading, and the starter's `"Hello, Immersive Web!"` welcome card
     still covering the top-left corner (`card_at_10s: true`).
  2. Between the 3-second and 10-second check pictures, `0%` of pixels outside the welcome card
     changed (no continuous idle motion).
  3. The agent left eight scratch files in `src/` (`make_mole.py`, `mole_gen.py`, `patch.txt`,
     `patch_mole.txt`, `test.txt`, `test_patch.txt`, `x.txt`, `index.patch.txt`).
  4. Checks had to be dispatched manually by the driver after each turn, and `muse-glimmer:30b-mlx`
     remained loaded in Ollama after the run ended.

## After-Run Verification (`GQ1c`, `99b61e27`, 2026-10-31)

One complete confined rehearsal (`gq1c-moles-1`) was run against commit
`99b61e271f61a3fc37fc20a3538b1a3546c304dd` on `ollama/muse-glimmer:30b-mlx` with the same sentence
as `K1` (`"Make a bonk-the-mole game! Friendly colourful moles pop up out of holes and wiggle, and I bonk them to get points."`).

### Step Timeline (UTC, 2026-10-31)

| UTC | Seconds | Step | Verified Outcome |
|---|---:|---|---|
| 23:24:15 | 0.8 | G1 before; scratch setup and control-plane start | `1,636` entries in `~/.halcyonic` and `~/.codex`; `mac-setup local-model muse-glimmer:30b-mlx` exit 0; `mac-setup browser` exit 0; `/api/health` 200 on `127.0.0.1:47882` |
| 23:25:56 | 0.09 | Runtime, model, root; `project.create` (`iwsdk-vr`); `workstream.create` | Codex registered, `ollama/muse-glimmer:30b-mlx` listed; project and workstream created |
| 23:25:56 | 0.004 | G2 before `execution.start` | `provider: "ollama"`, `signed_in: false`, `key_variable_names: 0` |
| 23:25:56 | 0.7 | G2 at `execution.start` | Kernel-sandboxed Codex server (`pid 95772`, `kernel: "sandboxed"`) with confined home and `.sb` profile inside the scratch data folder; 1 open discovery server (`pid 95758`); `problems: []` |
| 23:25:56 | 480.8 | Agent Turn 1 (child's prompt) | `memory_pressure` 83% free before start; `/api/ps` `[]` before start; `runtime.turn.completed` in 480.8 s; `0` approvals, `0` questions; `execution.files.observed` (`created: 2, changed: 1, deleted: 0`) |
| 23:33:57 | 0.5 | `UL1` after Turn 1 | `OllamaUnloader` unloaded `muse-glimmer:30b-mlx` on `runtime.turn.completed`; `/api/ps` returned `[]` at `23:33:58.200Z` and `memory_pressure` rose from 36% during Turn 1 back to 71% free before Turn 2 (`0 s` memory-gate wait) |
| 23:33:57 | 31.1 | Check 1 (`A1` automatic internal check) | Dispatched automatically by `ControlPlane` within 1 ms of `execution.files.observed` (`command.accepted` with `received_via: "internal"`); `check.files: "current"` (`T2`); `passed: false, ready: false` (`tsc` exit 2, `build` exit 0, 1 page exception, `reads_confined: true`) |
| 23:34:29 | 404.6 | Agent Turn 2 (child's fix-up + `FX1` check output) | `execution.send_instruction` automatically appended Check 1's `tsc.log` and page exception (`FX1`); `2` shell approvals requested and denied; `runtime.turn.completed` in 404.6 s; `execution.files.observed` (`created: 1, changed: 1, deleted: 1`) |
| 23:41:12 | 100.3 | Check 2 (`A1` automatic internal check) | Dispatched automatically by `ControlPlane` (`received_via: "internal"`); `check.files: "current"` (`T2`); **`passed: true, ready: true`** (`tsc` exit 0 in 0.76 s, `build` exit 0 in 5.21 s, `0` page exceptions, `0` console errors, `1` canvas, `edges_at_3s: 42,343`, `edges_at_10s: 37,957`, `laptop.layer: "starter"`, `laptop.targets: 6`, `mouse: "reached"`, `key: "reached"`, `response_seen: true`, `vr.offered: "offered"`, `vr.ready: true`, `reads_confined: true`, `own_permissions: true`) |
| 23:42:54 | 0.001 | G3 `reads_confined` | `reads_confined: true` |
| 23:42:54 | 1.0 | `project.preview.start` | Serving on loopback port `47930`, `index.html` HTTP `200` (`410` bytes), `preview.files: "current"` (`T2`) |
| 23:42:55 | 0.001 | Play on my laptop admission conditions (read-only) | `passed: true`, `laptop_ready: true`, `reads_confined: true`, `check_files: "current"`, `preview_serving: true`, `preview_files: "current"` |
| 23:42:55 | 0.009 | `project.make_vr_version` (command only, no agent) | Completed; created `"Bonk the Moles VR"` from starter `iwsdk-vr` |
| 23:43:09 | 2.0 | Control-plane stop (`SIGTERM`) and G1 after | Process stopped cleanly; `0` listeners on `47882` or `47930-47949`; `0` processes left; `ollama /api/ps` `[]` (`UL1`); G1 `diff -u` `0` lines (`1,636` entries unchanged) |

Total agent time across both turns was `885.4 s` (`14.8 min`, well under the 40-minute cap).

### Before-and-After Comparison (`K1` vs `GQ1c` on `ollama/muse-glimmer:30b-mlx`)

| Observation | `K1` Baseline (`0b38be8a`) | `GQ1c` Verification (`99b61e27`) | Unit Verified |
|---|---|---|---|
| Distinct hues covering > 1% of frame at 10 s | 2 (`azure`, `pink`) | 5 (`red`, `yellow`, `yellow-green`, `cyan`, `azure`) | `GQ1a` + `GQ1b` |
| Frame pixels outside welcome card changed between 3 s and 10 s | `0.00%` | `1.25%` (moles bobbing in and out of burrows on the meadow island) | `GQ1a` + `GQ1b` |
| Welcome card still visible at 10 s (`card_at_10s`) | `true` (covered top-left corner) | `false` (visible at 3 s, hidden at 6 s before the 10 s capture) | `GQ1a` |
| Automatic `project.check` after file-changing turn | Manual driver command required | Automatic internal command (`received_via: "internal"`) after both Turn 1 and Turn 2 | `A1` |
| Staleness state on `check` and `preview` views | Not tracked | `check.files: "current"` and `preview.files: "current"` verified | `T2` |
| Check errors supplied to fix-up turn | None (agent fixed blind) | Check 1's `tsc.log` (4 TypeScript errors) and 1 page exception appended automatically; Turn 2 fixed all 4 TypeScript errors (`tsc` exit 0) and the page exception (`0` exceptions) | `FX1` |
| Non-TypeScript files left in `src/` | 8 files (`make_mole.py`, `mole_gen.py`, `patch.txt`, `patch_mole.txt`, `test.txt`, `test_patch.txt`, `x.txt`, `index.patch.txt`) | 0 `.py`/`.txt`/`.sh`/`.patch` files in `src/` (only 1 five-byte `src/index.new.ts.bak` and an empty `tmp/new_index.ts` outside `src/`) | `SC1` |
| Ollama model state after turn/run completion | Remained loaded in memory (`23.6 GB`), blocking the next run for 270 s under the 40% memory gate | Unloaded automatically (`keep_alive: 0`) within 1 s of `runtime.turn.completed`; `/api/ps` `[]` and 71% memory free before Turn 2 (`0 s` wait) and `[]` after shutdown | `UL1` |

## Remaining Risks and Open Observations

- **Backup file suffix (`.bak`) and `tmp/` folder:** while `SC1` eliminated all `.py`, `.txt`,
  `.sh`, and `.patch` scripts from `src/` (down from 8 in `K1` and 6 in `GQ1a` to 0 in `GQ1c`), the
  model left one 5-byte `src/index.new.ts.bak` file and an empty 0-byte `tmp/new_index.ts` file
  outside `src/`. Neither affects `tsc --noEmit` or Vite's build because neither is `.ts` inside
  `src/`, but a future tightening of `AGENTS.md` could also name `.bak` files and temporary
  folders.
- **Unloading between turns of the same workstream (`UL1`):** because `OllamaUnloader` unloads a
  model Halcyonic loaded when `runtime.turn.completed` fires and no other execution is active,
  memory returns immediately while the automatic check (`A1`) runs its headless Chromium instances
  (preventing memory pressure during the 7-browser twin check), at the cost of Ollama reloading the
  model from disk if the user sends a follow-up turn.
