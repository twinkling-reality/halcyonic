# Recorded native tool results

Date: 2026-10-13. Question: can the person and a scoped workspace request read actual final
command/file-change evidence without treating model prose as proof or expanding runtime authority?

## Source and environment

Codex app-server 0.157.0, local macOS arm64 binary SHA-256
`ad0be20d04e2ba6146ecdb51d7f8b7b0fe15420a15dc9b0057518d858f1f3714`, with the existing synthetic
fake Responses provider and isolated approved Git directory. Native Java compiled against Android
API 36 with the installed JDK; plain JVM consumers and the actual loopback HTTP producer were used.
No hosted model, inference quota, download, private repository, device, build installation or app
launch was part of these witnesses.

## Verified behavior

- The real pinned runtime applied the synthetic patch and ran one deterministic offline command
  checking exact file bytes. The command emitted one explicit synthetic output line. Final native
  file-change and command result events were observed through `Recorder`; exact exit/output/path/
  patch fields were read through the scoped service and the coordinator's actual typed tools.
  The reported final reply remained reported and `last_test_run` remained null. Proxy/socket
  checks found no egress beyond loopback. The authorized local request used `CommandService`;
  this witness itself is not an HTTP-authentication test.
- Final focused contracts/adapter/projection/host/redaction/model/native/coordinator checks: 116/116 passed, zero
  skipped. They cover whole-source omission, held-value replacement crossing the storage boundary,
  Unicode/codepoint/UTF-8 and serialized aggregate bounds, foreign paths and rename destinations,
  malformed native identities, duplicate notifications, foreign/stale completion correlation,
  accepted-only exact journal reads, replay and recent-list eviction, and stable escaped model
  parts and latest-list paging.
- The native result consumer compiled with the production Activity and read an actual authenticated
  host result list and exact body over its existing proof transport. Unauthenticated reads returned
  401, extra query fields 400, and a foreign journal 404. Its parser refuses changed scope/runtime/
  provenance/order/coverage/body metadata, accepts reordered equivalent JSON objects, and preserves
  LF, spaces, blank lines and literal backslashes while visibly marking other controls. Paging and
  permanently revoked view leases were exercised without Android rendering.
- Existing native panel suite: 89/89 passed, zero skipped, including development and production
  Android API compilation. Generated optional `reports_tool_results` true/false/absent C# reads and
  roundtrips: 2/2 passed, zero skipped. TypeScript type checking passed. The pinned combined witness
  passed separately, 1/1 with zero skipped.

## Evidence limits

`reports_tool_results` is adapter support, not live connection health. Recorded results can outlive
that adapter and are independent of current execution status. Source field completeness means only
the admitted structured native field survived the stated replacement and bounds; producer capture
and current files are unknown. File-change completion is separate from the patch text, including
declined/failed items. Command names and output words never create test/build evidence.

Known-held replacement does not identify all secrets. Unknown credentials can remain in owner work
content. This content is durable in the owner-only journal, never an operational log. Lexical path
classification cannot prove symlink ownership. Source field limits do not bound the app-server RPC
frame received earlier, nor make overall journal/index memory bounded. Latest-list paging covers
only sixteen retained summaries; exact accepted-event reads remain available after list eviction.

Main workspace presentation has its own client/render evidence. Actual Quest readability, focus/
pause/return, pairing lifetime and full live edit→build→installed-app correspondence remain device
or workflow acceptance gates; these checks do not claim them.
