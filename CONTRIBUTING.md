# Contributing

## Setup

1. Install Node.js 24.15 or newer.
2. Install pnpm 10 or newer. It switches to the version pinned in `package.json`
   (`packageManager`) on first use. The pin is pnpm 11: pnpm 12 ships as a native binary that
   pnpm 10's version switching cannot install (see ADR 0004).
3. `pnpm install`
4. For C# work (contracts or `apps/xr`): the .NET 10 SDK on `PATH`.

There is no build step. Node runs the TypeScript sources directly using type stripping, and
`pnpm typecheck` runs the TypeScript compiler for checking only. Code must therefore use only
erasable TypeScript syntax: no `enum`, `namespace` or constructor parameter properties, and
`import type` for type-only imports. The compiler settings enforce this.

## Workflow

1. Read [AGENTS.md](AGENTS.md) and the canonical documents for the area you are changing.
2. Make a focused change with tests.
3. Update the canonical documentation in the same change when behavior or architecture changes.
4. Run `pnpm check`. It must pass. After changing a contract or anything in `apps/xr`, run
   `pnpm test:csharp` as well.

## Where things go

| Change | Location |
| --- | --- |
| A wire contract (event, command, message, view) | `packages/contracts`, then `pnpm contracts:emit` |
| Rules over the contracts: status, attention, admission | `packages/domain` |
| The runtime adapter port | `packages/runtime-core` |
| A runtime integration | `packages/integrations/<name>` |
| Journal, command handling, HTTP, WebSocket | `apps/control-plane` |
| Mock runtime scenarios | `fixtures/scenarios` |
| Recorded traces | `fixtures/traces`, via `pnpm fixtures:record` |
| Repository-wide structural tests | `tooling` |
| The XR client core (C#, no engine references) | `apps/xr/Packages/com.halcyonic.client` |
| Its .NET build and tests | `apps/xr/dotnet` |

Changing a contract usually means: edit the TypeBox schema, update the projection or adapters,
run `pnpm contracts:emit` and `pnpm fixtures:record`, review both diffs, and update
`docs/internal/architecture/EVENTS.md` or `REALTIME.md`.

## Conventions

- Strict TypeScript. No `any`, no non-null assertions, exhaustive `switch` over unions.
- Validate every external input with the contracts' validators. Never trust parsed JSON.
- Inject clocks, schedulers and id generators at infrastructure boundaries so behavior is
  testable and reproducible.
- Tests use `node:test`. Each test creates its own data.
- Formatting and linting are Biome's (`pnpm format`, `pnpm lint`).
- Commit messages are descriptive and in the imperative. Keep commits small and coherent, and keep
  `main` passing `pnpm check`.
- Never commit credentials, databases, logs or private repository content.

## License

Halcyonic is licensed under Apache-2.0. Contributions are accepted under the same license, as
section 5 of the license describes. New dependencies must be compatible with it.

## Documentation

Canonical documents describe how the system works now. Plans, status notes and research diaries
do not belong in the repository; put scratch material in `/.private/`, which git ignores. See the
[documentation map](docs/internal/README.md) for what goes where.
