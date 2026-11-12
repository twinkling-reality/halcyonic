import { createHash } from 'node:crypto';
import {
  COMMAND_VARIANTS,
  type CommandEnvelope,
  type CommandId,
  type CommandType,
  type ExecutionId,
  type ProjectId,
  parseCommandEnvelope,
  type WorkspaceRequestProposal,
  type WorkstreamId,
} from '@halcyonic/contracts';

/** Where a request stands (ADR 0030): a task and its current execution, a project, or all work. */
export interface ActionScope {
  readonly project_id: ProjectId | null;
  readonly workstream_id: WorkstreamId | null;
  readonly execution_id: ExecutionId | null;
}

/** What one entry still needs from the person, by the name of its field in the command's payload. */
export interface ActionNeed {
  readonly name: string;
  /**
   * `person_words` is filled by the host with the person's own words, whole (AUDIT2's N4): an instruction a task gets
   * is never a text the model wrote.
   */
  readonly kind: 'text' | 'choice' | 'choices' | 'yes_no' | 'count' | 'secret' | 'person_words';
  readonly label: string;
  readonly required: boolean;
  /** For a choice: the list its rows come from, and for several, the most that may be chosen. */
  readonly list?: string;
  readonly most?: number;
}

/** One thing that can be done here now, as the host lists it (ADR 0060). */
export interface ActionEntry {
  readonly command_type: CommandType;
  readonly title: string;
  readonly description: string;
  readonly confirm: WorkspaceRequestProposal['confirm'];
  readonly sayable: boolean;
  readonly needs: readonly ActionNeed[];
}

/** An entry that opens a list the host holds instead of doing a command (a project's tasks, its versions). */
export interface LookEntry {
  readonly opens: string;
  readonly title: string;
  readonly description: string;
}

/**
 * One row of a list the host holds: its kind (one enum, R3), its label and detail in the host's words, whether the
 * label is a person's or another host's name, and the value only the host holds (an id), never shown to the model.
 */
export interface ListRow {
  readonly kind: string;
  readonly label: string;
  readonly detail: string | null;
  readonly theirs: boolean;
  readonly value: string | number | boolean;
}

/** What a built command will do, in the host's own words, and the digest of what the person will be shown. */
export interface ActionReview {
  readonly what_happens: string;
  readonly digest: string;
}

/**
 * The host's list of what can be done where the person stands (ADR 0060). The list, its rows and the review are the
 * host's (CORE's part): the request loop only reads them, so buttons, saying it and the one card never disagree.
 */
export interface ActionsHere {
  actions(scope: ActionScope): readonly ActionEntry[];
  /** The lists entries open here; empty where none does. */
  looks(scope: ActionScope): readonly LookEntry[];
  list(scope: ActionScope, name: string): readonly ListRow[];
  /** Null when the host cannot say what the command would do: then nothing is done. */
  review(command: CommandEnvelope): ActionReview | null;
}

/** The most choices one read lists. */
export const CHOICES_LISTED = 32;

/**
 * What the assistant may propose: the entries the host marks sayable, never one that needs a secret (its value is
 * typed in a field, never passed through a model), and never one of the coordinator's own commands or an answer to a
 * runtime's approval, whatever a list says (ADR 0060, R6).
 */
export function sayable(entries: readonly ActionEntry[]): ActionEntry[] {
  return entries.filter(
    (entry) =>
      entry.sayable &&
      !entry.needs.some((need) => need.kind === 'secret') &&
      !entry.command_type.startsWith('workspace.request.') &&
      entry.command_type !== 'execution.respond_to_approval' &&
      entry.command_type !== 'execution.answer_question',
  );
}

/** The digest of a list as read, so a proposal can tell whether the rows it chose from have changed. */
export function listDigest(rows: readonly ListRow[]): string {
  return createHash('sha256')
    .update(
      JSON.stringify(
        rows.slice(0, CHOICES_LISTED).map((r) => [r.kind, r.label, r.detail, r.value]),
      ),
    )
    .digest('hex');
}

type Built =
  | { readonly ok: true; readonly command: CommandEnvelope }
  | { readonly ok: false; readonly reason: string; readonly field?: string };

type PayloadSchema = {
  properties?: Record<string, { anyOf?: { type?: string }[]; type?: string }>;
  required?: string[];
};

function payloadSchema(commandType: CommandType): PayloadSchema | undefined {
  const variant = COMMAND_VARIANTS.find(
    (candidate) => (candidate.properties.command_type as { const?: string }).const === commandType,
  );
  return variant?.properties.payload as PayloadSchema | undefined;
}

const BINDING = ['project_id', 'workstream_id', 'execution_id'] as const;

/**
 * Builds the command an entry names from the request's binding and the model's values. The ids come from the binding
 * and from the choices the host listed, never from the model; a value for a field the entry does not need, a missing
 * required one or one of the wrong kind builds nothing; the result must pass the command's own validator.
 */
export function buildProposed(
  entry: ActionEntry,
  scope: ActionScope,
  values: Readonly<Record<string, unknown>>,
  rowsOf: (list: string) => readonly ListRow[],
  envelope: {
    readonly command_id: CommandId;
    readonly issued_at: string;
    /** The person's own words, whole, for a `person_words` need. */
    readonly person_words?: string;
  },
): Built {
  const schema = payloadSchema(entry.command_type);
  if (!schema?.properties) return { ok: false, reason: 'unknown_command' };
  for (const name of Object.keys(values))
    if (!entry.needs.some((need) => need.name === name))
      return { ok: false, reason: 'not_needed', field: name };
  const payload: Record<string, unknown> = {};
  for (const key of BINDING) {
    const bound = scope[key];
    if (key in schema.properties && bound !== null) payload[key] = bound;
  }
  for (const need of entry.needs) {
    if (need.kind === 'person_words') {
      if (values[need.name] !== undefined)
        return { ok: false, reason: 'the_persons_words', field: need.name };
      if (envelope.person_words === undefined)
        return { ok: false, reason: 'missing', field: need.name };
      payload[need.name] = envelope.person_words;
      continue;
    }
    const value = values[need.name];
    if (value === undefined) {
      if (need.required) return { ok: false, reason: 'missing', field: need.name };
      continue;
    }
    if (need.kind === 'secret')
      return { ok: false, reason: 'secret_not_sayable', field: need.name };
    if (need.kind === 'text' && typeof value !== 'string')
      return { ok: false, reason: 'wrong_kind', field: need.name };
    if (need.kind === 'yes_no' && typeof value !== 'boolean')
      return { ok: false, reason: 'wrong_kind', field: need.name };
    if (need.kind === 'count' && !Number.isInteger(value))
      return { ok: false, reason: 'wrong_kind', field: need.name };
    if (need.kind === 'choice' || need.kind === 'choices') {
      const listed = need.list === undefined ? [] : rowsOf(need.list).slice(0, CHOICES_LISTED);
      const several = need.kind === 'choices';
      const indices = several ? value : [value];
      if (
        !Array.isArray(indices) ||
        indices.length === 0 ||
        (several && indices.length > (need.most ?? 1)) ||
        new Set(indices).size !== indices.length
      )
        return { ok: false, reason: 'wrong_kind', field: need.name };
      const chosen = indices.map((index) =>
        Number.isInteger(index) ? listed[index as number] : undefined,
      );
      if (chosen.some((row) => row === undefined))
        return { ok: false, reason: 'no_such_choice', field: need.name };
      const values = chosen.map((row) => (row as ListRow).value);
      payload[need.name] = several ? values : values[0];
    } else payload[need.name] = value;
  }
  // A field the command requires that may be null, and that nothing gave, is null: absent is not the same as none.
  for (const key of schema.required ?? [])
    if (!(key in payload) && schema.properties[key]?.anyOf?.some((part) => part.type === 'null'))
      payload[key] = null;
  const command = {
    schema_version: 1,
    command_id: envelope.command_id,
    issued_at: envelope.issued_at,
    client: { name: 'workspace-agent', version: null, device_label: null },
    command_type: entry.command_type,
    payload,
  };
  const checked = parseCommandEnvelope(command);
  if (!checked.ok) return { ok: false, reason: 'invalid', field: checked.issues[0]?.path ?? '/' };
  return { ok: true, command: checked.value };
}
