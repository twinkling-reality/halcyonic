# 0032: Recover the workspace when it leaves view

Date: 2026-10-15. Status: accepted product direction; implementation and thresholds remain a
candidate until actual renderer, device and wearer validation.

## Context

The owner rejected the installed fixed-anchor behavior: the workspace stayed behind and left
view during head movement. Earlier tests deliberately preserved its yaw under head turns. Those
remain a comparison for an explicitly pinned setup, not the production acceptance criterion.
The first-pose tracked/worn/focused readiness correction is retained.

## Decision

The main application enables responsive content recovery. Small in-view movement leaves reading
still. After the full reading object leaves the current head-relative field, or its distance leaves
a bounded reading band, recovery waits for the wearer to rest, samples one new content anchor,
and uses the existing content slide. It does not continuously attach UI to every head movement.

Pending voice or keyboard capture defers recovery until its input returns, preserving unaccepted words.
Task/frame input and held presses are revoked during motion and settling. Retained column compact/
expand semantics preserve local draft text and pages while revoking prior authorization; only a
fresh settled draw grants it again. Repeated passive redraws retain an identical slide destination's
timeline. No XR camera/tracking transforms, agent/shelf/room placements or native Android panel
placement are changed. Minimized work does not follow itself back into view. Reset position remains
explicitly available.

## Candidate values and consequences

Recovery reuses the existing 0.4 s dwell and 20 degrees/s calm-turn threshold from PeekChoice,
0.25 s content slide and 0.35 s button settling. New calm translation is 0.2 m/s; the distance band
is 0.65–1.5 times the existing reading distance. Pitch eligibility and placement are both bounded to ±25 degrees. Unsupported pitch leaves the
existing object still and does not repeatedly recover; it does not claim to fit. A prospective
anchor must clear actual placed character bodies/labels and its connector without moving them;
an unsafe candidate is deferred before input or content mutation. These are design
choices requiring wearer validation, not Meta specifications. Keep things still uses a direct
content reposition but retains the same input barrier. Fast continuous turns/walking defer recovery
until the wearer rests, avoiding a reading object that continually chases motion.

## Evidence and remaining work

See [responsive-workspace validation](../validation/responsive-workspace.md). Pure policy tests and
source compilation are distinct from actual synthetic moving-head rendering, headset response,
comfort, draft continuity and visual acceptance. The flat interaction model remains a separate
product concern; this motion correction does not claim to make it diegetic.
