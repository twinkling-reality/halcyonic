# Interface palette and Settings categories

- Question: can the headset offer coherent local preset preview/apply/cancel/restore and categories
  without changing work, comfort, pairing, provenance or input semantics?
- Dates: 2026-10-11 and 2026-10-12.
- Environment: original source candidate based on organization `9ea346cf`; the October 12 replay combines instruction integration `be497166` with Settings through `c7a64021`. Unity 6000.3.25f1 packages, .NET 10.
- Current result: the integrated `ae6da8f426da47d99ffa8def56a4333d1a29676e` actual Unity
  `AppearanceCheck` completed with exit 0 on October 12. Glaze and Graphite each passed the
  component, workspace and task-file galleries at Standard and Larger, all 12 gallery success
  markers plus the final appearance success marker. This includes the real category, preview,
  Cancel, Apply, Restore, visibility rollback and seeded cache witnesses. Wearer/device acceptance
  and live activation remain separate observations.

## Neutral adoption candidate, October 15

The candidate adds the stable `neutral` key and makes Neutral the default for absent or invalid
appearance values and explicit Restore. Its low-chroma surfaces and neutral action colours are
the previously explicit InstrumentStudy comparison values; the alias refers to the same object,
not a fourth chooser entry. Presets list Neutral, historical Glaze, then Graphite. The exact
`glaze` and `graphite` keys and colours are preserved. The version-1 serialized envelope remains
unchanged; no migration, headset preference read/write or device activation was performed.

Source audit confirms `AppearanceControls.Awake` loads only `halcyonic.appearance` and applies the
loaded palette. `AppearanceSettings.Load` has no persistence callback. `AppearanceColumn` previews
without saving, rolls back on Cancel/focus/close, and invokes its save callback only from explicit
Apply or explained Restore. Restore now names `GlazePalette.Default.Name`. Character-colour,
Comfort/Still, placement and project state are separate consumers and were not changed.

Focused tests retain exact historical surface values, verify all six state/practice tone tuples
are unchanged across the three presets, check immutable preset order and exact lookup keys,
round-trip all three values, and exercise Neutral preview/rollback/apply from saved historical
Glaze. Existing contrast and navigator lifetime checks remain in force. These are source and
managed-behavior results, not actual three-palette Unity composition, character harmony, headset
legibility or wearer acceptance. October 12's two-palette evidence below does not validate this
new candidate. The spatial Appearance entrypoint now iterates the same three-preset list as
the full Appearance and cache entrypoints. The material aesthetic remains independently rejected;
this palette candidate does not approve the current material composition.

## Current integrated evidence, October 12

- The final production/C# tree `d7fcecd2` passed 1033 repository and 1566 C# tests, zero skipped,
  and independent source/security review. `ae6da8f4` changes only the Editor Judge witness and
  canonical explanation; its repository gate passed 1033 tests. Aggregate runtime/all Editor
  source compilation also passed, with four existing nullable warnings. Actual Unity compiled
  the project and ran the final checks; aggregate compilation alone is not assembly-boundary proof.
- Exact `d7fcecd2` actual `OrganizationCheck` and `InstructionCheck` each exited 0 at both text
  sizes. Their fresh archives hold 146 and 24 PNGs respectively, including the visible paged
  Settings index beside the file and retained instruction Compact/Expand. The final `ae6da8f4`
  appearance run retains the same production and instruction source and includes complete Usage
  detail parts, both presets' real appearance pages, and all gallery assertions.
- Exact `d7fcecd2` `AppearanceCacheCheck` exited 0 across both presets and sizes. Individually
  replacing label, peek and banner `RefreshPalette` with a no-op made each exact same check exit 1
  with four active cached-versus-fresh failures, one per preset/size. Original bytes were restored
  after each mutation, and the warm checkout was clean before the final candidate run. Label
  failures changed 2953 Standard / 3236 Larger pixels, at most 2 RGB levels: the exact comparison
  catches changes the unrelated general render tolerance would ignore. State/practice colours
  remain identical by design; their seeded pixel invariance passes but is not neutral-refresh
  mutation proof.
- Fresh renders and SHA-256 manifests are retained only in the ignored coordinator scratch area.
  Representative Standard/Larger category, appearance preview/applied/cancel, instruction
  compact/resumed/review, and complete Usage parts were visually inspected for full stage,
  surfaces, words and controls. These images and geometry checks do not establish wearer comfort,
  headset both-eye rendering or live MonoBehaviour activation.

The synchronous Editor cache fixture explicitly seeds existing OnDisable/OnEnable subscriptions,
with the same model, pose, breath, turn and opacity for cached and fresh objects. This proves the
seeded refresh and rollback paths; it does not prove a live play-mode or device activation cycle.
The capture helpers explicitly clear reused target depth. Controlled near-depth priming with the
camera's incidental clear disabled catches removal of that helper clear; ordinary repeated
captures alone did not reliably expose the defect. The controlled regression is not a claim that
every render backend has the same persistence behavior.

## Player/editor build boundary

The first Android player build at `f2eb95e3` failed with CS1061: the unguarded
`MenuDirector.PressSubjectForRender` called `MenuFrameView.SettleSubjectForRender`, which exists
only under `UNITY_EDITOR`. The director's contiguous render-only fields and helper block,
including `DrawNow`, now share that editor guard. Consumer inspection found only Editor callers
and the internal render helper chain. Production settling, pointer handling and action authority
are unchanged; the player does not acquire a settle bypass to make the build compile.

An aggregate runtime-only compile with Android/development symbols, no `UNITY_EDITOR` symbols,
no Editor source files and no Editor assembly references passes with two existing nullable
warnings. The same response with the original director source reproduces the exact CS1061
failure. Player metadata contains none of the guarded director helper names or fields. The
aggregate all-Editor compile also passes with its four existing nullable warnings, preserving
the render entrypoints. These checks isolate the source boundary; the actual Android APK build
is a separate coordinator check.

The actual development APK build from `a6db5702` subsequently succeeded with Unity exit 0 on
October 12, producing 114517436 bytes. It was installed over the existing development app on a
Quest 3 and launched. The live connection proved the control plane's token before upgrading;
the existing app-private token retained mode 600 and 44 bytes, no temporary token write or shared
copy remained, and no cleartext or StrictMode line was reported. No token rewrite was needed.
The device checker exited 1 solely because its exclusive-USB-route assumption rejected an
existing unrelated route alongside Halcyonic's correct loopback route; that unrelated route was
preserved. This is not an all-pass checker result or wearer acceptance. Both-eye rendering,
gesture feel, live palette activation and comfort still require the focused headset trial.

## Historical checkpoints and failures

On October 11, isolated Settings checkpoints `9f2cbd32` / `9ebebe46` passed 79 focused tests;
`9ebebe46` passed 1033 repository / 1527 C# tests. `f224b498` passed 83 focused tests and
1033 / 1531 full tests. `c7a64021` passed 85 focused tests, 1033 / 1533 full tests and independent
security review. Their aggregate source compiles passed without launching Unity. Those source
checks did not approve the then-unrun corrected compositions.

The October 12 replay first passed 238 combined focused tests and four real instruction/Settings
lifetime cases; `73d74ab0` passed 1033 / 1561 full tests. The bounded cache entrypoint `92c80f53`
was source compiled. Final integration added measured Usage main/detail pages, their complete
Judge traversal, and measured Settings category pages with two additional size regressions.

The actual October 11 `f224b498` appearance failure is detailed below. On October 12, `f75703fe`
OrganizationCheck exposed a category index that ignored its narrowed heading/page height and hid
Settings beside the file. `d7fcecd2` corrected measured category packing without reducing limits.
Its whole appearance pass still failed eight diagnostics: four at Larger per preset from a Judge
fixture attempting a hidden Settings change while a lone approval file occupied the field.
`ae6da8f4` preserves the production lone-file budget and proves reachable Comfort change/restore
before opening the file. A distinctly logged direct Comfort-model transition separately retains
all before-draw request read/Yes invalidation assertions. The final whole pass succeeds; no hidden
button authority or physical geometry waiver was introduced.

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
preserves its saved breath, turn phase and opacity. Seeded same-session rendered consistency and rollback pass as recorded above; live play-mode
and device lifecycle consistency remain unverified.

Remaining literal colours: CharacterView state lights and testing ring; CharacterIdentity hue,
saturation and value; CharacterBody shader fog, ink, gloss/glint and ghosting material values;
WorkspaceVisuals legacy text/connector colours and WorkspaceDirector's reticle; shader-internal
white glass sheen. The UI palette does not customize body identity or these material treatments. The separate selected-task
Character colour control offers bounded headset-local identity hues, as documented in
[XR_CLIENT.md](../architecture/XR_CLIENT.md#optional-character-identity-and-animation-2026-10-13);
it changes no interface palette or task-state colour and has no headset acceptance claim.

## Checks exercised and remaining device observations

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

The historical actual `AppearanceCheck` at `f224b498` ran on 2026-10-11. The whole pass failed:
organization fixture diagnostics were present, and label,
peek and banner neutral-change guards failed at both sizes. Category/Appearance and visibility
fixtures emitted no diagnostics. Archived images show active cache unchanged and fresh neutral
plates differing by only 1 or 2 RGB levels, below the general image tolerance. Plain edit-mode
MonoBehaviour subscriptions were not being exercised. The bounded followup explicitly invokes
the existing OnDisable/OnEnable lifecycle in its Editor fixture, unsubscribes before activation
and cleanup, and uses exact RGB comparison for these deterministic same-pose cache witnesses.
The final integrated seeded rerun and neutral mutation checks pass as recorded above. This remains
seeded lifecycle coverage, not live activation evidence; device frame cost, discoverability and
wearer comfort are not declared resolved.

## Composition witness

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
preferences, connect a session or send work. Each state uses the ordinary plane geometry
checks. Cached label, working/waiting badges, peek, practice mark and banner are rendered
against freshly drawn equivalents at the same palette, transforms, seeded loop pose and
opacity, both while active and after a disabled palette change/reactivation. Neutral plates
must visibly change and restore; unchanged status/practice colors must keep their pixels.
These are deterministic seeded poses, not observations of live animation timing.

All mutable palette, text-size and measured-field state is restored in the batch entrypoint's
finally block. PNGs go only to ignored Builds directories. The final actual integrated pass and
image review supersede the earlier prepared-only status and `f224b498` failure for this combined
source. Passing engine-free tests alone does not approve Graphite's composition.

The executed neutral source mutation check temporarily made each targeted cached component's
`RefreshPalette` return without updating, ran the exact same seeded fixture and required its
cached-versus-fresh exact RGB guard to fail for changed neutral plates. The bounded
`WorkspaceRender.AppearanceCacheCheck` entrypoint invokes these same unchanged witnesses at both
sizes and presets, with the same mutable-state restoration, without unrelated composition galleries.
Neutral refresh mutations apply individually to label, peek and banner. The preserved state/practice
colours have no neutral pixel-change claim; their seeded invariance is checked separately. Each
mutation was restored before the final candidate run. No palette values or general render tolerances are loosened.

The hidden-side regression uses real SettingsHub/SettingsColumn Forget details and confirmation,
then the same stable file becomes focused, with and without its own side. The hidden retained
menu side cannot block fresh file draw/action proof or retain old Yes. Returning menu-owned
details regain the front after a fresh draw and block the file behind them. This is an
engine-free ownership regression. The October 12 combined regression uses the real FileColumn
instead of a synthetic focused column: an explicitly typed draft hides Appearance preview, and a
scoped delivered voice result focuses an ordinary file while real Settings Forget details stand.
Both text sizes exercise exact drawn Send, retained draft/read proof through Compact, stale keyboard
rejection, spoken reviewed Yes, hidden old Apply/Forget refusal, fresh Settings return and Compact
from returned Settings. One exact instruction payload is acknowledged locally; no runtime completion
is claimed. It verifies the core lifecycle combination, not physical visibility or wearer fit.


## Independent workspace appearance validation (2026-10-13)

At the current independent-console implementation, the component, full workspace and task-file
passes succeed in both presets at both real-font sizes. The same whole run exposed 1,560 failures
in the appearance-specific fixtures, which still placed the rejected six-agent arc and an invented
video window across the console. Those fixtures now retain their six journal task identities
independently from zero characters or one explicitly confirmed shelf. They no longer assert an
external window or use a character target as the task identity.

The normal ordinary-file pair keeps a visible preview. The specifically measured shelf pose and
file pair exercise `MenuAside`, preview rollback and refusal of hidden Apply; shelf presence alone
is not a visibility rule. Both cases retain the focused-input rollback and stale-frame refusal
on return, full field/type/target/occupied-body checks, and actual light-line checks wherever a
character exists. The character-free console does not invent a line to a nonexistent character.
All five preview specimens, local choice without saving, Cancel without saving, Apply once and
appearance-only Restore once remain exercised. Cache refresh, pixel and fade witnesses are unchanged.

`AppearanceFlowCheck` reuses these exact production flows and cache checks in both presets and
sizes; it passes after migration. Independent source review found no lost behavioral guard.
The subsequent complete `AppearanceCheck` also passes at `32169294`, including all component,
workspace and task-file galleries, the appearance flows and the cache witnesses in both presets
and sizes. The integrated repository check passes 1,172 tests, no failures or skips, plus type
checking and lint. Headset preset legibility, live activation and wearer comfort remain unverified
for this correction.

### Scoped character colour rendering witness

`Halcyonic.XR.Workspace.Editor.WorkspaceRender.CharacterColourCheck` exercises the production
Character colour route with a real `CharacterView`, `CharacterTarget`, body material property block
and rendered body pixels. It reuses the measured confirmed-shelf fixture and opts into the spatial
console solely for this editor witness. A synthetic welcome/snapshot is pumped through the actual
session watermark machinery; no transport starts, network request, runtime command or device is used.
The actual AppearanceControls save path is checked by reloading its bounded preference after Apply.
The witness fits the body only after opening the selected task and rejects the helper's diagnostic
fallback. Every shot uses the spatial composition checker, retaining actual character/label clearance.
The witness covers unchanged choice, visible preview, exact Cancel rollback, Apply, configured/source
mismatch, session and watermark loss, target removal, and menu-only destruction leaving the body alive.
Its nested cleanup restores the exact presence and typed values of both appearance preferences,
and restores the interface palette, text scale, field and active render target even on failure.
The source-key delegate is an explicitly synthetic stand-in for ControlPlaneConnection's captured key;
this fixture does not verify actual connection creation. Edit-mode non-ExecuteAlways objects do not
receive the play-mode destruction callback, so the witness explicitly invokes production OnDestroy
before destroying the menu and checks the surviving body before and after destruction. That verifies
cleanup effects, not play-mode event delivery. The first host run failed: the fixture used the legacy
separated-header checker, fitted against the wider chooser and continued with the helper's invalid
fallback, and assumed edit-mode callback delivery. A subsequent run passed the colour assertions
but encountered a native Editor shutdown failure and an inactive-target geometry fixture error.
After restricting geometry to active characters, matching the existing legacy checker, the actual
run passes every colour/material/pixel/persistence assertion at both reading sizes and exits cleanly.
None of this establishes wearer acceptance or actual connection-key capture.
