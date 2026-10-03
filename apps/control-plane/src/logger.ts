/**
 * The logging surface the control plane uses. Fastify's pino logger satisfies it. Log context
 * objects carry identifiers (project, workstream, execution, command, runtime), never secrets,
 * tokens, or instruction and message text.
 */
export interface Logger {
  debug(context: object, message: string): void;
  info(context: object, message: string): void;
  warn(context: object, message: string): void;
  error(context: object, message: string): void;
}

/**
 * An error whose message is Halcyonic's own fixed words, never text from outside: a setting's
 * problem, a contract's issue paths, a speech engine's state. Its message is logged; any other
 * error's is not, since it can quote what it read (errorForLog in http/server.ts).
 */
export class OwnWordsError extends Error {}

export const silentLogger: Logger = {
  debug: () => {},
  info: () => {},
  warn: () => {},
  error: () => {},
};
