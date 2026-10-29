import Type, { type Static } from 'typebox';

const strict = { additionalProperties: false } as const;

/** A starter the host vets and keeps, named by its folder under `starters/` (ADR 0037). */
export const StarterId = Type.String({ pattern: '^[a-z][a-z0-9-]{0,62}$' });
export type StarterId = Static<typeof StarterId>;

/**
 * A starter a new project can begin from. `installed` is false until the host has installed its
 * pinned dependencies; a project cannot start from it before.
 */
export const StarterView = Type.Object(
  {
    starter_id: StarterId,
    title: Type.String({ minLength: 1, maxLength: 80, pattern: '\\S' }),
    installed: Type.Boolean(),
  },
  strict,
);
export type StarterView = Static<typeof StarterView>;

export const StartersResponse = Type.Object({ starters: Type.Array(StarterView) }, strict);
export type StartersResponse = Static<typeof StartersResponse>;
