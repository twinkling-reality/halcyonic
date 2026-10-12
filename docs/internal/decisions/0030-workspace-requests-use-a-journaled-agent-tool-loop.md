# ADR 0030: Workspace requests use a journaled agent tool loop

- Status: Accepted design; implementation and end-to-end acceptance pending
- Date: 2026-10-12
- Complements ADR 0025. The creation companion remains a separate, transient proposal flow.

## Context

The owner requires open-ended requests carried through to an outcome in VR. Field dictation and
the creation companion do not provide that capability: the companion sends no tools and proposes
only a first task. A phrase router or a model choosing one predefined screen would still require
the person to coordinate the work. Panels should make the work and its decisions inspectable.

The current local model transport can support a real tool loop. Dated synthetic round trips on
the configured model are recorded in [contextual-voice.md](../validation/contextual-voice.md).
Those prove the protocol, not autonomous task completion. Existing runtime adapters already carry
software instructions through their verified execution surfaces. Their permissions, capabilities
and observations remain authoritative.

## Decision

A separate workspace request coordinator assembles context, asks the model, validates and runs
available tools, reads results and continues. Natural-language requests are not classified into a
fixed phrase catalogue. Typed tools define real operations; reusable panels display context,
reported reasoning, observed receipts and decisions without defining the extent of the agent's
reasoning. Held speech supplies an editable ordinary request through the current transcription path.

The first consumed scope is an existing project, task and current execution. The deliberate request
binds that scope and authenticated principal. Reads and authorized continuation run without asking
the person to open each panel. Required human approvals and questions stay under their existing
review rules. Different project, runtime, model or folder authority is never inferred from model
text. Tool output, task titles and quoted material are data, not new instructions from the person.

Request lifecycle facts go through `Recorder` and a journal projection consumed by the request UI.
A request identifies one direction about existing work; it does not replace a Workstream or create
a vendor-session character. Reconnection reads the same request. In-memory model messages and
in-flight tasks are implementation details, not an independent source of durable work state.

Submission, continuation and cancellation use admitted, deduplicated commands. Before a mutating
tool acts, its prepared command identity is durable. Dispatch preserves the original authenticated
principal through `CommandService` and rechecks current journal, project/task/execution, directory
policy and declared capability. Awaited reads do not extend expired authority. A changed target,
revocation or cancellation prevents further actions. Model-generated commands never use an
internal privileged principal.

Existing command and runtime events provide action evidence. Command acceptance is pending, and
confirmation that an instruction was delivered is not confirmation that the software task finished.
The loop observes the relevant runtime outcome before continuing. A timeout or response loss with
unknown effect never authorizes a new mutation attempt. Model prose remains reported; a settled
conversation does not certify the correctness or completion of software work.

External Understanding and Evaluation conclusions remain read-through under ADR 0010. The journal
can retain read recipes, source references and times, not turn a provider document into observed
domain truth. After restart those observations expire and require a fresh authorized read. A
restart interrupts coordinator ownership and reconciles already-issued command outcomes. The
first implementation requires explicit Resume before new mutations; it does not replay a pending
or unknown command. Runtime work can continue independently while its coordinator is interrupted.

The model transport remains local and verifies actual tool capability. Bounds apply to model time,
turn count, tool batches, context and input/output size. Waiting for runtime or human input does not
continuously call the model. Reaching a bound produces an explicit resumable state. There is no
hosted fallback, model download, arbitrary network tool, raw shell tool, credential tool or assumed
external-screen access. Native runtime tools continue to use their existing sandbox and approvals.

## Consequences

The implementation must include the lifecycle contract, projection, host loop and actual request
consumer together. Generated JSON Schema and C# bindings come from TypeBox once. Unity and native
surfaces must share request identity and show reported text separately from command/runtime facts.
Hiding a panel does not cancel already-authorized work; cancelling planning does not imply that a
runtime was interrupted. Both outcomes need clear, truthful controls and receipts.

Acceptance requires real model tool/result continuation and an actual control-plane/runtime path,
with negative cases for malformed calls, changed context, revocation, response loss, duplicates and
restart. Independent review must examine authority and persistence. Headset speech, interruption,
correction, contextual views and other-app return require device evidence. A passing protocol probe
or a new request screen alone is not acceptance of the owner's end-to-end agent requirement.
