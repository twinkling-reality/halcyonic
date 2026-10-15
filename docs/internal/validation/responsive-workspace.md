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
translation/distance candidates. Focus/tracking readiness, a manual drag, active voice, or minimized
work prevent automatic recovery. Current head-relative full-object corners trigger the recovery
request; calm dwell captures one anchor. Existing navigation compact/expand retains drafts and
pages while removing authorization. Director, actual content buttons and chrome reject input during
motion/settling; old presses are cancelled. An unchanged slide target preserves its timeline.

12 focused pure policy/initial-placement tests passed. This is not actual rendering or comfort proof.
The editor ResponsiveWorkspaceCheck uses production MenuDirector/MenuPlane/UI and a synthetic
current session that is never started. It drives yaw and translation traces, repeated passive
redraws, still/slide preferences, current-head front-field corners, target/text thresholds, retained
draft text, stale press release and not-ready input negatives. It restores static renderer state,
destroys its synthetic scene, and starts no transport or real command. It has not yet executed.

## Required actual acceptance

Run both reading sizes and both motion preferences. Confirm recovery enters the current front field,
settles under redraw, preserves drafts, cancels stale presses and does not move room-placed agents.
Then check the device with the wearer: slow reading movements remain still; a deliberate head turn
or translation recovers promptly after rest; no nausea, chasing, lag, accidental activation or draft
loss. None of these device/wearer outcomes is established by source tests or the old static captures.
