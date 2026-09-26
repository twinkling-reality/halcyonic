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

export const silentLogger: Logger = {
  debug: () => {},
  info: () => {},
  warn: () => {},
  error: () => {},
};
