import type { Static, TObject, TSchema } from 'typebox';
import Schema from 'typebox/schema';
import { COMMAND_VARIANTS, type CommandEnvelope } from './commands.ts';
import { EVENT_VARIANTS, type EventEnvelope } from './events.ts';
import { type ClientMessage, HelloMessage, PingMessage } from './realtime.ts';

export interface ValidationIssue {
  /** JSON Pointer to the offending value, `/` for the root. */
  path: string;
  message: string;
}

export type Validated<T> = { ok: true; value: T } | { ok: false; issues: ValidationIssue[] };

const MAX_ISSUES = 20;

function invalid(path: string, message: string): { ok: false; issues: ValidationIssue[] } {
  return { ok: false, issues: [{ path, message }] };
}

/** Compiles a schema into a validator that returns typed values or readable issues. */
export function compileValidator<T extends TSchema>(
  schema: T,
): (value: unknown) => Validated<Static<T>> {
  const validator = Schema.Compile(schema);
  return (value) => {
    if (validator.Check(value)) {
      return { ok: true, value: value as Static<T> };
    }
    const [, errors] = validator.Errors(value);
    const seen = new Set<string>();
    const issues: ValidationIssue[] = [];
    for (const error of errors) {
      // A `false` subschema always reports alongside the more useful additionalProperties error.
      if (error.keyword === 'boolean') continue;
      const issue = {
        path: error.instancePath === '' ? '/' : error.instancePath,
        message: error.message,
      };
      const key = `${issue.path} ${issue.message}`;
      if (seen.has(key)) continue;
      seen.add(key);
      issues.push(issue);
      if (issues.length === MAX_ISSUES) break;
    }
    return {
      ok: false,
      issues: issues.length > 0 ? issues : [{ path: '/', message: 'is invalid' }],
    };
  };
}

/**
 * Validates a discriminated union by selecting the variant from the discriminator first. The
 * result is equivalent to validating against the union, but the issues describe the selected
 * variant instead of every alternative.
 */
function compileDiscriminated(
  variants: readonly TObject[],
  discriminator: string,
): (value: unknown) => Validated<unknown> {
  const byTag = new Map<string, (value: unknown) => Validated<unknown>>();
  for (const variant of variants) {
    const tag = (variant.properties[discriminator] as { const?: unknown } | undefined)?.const;
    if (typeof tag !== 'string') {
      throw new Error(`variant is missing a constant "${discriminator}"`);
    }
    if (byTag.has(tag)) {
      throw new Error(`duplicate ${discriminator} "${tag}"`);
    }
    byTag.set(tag, compileValidator(variant));
  }
  return (value) => {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
      return invalid('/', 'must be an object');
    }
    const tag = (value as Record<string, unknown>)[discriminator];
    if (typeof tag !== 'string') {
      return invalid(`/${discriminator}`, 'must be a string');
    }
    const validate = byTag.get(tag);
    if (validate === undefined) {
      return invalid(`/${discriminator}`, `unknown ${discriminator} "${tag}"`);
    }
    return validate(value);
  };
}

const validateEventShape = compileDiscriminated(EVENT_VARIANTS, 'event_type');
const validateCommandShape = compileDiscriminated(COMMAND_VARIANTS, 'command_type');
// `command` messages are validated separately by parseClientMessage.
const validateClientShape = compileDiscriminated([HelloMessage, PingMessage], 'type');

/**
 * Rules that JSON Schema cannot express. They are part of the contract and are documented in
 * docs/internal/architecture/EVENTS.md.
 */
function checkEventSemantics(event: EventEnvelope): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  if (event.execution_id !== null && event.workstream_id === null) {
    issues.push({ path: '/workstream_id', message: 'is required when execution_id is set' });
  }
  if (event.workstream_id !== null && event.project_id === null) {
    issues.push({ path: '/project_id', message: 'is required when workstream_id is set' });
  }
  if (event.event_type === 'runtime.agent_message' && event.provenance.epistemic !== 'reported') {
    issues.push({ path: '/provenance/epistemic', message: 'agent text must be "reported"' });
  }
  return issues;
}

export function parseEventEnvelope(value: unknown): Validated<EventEnvelope> {
  const shape = validateEventShape(value);
  if (!shape.ok) return shape;
  const event = shape.value as EventEnvelope;
  const issues = checkEventSemantics(event);
  return issues.length === 0 ? { ok: true, value: event } : { ok: false, issues };
}

export function parseCommandEnvelope(value: unknown): Validated<CommandEnvelope> {
  return validateCommandShape(value) as Validated<CommandEnvelope>;
}

/**
 * Validates a realtime client message. Commands are validated in a second step so that a bad
 * command reports the command's own issues, prefixed with `/command`.
 */
export function parseClientMessage(value: unknown): Validated<ClientMessage> {
  if (
    typeof value === 'object' &&
    value !== null &&
    !Array.isArray(value) &&
    (value as Record<string, unknown>).type === 'command'
  ) {
    const extraKeys = Object.keys(value).filter((key) => key !== 'type' && key !== 'command');
    if (extraKeys.length > 0) {
      return invalid('/', `must not have additional properties: ${extraKeys.join(', ')}`);
    }
    const command = parseCommandEnvelope((value as Record<string, unknown>).command);
    if (!command.ok) {
      return {
        ok: false,
        issues: command.issues.map((issue) => ({
          path: `/command${issue.path === '/' ? '' : issue.path}`,
          message: issue.message,
        })),
      };
    }
    return { ok: true, value: { type: 'command', command: command.value } };
  }
  return validateClientShape(value) as Validated<ClientMessage>;
}
