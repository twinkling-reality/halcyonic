import type {
  CommandEnvelope,
  CommandType,
  HereAction,
  HereNeed,
  HereScope,
  RejectionCode,
} from '@halcyonic/contracts';
import { admitHere, type RuntimeCatalog } from './admission.ts';
import { boundTo, COMMAND_FIELDS, type FieldSource } from './command-fields.ts';
import { COMMAND_SAYING } from './policy.ts';
import type { Projection } from './projection.ts';

type Listed = keyof typeof COMMAND_FIELDS;

/**
 * The host's order of what can be done in each scope, its main action first (ADR 0060). A task's own
 * entries come first in a task's scope, then its project's, so what a person says about the project
 * while in a task is reachable there too. All work offers a new project.
 */
const TASK_ORDER: readonly Listed[] = [
  'execution.send_instruction',
  'execution.answer_question',
  'execution.respond_to_approval',
  'execution.interrupt',
  'execution.drop_queued_instruction',
  'execution.start',
  'workstream.add_to_project',
  'workstream.fit_to_project',
  'project.check',
];
const PROJECT_ORDER: readonly Listed[] = [
  'workstream.create',
  'project.check',
  'project.preview.start',
  'project.preview.stop',
  'project.open_on_mac',
  'project.restore',
  'project.send',
  'project.publish',
  'project.unpublish',
  'project.make_private',
  'project.share.start',
  'project.share.stop',
  'project.save_copy',
  'project.show_in_finder',
  'project.make_vr_version',
  'project.rules.set',
  'project.own_check.choose',
  'project.set_location',
  'project.link_catalog',
  'project.unlink_catalog',
  'project.secret.set',
  'project.secret.remove',
  // Last of the project's own: it takes the project off the list, its folder left as it is (ADR 0063).
  'project.remove_from_list',
  // Where the assistant runs is the person's to choose from any place (ADR 0030, 2026-11-12).
  'assistant.model.choose',
];
const ALL_ORDER: readonly Listed[] = ['project.create', 'assistant.model.choose'];

/** The lists a project's scope opens, each the same one shape (ADR 0060, addition B). */
const PROJECT_LISTS: readonly Extract<HereAction, { kind: 'list' }>[] = [
  {
    kind: 'list',
    list: 'tasks',
    title: 'Tasks',
    description: "The project's tasks, newest first, each with what can be done with it.",
  },
  {
    kind: 'list',
    list: 'versions',
    title: 'History',
    description: "The project's saved versions, newest first, each with what can be done with it.",
  },
];

/** Why a command is not offered here: admission's own refusal for the state, by its code. */
export interface NotOffered {
  readonly command_type: CommandType;
  readonly code: RejectionCode;
  readonly message: string;
}

/**
 * The command a scope binds, before the person gives anything: the scope's ids in the fields that
 * name them, the host's own fills, and nothing else. Null when the scope does not bind it.
 */
export function boundCommand(commandType: Listed, scope: HereScope): CommandEnvelope | null {
  const fields = COMMAND_FIELDS[commandType];
  const level = boundTo(fields);
  if (level === 'task' && scope.workstream_id === null) return null;
  if (level === 'project' && scope.project_id === null) return null;
  const payload: Record<string, unknown> = {};
  for (const [name, field] of Object.entries(fields)) {
    if (field.from === 'project') payload[name] = scope.project_id;
    else if (field.from === 'workstream') {
      if (scope.workstream_id !== null) payload[name] = scope.workstream_id;
    } else if (field.from === 'execution') {
      if (scope.execution_id === null) return null;
      payload[name] = scope.execution_id;
    } else if (field.from === 'host' && field.fill === 'empty') payload[name] = {};
  }
  return { command_type: commandType, payload } as unknown as CommandEnvelope;
}

function needsOf(fields: Readonly<Record<string, FieldSource>>): HereNeed[] {
  const needs: HereNeed[] = [];
  for (const [name, field] of Object.entries(fields)) {
    if (field.from !== 'person') continue;
    needs.push({
      name,
      kind: field.need,
      label: field.label,
      required: field.required,
      list: field.list,
      max: field.max,
    });
  }
  return needs;
}

/** A need the assistant may never fill: a secret, typed in a field, or a runtime's question's answers. */
const NEVER_SAID = new Set<HereNeed['kind']>(['secret', 'answers']);

/** One command's entry: the host's words, how it is confirmed, whether it may be said, its needs. */
export function commandEntry(commandType: Listed): Extract<HereAction, { kind: 'command' }> {
  const saying = COMMAND_SAYING[commandType];
  const needs = needsOf(COMMAND_FIELDS[commandType]);
  return {
    kind: 'command',
    command_type: commandType,
    title: saying.title,
    description: saying.description,
    confirm: saying.confirm,
    undo: saying.undo ?? null,
    sayable: saying.sayable && !needs.some((need) => NEVER_SAID.has(need.kind)),
    needs,
  };
}

/**
 * A task at its limit or waiting for one takes an instruction only as a raise, so the list asks the
 * admission about the raise it would be; the limit itself is the person's to give.
 */
function asRaise(
  command: CommandEnvelope,
  scope: HereScope,
  projection: Projection,
): CommandEnvelope {
  if (command.command_type !== 'execution.send_instruction' || scope.execution_id === null)
    return command;
  const cap = projection.executionFacts(scope.execution_id)?.spendingCap ?? 'none';
  if (cap !== 'reached' && cap !== 'waiting') return command;
  return { ...command, payload: { ...command.payload, spending_cap_usd: 0.01 } };
}

/**
 * An instruction's new spending limit is a need only for a task at its limit or waiting for one
 * (lead's ruling, 2026-11-13): nowhere else is it offered, so nobody fills a field that is then
 * refused.
 */
function withoutUnofferedNeeds(
  entry: Extract<HereAction, { kind: 'command' }>,
  scope: HereScope,
  projection: Projection,
): Extract<HereAction, { kind: 'command' }> {
  if (entry.command_type !== 'execution.send_instruction' || scope.execution_id === null)
    return entry;
  const cap = projection.executionFacts(scope.execution_id)?.spendingCap ?? 'none';
  if (cap === 'reached' || cap === 'waiting') {
    // Only the raise carries it on, so it is needed, not a choice.
    return {
      ...entry,
      needs: entry.needs.map((need) =>
        need.name === 'spending_cap_usd' ? { ...need, required: true } : need,
      ),
    };
  }
  return { ...entry, needs: entry.needs.filter((need) => need.name !== 'spending_cap_usd') };
}

/**
 * What can be done here now (ADR 0060): each command of the scope, in the host's order, that the
 * one admission admits for the state with only what the scope binds; then the lists the scope opens.
 * `refused` collects what was left out and why, for the host's own reasons to join (a check that is
 * not set up on this computer) and for tests; it is never shown as an entry.
 */
export function hereActions(
  scope: HereScope,
  projection: Projection,
  runtimes: RuntimeCatalog,
): { actions: HereAction[]; refused: NotOffered[] } {
  const order =
    scope.workstream_id !== null
      ? [...TASK_ORDER, ...PROJECT_ORDER]
      : scope.project_id !== null
        ? PROJECT_ORDER
        : ALL_ORDER;
  const actions: HereAction[] = [];
  const refused: NotOffered[] = [];
  // Nothing is done in the place of a project taken off the list (ADR 0063): no entry and no list.
  if (
    scope.project_id !== null &&
    projection.project(scope.project_id) !== undefined &&
    !projection.listed(scope.project_id)
  )
    return { actions, refused };
  const seen = new Set<CommandType>();
  for (const commandType of order) {
    if (seen.has(commandType)) continue;
    const command = boundCommand(commandType, scope);
    if (command === null) continue;
    seen.add(commandType);
    const admission = admitHere(asRaise(command, scope, projection), projection, runtimes);
    if (!admission.admitted) {
      refused.push({
        command_type: commandType,
        code: admission.rejection.code,
        message: admission.rejection.message,
      });
      continue;
    }
    actions.push(withoutUnofferedNeeds(commandEntry(commandType), scope, projection));
  }
  if (scope.project_id !== null && projection.listed(scope.project_id))
    actions.push(...PROJECT_LISTS);
  return { actions, refused };
}
