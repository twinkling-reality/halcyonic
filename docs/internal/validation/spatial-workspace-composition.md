# Spatial workspace composition candidate

2026-10-15. This is an opt-in renderer candidate, not headset acceptance or a default migration.
The owner's actual headset review rejected the flat stacked composition; see the updated
[style direction](../product/STYLE.md#spatial-composition-candidate--2026-10-15).

`Halcyonic.XR.Workspace.Editor.WorkspaceRender.SpatialWorkspaceCheck` renders three actual
MenuDirector routes at standard and larger text: Projects, a selected working task's local
instruction draft, and a selected task waiting for approval. A synthetic welcome/snapshot/live
transport seam establishes the real session watermark; the production route and drawn-frame
callbacks open each view. No executor, private data, socket, or command submission is required.
Synthetic work retains Practice provenance. The fixture asserts readable text, unchanged target
minimums, current frame footer availability, measured field, reading/control orientation and no
navigation/draft command submission. Chrome bounds enclose the unified work object; the older
separate-rectangle spacing check does not describe this geometry.

Checks before the first Unity render: client core .NET Standard build passed; aggregate Editor
source compile against installed Unity/SDK assemblies passed with existing warnings. The latter
is a syntax/API check, not an assembly-definition build or runtime proof. Actual Unity execution,
visual inspection, interaction regressions and headset acceptance remain outstanding. Production
keeps the existing default until these establish that the candidate is worth migrating.
