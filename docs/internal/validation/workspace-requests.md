# Workspace request verification

## 2026-10-13: Typed recorded result reads

The same approved synthetic edit/check witness now consumes observed final command and file-change
results through the production scoped reader and the coordinator's `read_results`/`read_result`
tools. The deterministic offline command emits an explicit synthetic output line, checked byte for
byte; it runs exactly once. A final reply stays reported and `last_test_run` stays unknown/null.
The pinned witness passed 1/1, zero skipped. Full bounds and source limitations are in
[recorded-tool-results.md](recorded-tool-results.md).

`read_results` returns typed metadata within 1,024 serialized bytes, with total/recent/earlier,
omitted count and `next_before`. `coverage: retained_latest` and `exhausted` explicitly limit paging
to the latest sixteen; no omitted earlier history is presented as scanned. `read_result` requires
one original event ID, `file_index` for file changes and an optional `part`. It returns one stable
retained native field part at most 256 serialized bytes inside the same 1,024-byte wrapper, with
native completion/exit/path excerpts, availability, stored completeness, truncation and held-value
replacement metadata. Inappropriate, required-but-absent or out-of-range selectors and metadata
that cannot fit are explicit unavailable/rejected reads, never a JSON-prefix excerpt or another
field. Existing total model context/input limits remain unchanged. Every tool result is data,
never new human authority, and command names do not identify tests or builds.

## 2026-10-13: Combined synthetic edit and check through the real Codex runtime

`codex-live-edit.e2e.test.ts` connects the production request coordinator, Recorder, CommandService,
approved-folder policy and real pinned Codex 0.157.0 adapter in one witness. The existing isolated
Codex test harness supplies temporary HOME/CODEX_HOME/XDG/TMPDIR, a scripted Responses provider
on loopback, an egress-refusing proxy and process socket monitoring. The coordinator's tool choices
are scripted too: this is an actual runtime/file/command witness, not model reasoning or inference.
No real repository, hosted provider, downloaded model, Unity player, build, install or device is used.

The test binds a disposable synthetic Git folder as a project, creates a task and selects a model
actually listed by the runtime as served on this Mac. Normal workspace-write/on-request policy is
unchanged. It waits for an observed setup turn completion on the exact execution, then submits one
authorized local request through CommandService. Actual HTTP authentication is not exercised here. Two `continue_task` actions pass through the original command path: an actual
`apply_patch` adds the synthetic file, and one actual Node command asserts its exact bytes and writes
an offline-check receipt. Filesystem assertions prove the edit/check occurred once; prepared command
IDs and observed native item/turn events prove the coordinator reconciled each outcome before the
next continuation. No command or result is synthesized into the runtime adapter.

The final coordinator answer remains `reported`. Native instruction delivery and relevant turn
completion remain separate receipts, and `last_test_run` stays null: observing a successful command
does not fabricate a structured passing-test event, whole-suite coverage or installed application
correspondence. The test also verifies that instruction/model-message text never enters operational
logs, runtime processes attempted no proxy egress and no sampled socket reached beyond loopback.
The harness's periodic socket monitor does not claim to prevent every possible short-lived socket.

Focused execution passed 1/1 with zero failures, cancellations, skips or todos, in 0.51 seconds
(0.76 seconds including the test process). TypeScript type checking and scoped Biome checks passed.
The native binary's independently checked SHA-256 matched the repository's Codex pin before this
run. Whole-repository validation belongs to the integration gate. Existing authority/restart tests,
natural model-to-runtime planning, secret-bearing result persistence, visible result consumers,
real build/install and headset editing/return remain separate acceptance work.

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
are reported; recorded actions remain separate. Request cancellation stops planning,
without claiming executor interruption or software-task completion.

Focused request, actual file binding, recovery and instruction tests passed 46/46, zero skipped;
Editor and Android player-symbol aggregate source compilation passed. Cases include arbitrary
open-ended typed text, accepted-submit duplicate blocking, stale keyboard/voice and review
callbacks, foreign tuples, unavailable offers, both-size spoken restore, unsaved request A retired
before fresh B, independent inactive drafts and exact16,001-astral instruction/request restoration.
That historical UI checkpoint preserved unknown pending commands in memory. The following source
checkpoint supplies durable-before-POST and exact original-owner recovery; actual process-death
and native round-trip acceptance remain separate. Actual request renders, native round-trip, model/runtime outcomes
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
reads. Elapsed time was 34.102 seconds. The file/action result is verified; that run did not
establish autonomous continuation and a final response. It remains a recorded failure,
not evidence for a completed end-to-end feature.

After the request entry was integrated, the actual Unity DedicatedWorkspaceCheck passed its
complete production navigation sequence at Standard and Larger text sizes, with the existing fit,
visibility, field-of-view and coplanarity guards unchanged. Thirty-four current-sequence captures
were retained; visual inspection confirmed the Larger task view exposes Ask workspace without
clipping its existing controls. This verifies the entry in its actual menu, not the request form,
headset legibility or wearer acceptance, which require separate checks.

## Context after a runtime wait

Natural runtime wakes retain bounded transient complete tool batches, including prior unavailable
provider reads. This context has a 1MiB aggregate memory bound and is discarded on restart,
explicit Resume, Cancel or settlement; it grants no authority and is not a durable request status.
One replaceable current snapshot supplies bounded action totals and recent reported native words.
The original tool name and opaque assistant call identity survive the wake. Remaining planning
turns/calls are explicit, without changing the existing limits or forcing a final model answer.

An accepted new anonymous turn keeps its own null native identity rather than borrowing the
previous named turn; terminal association still uses the actually applied active turn. Inferred
reconstruction remains a plainly explained interrupted state, not confirmed completion.

Thirty-two focused real pipeline/domain/identity checks passed with zero skipped, including two
runtime waits, single fresh-snapshot replacement, bounded receipt metadata, exact observe-tool
identity and a named-to-new-anonymous turn. Typecheck and scoped lint passed. Actual local model
and software-output acceptance remains a separate witness; prior real probes completed software
work but exhausted planning before a reported final response, so they are not overall passes.

## Consumed pending identity and input lifetime

The XR request consumer now saves its exact pending command UUID before POST through the consumed
MenuDirector BeforeSubmit hook and exact durable IsSaved comparison. Request copiers, retirement,
recovery and ordinary draft inspection include that ID. A failed save performs no POST. Original
command GET verifies journal, ID, full project/task/execution binding and command type. Duplicate
or partial acknowledgements do not resolve it; foreign, unreadable, not-found and unknown-effect
results keep the original identity without retrying or minting another command. Confirmed host
request references are adopted only from the typed original-owner read, then request GET separately
checks the request's full tuple before action offers.

Focused tests pass 66/66, zero skipped, including persistence-before-POST ordering, failed writes,
UUIDv4 recreation without another POST, duplicate/rejected/conflict acknowledgements, foreign ID,
type, journal, task and execution results, unknown effects and unavailable reads. Actual FileColumn
regressions cover an old drawn entry pressed after snapshot B but before Tick: A's latest words
remain on A and fresh B stays empty. Actual keyboard and MenuVoice destinations are revoked when
leaving the draft section. Existing both-size Unicode, spoken review, retention and Judge walk
coverage remains passing. Actions uses meaningful recorded status and omits journal positions or
receipt jargon, with no task-completion claim.

RequestCheck exercises the actual request column, director input and complete console at both real
font sizes with explicitly synthetic metadata and request results. It captures local typed parts,
accepted-but-unresolved state, reported reply, recorded actions, spoken review, global Minimize and
unavailable draft. It never invokes a model, reads private storage or proves a live session
watermark. Actual run and visual inspection are pending until the integrated Unity gate.
Independent source review then identified two bounded context cases: pausing after the first
call of a multi-call turn left unmatched native calls, and a refused busy Resume cleared cached
context before checking its refusal. Remaining calls now receive truthful `executed: false`
results without being dispatched or replayed. Resume clears transient context only after its
busy/stopped checks pass. Twenty-nine focused request checks, typecheck and scoped lint passed;
this follow-up does not claim a new actual model/runtime witness.

## Repeated real model and runtime witness after context repair

The same isolated witness passed against the reviewed coordinator at `c2c64d92`. Local
`qwen3.5:9b` used three model turns and two tool calls, delivered one confirmed action, observed
the real OpenCode execution complete, and returned a reported reply. Independent comparison
verified the exact requested file bytes and that no extra files appeared. Elapsed time was
20.807 seconds. The model was explicitly selected for this probe; this does not establish the
owner's live model configuration. No hosted call, live workspace edit or host/settings change
was made. The prior two failures above remain part of the evidence.

This proves one actual model-to-runtime-to-result exchange using the general tool loop. It does
not establish reliability across arbitrary tasks, native or XR input, process recreation,
headset voice, or wearer acceptance. The integrated deterministic request/domain/identity checks
also passed 35/35 with zero skipped, and TypeScript type checking passed.

The actual Unity RequestCheck at the same checkpoint passed Standard and Larger text sizes,
producing twenty captures of the actual request column and director flow. Visual inspection
identified that the request form did not visibly name its bound task. That presentation follow-up
requires a new render; geometric passage alone is not usability acceptance.

After the task-identity and dropped-view follow-up, the actual RequestCheck at `9cbc4b90` passed
both text sizes with twenty new captures. Each visible request frame asserts the bound task's
actual outside-text title. Visual inspection of the Larger spoken review and Actions pages
confirmed that identity remains visible and the duplicate Actions body heading is gone. The
complete `pnpm check` passed 1,154 tests with zero skipped, plus typecheck and lint, at this source.

The broader legacy WorkspaceRender.Check at `c2c64d92` failed at both sizes. It still creates old
multi-character lineups and a fabricated video window, uses character-attached menu budgets, and
expects obsolete navigation and banner behavior. The Organization witness also stopped at a
missing visible Move target; later journeys were not reached. These results do not establish a
current product defect for every failed assumption, nor may they all be dismissed as fixture
failures: shape-fade pixel checks and current composition coverage still need investigation and
updated actual navigation. Full visual acceptance, final native consumers and device work remain
open despite the passing focused request and code checks.

## Shared client sender and checkpoint limits

The complete C# suite exposed a missing consumer integration: CommandSubmissions did not recognize
the three generated request command classes before calling the transport. The tracker now handles
them, and a consumed Submit regression verifies each reaches the send delegate once, keeps an
acknowledgement separate from a journaled outcome, and preserves unknown delivery. Request-not-found
and command feedback have ordinary request-specific words. Unknown delivery directs Refresh rather
than another send; completed request commands do not claim software-task completion.

The initial full C# run had 1,662 passes and three failures: the sender mapping, missing refusal
copy and an obsolete Projects subject expectation. After those fixes, a second run had 1,665
passes and one failure out of 1,666: new unknown-delivery copy violated the established “Not sure”
wording pattern. The copy was corrected, retaining saved-result recovery, and all 28 focused
sender/presenter checks passed with zero skipped. The full suite was not repeated after that final
wording correction; a final complete gate remains required.

The focused actual DirectorCheck at `42588dfb` also failed both sizes. Its first visible-Minimize
transition did not produce the expected dock, preventing the intended Tasks route and cascading
into later checks; the old six-agent setup still produces overlap failures. The fixture migration
is therefore not a passing visual witness. Its results require investigation through the actual
current input and composition before claiming complete navigation, input lifetime or layout coverage.
