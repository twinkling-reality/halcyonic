# Interface palette and Settings categories

- Question: can the headset offer coherent local preset preview/apply/cancel/restore and categories
  without changing work, comfort, pairing, provenance or input semantics?
- Date: 2026-10-11.
- Environment: source candidate based on organization `9ea346cf`, Unity 6000.3.25f1 packages, .NET 10.
- Status: first engine-free core compile and focused check passed 36 tests on 2026-10-11
  (AppearanceSettings, existing SettingsColumn and GlazeToken tests). A later wrapper provenance/
  confirmation regression is prepared but not yet included in that run. No new Unity or device
  checks. Graphite composition and cached stage consumers remain unverified.

## Source behavior and scope

`GlazePalette` is immutable semantic data. Glaze matches all original surface/text/outline and tone
values. Graphite changes neutral surfaces and the blue action accent, retaining state/practice
colours. Type, target sizes, geometry, motion and white glass optics retain their values. These
choices are implementation decisions requiring composition validation, not inferred preferences.

Settings opens categories rather than paging through unrelated groups. The hub preserves a
setting value's `ValueRuns`, including a paired computer's outside address. Existing setting
leaves retain their confirmation routes. Appearance has persistent single-choice marks; choices
are local until Preview. Its five labelled specimen pages exercise real controls, including
outside-word grounds, without sending work or opening a keyboard. Bounded body paging keeps the
footer to Close, Cancel and Apply. Restore default has its own explanation and affects appearance
only. A preview is owned by the Appearance leaf; every departure through `FocusLeft` rolls back
to its appearance state's applied preset. Only Apply and Restore call the local save callback.

The saved appearance value is versioned and separate from comfort, space, pairing and project
visibility. Damaged, unsupported or incomplete values fall back to the original Glaze appearance.
No preference contents were inspected for this work.

Real initial consumers are Glaze token getters, menu glass surfaces, prompt/button paint and
outside-word grounds. Button caches include the palette version. The director requests redraw
and clears old drawn target proof with `Moved`, preserving the existing frames' text-reading
evidence where layout did not change. Additional widget cache refresh must be completed and
measured before claiming same-session consistency.

Remaining literal colours: CharacterView state lights and testing ring; CharacterIdentity hue,
saturation and value; CharacterBody shader fog, ink, gloss/glint and ghosting material values;
WorkspaceVisuals legacy text/connector colours and WorkspaceDirector's reticle; shader-internal
white glass sheen. The UI palette does not customize body identity or these material treatments.

## Required checks

1. Focused isolated .NET tests: default exact values, both presets' contrast, strict preference
   fallback/roundtrip, local choice versus temporary preview versus saved apply, rollback on every
   departure and appearance-only restore; category support and outside-value provenance.
2. Navigator/director integration: old-frame Apply/Forget/input presses are refused after category
   and palette redraw; compact, Move, focus and session replacement release the preview, while
   retained content/drafts/pages remain as their current contracts require.
3. Actual compositions at both text sizes, white passthrough and lit selection: first visit,
   Projects, Settings index/leaves/specimen, focused answers/input/review and their outside-word
   grounds. Every word stays readable, every target separated, no clipping/footer overflow, and
   the same hierarchy holds for both presets.
4. Same-session cache/material update and rollback witnesses, not swatches alone: existing stage
   labels, badges, peek, banner and menu alongside the open file. Contrast and provenance survive
   changing and restoring the preset without a state/status change.
5. Allocation and draw-cost checks hold existing field, type, target, 60 per-surface/220 per-scene
   and allocation guards. Actual headset palette legibility, both-eye rendering and comfort remain
   wearer/device observations.

Unity and device jobs are held for the owner's active trial. This record does not claim renders,
mutation checks, frame cost, discoverability or user complaints resolved.
