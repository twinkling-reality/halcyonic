# Workspace request verification

## 2026-10-12: Journal contract and authority boundary

The first implementation stage defines the general request commands, lifecycle events and
bounded request read model from TypeBox, with generated schema/C# bindings and typed client API.
It complements [ADR 0030](../decisions/0030-workspace-requests-use-a-journaled-agent-tool-loop.md).
The host coordinator, authenticated routes and actual request UI are pending at this stage; no
end-to-end agent outcome is claimed. Without an attached coordinator commands are refused.

The journal binds each request to its original principal, project, task and actual execution.
Prepared mutation records admit only `execution.send_instruction`, preserve the original envelope,
step identity, observation cursor and active native turn, and cannot be overwritten by another
payload. The latest 64 public receipts are bounded; all prepared action receipts remain discoverable
in the private projection for restart reconciliation. An instruction delivery receipt does not
complete a software task, and reported model reply does not change execution outcome.

Original request/resume identity uses a process-keyed raw envelope digest, bound to the sending
principal and existing resend limit. Raw text and digest keys are never journaled/logged. Another
principal's correct and incorrect guesses both conflict. Held-secret rotation does not invalidate
an exact resend in that process; restart/eviction without the digest fails closed. Requests are
journaled with held values redacted, without comparing redacted text as a duplicate oracle.

Focused source tests: four domain replay/schema/retention/immutable-binding cases, one real
control-plane raw-digest/rotation/restart/foreign-principal/rate-limit case, and 20 existing
control-plane cases passed (25/25). TypeScript typecheck and contract generation passed.
Live model, native runtime, Unity, device and visual acceptance are separate and unrun here.

## 2026-10-12: Native local tool transport

`workspace-agent/ollama.ts` implements the native structured tool protocol described in Ollama's
[chat API](https://docs.ollama.com/api/chat) and [tool-calling guide](https://docs.ollama.com/capabilities/tool-calling),
rechecked on this date. It verifies the configured loopback model's tags and declared tool support
before each inference. Metadata status reads do not invoke the model. There is no remote fallback,
model download, text-to-command parser or tool execution inside this transport.

The whole request, metadata/body reads included, has a 45-second bound and cancellation. Input is
snapshotted before awaiting metadata and bounded to 12 KiB; replies to 128 KiB with strict UTF-8.
Context/output are 8192/1024 tokens. Incomplete responses, exhausted generation/context, foreign
model names, unoffered calls, malformed argument objects and duplicate calls fail the whole turn.
Only a complete assistant response reaches the coordinator, which must validate each tool's full
schema and current authority before dispatch. Error text never relays the model's input or output.

Seventeen focused transport and existing companion checks passed, along with typecheck and scoped
lint. A real local Ollama 0.34.4 probe through this transport explicitly selected local `qwen3.5:9b`:
two model turns, one schema-valid synthetic read-only call, then a reply identifying the pending
human review, in 1.557 seconds. The actual API adds an opaque `id` beside a tool call's `function`;
the initial strict parser refused it, then the implementation and regression test were corrected
to preserve this bounded reference only within transient model conversation. It never becomes a
domain command identity. The probe exposed no action tool, read no real project, and logged only
shape/count/timing metadata. It establishes protocol compatibility, not coordinator, runtime or
headset task completion. Those consumers and their end-to-end verification remain pending.

## Separate local request continuation

The TypeBox continuation defines a nullable request reference/editor separately from ordinary
instructions. It retains the exact UUIDv7 host request reference, draft text, spoken origin and
editor part, with no read/armed/send/approval fields. Shared current fixtures cover submitted and
unsubmitted requests and invalid identities/coercions/authority. The C# reader migrates only a
missing `request` field from earlier private version-1 records to null and missing `surface` to
`task`; the current schema still requires their explicit presence. A request with no bound execution cannot pass current-context
validation. Durable view storage retains both drafts within the existing aggregate byte bound.
These schema/codec facts are separate from production capture, request-owner/tuple checks after
host reads, native return, process recreation and actual headset acceptance.

### Native continuation retention

The production Java reader now consumes the nullable request draft/reference separately from
instruction words, including spoken origin and ordinary page. Only the missing legacy v1
`request` key migrates to null and `surface` to `task` before strict validation. Request-only records remain in the bounded
private draft store, and capture/selection copies the request only across the original complete
connection/journal/project/task/execution tuple. This is retention plumbing; the native general
request controls and their authenticated exact-tuple response check are not yet implemented.

Four focused checks passed: actual production/development Android API compilation, legacy omission
and strict missing/extra rejection, request-only durable retention, and every tuple component's
copy refusal. Device handoff and actual request UI remain unverified.

### Explicit selected surface

The ordinary continuation stores `surface: task | request` independently of its instruction and
request drafts. This prevents an inactive request from taking over a later Activity selection after
recovery. Shared fixtures exercise both selections with both drafts, and reject null, numeric and
unknown selectors. The C# and Java readers migrate only a missing legacy selector to `task` and
retain both drafts. Current-context matching requires an execution for the request surface even
when its editor is empty. Actual production capture/restore consumers remain a separate check.

Independent source review found a C# UTF-16-length mismatch with TypeBox and Java codepoint
limits. The C# reader now counts supplementary pairs as one codepoint. Compact shared Unicode
boundary vectors exercise both instruction and request drafts through all three readers at and
above 32,000 codepoints, without weakening the existing byte limits.
Native encoding preserves valid supplementary pairs as UTF-8 rather than expanding each into two
Unicode escapes; isolated surrogate code units remain escaped. Accepted maximum-size drafts are
also written through the native draft store and read again, since parse-only acceptance does not
prove their serialized form stays within the same continuation byte bound.

Seventy-one contract/native checks and seventeen C# continuation/storage checks passed, with
zero skipped, after both corrections. Typecheck, contract generation and scoped lint passed.
Independent source review found no remaining selector/codec issue. The instruction restore and
native input consumers have separate Unicode-limit follow-ups; these codec results do not claim
the unfinished request controls or headset return path are accepted.

## Host coordinator and consumed read routes

The production host now attaches a journal-backed coordinator and consumes the bounded local
Ollama tool transport. `GET /api/workspace/agent` reads local configured model/tool metadata only;
`GET /api/workspace/requests/:id` requires the original authenticated principal. The first scope is
one existing current task execution. The typed catalog provides scoped workspace/event/provider
reads, instruction continuation, observed receipts and a reported question to the person. It has
no shell, approval-answer, secret-answer, credential, arbitrary-network or other-project tool.
Instruction offering uses current declared capability. Every dispatch retains the original
principal through `CommandService` and rechecks context after awaits and before action.

Model-only tool turns may have empty content: no blank/invented reported reply is journaled. A
clarification tool's actual question is retained as reported text. Provider conclusions remain
transient read-through. Outside results are explicitly shortened when larger than their context
budget; event pages advance over all scanned rows even when other executions are filtered out.
Conversation history keeps the exact original direction and bounded recent complete tool batches.
An oversized exact input fails by its named transport bound instead of silently changing the ask.

One local inference runs at once, at most four planning workers may hold leases, and each principal
has eight new planning starts per fixed minute. Cancelled workers retain a lease until an awaited
provider/transport actually exits. Each continuation has eight model turns, 64 calls and 180 seconds
of active model/tool time; runtime/human waiting makes no repeated model calls. A full queue or scan
bound interrupts explicitly for Resume. All prior prepared action IDs remain retained.

Native outcomes come only from the projection's accepted observed lifecycle receipts. Baselines
include whether a native turn was already active, its opaque reference and journal position;
anonymous new turns retain their accepted start position. Superseded/old sequence journal events
never unlock another action. Scan progress is separately journaled and never rewinds. Synchronous
completion and waiting-to-worker-release races coalesce a fresh read rather than losing the wake.

Focused deterministic pipeline tests exercise the actual Recorder/CommandService with a synthetic
controllable runtime and model; these are separate from the current-model transport proof. Actual
model-to-real-runtime work, independent final security review, Unity/native request UI consumption,
headset voice/return and visual acceptance remain pending. The installed older APK/live host are
unchanged by this source implementation. Upgrade order is explicit in REALTIME.md.

The frozen host checkpoint passed 44 focused tests (18 new pipeline cases, five request projection
cases, the raw duplicate case, and 20 existing control-plane lifecycle cases), zero skipped,
plus TypeScript typecheck, regenerated contracts, scoped lint and diff checks. The cursor-page
fixture uses actual reported agent-message provenance so the producer contract accepts its older
run rows. No real project/provider/model/device action was invoked by these tests.


## Exact command recovery and pending continuation

A new exact authenticated command read retains the original journaled sender across replay,
including rejected commands, independently of the recent snapshot window. Its generated response
contains the current journal and ordinary command view, without request words or envelope. The
shared continuation holds a nullable client UUID command reference before a request ID is known;
legacy readers migrate only this missing key. Native storage preserves pending-only records.
Consumer submission ordering and actual process-recreation recovery remain separately verified
work; this API does not imply automatic retry or task completion.

The recovery delta passed 119 focused host/domain/contract/native checks, one real paired-device
TLS ownership/revocation test and 19 C# codec/API checks, with zero skipped. Typecheck, contract
generation and scoped lint passed. Independent source review found no remaining authority,
replay or schema issue in this delta. Actual UI submission/return and headset acceptance remain
pending.

## Consumed XR request view checkpoint

The production Activity entry now opens the retained request view for its exact task/run. Its
availability GET, typed and held-voice draft, full-read Start/Continue and spoken review use the
actual engine-free column and normal director navigation. GET-by-request-ID results must match
all original journal/project/task/execution fields before Resume or Cancel is actionable. Replies
are reported; observed action receipts remain separate. Request cancellation stops planning,
without claiming executor interruption or software-task completion.

Focused request, actual file binding, recovery and instruction tests passed 46/46, zero skipped;
Editor and Android player-symbol aggregate source compilation passed. Cases include arbitrary
open-ended typed text, accepted-submit duplicate blocking, stale keyboard/voice and review
callbacks, foreign tuples, unavailable offers, both-size spoken restore, unsaved request A retired
before fresh B, independent inactive drafts and exact16,001-astral instruction/request restoration.
This checkpoint preserves an unknown pending command in memory. Saving that identity before POST
and exact original-owner recovery after process recreation is still required before claiming
restart-safe request delivery. Actual request renders, native round-trip, model/runtime outcomes
and wearer acceptance are separate; no end-to-end outcome is claimed by these UI source checks.

### Producer identity and bounded context follow-up

OpenCode can legitimately reconstruct an inferred turn outcome after stream disconnection. This
slice keeps the observed-outcome threshold: an applied inferred reconstruction interrupts with
`missing_observed_outcome`, and never becomes confirmed or waits forever. An accepted anonymous
terminal keeps its actual pre-apply named active-turn association, without rewriting the source
journal event. Two real pipeline regressions cover both cases.

Workspace tool context now preserves structured status, native-session presence, synthetic origin,
all declared capabilities and pending human counts. Only outside titles/objectives/check words are
explicitly shortened. Event pages retain their cursor and completeness flags. Complete recent
assistant/tool batches pack by serialized bytes under the existing 12KiB transport limit; the
latest result is preserved or fails by a named bound. No input/output limit was enlarged.

The follow-up passed 29 focused request/domain/raw-identity cases, including five new producer and
context/packing regressions, zero skipped, plus typecheck and scoped lint. Actual model/runtime
acceptance remains separate; deterministic model fixtures do not establish useful planning.

The native instruction editor now applies the canonical 32,000-codepoint insertion limit through a
plain helper consumed by its actual Android InputFilter. Incoming drafts are not shortened. The
focused production/development Android compile and insertion/replacement/supplementary-pair checks
passed (2/2). Actual keyboard/composition behavior on a headset is still pending.


## Isolated real model and runtime witness

The production coordinator/transport ran against local Ollama 0.34.4 and real OpenCode 2.0.18
with an explicitly selected local `qwen3.5:9b`. The temporary project, journal, home and runtime
record were isolated from the owner's workspace. The runtime's sandbox denied other user files
and exposed only the approved local model endpoint. No hosted model, approval answer, live host
restart or settings change was involved. The request asked for one synthetic file edit and a
verification read; the witness independently compared the exact resulting bytes and file list.

The first run reached eight read calls without dispatching work and interrupted at the planning
bound. Inspection found generic tool-result shortening could hide the execution capabilities and
page cursor. After the structured-context correction, the second run dispatched one instruction,
observed its real runtime completion, and produced exactly the expected file with no extra files.
It nevertheless failed the full witness: after eight model turns and nine calls, the coordinator
interrupted instead of returning a final reported reply, repeating unavailable optional provider
reads. Elapsed time was 34.102 seconds. The file/action result is verified; dependable autonomous
continuation and final response are not yet established. This remains a concrete open failure,
not evidence for a completed end-to-end feature.
