# The headset's icons from Material Symbols

- **Question:** Where do the headset's icons come from, under what licence, and can the committed
  icon font and atlas be made again from that source exactly?
- **Date:** 2026-10-02.
- **Environment:** macOS 26.7 on an Apple M5 Max; Python 3.14 with fontTools 4.65.0; Unity
  6000.3.25f1 in batch mode with the Android build target.
- **Method:** The source font, downloaded on 2026-10-01 with the owner's leave and kept outside the
  repository, hashed and read with fontTools; `apps/xr/tools/glaze_icons.py` run on it in a scratch
  copy of its folders and its output compared byte for byte with the committed font and table; the
  atlas built again in Unity (`GlazeIconAtlas.Check`) over the committed one and compared with it;
  the renders run with the icons in place. No headset.
- **Status:** The source and the font's making are verified. The commit and the licence are as the
  first download recorded them (below); the icons on a headset are not seen yet.

## Findings

### The source

- google/material-design-icons at commit `bd8cb85`, release 2.972, the file
  `variablefont/MaterialSymbolsRounded[FILL,GRAD,opsz,wght].ttf`: 15,214,320 bytes, SHA-256
  `c2182b6337495e64cc9e2311c52522567ac277d25a842bd027f9c9e3a5cc6d86`. The commit is as the first
  download recorded it; the hash and everything else here are read from the file.
- Its name table says Material Symbols Rounded, Version 2.972, Copyright 2026 Google LLC, and names
  no licence; the repository licenses its icons under the Apache License 2.0
  ([headset-ui-guidance.md](headset-ui-guidance.md)), whose text is committed beside the font
  (`apps/xr/Assets/Halcyonic/UI/Icons/LICENSE.txt`). NOTICE names it.
- Four axes: FILL 0 to 1, GRAD -50 to 200, opsz 20 to 48, wght 100 to 700; 6,646 glyphs.

### The font Halcyonic makes from it

- `glaze_icons.py` pins the font at FILL 1, wght 500, GRAD 0 and opsz 24 (ADR 0023's filled
  style, weight 500), keeps only the 13 glyphs the client core's `GlazeIcon` names, renames it
  Material Symbols Rounded Glaze, and says in its name table that it was changed and how, as the
  Apache License asks of a changed file. It writes the font, 4,232 bytes, SHA-256
  `739638df95657882873174574dfb307e1f14c5233ce681063204d36031e5e624`, and the code point table
  `GlazeIconGlyphs`.
- Run again on the source above with fontTools 4.65.0, it makes both files byte for byte as
  committed.
- Every glyph is drawn inside its 960 unit em, and the font's line runs from -96 to 1,056, so the
  line's middle is the em's: an icon centred on its label turns in place.

| Meaning | Glyph | Code point |
| --- | --- | --- |
| Not started | radio_button_unchecked | U+E836 |
| Starting | hourglass_top | U+EA5B |
| Working | progress_activity | U+E9D0 |
| Checking its work | fact_check | U+F0C5 |
| Waiting for you | front_hand | U+E769 |
| Finished this round | flag | U+E153 |
| Checks failed | assignment_late | U+E85F |
| Couldn't finish | cancel | U+E5C9 |
| Stopped | stop_circle | U+EF71 |
| Can't tell yet | help | U+E887 |
| Last known | history | U+E28E |
| Practice | science | U+EA4B |
| Demo and Recorded | movie | U+E02C |

### The atlas

- `GlazeIconAtlas` draws the 13 glyphs into a static TextMeshPro SDF atlas of 256 by 256 pixels,
  sampled at 56 with a padding of 7, which keeps no reference to the font and has no fallback.
- Built again over the committed atlas, it is the same in every glyph, pixel and material setting;
  only the identifiers Unity gives the texture and the material inside the asset change, and
  nothing outside the asset refers to them. Until this check, a build over an existing atlas left
  it named after the temporary file it was built in ("GlazeIcons building"); the builder now names
  it after its own file.
- The renders find every icon in the atlas, drawn from it alone, and no label of words drawing
  from it (`GlazeRender`, and every pass that checks labels literally).
