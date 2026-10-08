# ADR 0026: The headset's interface is a game menu of places, sections and prompts, on one plane facing the eyes

- Status: Accepted on 2026-10-02 by the owner, with the split header; notes added from the owner's picks
  on 2026-10-08.
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
  first. A row is a state icon, the task's title, its project as the small fact, and a chevron.
  Choosing a row slides that task's file out beside the menu.
  Tasks only reads and navigates; actions stay in files. Rows that do not fit page by Next page.
- **A task's file replaces the workspace panel:** its subject, the task's title under its state pill
  (the split header, below); a row of 4
  sections, Waiting, Activity, Changes and Checks, an amber dot on the one that waits; a page of the
  chosen section alone, in a few short lines; and a footer of prompts. No sub-sections. A line with
  more opens a side panel that slides out to the right on the same plane (the changed files with their
  icons, the agent's reasons, the checks), holding no action but its own Close. The file opens on
  Waiting when something waits, else on Activity, out of its character, with a light line from under
  the character's label to the file's subject.
- **The split header:** a file's subject carries its character's state pill, the same badge as the
  character's label on the stage, on its top edge at its left, its word at the content's 18 dp, so
  the file reads as that label opened up and its state shows at a readable size. Every column's
  subject keeps the pill's room, so the plates and titles stay level. The section tabs stay a row of
  separate shapes.
- **New project** shows its steps as a row of shapes, as a file shows its sections: Your idea,
  Questions, Recap, Build. The companion's turn is the Questions page; the recap and the
  paged review before Yes, start building keep their rules.
- **A footer of prompts.** Each action is a round key cap holding its icon, then its words, with no
  plate. Hit areas are 60 dp tall, unseen, 12 mm apart. Close is always far left; the one main action
  always far right, its cap filled with the accent and its words in the accent; at most one secondary
  beside it, and one rare action, such as Stop, beside Close. Pointed at, a prompt shows the selection
  treatment's frame; pressed, its cap sinks and Touch plays. Confirmations keep ADR 0023's rule: Yes
  appears in a slot no control held on that page or since, Cancel takes the first press's place, and
  the whole request shows before Yes, its parts paged by a row on the page. Move and Reset position have
  no slot: Reset position lives in Settings' Your space, and holding a file's subject drags the whole
  plane round the eyes at touch distance, still facing them (lane U's hold-to-drag, `PanelDrag`), if
  that keeps the plane rule; otherwise holding does nothing.

The design system, as hard rules. The renders check those marked checked, in `DirectionsRender`
today and, once built, in lane U's renders of every surface:

1. **One plane, facing the eyes at its centre (checked).** The subject, sections, content and side
   panels of a composition are separate shapes on one flat plane, square to the line of sight at the
   composition's centre (within 0.5 degrees), 0.46 m away, never rolled; no part is angled against
   another. Parts stand a degree apart on the plane and columns 15 mm apart; every part of a column
   takes its width, and the columns start on one line and end on one line, a shorter column's last
   part reaching down to it. Detail slides out to the right, along the plane, and the whole plane
   re-centres as one piece, eased over a short slide: what was there shifts left by half the new
   part's width, so what was pressed travels least.
2. **Text as the eyes see it (checked).** Each label's em is measured from the eyes, slant included,
   and no text reads under Meta's 14 dp (`Glaze.MinimumTextDegrees`). A flat plane loses size away
   from its centre, to 84 percent at the far corners of a menu and file side by side, so the smallest
   size, 15 dp, is only for small facts inside a row, and prompts are 18 dp. An upright plane stays in
   the renders as the case this check must catch. The stage's labels are held to it too: on a desk,
   where the eyes look down on them, they lean back to face the eyes, as ADR 0023 now records;
   standing upright there, their badge words read at 64 percent (lane U's inventory, 2026-10-02).
3. **One selection treatment (checked).** Chosen, a shape lights up, white at 10 percent, and gains a
   crisp white frame; pointed at, it gains the frame alone, fainter. Places, sections, rows, answers
   and prompts all use it. Nothing is marked by an underline or a bar, and the accent marks only the
   main action.
4. **Type only steps down (checked).** Three sizes on the menu's surfaces: Title, 24 dp, drawn light,
   for the subject, one a column; Body, 18 dp, for the sections, the content and the prompts; Label,
   15 dp, for small facts inside a row (below, a side panel's fact names). From the top of a
   column to its bottom, size never grows again, so a page's source line, one a page, stands last on
   the page, at Body in the secondary colour, and a detail under a part, as each Seorak part's
   availability, coverage and freshness, is its own Body line in the secondary colour. Partial, stale
   or unavailable, it stays in the secondary colour and its words say so: amber means waiting for you
   and nothing else. Only the chosen section and the main action are drawn heavier. The one
   exception: a state pill on a subject's top edge, the split header's, reads with that subject.
5. **One grid.** An 8 dp step: 24 dp padding, 16 dp between groups, 8 dp from a label to its value.
   Words and icons start on one left content line, small facts and chevrons end on the right one, and
   icons stand in a fixed 24 dp column. A shape round words, as an answer or a well, reaches 0.7
   degrees past the content line, and its words stay on it. Every shape has one corner radius, 0.9
   degrees.
6. **Glass.** The panel colour at 96 percent opacity; a light from the top edge fading out by a third
   of the height, and on a page of content within its top padding, so no row stands in it; a sheen
   along the top edge; a hairline edge. No blur and no corner ticks. In the product the `Surface`
   shader draws it in one call.
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
  chipped, the agent's words marked as theirs (leaning on a ground only Halcyonic draws, since
  2026-10-08, below), and each Seorak part's own availability, coverage and freshness;
  `EntryScreens`' flows, `NewWorkDraft`, `NewWorkReview`, `NewWorkSubmission`, `ProjectIdea`,
  `FolderConnect` and `ConnectScreens`; and the companion's `CompanionExchange`, `CompanionText` and
  `CreationDraft` ([ADR 0025](0025-the-companion-is-a-local-model-whose-exchange-stays-on-the-headset.md)).
  New models for the menu, its sections, page lines, side panels and footer slots replace
  `PanelModel`'s tabs and bar.
- **Kept from ADR 0023:** tokens in the client core; `GlazeText` and `GlazeButton`, extended with the
  light title, a Prompt role and a Row role without a plate; the state language, badges, icons and
  atlas; character labels and the peek; touch distance, 0.46 m; 60 dp targets 12 mm apart; the Glaze
  sounds; and the render checks, extended by the rules above. **Superseded:**
  foreground panels of 44 by 26 degrees with Move, Reset position and Close in a header and every
  action in a bottom bar; the workspace's tabs and sub-tabs, its Close at the end of the tabs, and
  Refresh and pagers beside headings; the rail's 2 rows of buttons; `PanelFrame` as every surface's
  frame; and the entry panel's screens, which move into Projects and New project.

### Decided after acceptance

Decided on 2026-10-02 by lane V, the design owner, as the lanes began to build, with the
coordinator's first-visit words:

- **One kind of small fact a list.** Tasks' rows carry each task's project, in the secondary colour,
  or amber on a row that waits; the state stands on the icon and its tone, the row's place in the
  list, and the file. With one project only, the rows carry no fact. A time without its noun, as
  "4 min", says nothing, so times stay on the file's Activity.
- **Tasks holds every task**, as More work does, so none disappears: what waits for you first, then
  what works, then what ended, newest first. Older ended tasks fall to later pages.
- **A footer's places and a prompt's emphasis are separate.** Only the main action is drawn as one,
  its cap filled with the accent and its words heavier, and only at the far right. Any other prompt
  there is drawn plain.
- **A list pages by Next page alone**, at the far right where nothing else is the main action, as on
  the menu or a side panel, else beside the main action; on the last page it reads First page and
  starts again. Paging is never the main action, and there is no Previous page: beside Close and a
  main action, two paging prompts do not fit.
- **A confirmation pages by a row.** A request in parts shows one part a page, with a row at the end,
  "Next part, 2 of 3", which only takes you on. Approve's Yes appears once the last part has shown;
  Deny's at once, as denying runs nothing and a person who sees part 1 of something dangerous must be
  able to refuse it then (the coordinator, 2026-10-02). Its footer is Close, Yes in the middle, and
  Cancel where the pressed prompt stood.
- **Each action is one footer prompt in its place.** On an approval: Approve as the main action, Deny
  beside it. Under a question's answer rows: Send answer as the main action. On a working task's
  Activity: Stop beside Close, and Tell it as the main action, opening a page whose rows are
  instructions (the recorded ones, in the demonstration).
- **Rows only take you somewhere and carry no side action.** Choosing a row lights it and may set the
  footer's main action or the prompt beside it, as a project's Add a task and Hide from stage, or
  a recap fact's Change. Choosing an answer lights it, and Send answer sends it; nothing is sent
  by the press that chooses. A project row opens its own side panel, and nothing toggles from a row. A
  side panel holds nothing but its own Close details.
- **Hold to talk is the prompt beside the main action** wherever the person can speak: a file's
  Waiting page, and New project's Your idea and Questions, where Go on without it is the last answer
  row. It is never a cap on a row. Wherever the person gives words, the same holds: a fixed
  question's answers, the first task's change page, and a page of words for a name (a project's, a
  new folder's), whose footer is Close, Hold to talk and Done. A row holding their own words opens
  the keyboard; heard words land in it, chosen, with today's line under it, "This is what your
  computer heard. Check it before you go on."
- **The companion's words are one quote of at most 2 rows**, its line and its question; the line
  drops first and the question is never cut. When it thinks the idea cannot be built as software,
  that view goes first, in our words and the secondary colour, and the quote is the question alone.
  Under that view a quote of 3 rows takes the page to the very edge of a Quest 3S's field at today's
  text size, 43.5 of 43.5 degrees, with nothing to spare.
- **Fixed questions page forward only**, as lists do (Answer a few questions, and Add a task): the
  question with "Question 2 of 4" as its small fact, its answers as rows, the row for their own
  answer, and last, where a question can be skipped, a skip that says what skipping leaves, as Name
  it later under the name question. Next question, the main action, records the chosen answer, and
  until one is chosen it waits with its reason, "Choose or type an answer first."; on the last
  question it is Make the recap. There is no
  Previous question. The recap's first task is composed from the answers, and its Change walks the
  questions again from the first, each answer already chosen, so Next question keeps it and Make
  the recap composes the task again. A first task rewritten by hand is never composed over; its
  Change opens the page of words.
- **An unavailable prompt keeps its place**, drawn quiet, its cap outlined even for the main action
  and its words in the secondary colour; its reason is the page's last line of content, above the
  source line. On a page of answers, Send answer or Next question waiting only for an answer to be
  chosen needs no reason line: the question and its answers above it are the reason. Any other
  reason still shows, as reading the question to its end or answering each question first.
- **Stop stands on Activity**, beside Close. On Waiting it stands only where Halcyonic can't answer
  the question, a secret or one its adapter marked unanswerable, since stopping is then the way on
  (ADR 0022); a question it can answer keeps Waiting's footer to Close, Hold to talk and Send
  answer.
- **The companion's note that it is an AI is the source line** of any page showing its words. New
  project's row of steps is the way back; there is no Back prompt.
- **The first visit** opened the menu on Projects, and the demonstration had a first visit of its own
  that opened Projects too (the owner, 2026-10-07). Both were replaced on 2026-10-08, below.
- **Closed, the menu is one rounded shape** on the plane's top line: its subject at 18 dp, saying what
  waits or that nothing is waiting, and an Open prompt at its right. Pressing it opens Tasks when
  something waits, else the place last open.
- **The demonstration's two lines stay on the line above the stage**, which the menu does not touch
  (ADR 0012). The stage's banner that carries them hangs under the labels, where the open menu
  stands, and steps aside for it in a live session. In the demonstration it stands raised instead,
  whatever is open, the closed bar included, which stands where it would hang (the coordinator,
  2026-10-08, after the render found the lines overlapping the bar once the demonstration opened
  closed): it stands a little more than a degree over the highest a risen character
  reaches, as it stands over a surface, and over the open plane's top edge, which over a desk's
  lineup stands where it would; it says the demonstration's lines alone, since the menu says what
  waits (the coordinator, 2026-10-07). The peek still takes its place. Amended 2026-10-08: the stage's
  lines stand raised above the stage always, live too (below).
- **The stage keeps its own sizes**, its labels' 20 dp titles and 16 dp badges, sized for its
  distances and held to text as the eyes see it; the three sizes govern the menu's plane.
- **A subject's line holds about 36 characters** at 24 dp in the menu's 32 degree column; a longer
  one wraps to a second line, and its plate grows.
- **Small text keeps 15 dp only where it reads at 14 dp or more** as the eyes see it. Toward the outer
  edges of a wide composition, where the plane's slant would shrink it under, as a side panel's
  counts at the far right or a chip at the far left of a file beside its side panel, it takes the
  content's 18 dp. The layout decides it from the place on the plane, before the text is laid.
- **A footer holds what its words fit, not a count.** Lane U's footer measure takes each prompt as
  its 1.45 degree cap, a grid step, its 18 dp words (a main action's 5 percent wider) and 1.2
  degrees of margin, 12 mm apart, against its column's content width. Frames grow whole with larger
  text, so a footer that fits at one size fits at both, and a render whose footer does not fit
  fails. In a 36 degree file, with about 34.4 of room, Close, Stop, Deny and Approve take 30.3, while
  Close, Start over, Hold to talk and Make the recap take 38.3: Start over stands on the recap
  alone, as its last row, its action beside Close only while that row is chosen. In the
  menu's 30.3, Close, Hide from the stage and Add a task take 31.4, so that prompt shortens to Hide
  from stage and Show on stage where the footer model fits them, else Hide its tasks and Show its
  tasks.
- **A question whose answers don't fit pages them by a row** (measured at real heights by lane W on
  2026-10-02: with the reason line and the typed and paging rows apart, even a one-row question
  could not share a page with one 2-row answer at the standard size). A page packs by height, not
  rows: a line of words takes its line, a target its 48 dp, against the page's content height
  (`MenuFrame`), packed for the file alone, the menu stepping aside when the page needs it. The
  question, quoted, heads the page in at most 2 rows, and it and its answers are one group, 8 dp
  apart rather than 16; the 12 mm between targets, Meta's spacing, and the source line's 16 dp stay.
  When it and one row of answers don't fit together, or when its own page would leave fewer pages
  in all, its answers' pages then holding more answers each under its first line (four 2-row
  answers: a question page and 2 answer pages rather than 4), the question has its own page or pages
  first, in parts, the last ending in a row to the answers, whose pages repeat its first line, cut,
  where it fits; on a tie it stays with its answers, read beside them. It counts as read whole, as
  ADR 0022 requires before sending, once its last part has shown. Answers keep the agent's order,
  short ones two a row, long ones one a row in at most 2 rows of words; a longer answer is cut, and choosing it slides out its side panel with all its words, so
  what Send answer sends can be read first. "Type my answer" and the paging row stand side by side
  last on every page: "More answers, 2 of 2", and "First answers, 1 of 2" on the last, a row since
  Hold to talk holds the place beside Send answer. Paging clears what was chosen, so Send answer
  only sends what is on the page in view, and answers chosen together are chosen on one page. No
  answer the agent offered is left out. A question of several prompts shows one at a time, each
  ending with a row, "Next question, 2 of 2", which keeps the earlier prompts' choices; after the
  last, a row "Your answers" opens a page listing each prompt's answer as a row back to it, and only
  there does Send answer send, so nothing sent is ever out of view.
- **Two columns at most.** The menu, a file and a side panel come to about 98 degrees with their
  gaps, past a Quest 3S's 93. When a file beside the menu opens a side panel, or the two don't fit
  the field together, as with a page taller than the menu's rows leave or a file's title on two
  rows, the menu steps aside, off the plane, with the re-centring's eased slide; it comes back once
  the side panel is closed and the two fit again, or the file closes. Amended 2026-10-03: the other
  way round too, when a row chosen in the menu opens its details with a file beside it, the menu's
  details take the front and the file steps aside, off the plane to the right, with the same slide,
  taking no press meanwhile; when the details close, it comes back. So no menu action acts on details
  the person never sees, and they see what they just chose.
- **Beside a window**, with the characters either side of a window straight ahead, the menu or a
  file opens centred under the window, between them, one at a time: the two together clear the
  characters' labels only 61 degrees below eye level, far past the field, so a file opened there
  takes the menu's place, the menu stepping aside, and a file's side panel takes the file's place at
  both text sizes, since a file and its side panel don't clear the characters either. The file
  draws no light line, which would run across the window; the pill on its subject still names its
  task's state. The window is assumed in a lane straight ahead, 48 by 28 degrees
  (`CharacterStage.WindowLaneHalfWidthDegrees`, `WindowLaneHalfHeightDegrees`), which the plane
  clears as it clears a label. Turned aside for a
  window, the plane stands under the characters, as everywhere. Nothing of Halcyonic's comes within
  a degree of the window's assumed place, straight ahead; the renders check it.
- **Labels are cleared where they stand** (2026-10-02, lane U's renders at the far stage on a Quest
  3S): placement clears each label at its own yaw, not with the plane's lowest corner, which had
  set a 64 degree plane's top at 18.9 degrees and taken Tasks' 4 rows beside a file to 43.9 of
  43.5. At the far stage this gains nothing, since the arc's outer labels stand over the plane's
  corners, and the hero stays 0.4 degrees too tall for a 3S there. So:
  - The head is taken to tip as much as the plane's bottom needs, at most 8 degrees
    (`WorkspacePlacement.ReadingPitch`), in place of 1.5 degrees for each degree past 26. The old
    rule left planes between 24.6 and 28.8 degrees tall fitting neither level nor tipped, which
    caught 3 rows too.
  - Tasks beside a file packs its rows by the stage's real height: 4 where they fit, 3 on a 3S at
    the far stage, decided when the menu opens, so a page never changes while it shows.
  - The footer keeps its full room below its prompts; halving it would gain 0.1 degree.
  - The menu never steps aside for good.
- **At larger text a side panel takes the file's place** (2026-10-02): a file and a side panel come
  to about 72 degrees, whose low corners leave a 3S's field wherever the top stands. The side panel
  stands where the file stood, keeping the file's light line and the pill on its subject, so it still
  reads as that task's, and Close details brings the file back. Amended 2026-10-03, after a render
  showed a setting's change, Text size's own included, drawn nowhere at larger text: a side panel in
  its frame's place, a file's or the menu's, at either text size, stands as wide as that frame and
  carries, where Close stood, Close details; a confirmation's Cancel; paging only while the side
  panel is in parts, which the frame's Next page turns, never the page it hides; and only the prompts
  its column marks safe in place, whose side panel shows everything they act on, as a setting's
  change and Forget's Yes. Nothing else is carried, as Send answer on an answer's panel, which sends
  the whole draft: Close details brings the page back, which sends with everything in view. A press
  or hold there counts only for what that side panel drew and allows, while it is the one drawn last
  for the frame in front: in its frame's place, what it carries; beside its frame, Close details
  alone. A task's file or New project opening beside the menu lets go of the menu's chosen row, whose
  side panel it would leave undrawn. Amended again 2026-10-03, after review: the menu or a file
  coming back from stepping aside takes no press as it slides in, every button waiting to settle as
  a new one does, so an Approve or an armed Yes under the hand is never pressed by the slide.
- **Lane U's views set the last spacing**, measured with the split header's pill: the plane's top
  stands 17.5 degrees below eye level (`MenuPage.TopDegrees`); a subject's title stands half a grid
  step under the pill's lower edge; the footer follows the page's last target 12 mm below it, with no
  16 dp gap; a small fact keeps 18 dp's room wherever it stands, so a line wraps the same anywhere,
  and is drawn at 15 dp where that reads, a fact from outside taking at most 40 percent of its line;
  a row's tone stands on its icon, and on its fact only where the row waits, its words in the text
  colour; and answers pair two a row
  where each fits half the row in one row.
- **A side panel's fact names its value above it at 18 dp**, in the secondary colour, the value at
  18 dp under it. At 15 dp the name would stand above larger type, which type stepping down
  forbids; the render caught it on Usage's side panel.
- **A page's source line counts as one of its rows.** With a side panel open, a page of 4 rows and
  its source line reaches 0.1 degrees past a Quest 3S's field, so such a page holds 3 rows, and 2
  at larger text.
- **Usage lists each limit as a row**: whose and which window, its share left as the small fact,
  "At most 60% left", and a chevron; chosen, its side panel says when it was seen, when it resets
  and whose account it is, with the source. Refresh stands beside Close and Next page at the far
  right. A meter under each row read as an underline, which one selection treatment forbids, so the
  words carry the share.
- **Settings lists each setting as a row** under its group's heading (Your space, Comfort, and in
  development builds Your computer), its value the small fact; chosen, its side panel says what it
  is now and what the change does, and the footer's main action is that one change, as Make text
  larger. A setting of more than two values steps to the next, its prompt naming it, as the sounds
  and the characters' arrangement do today.
- **The light line is one leg** from under the character's label to the file's subject: straight
  down from the middle of their overlap where the label stands over the subject, as the eyes see
  them, else from the label's nearer bottom corner to the subject's nearer top corner. Leaving from
  under the label, below any mark, it crosses no words, and it crosses no other label or character;
  the render checks it against every label and character as the eyes see them. Over a desk, where
  the plane stands above the lineup, it rises from the top of the character's body to the file's
  bottom edge.
- **A page of content keeps its glow above its first row.** The light from its top edge ends within
  its top padding, so a row that is not chosen never looks lit; a subject keeps the glow's full
  reach. The render checks that nothing to press stands in a page's glow.
- **New project's pages page as the menu's do** (2026-10-03, the coordinator). Where a page needs
  more than the stage gives, a list (the recap's facts, the folders, the agent apps and models)
  turns by the footer's Next page, "First page" on the last, in the secondary place beside the main
  action; a question's answers turn as answers do, by "More answers, 2 of 2", the question heading
  them where it fits and first on a page of its own where it and a row of them don't, a turn
  clearing what was chosen; the unknown start reads in parts, as a confirmation, by "Next part, 2 of
  3", Clear waiting for its last part; and only Starting, whose secondary place holds its change,
  turns by a row, "Next page, 2 of 3". The source line and the reason stay on every page. A page's
  room is read once for what shows, never as the head moves, and anything new starts at its first
  page.
- **Never four prompts** (2026-10-03, the coordinator). The footer's middle acts on the chosen row;
  with none chosen, it pages. A chosen row's action replaces the pager, since a turn closes it
  anyway: the person closes its details or chooses another row on the page, then turns. New
  project's Start over is the recap's last row, on its last page even when the recap fits one,
  chosen as a fact is, its side panel saying what starting over clears and its action arming the
  confirmation in place; How it runs' Change agent app is a row before the models. Each chosen
  row's action, Start over's Yes and Start building are safe in place, so a side panel standing in
  the recap's place carries them. The workspace render lays every New project footer at both text
  sizes and fails on four prompts or a footer that doesn't fit.

### Decided on 2026-10-08, from the owner's picks

On 2026-10-08 the owner picked the recommended options of lane V's proposal "One kit, four forms" and of
its page on outside words, "Whose words" (both outside the repository). These notes record what they
change here; [ADR 0013](0013-characters-are-bots-with-a-living-surface.md) and
[ADR 0027](0027-the-headset-moves-only-to-answer-the-person-or-show-a-wait.md) carry the characters and
the motion. None of it is built yet; lanes U, C and W build it, each step with its renders.

- **The first visit is one question.** On a headset's first visit to a computer with no task at all,
  one plate asks "What would you like to work on?", with two rows, Something new and A project on
  your computer, and Something new chosen as it opens. The chosen row sets the main action, Start a
  project or Show my projects; with Something new, Hold to talk stands beside it, and the words it
  hears land in New project's Your idea as the person's own. Neither row has a chevron, since both
  only choose. Start a project opens New project alone in the plate's place, and Show my projects
  opens Projects alone with its folders.
  - The row of places holds Settings alone, at its right end, in the slot it keeps once Tasks,
    Projects and Usage join it with the first task (lane V, after the pick, so Text size, Reset
    position and pairing stay one press away). On the plate and on Projects alone nothing in it is
    lit, since neither is a place yet.
  - Close folds to the bar, which reads "Nothing is running yet", and while there is no task its Open
    brings the plate back. The stage shows nothing, the first-time hint hides while there is no
    character, and the banner steps aside for the plate as it does for the menu.
  - Once a task exists the first visit is over for that computer on that headset: every later visit
    opens closed, the bar and the characters, and a first visit to a computer that already has work
    opens closed with its places.
  - The demonstration's first visit opens closed too (the owner, 2026-10-08): its recorded characters
    at work, its two lines above the stage and the bar saying what waits. Projects and New project's
    recorded companion stay one press away, and the headset still keeps each first visit apart.
  - As built (lane C, 2026-10-08): the plate opens by itself once each app start, when the live
    state is first ready with no workstream, and a reconnect doesn't open it again; nothing is
    decided before the first snapshot or on a stale state. "For that computer" is its journal: the
    headset keeps the journals that have had a task (`halcyonic.entry.started`, the 8 most recent),
    so another computer with no task still asks. The demonstration's lines stand raised above the
    stage over the closed bar too, since the bar stands where they would hang.
- **A task opened from its character opens its file alone,** and the menu stays closed until the
  person opens it. A task chosen in Tasks still slides out beside the menu.
- **A grip under the plane and under the bar.** Holding it and moving drags the whole plane round
  the eyes, still facing them, as holding a file's subject does (`PanelDrag`); its target keeps the
  60 dp hit area. Reset position stays in Settings' Your space.
- **Where tasks wait is asked once,** the first time another window takes focus and the person comes
  back: "Where should your tasks wait?", with Below my screen, On my right and On my left, and the
  main action Keep them here, Move them right or Move them left, which moves the bar, files and
  characters together. Close leaves them, and the question is not asked again. Without a desk the
  answer picks today's arrangements, Either side of a window, or Room for a window turned to that
  side. Settings can change it later, by a row that replaces The characters.
- **Characters an open file would hide step aside** along the desk, and come back when it closes.
- **Tasks keeps what left the stage under Earlier,** newest first, after what waits, what works and
  what ended recently ([ADR 0013](0013-characters-are-bots-with-a-living-surface.md)'s stage for now).
  An Earlier row's small fact is its age with its noun, "Ended 2 days ago" or "Last heard 3 days ago",
  in place of its project: the one exception to one kind of small fact a list, since there the age
  is what tells the rows apart. Opening one brings its character back while its file is open, and
  Keep on stage keeps it there, the secondary prompt beside the main action, or the main action where
  the page has none.
  - When 3 or more quiet tasks would take the stage as a session starts, a plate asks once: "4 tasks
    haven't been heard from since Monday." ("for over a week" past 6 days), then "Move them to
    Earlier? Tasks keeps them, and you can bring any back.", with Move to Earlier as its main action.
    Close leaves them and never asks again about those tasks.
  - The headset keeps what moved, as Hide from stage does; whether every headset should share it is
    open ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)). In the demonstration nothing ages, since
    its clock is the recording's.
- **Words from outside lean, on a ground only Halcyonic draws.** Halcyonic's own words stand upright;
  text it didn't write leans. Where it meets Halcyonic's words it sits on a ground only Halcyonic
  draws:
  - inside one of Halcyonic's sentences, on a token, a faint ground hugging the words so both edges
    show, and last in the sentence where the words can be put so;
  - in a row that is all theirs, as an agent's answer or a folder's name, with Halcyonic's quote mark
    (`format_quote`) in the icon column where it is free, or Halcyonic's icon for the thing;
  - a block of theirs, as the agent's question or a source's summary, on one ground with the quote
    mark at its corner, its source on the page's source line;
  - a title in its own plate leans, the plate its ground, and the pill on its edge stays upright.

  Pills, the light line, prompts, places and sections carry Halcyonic's words only. Halcyonic adds no
  quotes and no leads such as "It says:"; a claim's chip stays as its class, never the only mark; and
  theirs is cut first, inside its ground. Lane W applies it as batch B of the outside-text audit
  ([outside-text-audit.md](../validation/outside-text-audit.md)).
- **The stage's lines stand above the stage** (lane V and the coordinator, after lane C's render found
  the banner under the labels overlapping the closed bar by about 2 degrees). The demonstration's two
  lines and a live session's lines stand raised above the stage, never under the labels. "Connected to
  your computer" shows for 3 seconds as the connection becomes live, then leaves over Leave; a
  connection problem stays until it clears. "Still open: {title}" moves into the closed bar's subject
  while nothing waits, and what waits wins. A render checks that no stage line overlaps the bar or a
  label, at both text sizes and on a Quest 3S.

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
- **A plane that stays where it was while a side panel slides out.** Rendered on 2026-10-02 for the
  menu and a file: it ends off square to the person, the file's far text reads under 14 dp (the main
  prompt at 73 percent), a corner falls past a Quest 3S's field, and the file's top runs into a
  character's label.
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
- The menu and a file side by side make a plane 64.3 by 33.0 degrees with the split header, from 17.5
  to 50.5 degrees below eye level along its middle (lane U's views); ADR 0023's panels reach about 42
  degrees. Whether the low edge is comfortable is judged on the
  headset, beside the open question on panels taller than 26 degrees
  ([OPEN_QUESTIONS.md](../product/OPEN_QUESTIONS.md)); if not, a menu shows 3 rows a page.
- A Quest 3S's field holds the menu and a file at both text sizes, as `FieldChecks` sees it with the
  head turned to the composition and tipped 8 degrees (`WorkspacePlacement.ReadingPitch`), with about
  a degree to spare at the near stage, Tasks showing 4 rows a page at the standard size where they
  fit and 3 at larger text or on a 3S at the far stage; lane U's
  views render all five of its compositions at both sizes and check it.
- Contrast at 96 percent is calculated, not measured: over a white wall, as in passthrough, secondary
  text keeps 4.8:1 (`GlazeChecks.Over`). The contrast check composites every surface over white, and
  the opacity rises or the secondary colour lightens wherever text falls short.
- Lane V's refined renders count 137 draw calls for the scene with the menu and a file, and 46 to 51
  for each column before batching; the separate shapes share one material in the product. The
  budgets are 60 a surface and 220 a scene.
- The renders' new checks move into lane U's `GlazeChecks` for every surface: text as the eyes see
  it, one plane facing the eyes, aligned columns and one selection treatment. Today's renders list
  what reads under 14 dp as seen without failing on it: the labels on a desk, and captions near the
  corners of Settings and the entry panel's Guide, both being replaced.
- The stage's labels on a desk lean back to face the eyes already, approved on 2026-10-02 and recorded
  in ADR 0023, and the stage render holds them to text as the eyes see it.
- Without Move in the footer, moving the plane rests on holding a file's subject, and Reset position
  on Settings; both are on the headset checklist.
- Not yet checked on a headset: reading at 0.46 m, the low edge at 50 degrees, the glass in
  passthrough against bright walls, the light line's comfort, unseen hit areas and their feedback,
  and turning between a file and its side panel.
- Revisit if reading at 0.46 m strains, if the low edge strains the neck, if unseen hit areas bring
  wrong presses, if the glass fails contrast in real rooms, or if the draw call budget is exceeded.
