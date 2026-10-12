# Native agent panel

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
connect/read limits and a 15-second network deadline constrain each request. Cancellation closes
the active socket. A lost response after a POST begins has an unknown effect, not a fabricated
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

## Work and continuity

The panel reads `GET /api/snapshot`, reads the first 50 recorded task events through
`GET /api/events`, and sends existing `POST /api/commands` envelopes. Project/task selection is
local context; it does not create work. Execution badges use the runtime's own `synthetic` flag,
including simulated executions in a live journal. Activity says its bounded range and leaves
full changes/checks/output exploration in Workspace.

Instruction, stop, approval and complete question answers require deliberate user actions and
declared runtime capabilities. Instruction and agent request reviews offer the entire safe plain
text in a scroll area before confirmation. Shortened approvals can only be denied. Secret,
shortened or ambiguous questions cannot be answered. Single/multiple choices and permitted free
text are checked together for every prompt. No question draft, read proof, approval callback or
confirmation is persisted. Immediately before every command, a fresh snapshot must match the
journal, project, workstream and current execution; approvals/questions must also match the exact
reviewed request and capabilities. Host acceptance remains acceptance until the runtime confirms.

`WorkspaceContinuation` is generated from the one TypeBox contract. Native parsing consumes the
shared valid/invalid fixtures, rejects duplicate keys, extra properties, coercions and excess
bytes, and uses the same private connection digest as C#. The digest includes credential rotation
and is never displayed, logged or sent. `halcyonic-workspace-panel.json` stores ordinary selected
context; `halcyonic-workspace-return.json` is a separate explicit-return marker. Peek preserves
the return during reconnect/process death; acknowledgement deletes only the matching handoff id.
The Unity caller must wait for a fresh authoritative projection before applying it. Android may
kill a stopped process, so memory alone is insufficient.
[Android activity lifecycle](https://developer.android.com/guide/components/activities/activity-lifecycle).

Native editor storage separately retains ordinary drafts per connection/journal/workstream/
execution within 4 MiB, without a draft-count cap or eviction. Navigation saves or refuses to
leave the editor; a new execution cannot receive an old draft automatically. Earlier-run drafts
remain readable and require explicit reviewed copy to the new run. A full or unreadable store
leaves the existing saved records intact. Only the selected context crosses back to Unity.

## Privacy and capture boundary

The native panel and its review dialogs use `FLAG_SECURE`. This is an explicit product privacy
decision: the entire panel can contain private project titles, task objectives, unsent instructions
and agent requests, and there is no reliable field-level rule saying only a password is private.
It also stays out of recent-app thumbnails. This blocks ordinary OS screenshot/screen-share
capture of the panel, and needs a separate, deliberate synthetic development-render path for
visual verification. It is not a claim that every system or physical-camera capture is prevented.
[Android FLAG_SECURE](https://developer.android.com/reference/android/view/WindowManager.LayoutParams#FLAG_SECURE).

No capture, microphone, accessibility, overlay, app-enumeration or notification permission is
added. Native voice input is not implemented; the existing local `/api/transcriptions` engine is
the available future path, with separate microphone consent and review. No cloud transcription
service was introduced. Meta's MediaProjection policy permits casting, livestreaming and screen
sharing; whether assistant analysis fits an approved purpose remains open. Android's projection
consent/token/service requirements, protected surfaces and Meta's one-projection limit must be
resolved before a real attachment flow. Opening this panel grants no capture permission.
[Meta MediaProjection](https://developers.meta.com/vr/documentation/native/native-media-projection/),
[Android projection](https://developer.android.com/media/grow/media-projection),
[Meta prohibited permissions](https://developers.meta.com/vr/resources/permissions-prohibited/).

## Evidence and remaining acceptance

`tooling/panel/panel.test.ts` compiles every native source against Android's API and runs the
actual plain-Java consumer. Its 40 cases pass: shared continuation/identity parity; native
capability, stale request, secret and full-answer rules; retained drafts and aggregate byte bound;
real ephemeral paired and loopback snapshot/events/command framing; revocation; wrong pin,
replacement connection, redirects, ambiguous headers, partial command reply and proof impostors.
TypeScript type checking and scoped Biome checks pass. These are source/API and host-protocol
checks, not Android view, JNI, packaging, IL2CPP or headset-runtime certification.

Still required: independent source review; integration gates; actual development/release APK
packaging and JNI inspection; synthetic native visual proof; headset launch/Home/return and
process-death recovery; explicit USB installed journey; real Wi-Fi pairing, pin failure and
revocation; native keyboard and complete review with hands/controllers; focus over another
immersive app. Foreground visible polling stops on pause. Background notifications and arbitrary
app control are not implemented capabilities.
