# ADR 0004: Node.js 24 running TypeScript directly, node:sqlite, Fastify, pnpm 11

- Status: Accepted
- Date: 2026-09-26

## Context

The control plane is a local TypeScript service. The dependency policy asks, for each dependency,
whether the platform already provides it. Verified on this machine and in the official Node.js 24
documentation on 2026-09-26:

- Node.js 24.15.0 (the current LTS line) runs `.ts` files directly. Type stripping is stable from
  24.12.0 and has emitted no warning since 24.3.0. It refuses TypeScript under `node_modules`, but
  pnpm workspace links resolve to real paths outside it, which a spike confirmed.
- `node:sqlite` became a release candidate in 24.15.0. It loaded without a warning, supports WAL,
  and enables foreign keys and defensive mode by default.
- `node:test` runs TypeScript test files directly.
- TypeScript 7.0.2 (the native compiler) type-checks the workspace; Salidium already uses it.
- pnpm 10.7.1, the version installed globally here, switches to a pinned pnpm 11 but **cannot**
  switch to pnpm 12.6.0: pnpm 12 ships as a native binary whose placeholder launcher fails with
  `ENOEXEC`. pnpm 11 enforces a minimum release age for dependencies by default.

## Decision

- Node.js >= 24.15.0 runs the sources with no build step. `tsc` checks types only, with
  `erasableSyntaxOnly`, `verbatimModuleSyntax` and `allowImportingTsExtensions`.
- `node:sqlite` for the journal, behind the `EventJournal` interface.
- `node:test` for tests. Biome for lint and format, matching Salidium.
- Fastify 5 with `@fastify/websocket` for HTTP and WebSocket.
- pnpm 11.27.1 pinned through `packageManager`.

Third-party runtime dependencies in the whole workspace: `fastify` and `@fastify/websocket` (control
plane) and `typebox` (contracts and the mock runtime). Development dependencies: `typescript`,
`@biomejs/biome`, `@types/node` and `@types/ws`.

## Alternatives considered

- **`better-sqlite3`**: mature, but a native addon to compile and ship when the platform provides
  SQLite.
- **Vitest**: better ergonomics, but a large dependency tree the platform's test runner makes
  unnecessary here.
- **A build step (tsc or esbuild) with emitted JavaScript**: needed only for publishing, which the
  control plane does not do.
- **pnpm 12**: current, but unusable here with the installed pnpm 10.

## Consequences

- Sources may use only erasable TypeScript syntax (no `enum`, `namespace` or parameter
  properties).
- `node:sqlite` is a release candidate: pin Node 24.15 or newer and re-check its stability before
  depending on newer APIs. Replacing it means one new `EventJournal` implementation.
- Moving to pnpm 12 requires contributors to have a pnpm that can install native binaries.
