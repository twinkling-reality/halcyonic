# ADR 0023: The headset's interface is one system of tokens, components and render-checked rules

- Status: Accepted on 2026-10-01 by the owner.
- Date: 2026-10-01

## Context

After the fifth headset session (2026-09-30) the owner found the function better and the
experience a headache: panels and labels over characters, over each other and under system
windows; actions placed wherever each screen needed them; every button the same grey rectangle
with text only, no icons and no colour meaning; confusing words; and character plates that mix the
title, the status and the reason in one shape ([quest-3-device.md](../validation/quest-3-device.md)).

An audit on 2026-10-01 rendered every screen with the four editor checks on main (2da2010) and every
character state at 2.4 m and on a desk, and read the Unity layer. All four checks pass, because they
test what they were written for: nothing is cut, nothing shows through a panel, no panel covers a
body. The causes of what the owner saw are structural:

- Each surface lays itself out with its own constants (`WorkspacePanel`, `EntryPanel`,
  `ProjectRail`, `UsageLeftGlance`, `RoomControls`, `PairingPanel`, `CharacterView`), and three
  assemblies keep their own visuals helpers with their own colours.
- `WorkspacePlacement` clears bodies but not labels, so every foreground panel at 2.4 m opens over
  the plates of the characters it passes, the one that needs the person included.
- Nothing models actions, so the primary action sits in a different place on each screen, and
  primary, navigation and destructive actions share one look. Amber means needs you, confirm,
  chosen, hidden and error.
- Character labels use `TextMesh` with the built-in bitmap font, panels TextMeshPro SDF.
- Three bugs come from the same causes: a note label shared by every entry screen leaves a voice
  error over the folder list; the answer line shows the first question while the body shows the
  second; the review spells Halcyonic's own ellipsis as a code point.

Meta's current guidance, read the same day
([headset-ui-guidance.md](../validation/headset-ui-guidance.md)), sets numbers the interface does not
meet: hand targets of at least 48 dp and 60 dp for primary controls, 12 mm between targets, touch
panels at 42 to 46 cm and not at 0.5 to 0.8 m (panels sit at 0.6 m), text of at least 14 dp,
dark backgrounds no darker than #1A1A1A, a sound for every successful selection, filled icons.

## Decision

- **One UI layer.** `Halcyonic.XR.UI` holds the tokens, the primitives (one SDF surface shader,
  TextMeshPro labels by type role that set text only through `LabelText`, icon glyphs), the
  components that take no input (badge, tag, count, title plate, peek card, banner, toast, empty
  and loading states) and layout (stacks, grids, zones). `Halcyonic.XR.UI.Interaction` holds the
  components on the Interaction SDK (button, list row, tabs, text field with hold to talk, confirm
  step, pager, action bar, panel frame); `PointerTarget` moves there, and `PanelButton` stays as an
  adapter with its API until every surface has moved. The stage assembly references only the first.
  (On 2026-10-01 Usage left, the last surface on `PanelButton`, moved onto the frame and
  `PanelButton` was removed, with the sizes kept for it.)
  No surface sets a colour, size or position of its own.
- **Tokens in code, once.** Colour roles with fixed meanings (amber only for needs you, red only
  for something that went wrong, the cobalt accent only for what can be acted on), sizes as angles
  (1 dp = 0.0625°, from Meta's 48 dp = 3°): body text 1.125°, nothing under 0.94°; standard targets
  3.75°, none under 3°, 1.5° apart; radii, depth, motion durations and the Glaze cue for each
  interaction. Panels are no darker than #1B222D.
- **Panels at touch distance.** Foreground panels open 0.46 m from the eyes, 44 × 26° (the Medium
  size 30 × 18°), facing the eyes, with Move, Reset position and Close in the header and every
  action in a bottom bar. (On 2026-10-01 the workspace, which stays beside its character and so
  neither moves nor resets, took only Close, at the end of its row of tabs, and Refresh beside a
  section's heading, so its body keeps room for the question and the log; a confirmation whose
  content pages shows its pager at the top, away from Yes. Usage left, which belongs to no character
  and stays where it opened, took only Close, in its header. On 2026-10-02 Move became the grab
  handle as well: pressed, it steps the panel aside as before; held, the panel follows the hand round
  the eyes at touch distance, as Meta lets a person reposition a touch panel. A separate handle in
  the header would have cut the panel's title. Neither Move nor Reset position moves a panel while
  a confirmation is armed.)
- **Words and decisions stay in the client core.** A state language maps every work state to a
  word, a tone, an icon, an edge and a motion, so no state is told by colour alone; an action set
  admits one primary, two secondary, one destructive and an overflow, and nothing more;
  presentation models per surface (character label, rail, panel, ambient strip) say what to show,
  never where. The Unity layer draws a model and lays it out by the tokens.
- **Character labels in three parts.** A title plate (at most two lines, 96% opaque, TextMeshPro),
  a state badge on its top edge, and marks for practice, demonstration and recorded work on its
  bottom edge; the reason shows only in the peek. (On 2026-10-01 the marks moved from beside the
  badge to the plate's bottom edge: beside it, a badge and a mark reach 11 to 13.5 degrees, and
  neighbours stand 12 apart.)
- **One word per state.** Not started, Starting, Working, Checking its work, Waiting for you,
  Finished this round, Checks failed, Couldn't finish, Stopped and Can't tell yet, kept once in the
  client core (`StateLanguage`). A badge says the short word; a sentence says the same state in
  full, as a person would ("1 task is waiting for you"). On 2026-10-01 the owner changed "Needs
  you" to "Waiting for you", after count lines such as "1 needs you" became sentences.
- **Zones from the eyes.** The virtual stage rises so bodies stand about 4° below eye level;
  foreground panels open below every plate there and above every body on a desk; the rail, peek,
  ambient strip and, in a window mode the person chooses, a lane for a 2D window each have a zone,
  and nothing Halcyonic draws pops up in front of the person.
- **One icon set.** Material Symbols Rounded (Apache-2.0), filled, weight 500, as a static
  TextMeshPro SDF atlas of only the glyphs used, made with fontTools, named by meaning in the
  client core. (On 2026-10-02 the badges and marks took their icons: 13 glyphs of version 2.972,
  at grade 0 and optical size 24, in a 256 pixel atlas that carries no font file and falls back to
  nothing, each on a label of its own beside the words it goes with, never in their text. Text
  from outside shows every Private Use Area character as its code point, so only Halcyonic draws
  an icon. Starting and Working turn theirs. On the stage a badge shows its icon only while it
  stays within the plate's 10.5 degrees, so Checking its work, Finished this round and Waiting for
  you with a count show their word alone there, and their icon in the peek and the workspace.
  Then the actions took theirs, before their words, from the spec's table: every action keeps its
  words, the window controls included, though the spec gave those only a word on hover; only Hold
  to talk shows the microphone, never approving, denying, stopping or any confirmation; a panel's
  bar shows its icons only where all its actions fit with them, else its words alone. The pager,
  the tabs and the rail's project pills have none yet.)
- **The renders enforce the rules.** Every render checks overlap between zones, target size and
  spacing, text size, contrast, that no state is told by colour alone, that navigation and the
  primary action stand in the same place on every screen, and Halcyonic's own words. A stage
  render of every state and a component gallery render are added.
- **One surface per commit.** Character labels and state, the rail, entry and Create, the
  workspace and its questions, then Usage left and the ambient strip.

## Alternatives considered

- **Meta's Interaction SDK UI Set.** Meta's own components and look, but built on uGUI canvases
  with a `PointableCanvasModule`, the second UI stack ADR 0014 declined for this job; its icons are
  bitmap atlases; and its licence allows no derivative works beyond samples (1.2.1) and forbids
  making its materials subject to an open-source licence (1.2.8), while re-theming it means copying
  its assets into this Apache-2.0 repository. It stays a reference for patterns and numbers.
- **Restyling each surface where it is.** Quickest, but leaves the causes: per-screen constants,
  shared labels, no action model. The same problems would return with the next screen.
- **Unity's world-space UI Toolkit.** Not evaluated: it would replace the TextMeshPro labels and
  plane targets the editor checks are built on, and its input from the Interaction SDK on a Quest is
  unverified here.
- **Lucide or Phosphor icons.** Lucide is outline only, against Meta's filled icons for immersive
  apps; Phosphor (MIT, with a fill weight) would also work.
- **Panels kept at 0.6 m.** Inside the 0.5 to 0.8 m range Meta's hands guidance asks to avoid. A
  ray layout at 1 m with the same tokens is the fallback if reading at 0.46 m strains.

## Consequences

- Every surface looks and behaves alike, a token change reaches all of them, a new screen is a new
  model, and the rules fail the build when broken.
- About fifteen Unity files and their renders change, one surface at a time, coordinated with the
  lanes working in the same files. `PointerTarget` moves assembly.
- Larger targets and gaps mean fewer rows per page: lists show four a page.
- Fonts (Liberation Sans Bold and Liberation Mono, SIL OFL) and the icon atlas (Apache-2.0) are
  committed with their licences, and NOTICE names them. (On 2026-10-02 NOTICE named the two fonts
  the app includes, Material Symbols Rounded and Liberation Sans. Neither Liberation Sans Bold nor
  Liberation Mono was added: strong text is drawn thicker by its material, and no text is set in a
  monospaced font yet.)
- Two quiet Glaze cues are proposed for presses that register and presses refused; they need the
  owner's approval. (Approved on 2026-10-01 and added as Touch and Not now, with every cue renamed
  to the words the person reads.)
- To check on the headset: reading at 0.46 m, list density with 60 dp targets, the stage's new
  height, a desk with little room above the lineup, the window lane, and the new cues. (On
  2026-10-01 window mode was built as Either side of a window, beside the two arrangements before it.
  The spec's four characters at 28 and 37 degrees at eye level assumed badges of icons alone; with
  words a badge is about 11 degrees wide, so the render placed two on each side at 32 degrees, one
  above eye level and one below, the outermost label reaching about 37 degrees.)
- Revisit if reading at 0.46 m strains, if 60 dp targets make lists unusable, if Meta licenses its
  set compatibly, or if the draw call budget (60 a panel, 220 a scene) is exceeded.
