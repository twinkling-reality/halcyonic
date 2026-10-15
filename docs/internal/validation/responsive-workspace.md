# Responsive workspace candidate

2026-10-15. The owner confirmed that the installed workspace stayed behind and left view. This
reverses the former production fixed-anchor policy; it does not reverse tracked first-pose gating.

## External grounding

Read current official Meta [MR key considerations](https://developers.meta.com/vr/design/mr-design-guideline/),
[Head](https://developers.meta.com/vr/design/head/) and [Windows](https://developers.meta.com/vr/design/windows/).
They favor comfortable spatial content, caution against excessive head locking, preserve accurate
head tracking, and distinguish Follow me from Pin to space. They do not prescribe this candidate's
dwell, translation band or settling values. The implementation changes app content only, never the
camera/tracking. Installed Interaction SDK 207 FollowTargetProvider was inspected: it is a continuous
movement provider, not a reading deadzone/rest policy, so no vendor movement API is added.

## Source and tests

The core WorkspaceFollow policy reuses PeekChoice dwell/calm-turn constants, with explicit new
translation/distance candidates. Focus/tracking readiness, a manual drag, active voice or pending keyboard, or minimized
work prevent automatic recovery. Current head-relative full-object corners trigger the recovery
request; calm dwell captures one anchor. Existing navigation compact/expand retains drafts and
pages while removing authorization. Director, actual content buttons and chrome reject input during
motion/settling; old presses are cancelled. An unchanged slide target preserves its timeline.

14 focused pure policy/initial-placement tests passed. This is not actual rendering or comfort proof.
The editor ResponsiveWorkspaceCheck uses production MenuDirector/MenuPlane/UI and a synthetic
current session that is never started. It drives yaw and translation traces, repeated passive
redraws, still/slide preferences, current-head front-field corners, target/text thresholds, retained
draft text and pending keyboard callback, stale press release and not-ready input negatives. It restores static renderer state,
destroys its synthetic scene, and starts no transport or real command. It has not yet executed.

The first actual editor run failed twelve composite draft/submission assertions and exited 134
during native shutdown; it is not a clean process result. Source review found the fixture selected
the ordinary Send instruction button before movement. Ordinary buttons activate on Select, so that
reachable action submitted the synthetic draft before recovery began. This is a fixture error,
not evidence that production recovery lost words. Diagnostic checks now name submission count,
armed steering and exact synthetic draft mismatch separately at each transition. The corrected
trace begins recovery without selecting Send, selects the retained old button while repositioning
has revoked authority, and releases only after settling. It requires no submission and unchanged
words both after denied selection and after release. It does not manufacture a held ordinary
button or claim that a permitted Send selection should be cancelled retroactively. The corrected
actual run remains pending; device and wearer outcomes remain unverified.

## Required actual acceptance

Run both reading sizes and both motion preferences. Confirm recovery enters the current front field,
settles under redraw, preserves drafts, cancels stale presses and does not move room-placed agents.
Then check the device with the wearer: slow reading movements remain still; a deliberate head turn
or translation recovers promptly after rest; no nausea, chasing, lag, accidental activation or draft
loss. None of these device/wearer outcomes is established by source tests or the old static captures.

## Source-review corrections before the first actual run

The pitch eligibility now matches the ±25 degree placement bound. A resting 80-degree pose is
explicitly unsupported; it leaves the existing object still rather than repeatedly moving,
settling and revoking input. An already-running move can still settle if the wearer enters that
unsupported pitch. No camera change or false front-field-fit assertion conceals the limitation.
The actual fixture includes a 20-degree pitch/yaw recovery and an 80-degree non-looping trace.

Before committing a requested anchor, MenuPlane.CanRecoverAt checks its complete prospective
footprint against the current head-relative field, actual character body/label clearance and the
predicted connector against prospective-viewer body/label geometry. It deliberately does not reuse
manual drag's legacy elevation band. Rejection occurs before pose/input mutation; it may reconsider
after a fresh calm dwell but must not visibly move or revoke input. It tests the one head-relative
candidate, not an exhaustive search for another free location. The actual fixture places a real
character at that proposed destination, requires refusal with unchanged character and UI poses,
then deliberately hides it and requires safe recovery. No stored room placement is changed.

The unchanged-destination SlideTo guard retains only an existing non-closing slide when there is no
new arrival or immediate request and position/rotation/scale match. Explicit immediate placement,
arrivals and manual Stand remain distinct. Frame/target settling and frame ownership still apply;
only a phantom animation restart is removed. The actual repeated-redraw trace is its regression
witness; actual execution remains pending.

The prospective connector chooser now receives the prospective viewer and obstacles throughout
endpoint selection and intersection checks; it does not choose against the old view first. A line
attached to its own body keeps the real body penetration test without adding the other-body halo
at that attachment; other bodies retain their clearance margin. The actual renderer uses the same
own-body rule. A positive fixture deliberately places the selected task's grounded real character
offset beside the next anchor, requires its prospective heading connector to be allowed, then
requires actual recovery with unchanged character pose, shared task identity and a real connector.
Thirty-nine actual connector samples must clear that body and label. This prevents a blanket
selected-character follow disable from passing the collision-negative case.
