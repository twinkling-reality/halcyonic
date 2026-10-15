# Spatial title type

Date: 2026-10-15. Candidate visual correction after the owner rejected the installed stacked menu.

Rajdhani Medium was fetched from the official Google Fonts repository's `ofl/rajdhani` directory,
whose Contents API identified the font and license. Source:
<https://github.com/google/fonts/tree/main/ofl/rajdhani>. Copyright: 2014 Indian Type Foundry;
SIL Open Font License 1.1. The exact font SHA-256 is
`12ff7dcfe4c206e3875ac53b1762eab57de6a2fa7f5a86c26b97b88d6591eac2`;
the license SHA-256 is `f62ef357d3a1c3d27edd35a6e1ba350e8a8d13499797964eeadefbf0b3b15d1f`.
The source license remains alongside the font, with a byte-identical copy in StreamingAssets for
APK distribution. The atlas check compares those copies; final APK inclusion is a packaging gate.

The installed TMP API, already consumed by the icon atlas builder, generates a static 1024-square
SDF atlas at 72-pixel sampling with nine-pixel padding. Required ASCII and UI punctuation are
checked before saving. The existing reading font is its fallback; the title font never becomes
the global default or an icon fallback. Display, Subject and Hero consume it; body type stays in
the existing reading face. Literal-text handling remains in GlazeText. The title cache resets with
other static presentation caches. Hero is 34 dp and remains subject to actual perceived-size and
field checks; this is not permission to shrink surrounding text or targets.

The first actual Unity generation saved the atlas and passed glyph, static-source, fallback and
production Hero consumer assertions. Unity then aborted during shutdown (exit 134), so this is
content-generation evidence, not a successful process check. The final composition run must verify
the saved atlas, both text sizes and actual full-context appearance. No headset acceptance or
complete redesign is claimed by this foundation change.

The subsequent complete first-composition fixture validated the saved atlas at both text sizes and
exited cleanly with code 0. Its appearance was rejected during visual review; passing geometry and
font checks does not establish design acceptance. A later narrow named-identity check preserves
literal markup/backslash handling and outside ranges while checking upright identity → italic prose
→ upright identity reuse against actual mesh vertices. That new check awaits its next Unity run.
