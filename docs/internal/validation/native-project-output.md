# Native project links to installed apps

- Date: 2026-10-13.
- Question: Can a person explicitly link an installed headset app to a software project and open
  it while retaining the native panel's selected work and drafts?
- Method: current official Android/Meta documentation, independent source review, real Android API
  compilation and consumed JVM/ephemeral-host regressions.
- Status: source/API and focused regression checks verified; actual Android view, private filesystem
  and Quest app discovery/launch behavior remain unverified. No device, installed application launch,
  provider or model was run for this record.

## Verified platform facts

Android's [package visibility guide](https://developer.android.com/training/package-visibility/declaring)
supports an intent signature under `<queries>` for discovering matching activities. This feature
does not require all-package visibility. The
[PackageManager reference](https://developer.android.com/reference/android/content/pm/PackageManager?hl=en)
defines `queryIntentActivities`; flags zero keep normal filtering, while `MATCH_DEFAULT_ONLY`
would require DEFAULT and omit ordinary launcher activities without that category.

The [Intent reference](https://developer.android.com/reference/android/content/Intent) defines
`makeMainActivity(ComponentName)` as a normal MAIN/LAUNCHER intent targeting an explicit component.
`NEW_TASK` can be added without clearing the destination's task. Explicit components do not use
implicit resolution to substitute another activity. Android's
[visibility use cases](https://developer.android.com/training/package-visibility/use-cases?authuser=2&hl=en)
recommend handling `ActivityNotFoundException`; launch is still subject to platform and component
permissions, and a successful call does not establish that the destination rendered.

Meta [prohibits QUERY_ALL_PACKAGES](https://developers.meta.com/vr/resources/permissions-prohibited/).
The [hybrid guide](https://developers.meta.com/vr/documentation/spatial-sdk/hybrid-apps-overview/)
requires a LAUNCHER fallback and documents a panel's OVERLAY_LAUNCHER entry inside another immersive
app. It does not prove that an already-visible panel stays visible across another app's launch,
nor whether every Quest app appears through the standard Android launcher query. These are device
questions; empty/unsupported discovery must keep the person's existing Home route available.

## Consumed boundary

`PanelProjectOutput` defines the native-only private record and consumed launch ordering. A strict
version-one store retains at most 128 unique connection/journal/project links within 64 KiB UTF-8.
Each link contains only the chosen package and exact launcher activity. The binding uses the
existing private connection digest; no token, host path, repository data, app label, app inventory,
draft or vendor session is stored in this record. Corrupt/unreadable data is unavailable rather
than silently overwritten as empty. Only a real absent file creates an empty store.

`PanelFiles.LINKS` uses the established descriptor-verified no-follow read and private atomic write:
0600 temporary file, file sync, rename and verified parent-directory sync. Removing a link rewrites
the bounded candidate and preserves every other scope; no application is uninstalled or changed.

`PanelAppLauncher` performs MAIN/LAUNCHER discovery only for the explicit chooser. It excludes this
app and unavailable, nonexported or permission-protected activities the caller cannot start.
Duplicate components are collapsed, labels are bounded/plain outside text, and identity is the
package/activity pair rather than the label. A displayed limit names truncation. Later availability
checks query only the saved package and require the exact launcher component; a changed default
or new alias does not silently replace the link.

An in-flight action is bound to the original full selected connection/journal/project/task/run
tuple, Activity/dialog authority epoch, draft/editor baseline, generation and previous link.
Task/run may be absent for a project-only view. A persistent link remains project-wide, but an
action cannot move to a different task or revive after focus, mode, selection or pairing loss.
The Activity validates fresh host ownership/projection after awaits, then preserves both draft
routes and pending command identity before leaving. The shared opening helper checks authority
before and after availability and again after persistence, calls start once, and never retries.

The actual Linked app view names the selected project and transient installed app label. An explicit
chooser offers bounded launcher activities and adds package/activity identity only for ambiguous
labels. Choosing an app opens a short project/app Save link view. Open linked app itself authorizes
one attempt, and Remove link itself removes this local association: these reversible local actions
add no second confirmation or reading proof. Their exact lease/fresh tuple/component/draft-saving
gates remain in force; runtime command, approval and question reading guards are unchanged.
Unconfirmed atomic replacement reports an unknown save outcome and asks the person to reopen the
view, since rename may already have occurred before a failed directory sync.

The fresh launch intent carries only MAIN, LAUNCHER, the chosen component and NEW_TASK: no extras,
data, ClipData, grants or task-clearing flags. Link/change/remove never launch. Local intent
acceptance is described as a launch request, not proof of visible output or latest source. This
consumer cannot inspect another app's data, capture its screen, build/deploy source, or grant a
runtime authority. The existing Workspace return route retains its separate fresh-state checks.

## Verification and remaining acceptance

The separate native output harness exercises the production codec, scope/lease and launch-order
helpers using synthetic identifiers and a test-only recording launch surface. It covers invalid/extra
and duplicate fields, byte/count limits, foreign bindings, project-only selection, every tuple change,
focus/top/pause/modal/editor/generation loss without revival, failed draft preservation, unavailable
components and launch refusal without retry. Android API compilation checks the real adapter and
Activity source. The already API-compiled adapter then runs against test-only recording Android
classes, covering disabled/nonexported/self/protected candidates, duplicate and same-label entries,
label/count bounds, exact saved-package query, no replacement alias, normal explicit intent shape
and launch refusal. These recording classes are never included in production compilation or APKs;
they do not establish Android intent marshaling or platform lifecycle behavior.

The focused `tooling/panel/panel.test.ts` plus `tooling/panel/output.test.ts` run passed 94/94 tests,
two suites, zero failed/cancelled/skipped/todo, in 32.6 seconds. It compiled both production and
development source sets against Android API 36 using Java 11. TypeScript type checking and scoped
Biome checks passed. A subsequent output-only 5/5 rerun passed after the test traversal explicitly
selected UTF-8 names for TypeScript typing; production source was unchanged. Existing native
voice, compact-mode, both-draft/pending-reference and real ephemeral paired-host protocol witnesses
remained passing. Full repository and C# checks belong to the final integration gate.

Independent review examined the actual Activity consumer and original-owner reads, mode/input
authority, picker callbacks, latest-store lock, persistence ordering and unknown post-rename outcome;
no actionable source blocker remained after that outcome message was corrected. Final packaging
must retain only the narrow launcher query and no prohibited package/install permissions. Device acceptance must exercise an
actual installed app, same-label entries, uninstall/update or missing activity, draft-save refusal,
permission/platform refusal, native panel launch/return, Home fallback and process recreation.
Installed/source build correspondence remains unknown even after a successful launch witness.
