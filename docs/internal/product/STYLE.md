# How Halcyonic looks

How the headset's menu, a task's file, New project and their side panels look, and where everything
on them goes ([ADR 0026](../decisions/0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md)).
The values live in the client core (`apps/xr/Packages/com.halcyonic.client`), so tests and renders
hold them: `Glaze.Menu` for sizes, grid, glass and selection, `MenuFrame` for places, sections, page
lines, side panels and the footer, and `PlaneComposition` for the plane. This guide says how to use
them. How Halcyonic speaks is [WORDS.md](WORDS.md). The stage, its characters, labels and peek, keeps
[ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)'s sizes
and rules.

Every surface is reviewed from the eyes against the owner's test: one thing at a time, fixed
controls, clean. `DirectionsRender` shows each rule below on the moments it was decided for.

## The plane

- **One plane facing the eyes.** A composition, such as the menu with a task's file beside it, is
  one flat plane, square to the line of sight at its centre within 0.5 degrees, 0.46 m from the eyes
  (`PlaneComposition.Distance`), never rolled. No part is angled against another.
- **Parts and columns.** Each part is its own rounded shape, a degree from the next
  (`PartGapDegrees`); columns stand 15 mm apart (`ColumnGapMeters`). Every part of a column takes
  its width, and the columns start on one line and end on one, a shorter column's last part
  reaching down to it. The menu's column is 32 degrees wide, a file's 38.
- **Detail slides out to the right**, along the plane, and the whole plane re-centres as one piece
  over a short eased slide, so what was pressed travels least.
- **Inside the field.** A composition stays inside a Quest 3S's field, 96 by 90 degrees less 1.5 at
  each edge, with the head turned to its centre and tipped by `WorkspacePlacement.ReadingPitch`, at
  most 8 degrees. The plane's top stands 17.5 degrees below eye level (`MenuPage.TopDegrees`), and
  the menu and a file side by side take 64.3 by 33.0 degrees, to 50.5 below eye level. A list of
  rows holds 4 a page at the standard text size and 3 at larger text (`MenuFrame.RowsAPage`), its
  source line counting as one, and Tasks beside a file takes 3 where the stage leaves room for no
  more, as on a 3S at the far stage, decided when the menu opens; any other page packs by height, at
  most `MenuPage.Height`. The head is taken to tip as much as the plane's bottom needs, at most 8
  degrees.
- **With nothing on the stage**, the menu stands where the person looks at rest, about 15 degrees
  down.
- **Labels are cleared where they stand**: placement keeps the plane clear of each label at the
  label's own yaw, so a wide plane needn't drop for a label at its far side.
- **At larger text a side panel takes the file's place**, keeping the file's light line and pill;
  Close details brings the file back. A file and a side panel together would leave the field.
- **Beside a window**, with the characters either side of one straight ahead, the menu or a file
  stands centred under the window, one at a time, never within a degree of it; a file's side panel
  takes the file's place there at both text sizes; and the file draws no light line, which would
  cross the window.
- **Text shrinks away from the centre**, as the eyes see it: a word x across and y up, in units of
  the distance, spans sqrt(1 + x²) / (1 + x² + y²) of its size (`PlaneComposition.ShrinkAt`), down
  to 84 percent at the far corners of a menu and file side by side.
- **Moving it.** There is no Move or Reset position prompt. Holding a file's subject drags the whole
  plane round the eyes, still facing them, and Reset position lives in Settings' Your space.
- **Two columns at most.** The menu, a file and a side panel would not fit the field together. When
  a file beside the menu opens a side panel, or shows a page taller than the menu's rows leave, the
  menu steps aside off the plane with the same eased slide, as it does whenever the two don't fit
  the field together, as with a file's title on two rows; it comes back once neither holds or the
  file closes.

## A composition's parts

From the top of a column: its subject, a row of separate shapes, the page, and the footer inside
the page's shape at its bottom.

- **The subject**, one a column, at the Title size, drawn light. In the menu's column a line holds
  about 36 characters; a longer subject wraps to a second line and its plate grows. Projects' subject
  is its purpose, "What would you like to work on?".
- **The split header.** A task's file carries its character's state pill on the subject's top edge
  at its left: the same badge as the character's label on the stage, its word at 18 dp, the title
  half a grid step under the pill's lower edge. Every
  column's subject keeps the pill's room, so the plates and titles stay level.
- **The row of shapes**: the menu's places (Tasks, Projects, Usage, Settings), a file's sections
  (Waiting, Activity, Changes, Checks) or New project's steps (Your idea, Questions, Recap, Build).
  Each is its own rounded shape, the chosen one lit, an amber dot on the one that waits.
  New project's steps are its way back; there is no Back prompt.
- **The page**: a few short lines, rows and answers, then the footer.
- **A side panel**: its subject, then facts, each a name in the secondary colour over its value, both
  at 18 dp, or lines, then its source. It holds nothing to press but its own Close details.
- **The closed menu**: one rounded shape on the plane's top line, its subject at 18 dp saying what
  waits or that nothing does, and Open at its right.

## Type

Three sizes on the plane (`Glaze.Menu`):

| Size | dp | Degrees | For |
| --- | --- | --- | --- |
| Title | 24 | 1.5 | The subject, one a column, drawn light |
| Body | 18 | 1.125 | Places, sections, content, prompts, headings inside a page, a side panel's fact names, the source line, the pill's word |
| Label | 15 | 0.9375 | Small facts inside a row; never a line of its own |

- **Type only steps down** from the top of a column to its bottom. The one exception is the split
  header's pill, which reads with the subject it stands on.
- **Nothing reads under 14 dp as the eyes see it** (`Glaze.MinimumTextDegrees`), measured from the
  eyes with the slant included.
- **Small text keeps 15 dp only where it reads at 14 dp or more.** Toward the outer edges of a wide
  composition, as a side panel's counts at the far right, it takes 18 dp
  (`PlaneComposition.SmallTextDegreesAt`). The layout decides from the part's place before laying
  the text, so nothing overlaps afterwards.
- **Heavier** are only the chosen section and the main action.
- **Upright is ours; theirs leans** (the owner's pick, 2026-10-08). Everything Halcyonic didn't write,
  an agent's words, a title, a name, a label, a source's sentence and the person's own words, leans by
  the font's italic amount, never quoted, on a ground only Halcyonic draws where it meets our words:
  white at 2 percent with a 5 percent hairline, in linear space as Unity blends, so it never reads as
  chosen; 0.32 degree corners; 0.25 degrees past the words, set in the sentence as a word is, our space
  before it and after it; our punctuation follows its edge as it follows a letter, clear of it and
  nearer than a word space, the ground stopping halfway to it; the words on it in the primary text colour. A
  separator such as ": " between two of theirs doesn't make the line ours.
  A row or a block all of it carries our quote mark (format_quote) in the icon column, in the
  secondary colour, where no icon of ours stands; a title in its plate leans with the plate as its
  ground. Theirs comes after our words where we can word it so, and is cut first, inside its ground.
  Pills, the light line, prompts, places and sections hold our words alone.
- **Last on a page**: an unavailable prompt's reason, then the source line, one a page, at 18 dp in
  the secondary colour. A detail under a part, as a Seorak part's coverage or freshness, is its own
  18 dp line in the secondary colour.
- **The companion's words** are one block of theirs of at most 2 rows, the page's source line naming
  the companion. Its view, when it has one, goes first in our words and the secondary colour, and the
  block is then its question alone.

## Colour

- Words in the text colour; quiet words, as a secondary line, a heading, a source or an unavailable
  prompt, in the secondary colour (`Glaze.Menu.QuietText`).
- **The accent marks only the main action.** **Amber means waiting for you** and nothing else; a
  stale or partial line says so in words, in the secondary colour.
- A line's tone is Primary, Secondary, Waiting, Good or Problem (`LineTone`).
- Every word holds 4.5 to 1 on the glass over a white wall, the brightest room in passthrough
  (`Glaze.Menu.GlassOverWhite`), and on a chosen shape over its lit fill (`Glaze.Menu.LitOverWhite`).

## Grid

- An 8 dp step, 0.5 degrees: 24 dp padding inside a shape, 16 dp between groups, 8 dp from a fact's
  name to its value.
- Words and icons start on one left content line; small facts and chevrons end on the right one.
  Icons stand in a fixed 24 dp column.
- A shape round words, as an answer or a well, reaches 0.7 degrees past the content line, and its
  words stay on the line.
- Every shape has one corner radius, 0.9 degrees.

## Glass

`Surface.DrawGlass` draws it in one call of the surface shader:

- the panel colour at 96 percent opacity, with no blur;
- a white hairline edge, 12 percent and 0.06 degrees;
- a light from the top edge, white at 6 percent, fading out by a third of the height; on a page of
  content it ends within the top padding, so no row stands in it and nothing looks chosen that is
  not;
- a sheen just inside the top edge, 20 percent and 0.06 degrees.

No corner ticks.

## Selection

One treatment for places, sections, rows, answers and prompts:

- **Chosen**: the shape lights up, white at 10 percent, and gains a crisp white frame at 78 percent.
- **Pointed at**: the frame alone, at 42 percent.
- Nothing is marked by an underline or a bar.
- Choosing lights; it never acts. Answers to a question that takes several may be chosen together;
  any other row is chosen one at a time.

## Rows and answers

- **A row**: its icon in the icon column, its words, a small fact at the right and, when it opens
  more, a chevron. Rows are 48 dp tall and 12 mm apart. Its state's tone stands on its icon, and on
  its fact only where the row waits, in amber; its words stay in the text colour, and a line with
  neither an icon nor a fact takes the tone on its words. A list's small facts are one kind, as
  Tasks' projects, in the secondary colour. A small fact keeps 18 dp's room wherever it stands, so a
  line wraps the same anywhere, and a fact from outside takes at most 40 percent of its line. On a
  page none of whose rows has an icon, as Usage's and Settings', words start on the content line.
  Nothing is drawn under a row: a meter there reads as an underline, so the small fact carries a
  share in words.
- **Rows only take you somewhere**: to a side panel, a page or a place. Ordinary rows never act and carry
  no side action. Focused answer controls are explicit bounded exceptions, including the truthful
  Stop escape inside the ambiguous-answer explanation. Choosing one may set the footer's main action or the prompt beside it, as a
  project's Add a task and Hide from stage. A line that reports a problem is a row that opens its
  side panel, and what fixes it is the footer's main action while it is chosen.
- **An offered answer** has a hairline shape at rest and the existing lit fill/frame when selected.
  Its provenance quote/lean stays at left; a persistent radio mark for one choice or checkbox mark for
  multiple choices stands in a separately reserved area at right. Hover adds only the frame and never
  changes that mark. The selected centre/check uses existing surface and check-glyph primitives.
- **Focused answer controls** are visibly bounded at rest: More choices, Write an answer, selected
  Edit answer, review Edit and clipped Read question. They keep the 48 dp targets and 12 mm spacing.
  Write/pager pairing uses actual half-width measurement. A saved custom answer has one lit Edit
  answer control with Selected fact, and its words stay fully accessible in editor and review.
- **The answer field** is bounded and labeled Your answer. Keyboard focus uses the lit treatment;
  a plain vertical insertion caret is shown only for the empty focused keyboard field. It is an input
  cue, not a selection bar; the generic prohibition on bars/underlines remains. Populated/long fields
  do not invent a caret position while the native keyboard owns editing. Voice-only fields promise
  Hold to talk below, never a keyboard. Done/transcription only change a buffer; Use this answer saves.
- **The focused composition** keeps the task title/state and one question, its choices and forward
  footer. File tabs and the competing Tasks/menu are suppressed without dropping navigator state.
  Short question text stays complete; long questions have full read-through and bounded clipped
  context with Read question. Choice-page count stays in the selection caption, with no extra target.
  Mandatory review shows all answers; only its final read part offers Send answer. Editor Cancel is
  the local Close-slot exception and preserves the saved draft. The column's Close discards the draft.

- **A page packs by height**, not rows: a line of words takes its line, a target its 48 dp, against
  the page's content height (`MenuFrame`), for the file alone. A question and its answers are one
  group, 8 dp apart.
- **Offered answers keep their order** and none is omitted. An answer that does not fit whole
  opens guarded full details; the page does not shrink text or hit targets. New project's own
  question/recap flow retains its separate behavior in the client contract.
- **A claim keeps its evidence class's chip**: "Agent says", "Subagent says", "Inferred", "Planned",
  "Author unknown" or "Model explains", beside the claim's words, which lean. Observed and measured
  facts, and Halcyonic's own words, take none.

## The footer

- **Places** (`PromptSlot`): Close far left; one rare action beside it, as Stop; the free middle,
  which only a confirmation's Yes takes; a secondary prompt beside the main action; and the main
  action far right.
- **A prompt** is a round key cap, 1.45 degrees, holding its icon at a degree (ADR 0023's floor for
  icons), then its words at 18 dp, with no
  plate. Hit areas are 60 dp tall, unseen, 12 mm apart. The footer follows the page's last target
  12 mm below it, with no 16 dp gap before it. Pointed at, it shows the selection frame;
  pressed, its cap sinks and Touch plays.
- **The main action**, one a page and only at the far right: its cap filled with the accent, its
  words heavier in the accent. Anything else there is drawn plain.
- **Unavailable**, a prompt keeps its place, drawn quiet: its cap outlined and its words in the
  secondary colour. An action says why, on the page's last line of content, except a page of
  answers waiting only for one to be chosen, which the answers themselves explain.
- **Hold to talk** is always the secondary prompt, and the only prompt with the microphone, wherever
  the person can speak or give words.
- **Paging**: a list pages by Next page alone, at the far right where nothing is the main action,
  else beside it. On the last page it reads First page. It waits while a row is chosen.
- **Confirming**: Cancel takes the pressed prompt's place, Close stays, and Yes appears in the free
  middle once every part of the request has shown. A request in parts pages by a row at the end of
  the page, "Next part, 2 of 3".
- **Width, not count.** A footer holds what its words fit in its column: each prompt measures its
  cap, a grid step, its words (a main action's 5 percent wider) and 1.2 degrees of margin, 12 mm
  apart. The component render logs each footer's measure. A frame grows whole with larger text, so a
  footer that fits at one size fits at both. One that does not fit is wrong, never squeezed.

## The light line

A file opened out of its character keeps a light line from the character to it: one leg from under
the character's label, below any mark, to the file's subject. Where the label stands over the
subject, as the eyes see them, it drops straight down from the middle of their overlap; else it
joins the label's nearer bottom corner to the subject's nearer top corner. It crosses no label and no
character, its own included. Over a desk, where the plane stands above the lineup, it rises from
the top of the character's body to the file's bottom edge. The renders draw it in the Holo colour,
about 3 mm wide at the label thinning to 1.2 mm, fading from 55 to 12 percent.

## Icons

Material Symbols Rounded, generic only ([material-symbols.md](../validation/material-symbols.md)):
the state icons, and for a file code, database, data_object, description for writing and any
unknown kind, image, terminal for a script, deployed_code for a package, and folder; chevron_right
marks what opens more. `FileKind` picks a file's from its name. Never a language's or a brand's logo.
An icon stands before words, never in their place.

## The checks

| Rule | Checked by |
| --- | --- |
| One plane facing the eyes, columns aligned | `GlazeChecks.OnePlane` |
| Text as the eyes see it | `GlazeChecks.TextAsSeen` |
| Type only steps down | `GlazeChecks.TypeStepsDown` |
| One selection treatment | `GlazeChecks.OneSelectionTreatment` |
| Inside a Quest 3S's field | `FieldChecks.Inside`, head turned to the composition and tipped by `ReadingPitch` |
| Contrast on the glass over white | `GlazeChecks.Over` and `Glaze.Menu.GlassOverWhite` |
| The glass lights from its top | The component render (`GlazeRender`) |
| A footer's width | The component render's footer measure |
| The light line, and no row in a page's glow | `DirectionsRender` |
