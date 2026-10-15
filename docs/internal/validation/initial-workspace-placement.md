# Initial workspace placement

- Date: 2026-10-15.
- Question: Can startup take an untracked or unworn camera pose as its permanent workspace anchor?
- Source: Unity 6000.3.25f1, Meta XR Core 207.0.0 and Unity OpenXR 1.18.0, using the installed package source and Unity API reference XML.
- Status: Source/API verification, focused .NET regression and actual production-director rendering passed, including negative controls. Full workspace rendering passed at both text sizes. The first Android APK build exposed a missing assembly reference that the standalone compilation did not cover; the reference is corrected. The replacement APK build and headset acceptance are pending.

## Verified defect and bounded correction

Previously `WorkspaceDirector.Start` opened the menu immediately. `StageNow` supplied the main
camera pose or fixed fallback, and the first `MenuDirector.Draw` cached that pose without checking
whether anyone wore the headset or whether its head pose was tracked. A later pose could not
correct it: the anchor was retained until explicit Reset position. This is a concrete startup
guard omission. It does not establish that this omission caused a particular blank wearer report;
focus, app visibility and later tracking-space movement require separate diagnostics.

The Android host now requires the current main camera, a valid head device with `isTracked` true
and both Position and Rotation tracking bits, wearer presence and app input focus. A pure consumed
`InitialWorkspacePlacement` gate requires 0.3 continuous seconds, resetting on any missing
condition, with no untracked timeout. The director keeps the first frame hidden and dirty while
waiting and retries every update. When readiness arrives it samples the current pose, not the
startup fallback. After an anchor exists, ordinary head/focus changes bypass this initial gate;
existing Move, selected views/drafts, minimize and restore rules remain unchanged. Tracking-origin
rebasing and recovery after moving during pause are not implemented by this correction.

## Installed API verification

The Unity 6000.3.25f1 `UnityEngine.XRModule.xml` documents `InputDevice.isValid`,
`CommonUsages.isTracked`, `CommonUsages.trackingState` and `InputTrackingState.Position/Rotation`.
The compiled Android-symbol source uses those exact installed XR APIs. No assumption that a
missing tracking flag means tracked is made here.

Meta XR Core 207.0.0 `OVRManager.cs` exposes instance `isUserPresent` (around line1993), backed
by `OVRPlugin.userPresent`, refreshed in its update path (around line2817). `OVRPlugin.cs`
implements userPresent as initialized native `ovrp_GetUserPresent` (around line3877). These source
facts verify the consumed API and its initialization behavior; they are not a real headset sensor
measurement. `Application.isFocused` and the existing FocusGuard suspension both gate initial
readiness; resume alone never manufactures focus authority.

`OVRManager` belongs to the installed SDK's `Oculus.VR` assembly. Workspace declares that direct
reference, as the Room presentation assembly already does. Assembly references are not transitive.
An actual Android player build caught the omitted reference with CS0103; a standalone compiler
given all SDK assemblies cannot verify Unity's assembly-definition dependency graph.

## Regression evidence and limits

Six focused .NET cases passed: tracked-but-unworn startup, each missing readiness condition
resetting the wait, late valid tracking releasing without host events, and clock rollback. A
negative mutation removing the settle wait failed all six; original source was restored and all
six passed again. Core builds under .NET Standard2.1/C#9; current complete runtime sources compiled
against the installed Unity/SDK assemblies for both Editor and Android player symbols. Existing
nullable/reference warnings remain; no compile errors were suppressed.

`DedicatedWorkspaceCheck` now drives the production director with a zero/right-facing fallback
while readiness is false and requires no visible/composed first frame. It then supplies the
actual eyes/forward pose and readiness without a host/session change, requiring a visible correctly
oriented first workspace. A subsequent false gate and changed head pose must not hide or move an
established workspace. Existing complete composition, view/draft, Move, minimize and optional-shelf
checks remain. The actual Unity dedicated check passed at both text sizes. Removing the initial
guard failed the unworn fallback and late-tracking assertions at both sizes; the source was
restored. The combined Workspace check subsequently passed both sizes on the integrated source.

A real unworn cold start followed by wearing the headset must verify sensor availability, first
placement and interaction. Established workspace origin changes and the cause of the reported
missing view remain unresolved.
