import Type, { type Static } from 'typebox';
import { HostPath } from './locations.ts';
import { ErrorInfo, Nullable, ProjectId, Text } from './primitives.ts';

const strict = { additionalProperties: false } as const;

/**
 * A GitHub repository's owner and name, `owner/name`, in GitHub's own characters: an owner of up to
 * 39 letters, digits and inner hyphens, and a name of up to 100 letters, digits, `.`, `_` and `-`
 * that is neither `.` nor `..`. Nothing else, so no path, URL or credential passes as one. Reported
 * by the catalog's source; never checked against GitHub.
 */
export const RepositoryFullName = Type.String({
  pattern:
    '^[A-Za-z0-9][A-Za-z0-9-]{0,38}/(?:[A-Za-z0-9_-][A-Za-z0-9._-]{0,99}|\\.[A-Za-z0-9_-][A-Za-z0-9._-]{0,98}|\\.\\.[A-Za-z0-9._-]{1,98})$',
});
export type RepositoryFullName = Static<typeof RepositoryFullName>;

/** GitHub's numeric repository id: its stable identity, which names and owners are not. */
export const RepositoryId = Type.Integer({ minimum: 1, maximum: Number.MAX_SAFE_INTEGER });

/**
 * One repository of the person's catalog, as a Project's link names it (ADR 0039): GitHub's id
 * beside its `owner/name` as the client sent it, taken from the catalog's answer; checked neither against the catalog nor GitHub.
 */
export const CatalogRepositoryRef = Type.Object(
  {
    source: Type.Literal('phat-porcupine'),
    repository_id: RepositoryId,
    full_name: RepositoryFullName,
  },
  strict,
);
export type CatalogRepositoryRef = Static<typeof CatalogRepositoryRef>;

/** Where the catalog came from. */
export const CatalogSource = Type.Object(
  {
    system: Type.Literal('phat-porcupine'),
    /** The major version of the source's catalog contract it was read through. */
    api_version: Type.Literal('1'),
    /**
     * True when a stand-in Halcyonic wrote produced it from invented projects, as the recorded
     * demonstration's sources do (ADR 0019); false for everything read from Phat Porcupine itself.
     */
    synthetic: Type.Boolean(),
    /**
     * True when Phat Porcupine served its own sample data or ran against a stand-in for GitHub.
     * Clients label such a catalog as a sample and never show it among real projects.
     */
    sample: Type.Boolean(),
  },
  strict,
);
export type CatalogSource = Static<typeof CatalogSource>;

/** A name in the source's open vocabulary: a client reads one it does not know as unknown. */
const OpenTerm = Type.String({ pattern: '^[a-z][a-z0-9_]{0,63}$' });
/** A time as the source wrote it: ISO 8601, not necessarily to the millisecond. */
const SourceTime = Type.String({ format: 'date-time' });
const Count = Type.Integer({ minimum: 0 });

export const CatalogRepository = Type.Object(
  {
    repository_id: RepositoryId,
    full_name: RepositoryFullName,
    primary: Type.Boolean(),
    visibility: Type.Union([
      Type.Literal('public'),
      Type.Literal('private'),
      Type.Literal('internal'),
    ]),
    fork: Type.Boolean(),
    archived: Type.Boolean(),
    language: Nullable(Text(100)),
    description: Nullable(Text(350)),
    pushed_at: Nullable(SourceTime),
    /** `removed`: the person no longer reaches it; `not_found`: GitHub no longer returns it. */
    access: Type.Union([Type.Literal('ok'), Type.Literal('removed'), Type.Literal('not_found')]),
  },
  strict,
);
export type CatalogRepository = Static<typeof CatalogRepository>;

/**
 * The project's state as the source derived it, in its own words. `unknown` names the signals it
 * could not read, so a state that depends on them may be missing; never read as zero.
 */
export const CatalogState = Type.Object(
  {
    state: OpenTerm,
    active: Type.Boolean(),
    last_activity_at: Nullable(SourceTime),
    /** Agent sessions the source's own signal sources report: never a Halcyonic workstream. */
    sessions: Type.Object({ running: Count, awaiting_input: Count }, strict),
    unknown: Type.Array(OpenTerm, { maxItems: 16 }),
  },
  strict,
);
export type CatalogState = Static<typeof CatalogState>;

/** Why the project needs the person, newest first. No link: the headset never opens one. */
export const CatalogAttention = Type.Object(
  {
    kind: OpenTerm,
    repository_id: RepositoryId,
    pull_request: Nullable(
      Type.Object({ number: Type.Integer({ minimum: 1 }), title: Text(256) }, strict),
    ),
    at: Nullable(SourceTime),
  },
  strict,
);
export type CatalogAttention = Static<typeof CatalogAttention>;

/**
 * What this computer knows of a catalog project. `project_ids` are the Halcyonic Projects the person
 * linked to one of its repositories (journaled, observed); each suggestion is a folder the host
 * matched by the named rule, inferred and never applied without the person.
 */
export const CatalogLocal = Type.Object(
  {
    project_ids: Type.Array(ProjectId, { maxItems: 100 }),
    suggestions: Type.Array(
      Type.Object(
        {
          root: HostPath,
          folder_name: Text(255),
          rule: Type.Union([
            Type.Literal('github_origin_full_name'),
            Type.Literal('folder_name_equals_repository_name'),
          ]),
        },
        strict,
      ),
      { maxItems: 20 },
    ),
  },
  strict,
);
export type CatalogLocal = Static<typeof CatalogLocal>;

export const CatalogProject = Type.Object(
  {
    /** The source's own id for the project, an opaque reference; stable until a merge or split. */
    catalog_id: Type.String({ minLength: 1, maxLength: 64, pattern: '^[A-Za-z0-9-]+$' }),
    name: Text(100),
    group_id: Nullable(Type.String({ minLength: 1, maxLength: 64, pattern: '^[A-Za-z0-9-]+$' })),
    group_order: Nullable(Count),
    /** The primary repository first. */
    repositories: Type.Array(CatalogRepository, { minItems: 1, maxItems: 100 }),
    state: CatalogState,
    attention: Type.Array(CatalogAttention, { maxItems: 5 }),
    local: CatalogLocal,
  },
  strict,
);
export type CatalogProject = Static<typeof CatalogProject>;

export const CatalogGroup = Type.Object(
  {
    group_id: Type.String({ minLength: 1, maxLength: 64, pattern: '^[A-Za-z0-9-]+$' }),
    name: Text(100),
    order: Count,
  },
  strict,
);
export type CatalogGroup = Static<typeof CatalogGroup>;

/**
 * Every project the person has across their accounts and organisations, read through from Phat
 * Porcupine when a client asks and never journaled (ADR 0039, as ADR 0010 for Salidium and Seorak).
 * Everything in it is `reported` by the source, never observed by this computer, and its text is
 * untrusted. Nothing but `available` carries projects, so an absent catalog never looks empty.
 */
export const CatalogResponse = Type.Union([
  Type.Object(
    {
      availability: Type.Literal('available'),
      provenance: Type.Literal('reported'),
      source: CatalogSource,
      generated_at: SourceTime,
      /**
       * How old the catalog is: when the source last read the person's repositories and their
       * activity from GitHub, and whether that read failed, so a client says the age instead of
       * implying the catalog is live. Null when it never has.
       */
      sync: Type.Object(
        {
          repositories_at: Nullable(SourceTime),
          repositories_failed: Type.Boolean(),
          activity_at: Nullable(SourceTime),
          activity_failed: Type.Boolean(),
        },
        strict,
      ),
      groups: Type.Array(CatalogGroup, { maxItems: 500 }),
      projects: Type.Array(CatalogProject, { maxItems: 2000 }),
      /** What the source left out over its bounds, or null when nothing was. */
      truncated: Nullable(Type.Object({ projects: Count, repositories: Count }, strict)),
    },
    strict,
  ),
  Type.Object({ availability: Type.Literal('unavailable'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('unauthorized'), reason: ErrorInfo }, strict),
  Type.Object({ availability: Type.Literal('incompatible'), reason: ErrorInfo }, strict),
]);
export type CatalogResponse = Static<typeof CatalogResponse>;
