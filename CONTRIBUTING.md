# Contributing

## Setup

1. Install Node.js 24.15 or newer (`engines.node` in `package.json`).
2. Install pnpm 10 or newer. It switches to the version pinned in `package.json`
   (`packageManager`, `pnpm@11.27.1`) on first use. The pin stays on pnpm 11 because pnpm 12 ships
   as a native binary that pnpm 10's version switching cannot install (see
   [ADR 0004](docs/internal/decisions/0004-control-plane-stack.md)).
3. `pnpm install`
4. For `pnpm check`: a JDK and Android's platform jar (`JAVA_HOME` and `ANDROID_JAR`), which the
   Android Java checks in `tooling/glance` and `tooling/panel` compile against. Unity 6000.3.25f1
   with Android Build Support supplies both. See the
   [local development runbook](docs/internal/runbooks/LOCAL_DEVELOPMENT.md).
5. For C# work (contracts or `apps/xr`): the .NET 10 SDK on `PATH`.

There is no TypeScript build step. Node runs the `.ts` sources directly using type stripping, and
`pnpm typecheck` runs the TypeScript 7 compiler for checking only. Code must therefore use only
erasable TypeScript syntax: no `enum`, `namespace` or constructor parameter properties, and
`import type` for type-only imports. The compiler settings enforce this.

## Workflow

1. Read [AGENTS.md](AGENTS.md) and the canonical documents for the area you are changing
   ([map](docs/internal/README.md)).
2. Make a focused change with tests.
3. Update the canonical documentation in the same change when behavior or architecture changes.
4. Run `pnpm check`. It must pass. After changing a contract or anything in `apps/xr`, run
   `pnpm test:csharp` as well.

## Where things go

| Change | Location |
| --- | --- |
| A wire contract (event, command, message, view) | `packages/contracts`, then `pnpm contracts:emit` |
| Rules over the contracts: projection, status, attention, admission | `packages/domain` |
| The runtime adapter port and capability model | `packages/runtime-core` |
| macOS Seatbelt confinement profiles and per-launch key gates | `packages/seatbelt` |
| An agent runtime integration (`mock`, `claude-code`, `opencode`, `codex`, `acp`) | `packages/integrations/<name>` |
| A read-through intelligence or catalog client (`salidium`, `seorak`, `omnorium`) | `packages/integrations/<name>` |
| A hosted provider declaration (data only) | `providers/<id>/provider.json` |
| A vetted project or world starter | `starters/<id>` |
| Journal, command handling, HTTP, WebSocket, Mac pages and CLIs | `apps/control-plane` |
| Mock runtime scenarios | `fixtures/scenarios` |
| Recorded traces and bundled headset recordings | `fixtures/traces` (`pnpm fixtures:record`), `apps/xr/Assets/Halcyonic/Resources` (`pnpm demonstration:record`, `pnpm companion:record`, `pnpm judge:record`) |
| Repository-wide structural, naming, Android Java and Quest device checks | `tooling` |
| The XR client core (C#, no `UnityEngine` references) | `apps/xr/Packages/com.halcyonic.client` |
| Its .NET build and NUnit tests | `apps/xr/dotnet` |
| The Unity layer and native Android hybrid panel, relay service and glance | `apps/xr/Assets`, `apps/xr/Android/panel`, `apps/xr/Android/glance` |

Changing a contract usually means: edit the TypeBox schema, update the projection or adapters,
run `pnpm contracts:emit` and `pnpm fixtures:record` (plus `pnpm demonstration:record` when the
bundled demonstration is affected), review the diffs, and update
`docs/internal/architecture/EVENTS.md` or `REALTIME.md`.

## Naming and conventions

- **Product name vs code identifiers.** The shown product name is **Halora**
  (`apps/xr/ProjectSettings/ProjectSettings.asset`, `apps/control-plane/src/visible-name.ts`,
  enforced by `tooling/visible-name.test.ts`). Keep `halcyonic` only where code names the
  repository, `@halcyonic/*` npm packages, `Halcyonic.*` C# namespaces, `com.halcyonic.*` package
  identifiers, `HALCYONIC_*` environment variables, or `~/.halcyonic` data paths.
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

Halora (repository `halcyonic`) is licensed under Apache-2.0. Contributions are accepted under the
same license, as section 5 of the license describes. New dependencies must be compatible with it.

## Documentation

Canonical documents describe how the system works now. Plans, status notes and research diaries
do not belong in the repository; put scratch material in `/.private/`, which git ignores.

Tracked `docs/internal/` is public. `docs/private/` is git-ignored and must not be quoted. The
standard lives in the [documentation map](docs/internal/README.md) under Documentation standard.
One fact has one home.
