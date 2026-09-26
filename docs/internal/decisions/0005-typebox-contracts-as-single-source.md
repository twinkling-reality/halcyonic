# ADR 0005: TypeBox schemas as the single source of the contracts

- Status: Accepted
- Date: 2026-09-26

## Context

Events, commands, realtime messages and views cross process and language boundaries: TypeScript
now, C# in the Unity client next. Maintaining separate TypeScript types, C# classes and JSON
Schema invites drift. Every external input must also be validated at runtime.

Verified on 2026-09-26 with `typebox` 1.3.34: its schemas are plain JSON Schema objects (its
internal marker is non-enumerable and never serialized), static types are inferred from them, and
its compiler validates JSON Schema drafts through 2020-12, including `format` and `$ref`.

## Decision

- Contracts are TypeBox definitions in `packages/contracts`. TypeScript types are inferred from
  them. Validation uses TypeBox's compiler, selecting the variant from the discriminator first so
  errors name the actual problem.
- `pnpm contracts:emit` writes the language-neutral document
  `packages/contracts/schema/halcyonic-contracts.schema.json`. Shared definitions are referenced
  by name (`$ref`) rather than repeated, which reduced the document from 1.2 MB to about 150 KB.
- Tests fail if the committed document is stale, or if it accepts or rejects anything differently
  from the TypeScript validators.
- Wire objects use explicit `null` instead of absent keys, and timestamps use one canonical UTC
  form.

## Alternatives considered

- **Hand-written JSON Schema with generated TypeScript.** Language neutral at the source, but a
  code generation step for every change and weaker inferred types for discriminated unions.
- **Zod.** Popular, but JSON Schema is a lossy conversion from it rather than its native form.
- **TypeBox references (`Type.Ref`) throughout the source.** Needs a reference context everywhere
  a schema is validated. Emitting references at serialization time keeps the source simple.

## Consequences

- C# bindings will be generated from, or validated against, the emitted document; they are never
  written independently.
- The document is a reviewed artifact: contract changes show up as diffs.
- Rules JSON Schema cannot express (scope hierarchy; agent text must be `reported`) live in
  `validation.ts` and are documented in [EVENTS.md](../architecture/EVENTS.md).
