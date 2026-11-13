# Make VR Version on larger web apps that are not games (VRIFY, 2026-11-09)

## Question

Make VR Version turned a small 2D canvas game into a VR game in one instruction ([showcase-finals.md](showcase-finals.md)).
Does it work on larger web apps that are not games: a 3D astronomy app, a site analysis map app with a terrain model,
and a classroom lesson app? The three are the owner's own projects; this record describes them only by what they are.

## Environment

- **The originals:** local clones of each project's committed content (`git clone --local --no-hardlinks`), outside
  this repository, with no `.env` or credentials. Dependencies installed offline from each project's own lockfile.
- **The control plane:** a scratch one on 127.0.0.1, with its own data folder: the starter's pinned set, Codex 0.157.0
  and OpenCode 2.0.18 as `pnpm mac-setup agent-apps` checks them, `muse-glimmer:30b-mlx` through Ollama, the clones'
  folder allowed, undo with `/usr/bin/git`.
- **The driver:** sends what the headset sends (`project.create` for the original, `project.make_vr_version`, then
  `workstream.create` titled from the recorded instruction with the instruction as its objective, and
  `execution.start` on Codex with it), denies every approval, and allows at most two fix-up turns ("It doesn't work
  yet. Please fix it.", to which the host adds the check's errors), each turn capped at 25 minutes.
- **The browser:** frames from the check's own contained browser (chrome-headless-shell in Seatbelt, outbound only to
  the page's port).

## The originals, and the view picked for each

| Original | What it is | View for VR | Runs offline with no keys |
| --- | --- | --- | --- |
| A night-sky astronomy app | A web app with a 3D journey through space, drawn with three.js and WebGL2 shaders, with large textures and catalogs | The Earth in a starfield | Yes, for that view. Its features that need its own back-end services were left out |
| A site analysis map app | A web map app (MapLibre GL, three.js) backed by a database | The site's ground as a raised relief model | Only with its database. A scratch database on loopback, filled from the project's own migrations and seed data, shows it. Its map data is in its repository |
| A classroom lesson app | A web app whose lessons are web pages (cards and steps); three.js draws only a small figure | One lesson, step by step | Yes |

Each frame showed the project's own name or logo, and one credits its image sources in a corner: a frame for the
video would need those out of view (competition content rules).

## Results

### The astronomy app, as the product stood (main `c002823a`)

The host's instruction was the game-shaped one: "Read that folder to understand the game: what you see, what you do,
how you score or win", then build "its main play and its score" from the starter's small game.

- **Failed.** Three turns of 25 minutes each (the first and both fix-ups), each stopped at the driver's cap, from
  20:14Z to 21:29Z. The agent wrote no file, so the host ran no check.
- **What it did:** 584 commands, all reading. 177 of them searched the original for the starter's game words: `game`,
  `score`, `critter`, `bonk`, `welcome`, `press one`, `win`. None opened the folder where the app draws its scenes.
- **The owner's folders:** nothing in `~/.halcyonic` or `~/.codex` changed (names, sizes and times, three levels deep).

A local model told to find a game's score in an app that has none keeps looking for one.

### The astronomy app, with an instruction for an app that is not a game

The change (one commit): the host's instruction asks whether the original is a game. A game still gets the main play
and its score from the starter's small game, as before. Anything else gets "its one most striking view, the scene
people open it for". The starter has a section for that, "A view from another app": `setupScene` in place of the
island, how to find the drawing code fast, rebuilding the original's own look, and one press that answers.

- **First try: passed the check, failed the look.** One turn of 390 s; the check passed and was ready: type check and
  build 0, no exception, one target with a press seen, VR offered, reads confined.
  - The agent followed the new steps. It listed the files that import three.js, read the original's sky shader,
    copied its sky picture (4.6 MB) and used `setupScene`.
  - What a person sees is a small blue ball on black. The sky picture was on a lit material in a scene with no light,
    so it drew black. The subject was a placeholder ball, not the original's Earth.
  - So the starter gained what its games already had: a whole small view to start from (a picture sky that needs no
    light, a lit subject with its own picture, one press that brings it closer), checked with the host's own check,
    and a line that `setupScene` adds no light.
- **Second try: passed and ready, and it reads as a place.** One turn of 786 s, 21:42Z to 21:55Z; the check passed and
  was ready, with the same findings as the first.
  - The frame shows the original's own panorama of the Milky Way all round the person, with the Earth in front from
    the original's picture of it, turning slowly. A press brings it closer.
  - It is the worked view with the original's two pictures and colours: the agent did not port the original's
    shaders, so there is no atmosphere, cloud layer or night lights.
  - The Earth reads dim: most of what faces the person is ocean, which that picture shows nearly black.
  - The card names the original and carries a line from its own pitch, not what the VR version does.

### The map app, with the instruction for an app that is not a game

The run on main's instruction was not made: by then the starter in this folder already had the new section, and the
plane would have mixed the new starter with the old instruction. The astronomy app's run stands for the old path.

- **First try (on the first commit): passed the check, failed the look.** One turn of 791 s, 21:57Z
  to 22:10Z; passed and ready (the laptop step's mouse did not reach the target; a key press did, and the response
  was seen).
  - The agent took the worked view and put two of the original's pictures in it: a shaded world map as the sky, and a
    small picture of the site on a flat 3 m card in the planet's place.
  - What a person sees is a grey world map all round and the card turning edge-on: a debug shape, not the ground.
  - Land needs a relief, which a planet does not show. So the starter gained `relief()`, which raises real ground
    from a height picture (grey, or Terrarium's encoding, which the original's own map notes name), coloured by
    height or draped with a picture of exactly the same ground. On one of the original's terrain tiles in the host's
    check it stands as a mountain range on a dark base, passed and ready.
- **Second try (with `relief()`): passed the check, not ready, failed the look.** One turn stopped at the 25-minute
  cap, 22:14Z to 22:39Z; its files were then checked: passed, not ready (the press drew no change the check could see).
  - The agent found the original's own site assets and used `relief()`. For heights it took a picture of a few
    elevation bands, and for the drape a land-cover map. A person sees a blocky blue shape on a dark base.
  - The original's real heights are a float32 grid with its metadata beside it, which `relief()` could not take, and
    a land-cover map's colours are class codes, not a picture. So `relief()` now takes a grid of heights, and the
    starter says to follow the original's own relief view to its heights and never to drape a coded data image. With
    the original's own grid, the host's check shows the ground and its ridges coloured by height, passed and ready.
- **Third try (with the grid): passed and ready, and it reads as the ground.** One turn of 645 s, 22:45Z to 22:56Z.
  - The agent copied the original's own height grid (5 MB), read its size from its metadata, and raised it with
    `relief()` on a table in front of the person, lit from the upper left, over a dark blue-black.
  - A person sees ridges and peaks in relief, coloured from green low ground to pale rock, on a dark base. A press
    brings the table closer for a few seconds.
  - It shows no imagery or findings: the original's data overlays, rivers and boundaries are not there.

### The lesson app, with the same instruction (on the second commit)

- **Passed the check, failed the look.** One turn of 560 s, 22:58Z to 23:07Z; passed and ready.
  - The agent took the worked view and put the original's social share image (its landing page as a picture) on a
    card, over the original's brand colour.
  - A person sees a poster of the web page floating in colour. The lesson itself, its steps building one after
    another, is not there.
  - The original draws its lessons with web pages, not 3D. Neither the worked view nor `relief()` gives the agent a
    way to bring that kind of view into VR.

### A game, on the same instruction: Balloon Pop

To see that a game still takes the game path, a clone of Balloon Pop, the finals' 2D game, ran once on the second
commit.

- **Passed and ready.** One turn of 590 s, 23:09Z to 23:19Z; four targets, a press seen.
- The agent took the game path: the starter's island in the meadow, the kit's balloons, the score sign, and the card
  "Balloon Pop", "Pop the balloons before they float away".
- In the check's frame at 10 s the balloons are small and high over the island, and the island has none of the
  starter's decor. One run cannot say whether that is worse than the finals' version.

## Verdict

| Original | One instruction, as main stood | After the starter changes |
| --- | --- | --- |
| The astronomy app | Failed: 75 minutes looking for a game, no file | Works: the Milky Way all round, the Earth in front |
| The map app | Not run (see above) | Works: the site's own relief on a table |
| The lesson app | Not run | Does not work: a poster of its landing page |

What made the difference, in order:

1. Asking for an app's most striking view instead of a game's main play and score.
2. A whole small view to start from, as games have a whole small game.
3. `relief()`, for land.
4. Following the original's own view to its data.

Each VR version is still simpler than its original. None ports the original's shaders, overlays or findings: there
is no atmosphere on the Earth, and no overlays or rivers on the ground.

## What the video would need

- **Names on screen:** the welcome card shows the original's own name for its first seconds, and the competition's
  content rules count a project's own name as a brand name. A shot must start after the card goes, or the card must
  be renamed (a hand edit to the agent's output, to be disclosed as one).
- **Credits:** the astronomy app's sky is ESO's eso0932a (credited "ESO/S. Brunier"), and its Earth is NASA's Blue
  Marble. The map app's heights are Copernicus DEM GLO-30, whose licence asks for credit. The video's credits would
  name them. Their licences were not checked here beyond the originals' own records.

## Not tried

- **The headset:** none of the VR versions has been seen on the Quest.
- **A second model:** every run was `muse-glimmer:30b-mlx`.
- **Repeats:** each try ran once, so these are single runs, not rates.
- **The map app and the lesson app on main's instruction:** not run.

## What a person does in it, not its most striking view (2026-11-13, a hosted model on the proving plane)

A hosted run of Make VR Version on an app that is not a game built its landing page well, because the instruction
asked for its one most striking view.

The instruction now asks, for an app that is not a game (the lead's decision, 2026-11-13T06:05Z), that the agent first
find from the app's own code the one thing people open it to DO and say it before building; build that use, working,
with the app's own content, never its landing page unless the app is nothing else; carry what cannot come (a server,
other people, an account) with the app's own sample content and say so; let the app's look dress the use, not
replace it; and end its turn with a few lines for the person on what the app is for, what was carried and what was
left out. "A view from another app" stays for an app whose use is itself a view. The game path is unchanged.
A fresh critic read the words on two other apps (one not a game, one a game) as a person and as an agent: the game
path is unharmed; it asked that the look line be scoped to apps that are not games, that the agent write its line and
build without waiting, that a stand-in carry what has no sample content, and that the view section yield to the use
(an app's biggest picture can sit only on its landing page). All four are in the words and the starter's AGENTS.md.

Seen on the new words: a hosted run on a small field guide written for the proof (a guide of species with a filter
and a search, a page per species, a form to log a sighting with five fields, and the log) built the guide in use, not
a scene: a critic who saw it found the cards, a species page, logging and the log, with seen or not seen on each card.
It also left out the search, fixed three of the five fields (a place to one of two, the time to now, the note to
empty), cut the log at six rows with "and N more", drew words too small to read at a laptop's distance, ran a title
into the next line, and said none of it on screen, where a person in the headset reads. Its closing words, which the
product shows with the result, did say five of those six omissions with their reasons (typing is hard in a headset);
they missed that a species page shows only a count and the last sighting, and implied it showed them all. The kit's
own pages helper
showed only its first six pages and asked for "the ones that matter most".
The same app on the local 30B model did not run to the end: it took the Mac under 30% free within three minutes and
was stopped (local-models.md, "A VR build beside other work"), so there is no local result beside the hosted one.

So the words now ask (the lead, 2026-11-13T08:20Z) that nothing of the use be dropped without a word: each way the
app finds things and each field of its forms gets a way that works with a hand (a row of the app's own choices,
minus and plus, steps of day and hour, a keyboard of letter buttons for free words), or is named among what is left
out; that every list be read to its end, and the kit's pages turn six at a time (`turn(1)`, `turn(-1)`); that words
be no smaller than the kit's type scale (`TYPE`, font sizes in metres at 1.6 m, which the pages draw with); and that
what is not carried yet be said in one line that stays in the scene beside the use and in the closing words. A fresh
critic read these words for an imagined recipe box (search, tags, a form with long steps and a photo, a sign-in) and a
small arcade game: the game path is unharmed; it found that the pages could not show what a filter leaves, that long
text and files had no rule, that a sign-in form would be rebuilt and faked, and that the welcome card's words leave at
once in VR. So the pages take `set(list)` and centre a last, partial six, short free words get a keyboard while long
text and files are named as left out, an account or a sign-in is never rebuilt, and the line stands in the scene.
The starter's check measures none of
these: its words are drawn into canvas textures, not the page, and a cut list has no one shape to find, so the
instruction carries them. Not yet seen: a run on these words.
