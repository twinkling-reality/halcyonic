# Salidium project map and execution links v0

- **Question:** What do the experimental map surfaces actually expose, and can a consumer
  preserve Halcyonic's one-folder Project boundary?
- **Date:** 2026-10-12.
- **Source:** the local primary Salidium repository at
  `ddb9556404f9f819809c13076e3406cd8523eafe`, clean when inspected. Source paths below are relative
  to that repository. Halcyonic consumer references are at `2b437719`.
- **Method:** read source, schemas, producer tests and documentation. No service, runtime,
  network, credentials, settings or preference reads; no producer changes.
- **Status:** source-verified only. Producer tests were inspected, not run. No live discovery,
  opt-in list, map or execution-links response was verified, and no headset rendering was tested.
  Source presence is not a claim about an installed daemon's version or active consent.

## Contract and evidence

`packages/project-map/src/schemas.ts:4-33` defines `salidium.project-map` 0.0 at
`/project-map/v0`. Version 0 has no compatibility promise. Every successful map carries
`experimental: true`, generation time, indexer identity, repository root, full commit and tree
object ids, commit time, coverage, nodes and edges (`schemas.ts:343-366`).

- Files are committed tree entries with a path, blob id, language and file mode. Symlinks are
  listed and not followed (`schemas.ts:126-149`). Modules are declarations in package, assembly
  or project manifests, not inferred application components (`schemas.ts:152-163`).
- Edges name imports, type imports, re-exports, dynamic literal imports, requires, containment,
  compilation or declared module dependencies. Each carries `observed` or `inferred`, a named
  rule, bounded repository-relative file/line evidence, and a count (`schemas.ts:179-223`).
- JavaScript and TypeScript get file edges; C# gets assembly/project membership, not method or
  type dependencies (`schemas.ts:226-229`). A test role from naming is explicitly inferred.
- Coverage reports omissions, missing blobs, limits, unresolved references and subjects not
  analyzed. Absence of an edge is not evidence of absence of behavior (`schemas.ts:285-339`).
  Components, responsibilities, data flow and working-tree contents are excluded (`schemas.ts:7-15`).

`packages/project-map/src/executionLinks.ts:16-32,103-173,214-237,263-289` defines
`salidium.execution-links` v0. Changed files join by exact repository path to a map at an
observed revision. File detail exposes containing modules and direct import neighbours; module
detail exposes declared dependent modules. Repository status distinguishes `mapped`,
`not-opted-in`, `no-revision`, `revision-gone` and `map-unavailable`; an unmatched file is never
silently matched by name. Truncation, omitted files and withheld map elements have explicit counts.

Revision selection offers the latest turn-end HEAD and then session-start HEAD, only to the
repository where that anchor was observed (`packages/project-map/src/link.ts:69-124`). Neither a
shared commit in another clone nor a similar filename is sufficient. Links do not establish that
mapped contents include the session's uncommitted changes or that a dependent module is affected.

## HTTP, consent and freshness

`packages/daemon/src/projectMap/routes.ts:46-137` serves GET only with the existing consumer
credential and loopback Host/Origin guards:

- `/project-map/v0/repositories` lists opted-in main roots and their grant times.
- `/project-map/v0/maps?repository=<exact-main-root>&commit=<full-object-id>` reads one root.
- `/project-map/v0/sessions/<encoded-salidium-session-id>/links` reads session links.

Discovery always advertises the experimental map entry because the routes are always mounted;
its presence does not mean a repository is opted in (`packages/daemon/src/daemon.ts:653-675`).
The schema's comment at `schemas.ts:421` says the entry appears while a repository is opted in;
the daemon implementation is more specific and is the verified behavior here.

Map service requests compare roots as exact strings, check opt-in before repository reads, build
on demand, and use bounded caching/serialized builds (`packages/daemon/src/projectMap/service.ts:82-86,181-225`).
Producer source reads committed objects through isolated Git plumbing rather than repository
configuration, hooks or remotes (`packages/daemon/src/projectMap/source.ts:10-47`,
`packages/daemon/src/projectMap/gitObjects.ts:10-30,407-447`,
`docs/architecture.md:471-487`). These source safeguards were not independently exercised here.

Session links cache a finished document for up to 60 seconds while evidence sequence and the
entire opt-in list are unchanged (`packages/daemon/src/links/sessionLinks.ts:29-32,89-96,141-144`).
`generatedAt` is document-generation time, not evidence that HEAD or working contents are current.
Maps themselves are immutable committed-tree descriptions; their commit and observation times
must remain visible alongside any newer execution evidence.

## Limits on a Halcyonic consumer

1. **Execution links have no repository filter.** The route forwards query parameters
   (`routes.ts:89-97`), but the actual handler accepts only `sessionId`
   (`packages/daemon/src/links/sessionLinks.ts:147-150`). It resolves every represented repository
   found in the changed files, up to the contract bound (`sessionLinks.ts:100-123`). Adding a
   `repository` query does not constrain reads. Filtering afterward would still permit producer
   reads in other Salidium-opted-in repositories. This is a consumer compatibility limit, not a
   finding that Salidium violates its own repository opt-in contract.
2. **Project access is a separate boundary.** Halcyonic binds a Project to one host-resolved folder
   (`apps/control-plane/src/locations.ts:47-69`) and revalidates it through `DirectoryPolicy`.
   An opted-in Salidium main root can be outside that folder. A first consumer can require exact
   equality between the approved folder and the requested main root; linked worktrees, containing
   repositories and visual repository groups need a separately designed boundary.
3. **Normalized Understanding cannot establish repository identity.** Halcyonic intentionally
   drops anchor roots and file repository roots when mapping reports, retaining commits and
   relative file paths (`packages/integrations/salidium/src/report.ts:96-105,186-192`). An overlay
   must validate raw producer repository identity on the host before normalizing and joining.
   Matching normalized `repository_path` alone can attach a file from a different repository.
4. **Existing discovery reads only consumer v1.** Halcyonic's `WireDiscovery` and `connect`
   currently select `salidium.consumer` major 1 and ignore `experimental`
   (`packages/integrations/salidium/src/wire.ts:47-63`, `connection.ts:148-167`). A future map reader
   must validate the experimental entry, bind it to the already checked daemon instance and exact
   loopback origin, keep redirects refused and enforce response/time bounds before parsing.
5. **Observation remains optional.** Halcyonic's own Codex home is deliberately not observed by
   installed Salidium, and its Understanding source returns `runtime_not_observed`
   (`apps/control-plane/src/intelligence/understanding.ts:37-57`). A map connection must not change
   watched folders or imply that an execution overlay is available when no report exists.

No map consumer, new access grant, runtime capability or change to Workstream state accompanies
this validation. Historical consumer/live checks remain in
[salidium-consumer-contract.md](salidium-consumer-contract.md).
