# What the interface costs a Quest 3

- **Question:** What does the interface of [ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)
  ask of a Quest 3 to draw and to keep current: draw calls against the ADR's budget of 60 a panel
  and 220 a scene, text meshes, triangles, and what it allocates each frame? Which of it can be
  measured off the device, and what only the headset tells?
- **Date:** 2026-10-01.
- **Environment:** Unity 6000.3.25f1 on an Apple M5 Max with macOS 26.7, the editor in batch mode
  on Metal, the Android build target, the project at the lane U head after ADR 0023's fifth step;
  the client core on .NET 10. No headset.
- **Method:** `MeasureRender` (**Halcyonic > Measure the Interface**, in batch mode as the runbook
  says) builds each surface as the person meets it, with the stage's own lineup, presenter and
  frame, and measures it after a first pass that lets the editor settle. Draw calls are counted as
  an upper bound: every material of every renderer that has something to draw, before any batching,
  since the editor's render statistics are not kept in batch mode. A screen shown again is counted
  by TextMeshPro's own event for every text mesh it builds (`TMPro_EventManager.TEXT_CHANGED_EVENT`).
  Allocations are Unity's own count of what the managed heap hands out ("GC Allocated In Frame",
  read before and after a stretch, the least of twelve repeats a moment apart), because Unity's Mono
  counts no thread's allocations and its heap's size moves only when it collects; the measure first
  checks that a 256-byte array counts (288 bytes). The client core's per-frame code is measured by
  `PerFrameTests` on .NET.
- **Status:** Measured off the device: draw calls at most, text, triangles, text meshes built again,
  the editor's time for that, and allocations. Not measured: anything on the headset (below).

## Findings

### Draw calls and geometry

Measured again on 2026-10-02, with the icons on the badges, the marks and the actions:

| Surface | Draw calls at most | Of them, the panel's | Text labels (characters) | Triangles |
| --- | --- | --- | --- | --- |
| The stage, rail, banner and a peek | 75 | 22 (the rail) | 41 (558) | 32,410 |
| Beside a window, another window with focus | 24 | none open | 12 (175) | 21,076 |
| The entry panel, Connect projects | 72 | 32 | 38 (463) | 32,346 |
| A workspace, Waiting for you | 75 | 35 | 40 (521) | 32,356 |
| A workspace, Doing | 68 | 28 | 36 (474) | 32,264 |
| Usage left, four windows | 69 | 29 | 32 (673) | 32,574 |
| Settings | 58 | 18 | 29 (542) | 32,338 |

- Every surface is far inside the budget, before any batching: at most 75 draw calls of 220 for
  everything showing, at most 35 of 60 for a panel. Surfaces share one material with instanced
  properties, text one font material and icons the icon atlas's, so the device can only draw fewer.
- Each icon is a label of one glyph with its own renderer, so it counts one more draw call, label
  and character and two more triangles. The icons on the characters' badges and marks added 4 to 7
  draw calls to each surface, and those on the actions 1 to 6 to a panel (on 2026-10-01, before
  any icon: 64 for the stage, 28 for a panel). A badge too long for its icon on the stage, and a
  bar too full for its actions' icons, draw none.
- The characters are nearly all the geometry: six bodies are about 32,000 triangles, a panel about
  500 to 900. Their shader's cost is estimated in [character-rendering.md](character-rendering.md).

### Laying out again what has not changed

The workspace, the entry panel and the rail lay their screen out again every half second while
they show, so the times and notices that change on their own stay current; Usage left every 15
seconds. Before this pass every label built its text mesh again each time, with nothing changed:

| Surface | Text meshes built again | Editor's time, before | After |
| --- | --- | --- | --- |
| The rail | 11 | 0.34 ms | 0.14 ms, none built |
| The entry panel, Connect projects | 18 | 0.50 ms | 0.13 ms, none built |
| A workspace, Waiting for you | 16 | 0.55 ms | 0.15 ms, none built |
| A workspace, Doing | 15 | 0.46 ms | 0.11 ms, none built |
| Usage left, four windows | 21 | 0.98 ms | 0.06 ms, none built |

- `GlazeText.Lay` forced every label's mesh to be built (`ForceMeshUpdate`). It now builds one only
  when the label's box changed or TextMeshPro says a property did: every TextMeshPro setter marks a
  change only for a new value (read in the package's source, `TMP_Text.cs`), so an unchanged label
  keeps its mesh.
- A line whose words stay the same but which becomes the agent's words, or stops being, is built
  again for the lean alone: the frame marks it changed when its lean flips, before laying it out.
  Measured on the mesh, drawn once after each change: 0.35 of its height, then upright, then 0.35.
  TextMeshPro runs the lean's hook only for a label that is active, so a line shown for the first
  time leans when it is first drawn, as it is activated, never earlier; nothing reads its mesh before.
- Two labels were still built twice each time, measured with one text and shown with another: a
  row's detail, when its short form was the one that fit, now remembers which fit for the same words
  and width (`GlazeButton.LayRow`); and a list's lines, which the frame measured on the first line
  shown, are now measured on a hidden label and the height kept for the same words, as rows' are
  (`PanelFrame.MeasureLine`).
- Laying a screen out again still allocates 4,600 to 7,900 bytes, the screen's model and its words,
  about 10 to 16 KB a second while a panel shows. Small, but not zero; the headset tells whether
  collecting it ever shows.

### Each frame

- Every per-frame method of the parts measured allocates nothing once warm: the characters'
  animation, their targets turning to the person, breathing badges, buttons, the peek, the rail,
  Usage left and Settings.
- Three did before: the peek walked the characters through `IEnumerable`, boxing an enumerator
  every frame it showed (40 bytes); it now takes a list the director refills every frame. The rail
  and Settings looked for a component that was not there every frame with `GetComponent`, which in
  the editor allocates for the missing component (about 600 bytes); `TryGetComponent` does not.
  In a player that lookup allocated nothing, but cost a call each frame until the component came.
- The client core's per-frame code allocates nothing over a thousand frames (`PerFrameTests`): focus
  presence, the peek's choice, the stage's placement in front of the person, and a section's read
  once it has its answer.

## What only the headset tells

- **Frame time.** Whether 72 frames a second hold with six characters, a panel and the peek, on the
  CPU and the GPU; the editor's milliseconds above are a Mac's, several times faster than a Quest
  3's. Follow `adb logcat -s VrApi` (frames a second) with the workspace open for a minute, and look
  for a hitch every half second.
- **Draw calls as drawn.** How many the device issues after batching, with both eyes in one pass
  under multiview; the counts above are the most it could be.
- **Fill.** A panel at 0.46 m covers a large share of each eye, every surface blended over what is
  behind; the characters' shader runs over about 240,000 pixels a frame across both eyes
  ([character-rendering.md](character-rendering.md)). Only the GPU's own counters say whether that
  is fine at the Quest 3's resolution.
- **Collection.** Whether collecting the few kilobytes a screen allocates every half second ever
  shows as a pause.
- **Heat.** Whether the device throttles over an hour beside the characters.
- **Sound.** How long the 81 clips take to render on the device at startup (`Halcyonic: sound
  ready ...`); 1.4 s on the Mac's .NET.

The checks for a session are in [XR_DEVELOPMENT.md](../runbooks/XR_DEVELOPMENT.md), "The interface on a
Quest".
