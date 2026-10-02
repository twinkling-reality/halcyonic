# Headset redesign research

- **Question:** How do current spatial platforms and agent tools keep an interface simple when a
  person must see what an agent needs, answer it, understand what it did and start new work, and
  which of those patterns fit a seated, hands-first Quest app whose workspace today puts a title,
  tabs, a second row of controls, an answer, a pager and an action bar on one 44 by 26 degree panel?
- **Date:** 2026-10-02.
- **Method:** Apple's visionOS Human Interface Guidelines (read through their JSON data files),
  Apple's WWDC23 spatial design sessions and SwiftUI and RealityKit reference pages; Meta's Horizon
  OS design pages and a Meta blog post; Android XR's design guides and Jetpack XR reference; the
  official documentation of the Codex agent app and public issue reports about it; official guides
  of three spatial productivity apps; the owner's own reference design for a request field with an
  attached shelf, in a private project, read only. Each page's own date is noted where it shows
  one. Measurements of today's workspace come from lane W's `WorkspaceRender` on lane-w-understand
  (fbdc5da). Prototype renders of three directions come from `DirectionsRender`, an editor-only
  render on lane-v-redesign. No headset.
- **Status:** Documentation, public reports and editor renders. Nothing here is verified on a
  headset.

## Findings

### Today's workspace

- On the 44 by 26 degree panel the fixed rows take about 24 degrees: title and goal 3.9, gap 0.6,
  tabs 3.0, a 12 mm gap 1.4, a heading or pills row 3.0, gap 1.4, gap 1.4 under the body, pager
  band 3.0, gap 1.4, action bar 3.7, padding 1.2. With a row of pills and the pager band, about
  2 degrees remain for the answer: its source line and at most one row. Measured by lane W in
  `WorkspaceRender`, far placement, 2026-10-02.
- A panel 26 degrees tall fits under the labels of the characters 2.4 m away with about 0.2 degrees
  to spare (lane U, `WorkspacePlacement`), so any taller surface, or larger text, needs the floor or
  the characters moved.

### Apple visionOS

Sources: the HIG pages
[ornaments](https://developer.apple.com/design/human-interface-guidelines/ornaments) (change log
2024-02-02), [tab bars](https://developer.apple.com/design/human-interface-guidelines/tab-bars)
(2026-06-08), [toolbars](https://developer.apple.com/design/human-interface-guidelines/toolbars)
(2025-12-16), [windows](https://developer.apple.com/design/human-interface-guidelines/windows)
(2025-06-09), [layout](https://developer.apple.com/design/human-interface-guidelines/layout)
(2026-09-09), [sheets](https://developer.apple.com/design/human-interface-guidelines/sheets)
(2026-03-24), [spatial layout](https://developer.apple.com/design/human-interface-guidelines/spatial-layout)
(2024-03-29), [eyes](https://developer.apple.com/design/human-interface-guidelines/eyes)
(2024-06-10); WWDC23 [Design for spatial user interfaces](https://developer.apple.com/videos/play/wwdc2023/10076/)
and [Principles of spatial design](https://developer.apple.com/videos/play/wwdc2023/10072/);
SwiftUI [pushWindow](https://developer.apple.com/documentation/swiftui/pushwindowaction) and
RealityKit [attachments](https://developer.apple.com/documentation/realitykit/realityviewattachments), undated.

- An ornament holds a window's controls without crowding its content, on a plane slightly in front
  of the window, moving with it; toolbars and tab bars are ornaments. Keep it visible and no wider
  than its window. A WWDC23 session says a bottom ornament overlaps the window's edge by 20 pt.
- The layout page's 2026-09-09 update puts supplemental content in an adjacent window opened beside
  the first, not in an ornament; ornaments are for controls.
- Tab bars are for navigation, never actions, with few items (WWDC23: at most six). Toolbars hold
  at most three groups and swap their controls in a modal state.
- Open a new window only at a meaningful moment; too many windows overwhelm. A pushed window
  replaces the current one in place and closing it brings the original back. A sheet floats in
  front of its window, which dims, and only one shows at a time.
- SwiftUI views can be attached to 3D entities, and labels tied to a point face the viewer.
- Sustained reading is placed at least 1 m away; closer only briefly. Targets are 60 by 60 pt.
- Prefer familiar windows and the least immersion that works; save spatial treatment for a key
  moment.

### Meta Horizon OS

Sources: [panels](https://developers.meta.com/horizon/design/panels/) (2026-03-02),
[layouts](https://developers.meta.com/horizon/design/styles_layouts/) (2026-04-19),
[windows](https://developers.meta.com/horizon/design/windows/) (2026-02-27),
[hands UI best practices](https://developers.meta.com/horizon/design/hands-ui-best-practices/) (2026-09-24),
[MR design guidelines](https://developers.meta.com/horizon/design/mr-design-guideline/) (2025-10-07),
[display](https://developers.meta.com/horizon/design/display/) (2026-03-11),
[tooltips](https://developers.meta.com/horizon/design/tooltips/) (2026-03-02),
[dialogs](https://developers.meta.com/horizon/design/dialogs/) (2026-03-02),
[wrist buttons](https://developers.meta.com/horizon/design/wrist-buttons/) (2026-03-02), and the
[v67 window layout post](https://www.meta.com/blog/meta-quest-v67-update-new-window-layout-creator-content-horizon-feed/) (2024-07-03).
[headset-ui-guidance.md](headset-ui-guidance.md) already records targets, gaps, type, contrast and focus.

- Panels come single, hinged (two or three joined at their edges) or in theater view. A panel has
  no built-in interface; its control bar is a pill under it. The system allows three hinged windows
  and three free ones.
- Menus are not attached to a moving hand or wrist; one spawned from the wrist then stays still in
  the world, with primary actions in front of the person. Wrist buttons are deprecated.
- A dialog is temporary: one primary button and an optional secondary. A long press reveals a
  context menu without the main action.
- Touch panels at 42 to 46 cm, ray panels at 0.8 to 3 m, nothing from 0.5 to 0.8 m; frequently
  used controls in a panel's lower half. Content looked at for long at least 0.5 m away. The older
  MR page's 70 cm figure conflicts with the newer hands page.

### Android XR

Sources: [spatial UI](https://developer.android.com/design/ui/xr/guides/spatial-ui) (2026-03-31),
[foundations](https://developer.android.com/design/ui/xr/guides/foundations) (2026-01-16),
[Jetpack XR UI](https://developer.android.com/develop/xr/jetpack-xr-sdk/ui-compose) (2026-09-22).

- A spatial panel opens 1.75 m away, 5 degrees below eye level, with what matters in the central
  41 degrees.
- Orbiters are floating controls anchored to a panel (navigation, toolbars, playback), 20 dp
  from it, used sparingly. A dialog pushes its panel back 125 dp.
- Spatialise only key moments and keep familiar components.

### Agent tools

Sources: the Codex app's official pages, which on 2026-10-02 redirect from developers.openai.com
to learn.chatgpt.com: [models](https://developers.openai.com/codex/models),
[prompting](https://developers.openai.com/codex/prompting/),
[code review in the app](https://learn.chatgpt.com/docs/code-review?surface=app),
[approvals and security](https://learn.chatgpt.com/docs/agent-approvals-security), all undated;
issue reports [#31157](https://github.com/openai/codex/issues/31157),
[#39346](https://github.com/openai/codex/issues/39346), [#45471](https://github.com/openai/codex/issues/45471).

- The model and reasoning control and the permissions control sit beneath the composer. Queued
  follow-up messages sit above it, editable.
- A turn's edited files show as a card listing each file with its added and removed lines and a
  Review action, which opens a separate review pane. Reported in issues, not in the official pages.
- An approval shows as an actionable card on the desktop; whether it replaces the composer or sits
  in the thread is not documented.
- The owner's reference design, read only: a single request field resting in a lighter shelf of the
  same outline, which holds one-line rows of running work; a request that needs the person takes
  the field's place as one card with its figure, a title, one line and at most two actions, the rest
  behind a More control.

### Spatial productivity apps

Sources: ShapesXR's [shape tool](https://learn.shapesxr.com/objects-creation/shape-tool) and
[settings](https://learn.shapesxr.com/basics/settings) guides, undated; Gravity Sketch's
[quick menus](https://gravitysketch.com/blog-post/updates/quick-menus-and-onboarding-rooms-update/)
(2024-10-22); Apple's [Freeform on Vision Pro](https://support.apple.com/guide/apple-vision-pro/create-and-manage-freeform-boards-tan5281cfbb6/visionos) guide, undated.

- ShapesXR brings tools to the hand and shows properties only for the selected object; Gravity
  Sketch opens two small quick-menu grids from a wrist turn and hides other tools while one is open;
  Freeform keeps one window with a bottom toolbar and a sidebar.

### Game interfaces

Added the same day, after the owner asked for the organisation of a game's menus and detective case
files. Read through public articles, interviews and wikis (wikis labelled); no game image was copied.

- **Dead Space.** The suit carries the interface: health on its spine, ammo on the weapon, and no
  heads-up display, to remove the "wall of safety" a HUD puts between player and game
  ([Game Developer](https://www.gamedeveloper.com/design/video-designing-i-dead-space-i-s-immersive-user-interface),
  2013-06-12, on Dino Ignacio's GDC 2013 talk). Inventory and map are holograms projected into
  the world, and the game keeps running while they are open
  ([Game Informer](https://gameinformer.com/interview/2023/02/22/dead-spaces-new-and-original-creative-directors-reflect-on-the-remake),
  2023-02-22). An independent case study names the remake's low-contrast, blurred text as a
  weakness ([Quest for UX](https://questforux.beehiiv.com/p/game-ux-case-study-dead-space-remake-422b),
  2025-12-17). The holographic menus' internal layout is unverified.
- **Destiny 2.** A handful of named top-level pages (Clan, Collections, Journey, Character,
  Inventory; Bungie Help, seen in search only). The Director, a map of destinations, became hard
  for new players, so activities were grouped into categories
  ([Destructoid](https://www.destructoid.com/destiny-2s-new-portal-ui-could-help-new-players-make-sense-of-the-game/),
  2024-09-10); the two later merged, the map above and the categories along the bottom
  ([Destinypedia](https://www.destinypedia.com/Director), a wiki). Bungie tuned by hand how strongly
  each item holds the pointer ([Game Developer](https://www.gamedeveloper.com/art/video-building-the-user-interface-for-bungie-s-i-destiny-i-),
  2016-06-17, about Destiny 1). Nothing opened documents where Destiny's button prompts sit or a
  rule of one focus area: those are the common convention, not a cited lesson.
- **Detective case files.** Return of the Obra Dinn's logbook uses a contents page, chapters and
  bookmarks so that it teaches itself
  ([Game Developer](https://www.gamedeveloper.com/business/road-to-the-igf-lucas-pope-s-i-return-of-the-obra-dinn-i-),
  2019-03-15). Alan Wake 2's first free-form case board overwhelmed playtesters; the shipped board
  follows the story in order, and the world keeps running in the Mind Place
  ([Game Developer](https://www.gamedeveloper.com/design/true-detective-meets-hearthstone-unlocking-the-metaphysical-mind-place-of-alan-wake-ii),
  2023-12-06). L.A. Noire files each clue in the notebook as it is found
  ([Game Informer](https://gameinformer.com/games/la_noire/b/ps3/archive/2011/05/12/everything-you-need-to-know-about-l-a-noire.aspx),
  2011-05-12). Disco Elysium's journal keeps open and done tasks apart, with short titles
  ([Disco Elysium wiki](https://discoelysium.wiki.gg/wiki/Tasks), a wiki, edited 2026-09-20).
- **VR games.** Lone Echo's arm display fades in when the wrist nears a point about 45 cm in front
  of the face; a deliberate swipe opens a tablet whose list leads to a detail with a back arrow;
  text works best larger and bold, light on medium dark
  ([Road to VR](https://www.roadtovr.com/designing-lone-echo-echo-arena-virtual-touchscreen-interfaces-robert-duncan/2/),
  2018-03-16). Half-Life: Alyx shows health on the back of the glove (Combine OverWiki, a wiki);
  Valve made the arms invisible
  ([Game Informer](https://gameinformer.com/interview/2020/03/23/valve-talks-half-life-alyx-and-why-arms-dont-work-in-vr),
  2020-03-23). Red Matter 2 keeps information on the tools in the hand, with no floating UI
  ([Road to VR](https://roadtovr.com/these-clever-tools-make-vr-way-more-immersive-inside-xr-design/)).

### The chosen direction's style

Added the same day, after the owner chose D with changes, naming Detroit: Become Human's interface
as the reference for clean, intentional type and layout.

- **Detroit: Become Human**, seen on Interface In Game's gallery
  ([interfaceingame.com](https://interfaceingame.com/games/detroit-become-human/), read 2026-10-02,
  the screenshots only; the page describes no style and credits no designer). Its settings and
  credits screens use one large, light title; small grey labels in spaced capitals above each
  value in a larger face; hairlines and thin corner marks round the chosen item; one blue accent; a
  translucent panel over the scene; and a single prompt, a glyph with a short word, at the bottom
  right. No image was copied.
- **File-type icons.** Material Symbols Rounded is already the project's icon set, under the Apache
  License 2.0 ([material-symbols.md](material-symbols.md)). Its generic glyphs code, database,
  data_object, description, image, terminal, deployed_code, folder and chevron_right are in the same
  source file (SHA-256 `c2182b6337495e64cc9e2311c52522567ac277d25a842bd027f9c9e3a5cc6d86`, the file
  that record names); they were cut in the Glaze style (filled, weight 500, grade 0, optical size
  24) with fontTools 4.65.0 into a temporary font for the prototype renders only, never committed.
  Language and brand logos were left out, since the competition rules forbid brands.
- **Measured in the styled renders** (`DirectionsRender.Styled.cs`, editor, Android target): a task's
  file 53 to 54 draw calls and its side panel 25 to 28, the menu with Tasks open 57, whole scenes with
  six characters 93 to 123, against lane U's budgets of 60 a panel and 220 a scene, counted before
  batching as `MeasureRender` counts; the prototype draws a panel's glass as about eleven separate
  strokes, which the product's Surface shader would draw in one. Frame time on a Quest is not
  measured.

### Star Citizen's mobiGlas

Added on 2026-10-02 at the owner's request, as a reference for how several panels relate. Read the
same day; no image was copied and the videos the article embeds could not be played here.

- **The article** (Jono Yuen, *Star Citizen - UI Revisited (part 2)*, HUDS+GUIS, 2023-05-04,
  [hudsandguis.com](https://www.hudsandguis.com/home/star-citizen-revisited-part-2)) is mostly images.
  Its text calls mobiGlas the player's main interface for inventory, missions, messages, ship and
  navigation: a see-through hologram projected from the wrist, readable from many angles and by other
  players, which it praises as social. It reports the developer's aims for modular, reusable parts,
  and names no problem but the difficulty of interfaces every player can see.
- **The developer's own notes** (Dave Richard, *Design Notes: mobiGlas*, 2015-01-31,
  [robertsspaceindustries.com](https://robertsspaceindustries.com/en/comm-link/engineering/14466-Design-Notes-MobiGlas)):
  an augmented-reality layer and the wrist hologram run together; open, the hologram takes about 95
  percent of the view. It is built on a grid from simple shapes, with solid fills separating sections
  because the screens float, one blue base, pale text and a few accents, and the world behind it
  blurred and tinted so text stays readable. Home is a hub that adapts: apps for the situation,
  favourites, recent apps. Objects step from dim to bright to animated as they become selectable,
  then selected, and filters cap how many labels show at once. The notes name clutter, items too
  close together to read, and readability against the world as the problems to solve.
- **What the images show** (inference, from uncaptioned screenshots): one large panel at a time, tied
  to the forearm so it is often seen at a slant; app cards that keep their title top left and close
  top right while the content changes in the same frame; detail opening as a pane on the right of the
  same panel, its actions at the bottom right; side panes angled in toward a flat centre; and a footer
  of key prompts. Outline-only cards with glow wash out against bright scenes.
- **Borrowed:** list and detail on one plane, the detail sliding out to the right; a footer of key
  prompts; controls in fixed places; solid fills, one base and one accent; a home that adapts to what
  waits; and, for a heads-up mode, few labels, the focused one brightest.
- **Left behind:** reading surfaces tied to the wrist and seen at a slant, side panes angled against
  the centre, outline-only glass, and a panel that fills the view.

### A plane below the eyes, measured

Added the same day, after the owner asked for every surface on one plane facing the person, upright.
`DirectionsRender.Refined.cs` (editor, Android target) lays the menu and a task's file on one plane
two ways: upright, and tipped back as a whole to face the eyes at its centre. It renders each also
from the eyes looking at it, square to that line, as the headset draws what a person looks at.

- **The geometry.** Text on a surface spans less as the eyes see it the more slanted the surface is
  to the line of sight. On an upright plane, a line at an angle e below eye level spans its size at
  the plane's distance times the square of cos e. On a flat plane facing the eyes, text a degrees to
  the side of the centre loses the factor cos a, and b degrees above or below it the square of
  cos b.
- **Upright.** The menu and a file side by side, 64.5 by 32.9 degrees from 18 to 42 degrees below
  eye level, show 19 labels under Meta's 14 dp as the eyes see them, the prompts at 56 percent of
  their size. A file alone, 38 by 24 degrees from 17 to 36 below, shows 5, at 68 percent. Seen from
  the eyes, the plane narrows toward its bottom and its rows lean.
- **Facing the eyes.** The same menu and file, 64 by 32 degrees from 18 to 50 degrees below eye
  level along the plane's middle, keep at least 84 percent of their size, at the far corners, and no
  text reads under 14 dp; the file alone, 38 by 24 degrees from 17 to 40 below, keeps 94 percent.
  The bottom edge stands 0.48 and 0.47 m from the eyes. Prompts of 15 dp would read 0.78 degrees at
  the far corners, under 14 dp; at 18 dp they read 0.95.
- **Why the checks had passed.** `GlazeChecks.TextLargeEnough` measures text at a surface's
  perpendicular distance, which is exact only where the surface faces the eyes. The level renders
  keep their image plane upright, as architecture is photographed, so an upright plane stays a
  rectangle in them and its low parts look larger than the eyes see them.
- **A side panel sliding out.** The menu alone, facing the eyes, holds 32 by 32 degrees from 16.5 to
  48.6 below eye level. When the file slides out and the plane stays where the menu put it, the
  composition ends off square, the file's far text reads under 14 dp (its main prompt at 73
  percent), a corner falls past a Quest 3S's field, and the file's top comes within -0.3 degrees of a
  character's label. Re-centred as one plane, the same composition passes every check.
- **A Quest 3S's field, as the product checks it.** `FieldChecks` turns the head to the composition
  and tips it only by `WorkspacePlacement.ReadingPitch`, at most 8 degrees; the earlier renders had
  allowed 18. So checked, the menu and a file reach 41.8 degrees below the view's middle, of the 43.5
  the field holds less its margin, and 39.0 to the side, of 46.5; a file alone 37.7 and 23.8.

## Patterns these share

1. A content surface holds content; persistent controls sit in one attached bar.
2. A second piece of content opens in a second surface beside the first, at a meaningful moment,
   and only one or two.
3. Going deeper either replaces the surface in place, with a way back, or brings one modal card to
   the front while the rest dims.
4. Small labels and actions attach to the object they are about and face the person.
5. Navigation strips are few and short; actions are not navigation.
6. What a person acts on is near the hands; what a person reads for long is at least 0.5 m away.
7. A few sections named in the person's words, one on screen at a time, and every button in one
   fixed place, as game menus and case files are organised.
8. State lives on the object it describes, and a file opens out of that object; the world keeps
   running while it is open.

## Consequences

- The owner rejected the first round's three directions on 2026-10-02 (too many kinds of thing on
  one surface, buttons in many places) and asked for a game menu's organisation. Lane V's second
  round proposes D, a game menu: a task's file with four plainly named sections across its top, one
  section at a time, and every button in a footer; and E, a case file the task's character projects,
  an index of the sections with a line each beside one page. Both are rendered by
  `DirectionsRender` beside the first round's A.
- The first round's directions were a card and its shelf with answers beside it (patterns 1, 2
  and 4), a story ending in what the task needs with a composer under it (the agent tools' thread),
  and one step at a time in a smaller panel (pattern 3). Whichever direction the owner chooses gets
  an ADR superseding the parts of ADR 0023 it replaces.
- Reading at 0.46 m sits under Apple's 1 m for sustained reading and Meta's 0.5 m for content
  looked at for long, while inside Meta's touch range. Whether long answers read comfortably there,
  or need a surface further away that the hand ray reaches, is a headset check.
- The owner chose D, then asked for its surfaces on one upright plane, single-word places, sections
  as separate shapes and type that only steps down. Measured, the upright plane shrank its low text
  under 14 dp, and the coordinator chose one plane facing the eyes at its centre instead. ADR 0026,
  accepted by the owner on 2026-10-02 with the split header, records D's structure and these rules.
