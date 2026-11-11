import Type, { type Static } from 'typebox';

/**
 * A project secret's name, as the variable an agent's commands read it by (ADR 0057): capitals,
 * digits and `_`, starting with a capital, at most 64 characters. Only names ever cross the
 * contracts; a secret's value is never in a command, an event, a view or a response.
 */
export const SecretName = Type.String({
  minLength: 1,
  maxLength: 64,
  pattern: '^[A-Z][A-Z0-9_]*$',
});
export type SecretName = Static<typeof SecretName>;

/** The most secrets one project keeps. */
export const MAX_PROJECT_SECRETS = 32;

/** The project's secrets by name, sorted, as the project's view lists them. */
export const SecretNames = Type.Array(SecretName, { maxItems: MAX_PROJECT_SECRETS });
export type SecretNames = Static<typeof SecretNames>;
