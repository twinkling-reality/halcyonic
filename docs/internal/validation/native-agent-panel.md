# Native agent panel

2026-10-13 addition: an expanded selected task offers **Read results**, a read-only native view of
accepted final command/file-change records. The list and exact event reads use the existing pinned
or development proof transport with the original connection/journal/project/task/execution.
Each fresh host read and callback must retain that selection, runtime, modal/editor/draft/pending
identity, generation and focused expanded lifetime. Opening the view first saves both ordinary
drafts; no reading-proof confirmation or command is introduced. A file-change record offers a
short file list and one selected patch; recorded bodies preserve LF/indentation/blank lines and
literal backslashes, with other control/ignorable characters shown literally. Content pages retain
the immutable event. Support availability is distinct from connection health and earlier recorded
facts remain readable. Native source, API and JVM/HTTP evidence and hardware limits are in
[recorded-tool-results.md](recorded-tool-results.md); device interaction is unverified.

Verified 2026-10-12 against the local producer and consumer source, Unity 6000.3.25f1's Android
API 36 jar and OpenJDK, and Meta's current hybrid-app documentation. The focused JVM checks use
fresh, ephemeral control planes and synthetic credentials. No device launch, Wi-Fi pairing,
native visual capture, agent provider request or Android APK build is evidence in this record yet.

## Platform contract

Meta supports a regular Android 2D activity beside an OpenXR immersive activity in one APK on
Horizon OS 69 or later. The production manifest now raises the installed `horizonos` namespace's
minimum from 60 to 69, retains target 207, and declares `AgentPanelActivity` with `2D` and
`OVERLAY_LAUNCHER` categories, a separate task affinity, and a resizable 640 by 720 dp default.
`PanelInBuilds` copies the native sources into development and release Gradle projects. The
development-only glance remains a separate package and retains its release exclusion check.
[Meta hybrid apps](https://developers.meta.com/vr/documentation/spatial-sdk/hybrid-apps-overview/).

`PanelBridge.open` uses an explicit panel intent with `NEW_TASK`. The Home path uses Meta's
documented immutable `PendingIntent` in `extra_launch_in_home_pending_intent`. Neither path ends
the immersive task. Explicit return targets `UnityPlayerGameActivity`, then removes the panel's
task. The returned result describes the actual launch exception, or a bounded timeout with an
explicit uncertainty message. It does not claim that a particular other app opened or received
focus. A visible 2D panel over another immersive app is an OS-supported surface, not an arbitrary
overlay or authority to control that app. Horizon's Virtual Display focus and pause rules remain
in force. [Unity lifecycle](https://developers.meta.com/vr/documentation/unity/unity-lifecycle/),
[input focus requirement](https://developers.meta.com/vr/resources/vrc-quest-input-4/),
[Android tasks](https://developer.android.com/guide/components/activities/tasks-and-back-stack).

The JNI bridge uses Unity's managed `AndroidApplication.currentActivity` without disposing it.
Launch runs on Android's UI thread and reports failure to the Unity caller. OS launcher intents
carry no credential, selection, draft, agent text or command extras; the exported activity ignores
incoming extras. [Unity AndroidApplication](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Android.AndroidApplication-currentActivity.html).

## Connection and authority

The production client independently reads the existing app-private `halcyonic-pairing.json`.
Each read opens without following links or blocking on a pipe, checks a regular file owned by
this app with no group/other permissions on the same descriptor, and applies a 16 KiB limit and
strict UTF-8/JSON parsing. This is the same storage boundary chosen by `ControlPlaneSettings` and
`FilePairingStore`; it does not import a credential from Unity intents or public storage.

`PanelTransport` connects directly, uses TLS, verifies the exact paired leaf certificate SHA-256
before writing any HTTP header, and rechecks connection identity immediately before bearer and
after the response. It neither follows redirects nor silently retries a command. It rejects
ambiguous headers, unsupported compression/chunking, invalid UTF-8, bodies over 8 MiB and command
bodies over 256 KiB. Address lookup has a bounded wait and a single bounded resolver worker;
connect/read limits and a 15-second watchdog constrain the whole network request, including a
blocked write. It closes the raw socket before the TLS wrapper, so TLS shutdown cannot wait for
the blocked writer. Each request owns its deadline/socket lease. Cancellation closes that lease.
A lost response after a POST begins has an unknown effect, not a fabricated
failure or success. A 401/403 clears the usable projection and review. The existing host checks
paired-device revocation on each request and before acting; the Java client does not invent its
own revocation state. See [network-pairing.md](network-pairing.md).

Only a debuggable APK offers an explicit **Connection (development)** choice. USB mode uses the
existing private `access-token` at `127.0.0.1:47800`; it asks the public health endpoint for the
existing HMAC loopback proof and consumes the bounded response on the same socket before sending
bearer. It never overwrites the token or pairing, chooses USB as a fallback, or enables USB in a
release APK. Its deliberate mode record holds only a boolean. Paired and USB connections have
different continuation keys; Unity must be using the same target for a return to restore context.
See [xr-loopback-proof.md](xr-loopback-proof.md).

Connection selection is local recovery navigation, so a missing paired computer or an awaiting
projection cannot disable the development chooser. Its opener and three choices still require
an expanded, resumed, top and focused Activity/dialog with current owner and generation, no
pending network request, and the existing save-before-leave guard. This grants no task command
or review authority; those still require a fresh matching projection. The native panel reads
`access-token` directly for explicitly selected USB mode; the older glance token is unrelated.
Opening the panel from Unity preserves ordinary context and does not silently select USB.

## Work and continuity

The panel reads `GET /api/snapshot`, reads the first 50 recorded task events through
`GET /api/events`, and sends existing `POST /api/commands` envelopes. Project/task selection is
local context; it does not create work. Execution badges use the runtime's own `synthetic` flag,
including simulated executions in a live journal. Activity says its bounded range and leaves
provider Changes/Checks exploration in Workspace. Both views now expose bounded recorded native
command/file-change results; those are separate from provider assessment and current-file proof.

Instruction, stop, approval and complete question answers require deliberate user actions and
declared runtime capabilities. Instruction and agent request reviews offer the entire safe plain
text in a scroll area before confirmation. Shortened approvals can only be denied. Secret,
shortened or ambiguous questions cannot be answered. Single/multiple choices and permitted free
text are checked together for every prompt. No question draft, read proof, approval callback or
confirmation is persisted. Immediately before every command, a fresh snapshot must match the
journal, project, workstream and current execution; approvals/questions must also match the exact
reviewed request and capabilities. Host acceptance remains acceptance until the runtime confirms.
Failed, malformed and unknown-status acknowledgements have no acceptance fallback.

Confirmation captures immutable connection/journal/project/workstream/execution context and a
review epoch. Dismissed or replaced dialogs cannot authorize stale callbacks, including Stop.
The Activity uses resumed, top-resumed, activity-window and dialog-window focus together;
onPause alone is insufficient on Android 10+ multi-resume. Focus loss expires read/confirmation
authority. An active keyboard serving this panel's own EditText may retain ordinary editing, but
never revives a confirmation. Final question review repeats every header, question, offered
option/description and answer after editing. Back remains usable during reads, and Workspace/
Other apps can cancel pending network work without waiting for a poll.
[Android multi-window lifecycle](https://developer.android.com/develop/adaptive-apps/guides/support-multi-window-mode),
[InputMethodManager](https://developer.android.com/reference/android/view/inputmethod/InputMethodManager).

`WorkspaceContinuation` is generated from the one TypeBox contract. Native parsing consumes the
shared valid/invalid fixtures, rejects duplicate keys, extra properties, coercions and excess
bytes, and uses the same private connection digest as C#. The digest includes credential rotation
and is never displayed, logged or sent. `halcyonic-workspace-panel.json` stores ordinary selected
context; `halcyonic-workspace-to-panel.json` stages inbound context separately, so a native autosave
cannot overwrite a new Unity handoff before `onNewIntent`. Inbound context is installed only
against a fresh matching projection. `halcyonic-workspace-return.json` is a separate explicit-return
marker. Peek preserves
the return during reconnect/process death; acknowledgement deletes only the matching handoff id.
A new outbound launch refuses while an earlier return waits; save and launch failures never erase
that return. The Unity caller must wait for a fresh authoritative projection before applying or
dispositioning it, then acknowledge its exact handoff id. Android may
kill a stopped process, so memory alone is insufficient.
[Android activity lifecycle](https://developer.android.com/guide/components/activities/activity-lifecycle).

Native editor storage separately retains ordinary drafts per connection/journal/workstream/
execution within 4 MiB, without a draft-count cap or eviction. Navigation saves or refuses to
leave the editor; a new execution cannot receive an old draft automatically. Earlier-run drafts
remain readable and require explicit reviewed copy to the new run. A full or unreadable store
leaves the existing saved records intact. Only the selected context crosses back to Unity.
Failed pause saves keep the bound ordinary draft in volatile recovery across same-process
resume; disk reload cannot replace it with an older draft. The storage warning stays visible
through polling until persistence succeeds. The selected private record remains an additional
recovery path when the aggregate editor store is full. If storage refuses every write, process
death before successful persistence cannot guarantee recovery; the panel does not claim otherwise.

Unity's retained views use a separate fixed `halcyonic-unity-workspace-views.json` record through
`AndroidPanelBridge.ReadWorkspaceViews`/`WriteWorkspaceViews`. The native read applies the same
no-follow/nonblocking descriptor, app ownership, private mode, regular-file, strict UTF-8 and
maximum-plus-one read boundary, with a 4 MiB aggregate limit. Only `ENOENT` at open means absent;
unreadable, symlink, special-file, over-limit and decoding failures are failed IO. An available
but malformed record remains available for the C# codec to reject, rather than becoming empty
storage. Both C# and native consumers validate the ordinary `WorkspaceViewStore` envelope before
atomic mode-600 replacement: unique continuation ids, exact selected membership, no extra fields,
no coercions and no record-count cap. The API accepts no caller filename and consumes no pending
native return. Engine-free recovery and fresh projection validation remain the Unity consumer's.

## Privacy and capture boundary

The native panel and its review dialogs use `FLAG_SECURE`. This is an explicit product privacy
decision: the entire panel can contain private project titles, task objectives, unsent instructions
and agent requests, and there is no reliable field-level rule saying only a password is private.
It also stays out of recent-app thumbnails. This blocks ordinary OS screenshot/screen-share
capture of the panel, and the development-only offline synthetic preview provides a deliberate
capture path for visual verification. The preview consumes a fixed public mock Snapshot through
the actual native renderer in a separate, internal development Activity and task. That window
has never held private work: its initialization reads only the fixed APK asset, creates no
private store or connection, ignores intent extras, sends no request and writes no selection
or draft. The real panel never clears `FLAG_SECURE`, including while preview is in front;
real review dialogs set it before showing. Java references being cleared would not establish
that a previously private compositor buffer had been replaced, so the preview does not reuse
the private window. Its Activity, source and asset are excluded from release builds and removed
before a release reuses the Gradle project. The native secure panel itself still needs an actual Quest display check:
Android documents blanking on non-secure displays, and Meta has not verified this flag for
Horizon virtual displays in the sources reviewed. It is not a claim that every system or
physical-camera capture is prevented.
[Android FLAG_SECURE](https://developer.android.com/reference/android/view/WindowManager.LayoutParams#FLAG_SECURE).

At this original checkpoint, no capture, microphone, accessibility, overlay, all-package inventory
or notification permission was added. Development builds now have the separately consented held
dictation consumer described in [native-panel-voice.md](native-panel-voice.md); release builds exclude
its microphone permission, and native capture still needs device acceptance. The explicit installed
app chooser uses only a narrow MAIN/LAUNCHER visibility query, described in
[native-project-output.md](native-project-output.md), without QUERY_ALL_PACKAGES. No cloud
transcription service was introduced. Meta's MediaProjection policy permits casting, livestreaming and screen
sharing; whether assistant analysis fits an approved purpose remains open. Android's projection
consent/token/service requirements, protected surfaces and Meta's one-projection limit must be
resolved before a real attachment flow. Opening this panel grants no capture permission.
[Meta MediaProjection](https://developers.meta.com/vr/documentation/native/native-media-projection/),
[Android projection](https://developer.android.com/media/grow/media-projection),
[Meta prohibited permissions](https://developers.meta.com/vr/resources/permissions-prohibited/).

## Evidence and remaining acceptance

`tooling/panel/panel.test.ts` compiles every native source against Android's API and runs the
actual plain-Java consumer. The first 46 cases passed before the separate-preview follow-up:
shared continuation/identity parity; native
capability, stale request, secret and full-answer rules; retained drafts and aggregate byte bound;
real ephemeral paired and loopback snapshot/events/command framing; revocation; wrong pin,
replacement connection, redirects, ambiguous headers, partial command reply and proof impostors.
The actual Activity-used authority/recovery controllers have ordered regressions for retargeting,
dismissed callbacks, focus/multi-resume revival, failed pause save and a new singleTask selection
waiting for its fresh projection. A blocked socket-write fault is injected after real TLS pin
authentication: the production watchdog closes it at about 15 seconds, reports unknown effect
and sends no retry. A direct stalled-peer attempt on this JVM filled its buffers and reached the
read timeout instead; the deterministic fault checks the write phase rather than claiming that
the kernel stall was reproduced. All four native actions are accepted and runtime-confirmed
through the real ephemeral paired producer with its explicitly synthetic mock runtime.
TypeScript type checking and scoped Biome checks pass. These are source/API and host-protocol
checks, not Android view, JNI, packaging, IL2CPP or headset-runtime certification.

The separate-preview follow-up adds one privacy boundary case (47 cases total). Three focused
cases passed: both production-only and development Android source compilation, no clearing of
the real window's secure flag with an internal development-only preview declaration, and the
fixed asset's Snapshot/synthetic execution validation. These checks establish source boundaries,
not compositor pixels or final APK manifest merging.

The fixed Unity storage bridge adds four cases (51 native cases total). Five focused cases pass:
production/development Android API compilation; shared continuation parity inside the aggregate
envelope with more than sixteen records; malformed/duplicate/selection negatives; the aggregate
byte refusal; and source checks for the fixed filename and absent-versus-failed descriptor path.
These establish format behavior and source/API boundaries. Actual Android syscall faults, JNI
dispatch, process-death recovery and headset disk/render performance still require device evidence.

Preliminary APK packaging was checked on 2026-10-12. A development build from `f60dec7a`
contained the native activity and separate internal synthetic preview activity and asset. A
release build from `572e1b00` succeeded at 74,325,937 bytes, SHA-256
`8463e9051a2589f7f88e90a33b0e5c65286a5bb74a9a32f99f6f2a4c8413a284`.
Its actual manifest and DEX contained the production activity, view-store validator and the
`PanelBridge` open, Peek, exact-Ack and private-view read/write entry points. The preview activity,
preview class and asset, microphone permission and media-projection service permission were absent.
These builds were not installed and do not establish JNI dispatch or compositor behavior. Their
source revisions precede later recovery and request-loop changes; final packaging must be checked
again on the source selected for installation.

Still required: final independent consumer review and integration gates; final APK packaging and
actual JNI dispatch; synthetic native visual proof; headset launch/Home/return and
process-death recovery; explicit USB installed journey; real Wi-Fi pairing, pin failure and
revocation; native keyboard and complete review with hands/controllers; focus over another
immersive app, including `FLAG_SECURE` on the actual Horizon virtual display and native keyboard
focus callbacks. Expanded polling stops on pause or loss of top-resumed ownership. Visible Compact
may continue bounded read-only snapshot polling without top/input focus; pause stops it, and no
hidden input, command or result-review authority survives.
Background notifications and arbitrary
app control are not implemented capabilities.

## Native workspace request consumer

The actual native task page now offers a separate workspace-request surface. Its ordinary editor,
spoken-origin label, page and saved request reference remain separate from task instructions.
The page reads host availability and verifies every exact request response against the current
connection, journal, project, task and execution. Older-host 404/unavailability retains words.
Reported replies and Actions are separate from task completion; Stop planning does not stop the
runtime. Submit, Resume and Cancel bind immutable reviewed context and recheck fresh host state.
Availability and action refresh preserve the active editor instead of replacing its view.

Pending request command IDs are saved to the ordinary private stores before POST. Every POST ACK,
including a completed duplicate, leaves the pending reference until an authenticated original-owner
command GET proves its journal, UUID, exact type and full task tuple. Missing, unavailable, foreign,
malformed or unknown-effect receipts preserve that ID without resubmitting. A rejected receipt
without an admitted target scope cannot prove the saved task binding. Earlier-run word copy is
refused while the current send remains unresolved. Back expires the owned operation; stale
callbacks cannot retain its busy lease or import a result into another selection.

The fixed private `halcyonic-native-request-sends.json` contains only command identity, full tuple,
expected request/type and `unsent`/`unknown` phase, under a 4MiB aggregate/parser bound. It stores
no credential, envelope or words. Atomic `unsent` is written before ordinary pending stores;
atomic `unknown` must succeed before the first POST byte. Failed phase writes block POST.
Only an exact matching unsent record offers deliberate cleanup after ordinary stores clear;
missing/corrupt/mismatched records and unknown attempts never grant cleanup or retry. Unrelated
entries survive replacement. These records provide local input safety, not domain lifecycle facts.

Seven focused checks passed with zero skipped: canonical producer offer/request consumption and
scope negatives; honest command outcome parsing; persistence/phase-before-POST and one attempt;
duplicate-completed ACK with unavailable owner GET; four partial-store/phase failures and restart;
copy with an unresolved ID; and actual ephemeral paired-host availability/rejection/revocation.
Production/development Android API and plain harness compilation passed, as did TypeScript
typecheck and scoped lint. The paired-host case deliberately had no coordinator/model and was
unavailable: it does not claim a positive native model journey. Faults exercise the consumed
dispatch/phase/draft models, not actual Android filesystem or Activity instrumentation.

Independent exact-source review and integrated gates remain required for this checkpoint.
Actual Android Back/focus/multi-resume/IME and crash recovery, secure Horizon rendering, JNI/APK
packaging, paired connected request controls and Unity/native/Unity return remain device gates.
File fsync/atomic replacement establish the source ordering; sudden device power-loss behavior
is unverified. At this checkpoint native input used EditText/system keyboard and preserved
speech-origin words from the shared handoff. The later development-only native held dictation
consumer is documented in [native-panel-voice.md](native-panel-voice.md); its hardware microphone,
consent/lifecycle and audio return remain device gates. Capture/share
still requires a separate deliberate consent and real attachment path; none was invented here.

Independent review found that fsyncing the temporary record without syncing its renamed directory
entry could restore an older unsent phase after a crash. The actual private writer now requires
file sync, rename and held-directory sync in that order. Directory open uses public Android
O_RDONLY/O_NOFOLLOW/O_NONBLOCK/O_CLOEXEC flags, then verifies directory type, owner, device and
inode; public API-36 compilation rejected O_DIRECTORY rather than permitting an invented flag.
The held parent identity is checked around rename/sync. Any open/write/rename/sync error blocks
the phase transition and therefore POST, without rolling an unknown phase back to unsent.

Eight focused checks passed, including ordered temporary-write/rename/directory-sync faults,
with zero skipped. Production/development Android API+harness compile, typecheck and scoped lint
passed. The public syscall signatures were checked against the
[Android Os API reference](https://developer.android.com/reference/android/system/Os).
Actual Android directory-sync support and crash/power-loss recovery remain device evidence,
not a claim made by the JVM ordering regressions.


## Compact selected-task monitor

Source and focused host/API checks verified 2026-10-13.

The production Activity offers explicit **Compact panel** and **Expand task controls** actions
for its selected task. The choice survives Activity recreation as local presentation state; it is
not added to the shared continuation contract, journal or command envelope. Compact shows the
complete safe plain project/task identity, the current host-projected run state (or task state
without an agent), separate Live/Recorded and simulated provenance, exact permission-request and
agent-question counts, and when the authenticated snapshot was last checked. Both compact and expanded views use the same status labels; Completed labels the current
execution's status, not certified software completion. Missing or foreign project/task bindings refuse the summary.
The Expand/Compact action remains outside the content scroll, including for long task titles.

Before either transition the existing aggregate and selected private stores must retain both
independent ordinary drafts, spoken origin/read part and the exact pending request command UUID.
A save failure refuses the transition. Successful transition expires old review authority, voice
and network generations before hiding the editor. Compact offers no instruction/request editor,
review, send, stop, answer or microphone action. Expand requires a new authenticated snapshot
before any action controls return; top-resumed and current window/dialog focus still gate every
command, review and voice lease. Existing immutable tuple checks, phase-before-POST ordering and
unknown-effect/no-retry rules are unchanged.

While the compact Activity remains resumed and visible it can perform the existing bounded
read-only snapshot GET even when another app owns top/input focus. Losing top/focus continues to
revoke all input and command authority. Pause/stop cancels polling immediately and labels the
displayed checked-at state as paused; it does not claim background liveness. Android documents
multi-resume from API 29 and manufacturer variation; actual Horizon lifecycle behavior remains a
device acceptance item. [Android multi-window lifecycle](https://developer.android.com/develop/ui/compose/layouts/adaptive/support-multi-window-mode#lifecycle).

This is content compaction within the existing secure Activity, not a programmatic OS-window
resize. The manifest remains resizable with 640 by 720 dp default and 480 by 480 dp minimum; the
person uses Horizon's window resizing. No manifest, permission, service, private storage schema,
transport or runtime capability is added.

Consumed authority/projection regressions cover failed transitions, hidden ticket revocation,
non-top read-only eligibility, pause, fresh-snapshot gating on Expand, exact selected identity,
provenance and decision counts, no-agent/unknown/finished states, and independent draft/pending
reference round trips. The final focused native suite passed 89/89 with zero skipped, including production/development
Android API-36 compilation and the actual plain-Java consumers. Type checking and scoped lint
passed. Independent source review found and closed a navigation refresh stall and an inert
selected synthetic-preview resume; consumed ordered regressions cover both repairs. Ordinary
focus return reoffers generation-expired controls from a fresh draw. Existing paired mock-runtime
protocol checks remain intact; no model, live host or device journey was run for this change.

These source/API checks do not establish Android view pixels or Horizon behavior. Native synthetic
480/640 layout proof and actual secure Horizon foreground/paused/recreated journeys remain
required, along with the final integrated gate and packaging.


## 2026-10-15: Native restoration text shimmer

The actual pending-restoration poll now calls an explicit loading-text method after the network
read is admitted, not from the mere presence of Checking copy. Its Android TextView draws a
translated LinearGradient through the letters using Glaze's 1.6-second period, 0.35 band and
0.55 cosine lift toward white. The pure `PanelWait` math is consumed by the gradient colour/offset
path. The text, layout and command authority do not change. Every ordinary setText clears the
shader, including current success/failure messages; current poll completion also clears it before
retention branches. Cancel network, focus/top loss, pause and detach stop animation. The existing
bounded transport supplies the real timeout/error, without a fake percentage or completion event.

A still wait uses Glaze's active foreground colour. Keep things still is mirrored from the actual
Unity ComfortControls.Apply path through a dedicated JNI method into an exact versioned boolean
record, `halcyonic-panel-motion.json`, at most32bytes. The existing no-follow app-private atomic
writer performs file sync, rename and directory sync. It uses no credential file, permission or
continuation. Unknown/corrupt/failed reads default to still. Movement additionally needs a successful
mirror in this process; a direct native process launch never trusts a previous motion value alone.
A failed write withdraws that process permission and attempts to remove the stale record. A later
still choice withdraws permission before IO. Disabled system animations also keep the wait still.

Official Android reference verified on2026-10-15:
[ValueAnimator.areAnimatorsEnabled](https://developer.android.com/reference/android/animation/ValueAnimator#areAnimatorsEnabled())
reports system-wide animator availability (API26), including duration-scale zero and battery saving;
[LinearGradient](https://developer.android.com/reference/android/graphics/LinearGradient)
documents the colour-stop shader and local matrix used here. Production and development sources
compile against Android API36. Focused consumed JVM checks cover active versus idle wait, still/
system-off/hidden suppression, motion through distinct phases, bounded lift, unknown preference
fallback, and stop/restart/stop. A negative mutation removing stop fails that regression, then the
restored source passes. The existing native suite passed90/90 with zero skips at the first source
checkpoint; final focused wait/API compilation checks pass after the colour and fail-safe follow-up.
Unity Editor and Android-symbol source compilation also pass. These checks establish source and
lifecycle policy, not visible native pixels, real JNI preference storage or wearer acceptance.
The native wait still needs an actual device observation while a permitted restoration read runs,
including failure exit and the still preference.

## Compact hierarchy and explicit control roles — October 15, 2026 candidate

The selected compact view omits the repeated product heading, duplicate project heading, global
navigation row and zero-permissions/zero-questions line. **Open task** is its single local control;
expanding restores Projects, Workspace and Other apps navigation. It retains task identity, actual state, source,
nonzero attention counts, pending-send uncertainty and the last-read freshness message. **Open task**
uses the same local expansion path and fresh-projection guard; **Keep a small view** uses the same
save-before-hide path. This reduces content density; it does not resize or position the OS window.
The manifest still declares a 640 by 720 dp initial window and a 480 by 480 dp minimum.
Meta's current [panel sizing guidance](https://developers.meta.com/vr/essentials/horizon-os-panel-sizing/)
and [AOSP panel contract](https://developers.meta.com/vr/documentation/android-apps/features-overview/)
were rechecked on October 15: the manifest declares initial dimensions and the OS manages user
placement/resizing. The compact content change does not establish a smaller physical window or a
comfortable placement beside video. Those dimensions and the actual resize interaction still need
wearer validation; no dynamic resize API is claimed or invoked by this change.

Button emphasis is assigned by its action site, never inferred from label text. In particular,
outside project/task names cannot become primary buttons by starting with “Send”. Final reviewed
continuations receive primary emphasis and remain disabled until the unchanged reading and
current-authority checks allow them. Native rest, pressed, focused and disabled appearances use
Android state lists, with disabled matched first. Source review found no command/authority changes.

The Android API 36 production/development compilation and all 92 focused native checks passed.
The state-list APIs were checked against the current official
[StateListDrawable](https://developer.android.com/reference/android/graphics/drawable/StateListDrawable)
and [ColorStateList](https://developer.android.com/reference/android/content/res/ColorStateList)
references. Actual native visuals, hand focus, OS window placement and visibility beside video
still need device validation; these source and JVM checks do not establish wearer acceptance.
