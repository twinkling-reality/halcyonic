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

## Patterns these share

1. A content surface holds content; persistent controls sit in one attached bar.
2. A second piece of content opens in a second surface beside the first, at a meaningful moment,
   and only one or two.
3. Going deeper either replaces the surface in place, with a way back, or brings one modal card to
   the front while the rest dims.
4. Small labels and actions attach to the object they are about and face the person.
5. Navigation strips are few and short; actions are not navigation.
6. What a person acts on is near the hands; what a person reads for long is at least 0.5 m away.

## Consequences

- Lane V proposes three directions for the owner, each rendered at the same five moments by
  `DirectionsRender` (lane-v-redesign): a card and its shelf with answers beside it (patterns 1, 2
  and 4), a story ending in what the task needs with a composer under it (the agent tools' thread),
  and one step at a time in a smaller panel (pattern 3). The owner chooses; the chosen direction
  gets an ADR superseding the parts of ADR 0023 it replaces.
- Reading at 0.46 m sits under Apple's 1 m for sustained reading and Meta's 0.5 m for content
  looked at for long, while inside Meta's touch range. Whether long answers read comfortably there,
  or need a surface further away that the hand ray reaches, is a headset check.
