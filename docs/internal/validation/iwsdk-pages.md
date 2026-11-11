# Pages of text and pictures in the VR starter

- **Question:** what can the starter's stack (`@iwsdk/core` 1.0.1, `@pmndrs/uikit` 1.0.78, `three` as `super-three`
  0.181.0) show for an app whose subject is its pages (a lesson, cards, steps, slides), and what is the smallest way
  to bring such an app into VR honestly (VRIFY unit 2)?
- **Date:** 2026-11-11.
- **Method:**
  - The SDK's own current documentation (iwsdk.dev, its "Spatial UI" pages, labelled v1.1.0-rc.1, one minor version
    ahead of the pin) and three.js's.
  - The pinned packages' own type definitions, which are the truth for these versions.
  - The starter's own code. No headset.
- **Status:** read from the documentation and the pinned packages; the starter's piece is type-checked. A run on a
  real app is the next step.

## What the stack offers

- **Native 3D UI, not web pages:** UIKitML, the starter's panel format, has HTML-like elements: `div`, `span`,
  `button`, `img`, `svg`, `video`, `input`, `textarea`, and Horizon panels and buttons (iwsdk.dev,
  `concepts/spatial-ui/uikitml`). Its text is MSDF, sharp at any distance. A custom TTF font comes in by `@font-face`
  with URLs relative to the UIKitML file. The default font, and characters outside basic Latin, are not stated.
- **No way to show a page's own HTML in VR:** the documentation names "HTML to canvas to texture" as the approach it
  chose against, for native 3D UI (`concepts/spatial-ui/`). `ScreenSpace` is a browser-only overlay outside XR. So
  an app's pages come in as content rebuilt in 3D, not as its HTML.
- **Changing a panel from code:**
  - every uikit component has `setProperties` (`component.d.ts`);
  - `Text` takes `text`;
  - `Image` takes `src` as a URL **or a three.js `Texture`** (`image.d.ts`), so a canvas texture can be a panel's
    picture;
  - the starter's own `panel.ts` hides elements with `setProperties({ display: 'none' })`.
- **Long text:** uikit's containers take `overflow: 'scroll'` (`content.d.ts`), scrolled by pointer drags
  (`scroll.js`'s pointer handlers). Whether a hand or controller drags it well on the headset: not tried.
- **Canvas textures:** three.js's `CanvasTexture` makes a texture from a canvas, and `needsUpdate` tells it to
  upload again after a redraw (threejs.org, `CanvasTexture`, `Texture`). The starter's score sign already draws its
  words this way.
- **Readable size:** 100 uikit units are one metre. The documentation says to judge readability by angular size,
  not pixels, and to check on a headset; it gives no distance.

## The starter's piece: `pages()` (`starters/iwsdk-vr/template/src/pages.ts`)

- **What:** each of the original's own cards (title, words, optional picture) is drawn on a canvas texture of its own,
  1600 pixels a metre. Each is a 0.64 by 0.8 m panel in a shallow arc 1.6 m in front of the person, at reading height,
  facing them.
- **The press answers:** `show(i)`, called from `onSelect`, brings that page forward 32 cm and grows it by 18 percent,
  dimming the others, on the game's own clock (`ambientMotion`, `gameTime`), so a check steps it like any motion.
- **Why canvas, not UIKitML:**
  - canvas is the way the starter's own sign already draws, and a rebuilt card's words and picture are fixed;
  - a canvas page needs no UIKitML document per card and no runtime layout pass.
  - The cost is the browser's own fonts, not MSDF: sharp at 1600 px a metre and at 1.6 m, by design, not yet seen on
    the headset.
- **The agent is told** in the starter's `AGENTS.md` when an app's subject is its pages, to read the cards' own words
  from the original's files (never the share image), and how to press one.

## Not verified

- The pages on the headset: legibility at 1.6 m, Quest Browser's own fonts on the canvas.
- A real run of Make VR Version on a pages app with the new piece: VRIFY's next step, with a local model.
- Scrolling a long uikit text with a controller or hand.
