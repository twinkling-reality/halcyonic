import type { ChoiceList, CommandType, NeedKind } from '@halcyonic/contracts';

/**
 * Where each field of a command's payload comes from (ADR 0060): the scope the person stands in binds
 * the ids, the host fills what it alone knows, a review is the digest of what the person read, and
 * the rest the person gives, each a need of a declared kind. A model never supplies an id: an id the
 * person picks is a `choice` the host lists and turns into the id itself.
 */
export type FieldSource =
  | { readonly from: 'project' | 'workstream' | 'execution' }
  | {
      readonly from: 'host';
      /** What the host fills from what it holds: an empty set of options, or the published commit. */
      readonly fill: 'empty' | 'published_commit';
    }
  | { readonly from: 'review' }
  | {
      readonly from: 'person';
      readonly need: NeedKind;
      readonly label: string;
      readonly required: boolean;
      /** For a choice: the host list its rows come from. */
      readonly list: ChoiceList | null;
      /** For several choices: how many at most. */
      readonly max: number | null;
    };

const project = { from: 'project' } as const;
const workstream = { from: 'workstream' } as const;
const execution = { from: 'execution' } as const;
const host = { from: 'host', fill: 'empty' } as const;
const review = { from: 'review' } as const;
const need = (
  kind: NeedKind,
  label: string,
  options: { required?: boolean; list?: ChoiceList; max?: number } = {},
): FieldSource => ({
  from: 'person',
  need: kind,
  label,
  required: options.required ?? true,
  list: options.list ?? null,
  max: options.max ?? null,
});
const optional = { required: false } as const;

/**
 * Every payload field of every command but the workspace request's own, which the assistant is and
 * never proposes. A test holds this to the contracts' schemas, field for field.
 */
export const COMMAND_FIELDS: Readonly<
  Record<Exclude<CommandType, `workspace.request.${string}`>, Readonly<Record<string, FieldSource>>>
> = {
  'project.create': {
    name: need('text', 'Name'),
    location: need('choice', 'Folder', { ...optional, list: 'folders' }),
  },
  'project.make_vr_version': {
    original_project_id: project,
    root: need('choice', 'Where', { list: 'folders' }),
    folder_name: need('text', 'Folder Name'),
  },
  'project.set_location': {
    project_id: project,
    location: need('choice', 'Folder', { list: 'folders' }),
    review,
  },
  'project.rules.set': { project_id: project, on: need('yes_no', 'Use Rules'), review },
  'project.restore': {
    project_id: project,
    checkpoint_id: need('choice', 'Version', { list: 'versions' }),
    review,
  },
  'project.check': { project_id: project, workstream_id: workstream },
  'project.own_check.choose': {
    project_id: project,
    scripts: need('choices', 'Commands', { list: 'own_check_scripts', max: 5 }),
  },
  'project.preview.start': { project_id: project },
  'project.preview.stop': { project_id: project },
  'project.open_on_mac': { project_id: project },
  'project.show_in_finder': { project_id: project },
  'project.save_copy': { project_id: project },
  'project.share.start': {
    project_id: project,
    way: need('choice', 'Share By', { list: 'share_ways' }),
  },
  'project.share.stop': { project_id: project },
  // The value goes by its own write-only request, typed, never by this command and never said (R6).
  'project.secret.set': { project_id: project, name: need('secret', 'Secret') },
  'project.secret.remove': {
    project_id: project,
    name: need('choice', 'Secret', { list: 'secrets' }),
  },
  'project.send_to_github': { project_id: project, title: need('text', 'Title'), review },
  'project.publish': { project_id: project, review },
  // One press (R1): the host fills the commit it holds as published, so what comes down is what it
  // published, and the command's own admission refuses one published again since (AUDIT2 S2).
  'project.unpublish': { project_id: project, commit: { from: 'host', fill: 'published_commit' } },
  'project.link_catalog': {
    project_id: project,
    catalog: need('choice', 'Repository', { list: 'catalog' }),
  },
  'project.unlink_catalog': { project_id: project },
  'workstream.create': {
    project_id: project,
    title: need('text', 'Title'),
    objective: need('text', 'Goal', optional),
    from_checkpoint: need('choice', 'From Version', { ...optional, list: 'versions' }),
  },
  'workstream.add_to_project': { workstream_id: workstream, review },
  'workstream.fit_to_project': { workstream_id: workstream },
  'execution.start': {
    workstream_id: workstream,
    runtime_id: need('choice', 'Agent', { list: 'runtimes' }),
    instruction: need('text', 'Instruction'),
    // Runtime-specific options the adapter validates: never the person's or a model's to fill here.
    options: host,
    model_ref: need('choice', 'Model', { ...optional, list: 'models' }),
    approval_level: need('choice', 'Asking', { ...optional, list: 'approval_levels' }),
    plan: need('yes_no', 'Plan First', optional),
    attachments: need('choices', 'Pictures', { ...optional, list: 'pictures', max: 4 }),
  },
  'execution.send_instruction': {
    execution_id: execution,
    text: need('text', 'Instruction'),
    leave_plan: need('yes_no', 'Carry Out Plan', optional),
    attachments: need('choices', 'Pictures', { ...optional, list: 'pictures', max: 4 }),
  },
  'execution.respond_to_approval': {
    execution_id: execution,
    approval_id: need('choice', 'Request', { list: 'approvals' }),
    decision: need('choice', 'Decision', { list: 'decisions' }),
    message: need('text', 'Why', optional),
  },
  'execution.answer_question': {
    execution_id: execution,
    question_id: need('choice', 'Question', { list: 'questions' }),
    answers: need('answers', 'Answers'),
  },
  'execution.interrupt': { execution_id: execution },
  'execution.drop_queued_instruction': {
    execution_id: execution,
    command_id: need('choice', 'Instruction', { list: 'queued_instructions' }),
  },
};

/**
 * The scope a command needs to be offered in: a task's when it names an execution, or a task and no
 * project; a project's when it names one (a project's check names a task only where it is in one);
 * else all work's.
 */
export function boundTo(fields: Readonly<Record<string, FieldSource>>): 'all' | 'project' | 'task' {
  const from = new Set(Object.values(fields).map((field) => field.from));
  if (from.has('execution')) return 'task';
  if (from.has('workstream') && !('project_id' in fields)) return 'task';
  return from.has('project') ? 'project' : 'all';
}
