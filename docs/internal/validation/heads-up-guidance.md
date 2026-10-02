# Heads-up guidance

- **Question:** What do Meta's rules allow for a heads-up mode in Halcyonic, glanceable status at
  the edges of the view while the person walks round the room or while a 2D window has the focus,
  and what references inform its layout?
- **Date:** 2026-10-02.
- **Method:** Meta's developer documentation, design guidelines, help and safety pages, and the
  OpenXR specification, read on the date above, with each page's own date where it shows one; the
  v207 Core SDK sources in the project's package cache; the owner's references, a post with an image
  of a runner's display, and two makers of displays for sport. No headset.
- **Status:** Documentation only. Nothing here is verified on a headset.

## Findings

### Following the head

- Meta ranks the choices: content placed in the world by default; a leashed follower on the head
  only for small, global interface that must stay findable; a rigid head lock only for tiny,
  short-lived markers nobody interacts with, never menus, reading surfaces or persistent status
  ([HUD placement, Immersive Web SDK](https://developers.meta.com/horizon/documentation/iwsdk/concepts/spatial-ui/hud/),
  2026-09-04). Its example follower stands 1 m ahead, turns with the head's yaw only, and has a dead
  zone and smoothing; the example's numbers carry no units, and the SDK is Meta's web one, not
  Unity's.
- In passthrough, content held close to the eyes tires people: anchor it, or let it follow loosely
  with smoothing; place objects about 1 m away, slightly below eye level; scale by angle what must
  stay readable ([mixed reality design guidelines](https://developers.meta.com/horizon/design/mr-design-guideline/),
  2025-10-07).
- While the person moves: content that follows the head goes to the side, out of the lower and
  central view; content fades when they move fast, back up or near a wall; and the more movement,
  the fewer and smaller the items ([boundaryless best practices](https://developers.meta.com/horizon/design/boundaryless-best-practices/),
  2026-09-04). Content pinned in front lowers awareness of the room, and content at the edges of the
  view asks for quick turns of the head and neck ([comfort](https://developers.meta.com/horizon/design/comfort/),
  2025-12-17).
- An action that lives only at the edge of the view needs a second way in
  ([field of view](https://developers.meta.com/horizon/essentials/field-of-view/), 2026-09-25,
  written for Meta's glasses).
- No Meta page gives a size, a duration or a count for content that follows the head.
- Follow components: the Spatial SDK's `Followable` is for Kotlin apps, 3 m ahead by default
  ([reference](https://developers.meta.com/horizon/reference/spatial-sdk/v0.14.0/com_meta_spatial_toolkit_followable/),
  no date); the Interaction SDK's v207 `OSPanelTransformer` turns a grabbed panel to face the head,
  which is not a follow
  ([release notes](https://developers.meta.com/horizon/downloads/package/meta-xr-interaction-sdk-ovr-integration/),
  2026-09-22), and is absent from the project's cached 207.0.0 packages; the Core SDK's
  `AlertViewHUD` building block trails the camera, smoothing at 7 a second and hiding after 20 s by
  default (its source in the package cache). No Unity page documents a loose follow.

### Walking round the room

- A Stationary boundary is for sitting or standing in place, 1 by 1 m; Roomscale is for moving
  round, 2 by 2 m or more ([boundary help](https://www.meta.com/help/quest/463504908043519/), no
  fixed date shown). Near its edge a grid shows and passthrough fades in
  ([integrate the boundary](https://developers.meta.com/horizon/documentation/unity/unity-ovrboundary/),
  2026-09-10).
- An app may drop the boundary: entirely, if it has no immersive part, or only while passthrough
  shows. Then the stage's tracking space is unreliable, so content is held by world locking or a
  spatial anchor, and safety is the app's responsibility
  ([boundaryless](https://developers.meta.com/horizon/documentation/unity/unity-boundaryless/),
  2026-02-06). Passthrough and a room scan do not replace the boundary, and with neither, Meta
  advises against content locked to the person
  ([passthrough health](https://developers.meta.com/horizon/design/mr-health-passthrough/),
  2025-07-25). The headset's narrow field hides obstacles, more so at speed
  ([general health](https://developers.meta.com/horizon/design/mr-health-general/), 2026-09-04), and
  the boundary may warn too late for someone moving fast
  ([Quest 3 health and safety warnings](https://www.meta.com/legal/quest/health-and-safety-warnings/quest-3/),
  no date).
- Halcyonic keeps the boundary on: `boundaryVisibilitySupport` is off in
  `OculusProjectConfig.asset`, and the stage scene does not suppress it. Walking with Halcyonic
  therefore needs a Roomscale boundary.

### Outdoors

- Meta's Quest 3 warnings say it is "Not recommended for use outdoors": tracking may fail there, and
  direct sun can damage the lenses and display; its help page says under a minute of direct sun,
  even indoors, can damage the lenses for good ([sunlight](https://www.meta.com/help/quest/254305933104071/),
  no date). A heads-up mode is for indoors.

### Another app in front

- Only one immersive app runs at a time
  ([Spatial SDK design tips](https://developers.meta.com/horizon/documentation/spatial-sdk/spatial-sdk-design-tips/),
  2025-05-28). A 2D window and the immersive app can both show, but only one has input, and the app
  without it must hide hands and ignore their input
  ([lifecycle](https://developers.meta.com/horizon/documentation/unity/unity-lifecycle/),
  2026-07-23; [horizon-os-multitasking.md](horizon-os-multitasking.md)). A session that is visible
  but not focused still renders
  ([OpenXR `XrSessionState`](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrSessionState.html)).

### References for the layout

- The owner's reference post (Linus Ekenstam, X, 2026-10-01, read through X's own embed) wishes
  runners could race their own past runs; its image, a concept rather than a product, shows pace,
  distance, elapsed time and heart rate stacked at the left edge, a split time against a best at the
  right edge, a ghost runner ahead, and a small distance and elevation strip at the bottom right.
- Engo 2's product page ([engoeyewear.com](https://engoeyewear.com/en-ww/products/engo-2), no date)
  puts its numbers a little to one side, read without turning the head and out of the way otherwise.
  Ghost Pacer ([ghostpacer.com](https://www.ghostpacer.com/), no date) shows a runner at a target or
  past pace. Neither explains the reference image's exact layout.

## Consequences

- Inference: heads-up items follow on a leash, never locked to the head. They turn with the head's
  yaw after a dead zone, smoothed, about 1 m away and slightly below eye level, at the sides, two of
  them, small, and fade while the person moves fast.
- Inference: the item that waits has a second way in. Standing still brings the stage back, where
  the person opens the task as usual.
- Walking needs a Roomscale boundary, and outdoors is out.
- Beside a focused window Halcyonic still shows its items, but its first pinch may only return the
  focus; the second opens the menu. Unverified on a headset.
- To check on a headset: whether a leashed follower stays calm while walking, reading at 1 m, the
  first pinch after a window had the focus, and how the boundary's grid and passthrough fade-in meet
  the items.
