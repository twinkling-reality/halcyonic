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

## Design acceptance status — 2026-10-15

The installed interface and the subsequent floating-panel composition were rejected in product
review. The sections below describe preserved implementation, not an accepted visual direction.
Improved spacing, controls, materials or an attached character do not resolve that rejection.

The required replacement is a coherent spatial composition with distinct roles for the current
work, task selection, contextual evidence, deliberate input and compact presence. It must not
confine every flow to one universal panel or scatter identical panels across the field. Regions
need not all be visible: their presence follows the activity, and substantial reading must remain
comfortable. Exact placement, depth, movement and region ownership are unresolved; no replacement
architecture is adopted here. See [open questions](OPEN_QUESTIONS.md).

Evaluate complete compositions and transitions with realistic working, permission, failed,
disconnected and long-content states. Preserve task identity, drafts, source attribution and exact
rendered-action authority. Geometry checks and editor images cannot establish design acceptance,
headset readability or comfort. The existing plane rules below must not prevent replacing the
rejected composition once a better whole interaction has been demonstrated.

## Independent spatial composition candidate — 2026-10-16

The main spatial Projects, Tasks, task, workspace-request and results views now draw a separate
project-context object beside the selected work. It contains up to three actual project choices,
the selected project's Tasks route, a larger project picker when needed, Workspace controls and
connection state. The selected task determines its project context. Choosing another project
opens its task picker; it does not relabel a retained task or its draft.

The selected work owns the central reading object. Ordinary instruction input reserves at least
three draft rows. When the measured field fits those rows and two recent Activity entries, a
passive Activity view remains above the composer. Otherwise the same editor owns the reading
position with an explicit Editing instruction label; Hide composer returns to Activity. The selected
task name, state and provenance appear once, above the Activity view or the focused editor. The
composer has one instruction label and no duplicate task or hero heading. Its passive context shows the newest bounded entries, in chronological order,
under Recent activity. Full Activity history retains its own reading position. Hide composer removes
that lower object while retaining the unsent draft. Results, workspace requests, questions and
approvals own the reading position when selected. They do not acquire input or decision authority
from the passive Activity view. Only the settled authoritative view can acknowledge reading or
accept its current actions. Results navigation belongs to the full task view; the independent
ordinary composer does not repeat that route. Its footer measures the offered controls at their
actual text size. When one row cannot fit, local Hide/Discard actions occupy the upper row and
voice/Send the lower row, with Send at the right. This preserves the target and text floors rather
than compressing labels to fit.

Context is 18 degrees wide and the reading object uses the existing 36-degree file width. Each
visible object owns an opaque reading backing. The task portrait is the actual task character at
the reading object's leading edge. Placement and recovery account for the combined conservative
bounds, including the full reserved animated portrait volume and its depth; no empty right-hand region is
allocated. The independent work title is 24 dp, measured at its actual row count. The external
portrait does not impose a minimum text-header height. The reading top
is stable within that placement, with height following measured content. Normal and larger text
use measured budgets, full target bounds and the same minimum readable text threshold. This is
still the existing placement distance, not an accepted new comfort distance.

Named identities use the bundled normal-width font with the same metrics for measurement and
drawing. Outside-text provenance, literal rendering and the current agent-prose treatment remain
intact. Local workspace overlays and retained legacy fixtures still use the preceding composition
below. This replacement remains a candidate: source, editor checks and synthetic images do not
establish visual acceptance, real-work usefulness or headset comfort.

## Previous single-face composition — 2026-10-15

The owner's headset review rejected the stacked menu composition. The candidate gives an open
task one reading column; its details replace that column and return to its retained draft. Only
Projects remains as a direct return while a task is open. One stable Workspace control opens
local workspace controls and the global destination index. The chooser does not repeat a four-tab
application row or allocate an empty navigation column. Move, Reset position, Minimize workspace,
Open work window, task portrait visibility and Projects/Tasks/Usage/Settings are reachable through
that local page. It packs complete targets by the same actual height budget and pages every route;
an off-page or stale action has no authority. Open work window remains unavailable when unsupported.
There is no permanent utility slab or target strip below the task. Only actual provenance marks
reserve a small final line; zero marks reserve no extra bottom region. Context, task and evidence
share one solid reading face.
The selected work's literal name uses the larger title role. Paragraphs and controls still face
the eyes together. There is no decorative folder shoulder or surrounding window frame.

`WorkspaceDirector` enables `MenuDirector.Setup.SpatialWorkspace` for the main application.
The setup option defaults off for explicitly retained legacy component/geometry fixtures; the
older layout below describes that legacy system. The actual spatial fixtures use Projects,
task instruction, pending approval, question, Results and Appearance frames and actions,
with explicit synthetic data. It adds no work capability or automatic character arrangement.
Known task titles and project-detail names carry explicit `MenuIdentityKind` metadata. In this
candidate only, the header has an upright literal name whose outside-text ranges remain intact.
It omits the redundant Task/Project eyebrow; project context and the state badge carry that context. Generic `SubjectIsData` does not opt in;
agent prose eligible for the scoped independent treatment below keeps its explicit reported-source
chip and literal outside ranges while becoming upright. Questions, tool text, approvals and
unattributed data retain their existing treatment. The state badge retains its measured header allocation.
Readability, full decision text, action authority, local drafts, target size and measured field
remain requirements. A quieter frame and a different font alone do not establish acceptance.

When the exact task's expanded main file shows its name, its character keeps the state badge and
provenance marks without repeating the name on a second plaque. The title returns on Projects,
minimize, focus loss, details without that identity, or another task. The full label's conservative
placement envelope stays reserved, so this presentation change does not move the shelf or reduce
clearance. With the duplicated title plaque absent, the task connector leaves the character's
body top and joins its named heading; both the drag predictor and rendered line use that same
rule. It must cross no body or label. Zero-character use remains complete.

## Coated reading material

The independent candidate reuses this material separately on each reading object. The continuous
face and context-header placement described here apply to the preceding single-face consumer.

The spatial workspace gives project context, named identity, sections and page one continuous
opaque coated face. A neutral virtual studio source reflects smoothly across that face from the
actual eye direction, with a maximum0.025 linear-luminance lift. The inward edge is0.006 distance
units deep (about2.8mm at the normal0.46m distance), with a0.001-unit bevel contained inside the
allocated footprint. There is no surrounding transparent carrier, inner opaque poster, repeated
material rim or periodic face texture. This analytic optical response does not capture room
reflection, refract or blur the scene, or transmit passthrough through the reading face.

The Workspace control stays in the context header; its local page replaces the evidence body on purpose.
A narrow physical edge connects the composition and is repeated on the minimized object.
No detached word-fitted utility rectangle is drawn. The complete
geometry/input allocation remains separate from the visual face, preserving target dimensions,
placement bounds and character/connector ownership. The legacy rule below that each part owns its
own rounded shape does not apply to this spatial composition. Activity records and its truthful
"No recorded activity yet." state lead utility routes such as Ask workspace and Read results;
a utility route is not recorded activity, and reported agent text keeps its provenance.

Header state icons use the same nominal em as their readable state word, centred in the existing
wider icon slot. Their rotating glyph rectangle stays inside the pill's fill, clear of its bright
border; the badge's outer size and stage icons are unchanged. Host-owned, nonpending plain reading
facts use primary ink on the spatial coated face. Outside text, provenance and semantic state tones
retain their treatments; unavailable words keep their declared unavailable treatment.

Small navigation/context, reached sections, utility labels and chevrons use primary ink; size
and position retain their hierarchy. Unreached sections remain unavailable in the quiet secondary
role. Selection on the coated face uses a 6-percent white lift with the existing selected frame.
Spatial task-footer actions have a full opaque rest boundary matching their target. One whole
control carries the main action's accent container/foreground; secondary controls use Control and
Outline. Icons stand inside that boundary without a second cap. Pressing depresses the complete
boundary; hover adds its focus edge. Legacy component fixtures keep their cap treatment. The
spatial footer has no decorative divider. Position and availability still follow the existing
Close/rare/secondary/main command contract; rendering never changes approval or send authority.

Material rendering does not redefine historical preset values or saved appearance keys. Palette
selection and default adoption follow the Colour section independently of the coating. Every
visible word and icon must retain actual whole-bounds backing and resolved-ink contrast of4.5:1,
including unavailable labels when present. The comparison at warm commit `9b547a02` passed those
technical checks across three palettes, both reading sizes and bright/dark backgrounds, together
with geometry, moving-eye and interior material/flat-negative checks. That editor evidence is not
wearer acceptance, calibrated passthrough readability or proof of comfort. Exact evidence and
remaining limits are in [material validation](../validation/instrument-material-study.md).

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
- **Organizing it.** The primary subject has named Move and Compact targets at its right, apart
  from the title and the decision footer. Holding Move drags the whole plane round the eyes. A short
  press opens placement details with Reset position and the actual character arrangements; selecting
  one only selects, and Apply changes it. Beside a window, the detail explains why the plane stays
  below it and offers those choices. Compact retains the current composition and draft; Close keeps
  its existing leave/discard meaning.
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
  is "Projects", matching its navigation place. The primary subject reserves the production
  measurer's exact Move/Compact widths, 12 mm between them and 12 mm clear of the title. Its plate is
  at least the 60 dp control height. TitleRows, SubjectHeight and page-height packing use that same
  reservation; a full retained task title is reachable through Move's Read title detail.
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
- **The compact workspace**: one rounded shape on the plane's top line, the retained task or place
  identity at 18 dp, with its truthful task state and waiting count when known. Move and Expand at
  its right are separate bounded targets. The surface itself never expands on a press. Expand and
  the retained task's character resume the exact file even when another task waits.

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
  the text, so nothing overlaps afterwards. The final small-label check uses its laid text bounds
  and finite em, including a left-column fact extending away from the center; the row has already
  reserved room for the 18 dp alternative.
- **Heavier** are only the chosen section and the main action.
- **Legacy/default outside-text treatment** (2026-10-08; superseded for the independent prose cases below). Outside text Halcyonic did not write,
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
- **Named spatial identity:** a project or task heading may stand upright using explicit identity metadata.
  The independent candidate omits the redundant `Project`/`Task` eyebrow; project context and the
  actual state badge establish its role. Retained legacy consumers may keep their fixed caption. This narrow presentation uses explicit identity metadata,
  retains outside-text ranges and literal escaping, and never applies to agent prose, approval
  questions, tool output or action labels. Reusing the label resets identity mode; its explicit prose
  treatment below, where eligible, is separate from identity.
  Host-owned context establishes what the name identifies; the name cannot supply or alter it.
- **Independent workspace prose candidate:** the ordinary instruction input and agent prose already
  carrying an `Agent says` or `Subagent says` chip use upright Liberation Sans body text. They retain
  literal escaping, rich-text parsing disabled, exact outside-text ranges and their source chip;
  upright never means observed or host-authored. Their decorative token/block grounds are omitted
  because the input label or reported-source chip supplies the context on an opaque reading face.
  Tool output, decisions, names and unattributed data retain their existing treatment. This is an
  explicit rendering mode, not named identity; every pooled ordinary/identity Set resets it, and each
  page line reapplies eligibility. Legacy consumers are unchanged. Focused-editor mode is a separate
  host-owned label beneath actual provenance, never text concatenated into a provenance mark.
  This source change remains subject to actual native render review, not aesthetic acceptance.
  Candidate engine checks group body/project hierarchy by semantic reading region. Footer actions
  are a separate measured 19dp role, not a descending continuation of the body; shared angular text
  floors and actual target bounds still apply. The real selected contextual project alone uses the
  existing coated selection lift (6 percent white) and selected frame; arbitrary controls retain the
  ordinary selection checks. Each exception has an actual-label/surface negative witness. Focused
  editor fallback must retain its measured allocation and one explicit mode label; actual task
  provenance remains exactly once in its separate label. These supersede the old universal
  descending-type and universal selection-fill assumptions for these specific candidate regions.
- **Last on a page**: an unavailable prompt's reason, then the source line, one a page, at 18 dp in
  the secondary colour. A detail under a part, as a Seorak part's coverage or freshness, is its own
  18 dp line in the secondary colour.
- **The companion's words** are one block of theirs of at most 2 rows, the page's source line naming
  the companion. Its view, when it has one, goes first in our words and the secondary colour, and the
  block is then its question alone.

## Colour

The interface's colour roles are immutable `GlazePalette` presets, read by `Glaze` separately from
type, sizes and motion. Neutral is the candidate default, using low-chroma surfaces and neutral
actions to reserve colour for work states and characters. Glaze preserves the original colours;
Graphite retains its graphite surfaces and blue action accent. Saved `glaze` and `graphite` choices
retain their exact meanings; there is no preference migration. State colours retain their meanings
in all three presets. Actual three-palette Appearance and character review is still required
before aesthetic acceptance. The Neutral candidate uses achromatic reading levels: well `#1D1D1D`,
panel `#242424`, raised `#2E2E2E`, control `#3A3A3A`, hover `#494949`, pressed `#595959`;
text `#F2F2F2`, secondary `#C4C4C4`, disabled `#A0A0A0`, outline `#8A8A8A`.
These are a design decision to remove the prior green/cream cast, not colours copied exactly from
a reference or an accepted visual result. Existing semantic status and blue accent colours remain. Settings' Appearance
chooses locally, then shows a labelled Preview with actual navigation, choice, input, status and
outside-word treatments. Preview controls send nothing to an agent. Apply theme keeps the preset;
Cancel or leaving restores the applied colours. Restore default appearance opens its own explanation
and names and restores only the current default (Neutral), without changing comfort, space,
projects or the paired computer. Missing or invalid appearance values also select the current
default; loading and previewing never save it.

Settings opens a category index for Appearance, Comfort, Your space and Your computer only where
that build supports it. Categories and setting rows navigate; their current value stays beside the
name, including provenance where a value comes from outside. A category returns by Back; a pending
appearance preview returns by Cancel. The specimen pages by a bounded body control, keeping Close,
Cancel and Apply theme as the three footer controls. Every variant retains the same grid, readable
type, hit targets and action hierarchy.

This first palette consumer covers the menu's token-based surfaces, prompts and outside-word
grounds. Character identity and body shader/material colours remain separate; this is not an
arbitrary colour picker or a claim that every character colour is configurable. Preset composition,
stage cache refresh and headset appearance require the validation record's checks.

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
  no side action. Focused answer controls are explicit bounded exceptions, including the explicit unavailable-choice Read why route. Choosing one may set the footer's main action or the prompt beside it, as a
  project's Add a task and Hide from stage. A line that reports a problem is a row that opens its
  side panel, and what fixes it is the footer's main action while it is chosen.
- **An offered answer** has a hairline shape at rest and the existing lit fill/frame when selected.
  Its provenance quote/lean stays at left; a persistent radio mark for one choice or checkbox mark for
  multiple choices stands in a separately reserved area at right. Hover adds only the frame and never
  changes that mark. The selected centre/check uses existing surface and check-glyph primitives.
- **Focused answer controls** are visibly bounded at rest: More choices, Write an answer, selected
  Edit answer, review Edit, unavailable-choice Read why and clipped Read question. They keep the 48 dp targets and 12 mm spacing.
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
  "Author unknown" or "Model explains", beside the claim's words. Legacy/default claims lean;
  eligible independent `Agent says`/`Subagent says` prose uses the scoped upright treatment above.
  Its evidence class remains reported regardless of presentation. Observed and measured
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

Unavailable choices keep their original list slot as bounded Read why navigation, with no radio or
checkbox mark. The dedicated explanation preserves provenance for every full conflicting word and
pages within the same field. Back returns to the same question/page and saved draft. Close and the
current Stop task occupy the explanation's footer; Stop still requires policy confirmation. No
unconditional warning/Stop target adds density to ordinary choices.

Tell it's instruction editor uses the focused composition and the existing bounded Input role,
Your instruction label, empty focused keyboard caret and bounded navigation. Middle read parts
keep Previous part and Next part, paired only at measured half widths; all field/pager targets and
12 mm gaps remain in the height budget. The editor has at most three footer prompts: local Cancel,
Hold to talk when available, and Send instruction. Done/transcription only change its local draft;
Send is explicit and remains quiet/unavailable until the complete current draft has been read.

Preset instructions are a focused choice page. Their actual wrapped rows, source, reason and body
pager fit the same measured field budget; browsing keeps the selected candidate. A full notice that
cannot share the page reads separately under What happened, with Back to choices.

Usage pages count the heading at its actual remaining width beside Move and Compact, reserve the
source line, and show only the rows that fit the measured stage page. The pager preserves source
order and every limit; a chosen limit keeps its details when text size changes. Its details page
measures each fact's label, value and gaps; all Agent app, Seen, Resets and Account facts remain
reachable through existing side-panel parts, with the exact source repeated on each. List paging
waits while a limit is chosen; Next page then turns only its details.

## Workspace presentation

The workspace opens expanded without characters. Optional spatial agents share one local
presentation state with the menu. Minimize workspace hides the complete workspace and optional
agents into one dock; it retains work identity and valid local drafts, rather than meaning close
or send. Input, voice and armed decision authority are suspended before the visibility change.
Restoring requires current layout and fresh drawn authority. Showing agents remains a separate
explicit display choice and does not create or change work.


The dedicated workspace is a composed rectangular frame, with connected task navigation and
content panes. Its attached header identifies Halcyonic, the current connection mode and selected
project/task even with no spatial agents. Global Move, Other apps and Minimize controls live on
that frame, separate from request decisions and local Close task actions. Minimize leaves one
Open workspace target and suspends the complete workspace. Other apps is unavailable where the
verified native panel bridge cannot run; it is never an arrangement preset.

The shared header consumes measured composition height before pages pack their content. The
character-free workspace centers in the available field rather than reserving an empty character
label band above it. Header, perimeter and connected panes participate in field/drag geometry at
both text sizes. Source, outside-text and decision-proof treatments remain on actual content.
Move offers direct hand movement and Reset position, without guessed window arrangements.
Actual Unity composition evidence is recorded in the current validation checkpoint below; wearer
acceptance remains pending.


A neutral structural divider may separate the attached workspace header from its connected panes.
It is horizontal, thinner than 0.2 degrees, uses only the neutral edge color, carries no action and
marks no selection. Chosen and pointed-at actions retain the existing fill/frame treatment; an
arbitrary bar or underline still fails that rule. The workspace perimeter is an unfilled outline,
so content's opening/closing remains visibly distinct from its actual background. Geometry
witnesses include the full shared header when measuring the composition center, field corners
and coplanarity; measuring only body panes would exclude real visible workspace content.


`DedicatedWorkspaceCheck` exercises the production menu without characters at Standard and Larger:
fresh/connected Projects, visible Tasks selection, a marked synthetic task in a live-origin journal,
local instruction, global Minimize/restore and disconnected draft. Its structural negative controls
remove the divider role and rotate the header against its body, preserving the arbitrary-bar and
coplanarity rejection paths. It is a simulated renderer fixture, not proof of a real runtime send,
native JNI return or wearer acceptance. The current validation checkpoint below records its
actual Unity outcome.


The console owns its world position independently of optional agents. Selecting a task or changing
agent placement never re-centers a hand-placed workspace; Reset position is explicit. The console
starts centered, retains its anchor and hand offset, and packs against current occupied agent
geometry and measured field bounds. Manual shelf height is not a desk floor for the console.
Shelf previews use the actual visible workspace perimeter or restore dock as a collision veto,
with at least one degree of spacing; they do not displace the workspace. Legacy floating arcs and
guessed window bands are not the presentation model for this workspace.


The header repeats only project context; the selected task's full title stays on its content pane.
Current state and Practice/Demo/Recorded provenance have independently measured reserved cells,
so outside project text cannot consume or ellipsize them. Placement failures use a measured local
explanation with Back to workspace; these details own no runtime or draft authority.


Passive redraws preserve the exact retained hand offset, including transitions to and from the global
restore dock. Only explicit movement or Reset position changes it. Finishing a hand drag, resetting
position, or returning from a local placement overlay repacks retained files at the current position;
ordinary instruction text survives, while old keyboard callbacks and ordered reading/confirmation
proof do not. Shelf placement reserves the workspace beneath its temporary overlay as well as the
visible overlay, so Apply cannot introduce an overlap that appears only on return. A real furniture
surface still bounds a hand drag; a manual shelf does not supply a fictitious floor.

A selected agent's light line first uses the nearest label-bottom/subject-top join. If that straight
path crosses measured current labels or bodies, the same crossing veto checks alternative endpoints
along those two edges. Neither line routing nor content redraw moves the console. A failure to find
a clear straight path remains visible to geometry checks instead of being hidden as successful proof.


The attached context row includes a full-size Projects target even when a focused instruction
steps the body navigation aside. Filtered Tasks shows the chosen project name without requiring a
selected task. State and source marks retain independently reserved header space; when that header
owns the selected state, the body omits its duplicate pill. Headerless render fixtures retain their
own state pill. Filtered task pages reserve the All projects content row, and keep Project details,
Saved drafts and the offered pager reachable together. The complete header remains in measured
composition and field checks; these changes do not reduce the minimum target or text size.

`DedicatedWorkspaceCheck` now follows the production project row into filtered Tasks, opens the
existing project details, and uses the visible Projects header target while a local instruction is
retained. It reopens that same task view and checks the draft survived and the body state pill was
not duplicated. The navigation-focused core checks pass 54/54 with no skips, and Editor plus
Android player-symbol source compilation passes (2026-10-12). At that source checkpoint the new header/navigation render remained pending; the subsequent
DedicatedWorkspaceCheck and current validation checkpoint below cover the revised composition.

The Larger project-detail fixture closes the actually visible detail panel before selecting its
project again: details replace the project list at that size. The first navigation render passed
Standard but correctly refused the hidden Larger project row. This fixture correction preserves
that visibility guard; the subsequent repeated exact render is recorded below.

The corrected DedicatedWorkspaceCheck ran on integration fc32 at both Standard and Larger with
30 current PNGs (2026-10-12). The project-to-task path, visible detail return, Projects breadcrumb,
retained instruction and header state/provenance passed the existing field, target, text and
coplanarity checks. The Larger project-task and reopened-instruction images were reviewed. This
is simulated source/render evidence, not acceptance by a wearer or a real command delivery.

Workspace request uses the attached console and measured focused-file room. Its local editor
retains the existing paged input/read guard; scope text, held voice, explicit Start/Continue and
spoken Yes remain visible controls. Model reply and recorded receipts have separate sections and
full measured paging, with source names treated as outside text. Request and recovery-focused
checks passed 46/46 with zero skips at the first source checkpoint, with Editor/Android source
compilation. The later RequestCheck result below supersedes its unrun screenshot status; full
integrated gates were pending at that source checkpoint. The current validation checkpoint below
records the later combined outcome.

RequestCheck is the request-specific real-font composition gate. It uses the actual production
column and director control routes with synthetic capability/result seams, and retains all field,
readability, target and coplanarity checks. Its source compiles with Editor and Android symbols.
The existing Judge walkthrough now
checks Tasks as the stable view subject and the actual waiting state on its row.

RequestCheck ran on integration c2c64 with 20 captures at Standard and Larger; the complete request
composition and original checks passed, and Larger spoken review and Actions were inspected.
That review identified missing task identity in the request body. The follow-up uses the actual
bound task title as the outside-text subject and measures its title height at both sizes; Request,
Reply and Actions remain the section labels. The redundant Actions body heading is removed.
The focused title/lifetime regressions pass 54/54 with zero skips; core, Editor and Android source
compilation pass. RequestCheck now also checks the actual outside-text task subject on each
visible request frame. The current validation checkpoint below includes the title-adjusted
request composition. No live send or wearer acceptance is implied.

The legacy director render route now starts from visible Projects and selects its offered Tasks
section before opening the waiting task. Global Minimize and Restore use the actual console and
dock pointer controls; Move is exercised after Restore on the visible console utility. Question
editor retention, review, stale callbacks, two-hand ownership and drag bounds remain checks. The
director's budget is the independently centered console with its measured header and occupied
geometry. Passive stage flags and Larger repacking must retain the exact hand offset while the
whole composition still fits and old input is revoked; automatic offset clamping is no longer an
expected outcome. Task identity is checked in the actual file and project context rather than a
removed character banner. At that source checkpoint Editor compilation passed while the migrated
render and legacy raw plane/fade investigation remained pending. The current validation checkpoint
below records the later combined outcome; clearance, field, readability and visibility thresholds
remain unchanged.
DirectorCheck runs all three existing director cases at both text sizes with the same PlaneState
guards and nonzero retained-offset counter. It diagnoses this migration separately from untouched
raw plane/stage cases; the full WorkspaceRender.Check retains them and remains the acceptance gate.
The retained FirstQuestion column's regression expects the current concrete Projects subject for
both choices while preserving its choice/action/voice checks. This legacy column test is not
evidence that fresh production entry uses the superseded first-question policy.


## Current validation checkpoint — 2026-10-15

The October13 production checkpoint `61646671` passed the complete Workspace and Appearance
Editor render gates, including current Projects/task navigation, retained instruction and request
views, character-free and confirmed-shelf compositions, and both Standard and Larger text. Both
Glaze and Graphite presets passed Appearance. Results additionally passed far/desk placement and
zero/one shelf with exact displayed pages and both drafts retained. These later runs supersede
pending-render statements in the source-checkpoint history above; they retain the original field,
text, target, visibility and coplanarity constraints. Detailed scope and limits are recorded in
[theme settings](../validation/theme-settings.md),
[workspace requests](../validation/workspace-requests.md) and
[recorded tool results](../validation/recorded-tool-results.md).

These are synthetic Editor composition and continuity checks. They do not establish wearer
readability, hand or microphone usability, live model reliability, native JNI/other-app return or
source/build correspondence for an opened external app. Those remain separate acceptance gates.


On Android the initial workspace waits for a present wearer, tracked head position and rotation,
and input focus before taking its first anchor. An unworn or untracked camera pose is not a valid
placement. Later ordinary head/focus changes preserve the established composition and Move offset;
this is not head-following UI. The source and remaining device checks are recorded in
[initial workspace placement](../validation/initial-workspace-placement.md).


### Native restoration wait

The native panel's actual pending-restore connection check shimmers through its words, using the
same 1.6-second sweep, 0.35 relative band and 0.55 lift toward white as Glaze. It changes no text,
position or progress value. Ordinary status/error text stops the sweep and restores its normal
colour; cancellation, focus/top loss, pause and detach stop it too. A finished or failed check
cannot keep presenting an animated wait. This treatment marks a pending connection read, never
agent execution or successful restoration.

Keep things still, disabled system animations and unknown motion preferences show a steady active
highlight instead. The Unity comfort setting mirrors only its motion choice into one tiny private
native record; no credentials or continuation authority cross through that record. Native motion
also requires a successful preference mirror in the current process, so a direct native launch
with unknown settings remains still. No native loading shimmer hardware acceptance is claimed by
source or JVM tests; evidence is in [native agent panel](../validation/native-agent-panel.md).

## October 15 composition reset: title and reading material

The owner rejected the actual installed stacked workspace. Earlier geometry and contrast checks
remain narrow technical evidence, not aesthetic acceptance. The replacement composition uses a
single selected-work title and subordinate navigation. Rajdhani Medium provides restrained
industrial headings; Liberation Sans remains the body face and fallback for unsupported heading
glyphs. A static, bundled SDF atlas consumes the OFL source; no runtime font download is required.
The principal work title is 34 dp, used once, while all existing reading/target minima remain.

The spatial reading surface follows [Coated reading material](#coated-reading-material): one
opaque face with bounded reflected light, rather than independently glossed parts. Focus and
selection retain their meanings. This material and typography do not establish a successful
diegetic experience; full-context evidence and fresh wearer review remain required.

### Task-focused composition candidate

The selected task occupies one reading column. Its Projects control returns to the workspace
chooser; Workspace carries Projects, Tasks, Usage and Settings. Those destinations do not
remain a second rail beside an open task. Project context and Workspace stay above the work;
practice/recorded provenance has its own small line only when present.
The named heading reserves its badge inset and Hero name; pagination and actual layout use the same inset, including larger-text SDF bounds.
Context, named heading, sections and reading body share the opaque coated face, without a folder
tab, clear rear carrier or independently backed internal plates. Workspace remains in the header; no organization strip is allocated below the face.
Bounded footer actions use the same corner radius as the header controls. Secondary actions use
the neutral reading-face colour and the shared quiet hairline; primary actions retain the blue
accent container and edge. Shape does not distinguish these roles.
This composition remains a candidate until actual full-context and wearer validation pass.

The focused work object's context row reserves one full Projects target in height. Project and
connection words align with that target; the composition's ordinary part gap follows it before
task identity. The chooser shares that single-target-height header. This removes unused focused
context space without reducing a hit target or type size. The same allocation drives the actual
composition and available page-height calculation; actual paired captures remain the design gate.

The type hierarchy resets for each independent known-project entry: its name leads its own
supporting task summary. A later project's name may therefore be larger than the preceding
project's summary. This is not a blanket exception to descending type: ordinary reading content
retains that rule, and each project's actual name-to-summary group must satisfy it independently.

### Material evidence

[Coated reading material](#coated-reading-material) defines the current implementation. Superseded
card, coating and carrier comparisons are retained only in the [validation history](../validation/instrument-material-study.md),
not as alternative authoring rules.

## Out-of-view workspace recovery, 2026-10-15 candidate

The owner rejected the fixed workspace staying behind when they turned. Reading content now stays
still through ordinary small head movement, then recovers to the resting wearer after it leaves
view. It never changes XR tracking or the camera, never moves room-placed characters, and does not
control native Android panel placement. A moving or settling reading object accepts no task or
frame input; its exact local drafts/pages survive and require a fresh physical draw. Keep things
still uses an immediate content reposition with the same input settling. Candidate timings and
translation tolerances require an actual wearer check; renderer success alone is not comfort.

The October 15 work-object candidate removes the chooser side rail and its unused horizontal
space. One complete Workspace target occupies the chooser header; task focus adds
the direct Projects return. The matching page-height calculation includes this header and only the actual provenance line. Its spatial proof checks the actual row geometry, single column,
full title provenance/bounds and whole-action rest backing, including an icon-only negative.
This candidate has not yet passed actual Unity rendering or wearer review; earlier coating
evidence does not establish these new compositions. No character is enabled or moved by this
change, and the existing character-placement and connector guarantees remain unchanged.

Selected tasks now also have a local body-only character portrait at their title's leading edge.
It uses the actual Workstream presentation and identity hue; the title measure reserves its
5.5-degree-wide slot plus the normal target gap, and the subject reserves its full6.5-degree-high
animated envelope. Its own labels, halo and ring are absent because the adjacent title/state
already identify the task. The portrait has no runtime command target. It is a local illustration
of the selected task, independent of whether a character has deliberately been placed in the room.
Opening work never enables or moves a room character.

Workspace's local controls offer Show task portraits or Hide task portraits. This device preference
applies to all tasks and survives app restart. A hidden portrait restores the full title width.
Per-task colour still binds to the current connection, journal and Workstream. Its existing
preview/apply/cancel route may now use a selected source-bound portrait without requiring a room
character; preview never grants runtime or approval authority. Source loss still revokes editing.
The spatial proof checks real Workstream binding, body-only presentation, title separation and
absence of command targets. These new portrait compositions remain unvalidated until actual
rendering and wearer review; the separate body component's implementation is not that evidence.
The compact object's leading edge uses the same selected task portrait and hue, with the complete
animated body fit inside its shorter allocation. Its adjacent literal task identity and state
remain readable without relying on the character alone. Open workspace retains its complete target; movement is available through Workspace after
opening. The portrait itself does not expand or command the task.

## Workspace controls candidate — second pass

Workspace is a deliberate local page, preserving the task and draft behind it. Move workspace
enters an explicit placement mode: the stable header target reads Move and enables its existing
real hold/drag path. The local page explains that hold and offers Reset position; Done moving
returns to the retained task. A normal Workspace hold never starts an unexpected move. Compact's
explicit named Move target keeps its existing behavior. Closing, focus loss or leaving the local
page revokes its drawn actions; movement and approvals never share input authority.

The unselected Projects root omits redundant Close because it only collapsed that root. Minimize
is available through Workspace. Selected project details retain their own close route. A task's
Close still closes the actual task context; it is not relabeled Back to tasks, because closing a
task opened from a character need not reveal an open task list.

Neutral now uses the established blue Accent family for the whole main-action control. That
accent remains distinct from amber attention and preserves state colours and saved preset keys.
Its foreground/container contrast is checked in core tests; actual resolved button contrast is
still part of the render gate. The local character is larger and reaches the face's leading edge
within its measured allocation. Neither that adjustment nor fewer controls establishes wearer
acceptance. Actual complete compositions and movement/draft/approval regressions remain required.

Local character bodies do not inherit the text/surface opacity shader. They therefore appear
only when the expanded subject or compact object is complete and disappear immediately when it
starts leaving. This is an intentional discrete identity transition, not a claimed body fade.
Workspace's Other apps action retains platform availability and requires the exact current drawn
local controls page. It is unavailable in explicit moving mode; successful switching uses the
existing draft capture and workspace suspension path.

The compact object gives identity and Open workspace one target-height band. State and the
workspace waiting count use a separate lower reading band with its full measured width, so a long
state cannot collide with the Open workspace target. Only if the exact state/count widths exceed
that band does the count use another line and the object grow. Status words, count and action
labels keep their size; a long literal task name may be cut within its controlled outside-text
range, with the complete task available through Open workspace.
