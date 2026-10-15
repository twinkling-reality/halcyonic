# Selected-task placement scene fixture

2026-10-15. `Halcyonic.XR.Workspace.Editor.WorkspaceRender.ShowTaskHereCheck` exercises the
actual Unity `WorkspaceDirector.FileFor` → current task row → `RoomControls` → `RoomPlacement`
→ `AgentShelfColumn` → `CharacterStage` path at both reading sizes. It requires the spatial
composition candidate in the executing project; it never silently tests a different layout.

The only transport is an explicit synthetic RealtimeSession inbox and an in-memory event-history
HTTP handler. Transport startup, native room startup, scene scans and permissions are not invoked.
Edit-mode initialization calls production Awake methods only when their fields have not already
been initialized. An Editor-only assembly reference exposes the existing Room implementation;
no production API or assembly dependency changes.

The fixture observes the real visible shelf preview and its real character after Apply. It checks
initial Cancel, exact requested task rather than the previously selected task, preserving the
confirmed pose/selection when another task is cancelled, cancelling when a welcome head is ahead
of cached state, and cancelling when the session instance changes. An old Apply frame cannot act.
The normal text, target and measured-field checks remain, and the real applied shelf must clear
the complete current workspace. Every command submission is forbidden. Synthetic source state is fixture evidence only, never wearer or runtime evidence.

Settings isolation saves and restores the exact presence and typed values of the five known
room/visibility/onboarding preference keys in a nested finally. The control-plane pairing store,
API, target and migration flag are replaced by in-memory witnesses and restored in finally,
so production FileFor's history read cannot reach user pairing/token storage or a live network.
The fixture never starts passthrough, requests room access or changes appearance preferences.

Client core build and aggregate Editor source compilation against installed Unity/SDK assemblies
passed before execution. This is not an asmdef/runtime PASS: root-owned actual Unity execution,
resulting images and any remaining scene failures must be recorded after the fixture runs.
