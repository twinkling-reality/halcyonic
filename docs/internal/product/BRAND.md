# Brand

Canonical reference for the name people see, its mark, colours, type and ground where Halora
appears outside the headset's panels (the app icon, browser tab icons and film cards). The
headset's panels follow [STYLE.md](STYLE.md); their words follow [WORDS.md](WORDS.md).

## The name

The shown product name is **Halora**: halcyon, the kingfisher of the calm-sea legend, and aurora,
the dawn. Internal code, npm packages (`@halcyonic/*`), C# namespaces (`Halcyonic.*`), CLI and
environment identifiers (`HALCYONIC_*`), folders and `~/.halcyonic` keep the working name
`halcyonic`, as [PRODUCT.md](PRODUCT.md) states (`tooling/visible-name.test.ts` guards user-facing
sources).

- Written "Halora" in prose and titles: capital H, the rest lowercase. Never "HALORA" in prose
  (only where a panel's eyebrow role renders all labels in spaced capitals), never "Halora VR".
- On macOS, `Start Halora.command` launches the control plane. The browser pages (`Pairing`, `Set
  Up This Computer`, `Usage`, `Photos and Videos` and `Play`) are titled by what they do, set their
  interface text in `Inter`, serve the kingfisher tab icon, and refer to the product as `Halora`.
- On the headset, the app's product name (`apps/xr/ProjectSettings/ProjectSettings.asset`,
  `productName`), the native Android panel (`AgentPanelActivity`), the glance notification
  (`GlanceActivity`), the workspace label (`WorkspaceText.Product`), and the default `Code Colours`
  palette (`CodeTheme.Halora`) all say `Halora`.

## The story

The halcyon at dawn over a calm sea. The kingfisher is calm and quick at once, and dawn is the
moment something begins. Together they hold the feeling Halora exists for
([PRODUCT.md](PRODUCT.md#the-feeling-it-exists-for), "The feeling it exists for"): wonder at being
somewhere new, and the ease of making your own thing there.

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
- The line under the wordmark is "The best way to build with AI agents is in VR."

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

- **Headset launcher icon (built):** the ground cropped square with the cream mark centred across
  Android `legacy`, `round` and `adaptive` densities (`apps/xr/Assets/Halcyonic/Brand/Icon/`, wired
  in `ProjectSettings.asset` and checked by `HaloraIcons`).
- **Mac browser tab icon (built):** the same mark on its ground at 32 and 64 px only
  (`apps/control-plane/src/network/pairing-page/` and `apps/control-plane/src/preview/viewer/`).
- **Film opening and closing cards (rendered outside the repository):** the ground drifting, the
  mark arriving, and the wordmark and tagline beneath it.

## Rules

- Light and warm. Never a dark board, never random colour blobs, never a flat single-colour field.
- The mark shows no other company's logo and stands beside none; a plain line naming the device it
  runs on is a fact, not a brand.
