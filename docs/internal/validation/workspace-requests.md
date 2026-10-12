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
lint. A real local Ollama 0.34.4 probe through this transport used the configured `qwen3.5:9b`:
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
missing `request` field from earlier private version-1 records to null; the current schema still
requires its explicit presence. A request with no bound execution cannot pass current-context
validation. Durable view storage retains both drafts within the existing aggregate byte bound.
These schema/codec facts are separate from production capture, request-owner/tuple checks after
host reads, native return, process recreation and actual headset acceptance.
