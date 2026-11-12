/**
 * What a starter says it is, from its own `starter.json` (the lead's decision of 2026-11-12, R2): the
 * host reads it once, as it reads a runtime's descriptor, and the rules ask it instead of comparing a
 * starter's id. A starter that declares nothing has the defaults, which are a starter with a build.
 */
export interface StarterDeclarations {
  /** `build`: the host's check builds and tries it; `none`: it has no build to check (a world's). */
  readonly check: 'build' | 'none';
  /** `kept`: a task may start from one of its versions; `none`: it changes in place as it is spoken to. */
  readonly versions: 'kept' | 'none';
  /** `own`: each task works in a copy of its own; `none`: its tasks work in the folder, one at a time. */
  readonly copies: 'own' | 'none';
  /** The file the starter's project is described by, which the host reads as it changes, or null. */
  readonly describes: string | null;
}

export const STARTER_DEFAULTS: StarterDeclarations = {
  check: 'build',
  versions: 'kept',
  copies: 'own',
  describes: null,
};

/** The starters this host has, by id, each with what it declares. */
export interface StarterCatalog {
  declares(starterId: string): StarterDeclarations;
}
