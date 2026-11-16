# Brand

Canonical: the name people see, its mark, colours, type and ground, where the product shows itself
outside the headset's panels (an app icon, a page's tab, the film's cards). The headset's panels
follow [STYLE.md](STYLE.md); their words follow [WORDS.md](WORDS.md). The owner approved the mark and
its board on 2026-11-16.

## The name

The name people see is **Halora**: halcyon, the kingfisher of the calm-sea legend, and aurora, the
dawn. Code, packages, folders and the repository keep the working name `halcyonic`, as
[PRODUCT.md](PRODUCT.md) says.

- Written "Halora": capital H, the rest lower case. Never "HALORA", never "Halora VR".
- In this checkout the setup page and `Start Halora.command` say Halora; the other Mac pages are
  titled by what they do. The headset app's product name (`apps/xr/ProjectSettings/ProjectSettings.asset`, `productName`),
  its panel's label and the workspace's own title (`WorkspaceText.Product`) say Halora too.

## The story

The halcyon at dawn over a calm sea. The kingfisher is calm and quick at once, and dawn is the
moment something begins. Together they hold the feeling Halora exists for (PRODUCT.md, "The feeling
it exists for"): wonder at being somewhere new, and the ease of making your own thing there.

## The mark

[brand/halora-mark.svg](brand/halora-mark.svg) is the mark, drawn once in a 200 by 200 box in
`currentColor`.

- One solid shape: a kingfisher perched and facing left, its eye cut out (even-odd fill). A big
  round head, a long dagger beak, a plump body, a short wedge tail, no legs.
- Cream `#FFF6E8` on colour. Ink `#2A211B` on cream.
- One drawing at every size, with no separate small version. Icons are made at 32 px and larger: at
  16 px the beak and tail merge.

## Colour

The ground's ramp, from the sea up to the sky:

| Role | Hex |
| --- | --- |
| Deep sea | `#1C6B7D` |
| Turquoise, the kingfisher's back | `#40A3B3` |
| Cream | `#FCF0DB` |
| Apricot | `#FABD80` |
| Kingfisher orange, its breast | `#ED7833` |
| Rose | `#E68C7A` |

Orange and turquoise are complements, both taken from the bird. Never add a third hue family.

## Type

- Instrument Serif (SIL Open Font License) for the wordmark and titles.
- Inter for words that do work.
- The line under the wordmark is "Direct your software work in VR". The submission's tagline is the
  long form of the same message.

## The ground

A soft-focus painting of dawn over the sea in the kingfisher's colours, light above and sea below,
with film grain.

- Source: [brand/halora-ground.frag](brand/halora-ground.frag), a WebGL 1 fragment shader of
  domain-warped noise mapped across the ramp, seed 3.1. The page that shows it blurs it, then lays
  the grain over the blur.
- One renderer. The noise differs by GPU, so a frame drawn on another renderer does not match the
  approved board. The approved rasters were drawn by SwiftShader, through Chrome with
  `--use-angle=swiftshader --enable-unsafe-swiftshader`, and any new frame is drawn the same way and
  compared with them first.
- The approved rasters are kept outside the repository: 2048 by 2048 (SHA-256
  `bb200b8d3abdae4dc7c3038ccbc6358bb1a638b4212e6a139643af2dc40dfea6`) and 3840 by 2160 (SHA-256
  `469ee0a4bd316a22418a647e0f69a2aa6f5eb2d0e92a47aa8c35804337421e35`). Only icons at their own sizes
  are tracked.

## Where it appears

Decided on 2026-11-16; not all of it is built in this checkout:

- The headset app's icon: the ground with the cream mark.
- The Mac page's tab icon: the same, at 32 and 64 px only.
- The film's opening and closing cards: the ground drifting, the mark arriving, the wordmark under
  it.

## Rules

- Light and warm. Never a dark board, never random colour blobs, never a flat single-colour field.
- The mark shows no other company's logo and stands beside none; a plain line naming the device it
  runs on is a fact, not a brand.
