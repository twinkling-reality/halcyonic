# The showcase finals on the owner's Mac (2026-11-04)

Three VR games, each made by Halcyonic from one instruction: a confined Codex on a local model wrote each, the host
checked it, and frames from the check's own browser show what a person sees.

## Question

As it stands on main, can Halcyonic turn one instruction into a VR game that a person reads at a glance: cohesive,
tactile, animated and fun, never a debug shape or a blank page? The test covers two new games from the starter, and a VR
version of an existing 2D game.

## Environment

- **The code:** main `ee0cabc0`.
- **The control plane:** the owner's own, on 127.0.0.1:47800, with the owner's data folder. Before the runs, that
  folder was set up:
  - the starter's pinned set, an APFS clone of a set the lane had installed, recorded by `pnpm mac-setup starter`;
  - the controller and hand models;
  - the showcase root allowed;
  - undo with `/usr/bin/git`.
- **The agent:** Codex 0.157.0, confined to each project (ADR 0041, with a temporary folder of its own), on
  `muse-glimmer:30b-mlx` through Ollama. Memory was 81% free at the first start, and never under 72% at a start.
- **What happens after each turn:** the lane's driver denied every approval, and a question would have stopped the run.
  After each turn that changed files, the host's own check ran: type check, build, the page in the check's browser,
  drawn of its own (DRAWN), the laptop step with the twin replay, and VR offered. While the check failed, the driver sent
  "It doesn't work yet. Please fix it so the game plays.", to which the host adds the check's errors. It allowed at most
  two such turns and stopped a run after a second failed check.
- **The instructions:** Bonk the Moles and Star Catcher began from a person's sentence: what the player sees and does,
  the score, and the game's name on its card. Balloon Pop VR began from the host's own instruction for a VR version of
  the 2D Balloon Pop, the original's own folder, which has its own git history.

## Result

| Game | Turns | Checks | Own pixels | Laptop and VR | Time |
| --- | --- | --- | --- | --- | --- |
| Bonk the Moles | 2 (806 s, 440 s) | first failed (type check 2, build 1), then passed and ready | 3.42% | 6 targets, a press seen against the twin; VR offered | 23 min |
| Star Catcher | 1 (380 s) | passed and ready | 3.90% | 5 targets, a press seen; VR offered | 8 min |
| Balloon Pop VR | 2 (755 s, 1,506 s; the second stopped at the driver's 25 min limit) | first failed (type check 2, one page exception), then passed and ready | 6.19% | 9 targets, a press seen; VR offered | 40 min |

In each passed check:

- There was no uncaught exception.
- `reads_confined` was true.
- The page was compared with the starter's own picture and found drawn of its own. The no-game cases on this Mac
  differ from that picture by 0.

**What a person sees.** These are frames from the check's browser at 1 s, 3 s and 6 s, then with real mouse presses
on the targets the starter's input layer reports:

- **Bonk the Moles:** friendly brown critter moles peek out of a ring of holes on the decorated island and pop up and
  down. A bonk bursts confetti, and the big score sign climbs: to 9 in eight seconds of presses every 300 ms. A first
  pass, pressing each hole in turn every 1.5 s, met only holes whose mole was down.
  - Card: "Bonk the Moles", "Bonk the popping moles for points".
- **Star Catcher:** the kit's yellow stars drift down over the island toward a woven basket. A catch bursts confetti, and the
  "Stars" sign counts.
  - Card: "Star Catcher", "Move the basket to catch falling stars".
- **Balloon Pop VR:** balloons in many colours, each with its knot and string, rise over the island. A press pops one
  with confetti, and the score climbs to 10.
  - Card: "Balloon Pop", "Pop the balloons before they float away". The host's VR-version instruction now asks for
    the card.

All three read at a glance as the game they are: on the moving, decorated island, built from the starter's toy kit,
with the score on the sign. None showed a bare shape or a blank page.

**What the host saw of the agent:**

- The person's own Codex folder (`~/.codex`) did not change in any run.
- In the data folder, only Halcyonic's own files changed: its journal, the checks (with the starter's picture DRAWN
  keeps), previews, checkpoints, and the confined Codex's home, temporary folder, server records and profiles. The
  starter's folder also changed, where a check copies the host's files.
- The Balloon Pop VR agent asked three times to remove backup files it had made; each was denied, and no backup is left
  in that project.
- Bonk the Moles kept one, `src/index.ts.bak`, which the agent did not try to remove.

## Since the rehearsals

Four rehearsals of these games on a scratch control plane shaped the starter and the host ([iwsdk-starter.md](iwsdk-starter.md)):

- **The game's own time** for press handlers (U7). A rehearsal's moles froze mid-bonk.
- **The toy kit** (U8). The rehearsal games' moles, stars and basket were bare spheres and a cylinder.
- **Writing with the file-editing tool** (U9), and a temporary folder for the confined agent (ADR 0041's amendment of
  2026-11-03). A rehearsal ended with no game when every here-document was refused.
- **The card sentence** in the VR-version instruction. A rehearsal kept "Untitled Game".
- **Drawn of the project's own** (DRAWN). Before it, every page read drawn, the empty starter's included.

## Not tried

- **The headset:** the three games are next previewed on the Quest.
- **Frame by frame:** whether a golden mole or a rainbow star, worth five, shows.
- **Two players, or a second model.**
