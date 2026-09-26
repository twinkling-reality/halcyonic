# Architecture decision records

An ADR records one significant decision that is expensive to reverse: its context, the options,
the choice and its consequences. ADRs are historical. When a decision changes, write a new ADR
that supersedes the old one and mark the old one `Superseded by ADR NNNN`; do not rewrite it. The
canonical architecture documents always describe the current state.

Write an ADR when a change alters a dependency rule, a contract's shape or versioning, the
storage or transport model, a security boundary, or the choice of a platform or major dependency.
Do not write one for routine implementation choices.

Name files `NNNN-short-title.md`, numbered in sequence, starting from [TEMPLATE.md](TEMPLATE.md).

| ADR | Decision | Status |
| --- | --- | --- |
| [0001](0001-work-is-the-object-runtimes-are-executors.md) | Work is the object; runtimes are capability-declared executors | Accepted |
| [0002](0002-append-only-journal-with-rebuilt-projection.md) | Append-only journal with a projection rebuilt at startup | Accepted |
| [0003](0003-rest-bootstrap-and-websocket-realtime.md) | REST for bootstrap and commands, WebSocket for live state | Accepted |
| [0004](0004-control-plane-stack.md) | Node.js 24 running TypeScript directly, `node:sqlite`, Fastify, pnpm 11 | Accepted |
| [0005](0005-typebox-contracts-as-single-source.md) | TypeBox schemas as the single source of the contracts | Accepted |
| [0006](0006-license-under-apache-2.md) | License under Apache-2.0, copyright Twinkling Reality | Accepted |
| [0007](0007-agent-control-belongs-to-halcyonic.md) | Agent control belongs to Halcyonic, through runtime APIs | Accepted |
| [0008](0008-engine-independent-csharp-client-core.md) | An engine-independent C# client core with generated contract bindings | Accepted |
| [0009](0009-opencode-v2-pinned-and-launched-by-halcyonic.md) | Target OpenCode's v2 API, pinned, on a server Halcyonic launches | Accepted |
| [0010](0010-external-intelligence-is-read-through.md) | External intelligence is read through, not journaled | Accepted |
