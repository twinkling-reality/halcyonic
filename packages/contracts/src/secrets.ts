import Type, { type Static } from 'typebox';
import { ClientInfo, CommandId, Timestamp } from './primitives.ts';

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

/**
 * A secret's value as one request carries it, write-only (ADR 0057): 8 to 8192 characters. It is in no
 * command, event, view or response; the Mac keeps it in its own store.
 */
export const SecretValue = Type.String({ minLength: 8, maxLength: 8192 });
export type SecretValue = Static<typeof SecretValue>;

/**
 * `POST /api/projects/:project_id/secrets`, from this Mac or a paired device (ADR 0057): the value
 * with the name, and the id of the `project.secret.set` command the Mac submits for it, names only,
 * as the caller. The answer is a `CommandSubmissionResponse`; no route ever gives a value back.
 */
export const ProjectSecretSetRequest = Type.Object(
  {
    command_id: CommandId,
    issued_at: Timestamp,
    client: ClientInfo,
    name: SecretName,
    value: SecretValue,
  },
  { additionalProperties: false },
);
export type ProjectSecretSetRequest = Static<typeof ProjectSecretSetRequest>;
