# How Halora looks

Canonical visual specification for Halora's headset panels, moments, buttons, typography, colour,
sound, motion and automated render checks. Token and layout logic lives in
`apps/xr/Packages/com.halcyonic.client` and the Unity rendering layer in
`apps/xr/Assets/Halcyonic/UI*`; client architecture and threading live in
[XR_CLIENT.md](../architecture/XR_CLIENT.md), on-screen copy in [WORDS.md](WORDS.md), and brand
surfaces outside the headset's panels in [BRAND.md](BRAND.md).

Every surface is evaluated from the wearer's eyes against three rules: one thing at a time, fixed
controls, clean.

## Room glass, buttons and code colours

Workspace panels, bars, tags and actions (`RoomSurface`, `AskGlass`, `RoomButton`, `RoomType` and
`CodeColouring`) render in neutral true-grey glass with zero hue in the chrome, enforced across all
rendered states by `KitRender.ChromeTakesNoHue` (maximum HSV saturation `0.12` above value `0.04`,
exempting only character/figure bodies, app content and code syntax spans).

- **Glass (`AskGlass`, `RoomSurface`, `Halcyonic/Room Glass`):** `AskGlass` defines neutral dark
  glass in two forms: `FaceColour` `#121212` at `FaceAlpha = 0.918` in Unity's linear blend (used on
  tag plates such as `RoomTag`), and `PageColour` `#0E0E0E` at `PageAlpha = 0.72` in sRGB
  (`RoomWords.GroundColour` on `RoomBand` for the agent's words band). `RoomSurface.DarkLook`
  renders panels with `#121212` at `DarkAlpha = 0.96` so bright passthrough rooms do not muddy the
  face, a 1 dp outer edge easing from `#919191` at the top (`LitEdgeTop`) to `#2C2C2C` at the foot
  (`LitEdgeFoot`), and 1 dp inner sentence/divider hairlines of white at `0.08`
  (`AskGlass.HairlineAlpha`). `RoomSurface.LightLook` inverts the same structure (`#FBFCFE` at
  `LightAlpha = 0.64`, `#E3E4E8` edge, `rgba(12, 14, 22, 0.08)` inner hairline). Squircle corners
  are 20 dp on panels, 22 dp on bars and 14 dp on tags; moving sheen is off (`RoomSheen.Peak = 0`),
  and panel drop shadows remain off (`DrawsShadow = false`) so layout bounds match visible glass
  edges.
- **Buttons (`RoomButton`, `Halcyonic/Room Halo`):** every target meets the 60.8 dp hand-target
  floor (`CoreDp = 48` on column actions, 60.8 dp on home actions), pairing a stroke glyph
  (`RoomGlyphs`) with unwrapped or balanced-wrapped white words on a soft black text underlay
  (`0.35` alpha). Standard actions sit on an opaque `#272727` capsule (`#3C3C3C` pointed, `#323232`
  held, `1.2 dp` edge feather); quiet secondary dialog actions hide their plate at rest and show it
  when pointed or held. A moment's single main action (and `Confirm` while locked) uses the lighter
  neutral plate `#474747` (`#555555` pointed) with white glyph and white words, distinguished by
  luminance rather than hue or rim (`RimPainted = false`). `Confirm`'s lock draws a 2 dp white
  progress ring 4 dp clear of the squircle chip; single- and multi-choice answer marks draw a 2 dp
  white ring or rounded box and a white check (`#FFFFFF`) when chosen. An action's optional aside
  chip (`AsideGround`) floats 10 dp to the right on `#2D2D2D` at `0.78` alpha with a 1 dp white
  edge at `0.06` and 17 dp label text. Unavailable actions are omitted rather than dimmed.
- **Room typography (`RoomType`):** reading words use `Inter Regular` at 20 dp on a 28 dp line
  (`-0.6%` tracking), runtime commands and code use `JetBrains Mono Regular` at 19 dp on a 28 dp
  line, and eyebrows and state labels use `Inter Regular` in spaced uppercase at 18 dp (`+0.24 em`
  tracking, `0.53` linear alpha so labels hold `4.5:1` over bright passthrough). `Larger` text
  multiplies scaled metrics by `1.15` and reflows.
- **Code syntax colouring (`CodeColouring`, `Appearance` > `Code Colours`):** `CodeTokens` and
  `GlazeRuns` colour syntax spans directly on glyph vertices without rich-text tags or string edits,
  holding at least `4.7:1` contrast on the glass across both dark and bright rooms:
  - `Halora` (default): keywords `#CFC1FF`, types `#F2BAD1`, names `#F3EBDC`, strings `#9EE6D8`,
    numbers `#A8D4FF`, comments `#C3CAD4`; punctuation and plain words stay white.
  - `Plain`: every token keeps the row's white.
  - `High Contrast`: keywords `#D3C0FF`, types `#FFB8E0`, names `#FFFFFF`, strings `#7FF2E0`,
    numbers `#99CFFF`, comments `#C9D0DA`.

## Spatial room layout and colour ramp

[ADR 0040](../decisions/0040-the-headset-is-a-room-of-projects.md) defines the room layout: projects
around the user, up to three open project screens with a bar beneath the active screen, moments from
the task's character, and a reading surface that unfolds on demand. Layout geometry and placement
rules are documented in [XR_CLIENT.md](../architecture/XR_CLIENT.md).

**Angular placement** (degrees at the eye with the head level; Quest 3 field is 110 by 96):

| Part | Where | Size |
| --- | --- | --- |
| Gallery tiles | Facing the person at reading distance, shelves from about 22 above to 18 below eye level | 14 by 7.9 per tile with many projects; 28 by 15.8 for a newcomer |
| A picked project's screen | About 2 m away, centred slightly above eye level (team screens at 24 degrees wide over their places) | 56 by 31.5 (48 by 27 at Larger) |
| Its bar | Under the screen, parallel and slightly in front, matching the screen's edges | 5.4 high per row, two rows when one does not fit; folds to a 6 by 0.45 handle while idle |
| The character | Beside the screen's right edge or at its project place in the room | About 5 by 6 |
| The moment slot | Beside or above the character, one moment at a time, actions beneath | 23 wide; height follows content |
| The reading surface | About 1 m ahead of where the person looks, slightly below eye level | 44 by 33 (704 by 528 dp) |
| Heads-up items | Low at the sides, about 31 below eye level and 31 to either side, on a leash | At most two, 2.8 high |
| Native compact panel | Beside other Quest apps, on the native Android panel route | About 20 by 10 |

**Colour (`SpatialKit`, `KitPillMaterial`).** Opaque carriers only; words never sit directly on the
room. Contrast is measured as a WCAG ratio (`SpatialKitTests`). Every neutral step in the kit is a
true grey (`R == G == B`), paired with white words and semantic state tones reserved for character
figures and non-chrome indicators. The character figure (`KitFigureLook`) keeps its own neutral and
state tones (`#DADDE0`, `#A0A5AC`, `#767B82`, `#59CFF1`, `#FABA59`, `#69D98D`, `#F7857D`) so text
ramp adjustments never alter a figure's eyes.

| Step | Value | For |
| --- | --- | --- |
| n0 (`Well`) | `#202020` | Sunk wells (commands, code, recessed spine and rail), empty screen edge |
| n1 (`Carrier`) | `#313131` | Primary word-carrying face, lit from above: `FaceTop` `#3A3A3A`, `FaceBottom` `#292929` |
| n2 (`Raised`) | `#383838` | Chips, stacked moment edges, selection lift |
| n3 (`Control`) | `#414141` | Resting control fill; `Thumb` `#3F3F3F` raises the mode switch thumb (`>= 1.5:1` on `n0`) |
| n4 (`Hairline`) | `#969696` | Locked answer rings, hairlines, dotted reported-code gutter (`>= 3:1` across `n0`..`FaceTop`) |
| n5 (`TextTertiary`) | `#DBDCDC` | Times, counts, `Reported` metadata (white at 78 percent) |
| n6 (`TextSecondary`) | `#E1E2E3` | Secondary words (white at 82 percent) |
| n7 (`Text` / `OnFill`) | `#FFFFFF` | Primary words and capsule labels |
| Primary (`PrimaryFill`) | `#006ADB` | Flat fallback primary fill (`>= 5:1` under `n7`), pressed `#0058B8` (`>= 6:1`) |
| Pill Blue / Pill Green | `#474747` | Prominent `KitPillMaterial` plate (`Blue` and `Green`): flat `#474747`, pointed `#555555`, white words, zero gloss, crown, catch-light, bounce or separate rim, matching `RoomButton.MainPlateColour` |
| Pill Neutral / Pill Red | `#3D3D3D` | Secondary and round `KitPillMaterial` true-grey glass (`Neutral` and `Red`): five stops `#555555`, `#404040`, `#3D3D3D`, `#303030`, `#474747`; outer rim `#222222`; bottom bounce `#797979` at `0.16` alpha; upper crown `0.22` to `0.06` to `0.00`; top catch-light `0.28` |
| Avatar Tint | `#0F3380` | Optional left-edge wash token (`KitGlassMaterial.LeftWash`, `28 -> 16 -> 6 -> 0` percent); off on shipped chrome (`KitRender.ChromeTakesNoHue`) |
| Live (`Accent`) | `#7ADCF7` | Active figure light (`>= 4.5:1` on every grey step); never a button fill |
| Needs (`Attention`) | `#FABA59` | Waiting figure light; `AttentionSign` `#FFC164` |
| Failed (`Failure`) | `#FFAAA3` | Failed figure light; `FailureSign` `#FFBBB4` |
| Passed | `#69D98D` | Passed figure light and checkpoint diff additions on `AddedBand` `#1B3324` |

Every text and state word holds at least `4.5:1` across `Well`, `Carrier`, `Raised`, `Control`,
`FaceTop` and `FaceBottom` (`n7 >= 7:1`), `n0` (`#202020`) holds `>= 7:1` on observed discs, `n4`
(`#969696`) holds `>= 3:1` on every surface a locked ring sits on while staying quieter than `n5`,
and no carrier is darker than `#1A1A1A`, the darkest step the display separates. State is conveyed
by form, weight and words, never by colour alone.

**Kit glass (`KitGlassMaterial`, `KitGlass`, `Halcyonic/Kit Glass`).** Kit carriers draw one opaque
vertical gradient (`FaceTop` `#3A3A3A`, `Carrier` `#313131` at middle, `FaceBottom` `#292929`) with
a soft radial white top light (`0.10` alpha fading by `60%` of height), a 1 dp outer white ring at
`0.06`, a 1 dp white top highlight at `0.30`, and one soft drop shadow (`18 dp` down, `40 dp` blur,
black at `0.24` alpha, fitted in linear light over `#B9AE9E`). Insets are 10 dp on panels, 8 dp on
the lost plate and earlier-moment lip, and 6 dp on tiles and tags. All face layers composite in sRGB
inside a single-pass textureless shader. Moment arrival bloom peaks at `0.11` so `n5` words keep
`>= 4.5:1` on the lit top; shipped chrome leaves panel tone and tint unset.

**Kit capsules (`KitButton`, `KitPillMaterial`, `KitPill`, `Halcyonic/Kit Pill`).** Capsules are
60 dp tall, at least 60 dp wide, and 12 mm apart. Prominent primary actions (`KitPillMeaning.Blue`
and `Green`, 28 dp side padding) render as the flat `#474747` plate (`#555555` pointed, with a 3 dp
outer ring `PrimaryRing` at `>= 3:1` contrast). Secondary capsules (`KitPillMeaning.Neutral` and
`Red`, 18 dp side padding) and 60 dp round icon buttons (`KitButtonRole.Round`, such as the reader's
close button) render through the five-stop neutral glass ramp (`#3D3D3D` middle, `#222222` rim,
`+5%` sRGB pointed lift at `103%` scale, `-12%` pressed darken at `97%` scale). Locked capsules draw
a 2 dp `n4` ring around their `n5` reason text; quiet controls (`KitPillMeaning.None`) use an
outlined capsule. The screen bar scales all buttons uniformly so labels share one type size,
splitting lead actions above navigation doors when a single row does not fit.

## Static typography and flow

Six static SDF font assets (`NOTICE`, [fonts-2026-10-30.md](../validation/fonts-2026-10-30.md)) are
built from bundled SIL Open Font License 1.1 sources (`KitFontAtlas`, `KitType`): `Inter`
(`SemiBold`, `Regular`, `Medium`) and `JetBrains Mono` (`Regular`). Every atlas holds printable
ASCII, typographic punctuation, literal code-point marks (`‹`, `›`, `⇥`, plus `↵` on `Inter` with
`KitMono` falling back to `KitReading` for `↵`, `Ĳ` and `ĳ`), and `KitGlyphs.Latin` (Latin-1
Supplement and Latin Extended-A), verified byte-for-byte against an in-memory rebuild by
`KitFontAtlas.Check()`, with `Liberation Sans` as the final fallback. `InstrumentFontAtlas` builds
`Rajdhani Medium` (falling back to `KitTitle` then `Liberation Sans`) for headings on `KitSpine` and
`Glaze` frames. In room faces, outside text stands upright, distinguished by its region, attribution
and monospace role while preserving exact `LineWords` runs. `Larger` multiplies by `1.15` and
reflows; nothing zooms.

| Role | Face | dp | Degrees | Tracking | Line | For |
| --- | --- | --- | --- | --- | --- | --- |
| Display | Inter SemiBold | 48 | 3.0 | -1% | 1.06 | Project name on an empty screen; tile initials at 34 |
| Title | Inter SemiBold | 28 | 1.75 | -0.5% | 1.18 | Moment subject, reader title, screen chip, `Projects`, lost plate |
| Body | Inter Regular | 18 | 1.125 | 0 | 1.40 | Reading prose and answer labels |
| Strong | Inter SemiBold | 18 | 1.125 | 0 | 1.30 | Emphasized names and primary capsule words |
| Label | Inter Medium | 15 | 0.9375 | +0.5% | 1.30 | Metadata lines, eyebrows, tile lines |
| Mono | JetBrains Mono Regular | 16 | 1.0 | 0 | 1.50 | Runtime commands, paths and output |

- **Cadence (`KitFlow`):** segments joined by `" · "` ([WORDS.md](WORDS.md)) lay out as separate
  labels positioned by character advance (spaces included) rather than glyph ink bounds. Line breaks
  occur only between segments with the separator dropped, so no line begins or ends with a dot; an
  oversized segment wraps within itself (up to five lines in the rail notice) before the flow
  resumes below (`KitRender.SeparatorsHold`).
- **Whole-word wrapping (`KitReading`):** labels never truncate where room exists. Project and task
  names wrap up to three balanced lines before cutting inside an outside-text run; trailing counts
  (such as `2 More`) drop onto a right-aligned line beneath when needed; paths and well commands
  break at spaces or after `/`, `?`, `&`, `=`, `,`, `;` or `:` (never inside a hyphenated word
  unless a single filename chip exceeds the measure), with wrapped well rows hanging two characters
  in (`KitRender.KitWordsHold`).

## Moments, reading surface and character figures

- **Moment card (`KitMoment`):** 368 dp wide in window glass (inset 10 dp), ordered top to bottom:
  20 dp white mark (ring with `?` or `!` for reported items, solid disc with `n0` glyph for host
  observations) beside the `Label` `n6` eyebrow (`Agent's Question`, `Observed on This Computer` or
  `The Agent Says`); `Title` 10 dp below (up to two lines); metadata in `Label` `n6` using `" · "`
  or runtime prose in `Body`; optional `KitWell` command 14 dp below (`Mono` `n7` on `n0`, radius
  12 dp, padding 12 by 14 dp, truncated after two lines with `{n} More Lines Not Shown` or `Cut ·
  {n} More Lines`); optional supporting line 12 dp below in `Body` `n6`; and answer capsules 16 dp
  below. Earlier moments appear as a single glass lip 28 dp narrower rising 10 dp above the card,
  with `{n} Earlier` (and `{n} Newer` when a question holds focus) at the eyebrow's right. In
  `KitComposer`'s footer strip, declared environment and runtime roles appear as `Host: This
  Computer` or `Host: Sandbox` and `Agent: {Role}` (`RuntimeFaces`), sharing equal halves when whole
  or stacking full-width so no label truncates; unchangeable chips render as plain `n5` facts
  without a button outline.
- **Reading surface (`KitReader`, `KitReading`):** 704 by 528 dp (44 by 33 degrees) with the
  question mark, `Body`-sized eyebrow, `Title`, and top-right 60 dp round close button (`Fold` with
  `GlazeIcon.Close`). Its `n0` well (inset 14 dp, 96 dp from top to 84 dp from bottom, radius 12 dp,
  padding 14 by 18 dp) lays commands by rows in `Mono` (comments `n5`, code `n7`), preserving
  indentation, showing tabs, line breaks and unprintable code points literally, hanging wrapped rows
  two characters in, and fading hidden edges over 24 dp without counting half-shown rows as read.
  `End of Command · ` plus the sandbox effect scrolls in after the final row. The fixed footer
  (12 dp from bottom) holds a 240 dp primary slot beside equal-width `Decline` and `Reply` capsules:
  the primary starts as locked `Read to End` with a left-to-right `LockedProgress` fill (`n7` at
  `14%` over `n1`) and becomes the `#474747` `Run` plate once the final row has shown. Lifting the
  lock plays the `Moment` sound cue.
- **Character figure (`KitFigureLook`, `KitFigure`, `KitFigureMeshes`):** floating porcelain ovoid
  companion (`Halcyonic/Kit Pearl` and `Halcyonic/Kit Figure Light`, GPU-instanced with
  `MaterialPropertyBlock`): inverted-teardrop torso (`0.132 m` by `0.156 m`) with saddle collar
  (`#141C26` interior bowl) and luminous waist belt (solid, or 14 dashes when `Unknown`, dimmed in
  `Stopped` and `Last Known`), two detached tapered wing arms (`0.032 m` by `0.111 m`, `0.011 m`
  gap), and floating dome head (`0.140 m` by `0.098 m`, `0.012 m` gap) with recessed `#030406` visor
  (`#C9D2DC` chamfer lip) and tilted scanline LED eyes (upward arcs when host checks passed). Shell
  shades from `#3D4552` to `#DCE3EC`..`#F8FAFD` across six `FigureFamily` identity hues (`#4F7FF2`,
  `#3BE6D4`, `#9B75EB`, `#F75CA8`, `#CA52D6`, `#393DF2`) and state tones (`Running` `#59CFF1`,
  `Needs You` `#FABA59`, `Passed` `#69D98D`, `Failed` `#F7857D`, `Not Started` / `Unknown` `#A0A5AC`
  with `#B9C0C8` belt, `Stopped` `#7A828E`, `Last Known` `#767B82` static). A 3-layer floor
  projection sits `0.095 m` below (`#05070A` AO shadow, concentric rings with cardinal ticks,
  bounded inner specular reflection; hidden in `Last Known`). Triangle budgets stay under `2,400`
  near (`2,178` open, `2,202` happy) and `600` far (`502`).

## Press guard, decision bar, sound and motion

- **Single settled press (`PressGuard`, `KitPress`):** every headset action is a single pinch acting
  on release over the target. A press counts only if the pinch began after the button settled (its
  command, label, position or visibility last changed) and, for a ray, after `0.35 s` of ray rest;
  hover lights only once the button accepts a press, and a refused pinch does not depress the plate.
  Ray dropouts under `0.1 s` preserve rest; sliding off before release or dragging past `4` degrees
  of ray turn or `2 cm` of finger travel cancels the press. Held controls (`Hold to Talk`) start on
  a counted pinch and end on release. Where command policy requires review, the review card's single
  `Confirm` is that press.
- **Decision bar (`KitDecisionBar`):** approvals in the slate footer occupy two fixed half-width
  slots 12 mm apart: way back on the left (`Deny`, then `Cancel`), way forward on the right
  (`Approve`, then `Confirm` under `Approve Request?` or `Deny Request?`). `Confirm` stays locked
  until the full request and notes have been read and for `0.7 s` after (`ConfirmLockSeconds`). A
  press while locked is refused rather than queued, and once the lock lifts any pinch already under
  way is dropped by the `0.35 s` settle guard.
- **Sound (`KitSounds`, `KitSoundPlayer`):** ten procedural cues synthesized in code in A major
  pentatonic without audio assets: `Tick` (3.2 kHz tap), `Press` (2.4 kHz tap + A6 bell), `Moment`
  (E6, A6), `Question` (C#6, E6, A6), `Sent` (A6, E6), `Open` and `Fold` (noise sweeps), `Passed`
  (A5, C#6, E6, A6 60 ms apart), `Failed` (low-passed triangle E5, B4) and `Lost` (A3 to E3 glide),
  from `-38` to `-18 dBFS`. Cues play spatially from their element without looping, only while
  focused, and never convey state without visual words and glyphs.
- **Motion (`KitMomentTimeline`, `KitMomentChoreography`):** event-driven only; `Keep Still`
  replaces motion with 120 ms opacity fades and stops all loops. Moment arrival runs over ~700 ms
  (2 dp light line from character at 60 ms, spring scale from 0.86 at 160 ms, staggered 6 dp part
  rises from 300 ms, character turn at 700 ms) and retires over 260 ms. Answering fades unchosen
  options over 120 ms, shrinks the card into its unlit receipt (`Sent` over `Waiting for the agent
  to start.` or `Declined` on separate lines, never `"Started"`) at 200 ms with the `Sent` cue, and
  retires at 1600 ms.

| Event | Duration | Curve |
| --- | --- | --- |
| Moment emerges from character | 500 ms | Spring (bounce `0.15`) |
| `Read Command` unfolds into reading surface | 500 ms | Material 3 emphasized decelerate `(0.05, 0.7, 0.1, 1)` |
| `Fold` returns reading surface | 250 ms | Material 3 emphasized accelerate `(0.3, 0, 0.8, 0.15)` |
| Tile expands into screen; region moves | 600 ms | Material 3 standard `(0.2, 0, 0, 1)` |
| Pointing at a tile | Immediate | `+5%` scale, lit edge, controller tick |
| Idle character breath | 3.2 s | Few millimetres vertical float; single hop on ready |

## Coated reading material

Spatial reading objects in `WorkspaceRender` (`KitWorkView`, `KitOverviewView`) use a continuous
opaque coated face with a smooth analytic studio reflection (`<= 0.025` linear-luminance lift), an
inward `0.006`-unit edge (`~2.8 mm` at `0.46 m`) with a `0.001`-unit bevel inside the footprint, and
no passthrough transmission, backdrop blur or periodic texture
([instrument-material-study.md](../validation/instrument-material-study.md)). Selection lifts by
`6%` white inside the selected frame, holding `>= 4.5:1` ink contrast across all palettes and
reading sizes.

## Plane fallback and workspace controls

Fallback `Glaze.Menu` and `PlaneComposition` views ([ADR 0026](../decisions/0026-the-headset-interface-is-a-game-menu-on-one-plane-facing-the-eyes.md);
layout mechanics in [XR_CLIENT.md](../architecture/XR_CLIENT.md)) hold the following invariants:

- **One plane facing the eyes:** flat, square to the line of sight within `0.5` degrees at `0.46 m`
  (`PlaneComposition.Distance`), never rolled; parts separated by `1` degree (`PartGapDegrees`),
  columns `15 mm` apart (`ColumnGapMeters`; menu `32` degrees wide, file `36`..`38`, side panel
  `26`), at most two columns visible at once, inside the `96` by `90`-degree Quest 3S field (`4`
  rows per list page at Standard, `3` at Larger; `MenuFrame.RowsAPage`).
- **Grid and surface (`Surface.DrawGlass`, `GlazePalette`):** 8 dp (`0.5`-degree) grid step, 24 dp
  inner padding, 16 dp group gap, 8 dp label-to-value gap, 24 dp icon column, `0.9`-degree corner
  radius; panel colour at `96%` opacity with a `12%` white `0.06`-degree hairline (`Neutral`
  default: well `#1D1D1D`, panel `#242424`, raised `#2E2E2E`, control `#3A3A3A`, hover `#494949`,
  pressed `#595959`, text `#F2F2F2`, secondary `#C4C4C4`, disabled `#A0A0A0`, outline `#8A8A8A`;
  plus `Glaze` and `Graphite`, [theme-settings.md](../validation/theme-settings.md)).
- **Selection, rows and icons:** chosen adds `10%` white fill and `78%` white frame; pointed adds
  `42%` white frame alone (`GlazeChecks.OneSelectionTreatment`). Rows are 48 dp tall, 12 mm apart,
  and navigate rather than execute commands; footer actions sit in `PromptSlot` at 60 dp height and
  12 mm spacing. Icons come from the bundled Material Symbols Rounded subset
  ([material-symbols.md](../validation/material-symbols.md)), always paired with words and never
  vendor or language logos.
- **Workspace recovery and native wait:** reading content stays fixed through small head turns and
  recovers smoothly to the resting wearer after leaving view
  ([ADR 0032](../decisions/0032-recover-the-workspace-when-it-leaves-view.md)), suspending task and
  frame input while moving or settling and preserving unsent drafts. In the native Android panel,
  pending restoration shimmers over its connection-check text (`1.6 s` sweep, steady highlight under
  `Keep Still`) and stops immediately on completion, error or focus loss
  ([native-agent-panel.md](../validation/native-agent-panel.md)).

## Automated render checks

| Rule | Checked by |
| --- | --- |
| Zero hue in shipped chrome | `KitRender.ChromeTakesNoHue` |
| Static font atlases, glyphs, fallbacks and licences | `KitFontAtlas.Check`, `InstrumentFontAtlas.Check` |
| Separator cadence and uncut kit words | `KitRender.SeparatorsHold`, `KitRender.KitWordsHold` |
| One plane facing the eyes, columns aligned | `GlazeChecks.OnePlane` |
| Apparent text floor (`>= 14 dp`) | `GlazeChecks.TextAsSeen`, `SpatialKitTests` |
| Descending type hierarchy | `GlazeChecks.TypeStepsDown` |
| Single selection treatment | `GlazeChecks.OneSelectionTreatment` |
| Quest 3S field containment | `FieldChecks.Inside` |
| Contrast over white and bright passthrough | `GlazeChecks.Over`, `Glaze.Menu.GlassOverWhite`, `SpatialKitTests` |
| Calm glass, footer width and light-line routing | `GlazeRender`, `DirectionsRender` |
