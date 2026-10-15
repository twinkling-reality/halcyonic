# Workspace view draw lifecycle

2026-10-15. Unity 6000.3.25f1 editor on macOS. Synthetic data only.

## Question and method

Can the existing plane report an ordinary opening, closing or hidden view as fully drawn?
This matters because the director forwards eligible layout acknowledgements to columns that
track which request text has been presented.

`Halcyonic.XR.Workspace.Editor.WorkspaceRender.DrawLifecycleCheck` drives the actual
`MenuPlane.Show` and `Advance` lifecycle at Standard and Larger text sizes, in both the split
and spatial compositions. It checks initial opening, cancellation before opening completes,
reopening the same frame, closing and hidden views, and repositioning. Positive controls require
the reopened frame and a fresh settled layout to report normally.

Run the entry point in the Unity editor with `-batchmode -quit -executeMethod` and the XR project.
It returns a nonzero exit code on a failed assertion. It does not contact a runtime or a headset.

## Result

The fixture compiled and exited 0. No opening, cancelled, closing or hidden-view acknowledgement
was found through the tested normal routes. No production guard was changed.

The plane still reports layout while `Repositioning` is true. This is intentional characterization
of the current boundary: `MenuDirector.OnDrawn` rejects acknowledgements while repositioning or
away. Clearing the plane's flag does not itself replay an acknowledgement; a fresh settled layout
reports again. The fixture does **not** exercise that director rejection or grant approval authority.

Repository checks also passed: 1,195 tests passed, one optional test skipped. These checks do not
compile Unity; the separate editor execution above establishes that this fixture compiled and ran.

## Spatial route observation and continuation fixture

`WorkspaceRender.SpatialWorkspaceCheck` also compiled and exited 0 at both text sizes after
correcting its fixture setup and observation timing. The working fixture now uses valid UUIDv7
project, task, execution and journal identities, and rebuilds its projection keys consistently.
The existing strict continuation parser is exercised unchanged. Its final work-window dispatch
uses an explicit synthetic callback and in-memory persistence, not an Android launch.

The route draws the changed page before inspecting its controls, explicitly finds the Move
action on its measured page, and redraws after Done moving and Minimize before checking rendered
visibility. Intentional pre-redraw stale-frame checks remain in place. The fixture still requires
exactly one synthetic dispatch, the same unsent draft, rejection of stale app controls during
movement, and retained placement/task/draft on restore. Diagnostics distinguish a missing dispatch
from a changed count without logging draft text.

This result covers the preserved composition, including its project-selection and approval views.
It does not accept that composition's design, run the separate question-interaction fixture,
demonstrate a real platform switch or prove the requested headset-driven software workflow.

## Limits

This is lifecycle regression coverage, not a finding that independent spatial regions are safe.
The current navigator has a single focused file and side-panel ownership model. Reusing the same
frame in a passive preview must not grant the focused request reading credit.

Approval read credit across actual director movement, region-specific visibility, delayed callbacks
between independently positioned regions, clipping and physical occlusion need separate integration
coverage when those consumers exist. Manually toggling a plane flag is not an adequate substitute
for driving the director's real movement lifecycle. No new composition, headset comfort, visual
acceptance or end-to-end runtime workflow is validated here.
