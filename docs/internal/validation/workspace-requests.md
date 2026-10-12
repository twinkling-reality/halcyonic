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
