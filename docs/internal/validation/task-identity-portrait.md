# Local task identity portrait — 2026-10-15

The portrait is a body-only view of the selected real workstream: the existing character mesh,
identity feature, supported local colour, state cues and global Still preference. It creates no
room-stage target, placement, task or runtime capability. The task's adjacent written state keeps
state independent of colour or motion. Local visibility and explicit room-agent placement are
separate choices.

## Standalone mechanical proof

Unity 6000.3.25f1, Android-target editor with Metal, warm 14b96c38, both Standard and Larger.
The correctly named `Halcyonic.XR.Workspace.Editor.WorkspaceRender.TaskPortraitCheck` exited 0.
Explicit synthetic Idle, Working, WaitingForHuman and Unknown presentations exercised the actual
component and CharacterView. The actual animated shell/feature vertices remained inside the
fixture's 3.5-by-5-degree slot and ahead of its opaque support. Body transform motion advanced;
supported violet selection changed the same identity in place; switching real task ids destroyed
the prior body; Hide cleared identity/body and explicit Show restored the selected task. After
settling Still, the checked pose remained stable.

These are component mechanics, not production composition or wearer acceptance. The standalone
fixture's synthetic title initially overlapped the portrait and extended outside its support. It
therefore cannot establish task-header layout, typography or placement quality; the production
SpatialWorkspaceCheck is the composition gate. Blue motion frames precede the explicit violet
preview, so their different colour is intentional.

## Raster evidence limitation and repair

Direct review found an unrelated gray support contribution disappearing in one motion crop.
The original whole-crop changed-pixel totals include that background contribution and are not
accepted as independent character-raster liveness proof, despite the gate's zero exit status.
No cause is assigned to that fixture contribution here.

The scoped fixture repair isolates motion and Still captures to the actual character renderers,
using the same camera, body meshes, materials and animation calls. It preserves and restores every
excluded renderer's prior enablement in `finally`; final whole-fixture state images retain their
other contributors. The synthetic label pivots are explicitly left aligned. This changes test
quality only, not production appearance or shader behaviour.

The repaired gate ran against warm 85b8cc50 with Metal and exited 0. Direct inspection of the
Working, WaitingForHuman and Unknown strips confirms only the actual body against a fixed black
background. Four unscaled camera crops sample actual frames at ticks 30, 60, 90 and 120; no
interpolated frames or recreated animation are used. Per-channel raster differences above 2/255
produce these totals across the three comparisons with the first frame:

| Text size | Idle | Working | WaitingForHuman | Unknown |
| --- | ---: | ---: | ---: | ---: |
| Standard | 422 | 949 | 1,075 | 1,046 |
| Larger | 549 | 1,271 | 1,422 | 1,399 |

After the actual Still pose settled, all eight isolated comparisons had zero pixels changed above
1/255 after another 120 actual animation steps. The same run passed mesh bounds, supported hue,
identity swap, explicit Hide and restore checks. These establish actual component raster motion
and Still stability in the host fixture; they do not establish the production header's layout,
headset compositor behaviour, performance on device, or wearer acceptance. The complete synthetic
24-image output and matching log were archived privately with a SHA-256 manifest so later editor
runs cannot silently replace this evidence.
