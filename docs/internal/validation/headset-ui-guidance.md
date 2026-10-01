# Headset interface guidance

- **Question:** What does Meta's current guidance say about hand targets, type, distance, contrast,
  feedback, motion, icons and system windows for a seated Quest 3 app, and can Meta's Interaction
  SDK UI Set, or an open icon set, be used in this Unity project and repository?
- **Date:** 2026-10-01.
- **Method:** Meta's design and Unity documentation (developers.meta.com/horizon, which now
  redirects to the same paths under /vr/), the Spatial SDK reference, Meta's licence page, the
  Unity Asset Store listing and the icon sets' GitHub repositories, read on the date above, each
  page's own date noted; the Interaction SDK 207.0.0 package in the project's package cache, read
  without changes. No headset.
- **Status:** Documentation and package contents. Nothing here is verified on a headset.

## Findings

### Hands, targets and distance

- Minimum hit target 48 dp at panel scale, with a smaller visual inside it padded by 4 to 6 dp
  ([hands UI best practices](https://developers.meta.com/horizon/design/hands-ui-best-practices/),
  2026-09-24). Primary controls at least 60 × 60 dp for hand tracking
  ([hit targets](https://developers.meta.com/horizon/design/styles_inputs_hit_targets/),
  2026-04-19), a requirement on the
  [Android design requirements](https://developers.meta.com/horizon/documentation/android-apps/design-requirements)
  page (2026-09-03).
- Accessibility minimum 22 × 22 mm, given as 48 × 48 dp or 3° of view at 0.42 m, with visuals at
  least 32 × 32 dp ([accessibility](https://developers.meta.com/horizon/design/accessibility/),
  2026-04-06). From that, 1 dp is about 0.0625° (inference). The Spatial SDK's default of 500 dp
  per meter gives a different physical size; Meta does not reconcile the two.
- Colliders of about 2.5° to 3° for poke and ray, and at least 12 mm between targets (hands UI best
  practices; [touch best practices](https://developers.meta.com/horizon/design/touch_bp/),
  2026-09-14).
- Touch panels 42 to 46 cm away, frequently used controls in the lower half, and the person may
  reposition them; ray panels comfortable at 0.8 to 3 m; avoid 0.5 to 0.8 m and show which mode is
  active (hands UI best practices). Content looked at for a long time at least 0.5 m away; about
  1 m suits menus ([display](https://developers.meta.com/horizon/design/display/), 2026-03-11).
  The two pages conflict for a panel that is both touched and read.
- Poke: hover from 0.15 m to 0.20 m, a 5 mm contact sphere, release after 2 mm of pull-back
  ([touch specs](https://developers.meta.com/horizon/design/touch_specs/), 2026-05-07). Ray: hover
  0.3 s, press 0.08 s, release 0.1 s ([raycasting specs](https://developers.meta.com/horizon/design/raycasting_specs/),
  2026-05-06); long press after 0.5 s (touch best practices). Default button travel 7 mm, with the
  hand stopped at the surface (hands UI best practices).
- Hands have no haptics; every successful selection gets a short sound; hover has no sound (hands
  UI best practices; raycasting specs).

### Type, colour and contrast

- Text at least 14, comfortably 18 or more; Body1 14/20, H3 20/24, H2 24/28 and H1 32/36 dp;
  regular body, bold titles, no italics
  ([typography](https://developers.meta.com/horizon/design/styles_typography/), 2026-04-19;
  [fonts and icons](https://developers.meta.com/horizon/design/fonts-icons/), 2026-03-02). The
  two pages name different typefaces (Inter, Optimistic Display).
- Contrast 4.5:1 for text, 3:1 for large text and for non-text UI
  ([color](https://developers.meta.com/horizon/design/styles_color/), 2026-04-19; accessibility).
- A Quest 3 shows 25 pixels per degree, 18 through passthrough
  ([compare devices](https://developers.meta.com/horizon/essentials/compare-devices/), 2026-09-30).
  Its LCD cannot separate values below 13/255 sRGB; dark backgrounds no darker than #1A1A1A, light
  ones no brighter than #DADADA (display; color).
- Semantic colour tokens rather than hex; never colour alone; test with the headset's
  colour-correction modes; bright walls in passthrough wash out text, so UI always sits on a
  backplate (color; typography; [UI Set](https://developers.meta.com/horizon/documentation/unity/unity-isdk-uiset/)).

### Placement, motion and system windows

- Minimise head-locked content; panels turn to face the person without roll; keep main controls on
  one panel; window control bars sit below a panel's bottom centre
  ([MR design guidelines](https://developers.meta.com/horizon/design/mr-design-guideline/),
  2025-10-07; [hands 3D](https://developers.meta.com/horizon/design/hands-3d-best-practices/),
  2026-09-14; [comfort](https://developers.meta.com/horizon/design/comfort/), 2025-12-17;
  [panels](https://developers.meta.com/horizon/design/panels/), 2026-03-02).
- No general animation timings; fade in large objects and avoid surprise pop-ups
  ([MR health: depth](https://developers.meta.com/horizon/design/mr-health-depth/), 2026-03-11).
  The UI Set's own transitions take 0.1 s (package cache).
- System overlays render within about 2 m; when focus is lost, hide input visuals and in-app menus
  closer than that ([focus awareness](https://developers.meta.com/horizon/documentation/unity/unity-focus-awareness/),
  2024-12-20). System dialogs draw over all app UI.
- Found in no page: text size in degrees or dmm, panel curvature, vertical placement in degrees,
  field-of-view zone sizes, a spacing grid, where system windows open.

### Icons

- Filled icons for immersive apps, never mixed with outlined, on a 24 dp grid
  ([icons and images](https://developers.meta.com/horizon/design/styles_icons_images/),
  2026-04-19; fonts and icons).
- Meta's icons ship in the Interaction SDK as bitmap atlases (`OCUIIconSet`: 369 filled and 318
  outlined sprites, 1024 × 1024) and in the Spatial SDK, under the Meta SDK licence; no openly
  licensed release was found.
- [Material Symbols](https://github.com/google/material-design-icons): Apache-2.0, SVG and font
  sources, a fill axis. [Lucide](https://github.com/lucide-icons/lucide): ISC, plus MIT for the
  icons that came from Feather; outline only. [Phosphor](https://github.com/phosphor-icons/core):
  MIT, six weights including Fill.

### The Interaction SDK UI Set

- 48 prefabs (buttons, toggles, sliders, dropdowns, dialogs, text input, tooltips) and four themes
  in Interaction SDK Essentials (`com.meta.xr.sdk.interaction` 207.0.0) at
  `Runtime/Sample/Objects/UISet`, since v69 (UI Set, 2025-03-18; package cache).
- Built on uGUI: each panel is a Canvas with `PokeInteractable`, `RayInteractable` and
  `PointableCanvas`, and the scene needs a `PointableCanvasModule` in its EventSystem
  ([canvas integration](https://developers.meta.com/horizon/documentation/unity/unity-isdk-canvas-integration/),
  2025-11-03).
- Licensed under the Meta Platforms Technologies SDK License Agreement, effective 2022-10-25
  ([licence](https://developers.meta.com/horizon/licenses/oculussdk/)): 1.2.1 allows no derivative
  works except sample code, 1.2.8 forbids making the materials subject to an open-source licence,
  1.3 requires Meta's notice on every copy. Re-theming the set means copying its theme assets into
  `Assets`.

## Consequences

- The headset's interface takes Meta's numbers (targets, gaps, type sizes, contrast, feedback,
  filled icons) and none of the UI Set's assets
  ([ADR 0023](../decisions/0023-the-headset-interface-is-one-system-of-tokens-and-components.md)).
- A touch panel at 0.46 m is inside Meta's touch range and under its 0.5 m comfort line; whether
  reading there is comfortable is a headset check.
- Today's panels (#0F141C) are darker than Meta's floor for dark backgrounds.
