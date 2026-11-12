import {
  type ChoiceList,
  type ClientInfo,
  COMMAND_VARIANTS,
  type CommandEnvelope,
  type CommandId,
  type HereScope,
  parseCommandEnvelope,
  type RejectionCode,
} from '@halcyonic/contracts';
import { boundCommand, COMMAND_FIELDS } from '@halcyonic/domain';
import type { HeldRow } from './choices.ts';

/** A value the person or the assistant gives for one need: words, a yes or no, a number, or row indices. */
export type GivenValue = string | boolean | number | readonly number[];

export interface Preparation {
  readonly scope: HereScope;
  readonly command_type: string;
  readonly values: Readonly<Record<string, GivenValue>>;
  /** The digest of each list as it was read, for every choice given by index. */
  readonly read: Readonly<Partial<Record<ChoiceList, string>>>;
  readonly command_id: CommandId;
  readonly issued_at: string;
  readonly client: ClientInfo;
}

export type Prepared =
  | { readonly ok: true; readonly command: CommandEnvelope }
  | {
      readonly ok: false;
      readonly code: RejectionCode | 'choices_changed';
      readonly message: string;
    };

export interface PrepareSources {
  /** The rows of a list for the scope, and its digest, as the host reads them now. */
  readonly rows: (list: ChoiceList) => Promise<{ rows: readonly HeldRow[]; digest: string } | null>;
}

/** The payload fields each command's schema requires, from its own definition. */
const REQUIRED = new Map<string, ReadonlySet<string>>(
  COMMAND_VARIANTS.map((variant) => [
    variant.properties.command_type.const as string,
    new Set((variant.properties.payload as { required?: string[] }).required ?? []),
  ]),
);

const refuse = (code: RejectionCode | 'choices_changed', message: string): Prepared => ({
  ok: false,
  code,
  message,
});

/**
 * Builds a command from what the person or the assistant gave (ADR 0060, the lead's addition A): the
 * scope's ids, the host's own fills, and each need's value, a choice turned from its index into the
 * id of the row it named in a list whose digest is still the one read. Never a payload a proposer
 * wrote. A review field is left for the review to fill. The command then meets its own validator here,
 * and the whole admission when it is submitted.
 */
export async function prepareCommand(
  given: Preparation,
  sources: PrepareSources,
): Promise<Prepared> {
  const type = given.command_type;
  if (!(type in COMMAND_FIELDS))
    return refuse('invalid_state', 'That is not a command the list offers.');
  const fields = COMMAND_FIELDS[type as keyof typeof COMMAND_FIELDS];
  const bound = boundCommand(type as keyof typeof COMMAND_FIELDS, given.scope);
  if (bound === null) return refuse('invalid_state', 'That command does not belong here.');
  const payload: Record<string, unknown> = { ...(bound.payload as Record<string, unknown>) };
  const required = REQUIRED.get(type) ?? new Set<string>();
  for (const name of Object.keys(given.values))
    if (fields[name]?.from !== 'person')
      return refuse('invalid_state', `${name} is not given here.`);
  for (const [name, field] of Object.entries(fields)) {
    if (field.from !== 'person') continue;
    const value = given.values[name];
    if (value === undefined) {
      if (field.required) return refuse('invalid_state', `${field.label} is needed.`);
      // An optional need the schema still names is given as nothing.
      if (required.has(name)) payload[name] = null;
      continue;
    }
    switch (field.need) {
      case 'text':
        if (typeof value !== 'string' || value.trim() === '')
          return refuse('invalid_state', `${field.label} needs words.`);
        payload[name] = value.trim();
        break;
      case 'yes_no':
        if (typeof value !== 'boolean')
          return refuse('invalid_state', `${field.label} is a yes or no.`);
        payload[name] = value;
        break;
      case 'count':
        if (typeof value !== 'number' || !Number.isInteger(value))
          return refuse('invalid_state', `${field.label} is a whole number.`);
        payload[name] = value;
        break;
      case 'choice':
      case 'choices': {
        const list = field.list;
        const read = list === null ? null : await sources.rows(list);
        if (list === null || read === null)
          return refuse('capability_unsupported', `${field.label} cannot be chosen here yet.`);
        if (given.read[list] !== read.digest)
          return refuse(
            'choices_changed',
            `The ${field.label.toLowerCase()} list changed. Read it again.`,
          );
        const indices = field.need === 'choice' ? [value] : value;
        if (!Array.isArray(indices) || indices.length === 0)
          return refuse('invalid_state', `${field.label} is chosen by its place in the list.`);
        if (field.max !== null && indices.length > field.max)
          return refuse('invalid_state', `At most ${field.max} for ${field.label}.`);
        const values: unknown[] = [];
        for (const index of indices) {
          const held = typeof index === 'number' ? read.rows[index] : undefined;
          if (held === undefined)
            return refuse('invalid_state', `${field.label} has no row ${String(index)}.`);
          values.push(held.value);
        }
        payload[name] = field.need === 'choice' ? values[0] : values;
        break;
      }
      // Typed on its own, never given through this path, and never said (R6).
      case 'secret':
      case 'answers':
        return refuse('capability_unsupported', `${field.label} is given on its own card.`);
    }
  }
  const command = {
    schema_version: 1,
    command_id: given.command_id,
    issued_at: given.issued_at,
    client: given.client,
    command_type: type,
    payload,
  };
  // A review the command carries is the review's to fill: check the rest now, with a stand-in.
  const reviewed = Object.entries(fields).filter(([, field]) => field.from === 'review');
  const probe = structuredClone(command) as { payload: Record<string, unknown> };
  for (const [name] of reviewed)
    if (required.has(name))
      probe.payload[name] = name === 'commit' ? '0'.repeat(40) : '0'.repeat(64);
  const parsed = parseCommandEnvelope(probe);
  if (!parsed.ok)
    return refuse('invalid_state', `The command does not hold together: ${parsed.issues[0] ?? ''}`);
  return { ok: true, command: command as unknown as CommandEnvelope };
}
