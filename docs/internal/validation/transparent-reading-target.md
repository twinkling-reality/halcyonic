# Transparent reading target — 2026-10-15

Question: do the actual Projects, selected-project task list and task file preserve opaque reading
pixels when the editor target starts transparent, rather than inherit opacity from its clear colour?

## Verified scope and method

Unity 6000.3.25f1, the Android-target editor, Linear colour space, production MenuDirector and
materials, source production 1e02c118 with editor-only fixture repairs through warm 016b7d94.
`WorkspaceRender.TransparentReadingCheck` creates explicit synthetic session data, follows the
actual Projects selection and opens the real file. At Standard and Larger it settles opening,
clears an ARGB32 linear target to RGBA zero, and reads the rendered RGBA pixels. It samples 81
interior face points and four exterior corners. The actual title's world renderer bounds supply
its raster footprint; title-core contrast uses brightest and darkest opaque pixels in that region,
not the mean of glyph strokes and empty space. It is a sampled title-core test, not an audit of
every label, metadata line or anti-aliased fringe.

The shader emits premultiplied colour with `Blend One OneMinusSrcAlpha`. The diagnostic composites
that target once over a known synthetic checker background in linear space, then converts the
image to sRGB. No camera image or private content is used. As a negative control, it fades the
actual production face to 0.15 while preserving overlaid text, and requires the same alpha check
to reject it. The face is restored after the control.

## Result

Corrected gate exited 0. All six views had minimum interior alpha 1.0000, maximum exterior alpha 0.0000
and title-core contrast 6.61–8.82, above 4.5. All six negative controls produced minimum alpha 0.1490
and were rejected. Direct inspection of the synthetic composites showed the checker outside the
face and no checker transmission through its interior. The first run exposed a fixture error:
TMP mesh-local bounds were treated as world bounds, yielding no title samples. The corrected
run uses world renderer bounds; it did not relax the contrast or alpha requirements.

## Platform semantics and limits

[Meta's Passthrough Windows documentation](https://developers.meta.com/vr/documentation/unity/unity-customize-passthrough-passthrough-windows/),
updated December 9, 2025 and checked October 15, 2026, describes framebuffer alpha 0 as revealing
passthrough and intermediate alpha as blending virtual content with passthrough. Its current
multiplicative mask shader no longer needs the historical runtime non-premultiplied-alpha switch.
That guidance describes its mask technique; it does not establish the cause of any particular
application capture or justify changing Halcyonic's blending mode.

This proves the sampled host-rendered target properties only. It does not reproduce the reported
headset washout, establish submitted device eye-buffer alpha, classify system focus/compositor
behaviour, or establish wearer readability and design acceptance. Those remain unresolved. A
controlled device comparison of the actual face and a plain opaque reference, with contemporaneous
focus/layer metadata, is the next discriminant; no speculative production shader change was made.

## Deliberate development device comparison

The development-only Reading visibility check uses the normal workspace's retained local overlay,
without a replacement camera or renderer. Its three labeled, equally sized/deep patches show the
production coated material, an independent black/white opaque shader reference, and a deliberate
translucent negative. The negative uses Meta's documented multiplicative framebuffer mask so an
opaque reading face underneath does not incorrectly leave its resulting alpha at 1. Runtime text
states shader intent; it does not report measured submitted eye-buffer alpha. Focus and eye-target
size flags are metadata, not a cause assignment. No model, transport command, room capture or
camera readback is introduced.

Its shader references are explicit in transient development build scenes, using the same
`BuildOptions.Development` boundary as the existing builder; runtime types compile only in the
editor or development builds. No shader enters Resources or Always Included. Unity 6.3's
[IProcessSceneWithReport documentation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Build.IProcessSceneWithReport.html),
checked October 15, 2026, confirms the per-build-scene callback, and warns that unchanged cached
scenes can skip it. Source guards check the boundary and absence of fallback shader lookup or
readback. Final release artifact exclusion remains unproven until its actual build is inspected;
it is not inferred from unused-shader stripping. Host patch RGBA, entry/return retention, production
packaging and device comparison must each be validated separately.
