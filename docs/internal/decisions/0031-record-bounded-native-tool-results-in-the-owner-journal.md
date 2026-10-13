# ADR 0031: Record bounded native tool results in the owner journal

- Status: Accepted design; device acceptance remains unverified
- Date: 2026-10-13
- Complements ADR 0011, ADR 0020 and ADR 0030

## Context

An instruction can change source and run a command through an approved coding runtime, but a
delivery receipt or the agent's final prose does not show what the command returned or which
native file-change item completed. Codex 0.157.0 supplies final structured command and file-change
fields. The existing tool-title journal policy already retains owner work content after replacing
exact values Halcyonic holds. Unknown credentials can remain in that content.

## Decision

Add observed `runtime.tool.result`, correlated to an observed tool start on the same execution,
turn and runtime. Only final live native notifications produce it; completion, exit code and
admitted command output or per-file patches remain distinct. A result is never a test-run event,
current-file reader, repository diff, semantic assessment or proof of correctness.

Codex declares `reports_tool_results`; other adapters declare false, and an absent legacy field is
unknown. This declaration describes support, not connection health or exhaustive tool/subagent
coverage. Previously accepted journal evidence remains readable without a registered adapter.

A source text field above 64 KiB UTF-8 is omitted whole. An admitted field is replaced for exact
known held values before truncation. Each stored body holds at most 4,096 codepoints and 16 KiB
UTF-8; a result's serialized payload holds at most 64 KiB and at most 32 file entries. Reasons,
retained-field completeness, file-list coverage and actual replacement counts are explicit.
`complete` means the whole admitted native field survived replacement and storage bounds. Native
producer truncation, full process output and the working tree's current contents remain unknown.

Paths are classified lexically relative to the bound execution directory. Foreign drive/UNC,
control and traversal syntax is unknown; outside or unknown primary or rename destination paths
omit the patch. No filesystem read resolves symlinks: lexical inclusion is not realpath ownership.

`Recorder` writes the bounded evidence to the durable owner journal. It never enters operational
logs. This extends the established owner work-content boundary; it does not guarantee secret-free
patches or output. Raw instruction/model/agent-message text is not a new result source. The
app-server transport currently receives unbounded native frames before field admission; these
storage bounds are not a process-memory or source-producer bound.

Two authenticated scoped GETs serve the latest sixteen body-free summaries and one immutable
accepted event. They bind journal/project/workstream/execution, keep recorded facts separate from
support availability, and cross-check the accepted projection index with the exact journal event.
An event remains readable after recent-list eviction. Native Results and the main workspace Results
view consume these reads, preserve drafts and pending command identity, and grant no mutation or
confirmation authority. Both Results consumers offer a short file list and one selected recorded
patch at a time, paging that immutable field.

The coordinator's `read_results` pages only the retained latest sixteen summaries. `read_result`
selects one event, one file where required and one stable text part. Each serialized text part is
at most 256 bytes and each typed response at most 1,024 bytes after JSON escaping. Invalid selectors
and metadata that cannot fit return explicit unavailability, never a JSON prefix. Existing model
context limits stay unchanged; tool content is data, not additional authority.

## Consequences

The person can inspect what an actual supported runtime recorded after an ordinary authorized edit
and command. Other runtime adapters have no emulated output, and old journals need no invented
results. The journal can retain unknown secrets in work content; owners must treat it accordingly.
Complete source patches, current-tree freshness, correctness, build/install correspondence and
device presentation still require independent evidence. No terminal stream, new shell capability,
automatic build, installation, launch or retry is introduced.

Validation: [recorded-tool-results.md](../validation/recorded-tool-results.md).
