# ADR 0026: The headset's interface is a game menu of places, sections and prompts, on one plane facing the eyes

- Status: Proposed
- Date: 2026-10-02

## Context

[ADR 0023](0023-the-headset-interface-is-one-system-of-tokens-and-components.md) kept a task's
workspace as one 44 by 26 degree panel: the title and goal, tabs, question pills, Refresh and a pager
beside the heading, the answer and a bar of buttons. On 2026-10-02 lane W measured it in
`WorkspaceRender`: about 24 of its 26 degrees are fixed rows, which leaves about 2 for an answer
([headset-redesign-research.md](../validation/headset-redesign-research.md)).

The owner rejected that panel, then a first round of 3 directions: too many things still forced into
one panel, and buttons all over the place. They asked for a game menu's organisation, naming Dead
Space, Destiny 2 and detective case files. From public sources on those games (same record), lane V
rendered 2 more directions in the editor (`DirectionsRender`), and the owner chose D, a game menu,
with 7 changes: footer prompts after Destiny 2, glass without blur, a strict type scale and grid after
Detroit: Become Human, minimal screens with a connected side panel, a light line from the character,
generic file-type icons under Apache-2.0 or MIT, and a Tasks view of every task.

On D, styled, the owner then asked for 5 more things:

1. Every surface faces the person squarely, on one shared plane, edges aligned, with side panels
   along that plane.
2. No generic web selection treatment, such as an underline under a tab or a bar beside a row.
3. The places Tasks, Projects, Usage and Settings, with creating a project an action.
4. Each section its own rounded shape, in a row above the content.
5. Type whose size follows importance and only ever steps down.

Rendering them the same day showed the cost of standing that plane upright. Below the eyes, the eyes
meet an upright plane at a slant, and its text spans its size times the square of the cosine of its
elevation: from 18 to 42 degrees below eye level, down to 56 percent, under Meta's 14 dp. The size
checks measure at a surface's perpendicular distance, exact only for a surface that faces the eyes,
so they passed, and the level renders, keeping verticals parallel, hid it (same record, "A plane
below the eyes, measured"). The coordinator chose one plane tipped back to face the eyes at its
centre, which keeps every word at its size.

## Decision

- **The stage stays; a menu takes the rail's place.** Its places are Tasks, Projects, Usage and
  Settings; Usage left's glance and Settings' sections become their pages. Closed, the menu is a
  slim bar with one line saying what waits and how to open it. Open, it is a column under the stage:
  its subject, a row of the places, and the chosen place's content. Creating is an action, not a
  place: New project is the main prompt of Projects.
- **Tasks is the orchestration view:** every running and recent task across all projects, what waits
  first, grouped by project. A row is a state icon, the task's title, a small fact (its project, a
  time, what it needs) and a chevron. Choosing a row slides that task's file out beside the menu.
  Tasks only reads and navigates; actions stay in files. Rows that do not fit page by footer prompts.
- **A task's file replaces the workspace panel:** its subject, the task's title alone; a row of 4
  sections, Waiting, Activity, Changes and Checks, an amber dot on the one that waits; a page of the
  chosen section alone, in a few short lines; and a footer of prompts. No sub-sections. A line with
  more opens a side panel that slides out to the right on the same plane (the changed files with their
  icons, the agent's reasons, the checks), holding no action but its own Close. The file opens on
  Waiting when something waits, else on Activity, out of its character, with a light line from the
  character's body to the file's top corners.
- **New project** shows its steps as a row of shapes, as a file shows its sections: Your idea,
  Questions, Recap, Start building. The companion's turn is the Questions page; the recap and the
  paged review before Yes, start building keep their rules.
- **A footer of prompts.** Each action is a round key cap holding its icon, then its words, with no
  plate. Hit areas are 60 dp tall, unseen, 12 mm apart. Close is always far left; the one main action
  always far right, its cap filled with the accent and its words in the accent; at most one secondary
  beside it, and one rare action, such as Stop, beside Close. Pointed at, a prompt shows the selection
  treatment's frame; pressed, its cap sinks and Touch plays. Confirmations keep ADR 0023's rule: Yes
  appears in a slot no control held on that page or since, Cancel takes the first press's place, and
  the whole request shows before Yes, its parts paged by footer prompts.

The design system, as hard rules. The renders check those marked checked, in `DirectionsRender`
today and, once built, in lane U's renders of every surface:

1. **One plane, facing the eyes at its centre (checked).** The subject, sections, content and side
   panels of a composition are separate shapes on one flat plane, square to the line of sight at the
   composition's centre (within 0.5 degrees), 0.46 m away, never rolled; no part is angled against
   another. Parts stand a degree apart on the plane and columns 15 mm apart; the columns start on one
   line and end on one line. Detail slides out to the right, along the plane.
2. **Text as the eyes see it (checked).** Each label's em is measured from the eyes, slant included,
   and no text reads under Meta's 14 dp (`Glaze.MinimumTextDegrees`). A flat plane loses size away
   from its centre, to 84 percent at the far corners of a menu and file side by side, so the smallest
   size, 15 dp, is only for small facts inside a row, and prompts are 18 dp. An upright plane stays in
   the renders as the case this check must catch.
3. **One selection treatment (checked).** Chosen, a shape lights up, white at 10 percent, and gains a
   crisp white frame; pointed at, it gains the frame alone, fainter. Places, sections, rows, answers
   and prompts all use it. Nothing is marked by an underline or a bar, and the accent marks only the
   main action.
4. **Type only steps down (checked).** Three sizes on the menu's surfaces: Title, 24 dp, drawn light,
   for the subject, one a column; Body, 18 dp, for the sections, the content and the prompts; Label,
   15 dp, for small facts inside a row and the names of facts in a side panel. From the top of a
   column to its bottom, size never grows again. Only the chosen section and the main action are
   drawn heavier.
5. **One grid.** An 8 dp step: 24 dp padding, 16 dp between groups, 8 dp from a label to its value.
   Words and icons start on one left content line, small facts and chevrons end on the right one, and
   icons stand in a fixed 24 dp column. A shape round words, as an answer or a well, reaches 0.7
   degrees past the content line, and its words stay on it. Every shape has one corner radius, 0.9
   degrees.
6. **Glass.** The panel colour at 96 percent opacity; a light from the top edge fading out by a third
   of the height; a sheen along the top edge; a hairline edge. No blur and no corner ticks. In the
   product the `Surface` shader draws it in one call.
7. **File-type icons** from Material Symbols Rounded (Apache-2.0,
   [material-symbols.md](../validation/material-symbols.md)), generic only: code, database,
   data_object, description, image, terminal, deployed_code, folder, and chevron_right for what opens
   more. No language or brand logo, which the competition's rules forbid
   ([competition-judge-build.md](../validation/competition-judge-build.md)). `glaze_icons.py` and the
   atlas add them.

- **The client core keeps every model and presenter, never weakened:** `WorkspaceScreen`,
  `WorkspaceSteering`, `QuestionPlace`, `QuestionDraft`; the presenters in `WorkAnswers.cs`
  (`UnderstandingPresenter`, `CheckedPresenter` and `AnswerRoom`, `AnswerDepth` giving a section's
  brief lines and a side panel's full ones) with lane W's truth rules: each claim keeps its class, a
  line without a chip only for observed facts with the source named on every page, Inferred always
  chipped, the agent's words quoted, and each Seorak part's own availability, coverage and freshness;
  `EntryScreens`' flows, `NewWorkDraft`, `NewWorkReview`, `NewWorkSubmission`, `ProjectIdea`,
  `FolderConnect` and `ConnectScreens`; and lane C's companion models once they reach main. New models
  for the menu, its sections, page lines, side panels and footer slots replace `PanelModel`'s tabs
  and bar.
- **Kept from ADR 0023:** tokens in the client core; `GlazeText` and `GlazeButton`, extended with the
  light title, a Prompt role and a Row role without a plate; the state language, badges, icons and
  atlas; character labels and the peek; touch distance, 0.46 m; 60 dp targets 12 mm apart; the Glaze
  sounds; and the render checks, extended by the rules above. **Superseded once this is accepted:**
  foreground panels of 44 by 26 degrees with Move, Reset position and Close in a header and every
  action in a bottom bar; the workspace's tabs and sub-tabs, its Close at the end of the tabs, and
  Refresh and pagers beside headings; the rail's 2 rows of buttons; `PanelFrame` as every surface's
  frame; and the entry panel's screens, which move into Projects and New project.

## Alternatives considered

- **One panel with better names.** Rejected by the owner twice: the fixed rows and the buttons stay.
- **A, a card and a shelf; B, a story thread; C, one step at a time.** Rejected in the first round.
- **E, a case file with an index**, projected from its character. Every section's line at once, but
  taller and 2 pages to read. Its light line is kept.
- **D, styled: each surface facing the eyes on its own.** The surfaces turn against each other; the
  owner rejected it.
- **One upright plane**, the owner's first wording. The eyes meet it at a slant: it narrows toward
  its bottom and its text shrinks to 56 percent. Set aside on 2026-10-02 once the renders measured
  text as the eyes see it.
- **An upright plane raised to near eye level**, where the slant costs under 7 percent within 15
  degrees. The stage would have to step up or back whenever a menu opens.
- **One gentle common curve**, which the owner allowed. It keeps text at the sides at its size, but
  its columns turn against each other; not needed while a composition stays within about 64 degrees.
- **Prompts at 15 dp**, the size of facts. At the far corners of a menu and file side by side they
  read 0.78 degrees, under 14 dp.
- **Meta's Interaction SDK UI Set**, declined in ADR 0023; nothing has changed. **Glass with
  real-time blur:** its cost on a Quest is unmeasured, and an independent case study names the Dead
  Space remake's blurred, low-contrast text as a weakness.

## Consequences

- A person learns once where to look and where to press, and a section's page gets most of the
  frame. The lanes rebuild the presentation, not the logic.
- The menu and a file side by side make a plane 64 by 32 degrees, from 18 to 50 degrees below eye
  level along its middle, its bottom edge 0.48 m away; ADR 0023's panels reach about 42 degrees. A
  file alone, 38 by 24 degrees, reaches 40. Whether the low edge is comfortable is judged on the
  headset, beside the open question on panels taller than 26 degrees
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)); if not, a menu shows 3 rows a page.
- Larger text, 15 percent, must still fit: each surface keeps about 15 percent spare, and a
  composition is checked inside a Quest 3S's field at both sizes, read with the head tipped
  (`WorkspacePlacement.ReadingPitch`, at most 8 degrees) when it is taller than designed.
- Contrast at 96 percent is calculated, not measured: over a white wall, as in passthrough, secondary
  text keeps 4.8:1 (`GlazeChecks.Over`). The contrast check composites every surface over white, and
  the opacity rises or the secondary colour lightens wherever text falls short.
- Lane V's refined renders count 137 draw calls for the scene with the menu and a file, and 46 to 51
  for each column before batching; the separate shapes share one material in the product. The
  budgets are 60 a surface and 220 a scene.
- The renders' new checks move into lane U's `GlazeChecks` for every surface: text as the eyes see
  it, one plane facing the eyes, aligned columns and one selection treatment.
- Move and Reset position (ADR 0023, 2026-10-02) have no slot in the footer; whether the menu's plane
  keeps them is open.
- Not yet checked on a headset: reading at 0.46 m, the low edge at 50 degrees, the glass in
  passthrough against bright walls, the light line's comfort, unseen hit areas and their feedback,
  and turning between a file and its side panel.
- Revisit if reading at 0.46 m strains, if the low edge strains the neck, if unseen hit areas bring
  wrong presses, if the glass fails contrast in real rooms, or if the draw call budget is exceeded.
