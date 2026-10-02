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
  style, weight 500), keeps only the glyphs the client core's `GlazeIcon` names, renames it
  Material Symbols Rounded Glaze, and says in its name table that it was changed and how, as the
  Apache License asks of a changed file. It writes the font and the code point table
  `GlazeIconGlyphs`.
- With the states and marks alone (13 glyphs) it wrote 4,232 bytes, SHA-256
  `739638df95657882873174574dfb307e1f14c5233ce681063204d36031e5e624`; run again on the source
  above with fontTools 4.65.0 it made both files byte for byte as committed. With the actions
  added (39 meanings, 38 glyphs, since Stop and Stopped share one) it writes 9,352 bytes, SHA-256
  `3ca655c43ef0bd90510f2455490563479a09be19d41ca6b721afd06149272570`.
- Every glyph is drawn inside its 960 unit em, but add_task, which reaches 6 units past its right
  edge, and the font's line runs from -96 to 1,056, so the line's middle is the em's: an icon
  centred on its label turns in place.

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
| Approve | check | U+E5CA |
| Deny | block | U+E033 |
| Stop | stop_circle | U+EF71 |
| Tell it | chat | U+E0B7 |
| Send answer | send | U+E163 |
| Hold to talk | mic | U+E029 |
| Type | keyboard | U+E312 |
| Start building | play_arrow | U+E037 |
| Start over | restart_alt | U+F053 |
| Refresh, Try again | refresh | U+E5D5 |
| Change | edit | U+E150 |
| Connect projects | link | U+E157 |
| Create a project | add | U+E145 |
| Add a task | add_task | U+F23A |
| Open now | open_in_full | U+F1CE |
| Keep creating | edit_note | U+E745 |
| Not now | schedule | U+E192 |
| Close, Cancel | close | U+E14C |
| Back | arrow_back | U+E5C4 |
| Next | arrow_forward | U+E5C8 |
| Move | drag_pan | U+F71E |
| Reset position | center_focus_strong | U+E3B4 |
| Show all | visibility | U+E417 |
| Settings | settings | U+E8B8 |
| Usage left | speed | U+E9E4 |
| A locked final press | lock | U+E88D |
| Code (a file's kind) | code | U+E86F |
| Database | database | U+F20E |
| Data | data_object | U+EAD3 |
| Writing, and a file of unknown kind | description | U+E873 |
| Image | image | U+E251 |
| Script | terminal | U+EB8E |
| Package | deployed_code | U+F720 |
| Folder | folder | U+E2C7 |
| A line that opens more | chevron_right | U+E409 |

### The atlas

- `GlazeIconAtlas` draws the glyphs into a static TextMeshPro SDF atlas, sampled at 56 with a
  padding of 7, which keeps no reference to the font and has no fallback: the 13 in 256 by 256
  pixels, the 38 in 512 by 512, and since 2026-10-02 the 47, with ADR 0026's file kinds and the
  chevron, in 512 by 512 too.
- Built again over the committed atlas, it is the same in every glyph, pixel and material setting;
  only the identifiers Unity gives the texture and the material inside the asset change, and
  nothing outside the asset refers to them. Until this check, a build over an existing atlas left
  it named after the temporary file it was built in ("GlazeIcons building"), and, with new glyphs,
  failed its own check: the atlas it had loaded before copying the new one over kept its old lookup
  tables. The builder now names it after its own file and reads its tables again.
- The renders find every icon in the atlas, drawn from it alone, and no label of words drawing
  from it (`GlazeRender`, and every pass that checks labels literally).
