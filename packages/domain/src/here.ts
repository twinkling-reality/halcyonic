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
  'project.send_to_github',
  'project.publish',
  'project.unpublish',
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
];
const ALL_ORDER: readonly Listed[] = ['project.create'];

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
    } else if (field.from === 'host') payload[name] = {};
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
  const seen = new Set<CommandType>();
  for (const commandType of order) {
    if (seen.has(commandType)) continue;
    const command = boundCommand(commandType, scope);
    if (command === null) continue;
    seen.add(commandType);
    const admission = admitHere(command, projection, runtimes);
    if (!admission.admitted) {
      refused.push({
        command_type: commandType,
        code: admission.rejection.code,
        message: admission.rejection.message,
      });
      continue;
    }
    actions.push(commandEntry(commandType));
  }
  if (scope.project_id !== null && projection.project(scope.project_id) !== undefined)
    actions.push(...PROJECT_LISTS);
  return { actions, refused };
}
