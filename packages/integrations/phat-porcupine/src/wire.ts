import { CatalogResponse, compileValidator } from '@halcyonic/contracts';
import Type, { type Static } from 'typebox';
import { type CatalogFailure, fail, type Instance } from './connection.ts';

// Halcyonic's own readers of Phat Porcupine's catalog contract, version 1 (its
// `shared/contract/consumer.ts`), tolerant as version 1 asks: fields it adds later are ignored, and a
// state or attention kind it adds is kept as a word Halcyonic reads as unknown. Bounds are version 1's
// (`CATALOG_BOUNDS`), which never loosen within it.

const Nullable = <T extends Parameters<typeof Type.Union>[0][number]>(schema: T) =>
  Type.Union([schema, Type.Null()]);
const Count = Type.Integer({ minimum: 0 });
const Time = Type.String({ minLength: 1, maxLength: 64 });
const Word = Type.String({ minLength: 1, maxLength: 64 });

const ContractEntry = Type.Object({
  name: Type.String(),
  major: Type.Integer(),
  minor: Type.Integer(),
  baseUrl: Type.String(),
});

const DiscoveryFileDocument = Type.Object({
  instanceId: Type.String({ minLength: 1, maxLength: 128 }),
  secret: Type.String({ minLength: 1, maxLength: 128 }),
  sample: Type.Boolean(),
  testGitHub: Type.Boolean(),
  contracts: Type.Array(ContractEntry, { maxItems: 32 }),
});

const DiscoveryAnswer = Type.Object({
  instanceId: Type.String({ minLength: 1, maxLength: 128 }),
  proof: Type.String({ minLength: 1, maxLength: 128 }),
});

const Repository = Type.Object({
  id: Type.Integer({ minimum: 1 }),
  primary: Type.Boolean(),
  fullName: Type.String({ minLength: 1, maxLength: 201 }),
  visibility: Type.String(),
  fork: Type.Boolean(),
  archived: Type.Boolean(),
  language: Nullable(Type.String({ maxLength: 100 })),
  description: Nullable(Type.String({ maxLength: 350 })),
  pushedAt: Nullable(Time),
  access: Type.String(),
});

const Project = Type.Object({
  id: Type.String({ minLength: 1, maxLength: 64 }),
  name: Type.String({ minLength: 1, maxLength: 100 }),
  groupId: Nullable(Type.String({ minLength: 1, maxLength: 64 })),
  groupOrder: Nullable(Count),
  repositories: Type.Array(Repository, { minItems: 1, maxItems: 100 }),
  state: Type.Object({
    state: Word,
    active: Type.Boolean(),
    lastActivityAt: Nullable(Time),
    sessions: Type.Object({ running: Count, awaitingInput: Count }),
    unknown: Type.Array(Word, { maxItems: 16 }),
  }),
  attention: Type.Array(
    Type.Object({
      kind: Word,
      repositoryId: Type.Integer({ minimum: 1 }),
      pullRequest: Nullable(
        Type.Object({
          number: Type.Integer({ minimum: 1 }),
          title: Type.String({ minLength: 1, maxLength: 256 }),
        }),
      ),
      at: Nullable(Time),
    }),
    { maxItems: 5 },
  ),
});

const Catalog = Type.Object({
  apiVersion: Type.String({ pattern: '^1\\.[0-9]{1,4}$' }),
  generatedAt: Time,
  sample: Type.Boolean(),
  testGitHub: Type.Boolean(),
  sync: Type.Object({
    repositoriesAt: Nullable(Time),
    repositoriesError: Nullable(Type.String()),
    activityAt: Nullable(Time),
    activityError: Nullable(Type.String()),
  }),
  groups: Type.Array(
    Type.Object({
      id: Type.String({ minLength: 1, maxLength: 64 }),
      name: Type.String({ minLength: 1, maxLength: 100 }),
      order: Count,
    }),
    { maxItems: 500 },
  ),
  projects: Type.Array(Project, { maxItems: 2000 }),
  truncated: Nullable(Type.Object({ projects: Count, repositories: Count })),
});

export type WireCatalog = Static<typeof Catalog>;

const validateDiscoveryFile = compileValidator(DiscoveryFileDocument);
const validateDiscoveryAnswer = compileValidator(DiscoveryAnswer);
const validateCatalog = compileValidator(Catalog);
const validateResponse = compileValidator(CatalogResponse);

export function readDiscoveryFileDocument(
  value: unknown,
): Static<typeof DiscoveryFileDocument> | null {
  return validateDiscoveryFile(value).ok ? (value as Static<typeof DiscoveryFileDocument>) : null;
}

export function readDiscoveryAnswer(value: unknown): Static<typeof DiscoveryAnswer> | null {
  return validateDiscoveryAnswer(value).ok ? (value as Static<typeof DiscoveryAnswer>) : null;
}

export function readCatalog(value: unknown): WireCatalog | CatalogFailure {
  const checked = validateCatalog(value);
  if (!checked.ok) return invalid(checked.issues[0]?.path);
  return value as WireCatalog;
}

/**
 * Phat Porcupine's catalog in Halcyonic's contract (ADR 0039): reported, with its age and its sample
 * flag, the instance's included. Pull request links, accounts, the user and Phat Porcupine's own ids
 * for repositories never cross; `local` stays empty for the control plane to fill from its journal.
 * What does not fit Halcyonic's contract, such as a repository name that is not GitHub's, makes the
 * whole answer incompatible rather than passing on part of it.
 */
export function toCatalogResponse(catalog: WireCatalog, instance: Instance): CatalogResponse {
  const text = (value: string | null) => (value !== null && /\S/.test(value) ? value : null);
  const answer: CatalogResponse = {
    availability: 'available',
    provenance: 'reported',
    source: {
      system: 'phat-porcupine',
      api_version: '1',
      synthetic: false,
      sample: catalog.sample || catalog.testGitHub || instance.sample || instance.testGitHub,
    },
    generated_at: catalog.generatedAt,
    sync: {
      repositories_at: catalog.sync.repositoriesAt,
      repositories_failed: catalog.sync.repositoriesError !== null,
      activity_at: catalog.sync.activityAt,
      activity_failed: catalog.sync.activityError !== null,
    },
    groups: catalog.groups.map((group) => ({
      group_id: group.id,
      name: group.name,
      order: group.order,
    })),
    projects: catalog.projects.map((project) => ({
      catalog_id: project.id,
      name: project.name,
      group_id: project.groupId,
      group_order: project.groupOrder,
      repositories: project.repositories.map((repository) => ({
        repository_id: repository.id,
        full_name: repository.fullName,
        primary: repository.primary,
        visibility: repository.visibility as 'public',
        fork: repository.fork,
        archived: repository.archived,
        language: text(repository.language),
        description: text(repository.description),
        pushed_at: repository.pushedAt,
        access: repository.access as 'ok',
      })),
      state: {
        state: project.state.state,
        active: project.state.active,
        last_activity_at: project.state.lastActivityAt,
        sessions: {
          running: project.state.sessions.running,
          awaiting_input: project.state.sessions.awaitingInput,
        },
        unknown: project.state.unknown,
      },
      attention: project.attention.map((item) => ({
        kind: item.kind,
        repository_id: item.repositoryId,
        pull_request:
          item.pullRequest === null
            ? null
            : { number: item.pullRequest.number, title: item.pullRequest.title },
        at: item.at,
      })),
      local: { project_ids: [], suggestions: [] },
    })),
    truncated: catalog.truncated,
  };
  const checked = validateResponse(answer);
  return checked.ok ? answer : invalid(checked.issues[0]?.path);
}

function invalid(path: string | undefined): CatalogFailure {
  return fail(
    'incompatible',
    'invalid_catalog',
    `Phat Porcupine's catalog is outside version 1 as Halcyonic reads it${path ? ` (at ${path})` : ''}.`,
  );
}
