# Interface palette and Settings categories

- Question: can the headset offer coherent local preset preview/apply/cancel/restore and categories
  without changing work, comfort, pairing, provenance or input semantics?
- Date: 2026-10-11.
- Environment: source candidate based on organization `9ea346cf`, Unity 6000.3.25f1 packages, .NET 10.
- Status: source checkpoints `9f2cbd32` and `9ebebe46`; isolated focused check passed 79/79
  tests (AppearanceSettings, SettingsColumn, GlazeToken and MenuNavigator). Root's exact
  `9ebebe46` full gate passed 1033 repository tests and 1527 C# tests, zero skipped, on
  2026-10-11 at 23:20:08 EDT. Aggregate runtime and all current Editor source compile passed
  without launching Unity; existing nullable warnings remain. A subsequent actual-composition
  lifetime fix `f224b498` passed 83/83 focused tests and the same aggregate compile; root's
  exact full gate passed 1033 repository / 1531 C# tests. A further hidden-side ownership and
  deterministic cache-witness correction passed 85/85 focused tests plus aggregate source
  compile; its exact full gate and independent review remain pending. Actual preset rendering and
  device checks have not run. The composition harness is prepared; visual acceptance remains open.

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
to its appearance state's applied preset. The director now reports actual menu visibility,
including focused cards and the menu stepping aside when a file cannot stand beside it. Hidden
retained columns release transient authority once, and returning requires a fresh draw; menu-owned
details and an ordinary visible file retain preview. A bounded repaint shows rollback in the same
composition. Compact uses its existing suspension once. Only Apply and Restore call the local save callback.

The saved appearance value is versioned and separate from comfort, space, pairing and project
visibility. Damaged, unsupported or incomplete values fall back to the original Glaze appearance.
No preference contents were inspected for this work.

Real initial consumers are Glaze token getters, menu glass surfaces, prompt/button paint and
outside-word grounds. Button caches include the palette version. The director requests redraw
and clears old drawn target proof with `Moved`, preserving the existing frames' text-reading
evidence where layout did not change. Current character labels, state badges, marks, peek cards and stage banners subscribe while
enabled and refresh their displayed model on palette changes and reactivation. Badge refresh
preserves its saved breath, turn phase and opacity. Source hooks are complete; their rendered
same-session consistency remains unverified.

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

The owner has released the headset trial. Root executed `AppearanceCheck` at `f224b498` on
2026-10-11. The whole pass failed: existing organization fixture diagnostics remain, and label,
peek and banner neutral-change guards failed at both sizes. Category/Appearance and visibility
fixtures emitted no diagnostics. Archived images show active cache unchanged and fresh neutral
plates differing by only 1 or 2 RGB levels, below the general image tolerance. Plain edit-mode
MonoBehaviour subscriptions were not being exercised. The bounded followup explicitly invokes
the existing OnDisable/OnEnable lifecycle in its Editor fixture, unsubscribes before activation
and cleanup, and uses exact RGB comparison for these deterministic same-pose cache witnesses.
This is seeded lifecycle coverage, not live activation evidence; its rerun remains pending. This record does not claim renders,
mutation checks, frame cost, discoverability or user complaints resolved.

## Prepared composition witness

`Halcyonic.XR.Workspace.Editor.WorkspaceRender.AppearanceCheck` keeps existing default render
entrypoints and adds isolated preset folders. It runs the existing component gallery, workspace
and task-file compositions at both reading sizes for each preset, preserving their existing
field, target, clipping, outside-word and cost checks. Existing director and judge-walk Settings
routes now enter the visible Comfort or Your space category before selecting a setting.

An additional real MenuDirector fixture on white passthrough renders the category index,
choices, all five preview pages, visible Cancel, Apply and Restore explanation/action routes.
A companion fixture uses the production director with stable ordinary/focused file frames and
Appearance retained, on
the normal stage and the existing beside-window arrangement in the same Quest 3S field. It
asserts the observed MenuAside flag before claiming that hidden path, verifies rollback and
old Apply refusal after returning, and allows a normal larger-text composition to step aside
if the real layout requires it. No narrower headset field is invented.

The fixtures own only in-memory appearance state and a counted save callback; they never read or write
preferences, connects a session or sends work. Each state uses the ordinary plane geometry
checks. Cached label, working/waiting badges, peek, practice mark and banner are rendered
against freshly drawn equivalents at the same palette, transforms, seeded loop pose and
opacity, both while active and after a disabled palette change/reactivation. Neutral plates
must visibly change and restore; unchanged status/practice colors must keep their pixels.
These are deterministic seeded poses, not observations of live animation timing.

All mutable palette, text-size and measured-field state is restored in the batch entrypoint's
finally block. PNGs go only to ignored Builds directories. The batch entrypoint is prepared
and source compiled. The `f224b498` whole pass failed as recorded above; the corrected
seeded-lifecycle/exact-pixel witness remains unrun. Its pixel and geometry assertions require
a fresh actual Unity pass and screenshot review; passing engine-free tests does not approve Graphite's composition.

A source mutation check remains pending: temporarily make each cached component's
`RefreshPalette` return without updating, run the exact same seeded fixture and require its
cached-versus-fresh exact RGB guard to fail for changed neutral plates. Restore the mutation
before any candidate run. No palette values or general render tolerances are loosened.

The hidden-side regression uses real SettingsHub/SettingsColumn Forget details and confirmation,
then the same stable file becomes focused, with and without its own side. The hidden retained
menu side cannot block fresh file draw/action proof or retain old Yes. Returning menu-owned
details regain the front after a fresh draw and block the file behind them. This is an
engine-free ownership regression; combined production instruction behavior still needs review.
